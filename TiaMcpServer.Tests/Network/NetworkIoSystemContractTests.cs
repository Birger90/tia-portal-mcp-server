using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Network;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>Request, payload, guard and catalog contract of network_write create_io_system (K28; not live-tested).</summary>
public sealed class NetworkIoSystemContractTests
{
    private static NetworkObjectTarget Node(string kind = "node") => new()
    {
        Kind = kind, DeviceName = "PLC_1", NodeId = "node-plc",
        InterfacePath = new[]
        {
            new NetworkInterfacePathSegment { Name = "Rack_0", PositionNumber = 0 },
            new NetworkInterfacePathSegment { Name = "PLC_1", PositionNumber = 1 },
        },
    };

    private static NetworkOperationRequest Create(NetworkObjectTarget? target = null, string? name = null)
        => new() { OperationId = "io", Operation = NetworkIoSystemMethods.CreateIoSystem, Target = target ?? Node(), IoSystem = name is null ? null : new() { Name = name } };

    [Fact]
    public void Catalog_ListsCreateIoSystemAsAWriteWithAnOptionalName()
    {
        Assert.Contains(NetworkIoSystemMethods.CreateIoSystem, NetworkOperationCatalog.WriteOperationNames);
        Assert.True(NetworkOperationCatalog.TryGetSpec(NetworkIoSystemMethods.CreateIoSystem, out var spec));
        Assert.Equal(new[] { "target" }, spec!.RequiredFields);
        Assert.Equal(new[] { "ioSystem" }, spec.OptionalFields);
        Assert.True(NetworkOperationCatalog.ValidateWrite(new[] { Create(name: "PROFINET IO-System") }).IsValid);
        Assert.True(NetworkOperationCatalog.ValidateWrite(new[] { Create() }).IsValid);
    }

    [Theory]
    [InlineData("deviceItem")]
    [InlineData("subnet")]
    public void Validation_RequiresANodeTarget(string kind)
    {
        var validation = NetworkOperationCatalog.ValidateWrite(new[] { Create(Node(kind)) });
        Assert.False(validation.IsValid);
        Assert.Contains("'target.kind' must be 'node'", validation.Error);
    }

    [Fact]
    public void Validation_RefusesABlankNameAndAMissingNodeId()
    {
        var blank = NetworkOperationCatalog.ValidateWrite(new[] { Create(name: " ") });
        Assert.Contains("'ioSystem.name' is required and must be nonblank", blank.Error);
        var target = Node();
        target.NodeId = null;
        var missing = NetworkOperationCatalog.ValidateWrite(new[] { Create(target) });
        Assert.Contains("'target.nodeId' is required for kind 'node'", missing.Error);
    }

    [Fact]
    public void Validation_IoSystemNameIsInapplicableToOtherOperations()
    {
        var validation = NetworkOperationCatalog.ValidateWrite(new[]
        {
            new NetworkOperationRequest { OperationId = "s", Operation = "create_subnet", Subnet = new() { Name = "n", NetworkType = "Ethernet" }, IoSystem = new() { Name = "x" } },
        });
        Assert.Contains("'ioSystem' is not valid for create_subnet", validation.Error);
    }

    [Fact]
    public void Policy_CreateIoSystemIsAProjectMutationGatedAsNetworkWriteContent()
    {
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(NetworkIoSystemMethods.CreateIoSystem));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, NetworkIoSystemMethods.CreateIoSystem));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, NetworkIoSystemMethods.CreateIoSystem));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, NetworkIoSystemMethods.CreateIoSystem));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, NetworkIoSystemMethods.CreateIoSystem));
    }

    private static string Payload(string nodeId = "node-plc", string? requested = "IO", string subnetId = "subnet-1", int number = 100)
        => WorkerJson.SerializePayload(new CreateIoSystemResultInfo
        {
            DeviceName = "PLC_1", NodeId = nodeId, RequestedName = requested, Name = "IO", Number = number, SubnetId = subnetId,
        });

    [Fact]
    public void Payload_DecodesTheDeclaredResult()
    {
        var item = NetworkPayloadContract.Project(Create(name: "IO"), WorkerCallResult.Ok(Payload()));
        Assert.Equal("succeeded", item.Status);
        Assert.Equal(100, item.Result!.Value.GetProperty("number").GetInt32());
        Assert.Equal("subnet-1", item.Result.Value.GetProperty("subnetId").GetString());
    }

    [Theory]
    [InlineData("other-node", "IO", "subnet-1", 100)]
    [InlineData("node-plc", null, "subnet-1", 100)]
    [InlineData("node-plc", "IO", " ", 100)]
    [InlineData("node-plc", "IO", "subnet-1", -1)]
    public void Payload_ContradictingTheRequestIsAProtocolError(string nodeId, string? requested, string subnetId, int number)
    {
        var item = NetworkPayloadContract.Project(Create(name: "IO"), WorkerCallResult.Ok(Payload(nodeId, requested, subnetId, number)));
        Assert.Equal("failed", item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    private static NetworkWriteEffect Effect(bool complete, string? subnet, string? ioSubnet = null, string? ioNumber = null)
        => new(NetworkIoSystemMethods.CreateIoSystem,
            new NetworkWriteTargetEvidence("io", NetworkIoSystemMethods.CreateIoSystem, "PLC_1", null, Array.Empty<string>(), null, null, "node-plc", null, null, null, null),
            new Dictionary<string, string>(),
            new Dictionary<string, string?>
            {
                [NetworkSettingKeys.Subnet] = subnet, [NetworkSettingKeys.IoSystemSubnet] = ioSubnet, [NetworkSettingKeys.IoSystemNumber] = ioNumber,
            },
            Array.Empty<NetworkNodeIdentityInfo>(), complete);

    [Fact]
    public void Guards_AreInformativeOnly()
    {
        Assert.Equal(new[] { NetworkGuardDefinitions.IoSystemCreateUnverified },
            NetworkIoSystemGuards.Evaluate("io", Effect(true, "subnet-1")).Select(guard => guard.Id));
        Assert.Equal(new[] { NetworkGuardDefinitions.IoSystemCreateUnverified, NetworkGuardDefinitions.IoSystemNodeUnconnected },
            NetworkIoSystemGuards.Evaluate("io", Effect(true, null)).Select(guard => guard.Id));
        Assert.Equal(new[] { NetworkGuardDefinitions.IoSystemCreateUnverified, NetworkGuardDefinitions.IoSystemAlreadyAssigned },
            NetworkIoSystemGuards.Evaluate("io", Effect(true, "subnet-1", "subnet-1", "100")).Select(guard => guard.Id));
        Assert.Equal(new[] { NetworkGuardDefinitions.IoSystemCreateUnverified, NetworkGuardDefinitions.Unverifiable },
            NetworkIoSystemGuards.Evaluate("io", Effect(false, null)).Select(guard => guard.Id));
        var severities = NetworkGuardDefinitions.Definitions
            .Where(definition => definition.Id.StartsWith("network_io_system_", StringComparison.Ordinal)).Select(definition => definition.Severity).ToArray();
        Assert.Equal(3, severities.Length);
        Assert.All(severities, severity => Assert.Equal("info", severity));
    }
}
