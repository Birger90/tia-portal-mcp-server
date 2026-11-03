using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// K22 (tag comments through create_tag/update_tag), K24 (real read-backs instead of trusted), K19 for blocks and K01/K20
/// (a write whose host re-read is refused for an inconsistent DB) against the plc-write-roundtrip FakeWorker, whose project
/// languages are en-US and de-DE (fr-FR is known but inactive) and whose SetPoint change leaves a DB inconsistent.
/// </summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcTagCommentAndReadBackTests
{
    private const string Roundtrip = "plc-write-roundtrip";

    private static PlcOperationRequest Op(string id, string operation, Action<PlcOperationRequest> configure)
    {
        var request = new PlcOperationRequest { OperationId = id, Operation = operation, PlcName = "Station_1", TableName = "Default tag table" };
        configure(request);
        return request;
    }

    private static string? FailureMessage(PlcGuardedWriteResponse response)
        => response.Error?.Message ?? string.Join("; ", response.Batch?.Operations.Select(o => o.Failure?.Message) ?? Array.Empty<string?>());

    [Fact]
    public void Catalog_TakesCommentWithCommentCultureOnCreateAndUpdateTag()
    {
        var create = Op("c", "create_tag", r => { r.Name = "T"; r.DataType = "Bool"; r.Comment = "x"; r.CommentCulture = "de-DE"; });
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { create }).IsValid);
        var update = Op("u", "update_tag", r => { r.Name = "Start"; r.Comment = ""; r.CommentCulture = "en-US"; });
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { update }).IsValid);

        Assert.Contains("commentCulture (e.g. en-US) is required with comment",
            PlcOperationCatalog.ValidateWrite(new[] { Op("c", "create_tag", r => { r.Name = "T"; r.DataType = "Bool"; r.Comment = "x"; }) }).Error);
        Assert.Contains("commentCulture is only valid with comment",
            PlcOperationCatalog.ValidateWrite(new[] { Op("u", "update_tag", r => { r.Name = "Start"; r.CommentCulture = "de-DE"; }) }).Error);
        Assert.Contains("'comment' is not valid for delete_tag",
            PlcOperationCatalog.ValidateWrite(new[] { Op("d", "delete_tag", r => { r.Name = "Start"; r.Comment = "x"; r.CommentCulture = "de-DE"; }) }).Error);
    }

    [Fact]
    public void CommentRules_ResolveTheCatalogueCultureAndWarnForAnInactiveOne()
    {
        var catalogue = new[] { "en-US", "de-DE", "fr-FR" };
        var active = new[] { "en-US", "de-DE" };
        Assert.Equal(("de-DE", (string?)null, (string?)null), PlcTagCommentRules.Resolve("DE-de", catalogue, active));
        var (culture, warning, refusal) = PlcTagCommentRules.Resolve("fr-FR", catalogue, active);
        Assert.Equal("fr-FR", culture);
        Assert.Contains("TIA Portal decides", warning);
        Assert.Null(refusal);
        var unknown = PlcTagCommentRules.Resolve("xx-XX", catalogue, active);
        Assert.Null(unknown.Culture);
        Assert.Contains("is not a TIA Portal language", unknown.Refusal);
        Assert.Contains("en-US, de-DE", unknown.Refusal);
    }

    /// <summary>K22: create_tag and update_tag write the comment of one culture and verify it by re-reading the comments.</summary>
    [Fact]
    public async Task CreateAndUpdateTag_WriteTheCommentAndVerifyItByReRead()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            Op("create", "create_tag", r => { r.Name = "K22Tag"; r.DataType = "Bool"; r.Comment = "Pumpe läuft"; r.CommentCulture = "de-de"; }),
            Op("update", "update_tag", r => { r.Name = "Start"; r.Comment = "Start pump 3"; r.CommentCulture = "en-US"; }));

        Assert.True(response.Success, FailureMessage(response));
        Assert.Equal(new[] { ("comment[de-de]", (string?)null, (string?)"Pumpe läuft") },
            response.Effects[0].Effect!.Changes.Where(c => c.Field.StartsWith("comment", StringComparison.Ordinal)).Select(c => (c.Field, c.Current, c.Requested)));
        var created = CanonicalJson.Deserialize<TagMutationResultInfo>(response.Batch!.Operations[0].Result!.Value.GetRawText());
        Assert.Equal(("de-DE", "Pumpe läuft"), (created.CommentCulture, created.Comment));
        var create = response.Verification!.Operations.Single(v => v.OperationId == "create");
        Assert.Equal((PlcVerificationPolicy.TagCheck, true), (create.Check, create.Success));
        Assert.Equal("/Default tag table/K22Tag exists; comment[de-DE]=Pumpe läuft", create.Expected);
        Assert.Equal(create.Expected, create.Actual);
        var update = response.Verification.Operations.Single(v => v.OperationId == "update");
        Assert.Equal((PlcVerificationPolicy.TagCheck, true, "/Default tag table/Start exists; comment[en-US]=Start pump 3"), (update.Check, update.Success, update.Actual));

        // The other language of Start stays untouched.
        var comments = await PlcSourceFolderOperations.InvokeReadAsync(fixture.Client, new PlcOperationRequest
        {
            OperationId = "r", Operation = PlcSourceFolderMethods.ReadTagComments, PlcName = "Station_1", TableName = "Default tag table",
        });
        Assert.Contains("Pumpe 1 starten", comments.Payload);
    }

    /// <summary>K22: a culture TIA Portal has no item for fails after the create and removes the created tag; an unknown one is refused before anything changes.</summary>
    [Fact]
    public async Task CreateTag_WithACultureWithoutItem_FailsAndRemovesTheTag_AndAnUnknownCultureChangesNothing()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);
        var before = await fixture.InventoryAsync();

        var inactive = await fixture.RunAsync(false, Op("fr", "create_tag", r => { r.Name = "K22Fr"; r.DataType = "Bool"; r.Comment = "Pompe"; r.CommentCulture = "fr-FR"; }));
        var failure = inactive.Batch!.Operations[0].Failure!;
        Assert.Equal(WorkerFailureCategories.TargetNotFound, failure.Category);
        Assert.Contains("no comment text item for culture 'fr-FR'", failure.Message);
        Assert.Contains("deleted again", failure.Message);
        Assert.Equal(before, await fixture.InventoryAsync());

        var unknown = await fixture.RunAsync(false, Op("xx", "create_tag", r => { r.Name = "K22Xx"; r.DataType = "Bool"; r.Comment = "?"; r.CommentCulture = "xx-XX"; }));
        Assert.Equal(WorkerFailureCategories.ValidationError, unknown.Batch!.Operations[0].Failure!.Category);
        Assert.Contains("is not a TIA Portal language", unknown.Batch.Operations[0].Failure!.Message);
        Assert.Equal(before, await fixture.InventoryAsync());
        // A failed item keeps the shared rule: no verification entry.
        Assert.Empty(unknown.Verification!.Operations);
    }

    /// <summary>K24: create_tag, delete_tag and delete_block are re-read instead of trusted (move_tag: HardwareModuleMcpTests).</summary>
    [Fact]
    public async Task TagAndBlockWrites_AreVerifiedByReRead()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            Op("create", "create_tag", r => { r.Name = "K24Tag"; r.DataType = "Int"; }),
            Op("delete", "delete_tag", r => { r.Name = "Start"; }),
            new PlcOperationRequest { OperationId = "block", Operation = "delete_block", BlockPath = "Station_1/Blocks/Empty_DB" });

        Assert.True(response.Success, FailureMessage(response));
        var checks = response.Verification!.Operations.ToDictionary(v => v.OperationId);
        Assert.Equal((PlcVerificationPolicy.TagCheck, true, "/Default tag table/K24Tag exists"), (checks["create"].Check, checks["create"].Success, checks["create"].Actual));
        Assert.Equal((PlcVerificationPolicy.TagCheck, "/Default tag table/Start absent", "/Default tag table/Start absent"),
            (checks["delete"].Check, checks["delete"].Expected, checks["delete"].Actual));
        Assert.Equal((PlcVerificationPolicy.BlockCheck, true, "Station_1/Blocks/Empty_DB absent"), (checks["block"].Check, checks["block"].Success, checks["block"].Actual));
        Assert.DoesNotContain(response.Verification.Operations, v => v.Check == "trusted");
    }

    /// <summary>K19 for blocks: block names are unique per PLC, so a later create of the name in another group supersedes the delete.</summary>
    [Fact]
    public async Task DeleteBlock_IsSupersededByALaterCreateOfTheNameInAnotherGroup()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "delete", Operation = "delete_block", BlockPath = "Station_1/Blocks/Locked" },
            new PlcOperationRequest { OperationId = "create", Operation = "create_block", BlockPath = "Station_1/Blocks/Motors/Locked", BlockType = "FC", Language = "LAD" });

        Assert.True(response.Success, FailureMessage(response));
        var delete = response.Verification!.Operations.Single(v => v.OperationId == "delete");
        Assert.Equal(WriteVerificationChecks.Superseded, delete.Check);
        Assert.Contains("'create'", delete.Message);
    }

    /// <summary>
    /// K01/K20: update_db_member is applied and read back by the worker, but the host's re-read is refused because the DB is
    /// inconsistent until compiled: the item succeeds with an unverifiable entry naming compile_check; plc_read carries the hint.
    /// </summary>
    [Fact]
    public async Task DbMemberUpdate_WhoseReReadHitsAnInconsistentDb_IsUnverifiableNotFailed()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "setpoint", Operation = "update_db_member", BlockPath = "Station_1/Settings", MemberPath = "Speed", SetPoint = true,
        });

        Assert.True(response.Success, FailureMessage(response));
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        var entry = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.UnverifiableCheck, true), (entry.Check, entry.Success));
        Assert.Equal(("SetPoint=True", "SetPoint=True"), (entry.Expected, entry.Actual));
        Assert.Contains(PlcExportConsistency.Marker, entry.Message);
        Assert.Contains("compile_check with blockPath", entry.Message);

        var read = await fixture.Client.PlcBlockEditAsync(PlcBlockEditMethods.GetDbValues, new PlcBlockEditRequestInfo { BlockPath = "Station_1/Settings" });
        Assert.False(read.Success);
        Assert.True(PlcExportConsistency.Carries(read.Error), read.Error);
    }

    /// <summary>
    /// K02/K20 (live D2): move_block after renumber_block of the same block in one call. The renumber's verification re-read is
    /// refused for the then inconsistent block (unverifiable, not failed); the move still fails, because the export is the
    /// move's own first step, and its failure names compile_check.
    /// </summary>
    [Fact]
    public async Task MoveAfterRenumber_StillFailsWithTheCompileHint_AndTheRenumberIsUnverifiable()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, Roundtrip);

        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "renumber", Operation = "renumber_block", BlockPath = "Station_1/Blocks/Motors/FB_Motor", Number = 4242 },
            new PlcOperationRequest { OperationId = "move", Operation = "move_block", BlockPath = "Station_1/Blocks/Motors/FB_Motor", TargetGroupPath = "Station_1/Blocks" });

        Assert.False(response.Success);
        Assert.Equal(new[] { "succeeded", "failed" }, response.Batch!.Operations.Select(o => o.Status));
        var move = response.Batch.Operations[1].Failure!;
        Assert.Equal(WorkerFailureCategories.EngineeringRejected, move.Category);
        Assert.Contains("compile_check with blockPath", move.Message);
        // The failed move may have touched the block, so it supersedes the renumber.
        Assert.Equal(WriteVerificationChecks.Superseded, response.Verification!.Operations.Single(v => v.OperationId == "renumber").Check);

        var alone = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "renumber", Operation = "renumber_block", BlockPath = "Station_1/Blocks/Settings", Number = 4242 });
        Assert.True(alone.Success, FailureMessage(alone));
        var renumber = Assert.Single(alone.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.UnverifiableCheck, true), (renumber.Check, renumber.Success));
        Assert.Equal(renumber.Expected, renumber.Actual);
        Assert.Contains(PlcExportConsistency.Marker, renumber.Message);
    }
}
