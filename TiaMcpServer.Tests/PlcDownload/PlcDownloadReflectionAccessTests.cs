using System.Security;
using Siemens.Engineering.Download.Configurations;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.PlcDownload;
using Xunit;
using static TiaMcpServer.Contracts.Plc.PlcDownloadPolicy;
using static TiaMcpServer.Tests.PlcDownload.PlcDownloadTestData;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// The production, Siemens-free <see cref="PlcDownloadReflectionAccess"/> the worker and FakeWorker
/// use to answer live configurations generically (CurrentSelection, Checked, SetPassword), alone and
/// behind the shared recorder, against executable doubles in the configurations namespace.
/// </summary>
public sealed class PlcDownloadReflectionAccessTests
{
    private const string Secret = "S3cr3t-pa55word!";

    private static PlcDownloadReflectionAccess Access(IReadOnlyDictionary<string, string?>? environment = null)
        => new(_ => "message", name => environment is not null && environment.TryGetValue(name, out var value) ? value : null);

    // Shapes the fail-closed member lookup must refuse.
    public enum OtherSelections { One, Two }

    public sealed class ReadOnlySelection
    {
        public StopModulesSelections CurrentSelection { get; } = StopModulesSelections.StopAll;
    }

    public sealed class StringSelection
    {
        public string CurrentSelection { get; set; } = "";
    }

    public sealed class HiddenSelection : StopModules
    {
        public new OtherSelections CurrentSelection { get; set; }
    }

    public sealed class ThrowingSelection
    {
        public StopModulesSelections CurrentSelection
        {
            get => StopModulesSelections.NoAction;
            set => throw new InvalidOperationException("refused by TIA Portal");
        }
    }

    /// <summary>Only the exact SetPassword(SecureString) overload is ever called.</summary>
    public sealed class OverloadedPasswordSetters
    {
        public string Called { get; private set; } = "";

        public void SetPassword(SecureString password) => Called = "secure";

        public void SetPassword(SecureString password, bool remember) => Called = "secure+bool";

        public void SetPassword(string password) => Called = "string";
    }

    public sealed class StringPasswordSetter
    {
        public void SetPassword(string password) { }
    }

    public sealed class IntChecked
    {
        public int Checked { get; set; }
    }

    [Fact]
    public void Describe_ReportsKindAndLiveEnumMemberNames()
    {
        var access = Access();
        var reset = access.Describe(new ResetModule());
        Assert.Equal(Kinds.Selection, reset.Kind);
        Assert.Equal(new[] { "DeleteAll", "NoAction" }, reset.AvailableSelections);
        Assert.Equal(Kinds.Check, access.Describe(new UpgradeTargetDevice()).Kind);
        Assert.Equal(Kinds.Password, access.Describe(new ModuleWriteAccessPassword()).Kind);
        Assert.Equal(Kinds.Other, access.Describe(new DeleteWebApplication()).Kind);
        Assert.Equal(Kinds.Other, access.Describe(new ReadOnlySelection()).Kind);
        Assert.Equal(Kinds.Other, access.Describe(new StringSelection()).Kind);
        Assert.Equal(Kinds.Other, access.Describe(new HiddenSelection()).Kind);
        Assert.Equal(Kinds.Other, access.Describe(new IntChecked()).Kind);
        Assert.Empty(access.Describe(new UpgradeTargetDevice()).AvailableSelections);
        Assert.Equal(Configurations + "ResetModule", access.CanonicalType(new ResetModule()));
        Assert.Equal(typeof(ResetModule).FullName, access.TypeName(new ResetModule()));
    }

    [Fact]
    public void ApplySelection_SetsTheExactMemberOfTheLivePropertysEnum()
    {
        var reset = new ResetModule();
        Assert.True(Access().ApplySelection(reset, "DeleteAll").Applied);
        Assert.Equal(ResetModuleSelections.DeleteAll, reset.CurrentSelection);

        var target = new TargetForSoftware();
        Assert.True(Access().ApplySelection(target, "PlcSimulationAdvanced").Applied);
        Assert.Equal(TargetForSoftwareSelections.PlcSimulationAdvanced, target.CurrentSelection);
    }

    [Theory]
    [InlineData("deleteAll")]
    [InlineData("DeleteAll ")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("DeleteAll, NoAction")]
    [InlineData("StopAll")]
    [InlineData("")]
    public void ApplySelection_RefusesAnythingButAnExactMemberName(string selection)
    {
        var reset = new ResetModule();
        var outcome = Access().ApplySelection(reset, selection);
        Assert.False(outcome.Applied);
        Assert.Equal(AbortReasons.SelectionUnavailable, outcome.AbortReason);
        Assert.Contains("ResetModuleSelections", outcome.Detail);
        if (selection.Length > 0) Assert.DoesNotContain(selection, outcome.Detail!.Replace("ResetModuleSelections", ""));
        Assert.Equal(ResetModuleSelections.NoAction, reset.CurrentSelection);
    }

    [Fact]
    public void MissingOrAmbiguousMembers_FailClosed()
    {
        var access = Access(new Dictionary<string, string?> { ["PW"] = Secret });
        foreach (var configuration in new object[] { new DeleteWebApplication(), new ReadOnlySelection(), new StringSelection(), new HiddenSelection(), new UpgradeTargetDevice() })
            Assert.Equal(AbortReasons.DecisionNotApplicable, access.ApplySelection(configuration, "StopAll").AbortReason);
        foreach (var configuration in new object[] { new ResetModule(), new IntChecked(), new DeleteWebApplication() })
            Assert.Equal(AbortReasons.DecisionNotApplicable, access.ApplyCheck(configuration).AbortReason);
        foreach (var configuration in new object[] { new ResetModule(), new UpgradeTargetDevice(), new StringPasswordSetter() })
            Assert.Equal(AbortReasons.DecisionNotApplicable, access.ApplyPassword(configuration, "PW").AbortReason);
        var overloaded = new OverloadedPasswordSetters();
        Assert.Equal(Kinds.Password, access.Describe(overloaded).Kind);
        Assert.True(access.ApplyPassword(overloaded, "PW").Applied);
        Assert.Equal("secure", overloaded.Called);
        Assert.Contains("CurrentSelection", access.ApplySelection(new DeleteWebApplication(), "X").Detail);
        Assert.Contains("Checked", access.ApplyCheck(new ResetModule()).Detail);
        Assert.Contains("SetPassword(SecureString)", access.ApplyPassword(new ResetModule(), "PW").Detail);
    }

    [Fact]
    public void ApplyCheck_SetsCheckedTrue()
    {
        var upgrade = new UpgradeTargetDevice();
        Assert.True(Access().ApplyCheck(upgrade).Applied);
        Assert.True(upgrade.Checked);
    }

    [Fact]
    public void ApplyPassword_ReadsTheNamedVariable_AndPassesItAsSecureString()
    {
        var password = new ModuleWriteAccessPassword();
        Assert.True(Access(new Dictionary<string, string?> { ["PLC_PW"] = Secret }).ApplyPassword(password, "PLC_PW").Applied);
        Assert.Equal(Secret, password.ReceivedForTest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ApplyPassword_MissingOrEmptyVariable_NamesNeitherTheVariableNorTheValue(string? value)
    {
        var password = new ModuleWriteAccessPassword();
        var outcome = Access(new Dictionary<string, string?> { ["PLC_PW"] = value, ["OTHER"] = Secret }).ApplyPassword(password, "PLC_PW");
        Assert.Equal(AbortReasons.PasswordUnavailable, outcome.AbortReason);
        // An unset "name" may be a pasted password, so the detail never repeats it.
        Assert.DoesNotContain("PLC_PW", outcome.Detail);
        Assert.Contains("not set or is empty", outcome.Detail);
        Assert.DoesNotContain(Secret, outcome.Detail);
        Assert.Null(password.ReceivedForTest);
        Assert.Equal(AbortReasons.PasswordUnavailable, Access().ApplyPassword(password, "not a name").AbortReason);
    }

    [Fact]
    public void ThrowingSetter_PropagatesTheOriginalException()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => Access().ApplySelection(new ThrowingSelection(), "StopAll"));
        Assert.Equal("refused by TIA Portal", thrown.Message);
    }

    private static (PlcDownloadResultInfo Result, PlcDownloadCallbackRecorder Recorder) Recorder(PlcDownloadEffects effects,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var result = Result(effects);
        result.ConfigurationsSeen.Clear();
        return (result, new PlcDownloadCallbackRecorder(
            new PlcDownloadDecisionSet(effects.Decisions, effects.Passwords, effects.StartAfterDownload, effects.Path.PcInterface.Name),
            result, Access(environment)));
    }

    [Fact]
    public void BehindTheRecorder_CallerDecisionsOverrideDefaults_AndTheEvidenceDecodes()
    {
        var effects = Effects(Item(pc: Hardware, target: "1 X1", numberExplicit: true,
            decisions: new[] { Select("StopModules", "NoAction"), Select("ResetModule", "DeleteAll"), Check("UpgradeTargetDevice") }));
        var (result, recorder) = Recorder(effects);
        var stop = new StopModules();
        var target = new TargetForSoftware { CurrentSelection = TargetForSoftwareSelections.PlcSimulationAdvanced };
        var reset = new ResetModule();
        var upgrade = new UpgradeTargetDevice();

        recorder.PreDownload(stop);
        recorder.PreDownload(target);
        recorder.PreDownload(reset);
        recorder.PreDownload(upgrade);
        recorder.PostDownload(new StartModules());

        Assert.Equal(StopModulesSelections.NoAction, stop.CurrentSelection);
        Assert.Equal(TargetForSoftwareSelections.CPU, target.CurrentSelection); // Hardware: the built-in default is the CPU.
        Assert.Equal(ResetModuleSelections.DeleteAll, reset.CurrentSelection);
        Assert.True(upgrade.Checked);
        Assert.Equal(new[] { Sources.Caller, Sources.Default, Sources.Caller, Sources.Caller, Sources.Default },
            result.ConfigurationsSeen.Select(seen => seen.Source));
        Assert.Equal(new[] { "CPU", "PlcSimulationAdvanced" }, result.ConfigurationsSeen[1].AvailableSelections);
        var decoded = PlcDownloadPayloadContract.DecodeResult(WorkerJson.SerializePayload(result), effects, requireDownloadResult: true);
        Assert.Equal(5, decoded.ConfigurationsSeen.Count);
    }

    [Fact]
    public void BehindTheRecorder_AnInvalidSelectionAborts_ListingTheValidNames()
    {
        var effects = Effects(Item(decisions: new[] { Select("ResetModule", "KeepActualValues") }));
        var (result, recorder) = Recorder(effects);
        var reset = new ResetModule { CurrentSelection = ResetModuleSelections.DeleteAll };

        // Audit T2: the documented NoAction is set instead of a throw; the abort is recorded.
        recorder.PreDownload(reset);

        Assert.Equal(ResetModuleSelections.NoAction, reset.CurrentSelection);
        Assert.Equal(("NoAction", Sources.Policy), (result.ConfigurationsSeen[^1].Selection, result.ConfigurationsSeen[^1].Source));
        var abort = result.Abort!;
        Assert.Equal((AbortReasons.SelectionUnavailable, "ResetModule", Kinds.Selection), (abort.Reason, abort.DecisionKey, abort.Kind));
        Assert.Equal(new[] { "DeleteAll", "NoAction" }, abort.AvailableSelections);
        Assert.Contains("Available: DeleteAll, NoAction", AbortMessage(abort));
        result.State = null;
        result.ErrorCount = null;
        result.WarningCount = null;
        result.Messages.Clear();
        Assert.Equal(AbortReasons.SelectionUnavailable,
            PlcDownloadPayloadContract.DecodeResult(WorkerJson.SerializePayload(result), effects, requireDownloadResult: false).Abort!.Reason);
    }

    [Fact]
    public void BehindTheRecorder_APasswordIsSupplied_ButNeverRecorded()
    {
        var effects = Effects(Item(passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }));
        var (result, recorder) = Recorder(effects, new Dictionary<string, string?> { ["PLC_PW"] = Secret });
        var password = new ModuleWriteAccessPassword();

        recorder.PreDownload(password);
        recorder.PostDownload(new StartModules());

        Assert.Equal(Secret, password.ReceivedForTest);
        var seen = result.ConfigurationsSeen[0];
        Assert.Equal((Decisions.PasswordSet, "PLC_PW", Sources.Caller, Kinds.Password), (seen.Decision, seen.PasswordVariable, seen.Source, seen.Kind));
        var payload = WorkerJson.SerializePayload(result);
        Assert.DoesNotContain(Secret, payload);
        Assert.Contains("PLC_PW", payload);
        var decoded = PlcDownloadPayloadContract.DecodeResult(payload, effects, requireDownloadResult: true);
        Assert.DoesNotContain(Secret, TiaMcpServer.Json.CanonicalJson.Serialize(decoded));
    }

    [Fact]
    public void BehindTheRecorder_AMissingPasswordVariableAborts_WithoutRepeatingItsName()
    {
        var effects = Effects(Item(passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") }));
        var (result, recorder) = Recorder(effects, new Dictionary<string, string?> { ["SOMETHING_ELSE"] = Secret });

        Assert.Throws<PlcDownloadAbortedException>(() => recorder.PreDownload(new ModuleWriteAccessPassword()));

        var abort = result.Abort!;
        Assert.Equal((AbortReasons.PasswordUnavailable, Kinds.Password), (abort.Reason, abort.Kind));
        Assert.DoesNotContain("PLC_PW", abort.Detail);
        Assert.DoesNotContain("PLC_PW", AbortMessage(abort));
        Assert.DoesNotContain("PLC_PW", WorkerJson.SerializePayload(result));
        Assert.DoesNotContain(Secret, WorkerJson.SerializePayload(result));
    }

    [Fact]
    public void BehindTheRecorder_APasswordConfigurationWithoutAnEntry_AsksForOne()
    {
        var (result, recorder) = Recorder(Effects());
        Assert.Throws<PlcDownloadAbortedException>(() => recorder.PreDownload(new ModuleWriteAccessPassword()));
        Assert.Equal((AbortReasons.DecisionRequired, Kinds.Password, "ModuleWriteAccessPassword"),
            (result.Abort!.Reason, result.Abort.Kind, result.Abort.DecisionKey));
        Assert.Contains("passwords: { \"ModuleWriteAccessPassword\":", AbortMessage(result.Abort));
    }

    [Fact]
    public void BehindTheRecorder_ADecisionOfTheWrongKindIsNotApplicable()
    {
        var effects = Effects(Item(decisions: new[] { Check("ResetModule"), Select("UpgradeTargetDevice", "Yes") }));
        var (result, recorder) = Recorder(effects);
        recorder.PreDownload(new ResetModule()); // Audit T2: answered NoAction, no throw.
        Assert.Equal(AbortReasons.DecisionNotApplicable, result.Abort!.Reason);
        Assert.Contains("Checked", result.Abort.Detail);
    }
}
