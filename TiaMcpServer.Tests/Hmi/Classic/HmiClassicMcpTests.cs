using System.Text.Json;
using ModelContextProtocol.Client;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.ClassicWinCC;

/// <summary>Classic WinCC (issue #28) through MCP and the FakeWorker: list_classic_objects and the five classic writes.</summary>
[Collection("Mcp protocol serial")]
public class HmiClassicMcpTests
{
    private const string Scenario = "hmi-write-roundtrip";

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        Assert.True(reply.StructuredContent is not null, string.Join(" ", reply.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)));
        return NetworkGuardedWriteMcpTests.Document(reply);
    }

    private static JsonElement Item(JsonElement document, int index) => document.GetProperty("batch").GetProperty("operations")[index];

    private static string Failure(JsonElement document, int index) => Item(document, index).GetProperty("failure").GetProperty("category").GetString()!;

    [Fact]
    public async Task ListExportImportDeleteAndFoldersRunOnTheClassicHmi()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

        var listed = await CallAsync(harness, "hmi_read", [new { operationId = "l", operation = "list_classic_objects", hmiName = "C", classicKind = "screens" }]);
        var screens = Item(listed, 0).GetProperty("result");
        Assert.Equal(new[] { "Main", "Settings" }, screens.GetProperty("objects").EnumerateArray().Select(o => o.GetProperty("name").GetString()));
        Assert.Equal("Service", screens.GetProperty("folders")[0].GetString());

        object[] writes =
        [
            new { operationId = "export", operation = "export_classic_objects", hmiName = "C", classicKind = "screens", directoryPath = @"C:\Exports", objectNames = new[] { "Main" } },
            new { operationId = "import", operation = "import_classic_objects", hmiName = "C", classicKind = "screens", filePath = @"C:\Imports\Screens.xml", groupPath = "Service" },
            new { operationId = "folder", operation = "create_classic_folder", hmiName = "C", classicKind = "tagTables", groupPath = "Line1" },
            new { operationId = "delete", operation = "delete_classic_object", hmiName = "C", classicKind = "textLists", objectName = "States" },
        ];
        var preview = await CallAsync(harness, "hmi_write", writes, dryRun: true);
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Equal(@"C:\Exports\Main.xml", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0].GetProperty("requested").GetString());
        Assert.Contains(preview.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == HmiGuardDefinitions.ClassicDeletes);
        Assert.Equal("export", preview.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("effects")[2].GetProperty("effect").GetProperty("dependsOn").ValueKind);

        var applied = await CallAsync(harness, "hmi_write", writes);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.StartsWith(@"screens Main -> C:\Exports\Main.xml", Item(applied, 0).GetProperty("result").GetProperty("appliedProperties")[0].GetString());
        Assert.Contains(Item(applied, 1).GetProperty("result").GetProperty("appliedProperties").EnumerateArray(), p => p.GetString() == "screens Detail");

        var after = await CallAsync(harness, "hmi_read", [new { operationId = "l", operation = "list_classic_objects", hmiName = "C", classicKind = "textLists" }]);
        Assert.Empty(Item(after, 0).GetProperty("result").GetProperty("objects").EnumerateArray());
    }

    [Fact]
    public async Task ScreenNumberClashUnifiedTargetAndMissingMemberFailTheItem()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

        var clash = await CallAsync(harness, "hmi_write", [new { operationId = "i", operation = "import_classic_objects", hmiName = "C", classicKind = "screens", filePath = @"C:\Imports\Clash.xml" }]);
        var unified = await CallAsync(harness, "hmi_write", [new { operationId = "i", operation = "delete_classic_object", hmiName = "H", classicKind = "screens", objectName = "Main" }]);
        var missing = await CallAsync(harness, "hmi_write", [new { operationId = "e", operation = "export_classic_objects", hmiName = "C", classicKind = "screens", directoryPath = @"C:\NoMember" }]);
        var unifiedRead = await CallAsync(harness, "hmi_read", [new { operationId = "l", operation = "list_classic_objects", hmiName = "H", classicKind = "screens" }]);

        Assert.Equal("validation_error", Failure(clash, 0));
        Assert.Contains("V21 pitfall 4", Item(clash, 0).GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal("target_kind_unsupported", unified.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("api_member_missing", Failure(missing, 0));
        Assert.Equal("target_kind_unsupported", Failure(unifiedRead, 0));
    }

    [Fact]
    public void Catalogs_RefuseBadShapes()
    {
        static string? Error(HmiWriteOperationRequest item) => HmiWriteCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("'classicKind' must be one of", Error(new() { OperationId = "a", Operation = "delete_classic_object", ClassicKind = "pictures", ObjectName = "X" }));
        Assert.Contains("skipped: reported to crash TIA Portal V21", Error(new() { OperationId = "a", Operation = "export_classic_objects", ClassicKind = "graphicLists", DirectoryPath = @"C:\x" }));
        Assert.Contains("'tableName' is required", Error(new() { OperationId = "a", Operation = "import_classic_objects", ClassicKind = "tags", FilePath = @"C:\x.xml" }));
        Assert.Contains("'tableName' is only valid", Error(new() { OperationId = "a", Operation = "delete_classic_object", ClassicKind = "screens", ObjectName = "X", TableName = "T" }));
        Assert.Contains("has no user folders", Error(new() { OperationId = "a", Operation = "create_classic_folder", ClassicKind = "textLists", GroupPath = "F" }));
        Assert.Contains("documents no Delete()", Error(new() { OperationId = "a", Operation = "delete_classic_object", ClassicKind = "screenSlideins", ObjectName = "Top" }));
        Assert.Contains("single object", Error(new() { OperationId = "a", Operation = "export_classic_objects", ClassicKind = "screenOverview", DirectoryPath = @"C:\x", ObjectNames = new() { "A" } }));
        Assert.True(HmiWriteCatalog.ValidateWrite(new[] { new HmiWriteOperationRequest { OperationId = "a", Operation = "import_classic_objects", ClassicKind = "screenGlobalElements", FilePath = @"\\server\share\g.xml", Overwrite = true } }).IsValid);

        static string? ReadError(HmiOperationRequest item) => HmiOperationCatalog.ValidateRead(new[] { item }).Error;
        Assert.Contains("missing required field(s): classicKind", ReadError(new() { OperationId = "r", Operation = "list_classic_objects" }));
        Assert.Contains("'tableName' is only valid with classicKind tags", ReadError(new() { OperationId = "r", Operation = "list_classic_objects", ClassicKind = "screens", TableName = "T" }));
        Assert.Contains("no user folders", ReadError(new() { OperationId = "r", Operation = "list_classic_objects", ClassicKind = "cycles", GroupPath = "F" }));
        Assert.True(HmiOperationCatalog.ValidateRead(new[] { new HmiOperationRequest { OperationId = "r", Operation = "list_classic_objects", ClassicKind = HmiClassicKinds.Tags, TableName = "Motors" } }).IsValid);
    }
}
