using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcWebApi;

/// <summary>
/// plc_online_read and plc_online_write end to end over the MCP protocol on the production surface,
/// with the in-memory FakePlcWebApiWorld behind the real service, pipeline and audit sink.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcWebApiMcpTests
{
    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public int Prompts { get; private set; }

        public string? LastPrompt { get; private set; }

        public string[] AuditLines => NetworkGuardedWriteMcpTests.AuditLines(_audit.Path);

        public FakePlcWebApiWorld World => Harness.PlcWebApiWorld;

        public static async Task<Session> StartAsync(McpAccessMode mode, string? elicitation = null)
        {
            var session = new Session();
            Directory.CreateDirectory(session._audit.Path);
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
            return (raw.StructuredContent!.Value, raw);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _audit.Dispose();
        }
    }

    private static Dictionary<string, object?> Operations(params object[] operations) => new() { ["operations"] = operations };

    [Fact]
    public async Task ReadOnly_ObservesOnline_AndHidesTheWriteTool()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);

        var (document, raw) = await session.CallAsync("plc_online_read", Operations(
            new { operationId = "mode", operation = "read_operating_mode", target = "Line1" },
            new { operationId = "diag", operation = "read_diagnostic_buffer", target = "Secure", count = 5 }));

        Assert.False(raw.IsError == true);
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Empty(session.AuditLines);
        var tools = (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();
        Assert.Contains("plc_online_read", tools);
        Assert.DoesNotContain("plc_online_write", tools);
        var hidden = await Assert.ThrowsAsync<McpProtocolException>(() => session.Harness.Client.CallToolAsync("plc_online_write",
            Operations(new { operationId = "stop", operation = "change_operating_mode", target = "Line1", operatingMode = "stop" })).AsTask());
        Assert.Equal(McpErrorCode.InvalidParams, hidden.ErrorCode);
        Assert.Equal("run", session.World.Plc("Line1").OperatingMode);
    }

    [Fact]
    public async Task ReadWrite_PromptsOncePerActualCall_AndNeverForADryRun()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var stop = Operations(new { operationId = "stop", operation = "change_operating_mode", target = "Line1", operatingMode = "stop" },
            new { operationId = "speed", operation = "write_tags", target = "Line1", tags = new[] { new { name = "\"DB_Line\".Speed", value = 0 } } });

        var (preview, _) = await session.CallAsync("plc_online_write", new(stop) { ["dryRun"] = true });
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        Assert.Equal(0, session.Prompts);
        Assert.Empty(session.World.Calls);

        var (applied, raw) = await session.CallAsync("plc_online_write", stop);
        Assert.False(raw.IsError == true);
        Assert.Equal("applied", applied.GetProperty("phase").GetString());
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(1, session.Prompts);
        Assert.Contains("operating mode STOP", session.LastPrompt);
        Assert.Equal("stop", session.World.Plc("Line1").OperatingMode);
        Assert.Equal("0", session.World.Plc("Line1").Tags["\"DB_Line\".Speed"]);
        var audit = session.AuditLines;
        Assert.Equal(2, audit.Length);
        Assert.All(audit, line => Assert.Contains("\"recordKind\":\"plc_online\"", line, StringComparison.Ordinal));
        Assert.Contains("\"confirmation\":{\"by\":\"user\",\"outcome\":\"confirmed\"}", audit[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadWrite_DeclinedPrompt_ChangesNothing()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "decline");

        var (document, raw) = await session.CallAsync("plc_online_write",
            Operations(new { operationId = "stop", operation = "change_operating_mode", target = "Line1", operatingMode = "stop" }));

        Assert.True(raw.IsError);
        Assert.Equal("access_denied", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(1, session.Prompts);
        Assert.Empty(session.World.Calls);
        Assert.Contains("\"outcome\":\"declined\"", Assert.Single(session.AuditLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_RunsWithoutPrompting()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, elicitation: "accept");

        var (document, _) = await session.CallAsync("plc_online_write",
            Operations(new { operationId = "ack", operation = "acknowledge_alarm", target = "Secure", alarmId = "123456" }));

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(0, session.Prompts);
        Assert.Equal(new[] { "123456" }, session.World.Plc("Secure").AcknowledgedAlarms);
        Assert.Contains("\"confirmation\":{\"by\":\"policy\",\"outcome\":\"not_requested\"}", Assert.Single(session.AuditLines), StringComparison.Ordinal);
        Assert.DoesNotContain(FakePlcWebApiWorld.Password, Assert.Single(session.AuditLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentShapeErrors_AreCanonical_AndNeverReachAPlc()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);

        foreach (var (tool, arguments) in new (string, Dictionary<string, object?>)[]
        {
            ("plc_online_read", Operations(new { operationId = "p", operation = "ping", target = "Line1", password = "x" })),
            ("plc_online_write", Operations(new { operationId = "p", operation = "flash_leds", target = "Line1", hwid = 1, user = "x" })),
            ("plc_online_write", new() { ["operations"] = Array.Empty<object>(), ["address"] = "10.0.0.1" }),
        })
            ArgumentRejectionAssert.IsCanonicalValidationError(await session.Harness.Client.CallToolAsync(tool, arguments), tool);

        Assert.Empty(session.World.Calls);
        Assert.Empty(session.AuditLines);
    }
}
