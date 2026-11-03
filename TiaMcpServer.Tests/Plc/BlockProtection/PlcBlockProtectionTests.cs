using System.Security;
using System.Text.Json;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.BlockProtection;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.BlockProtection;

/// <summary>
/// Issue #5: block know-how and write protection. The Siemens-free run against stand-in providers carrying the documented full
/// names, the host catalog, policy, password map and decoding, and the plc-write-roundtrip FakeWorker end to end. Not live-tested:
/// which object lists the write-protection provider, what TIA Portal rejects and whether it compiles afterwards are decided only live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcBlockProtectionTests
{
    private const string PasswordVariable = "TIA_MCP_TEST_BLOCK_PROTECTION";
    private const string NewPasswordVariable = "TIA_MCP_TEST_BLOCK_PROTECTION_NEW";
    private const string InvalidPasswordVariable = "TIA_MCP_TEST_BLOCK_PROTECTION_INVALID";
    private const string Password = "Fake-Block-Pw1";

    static PlcBlockProtectionTests()
    {
        // Set before any worker starts, so the FakeWorker inherits them like the real worker inherits the host's environment.
        Environment.SetEnvironmentVariable(PasswordVariable, Password);
        Environment.SetEnvironmentVariable(NewPasswordVariable, "Fake-Block-Pw2");
        Environment.SetEnvironmentVariable(InvalidPasswordVariable, "has\"quote");
        Environment.SetEnvironmentVariable("TIA_MCP_TEST_KNOW_HOW", Password);
    }

    private static PlcBlockProtectionTarget Target(TestProtectionState state, bool withWrite = true, bool withKnowHow = true)
    {
        var knowHow = new PlcBlockProtectionProvider(state);
        var write = new PlcBlockWriteProtectionProvider(state);
        var offered = new List<Type>();
        if (withKnowHow) offered.Add(typeof(PlcBlockProtectionProvider));
        if (withWrite) offered.Add(typeof(PlcBlockWriteProtectionProvider));
        return new PlcBlockProtectionTarget("PLC_1", "PLC_1/Blocks/Main", "Main", "SCL", new TestProtectableBlock(state), offered,
            type => type == typeof(PlcBlockProtectionProvider) ? knowHow : type == typeof(PlcBlockWriteProtectionProvider) ? write : null);
    }

    private static readonly Func<string, SecureString> Resolve = PlcBlockProtectionRun.Resolver(Environment.GetEnvironmentVariable, null);

    private static PlcBlockProtectionRequestInfo Request(string? password = PasswordVariable, string? newPassword = null)
        => new() { BlockPath = "PLC_1/Main", PasswordSource = password, NewPasswordSource = newPassword };

    [Fact]
    public void Read_ReportsStateProvidersMembersAndInvalidCharacters()
    {
        var state = new TestProtectionState { Write = "x", Enabled = true };
        var info = PlcBlockProtectionRun.Read(Target(state), _ => false);
        Assert.False(info.State.KnowHowProtected);
        Assert.True(info.State.WriteProtectionDefined);
        Assert.True(info.State.WriteProtectionEnabled);
        Assert.False(info.IsFailSafe);
        Assert.True(info.KnowHow.Available);
        Assert.Equal(new[] { "\"", " " }, info.KnowHow.InvalidPasswordCharacters);
        Assert.Equal(2, info.KnowHow.Members.Count);
        Assert.Equal(4, info.WriteProtection.Members.Count);
        Assert.Null(info.WriteProtection.InvalidPasswordCharacters);

        var none = PlcBlockProtectionRun.Read(Target(state, withWrite: false), _ => false);
        Assert.False(none.WriteProtection.ServiceOffered);
        Assert.Null(none.State.WriteProtectionDefined);
        Assert.Contains("does not list", none.WriteProtection.Note);
    }

    [Fact]
    public void Write_KnowHowAndWriteProtection_ReadsTheStateBack_AndReportsOnlySources()
    {
        var state = new TestProtectionState();
        var set = PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.SetKnowHowProtection, Target(state), Request(), _ => false, Resolve);
        Assert.Equal(Password, state.KnowHow);
        Assert.Equal((false, true), (set.Before.KnowHowProtected, set.After.KnowHowProtected));
        Assert.Equal(PlcBlockProtectionNames.ReadBackMatched, set.ReadBack);
        var use = Assert.Single(set.PasswordSources);
        Assert.Equal((PasswordVariable, "environment", PlcBlockProtectionNames.KnowHowPurpose), (use.Source, use.Kind, use.Purpose));
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(set));

        PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.RemoveKnowHowProtection, Target(state), Request(), _ => false, Resolve);
        Assert.Null(state.KnowHow);

        PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.DefineWriteProtection, Target(state), Request(), _ => false, Resolve);
        var enabled = PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.SetWriteProtection, Target(state), Request(), _ => false, Resolve);
        Assert.True(enabled.After.WriteProtectionEnabled);
        var changed = PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.ChangeWriteProtectionPassword, Target(state), Request(newPassword: NewPasswordVariable), _ => false, Resolve);
        Assert.Equal("Fake-Block-Pw2", state.Write);
        Assert.Equal(2, changed.PasswordSources.Count);
        Assert.Contains(changed.Messages, m => m.Contains("not verified"));
        PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.RemoveWriteProtection, Target(state), Request(NewPasswordVariable), _ => false, Resolve);
        Assert.False(state.Enabled);
    }

    [Fact]
    public void Write_RefusesInvalidCharacters_MissingProvidersAndSources_AndAnIgnoredCall()
    {
        var state = new TestProtectionState();
        var invalid = Assert.Throws<RuntimeBindingException>(() =>
            PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.SetKnowHowProtection, Target(state), Request(InvalidPasswordVariable), _ => false, Resolve));
        Assert.Equal(WorkerFailureCategories.ValidationError, invalid.Category);
        Assert.DoesNotContain("has\"quote", invalid.Message);
        Assert.Empty(state.Calls);

        var missing = Assert.Throws<RuntimeBindingException>(() =>
            PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.DefineWriteProtection, Target(state, withWrite: false), Request(), _ => false, Resolve));
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, missing.Category);

        var unset = Assert.Throws<RuntimeBindingException>(() =>
            PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.SetKnowHowProtection, Target(state), Request("TIA_MCP_TEST_UNSET_VARIABLE_XYZ"), _ => false, Resolve));
        Assert.Equal(WorkerFailureCategories.ValidationError, unset.Category);

        var noNew = Assert.Throws<RuntimeBindingException>(() =>
            PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.ChangeWriteProtectionPassword, Target(state), Request(), _ => false, Resolve));
        Assert.Equal(WorkerFailureCategories.ValidationError, noNew.Category);

        state.Silent = true;
        var ignored = Assert.Throws<RuntimeBindingException>(() =>
            PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.SetKnowHowProtection, Target(state), Request(), _ => false, Resolve));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ignored.Category);

        var bare = Assert.Throws<RuntimeBindingException>(() => PlcBlockProtectionRun.Write(PlcBlockProtectionMethods.SetKnowHowProtection,
            new PlcBlockProtectionTarget("PLC_1", "PLC_1/Blocks/Main", "Main", "LAD", new TestProtectableBlock(state),
                new List<Type> { typeof(PlcBlockProtectionProvider) }, _ => new object()), Request(), _ => false, Resolve));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, bare.Category);
    }

    [Fact]
    public void Catalog_Policy_AndPasswordMap()
    {
        PlcOperationRequest Op(string operation, string? password = PasswordVariable, string? newPassword = null)
            => new() { OperationId = "p", Operation = operation, BlockPath = "PLC_1/Main", PasswordVariable = password, NewPasswordVariable = newPassword };

        foreach (var method in PlcBlockProtectionMethods.Write.Where(m => m != PlcBlockProtectionMethods.ChangeWriteProtectionPassword))
        {
            Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(method) }).IsValid, method);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(method, password: null) }).IsValid, method);
            Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(method, newPassword: NewPasswordVariable) }).IsValid, method);
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(method));
        }

        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcBlockProtectionMethods.ChangeWriteProtectionPassword, newPassword: NewPasswordVariable) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcBlockProtectionMethods.ChangeWriteProtectionPassword) }).IsValid);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcBlockProtectionMethods.SetKnowHowProtection, password: TiaMcpServer.PlcDownload.PlcDownloadPasswordMap.RedactedVariable) }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { new PlcOperationRequest { OperationId = "r", Operation = PlcBlockProtectionMethods.GetBlockProtection, BlockPath = "PLC_1/Main" } }).IsValid);
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcBlockProtectionMethods.GetBlockProtection));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcBlockProtectionMethods.GetBlockProtection));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcBlockProtectionMethods.SetKnowHowProtection));
        Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(PlcBlockProtectionMethods.GetBlockProtection));

        var previous = TiaMcpServer.PlcDownload.PasswordMapStore.Entries;
        try
        {
            TiaMcpServer.PlcDownload.PasswordMapStore.Set(new[]
            {
                new PasswordMapEntry("Proj", "PLC_1", PlcBlockProtectionNames.KnowHowPurpose, "KNOW_HOW_PW"),
                new PasswordMapEntry(null, null, PlcBlockProtectionNames.WriteProtectionPurpose, "credential:TiaMcp/Alle/Write"),
            }, null);
            var knowHow = Op(PlcBlockProtectionMethods.SetKnowHowProtection, password: null);
            var write = Op(PlcBlockProtectionMethods.ChangeWriteProtectionPassword, password: null, newPassword: NewPasswordVariable);
            var other = new PlcOperationRequest { OperationId = "o", Operation = PlcBlockProtectionMethods.RemoveKnowHowProtection, BlockPath = "PLC_2/Main" };
            PlcWriteTools.WithMappedSafetyPasswords(new[] { knowHow, write, other }, "C:/Projects/Proj/Proj.ap21");
            Assert.Equal("KNOW_HOW_PW", knowHow.PasswordVariable);
            Assert.Equal("credential:TiaMcp/Alle/Write", write.PasswordVariable);
            Assert.Equal(NewPasswordVariable, write.NewPasswordVariable);
            Assert.Null(other.PasswordVariable);
            // The block protection purposes are never offered as download or online password answers.
            Assert.Empty(TiaMcpServer.PlcDownload.PasswordMapStore.Missing("C:/Projects/Proj/Proj.ap21", "PLC_1", Array.Empty<string>()));
        }
        finally
        {
            TiaMcpServer.PlcDownload.PasswordMapStore.Set(previous, null);
        }

        Assert.Contains(TiaMcpServer.Contracts.Shared.PasswordManagement.PasswordPurposes.Known, p => p.Purpose == PlcBlockProtectionNames.KnowHowPurpose);
        Assert.Contains(TiaMcpServer.Contracts.Shared.PasswordManagement.PasswordPurposes.Known, p => p.Purpose == PlcBlockProtectionNames.WriteProtectionPurpose);
    }

    [Fact]
    public void Decode_RequiresTheRequestedSources_AndAConsistentReadBack()
    {
        var item = new PlcOperationRequest { OperationId = "k", Operation = PlcBlockProtectionMethods.SetKnowHowProtection, BlockPath = "PLC_1/Main", PasswordVariable = PasswordVariable };
        var result = PlcBlockProtectionRun.Write(item.Operation, Target(new TestProtectionState()), Request(), _ => false, Resolve);
        var element = PlcBlockProtection.DecodeWrite(item, WorkerJson.SerializePayload(result));
        var results = new Dictionary<string, JsonElement> { ["k"] = element };
        Assert.True(PlcBlockProtection.HasReadBack(item, results));
        var check = PlcBlockProtection.Verify(item, results);
        Assert.Equal((PlcBlockProtection.Check, true), (check.Check, check.Success));

        result.PasswordSources[0].Source = "OTHER_VARIABLE";
        Assert.ThrowsAny<JsonException>(() => PlcBlockProtection.DecodeWrite(item, WorkerJson.SerializePayload(result)));
        result.PasswordSources[0].Source = PasswordVariable;
        result.After.KnowHowProtected = false;
        Assert.ThrowsAny<JsonException>(() => PlcBlockProtection.DecodeWrite(item, WorkerJson.SerializePayload(result)));
        result.After.KnowHowProtected = null;
        result.ReadBack = PlcBlockProtectionNames.ReadBackUnreadable;
        var unreadable = new Dictionary<string, JsonElement> { ["k"] = PlcBlockProtection.DecodeWrite(item, WorkerJson.SerializePayload(result)) };
        Assert.False(PlcBlockProtection.HasReadBack(item, unreadable));
    }

    private static object Item(string id, string operation, string blockPath, string? password = PasswordVariable, string? newPassword = null)
        => newPassword is null
            ? new { operationId = id, operation, blockPath, passwordVariable = password }
            : new { operationId = id, operation, blockPath, passwordVariable = password, newPasswordVariable = newPassword };

    [Fact]
    public async Task Mcp_ProtectsReadsAndUnprotects_AndAFailSafeBlockNeedsTheSafetyLogin()
    {
        var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        var ui = new FakeWorkerUiOpenProject("plc-write-roundtrip");
        var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "plc-write-roundtrip");
        try
        {
            async Task<JsonElement> Call(string tool, params object[] operations)
            {
                var raw = await harness.Client.CallToolAsync(tool, new Dictionary<string, object?> { ["operations"] = operations });
                Assert.Empty(StructuredContractInspector.FindViolations(raw));
                return raw.StructuredContent!.Value;
            }

            var read = (await Call("plc_read", new { operationId = "r", operation = "get_block_protection", blockPath = "Station_1/Main" }))
                .GetProperty("batch").GetProperty("operations")[0];
            Assert.True(read.GetProperty("status").GetString() == "succeeded", read.ToString());
            Assert.False(read.GetProperty("result").GetProperty("state").GetProperty("knowHowProtected").GetBoolean());

            var applied = await Call("plc_write",
                Item("k", "set_block_know_how_protection", "Station_1/Main"),
                Item("d", "define_block_write_protection", "Station_1/Main"),
                Item("w", "set_block_write_protection", "Station_1/Main"),
                Item("c", "change_block_write_protection_password", "Station_1/Main", newPassword: NewPasswordVariable));
            Assert.True(applied.GetProperty("success").GetBoolean(), applied.ToString());
            Assert.Contains(applied.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == PlcGuardDefinitions.SetsBlockProtection);
            var checks = applied.GetProperty("verification").GetProperty("operations").EnumerateArray().ToList();
            Assert.Equal("blockProtection", checks.Single(c => c.GetProperty("operationId").GetString() == "k").GetProperty("check").GetString());
            Assert.DoesNotContain(Password, applied.ToString());

            var after = (await Call("plc_read", new { operationId = "r", operation = "get_block_protection", blockPath = "Station_1/Main" }))
                .GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("state");
            Assert.True(after.GetProperty("knowHowProtected").GetBoolean());
            Assert.True(after.GetProperty("writeProtectionEnabled").GetBoolean());

            var removed = await Call("plc_write",
                Item("u", "remove_block_know_how_protection", "Station_1/Main"),
                Item("x", "remove_block_write_protection", "Station_1/Main", password: NewPasswordVariable));
            Assert.True(removed.GetProperty("success").GetBoolean(), removed.ToString());
            Assert.Contains(removed.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == PlcGuardDefinitions.LowersBlockProtection);

            var unsupported = await Call("plc_write", Item("n", "define_block_write_protection", "PLC_2/Main"));
            Assert.Equal(WorkerFailureCategories.UnsupportedCapability,
                unsupported.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());

            var missing = await Call("plc_write", Item("m", "set_block_know_how_protection", "Station_1/NoSuchBlock"));
            Assert.False(missing.GetProperty("success").GetBoolean());
            Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.GetProperty("error").GetProperty("category").GetString());

            // Security review F7: an unreadable planning read on an F-CPU fires the acceptance guard (fail safe); dry run, nothing asked.
            var rawUnreadable = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?> { ["operations"] = new object[] { Item("l", "set_block_know_how_protection", "Station_1/Locked") }, ["dryRun"] = true });
            var unreadable = rawUnreadable.StructuredContent!.Value;
            var unreadableGuards = unreadable.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()).ToList();
            Assert.Contains(PlcGuardDefinitions.SafetyAcceptance, unreadableGuards);
            Assert.Contains(PlcGuardDefinitions.StateUnverifiable, unreadableGuards);
            Assert.DoesNotContain(PlcGuardDefinitions.SafetyLoginRequired, unreadableGuards);

            var failSafe = await Call("plc_write", Item("f", "set_block_know_how_protection", "Station_1/Safety_FB"));
            Assert.False(failSafe.GetProperty("success").GetBoolean());
            Assert.Contains(failSafe.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == PlcGuardDefinitions.SafetyLoginRequired);
        }
        finally
        {
            await harness.DisposeAsync();
            ui.Dispose();
            audit.Dispose();
        }
    }
}
