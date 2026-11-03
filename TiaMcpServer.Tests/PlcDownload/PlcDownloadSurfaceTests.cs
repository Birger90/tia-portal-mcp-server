using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>
/// The registered plc_download surface over the real MCP protocol. These calls never need a worker,
/// so the harness gets a worker path that cannot start: any accidental launch fails the call, and the
/// tests also assert the transport was never created. They run on every platform.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcDownloadSurfaceTests
{
    private const string NoWorker = "plc-download-worker-must-not-start.exe";

    private static Task<McpProtocolTestHarness> StartAsync(McpAccessMode mode, string? audit = null)
        => McpProtocolTestHarness.StartProductionSurfaceAsync(mode, audit, workerExecutablePath: NoWorker);

    private static void AssertNoWorker(McpProtocolTestHarness harness)
        => Assert.Null(typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(harness.WorkerClient));

    [Theory]
    [InlineData(McpAccessMode.ReadOnly, false, 11)]
    [InlineData(McpAccessMode.ReadWrite, true, 30)]
    [InlineData(McpAccessMode.Full, true, 30)]
    public async Task PlcDownload_IsRegisteredOnlyInWritableModes(McpAccessMode mode, bool registered, int count)
    {
        await using var harness = await StartAsync(mode);
        var names = (await harness.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        Assert.Equal(registered, names.Contains("plc_download"));
        Assert.Contains("plc_read", names);
        Assert.Equal(count, names.Length);
        AssertNoWorker(harness);
    }

    [Fact]
    public async Task ReadOnly_CannotCallPlcDownload()
    {
        await using var harness = await StartAsync(McpAccessMode.ReadOnly);
        var exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
            harness.Client.CallToolAsync("plc_download", new Dictionary<string, object?> { ["dryRun"] = true }).AsTask());
        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        AssertNoWorker(harness);
    }

    [Fact]
    public async Task Schema_IsClosedWithExactInputsAndConservativeAnnotations()
    {
        await using var harness = await StartAsync(McpAccessMode.Full);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), tool => tool.Name == "plc_download");
        var schema = tool.ProtocolTool.InputSchema;

        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        var properties = schema.GetProperty("properties");
        Assert.Equal(new[] { "decisions", "dryRun", "mode", "passwords", "pcInterface", "plcName", "portalConfirmations", "rhTarget", "scope", "startAfterDownload", "targetInterface",
                "trustedCertificates" },
            properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.False(schema.TryGetProperty("required", out var required) && required.GetArrayLength() > 0);
        var pcInterface = properties.GetProperty("pcInterface");
        Assert.False(pcInterface.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(new[] { "name", "number" }, pcInterface.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Order());
        // pcInterface may be omitted but not null; plcName and targetInterface accept null as omitted.
        Assert.Equal("object", pcInterface.GetProperty("type").GetString());
        Assert.False(pcInterface.TryGetProperty("default", out _));
        Assert.Contains(properties.GetProperty("plcName").GetProperty("type").EnumerateArray(), type => type.GetString() == "null");
        Assert.Contains(properties.GetProperty("targetInterface").GetProperty("type").EnumerateArray(), type => type.GetString() == "null");
        Assert.Equal("PN/IE", properties.GetProperty("mode").GetProperty("default").GetString());
        Assert.Equal("hardware_and_software", properties.GetProperty("scope").GetProperty("default").GetString());
        Assert.True(properties.GetProperty("startAfterDownload").GetProperty("default").GetBoolean());
        Assert.False(properties.GetProperty("dryRun").GetProperty("default").GetBoolean());
        Assert.Equal("decline", properties.GetProperty("portalConfirmations").GetProperty("default").GetString());
        // decisions: object (or null = omitted) of enum member names or true; passwords: object of password sources.
        var decisions = properties.GetProperty("decisions");
        Assert.Equal(new[] { "null", "object" }, decisions.GetProperty("type").EnumerateArray().Select(type => type.GetString()).Order());
        var decisionValue = decisions.GetProperty("additionalProperties").GetProperty("anyOf");
        Assert.Equal("string", decisionValue[0].GetProperty("type").GetString());
        Assert.True(decisionValue[1].GetProperty("const").GetBoolean());
        var passwords = properties.GetProperty("passwords");
        // A source: an environment variable NAME, { env } or { credential: "TiaMcp/..." } (PasswordSourceArgument).
        var sources = passwords.GetProperty("additionalProperties").GetProperty("anyOf").EnumerateArray().ToArray();
        Assert.Equal("string", sources[0].GetProperty("type").GetString());
        Assert.Equal(new[] { "env", "credential" }, sources.Skip(1).Select(source => source.GetProperty("required")[0].GetString()));
        Assert.Contains("NAME of an environment variable", passwords.GetProperty("description").GetString());
        // trustedCertificates: an array (or null = omitted) of at most 16 SHA-256 fingerprint strings.
        var trusted = properties.GetProperty("trustedCertificates");
        Assert.Equal(new[] { "array", "null" }, trusted.GetProperty("type").EnumerateArray().Select(type => type.GetString()).Order());
        Assert.Equal("string", trusted.GetProperty("items").GetProperty("type").GetString());
        Assert.Equal(16, trusted.GetProperty("maxItems").GetInt32());
        Assert.Matches(trusted.GetProperty("items").GetProperty("pattern").GetString()!, "AB:" + new string('C', 62));
        Assert.DoesNotMatch(trusted.GetProperty("items").GetProperty("pattern").GetString()!, new string('C', 63));
        Assert.Contains("USER checked and approved", trusted.GetProperty("description").GetString());
        Assert.Contains("REAL controller", tool.ProtocolTool.Description);
        Assert.Contains("trustedCertificates", tool.ProtocolTool.Description);

        var annotations = tool.ProtocolTool.Annotations!;
        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.True(annotations.OpenWorldHint);
        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        AssertNoWorker(harness);
    }

    public static TheoryData<string> ShapeErrors => new()
    {
        """{"unexpectedArgument":true}""",
        """{"projectPath":"C:/Projects/Line.ap21"}""",
        """{"confirm":true}""",
        """{"dryRun":null}""",
        """{"startAfterDownload":null}""",
        """{"dryRun":"true"}""",
        """{"mode":null}""",
        """{"scope":null}""",
        """{"scope":7}""",
        """{"pcInterface":{"name":"PLCSIM","number":1,"address":"192.168.0.1"}}""",
        """{"pcInterface":{"name":"PLCSIM"}}""",
        """{"pcInterface":{"number":1}}""",
        """{"pcInterface":{"name":"PLCSIM","number":"1"}}""",
        """{"pcInterface":"PLCSIM"}""",
        // An explicit null must not silently select the default { PLCSIM, 1 }.
        """{"pcInterface":null}""",
        """{"targetInterface":["1 X1"]}""",
        """{"portalConfirmations":null}""",
        """{"portalConfirmations":true}""",
        // decisions values are an enum member name or true; anything else, nesting and duplicate keys are shape errors.
        """{"decisions":{"ResetModule":false}}""",
        """{"decisions":{"ResetModule":1}}""",
        """{"decisions":{"ResetModule":null}}""",
        """{"decisions":{"ResetModule":["DeleteAll"]}}""",
        """{"decisions":{"ResetModule":{"value":"DeleteAll"}}}""",
        """{"decisions":{"ResetModule":"DeleteAll","ResetModule":"NoAction"}}""",
        """{"decisions":["ResetModule"]}""",
        """{"decisions":"ResetModule=DeleteAll"}""",
        """{"passwords":{"ModuleWriteAccessPassword":true}}""",
        """{"passwords":{"ModuleWriteAccessPassword":null}}""",
        """{"passwords":{"ModuleWriteAccessPassword":"A","ModuleWriteAccessPassword":"B"}}""",
        """{"passwords":["PLC_PW"]}""",
    };

    [Theory]
    [MemberData(nameof(ShapeErrors))]
    public async Task ArgumentShapeErrors_AreCanonicalUnauditedAndNeverReachAWorker(string arguments)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await StartAsync(McpAccessMode.Full, audit.Path);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value);

        var result = await harness.Client.CallToolAsync("plc_download", parsed);

        ArgumentRejectionAssert.IsCanonicalValidationError(result, "plc_download");
        var document = result.StructuredContent!.Value;
        Assert.Equal("plc_download", document.GetProperty("tool").GetString());
        Assert.Equal("error", document.GetProperty("phase").GetString());
        Assert.Empty(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
        AssertNoWorker(harness);
    }

    [Theory]
    [InlineData("""{"scope":"everything"}""", "validation_error")]
    [InlineData("""{"pcInterface":{"name":"","number":1}}""", "validation_error")]
    [InlineData("""{"pcInterface":{"name":null,"number":1}}""", "validation_error")]
    [InlineData("""{"dryRun":true}""", "binding_conflict")]
    [InlineData("""{}""", "binding_conflict")]
    // Explicit nulls of the optional selectors mean omitted (like other tools' optional selectors): the call enters.
    [InlineData("""{"plcName":null,"targetInterface":null}""", "binding_conflict")]
    [InlineData("""{"decisions":null,"passwords":null}""", "binding_conflict")]
    [InlineData("""{"decisions":{"ResetModule":"DeleteAll","UpgradeTargetDevice":true,"SomeFutureType":"X"}}""", "binding_conflict")]
    [InlineData("""{"portalConfirmations":"accept"}""", "binding_conflict")]
    [InlineData("""{"portalConfirmations":"yes"}""", "validation_error")]
    [InlineData("""{"decisions":{"ResetModule":" "}}""", "validation_error")]
    [InlineData("""{"decisions":{"StartModules":"NoAction"}}""", "validation_error")]
    [InlineData("""{"decisions":{"Siemens.Engineering.Download.Configurations.ResetModule":"DeleteAll"}}""", "validation_error")]
    [InlineData("""{"passwords":{"ModuleWriteAccessPassword":"my plain password"}}""", "validation_error")]
    [InlineData("""{"passwords":{"ModuleWriteAccessPassword":"TIA_MCP_TEST_UNSET_VARIABLE_4711"}}""", "validation_error")]
    public async Task EnteredRejections_AreCanonicalAuditedOnceAndNeverReachAWorker(string arguments, string category)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        await using var harness = await StartAsync(McpAccessMode.ReadWrite, audit.Path);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!
            .ToDictionary(pair => pair.Key, pair => (object?)pair.Value);

        var result = await harness.Client.CallToolAsync("plc_download", parsed);

        Assert.Empty(StructuredContractInspector.FindViolations(result));
        Assert.True(result.IsError == true);
        var document = result.StructuredContent!.Value;
        Assert.Equal(category, document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("result").ValueKind);
        var line = Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
        var record = JsonDocument.Parse(line).RootElement;
        Assert.Equal("plc_download", record.GetProperty("tool").GetString());
        Assert.Equal("error", record.GetProperty("phase").GetString());
        // An invalid password value is never echoed: it may be the password itself.
        Assert.DoesNotContain("my plain password", document.GetRawText());
        Assert.DoesNotContain("my plain password", line);
        AssertNoWorker(harness);
    }
}
