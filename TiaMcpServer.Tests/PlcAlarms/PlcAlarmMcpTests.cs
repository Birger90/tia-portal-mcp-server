using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// The alarm, alarm class, text list and supervision operations end to end against the scripted FakeWorker
/// (plc-alarms-roundtrip), whose workbooks are read and patched by the production XLSX code. Like every FakeWorker
/// protocol test these launch TiaMcpServer.FakeWorker.exe.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcAlarmMcpTests
{
    private const string Scenario = "plc-alarms-roundtrip";

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
            Files = Directory.CreateTempSubdirectory("tia-mcp-alarm-mcp-").FullName;
        }

        public McpProtocolTestHarness Harness { get; private set; } = null!;

        public string[] Methods => _requests.Methods();

        public string Files { get; }

        public static async Task<Session> StartAsync(McpAccessMode mode)
        {
            var session = new Session();
            session.Harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(mode, session._audit.Path, Scenario);
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
            var (document, raw) = await CallAsync("plc_read", Args(("operations", operations)));
            Assert.False(raw.IsError == true);
            return document.GetProperty("batch").GetProperty("operations").EnumerateArray().ToArray();
        }

        public Task<(JsonElement Document, CallToolResult Raw)> WriteAsync(bool dryRun, params object[] operations)
            => CallAsync("plc_write", Args(("dryRun", dryRun), ("operations", operations)));

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _uiOpen.Dispose();
            _requests.Dispose();
            _audit.Dispose();
            Directory.Delete(Files, recursive: true);
        }
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static string[] GuardIds(JsonElement document) => document.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    private static JsonElement Cell(JsonElement workbook, string sheet, string cell)
        => workbook.GetProperty("sheets").EnumerateArray().Single(s => s.GetProperty("name").GetString() == sheet)
            .GetProperty("rows").EnumerateArray().SelectMany(r => r.GetProperty("cells").EnumerateArray())
            .Single(c => c.GetProperty("cell").GetString() == cell);

    [Fact]
    public async Task ReadOnly_ListsTextListsAndReadsTheThreeWorkbooks()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadOnly);
        var items = await session.ReadAsync(
            new { operationId = "lists", operation = "list_text_lists" },
            new { operationId = "tl", operation = "read_text_list_workbook", plcName = "PLC_1", maxElements = 2 },
            new { operationId = "texts", operation = "read_alarm_text_workbook", plcName = "PLC_1", exportOption = "OptionA", cultures = new[] { "de-DE" } },
            new { operationId = "sv", operation = "read_supervision_workbook", blockPath = "PLC_1/ProDiag_FB" },
            new { operationId = "bad-option", operation = "read_alarm_text_workbook", plcName = "PLC_1", exportOption = "NoSuchOption" });

        Assert.All(items.Take(4), item => Assert.Equal("succeeded", item.GetProperty("status").GetString()));
        var lists = items[0].GetProperty("result");
        Assert.Equal(new[] { "en-US", "de-DE" }, lists.GetProperty("projectLanguages").GetProperty("active").EnumerateArray().Select(c => c.GetString()));
        var plc = lists.GetProperty("plcs")[0];
        Assert.Equal(new[] { "USER_1", "USER_2" }, plc.GetProperty("userTextLists").EnumerateArray().Select(l => l.GetProperty("name").GetString()));
        Assert.True(plc.GetProperty("systemTextLists")[0].GetProperty("isSystem").GetBoolean());
        Assert.Equal("Decimal", plc.GetProperty("userTextLists")[0].GetProperty("listRange").GetString());

        var workbook = items[1].GetProperty("result");
        Assert.Equal("textLists", workbook.GetProperty("kind").GetString());
        Assert.True(PlcAlarmPolicy.IsWorkbookHash(workbook.GetProperty("workbookHash").GetString()));
        var sheet = workbook.GetProperty("sheets")[0];
        Assert.Equal((4, 2, true, 3), (sheet.GetProperty("rowCount").GetInt32(), sheet.GetProperty("rows").GetArrayLength(), sheet.GetProperty("rowsTruncated").GetBoolean(),
            sheet.GetProperty("nextStartRow").GetInt32()));
        Assert.False(workbook.GetProperty("complete").GetBoolean());
        Assert.Equal("Stopped", Cell(workbook, "Text lists", "D2").GetProperty("value").GetString());
        Assert.Equal(new[] { "de-DE" }, items[2].GetProperty("result").GetProperty("cultures").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal("PLC_1/ProDiag_FB", items[3].GetProperty("result").GetProperty("blockPath").GetString());
        Assert.Equal("validation_error", items[4].GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("OptionA, OptionB", items[4].GetProperty("failure").GetProperty("message").GetString());
    }

    [Fact]
    public async Task UpdateAndDeleteTextLists_AreReadBack_AndSystemListsAreRefused()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "rename", operation = "update_text_list", plcName = "PLC_1", textListName = "user_1", newName = "MOTOR", listRange = "Bit", comment = "Motor", commentCulture = "en-US" },
            new { operationId = "drop", operation = "delete_text_list", plcName = "PLC_1", textListName = "USER_2" });

        Assert.False(raw.IsError == true);
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Contains("plc_deletes_text_list", GuardIds(document));
        var effect = document.GetProperty("effects")[0].GetProperty("effect");
        Assert.Equal("TextList", effect.GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(new[] { "name", "listRange", "comment[en-US]" }, effect.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString()));
        var checks = document.GetProperty("verification").GetProperty("operations").EnumerateArray().ToArray();
        Assert.All(checks, check => Assert.Equal(("textList", true), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean())));
        Assert.Equal("MOTOR listRange=Bit comment[en-US]=Motor", checks[0].GetProperty("actual").GetString());
        var result = document.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        // The rename runs first and is read back (TIA Portal may ignore a colliding rename silently).
        Assert.Equal(new[] { "Name", "ListRange", "Comment[en-US]" }, result.GetProperty("appliedMembers").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(("decline", 0), (result.GetProperty("portalConfirmationPolicy").GetString(), result.GetProperty("portalConfirmationsObserved").GetInt32()));

        var (system, systemRaw) = await session.WriteAsync(false,
            new { operationId = "sys", operation = "delete_text_list", plcName = "PLC_1", textListName = "SystemTextList_1" });
        Assert.True(systemRaw.IsError == true);
        Assert.Equal("validation_error", system.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("system text list", system.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, session.Methods.Count(m => m == "delete_text_list"));
    }

    [Fact]
    public async Task CellUpdate_PreviewsPatchesImportsAndVerifies()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var names = new[] { "USER_1", "USER_2" };
        var hash = (await session.ReadAsync(new { operationId = "tl", operation = "read_text_list_workbook", plcName = "PLC_1", textListNames = names }))[0]
            .GetProperty("result").GetProperty("workbookHash").GetString();
        var edits = new object[]
        {
            new { sheet = "Text lists", cell = "D2", expected = "Stopped", value = "Halted" },
            new { sheet = "Text lists", cell = "A5", expected = "", value = "USER_2" },
            new { sheet = "Text lists", cell = "B5", expected = "", value = "1", kind = "number" },
        };

        var (preview, _) = await session.WriteAsync(true, new { operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", textListNames = names, cellEdits = edits, expectedWorkbookHash = hash });
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        Assert.Contains("plc_workbook_round_trip", GuardIds(preview));
        var change = preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0];
        Assert.Equal(("Text lists!D2", "Stopped", "Halted"), (change.GetProperty("field").GetString(), change.GetProperty("current").GetString(), change.GetProperty("requested").GetString()));
        Assert.DoesNotContain("update_text_list_cells", session.Methods);

        var (applied, raw) = await session.WriteAsync(false, new { operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", textListNames = names, cellEdits = edits, expectedWorkbookHash = hash });
        Assert.False(raw.IsError == true);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        var result = applied.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(hash, result.GetProperty("workbookHashBefore").GetString());
        Assert.Equal("Halted", result.GetProperty("edits")[0].GetProperty("after").GetString());
        var verification = applied.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("cells", true), (verification.GetProperty("check").GetString(), verification.GetProperty("success").GetBoolean()));

        var after = (await session.ReadAsync(new { operationId = "tl", operation = "read_text_list_workbook", plcName = "PLC_1", textListNames = names }))[0].GetProperty("result");
        Assert.Equal(after.GetProperty("workbookHash").GetString(), verification.GetProperty("contentHash").GetString());
        Assert.Equal(result.GetProperty("workbookHashAfter").GetString(), after.GetProperty("workbookHash").GetString());
        Assert.Equal("Halted", Cell(after, "Text lists", "D2").GetProperty("value").GetString());
        Assert.Equal("number", Cell(after, "Text lists", "B5").GetProperty("kind").GetString());

        var (stale, _) = await session.WriteAsync(false, new { operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", textListNames = names, cellEdits = edits, expectedWorkbookHash = hash });
        Assert.Equal("state_changed", stale.GetProperty("error").GetProperty("category").GetString());
        var (wrong, _) = await session.WriteAsync(false, new
        {
            operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", textListNames = names, expectedWorkbookHash = after.GetProperty("workbookHash").GetString(),
            cellEdits = new object[] { new { sheet = "Text lists", cell = "D2", expected = "Stopped", value = "x" } },
        });
        Assert.Equal("validation_error", wrong.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("holds 'Halted'", wrong.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(1, session.Methods.Count(m => m == "update_text_list_cells"));
    }

    /// <summary>M2: a text list cell update names its user lists, so it never exports and imports a system list's rows again.</summary>
    [Fact]
    public async Task TextListCellUpdates_RequireTextListNames()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);

        var (document, raw) = await session.WriteAsync(true, new
        {
            operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", expectedWorkbookHash = "cells:sha256:" + new string('a', 64),
            cellEdits = new object[] { new { sheet = "Text lists", cell = "D2", expected = "Stopped", value = "x" } },
        });

        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("update_text_list_cells needs textListNames", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("plc_alarm_plan", session.Methods);
    }

    [Fact]
    public async Task ExportsAndImports_UseLocalFilesOnly_AndNeverOverwriteUnasked()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var path = Path.Combine(session.Files, "TextLists.xlsx");
        var classes = Path.Combine(session.Files, "AlarmClasses.dat");

        var (exported, _) = await session.WriteAsync(false,
            new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = path },
            new { operationId = "texts", operation = "export_alarm_texts", plcName = "PLC_1", exportOption = "OptionB", filePath = Path.Combine(session.Files, "AlarmTexts.xlsx") },
            new { operationId = "sv", operation = "export_supervisions", blockPath = "PLC_1/ProDiag_FB", filePath = Path.Combine(session.Files, "Supervisions.xlsx") },
            new { operationId = "classes", operation = "export_alarm_classes", filePath = classes });
        Assert.True(exported.GetProperty("success").GetBoolean(), exported.GetRawText());
        Assert.All(exported.GetProperty("verification").GetProperty("operations").EnumerateArray(),
            check => Assert.Equal(("file", true), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean())));
        Assert.Equal(PlcAlarmFiles.Hash(path).Sha256, exported.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("sha256").GetString());
        Assert.Equal("AlarmClasses", exported.GetProperty("effects")[3].GetProperty("effect").GetProperty("target").GetProperty("kind").GetString());
        Assert.Equal(4, Directory.GetFiles(session.Files).Length);

        var (again, againRaw) = await session.WriteAsync(false, new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = path });
        Assert.True(againRaw.IsError == true);
        Assert.Contains("pass overwrite:true", again.GetProperty("error").GetProperty("message").GetString());
        var (replaced, _) = await session.WriteAsync(false, new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = path, overwrite = true });
        Assert.True(replaced.GetProperty("success").GetBoolean());
        Assert.Contains("plc_export_overwrites_file", GuardIds(replaced));
        Assert.True(replaced.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("overwritten").GetBoolean());

        var (imported, _) = await session.WriteAsync(false,
            new { operationId = "tl", operation = "import_text_lists", plcName = "PLC_1", filePath = path },
            new { operationId = "classes", operation = "import_alarm_classes", filePath = classes });
        Assert.True(imported.GetProperty("success").GetBoolean(), imported.GetRawText());
        Assert.Contains("plc_imports_alarm_data", GuardIds(imported));
        var importResult = imported.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal("Override", importResult.GetProperty("importOption").GetString());
        Assert.Equal(("rows", true, 0), (importResult.GetProperty("importCheck").GetProperty("method").GetString(),
            importResult.GetProperty("importCheck").GetProperty("verified").GetBoolean(), importResult.GetProperty("importCheck").GetProperty("rowsNotShown").GetInt32()));
        Assert.Equal(new[] { ("importedRows", true), ("fileHash", true) },
            imported.GetProperty("verification").GetProperty("operations").EnumerateArray().Select(c => (c.GetProperty("check").GetString()!, c.GetProperty("success").GetBoolean())));

        var (missing, _) = await session.WriteAsync(false, new { operationId = "tl", operation = "import_text_lists", plcName = "PLC_1", filePath = Path.Combine(session.Files, "absent.xlsx") });
        Assert.Equal("target_not_found", missing.GetProperty("error").GetProperty("category").GetString());
        // UNC shares are allowed (owner decision 2026-11-01); device paths stay refused.
        var (device, deviceRaw) = await session.WriteAsync(false, new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = @"\\?\C:\TextLists.xlsx" });
        Assert.True(deviceRaw.IsError == true);
        Assert.Equal("validation_error", device.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(1, session.Methods.Count(m => m == "import_text_lists"));
    }

    [Fact]
    public async Task Supervisions_OfAnFbWithoutProDiag_AreUnsupported()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var item = (await session.ReadAsync(new { operationId = "sv", operation = "read_supervision_workbook", blockPath = "PLC_1/Main" }))[0];

        Assert.Equal("unsupported_capability", item.GetProperty("failure").GetProperty("category").GetString());
        var (document, _) = await session.WriteAsync(false, new
        {
            operationId = "sv", operation = "update_supervision_cells", blockPath = "PLC_1/Main", expectedWorkbookHash = "cells:sha256:" + new string('0', 64),
            cellEdits = new object[] { new { sheet = "Supervisions", cell = "B2", expected = "x", value = "y" } },
        });
        Assert.Equal("unsupported_capability", document.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain("update_supervision_cells", session.Methods);
    }

    [Fact]
    public async Task SystemTextLists_AreReadable_ButNeverNamedByACellUpdate()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var read = (await session.ReadAsync(new { operationId = "tl", operation = "read_text_list_workbook", plcName = "PLC_1", textListNames = new[] { "systemtextlist_1", "user_1" } }))[0];
        Assert.Equal("succeeded", read.GetProperty("status").GetString());
        Assert.Equal(new[] { "SystemTextList_1", "USER_1" }, read.GetProperty("result").GetProperty("textListNames").EnumerateArray().Select(n => n.GetString()));

        var (refused, raw) = await session.WriteAsync(true, new
        {
            operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", textListNames = new[] { "SystemTextList_1" },
            expectedWorkbookHash = read.GetProperty("result").GetProperty("workbookHash").GetString(),
            cellEdits = new object[] { new { sheet = "Text lists", cell = "D2", expected = "Stopped", value = "x" } },
        });
        Assert.True(raw.IsError == true);
        Assert.Equal("validation_error", refused.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("system text list", refused.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain("update_text_list_cells", session.Methods);
    }

    [Fact]
    public async Task Rename_ToAnExistingName_IsRefusedAtPlanning()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        foreach (var taken in new[] { "user_2", "SYSTEMTEXTLIST_1" })
        {
            var (document, raw) = await session.WriteAsync(true, new { operationId = "rename", operation = "update_text_list", plcName = "PLC_1", textListName = "USER_1", newName = taken });
            Assert.True(raw.IsError == true, taken);
            Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
            Assert.Contains("already has", document.GetProperty("error").GetProperty("message").GetString());
        }

        Assert.DoesNotContain("update_text_list", session.Methods);
    }

    [Fact]
    public async Task CellUpdate_FailsWhenTheImportChangedACellNoEditRequested()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var hash = (await session.ReadAsync(new { operationId = "tl", operation = "read_text_list_workbook", plcName = "PLC_1", textListNames = new[] { "USER_1" } }))[0]
            .GetProperty("result").GetProperty("workbookHash").GetString();

        var (document, raw) = await session.WriteAsync(false, new
        {
            operationId = "cells", operation = "update_text_list_cells", plcName = "PLC_1", textListNames = new[] { "USER_1" }, expectedWorkbookHash = hash,
            cellEdits = new object[] { new { sheet = "Text lists", cell = "D2", expected = "Stopped", value = "SIDE_EFFECT" } },
        });

        Assert.False(raw.IsError == true);
        Assert.False(document.GetProperty("success").GetBoolean());
        var item = document.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("postcondition_failed", item.GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal("Text lists!A1: 'Text list' -> 'changed by import'", item.GetProperty("result").GetProperty("unrequestedChanges")[0].GetString());
    }

    [Fact]
    public async Task PortalConfirmations_AreDeclinedByDefault_AndAcceptedOnRequest()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var path = Path.Combine(session.Files, "with-dialog.xlsx");

        var (declined, _) = await session.WriteAsync(false, new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = path });
        Assert.False(declined.GetProperty("success").GetBoolean());
        var item = declined.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Contains("answered each one No", item.GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal("No", item.GetProperty("result").GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());

        var (accepted, _) = await session.WriteAsync(false, new
        {
            operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = Path.Combine(session.Files, "with-dialog-2.xlsx"), portalConfirmations = "accept",
        });
        Assert.True(accepted.GetProperty("success").GetBoolean(), accepted.GetRawText());
        var acceptedItem = accepted.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("Yes", acceptedItem.GetProperty("result").GetProperty("portalConfirmations")[0].GetProperty("answer").GetString());
        Assert.Contains(acceptedItem.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("answered Yes", StringComparison.Ordinal));

        var (invalid, invalidRaw) = await session.WriteAsync(true, new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = path, portalConfirmations = "yes" });
        Assert.True(invalidRaw.IsError == true);
        Assert.Contains("portalConfirmations must be one of", invalid.GetProperty("error").GetProperty("message").GetString());
    }

    /// <summary>M3: an import without a documented export is trusted with an explicit "not read back" warning, not notVerified.</summary>
    [Fact]
    public async Task Imports_WithoutADocumentedReexport_AreTrusted_WithAWarning()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var settings = Path.Combine(session.Files, "Settings.xlsx");
        File.WriteAllBytes(settings, TiaMcpServer.FakeWorker.Plc.XlsxFixture.Build(new[] { new TiaMcpServer.FakeWorker.Plc.XlsxFixture.Sheet("Settings", new IReadOnlyList<object?>[] { new object?[] { "x" } }) }));

        var (document, raw) = await session.WriteAsync(false, new { operationId = "settings", operation = "import_supervision_settings", blockPath = "PLC_1/ProDiag_FB", filePath = settings });

        Assert.False(raw.IsError == true);
        var item = document.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.Equal(("none", "notReadBack"), (item.GetProperty("result").GetProperty("importCheck").GetProperty("method").GetString(),
            item.GetProperty("result").GetProperty("importCheck").GetProperty("outcome").GetString()));
        Assert.Equal(PlcAlarmFiles.Hash(settings).Sha256, item.GetProperty("result").GetProperty("sha256").GetString());
        Assert.Contains(item.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("not read back: no documented export", StringComparison.Ordinal));
        var check = document.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("trusted", true), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean()));
        Assert.Contains("not read back", check.GetProperty("message").GetString());
        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
    }

    /// <summary>
    /// M3: an imported row the re-export does not show contradicts the import (notVerified, root success false, the item
    /// succeeded); a layout difference (a renamed header) is inconclusive: trusted with a message and a warning.
    /// </summary>
    [Fact]
    public async Task Imports_ContradictedAreNotVerified_InconclusiveAreTrusted()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        string Write(string name, string id)
        {
            var file = Path.Combine(session.Files, name);
            File.WriteAllBytes(file, TiaMcpServer.FakeWorker.Plc.XlsxFixture.Build(new[]
            {
                new TiaMcpServer.FakeWorker.Plc.XlsxFixture.Sheet("Supervisions", new IReadOnlyList<object?>[] { new object?[] { "ID", "Text" }, new object?[] { id, "Valve" } }),
            }));
            return file;
        }

        var (contradicted, raw) = await session.WriteAsync(false, new { operationId = "sv", operation = "import_supervisions", blockPath = "PLC_1/ProDiag_FB", filePath = Write("Kept.xlsx", "KEPT_BY_IMPORT") });
        Assert.False(raw.IsError == true);
        var item = contradicted.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var importCheck = item.GetProperty("result").GetProperty("importCheck");
        Assert.Equal(("contradicted", 1), (importCheck.GetProperty("outcome").GetString(), importCheck.GetProperty("rowsNotShown").GetInt32()));
        var check = contradicted.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("notVerified", false), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean()));
        Assert.Contains("changed the project", check.GetProperty("message").GetString());
        Assert.False(contradicted.GetProperty("success").GetBoolean());

        var (inconclusive, _) = await session.WriteAsync(false, new { operationId = "sv", operation = "import_supervisions", blockPath = "PLC_1/ProDiag_FB", filePath = Write("Renamed.xlsx", "SIDE_EFFECT") });
        var renamed = inconclusive.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("inconclusive", renamed.GetProperty("result").GetProperty("importCheck").GetProperty("outcome").GetString());
        Assert.Contains(renamed.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("verification is inconclusive", StringComparison.Ordinal));
        var trusted = inconclusive.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("trusted", true), (trusted.GetProperty("check").GetString(), trusted.GetProperty("success").GetBoolean()));
        Assert.Contains("inconclusive", trusted.GetProperty("message").GetString());
        Assert.True(inconclusive.GetProperty("success").GetBoolean(), inconclusive.GetRawText());
    }

    /// <summary>L6: an assignment that does not read back by FB name is notVerified, not a failed write and not success.</summary>
    [Fact]
    public async Task Assignments_ThatDoNotReadBackByName_AreNotVerified()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);

        var (document, raw) = await session.WriteAsync(false, new { operationId = "assign", operation = "set_prodiag_assignment", tableName = "Tags", name = "Opaque_1", proDiagFb = "ProDiag_FB" });

        Assert.False(raw.IsError == true);
        var item = document.GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").GetProperty("assignedProDiagFbAfter").ValueKind);
        var warnings = item.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToArray();
        Assert.Contains(warnings, w => w.Contains("before the write is treated as unset", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("does not read back", StringComparison.Ordinal));
        var check = document.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("notVerified", false, "(unreadable)"), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean(), check.GetProperty("actual").GetString()));
        Assert.Contains("neither the FB name nor a block", check.GetProperty("message").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task FilesBelowALink_AreAllowed_AndReported()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var target = Directory.CreateDirectory(Path.Combine(session.Files, "target")).FullName;
        var linked = Path.Combine(session.Files, "linked");
        Directory.CreateSymbolicLink(linked, target);

        var (document, _) = await session.WriteAsync(false, new { operationId = "tl", operation = "export_text_lists", plcName = "PLC_1", filePath = Path.Combine(linked, "TextLists.xlsx") });

        Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
        Assert.Contains("plc_file_on_network_or_link", GuardIds(document));
        Assert.True(File.Exists(Path.Combine(target, "TextLists.xlsx")));
    }

    [Fact]
    public async Task ProDiag_EvidenceAssignmentAndAlarmInformation()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var reads = await session.ReadAsync(
            new { operationId = "fb", operation = "get_prodiag_fb", blockPath = "PLC_1/ProDiag_FB" },
            new { operationId = "main", operation = "get_prodiag_fb", blockPath = "PLC_1/Main" },
            new { operationId = "tag", operation = "get_prodiag_assignment", tableName = "Tags", name = "Valve_1" });
        var fb = reads[0].GetProperty("result");
        Assert.Equal(("ProDiag", true, 1), (fb.GetProperty("programmingLanguage").GetString(), fb.GetProperty("isProDiag").GetBoolean(), fb.GetProperty("supervisionCount").GetInt32()));
        Assert.False(reads[1].GetProperty("result").GetProperty("isProDiag").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reads[2].GetProperty("result").GetProperty("assignedProDiagFb").ValueKind);
        var (both, bothRaw) = await session.CallAsync("plc_read", Args(("operations", new object[]
        {
            new { operationId = "both", operation = "get_prodiag_assignment", tableName = "Tags", name = "Valve_1", blockPath = "PLC_1/Data" },
        })));
        Assert.True(bothRaw.IsError == true);
        Assert.Contains("either as a tag", both.GetProperty("error").GetProperty("message").GetString());

        var directory = Path.Combine(session.Files, "ProDiag");
        var (written, _) = await session.WriteAsync(false,
            new { operationId = "assign", operation = "set_prodiag_assignment", tableName = "Tags", name = "Valve_1", proDiagFb = "ProDiag_FB" },
            new { operationId = "info", operation = "export_prodiag_info", blockPath = "PLC_1/ProDiag_FB", directoryPath = directory });
        Assert.True(written.GetProperty("success").GetBoolean(), written.GetRawText());
        var change = written.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0];
        Assert.Equal(("assignedProDiagFb", "ProDiag_FB"), (change.GetProperty("field").GetString(), change.GetProperty("requested").GetString()));
        Assert.Equal(new[] { ("proDiagAssignment", true), ("directory", true) },
            written.GetProperty("verification").GetProperty("operations").EnumerateArray().Select(c => (c.GetProperty("check").GetString()!, c.GetProperty("success").GetBoolean())));
        Assert.Equal(2, Directory.GetFiles(directory).Length);

        var (again, againRaw) = await session.WriteAsync(true, new { operationId = "info", operation = "export_prodiag_info", blockPath = "PLC_1/ProDiag_FB", directoryPath = directory });
        Assert.True(againRaw.IsError == true);
        Assert.Contains("already exists", again.GetProperty("error").GetProperty("message").GetString());
    }
}
