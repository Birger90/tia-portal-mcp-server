using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.ProjectServers;

/// <summary>
/// project_server_write end to end against the scripted FakeWorker Project Server state (TIA_MCP_FAKE_WORKER_PROJECT_SERVERS=1;
/// object access only, no Siemens evidence): discovery, unbound execution, acknowledge prompts in read-write, policy in full,
/// dry runs, read-back through the bind_project inventory and the audit record.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class ProjectServerWriteMcpTests
{
    private const string Variable = "TIA_MCP_FAKE_WORKER_PROJECT_SERVERS";

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

        public McpProtocolTestHarness Harness { get; private set; } = null!;
        public int Prompts { get; private set; }
        public string? LastPrompt { get; private set; }
        public string AuditText => string.Join("\n", NetworkGuardedWriteMcpTests.AuditLines(_audit.Path));
        public int AuditCount => NetworkGuardedWriteMcpTests.AuditLines(_audit.Path).Count();

        public static async Task<Session> StartAsync(McpAccessMode mode, string? elicitation = "accept")
        {
            var session = new Session();
            Directory.CreateDirectory(session._audit.Path);
            Environment.SetEnvironmentVariable(Variable, "1");
            McpClientOptions? options = elicitation is null ? null : new()
            {
                Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (request, _) =>
                    {
                        session.Prompts++;
                        session.LastPrompt = request?.Message;
                        return ValueTask.FromResult(new ElicitResult
                        {
                            Action = elicitation,
                            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
                        });
                    },
                },
            };
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, clientOptions: options);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            Assert.Equal(TiaMcpServer.Json.CanonicalJson.Serialize(raw.StructuredContent!.Value), Assert.Single(raw.Content.OfType<TextContentBlock>()).Text);
            return (raw.StructuredContent!.Value, raw);
        }

        public Task<(JsonElement Document, CallToolResult Raw)> WriteAsync(bool? dryRun, params object[] operations)
        {
            var arguments = new Dictionary<string, object?> { ["operations"] = operations };
            if (dryRun is not null) arguments["dryRun"] = dryRun;
            return CallAsync("project_server_write", arguments);
        }

        public async Task<JsonElement> InspectAsync(Dictionary<string, object?> arguments)
        {
            var (document, raw) = await CallAsync("bind_project", arguments);
            Assert.False(raw.IsError == true, document.GetRawText());
            return document.GetProperty("result").GetProperty("value").GetProperty("inspection");
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            Environment.SetEnvironmentVariable(Variable, _previous);
            _audit.Dispose();
        }
    }

    private static readonly JsonElement Root = JsonSerializer.Deserialize<JsonElement>("""{"isRoot":true,"name":null}""");

    private static string[] GuardIds(JsonElement root) => root.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    private static JsonElement[] Items(JsonElement root) => root.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();

    [Fact]
    public async Task Discovery_OfferedInWritableModesOnly()
    {
        foreach (var (mode, expected) in new[] { (McpAccessMode.ReadOnly, false), (McpAccessMode.ReadWrite, true), (McpAccessMode.Full, true) })
        {
            await using var session = await Session.StartAsync(mode, elicitation: null);
            var tools = (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();
            Assert.Equal(expected, tools.Contains("project_server_write"));
        }
    }

    [Fact]
    public async Task ReadWrite_SetAndCreateSessionWithoutBinding_RunWithoutPrompt_AndAreReadBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var directory = Path.Combine(Path.GetTempPath(), "psrv-mcp-" + Guid.NewGuid().ToString("N"));

        var (document, raw) = await session.WriteAsync(null,
            new { operationId = "port", operation = "set_server_connection", serverAlias = "PsrvA", port = 9000 },
            new { operationId = "session", operation = "create_local_session", serverAlias = "PsrvA", group = Root, serverProjectName = "LineA", sessionName = "LineA_bs", sessionDirectory = directory });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(0, session.Prompts);
        Assert.Contains("project_server_creates_local_session", GuardIds(document));
        var created = Items(document)[1].GetProperty("result");
        Assert.Equal(2, created.GetProperty("sessionId").GetInt32());
        Assert.EndsWith("LineA_bs.als21", created.GetProperty("sessionPath").GetString());
        var checks = document.GetProperty("verification").GetProperty("operations").EnumerateArray().Select(o => (o.GetProperty("check").GetString(), o.GetProperty("success").GetBoolean())).ToArray();
        Assert.Equal(new[] { ("serverConnection", true), ("localSession", true) }, checks);

        var connections = await session.InspectAsync(new() { ["action"] = "list_server_connections" });
        Assert.Equal(9000, connections.GetProperty("serverConnections").GetProperty("connections")[0].GetProperty("port").GetInt32());
        Assert.Equal(1, session.AuditCount);
    }

    [Fact]
    public async Task ReadWrite_DeleteSession_AsksOnce_DeclineChangesNothing()
    {
        await using (var declining = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "decline"))
        {
            var (document, raw) = await declining.WriteAsync(null,
                new { operationId = "del", operation = "delete_local_session_from_server", serverAlias = "PsrvA", group = Root, serverProjectName = "LineA", sessionId = 1 });
            Assert.True(raw.IsError);
            Assert.Equal("blocked", document.GetProperty("phase").GetString());
            Assert.Equal(1, declining.Prompts);
            Assert.Contains("Deletes the local session 1", declining.LastPrompt ?? string.Empty, StringComparison.Ordinal);
            var sessions = await declining.InspectAsync(new() { ["action"] = "list_local_sessions", ["serverAlias"] = "PsrvA", ["group"] = Root, ["serverProjectName"] = "LineA" });
            Assert.Single(sessions.GetProperty("localSessions").GetProperty("sessions").EnumerateArray());
        }

        await using var accepting = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (accepted, _) = await accepting.WriteAsync(null,
            new { operationId = "del", operation = "delete_local_session_from_server", serverAlias = "PsrvA", group = Root, serverProjectName = "LineA", sessionId = 1 });
        Assert.True(accepted.GetProperty("success").GetBoolean(), accepted.GetRawText());
        Assert.Equal(1, accepting.Prompts);
        Assert.Contains("\"user\"", accepting.AuditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_UploadAndDeleteConnection_RunUnderPolicy_DryRunTouchesNothing()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var project = Path.Combine(Path.GetTempPath(), "psrv-mcp-" + Guid.NewGuid().ToString("N"), "Line7.ap21");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        await File.WriteAllTextAsync(project, "fake");
        try
        {
            object[] items =
            [
                new { operationId = "conn", operation = "create_server_connection", serverAlias = "PsrvB", protocol = "Https", host = "mu-b.example", port = 8735 },
                new { operationId = "upload", operation = "add_project_to_server", serverAlias = "PsrvA", group = Root, projectPath = project },
                new { operationId = "drop", operation = "delete_server_connection", serverAlias = "PsrvB" },
            ];
            var (preview, _) = await session.WriteAsync(true, items);
            Assert.Equal("preview", preview.GetProperty("phase").GetString());
            Assert.Contains("project_server_uploads_project", GuardIds(preview));
            Assert.Contains("project_server_deletes_connection", GuardIds(preview));
            var before = await session.InspectAsync(new() { ["action"] = "list_server_projects", ["serverAlias"] = "PsrvA", ["group"] = Root });
            Assert.Single(before.GetProperty("serverProjects").GetProperty("projects").EnumerateArray());

            var (applied, _) = await session.WriteAsync(null, items);
            Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
            Assert.Equal(0, session.Prompts);
            Assert.All(applied.GetProperty("guards").EnumerateArray().Where(g => g.GetProperty("severity").GetString() == "acknowledge"),
                guard => Assert.True(guard.GetProperty("acknowledged").GetBoolean()));
            var verification = applied.GetProperty("verification").GetProperty("operations").EnumerateArray().ToArray();
            Assert.Contains(verification, v => v.GetProperty("operationId").GetString() == "upload" && v.GetProperty("actual").GetString() == "Line7");
            var connections = await session.InspectAsync(new() { ["action"] = "list_server_connections" });
            Assert.Single(connections.GetProperty("serverConnections").GetProperty("connections").EnumerateArray());
            Assert.Contains("\"policy\"", session.AuditText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(project)!, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownAlias_RefusesBeforeAnyItem_AndRejectedUploadIsEngineeringRejected()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (unknown, raw) = await session.WriteAsync(null, new { operationId = "x", operation = "delete_server_connection", serverAlias = "Customer" });
        Assert.True(raw.IsError);
        Assert.Equal("target_not_found", unknown.GetProperty("error").GetProperty("category").GetString());

        var project = Path.Combine(Path.GetTempPath(), "psrv-mcp-" + Guid.NewGuid().ToString("N"), "Rejected.ap21");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        await File.WriteAllTextAsync(project, "fake");
        try
        {
            var (rejected, _) = await session.WriteAsync(null, new { operationId = "upload", operation = "add_project_to_server", serverAlias = "PsrvA", group = Root, projectPath = project });
            Assert.False(rejected.GetProperty("success").GetBoolean());
            var item = Items(rejected)[0];
            Assert.Equal("engineering_rejected", item.GetProperty("failure").GetProperty("category").GetString());
            Assert.False(rejected.GetProperty("verification").GetProperty("operations")[0].GetProperty("success").GetBoolean());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(project)!, recursive: true);
        }
    }

    [Fact]
    public async Task DeclinedPortalConfirmation_FailsTheItem_AcceptWarns()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var directory = Path.Combine(Path.GetTempPath(), "psrv-mcp-" + Guid.NewGuid().ToString("N"));
        var (declined, _) = await session.WriteAsync(null,
            new { operationId = "s", operation = "create_local_session", serverAlias = "PsrvA", group = Root, serverProjectName = "LineA", sessionName = "AskDialog", sessionDirectory = directory });
        Assert.Equal("failed", Items(declined)[0].GetProperty("status").GetString());
        Assert.Equal("No", Items(declined)[0].GetProperty("result").GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());

        var (accepted, _) = await session.WriteAsync(null,
            new { operationId = "s2", operation = "create_local_session", serverAlias = "PsrvA", group = Root, serverProjectName = "LineA", sessionName = "AskDialog", sessionDirectory = directory, portalConfirmations = "accept" });
        Assert.Equal("succeeded", Items(accepted)[0].GetProperty("status").GetString());
        Assert.Contains(Items(accepted)[0].GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("answered Yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForeignField_IsValidationErrorWithoutWorkerCall()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(null, new { operationId = "x", operation = "delete_server_connection", serverAlias = "PsrvA", host = "h" });
        Assert.True(raw.IsError);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
    }
}
