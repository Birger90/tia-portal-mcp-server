using Siemens.Engineering.Download.Configurations;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcDownload;
using TiaMcpServer.Tests.PlcDownload.ConfigurationProxies;
using Xunit;
using static TiaMcpServer.Contracts.Plc.PlcDownloadPolicy;
using static TiaMcpServer.Tests.PlcDownload.PlcDownloadTestData;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// The fail-closed callback decision loop the real worker and the FakeWorker share
/// (<see cref="PlcDownloadCallbackRecorder"/>) over a scripted configuration access, the exact
/// canonical type walk, and the worker-side request validation, all without Siemens assemblies.
/// </summary>
public sealed class PlcDownloadCallbackRecorderTests
{
    /// <summary>A scripted configuration: runtime type name, canonical type, kind, enum names, and how answering behaves.</summary>
    private sealed record Scripted(string Type, string? Canonical, string? Message = "message", string Kind = Kinds.Selection,
        string[]? Names = null, PlcDownloadApplyOutcome? Outcome = null, Exception? Failure = null);

    private sealed class ScriptedAccess : IPlcDownloadConfigurationAccess
    {
        public List<(string? Canonical, string Answer)> Applied { get; } = new();

        public string TypeName(object configuration) => ((Scripted)configuration).Type;

        public string? CanonicalType(object configuration) => ((Scripted)configuration).Canonical;

        public string? ReadMessage(object configuration) => ((Scripted)configuration).Message;

        public PlcDownloadConfigurationShape Describe(object configuration)
            => new(((Scripted)configuration).Kind, ((Scripted)configuration).Names ?? new[] { "StopAll", "NoAction" });

        public PlcDownloadApplyOutcome ApplySelection(object configuration, string selection) => Apply(configuration, selection);

        public PlcDownloadApplyOutcome ApplyCheck(object configuration) => Apply(configuration, "checked");

        public PlcDownloadApplyOutcome ApplyPassword(object configuration, string variable) => Apply(configuration, "password:" + variable);

        private PlcDownloadApplyOutcome Apply(object configuration, string answer)
        {
            var scripted = (Scripted)configuration;
            Applied.Add((scripted.Canonical, answer));
            if (scripted.Failure is not null) throw scripted.Failure;
            return scripted.Outcome ?? PlcDownloadApplyOutcome.Success;
        }
    }

    private sealed class Harness
    {
        public Harness(bool startAfterDownload = true, Func<Exception, bool>? isFatal = null, PlcDownloadDecisionInfo[]? decisions = null,
            PlcDownloadPasswordInfo[]? passwords = null, string pc = "PLCSIM", bool? secure = true, bool allowInsecure = false)
        {
            Result = new PlcDownloadResultInfo { StartAfterDownload = startAfterDownload };
            Recorder = new PlcDownloadCallbackRecorder(new PlcDownloadDecisionSet(decisions, passwords, startAfterDownload, pc, allowInsecurePasswordTransfer: allowInsecure),
                Result, Access, isFatal, readSecureCommunication: _ => secure);
        }

        public ScriptedAccess Access { get; } = new();

        public PlcDownloadResultInfo Result { get; }

        public PlcDownloadCallbackRecorder Recorder { get; }

        public List<(string? Canonical, string Answer)> Applied => Access.Applied;

        public PlcDownloadAbortedException Aborts(string phase, Scripted configuration)
            => Assert.Throws<PlcDownloadAbortedException>(() => Recorder.Handle(phase, configuration));
    }

    private static Scripted Defaulted(string type) => new(type, type);

    private static void AssertComplete(PlcDownloadConfigurationInfo seen)
    {
        Assert.True(Phases.IsKnown(seen.Phase));
        Assert.False(string.IsNullOrWhiteSpace(seen.Type));
        Assert.True(Decisions.IsKnown(seen.Decision));
        Assert.True(Kinds.IsKnown(seen.Kind));
        Assert.Equal(DecisionKey(seen.CanonicalType), seen.DecisionKey);
        if (seen.Decision == Decisions.Aborted)
        {
            Assert.Null(seen.Selection);
            Assert.Null(seen.Source);
            Assert.Null(seen.PasswordVariable);
        }
        else
        {
            Assert.NotNull(seen.CanonicalType);
            Assert.True(Sources.IsKnown(seen.Source));
        }
    }

    [Fact]
    public void Defaults_AreAnsweredAndRecordedWithTheirSource()
    {
        var harness = new Harness();
        harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.StopModules));
        harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.TargetForSoftware));
        harness.Recorder.PostDownload(Defaulted(ConfigurationTypes.StartModules));

        Assert.Null(harness.Result.Abort);
        Assert.Equal(new[] { "StopAll", "PlcSimulationAdvanced", "StartModule" }, harness.Result.ConfigurationsSeen.Select(seen => seen.Selection));
        Assert.Equal(new (string?, string)[] { (ConfigurationTypes.StopModules, "StopAll"), (ConfigurationTypes.TargetForSoftware, "PlcSimulationAdvanced"),
            (ConfigurationTypes.StartModules, "StartModule") }, harness.Applied);
        Assert.All(harness.Result.ConfigurationsSeen, seen =>
        {
            AssertComplete(seen);
            Assert.Equal(Decisions.Selected, seen.Decision);
            Assert.Equal(Sources.Default, seen.Source);
            Assert.Equal("message", seen.Message);
            Assert.Equal(new[] { "StopAll", "NoAction" }, seen.AvailableSelections);
        });
    }

    [Fact]
    public void Hardware_TargetForSoftwareDefaultIsTheCpu()
    {
        var harness = new Harness(pc: Hardware);
        harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.TargetForSoftware));
        Assert.Equal((ConfigurationTypes.TargetForSoftware, "CPU"), Assert.Single(harness.Applied));
    }

    [Theory]
    [InlineData(true, "StartModule")]
    [InlineData(false, "NoAction")]
    public void StartAfterDownload_SelectsStartModuleOrNoAction(bool start, string selection)
    {
        var harness = new Harness(start);
        harness.Recorder.PostDownload(Defaulted(ConfigurationTypes.StartModules));
        Assert.Equal((ConfigurationTypes.StartModules, selection), Assert.Single(harness.Applied));
        Assert.Equal(selection, Assert.Single(harness.Result.ConfigurationsSeen).Selection);
    }

    [Fact]
    public void CallerDecisions_OverrideDefaults_AndAnswerCheckAndPasswordConfigurations()
    {
        var harness = new Harness(decisions: new[] { Select("StopModules", "NoAction"), Check("UpgradeTargetDevice") },
            passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") });
        harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.StopModules));
        harness.Recorder.PreDownload(new Scripted(Configurations + "UpgradeTargetDevice", Configurations + "UpgradeTargetDevice", Kind: Kinds.Check));
        harness.Recorder.PreDownload(new Scripted(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", Kind: Kinds.Password));
        harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.ConsistentBlocksDownload));

        Assert.Equal(new (string?, string)[]
        {
            (ConfigurationTypes.StopModules, "NoAction"), (Configurations + "UpgradeTargetDevice", "checked"),
            (Configurations + "ModuleWriteAccessPassword", "password:PLC_PW"), (ConfigurationTypes.ConsistentBlocksDownload, "ConsistentDownload"),
        }, harness.Applied);
        var seen = harness.Result.ConfigurationsSeen;
        Assert.All(seen, AssertComplete);
        Assert.Equal(new[] { Decisions.Selected, Decisions.Checked, Decisions.PasswordSet, Decisions.Selected }, seen.Select(entry => entry.Decision));
        Assert.Equal(new[] { Sources.Caller, Sources.Caller, Sources.Caller, Sources.Default }, seen.Select(entry => entry.Source));
        Assert.Equal(new string?[] { "NoAction", null, null, "ConsistentDownload" }, seen.Select(entry => entry.Selection));
        Assert.Equal("PLC_PW", seen[2].PasswordVariable);
        Assert.Empty(seen[1].AvailableSelections); // Only selection configurations list enum names.
        Assert.Equal(new[] { "StopModules", "UpgradeTargetDevice", "ModuleWriteAccessPassword", "ConsistentBlocksDownload" },
            seen.Select(entry => entry.DecisionKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void PasswordOverANonSecureChannel_IsNotSent_AndAbortsWithInsecureCommunication(bool? secure)
    {
        // Audit T1: IsSecureCommunication false or unreadable without allowInsecurePasswordTransfer: nothing is handed to SetPassword.
        var harness = new Harness(passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }, secure: secure);
        harness.Aborts(Phases.PreDownload, new Scripted(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", Kind: Kinds.Password));

        Assert.Empty(harness.Applied);
        var abort = harness.Result.Abort!;
        Assert.Equal(AbortReasons.InsecureCommunication, abort.Reason);
        Assert.Contains("allowInsecurePasswordTransfer", abort.Detail);
        Assert.DoesNotContain("PLC_PW", abort.Detail);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        Assert.Equal(Decisions.Aborted, seen.Decision);
        Assert.Equal(secure, seen.SecureCommunication);
        Assert.Contains("allowInsecurePasswordTransfer", AbortMessage(abort));
    }

    [Fact]
    public void PasswordOverANonSecureChannel_IsSent_WhenTheCallerAllowedIt()
    {
        var harness = new Harness(passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }, secure: false, allowInsecure: true);
        harness.Recorder.PreDownload(new Scripted(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", Kind: Kinds.Password));

        Assert.Equal(new (string?, string)[] { (Configurations + "ModuleWriteAccessPassword", "password:PLC_PW") }, harness.Applied);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        Assert.Equal(Decisions.PasswordSet, seen.Decision);
        Assert.False(seen.SecureCommunication);
        Assert.Null(harness.Result.Abort);
    }

    [Fact]
    public void SecureCommunication_IsReadByReflection_FromTheDocumentedProperty()
    {
        Assert.True(TiaMcpServer.Contracts.PlcOnline.PlcPasswordChannel.Read(new SecureFlag { IsSecureCommunication = true }));
        Assert.False(TiaMcpServer.Contracts.PlcOnline.PlcPasswordChannel.Read(new SecureFlag()));
        Assert.Null(TiaMcpServer.Contracts.PlcOnline.PlcPasswordChannel.Read(new object()));
        Assert.Null(TiaMcpServer.Contracts.PlcOnline.PlcPasswordChannel.TryRead(new ThrowingFlag(), _ => false));
        Assert.Throws<ObjectDisposedException>(() => TiaMcpServer.Contracts.PlcOnline.PlcPasswordChannel.TryRead(new ThrowingFlag(), e => e is ObjectDisposedException));
    }

    public sealed class SecureFlag
    {
        public bool IsSecureCommunication { get; set; }
    }

    public sealed class ThrowingFlag
    {
        public bool IsSecureCommunication => throw new ObjectDisposedException("configuration");
    }

    [Fact]
    public void UndecidedConfiguration_Aborts_WithItsKeyKindAndAvailableSelections()
    {
        var harness = new Harness();
        var undecided = new Scripted(Configurations + "DataBlockReinitialization", Configurations + "DataBlockReinitialization", "Reinitialize?",
            Names: new[] { "StopPlcAndReinitialize" });
        var exception = harness.Aborts(Phases.PreDownload, undecided);

        Assert.Equal(Phases.PreDownload, exception.Phase);
        Assert.Equal(undecided.Type, exception.Type);
        Assert.Empty(harness.Applied);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        AssertComplete(seen);
        Assert.Equal(Decisions.Aborted, seen.Decision);
        var abort = harness.Result.Abort!;
        Assert.Equal((AbortReasons.DecisionRequired, "DataBlockReinitialization", Kinds.Selection, "Reinitialize?"),
            (abort.Reason, abort.DecisionKey, abort.Kind, abort.Message));
        Assert.Equal(new[] { "StopPlcAndReinitialize" }, abort.AvailableSelections);
        Assert.Contains("decisions: { \"DataBlockReinitialization\": \"<selection>\" } using one of: StopPlcAndReinitialize", AbortMessage(abort));
    }

    [Fact]
    public void UndecidedConfigurationWithNoAction_IsAnsweredNoAction_AndRecordedAsTheAbortWithoutAThrow()
    {
        // Audit T2: the documented NoAction member is set instead of throwing from the callback (K27).
        var harness = new Harness();
        var undecided = new Scripted(Configurations + "DataBlockReinitialization", Configurations + "DataBlockReinitialization", "Reinitialize?",
            Names: new[] { "StopPlcAndReinitialize", "NoAction" });
        harness.Recorder.PreDownload(undecided);

        Assert.Equal(new (string?, string)[] { (undecided.Canonical, "NoAction") }, harness.Applied);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        AssertComplete(seen);
        Assert.Equal((Decisions.Selected, "NoAction", Sources.Policy), (seen.Decision, seen.Selection, seen.Source));
        var abort = harness.Result.Abort!;
        Assert.Equal((AbortReasons.DecisionRequired, "DataBlockReinitialization", Kinds.Selection, "Reinitialize?"),
            (abort.Reason, abort.DecisionKey, abort.Kind, abort.Message));
        Assert.Equal(new[] { "StopPlcAndReinitialize", "NoAction" }, abort.AvailableSelections);
        Assert.Contains("decisions: { \"DataBlockReinitialization\": \"<selection>\" } using one of: StopPlcAndReinitialize, NoAction", AbortMessage(abort));
    }

    [Fact]
    public void ConfigurationWithoutANamespaceType_AbortsAsUnsupported_EvenWithAMatchingDecision()
    {
        var harness = new Harness(decisions: new[] { Select("Proxy", "StopAll") });
        harness.Aborts(Phases.PreDownload, new Scripted("Vendor.Proxy", null));
        Assert.Empty(harness.Applied);
        Assert.Equal(AbortReasons.UnsupportedConfiguration, harness.Result.Abort!.Reason);
        Assert.Null(harness.Result.Abort.DecisionKey);
    }

    [Theory]
    [InlineData(PlcDownloadPolicy.Phases.PostDownload, ConfigurationTypes.StopModules)]
    [InlineData(PlcDownloadPolicy.Phases.PostDownload, ConfigurationTypes.OverwriteSystemData)]
    [InlineData(PlcDownloadPolicy.Phases.PreDownload, ConfigurationTypes.StartModules)]
    public void DefaultedTypeInTheWrongPhase_NeedsADecision(string phase, string type)
    {
        var harness = new Harness();
        harness.Aborts(phase, Defaulted(type) with { Names = new[] { "StopAll" } });
        Assert.Empty(harness.Applied);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        Assert.Equal(type, seen.CanonicalType);
        Assert.Equal(Decisions.Aborted, seen.Decision);
        Assert.Equal(AbortReasons.DecisionRequired, harness.Result.Abort!.Reason);

        // Audit T2: with a NoAction member the same question is answered NoAction and recorded as the abort, without a throw.
        var noAction = new Harness();
        noAction.Recorder.Handle(phase, Defaulted(type));
        Assert.Equal((type, "NoAction"), Assert.Single(noAction.Applied));
        Assert.Equal(AbortReasons.DecisionRequired, noAction.Result.Abort!.Reason);
        Assert.Equal(Sources.Policy, Assert.Single(noAction.Result.ConfigurationsSeen).Source);
    }

    [Fact]
    public void MalformedCanonicalTypesAndThrowingAccess_FailClosed()
    {
        var harness = new Harness(decisions: new[] { Check("ResetModule") });
        harness.Aborts(Phases.PreDownload, new Scripted("X.Y", "Other.Namespace.ResetModule"));
        Assert.Null(harness.Result.ConfigurationsSeen[0].CanonicalType);
        Assert.Equal(AbortReasons.UnsupportedConfiguration, harness.Result.Abort!.Reason);

        var throwing = new PlcDownloadResultInfo();
        var recorder = new PlcDownloadCallbackRecorder(new PlcDownloadDecisionSet(null, null, true, "PLCSIM"), throwing, new ThrowingAccess());
        Assert.Throws<PlcDownloadAbortedException>(() => recorder.PreDownload(new object()));
        Assert.Equal(AbortReasons.UnsupportedConfiguration, throwing.Abort!.Reason);
        Assert.Equal(Kinds.Other, throwing.Abort.Kind);
        Assert.Null(throwing.ConfigurationsSeen[0].Message);
    }

    private sealed class ThrowingAccess : IPlcDownloadConfigurationAccess
    {
        public string TypeName(object configuration) => "X.Z";
        public string? CanonicalType(object configuration) => throw new InvalidOperationException("type");
        public string? ReadMessage(object configuration) => null;
        public PlcDownloadConfigurationShape Describe(object configuration) => throw new InvalidOperationException("shape");
        public PlcDownloadApplyOutcome ApplySelection(object configuration, string selection) => throw new InvalidOperationException("must not apply");
        public PlcDownloadApplyOutcome ApplyCheck(object configuration) => throw new InvalidOperationException("must not apply");
        public PlcDownloadApplyOutcome ApplyPassword(object configuration, string variable) => throw new InvalidOperationException("must not apply");
    }

    [Theory]
    [InlineData(PlcDownloadPolicy.AbortReasons.SelectionUnavailable)]
    [InlineData(PlcDownloadPolicy.AbortReasons.DecisionNotApplicable)]
    [InlineData(PlcDownloadPolicy.AbortReasons.PasswordUnavailable)]
    public void AnUnappliedAnswer_AbortsWithItsReasonAndDetail(string reason)
    {
        var harness = new Harness();
        var outcome = reason switch
        {
            AbortReasons.SelectionUnavailable => PlcDownloadApplyOutcome.SelectionUnavailable("OverwriteSystemDataSelections has no member with that exact name"),
            AbortReasons.DecisionNotApplicable => PlcDownloadApplyOutcome.NotApplicable("it has no CurrentSelection"),
            _ => PlcDownloadApplyOutcome.PasswordUnavailable("Environment variable 'PW' is not set or is empty in the worker process."),
        };
        harness.Aborts(Phases.PreDownload, new Scripted(ConfigurationTypes.OverwriteSystemData, ConfigurationTypes.OverwriteSystemData, Outcome: outcome));

        // Audit T2: a selection configuration then also tries the documented NoAction, which this scripted object refuses too.
        Assert.Equal((ConfigurationTypes.OverwriteSystemData, "Overwrite"), harness.Applied[0]);
        Assert.Equal(reason == AbortReasons.PasswordUnavailable ? 1 : 2, harness.Applied.Count);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        AssertComplete(seen);
        Assert.Equal(Decisions.Aborted, seen.Decision);
        Assert.Equal(reason, harness.Result.Abort!.Reason);
        Assert.Equal(outcome.Detail, harness.Result.Abort.Detail);
        Assert.Null(harness.Recorder.DecisionFailure);
    }

    [Fact]
    public void ThrowingSetter_IsRecordedAsDecisionRejected_AndThrowsTheAbort()
    {
        var harness = new Harness();
        var setter = new InvalidOperationException("CurrentSelection refused");
        var exception = harness.Aborts(Phases.PreDownload,
            new Scripted(ConfigurationTypes.StopModules, ConfigurationTypes.StopModules, "Stop?", Failure: setter));

        Assert.Same(setter, exception.InnerException);
        Assert.Same(setter, harness.Recorder.DecisionFailure);
        var seen = Assert.Single(harness.Result.ConfigurationsSeen);
        AssertComplete(seen);
        Assert.Equal(Decisions.Aborted, seen.Decision);
        Assert.Equal(ConfigurationTypes.StopModules, seen.CanonicalType);
        Assert.Equal((Phases.PreDownload, ConfigurationTypes.StopModules, "Stop?", AbortReasons.DecisionRejected, (string?)null),
            (harness.Result.Abort!.Phase, harness.Result.Abort.Type, harness.Result.Abort.Message, harness.Result.Abort.Reason, harness.Result.Abort.Detail));
    }

    [Fact]
    public void FatalSetterFailure_IsRecorded_ThenRethrownUnchanged()
    {
        var harness = new Harness(isFatal: exception => exception is TimeoutException);
        var fatal = new TimeoutException("portal gone");
        var thrown = Assert.Throws<TimeoutException>(() => harness.Recorder.PreDownload(
            new Scripted(ConfigurationTypes.StopModules, ConfigurationTypes.StopModules, Failure: fatal)));

        Assert.Same(fatal, thrown);
        Assert.Equal(AbortReasons.DecisionRejected, harness.Result.Abort!.Reason);
        Assert.Equal(Decisions.Aborted, Assert.Single(harness.Result.ConfigurationsSeen).Decision);
    }

    [Fact]
    public void CallbacksAfterAnAbort_AreRecordedAsAborted_AndThrowAgain()
    {
        var harness = new Harness();
        harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.StopModules));
        harness.Aborts(Phases.PreDownload, new Scripted("X.Unknown", null));
        var first = harness.Result.Abort;

        harness.Aborts(Phases.PreDownload, Defaulted(ConfigurationTypes.ConsistentBlocksDownload) with { Names = new[] { "ConsistentDownload" } });
        // Audit T2: a later selection with a documented NoAction is answered NoAction (source policy) without a throw.
        harness.Recorder.PostDownload(Defaulted(ConfigurationTypes.StartModules));
        harness.Aborts(Phases.PreDownload, new Scripted(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", Kind: Kinds.Password));

        Assert.Same(first, harness.Result.Abort);
        Assert.Equal("X.Unknown", harness.Result.Abort!.Type);
        Assert.Equal(new[] { Decisions.Selected, Decisions.Aborted, Decisions.Aborted, Decisions.Selected, Decisions.Aborted },
            harness.Result.ConfigurationsSeen.Select(seen => seen.Decision));
        Assert.Equal(Sources.Policy, harness.Result.ConfigurationsSeen[3].Source);
        // Only NoAction is applied after the abort.
        Assert.Equal(new (string?, string)[] { (ConfigurationTypes.StopModules, "StopAll"), (ConfigurationTypes.StartModules, "NoAction") }, harness.Applied);
        Assert.All(harness.Result.ConfigurationsSeen, AssertComplete);
    }

    [Fact]
    public void MoreThanTheConfigurationLimit_Aborts_AndRecordingStaysBounded()
    {
        var harness = new Harness();
        for (var index = 0; index < MaxConfigurations; index++)
            harness.Recorder.PreDownload(Defaulted(ConfigurationTypes.StopModules));
        Assert.Null(harness.Result.Abort);

        harness.Aborts(Phases.PreDownload, Defaulted(ConfigurationTypes.StopModules));
        Assert.Equal(AbortReasons.ConfigurationLimitExceeded, harness.Result.Abort!.Reason);
        Assert.Equal(MaxConfigurations + 1, harness.Result.ConfigurationsSeen.Count);
        Assert.Equal(Decisions.Aborted, harness.Result.ConfigurationsSeen[^1].Decision);

        harness.Aborts(Phases.PostDownload, Defaulted(ConfigurationTypes.StartModules));
        Assert.Equal(MaxConfigurations + 1, harness.Result.ConfigurationsSeen.Count);
        Assert.Equal(MaxConfigurations, harness.Applied.Count);
        Assert.All(harness.Result.ConfigurationsSeen, AssertComplete);
    }

    [Fact]
    public void MessagesAndSelectionLists_AreBounded_AndAReadFailurePropagatesUnrecorded()
    {
        var harness = new Harness();
        harness.Recorder.PreDownload(new Scripted(ConfigurationTypes.StopModules, ConfigurationTypes.StopModules, new string('m', MaxTextChars + 50),
            Names: Enumerable.Range(0, MaxAvailableSelections + 5).Select(index => "Member" + index).Append(" ").ToArray()));
        Assert.Equal(MaxTextChars, harness.Result.ConfigurationsSeen[0].Message!.Length);
        Assert.Equal(MaxAvailableSelections, harness.Result.ConfigurationsSeen[0].AvailableSelections.Count);

        var result = new PlcDownloadResultInfo();
        var recorder = new PlcDownloadCallbackRecorder(new PlcDownloadDecisionSet(null, null, true, "PLCSIM"), result,
            new PlcDownloadReflectionAccess(_ => throw new ObjectDisposedException("configuration"), _ => null));
        Assert.Throws<ObjectDisposedException>(() => recorder.PreDownload(new StopModules()));
        Assert.Empty(result.ConfigurationsSeen);
        Assert.Null(result.Abort);
    }

    [Fact]
    public void RecorderEvidence_DecodesThroughTheHostContract_WithCallerDecisions()
    {
        var effects = Effects(Item(decisions: new[] { Select("StopModules", "NoAction"), Check("UpgradeTargetDevice") },
            passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }));
        var result = Result(effects);
        result.ConfigurationsSeen.Clear();
        var recorder = new PlcDownloadCallbackRecorder(
            new PlcDownloadDecisionSet(effects.Decisions, effects.Passwords, effects.StartAfterDownload, effects.Path.PcInterface.Name),
            result, new ScriptedAccess(), readSecureCommunication: _ => true);
        recorder.PreDownload(Defaulted(ConfigurationTypes.StopModules));
        recorder.PreDownload(new Scripted(Configurations + "UpgradeTargetDevice", Configurations + "UpgradeTargetDevice", Kind: Kinds.Check));
        recorder.PreDownload(new Scripted(Configurations + "ModuleWriteAccessPassword", Configurations + "ModuleWriteAccessPassword", Kind: Kinds.Password));
        recorder.PreDownload(Defaulted(ConfigurationTypes.AlarmTextLibrariesDownload));
        recorder.PostDownload(Defaulted(ConfigurationTypes.StartModules));

        var decoded = PlcDownloadPayloadContract.DecodeResult(WorkerJson.SerializePayload(result), effects, requireDownloadResult: true);
        Assert.Equal(5, decoded.ConfigurationsSeen.Count);

        result.State = null;
        result.ErrorCount = null;
        result.WarningCount = null;
        result.Messages.Clear();
        Assert.Throws<PlcDownloadAbortedException>(() => recorder.PostDownload(new Scripted("X.Unknown", null)));
        Assert.Equal(AbortReasons.UnsupportedConfiguration,
            PlcDownloadPayloadContract.DecodeResult(WorkerJson.SerializePayload(result), effects, requireDownloadResult: false).Abort!.Reason);
    }

    [Fact]
    public void ConfirmationLog_ClosesTheScopeBeforeCopying_RecordsTheAnswer_AndCountsBeyondTheList()
    {
        var log = new PlcDownloadPortalConfirmationLog();
        var events = new List<string>();
        var scope = new RecordingScope(() => events.Add($"disposed after {log.Observed}"));
        log.AttachScope(scope);
        log.Record("T", new string('x', MaxTextChars + 10), PortalConfirmationAnswers.No);
        log.Record(null, null, PortalConfirmationAnswers.Yes);
        log.Record("T", null, "maybe");

        var result = new PlcDownloadResultInfo();
        log.CloseInto(result);
        log.CloseInto(result); // Idempotent: the scope is disposed once.

        Assert.Equal(new[] { "disposed after 3" }, events);
        Assert.Equal(3, result.PortalConfirmationsObserved);
        Assert.True(result.PortalConfirmationsComplete);
        Assert.Equal(MaxTextChars, result.PortalConfirmations[0].Text!.Length);
        Assert.Equal("(unknown)", result.PortalConfirmations[1].Type);
        // Anything but an exact Yes is recorded as No.
        Assert.Equal(new[] { "No", "Yes", "No" }, result.PortalConfirmations.Select(entry => entry.Answer));

        for (var index = 0; index < MaxPortalConfirmations; index++)
            log.Record("T", "t", PortalConfirmationAnswers.No);
        log.CloseInto(result);
        Assert.Equal(MaxPortalConfirmations, result.PortalConfirmations.Count);
        Assert.Equal(MaxPortalConfirmations + 3, result.PortalConfirmationsObserved);
        Assert.False(result.PortalConfirmationsComplete);

        var empty = new PlcDownloadResultInfo();
        new PlcDownloadPortalConfirmationLog().CloseInto(empty);
        Assert.Equal((0, true, 0), (empty.PortalConfirmationsObserved, empty.PortalConfirmationsComplete, empty.PortalConfirmations.Count));
    }

    private sealed class RecordingScope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }

    [Fact]
    public void CanonicalType_IsTheFirstPublicTypeOfTheConfigurationsNamespace_Exactly()
    {
        Assert.Equal(ConfigurationTypes.StopModules, CanonicalConfigurationType(typeof(StopModules)));
        Assert.Equal(ConfigurationTypes.StartModules, CanonicalConfigurationType(typeof(StartModules)));
        // Implementation or proxy types outside the namespace are walked past to the public base.
        Assert.Equal(ConfigurationTypes.StopModules, CanonicalConfigurationType(typeof(InternalStopModules)));
        Assert.Equal(ConfigurationTypes.StartModules, CanonicalConfigurationType(typeof(ProxyStartModules)));
        // A public subclass in the namespace is its own type: an `is StopModules` check would have matched it.
        Assert.True(typeof(StopModulesVariant).IsSubclassOf(typeof(StopModules)));
        Assert.Equal(Configurations + "StopModulesVariant", CanonicalConfigurationType(typeof(StopModulesVariant)));
        Assert.Equal(Configurations + "StopModulesVariant", CanonicalConfigurationType(typeof(ProxyStopModulesVariant)));
        Assert.Equal(AbortReasons.DecisionRequired,
            new PlcDownloadDecisionSet(null, null, true, "PLCSIM").Resolve(Phases.PreDownload, CanonicalConfigurationType(typeof(StopModulesVariant))).AbortReason);
        // Types without a default are reported too (they can be decided); foreign types and null are not.
        Assert.Equal(Configurations + "DataBlockReinitialization", CanonicalConfigurationType(typeof(DataBlockReinitialization)));
        Assert.Equal(Configurations + "ModuleWriteAccessPassword", CanonicalConfigurationType(typeof(ModuleWriteAccessPassword)));
        Assert.Null(CanonicalConfigurationType(typeof(string)));
        Assert.Null(CanonicalConfigurationType((Type?)null));
    }

    [Fact]
    public void CanonicalType_ByNames_IgnoresNonPublicLevelsAndOtherNamespaces()
    {
        PlcDownloadTypeLevel Level(string ns, string name, bool isPublic = true) => new(ns, ns + "." + name, isPublic);
        Assert.Equal(ConfigurationTypes.TargetForSoftware, CanonicalConfigurationType(new[]
        {
            Level(ConfigurationsNamespace, "TargetForSoftwareImpl", isPublic: false),
            Level("Siemens.Engineering.Download.Internal", "Proxy"),
            Level(ConfigurationsNamespace, "TargetForSoftware"),
        }));
        Assert.Equal(Configurations + "TargetForSoftwareEx", CanonicalConfigurationType(new[]
        {
            Level(ConfigurationsNamespace, "TargetForSoftwareEx"),
            Level(ConfigurationsNamespace, "TargetForSoftware"),
        }));
        Assert.Null(CanonicalConfigurationType(new[] { Level("siemens.engineering.download.configurations", "StopModules") }));
        Assert.Null(CanonicalConfigurationType(new[] { Level(ConfigurationsNamespace, "Outer+Nested") }));
        Assert.Null(CanonicalConfigurationType(Array.Empty<PlcDownloadTypeLevel>()));
    }

    public static TheoryData<string, string> InvalidWorkerRequests => new()
    {
        { "plc", WorkerFailureCategories.ValidationError },
        { "device", WorkerFailureCategories.ValidationError },
        { "mode", WorkerFailureCategories.ValidationError },
        { "pc", WorkerFailureCategories.ValidationError },
        { "target", WorkerFailureCategories.ValidationError },
        { "scope", WorkerFailureCategories.ValidationError },
        { "scopeCase", WorkerFailureCategories.ValidationError },
        { "portalPolicy", WorkerFailureCategories.ValidationError },
        { "nullDecisions", WorkerFailureCategories.ValidationError },
        { "startModulesDecision", WorkerFailureCategories.ValidationError },
        { "passwordValue", WorkerFailureCategories.ValidationError },
    };

    private static PlcDownloadRequestInfo Request() => new()
    {
        PlcName = "PLC_1", DeviceName = "Station_1", Mode = "PN/IE", PcInterfaceName = "PLCSIM", PcInterfaceNumber = 1,
        TargetInterface = "1 X1", Scope = Scopes.Software, StartAfterDownload = true,
    };

    [Theory]
    [MemberData(nameof(InvalidWorkerRequests))]
    public void WorkerRequestValidation_RefusesBlanksUnknownScopesAndMalformedDecisions(string violation, string category)
    {
        var request = Request();
        switch (violation)
        {
            case "plc": request.PlcName = " "; break;
            case "device": request.DeviceName = ""; break;
            case "mode": request.Mode = null!; break;
            case "pc": request.PcInterfaceName = "\t"; break;
            case "target": request.TargetInterface = ""; break;
            case "scope": request.Scope = "everything"; break;
            case "scopeCase": request.Scope = "Software"; break;
            case "portalPolicy": request.PortalConfirmations = "Yes"; break;
            case "nullDecisions": request.Decisions = null!; break;
            case "startModulesDecision": request.Decisions.Add(Select("StartModules", "StartModule")); break;
            case "passwordValue": request.Passwords.Add(Password("ModuleWriteAccessPassword", "my password")); break;
        }

        var validation = ValidateWorkerRequest(request);
        Assert.False(validation.IsValid);
        Assert.Equal(category, validation.FailureCategory);
        Assert.Null(validation.DownloadOptions);
        Assert.False(string.IsNullOrWhiteSpace(validation.Message));
        Assert.DoesNotContain("my password", validation.Message);
    }

    [Theory]
    [InlineData("PLCSIM")]
    [InlineData(Hardware)]
    [InlineData("plcsim")]
    public void WorkerRequestValidation_AcceptsEveryPcInterface_AndRefusesNull(string pc)
    {
        var request = Request();
        request.PcInterfaceName = pc;
        request.Decisions.Add(Select("ResetModule", "DeleteAll"));
        request.Passwords.Add(Password("ModuleWriteAccessPassword", "PLC_PW"));
        request.PortalConfirmations = PortalConfirmationPolicies.Accept;
        var validation = ValidateWorkerRequest(request);
        Assert.True(validation.IsValid, validation.Message);
        Assert.Equal("Software", validation.DownloadOptions);
        Assert.Equal(WorkerFailureCategories.ValidationError, ValidateWorkerRequest(null).FailureCategory);
    }

    [Theory]
    [InlineData(false, "PLCSIM", new[] { "PLCSIM" }, false)]
    [InlineData(false, "PLCSIM", new[] { "PLCSIM", "Intel(R) Ethernet Connection" }, false)]
    [InlineData(false, "PLCSIM", new[] { "PLCSIM", "PLCSIM" }, true)]
    [InlineData(false, "PLCSIM", new[] { "PLCSIM", "plcsim", "PLCSIM " }, false)]
    [InlineData(true, "PLCSIM", new[] { "PLCSIM", "PLCSIM" }, false)]
    [InlineData(false, "PLCSIM", new string[0], false)]
    // The rule is not PLCSIM-specific: any equally named interfaces are ambiguous without a number.
    [InlineData(false, "Intel(R) Ethernet Connection", new[] { "PLCSIM", "Intel(R) Ethernet Connection", "Intel(R) Ethernet Connection" }, true)]
    [InlineData(true, "Intel(R) Ethernet Connection", new[] { "Intel(R) Ethernet Connection", "Intel(R) Ethernet Connection" }, false)]
    [InlineData(false, "PLCSIM", new[] { "PLCSIM", "Intel(R) Ethernet Connection", "Intel(R) Ethernet Connection" }, false)]
    public void ImplicitNumber_IsAmbiguousOnlyWithSeveralInterfacesOfTheRequestedName(bool numberExplicit, string requested, string[] names, bool ambiguous)
        => Assert.Equal(ambiguous, IsImplicitInterfaceNumberAmbiguous(names, requested, numberExplicit));
}
