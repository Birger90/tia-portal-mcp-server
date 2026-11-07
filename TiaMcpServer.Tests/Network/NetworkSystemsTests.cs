using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Issue #11: transfer areas (network_read list_transfer_areas, network_write create/update/delete_transfer_area), delete_io_system
/// and disconnect_from_io_system. The Siemens-free run against stand-ins, the catalog and policy, and the hardware-modules FakeWorker
/// end to end. Not live-tested; V21 documents no separate member to create a DP master system.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class NetworkSystemsTests
{
    private enum AreaType { CD, DDX }

    private sealed class Area(Composition owner, string name)
    {
        public string Name { get; set; } = name;
        public AreaType Type => AreaType.CD;
        public List<object> LocalAddresses { get; } = new() { new object(), new object() };
        public void Delete() { if (!owner.Keep) owner.Items.Remove(this); }
    }

    private sealed class Composition : IEnumerable<Area>
    {
        public List<Area> Items { get; } = new();
        public bool Keep { get; set; }
        public bool Detached { get; set; }

        public Area Create(string name, AreaType type)
        {
            var area = new Area(this, name);
            if (!Detached) Items.Add(area);
            return area;
        }

        public IEnumerator<Area> GetEnumerator() => Items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Interface
    {
        public Composition TransferAreas { get; } = new();
    }

    private const string Collection = NetworkSystemNames.TransferAreaCollections.TransferAreas;

    [Fact]
    public void Run_CreateDescribeSelectDelete_AreReadBackFromTheComposition()
    {
        var networkInterface = new Interface();
        var (composition, error) = TransferAreaRun.Composition(networkInterface, Collection);
        Assert.Null(error);
        var (created, index, before, after) = TransferAreaRun.Create(composition!, new TransferAreaRequestInfo { Collection = Collection, Name = "TA_1" }, AreaType.CD, null);
        Assert.Equal((0, 0, 1), (index, before, after));

        var described = TransferAreaRun.Describe(created, Collection, index, _ => false, _ => new List<TransferAreaAttributeInfo>());
        Assert.Equal("TA_1", described.Name);
        Assert.Equal("CD", described.Properties.Single(property => property.Name == "Type").Value.Value);
        Assert.Equal("2", described.Properties.Single(property => property.Name == "LocalAddressesCount").Value.Value);
        Assert.Equal(RuntimeValueStates.Missing, described.Properties.Single(property => property.Name == "PositionNumber").Value.State);

        var elements = TransferAreaRun.Elements(composition!, Collection);
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch,
            Assert.Throws<RuntimeBindingException>(() => TransferAreaRun.Select(elements, Collection, 0, "other", area => ((Area)area).Name)).Category);
        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            Assert.Throws<RuntimeBindingException>(() => TransferAreaRun.Select(elements, Collection, 1, "TA_1", area => ((Area)area).Name)).Category);
        var selected = TransferAreaRun.Select(elements, Collection, 0, "TA_1", area => ((Area)area).Name);
        Assert.Equal((1, 0), TransferAreaRun.Delete(composition!, Collection, selected));
    }

    [Fact]
    public void Run_UnconfirmedWrites_ArePostconditionFailed_AndMissingMembersAreReported()
    {
        var networkInterface = new Interface();
        var (composition, _) = TransferAreaRun.Composition(networkInterface, Collection);
        ((Composition)composition!).Detached = true;
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, Assert.Throws<RuntimeBindingException>(() =>
            TransferAreaRun.Create(composition, new TransferAreaRequestInfo { Collection = Collection, Name = "TA_1" }, AreaType.CD, null)).Category);

        var kept = new Composition { Keep = true };
        var area = kept.Create("TA_1", AreaType.CD);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, Assert.Throws<RuntimeBindingException>(() => TransferAreaRun.Delete(kept, Collection, area)).Category);

        // The interface has no MulticastableTransferAreas: reported, not thrown.
        var (missing, reason) = TransferAreaRun.Composition(networkInterface, NetworkSystemNames.TransferAreaCollections.Multicastable);
        Assert.Null(missing);
        Assert.Contains("MulticastableTransferAreas", reason);
        // An overload the installation does not offer is api_member_missing.
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, Assert.Throws<RuntimeBindingException>(() =>
            TransferAreaRun.Create(kept, new TransferAreaRequestInfo { Collection = Collection, Name = "TA_2", PositionNumber = 3 }, AreaType.CD, null)).Category);
    }

    private static NetworkObjectTarget Item => new()
    {
        Kind = "deviceItem", DeviceName = "PLC_1",
        ItemPath = new[] { new NetworkDeviceItemPathSegment { Index = 0, Name = "Rack_0", PositionNumber = 0, TypeIdentifier = "System:Rack.S71500" } },
    };

    private static bool Valid(NetworkOperationRequest operation) => NetworkOperationCatalog.ValidateWrite(new[] { operation }).IsValid;

    [Fact]
    public void Catalog_ValidatesTransferAreaShapes_AndTheIoSystemTarget()
    {
        NetworkOperationRequest Create(NetworkTransferAreaRequest area, NetworkObjectTarget? partner = null)
            => new() { OperationId = "t", Operation = HardwareModuleMethods.CreateTransferArea, Target = Item, TransferArea = area, Partner = partner };

        Assert.True(Valid(Create(new() { Collection = "transferAreas", Name = "TA", Type = "CD", PositionNumber = 2 })));
        Assert.False(Valid(Create(new() { Collection = "transferAreas", Type = "CD" })));
        Assert.False(Valid(Create(new() { Collection = "transferAreas", Name = "TA", Type = "Nope" })));
        Assert.False(Valid(Create(new() { Collection = "transferAreas", Name = "TA", Type = "CD" }, Item)));
        Assert.True(Valid(Create(new() { Collection = "multicastable", Type = "DDX", Name = "M", DataLength = 4 }, Item)));
        Assert.True(Valid(Create(new() { Collection = "multicastable", Type = "DDX", TemplateIndex = 0 }, Item)));
        Assert.False(Valid(Create(new() { Collection = "multicastable", Type = "DDX", TemplateIndex = 0, Name = "M" }, Item)));
        Assert.False(Valid(Create(new() { Collection = "multicastable", Type = "DDX", DataLength = 4 }, Item)));
        Assert.False(Valid(Create(new() { Collection = "multicastable", Type = "DDX" })));
        Assert.False(Valid(Create(new() { Collection = "other", Type = "DDX" }, Item)));

        var delete = new NetworkOperationRequest { OperationId = "d", Operation = HardwareModuleMethods.DeleteTransferArea, Target = Item, TransferArea = new() { Collection = "transferAreas", Index = 0, Name = "TA" } };
        Assert.True(Valid(delete));
        delete.TransferArea.Type = "CD";
        Assert.False(Valid(delete));
        Assert.False(Valid(new NetworkOperationRequest { OperationId = "u", Operation = HardwareModuleMethods.UpdateTransferArea, Target = Item, TransferArea = new() { Collection = "transferAreas", Index = 0, Name = "TA" } }));

        var ioSystem = new NetworkObjectTarget { Kind = "ioSystem", SubnetId = "s", Number = 100 };
        Assert.True(Valid(new NetworkOperationRequest { OperationId = "i", Operation = HardwareModuleMethods.DeleteIoSystem, Target = ioSystem }));
        Assert.False(Valid(new NetworkOperationRequest { OperationId = "i", Operation = HardwareModuleMethods.DeleteIoSystem, Target = Item }));
        Assert.False(Valid(new NetworkOperationRequest { OperationId = "i", Operation = HardwareModuleMethods.DeleteIoSystem, Target = new NetworkObjectTarget { Kind = "ioSystem", SubnetId = "s" } }));
        Assert.True(Valid(new NetworkOperationRequest { OperationId = "c", Operation = HardwareModuleMethods.DisconnectFromIoSystem, Target = Item }));
        Assert.False(Valid(new NetworkOperationRequest { OperationId = "c", Operation = HardwareModuleMethods.DisconnectFromIoSystem, Target = ioSystem }));
    }

    [Fact]
    public void Policy_ReadObserves_WritesMutate()
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(HardwareModuleMethods.ListTransferAreas));
        Assert.Equal("network_read", ProjectCapabilityCatalog.OperationFor(HardwareModuleMethods.ListTransferAreas));
        foreach (var method in new[] { HardwareModuleMethods.CreateTransferArea, HardwareModuleMethods.UpdateTransferArea, HardwareModuleMethods.DeleteTransferArea,
            HardwareModuleMethods.DeleteIoSystem, HardwareModuleMethods.DisconnectFromIoSystem })
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("network_write", ProjectCapabilityCatalog.OperationFor(method));
        }
    }

    private static object Segment(int index, string name, int position, string type) => new { index, name, positionNumber = position, typeIdentifier = type };

    private static object InterfaceItem(int index, string name, int position) => new
    {
        kind = "deviceItem", deviceName = "PLC_1",
        itemPath = new[]
        {
            Segment(0, "Rack_0", 0, "System:Rack.S71500"), Segment(0, "PLC_1", 1, "OrderNumber:6ES7 516-3AN01-0AB0/V2.8"),
            // K95: the built-in interface has no type identifier; "" is accepted like null.
            Segment(index, name, position, ""),
        },
    };

    private static object X1 => InterfaceItem(0, "PROFINET interface_1", 32768);

    private static object X2 => InterfaceItem(1, "PROFINET interface_2", 33024);

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, string tool, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync(tool, arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Item0(JsonElement root, int index = 0) => root.GetProperty("batch").GetProperty("operations")[index];

    [Fact]
    public async Task Mcp_TransferAreas_CreateUpdateDelete()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "hardware-modules");

        var created = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "a", operation = "create_transfer_area", target = X1, transferArea = new { collection = "transferAreas", name = "TA_1", type = "CD", positionNumber = 2 } },
        });
        Assert.True(created.GetProperty("success").GetBoolean(), created.GetRawText());
        Assert.Equal("TA_1", Item0(created).GetProperty("result").GetProperty("after").GetProperty("name").GetString());

        var multicast = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "m", operation = "create_transfer_area", target = X1, partner = X2, transferArea = new { collection = "multicastable", type = "DDX", name = "ToX2", dataLength = 8 } },
        });
        Assert.True(multicast.GetProperty("success").GetBoolean(), multicast.GetRawText());
        Assert.Equal("PROFINET interface_2", Item0(multicast).GetProperty("result").GetProperty("partner").GetProperty("name").GetString());

        var list = await CallAsync(harness, "network_read", new object[] { new { operationId = "l", operation = "list_transfer_areas", target = X1 } });
        var result = Item0(list).GetProperty("result");
        Assert.Equal("TA_1", Assert.Single(result.GetProperty("transferAreas").EnumerateArray()).GetProperty("name").GetString());
        var area = Assert.Single(result.GetProperty("multicastableTransferAreas").EnumerateArray());
        Assert.Contains(area.GetProperty("attributes").EnumerateArray(), attribute => attribute.GetProperty("name").GetString() == "Comment");

        var update = new
        {
            operationId = "u", operation = "update_transfer_area", target = X1, transferArea = new { collection = "multicastable", index = 0, name = "ToX2" },
            attributes = new object[] { new { name = "Comment", kind = "string", value = "to X2" } },
        };
        var updated = await CallAsync(harness, "network_write", new object[] { update });
        Assert.True(updated.GetProperty("success").GetBoolean(), updated.GetRawText());
        Assert.Equal("applied", Item0(updated).GetProperty("result").GetProperty("attributes")[0].GetProperty("status").GetString());

        var stale = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "d", operation = "delete_transfer_area", target = X1, transferArea = new { collection = "transferAreas", index = 0, name = "Other" } },
        });
        Assert.Equal("target_evidence_mismatch", stale.GetProperty("error").GetProperty("category").GetString());

        var deleted = await CallAsync(harness, "network_write", new object[]
        {
            new { operationId = "d", operation = "delete_transfer_area", target = X1, transferArea = new { collection = "transferAreas", index = 0, name = "TA_1" } },
        });
        Assert.True(deleted.GetProperty("success").GetBoolean(), deleted.GetRawText());
        Assert.Contains(deleted.GetProperty("guards").EnumerateArray(), guard => guard.GetProperty("id").GetString() == "network_deletes_transfer_area");
        Assert.Equal(0, Item0(deleted).GetProperty("result").GetProperty("countAfter").GetInt32());
    }

    [Fact]
    public async Task Mcp_DisconnectFromIoSystem_ThenDeleteTheIoSystem()
    {
        using var audit = new TempAuditDirectory();
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "hardware-modules");
        var ioSystem = new { kind = "ioSystem", subnetId = "subnet-pn-1", number = 100 };

        var preview = await CallAsync(harness, "network_write", new object[] { new { operationId = "i", operation = "delete_io_system", target = ioSystem } }, dryRun: true);
        Assert.Equal("preview", preview.GetProperty("phase").GetString());
        var guard = Assert.Single(preview.GetProperty("guards").EnumerateArray(), entry => entry.GetProperty("id").GetString() == "network_deletes_io_system");
        Assert.Contains("PLC_1", guard.GetProperty("message").GetString());

        var disconnected = await CallAsync(harness, "network_write", new object[] { new { operationId = "c", operation = "disconnect_from_io_system", target = X1 } });
        Assert.True(disconnected.GetProperty("success").GetBoolean(), disconnected.GetRawText());
        Assert.Equal(100, Item0(disconnected).GetProperty("result").GetProperty("ioSystemNumber").GetInt32());

        var again = await CallAsync(harness, "network_write", new object[] { new { operationId = "c", operation = "disconnect_from_io_system", target = X1 } });
        Assert.Equal("engineering_rejected", Item0(again).GetProperty("failure").GetProperty("category").GetString());

        var deleted = await CallAsync(harness, "network_write", new object[] { new { operationId = "i", operation = "delete_io_system", target = ioSystem } });
        Assert.True(deleted.GetProperty("success").GetBoolean(), deleted.GetRawText());
        Assert.Equal("PROFINET IO-System", Item0(deleted).GetProperty("result").GetProperty("name").GetString());

        var gone = await CallAsync(harness, "network_write", new object[] { new { operationId = "i", operation = "delete_io_system", target = ioSystem } });
        Assert.Equal("target_not_found", gone.GetProperty("error").GetProperty("category").GetString());
    }
}
