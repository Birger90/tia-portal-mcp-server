using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.ChangeDetection;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.ChangeDetection;

/// <summary>
/// plc_read check_download_readiness (idea from 1748477582/tia-portal-openness-mcp CheckDownloadReadiness, MIT; reimplemented):
/// the Siemens-free verdict, the catalog and policy, the payload contract and the plc-change-detection FakeWorker end to end.
/// Not live-tested: whether PlcBlock.IsConsistent and the checksum agree with TIA Portal's own download check is decided live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class PlcDownloadReadinessTests
{
    private static PlcChecksumPlcInfo Checksum(string? state) => state is null
        ? new PlcChecksumPlcInfo { PlcName = "PLC_1", Unavailable = "not listed" }
        : new PlcChecksumPlcInfo
        {
            PlcName = "PLC_1", Available = true,
            Software = new RuntimeValueInfo { State = state, Value = state == RuntimeValueStates.Value ? "AB" : state == RuntimeValueStates.Empty ? string.Empty : null },
            TextLists = new RuntimeValueInfo { State = RuntimeValueStates.Value, Value = "CD" },
        };

    private static PlcDownloadReadinessPlcInfo Entry(string? checksumState, params (string Path, string? Consistent)[] blocks)
    {
        var entry = new PlcDownloadReadinessPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1", Checksums = Checksum(checksumState) };
        foreach (var (path, consistent) in blocks) PlcDownloadReadiness.Count(entry, path, consistent);
        return PlcDownloadReadiness.Evaluate(entry);
    }

    [Fact]
    public void EveryBlockConsistentAndAChecksum_IsReady()
    {
        var entry = Entry(RuntimeValueStates.Value, ("PLC_1/Blocks/Main", "True"), ("PLC_1/Blocks/FC1", "true"));

        Assert.Equal(PlcDownloadReadiness.Ready, entry.Verdict);
        Assert.Empty(entry.Reasons);
        Assert.Equal(2, entry.BlockCount);
    }

    [Fact]
    public void AnInconsistentBlock_IsNotReady_AndNamesTheBlockAndCompileCheck()
    {
        var entry = Entry(RuntimeValueStates.Value, ("PLC_1/Blocks/Main", "True"), ("PLC_1/Blocks/FB_Motor", "False"));

        Assert.Equal(PlcDownloadReadiness.NotReady, entry.Verdict);
        Assert.Equal(new[] { "PLC_1/Blocks/FB_Motor" }, entry.InconsistentBlocks);
        Assert.Contains("compile_check", Assert.Single(entry.Reasons), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RuntimeValueStates.Empty)]
    [InlineData(RuntimeValueStates.Null)]
    public void AnEmptySoftwareChecksum_IsNotReady(string state)
    {
        var entry = Entry(state, ("PLC_1/Blocks/Main", "True"));

        Assert.Equal(PlcDownloadReadiness.NotReady, entry.Verdict);
        Assert.Contains("checksum is empty", Assert.Single(entry.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingUnreadable_WithoutAContradiction_IsUnknown()
    {
        Assert.Equal(PlcDownloadReadiness.Unknown, Entry(RuntimeValueStates.Value, ("PLC_1/Blocks/Main", null)).Verdict);
        Assert.Equal(PlcDownloadReadiness.Unknown, Entry(null, ("PLC_1/Blocks/Main", "True")).Verdict);
        Assert.Equal(PlcDownloadReadiness.Unknown, Entry(RuntimeValueStates.Unreadable, ("PLC_1/Blocks/Main", "True")).Verdict);
        // An inconsistent block decides even when something else is unreadable.
        Assert.Equal(PlcDownloadReadiness.NotReady, Entry(null, ("PLC_1/Blocks/Main", "False"), ("PLC_1/Blocks/X", "?")).Verdict);
    }

    [Fact]
    public void InconsistentBlocks_AreCappedAndTheRestCounted()
    {
        var blocks = Enumerable.Range(0, PlcDownloadReadiness.MaxListedBlocks + 3).Select(i => ($"PLC_1/Blocks/B{i}", (string?)"False")).ToArray();
        var entry = Entry(RuntimeValueStates.Value, blocks);

        Assert.Equal(PlcDownloadReadiness.MaxListedBlocks, entry.InconsistentBlocks.Count);
        Assert.Equal(3, entry.InconsistentBlocksOmitted);
        Assert.Equal(PlcDownloadReadiness.MaxListedBlocks + 3, entry.InconsistentCount);
    }

    [Fact]
    public void IsAnObserveReadOfPlcRead_InEveryMode()
    {
        Assert.Contains(PlcChangeDetectionMethods.CheckDownloadReadiness, PlcChangeDetectionMethods.Read);
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcChangeDetectionMethods.CheckDownloadReadiness));
        Assert.Equal("plc_read", ProjectCapabilityCatalog.OperationFor(PlcChangeDetectionMethods.CheckDownloadReadiness));
        Assert.Contains(PlcChangeDetectionMethods.CheckDownloadReadiness, PlcOperationCatalog.ReadOperationNames);
    }

    [Fact]
    public void PayloadContract_AcceptsAConsistentResult_AndRejectsContradictions()
    {
        var op = new PlcOperationRequest { OperationId = "r", Operation = PlcChangeDetectionMethods.CheckDownloadReadiness, PlcName = "PLC_1" };
        PlcDownloadReadinessReadInfo Good() => new() { Plcs = { Entry(RuntimeValueStates.Value, ("PLC_1/Blocks/Main", "True"), ("PLC_1/Blocks/FB", "False")) } };

        var element = PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(Good()));
        Assert.Equal("notReady", element.GetProperty("plcs")[0].GetProperty("verdict").GetString());

        var bad = Good();
        bad.Plcs[0].Verdict = "maybe";
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(bad)));
        bad = Good();
        bad.Plcs[0].Reasons.Clear();
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(bad)));
        bad = Good();
        bad.Plcs[0].InconsistentCount = 5;
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(bad)));
        bad = Good();
        bad.Plcs[0].PlcName = "PLC_9";
        bad.Plcs[0].DeviceName = "Station_9";
        Assert.ThrowsAny<JsonException>(() => PlcChangeDetectionPayloadContract.DecodeRead(op, WorkerJson.SerializePayload(bad)));
    }

    [Fact]
    public async Task Mcp_ChecksEveryPlc_InReadOnly()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var ui = new FakeWorkerUiOpenProject("plc-change-detection");
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly, audit.Path, "plc-change-detection");

        var raw = await harness.Client.CallToolAsync("plc_read", new Dictionary<string, object?>
        {
            ["operations"] = new object[] { new { operationId = "r", operation = "check_download_readiness" } },
        });
        Assert.Empty(StructuredContractInspector.FindViolations(raw));
        var item = raw.StructuredContent!.Value.GetProperty("batch").GetProperty("operations")[0];

        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var plcs = item.GetProperty("result").GetProperty("plcs");
        Assert.Equal("notReady", plcs[0].GetProperty("verdict").GetString());
        Assert.Equal("PLC_1/Blocks/FB_Motor", plcs[0].GetProperty("inconsistentBlocks")[0].GetString());
        Assert.Equal("unknown", plcs[1].GetProperty("verdict").GetString());
        Assert.Equal(1, plcs[1].GetProperty("unreadableCount").GetInt32());
    }
}
