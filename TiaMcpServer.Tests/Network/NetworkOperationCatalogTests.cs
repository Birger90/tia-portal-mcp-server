using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class NetworkOperationCatalogTests
{
    [Theory]
    [InlineData(256, false)]
    [InlineData(257, false)]
    [InlineData(200000, false)]
    [InlineData(256, true)]
    [InlineData(257, true)]
    [InlineData(200000, true)]
    public void OperationNameDiagnosticLimit_PreservesBoundaryAndBoundsUnknownNames(int length, bool write)
    {
        var name = new string('n', length);
        var item = new NetworkOperationRequest { OperationId = "id", Operation = name };

        var validation = write ? NetworkOperationCatalog.ValidateWrite([item]) : NetworkOperationCatalog.ValidateRead([item]);

        Assert.False(validation.IsValid);
        Assert.Equal("validation_error", validation.Category);
        if (length <= 256)
            Assert.Contains(name, validation.Error);
        else
        {
            Assert.Equal("Each operation name must be at most 256 characters.", validation.Error);
            Assert.DoesNotContain(name, validation.Error);
        }
    }

    [Theory]
    [InlineData("inspect_network_object", false)]
    [InlineData("configure_network_device", false)]
    [InlineData("inspect_network_object", true)]
    [InlineData("configure_network_device", true)]
    public void QualifiedNodeTarget_AcceptsPreferredAndLegacyOwnerPaths(string operation, bool legacy)
    {
        var request = Configure();
        request.Operation = operation;
        request.Target!.Kind = "node";
        if (operation == "inspect_network_object") request.Changes = null;
        if (legacy)
        {
            request.Target.ItemPath = new[] { new NetworkDeviceItemPathSegment { Index = 0, Name = "X1", PositionNumber = 1, TypeIdentifier = "OrderNumber:X1" } };
            request.Target.NodeIndex = 0;
        }
        else
        {
            request.Target.InterfacePath = new[] { new NetworkInterfacePathSegment { Name = "X1", PositionNumber = 1 } };
            request.Target.InterfaceName = "X1";
        }
        var validation = operation == "inspect_network_object"
            ? NetworkOperationCatalog.ValidateRead(new[] { request }) : NetworkOperationCatalog.ValidateWrite(new[] { request });
        Assert.True(validation.IsValid, validation.Error);
    }

    [Theory]
    [InlineData("inspect_network_object", "both")]
    [InlineData("configure_network_device", "both")]
    [InlineData("inspect_network_object", "name")]
    [InlineData("configure_network_device", "name")]
    [InlineData("inspect_network_object", "index")]
    [InlineData("configure_network_device", "index")]
    [InlineData("inspect_network_object", "empty")]
    [InlineData("configure_network_device", "empty")]
    [InlineData("inspect_network_object", "legacy-missing-index")]
    [InlineData("configure_network_device", "legacy-missing-index")]
    public void QualifiedNodeTarget_ValidatesOwnerPath(string operation, string shape)
    {
        var request = Configure();
        request.Operation = operation;
        request.Target!.Kind = "node";
        if (operation == "inspect_network_object") request.Changes = null;
        if (shape is "both" or "legacy-missing-index")
            request.Target.ItemPath = new[] { new NetworkDeviceItemPathSegment { Index = 0, Name = "X1", PositionNumber = 1, TypeIdentifier = "OrderNumber:X1" } };
        if (shape == "both") request.Target.InterfacePath = new[] { new NetworkInterfacePathSegment { Name = "X1", PositionNumber = 1 } };
        if (shape == "empty") request.Target.InterfacePath = Array.Empty<NetworkInterfacePathSegment>();
        if (shape == "name") request.Target.InterfaceName = "X1";
        if (shape == "index") request.Target.NodeIndex = 0;
        var invalidTargetValidation = operation == "inspect_network_object"
            ? NetworkOperationCatalog.ValidateRead(new[] { request }) : NetworkOperationCatalog.ValidateWrite(new[] { request });
        Assert.False(invalidTargetValidation.IsValid);
    }
    private const int ExpectedMaxOperationIdLength = 256;

    private static NetworkOperationRequest Op(
        string id,
        string operation,
        Action<NetworkOperationRequest>? configure = null)
    {
        var request = new NetworkOperationRequest { OperationId = id, Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    /// <summary>A minimal valid configure_network_device operation: exact target, one change.</summary>
    private static NetworkOperationRequest Configure(
        string id = "cfg",
        string? deviceName = "PLC_1",
        string? nodeId = "7",
        Action<NetworkOperationRequest>? adjust = null)
    {
        var request = new NetworkOperationRequest
        {
            OperationId = id,
            Operation = "configure_network_device",
            Target = new NetworkObjectTarget { DeviceName = deviceName, NodeId = nodeId },
            Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" },
        };
        adjust?.Invoke(request);
        return request;
    }

    [Fact]
    public void All_MatchesTheDedicatedNetworkContract()
    {
        var expected = new Dictionary<string, (NetworkOperationCategory Category, string[] Required, string[] Optional)>
        {
            ["read_hardware_config"] = (NetworkOperationCategory.Read, Array.Empty<string>(), new[] { "deviceName", "plcName", "includeIoDetails", "includeTagMatches", "pageSize", "cursor" }),
            ["search_equipment_catalog"] = (NetworkOperationCategory.Read, new[] { "query" }, new[] { "maxResults" }),
            ["add_network_device"] = (NetworkOperationCategory.Write, new[] { "typeIdentifier", "deviceName" }, new[] { "deviceItemName" }),
            ["configure_network_device"] = (NetworkOperationCategory.Write, new[] { "target", "changes" }, Array.Empty<string>()),
            ["list_network_objects"] = (NetworkOperationCategory.Read, new[] { "objectKinds" }, new[] { "deviceName", "pageSize", "cursor" }),
            ["inspect_network_object"] = (NetworkOperationCategory.Read, new[] { "target" }, new[] { "attributeNames" }),
            ["create_subnet"] = (NetworkOperationCategory.Write, new[] { "subnet" }, Array.Empty<string>()),
            ["update_subnet"] = (NetworkOperationCategory.Write, new[] { "target", "subnetChanges" }, Array.Empty<string>()),
            ["delete_subnet"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
            ["create_io_system"] = (NetworkOperationCategory.Write, new[] { "target" }, new[] { "ioSystem" }),
            ["list_plug_locations"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["find_module_types"] = (NetworkOperationCategory.Read, new[] { "typeIdentifier" }, Array.Empty<string>()),
            ["find_container_types"] = (NetworkOperationCategory.Read, new[] { "typeIdentifier" }, Array.Empty<string>()),
            ["list_io_addresses"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["plug_device_item"] = (NetworkOperationCategory.Write, new[] { "target", "typeIdentifier", "positionNumber" }, new[] { "deviceItemName" }),
            ["move_device_item"] = (NetworkOperationCategory.Write, new[] { "target", "positionNumber" }, new[] { "destination" }),
            ["copy_device_item"] = (NetworkOperationCategory.Write, new[] { "target", "positionNumber" }, new[] { "destination" }),
            ["delete_device_item"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
            ["change_device_item_type"] = (NetworkOperationCategory.Write, new[] { "target", "typeIdentifier" }, Array.Empty<string>()),
            ["set_hardware_attributes"] = (NetworkOperationCategory.Write, new[] { "target", "attributes" }, new[] { "attributeErrorPolicy", "attributeWriteMode" }),
            ["set_io_address"] = (NetworkOperationCategory.Write, new[] { "target", "address", "addressChanges" }, Array.Empty<string>()),
            ["get_custom_identity"] = (NetworkOperationCategory.Read, new[] { "target", "key" }, Array.Empty<string>()),
            ["set_custom_identity"] = (NetworkOperationCategory.Write, new[] { "target", "key", "value" }, Array.Empty<string>()),
            ["remove_custom_identity"] = (NetworkOperationCategory.Write, new[] { "target", "key" }, Array.Empty<string>()),
            ["list_port_connections"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["connect_ports"] = (NetworkOperationCategory.Write, new[] { "target", "partner" }, Array.Empty<string>()),
            ["disconnect_ports"] = (NetworkOperationCategory.Write, new[] { "target", "partner" }, Array.Empty<string>()),
            ["list_transfer_areas"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["create_transfer_area"] = (NetworkOperationCategory.Write, new[] { "target", "transferArea" }, new[] { "partner" }),
            ["update_transfer_area"] = (NetworkOperationCategory.Write, new[] { "target", "transferArea", "attributes" }, Array.Empty<string>()),
            ["delete_transfer_area"] = (NetworkOperationCategory.Write, new[] { "target", "transferArea" }, Array.Empty<string>()),
            ["delete_io_system"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
            ["disconnect_from_io_system"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
            ["export_cax"] = (NetworkOperationCategory.Write, new[] { "filePath" }, new[] { "deviceName", "logFilePath", "overwrite" }),
            ["import_cax"] = (NetworkOperationCategory.Write, new[] { "filePath", "importOption" }, new[] { "logFilePath", "overwrite" }),
            ["export_io_map"] = (NetworkOperationCategory.Write, new[] { "filePath" }, new[] { "deviceName", "plcName", "includeTagMatches", "overwrite", "csvDelimiter", "rawCells" }),
            // Package G hardware features.
            ["get_interface_nodes"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["create_subnet_and_connect_node"] = (NetworkOperationCategory.Write, new[] { "target", "newSubnetName" }, new[] { "nodeName" }),
            ["disconnect_node_from_subnets"] = (NetworkOperationCategory.Write, new[] { "target" }, new[] { "nodeName" }),
            ["get_io_controller"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["get_im_partner_port"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["connect_im_port"] = (NetworkOperationCategory.Write, new[] { "target", "partner" }, Array.Empty<string>()),
            ["disconnect_im_port"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
            ["get_pc_interface_assignment"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["assign_pc_interface"] = (NetworkOperationCategory.Write, new[] { "target", "assignmentMode" }, new[] { "partner" }),
            ["get_module_description_update"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["update_module_description"] = (NetworkOperationCategory.Write, new[] { "target" }, Array.Empty<string>()),
            ["export_telecontrol_data_points"] = (NetworkOperationCategory.Write, new[] { "target", "filePath" }, new[] { "overwrite" }),
            ["import_telecontrol_data_points"] = (NetworkOperationCategory.Write, new[] { "target", "filePath" }, Array.Empty<string>()),
            ["list_communication_connections"] = (NetworkOperationCategory.Read, new[] { "target" }, Array.Empty<string>()),
            ["create_communication_connection"] = (NetworkOperationCategory.Write, new[] { "target", "partner", "connectionType" }, new[] { "nodeName", "partnerNodeName" }),
            ["delete_communication_connection"] = (NetworkOperationCategory.Write, new[] { "target", "connectionIndex" }, new[] { "connectionName" }),
        };

        var actual = NetworkOperationCatalog.All.ToDictionary(spec => spec.Name, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.OrderBy(name => name), actual.Keys.OrderBy(name => name));
        foreach (var (name, expectedSpec) in expected)
        {
            var actualSpec = actual[name];
            Assert.Equal(expectedSpec.Category, actualSpec.Category);
            Assert.Equal(expectedSpec.Required, actualSpec.RequiredFields);
            Assert.Equal(expectedSpec.Optional, actualSpec.OptionalFields);
        }
    }

    [Fact]
    public void ValidateRead_RejectsEmptyAndOverFiftyItemBatches()
    {
        var empty = NetworkOperationCatalog.ValidateRead(Array.Empty<NetworkOperationRequest>());
        var oversized = NetworkOperationCatalog.ValidateRead(
            Enumerable.Range(0, OperationBatchRules.MaxItems + 1)
                .Select(index => Op($"id{index}", "read_hardware_config"))
                .ToArray());

        Assert.False(empty.IsValid);
        Assert.Contains("at least one", empty.Error);
        Assert.False(oversized.IsValid);
        Assert.Contains("50", oversized.Error);
        Assert.Equal("Batch must contain at least one operation.", NetworkOperationCatalog.ValidateRead(null).Error);
    }

    [Fact]
    public void ValidateRead_RejectsDuplicateIdsAndWriteOperations()
    {
        var duplicates = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("same", "read_hardware_config"),
            Op("same", "search_equipment_catalog", operation => operation.Query = "CPU"),
        });
        var wrongCategory = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("w1", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
            }),
        });

        Assert.False(duplicates.IsValid);
        Assert.Contains("same", duplicates.Error);
        Assert.False(wrongCategory.IsValid);
        Assert.Contains("write", wrongCategory.Error);
    }

    [Fact]
    public void ValidateRead_BoundsOperationIdBeforeItCanInflateAResponseItem()
    {
        var exact = NetworkOperationCatalog.ValidateRead(new[]
        {
            // Control characters expand to six-character JSON escapes, so this exercises the
            // worst practical canonical rendering while remaining inside the identifier bound.
            Op(new string('\u0001', ExpectedMaxOperationIdLength), "read_hardware_config"),
        });
        var oversized = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op(new string('x', ExpectedMaxOperationIdLength + 1), "read_hardware_config"),
        });

        Assert.True(exact.IsValid, exact.Error);
        Assert.False(oversized.IsValid);
        Assert.Contains($"at most {ExpectedMaxOperationIdLength} characters", oversized.Error);
        Assert.DoesNotContain(new string('x', ExpectedMaxOperationIdLength + 1), oversized.Error);
    }

    [Fact]
    public void ValidateRead_HardwarePaginationAcceptsCursorOnlyAndBoundsPageSize()
    {
        var cursorOnly = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("cursor", "read_hardware_config", operation => operation.Cursor = "opaque"),
        });
        var invalidPageSize = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("bounds", "read_hardware_config", operation => operation.PageSize = 201),
        });

        Assert.True(cursorOnly.IsValid, cursorOnly.Error);
        Assert.False(invalidPageSize.IsValid);
        Assert.Contains("pageSize", invalidPageSize.Error);
    }

    [Fact]
    public void ValidateRead_HardwarePaginationFieldsAreInvalidForOtherOperations()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("catalog", "search_equipment_catalog", operation =>
            {
                operation.Query = "CPU";
                operation.PageSize = 10;
                operation.Cursor = "opaque";
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("pageSize", result.Error);
        Assert.Contains("cursor", result.Error);
    }

    [Fact]
    public void ValidateRead_RejectsMissingAndInapplicableFieldsAndZeroMaxResults()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            Op("missing", "search_equipment_catalog"),
            Op("inapplicable", "read_hardware_config", operation => operation.Query = "CPU"),
            Op("bounds", "search_equipment_catalog", operation =>
            {
                operation.Query = "CPU";
                operation.MaxResults = 0;
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("query", result.Error);
        Assert.Contains("not valid", result.Error);
        Assert.Contains("maxResults", result.Error);
        Assert.Contains("1 or greater", result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsAnExactTargetWithOneChange()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[] { Configure() });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsConfigureWithoutTargetOrChanges()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("w1", "configure_network_device"),
        });

        Assert.False(result.IsValid);
        Assert.Contains("target", result.Error);
        Assert.Contains("changes", result.Error);
    }

    [Fact]
    public void ValidateWrite_RequiresTargetAndChangesOnlyForConfigure()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("add", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWrite_RejectsBlankDeviceItemNameOnAddNetworkDevice(string deviceItemName)
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("add", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
                operation.DeviceItemName = deviceItemName;
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("'deviceItemName' must not be blank when supplied.", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsTargetAndChangesOnAddNetworkDevice()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Op("add", "add_network_device", operation =>
            {
                operation.TypeIdentifier = "OrderNumber:6ES7";
                operation.DeviceName = "PLC_1";
                operation.Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "7" };
                operation.Changes = new NetworkDeviceChanges { IpAddress = "192.168.0.10" };
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("'target' is not valid", result.Error);
        Assert.Contains("'changes' is not valid", result.Error);
    }

    [Theory]
    [InlineData(null, "7", "deviceName")]
    [InlineData("   ", "7", "deviceName")]
    [InlineData("PLC_1", null, "nodeId")]
    [InlineData("PLC_1", "   ", "nodeId")]
    public void ValidateWrite_RejectsBlankTargetIdentities(string? deviceName, string? nodeId, string expected)
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation =>
                operation.Target = new NetworkObjectTarget { DeviceName = deviceName, NodeId = nodeId }),
        });

        Assert.False(result.IsValid);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsChangesThatRequestNothing()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges()),
        });

        Assert.False(result.IsValid);
        Assert.Contains("at least one change", result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsPnDeviceNameAutoGenerationAsAChange()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges { PnDeviceNameAutoGeneration = false }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsBlankScalarChanges()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges { IpAddress = "  " }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("ipAddress", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsSubnetSelectorWithoutSubnetId()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                Subnet = new NetworkSubnetTarget { SubnetId = "  " },
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("subnet.subnetId", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsIoSystemSelectorMissingSubnetIdOrNumber()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                IoSystem = new NetworkIoSystemTarget(),
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("ioSystem.subnetId", result.Error);
        Assert.Contains("ioSystem.number", result.Error);
    }

    [Fact]
    public void ValidateWrite_RejectsIoSystemSubnetIdThatDisagreesWithTheSubnetChange()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                Subnet = new NetworkSubnetTarget { SubnetId = "S1" },
                IoSystem = new NetworkIoSystemTarget { SubnetId = "S2", Number = 100 },
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains("same subnet", result.Error);
    }

    [Fact]
    public void ValidateWrite_AcceptsMatchingSubnetAndIoSystemSelectors()
    {
        var result = NetworkOperationCatalog.ValidateWrite(new[]
        {
            Configure(adjust: operation => operation.Changes = new NetworkDeviceChanges
            {
                Subnet = new NetworkSubnetTarget { SubnetId = "S1" },
                IoSystem = new NetworkIoSystemTarget { SubnetId = "S1", Number = 100 },
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void ValidateAccessMode_ReturnsOneDeniedItemError()
    {
        var errors = NetworkOperationCatalog.ValidateAccessMode(new[]
        {
            Op("read", "read_hardware_config"),
            Configure("write"),
        }, McpAccessMode.ReadOnly);

        Assert.Single(errors);
        Assert.Contains("configure_network_device", errors[0]);
        Assert.Contains("write", errors[0]);
    }
}
