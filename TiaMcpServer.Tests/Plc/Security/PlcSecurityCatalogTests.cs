using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Security;

/// <summary>Policy, validation and wiring of the CPU protection operations; nothing here needs TIA Portal or a worker.</summary>
public sealed class PlcSecurityCatalogTests
{
    private static PlcOperationRequest Op(string operation, string? accessLevel = null, string? plcName = null, string id = "a")
        => new() { OperationId = id, Operation = operation, AccessLevel = accessLevel, PlcName = plcName };

    [Theory]
    [InlineData("FullAccess")]
    [InlineData("FullAccessIncludingFailsafe")]
    [InlineData("ReadAccess")]
    [InlineData("HMIAccess")]
    [InlineData("NoAccess")]
    public void EveryLevelOfTheProtectionDialogIsSettable(string level)
    {
        Assert.Null(PlcSecurityPolicy.AccessLevelError(level));
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcSecurityMethods.SetAccessLevel, level) }).IsValid);
    }

    [Theory]
    [InlineData("None", "never set")]
    [InlineData("fullaccess", "case-sensitive")]
    [InlineData("Full", "not supported")]
    [InlineData(" ", "required")]
    public void EveryOtherLevelIsRefusedBeforeAnyWorkerCall(string level, string reason)
    {
        Assert.Contains(reason, PlcSecurityPolicy.AccessLevelError(level));
        var validation = PlcOperationCatalog.ValidateWrite(new[] { Op(PlcSecurityMethods.SetAccessLevel, level) });
        Assert.False(validation.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, validation.Category);
        Assert.Contains("FullAccessIncludingFailsafe, FullAccess, ReadAccess, HMIAccess, NoAccess", validation.Error);
    }

    [Fact]
    public void TheDocumentedLevelsAreTheSkillsSix_AndNoneIsNotSettable()
    {
        Assert.Equal(new[] { "None", "FullAccess", "ReadAccess", "HMIAccess", "NoAccess", "FullAccessIncludingFailsafe" }, PlcSecurityPolicy.DocumentedAccessLevels);
        Assert.DoesNotContain("None", PlcSecurityPolicy.SettableAccessLevels);
        Assert.All(PlcSecurityPolicy.SettableAccessLevels, level => Assert.Contains(level, PlcSecurityPolicy.DocumentedAccessLevels));
    }

    [Fact]
    public void FieldSets_AccessLevelRequiredOnlyForTheLevelWrite()
    {
        Assert.True(PlcOperationCatalog.TryGetWriteFields(PlcSecurityMethods.SetAccessLevel, out var required, out var optional));
        Assert.Equal(new[] { "accessLevel" }, required);
        Assert.Equal(new[] { "plcName", "accessLevelPasswords", "resetAccessLevelPasswords" }, optional);
        Assert.True(PlcOperationCatalog.TryGetWriteFields(PlcSecurityMethods.DisableConfidentialDataProtection, out required, out optional));
        Assert.Empty(required);
        Assert.Equal(new[] { "plcName", "passwordVariable", "allConfiguration" }, optional);
        Assert.True(PlcOperationCatalog.TryGetWriteFields(PlcSecurityMethods.ProtectConfidentialData, out required, out optional));
        Assert.Empty(required);
        Assert.Equal(new[] { "plcName", "passwordVariable", "allConfiguration" }, optional);
        Assert.True(PlcOperationCatalog.TryGetWriteFields(PlcSecurityMethods.ChangeConfidentialDataPassword, out required, out optional));
        Assert.Equal(new[] { "passwordVariable", "newPasswordVariable" }, required);

        var missing = PlcOperationCatalog.ValidateWrite(new[] { Op(PlcSecurityMethods.SetAccessLevel) });
        Assert.Contains("missing required field(s): accessLevel", missing.Error);
        var foreign = PlcOperationCatalog.ValidateWrite(new[] { Op(PlcSecurityMethods.DisableConfidentialDataProtection, "FullAccess") });
        Assert.Contains("'accessLevel' is not valid for disable_plc_confidential_data_protection", foreign.Error);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcSecurityMethods.DisableConfidentialDataProtection, plcName: "PLC_1") }).IsValid);
    }

    [Fact]
    public void TheReadTakesOnlyPlcName_AndTheWritesAreNotReads()
    {
        Assert.Contains(PlcSecurityMethods.GetSecurity, PlcOperationCatalog.ReadOperationNames);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcSecurityMethods.GetSecurity) }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { Op(PlcSecurityMethods.GetSecurity, plcName: "PLC_1") }).IsValid);
        Assert.Contains("'accessLevel' is not valid for get_plc_security",
            PlcOperationCatalog.ValidateRead(new[] { Op(PlcSecurityMethods.GetSecurity, "FullAccess") }).Error);
        Assert.Contains("is a write operation; use plc_write",
            PlcOperationCatalog.ValidateRead(new[] { Op(PlcSecurityMethods.SetAccessLevel, "FullAccess") }).Error);
        Assert.Contains("is a read operation; use plc_read",
            PlcOperationCatalog.ValidateWrite(new[] { Op(PlcSecurityMethods.GetSecurity) }).Error);
    }

    [Fact]
    public void Capabilities_ReadObservesInEveryMode_WritesAreProjectMutationsInWritableModesOnly()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcSecurityMethods.GetSecurity));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, PlcSecurityMethods.GetSecurity));
        foreach (var write in PlcSecurityMethods.Write)
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(write));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, write));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, write));
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(write));
        }

        Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(PlcSecurityMethods.GetSecurity));
        Assert.All(PlcSecurityMethods.All, m => Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(m)));
        Assert.All(PlcSecurityMethods.All, m => Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, m)));
        Assert.Contains("is not permitted in read-only mode",
            Assert.Single(PlcOperationCatalog.ValidateAccessMode(new[] { Op(PlcSecurityMethods.SetAccessLevel, "FullAccess") }, McpAccessMode.ReadOnly)));
    }

    [Fact]
    public void ApiMemberMissing_IsAnApprovedCategory()
    {
        Assert.Equal("api_member_missing", WorkerFailureCategories.ApiMemberMissing);
        Assert.True(WorkerFailureCategories.IsKnown(WorkerFailureCategories.ApiMemberMissing));
        Assert.False(WorkerFailureCategories.IsPreconditionRefusal(WorkerFailureCategories.ApiMemberMissing));
    }

    [Fact]
    public void MethodLists_AreDisjointAndComplete()
    {
        Assert.Equal(5, PlcSecurityMethods.All.Count);
        Assert.Empty(PlcSecurityMethods.Read.Intersect(PlcSecurityMethods.Write));
        Assert.True(PlcSecurityMethods.IsWorkerMethod(PlcSecurityMethods.DisableConfidentialDataProtection));
        Assert.False(PlcSecurityMethods.IsWorkerMethod("set_plc_password"));
    }

    [Fact]
    public void TheWorkerRequestCarriesOnlyPasswordSources()
    {
        Assert.Equal(new[] { "AccessLevel", "AccessLevelPasswords", "AllConfiguration", "NewPasswordVariable", "PasswordVariable", "PlcName", "ResetAccessLevelPasswords" },
            typeof(PlcSecurityRequestInfo).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        var op = Op(PlcSecurityMethods.SetAccessLevel, "HMIAccess", "PLC_1");
        op.AccessLevelPasswords = new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(new[] { new PlcDownloadPasswordInfo { Type = "FullAccess", Variable = "credential:TiaMcp/P/PLC_1/Full" } });
        op.ResetAccessLevelPasswords = new[] { "ReadAccess" };
        var forwarded = PlcSecurityPayloadContract.ToRequest(op);
        Assert.Equal(("PLC_1", "HMIAccess"), (forwarded.PlcName, forwarded.AccessLevel));
        Assert.Equal(("FullAccess", "credential:TiaMcp/P/PLC_1/Full"), (forwarded.AccessLevelPasswords.Single().Level, forwarded.AccessLevelPasswords.Single().Source));
        Assert.Equal(new[] { "ReadAccess" }, forwarded.ResetAccessLevelPasswords);
    }

    [Fact]
    public void PasswordFields_AreValidated_AndNeverEchoed()
    {
        var op = Op(PlcSecurityMethods.SetAccessLevel, "NoAccess", "PLC_1");
        op.AccessLevelPasswords = new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(new[]
        {
            new PlcDownloadPasswordInfo { Type = "NoAccess", Variable = "PW" },
            new PlcDownloadPasswordInfo { Type = "FullAccess", Variable = TiaMcpServer.PlcDownload.PlcDownloadPasswordMap.RedactedVariable },
        });
        op.ResetAccessLevelPasswords = new[] { "FullAccess", "FullAccess" };
        var error = PlcOperationCatalog.ValidateWrite(new[] { op }).Error!;
        Assert.Contains("'NoAccess', which takes no password", error);
        Assert.Contains("never the password itself; the value was redacted", error);
        Assert.Contains("must not repeat a level", error);
        Assert.Contains("both accessLevelPasswords and resetAccessLevelPasswords", error);

        var protect = Op(PlcSecurityMethods.ProtectConfidentialData, plcName: "PLC_1");
        Assert.Contains("needs passwordVariable", PlcOperationCatalog.ValidateWrite(new[] { protect }).Error);
        protect.AllConfiguration = true;
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { protect }).IsValid);
        var disable = Op(PlcSecurityMethods.DisableConfidentialDataProtection, plcName: "PLC_1");
        disable.AllConfiguration = true;
        disable.PasswordVariable = "PW";
        Assert.Contains("takes no password", PlcOperationCatalog.ValidateWrite(new[] { disable }).Error);
    }

    [Fact]
    public void CompareToOnline_TakesOnlinePasswordSources()
    {
        var compare = new PlcOperationRequest { OperationId = "c", Operation = "compare_to_online", PlcName = "PLC_1" };
        compare.Passwords = new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(new[] { new PlcDownloadPasswordInfo { Type = "OnlineReadAccessPassword", Variable = "credential:TiaMcp/P/PLC_1/Read" } });
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { compare }).IsValid);
        compare.Passwords = new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(new[] { new PlcDownloadPasswordInfo { Type = "OnlineReadAccessPassword", Variable = TiaMcpServer.PlcDownload.PlcDownloadPasswordMap.RedactedVariable } });
        Assert.Contains("never the password itself", PlcOperationCatalog.ValidateRead(new[] { compare }).Error);
        var read = Op(PlcSecurityMethods.GetSecurity);
        read.Passwords = new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(Array.Empty<PlcDownloadPasswordInfo>());
        Assert.Contains("'passwords' is not valid for get_plc_security", PlcOperationCatalog.ValidateRead(new[] { read }).Error);
    }

    [Fact]
    public void LevelsAbove_NameThePasswordsAProtectedLevelNeeds()
    {
        Assert.Empty(PlcSecurityPolicy.LevelsAbove("FullAccessIncludingFailsafe"));
        Assert.Equal(new[] { "FullAccessIncludingFailsafe" }, PlcSecurityPolicy.LevelsAbove("FullAccess"));
        Assert.Equal(new[] { "FullAccessIncludingFailsafe", "FullAccess", "ReadAccess" }, PlcSecurityPolicy.LevelsAbove("HMIAccess"));
        Assert.Equal(PlcSecurityPolicy.PasswordLevels, PlcSecurityPolicy.LevelsAbove("NoAccess"));
    }

    [Fact]
    public void TheServerPasswordMap_FillsOmittedSources_ButNeverOverridesTheCall()
    {
        var map = new Dictionary<string, string> { ["FullAccess"] = "credential:TiaMcp/Map/Full", ["ReadAccess"] = "MAP_READ", ["PlcMasterSecretPassword"] = "MAP_SECRET" };
        string? Lookup(PlcOperationRequest _, string purpose) => map.TryGetValue(purpose, out var source) ? source : null;
        var level = Op(PlcSecurityMethods.SetAccessLevel, "HMIAccess", "PLC_1");
        level.AccessLevelPasswords = new TiaMcpServer.PlcDownload.PlcDownloadPasswordMap(new[] { new PlcDownloadPasswordInfo { Type = "FullAccess", Variable = "CALL_FULL" } });
        PlcWriteTools.WithMappedCpuProtectionPasswords(level, Lookup);
        Assert.Equal(new[] { ("FullAccess", "CALL_FULL"), ("ReadAccess", "MAP_READ") }, level.AccessLevelPasswords!.Entries.Select(e => (e.Type, e.Variable)));

        var protect = Op(PlcSecurityMethods.ProtectConfidentialData, plcName: "PLC_1");
        PlcWriteTools.WithMappedCpuProtectionPasswords(protect, Lookup);
        Assert.Equal("MAP_SECRET", protect.PasswordVariable);
        var disableAll = Op(PlcSecurityMethods.DisableConfidentialDataProtection, plcName: "PLC_1");
        disableAll.AllConfiguration = true;
        PlcWriteTools.WithMappedCpuProtectionPasswords(disableAll, Lookup);
        Assert.Null(disableAll.PasswordVariable);
    }

    [Fact]
    public void Planning_TargetsOneSettingOfThePlc_AndFiresTheInfoGuard()
    {
        var state = PlcWorkingState.Create(new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" }, true, tree: null);

        var level = state.Resolve(Op(PlcSecurityMethods.SetAccessLevel, "FullAccess", "PLC_1"));
        Assert.Null(level.Error);
        Assert.Equal(new PlcTargetIdentity(PlcWorkingState.PlcSecurityKind, "PLC_1", "Station_1", null, null, "accessLevel", null, null), level.Effect!.Target);
        Assert.Equal(new PlcFieldChange("accessLevel", null, "FullAccess"), Assert.Single(level.Effect.Changes));
        Assert.Equal(PlcGuardDefinitions.LowersCpuProtection, Assert.Single(level.Guards).Id);

        var confidential = state.Resolve(Op(PlcSecurityMethods.DisableConfidentialDataProtection, plcName: "PLC_1"));
        Assert.Equal("confidentialDataProtection", confidential.Effect!.Target.Name);
        Assert.Equal(PlcGuardDefinitions.LowersCpuProtection, Assert.Single(confidential.Guards).Id);
        Assert.Contains("Unprotect()", confidential.Guards[0].Message);

        var protectedLevel = state.Resolve(Op(PlcSecurityMethods.SetAccessLevel, "NoAccess", "PLC_1"));
        Assert.Equal(PlcGuardDefinitions.RaisesCpuProtection, Assert.Single(protectedLevel.Guards).Id);
        Assert.Contains("needs at least the FullAccess password", protectedLevel.Guards[0].Message);
    }

    [Fact]
    public void UnverifiablePlc_KeepsTheRequestedSettingIdentity()
    {
        var effect = PlcWorkingState.Unresolved(Op(PlcSecurityMethods.DisableConfidentialDataProtection), "PLC_9", null);
        Assert.Equal(PlcWorkingState.PlcSecurityKind, effect.Target.Kind);
        Assert.Equal("confidentialDataProtection", effect.Target.Name);
    }

    [Fact]
    public void VerificationKeys_SeparateTheTwoSettings_AndMatchTheSameSetting()
    {
        var a = Op(PlcSecurityMethods.SetAccessLevel, "FullAccess", "PLC_1", "a");
        var b = Op(PlcSecurityMethods.DisableConfidentialDataProtection, plcName: "PLC_1", id: "b");
        var c = Op(PlcSecurityMethods.SetAccessLevel, "FullAccessIncludingFailsafe", "PLC_1", "c");
        var state = PlcWorkingState.Create(new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1" }, true, tree: null);
        var effects = new[] { a, b, c }.ToDictionary(item => item.OperationId, item => state.Resolve(item).Effect!, StringComparer.Ordinal);
        var policy = new PlcVerificationPolicy(null!, effects);

        Assert.Empty(policy.ObjectKeys(a, false).Intersect(policy.ObjectKeys(b, true)));
        Assert.Equal(policy.ObjectKeys(a, false), policy.ObjectKeys(c, true));
        Assert.False(policy.HasReadBack(a));
        Assert.False(policy.HasReadBack(b));
    }
}
