using System.Security.Cryptography;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// End-to-end hardware-module, tag-move and I/O-map operations through the MCP surface against the
/// stateful <c>hardware-modules</c> FakeWorker fixture (object access only; no Siemens evidence).
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class HardwareModuleMcpTests
{
    private const string Scenario = "hardware-modules";

    private static object Rack => new { kind = "deviceItem", deviceName = "PLC_1", itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500") } };

    private static object Module(int index, string name, int position, string type)
        => new { kind = "deviceItem", deviceName = "PLC_1", itemPath = new[] { Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(index, name, position, type) } };

    private static object Di => Module(1, "DI 16x24VDC_1", 2, "OrderNumber:6ES7 521-1BH00-0AB0/V2.1");

    private static object Dq => Module(2, "DQ 16x24VDC_1", 3, "OrderNumber:6ES7 522-1BH00-0AB0/V2.1");

    private static object Segment(int index, string name, int position, string type) => new { index, name, positionNumber = position, typeIdentifier = type };

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Result(JsonElement root, int index = 0)
    {
        var item = root.GetProperty("batch").GetProperty("operations")[index];
        Assert.True(item.GetProperty("status").GetString() == "succeeded", item.GetRawText());
        return item.GetProperty("result");
    }

    private static Task<McpProtocolTestHarness> StartAsync(TempAuditDirectory audit)
        => McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, Scenario);

    private static string[] GuardIds(JsonElement root) => root.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()!).ToArray();

    [Fact]
    public async Task NetworkRead_ListsPlugLocationsModuleTypesAndAddresses()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_read", new object[]
        {
            new { operationId = "free", operation = "list_plug_locations", target = Rack },
            new { operationId = "types", operation = "find_module_types", typeIdentifier = "OrderNumber:6ES7 521-1BH00" },
            new { operationId = "containers", operation = "find_container_types", typeIdentifier = "OrderNumber:6ES7 521-1BH00-0AB0/V2.1" },
            new { operationId = "addresses", operation = "list_io_addresses", target = Di },
            new { operationId = "device", operation = "list_plug_locations", target = new { kind = "device", deviceName = "PLC_1" } },
        });

        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal(new[] { 4, 5, 6, 7, 8 }, Result(root, 0).GetProperty("plugLocations").EnumerateArray().Select(p => p.GetProperty("positionNumber").GetInt32()));
        Assert.Equal(2, Result(root, 1).GetProperty("moduleTypes").GetArrayLength());
        Assert.Equal("System:Rack.S71500", Result(root, 2).GetProperty("containerTypes")[0].GetString());
        var address = Result(root, 3).GetProperty("addresses")[0];
        Assert.Equal(("Input", 0, 2), (address.GetProperty("ioType").GetString(), address.GetProperty("startAddress").GetInt32(), address.GetProperty("length").GetInt32()));
        Assert.Empty(Result(root, 4).GetProperty("target").GetProperty("itemPath").EnumerateArray());
    }

    [Fact]
    public async Task Plug_PreviewsFeasibilityAppliesAndIsVisibleToTheNextRead()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var plug = new { operationId = "plug", operation = "plug_device_item", target = Rack, typeIdentifier = "OrderNumber:6ES7 521-1BL00-0AB0/V2.1", positionNumber = 4, deviceItemName = "DI 32_1" };

        var preview = await CallAsync(harness, "network_write", new object[] { plug }, dryRun: true);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var effect = preview.GetProperty("effects")[0].GetProperty("effect");
        Assert.True(effect.GetProperty("hardware").GetProperty("canPlug").GetBoolean());
        Assert.Equal("true", effect.GetProperty("currentSettings").GetProperty("canPlug").GetString());
        Assert.Equal("4", effect.GetProperty("requestedSettings").GetProperty("positionNumber").GetString());

        var applied = await CallAsync(harness, "network_write", new object[] { plug });
        Assert.Equal("applied", applied.GetProperty("phase").GetString());
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        var created = Result(applied).GetProperty("result");
        Assert.Equal(("DI 32_1", 4), (created.GetProperty("name").GetString(), created.GetProperty("positionNumber").GetInt32()));
        Assert.Equal("trusted", applied.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());

        var read = await CallAsync(harness, "network_read", new object[] { new { operationId = "hw", operation = "read_hardware_config" } });
        var rack = Result(read).GetProperty("devices")[0].GetProperty("items")[0];
        Assert.Equal(4, rack.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Plug_IntoAnOccupiedPositionFailsBeforeAnyMutation()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "plug", operation = "plug_device_item", target = Rack, typeIdentifier = "OrderNumber:6ES7 521-1BL00-0AB0/V2.1", positionNumber = 2 },
        });
        Assert.Equal("error", root.GetProperty("phase").GetString());
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("CanPlugNew returned false", root.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task MoveCopyChangeTypeAndDelete_RunInOneCallWithTheirGuards()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "copy", operation = "copy_device_item", target = Di, positionNumber = 5 },
            new { operationId = "type", operation = "change_device_item_type", target = Dq, typeIdentifier = "OrderNumber:6ES7 522-1BL01-0AB0/V2.1" },
            new { operationId = "move", operation = "move_device_item", target = Di, positionNumber = 6 },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Contains("network_changes_device_item_type", GuardIds(root));
        Assert.Equal(6, Result(root, 2).GetProperty("result").GetProperty("positionNumber").GetInt32());

        // The copy kept index 3; the moved DI is now the last item of the rack.
        var delete = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "delete", operation = "delete_device_item", target = Module(1, "DQ 16x24VDC_1", 3, "OrderNumber:6ES7 522-1BL01-0AB0/V2.1") },
        }, dryRun: true);
        Assert.Contains("network_deletes_device_item", GuardIds(delete));
        Assert.Equal("1", delete.GetProperty("effects")[0].GetProperty("effect").GetProperty("currentSettings").GetProperty("removedItemCount").GetString());
    }

    [Fact]
    public async Task SetHardwareAttributes_ConvertsTypedValuesAndHonoursThePolicy()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new
            {
                operationId = "attrs", operation = "set_hardware_attributes", target = Di, attributeErrorPolicy = "ignore",
                attributes = new object[]
                {
                    new { name = "IsochronousTi", kind = "integer", value = 5 },
                    new { name = "IsochronousMode", kind = "boolean", value = true },
                    new { name = "Comment", kind = "string", value = "reject" },
                },
            },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Contains("network_attribute_errors_ignored", GuardIds(root));
        var outcomes = Result(root).GetProperty("attributes");
        Assert.Equal(new[] { "applied", "applied", "ignored" }, outcomes.EnumerateArray().Select(o => o.GetProperty("status").GetString()));
        Assert.Equal("System.Double", outcomes[0].GetProperty("targetType").GetString());

        var unknown = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "bad", operation = "set_hardware_attributes", target = Di, attributes = new object[] { new { name = "TypeName", kind = "string", value = "x" } } },
        }, dryRun: true);
        Assert.Equal("validation_error", unknown.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("not writable", unknown.GetProperty("error").GetProperty("message").GetString());

        var abort = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "abort", operation = "set_hardware_attributes", target = Di, attributes = new object[] { new { name = "Comment", kind = "string", value = "reject" } } },
        });
        Assert.False(abort.GetProperty("success").GetBoolean());
        Assert.Equal("engineering_rejected", abort.GetProperty("batch").GetProperty("operations")[0].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task SetIoAddress_WarnsThatTagsAreNotRewiredAndReadsBack()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "addr", operation = "set_io_address", target = Di, address = new { ioType = "Input", startAddress = 0 }, addressChanges = new { startAddress = 10 } },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var guard = root.GetProperty("guards").EnumerateArray().Single(g => g.GetProperty("id").GetString() == "network_io_address_tags_not_rewired");
        Assert.Contains("'Start' (%I0.0)", guard.GetProperty("message").GetString());
        Assert.Contains("'Stop' (%I0.1)", guard.GetProperty("message").GetString());
        Assert.Equal(10, Result(root).GetProperty("after")[0].GetProperty("startAddress").GetInt32());
    }

    [Fact]
    public async Task ExportIoMap_WritesTheFileAndRefusesToOverwriteUnlessAsked()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var directory = Directory.CreateTempSubdirectory("hw-io-map-").FullName;
        try
        {
            var path = Path.Combine(directory, "io-map.csv");
            var first = await CallAsync(harness, "network_write", new object[] { new { operationId = "export", operation = "export_io_map", filePath = path } });
            Assert.True(first.GetProperty("success").GetBoolean(), first.GetRawText());
            var result = Result(first);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.GetProperty("sha256").GetString());
            Assert.Equal(2, result.GetProperty("addressRowCount").GetInt32());
            Assert.Equal(3, result.GetProperty("tagMatchCount").GetInt32());
            Assert.Contains(",Start,Bool,%I0.0,Default tag table,/", File.ReadAllText(path));

            var refused = await CallAsync(harness, "network_write", new object[] { new { operationId = "again", operation = "export_io_map", filePath = path } });
            Assert.Equal("validation_error", refused.GetProperty("error").GetProperty("category").GetString());
            Assert.Contains("overwrite:true", refused.GetProperty("error").GetProperty("message").GetString());

            var replaced = await CallAsync(harness, "network_write", new object[] { new { operationId = "replace", operation = "export_io_map", filePath = path, overwrite = true, csvDelimiter = ";" } });
            Assert.True(replaced.GetProperty("success").GetBoolean(), replaced.GetRawText());
            Assert.Contains("network_export_overwrites_file", GuardIds(replaced));
            Assert.True(Result(replaced).GetProperty("overwritten").GetBoolean());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlcRead_ReportsOccupancyAndTheNextFreeAddress()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var root = await CallAsync(harness, "plc_read", new object[]
        {
            new { operationId = "map", operation = "read_address_occupancy" },
            new { operationId = "free", operation = "find_free_address", area = "M", dataType = "Int", startByte = 10 },
            new { operationId = "input", operation = "find_free_address", area = "I", dataType = "Bool", requireHardware = true },
        });
        Assert.True(root.GetProperty("success").GetBoolean(), root.GetRawText());
        var map = Result(root, 0);
        Assert.Equal("Overlap", map.GetProperty("overlaps")[0].GetProperty("secondTag").GetString());
        Assert.Equal("Orphan", map.GetProperty("tagsOutsideHardware")[0].GetProperty("tagName").GetString());
        Assert.Equal("Symbolic", map.GetProperty("unparsedTags")[0].GetProperty("tagName").GetString());
        Assert.Equal("%MW12", Result(root, 1).GetProperty("address").GetString());
        Assert.Equal("%I0.2", Result(root, 2).GetProperty("address").GetString());
    }

    [Fact]
    public async Task PlcWrite_MoveTagIsAGuardedRecreateVisibleToTheNextRead()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await StartAsync(audit);
        var move = new { operationId = "move", operation = "move_tag", tableName = "Default tag table", name = "Speed", targetTableName = "Motors" };
        var preview = await CallAsync(harness, "plc_write", new object[] { move }, dryRun: true);
        Assert.Contains("plc_move_tag_recreates", GuardIds(preview));
        Assert.Equal("/Motors", preview.GetProperty("effects")[0].GetProperty("effect").GetProperty("placement").GetString());

        var applied = await CallAsync(harness, "plc_write", new object[] { move });
        Assert.True(applied.GetProperty("success").GetBoolean(), applied.GetRawText());
        Assert.StartsWith("__mcp_move_", Result(applied).GetProperty("temporaryName").GetString());
        // K24: the move is re-read (gone from the source, in the target with the reported data type and address), not trusted.
        var check = applied.GetProperty("verification").GetProperty("operations")[0];
        Assert.Equal(("tag", true), (check.GetProperty("check").GetString(), check.GetProperty("success").GetBoolean()));
        Assert.StartsWith("/Default tag table/Speed absent; /Motors/Speed exists dataType=", check.GetProperty("actual").GetString());

        var read = await CallAsync(harness, "plc_read", new object[] { new { operationId = "tags", operation = "list_tag_tables", tableName = "Motors" } });
        var motors = Result(read).GetProperty("plcs")[0].GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Motors");
        Assert.Equal("Speed", motors.GetProperty("tags")[0].GetProperty("name").GetString());

        var same = await CallAsync(harness, "plc_write", new object[]
        {
            new { operationId = "same", operation = "move_tag", tableName = "Motors", name = "Speed", targetTableName = "Motors" },
        }, dryRun: true);
        Assert.Equal("validation_error", same.GetProperty("error").GetProperty("category").GetString());
    }
}

/// <summary>Every hardware-module field reaches the worker inside WorkerRequest.HardwareModule (echo fixture).</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class HardwareModuleForwardingTests
{
    [Fact]
    public async Task EveryFieldIsForwardedInTheNestedRequest()
    {
        var binding = new TiaMcpServer.Contracts.Project.ProjectSessionBinding(null);
        using var client = new TiaMcpServer.Worker.OpennessWorkerClient(binding, logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await TiaMcpServer.Tests.Worker.FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");
        using var value = JsonDocument.Parse("2.5");
        var operation = new TiaMcpServer.Network.NetworkOperationRequest
        {
            OperationId = "op", Operation = "set_hardware_attributes",
            Target = new() { Kind = "deviceItem", DeviceName = "PLC_1", ItemPath = new[] { new TiaMcpServer.Network.NetworkDeviceItemPathSegment { Index = 0, Name = "Rack_0", PositionNumber = 0, TypeIdentifier = "System:Rack.S71500" } } },
            Attributes = new[] { new TiaMcpServer.Network.HardwareAttributeAssignment { Name = "IsochronousTi", Kind = "number", Value = value.RootElement.Clone() } },
            AttributeErrorPolicy = "ignore",
            AttributeWriteMode = "sequential",
        };

        var result = await TiaMcpServer.Network.HardwareModuleInvoker.InvokeAsync(client, operation);

        Assert.True(result.Success, result.Error);
        var request = JsonSerializer.Deserialize<TiaMcpServer.Contracts.Worker.WorkerRequest>(result.Payload, TiaMcpServer.Contracts.Json.WorkerJson.Envelope)!;
        Assert.Equal("set_hardware_attributes", request.Method);
        var forwarded = request.HardwareModule!;
        Assert.Equal("Rack_0", Assert.Single(forwarded.Target!.ItemPath!).Name);
        var attribute = Assert.Single(forwarded.Attributes!);
        Assert.Equal(("IsochronousTi", "number", 2.5), (attribute.Name, attribute.Kind, attribute.NumberValue!.Value));
        Assert.Equal(("ignore", "sequential"), (forwarded.AttributeErrorPolicy, forwarded.AttributeWriteMode));

        var read = await TiaMcpServer.Network.HardwareModuleInvoker.InvokeAsync(client, new TiaMcpServer.Network.NetworkOperationRequest
        {
            OperationId = "types", Operation = "find_module_types", TypeIdentifier = "OrderNumber:6ES7",
        });
        var readRequest = JsonSerializer.Deserialize<TiaMcpServer.Contracts.Worker.WorkerRequest>(read.Payload, TiaMcpServer.Contracts.Json.WorkerJson.Envelope)!;
        Assert.Equal("OrderNumber:6ES7", readRequest.HardwareModule!.TypeIdentifier);
    }
}
