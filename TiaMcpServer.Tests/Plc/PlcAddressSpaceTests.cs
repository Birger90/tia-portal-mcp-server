using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public sealed class PlcAddressSpaceTests
{
    [Theory]
    [InlineData("%I0.0", "I", 0, 1)]
    [InlineData(" %q4.7 ", "Q", 39, 1)]
    [InlineData("%MW10", "M", 80, 16)]
    [InlineData("%MB11", "M", 88, 8)]
    [InlineData("%ID64", "I", 512, 32)]
    [InlineData("%E1.2", "I", 10, 1)]
    [InlineData("%AW2", "Q", 16, 16)]
    public void Parse_AcceptsAbsoluteIqmAddresses(string text, string area, long startBit, int bits)
    {
        Assert.True(PlcAddressSpace.TryParse(text, out var parsedArea, out var parsedStart, out var parsedBits));
        Assert.Equal((area, startBit, bits), (parsedArea, parsedStart, parsedBits));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%M10")]
    [InlineData("%MW10.1")]
    [InlineData("%I0.8")]
    [InlineData("%IW64:P")]
    [InlineData("%DB1.DBX0.0")]
    [InlineData("Motor")]
    public void Parse_RejectsEverythingElse(string text)
        => Assert.False(PlcAddressSpace.TryParse(text, out _, out _, out _));

    private static PlcTagInventoryPlcInfo Plc() => new()
    {
        PlcName = "PLC_1",
        DeviceName = "PLC_1",
        Tables =
        {
            new TagTableInfo
            {
                Name = "Default", FolderPath = "/",
                Tags =
                {
                    new TagInfo { Name = "Start", DataType = "Bool", LogicalAddress = "%I0.0" },
                    new TagInfo { Name = "Speed", DataType = "Int", LogicalAddress = "%MW10" },
                    new TagInfo { Name = "Overlap", DataType = "Byte", LogicalAddress = "%MB11" },
                    new TagInfo { Name = "Orphan", DataType = "Bool", LogicalAddress = "%I100.0" },
                    new TagInfo { Name = "Symbolic", DataType = "Bool", LogicalAddress = "" },
                },
            },
        },
    };

    private static HardwareConfigInfo Hardware() => new()
    {
        Devices =
        {
            new DeviceInfo
            {
                Name = "PLC_1",
                Items =
                {
                    new DeviceItemInfo
                    {
                        Name = "DI",
                        IoDetails = new DeviceItemIoDetailsInfo
                        {
                            Addresses =
                            {
                                new IoAddressInfo { IoType = "Input", StartAddress = 0, Length = 2, ControllerNames = { "PLC_1" } },
                                new IoAddressInfo { IoType = "Input", StartAddress = 200, Length = 2, ControllerNames = { "Other_PLC" } },
                            },
                        },
                    },
                },
            },
        },
    };

    [Fact]
    public void Occupancy_ListsTagsHardwareOverlapsAndOrphans()
    {
        var messages = new List<string>();
        var hardware = PlcAddressSpace.Hardware(Hardware(), "PLC_1", messages);
        var result = PlcAddressSpace.Occupancy(Plc(), true, PlcAddressSpace.AreaNames, hardware, messages);

        var inputs = result.Areas.Single(area => area.Area == "I");
        Assert.Equal(new[] { "hardware", "tag", "tag" }, inputs.Entries.Select(entry => entry.Kind));
        Assert.Equal("%IW0", inputs.Entries[0].Address);
        var overlap = Assert.Single(result.Overlaps);
        Assert.Equal(("Speed", "Overlap"), (overlap.FirstTag, overlap.SecondTag));
        Assert.Equal("Orphan", Assert.Single(result.TagsOutsideHardware).TagName);
        Assert.Equal("Symbolic", Assert.Single(result.UnparsedTags).TagName);
        Assert.Equal(2, result.Areas.Single(area => area.Area == "M").UsedBytes);
        Assert.True(result.HardwareIncluded);
    }

    [Fact]
    public void Occupancy_WithoutHardwareReportsNoOrphans()
    {
        var result = PlcAddressSpace.Occupancy(Plc(), false, new[] { "I" }, null, new List<string>());
        Assert.False(result.HardwareIncluded);
        Assert.False(result.IsComplete);
        Assert.Empty(result.TagsOutsideHardware);
        Assert.Single(result.Areas);
    }

    [Theory]
    [InlineData("M", "Bool", 0, 1, false, "%M0.0")]
    [InlineData("M", "Int", 10, 1, false, "%MW12")]
    [InlineData("M", "Byte", 10, 1, false, "%MB12")]
    [InlineData("M", "DInt", 9, 4, false, "%MD12")]
    [InlineData("I", "Bool", 0, 1, true, "%I0.1")]
    [InlineData("I", "Word", 0, 1, true, null)]
    public void FindFree_ReturnsTheLowestUnusedAlignedAddress(string area, string dataType, int startByte, int align, bool requireHardware, string? expected)
    {
        var messages = new List<string>();
        var hardware = requireHardware ? PlcAddressSpace.Hardware(Hardware(), "PLC_1", messages) : null;
        var result = PlcAddressSpace.FindFree(Plc(), true, area, dataType, startByte, 65535, align, requireHardware, hardware, messages);
        Assert.Equal(expected, result.Address);
        Assert.Equal(expected is not null, result.Found);
    }

    [Fact]
    public void FindFree_IncompleteInventoryWarns()
    {
        var messages = new List<string>();
        var result = PlcAddressSpace.FindFree(Plc(), false, "M", "Bool", 0, 0, 1, false, null, messages);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Messages, message => message.Contains("incomplete", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_RejectsUnknownAreasTypesAndRanges()
    {
        string? Error(PlcOperationRequest request)
        {
            var validation = PlcOperationCatalog.ValidateRead(new[] { request });
            return validation.IsValid ? null : validation.Error;
        }
        Assert.Contains("'area' must be one of", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "X", DataType = "Bool" }));
        Assert.Contains("no documented length with a 1/8/16/32-bit", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "LReal" }));
        Assert.Contains("no documented length", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "Time" }));
        Assert.Contains("'endByte' must be between 0 and 65535", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "Bool", EndByte = 65536 }));
        Assert.Contains("'startByte' must be between 0 and 65535", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "Bool", StartByte = 70000 }));
        Assert.Contains("'requireHardware' applies only", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "Bool", RequireHardware = true }));
        Assert.Contains("'endByte' must not be below", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "Bool", StartByte = 5, EndByte = 4 }));
        Assert.Contains("'alignBytes' must be", Error(new() { OperationId = "a", Operation = "find_free_address", Area = "M", DataType = "Int", AlignBytes = 3 }));
        Assert.Contains("duplicates", Error(new() { OperationId = "a", Operation = "read_address_occupancy", Areas = new[] { "I", "I" } }));
        Assert.Null(Error(new() { OperationId = "a", Operation = "read_address_occupancy", Areas = new[] { "Q", "M" }, IncludeHardware = false }));
        Assert.Null(Error(new() { OperationId = "a", Operation = "find_free_address", Area = "Q", DataType = "time_of_day", AlignBytes = 4, RequireHardware = true }));
    }

    private static PlcTagInventoryPlcInfo PlcWith(params TagInfo[] tags)
    {
        var table = new TagTableInfo { Name = "Default", FolderPath = "/" };
        foreach (var tag in tags) table.Tags.Add(tag);
        return new PlcTagInventoryPlcInfo { PlcName = "PLC_1", DeviceName = "PLC_1", Tables = { table } };
    }

    [Theory]
    [InlineData("LReal", "%MB100", 64)]
    [InlineData("LInt", "%MD100", 64)]
    [InlineData("DTL", "%MB100", 96)]
    [InlineData("Int", "%M100.0", 16)]
    [InlineData("Bool", "%MB100", 8)]
    public void Occupancy_UsesTheDocumentedDataTypeLengthAndReportsSpellingMismatches(string dataType, string address, int expectedBits)
    {
        var result = PlcAddressSpace.Occupancy(PlcWith(new TagInfo { Name = "T", DataType = dataType, LogicalAddress = address }), true,
            new[] { "M" }, null, new List<string>());
        var entry = Assert.Single(result.Areas.Single().Entries);
        Assert.Equal(expectedBits, entry.BitLength);
        var mismatch = Assert.Single(result.WidthMismatches);
        Assert.Equal(("T", address), (mismatch.TagName, mismatch.LogicalAddress));
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void FindFree_SkipsTheFullDocumentedLengthOfWiderTypes()
    {
        var plc = PlcWith(new TagInfo { Name = "Big", DataType = "LReal", LogicalAddress = "%MB100" });
        var result = PlcAddressSpace.FindFree(plc, true, "M", "Byte", 100, 200, 1, false, null, new List<string>());
        Assert.Equal("%MB108", result.Address);
        Assert.True(result.IsComplete);
    }

    [Theory]
    [InlineData("\"Motor_UDT\"")]
    [InlineData("Array[0..9] of Bool")]
    [InlineData("String")]
    [InlineData("Time")]
    public void UnsizedTypes_AreListedAndMakeResultsIncomplete(string dataType)
    {
        var plc = PlcWith(new TagInfo { Name = "Blob", DataType = dataType, LogicalAddress = "%I0.0" });
        var occupancy = PlcAddressSpace.Occupancy(plc, true, new[] { "I" }, null, new List<string>());
        Assert.False(occupancy.IsComplete);
        Assert.Empty(occupancy.Areas.Single().Entries);
        var unsized = Assert.Single(occupancy.UnsizedTags);
        Assert.Equal(("Blob", "I", 0, 0, 1), (unsized.TagName, unsized.Area, unsized.StartByte, unsized.StartBit, unsized.SpelledBits));

        var free = PlcAddressSpace.FindFree(plc, true, "I", "Bool", 0, 10, 1, false, null, new List<string>());
        Assert.Equal("%I0.1", free.Address);
        Assert.False(free.IsComplete);
        Assert.Single(free.UnsizedTags);
        Assert.Contains(free.Messages, message => message.Contains("'Blob'", StringComparison.Ordinal) && message.Contains("without a documented length", StringComparison.Ordinal));
    }

    [Fact]
    public void FindFree_SweepsLargeInventoriesAndHonoursCancellation()
    {
        var tags = Enumerable.Range(0, 60000).Select(i => new TagInfo { Name = $"B{i}", DataType = "Bool", LogicalAddress = $"%M{i / 8}.{i % 8}" }).ToArray();
        var plc = PlcWith(tags);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = PlcAddressSpace.FindFree(plc, true, "M", "Word", 0, 65535, 2, false, null, new List<string>());
        Assert.Equal("%MW7500", result.Address);
        var full = PlcAddressSpace.FindFree(PlcWith(new TagInfo { Name = "All", DataType = "DWord", LogicalAddress = "%MD0" }), true, "M", "Bool", 4, 4, 1, false, null, new List<string>());
        Assert.Equal("%M4.0", full.Address);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"sweep took {watch.Elapsed}");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => PlcAddressSpace.FindFree(plc, true, "M", "Bool", 0, 65535, 1, false, null, new List<string>(), cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => PlcAddressSpace.Occupancy(plc, true, PlcAddressSpace.AreaNames, null, new List<string>(), cancelled.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlcAddressSpace.FindFree(plc, true, "M", "Bool", 0, 65536, 1, false, null, new List<string>()));
    }

    [Fact]
    public void FindFree_RequireHardwareSearchesEachRangeAndPicksTheLowest()
    {
        var hardware = new List<PlcAddressOccupant>
        {
            new("hardware", "I", 10, 0, 16, "%IW10", null, null, null, null, "PLC_1", "DI2", "Input"),
            new("hardware", "I", 0, 0, 16, "%IW0", null, null, null, null, "PLC_1", "DI1", "Input"),
        };
        var plc = PlcWith(new TagInfo { Name = "W", DataType = "Word", LogicalAddress = "%IW0" });
        var result = PlcAddressSpace.FindFree(plc, true, "I", "Byte", 0, 65535, 1, true, hardware, new List<string>());
        Assert.Equal("%IB10", result.Address);
        Assert.Null(PlcAddressSpace.FindFree(plc, true, "I", "DWord", 0, 65535, 1, true, hardware, new List<string>()).Address);
    }
}
