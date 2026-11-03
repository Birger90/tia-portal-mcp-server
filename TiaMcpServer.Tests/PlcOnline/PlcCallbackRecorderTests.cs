using Siemens.Engineering.Upload.Configurations;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Tests.PlcOnline.CallbackProxies;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// The Siemens-free fail-closed decision loop shared by the worker and the FakeWorker for station upload
/// and online legitimation: only caller entries answer, by exact canonical type short name; passwords come
/// from environment variable NAMES; everything else aborts with the live enum member names.
/// </summary>
public sealed class PlcCallbackRecorderTests
{
    private static readonly Dictionary<string, string?> Environment = new(StringComparer.Ordinal) { ["PLC_SECRET"] = "s3cret", ["EMPTY"] = "" };

    private static PlcCallbackRecorder Recorder(IEnumerable<(string Type, string Selection)>? decisions = null,
        IEnumerable<(string Type, string Variable)>? passwords = null, Func<Exception, bool>? isFatal = null)
        => new(decisions?.Select(d => new PlcCallbackDecisionInfo { Type = d.Type, Selection = d.Selection }).ToArray(),
            passwords?.Select(p => new PlcCallbackPasswordInfo { Type = p.Type, Variable = p.Variable }).ToArray(),
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.UploadConfigurationsNamespace,
                configuration => configuration is UploadMissingProducts ? "Products are missing." : null,
                variable => Environment.GetValueOrDefault(variable)),
            isFatal);

    [Fact]
    public void Selection_IsAnsweredOnlyByTheCallersDecision()
    {
        var recorder = Recorder(new[] { ("UploadMissingProducts", "TryUpload") });
        var configuration = new UploadMissingProducts();

        recorder.Handle(configuration);

        Assert.Equal(UploadMissingProductsSelections.TryUpload, configuration.CurrentSelection);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal(PlcCallbackPolicy.Decisions.Selected, seen.Decision);
        Assert.Equal("TryUpload", seen.Selection);
        Assert.Equal("Siemens.Engineering.Upload.Configurations.UploadMissingProducts", seen.CanonicalType);
        Assert.Equal("UploadMissingProducts", seen.DecisionKey);
        Assert.Equal(PlcCallbackPolicy.Kinds.Selection, seen.Kind);
        Assert.Equal(new[] { "NoAction", "TryUpload" }, seen.AvailableSelections);
        Assert.Equal("Products are missing.", seen.Message);
        Assert.Null(recorder.Abort);
        Assert.Empty(recorder.UnusedKeys());
    }

    [Fact]
    public void MissingDecision_AbortsAndListsTheAvailableSelections()
    {
        var recorder = Recorder();
        var configuration = new UploadMissingProducts { CurrentSelection = UploadMissingProductsSelections.TryUpload };

        var thrown = Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(configuration));

        Assert.Equal(UploadMissingProductsSelections.TryUpload, configuration.CurrentSelection);
        var abort = recorder.Abort!;
        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionMissing, abort.Reason);
        Assert.Equal("UploadMissingProducts", abort.DecisionKey);
        Assert.Equal(new[] { "NoAction", "TryUpload" }, abort.AvailableSelections);
        Assert.Contains("NoAction, TryUpload", thrown.Message);
        Assert.Equal(PlcCallbackPolicy.Decisions.Aborted, Assert.Single(recorder.Seen).Decision);
    }

    [Theory]
    [InlineData("noaction")]
    [InlineData("1")]
    [InlineData("Stop")]
    public void SelectionThatIsNotAnExactMember_IsUnavailable(string selection)
    {
        var recorder = Recorder(new[] { ("UploadMissingProducts", selection) });
        var configuration = new UploadMissingProducts { CurrentSelection = UploadMissingProductsSelections.TryUpload };

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(configuration));

        Assert.Equal(PlcCallbackPolicy.AbortReasons.SelectionUnavailable, recorder.Abort!.Reason);
        Assert.Equal(UploadMissingProductsSelections.TryUpload, configuration.CurrentSelection);
    }

    [Fact]
    public void Password_IsReadFromTheNamedVariableAndNeverRecorded()
    {
        var recorder = Recorder(passwords: new[] { ("ModuleReadAccessPassword", "PLC_SECRET") });
        var configuration = new ModuleReadAccessPassword();

        recorder.Handle(configuration);

        Assert.Equal(6, configuration.PasswordLength);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal(PlcCallbackPolicy.Decisions.PasswordSet, seen.Decision);
        Assert.Equal(PlcCallbackPolicy.Kinds.Password, seen.Kind);
        Assert.Equal("PLC_SECRET", seen.PasswordVariable);
        Assert.DoesNotContain("s3cret", System.Text.Json.JsonSerializer.Serialize(recorder.Seen));
    }

    [Theory]
    [InlineData("UNSET")]
    [InlineData("EMPTY")]
    public void UnsetOrEmptyVariable_IsPasswordUnavailable(string variable)
    {
        var recorder = Recorder(passwords: new[] { ("ModuleReadAccessPassword", variable) });

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(new ModuleReadAccessPassword()));

        Assert.Equal(PlcCallbackPolicy.AbortReasons.PasswordUnavailable, recorder.Abort!.Reason);
        Assert.DoesNotContain(variable, recorder.Abort.Detail);
    }

    [Fact]
    public void AnswerOfTheWrongKind_IsNotApplicable()
    {
        var selectionForPassword = Recorder(new[] { ("ModuleReadAccessPassword", "NoAction") });
        Assert.Throws<PlcCallbackAbortedException>(() => selectionForPassword.Handle(new ModuleReadAccessPassword()));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, selectionForPassword.Abort!.Reason);

        var passwordForSelection = Recorder(passwords: new[] { ("UploadMissingProducts", "PLC_SECRET") });
        Assert.Throws<PlcCallbackAbortedException>(() => passwordForSelection.Handle(new UploadMissingProducts()));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, passwordForSelection.Abort!.Reason);
    }

    [Fact]
    public void UnanswerableAndForeignConfigurations_AreUnsupported()
    {
        var notice = Recorder();
        Assert.Throws<PlcCallbackAbortedException>(() => notice.Handle(new UploadNotice()));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.UnsupportedConfiguration, notice.Abort!.Reason);
        Assert.Equal(PlcCallbackPolicy.Kinds.Other, notice.Abort.Kind);

        var foreign = Recorder(new[] { ("ForeignConfiguration", "NoAction") });
        Assert.Throws<PlcCallbackAbortedException>(() => foreign.Handle(new ForeignConfiguration()));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.UnsupportedConfiguration, foreign.Abort!.Reason);
        Assert.Null(foreign.Abort.DecisionKey);
    }

    [Fact]
    public void ImplementationSubclass_IsAnsweredAsItsPublicType()
    {
        var recorder = Recorder(new[] { ("UploadMissingProducts", "NoAction") });
        var configuration = new InternalUploadMissingProducts { CurrentSelection = UploadMissingProductsSelections.TryUpload };

        recorder.Handle(configuration);

        Assert.Equal(UploadMissingProductsSelections.NoAction, configuration.CurrentSelection);
        var seen = Assert.Single(recorder.Seen);
        Assert.Equal("Siemens.Engineering.Upload.Configurations.UploadMissingProducts", seen.CanonicalType);
        Assert.EndsWith("InternalUploadMissingProducts", seen.Type);
    }

    [Fact]
    public void AfterAnAbort_EveryLaterConfigurationIsRecordedAndAborts()
    {
        var recorder = Recorder(new[] { ("UploadMissingProducts", "NoAction") });
        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(new UploadNotice()));
        var later = new UploadMissingProducts { CurrentSelection = UploadMissingProductsSelections.TryUpload };

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(later));

        Assert.Equal(UploadMissingProductsSelections.TryUpload, later.CurrentSelection);
        Assert.Equal(2, recorder.Seen.Count);
        Assert.All(recorder.Seen, seen => Assert.Equal(PlcCallbackPolicy.Decisions.Aborted, seen.Decision));
        Assert.EndsWith("UploadNotice", recorder.Abort!.Type);
        Assert.Equal(new[] { "UploadMissingProducts" }, recorder.UnusedKeys());
    }

    [Fact]
    public void ThrowingSetter_IsRecordedAsRejectedAndRethrownUnchanged()
    {
        var recorder = Recorder(new[] { ("RejectingSelection", "NoAction") });

        var thrown = Assert.Throws<InvalidOperationException>(() => recorder.Handle(new RejectingSelection()));

        Assert.Equal("TIA Portal rejected the selection.", thrown.Message);
        Assert.Equal(PlcCallbackPolicy.AbortReasons.AnswerRejected, recorder.Abort!.Reason);
        Assert.Same(thrown, recorder.AnswerFailure);
    }

    [Fact]
    public void MoreThanTheConfigurationLimit_Aborts()
    {
        var decisions = new[] { ("UploadMissingProducts", "NoAction") };
        var recorder = Recorder(decisions);
        for (var i = 0; i < PlcCallbackPolicy.MaxConfigurations; i++) recorder.Handle(new UploadMissingProducts());

        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(new UploadMissingProducts()));

        Assert.Equal(PlcCallbackPolicy.AbortReasons.ConfigurationLimitExceeded, recorder.Abort!.Reason);
        Assert.Equal(PlcCallbackPolicy.MaxConfigurations + 1, recorder.Seen.Count);
    }

    [Theory]
    [InlineData("Upload Missing", "NoAction", null, null, "configuration type short name")]
    [InlineData("UploadMissingProducts", "No Action", null, null, "enum member name")]
    [InlineData(null, null, "ModuleReadAccessPassword", "my password!", "environment variable")]
    [InlineData("ModuleReadAccessPassword", "NoAction", "ModuleReadAccessPassword", "PLC_SECRET", "more than once")]
    public void ValidateEntries_RejectsMalformedEntries(string? decisionType, string? selection, string? passwordType, string? variable, string expected)
    {
        var decisions = decisionType is null ? null : new[] { new PlcCallbackDecisionInfo { Type = decisionType, Selection = selection! } };
        var passwords = passwordType is null ? null : new[] { new PlcCallbackPasswordInfo { Type = passwordType, Variable = variable! } };

        Assert.Contains(expected, PlcCallbackPolicy.ValidateEntries(decisions, passwords));
        Assert.Null(PlcCallbackPolicy.ValidateEntries(new[] { new PlcCallbackDecisionInfo { Type = "UploadMissingProducts", Selection = "NoAction" } },
            new[] { new PlcCallbackPasswordInfo { Type = "ModuleReadAccessPassword", Variable = "PLC_SECRET" } }));
    }

    [Fact]
    public void CanonicalType_IsTheFirstPublicTypeOfTheNamespace()
    {
        Assert.Equal("Siemens.Engineering.Upload.Configurations.UploadMissingProducts",
            PlcCallbackPolicy.CanonicalType(typeof(InternalUploadMissingProducts), PlcCallbackPolicy.UploadConfigurationsNamespace));
        Assert.Null(PlcCallbackPolicy.CanonicalType(typeof(ForeignConfiguration), PlcCallbackPolicy.UploadConfigurationsNamespace));
        Assert.Null(PlcCallbackPolicy.CanonicalType(typeof(UploadMissingProducts), PlcCallbackPolicy.OnlineConfigurationsNamespace));
        Assert.Equal("UploadMissingProducts", PlcCallbackPolicy.DecisionKey("Siemens.Engineering.Upload.Configurations.UploadMissingProducts"));
    }
    [Fact]
    public async Task ConcurrentCallbacks_AreAllRecordedUnderOneLock()
    {
        var recorder = Recorder(new[] { ("UploadMissingProducts", "NoAction") });
        const int Threads = 8;
        const int PerThread = 10;
        using var start = new ManualResetEventSlim();
        var workers = Enumerable.Range(0, Threads).Select(_ => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < PerThread; i++) recorder.Handle(new UploadMissingProducts());
        })).ToArray();
        start.Set();
        await Task.WhenAll(workers);

        var seen = recorder.Seen;
        Assert.Equal(Threads * PerThread, seen.Count);
        Assert.All(seen, entry => Assert.Equal(PlcCallbackPolicy.Decisions.Selected, entry.Decision));
        Assert.Null(recorder.Abort);
        Assert.NotSame(recorder.Seen, recorder.Seen);
    }

    [Fact]
    public void UnsetPasswordVariable_IsNotRepeatedInTheAbort()
    {
        var recorder = Recorder(passwords: new[] { ("ModuleReadAccessPassword", "MyPastedPassw0rd") });
        Assert.Throws<PlcCallbackAbortedException>(() => recorder.Handle(new ModuleReadAccessPassword()));
        Assert.Equal(PlcCallbackPolicy.AbortReasons.PasswordUnavailable, recorder.Abort!.Reason);
        Assert.DoesNotContain("MyPastedPassw0rd", System.Text.Json.JsonSerializer.Serialize(recorder.Abort));
        Assert.DoesNotContain("MyPastedPassw0rd", System.Text.Json.JsonSerializer.Serialize(recorder.Seen));
    }
}
