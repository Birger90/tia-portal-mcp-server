using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>
/// The technology object operations of plc_read/plc_write end to end against the scripted FakeWorker
/// (technology-objects, and technology-objects-silent, whose Create/Connect/Import do nothing). Like every FakeWorker
/// protocol test these launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class TechnologyObjectMcpTests
{
    private const string Scenario = "technology-objects";
    private const string SilentScenario = "technology-objects-silent";

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly FakeWorkerRequestLog _requests;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        private Session(string scenario)
        {
            Directory.CreateDirectory(_audit.Path);
            _requests = new FakeWorkerRequestLog(_audit.Path);
            _uiOpen = new FakeWorkerUiOpenProject(scenario);
        }

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public string[] Methods => _requests.Methods();

        public string AuditText => string.Join("\n", NetworkGuardedWriteMcpTests.AuditLines(_audit.Path));

        public static async Task<Session> StartAsync(McpAccessMode mode, string scenario = Scenario)
        {
            var session = new Session(scenario);
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, scenario);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            return (raw.StructuredContent!.Value, raw);
        }

        public async Task<JsonElement[]> ReadAsync(params object[] operations)
        {
            var (document, raw) = await CallAsync("plc_read", new() { ["operations"] = operations });
            Assert.False(raw.IsError == true, document.GetRawText());
            return document.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> WriteAsync(bool dryRun, params object[] operations)
            => await CallAsync("plc_write", new() { ["operations"] = operations, ["dryRun"] = dryRun });

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static string[] Guards(JsonElement document)
        => document.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    private static JsonElement Item(JsonElement document, int index) => document.GetProperty("batch").GetProperty("operations")[index];

    private static string? Category(JsonElement item) => item.GetProperty("failure").GetProperty("category").GetString();

    private static JsonElement Verification(JsonElement document, string operationId)
        => document.GetProperty("verification").GetProperty("operations").EnumerateArray().Single(v => v.GetProperty("operationId").GetString() == operationId);

    [Fact]
    public async Task ReadOnly_ListsObjectsReadsTypedParametersConnectionsAndContent()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var items = await session.ReadAsync(
            new { operationId = "list", operation = "list_technology_objects" },
            new { operationId = "axes", operation = "list_technology_objects", plcName = "PLC_1", folderPath = "/Axes" },
            new { operationId = "page", operation = "get_technology_object_parameters", name = "axis_1", prefix = "Dynamics.", limit = 1 },
            new { operationId = "one", operation = "get_technology_object_parameters", name = "Axis_1", parameterName = "Actor.Type" },
            new { operationId = "hw", operation = "get_technology_object_hardware_connection", name = "Axis_1" },
            new { operationId = "xml", operation = "get_technology_object_content", name = "Axis_1" },
            new { operationId = "missing", operation = "get_technology_object_parameters", name = "Axis_9" });

        Assert.All(items.Take(6), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        var plc = items[0].GetProperty("result").GetProperty("plcs")[0];
        Assert.Equal(new[] { "Axis_1", "Axis_2", "Enc_1", "Ident_1", "MeasIn_1" }, plc.GetProperty("technologyObjects").EnumerateArray().Select(o => o.GetProperty("name").GetString()).Order());
        Assert.Equal(new[] { "/Axes", "/Axes/Line1" }, plc.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("path").GetString()));
        var axis = plc.GetProperty("technologyObjects").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "Axis_1");
        Assert.Equal("TO_PositioningAxis", axis.GetProperty("systemLibElement").GetString());
        Assert.Equal("10.0", axis.GetProperty("systemLibVersion").GetString());
        Assert.Equal(2, axis.GetProperty("number").GetInt32());
        Assert.Equal(new[] { "Axis_2" }, items[1].GetProperty("result").GetProperty("plcs")[0].GetProperty("technologyObjects").EnumerateArray().Select(o => o.GetProperty("name").GetString()));

        var page = items[2].GetProperty("result");
        Assert.Equal(2, page.GetProperty("matchedCount").GetInt32());
        Assert.Equal(1, page.GetProperty("nextOffset").GetInt32());
        var velocity = Assert.Single(page.GetProperty("parameters").EnumerateArray());
        Assert.Equal("Dynamics.Velocity", velocity.GetProperty("name").GetString());
        Assert.Equal("real", velocity.GetProperty("valueKind").GetString());
        Assert.Equal("System.Double", velocity.GetProperty("valueType").GetString());
        Assert.Equal("100", velocity.GetProperty("value").GetString());

        var actorType = Assert.Single(items[3].GetProperty("result").GetProperty("parameters").EnumerateArray());
        Assert.Equal("enum", actorType.GetProperty("valueKind").GetString());
        Assert.Equal("Drive", actorType.GetProperty("value").GetString());
        Assert.Equal(new[] { "Analog", "Drive", "Stepper" }, actorType.GetProperty("enumMembers").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(JsonValueKind.Null, items[3].GetProperty("result").GetProperty("totalCount").ValueKind);

        var interfaces = items[4].GetProperty("result").GetProperty("interfaces").EnumerateArray().ToArray();
        Assert.Equal(new[] { "actor", "sensor", "sensor", "torque" }, interfaces.Select(i => i.GetProperty("interface").GetString()));
        Assert.Equal(new int?[] { null, 0, 1, null }, interfaces.Select(i => i.GetProperty("index").ValueKind == JsonValueKind.Null ? (int?)null : i.GetProperty("index").GetInt32()));
        Assert.All(interfaces, i => Assert.False(i.GetProperty("isConnected").GetBoolean()));
        // K18: the listed encoder provider that GetService does not return is reported, the read does not fail.
        Assert.Equal(new[] { ListedServices.UnavailableReason(TechnologyObjectPolicy.SiemensNames.EncoderProvider) },
            items[4].GetProperty("result").GetProperty("unavailableServices").EnumerateArray().Select(r => r.GetString()));

        var xml = items[5].GetProperty("result");
        Assert.Equal("xml", xml.GetProperty("format").GetString());
        Assert.Equal(ContentHashRules.Compute("xml", xml.GetProperty("content").GetString()!), xml.GetProperty("contentHash").GetString());

        Assert.Equal("failed", items[6].GetProperty("status").GetString());
        Assert.Equal("target_not_found", items[6].GetProperty("failure").GetProperty("category").GetString());
    }

    /// <summary>One valid item per technology object write, so read-only can only refuse it for the access mode.</summary>
    private static PlcOperationRequest ValidWrite(string operation)
    {
        var item = new PlcOperationRequest { OperationId = "w", Operation = operation, Name = "Axis_1" };
        switch (operation)
        {
            case TechnologyObjectMethods.Create: (item.TechnologyType, item.Version, item.Name) = ("TO_PositioningAxis", "10.0", "Axis_8"); break;
            case TechnologyObjectMethods.Rename: item.NewName = "Axis_8"; break;
            case TechnologyObjectMethods.CreateGroup: (item.FolderPath, item.Name) = ("/Axes/New", null); break;
            case TechnologyObjectMethods.DeleteGroup: (item.FolderPath, item.Name) = ("/Axes", null); break;
            case TechnologyObjectMethods.RenameGroup: (item.FolderPath, item.NewName, item.Name) = ("/Axes", "Drives", null); break;
            case TechnologyObjectMethods.SetParameter: (item.ParameterName, item.Value) = ("Dynamics.Velocity", "1"); break;
            case TechnologyObjectMethods.ConnectHardware: (item.ConnectionInterface, item.ConnectionKind, item.DbMemberPath) = ("actor", "dbMember", "\"D\".T"); break;
            case TechnologyObjectMethods.DisconnectHardware: item.ConnectionInterface = "actor"; break;
            case TechnologyObjectMethods.Export: item.FilePath = @"C:\Exports\New.xml"; break;
            case TechnologyObjectMethods.Import: (item.FilePath, item.Name) = (@"C:\Imports\Axis_3.xml", null); break;
            case TechnologyObjectMethods.UpdateContent: (item.Content, item.ExpectedContentHash) = ("<Document />", "xml:sha256:" + new string('0', 64)); break;
        }

        return item;
    }

    [Fact]
    public async Task ReadOnly_RefusesEveryTechnologyObjectWriteIncludingExport()
    {
        await using (var session = await Session.StartAsync(McpAccessMode.ReadOnly))
        {
            var tools = (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();
            Assert.DoesNotContain("plc_write", tools);
            // plc_read does not run a write either: an export item is not one of its operations.
            var (document, _) = await session.CallAsync("plc_read", new()
            {
                ["operations"] = new object[] { new { operationId = "x", operation = "export_technology_object", name = "Axis_1", filePath = @"C:\Exports\New.xml" } },
            });
            Assert.Contains("validation_error", document.GetRawText());
            Assert.DoesNotContain(session.Methods, TechnologyObjectMethods.IsWorkerMethod);
        }

        // The write domain itself denies every write, export included, before any worker call.
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Scenario, McpAccessMode.ReadOnly);
        foreach (var operation in TechnologyObjectMethods.Write)
        {
            var response = await fixture.RunAsync(false, ValidWrite(operation));
            Assert.True(response.Error?.Category == WorkerFailureCategories.AccessDenied, $"{operation}: {response.Error?.Category} {response.Error?.Message}");
        }

        Assert.Equal(12, TechnologyObjectMethods.Write.Count);
        Assert.DoesNotContain(requests.Methods(), TechnologyObjectMethods.IsWorkerMethod);
    }

    [Fact]
    public async Task ReadWrite_RunsTheSameValidItemsPastTheAccessCheck()
    {
        // The items above are valid: in read-write the same export item reaches planning (and runs).
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(true,
            new { operationId = "e", operation = "export_technology_object", name = "Axis_1", filePath = @"C:\Exports\New.xml" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.Equal(@"C:\Exports\New.xml", document.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0].GetProperty("requested").GetString());
    }

    [Fact]
    public async Task DryRun_PlansCreateAndDependentParameterWithoutMutating()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(true,
            new { operationId = "create", operation = "create_technology_object", name = "Axis_9", technologyType = "TO_PositioningAxis", version = "10.0", folderPath = "/Axes" },
            new { operationId = "speed", operation = "set_technology_object_parameter", name = "Axis_9", parameterName = "Dynamics.Velocity", value = "250.5" },
            new { operationId = "actor", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Actor.InverseDirection", value = "true" });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        var effects = document.GetProperty("effects").EnumerateArray().Select(e => e.GetProperty("effect")).ToArray();
        Assert.Equal("TechnologyObject", effects[0].GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal("/Axes", effects[0].GetProperty("placement").GetString());
        Assert.Equal("create", effects[1].GetProperty("dependsOn").GetString());
        var change = Assert.Single(effects[2].GetProperty("changes").EnumerateArray());
        Assert.Equal("boolean false", change.GetProperty("current").GetString());
        Assert.Equal("true", change.GetProperty("requested").GetString());
        Assert.Contains("plc_technology_parameter_not_hardware_connection", Guards(document));
        Assert.DoesNotContain(session.Methods, m => m is "create_technology_object" or "set_technology_object_parameter");
    }

    [Fact]
    public async Task Full_CreatesSetsParametersAndVerifiesByReadingBack()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "create", operation = "create_technology_object", name = "Axis_9", technologyType = "TO_PositioningAxis", version = "10.0", folderPath = "/Axes" },
            new { operationId = "speed", operation = "set_technology_object_parameter", name = "Axis_9", parameterName = "Dynamics.Velocity", value = "250.5" },
            new { operationId = "enum", operation = "set_technology_object_parameter", name = "Axis_9", parameterName = "Actor.Type", value = "Stepper" },
            new { operationId = "tag", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "_PositionLimits_HW.MinSwitchAddress", tableName = "axis tags", tagName = "HwLimitMin" },
            new { operationId = "unit", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Units.LengthUnit", value = "1010" });

        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.True(document.GetProperty("verification").GetProperty("success").GetBoolean());
        var created = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("technologyObject");
        Assert.Equal("/Axes", created.GetProperty("groupPath").GetString());
        Assert.Equal("technologyObject", Verification(document, "create").GetProperty("check").GetString());
        var speed = Verification(document, "speed");
        Assert.Equal("technologyObjectParameter", speed.GetProperty("check").GetString());
        Assert.Equal("real 250.5", speed.GetProperty("expected").GetString());
        Assert.Equal("real 250.5", speed.GetProperty("actual").GetString());
        Assert.Equal("enum Stepper", Verification(document, "enum").GetProperty("actual").GetString());
        Assert.Equal("plcTag HwLimitMin", Verification(document, "tag").GetProperty("actual").GetString());

        var items = await session.ReadAsync(new { operationId = "p", operation = "get_technology_object_parameters", name = "Axis_9", parameterName = "Dynamics.Velocity" });
        Assert.Equal("250.5", items[0].GetProperty("result").GetProperty("parameters")[0].GetProperty("value").GetString());
        Assert.Contains("create_technology_object", session.Methods);
    }

    [Fact]
    public async Task Planning_RefusesValuesThatDoNotConvertToTheCurrentType()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (range, _) = await session.WriteAsync(false,
            new { operationId = "range", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Units.LengthUnit", value = "70000" });
        Assert.Equal("validation_error", range.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("System.UInt16", range.GetProperty("error").GetProperty("message").GetString());

        var (member, _) = await session.WriteAsync(false,
            new { operationId = "member", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Actor.Type", value = "Servo" });
        Assert.Contains("Analog, Drive, Stepper", member.GetProperty("error").GetProperty("message").GetString());

        var (tag, _) = await session.WriteAsync(false,
            new { operationId = "tag", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Dynamics.Velocity", tableName = "Axis tags", tagName = "HwLimitMin" });
        Assert.Contains("not a PlcTag", tag.GetProperty("error").GetProperty("message").GetString());

        var (unreadable, _) = await session.WriteAsync(false,
            new { operationId = "u", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Simulation.Unreadable", value = "1" });
        Assert.Contains("unreadable", unreadable.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("set_technology_object_parameter", session.Methods);
    }

    [Fact]
    public async Task SilentlyIgnoredParameterWriteFailsPostconditionAndReportsTheCurrentValue()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "ignored", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Simulation.Ignored", value = "2" });

        Assert.False(raw.IsError == true);
        Assert.False(document.GetProperty("success").GetBoolean());
        var item = document.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("postcondition_failed", item.GetProperty("failure").GetProperty("category").GetString());
        var check = Verification(document, "ignored");
        Assert.Equal("real 1", check.GetProperty("actual").GetString());
    }

    [Fact]
    public async Task ConnectAndDisconnectAreVerifiedAndAConnectedInterfaceFiresTheReplaceGuard()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (connect, _) = await session.WriteAsync(false,
            new { operationId = "actor", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "actor", connectionKind = "address", inputAddress = 2048, outputAddress = 2048 },
            new { operationId = "sensor", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "sensor", interfaceIndex = 1, connectionKind = "module", modulePath = new[] { "Drive_1", "Module_1" } });
        Assert.True(connect.GetProperty("success").GetBoolean(), connect.GetRawText());
        // The read-back names the requested target in the documented properties, not only IsConnected.
        var actor = Verification(connect, "actor");
        Assert.Equal("connected; InputAddress=2048; OutputAddress=2048; ConnectOption=Default", actor.GetProperty("expected").GetString());
        Assert.Equal("connected; InputAddress=2048; OutputAddress=2048; ConnectOption=Default", actor.GetProperty("actual").GetString());
        Assert.Equal("connected; InputOutputModule=Drive_1/Module_1", Verification(connect, "sensor").GetProperty("actual").GetString());
        Assert.Equal("technologyObjectConnection", Verification(connect, "sensor").GetProperty("check").GetString());
        var result = Item(connect, 0).GetProperty("result");
        Assert.False(result.GetProperty("previousInterface").GetProperty("isConnected").GetBoolean());
        Assert.True(result.GetProperty("interface").GetProperty("isConnected").GetBoolean());

        var (again, _) = await session.WriteAsync(true,
            new { operationId = "again", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "actor", connectionKind = "dbMember", dbMemberPath = "\"DriveData\".Telegram" });
        Assert.Contains("plc_replaces_hardware_connection", Guards(again));

        var (ambiguous, _) = await session.WriteAsync(false,
            new { operationId = "sensors", operation = "disconnect_technology_object_hardware", name = "Axis_1", connectionInterface = "sensor" });
        Assert.Equal("target_ambiguous", ambiguous.GetProperty("error").GetProperty("category").GetString());

        var (disconnect, _) = await session.WriteAsync(false,
            new { operationId = "off", operation = "disconnect_technology_object_hardware", name = "Axis_1", connectionInterface = "actor" });
        Assert.True(disconnect.GetProperty("success").GetBoolean(), disconnect.GetRawText());
        Assert.Equal("not connected", Verification(disconnect, "off").GetProperty("actual").GetString());

        var (missing, _) = await session.WriteAsync(false,
            new { operationId = "cam", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "outputCam", connectionKind = "address", outputAddress = 8 });
        Assert.Equal("target_kind_unsupported", missing.GetProperty("error").GetProperty("category").GetString());
    }

    /// <summary>
    /// K18 (live V21 2026-11-02, Achse_1 TO_PositioningAxis 6.0): an actor connect failed unsupported_capability because the
    /// listed but unavailable EncoderHardwareConnectionProvider was resolved although only AxisHardwareConnectionProvider
    /// serves the actor interface. Only needed providers are resolved; the sensor falls back to the encoder provider.
    /// </summary>
    [Fact]
    public async Task AConnectResolvesOnlyTheProvidersItsInterfaceNeeds()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (actor, _) = await session.WriteAsync(false,
            new { operationId = "actor", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "actor", connectionKind = "tag", tableName = "Axis tags", tagName = "HwLimitMin" },
            new { operationId = "sensor", operation = "connect_technology_object_hardware", name = "Enc_1", connectionInterface = "sensor", connectionKind = "dbMember", dbMemberPath = "\"Enc\".Value" });
        Assert.True(actor.GetProperty("success").GetBoolean(), actor.GetRawText());
        Assert.Equal("technologyObjectConnection", Verification(actor, "actor").GetProperty("check").GetString());
        Assert.Equal(TechnologyObjectPolicy.SiemensNames.EncoderProvider,
            Item(actor, 1).GetProperty("result").GetProperty("interface").GetProperty("provider").GetString());

        // The axis provider Enc_1 lists is unavailable: an actor connect names that reason, once "Nothing was changed.".
        var (unavailable, _) = await session.WriteAsync(false,
            new { operationId = "encActor", operation = "connect_technology_object_hardware", name = "Enc_1", connectionInterface = "actor", connectionKind = "dbMember", dbMemberPath = "\"Enc\".Value" });
        var error = unavailable.GetProperty("error");
        Assert.Equal("unsupported_capability", error.GetProperty("category").GetString());
        var message = error.GetProperty("message").GetString()!;
        Assert.Contains(ListedServices.UnavailableReason(TechnologyObjectPolicy.SiemensNames.AxisProvider), message);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(message, "Nothing was changed\\.").Count);
    }

    /// <summary>
    /// K19 (live V21 2026-11-02): export, delete and import of the same object into another folder in one call. TO names are
    /// unique per PLC, so the import that re-creates the name supersedes the delete regardless of folder.
    /// </summary>
    [Fact]
    public async Task AnImportReCreatingTheNameInAnotherFolderSupersedesTheEarlierDelete()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (moved, _) = await session.WriteAsync(false,
            new { operationId = "export", operation = "export_technology_object", name = "Axis_2", filePath = @"C:\Exports\Axis_2.xml" },
            new { operationId = "delete", operation = "delete_technology_object", name = "Axis_2" },
            new { operationId = "import", operation = "import_technology_object", filePath = @"C:\Exports\Axis_2.xml", folderPath = "/Axes/Line1" });
        Assert.True(moved.GetProperty("success").GetBoolean(), moved.GetRawText());
        var delete = Verification(moved, "delete");
        Assert.Equal("superseded", delete.GetProperty("check").GetString());
        Assert.Contains("'import'", delete.GetProperty("message").GetString());
        Assert.Equal("/Axes/Line1/Axis_2 exists", Verification(moved, "import").GetProperty("actual").GetString());

        // A later delete of the imported name supersedes the import in turn.
        var (again, _) = await session.WriteAsync(false,
            new { operationId = "import2", operation = "import_technology_object", filePath = @"C:\Exports\Axis_2.xml", folderPath = "/Axes", overwrite = true },
            new { operationId = "delete2", operation = "delete_technology_object", name = "Axis_2" });
        Assert.True(again.GetProperty("success").GetBoolean(), again.GetRawText());
        Assert.Equal("superseded", Verification(again, "import2").GetProperty("check").GetString());
        Assert.Equal("Axis_2 absent", Verification(again, "delete2").GetProperty("actual").GetString());
    }

    /// <summary>
    /// K59 (live V21 2026-11-02: create_technology_object, then export_technology_object of the same name in one call; the
    /// create's verification said "superseded by later operation 'e'"): an export writes a file and changes no project object,
    /// so it never supersedes an item on the exported object, and a later change of the object never supersedes the export.
    /// </summary>
    [Fact]
    public async Task AnExportNeitherSupersedesNorIsSupersededByItemsOnTheExportedObject()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(false,
            new { operationId = "create", operation = "create_technology_object", name = "Axis_9", technologyType = "TO_PositioningAxis", version = "10.0", folderPath = "/Axes" },
            new { operationId = "e", operation = "export_technology_object", name = "Axis_9", filePath = @"C:\Exports\Axis_9.xml" },
            new { operationId = "delete", operation = "delete_technology_object", name = "Axis_9" },
            new { operationId = "e2", operation = "export_technology_object", name = "Axis_1", filePath = @"C:\Exports\Axis_9.xml", overwrite = true });

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        // The delete (not the export) supersedes the create; the export is superseded only by the later write of its file.
        Assert.Contains("'delete'", Verification(document, "create").GetProperty("message").GetString());
        var export = Verification(document, "e");
        Assert.Equal("superseded", export.GetProperty("check").GetString());
        Assert.Contains("'e2'", export.GetProperty("message").GetString());
        Assert.Equal("Axis_9 absent", Verification(document, "delete").GetProperty("actual").GetString());

        var (alone, _) = await session.WriteAsync(false,
            new { operationId = "create", operation = "create_technology_object", name = "Axis_8", technologyType = "TO_PositioningAxis", version = "10.0", folderPath = "/Axes" },
            new { operationId = "e", operation = "export_technology_object", name = "Axis_8", filePath = @"C:\Exports\Axis_8.xml" });
        Assert.True(alone.GetProperty("success").GetBoolean(), alone.GetRawText());
        Assert.Equal("technologyObject", Verification(alone, "create").GetProperty("check").GetString());
        Assert.Equal("trusted", Verification(alone, "e").GetProperty("check").GetString());
    }

    [Fact]
    public async Task ExportImportAndContentRoundTrip()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (exists, _) = await session.WriteAsync(false,
            new { operationId = "exists", operation = "export_technology_object", name = "Axis_1", filePath = @"C:\Exports\Existing.xml" });
        Assert.False(exists.GetProperty("success").GetBoolean());
        Assert.Equal("validation_error", exists.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());

        var (export, _) = await session.WriteAsync(false,
            new { operationId = "export", operation = "export_technology_object", name = "Axis_1", filePath = @"C:\Exports\Existing.xml", overwrite = true },
            new { operationId = "import", operation = "import_technology_object", filePath = @"C:\Imports\Axis_3.xml", folderPath = "/Axes/Line1" },
            new { operationId = "after", operation = "set_technology_object_parameter", name = "Axis_3", parameterName = "PositionLimits_HW.Active", value = "TRUE" });
        Assert.True(export.GetProperty("success").GetBoolean(), export.GetRawText());
        var exported = export.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(64, exported.GetProperty("fileSha256").GetString()!.Length);
        Assert.Equal("trusted", Verification(export, "export").GetProperty("check").GetString());
        Assert.Equal("/Axes/Line1/Axis_3 exists", Verification(export, "import").GetProperty("actual").GetString());
        Assert.Equal("import", export.GetProperty("effects")[2].GetProperty("effect").GetProperty("dependsOn").GetString());

        var read = await session.ReadAsync(new { operationId = "xml", operation = "get_technology_object_content", name = "Axis_1" });
        var content = read[0].GetProperty("result");
        var edited = content.GetProperty("content").GetString()!.Replace("<Revision>0</Revision>", "<Revision>7</Revision>", StringComparison.Ordinal);
        var (update, _) = await session.WriteAsync(false,
            new { operationId = "update", operation = "update_technology_object_content", name = "Axis_1", content = edited, expectedContentHash = content.GetProperty("contentHash").GetString() });
        Assert.True(update.GetProperty("success").GetBoolean(), update.GetRawText());
        Assert.Contains("plc_technology_object_import_replaces", Guards(update));
        Assert.Equal("contentHash", Verification(update, "update").GetProperty("check").GetString());

        var (stale, _) = await session.WriteAsync(false,
            new { operationId = "stale", operation = "update_technology_object_content", name = "Axis_1", content = edited, expectedContentHash = content.GetProperty("contentHash").GetString() });
        Assert.Equal("state_changed", stale.GetProperty("error").GetProperty("category").GetString());
    }

    [Fact]
    public async Task GroupsRenameAndDeleteWithContentsGuardAndUnknownVersionIsRejected()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (groups, _) = await session.WriteAsync(false,
            new { operationId = "group", operation = "create_technology_object_group", folderPath = "/Axes/Line2" },
            new { operationId = "rename", operation = "rename_technology_object_group", folderPath = "/Axes/Line2", newName = "Line3" },
            new { operationId = "to", operation = "rename_technology_object", name = "Axis_2", newName = "Spindle" },
            new { operationId = "delete", operation = "delete_technology_object_group", folderPath = "/Axes" });
        Assert.True(groups.GetProperty("success").GetBoolean(), groups.GetRawText());
        Assert.Contains("plc_deletes_technology_object_group_contents", Guards(groups));
        // The in-call working state sees the earlier rename and the created/renamed group.
        var removes = groups.GetProperty("effects")[3].GetProperty("effect").GetProperty("removes");
        Assert.Equal(new[] { "/Axes/Spindle" }, removes.GetProperty("blocks").EnumerateArray().Select(b => b.GetString()));
        Assert.Equal(new[] { "/Axes/Line1", "/Axes/Line3" }, removes.GetProperty("groups").EnumerateArray().Select(b => b.GetString()).Order());
        Assert.Equal("group", groups.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.All(new[] { "group", "rename", "to" }, id => Assert.Equal("superseded", Verification(groups, id).GetProperty("check").GetString()));
        var deleted = Verification(groups, "delete");
        Assert.Equal("technologyObjectGroup", deleted.GetProperty("check").GetString());
        Assert.Equal("/Axes absent", deleted.GetProperty("actual").GetString());

        var (rejected, _) = await session.WriteAsync(false,
            new { operationId = "bad", operation = "create_technology_object", name = "Axis_5", technologyType = "TO_PositioningAxis", version = "9.0" });
        var failure = rejected.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("engineering_rejected", failure.GetProperty("category").GetString());
        Assert.Contains("does not exist or is not a valid technology object", failure.GetProperty("message").GetString());
        Assert.Contains("create_technology_object", session.AuditText);
    }

    [Fact]
    public async Task ShapeErrorsAreRejectedBeforeAnyWorkerCall()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        // UNC shares are allowed since 2026-11-01; a device path stays a shape error.
        var (device, raw) = await session.WriteAsync(false,
            new { operationId = "dev", operation = "export_technology_object", name = "Axis_1", filePath = @"\\?\C:\Exports\Axis_1.xml" });
        Assert.True(raw.IsError == true);
        Assert.Contains("normalization", device.GetProperty("error").GetProperty("message").GetString());

        var (torque, _) = await session.WriteAsync(false,
            new { operationId = "torque", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "torque", connectionKind = "tag", tableName = "Axis tags", tagName = "HwLimitMin" });
        Assert.Contains("no documented Connect overload", torque.GetProperty("error").GetProperty("message").GetString());

        var (both, _) = await session.WriteAsync(false,
            new { operationId = "both", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Dynamics.Velocity", value = "1", tableName = "Axis tags", tagName = "HwLimitMin" });
        Assert.Contains("exactly one of value", both.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(session.Methods, m => m.Contains("technology_object", StringComparison.Ordinal) && !m.StartsWith("list_", StringComparison.Ordinal) && !m.StartsWith("get_", StringComparison.Ordinal));

        // A value over the text bound is refused by the catalog, naming the bound, before any worker call (not even a planning read).
        var before = session.Methods.Length;
        var (longValue, longRaw) = await session.WriteAsync(false,
            new { operationId = "long", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Axis.Name", value = new string('x', TechnologyObjectPolicy.MaxTextChars + 1) });
        Assert.True(longRaw.IsError == true);
        Assert.Equal("validation_error", longValue.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("'value' has 4001 characters; at most 4000 are accepted.", longValue.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(before, session.Methods.Length);
    }

    [Fact]
    public async Task Ident_StateIsTheConnectedIdentDeviceAndTheConnectIsVerifiedByIt()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (preview, _) = await session.WriteAsync(true,
            new { operationId = "ident", operation = "connect_technology_object_hardware", name = "Ident_1", connectionInterface = "ident", connectionKind = "module", modulePath = new[] { "Reader_1", "Head_1" } });
        Assert.DoesNotContain("plc_replaces_hardware_connection", Guards(preview));
        Assert.Equal("not connected", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0].GetProperty("current").GetString());

        var (connect, _) = await session.WriteAsync(false,
            new { operationId = "ident", operation = "connect_technology_object_hardware", name = "Ident_1", connectionInterface = "ident", connectionKind = "module", modulePath = new[] { "Reader_1", "Head_1" } });
        Assert.True(connect.GetProperty("success").GetBoolean(), connect.GetRawText());
        Assert.Equal("connected; ConnectedIdentDevice=Reader_1/Head_1", Verification(connect, "ident").GetProperty("actual").GetString());
        Assert.Equal(JsonValueKind.Null, Item(connect, 0).GetProperty("result").GetProperty("interface").GetProperty("isConnected").ValueKind);

        var (again, _) = await session.WriteAsync(true,
            new { operationId = "again", operation = "connect_technology_object_hardware", name = "Ident_1", connectionInterface = "ident", connectionKind = "module", modulePath = new[] { "Reader_1", "Head_1" } });
        Assert.Contains("plc_replaces_hardware_connection", Guards(again));
        Assert.Equal("connected", again.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0].GetProperty("current").GetString());
    }

    [Fact]
    public async Task Silent_CreatesConnectsAndImportsThatChangeNothingFailPostcondition()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, SilentScenario);
        var (create, _) = await session.WriteAsync(false,
            new { operationId = "create", operation = "create_technology_object", name = "Axis_9", technologyType = "TO_PositioningAxis", version = "10.0" });
        Assert.False(create.GetProperty("success").GetBoolean());
        Assert.Equal("postcondition_failed", Category(Item(create, 0)));

        var (group, _) = await session.WriteAsync(false, new { operationId = "group", operation = "create_technology_object_group", folderPath = "/Drives" });
        Assert.Equal("postcondition_failed", Category(Item(group, 0)));

        var (connect, _) = await session.WriteAsync(false,
            new { operationId = "actor", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "actor", connectionKind = "address", inputAddress = 64, outputAddress = 64 });
        var failure = Item(connect, 0).GetProperty("failure");
        Assert.Equal("postcondition_failed", failure.GetProperty("category").GetString());
        Assert.Contains("IsConnected=false", failure.GetProperty("message").GetString());

        var (import, _) = await session.WriteAsync(false,
            new { operationId = "import", operation = "import_technology_object", filePath = @"C:\Imports\Axis_3.xml" });
        Assert.Equal("postcondition_failed", Category(Item(import, 0)));
        Assert.Contains("connect_technology_object_hardware", session.Methods);
    }

    [Fact]
    public async Task ConnectReadingBackAnotherTargetFailsPostcondition()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        // The fixture connects "Drive".Mangled to "Drive".Other: IsConnected turns true, but the target differs.
        var (connect, _) = await session.WriteAsync(false,
            new { operationId = "torque", operation = "connect_technology_object_hardware", name = "Axis_1", connectionInterface = "torque", connectionKind = "dbMember", dbMemberPath = "\"Drive\".Mangled" });
        var failure = Item(connect, 0).GetProperty("failure");
        Assert.Equal("postcondition_failed", failure.GetProperty("category").GetString());
        Assert.Contains("PathToDBMember=\"Drive\".Other instead of PathToDBMember=\"Drive\".Mangled", failure.GetProperty("message").GetString());
        // The failed item carries the interface before and after the call as structured data.
        var evidence = Item(connect, 0).GetProperty("result");
        Assert.False(evidence.GetProperty("previousInterface").GetProperty("isConnected").GetBoolean());
        Assert.True(evidence.GetProperty("interface").GetProperty("isConnected").GetBoolean());
        Assert.Contains(evidence.GetProperty("interface").GetProperty("properties").EnumerateArray(),
            p => p.GetProperty("name").GetString() == "PathToDBMember" && p.GetProperty("value").GetString() == "\"Drive\".Other");
    }

    [Fact]
    public async Task CreatesOfExistingNamesAreRefusedBeforeAnyWorkerCall()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (create, raw) = await session.WriteAsync(false,
            new { operationId = "dup", operation = "create_technology_object", name = "axis_2", technologyType = "TO_PositioningAxis", version = "10.0" });
        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", create.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("already exists in group '/Axes'", create.GetProperty("error").GetProperty("message").GetString());

        var (group, _) = await session.WriteAsync(false, new { operationId = "dupGroup", operation = "create_technology_object_group", folderPath = "/axes/line1" });
        Assert.Equal("validation_error", group.GetProperty("error").GetProperty("category").GetString());

        // A name deleted earlier in the same call may be created again.
        var (recreate, _) = await session.WriteAsync(true,
            new { operationId = "del", operation = "delete_technology_object", name = "Axis_2" },
            new { operationId = "again", operation = "create_technology_object", name = "Axis_2", technologyType = "TO_SpeedAxis", version = "7.0" });
        Assert.Equal("preview", recreate.GetProperty("phase").GetString());
        Assert.DoesNotContain(session.Methods, m => m is "create_technology_object" or "create_technology_object_group" or "delete_technology_object");
    }

    [Fact]
    public async Task AnImportThatShowsNothingNewFailsOrIsNotVerifiable()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (plain, _) = await session.WriteAsync(false, new { operationId = "import", operation = "import_technology_object", filePath = @"C:\Imports\Nothing.xml" });
        Assert.Equal("postcondition_failed", Category(Item(plain, 0)));

        // With overwrite an unchanged name list may be a replacement: the item completes, but nothing can be re-read.
        var (overwrite, _) = await session.WriteAsync(false, new { operationId = "import", operation = "import_technology_object", filePath = @"C:\Imports\Nothing.xml", overwrite = true });
        Assert.Equal("succeeded", Item(overwrite, 0).GetProperty("status").GetString());
        var check = Verification(overwrite, "import");
        Assert.False(check.GetProperty("success").GetBoolean());
        Assert.Contains("not verifiable", check.GetProperty("message").GetString());
        Assert.False(overwrite.GetProperty("verification").GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task AnUnreadablePlanningRead_InformsWithStateUnverifiable_AndTiaPortalDecides()
    {
        // Owner decision: never stricter than TIA Portal; the item runs and TIA Portal's answer is the item's result.
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(false,
            new { operationId = "p", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Simulation.Throws", value = "1" });
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.Contains("plc_state_unverifiable", Guards(document));
        Assert.Contains("set_technology_object_parameter", session.Methods);
    }

    [Fact]
    public async Task ParameterValuesAreWrittenExactlyFlagsAndUnlistedMembersIncluded()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        // Real exactness is the worker's decision (the host checks syntax only): refused at the item, naming the shortest text.
        var (precision, _) = await session.WriteAsync(false,
            new { operationId = "lead", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Mechanics.LeadScrew", value = "16777217" });
        Assert.Equal("validation_error", Category(Item(precision, 0)));
        Assert.Contains("nearest representable value is 16777216", Item(precision, 0).GetProperty("failure").GetProperty("message").GetString());
        var (syntax, _) = await session.WriteAsync(false,
            new { operationId = "lead", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Mechanics.LeadScrew", value = "1,5" });
        Assert.Equal("validation_error", syntax.GetProperty("error").GetProperty("category").GetString());
        var (maximum, _) = await session.WriteAsync(false,
            new { operationId = "max", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Mechanics.LeadScrew", value = "3.4028235E+38" },
            new { operationId = "zero", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Dynamics.Acceleration", value = "-0" });
        Assert.True(maximum.GetProperty("success").GetBoolean(), maximum.GetRawText());
        Assert.Equal("real 3.4028235E+38", Verification(maximum, "max").GetProperty("actual").GetString());
        Assert.Equal("real -0", Verification(maximum, "zero").GetProperty("actual").GetString());

        var (document, _) = await session.WriteAsync(false,
            new { operationId = "flags", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Axis.Flags", value = "Left, Right" },
            new { operationId = "code", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Axis.Code", value = "Code204" },
            new { operationId = "precise", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Axis.Precise", value = "2.25" });
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal("enum Left, Right", Verification(document, "flags").GetProperty("actual").GetString());
        Assert.Equal("enum Code204", Verification(document, "code").GetProperty("actual").GetString());

        var items = await session.ReadAsync(new { operationId = "p", operation = "get_technology_object_parameters", name = "Axis_1", parameterName = "Axis.Code" });
        var code = items[0].GetProperty("result").GetProperty("parameters")[0];
        Assert.True(code.GetProperty("enumMembersTruncated").GetBoolean());
        Assert.Equal(TechnologyObjectPolicy.MaxEnumMembers, code.GetProperty("enumMembers").GetArrayLength());

        // An unlisted name the live enum does not have either is refused by the worker.
        var (bogus, _) = await session.WriteAsync(false,
            new { operationId = "bogus", operation = "set_technology_object_parameter", name = "Axis_1", parameterName = "Axis.Code", value = "Bogus" });
        Assert.Equal("validation_error", Category(Item(bogus, 0)));
    }

    [Fact]
    public async Task ATagCreatedEarlierInTheCallMakesTheParameterItemDependOnIt()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(true,
            new { operationId = "table", operation = "create_tag_table", plcName = "PLC_1", tableName = "Limits" },
            new { operationId = "tag", operation = "create_tag", plcName = "PLC_1", tableName = "Limits", name = "HwMin", dataType = "Bool", logicalAddress = "%I0.0" },
            new { operationId = "param", operation = "set_technology_object_parameter", plcName = "PLC_1", name = "Axis_1", parameterName = "_PositionLimits_HW.MinSwitchAddress", tableName = "Limits", tagName = "HwMin" });
        Assert.False(raw.IsError == true, document.GetRawText());
        Assert.Equal("tag", document.GetProperty("effects")[2].GetProperty("effect").GetProperty("dependsOn").GetString());
    }

    [Fact]
    public async Task ExportToANetworkDriveRunsWithAWarning()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(false,
            new { operationId = "export", operation = "export_technology_object", name = "Axis_1", filePath = "N:/Shared/Axis_1.xml" });
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Equal(@"N:\Shared\Axis_1.xml", Item(document, 0).GetProperty("result").GetProperty("filePath").GetString());
        Assert.Contains(@"is on a network drive (N:\\)", document.GetRawText());
    }
}
