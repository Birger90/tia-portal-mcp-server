using TiaMcpServer.Contracts.Plc;
using Xunit;
using static TiaMcpServer.Contracts.Plc.PlcDownloadPolicy;
using static TiaMcpServer.Tests.PlcDownload.PlcDownloadTestData;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>The Siemens-free defaults, caller-decision rules, scope map and interface rules shared by host and worker.</summary>
public sealed class PlcDownloadPolicyTests
{
    private static PlcDownloadDecisionSet Set(PlcDownloadDecisionInfo[]? decisions = null, PlcDownloadPasswordInfo[]? passwords = null,
        bool start = true, string pc = "PLCSIM")
        => new(decisions, passwords, start, pc);

    [Fact]
    public void AbortMessage_IsOneTextForWorkerHostAndFakeWorker()
    {
        var pre = AbortMessage(new PlcDownloadAbortInfo
        {
            Phase = Phases.PreDownload, Type = Configurations + "Unknown", Reason = AbortReasons.UnsupportedConfiguration,
        });
        Assert.Equal($"plc_download aborted in the {Phases.PreDownload} phase: TIA Portal presented '{Configurations}Unknown' ({AbortReasons.UnsupportedConfiguration}). "
            + $"Its type is not a public type of {ConfigurationsNamespace}, so no decision can be given for it. "
            + "The PLC or simulator may already have been stopped. Inspect it before retrying.", pre);
        var rejected = AbortMessage(new PlcDownloadAbortInfo { Phase = Phases.PostDownload, Type = "T", DecisionKey = "T", Reason = AbortReasons.DecisionRejected });
        Assert.Contains("TIA Portal rejected the answer for 'T'.", rejected);
        Assert.EndsWith("The download was already transferred; the PLC or simulator may be stopped. Inspect it before retrying.", rejected);
    }

    [Theory]
    [InlineData("StopModules", "StopAll")]
    [InlineData("ConsistentBlocksDownload", "ConsistentDownload")]
    [InlineData("AlarmTextLibrariesDownload", "ConsistentDownload")]
    [InlineData("OverwriteSystemData", "Overwrite")]
    [InlineData("TargetForSoftware", "PlcSimulationAdvanced")]
    public void PreDownloadDefaults_OnPlcsim(string type, string selection)
    {
        foreach (var start in new[] { true, false })
        {
            var decision = Set(start: start).Resolve(Phases.PreDownload, Configurations + type);
            Assert.True(decision.Handled);
            Assert.Equal((Decisions.Selected, selection, Sources.Default), (decision.Decision, decision.Selection, decision.Source));
            Assert.Null(decision.AbortReason);
        }
    }

    [Theory]
    [InlineData("PLCSIM", "PlcSimulationAdvanced")]
    [InlineData(Hardware, "CPU")]
    [InlineData("plcsim", "CPU")]
    [InlineData("PLCSIM ", "CPU")]
    public void TargetForSoftwareDefault_FollowsTheExactPcInterfaceName(string pc, string selection)
    {
        Assert.Equal(selection, TargetForSoftwareDefault(pc));
        Assert.Equal(selection, Set(pc: pc).Resolve(Phases.PreDownload, ConfigurationTypes.TargetForSoftware).Selection);
    }

    [Theory]
    [InlineData(true, "StartModule")]
    [InlineData(false, "NoAction")]
    public void PostDownload_StartModulesFollowsStartAfterDownload(bool startAfterDownload, string selection)
    {
        var decision = Set(start: startAfterDownload).Resolve(Phases.PostDownload, ConfigurationTypes.StartModules);
        Assert.True(decision.Handled);
        Assert.Equal(selection, decision.Selection);
        Assert.Equal(Sources.Default, decision.Source);
    }

    [Fact]
    public void DefaultsCoverExactlySixTypes()
    {
        Assert.Equal(new[]
        {
            ConfigurationTypes.AlarmTextLibrariesDownload, ConfigurationTypes.ConsistentBlocksDownload, ConfigurationTypes.OverwriteSystemData,
            ConfigurationTypes.StartModules, ConfigurationTypes.StopModules, ConfigurationTypes.TargetForSoftware,
        }, DefaultConfigurationTypes.Order(StringComparer.Ordinal));
        foreach (var type in DefaultConfigurationTypes)
            Assert.True(DefaultSelection(Phases.PreDownload, type, true, "PLCSIM") is not null
                || DefaultSelection(Phases.PostDownload, type, true, "PLCSIM") is not null);
    }

    public static TheoryData<string, string?, string> UndecidedConfigurations => new()
    {
        // Defaulted types in the wrong phase have a key, so a caller decision could answer them.
        { Phases.PostDownload, ConfigurationTypes.StopModules, AbortReasons.DecisionRequired },
        { Phases.PostDownload, ConfigurationTypes.TargetForSoftware, AbortReasons.DecisionRequired },
        { Phases.PreDownload, ConfigurationTypes.StartModules, AbortReasons.DecisionRequired },
        // Consequences without a default, and every password prompt without a passwords entry.
        { Phases.PreDownload, Configurations + "DataBlockReinitialization", AbortReasons.DecisionRequired },
        { Phases.PreDownload, Configurations + "ResetModule", AbortReasons.DecisionRequired },
        { Phases.PreDownload, Configurations + "UpgradeTargetDevice", AbortReasons.DecisionRequired },
        { Phases.PreDownload, Configurations + "ModuleWriteAccessPassword", AbortReasons.DecisionRequired },
        { Phases.PostDownload, Configurations + "StartBackupModules", AbortReasons.DecisionRequired },
        // No decision key: no type of the namespace, a malformed name, an unknown phase.
        { Phases.PreDownload, null, AbortReasons.UnsupportedConfiguration },
        { Phases.PreDownload, "StopModules", AbortReasons.UnsupportedConfiguration },
        { Phases.PreDownload, Configurations + "Nested+Type", AbortReasons.UnsupportedConfiguration },
        { "during_download", ConfigurationTypes.StopModules, AbortReasons.UnsupportedConfiguration },
    };

    [Theory]
    [MemberData(nameof(UndecidedConfigurations))]
    public void WithoutDecisionOrDefault_Aborts(string phase, string? type, string reason)
    {
        var decision = Set().Resolve(phase, type);
        Assert.False(decision.Handled);
        Assert.Null(decision.Selection);
        Assert.Null(decision.Source);
        Assert.Equal(reason, decision.AbortReason);
    }

    [Fact]
    public void CallerDecisions_OverrideDefaults_AndApplyInEitherPhase()
    {
        var set = Set(new[] { Select("StopModules", "NoAction"), Select("ResetModule", "DeleteAll"), Check("UpgradeTargetDevice") },
            new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }, pc: Hardware);

        var stop = set.Resolve(Phases.PreDownload, ConfigurationTypes.StopModules);
        Assert.Equal((Decisions.Selected, "NoAction", Sources.Caller), (stop.Decision, stop.Selection, stop.Source));
        Assert.Equal("NoAction", set.Resolve(Phases.PostDownload, ConfigurationTypes.StopModules).Selection);
        var reset = set.Resolve(Phases.PostDownload, Configurations + "ResetModule");
        Assert.Equal((Decisions.Selected, "DeleteAll", Sources.Caller), (reset.Decision, reset.Selection, reset.Source));
        var check = set.Resolve(Phases.PreDownload, Configurations + "UpgradeTargetDevice");
        Assert.Equal((Decisions.Checked, null, Sources.Caller), (check.Decision, check.Selection, check.Source));
        var password = set.Resolve(Phases.PreDownload, Configurations + "ModuleWriteAccessPassword");
        Assert.Equal((Decisions.PasswordSet, "PLC_PW", Sources.Caller), (password.Decision, password.PasswordVariable, password.Source));
        // Untouched defaults stay: TargetForSoftware on hardware is the CPU.
        Assert.Equal(("CPU", Sources.Default), (set.Resolve(Phases.PreDownload, ConfigurationTypes.TargetForSoftware).Selection,
            set.Resolve(Phases.PreDownload, ConfigurationTypes.TargetForSoftware).Source));
        // Keys are exact and case-sensitive.
        Assert.Equal(AbortReasons.DecisionRequired, set.Resolve(Phases.PreDownload, Configurations + "resetModule").AbortReason);
    }

    public static TheoryData<string, PlcDownloadDecisionInfo[], PlcDownloadPasswordInfo[]> InvalidDecisions => new()
    {
        { "not a configuration type short name", new[] { Select("Reset Module", "DeleteAll") }, [] },
        { "not a configuration type short name", new[] { Select("", "DeleteAll") }, [] },
        { "use 'ResetModule', not the full type name", new[] { Select(Configurations + "ResetModule", "DeleteAll") }, [] },
        { "more than once", new[] { Select("ResetModule", "DeleteAll"), Check("ResetModule") }, [] },
        { "decisions.StartModules is not accepted", new[] { Select("StartModules", "NoAction") }, [] },
        { "decisions.ResetModule must be a non-empty enum member name", new[] { Select("ResetModule", "") }, [] },
        { "decisions.ResetModule must be a non-empty enum member name", new[] { Select("ResetModule", " ") }, [] },
        { "decisions.ResetModule must be a non-empty enum member name", new[] { Select("ResetModule", "Delete All") }, [] },
        { "decisions.ResetModule must be a non-empty enum member name", new[] { new PlcDownloadDecisionInfo { Type = "ResetModule" } }, [] },
        { "either a selection name or true, not both", new[] { new PlcDownloadDecisionInfo { Type = "ResetModule", Selection = "DeleteAll", Check = true } }, [] },
        { "listed in both decisions and passwords", new[] { Check("ModuleWriteAccessPassword") }, new[] { Password("ModuleWriteAccessPassword", "PW") } },
        { "must be the NAME of an environment variable", [], new[] { Password("ModuleWriteAccessPassword", "") } },
        { "must be the NAME of an environment variable", [], new[] { Password("ModuleWriteAccessPassword", "s3cr3t!") } },
        { "must be the NAME of an environment variable", [], new[] { Password("ModuleWriteAccessPassword", "1PW") } },
        { "more than once", [], new[] { Password("BlockBindingPassword", "A"), Password("BlockBindingPassword", "B") } },
        { "at most 64 entries", Enumerable.Range(0, 65).Select(index => Check("Type" + index)).ToArray(), [] },
    };

    [Theory]
    [MemberData(nameof(InvalidDecisions))]
    public void DecisionShapeRules_RefuseInvalidOrAmbiguousInput_WithoutEchoingSecrets(string expected,
        PlcDownloadDecisionInfo[] decisions, PlcDownloadPasswordInfo[] passwords)
    {
        var message = ValidateDecisions(decisions, passwords);
        Assert.NotNull(message);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("s3cr3t!", message);
        Assert.DoesNotContain("Delete All", message);
    }

    [Fact]
    public void DecisionShapeRules_AcceptUnknownTypesChecksAndPasswordVariables()
    {
        Assert.Null(ValidateDecisions(new[] { Select("SomeFutureConfiguration", "Whatever_1"), Check("CheckBeforeDownload") },
            new[] { Password("PlcMasterSecretPassword", "PLC_MASTER_SECRET"), Password("_X", "_pw2") }));
        Assert.Null(ValidateDecisions(Array.Empty<PlcDownloadDecisionInfo>(), Array.Empty<PlcDownloadPasswordInfo>()));
        Assert.NotNull(ValidateDecisions(null, Array.Empty<PlcDownloadPasswordInfo>()));
    }

    [Theory]
    [InlineData(Scopes.HardwareAndSoftware, "Hardware, Software")]
    [InlineData(Scopes.Software, "Software")]
    [InlineData(Scopes.SoftwareOnlyChanges, "SoftwareOnlyChanges")]
    public void Scope_MapsToDownloadOptionNames(string scope, string options)
    {
        Assert.True(TryGetDownloadOptionNames(scope, out var names));
        Assert.Equal(options, names);
        var members = names.Split(", ");
        Assert.DoesNotContain("None", members);
        Assert.False(members.Contains("Software") && members.Contains("SoftwareOnlyChanges"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Software")]
    [InlineData("hardware")]
    [InlineData("hardware_only")]
    public void UnknownScope_HasNoOptions(string? scope)
    {
        Assert.False(TryGetDownloadOptionNames(scope, out var names));
        Assert.Empty(names);
    }

    [Theory]
    [InlineData("PLCSIM", true)]
    [InlineData("plcsim", false)]
    [InlineData("PLCSIM ", false)]
    [InlineData(" PLCSIM", false)]
    [InlineData("PLCSIM Advanced", false)]
    [InlineData("Intel(R) Ethernet Connection", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheExactPlcsimInterfaceIsASimulationTarget(string? name, bool expected)
        => Assert.Equal(expected, IsSimulationInterface(name));

    [Theory]
    [InlineData("decline", "No")]
    [InlineData("accept", "Yes")]
    public void PortalConfirmationPolicies_MapToTheirAnswer(string policy, string answer)
    {
        Assert.True(PortalConfirmationPolicies.IsKnown(policy));
        Assert.Equal(answer, PortalConfirmationPolicies.AnswerFor(policy));
        Assert.False(PortalConfirmationPolicies.IsKnown("Accept"));
    }

    [Fact]
    public void AbortMessages_NameTheKeyAndWhatToPass_ButNeverAPassword()
    {
        PlcDownloadAbortInfo Abort(string kind, string reason, params string[] available) => new()
        {
            Phase = Phases.PreDownload, Type = Configurations + "X", CanonicalType = Configurations + "X", DecisionKey = "X",
            Kind = kind, AvailableSelections = available.ToList(), Reason = reason,
        };

        Assert.Contains("decisions: { \"X\": \"<selection>\" } using one of: DeleteAll, NoAction",
            AbortMessage(Abort(Kinds.Selection, AbortReasons.DecisionRequired, "DeleteAll", "NoAction")));
        Assert.Contains("decisions: { \"X\": true }", AbortMessage(Abort(Kinds.Check, AbortReasons.DecisionRequired)));
        Assert.Contains("passwords: { \"X\": \"<NAME_OF_ENVIRONMENT_VARIABLE>\" }", AbortMessage(Abort(Kinds.Password, AbortReasons.DecisionRequired)));
        Assert.Contains("Available: DeleteAll, NoAction", AbortMessage(Abort(Kinds.Selection, AbortReasons.SelectionUnavailable, "DeleteAll", "NoAction")));
        var post = Abort(Kinds.Other, AbortReasons.UnsupportedConfiguration);
        post.Phase = Phases.PostDownload;
        Assert.Contains("already transferred", AbortMessage(post));
        var password = Abort(Kinds.Password, AbortReasons.PasswordUnavailable);
        password.Detail = "Environment variable 'PLC_PW' is not set or is empty in the worker process.";
        Assert.Contains("'PLC_PW'", AbortMessage(password));
    }

    [Fact]
    public void CompletionIsNeverClaimed()
    {
        Assert.Contains("Not verified", CompletionNote);
        Assert.Contains("may return before the device", CompletionNote);
        Assert.False(new PlcDownloadResultInfo().CompletionVerified);
    }
}
