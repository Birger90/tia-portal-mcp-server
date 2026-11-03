using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using PortalProject = Siemens.Engineering.Project;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// The session-wide TIA Portal confirmation policy and the plc_download scope that answers every
/// confirmation by the call's policy (No by default, Yes for accept in writable modes) while it is
/// open, against the executable Portal boundary doubles.
/// </summary>
[Collection("Portal session boundary")]
public sealed class TiaPortalSessionConfirmationTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TiaPortalProcess[] _previous = TiaPortal.Processes.ToArray();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "confirmation-session-" + Guid.NewGuid().ToString("N"));

        public Fixture(bool allowConfirmations)
        {
            Directory.CreateDirectory(_directory);
            TiaPortal.Processes.Clear();
            var path = Path.Combine(_directory, "A.ap21");
            File.WriteAllText(path, "offline fixture");
            var process = new TiaPortalProcess { Id = 101, ProjectPath = new FileInfo(path) };
            process.Portal = new TiaPortal { Process = process };
            process.Portal.Projects.Items.Add(new PortalProject { Path = new FileInfo(path), Calls = process.Portal.Projects.Calls });
            TiaPortal.Processes.Add(process);
            Portal = process.Portal;
            Session = new Session(allowConfirmations);
            Session.EnsurePortalConnected(101);
        }

        public Session Session { get; }

        public TiaPortal Portal { get; }

        public void Dispose()
        {
            try { Session.Dispose(); }
            finally
            {
                TiaPortal.Processes.Clear();
                TiaPortal.Processes.AddRange(_previous);
                Directory.Delete(_directory, true);
            }
        }
    }

    private static bool WritableMode()
        => WorkerOperationAuthorization.AllowsTiaConfirmations(WorkerOperationAuthorization.ParseAccessMode(Environment.GetCommandLineArgs()));

    [Fact]
    public void SessionWidePolicy_IsUnchangedOutsideTheScope()
    {
        using (var allowed = new Fixture(allowConfirmations: true))
            Assert.Equal(WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, allowed.Portal.RaiseConfirmation());
        using var refused = new Fixture(allowConfirmations: false);
        Assert.Equal(ConfirmationResult.No, refused.Portal.RaiseConfirmation());
    }

    [Fact]
    public void UnscopedConfirmations_AreReportedToTheNextRequest_WithTheirText_ThenCleared()
    {
        using var f = new Fixture(allowConfirmations: false);
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation("Möchten Sie die Änderungen\r\n speichern?"));
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());

        var line = Assert.Single(f.Session.EndLingeringConfirmations());
        Assert.StartsWith("2 TIA Portal confirmation dialog(s) arrived while no MCP call was answering dialogs and were answered No", line);
        Assert.Contains("\"Möchten Sie die Änderungen speichern?\"", line);
        Assert.Empty(f.Session.EndLingeringConfirmations());
    }

    [Fact]
    public void LateConfirmations_CarryTheirDialogText()
    {
        using var f = new Fixture(allowConfirmations: true);
        using (f.Session.DeclineConfirmations(_ => { })) { }
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation("Projekt speichern?"));

        var line = Assert.Single(f.Session.EndLingeringConfirmations());
        Assert.Contains("were raised after the previous call returned and were answered No", line);
        Assert.Contains("\"Projekt speichern?\"", line);
    }

    [Fact]
    public void Scope_AnswersEveryConfirmationNo_RecordsIt_AndRestoresThePolicy()
    {
        using var f = new Fixture(allowConfirmations: true);
        var seen = new List<ConfirmationEventArgs>();

        using (f.Session.DeclineConfirmations(seen.Add))
        {
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        }

        Assert.Equal(2, seen.Count);
        // Disposed, the scope lingers with its answer until the next request starts: a late dialog is still
        // declined (never the session-wide Yes) and recorded as late, not observed.
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Equal(2, seen.Count);
        Assert.Contains("1 TIA Portal confirmation dialog(s) were raised after the previous call returned and were answered No",
            Assert.Single(f.Session.EndLingeringConfirmations()));
        Assert.Equal(WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Equal(2, seen.Count);
        // The session-wide answer is not silent: the next request reports it (K42).
        Assert.Contains("1 TIA Portal confirmation dialog(s) arrived while no MCP call was answering dialogs",
            Assert.Single(f.Session.EndLingeringConfirmations()));
        Assert.Empty(f.Session.EndLingeringConfirmations());
    }

    [Fact]
    public void AcceptScope_AnswersYesInWritableModes_RecordsTheAnswer_AndRestoresThePolicy()
    {
        // The session-wide policy refuses here, so a Yes can only come from the accepting scope.
        using var f = new Fixture(allowConfirmations: false);
        var answers = new List<ConfirmationResult>();
        var expected = WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No;

        using (f.Session.AnswerConfirmations(accept: true, (_, answer) => answers.Add(answer)))
        {
            Assert.Equal(expected, f.Portal.RaiseConfirmation());
            Assert.Equal(expected, f.Portal.RaiseConfirmation());
        }

        Assert.Equal(new[] { expected, expected }, answers);
        Assert.Equal(expected, f.Portal.RaiseConfirmation());
        Assert.Equal(2, answers.Count);
        Assert.Single(f.Session.EndLingeringConfirmations());
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());

        using (f.Session.AnswerConfirmations(accept: false, (_, answer) => answers.Add(answer)))
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Equal(ConfirmationResult.No, answers[^1]);
        Assert.Throws<ArgumentNullException>(() => f.Session.AnswerConfirmations(true, null!));
    }

    [Fact]
    public void Scope_StillAnswersNo_WhenRecordingThrows_AndEndsOnException()
    {
        using var f = new Fixture(allowConfirmations: true);
        void FailingDownload()
        {
            using var scope = f.Session.DeclineConfirmations(_ => throw new FormatException("observer failed"));
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
            throw new InvalidOperationException("download failed");
        }

        Assert.Throws<InvalidOperationException>(FailingDownload);

        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        f.Session.EndLingeringConfirmations();
        Assert.Equal(WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, f.Portal.RaiseConfirmation());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CloseInto_WaitsForAnInFlightRecording_SoTheEvidenceIsComplete(bool accept, bool sessionWideYes)
    {
        // The session-wide policy is the opposite of the scope's answer where the mode allows it, so the
        // dialog after the close shows that the scope (and its recording) has ended.
        using var f = new Fixture(allowConfirmations: sessionWideYes);
        var log = new PlcDownloadPortalConfirmationLog();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var scope = f.Session.AnswerConfirmations(accept, (args, answer) =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            log.Record(args.GetType().FullName, "late",
                answer == ConfirmationResult.Yes ? PlcDownloadPolicy.PortalConfirmationAnswers.Yes : PlcDownloadPolicy.PortalConfirmationAnswers.No);
        });
        log.AttachScope(scope);
        var scoped = accept && WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No;

        var dialog = Task.Run(() => f.Portal.RaiseConfirmation());
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var result = new PlcDownloadResultInfo();
        var closing = Task.Run(() => log.CloseInto(result));
        await Task.Delay(200);
        Assert.False(closing.IsCompleted, "CloseInto must wait for the recording that is in progress.");

        release.Set();
        Assert.Equal(scoped, await dialog);
        await closing;
        Assert.Equal(1, result.PortalConfirmationsObserved);
        Assert.Equal(scoped == ConfirmationResult.Yes ? "Yes" : "No", Assert.Single(result.PortalConfirmations).Answer);
        Assert.True(result.PortalConfirmationsComplete);
        // After the scope closed, a dialog keeps the call's answer, is reported as late and is not in the evidence;
        // once the next request started, the session-wide answer applies again.
        Assert.Equal(scoped, f.Portal.RaiseConfirmation());
        Assert.Equal(1, log.Observed);
        Assert.Single(f.Session.EndLingeringConfirmations());
        Assert.Equal(sessionWideYes && WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Equal(1, log.Observed);
    }

    [Fact]
    public void Scopes_DoNotNest_AndAStaleDisposeKeepsALaterScope()
    {
        using var f = new Fixture(allowConfirmations: true);
        var first = f.Session.DeclineConfirmations(_ => { });
        Assert.Throws<InvalidOperationException>(() => f.Session.DeclineConfirmations(_ => { }));
        first.Dispose();

        var later = new List<ConfirmationEventArgs>();
        using (f.Session.DeclineConfirmations(later.Add))
        {
            first.Dispose();
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        }

        Assert.Single(later);
        Assert.Throws<ArgumentNullException>(() => f.Session.DeclineConfirmations(null!));
    }
    [Fact]
    public void LingeringScope_EndsAtTheNextScopeOrRequest_AndNeverLeaksBeyondIt()
    {
        using var f = new Fixture(allowConfirmations: true);
        using (f.Session.AnswerConfirmations(false, (_, _) => { }, owner: "plc_download")) { }
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());

        // A new call's scope replaces the lingering one; its late dialog is still reported at the next request.
        var seen = new List<ConfirmationResult>();
        using (f.Session.AnswerConfirmations(false, (_, answer) => seen.Add(answer), owner: "plc_go_online"))
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Single(seen);

        var lines = f.Session.EndLingeringConfirmations();
        Assert.Single(lines);
        Assert.Contains("after plc_download returned", lines[0]);
        Assert.Contains("Siemens.Engineering.ConfirmationEventArgs", lines[0]);
        Assert.Empty(f.Session.EndLingeringConfirmations());
        Assert.Equal(WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, f.Portal.RaiseConfirmation());
    }

    [Fact]
    public async Task LingeringScope_IsThreadSafeAgainstConcurrentDialogsAndTheNextRequest()
    {
        using var f = new Fixture(allowConfirmations: true);
        using (f.Session.AnswerConfirmations(false, (_, _) => { }, owner: "plc_download")) { }
        var answers = new System.Collections.Concurrent.ConcurrentBag<ConfirmationResult>();
        var dialogs = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++) answers.Add(f.Portal.RaiseConfirmation());
        })).ToArray();
        var ended = new List<string>();
        var request = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++) ended.AddRange(f.Session.EndLingeringConfirmations());
        });
        await Task.WhenAll(dialogs.Append(request));

        Assert.Equal(200, answers.Count);
        // Every dialog got either the call's No (scope still lingering) or the session-wide answer (ended), never anything else.
        Assert.All(answers, answer => Assert.True(answer == ConfirmationResult.No || WritableMode() && answer == ConfirmationResult.Yes));
        ended.AddRange(f.Session.EndLingeringConfirmations());
        Assert.True(ended.Count(line => line.Contains("were raised after plc_download returned", StringComparison.Ordinal)) <= 1);
        // Every dialog is reported exactly once: late (scope still lingering) or answered by the session-wide policy.
        Assert.Equal(200, ended.Sum(line => int.Parse(line.Substring(0, line.IndexOf(' ')), System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Empty(f.Session.EndLingeringConfirmations());
    }

    [Fact]
    public void OnlineScope_AnswersByPolicy_ComparesAlwaysDecline_AndBeginRequestEndsEverything()
    {
        using var f = new Fixture(allowConfirmations: true);
        var accept = new TiaMcpServer.Contracts.PlcOnline.PlcOnlineRequestInfo { PortalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Accept };
        var expectedAccept = WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No;

        var (online, onlineScope) = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.Open(f.Session, "plc_go_online", accept, _ => "Continue?");
        using (onlineScope)
            Assert.Equal(expectedAccept, f.Portal.RaiseConfirmation());
        Assert.Equal(1, online.Observed);
        Assert.Equal(expectedAccept, f.Portal.RaiseConfirmation());
        var deferredRan = 0;
        TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.NextRequest.Defer(() =>
        {
            deferredRan++;
            return new[] { "late legitimation refused" };
        });
        var lines = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session);
        Assert.Equal(2, lines.Count);
        Assert.Contains("after plc_go_online returned", lines[0]);
        Assert.Equal("late legitimation refused", lines[1]);
        Assert.Equal(1, deferredRan);
        Assert.Empty(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session));

        var (compare, compareScope) = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.Open(f.Session, "compare_to_online", accept, _ => null);
        using (compareScope)
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        var result = new PlcDownloadResultInfo();
        compare.CloseInto(result);
        Assert.Equal("No", Assert.Single(result.PortalConfirmations).Answer);
        TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session);
    }

    [Fact]
    public void ScanScope_AlwaysDeclines_EvenWhereTheSessionWouldAnswerYes()
    {
        using var f = new Fixture(allowConfirmations: true);
        var (log, scope) = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.OpenDeclining(f.Session, "scan_accessible_devices", _ => "Allow access?");
        using (scope)
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Contains("Allow access?", PlcAccessibleDeviceProjection.DeclinedConfirmations(log));
        // Closed by DeclinedConfirmations: a late dialog keeps the scan's No until the next request starts.
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Contains("after scan_accessible_devices returned", Assert.Single(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session)));
        Assert.Equal(WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, f.Portal.RaiseConfirmation());
    }

    [Fact]
    public void ScanFailure_ReportsADialogDeclinedWhileTheScanFailed_AndLeavesPortalLossAlone()
    {
        using var f = new Fixture(allowConfirmations: true);
        var (log, scope) = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.OpenDeclining(f.Session, "scan_accessible_devices", _ => "Allow access?");
        using (scope)
        {
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
            var failure = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.ScanFailure(
                new EngineeringException("interface busy"), log, "GetAccessibleDevices()");
            Assert.NotNull(failure);
            Assert.Equal(TiaMcpServer.Contracts.Worker.WorkerFailureCategories.WorkerOperationFailed, failure!.FailureCategory);
            Assert.Contains("Allow access?", failure.Message);
            Assert.Contains("GetAccessibleDevices() also failed: TIA Portal operation failed: interface busy", failure.Message);
            // The Siemens evidence stays, so a Portal disposal during the request is still classified portal_lost.
            Assert.Equal("interface busy", failure.EngineeringError!.Message);
        }

        TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session);
        var (quiet, quietScope) = TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.OpenDeclining(f.Session, "scan_accessible_devices", _ => null);
        using (quietScope)
        {
            // No dialog: the original exception is rethrown; a lost Portal and an already typed failure are never wrapped.
            Assert.Null(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.ScanFailure(new EngineeringException("busy"), quiet, "GetAccessibleDevices()"));
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
            Assert.Null(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.ScanFailure(new NonRecoverableException("closed"), quiet, "GetAccessibleDevices()"));
            Assert.Null(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.ScanFailure(
                new WorkerOperationException(TiaMcpServer.Contracts.Worker.WorkerFailureCategories.TargetNotFound, "gone"), quiet, "GetAccessibleDevices()"));
        }

        TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session);
    }

    [Fact]
    public void LibraryScope_AnswersByTheItemPolicy_AndLingersUntilTheNextRequest()
    {
        using var f = new Fixture(allowConfirmations: true);
        var decline = new TiaMcpServer.Contracts.Library.LibraryRequestInfo { PortalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Decline };
        var accept = new TiaMcpServer.Contracts.Library.LibraryRequestInfo { PortalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Accept };

        var (closing, closeScope) = TiaMcpServer.OpennessWorker.Openness.Libraries.LibraryConfirmationScope.Open(f.Session, "library_close_global_library", decline, _ => "Save?");
        using (closeScope)
            Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        var result = new TiaMcpServer.Contracts.Library.LibraryWriteResultInfo();
        closing.CloseInto((entries, observed, complete) => (result.PortalConfirmations, result.PortalConfirmationsObserved, result.PortalConfirmationsComplete) = (entries, observed, complete));
        Assert.Equal(("No", "Save?", 1, true), (Assert.Single(result.PortalConfirmations).Answer, result.PortalConfirmations[0].Text,
            result.PortalConfirmationsObserved, result.PortalConfirmationsComplete));
        // After the item returned: a late dialog keeps the item's No (never the session-wide Yes) and is not in its evidence.
        Assert.Equal(ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Equal(1, closing.Observed);
        var late = Assert.Single(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session));
        Assert.Contains("after library_close_global_library returned and were answered No", late);
        Assert.Equal(ConfirmationResult.Yes, f.Portal.RaiseConfirmation());

        var (opening, openScope) = TiaMcpServer.OpennessWorker.Openness.Libraries.LibraryConfirmationScope.Open(f.Session, "library_open_global_library", accept, _ => null);
        using (openScope)
            Assert.Equal(WritableMode() ? ConfirmationResult.Yes : ConfirmationResult.No, f.Portal.RaiseConfirmation());
        Assert.Equal(1, opening.Observed);
        // The dialog between the requests was answered by the session-wide policy and is reported, not silent (K42).
        Assert.Contains("1 TIA Portal confirmation dialog(s) arrived while no MCP call was answering dialogs",
            Assert.Single(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session)));
        Assert.Empty(TiaMcpServer.OpennessWorker.Openness.PlcOnline.PlcOnlineConfirmationScope.BeginRequest(f.Session));
    }
}
