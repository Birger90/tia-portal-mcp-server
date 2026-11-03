using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing;

/// <summary>get_db_snapshot / create_db_snapshot (adopted from BetterTIA/watch watch_db): the snapshot reader and the guarded round trip.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcDbSnapshotTests
{
    private const string Export = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <SW.Interface.Snapshot ID="0">
            <AttributeList>
              <Snapshot>
                <Values xmlns="http://www.siemens.com/automation/Openness/SW/Interface/Snapshot/v1">
                  <Value Path="Speed" Type="Int">250</Value>
                  <Value Path="Limits.regMin" Type="Int">-500</Value>
                  <Value Path="Limits.regMax" Type="Int">30000</Value>
                  <Value Path="LimitsX" Type="Bool">TRUE</Value>
                  <Value Path="Buffer[1]" Type="Real">1.5</Value>
                </Values>
              </Snapshot>
              <SnapshotDate>2026-10-16T10:00:00Z</SnapshotDate>
            </AttributeList>
          </SW.Interface.Snapshot>
        </Document>
        """;

    [Fact]
    public void Reader_ReturnsTypedValuesAndTheSnapshotDate()
    {
        var reading = PlcDbSnapshotDocument.Read(Export, null, 100);

        Assert.True(reading.HasSnapshot);
        Assert.Equal("2026-10-16T10:00:00Z", reading.SnapshotDate);
        Assert.Equal(5, reading.TotalValues);
        Assert.Equal(("Speed", "250", "Int"), (reading.Values[0].Path, reading.Values[0].Value, reading.Values[0].DataType));
        Assert.False(reading.Truncated);
    }

    [Fact]
    public void Reader_FiltersAtOrBelowAMemberAndCapsTheValues()
    {
        var limits = PlcDbSnapshotDocument.Read(Export, "limits", 100);
        Assert.Equal(new[] { "Limits.regMin", "Limits.regMax" }, limits.Values.Select(v => v.Path));
        Assert.Equal((5, 2), (limits.TotalValues, limits.MatchedValues));

        var capped = PlcDbSnapshotDocument.Read(Export, null, 2);
        Assert.True(capped.Truncated);
        Assert.Equal(2, capped.Values.Count);

        var none = PlcDbSnapshotDocument.Read(Export, "Missing", 100);
        Assert.Empty(none.Values);
        Assert.Equal(new[] { "Speed", "Limits", "LimitsX", "Buffer" }, none.TopLevelMembers);
        Assert.True(PlcDbSnapshotDocument.IsAtOrBelow("Buffer[1]", "Buffer"));
        Assert.False(PlcDbSnapshotDocument.IsAtOrBelow("LimitsX", "Limits"));
    }

    [Fact]
    public void Reader_ReportsAnExportWithoutSnapshotAndRefusesNonXml()
    {
        Assert.False(PlcDbSnapshotDocument.Read("<Document><X><AttributeList /></X></Document>", null, 10).HasSnapshot);
        Assert.Throws<ArgumentException>(() => PlcDbSnapshotDocument.Read("not xml", null, 10));
    }

    [Fact]
    public void Catalog_GatesTheReadAsExportAndTheCreateAsMutation()
    {
        Assert.Contains(PlcBlockEditMethods.GetDbSnapshot, PlcOperationCatalog.ReadOperationNames);
        Assert.Contains(PlcBlockEditMethods.CreateDbSnapshot, PlcOperationCatalog.WriteOperationNames);
        Assert.Equal(OperationCapability.TemporaryExport, OperationPolicyCatalog.GetCapability(PlcBlockEditMethods.GetDbSnapshot));
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(PlcBlockEditMethods.CreateDbSnapshot));
        Assert.Equal("plc_read", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(PlcBlockEditMethods.GetDbSnapshot));
        Assert.Equal("plc_write", TiaMcpServer.Contracts.Project.ProjectCapabilityCatalog.OperationFor(PlcBlockEditMethods.CreateDbSnapshot));
        var bad = new PlcOperationRequest { OperationId = "s", Operation = PlcBlockEditMethods.GetDbSnapshot, BlockPath = "PLC_1/Settings", MaxElements = 0 };
        Assert.Contains("maxElements must be between 1 and 20000", PlcOperationCatalog.ValidateRead(new[] { bad }).Error);
    }

    [Fact]
    public async Task CreateThenGet_StoresTheActualValuesAndVerifiesByReRead()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var empty = await ReadAsync(fixture, new PlcOperationRequest { OperationId = "g", Operation = PlcBlockEditMethods.GetDbSnapshot, BlockPath = "Station_1/Settings" });
        Assert.False(empty.HasSnapshot);

        var preview = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "c", Operation = PlcBlockEditMethods.CreateDbSnapshot, BlockPath = "Station_1/Settings" });
        Assert.Equal("preview", preview.Phase);
        Assert.Contains(preview.Guards, g => g.Id == PlcGuardDefinitions.StoresDbSnapshot && g.Severity == "info");
        Assert.False((await ReadAsync(fixture, new PlcOperationRequest { OperationId = "g", Operation = PlcBlockEditMethods.GetDbSnapshot, BlockPath = "Station_1/Settings" })).HasSnapshot);

        var response = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "c", Operation = PlcBlockEditMethods.CreateDbSnapshot, BlockPath = "Station_1/Settings", MemberPath = "Limits",
        });
        Assert.True(response.Success, response.Error?.Message ?? response.Batch?.Operations[0].Failure?.Message);
        var created = CanonicalJson.Deserialize<PlcDbSnapshotInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
        Assert.Equal(("Online", true), (created.OnlineState, created.HasSnapshot));
        Assert.Equal(new[] { "Limits.regMin", "Limits.regMax" }, created.Values.Select(v => v.Path));
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.DbSnapshotCheck, true), (check.Check, check.Success));

        var stored = await ReadAsync(fixture, new PlcOperationRequest { OperationId = "g", Operation = PlcBlockEditMethods.GetDbSnapshot, BlockPath = "Station_1/Settings", MaxElements = 1 });
        Assert.Equal((created.SnapshotDate, true, 1), (stored.SnapshotDate, stored.Truncated, stored.Values.Count));
        Assert.Equal("100", stored.Values[0].Value);
    }

    [Fact]
    public async Task Snapshot_RefusesABlockThatIsNotADataBlock()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var read = new PlcOperationRequest { OperationId = "g", Operation = PlcBlockEditMethods.GetDbSnapshot, BlockPath = "Station_1/Safety_FB" };

        var item = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));

        Assert.Equal(TiaMcpServer.Contracts.Worker.WorkerFailureCategories.TargetKindUnsupported, item.Failure?.Category);
    }

    private static async Task<PlcDbSnapshotInfo> ReadAsync(PlcGuardedWriteFixture fixture, PlcOperationRequest read)
    {
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { read }).IsValid, PlcOperationCatalog.ValidateRead(new[] { read }).Error);
        var item = PlcPayloadContract.Project(read, await PlcWorkerInvoker.InvokeReadAsync(fixture.Client, read));
        Assert.True(item.Result is not null, item.Failure?.Message);
        return CanonicalJson.Deserialize<PlcDbSnapshotInfo>(item.Result!.Value.GetRawText());
    }
}
