using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.OpcUa;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.OpcUa;

/// <summary>
/// Issue #24: the OPC UA server interfaces and access rules of a PLC. Catalog, policy, decoding and verification, and the
/// plc-write-roundtrip FakeWorker end to end (an in-memory model; no Siemens evidence). Not live-tested.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcOpcUaTests
{
    [Fact]
    public void Policy_ReadsObserve_WritesMutate_AndMapToThePlcTools()
    {
        foreach (var method in PlcOpcUaMethods.Read)
        {
            Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(method));
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(method));
        }

        foreach (var method in PlcOpcUaMethods.Write)
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(method));
            Assert.Contains(method, PlcOperationCatalog.WriteOperationNames);
        }

        Assert.Contains(PlcOpcUaMethods.GetConfiguration, PlcOperationCatalog.ReadOperationNames);
    }

    public static TheoryData<PlcOperationRequest, string> Invalid => new()
    {
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.CreateInterface, InterfaceKind = "companion", InterfaceName = "X" }, "interfaceKind must be one of" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.CreateInterface, InterfaceKind = "referenceNamespace", InterfaceName = "X" }, "needs filePath" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.ImportInterface, InterfaceKind = "simatic", InterfaceName = "X", FilePath = @"C:\A.xml" }, "no Import" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.SetInterface, InterfaceKind = "server", InterfaceName = "X" }, "at least one of" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.SetInterface, InterfaceKind = "server", InterfaceName = "X", GenerateNodes = true }, "reference namespaces only" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.SetNamespaceRestriction, NamespaceUri = "urn:x" }, "at least one of" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.CreateRole, RoleName = "R" }, "definingNamespace" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.SetNamespacePermission, RoleName = "R", NamespaceUri = "urn:x", Permission = "Write" }, "enabled" },
        { new() { OperationId = "a", Operation = PlcOpcUaMethods.ExportInterface, InterfaceKind = "server", InterfaceName = "X", FilePath = "out.xml" }, "absolute path" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Validation_RefusesMisshapedItems(PlcOperationRequest item, string fragment)
    {
        var result = PlcOperationCatalog.ValidateWrite(new[] { item });
        Assert.False(result.IsValid);
        Assert.Contains(fragment, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_FiresTheLowersAccessGuard_OnlyForRelaxations()
    {
        var plc = new TiaMcpServer.Contracts.Plc.PlcTagInventoryPlcInfo { PlcName = "Station_1", DeviceName = "Station_1" };
        var relax = new PlcOperationRequest { OperationId = "r", Operation = PlcOpcUaMethods.SetNamespaceRestriction, NamespaceUri = "urn:x", SigningRequired = false };
        Assert.Contains(PlcOpcUa.Resolve(relax, plc).Guards, g => g.Id == PlcGuardDefinitions.OpcUaLowersAccess);
        var tighten = new PlcOperationRequest { OperationId = "t", Operation = PlcOpcUaMethods.SetNamespaceRestriction, NamespaceUri = "urn:x", SigningRequired = true };
        Assert.DoesNotContain(PlcOpcUa.Resolve(tighten, plc).Guards, g => g.Id == PlcGuardDefinitions.OpcUaLowersAccess);
        Assert.Contains(PlcOpcUa.Resolve(tighten, plc).Guards, g => g.Id == PlcGuardDefinitions.OpcUaChangesServer);
    }

    [Fact]
    public void Import_KeepsItsSpecificMessage_InOneGuardPerId()
    {
        // Security review F9: the import's own message (content replaced, verified by a re-export) was dropped by GroupBy(Id).First().
        var plc = new TiaMcpServer.Contracts.Plc.PlcTagInventoryPlcInfo { PlcName = "Station_1", DeviceName = "Station_1" };
        var item = new PlcOperationRequest { OperationId = "i", Operation = PlcOpcUaMethods.ImportInterface, InterfaceKind = "server", InterfaceName = "Machine", FilePath = @"C:\OpcUa\Machine.xml" };
        var guard = Assert.Single(PlcOpcUa.Resolve(item, plc).Guards, g => g.Id == PlcGuardDefinitions.OpcUaChangesServer);
        Assert.Contains("changes the OPC UA server configuration", guard.Message);
        Assert.Contains("replaces the content of the server interface 'Machine'", guard.Message);
        Assert.Equal(PlcOpcUa.Resolve(item, plc).Guards.Count, PlcOpcUa.Resolve(item, plc).Guards.Select(g => g.Id).Distinct().Count());
    }

    [Fact]
    public void Decode_AndVerify_FollowTheWorkerEvidence()
    {
        var item = new PlcOperationRequest { OperationId = "i", Operation = PlcOpcUaMethods.ImportInterface, InterfaceKind = "server", InterfaceName = "Machine", FilePath = @"C:\OpcUa\Machine.xml" };
        var result = new PlcOpcUaWriteResultInfo
        {
            Operation = PlcOpcUaMethods.ImportInterface, PlcName = "Station_1", Target = "server interface 'Machine'", Calls = { "ServerInterface.Import(FileInfo)" },
            Evidence = PlcOpcUaEvidence.NotReadBack, Note = "differs", Sha256 = new string('a', 64), ReExportSha256 = new string('b', 64),
        };
        var element = PlcOpcUa.DecodeWrite(item, WorkerJson.SerializePayload(result));
        var check = PlcOpcUa.Verify(item, null, new Dictionary<string, JsonElement> { ["i"] = element });
        Assert.False(check.Success);
        Assert.Equal(PlcOpcUa.Check, check.Check);

        var delete = new PlcOperationRequest { OperationId = "d", Operation = PlcOpcUaMethods.DeleteRole, RoleName = "R" };
        var deleted = new PlcOpcUaWriteResultInfo { Operation = PlcOpcUaMethods.DeleteRole, PlcName = "Station_1", Target = "role 'R'", Calls = { "RoleMapping.Delete()" }, Evidence = PlcOpcUaEvidence.Verified };
        Assert.ThrowsAny<JsonException>(() => PlcOpcUa.DecodeWrite(delete, WorkerJson.SerializePayload(deleted)));
        Assert.False(PlcOpcUa.Verify(delete, new TiaMcpServer.OperationBatches.StructuredOperationFailure("target_not_found", "x"), new Dictionary<string, JsonElement>()).Success);
    }

    [Fact]
    public async Task FakeWorker_WritesInterfacesRestrictionsAndRoles_AndAPlcWithoutTheProviderIsUnsupported()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip", McpAccessMode.ReadWrite);
        var applied = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "c", Operation = PlcOpcUaMethods.CreateInterface, PlcName = "Station_1", InterfaceKind = "server", InterfaceName = "Packaging" },
            new PlcOperationRequest { OperationId = "e", Operation = PlcOpcUaMethods.SetInterface, PlcName = "Station_1", InterfaceKind = "server", InterfaceName = "Packaging", Enabled = true },
            new PlcOperationRequest { OperationId = "x", Operation = PlcOpcUaMethods.ExportInterface, PlcName = "Station_1", InterfaceKind = "server", InterfaceName = "Machine", FilePath = @"C:\OpcUa\Out.xml" },
            new PlcOperationRequest { OperationId = "i", Operation = PlcOpcUaMethods.ImportInterface, PlcName = "Station_1", InterfaceKind = "server", InterfaceName = "Machine", FilePath = @"C:\OpcUa\Machine.xml" },
            new PlcOperationRequest { OperationId = "s", Operation = PlcOpcUaMethods.SetNamespaceRestriction, PlcName = "Station_1", NamespaceUri = "http://example.org/Machine", ApplyRestrictionsToBrowse = true },
            new PlcOperationRequest { OperationId = "r", Operation = PlcOpcUaMethods.CreateRole, PlcName = "Station_1", RoleName = "Maintenance", DefiningNamespace = "http://example.org/Machine" },
            new PlcOperationRequest { OperationId = "p", Operation = PlcOpcUaMethods.SetNamespacePermission, PlcName = "Station_1", RoleName = "Operator",
                NamespaceUri = "http://example.org/Machine", Permission = "Write", Enabled = true });
        Assert.True(applied.Success, JsonSerializer.Serialize(applied.Error));
        Assert.All(applied.Verification!.Operations, check => Assert.True(check.Success, check.Message));
        Assert.Contains(applied.Guards, g => g.Id == PlcGuardDefinitions.OpcUaLowersAccess);
        Assert.Equal(new string('e', 64), applied.Batch!.Operations[2].Result!.Value.GetProperty("sha256").GetString());

        var unknownPermission = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "u", Operation = PlcOpcUaMethods.SetNamespacePermission, PlcName = "Station_1", RoleName = "Operator",
            NamespaceUri = "http://example.org/Machine", Permission = "Execute", Enabled = false,
        });
        Assert.False(Assert.Single(unknownPermission.Verification!.Operations).Success);

        var read = await fixture.Client.PlcOpcUaAsync(PlcOpcUaMethods.GetConfiguration, new PlcOpcUaRequestInfo());
        Assert.True(read.Success, read.Error);
        var configuration = PlcOpcUa.DecodeRead(new PlcOperationRequest { OperationId = "g", Operation = PlcOpcUaMethods.GetConfiguration }, read.Payload);
        var plc1 = configuration.GetProperty("plcs").EnumerateArray().Single(p => p.GetProperty("plcName").GetString() == "PLC_1");
        Assert.Contains(plc1.GetProperty("serverInterfaces").EnumerateArray(), i => i.GetProperty("name").GetString() == "Packaging" && i.GetProperty("enabled").GetBoolean());
        Assert.Contains(plc1.GetProperty("roleMappings").EnumerateArray(), r => r.GetProperty("name").GetString() == "Maintenance");
        Assert.False(configuration.GetProperty("plcs").EnumerateArray().Single(p => p.GetProperty("plcName").GetString() == "PLC_2").GetProperty("providerAvailable").GetBoolean());

        var other = await fixture.RunAsync(false, new PlcOperationRequest { OperationId = "o", Operation = PlcOpcUaMethods.DeleteRole, PlcName = "PLC_2", RoleName = "Operator" });
        Assert.Equal("unsupported_capability", other.Batch!.Operations.Single().Failure!.Category);
    }
}
