using ModelContextProtocol.Client;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.Screens;

/// <summary>export_text_lists / import_text_lists of WinCC Unified user text lists, through MCP and the FakeWorker.</summary>
[Collection("Mcp protocol serial")]
public class HmiTextListFileMcpTests
{
    private const string Scenario = "hmi-write-roundtrip";

    private static async Task<System.Text.Json.JsonElement> WriteAsync(McpProtocolTestHarness harness, object[] operations, bool dryRun = false)
    {
        var reply = await harness.Client.CallToolAsync("hmi_write", new Dictionary<string, object?> { ["operations"] = operations, ["dryRun"] = dryRun });
        Assert.True(reply.StructuredContent is not null, string.Join(" ", reply.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(t => t.Text)));
        return NetworkGuardedWriteMcpTests.Document(reply);
    }

    private static string Failure(System.Text.Json.JsonElement document, int index)
        => document.GetProperty("batch").GetProperty("operations")[index].GetProperty("failure").GetProperty("category").GetString()!;

    [Fact]
    public async Task ExportAndImportRunWithTheUnverifiedMemberGuard()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        object[] operations =
        [
            new { operationId = "export", operation = "export_text_lists", directoryPath = @"C:\Exports", textListNames = new[] { "States" } },
            new { operationId = "import", operation = "import_text_lists", filePath = @"C:\Imports\Lists.xml" },
        ];

        var preview = await WriteAsync(harness, operations, dryRun: true);
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        Assert.Contains(preview.GetProperty("guards").EnumerateArray(), g => g.GetProperty("id").GetString() == HmiGuardDefinitions.TextListFileUnverified);
        Assert.Equal(@"C:\Exports\States.xml", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("changes")[0].GetProperty("requested").GetString());

        var applied = await WriteAsync(harness, operations);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        var results = applied.GetProperty("batch").GetProperty("operations");
        Assert.StartsWith(@"textList States -> C:\Exports\States.xml", results[0].GetProperty("result").GetProperty("appliedProperties")[0].GetString());
        Assert.Contains(results[1].GetProperty("result").GetProperty("appliedProperties").EnumerateArray(), p => p.GetString() == "textList Valves");

        // The same file again: without overwrite the export refuses, the import is TIA Portal's to reject.
        var again = await WriteAsync(harness, [new { operationId = "export", operation = "export_text_lists", directoryPath = @"C:\Exports", textListNames = new[] { "States" } }]);
        Assert.Equal("validation_error", Failure(again, 0));
        var replaced = await WriteAsync(harness, [new { operationId = "export", operation = "export_text_lists", directoryPath = @"C:\Exports", textListNames = new[] { "States" }, overwrite = true }]);
        Assert.True(replaced.GetProperty("success").GetBoolean(), replaced.GetRawText());
        var reimport = await WriteAsync(harness, [new { operationId = "import", operation = "import_text_lists", filePath = @"C:\Exports\States.xml" }]);
        Assert.Equal("engineering_rejected", Failure(reimport, 0));
    }

    [Fact]
    public async Task AMissingMemberIsApiMemberMissingAndAMissingListIsTargetNotFound()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

        var missing = await WriteAsync(harness, [new { operationId = "e", operation = "export_text_lists", directoryPath = @"C:\NoMember" }]);
        var unknown = await WriteAsync(harness, [new { operationId = "e", operation = "export_text_lists", directoryPath = @"C:\Exports", textListNames = new[] { "Absent" } }]);

        Assert.Equal("api_member_missing", Failure(missing, 0));
        Assert.Equal("target_not_found", Failure(unknown, 0));
    }

    [Fact]
    public void Catalog_RefusesBadShapes()
    {
        static string? Error(HmiWriteOperationRequest item) => HmiWriteCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("'directoryPath' is required", Error(new HmiWriteOperationRequest { OperationId = "e", Operation = "export_text_lists" }));
        Assert.Contains("'filePath' is required", Error(new HmiWriteOperationRequest { OperationId = "i", Operation = "import_text_lists" }));
        Assert.Contains("'directoryPath'", Error(new HmiWriteOperationRequest { OperationId = "e", Operation = "export_text_lists", DirectoryPath = @"\\?\C:\x" }));
        Assert.Contains("'textListNames' must hold", Error(new HmiWriteOperationRequest { OperationId = "e", Operation = "export_text_lists", DirectoryPath = @"C:\x", TextListNames = new() { "A", "a" } }));
        Assert.Contains("'textListNames' is not valid", Error(new HmiWriteOperationRequest { OperationId = "i", Operation = "import_text_lists", FilePath = @"C:\x.xml", TextListNames = new() { "A" } }));
        Assert.True(HmiWriteCatalog.ValidateWrite(new[] { new HmiWriteOperationRequest { OperationId = "i", Operation = "import_text_lists", FilePath = @"\\server\share\lists.xml", Overwrite = true } }).IsValid);
    }
}
