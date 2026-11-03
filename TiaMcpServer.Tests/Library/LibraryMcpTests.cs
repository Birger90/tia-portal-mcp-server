using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>
/// library_read and library_write end to end against the scripted FakeWorker (library-roundtrip; object access
/// only, no Siemens evidence). Like every FakeWorker protocol test these launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class LibraryMcpTests
{
    private const string Scenario = "library-roundtrip";
    private const string PlantPath = @"C:\Libraries\Plant\Plant.al21";

    private sealed class Session : IAsyncDisposable
    {
        private readonly TempAuditDirectory _audit = new();
        private readonly FakeWorkerRequestLog _requests;
        private readonly FakeWorkerUiOpenProject _uiOpen;

        private Session()
        {
            Directory.CreateDirectory(_audit.Path);
            _requests = new FakeWorkerRequestLog(_audit.Path);
            _uiOpen = new FakeWorkerUiOpenProject(Scenario);
        }

        public string AuditText => string.Join("\n", NetworkGuardedWriteMcpTests.AuditLines(_audit.Path));

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public int Prompts { get; private set; }

        public string[] Methods => _requests.Methods();

        public static async Task<Session> StartAsync(McpAccessMode mode, string? elicitation = null)
        {
            var session = new Session();
            McpClientOptions? options = elicitation is null ? null : new()
            {
                Capabilities = new ClientCapabilities { Elicitation = new ElicitationCapability { Form = new FormElicitationCapability() } },
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = (_, _) =>
                    {
                        session.Prompts++;
                        return ValueTask.FromResult(new ElicitResult
                        {
                            Action = elicitation,
                            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) },
                        });
                    },
                },
            };
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, Scenario, clientOptions: options);
            return session;
        }

        public async Task<(JsonElement Document, CallToolResult Raw)> CallAsync(string tool, Dictionary<string, object?> arguments)
        {
            var raw = await Harness.Client.CallToolAsync(tool, arguments);
            Assert.Empty(StructuredContractInspector.FindViolations(raw));
            Assert.Equal(TiaMcpServer.Json.CanonicalJson.Serialize(raw.StructuredContent!.Value), Assert.Single(raw.Content.OfType<TextContentBlock>()).Text);
            return (raw.StructuredContent!.Value, raw);
        }

        public async Task<JsonElement[]> ReadAsync(params object[] operations)
        {
            var (document, raw) = await CallAsync("library_read", Args(("operations", operations)));
            Assert.False(raw.IsError == true, document.GetRawText());
            return document.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        }

        public Task<(JsonElement Document, CallToolResult Raw)> WriteAsync(bool? dryRun, params object[] operations)
        {
            var arguments = Args(("operations", operations));
            if (dryRun is not null) arguments["dryRun"] = dryRun;
            return CallAsync("library_write", arguments);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
        }
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static JsonElement Result(JsonElement item)
    {
        Assert.True(item.GetProperty("status").GetString() == "succeeded", item.GetRawText());
        return item.GetProperty("result");
    }

    private static string[] GuardIds(JsonElement root) => root.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task ReadOnly_DiscoversOnlyLibraryRead_AndReadsTheProjectLibrary()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var tools = (await session.Harness.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();
        Assert.Contains("library_read", tools);
        Assert.DoesNotContain("library_write", tools);

        var items = await session.ReadAsync(
            new { operationId = "libs", operation = "list_libraries" },
            new { operationId = "infos", operation = "list_global_library_infos" },
            new { operationId = "tree", operation = "get_library_tree", library = "project" },
            new { operationId = "type", operation = "get_type", library = "project", typePath = "/Motors/PumpControl" },
            new { operationId = "instances", operation = "find_type_instances", library = "project", typePath = "/Motors/PumpControl", version = "1.0.0" },
            new { operationId = "linked", operation = "get_instance_info", blockPath = "PumpControl" },
            new { operationId = "plain", operation = "get_instance_info", blockPath = "Main" });

        var libraries = Result(items[0]).GetProperty("libraries");
        Assert.Equal("project", Assert.Single(libraries.EnumerateArray()).GetProperty("library").GetString());
        // The scenario also lists a system library (K80); the user library comes first.
        var info = Assert.Single(Result(items[1]).GetProperty("infos").EnumerateArray(), entry => entry.GetProperty("libraryType").GetString() == "User");
        Assert.Equal((PlantPath, false), (info.GetProperty("path").GetString(), info.GetProperty("isOpen").GetBoolean()));
        var tree = Result(items[2]);
        Assert.Equal(new[] { "/MotorData", "/Motors", "/Motors/PumpControl", "/Motors/Legacy", "/Valve", "/Station" },
            tree.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("path").GetString()));
        Assert.True(tree.GetProperty("complete").GetBoolean());
        Assert.Equal("all", tree.GetProperty("masterCopyScope").GetString());
        var pump = tree.GetProperty("entries")[2];
        Assert.Equal("codeBlock", pump.GetProperty("typeKind").GetString());
        Assert.Equal(new[] { "Committed", "InWork" }, pump.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("state").GetString()));
        Assert.Equal("Siemens.Engineering.SW.Blocks.FB", tree.GetProperty("entries")[4].GetProperty("contentTypes")[0].GetString());
        Assert.Equal(2, Result(items[3]).GetProperty("versions").GetArrayLength());
        Assert.Equal("PumpControl", Result(items[4]).GetProperty("versions")[0].GetProperty("instances")[0].GetProperty("name").GetString());
        Assert.True(Result(items[5]).GetProperty("linked").GetBoolean());
        Assert.Equal("1.0.0", Result(items[5]).GetProperty("versionNumber").GetString());
        Assert.False(Result(items[6]).GetProperty("linked").GetBoolean());
        Assert.DoesNotContain(session.Methods, TiaMcpServer.Contracts.Library.LibraryMethods.IsWriteMethod);
    }

    [Fact]
    public async Task ReadOnly_PagesTheTree_AndAMissingSiemensMemberFailsClosed()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var items = await session.ReadAsync(
            new { operationId = "page", operation = "get_library_tree", library = "project", content = "types", offset = 1, limit = 2, includeVersions = false },
            new { operationId = "check", operation = "update_check", library = "project" });

        var page = Result(items[0]);
        Assert.Equal((4, 2), (page.GetProperty("total").GetInt32(), page.GetProperty("returned").GetInt32()));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("entries")[0].GetProperty("versions").ValueKind);
        Assert.Equal("failed", items[1].GetProperty("status").GetString());
        Assert.Equal("unsupported_capability", items[1].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task ReadWrite_ContentWritesRunWithoutPrompt_PreviewFirst_AndReadsSeeTheChange()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var rename = new { operationId = "rename", operation = "rename_master_copy", library = "project", masterCopyName = "Valve", newName = "Valve_V2" };
        var (preview, _) = await session.WriteAsync(true, rename);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        Assert.Equal("master copy 'Valve' -> 'Valve_V2'", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("target").GetString());
        Assert.DoesNotContain("library_rename_master_copy", session.Methods);

        var (applied, raw) = await session.WriteAsync(null, rename,
            new { operationId = "capture", operation = "create_master_copy", library = "project", source = new { kind = "block", path = "PLC_1/Main" } });
        Assert.False(raw.IsError == true);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(0, session.Prompts);
        Assert.Equal("Valve_V2", applied.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("nameAfter").GetString());
        // K61: the rename is read back by the worker (trusted); the captured master copy is read back in the project library's tree.
        Assert.Equal(new[] { ("trusted", true), ("masterCopy", true) }, applied.GetProperty("verification").GetProperty("operations").EnumerateArray()
            .Select(check => (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean())));

        var tree = Result((await session.ReadAsync(new { operationId = "tree", operation = "get_library_tree", library = "project", content = "masterCopies" }))[0]);
        Assert.Equal(new[] { "/Valve_V2", "/Station", "/Main" }, tree.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("path").GetString()));
        Assert.Contains("\"tool\":\"library_write\"", session.AuditText);
    }

    [Fact]
    public async Task ReadWrite_LifecycleItemPromptsOnce_AndDependentItemsReplanAfterTheOpen()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var (applied, _) = await session.WriteAsync(null,
            new { operationId = "open", operation = "open_global_library", path = PlantPath, openMode = "ReadWrite" },
            new { operationId = "copy", operation = "copy_master_copy", library = "project", masterCopyName = "Valve", targetLibrary = PlantPath },
            new { operationId = "save", operation = "save_global_library", library = PlantPath });

        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.Equal(1, session.Prompts);
        // The copy and the save depend on the open: they are planned again just before their own mutation.
        // K61: after the mutations the open is read back in list_libraries and the copy in the target library's tree.
        var library = session.Methods.Where(method => method.StartsWith("library_", StringComparison.Ordinal)).TakeLast(7);
        Assert.Equal(new[] { "library_open_global_library", "library_plan", "library_copy_master_copy", "library_plan", "library_save_global_library",
            "library_list_libraries", "library_get_library_tree" }, library);
        Assert.Equal(new[] { ("openLibraries", true), ("masterCopy", true), ("trusted", true) }, applied.GetProperty("verification").GetProperty("operations")
            .EnumerateArray().Select(check => (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean())));
        Assert.Equal("copy of master copy 'Valve' in " + PlantPath, applied.GetProperty("effects")[1].GetProperty("effect").GetProperty("target").GetString());
        var guards = GuardIds(applied);
        Assert.Contains("library_writes_files", guards);
        Assert.Contains("library_global_unsaved", guards);
        Assert.All(applied.GetProperty("guards").EnumerateArray(), guard => Assert.Equal("info", guard.GetProperty("severity").GetString()));

        var items = await session.ReadAsync(
            new { operationId = "libs", operation = "list_libraries" },
            new { operationId = "tree", operation = "get_library_tree", library = PlantPath });
        Assert.Contains(Result(items[0]).GetProperty("libraries").EnumerateArray(), library => library.GetProperty("library").GetString() == PlantPath);
        Assert.Contains(Result(items[1]).GetProperty("entries").EnumerateArray(), entry => entry.GetProperty("path").GetString() == "/Valve");

        var (closed, _) = await session.WriteAsync(null, new { operationId = "close", operation = "close_global_library", library = PlantPath });
        Assert.True(closed.GetProperty("success").GetBoolean(), closed.GetRawText());
        Assert.Equal(2, session.Prompts);
        Assert.Contains("library_close_unsaved", GuardIds(closed));
    }

    [Fact]
    public async Task ReadWrite_SystemLibrary_IsOpenedByItsGlobalLibraryInfo_ReadOnly_AndReadBack()
    {
        // K80 (live 2026-11-03): TIA Portal refuses a path for system and corporate libraries; the path from
        // list_global_library_infos still selects it, and the worker opens it with Open(GlobalLibraryInfo).
        const string systemPath = @"C:\Program Files\Siemens\Automation\Portal V21\Data\Lib\PrintingSysLib.as21";
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "accept");
        var infos = await session.ReadAsync(new { operationId = "infos", operation = "list_global_library_infos" });
        var info = Assert.Single(Result(infos[0]).GetProperty("infos").EnumerateArray(), entry => entry.GetProperty("libraryType").GetString() == "System");
        Assert.Equal(systemPath, info.GetProperty("path").GetString());

        var (readWrite, _) = await session.WriteAsync(null, new { operationId = "rw", operation = "open_global_library", path = systemPath, openMode = "ReadWrite" });
        Assert.False(readWrite.GetProperty("success").GetBoolean());
        Assert.Contains("openMode ReadWrite is not possible", readWrite.GetRawText());

        var (preview, _) = await session.WriteAsync(true, new { operationId = "open", operation = "open_global_library", path = systemPath });
        Assert.Contains("Open(GlobalLibraryInfo)", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("target").GetString());

        var (applied, _) = await session.WriteAsync(null, new { operationId = "open", operation = "open_global_library", path = systemPath });
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        var result = Result(applied.GetProperty("batch").GetProperty("operations")[0]);
        Assert.Equal("system", result.GetProperty("library").GetProperty("kind").GetString());
        Assert.Contains("GlobalLibraryComposition.Open(GlobalLibraryInfo)", result.GetProperty("note").GetString());
        var check = applied.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("openLibraries", true), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean()));

        var listed = await session.ReadAsync(new { operationId = "libs", operation = "list_libraries" });
        Assert.Contains(Result(listed[0]).GetProperty("libraries").EnumerateArray(), library => library.GetProperty("library").GetString() == systemPath);
    }

    [Fact]
    public async Task ReadWrite_DeclinedLifecyclePrompt_ChangesNothing()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, elicitation: "decline");
        var (blocked, raw) = await session.WriteAsync(null, new { operationId = "open", operation = "open_global_library", path = PlantPath });

        Assert.True(raw.IsError);
        Assert.Equal("blocked", blocked.GetProperty("phase").GetString());
        Assert.Equal("access_denied", blocked.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(1, session.Prompts);
        Assert.DoesNotContain("library_open_global_library", session.Methods);
    }

    [Fact]
    public async Task Full_RunsLifecycleWithoutPrompt_TypeVersionWorkflow()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, elicitation: "accept");
        var (opened, _) = await session.WriteAsync(null, new { operationId = "open", operation = "open_global_library", path = PlantPath });
        Assert.True(opened.GetProperty("success").GetBoolean(), opened.GetRawText());
        Assert.Equal(0, session.Prompts);

        var (edited, _) = await session.WriteAsync(null, new { operationId = "edit", operation = "edit_type_version", library = "project", typePath = "MotorData" });
        Assert.True(edited.GetProperty("success").GetBoolean(), edited.GetRawText());
        var inWork = edited.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("versionAfter");
        Assert.Equal(("1.0.1", "InWork"), (inWork.GetProperty("versionNumber").GetString(), inWork.GetProperty("state").GetString()));

        var (released, _) = await session.WriteAsync(null,
            new { operationId = "release", operation = "release_type_version", library = "project", typePath = "MotorData", version = "1.0.1",
                versionNumber = "1.1.0", author = "Automation", comment = "Validated release" },
            new { operationId = "default", operation = "set_default_type_version", library = "project", typePath = "MotorData", version = "1.1.0" });
        Assert.True(released.GetProperty("success").GetBoolean(), released.GetRawText());
        var type = Result((await session.ReadAsync(new { operationId = "type", operation = "get_type", library = "project", typePath = "MotorData" }))[0]);
        var versions = type.GetProperty("versions").EnumerateArray().ToArray();
        Assert.Equal(("1.1.0", "Committed", true), (versions[1].GetProperty("versionNumber").GetString(), versions[1].GetProperty("state").GetString(),
            versions[1].GetProperty("isDefault").GetBoolean()));
        Assert.False(versions[0].GetProperty("isDefault").GetBoolean());

        var (deleted, _) = await session.WriteAsync(null, new { operationId = "delete", operation = "delete_type_version", library = "project", typePath = "MotorData", version = "1.0.0" });
        Assert.True(deleted.GetProperty("success").GetBoolean(), deleted.GetRawText());
        Assert.Contains("library_deletes", GuardIds(deleted));
        Assert.Equal(0, session.Prompts);
    }

    [Fact]
    public async Task Full_AnUnreadablePostcondition_IsNotVerified_NeverTrusted()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full, elicitation: "accept");
        var (edited, _) = await session.WriteAsync(null, new { operationId = "edit", operation = "edit_type_version", library = "project", typePath = "MotorData" });
        Assert.True(edited.GetProperty("success").GetBoolean(), edited.GetRawText());
        Assert.Equal("trusted", edited.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());

        var (released, _) = await session.WriteAsync(null,
            new { operationId = "release", operation = "release_type_version", library = "project", typePath = "MotorData", version = "1.0.1",
                versionNumber = "1.1.0", comment = "unreadable-version-number" });
        // The call returned, so the item succeeded; its result could not be read back, so the call is not verified.
        var item = released.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.Contains("VersionNumber", item.GetProperty("result").GetProperty("unverified").GetString(), StringComparison.Ordinal);
        Assert.False(released.GetProperty("success").GetBoolean());
        var entry = released.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("release", "notVerified", false), (entry.GetProperty("operationId").GetString(), entry.GetProperty("check").GetString(), entry.GetProperty("success").GetBoolean()));
        Assert.Contains("could not be read back", entry.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(released.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Contains(released.GetProperty("warnings").EnumerateArray(), warning => warning.GetString() == WriteExecution.VerificationFailureMessage);
        // A completed item without the evidence keeps no unverified member.
        Assert.False(edited.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").TryGetProperty("unverified", out _));
    }

    [Fact]
    public async Task ReadWrite_PortalRejection_StopsTheCallWithEngineeringError()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var place = new { operationId = "place", operation = "instantiate_master_copy", library = "project", masterCopyName = "Valve",
            target = new { kind = "blockGroup", plcName = "PLC_1" } };
        var (first, _) = await session.WriteAsync(null, place);
        Assert.True(first.GetProperty("success").GetBoolean(), first.GetRawText());

        var (second, raw) = await session.WriteAsync(null, place with { },
            new { operationId = "later", operation = "rename_type", library = "project", typePath = "MotorData", newName = "MotorData2" });
        Assert.False(raw.IsError == true);
        Assert.False(second.GetProperty("success").GetBoolean());
        var operations = second.GetProperty("batch").GetProperty("operations");
        Assert.Equal("engineering_rejected", operations[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("EngineeringTargetInvocationException", operations[0].GetProperty("failure").GetProperty("engineeringError").GetProperty("type").GetString());
        Assert.Equal("skipped", operations[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Full_CloseWithUnsavedChangesOfThisServer_InformsAndCloses()
    {
        // Owner decision (never stricter than TIA Portal): TIA Portal lets a user close without saving; the guard names
        // the discarded changes and the close runs (read-write confirms a lifecycle item anyway).
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (changed, _) = await session.WriteAsync(null,
            new { operationId = "open", operation = "open_global_library", path = PlantPath, openMode = "ReadWrite" },
            new { operationId = "rename", operation = "rename_master_copy", library = PlantPath, masterCopyName = "Pump", newName = "Pump_V2" });
        Assert.True(changed.GetProperty("success").GetBoolean(), changed.GetRawText());

        var close = new { operationId = "close", operation = "close_global_library", library = PlantPath.ToLowerInvariant() };
        var (preview, _) = await session.WriteAsync(true, close);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var guard = Assert.Single(preview.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == "library_close_unsaved_changes");
        Assert.Equal("info", guard.GetProperty("severity").GetString());
        Assert.Contains("rename_master_copy (rename) in an earlier call", guard.GetProperty("message").GetString());

        var (closed, raw) = await session.WriteAsync(null, close);
        Assert.False(raw.IsError == true, closed.GetRawText());
        Assert.Equal("applied", closed.GetProperty("phase").GetString());
        Assert.Contains("library_close_global_library", session.Methods);
    }

    [Fact]
    public async Task Full_PortalConfirmations_DeclineFailsTheItem_AcceptWarns_AndAFailureKeepsTheEvidence()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        const string prompting = @"C:\Libraries\New\Prompting\Prompting.al21";
        var create = new { operationId = "create", operation = "create_global_library", targetDirectory = @"C:\Libraries\New", name = "Prompting" };
        var (created, _) = await session.WriteAsync(null, create);
        Assert.True(created.GetProperty("success").GetBoolean(), created.GetRawText());
        var createdResult = created.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(("decline", 0, true), (createdResult.GetProperty("portalConfirmationPolicy").GetString(),
            createdResult.GetProperty("portalConfirmationsObserved").GetInt32(), createdResult.GetProperty("portalConfirmationsComplete").GetBoolean()));

        var (preview, _) = await session.WriteAsync(true, new { operationId = "close", operation = "close_global_library", library = prompting });
        Assert.Contains(preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("consequences").EnumerateArray(),
            c => c.GetString()!.Contains("answered No, recorded and fail the item", StringComparison.Ordinal));

        var (declined, raw) = await session.WriteAsync(null, new { operationId = "close", operation = "close_global_library", library = prompting });
        Assert.False(raw.IsError == true);
        Assert.False(declined.GetProperty("success").GetBoolean());
        var item = declined.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal(("failed", "worker_operation_failed"), (item.GetProperty("status").GetString(), item.GetProperty("failure").GetProperty("category").GetString()));
        var dialog = Assert.Single(item.GetProperty("result").GetProperty("portalConfirmations").EnumerateArray());
        Assert.Equal("No", dialog.GetProperty("answer").GetString());
        Assert.Contains("save the changes", dialog.GetProperty("text").GetString());

        Assert.True((await session.WriteAsync(null, create)).Document.GetProperty("success").GetBoolean());
        var (accepted, _) = await session.WriteAsync(null, new { operationId = "close", operation = "close_global_library", library = prompting, portalConfirmations = "accept" });
        Assert.True(accepted.GetProperty("success").GetBoolean(), accepted.GetRawText());
        Assert.Equal("Yes", accepted.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());
        Assert.Contains(accepted.GetProperty("batch").GetProperty("operations")[0].GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("answered Yes", StringComparison.Ordinal));

        var (rejected, _) = await session.WriteAsync(null, new { operationId = "release", operation = "release_type_version", library = "project",
            typePath = "/Motors/PumpControl", version = "1.0.1", versionNumber = "1.1.0", comment = "reject-after-confirmation" });
        var release = rejected.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("engineering_rejected", release.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("EngineeringTargetInvocationException", release.GetProperty("failure").GetProperty("engineeringError").GetProperty("type").GetString());
        Assert.Equal(1, release.GetProperty("result").GetProperty("portalConfirmationsObserved").GetInt32());
    }

    [Fact]
    public async Task Full_ALateDialogAfterAnItem_IsAWarningOnTheNextResponse()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        // A library created in a call is addressed in a later call (its path is known from the result).
        Assert.True((await session.WriteAsync(null,
            new { operationId = "create", operation = "create_global_library", targetDirectory = @"C:\Libraries\New", name = "LatePrompt" })).Document
            .GetProperty("success").GetBoolean());
        var (closed, _) = await session.WriteAsync(null,
            new { operationId = "close", operation = "close_global_library", library = @"C:\Libraries\New\LatePrompt\LatePrompt.al21" });
        Assert.True(closed.GetProperty("success").GetBoolean(), closed.GetRawText());
        Assert.Equal("openLibraries", closed.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());
        // The next worker response after the close is its own verification read (K61): the late dialog's warning reaches the
        // root warnings of the same call, not the item's result.
        Assert.Contains(closed.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains(
            "raised after library_close_global_library returned and were answered No", StringComparison.Ordinal));
        Assert.DoesNotContain("raised after", closed.GetProperty("batch").GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Full_Issue17Operations_RoundTripThroughTheFakeWorker()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, _) = await session.WriteAsync(null,
            new { operationId = "mcFolder", operation = "create_master_copy_folder", library = "project", newName = "Valves" },
            new { operationId = "mc", operation = "create_master_copy", library = "project", masterCopyFolderPath = "/Valves",
                source = new { kind = "block", path = "Main" } },
            new { operationId = "typeFolder", operation = "create_type_folder", library = "project", folderPath = "/Motors", newName = "Drives" },
            new { operationId = "fromDocs", operation = "create_type_from_documents", library = "project", folderPath = "/Motors/Drives",
                documentDirectory = @"C:\Libraries\Docs", documentName = "NewType", plcName = "PLC_1" },
            new { operationId = "dnu", operation = "set_type_do_not_use", library = "project", typePath = "MotorData", doNotUse = true },
            new { operationId = "export", operation = "export_type_version", library = "project", typePath = "MotorData", filePath = @"C:\Libraries\Export\MotorData.xml" },
            new { operationId = "clean", operation = "clean_up_library", library = "project", folderPath = "/Motors", cleanUpMode = "DeleteUnusedTypes" });
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        var items = document.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        Assert.Equal("NewType", Result(items[3]).GetProperty("created")[0].GetProperty("name").GetString());
        Assert.Equal("Success", Result(items[3]).GetProperty("transfer").GetProperty("state").GetString());
        Assert.Equal(64, Result(items[5]).GetProperty("transfer").GetProperty("files")[0].GetProperty("sha256").GetString()!.Length);
        Assert.Contains("library_deletes", GuardIds(document));
        Assert.Contains("library_export_overwrites_files", GuardIds(document));

        var reads = await session.ReadAsync(
            new { operationId = "tree", operation = "get_library_tree", library = "project", content = "masterCopies" },
            new { operationId = "type", operation = "get_type", library = "project", typePath = "MotorData" },
            new { operationId = "formats", operation = "get_supported_export_formats", library = "project", typePath = "MotorData" },
            new { operationId = "compare", operation = "compare_types", library = "project", typePath = "MotorData", targetTypePath = "/Motors/Drives/NewType" });
        Assert.Contains(Result(reads[0]).GetProperty("entries").EnumerateArray(), e => e.GetProperty("path").GetString() == "/Valves/Main"
            && e.GetProperty("kind").GetString() == "masterCopy");
        Assert.Contains(Result(reads[0]).GetProperty("entries").EnumerateArray(), e => e.GetProperty("kind").GetString() == "masterCopyFolder");
        Assert.True(Result(reads[1]).GetProperty("doNotUse").GetBoolean());
        Assert.Equal(new[] { "SimaticML", "SimaticSD" }, Result(reads[2]).GetProperty("formats").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal("ObjectsDifferent", Result(reads[3]).GetProperty("properties")[0].GetProperty("status").GetString());

        // A second export of the same file without overwrite is refused by the worker (nothing replaced).
        var (again, _) = await session.WriteAsync(null,
            new { operationId = "export", operation = "export_type_version", library = "project", typePath = "MotorData", filePath = @"C:\Libraries\Export\MotorData.xml" });
        Assert.False(again.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ReadWrite_ArchiveGlobalLibrary_IsALifecycleItem_ThatConfirms()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "accept");
        Assert.True((await session.WriteAsync(null, new { operationId = "open", operation = "open_global_library", path = PlantPath })).Document.GetProperty("success").GetBoolean());
        var before = session.Prompts;
        var (archived, _) = await session.WriteAsync(null,
            new { operationId = "archive", operation = "archive_global_library", library = PlantPath, targetDirectory = @"C:\Libraries\New", name = "Plant_2026" });
        Assert.True(archived.GetProperty("success").GetBoolean(), archived.GetRawText());
        Assert.Equal(before + 1, session.Prompts);
        Assert.Contains("library_writes_files", GuardIds(archived));
        Assert.EndsWith("Plant_2026.zal21", Result(archived.GetProperty("batch").GetProperty("operations")[0]).GetProperty("transfer").GetProperty("files")[0]
            .GetProperty("path").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadWrite_SecondaryProject_OpensConfirmed_CopiesAMasterCopy_AndCloses()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "accept");
        const string secondary = @"C:\Projects\Old\Old.ap21";
        var (opened, _) = await session.WriteAsync(null,
            new { operationId = "open", operation = "open_secondary_project", path = secondary },
            new { operationId = "copy", operation = "create_master_copy", library = "project", source = new { kind = "block", path = "PLC_OLD/LegacyFB", project = secondary } });
        Assert.True(opened.GetProperty("success").GetBoolean(), opened.GetRawText());
        Assert.Equal(1, session.Prompts);
        Assert.Contains("library_secondary_project", GuardIds(opened));
        Assert.False(Result(opened.GetProperty("batch").GetProperty("operations")[0]).GetProperty("secondaryProject").GetProperty("isPrimary").GetBoolean());

        var listed = await session.ReadAsync(new { operationId = "list", operation = "list_secondary_projects" },
            new { operationId = "tree", operation = "get_library_tree", library = "project", content = "masterCopies" });
        Assert.Equal(secondary, Result(listed[0]).GetProperty("projects")[0].GetProperty("path").GetString());
        Assert.Contains(Result(listed[1]).GetProperty("entries").EnumerateArray(), e => e.GetProperty("name").GetString() == "LegacyFB");

        var (closed, _) = await session.WriteAsync(null, new { operationId = "close", operation = "close_secondary_project", secondaryProject = secondary });
        Assert.True(closed.GetProperty("success").GetBoolean(), closed.GetRawText());
        Assert.Equal(2, session.Prompts);
        Assert.Equal(0, Result((await session.ReadAsync(new { operationId = "list", operation = "list_secondary_projects" }))[0]).GetProperty("projects").GetArrayLength());
    }

    [Fact]
    public async Task ReadWrite_PortalSettings_EveryChangeAsks_SecurityChangeAlsoFiresTheAcknowledgeGuard()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "accept");
        var listed = await session.ReadAsync(new { operationId = "list", operation = "list_portal_settings" });
        var settings = Result(listed[0]).GetProperty("settings").EnumerateArray().ToArray();
        Assert.Contains(settings, s => s.GetProperty("name").GetString() == "CertificateCheck" && s.GetProperty("securityRelevant").GetBoolean());
        Assert.Contains(settings, s => s.GetProperty("name").GetString() == "ReadOnlyInfo" && !s.GetProperty("writable").GetBoolean());

        var (plain, _) = await session.WriteAsync(null, new { operationId = "start", operation = "set_portal_setting", settingsFolderPath = "General", settingName = "ShowStartView", settingValue = "false" });
        Assert.True(plain.GetProperty("success").GetBoolean(), plain.GetRawText());
        // Security review F2: every set_portal_setting confirms in read-write; a plain one fires no acknowledge guard.
        Assert.Equal(1, session.Prompts);
        Assert.DoesNotContain("library_portal_security_setting", GuardIds(plain));
        Assert.Equal("false", Result(plain.GetProperty("batch").GetProperty("operations")[0]).GetProperty("setting").GetProperty("after").GetString());
        Assert.Contains("library_portal_setting", GuardIds(plain));

        var (security, _) = await session.WriteAsync(null, new { operationId = "cert", operation = "set_portal_setting", settingsFolderPath = "General/Security", settingName = "CertificateCheck", settingValue = "false" });
        Assert.True(security.GetProperty("success").GetBoolean(), security.GetRawText());
        Assert.Equal(2, session.Prompts);
        var guard = security.GetProperty("guards").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "library_portal_security_setting");
        Assert.True(guard.GetProperty("acknowledged").GetBoolean());

        var (refused, _) = await session.WriteAsync(null, new { operationId = "ro", operation = "set_portal_setting", settingsFolderPath = "General", settingName = "ReadOnlyInfo", settingValue = "X" });
        Assert.Equal("unsupported_capability", refused.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task ReadWrite_PortalSettings_AreClassifiedByTheResolvedNames_AndADriftedClassificationFails()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite, "accept");
        // The caller's text "Display" names nothing security-relevant; TIA Portal (the model) resolves it to General/Accept.
        var (aliased, _) = await session.WriteAsync(null, new { operationId = "alias", operation = "set_portal_setting", settingsFolderPath = "Display", settingName = "AutoAcceptCertificates", settingValue = "true" });
        Assert.True(aliased.GetProperty("success").GetBoolean(), aliased.GetRawText());
        Assert.Contains("library_portal_security_setting", GuardIds(aliased));
        Assert.Equal("General/Accept", Result(aliased.GetProperty("batch").GetProperty("operations")[0]).GetProperty("setting").GetProperty("folderPath").GetString());

        // Planned as not security-relevant, resolved as security-relevant at the change: the item fails, nothing is changed.
        var (drifted, _) = await session.WriteAsync(null, new { operationId = "drift", operation = "set_portal_setting", settingsFolderPath = "Drift", settingName = "Option", settingValue = "true" });
        Assert.False(drifted.GetProperty("success").GetBoolean(), drifted.GetRawText());
        var failure = drifted.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure");
        Assert.Equal("state_changed", failure.GetProperty("category").GetString());
        Assert.Contains("Nothing was changed", failure.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvalidInput_IsAnArgumentError_WithoutWorkerCalls()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (archive, raw) = await session.WriteAsync(null, new { operationId = "open", operation = "open_global_library", path = @"C:\Libraries\Archive\Plant.zal21" });
        Assert.True(raw.IsError);
        Assert.Equal("validation_error", archive.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("retrieve_global_library", archive.GetProperty("error").GetProperty("message").GetString());

        var (wrongTool, wrongRaw) = await session.WriteAsync(null, new { operationId = "read", operation = "list_libraries" });
        Assert.True(wrongRaw.IsError);
        Assert.Contains("use library_read", wrongTool.GetProperty("error").GetProperty("message").GetString());

        var (misplaced, _) = await session.CallAsync("library_read", Args(("operations", new object[]
            { new { operationId = "tree", operation = "get_library_tree", library = "project", masterCopyName = "Valve" } })));
        Assert.Contains("'masterCopyName' is not valid", misplaced.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(session.Methods, method => method.StartsWith("library_", StringComparison.Ordinal));
    }
}
