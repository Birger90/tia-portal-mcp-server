using System.Text.Json;
using ModelContextProtocol.Client;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Hmi;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.UnifiedExtensions;

/// <summary>Unified audit classes, runtime settings and plant views (issue #29) through MCP and the FakeWorker.</summary>
[Collection("Mcp protocol serial")]
public class HmiUnifiedConfigurationMcpTests
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

    [Fact]
    public async Task AuditRuntimeAndPlantViewWritesRunAndPlantViewsAreRead()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);
        object[] writes =
        [
            new { operationId = "audit", operation = "create_audit_class", hmiName = "H", auditClassName = "Audit_2" },
            new { operationId = "comment", operation = "set_audit_class_property", hmiName = "H", auditClassName = "Audit_2", propertyName = "CommentRequired", value = "true" },
            new { operationId = "start", operation = "set_runtime_setting", hmiName = "H", propertyName = "StartScreen", value = "Detail" },
            new { operationId = "view", operation = "create_plant_view", hmiName = "H", plantViewName = "Area" },
            new { operationId = "node", operation = "create_plant_view_node", hmiName = "H", plantViewName = "Area", nodeName = "Pump", plantObjectType = "Motor" },
            new { operationId = "iface", operation = "set_plant_object_interface_property", hmiName = "H", plantViewName = "Plant", nodePath = "Line1", interfaceName = "Speed", propertyName = "PlcTag", value = "DB1.Speed" },
            new { operationId = "drop", operation = "delete_plant_view_node", hmiName = "H", plantViewName = "Plant", nodePath = "Line1" },
        ];

        var preview = await CallAsync(harness, "hmi_write", writes, dryRun: true);
        Assert.True(preview.GetProperty("success").GetBoolean(), preview.GetRawText());
        var guards = preview.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()).ToList();
        Assert.Contains(HmiGuardDefinitions.AuditPropertyUnverified, guards);
        Assert.Contains(HmiGuardDefinitions.DeletesConfiguration, guards);
        Assert.Equal("audit", preview.GetProperty("effects")[1].GetProperty("effect").GetProperty("dependsOn").GetString());
        Assert.Equal("view", preview.GetProperty("effects")[4].GetProperty("effect").GetProperty("dependsOn").GetString());

        var applied = await CallAsync(harness, "hmi_write", writes);
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());

        var read = await CallAsync(harness, "hmi_read", [new { operationId = "p", operation = "list_plant_views", hmiName = "H" }]);
        var views = Item(read, 0).GetProperty("result").GetProperty("plantViews").EnumerateArray().ToList();
        Assert.Equal("Pump", views.Single(v => v.GetProperty("name").GetString() == "Area").GetProperty("nodes")[0].GetProperty("nodePath").GetString());
        Assert.Empty(views.Single(v => v.GetProperty("name").GetString() == "Plant").GetProperty("nodes").EnumerateArray());
    }

    [Fact]
    public async Task MissingProviderIsApiMemberMissingAndTheCatalogNamesDocumentedProperties()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

        var missing = await CallAsync(harness, "hmi_write", [new { operationId = "v", operation = "create_plant_view", hmiName = "H", plantViewName = "NoProvider" }]);

        Assert.Equal("api_member_missing", Item(missing, 0).GetProperty("failure").GetProperty("category").GetString());
        static string? Error(HmiWriteOperationRequest item) => HmiWriteCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("documented HmiAuditClass property", Error(new() { OperationId = "a", Operation = "set_audit_class_property", AuditClassName = "A", PropertyName = "IsGmpEnabled", Value = "true" }));
        Assert.Contains("documented PlantObjectInterface property", Error(new() { OperationId = "a", Operation = "set_plant_object_interface_property", PlantViewName = "P", NodePath = "N", InterfaceName = "I", PropertyName = "Members", Value = "x" }));
        Assert.Contains("'settingsSection' must be one of", Error(new() { OperationId = "a", Operation = "set_runtime_setting", SettingsSection = "Nope", PropertyName = "X", Value = "1" }));
        Assert.Contains("'nodeName' is invalid", Error(new() { OperationId = "a", Operation = "create_plant_view_node", PlantViewName = "P", NodeName = "a/b" }));
        Assert.True(HmiWriteCatalog.ValidateWrite(new[] { new HmiWriteOperationRequest { OperationId = "a", Operation = "set_runtime_setting", SettingsSection = "OpcUaServerRuntimeSettings", PropertyName = "Port", Value = "4840" } }).IsValid);
    }
}
