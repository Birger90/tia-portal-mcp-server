using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing;

/// <summary>rename_tags / rename_objects (adopted from BetterTIA/writer RenameTags / RenameObjects).</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcBatchRenameTests
{
    [Fact]
    public void Plan_ParksSwapsAndRotationsUnderTemporaryNames()
    {
        var steps = PlcBatchRenamePlan.Plan(new[] { "A", "B", "C", "D" }, new[] { "B", "C", "A", "E" }, "s");

        Assert.Equal(new[] { (0, "zzTmp_s_0"), (1, "zzTmp_s_1"), (2, "zzTmp_s_2"), (3, "E"), (0, "B"), (1, "C"), (2, "A") }, steps);
        Assert.Equal(new[] { (0, "abc") }, PlcBatchRenamePlan.Plan(new[] { "ABC" }, new[] { "abc" }, "s"));
        Assert.Empty(PlcBatchRenamePlan.Plan(new[] { "A" }, new[] { "A" }, "s"));
        Assert.Contains("appears twice", Assert.Throws<ArgumentException>(() => PlcBatchRenamePlan.Plan(new[] { "A", "B" }, new[] { "X", "x" }, "s")).Message);
    }

    [Fact]
    public void Catalog_RequiresRenamesAndRefusesTheTemporaryPrefix()
    {
        static string? Error(PlcOperationRequest item) => PlcOperationCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("renames is required", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameTags }));
        Assert.Contains("prefix of the temporary names", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameTags, Renames = new() { new() { Name = "A", NewName = "zzTmp_1" } } }));
        Assert.Contains("appears twice", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameObjects, Renames = new() { new() { Name = "A", NewName = "X" }, new() { Name = "a", NewName = "Y" } } }));
        Assert.Contains("'/'", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameObjects, Renames = new() { new() { Name = "A", NewName = "X/Y" } } }));
    }

    [Fact]
    public async Task RenameTags_SwapsTwoTagsInOneTransactionAndVerifiesTheInventory()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "swap", Operation = PlcBlockEditMethods.RenameTags, PlcName = "Station_1", Renames = new() { new() { Name = "Start", NewName = "Motor1" }, new() { Name = "Motor1", NewName = "Start" } } },
            new PlcOperationRequest { OperationId = "after", Operation = "update_tag", PlcName = "Station_1", TableName = "Motors", FolderPath = "/Line", Name = "Start", ExternalAccessible = false });

        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        Assert.Equal(new[] { ("tag /Default tag table/Start", "Start", "Motor1"), ("tag /Line/Motors/Motor1", "Motor1", "Start") },
            response.Effects[0].Effect!.Changes.Select(c => (c.Field, c.Current, c.Requested)));
        Assert.Equal("swap", response.Effects[1].Effect!.DependsOn);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.BatchRenameRuns);
        var check = response.Verification!.Operations[0];
        Assert.Equal((PlcVerificationPolicy.BatchRenameCheck, true), (check.Check, check.Success));
        var result = CanonicalJson.Deserialize<PlcBatchRenameResultInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
        Assert.Equal(4, result.Steps.Count);
        Assert.Contains("\"Motor1\"", await fixture.InventoryAsync());
    }

    [Fact]
    public async Task RenameTags_ResolvesEveryNameBeforeAnythingRuns()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameTags, PlcName = "Station_1", Renames = new() { new() { Name = "Nope", NewName = "X" } } });

        Assert.Equal("target_not_found", response.Error?.Category);
    }

    [Fact]
    public async Task RenameObjects_RollsBackEveryRenameWhenOneIsRefused()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var ok = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "ok", Operation = PlcBlockEditMethods.RenameObjects, PlcName = "Station_1",
            Renames = new() { new() { Name = "FC_Old", NewName = "FC_Legacy" }, new() { Name = "UDT_Settings", NewName = "UDT_Config" } },
        });
        Assert.True(ok.Success, ok.Error?.Message ?? ok.Batch?.Operations[0].Failure?.Message);
        Assert.Equal((PlcVerificationPolicy.BatchRenameCheck, true), (ok.Verification!.Operations[0].Check, ok.Verification.Operations[0].Success));

        var refused = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "bad", Operation = PlcBlockEditMethods.RenameObjects, PlcName = "Station_1",
            Renames = new() { new() { Name = "FC_Legacy", NewName = "FC_X" }, new() { Name = "Main", NewName = "RejectedByPortal" } },
        });
        Assert.False(refused.Success);
        var failure = refused.Batch!.Operations[0].Failure!;
        Assert.Equal("engineering_rejected", failure.Category);
        Assert.Contains("'FC_Legacy' is named 'FC_Legacy'", failure.Message);
    }
}
