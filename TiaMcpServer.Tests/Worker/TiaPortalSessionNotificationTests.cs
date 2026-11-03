using Siemens.Engineering;
using TiaMcpServer.Contracts.Worker;
using Xunit;
using PortalProject = Siemens.Engineering.Project;
using Session = TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// K75 (live 2026-11-03): TIA Portal's modal information message after export_project_texts blocked the Openness call until a human
/// clicked OK. The session handles every <c>Notification</c> (IsHandled = true), records it for the running request and reports
/// those that arrive between requests to the next one, against the Portal boundary doubles.
/// </summary>
[Collection("Portal session boundary")]
public sealed class TiaPortalSessionNotificationTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly TiaPortalProcess[] _previous = TiaPortal.Processes.ToArray();
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "notification-session-" + Guid.NewGuid().ToString("N"));

        public Fixture()
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
            Session = new Session();
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

    [Fact]
    public void NotificationDuringARequest_IsHandled_RecordedForTheItem_AndWarned()
    {
        using var f = new Fixture();
        Assert.Empty(f.Session.BeginRequestNotifications());
        var original = Console.Error;
        var buffer = new StringWriter();
        Console.SetError(buffer);
        NotificationEventArgs args;
        try
        {
            args = f.Portal.RaiseNotification("Export erfolgreich abgeschlossen. (0032:000002)", "Projekttexte", "24219 Bytes");
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.True(args.IsHandled);
        var recorded = Assert.Single(f.Session.RequestNotifications());
        Assert.Equal(("Projekttexte", "Export erfolgreich abgeschlossen. (0032:000002)", "24219 Bytes", "Information", true),
            (recorded.Caption, recorded.Text, recorded.DetailText, recorded.Icon, recorded.Handled));
        Assert.Contains("TIA Portal notification during this call: \"Projekttexte\" Export erfolgreich abgeschlossen. (0032:000002) (24219 Bytes) [Information]; handled",
            buffer.ToString());
        f.Session.EndRequestNotifications();
        // The next request starts with an empty record and no late line.
        Assert.Empty(f.Session.BeginRequestNotifications());
        Assert.Empty(f.Session.RequestNotifications());
    }

    [Fact]
    public void NotificationBetweenRequests_IsHandled_AndReportedToTheNextRequestOnce()
    {
        using var f = new Fixture();
        f.Session.BeginRequestNotifications();
        f.Session.EndRequestNotifications();
        var original = Console.Error;
        Console.SetError(TextWriter.Null);
        NotificationEventArgs args;
        try
        {
            args = f.Portal.RaiseNotification("Übersetzen abgeschlossen", icon: NotificationIcon.Warning);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.True(args.IsHandled);
        var line = Assert.Single(f.Session.BeginRequestNotifications());
        Assert.StartsWith("1 TIA Portal notification(s) arrived while no MCP call was running and were handled", line);
        Assert.Contains("Übersetzen abgeschlossen [Warning]", line);
        // Not part of the new request's own record, and reported once.
        Assert.Empty(f.Session.RequestNotifications());
        f.Session.EndRequestNotifications();
        Assert.Empty(f.Session.BeginRequestNotifications());
    }

    private sealed class ReadOnlyHandled : EventArgs
    {
        public string Text => "Info";

        public bool IsHandled => false;
    }

    [Fact]
    public void Handling_ReportsNotHandled_WhenIsHandledCannotBeSet()
    {
        var info = PortalNotificationHandling.Handle(new ReadOnlyHandled());
        Assert.Equal(("Info", false), (info.Text, info.Handled));
        Assert.Contains("IsHandled could not be set", PortalNotificationHandling.RequestWarning(info));
    }
}
