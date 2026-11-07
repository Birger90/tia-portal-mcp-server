using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>Siemens-free rules of the hardware-module operations: value conversion, request validation, CSV projection.</summary>
public sealed class HardwareModuleUnitTests
{
    private static HardwareAttributeValueInfo Value(string kind, object? value) => kind switch
    {
        "string" => new() { Name = "A", Kind = kind, StringValue = (string?)value },
        "boolean" => new() { Name = "A", Kind = kind, BooleanValue = (bool?)value },
        "integer" => new() { Name = "A", Kind = kind, IntegerValue = value is null ? null : Convert.ToInt64(value) },
        "number" => new() { Name = "A", Kind = kind, NumberValue = value is null ? null : Convert.ToDouble(value) },
        _ => new() { Name = "A", Kind = kind, EnumSymbol = (string?)value },
    };

    private enum SampleMode { Automatic, Manual }

    [Fact]
    public void Converter_BooleanAndStringNeedTheirDeclaredTypes()
    {
        Assert.Equal(true, HardwareAttributeValueConverter.Convert(Value("boolean", true), new[] { typeof(bool) }).Value);
        Assert.Equal("x", HardwareAttributeValueConverter.Convert(Value("string", "x"), new[] { typeof(string) }).Value);
        var refused = HardwareAttributeValueConverter.Convert(Value("boolean", true), new[] { typeof(string) });
        Assert.False(refused.Success);
        Assert.Contains("System.String", refused.Error);
    }

    [Fact]
    public void Converter_IntegerTakesTheFirstIntegralTypeThatHoldsIt()
    {
        var narrow = HardwareAttributeValueConverter.Convert(Value("integer", 300), new[] { typeof(byte), typeof(int) });
        Assert.True(narrow.Success);
        Assert.Equal(typeof(int), narrow.TargetType);
        Assert.Equal(300, narrow.Value);
        Assert.False(HardwareAttributeValueConverter.Convert(Value("integer", -1), new[] { typeof(uint) }).Success);
        var floating = HardwareAttributeValueConverter.Convert(Value("integer", 1000), new[] { typeof(double) });
        Assert.Equal(1000d, floating.Value);
    }

    [Fact]
    public void Converter_NumberGoesOnlyToFloatingTypes()
    {
        Assert.Equal(0.5, HardwareAttributeValueConverter.Convert(Value("number", 0.5), new[] { typeof(double) }).Value);
        Assert.Equal(0.5f, HardwareAttributeValueConverter.Convert(Value("number", 0.5), new[] { typeof(float) }).Value);
        var refused = HardwareAttributeValueConverter.Convert(Value("number", 1.5), new[] { typeof(int) });
        Assert.False(refused.Success);
        Assert.Contains("kind 'integer'", refused.Error);
    }

    [Fact]
    public void Converter_EnumNeedsExactlyOneDefiningTypeAndExactCase()
    {
        var parsed = HardwareAttributeValueConverter.Convert(Value("enum", "Manual"), new[] { typeof(SampleMode) });
        Assert.Equal(SampleMode.Manual, parsed.Value);
        var wrongCase = HardwareAttributeValueConverter.Convert(Value("enum", "manual"), new[] { typeof(SampleMode) });
        Assert.False(wrongCase.Success);
        Assert.Contains("Automatic, Manual", wrongCase.Error);
    }

    [Fact]
    public void ReadBackEquals_ComparesNumericFamiliesAndEnumsExactly()
    {
        Assert.True(HardwareAttributeValueConverter.ReadBackEquals(5, 5L));
        Assert.True(HardwareAttributeValueConverter.ReadBackEquals(SampleMode.Manual, SampleMode.Manual));
        Assert.False(HardwareAttributeValueConverter.ReadBackEquals(SampleMode.Manual, 1));
        Assert.False(HardwareAttributeValueConverter.ReadBackEquals("1", 1));
        Assert.True(HardwareAttributeValueConverter.ReadBackEquals(null, null));
    }

    private static NetworkOperationRequest Item(string operation, Action<NetworkOperationRequest>? configure = null)
    {
        var request = new NetworkOperationRequest { OperationId = "op", Operation = operation };
        configure?.Invoke(request);
        return request;
    }

    private static NetworkObjectTarget DeviceItem(params (int Index, string Name, int Position, string Type)[] path) => new()
    {
        Kind = "deviceItem", DeviceName = "PLC_1",
        ItemPath = path.Select(segment => new NetworkDeviceItemPathSegment
        {
            Index = segment.Index, Name = segment.Name, PositionNumber = segment.Position, TypeIdentifier = segment.Type,
        }).ToArray(),
    };

    private static NetworkObjectTarget Rack => DeviceItem((0, "Rack_0", 0, "System:Rack.S71500"));

    private static string? Errors(NetworkOperationRequest request)
    {
        var category = HardwareModuleMethods.Write.Contains(request.Operation) || request.Operation == HardwareModuleMethods.ExportIoMap
            ? NetworkOperationCatalog.ValidateWrite(new[] { request })
            : NetworkOperationCatalog.ValidateRead(new[] { request });
        return category.IsValid ? null : category.Error;
    }

    [Fact]
    public void Validation_AcceptsWellFormedHardwareItems()
    {
        Assert.Null(Errors(Item("plug_device_item", r => { r.Target = Rack; r.TypeIdentifier = "OrderNumber:6ES7 521-1BH00-0AB0/V2.1"; r.PositionNumber = 4; })));
        Assert.Null(Errors(Item("plug_device_item", r => { r.Target = new() { Kind = "device", DeviceName = "PLC_1" }; r.TypeIdentifier = "System:Rack.S71500"; r.PositionNumber = 0; })));
        Assert.Null(Errors(Item("move_device_item", r => { r.Target = DeviceItem((0, "Rack_0", 0, "System:Rack.S71500"), (1, "DI", 2, "OrderNumber:X")); r.PositionNumber = 5; })));
        Assert.Null(Errors(Item("list_plug_locations", r => r.Target = new() { Kind = "device", DeviceName = "PLC_1" })));
        Assert.Null(Errors(Item("find_module_types", r => r.TypeIdentifier = "OrderNumber:6ES7 521")));
        using var value = JsonDocument.Parse("true");
        Assert.Null(Errors(Item("set_hardware_attributes", r =>
        {
            r.Target = Rack;
            r.Attributes = new[] { new HardwareAttributeAssignment { Name = "IsochronousMode", Kind = "boolean", Value = value.RootElement.Clone() } };
            r.AttributeErrorPolicy = "ignore";
            r.AttributeWriteMode = "sequential";
        })));
        Assert.Null(Errors(Item("set_io_address", r =>
        {
            r.Target = Rack;
            r.Address = new() { IoType = "Input", StartAddress = 0 };
            r.AddressChanges = new() { StartAddress = 10 };
        })));
        Assert.Null(Errors(Item("export_io_map", r => { r.FilePath = @"C:\Exports\io.csv"; r.CsvDelimiter = ";"; })));
    }

    [Fact]
    public void Validation_RejectsDeviceTargetsForItemOnlyOperationsAndBadFields()
    {
        Assert.Contains("must be 'deviceItem'", Errors(Item("delete_device_item", r => r.Target = new() { Kind = "device", DeviceName = "PLC_1" })));
        Assert.Contains("'target.nodeId' is not applicable", Errors(Item("list_plug_locations", r => r.Target = new() { Kind = "device", DeviceName = "PLC_1", NodeId = "x" })));
        Assert.Contains("must not be negative", Errors(Item("plug_device_item", r => { r.Target = Rack; r.TypeIdentifier = "OrderNumber:X"; r.PositionNumber = -1; })));
        Assert.Contains("not a creatable", Errors(Item("change_device_item_type", r => { r.Target = Rack; r.TypeIdentifier = "6ES7"; })));
        Assert.Contains("must not be blank", Errors(Item("plug_device_item", r => { r.Target = Rack; r.TypeIdentifier = "OrderNumber:X"; r.PositionNumber = 1; r.DeviceItemName = " "; })));
        Assert.Contains("at least one change", Errors(Item("set_io_address", r => { r.Target = Rack; r.Address = new() { IoType = "Input", StartAddress = 0 }; r.AddressChanges = new(); })));
        Assert.Contains("'address.ioType'", Errors(Item("set_io_address", r => { r.Target = Rack; r.Address = new() { IoType = "input", StartAddress = 0 }; r.AddressChanges = new() { StartAddress = 1 }; })));
        Assert.Contains("absolute file path", Errors(Item("export_io_map", r => r.FilePath = "io.csv")));
        Assert.Contains("must end in .csv", Errors(Item("export_io_map", r => r.FilePath = @"C:\Exports\io.txt")));
        Assert.Contains("Windows device name", Errors(Item("export_io_map", r => r.FilePath = @"C:\Exports\CON.csv")));
        Assert.Contains("ends with a dot or a blank", Errors(Item("export_io_map", r => r.FilePath = @"C:\Exports \io.csv")));
        Assert.Contains("'csvDelimiter'", Errors(Item("export_io_map", r => { r.FilePath = @"C:\x\io.csv"; r.CsvDelimiter = "|"; })));
        Assert.Contains("'destination.itemPath[0].typeIdentifier'", Errors(Item("copy_device_item", r =>
        {
            r.Target = Rack;
            r.PositionNumber = 1;
            r.Destination = DeviceItem((0, "Rack_0", 0, "  "));
        })));
        // K95: built-in interfaces and ports have no type identifier; "" (as list_port_connections used to report) means none.
        Assert.DoesNotContain("typeIdentifier", Errors(Item("copy_device_item", r =>
        {
            r.Target = Rack;
            r.PositionNumber = 1;
            r.Destination = DeviceItem((0, "Rack_0", 0, ""));
        })) ?? string.Empty);
    }

    [Theory]
    [InlineData("string", "1", "must be a JSON string")]
    [InlineData("boolean", "\"true\"", "must be true or false")]
    [InlineData("integer", "1.5", "must be an integer")]
    [InlineData("number", "\"1\"", "finite JSON number")]
    [InlineData("enum", "\" \"", "enum member name")]
    [InlineData("object", "1", "Valid values: string, boolean, integer, number, enum")]
    [InlineData("string", "null", "writing null is not supported")]
    public void Validation_RejectsAttributeValuesThatDoNotMatchTheirKind(string kind, string json, string expected)
    {
        using var value = JsonDocument.Parse(json);
        var errors = Errors(Item("set_hardware_attributes", r =>
        {
            r.Target = Rack;
            r.Attributes = new[] { new HardwareAttributeAssignment { Name = "A", Kind = kind, Value = value.RootElement.Clone() } };
        }));
        Assert.Contains(expected, errors);
    }

    [Fact]
    public void Validation_RejectsDuplicateAndTooManyAttributes()
    {
        using var value = JsonDocument.Parse("1");
        var assignment = new HardwareAttributeAssignment { Name = "A", Kind = "integer", Value = value.RootElement.Clone() };
        Assert.Contains("duplicates attribute 'A'", Errors(Item("set_hardware_attributes", r => { r.Target = Rack; r.Attributes = new[] { assignment, assignment }; })));
        Assert.Contains("must not exceed 100", Errors(Item("set_hardware_attributes", r =>
        {
            r.Target = Rack;
            r.Attributes = Enumerable.Range(0, 101).Select(i => new HardwareAttributeAssignment { Name = $"A{i}", Kind = "integer", Value = value.RootElement.Clone() }).ToArray();
        })));
    }

    [Fact]
    public void Invoker_DefaultsTheMoveDestinationToTheCurrentParent()
    {
        var nested = DeviceItem((0, "Rack_0", 0, "System:Rack.S71500"), (2, "DI", 2, "OrderNumber:X"));
        var request = HardwareModuleInvoker.BuildRequest(Item("move_device_item", r => { r.Target = nested; r.PositionNumber = 7; }));
        Assert.Equal("Rack_0", Assert.Single(request.Destination!.ItemPath!).Name);
        var topLevel = HardwareModuleInvoker.BuildRequest(Item("copy_device_item", r => { r.Target = Rack; r.PositionNumber = 1; }));
        Assert.Null(topLevel.Destination!.ItemPath);
        Assert.Equal("PLC_1", topLevel.Destination.DeviceName);
        var plug = HardwareModuleInvoker.BuildRequest(Item("plug_device_item", r => { r.Target = new() { Kind = "device", DeviceName = "PLC_1" }; r.TypeIdentifier = "System:Rack.S71500"; r.PositionNumber = 0; }));
        Assert.Null(plug.Target!.ItemPath);
        Assert.Equal(string.Empty, plug.Name);
        Assert.Null(plug.Destination);
    }

    [Fact]
    public void Csv_ProjectsAddressAndChannelRowsWithQuoting()
    {
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PLC;1",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = "Rack_0",
                            Items =
                            {
                                new DeviceItemInfo
                                {
                                    Name = "DI \"16\"", TypeIdentifier = "OrderNumber:X", PositionNumber = 2,
                                    IoDetails = new DeviceItemIoDetailsInfo
                                    {
                                        Addresses = { new IoAddressInfo { IoType = "Input", StartAddress = 0, Length = 2, ControllerNames = { "PLC;1" } } },
                                        Channels =
                                        {
                                            new IoChannelInfo { Number = 0, IoType = "Input", Type = "Digital", LogicalAddress = "%I0.0",
                                                TagMatches = { new IoTagMatchInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0", TableName = "T", FolderPath = "/" } } },
                                            new IoChannelInfo { Number = 1, IoType = "Input", Type = "Digital", LogicalAddress = "%I0.1" },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        var projection = HardwareIoMapCsv.Build(config, ";");
        var lines = projection.Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Equal(string.Join(";", HardwareIoMapCsv.Columns), lines[0]);
        Assert.Equal("address;\"PLC;1\";\"Rack_0/DI \"\"16\"\"\";\"DI \"\"16\"\"\";OrderNumber:X;2;Input;0;2;;\"PLC;1\";;;;;;;;;", lines[1]);
        Assert.EndsWith(";0;Input;Digital;%I0.0;Start;Bool;%I0.0;T;/", lines[2]);
        Assert.EndsWith(";1;Input;Digital;%I0.1;;;;;", lines[3]);
        Assert.Equal((1, 2, 1), (projection.AddressRows, projection.ChannelRows, projection.TagMatches));
        Assert.All(lines, line => Assert.Equal(HardwareIoMapCsv.Columns.Count - 1, CountDelimiters(line, ';')));
    }

    private static int CountDelimiters(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        foreach (var character in line)
        {
            if (character == '"') quoted = !quoted;
            else if (character == delimiter && !quoted) count++;
        }

        return count;
    }

    private static HardwareConfigInfo FormulaConfig() => new()
    {
        Devices =
        {
            new DeviceInfo
            {
                Name = "=HYPERLINK(\"http://x\")",
                Items =
                {
                    new DeviceItemInfo
                    {
                        Name = "+cmd", TypeIdentifier = "@SUM(A1)", PositionNumber = 1,
                        IoDetails = new DeviceItemIoDetailsInfo
                        {
                            Addresses = { new IoAddressInfo { IoType = "-Input", StartAddress = 0, Length = 1, Context = "\tctx", ControllerNames = { "\rPLC" } } },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public void Csv_NeutralizesFormulaLeadCharactersUnlessRawCellsAreRequested()
    {
        var neutral = HardwareIoMapCsv.Build(FormulaConfig(), ",");
        var row = neutral.Text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];
        Assert.StartsWith("address,\"'=HYPERLINK(\"\"http://x\"\")\",'+cmd,'+cmd,'@SUM(A1),1,'-Input,0,1,'\tctx,\"'\rPLC\"", row);
        // Device; ItemPath and Item (both "+cmd"); TypeIdentifier; IoType; Context; Controllers.
        Assert.Equal(7, neutral.NeutralizedCells);

        var raw = HardwareIoMapCsv.Build(FormulaConfig(), ",", rawCells: true);
        Assert.Equal(0, raw.NeutralizedCells);
        Assert.Contains(",+cmd,+cmd,@SUM(A1),1,-Input,", raw.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\\?\C:\io.csv")]
    [InlineData(@"\\.\pipe\io.csv")]
    [InlineData(@"\??\C:\io.csv")]
    public void Export_RefusesDevicePaths(string path)
    {
        Assert.True(NetworkOperationCatalog.IsDevicePath(path));
        Assert.False(NetworkOperationCatalog.IsAbsoluteFilePath(path));
        Assert.Contains("normalization", HardwareIoMapExport.Precheck(path, overwrite: true)!.Error);
        Assert.True(NetworkOperationCatalog.IsAbsoluteFilePath(@"C:\exports\io.csv"));
        Assert.False(NetworkOperationCatalog.IsUncOrDevicePath("/tmp/io.csv"));
    }

    [Theory]
    [InlineData(@"\\server\share\io.csv")]
    [InlineData("//server/share/io.csv")]
    public void Export_AcceptsUncShares(string path)
    {
        // Owner decision 2026-11-01: UNC shares are allowed for every file input and output.
        Assert.False(NetworkOperationCatalog.IsDevicePath(path));
        Assert.True(NetworkOperationCatalog.IsAbsoluteFilePath(path));
    }

    [Fact]
    public void ExportPrecheck_RefusesMissingDirectoriesDirectoriesAndExistingFilesUnlessOverwrite()
    {
        var directory = Directory.CreateTempSubdirectory("hw-export-").FullName;
        try
        {
            var path = Path.Combine(directory, "io.csv");
            Assert.Null(HardwareIoMapExport.Precheck(path, overwrite: false));
            File.WriteAllText(path, "old", Encoding.UTF8);
            Assert.Contains("already exists", HardwareIoMapExport.Precheck(path, overwrite: false)!.Error);
            Assert.Null(HardwareIoMapExport.Precheck(path, overwrite: true));
            Assert.Contains("does not exist", HardwareIoMapExport.Precheck(Path.Combine(directory, "missing", "io.csv"), false)!.Error);
            Directory.CreateDirectory(Path.Combine(directory, "dir.csv"));
            Assert.Contains("is a directory", HardwareIoMapExport.Precheck(Path.Combine(directory, "dir.csv"), true)!.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Methods_FeedThePolicyAndCapabilityCatalogs()
    {
        foreach (var method in HardwareModuleMethods.Read)
        {
            Assert.Equal(TiaMcpServer.Contracts.Safety.OperationCapability.Observe, TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(method));
            Assert.NotNull(TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(method));
        }

        foreach (var method in HardwareModuleMethods.Write.Append(HardwareModuleMethods.ExportIoMap))
        {
            Assert.Equal(TiaMcpServer.Contracts.Safety.OperationCapability.ProjectMutation, TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.GetCapability(method));
            Assert.False(TiaMcpServer.Contracts.Safety.OperationPolicyCatalog.IsAllowed(TiaMcpServer.Contracts.Safety.McpAccessMode.ReadOnly, method));
        }

        Assert.Equal("network_write", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(HardwareModuleMethods.PlugDeviceItem));
        Assert.Equal("network_write", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(HardwareModuleMethods.ProbePlug));
        Assert.Equal("network_read", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(HardwareModuleMethods.ListPlugLocations));
        Assert.True(TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.Supports(
            TiaMcpServer.Contracts.Project.ProjectContainerKinds.LocalSession, HardwareModuleMethods.SetHardwareAttributes));
    }
}
