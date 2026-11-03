using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing;

/// <summary>export_tag_table / import_tag_table: one tag table as a SimaticML file (Siemens tags-and-tagtables skill).</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcTagTableFileTests
{
    private static PlcOperationRequest Export(string table, string? folder, string path, bool? overwrite = null, string id = "e") => new()
    {
        OperationId = id, Operation = PlcBlockEditMethods.ExportTagTable, PlcName = "Station_1", TableName = table, FolderPath = folder, FilePath = path, Overwrite = overwrite,
    };

    private static PlcOperationRequest Import(string path, string? folder = null, string? table = null, bool? overwrite = null, string id = "i") => new()
    {
        OperationId = id, Operation = PlcBlockEditMethods.ImportTagTable, PlcName = "Station_1", TableName = table, FolderPath = folder, FilePath = path, Overwrite = overwrite,
    };

    private static string Failures(PlcGuardedWriteResponse response)
        => $"{response.Phase} {response.Error?.Message} {string.Join("; ", response.Batch?.Operations.Select(o => o.Failure?.Message) ?? Array.Empty<string?>())} "
           + string.Join("; ", response.Verification?.Operations.Select(o => $"{o.Check}:{o.Success} {o.Expected} | {o.Actual} {o.Message}") ?? Array.Empty<string>());

    [Fact]
    public void Catalog_RequiresTheFileAndTheTableAndChecksThePath()
    {
        Assert.Contains("tableName", PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "e", Operation = PlcBlockEditMethods.ExportTagTable, FilePath = @"C:\x.xml" } }).Error);
        Assert.Contains("filePath", PlcOperationCatalog.ValidateWrite(new[] { new PlcOperationRequest { OperationId = "i", Operation = PlcBlockEditMethods.ImportTagTable } }).Error);
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { Export("Motors", "/Line", @"\\?\C:\x.xml") }).IsValid);
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Export("Motors", "/Line", @"\\server\share\Motors.xml") }).IsValid);
        Assert.Contains("tableName must be nonblank", PlcOperationCatalog.ValidateWrite(new[] { Import(@"C:\x.xml", table: " ") }).Error);
    }

    [Fact]
    public void ImportCheck_TakesReturnedNamesThenNewNamesThenTheExpectedName()
    {
        var before = new[] { "Default tag table", "Motors" };
        Assert.Equal(new[] { "Valves" }, PlcTagTableImportCheck.Imported(null, before, before.Append("Valves").ToArray(), null, false, "/").Tables);
        Assert.Equal(new[] { "Motors" }, PlcTagTableImportCheck.Imported(new[] { "Motors" }, before, before, null, true, "/").Tables);
        Assert.Equal(new[] { "Motors" }, PlcTagTableImportCheck.Imported(null, before, before, "motors", true, "/").Tables);
        var unknown = PlcTagTableImportCheck.Imported(null, before, before, null, true, "/");
        Assert.Empty(unknown.Tables);
        Assert.Null(unknown.Failure);
        Assert.Contains("pass tableName", unknown.Warning);
        Assert.Contains("no new tag table", PlcTagTableImportCheck.Imported(null, before, before, null, false, "/").Failure);
        Assert.Contains("is not in", PlcTagTableImportCheck.Imported(null, before, before, "Valves", false, "/").Failure);
        Assert.Contains("already in", PlcTagTableImportCheck.Imported(null, before, before, "Motors", false, "/").Failure);
        Assert.Contains("not tag table 'Valves'", PlcTagTableImportCheck.Imported(new[] { "Motors" }, before, before.Append("Valves").ToArray(), "Valves", true, "/").Failure);
    }

    [Fact]
    public async Task Export_WritesTheFileAndRefusesAnExistingOneWithoutOverwrite()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, Export("Motors", "/Line", @"C:\Exports\Motors.xml"));
        Assert.True(response.Success, Failures(response));
        var result = CanonicalJson.Deserialize<PlcTagTableFileResultInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
        Assert.Equal(("Motors", 1, 1, false, 64), (Assert.Single(result.Tables), result.TagCount, result.UserConstantCount, result.Overwrite, result.FileSha256.Length));
        Assert.Equal("file", Assert.Single(response.Verification!.Operations).Check); // K50: the worker re-hashed the placed file

        var again = await fixture.RunAsync(false, Export("Motors", "/Line", @"C:\Exports\Motors.xml"));
        Assert.Equal("validation_error", again.Batch!.Operations[0].Failure!.Category);

        var replaced = await fixture.RunAsync(false, Export("Motors", "/Line", @"C:\Exports\Motors.xml", overwrite: true));
        Assert.True(replaced.Success, Failures(replaced));
        Assert.True(CanonicalJson.Deserialize<PlcTagTableFileResultInfo>(replaced.Batch!.Operations[0].Result!.Value.GetRawText()).Overwrite);
    }

    [Fact]
    public async Task Export_OfAMissingTableIsRefusedBeforeAnythingRuns()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(true, Export("Valves", "/", @"C:\Exports\Valves.xml"));

        Assert.Equal("target_not_found", response.Error?.Category);
    }

    [Fact]
    public async Task Import_CreatesANewTableAndVerifiesItByReRead()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        // A later item of the call may use the imported table.
        var preview = await fixture.RunAsync(true, Import(@"C:\Imports\Valves.xml", "/", "Valves"),
            new PlcOperationRequest { OperationId = "t", Operation = "create_tag", PlcName = "Station_1", TableName = "Valves", Name = "Valve3", DataType = "Bool", LogicalAddress = "%Q1.2" });
        Assert.True(preview.Success, Failures(preview));

        var response = await fixture.RunAsync(false, Import(@"C:\Imports\Valves.xml", "/", "Valves"));
        Assert.True(response.Success, Failures(response));
        var result = CanonicalJson.Deserialize<PlcTagTableFileResultInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
        Assert.Equal(("Valves", 2, false), (Assert.Single(result.Tables), result.TagCount, result.Overwrite));
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.TagTableCheck, true, "/Valves tags=2 userConstants=0"), (check.Check, check.Success, check.Actual));
    }

    [Fact]
    public async Task Import_WithoutTableNameFindsTheNewTable()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, Import(@"C:\Imports\Valves.xml"));

        Assert.True(response.Success, Failures(response));
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.TagTableCheck, true), (check.Check, check.Success));
        Assert.Contains("/Valves", check.Actual);
    }

    [Fact]
    public async Task Import_OverAnExistingTableNeedsOverwriteAndFiresTheGuard()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var refused = await fixture.RunAsync(true, Import(@"C:\Imports\Motors.xml", "/Line", "Motors"));
        Assert.Equal("validation_error", refused.Error?.Category);

        var preview = await fixture.RunAsync(true, Import(@"C:\Imports\Motors.xml", "/Line", "Motors", overwrite: true));
        Assert.True(preview.Success, Failures(preview));
        Assert.Contains(preview.Guards, g => g.Id == PlcGuardDefinitions.ImportReplacesTagTable);

        var response = await fixture.RunAsync(false, Import(@"C:\Imports\Motors.xml", "/Line", "Motors", overwrite: true));
        Assert.True(response.Success, Failures(response));
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.TagTableCheck, true), (check.Check, check.Success));
        Assert.Contains("tags=3", check.Actual);
    }

    [Fact]
    public async Task Import_RoundTripsAnExportedTableAndReportsAPortalRejection()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, Export("Default tag table", "/", @"C:\Exports\Rejected.xml"), Import(@"C:\Exports\Rejected.xml", "/Line", "Default tag table"));

        Assert.False(response.Success);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Equal("engineering_rejected", response.Batch.Operations[1].Failure!.Category);
    }
}
