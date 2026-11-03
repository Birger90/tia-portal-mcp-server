using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing;

/// <summary>replace_tag_comments (adopted from BetterTIA/writer ReplaceTagComments).</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcTagCommentReplaceTests
{
    private static PlcOperationRequest Replace(string? culture = null, string? table = null, string? folder = null, params (string Find, string Replace)[] pairs) => new()
    {
        OperationId = "c", Operation = PlcBlockEditMethods.ReplaceTagComments, PlcName = "Station_1", Culture = culture, TableName = table, FolderPath = folder,
        Replacements = pairs.Select(p => new TiaMcpServer.Contracts.Plc.Interface.PlcTextReplacementInfo { Find = p.Find, Replace = p.Replace }).ToList(),
    };

    [Fact]
    public void Catalog_RequiresReplacementsAndATableForAFolder()
    {
        Assert.Contains("replacements is required", PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "c", Operation = PlcBlockEditMethods.ReplaceTagComments } }).Error);
        Assert.Contains("folderPath is only valid with tableName", PlcOperationCatalog.ValidateWrite(new[] { Replace(folder: "/Line", pairs: ("a", "b")) }).Error);
    }

    [Fact]
    public async Task Preview_ListsEveryChangedTextAndApplyVerifiesByReRead()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var preview = await fixture.RunAsync(true, Replace(null, null, null, ("pump 1", "pump 2"), ("Pump 10", "Pump 1")));
        Assert.True(preview.Success, preview.Error?.Message);
        Assert.Equal(new[] { ("comment /Default tag table/Start [en-US]", "Start pump 1", "Start pump 2"), ("comment /Line/Motors/Motor1 [en-US]", "Pump 10 motor", "Pump 1 motor") },
            preview.Effects[0].Effect!.Changes.Select(c => (c.Field, c.Current, c.Requested)));

        var response = await fixture.RunAsync(false, Replace("de-DE", "Default tag table", "/", ("Pumpe", "Ventil")));
        Assert.True(response.Success, response.Error?.Message ?? response.Batch?.Operations[0].Failure?.Message);
        var result = CanonicalJson.Deserialize<PlcTagCommentReplaceResultInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
        Assert.Equal(("Pumpe 1 starten", "Ventil 1 starten"), (result.Changes.Single().Old, result.Changes.Single().New));
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.TagCommentsCheck, true), (check.Check, check.Success));
    }

    [Fact]
    public async Task Plan_RefusesAReplacementThatChangesNothing()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(true, Replace(pairs: ("Valve", "Gate")));

        Assert.Equal("validation_error", response.Error?.Category);
    }

    /// <summary>
    /// K25 (live V21 2026-11-02): a no-op replace_tag_comments in a call with valid items. Planning refuses any item before the
    /// first one runs (as for every planning refusal of plc_write), so the refusal stays and its message says that no item ran.
    /// </summary>
    [Fact]
    public async Task Plan_RefusalOfOneItemSaysTheWholeCallWasNotRun()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var before = await fixture.InventoryAsync();

        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "t", Operation = "create_tag_table", PlcName = "Station_1", TableName = "K25 table" },
            Replace(pairs: ("Valve", "Gate")));

        Assert.Equal("validation_error", response.Error?.Category);
        Assert.Contains("replace_tag_comments changes nothing", response.Error!.Message);
        Assert.Contains("No item of this call was run", response.Error.Message);
        Assert.Contains("all 2 items", response.Error.Message);
        Assert.Equal(before, await fixture.InventoryAsync());

        // A single-item call names only its own refusal.
        var single = await fixture.RunAsync(false, Replace(pairs: ("Valve", "Gate")));
        Assert.DoesNotContain("No item of this call was run", single.Error!.Message);
    }
}
