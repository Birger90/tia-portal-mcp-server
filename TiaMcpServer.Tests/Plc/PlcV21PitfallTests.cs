using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>The guards, warnings and verification of row 22 through the guarded pipeline against the plc-write FakeWorker.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcV21PitfallWriteTests
{
    private const string Main = "Station_1/Main";

    private const string S7dclRoute = "--- FILE: Main.s7dcl ---\n"
        + "{\n    S7_Optimized := \"TRUE\"\n}\n"
        + "ORGANIZATION_BLOCK \"Main\"\n    VAR_TEMP\n        tmr : _.TONR;\n    END_VAR\n\n"
        + "    { S7_Language := \"LAD\" }\n    NETWORK\n        RUNG wire#powerrail\n"
        + "            \"Data_A\".arr[\"Data_B\".structArr[1].idx] := TRUE;\n        END_RUNG\n    END_NETWORK\nEND_ORGANIZATION_BLOCK\n"
        + "--- FILE: Main.s7res ---\nres\n";

    private const string TextualBundle = "--- FILE: Main.xml ---\n<Document/>\n--- FILE: Main.s7dcl ---\n"
        + "{\n    S7_EditorMode := \"[SCL,TextualInterface]\"\n}\nORGANIZATION_BLOCK \"Main\"\nEND_ORGANIZATION_BLOCK\n";

    private static PlcOperationRequest Update(string content, string hash) => new()
    {
        OperationId = "content", Operation = "update_block_logic", BlockPath = Main, Format = "xml", Content = content, ExpectedContentHash = hash,
    };

    [Fact]
    public async Task SimaticSdRoute_FiresTheInformativeGuard_WarnsAndComparesConstructs()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, Update(S7dclRoute, await fixture.ReadHashAsync(Main, "xml")));

        Assert.True(response.Success);
        var guard = Assert.Single(response.Guards, g => g.Id == PlcGuardDefinitions.S7dclImportMayAlter);
        Assert.Equal(WriteGuardSeverities.Info, guard.Severity);
        Assert.Contains("'tmr' is declared with the project type _.TONR", guard.Message);
        Assert.Contains("network 1 (LAD)", guard.Message);
        Assert.Contains("AT overlays could not be checked", guard.Message);
        Assert.Contains(response.Batch!.Operations[0].Warnings, w => w.Contains("SimaticML (.xml) route avoids them"));
        var entry = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.S7dclConstructsCheck, true), (entry.Check, entry.Success));
        Assert.StartsWith("xml:sha256:", entry.ContentHash);
    }

    [Fact]
    public async Task SimaticSdRoute_WhoseImportAlteredAConstruct_IsNotVerified()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-s7dcl-mangles");

        var response = await fixture.RunAsync(false, Update(S7dclRoute, await fixture.ReadHashAsync(Main, "xml")));

        Assert.False(response.Success);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        var entry = Assert.Single(response.Verification!.Operations);
        Assert.False(entry.Success);
        Assert.Equal(PlcVerificationPolicy.S7dclConstructsCheck, entry.Check);
        Assert.Contains("project_type_named_like_system_type tmr: altered (TONR_TIME)", entry.Actual);
        Assert.Contains("network 1: preserved", entry.Actual);
        Assert.NotNull(entry.ContentHash);
    }

    [Fact]
    public async Task SimaticMlRoute_FiresNoS7dclGuard()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var bundle = "--- FILE: Main.xml ---\n<Document/>\n" + S7dclRoute.Replace("--- FILE: Main.s7res ---\nres\n", string.Empty);

        var response = await fixture.RunAsync(true, Update(bundle, await fixture.ReadHashAsync(Main, "xml")));

        Assert.Equal("preview", response.Phase);
        Assert.DoesNotContain(response.Guards, g => g.Id is PlcGuardDefinitions.S7dclImportMayAlter or PlcGuardDefinitions.TextualInterfaceOverwrite);
    }

    [Fact]
    public async Task TextualInterface_FiresTheOverwriteAndSaveGuards_WithTheLedgerCount_AndFlagsTheRead()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, Update(TextualBundle, await fixture.ReadHashAsync(Main, "xml")));

        Assert.True(response.Success);
        var overwrite = Assert.Single(response.Guards, g => g.Id == PlcGuardDefinitions.TextualInterfaceOverwrite);
        Assert.Equal(WriteGuardSeverities.Info, overwrite.Severity);
        Assert.Contains("exported it 2 time(s) since it opened the project", overwrite.Message);
        Assert.Contains("does not avoid it with this write path", overwrite.Message);
        Assert.Contains("save_project", Assert.Single(response.Guards, g => g.Id == PlcGuardDefinitions.SaveBeforeWrite).Message);
        Assert.Contains(response.Batch!.Operations[0].Warnings, w => w.Contains("textual interface"));
        Assert.Contains(PlcBlockExportStateMethods.GetExportState, requests.Methods());

        var read = new PlcOperationRequest { OperationId = "read", Operation = "get_block_content", BlockPath = Main, Format = "xml" };
        var item = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        Assert.Contains(item.Result!.Value.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("textual interface"));
    }
}
