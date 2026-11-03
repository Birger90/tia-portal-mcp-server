using System.Text.Json;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.FakeWorker.Plc;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.SimaticSd;

/// <summary>
/// The SIMATIC SD reads and writes through the guarded plc_write pipeline against the plc-write-roundtrip FakeWorker
/// (PLC_1 group SD holds the fixture blocks; FB_Safety is an F-block of the password-protected F-CPU). The FakeWorker
/// models shapes only, so these tests verify the server's planning, routing and verification, not TIA Portal.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class SimaticSdRoundTripTests
{
    private const string Fb = "Station_1/Blocks/SD/MCPVerify_FB_LAD_v3";
    private const string Mixed = "Station_1/Blocks/SD/MCPVerify_Mixed_LADSCL";
    private const string Safety = "Station_1/Blocks/SD/FB_Safety";
    private const string PasswordVariable = "TIA_MCP_TEST_SD_SAFETY_PASSWORD";

    private static async Task<JsonElement> ReadAsync(PlcGuardedWriteFixture fixture, PlcOperationRequest read)
    {
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { read }).IsValid, PlcOperationCatalog.ValidateRead(new[] { read }).Error);
        var item = PlcSdNetworkReader.Handles(read.Operation)
            ? await PlcSdNetworkReader.ExecuteAsync(fixture.Client, read, CancellationToken.None)
            : PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        Assert.True(item.Result is not null, item.Failure?.Message);
        return item.Result!.Value;
    }

    private static Task<JsonElement> NetworksAsync(PlcGuardedWriteFixture fixture, string blockPath)
        => ReadAsync(fixture, new PlcOperationRequest { OperationId = "l", Operation = SimaticSdMethods.ListNetworks, BlockPath = blockPath });

    private static void AssertSucceeded(PlcGuardedWriteResponse response)
    {
        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch?.Operations.Select(o => o.Failure?.Message) ?? Array.Empty<string?>()));
        Assert.All(response.Verification!.Operations, v => Assert.True(v.Success, v.Message));
    }

    [Fact]
    public async Task Reads_ReturnTheSdBundleTheNetworksAndTheFailSafeFlag()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var content = await ReadAsync(fixture, new PlcOperationRequest { OperationId = "c", Operation = "get_block_content", BlockPath = Fb, Format = "sd" });
        Assert.Equal("sd", content.GetProperty("format").GetString());
        Assert.Equal(SimaticSdFixtures.CookbookFbLadV3.Bundle, content.GetProperty("content").GetString());
        Assert.False(content.TryGetProperty("failSafe", out _));
        var hash = content.GetProperty("contentHash").GetString()!;
        Assert.StartsWith("sd:sha256:", hash);
        Assert.Equal(hash, await fixture.ReadHashAsync(Fb, "sd"));

        var networks = await NetworksAsync(fixture, Fb);
        Assert.Equal(4, networks.GetProperty("networkCount").GetInt32());
        Assert.Equal(hash, networks.GetProperty("contentHash").GetString());
        var first = networks.GetProperty("networks")[0];
        Assert.Equal("MLC_463", first.GetProperty("titleId").GetString());
        Assert.Equal("N1", first.GetProperty("title")[0].GetProperty("text").GetString());
        Assert.Equal("pragma", first.GetProperty("languageSource").GetString());

        var safety = await ReadAsync(fixture, new PlcOperationRequest { OperationId = "s", Operation = "get_block_content", BlockPath = Safety, Format = "sd" });
        Assert.True(safety.GetProperty("failSafe").GetBoolean());
        Assert.True((await NetworksAsync(fixture, Safety)).GetProperty("failSafe").GetBoolean());
    }

    [Fact]
    public async Task InsertNetwork_PreviewsThenImportsThroughUpdateBlockLogicSdAndVerifiesByReExport()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var hash = await fixture.ReadHashAsync(Fb, "sd");
        var insert = new PlcOperationRequest
        {
            OperationId = "ins", Operation = SimaticSdMethods.InsertNetwork, BlockPath = Fb, Position = 2, ExpectedContentHash = hash,
            Code = "RUNG wire#powerrail\n    Contact( #Trig )\n    { S7_Templates := \"time_type := Time\" }\n    #tonInst.TON(\n        pt := T#1s,\n        et =>\n    )\nEND_RUNG",
        };

        var preview = await fixture.RunAsync(true, insert);
        Assert.True(preview.Success, preview.Error?.Message);
        Assert.Equal("network 2", preview.Effects[0].Effect!.Changes.Single().Field);
        Assert.Contains(preview.Guards, g => g.Id == PlcGuardDefinitions.ReimportsBlockForText);
        Assert.Contains(preview.Guards, g => g.Id == PlcGuardDefinitions.S7dclImportMayAlter);
        Assert.Equal(hash, await fixture.ReadHashAsync(Fb, "sd"));

        var response = await fixture.RunAsync(false, insert);
        AssertSucceeded(response);
        var check = response.Verification!.Operations.Single();
        Assert.Equal(PlcVerificationPolicy.NetworkCodeCheck, check.Check);
        Assert.Contains("codeMatchesExport: true", check.Message);
        Assert.Contains("compile_check", check.Message);
        Assert.Equal(await fixture.ReadHashAsync(Fb, "sd"), check.ContentHash);
        Assert.Equal("sd", response.Batch!.Operations[0].Result!.Value.GetProperty("format").GetString());
        Assert.Contains("update_block_logic", requests.Methods());

        var networks = (await NetworksAsync(fixture, Fb)).GetProperty("networks");
        Assert.Equal(5, networks.GetArrayLength());
        Assert.Contains("{ S7_Templates := \"time_type := Time\" }", networks[1].GetProperty("code").GetString());
        Assert.Equal("MLC_3qd", networks[2].GetProperty("titleId").GetString());
    }

    [Fact]
    public async Task NetworkEdits_ChainOnOneBlock_AndANormalizedExportIsInformationNotFailure()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        // The FakeWorker exports Move( as the reported V21 form Move{ Card:=1; DisableENO:=TRUE }(.
        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "move", Operation = SimaticSdMethods.InsertNetwork, BlockPath = Mixed, Code = "RUNG wire#powerrail\n    Move( in := 42, out1 => #Sum )\nEND_RUNG" },
            new PlcOperationRequest { OperationId = "scl", Operation = SimaticSdMethods.ReplaceNetwork, BlockPath = Mixed, NetworkNumber = 3, Code = "#Sum := #Va - #Vb;" },
            new PlcOperationRequest { OperationId = "del", Operation = SimaticSdMethods.DeleteNetwork, BlockPath = Mixed, NetworkNumber = 4 });

        AssertSucceeded(response);
        Assert.Equal(new[] { null, "move", "scl" }, response.Effects.Select(e => e.Effect!.DependsOn));
        var networks = (await NetworksAsync(fixture, Mixed)).GetProperty("networks");
        Assert.Equal(new[] { "LAD", "LAD", "SCL", "LAD" }, networks.EnumerateArray().Select(n => n.GetProperty("language").GetString()));
        Assert.Equal("#Sum := #Va - #Vb;", networks[2].GetProperty("code").GetString());
        Assert.Contains("Move{", networks[3].GetProperty("exportOnlyForms").EnumerateArray().Select(f => f.GetString()));

        // A later edit re-imports the export form of the untouched network: the info guard names it.
        var hash = await fixture.ReadHashAsync(Mixed, "sd");
        var later = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "x", Operation = SimaticSdMethods.ReplaceNetwork, BlockPath = Mixed, NetworkNumber = 1,
            Code = "RUNG wire#powerrail\n    Contact( #Fwd )\n    Coil( #RunFwd )\nEND_RUNG", ExpectedContentHash = hash });
        Assert.Contains(later.Guards, g => g.Id == PlcGuardDefinitions.SdReimportsExportForm && g.Message.Contains("network 4 (Move{)"));

        var moved = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "m2", Operation = SimaticSdMethods.ReplaceNetwork, BlockPath = Mixed, NetworkNumber = 4,
            Code = "RUNG wire#powerrail\n    Move( in := 1, out1 => #Sum )\nEND_RUNG", ExpectedContentHash = hash });
        AssertSucceeded(moved);
        Assert.Contains("codeMatchesExport: false", moved.Verification!.Operations.Single().Message);
        Assert.Contains("Move( in := 1, out1 => #Sum )", moved.Verification.Operations.Single().Expected);
        Assert.Contains("Move{ Card:=1; DisableENO:=TRUE }( in := 1, out1 => #Sum )", moved.Verification.Operations.Single().Actual);
    }

    [Fact]
    public async Task NetworkEdits_RefuseStaleHashesNumbersOutOfRangeAndUnchangedCode()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var stale = TiaMcpServer.Contracts.Shared.ContentHashRules.Compute("sd", "old");

        Assert.Equal("state_changed", (await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "a", Operation = SimaticSdMethods.DeleteNetwork, BlockPath = Fb, NetworkNumber = 1, ExpectedContentHash = stale })).Error?.Category);
        Assert.Equal("target_not_found", (await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "b", Operation = SimaticSdMethods.DeleteNetwork, BlockPath = Fb, NetworkNumber = 9 })).Error?.Category);
        Assert.Equal("target_not_found", (await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "c", Operation = SimaticSdMethods.InsertNetwork, BlockPath = Fb, Position = 9, Code = "#a := 1;" })).Error?.Category);
        var same = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "d", Operation = SimaticSdMethods.ReplaceNetwork, BlockPath = Mixed, NetworkNumber = 3, Code = "#Sum := #Va + #Vb;" });
        Assert.Equal("validation_error", same.Error?.Category);
        Assert.Contains("changes nothing", same.Error!.Message);
        // An empty block (generated sd export without networks) takes a first network.
        var empty = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "e", Operation = SimaticSdMethods.InsertNetwork, BlockPath = "Station_1/Main", Code = "RUNG wire#powerrail\n    Coil( \"Out\" )\nEND_RUNG" });
        AssertSucceeded(empty);
        Assert.Equal(1, (await NetworksAsync(fixture, "Station_1/Main")).GetProperty("networkCount").GetInt32());
    }

    [Fact]
    public async Task UpdateBlockLogicSd_ImportsAnEditedBundleAndRefusesAnotherDeclaredName()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var content = (await ReadAsync(fixture, new PlcOperationRequest { OperationId = "c", Operation = "get_block_content", BlockPath = Mixed, Format = "sd" })).GetProperty("content").GetString()!;
        var hash = await fixture.ReadHashAsync(Mixed, "sd");

        var edited = content.Replace("#Sum := #Va + #Vb;", "#Sum := #Va * #Vb;");
        var response = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "u", Operation = "update_block_logic", BlockPath = Mixed, Format = "sd", Content = edited, ExpectedContentHash = hash });
        AssertSucceeded(response);
        Assert.Equal("contentHash", response.Verification!.Operations.Single().Check);
        Assert.StartsWith("sd:sha256:", response.Verification.Operations.Single().ContentHash);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.S7dclImportMayAlter);

        var renamed = edited.Replace("FUNCTION_BLOCK \"MCPVerify_Mixed_LADSCL\"", "FUNCTION_BLOCK \"Other\"");
        var refused = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "r", Operation = "update_block_logic", BlockPath = Mixed, Format = "sd", Content = renamed,
            ExpectedContentHash = await fixture.ReadHashAsync(Mixed, "sd") });
        Assert.False(refused.Success);
        Assert.Contains("declares 'Other'", refused.Batch!.Operations.Single().Failure!.Message);
    }

    [Fact]
    public async Task CreateBlockFromDocument_ImportsIntoTheGroupAndRefusesAnExistingNameWithoutOverwrite()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var document = SimaticSdFixtures.CookbookFcLad.Bundle.Replace("MCPVerify_FC_LAD", "FC_New");

        var single = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "new", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks/Motors", Content = document });
        AssertSucceeded(single);
        Assert.Equal(PlcVerificationPolicy.CreatedBlockCheck, single.Verification!.Operations.Single().Check);
        Assert.Equal(await fixture.ReadHashAsync("Station_1/Blocks/Motors/FC_New", "sd"), single.Verification.Operations.Single().ContentHash);
        Assert.Equal("PLC_1/Blocks/Motors/FC_New", single.Effects[0].Effect!.Target.BlockPath);

        // A network added to the new block in the same call depends on the create and supersedes its entry.
        var second = SimaticSdFixtures.CookbookFcLad.Bundle.Replace("MCPVerify_FC_LAD", "FC_New2");
        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "new2", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks/Motors", Content = second },
            new PlcOperationRequest { OperationId = "net", Operation = SimaticSdMethods.InsertNetwork, BlockPath = "Station_1/Blocks/Motors/FC_New2", Code = "#DST := #V1;", Language = "SCL" });
        AssertSucceeded(response);
        Assert.Equal(new[] { "superseded", PlcVerificationPolicy.NetworkCodeCheck }, response.Verification!.Operations.Select(v => v.Check));
        Assert.Equal("new2", response.Effects[1].Effect!.DependsOn);
        Assert.Equal(8, (await NetworksAsync(fixture, "Station_1/Blocks/Motors/FC_New2")).GetProperty("networkCount").GetInt32());

        var taken = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "t", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks", Content = document });
        Assert.Equal("validation_error", taken.Error?.Category);
        Assert.Contains("already exists", taken.Error!.Message);
        var overwrite = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "o", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks", Content = document, Overwrite = true });
        Assert.Contains(overwrite.Guards, g => g.Id == PlcGuardDefinitions.SdDocumentReplacesBlock);

        var rejected = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "rj", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks",
            Content = SimaticSdFixtures.CookbookFcLad.Bundle.Replace("MCPVerify_FC_LAD", "RejectedByPortal") });
        Assert.False(rejected.Success);
        Assert.Contains("Syntax Error", rejected.Batch!.Operations.Single().Failure!.Message);
    }

    [Fact]
    public async Task FailSafeSdBlocks_FollowTheLoginRuleAndFireTheAcceptanceGuard()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var edit = new PlcOperationRequest { OperationId = "edit", Operation = SimaticSdMethods.InsertNetwork, BlockPath = Safety, Code = "RUNG #Start\n    Coil( #Lamp_Green )\nEND_RUNG", Language = "FBD" };

        var blocked = await fixture.RunAsync(false, edit);
        Assert.Equal("blocked", blocked.Phase);
        Assert.Contains(blocked.Guards, g => g.Id == PlcGuardDefinitions.SafetyLoginRequired);
        Assert.Contains(blocked.Guards, g => g.Id == PlcGuardDefinitions.SafetyAcceptance);

        var created = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "c", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks",
            Content = SimaticSdFixtures.BetterTiaFbSafety.Bundle.Replace("FB_Safety", "FB_Safety2") });
        Assert.Contains(created.Guards, g => g.Id == PlcGuardDefinitions.SafetyAcceptance);
        Assert.Contains(created.Guards, g => g.Id == PlcGuardDefinitions.SafetyLoginRequired);

        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "login", Operation = TiaMcpServer.Contracts.Plc.Safety.PlcSafetyMethods.Login, PlcName = "Station_1", PasswordVariable = PasswordVariable },
            edit);
        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        Assert.DoesNotContain(response.Guards, g => g.Id == PlcGuardDefinitions.SafetyLoginRequired);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.SafetyAcceptance);
        Assert.True(response.Verification!.Operations.Single(v => v.OperationId == "edit").Success);
    }

    private static PlcOperationRequest Login => new()
    {
        OperationId = "login", Operation = TiaMcpServer.Contracts.Plc.Safety.PlcSafetyMethods.Login, PlcName = "Station_1", PasswordVariable = PasswordVariable,
    };

    private static async Task<JsonElement> Rtg1Async(PlcGuardedWriteFixture fixture)
    {
        var administration = await ReadAsync(fixture, new PlcOperationRequest { OperationId = "a", Operation = TiaMcpServer.Contracts.Plc.Safety.PlcSafetyMethods.GetAdministration, PlcName = "Station_1" });
        return administration.GetProperty("plcs")[0].GetProperty("runtimeGroups").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "RTG1");
    }

    private static string? Attribute(JsonElement group, string name)
        => group.GetProperty("attributes").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name).GetProperty("value").GetString();

    private static async Task<bool> BlockExistsAsync(PlcGuardedWriteFixture fixture, string name)
        => (await fixture.Client.GetBlockContentAsync("Station_1/" + name, "sd")).Success;

    /// <summary>
    /// L1-L4 (live, V21, 2026-11-02): an SD write onto the main safety block of RTG1 is not imported onto it; the worker imports it as a
    /// temporary F-block, compiles it, exports its SimaticML, gives it FB_Safety's name, number and AutoNumber, imports that onto
    /// FB_Safety and deletes the temporary block. The reference stays, the result names the route, the PLC compiles.
    /// </summary>
    [Fact]
    public async Task SdEditOfTheMainSafetyBlock_GoesThroughATemporaryFBlock_AndTheReferenceStays()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var edit = new PlcOperationRequest { OperationId = "edit", Operation = SimaticSdMethods.ReplaceNetwork, BlockPath = Safety, NetworkNumber = 1, Code = "RUNG #Start\n    Coil( #Lamp_Green )\nEND_RUNG", Language = "FBD" };

        var response = await fixture.RunAsync(false, Login, edit);

        AssertSucceeded(response);
        var guard = Assert.Single(response.Guards, g => g.Id == PlcGuardDefinitions.ImportMayUnlinkMainSafetyBlock);
        Assert.Contains("RTG1", guard.Message);
        Assert.Contains("temporary F-block", guard.Message);
        Assert.Contains("SimaticML", guard.Message);
        var item = response.Batch!.Operations.Single(o => o.OperationId == "edit");
        Assert.DoesNotContain(item.Warnings, w => w.Contains("restored") || w.Contains("could NOT"));
        var route = item.Result!.Value.GetProperty("mainSafetyBlockRoute");
        Assert.Equal("sdViaXmlForMainSafetyBlock", route.GetProperty("route").GetString());
        Assert.Equal("simaticMl", route.GetProperty("mainImport").GetString());
        Assert.Equal("deleted", route.GetProperty("temporaryBlockState").GetString());
        var temporary = route.GetProperty("temporaryBlockName").GetString()!;
        Assert.Matches("^zzMcpTmp_FB_Safety_[0-9a-f]{8}$", temporary);
        Assert.Equal("RTG1", route.GetProperty("runtimeGroups")[0].GetString());
        Assert.Equal("Success", route.GetProperty("temporaryCompile").GetProperty("state").GetString());
        Assert.False(route.GetProperty("mainAutoNumber").GetBoolean());
        Assert.Equal(PlcVerificationPolicy.NetworkCodeCheck, response.Verification!.Operations.Single(v => v.OperationId == "edit").Check);

        Assert.Equal("FB_Safety", Attribute(await Rtg1Async(fixture), TiaMcpServer.Contracts.Plc.Safety.PlcSafetyRuntimeGroupBinding.MainSafetyBlockName));
        Assert.False(await BlockExistsAsync(fixture, temporary));
        var xml = (await ReadAsync(fixture, new PlcOperationRequest { OperationId = "x", Operation = "get_block_content", BlockPath = Safety, Format = "xml" })).GetProperty("content").GetString()!;
        var identity = TiaMcpServer.Contracts.Plc.Interface.SimaticMlBlockIdentity.Read(xml);
        Assert.Equal(("FB_Safety", "false"), (identity.Name, identity.AutoNumber));
        Assert.Equal(route.GetProperty("mainNumber").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), identity.Number);
        var compile = await fixture.Client.CompileCheckAsync(null, "Station_1");
        Assert.True(compile.Success, compile.Error);
        Assert.Contains("\"overallState\":\"Success\"", compile.Payload);
    }

    /// <summary>update_block_logic format sd onto the main safety block takes the route too (also the implicit bundle-without-.xml route).</summary>
    [Theory]
    [InlineData("sd")]
    [InlineData("xml")]
    public async Task UpdateBlockLogicOfTheMainSafetyBlock_InSdOrABundleWithoutXml_TakesTheRoute(string format)
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var hash = await fixture.ReadHashAsync(Safety, format);
        var content = SimaticSdFixtures.BetterTiaFbSafety.Bundle;

        var response = await fixture.RunAsync(false, Login, new PlcOperationRequest { OperationId = "u", Operation = "update_block_logic", BlockPath = Safety, Format = format, Content = content, ExpectedContentHash = hash });

        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        var route = response.Batch!.Operations.Single(o => o.OperationId == "u").Result!.Value.GetProperty("mainSafetyBlockRoute");
        Assert.Equal("simaticMl", route.GetProperty("mainImport").GetString());
        Assert.Contains(route.GetProperty("notes").EnumerateArray(), n => n.GetString()!.Contains("S7_BlockNumber \"30\" was removed"));
        Assert.True(response.Verification!.Operations.Single(v => v.OperationId == "u").Success);
        Assert.Equal("FB_Safety", Attribute(await Rtg1Async(fixture), TiaMcpServer.Contracts.Plc.Safety.PlcSafetyRuntimeGroupBinding.MainSafetyBlockName));
    }

    /// <summary>A temporary block that does not compile stops before the main safety block: the item fails with the compiler messages, the block is unchanged.</summary>
    [Fact]
    public async Task ATemporaryCompileError_FailsTheItem_LeavesTheMainSafetyBlockUnchanged_AndDeletesTheTemporaryBlock()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var hash = await fixture.ReadHashAsync(Safety, "sd");
        var content = SimaticSdFixtures.BetterTiaFbSafety.Bundle.Replace("    VAR_INPUT", "    // fake:compile-error\n    VAR_INPUT");

        var response = await fixture.RunAsync(false, Login, new PlcOperationRequest { OperationId = "u", Operation = "update_block_logic", BlockPath = Safety, Format = "sd", Content = content, ExpectedContentHash = hash });

        var item = response.Batch!.Operations.Single(o => o.OperationId == "u");
        Assert.Equal("failed", item.Status);
        Assert.Equal("worker_operation_failed", item.Failure!.Category);
        Assert.Contains("did not compile", item.Failure.Message);
        Assert.Contains("invalid operand", item.Failure.Message);
        Assert.Contains("was not changed", item.Failure.Message);
        Assert.Equal("not_started", item.Failure.BlockImportOutcome!.ImportStage);
        var route = item.Result!.Value;
        Assert.Equal("notStarted", route.GetProperty("mainImport").GetString());
        Assert.Equal("deleted", route.GetProperty("temporaryBlockState").GetString());
        Assert.Equal(1, route.GetProperty("temporaryCompile").GetProperty("errorCount").GetInt32());
        Assert.Equal(hash, await fixture.ReadHashAsync(Safety, "sd"));
        Assert.False(await BlockExistsAsync(fixture, route.GetProperty("temporaryBlockName").GetString()!));
        Assert.Equal("FB_Safety", Attribute(await Rtg1Async(fixture), TiaMcpServer.Contracts.Plc.Safety.PlcSafetyRuntimeGroupBinding.MainSafetyBlockName));
    }

    /// <summary>A temporary block the route cannot delete is never left silently: a warning and the evidence name it; the same write is then refused.</summary>
    [Fact]
    public async Task ATemporaryBlockThatCannotBeDeleted_IsNamed_AndARepeatedWriteIsRefused()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var content = SimaticSdFixtures.BetterTiaFbSafety.Bundle.Replace("    VAR_INPUT", "    // fake:delete-fails\n    VAR_INPUT");
        var update = new PlcOperationRequest { OperationId = "u", Operation = "update_block_logic", BlockPath = Safety, Format = "sd", Content = content, ExpectedContentHash = await fixture.ReadHashAsync(Safety, "sd") };

        var response = await fixture.RunAsync(false, Login, update);

        var item = response.Batch!.Operations.Single(o => o.OperationId == "u");
        Assert.Equal("succeeded", item.Status);
        var route = item.Result!.Value.GetProperty("mainSafetyBlockRoute");
        Assert.Equal("left", route.GetProperty("temporaryBlockState").GetString());
        var temporary = route.GetProperty("temporaryBlockName").GetString()!;
        Assert.Contains(item.Warnings, w => w.Contains(temporary) && w.Contains("delete_block"));
        Assert.True(await BlockExistsAsync(fixture, temporary));

        var again = await fixture.RunAsync(false, Login, new PlcOperationRequest
        {
            OperationId = "u", Operation = "update_block_logic", BlockPath = Safety, Format = "sd", Content = content, ExpectedContentHash = await fixture.ReadHashAsync(Safety, "sd"),
        });
        var refused = again.Batch!.Operations.Single(o => o.OperationId == "u");
        Assert.Equal("validation_error", refused.Failure!.Category);
        Assert.Contains("already exists", refused.Failure.Message);
        Assert.Equal("notCreated", refused.Result!.Value.GetProperty("temporaryBlockState").GetString());
    }

    [Fact]
    public async Task CreateBlockFromDocumentWithOverwriteOntoTheMainSafetyBlock_TakesTheRoute()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);

        var response = await fixture.RunAsync(false, Login, new PlcOperationRequest
        {
            OperationId = "c", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks/SD", Content = SimaticSdFixtures.BetterTiaFbSafety.Bundle, Overwrite = true,
        });

        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.ImportMayUnlinkMainSafetyBlock && g.Message.Contains("temporary F-block"));
        var result = response.Batch!.Operations.Single(o => o.OperationId == "c").Result!.Value;
        Assert.Equal("sdViaXmlForMainSafetyBlock", result.GetProperty("mainSafetyBlockRoute").GetProperty("route").GetString());
        Assert.Contains(result.GetProperty("messages").EnumerateArray(), m => m.GetString()!.Contains("imported as SimaticML"));
        Assert.Equal("FB_Safety", Attribute(await Rtg1Async(fixture), TiaMcpServer.Contracts.Plc.Safety.PlcSafetyRuntimeGroupBinding.MainSafetyBlockName));
    }

    /// <summary>
    /// The safety net (scenario plc-write-no-route: the worker could not tell the block is a main safety block, so the SD document is
    /// imported directly; L1 drops the reference, L2 it cannot be re-assigned): the item succeeds with the repair and fails verification.
    /// </summary>
    [Fact]
    public async Task ADirectSdImportWhoseReferenceCannotBeRestored_SucceedsWithTheRepair_ButFailsVerification()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-no-route", McpAccessMode.Full);
        var edit = new PlcOperationRequest { OperationId = "edit", Operation = SimaticSdMethods.ReplaceNetwork, BlockPath = Safety, NetworkNumber = 1, Code = "RUNG #Start\n    Coil( #Lamp_Green )\nEND_RUNG", Language = "FBD" };

        var response = await fixture.RunAsync(false, Login, edit);

        var item = response.Batch!.Operations.Single(o => o.OperationId == "edit");
        Assert.Equal("succeeded", item.Status);
        Assert.False(item.Result!.Value.TryGetProperty("mainSafetyBlockRoute", out _));
        Assert.Contains(item.Warnings, w => w.Contains("could NOT be restored") && w.Contains("set_safety_runtime_group"));
        var check = response.Verification!.Operations.Single(v => v.OperationId == "edit");
        Assert.False(check.Success);
        Assert.Equal(TiaMcpServer.Contracts.Plc.Safety.PlcMainSafetyLinks.Check, check.Check);
        Assert.Contains("RTG1", check.Actual);
        Assert.Contains("set_safety_runtime_group", check.Message);
        Assert.False(response.Verification.Success);
        var compile = await fixture.Client.CompileCheckAsync(null, "Station_1");
        Assert.Contains("does not have a valid main safety block", compile.Payload);

        // The repair fails closed (L2, no assignable member): api_member_missing with the attributes the group reported.
        var repair = await fixture.RunAsync(false, Login, new PlcOperationRequest
        {
            OperationId = "set", Operation = TiaMcpServer.Contracts.Plc.Safety.PlcSafetyMethods.SetRuntimeGroup, PlcName = "Station_1", RuntimeGroupName = "RTG1", MainSafetyBlockPath = Safety,
        });
        var failure = repair.Batch!.Operations.Single(o => o.OperationId == "set").Failure!;
        Assert.Equal("api_member_missing", failure.Category);
        Assert.Contains("MainSafetyBlockName (Read)", failure.Message);
    }

    /// <summary>D15: an uncompiled block exports no SimaticML; get_block_content xml says so and returns the SD documents, and after compile_check the warning is gone.</summary>
    [Fact]
    public async Task AnUncompiledBlock_ReadInXml_WarnsThatTiaPortalExportedNoSimaticMl()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var document = SimaticSdFixtures.CookbookFcLad.Bundle.Replace("MCPVerify_FC_LAD", "MCP_SD_OhneTexte");
        Assert.True((await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "c", Operation = SimaticSdMethods.CreateBlockFromDocument, TargetGroupPath = "Station_1/Blocks", Content = document })).Success);
        var read = new PlcOperationRequest { OperationId = "r", Operation = "get_block_content", BlockPath = "Station_1/Blocks/MCP_SD_OhneTexte", Format = "xml" };

        var item = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));

        Assert.True(item.Result is not null, item.Failure?.Message);
        Assert.Contains(item.Warnings, w => w.Contains("was not exported as SimaticML") && w.Contains("run compile_check and read again"));
        Assert.Contains("--- FILE: MCP_SD_OhneTexte.s7dcl ---", item.Result!.Value.GetProperty("content").GetString());
        Assert.True((await fixture.Client.CompileCheckAsync("Station_1/Blocks/MCP_SD_OhneTexte", null)).Success);
        var again = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        Assert.DoesNotContain(again.Warnings, w => w.Contains("was not exported as SimaticML"));
    }

    [Fact]
    public async Task GetSafetyAdministration_ReportsTheAttributeInfosOfEveryRuntimeGroup()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var infos = (await Rtg1Async(fixture)).GetProperty("attributeInfos").EnumerateArray().Select(i => (i.GetProperty("name").GetString(), i.GetProperty("accessMode").GetString())).ToList();

        Assert.Contains(("MainSafetyBlockName", "Read"), infos);
    }

    [Fact]
    public async Task OnlyTheSimaticSdImportRoute_FiresTheUnlinkGuard()
    {
        Environment.SetEnvironmentVariable(PasswordVariable, PlcWriteRoundtripScenarioPassword);
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.Full);
        var hash = await fixture.ReadHashAsync(Safety, "sd");
        var content = (await ReadAsync(fixture, new PlcOperationRequest { OperationId = "c", Operation = "get_block_content", BlockPath = Safety, Format = "sd" })).GetProperty("content").GetString()!;

        var sd = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "u", Operation = "update_block_logic", BlockPath = Safety, Format = "sd", Content = content, ExpectedContentHash = hash });
        Assert.Contains(sd.Guards, g => g.Id == PlcGuardDefinitions.ImportMayUnlinkMainSafetyBlock);
        Assert.False(PlcSafetyPlanning.ImportsThroughSimaticSd(new PlcOperationRequest { OperationId = "x", Operation = "set_network_text", BlockPath = Safety }));
        Assert.True(PlcSafetyPlanning.ImportsBlock(new PlcOperationRequest { OperationId = "x", Operation = "set_network_text", BlockPath = Safety }));
        Assert.True(PlcSafetyPlanning.ImportsThroughSimaticSd(new PlcOperationRequest { OperationId = "x", Operation = "update_block_logic", BlockPath = Safety, Content = content }));
    }

    private const string PlcWriteRoundtripScenarioPassword = "FakeSafetyPassword!1";
}
