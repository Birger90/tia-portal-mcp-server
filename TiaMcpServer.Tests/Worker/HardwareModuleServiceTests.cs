using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Extensions;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

/// <summary>HardwareModuleService against the offline object doubles: postconditions, attribute handler failures, address identity.</summary>
public sealed class HardwareModuleServiceTests
{
    private const string RackType = "System:Rack.S71500";
    private const string DiType = "OrderNumber:6ES7 521-1BH00-0AB0/V2.1";
    private const string DqType = "OrderNumber:6ES7 522-1BH00-0AB0/V2.1";

    private sealed record Fixture(SiemensProject Project, Device Device, DeviceItem Rack, DeviceItem Di, DeviceItem Dq);

    private static Fixture Build()
    {
        var project = new SiemensProject { Path = new FileInfo(@"C:\p\p.ap21") };
        var device = new Device { Name = "PLC_1", TypeIdentifier = "System:Device.S71500" };
        var rack = new DeviceItem { Name = "Rack_0", TypeIdentifier = RackType, PositionNumber = 0 };
        var di = new DeviceItem { Name = "DI 16x24VDC_1", TypeIdentifier = DiType, PositionNumber = 2 };
        var dq = new DeviceItem { Name = "DQ 16x24VDC_1", TypeIdentifier = DqType, PositionNumber = 3 };
        device.DeviceItems.Items.Add(rack);
        rack.Parent = device;
        rack.DeviceItems.Items.Add(di);
        rack.DeviceItems.Items.Add(dq);
        di.Parent = rack;
        dq.Parent = rack;
        project.Devices.Items.Add(device);
        return new Fixture(project, device, rack, di, dq);
    }

    private static DeviceItemPathSegmentInfo Segment(int index, DeviceItem item) => new()
    {
        Index = index, Name = item.Name, TypeIdentifier = item.TypeIdentifier!, PositionNumber = item.PositionNumber,
    };

    private static HardwareObjectSelectorInfo RackSelector(Fixture f) => new() { DeviceName = "PLC_1", ItemPath = new() { Segment(0, f.Rack) } };

    private static HardwareObjectSelectorInfo ItemSelector(Fixture f, int index, DeviceItem item)
        => new() { DeviceName = "PLC_1", ItemPath = new() { Segment(0, f.Rack), Segment(index, item) } };

    private static WorkerOperationException Fails(Func<object> act) => Assert.Throws<WorkerOperationException>(() => act());

    [Fact]
    public void Plug_ReturnsTheDirectChildAtTheRequestedPosition()
    {
        var f = Build();
        var result = HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo
        {
            Target = RackSelector(f), TypeIdentifier = DiType, Name = "DI_new", PositionNumber = 4,
        });
        Assert.Equal(("DI_new", 4), (result.Result!.Name, result.Result.PositionNumber));
        Assert.Equal(new[] { "Rack_0", "DI_new" }, result.Result.ItemPath.Select(segment => segment.Name));
    }

    [Fact]
    public void Plug_NullReturnAndNoNewItemFailsPostcondition()
    {
        var f = Build();
        f.Rack.PlacedResult = item => { f.Rack.DeviceItems.Items.Remove(item); return null; };
        var ex = Fails(() => HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo { Target = RackSelector(f), TypeIdentifier = DiType, PositionNumber = 4 }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("returned no device item", ex.Message, StringComparison.Ordinal);
        Assert.Contains("0 new items with type identifier", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plug_NullReturnButTheNewItemIsFound_IsAccepted()
    {
        var f = Build();
        f.Rack.PlacedResult = _ => null;
        var result = HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo { Target = RackSelector(f), TypeIdentifier = DiType, PositionNumber = 4 });
        Assert.Equal(new[] { "Rack_0", DiType }, result.Result!.ItemPath.Select(segment => segment.Name));
        Assert.Equal(new[] { "Rack_0" }, result.PlacedUnder!.ItemPath.Select(segment => segment.Name));
    }

    /// <summary>K05 (live 2026-11-02): PlugNew into a rack returns the module as a direct child of the device.</summary>
    private static Func<DeviceItem, DeviceItem?> DeviceLevel(Fixture f) => item =>
    {
        f.Rack.DeviceItems.Items.Remove(item);
        f.Device.DeviceItems.Items.Add(item);
        item.Parent = f.Device;
        return item;
    };

    [Fact]
    public void Plug_IntoARackThatPlacesTheModuleUnderTheDevice_IsAcceptedAndReportsPlacedUnder()
    {
        var f = Build();
        f.Rack.PlacedResult = DeviceLevel(f);
        var result = HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo
        {
            Target = RackSelector(f), TypeIdentifier = DiType, Name = "DI_32_Test", PositionNumber = 4,
        });
        Assert.Equal(new[] { "DI_32_Test" }, result.Result!.ItemPath.Select(segment => segment.Name));
        Assert.Equal(1, result.Result.ItemPath[0].Index);
        Assert.Equal(("PLC_1", 0), (result.PlacedUnder!.DeviceName, result.PlacedUnder.ItemPath.Count));
        Assert.Contains(result.Messages, message => message.Contains("directly under device 'PLC_1' at position 4", StringComparison.Ordinal));
    }

    [Fact]
    public void Plug_DeviceLevelPlacementWithAnotherTypeOrPosition_StillFails()
    {
        var f = Build();
        f.Rack.PlacedResult = item => { DeviceLevel(f)(item); item.TypeIdentifier = DqType; return item; };
        var typed = Fails(() => HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo { Target = RackSelector(f), TypeIdentifier = DiType, PositionNumber = 4 }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, typed.FailureCategory);
        Assert.Contains($"reports type identifier '{DqType}'", typed.Message, StringComparison.Ordinal);

        var g = Build();
        g.Rack.PlacedResult = DeviceLevel(g);
        g.Rack.ActualPosition = requested => requested + 1;
        var shifted = Fails(() => HardwareModuleService.Plug(g.Project, new HardwareModuleRequestInfo { Target = RackSelector(g), TypeIdentifier = DiType, PositionNumber = 4 }));
        Assert.Contains("at position 5, not 4", shifted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plug_DeviceTargetRefusedByCanPlugNew_NamesTheRackSelector()
    {
        var f = Build();
        f.Device.CanPlugNewRule = (_, _, _) => false;
        var ex = Fails(() => HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo
        {
            Target = new HardwareObjectSelectorInfo { DeviceName = "PLC_1" }, TypeIdentifier = DiType, PositionNumber = 1,
        }));
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
        Assert.Contains("Modules usually plug into the rack item", ex.Message, StringComparison.Ordinal);
        Assert.Contains("itemPath: [{ index: 0, name: 'Rack_0', positionNumber: 0, typeIdentifier: 'System:Rack.S71500' }]", ex.Message, StringComparison.Ordinal);
        Assert.Empty(f.Device.DeviceItems.Items.Where(item => item.Name != "Rack_0"));
    }

    [Fact]
    public void Move_IntoARackThatPlacesTheItemUnderTheDevice_IsAccepted()
    {
        var f = Build();
        f.Rack.PlacedResult = DeviceLevel(f);
        var result = HardwareModuleService.Move(f.Project, new HardwareModuleRequestInfo
        {
            Target = ItemSelector(f, 0, f.Di), Destination = RackSelector(f), PositionNumber = 6,
        });
        Assert.Empty(result.PlacedUnder!.ItemPath);
        Assert.Equal(new[] { f.Di.Name }, result.Result!.ItemPath.Select(segment => segment.Name));
    }

    [Fact]
    public void Plug_ReturnedSubItemOrOtherPositionFailsPostcondition()
    {
        var f = Build();
        f.Rack.PlacedResult = item =>
        {
            var sub = new DeviceItem { Name = "Sub", TypeIdentifier = "System:Sub", PositionNumber = 0 };
            item.DeviceItems.Items.Add(sub);
            sub.Parent = item;
            return sub;
        };
        var nested = Fails(() => HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo { Target = RackSelector(f), TypeIdentifier = DiType, PositionNumber = 4 }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, nested.FailureCategory);
        Assert.Contains("not directly in", nested.Message, StringComparison.Ordinal);

        var g = Build();
        g.Rack.ActualPosition = requested => requested + 1;
        var shifted = Fails(() => HardwareModuleService.Plug(g.Project, new HardwareModuleRequestInfo { Target = RackSelector(g), TypeIdentifier = DiType, PositionNumber = 4 }));
        Assert.Contains("at position 5, not 4", shifted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Plug_NameDifferentFromTheRequestFailsPostcondition()
    {
        var f = Build();
        f.Rack.PlacedResult = item => { item.Name = "Renamed"; return item; };
        var ex = Fails(() => HardwareModuleService.Plug(f.Project, new HardwareModuleRequestInfo { Target = RackSelector(f), TypeIdentifier = DiType, Name = "DI_new", PositionNumber = 4 }));
        Assert.Contains("named 'Renamed', not 'DI_new'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_ConfirmsPositionAndThatTheSourcePathIsGone()
    {
        var f = Build();
        var result = HardwareModuleService.Move(f.Project, new HardwareModuleRequestInfo
        {
            Target = ItemSelector(f, 0, f.Di), Destination = RackSelector(f), PositionNumber = 6,
        });
        Assert.Equal(6, result.Result!.PositionNumber);

        var g = Build();
        g.Rack.ActualPosition = _ => 7;
        var ex = Fails(() => HardwareModuleService.Move(g.Project, new HardwareModuleRequestInfo { Target = ItemSelector(g, 0, g.Di), Destination = RackSelector(g), PositionNumber = 6 }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("at position 7, not 6", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Copy_NullReturnFailsPostconditionAndLeavesTheSource()
    {
        var f = Build();
        f.Rack.PlacedResult = _ => null;
        var ex = Fails(() => HardwareModuleService.Copy(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di), Destination = RackSelector(f), PositionNumber = 6 }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains(f.Di, f.Rack.DeviceItems.Items);
    }

    [Fact]
    public void Delete_ThatDoesNotRemoveTheItemFailsPostcondition()
    {
        var f = Build();
        f.Di.IgnoreDelete = true;
        var ex = Fails(() => HardwareModuleService.Delete(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di) }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
    }

    [Fact]
    public void ChangeType_IgnoredByPortalFailsPostcondition()
    {
        var f = Build();
        f.Di.IgnoreChangeType = true;
        var ex = Fails(() => HardwareModuleService.ChangeType(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di), TypeIdentifier = "OrderNumber:6ES7 521-1BH00-0AB0/V2.2" }));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("still reports type identifier", ex.Message, StringComparison.Ordinal);
    }

    private static Fixture WithAttributes()
    {
        var f = Build();
        f.Di.AttributeInfos.Add(new EngineeringAttributeInfo { Name = "Comment", SupportedTypes = new[] { typeof(string) } });
        f.Di.AttributeInfos.Add(new EngineeringAttributeInfo { Name = "IsochronousMode", SupportedTypes = new[] { typeof(bool) } });
        f.Di.Attributes["Comment"] = "old";
        f.Di.Attributes["IsochronousMode"] = false;
        f.Di.RejectAttribute = (name, _) => name == "IsochronousMode" ? "Isochronous mode needs a configured sync domain." : null;
        return f;
    }

    private static HardwareModuleRequestInfo AttributeWrite(Fixture f, string policy, string mode = "bulk") => new()
    {
        Target = ItemSelector(f, 0, f.Di),
        AttributeErrorPolicy = policy,
        AttributeWriteMode = mode,
        Attributes = new()
        {
            new HardwareAttributeValueInfo { Name = "Comment", Kind = "string", StringValue = "new" },
            new HardwareAttributeValueInfo { Name = "IsochronousMode", Kind = "boolean", BooleanValue = true },
        },
    };

    [Fact]
    public void BulkSet_PolicyIgnoreReportsTheRejectedAttributeAsIgnored()
    {
        var f = WithAttributes();
        var result = HardwareModuleService.SetAttributes(f.Project, AttributeWrite(f, "ignore"));
        Assert.Equal(new[] { ("Comment", "applied"), ("IsochronousMode", "ignored") }, result.Attributes.Select(a => (a.Name, a.Status)));
        Assert.Equal(1, f.Di.BulkCalls);
    }

    [Fact]
    public void BulkSet_PolicyAbortIsEngineeringRejectedWithReadBack()
    {
        var f = WithAttributes();
        var ex = Fails(() => HardwareModuleService.SetAttributes(f.Project, AttributeWrite(f, "abort")));
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, ex.FailureCategory);
        Assert.Contains(ex.Warnings, warning => warning.StartsWith("Comment: applied", StringComparison.Ordinal));
        Assert.Contains(ex.Warnings, warning => warning.StartsWith("IsochronousMode: notApplied", StringComparison.Ordinal));
    }

    [Fact]
    public void BulkSet_UnanswerableCallbackIsReportedWithTheReadBackAfterSetAttributesReturns()
    {
        var f = WithAttributes();
        f.Di.SelectionFailureForCallback = new InvalidOperationException("CurrentSelection is read-only here");
        var ex = Fails(() => HardwareModuleService.SetAttributes(f.Project, AttributeWrite(f, "abort")));
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, ex.FailureCategory);
        Assert.Contains("SetAttributes ran", ex.Message, StringComparison.Ordinal);
        Assert.Contains("read-only here", ex.Message, StringComparison.Ordinal);
        // Portal continued with its default (ignore): the comment is applied, the rejected attribute is not.
        Assert.Equal("new", f.Di.Attributes["Comment"]);
        Assert.Contains(ex.Warnings, warning => warning.StartsWith("Comment: applied", StringComparison.Ordinal));
        Assert.Contains(ex.Warnings, warning => warning.StartsWith("IsochronousMode: notApplied", StringComparison.Ordinal));
    }

    [Fact]
    public void SequentialSet_SilentlyIgnoredWriteFailsPostcondition()
    {
        var f = WithAttributes();
        f.Di.RejectAttribute = null;
        f.Di.SilentlyIgnored.Add("Comment");
        var ex = Fails(() => HardwareModuleService.SetAttributes(f.Project, AttributeWrite(f, "abort", "sequential")));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("Comment: notApplied", ex.Message, StringComparison.Ordinal);
    }

    private static Fixture WithAddresses()
    {
        var f = Build();
        f.Di.Addresses.Items.Add(new Address { IoType = AddressIoType.Input, StartAddress = 0, Length = 2 });
        f.Di.Addresses.Items.Add(new Address { IoType = AddressIoType.Input, StartAddress = 10, Length = 2 });
        return f;
    }

    private static HardwareModuleRequestInfo AddressWrite(Fixture f, int from, int to) => new()
    {
        Target = ItemSelector(f, 0, f.Di),
        Address = new HardwareAddressSelectorInfo { IoType = "Input", StartAddress = from },
        AddressChanges = new HardwareAddressChangesInfo { StartAddress = to },
    };

    [Fact]
    public void SetIoAddress_IdentifiesTheWrittenAddressByTypeAndStartEvenWhenPortalReorders()
    {
        var f = WithAddresses();
        var first = f.Di.Addresses.Items[0];
        first.StartAddressChanged = address => { f.Di.Addresses.Items.Remove(address); f.Di.Addresses.Items.Add(address); };

        var result = HardwareModuleService.SetIoAddress(f.Project, AddressWrite(f, 0, 20));

        Assert.Equal(new[] { 10, 20 }, result.After.Select(address => address.StartAddress!.Value));
        Assert.DoesNotContain(result.Messages, message => message.Contains("also changed", StringComparison.Ordinal));
    }

    [Fact]
    public void SetIoAddress_StartPortalDidNotKeepFailsPostcondition()
    {
        var f = WithAddresses();
        var first = f.Di.Addresses.Items[0];
        var reverting = false;
        first.StartAddressChanged = address =>
        {
            if (reverting) return;
            reverting = true;
            address.StartAddress = 0;
            reverting = false;
        };

        var ex = Fails(() => HardwareModuleService.SetIoAddress(f.Project, AddressWrite(f, 0, 20)));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("no Input address of length 2 starts at 20", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetIoAddress_TwoCandidatesAfterTheWriteCannotBeIdentified()
    {
        var f = WithAddresses();
        var ex = Fails(() => HardwareModuleService.SetIoAddress(f.Project, AddressWrite(f, 0, 10)));
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, ex.FailureCategory);
        Assert.Contains("cannot be identified", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>K06 (live 2026-11-02): the S7-1500 DI 32 module's addresses sit on a same-named sub-item without type identifier.</summary>
    private static (Fixture Fixture, DeviceItem Sub) WithSubItemAddresses()
    {
        var f = Build();
        var sub = new DeviceItem { Name = f.Di.Name, TypeIdentifier = null, PositionNumber = 1 };
        sub.Parent = f.Di;
        f.Di.DeviceItems.Items.Add(sub);
        sub.Addresses.Items.Add(new Address { IoType = AddressIoType.Input, StartAddress = 10, Length = 4 });
        return (f, sub);
    }

    [Fact]
    public void ListIoAddresses_ItemWithoutOwnAddressesListsItsSubItemsWithTheirPath()
    {
        var (f, _) = WithSubItemAddresses();
        var result = HardwareModuleService.ListIoAddresses(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di) });
        var address = Assert.Single(result.Addresses);
        Assert.Equal(("Input", 10, 4, 0), (address.IoType, address.StartAddress!.Value, address.Length!.Value, address.Index));
        var segment = Assert.Single(address.SubItemPath);
        Assert.Equal((0, f.Di.Name, (int?)1, (string?)null), (segment.Index, segment.Name, segment.PositionNumber, segment.TypeIdentifier));
        Assert.Contains(result.Messages, message => message.Contains("no addresses of its own", StringComparison.Ordinal));
    }

    [Fact]
    public void ListIoAddresses_OwnAddressesAreListedWithoutSubItems()
    {
        var (f, _) = WithSubItemAddresses();
        f.Di.Addresses.Items.Add(new Address { IoType = AddressIoType.Input, StartAddress = 0, Length = 2 });
        var result = HardwareModuleService.ListIoAddresses(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di) });
        var address = Assert.Single(result.Addresses);
        Assert.Empty(address.SubItemPath);
    }

    /// <summary>
    /// K95 (live 2026-11-07, CPU 1516-3 PN/DP V2.9): a sub-item whose TypeIdentifier is null is selected by index, name and
    /// position with a segment typeIdentifier of null or "" (or a typed parent segment without one); a nonblank type
    /// identifier the item does not have is refused, and a wrong name at the index is refused as before.
    /// </summary>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", null, true)]
    [InlineData(null, "omitParentType", true)]
    [InlineData("System:Sub", null, false)]
    [InlineData(null, "wrongName", false)]
    public void ListIoAddresses_SubItemWithoutTypeIdentifierIsSelectable(string? segmentType, string? variant, bool resolves)
    {
        var (f, sub) = WithSubItemAddresses();
        var di = Segment(0, f.Di);
        if (variant == "omitParentType") di.TypeIdentifier = null;
        var target = new HardwareObjectSelectorInfo
        {
            DeviceName = "PLC_1",
            ItemPath = new()
            {
                Segment(0, f.Rack), di,
                new DeviceItemPathSegmentInfo { Index = 0, Name = variant == "wrongName" ? "Other" : sub.Name, PositionNumber = sub.PositionNumber, TypeIdentifier = segmentType },
            },
        };
        if (!resolves)
        {
            var ex = Fails(() => HardwareModuleService.ListIoAddresses(f.Project, new HardwareModuleRequestInfo { Target = target }));
            Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch, ex.FailureCategory);
            return;
        }

        var result = HardwareModuleService.ListIoAddresses(f.Project, new HardwareModuleRequestInfo { Target = target });
        Assert.Equal(10, Assert.Single(result.Addresses).StartAddress);
        Assert.Null(result.Target.ItemPath[2].TypeIdentifier);
    }

    /// <summary>
    /// K39 (live V21 2026-11-02, S7-1200 and ET 200SP): three "is not supported" messages per address. An attribute the
    /// address does not list in GetAttributeInfos is not read, and the call reports it once.
    /// </summary>
    [Fact]
    public void ListIoAddresses_AttributesNotOfferedAreNotReadAndNotedOncePerCall()
    {
        var f = Build();
        var addresses = Enumerable.Range(0, 3).Select(i => new Address { IoType = AddressIoType.Input, StartAddress = i * 2, Length = 2 }).ToList();
        foreach (var address in addresses)
        {
            address.Attributes["ProcessImage"] = 1;
            f.Di.Addresses.Items.Add(address);
        }

        var result = HardwareModuleService.ListIoAddresses(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di) });

        Assert.Equal(3, result.Addresses.Count);
        Assert.All(result.Addresses, a => Assert.Equal((1, (string?)null, (bool?)null, (long?)null), (a.ProcessImage!.Value, a.Context, a.IsochronousMode, a.InterruptObNumber)));
        Assert.All(addresses, a => Assert.Equal(new[] { "ProcessImage" }, a.AttributeReads));
        Assert.Equal(new[]
        {
            "'Context' is not offered by 3 of 3 addresses (GetAttributeInfos); it is reported as null there.",
            "'InterruptObNumber' is not offered by 3 of 3 addresses (GetAttributeInfos); it is reported as null there.",
            "'IsochronousMode' is not offered by 3 of 3 addresses (GetAttributeInfos); it is reported as null there.",
        }, result.Messages);
    }

    [Fact]
    public void ListIoAddresses_OfferedAttributeThatFailsIsReportedOncePerCall()
    {
        var f = Build();
        for (var i = 0; i < 2; i++)
            f.Di.Addresses.Items.Add(new Address
            {
                IoType = AddressIoType.Output, StartAddress = i, Length = 1,
                AttributeInfos = new() { new EngineeringAttributeInfo { Name = "Context" } },
            });

        var result = HardwareModuleService.ListIoAddresses(f.Project, new HardwareModuleRequestInfo { Target = ItemSelector(f, 0, f.Di) });

        var failure = Assert.Single(result.Messages, m => m.StartsWith("Could not read", StringComparison.Ordinal));
        Assert.Equal("Could not read Context of 2 of 2 addresses (first: 'Context' is not supported.); it is reported as null there.", failure);
        Assert.Equal(4, result.Messages.Count);
    }

    /// <summary>
    /// K58: the I/O map (read_hardware_config ioDetails, export_io_map) shares the K39 rule through DynamicAttributeNotes; its
    /// notes name the objects and the device item they belong to.
    /// </summary>
    [Fact]
    public void DynamicAttributeNotes_ReadOnlyOfferedAttributesAndNameObjectsAndContext()
    {
        var notes = new DynamicAttributeNotes("channels");
        var offered = new Address { Attributes = { ["ChannelAddress"] = 8 } };
        var bare = new Address();
        var values = new[] { offered, bare }
            .Select(a => notes.Read<object>(notes.Offered(a), "ChannelAddress", () => a.GetAttribute("ChannelAddress"))).ToArray();
        var messages = new List<string>();
        notes.AddTo(messages, 2, " of device item 'DI'");

        Assert.Equal(new object?[] { 8, null }, values);
        Assert.Empty(bare.AttributeReads);
        Assert.Equal(new[] { "'ChannelAddress' is not offered by 1 of 2 channels of device item 'DI' (GetAttributeInfos); it is reported as null there." }, messages);
    }

    [Fact]
    public void HardwareIoMapReader_ReadsDynamicAttributesOnlyThroughGetAttributeInfos()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareIoMapReader.cs"));

        Assert.Contains("notes.Read<string>(notes.Offered(dynamic), \"Context\"", source, StringComparison.Ordinal);
        Assert.Contains("var offered = notes.Offered((IEngineeringObject)channel);", source, StringComparison.Ordinal);
        Assert.Equal(3, source.Split(".GetAttribute(").Length - 1);
        Assert.Equal(3, source.Split("notes.Read<").Length - 1);
    }

    private static string RepositoryRoot() => TiaMcpServer.Tests.TestSupport.RepositoryFiles.Root;

    [Fact]
    public void SetIoAddress_WritesASubItemAddressAndReadsItBack()
    {
        var (f, sub) = WithSubItemAddresses();
        var result = HardwareModuleService.SetIoAddress(f.Project, AddressWrite(f, 10, 40));
        Assert.Equal(40, sub.Addresses.Items[0].StartAddress);
        var after = Assert.Single(result.After);
        Assert.Equal((40, f.Di.Name), (after.StartAddress!.Value, after.SubItemPath.Single().Name));
        Assert.Equal(new[] { 0 }, result.ChangedIndexes);
    }

    [Fact]
    public void SetIoAddress_SameStartOnTwoSubItemsIsAValidationErrorListingTheCandidates()
    {
        var (f, _) = WithSubItemAddresses();
        var second = new DeviceItem { Name = "DI_32_2", TypeIdentifier = null, PositionNumber = 2, Parent = f.Di };
        second.Addresses.Items.Add(new Address { IoType = AddressIoType.Input, StartAddress = 10, Length = 2 });
        f.Di.DeviceItems.Items.Add(second);
        var ex = Fails(() => HardwareModuleService.SetIoAddress(f.Project, AddressWrite(f, 10, 40)));
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
        Assert.Contains($"on sub-item '{f.Di.Name}'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("on sub-item 'DI_32_2'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was changed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ListPlugLocations_ReadsTheDocumentedPropertiesByName()
    {
        var f = Build();
        f.Rack.PlugLocations.Add(new PlugLocation { PositionNumber = 4, Label = "4" });
        var result = HardwareModuleService.ListPlugLocations(f.Project, new HardwareModuleRequestInfo { Target = RackSelector(f) });
        var location = Assert.Single(result.PlugLocations);
        Assert.Equal((4, "4"), (location.PositionNumber, location.Label));
    }
}
