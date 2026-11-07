using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Shared.ClaudeBridge;
using TiaMcpServer.Contracts.Shared.ClaudeInbox;
using TiaMcpServer.Contracts.Shared.PasswordManagement;
using Xunit;

namespace TiaMcpServer.Tests.Contracts.ClaudeInbox;

public sealed class ClaudeBridgeTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 3, 10, 20, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claude-bridge-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static ClaudeBridgeSettings On(int timeout = 5, IReadOnlyList<string>? arguments = null)
        => new(true, "claude", arguments ?? ClaudeBridgeSettings.DefaultArguments, timeout, ClaudeBridgeSettings.Default.AllowedTools);

    private sealed class ScriptedRunner(Func<ScriptedRunner, ClaudeRunResult> behave) : IClaudeProcessRunner
    {
        public List<(string Executable, IReadOnlyList<string> Arguments, string? Input, TimeSpan Timeout)> Calls { get; } = new();

        public Task<ClaudeRunResult> RunAsync(string executable, IReadOnlyList<string> arguments, string? standardInput, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add((executable, arguments, standardInput, timeout));
            return Task.FromResult(behave(this));
        }
    }

    private (ClaudeInboxStore Store, ClaudeBridgeState State, List<string> Log) Setup()
        => (new ClaudeInboxStore(_root), ClaudeBridgeState.Load(Path.Combine(_root, "bridge", "state.json")), new List<string>());

    private ClaudeBridgeEngine Engine(ClaudeInboxStore store, ClaudeBridgeState state, IClaudeProcessRunner runner, List<string> log)
        => new(store, state, runner, log.Add, () => Now, _root);

    [Fact]
    public void DefaultTools_AreExactlyTheRegisteredMcpTools_AndNoBuiltInTool()
    {
        var registered = typeof(TiaMcpServer.Tools.ProjectWriteTools).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name).Where(n => n is not null).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(registered, ClaudeBridgeSettings.TiaToolNames.Order(StringComparer.Ordinal));
        Assert.All(ClaudeBridgeSettings.Default.AllowedTools, t => Assert.StartsWith("mcp__tia-portal__", t));
        Assert.False(ClaudeBridgeSettings.Default.Enabled);
        Assert.Contains("--tools", ClaudeBridgeSettings.DefaultArguments);
        Assert.Equal("", ClaudeBridgeSettings.DefaultArguments[ClaudeBridgeSettings.DefaultArguments.ToList().IndexOf("--tools") + 1]);
        Assert.Null(ClaudeBridgeSettings.Default.Problem());
    }

    [Fact]
    public void Command_ExpandsPlaceholders_AndSendsPromptOnStdinUnlessTemplated()
    {
        var command = ClaudeBridgeCommand.Build(On(), "PROMPT", "20261103T101500Z-abcdef01", @"C:\Users\u\AppData\Roaming\TiaMcpServer");
        Assert.Equal("PROMPT", command.StandardInput);
        Assert.Equal(string.Join(",", ClaudeBridgeSettings.Default.AllowedTools), command.Arguments[command.Arguments.ToList().IndexOf("--allowedTools") + 1]);
        var templated = ClaudeBridgeCommand.Build(On(arguments: new[] { "-p", "{prompt}", "--id={requestId}" }), "P \"x\"", "20261103T101500Z-abcdef01", "R");
        Assert.Null(templated.StandardInput);
        Assert.Equal(new[] { "-p", "P \"x\"", "--id=20261103T101500Z-abcdef01" }, templated.Arguments);
    }

    [Fact]
    public void Prompt_CarriesRequestAndFinishInstruction()
    {
        var request = ClaudeInboxContractTests.SampleRequest(id: "20261103T101500Z-abcdef01", action: ClaudeActions.Explain);
        var prompt = ClaudeBridgePrompt.Build(request);
        Assert.Contains("20261103T101500Z-abcdef01", prompt);
        Assert.Contains("blockPath=PLC_1/Blocks/Line \"A\"/Main", prompt);
        Assert.Contains("tia_inbox reply_request", prompt);
        Assert.Contains(@"C:\Projekte\Anlage\Anlage.ap21", prompt);
        Assert.Contains("never download", prompt);
    }

    [Fact]
    public async Task Disabled_RunsNothing()
    {
        var (store, state, log) = Setup();
        store.WriteRequest(ClaudeInboxContractTests.SampleRequest(id: "20261103T101500Z-abcdef01"));
        var runner = new ScriptedRunner(_ => throw new InvalidOperationException());
        Assert.Equal(0, await Engine(store, state, runner, log).ProcessPendingAsync(ClaudeBridgeSettings.Default, CancellationToken.None));
        Assert.Empty(runner.Calls);
        Assert.Null(store.ReadStatus("20261103T101500Z-abcdef01"));
    }

    [Fact]
    public async Task RunWithoutReply_WritesCliResultAsReply_AndNeverRunsTwice()
    {
        var (store, state, log) = Setup();
        store.WriteRequest(ClaudeInboxContractTests.SampleRequest(id: "20261103T101500Z-abcdef01"));
        store.WriteRequest(ClaudeInboxContractTests.SampleRequest(id: "20261103T101600Z-abcdef02"));
        var runner = new ScriptedRunner(_ => new ClaudeRunResult(0, "{\"type\":\"result\",\"is_error\":false,\"result\":\"Main ruft FB_Motor auf.\"}", "", false));
        var engine = Engine(store, state, runner, log);
        Assert.Equal(2, await engine.ProcessPendingAsync(On(), CancellationToken.None));
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), runner.Calls[0].Timeout);
        Assert.Contains("20261103T101500Z-abcdef01", runner.Calls[0].Input);
        var reply = store.ReadReply("20261103T101500Z-abcdef01")!;
        Assert.Equal((ClaudeReply.StatusDone, ClaudeReply.FromBridge), (reply.Status, reply.From));
        Assert.Contains("Main ruft FB_Motor auf.", reply.Text);
        Assert.Equal(ClaudeRequestStatus.Finished, store.ReadStatus("20261103T101500Z-abcdef01")!.State);

        // A reply that disappears (deleted by hand) does not make the request run again: the state file remembers it.
        File.Delete(store.ReplyPath("20261103T101500Z-abcdef01"));
        var reloaded = ClaudeBridgeState.Load(Path.Combine(_root, "bridge", "state.json"));
        Assert.Equal(0, await Engine(store, reloaded, runner, log).ProcessPendingAsync(On(), CancellationToken.None));
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ClaudesOwnReply_IsKept()
    {
        var (store, state, log) = Setup();
        store.WriteRequest(ClaudeInboxContractTests.SampleRequest(id: "20261103T101500Z-abcdef01"));
        var runner = new ScriptedRunner(_ =>
        {
            store.WriteReply("20261103T101500Z-abcdef01", ClaudeReply.StatusDone, "Antwort über tia_inbox", ClaudeReply.FromClaude, Now);
            return new ClaudeRunResult(0, "{\"result\":\"fertig\"}", "", false);
        });
        await Engine(store, state, runner, log).ProcessPendingAsync(On(), CancellationToken.None);
        var reply = store.ReadReply("20261103T101500Z-abcdef01")!;
        Assert.Equal((ClaudeReply.FromClaude, "Antwort über tia_inbox"), (reply.From, reply.Text));
    }

    [Theory]
    [InlineData(true, null, null, ClaudeRequestStatus.TimedOut)]
    [InlineData(false, 1, null, ClaudeRequestStatus.Failed)]
    [InlineData(false, null, "not found", ClaudeRequestStatus.Failed)]
    public async Task FailedRuns_WriteFailedReplyAndStatus(bool timedOut, int? exit, string? startError, string expectedState)
    {
        var (store, state, log) = Setup();
        store.WriteRequest(ClaudeInboxContractTests.SampleRequest(id: "20261103T101500Z-abcdef01"));
        var runner = new ScriptedRunner(_ => new ClaudeRunResult(exit, "partial", "boom", timedOut, startError));
        await Engine(store, state, runner, log).ProcessPendingAsync(On(), CancellationToken.None);
        Assert.Equal(expectedState, store.ReadStatus("20261103T101500Z-abcdef01")!.State);
        var reply = store.ReadReply("20261103T101500Z-abcdef01")!;
        Assert.Equal(ClaudeReply.StatusFailed, reply.Status);
        Assert.Contains("nicht erledigen", reply.Text);
    }

    [Fact]
    public async Task OldAndInvalidRequests_AreSkippedNotRun()
    {
        var (store, state, log) = Setup();
        store.WriteRequest(ClaudeInboxContractTests.SampleRequest(id: "20261101T101500Z-abcdef01"));
        var old = ClaudeInboxContractTests.SampleRequest(id: "20261101T101500Z-abcdef02");
        File.WriteAllText(store.RequestPath(old.Id), ClaudeInboxSerializer.Serialize(old).Replace("2026-11-03T10:15:00Z", "2026-11-01T10:15:00Z"));
        File.WriteAllText(Path.Combine(store.InboxDirectory, "20261103T101500Z-abcdef03.json"), "{");
        var runner = new ScriptedRunner(_ => new ClaudeRunResult(0, "", "", false));
        var started = await Engine(store, state, runner, log).ProcessPendingAsync(On(), CancellationToken.None);
        Assert.Equal(1, started);
        Assert.Equal(ClaudeRequestStatus.Skipped, store.ReadStatus(old.Id)!.State);
        Assert.Equal(ClaudeRequestStatus.Skipped, store.ReadStatus("20261103T101500Z-abcdef03")!.State);
    }

    [Theory]
    [InlineData("{\"result\":\"ok\",\"is_error\":true}", "ok", true)]
    [InlineData("plain text", "plain text", false)]
    [InlineData("", "", false)]
    public void FinalOutput_ReadsJsonResultOrRawText(string output, string text, bool isError)
        => Assert.Equal((text, isError), ClaudeBridgeEngine.FinalOutput(output));

    [Fact]
    public void Settings_Version2_RoundTripsAndKeepsAccessMode()
    {
        var bridge = new ClaudeBridgeSettings(true, @"C:\Tools\claude.exe", new[] { "-p", "" }, 30, new[] { "mcp__tia-portal__plc_read" });
        var json = ServerSettingsFile.Serialize(new ServerSettings(McpAccessMode.ReadOnly, bridge));
        Assert.Contains("\"version\": 2", json);
        var back = ServerSettingsFile.Parse(json);
        Assert.Equal(McpAccessMode.ReadOnly, back.AccessMode);
        Assert.Equal(new[] { "-p", "" }, back.ClaudeBridge!.Arguments);
        Assert.Contains("\"version\": 1", ServerSettingsFile.Serialize(new ServerSettings(McpAccessMode.Full)));
    }

    [Theory]
    [InlineData("""{ "version": 2, "claudeBridge": { "enabled": true } }""", "needs the member")]
    [InlineData("""{ "version": 2, "claudeBridge": { "enabled": "yes", "command": "c", "arguments": [], "timeoutMinutes": 5, "allowedTools": [] } }""", "true or false")]
    [InlineData("""{ "version": 2, "claudeBridge": { "enabled": true, "command": "", "arguments": [], "timeoutMinutes": 5, "allowedTools": [] } }""", "command")]
    [InlineData("""{ "version": 2, "claudeBridge": { "enabled": true, "command": "c", "arguments": [], "timeoutMinutes": 0, "allowedTools": [] } }""", "timeoutMinutes")]
    [InlineData("""{ "version": 2, "claudeBridge": { "enabled": true, "command": "c", "arguments": [], "timeoutMinutes": 5, "allowedTools": ["a,b"] } }""", "allowedTools")]
    [InlineData("""{ "version": 2, "claudeBridge": { "enabled": true, "command": "c", "arguments": [], "timeoutMinutes": 5, "allowedTools": [], "shell": true } }""", "unknown member")]
    public void Settings_BridgeObjectIsClosedAndChecked(string json, string expected)
        => Assert.Contains(expected, Assert.Throws<ServerSettingsException>(() => ServerSettingsFile.Parse(json)).Message);

    [Fact]
    public void Editor_SavesBridgeAndAccessModeWithoutLosingTheOther()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        var first = ServerSettingsEditor.Load(path);
        var second = ServerSettingsEditor.Load(path);
        Assert.Null(first.Save(McpAccessMode.ReadOnly));
        Assert.Null(second.SaveBridge(On()));
        Assert.Null(first.Save(McpAccessMode.Full));
        var stored = ServerSettingsFile.Parse(File.ReadAllText(path));
        Assert.Equal(McpAccessMode.Full, stored.AccessMode);
        Assert.True(stored.ClaudeBridge!.Enabled);
        Assert.Contains("ungültig", ServerSettingsEditor.Load(path).SaveBridge(new ClaudeBridgeSettings(true, "", Array.Empty<string>(), 5, Array.Empty<string>()))!);
    }

    [Fact]
    public void EditorText_RoundTripsEmptyArgument()
    {
        var text = ClaudeBridgeEditorText.ArgumentsToText(ClaudeBridgeSettings.DefaultArguments);
        Assert.Contains("\"\"", text);
        Assert.Equal(ClaudeBridgeSettings.DefaultArguments, ClaudeBridgeEditorText.ArgumentsFromText(text));
    }

    [Fact]
    public void Autostart_IsCmdStartingTheBridgeMinimized_AndRefusesCmdSpecialCharacters()
    {
        var content = ClaudeBridgeAutostart.Content(@"C:\Users\u\AppData\Local\TiaMcpServer\claude-bridge\TiaMcpClaudeBridge.exe");
        Assert.StartsWith("@echo off\r\n", content);
        Assert.Contains("start \"\" /min \"C:\\Users\\u\\AppData\\Local\\TiaMcpServer\\claude-bridge\\TiaMcpClaudeBridge.exe\"", content);
        Assert.Throws<ArgumentException>(() => ClaudeBridgeAutostart.Content("C:\\x\" & calc"));
        Assert.EndsWith(".cmd", ClaudeBridgeAutostart.FileName);
        Assert.NotNull(ClaudeBridgeAutostart.Problem(@"C:\missing.exe", _ => false));
    }
}
