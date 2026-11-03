using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The read-backs of the operations adopted from BetterTIA.</summary>
public sealed partial class PlcVerificationPolicy
{
    public const string DbSnapshotCheck = "dbSnapshot";
    public const string NetworkTextCheck = "networkText";
    public const string RenamedMembersCheck = "renamedMembers";
    public const string BatchRenameCheck = "batchRename";
    public const string TagCommentsCheck = "tagComments";

    private static bool HandlesEditingReadBack(string operation)
        => operation == PlcBlockEditMethods.CreateDbSnapshot || PlcBlockEditMethods.DocumentEdits.Contains(operation, StringComparer.Ordinal)
            || PlcBlockEditMethods.BatchWrites.Contains(operation, StringComparer.Ordinal) || operation == PlcBlockEditMethods.ImportTagTable;

    private Task<WriteOperationVerification> EditingReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
        => item.Operation switch
        {
            PlcBlockEditMethods.CreateDbSnapshot => DbSnapshotReadBackAsync(item, failure),
            PlcBlockEditMethods.RenameMembers => RenamedMembersReadBackAsync(item, failure),
            PlcBlockEditMethods.RenameTags => TagRenameReadBackAsync(item, failure),
            PlcBlockEditMethods.RenameObjects => Task.FromResult(ObjectRenameReadBack(item, failure)),
            PlcBlockEditMethods.ReplaceTagComments => TagCommentsReadBackAsync(item, failure),
            PlcBlockEditMethods.ImportTagTable => TagTableImportReadBackAsync(item, failure),
            _ => DocumentReadBackAsync(item, failure),
        };

    /// <summary>import_tag_table: list_tag_tables again; every table the worker reports imported must be in the folder with the reported counts.</summary>
    private async Task<WriteOperationVerification> TagTableImportReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var plcName = effects[item.OperationId].Target.PlcName;
        var folder = NormalizeFolder(item.FolderPath);
        var read = await client.ListTagTablesAsync(item.PlcName ?? plcName).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = "list_tag_tables" }, read);
        if (projected.Result is not { } result)
            return failure is null
                ? Fail(item, TagTableCheck, $"Operation '{item.OperationId}': the tag tables could not be re-read ({projected.Failure?.Message}).")
                : new(item.OperationId, false, TagTableCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the tag tables could not be re-read.");
        var tables = CanonicalJson.Deserialize<PlcTagInventoryInfo>(result.GetRawText()).Plcs
            .Where(p => PlcNameRules.Comparer.Equals(p.PlcName, plcName)).SelectMany(p => p.Tables)
            .Where(t => string.Equals(NormalizeFolder(t.FolderPath), folder, StringComparison.OrdinalIgnoreCase)).ToList();
        if (failure is not null || !_results.TryGetValue(item.OperationId, out var written))
            return new(item.OperationId, false, TagTableCheck, null, string.Join("; ", tables.Select(t => t.Name)), failure is null
                ? $"Operation '{item.OperationId}': no import result is known."
                : $"Operation '{item.OperationId}' failed with {failure.Category}; the tag tables of '{folder}' are reported.");
        var imported = CanonicalJson.Deserialize<PlcTagTableFileResultInfo>(written.GetRawText());
        if (imported.Tables.Count == 0)
            return new(item.OperationId, true, TagTableCheck, null, string.Join("; ", tables.Select(t => t.Name)),
                $"Operation '{item.OperationId}': the worker could not tell which table the import replaced; the tag tables of '{folder}' are reported. Pass tableName to have it checked.");
        string PathOf(string name) => folder == "/" ? "/" + name : $"{folder}/{name}";
        string Describe(string name) => tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) is { } table
            ? $"{PathOf(table.Name)} tags={table.Tags.Count} userConstants={table.UserConstants.Count}"
            : $"{PathOf(name)} absent";
        var expected = $"{string.Join("; ", imported.Tables.Select(PathOf))} tags={imported.TagCount} userConstants={imported.UserConstantCount}";
        var found = imported.Tables.Select(n => tables.FirstOrDefault(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
        var actual = found.All(t => t is not null)
            ? $"{string.Join("; ", found.Select(t => PathOf(t!.Name)))} tags={found.Sum(t => t!.Tags.Count)} userConstants={found.Sum(t => t!.UserConstants.Count)}"
            : string.Join("; ", imported.Tables.Select(Describe));
        var success = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        return new(item.OperationId, success, TagTableCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read tag tables differ from the import result. Inspect them with plc_read list_tag_tables.");
    }

    /// <summary>replace_tag_comments: read_tag_comments again; every text the worker reports changed must read back as written.</summary>
    private async Task<WriteOperationVerification> TagCommentsReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var plcName = effects[item.OperationId].Target.PlcName;
        var (inventory, problem) = await PlcTagCommentPlanning.ReadAsync(client, item, plcName).ConfigureAwait(false);
        if (inventory is null)
            return failure is null
                ? Fail(item, TagCommentsCheck, $"Operation '{item.OperationId}': the tag comments could not be re-read ({problem}).")
                : new(item.OperationId, false, TagCommentsCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the tag comments could not be re-read.");
        if (failure is not null || !_results.TryGetValue(item.OperationId, out var written))
            return new(item.OperationId, false, TagCommentsCheck, null, null, failure is null
                ? $"Operation '{item.OperationId}': no replacement result is known."
                : $"Operation '{item.OperationId}' failed with {failure.Category}; the transaction was rolled back unless the failure says otherwise. Inspect the comments with plc_read read_tag_comments.");
        var reported = CanonicalJson.Deserialize<PlcTagCommentReplaceResultInfo>(written.GetRawText());
        var current = PlcTagCommentPlanning.Texts(inventory, new PlcOperationRequest { OperationId = item.OperationId, Operation = item.Operation, TableName = item.TableName, FolderPath = item.FolderPath }, plcName)
            .GroupBy(t => (PlcTagCommentPlanning.ObjectPath(t.Table, t.Member.Name).ToUpperInvariant(), t.Text.Culture.ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.First().Text.Text);
        var expected = string.Join("; ", reported.Changes.Select(c => $"{c.Object} [{c.Culture}]={c.New}"));
        var actual = string.Join("; ", reported.Changes.Select(c => $"{c.Object} [{c.Culture}]={current.GetValueOrDefault((c.Object.ToUpperInvariant(), c.Culture.ToUpperInvariant())) ?? "(absent)"}"));
        var success = string.Equals(expected, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, TagCommentsCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read comments do not show every replacement. Inspect them with plc_read read_tag_comments.");
    }

    /// <summary>rename_tags: the PLC's tag inventory is re-read; every new name must be there and every old name that no rename takes over gone.</summary>
    private async Task<WriteOperationVerification> TagRenameReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var renames = item.Renames ?? new List<PlcNameRenameInfo>();
        var read = await client.ListTagTablesAsync(item.PlcName ?? effects[item.OperationId].Target.PlcName).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = "list_tag_tables" }, read);
        if (projected.Result is not { } result)
            return failure is null
                ? Fail(item, BatchRenameCheck, $"Operation '{item.OperationId}': the tag tables could not be re-read ({projected.Failure?.Message}).")
                : new(item.OperationId, false, BatchRenameCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the tag tables could not be re-read.");
        var names = CanonicalJson.Deserialize<PlcTagInventoryInfo>(result.GetRawText()).Plcs
            .Where(p => PlcNameRules.Comparer.Equals(p.PlcName, effects[item.OperationId].Target.PlcName))
            .SelectMany(p => p.Tables).SelectMany(t => t.Tags).Select(t => t.Name).ToList();
        var newNames = new HashSet<string>(renames.Select(r => r.NewName), StringComparer.OrdinalIgnoreCase);
        string Describe(PlcNameRenameInfo r)
            => $"{r.Name}->{(names.Contains(r.NewName, StringComparer.Ordinal) ? r.NewName : "(absent)")}"
               + (!newNames.Contains(r.Name) && names.Contains(r.Name, StringComparer.OrdinalIgnoreCase) ? $" ({r.Name} still present)" : string.Empty);
        var expected = string.Join("; ", renames.Select(r => $"{r.Name}->{r.NewName}"));
        var actual = string.Join("; ", renames.Select(Describe));
        if (failure is not null)
            return new(item.OperationId, false, BatchRenameCheck, expected, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current tag names are reported.");
        var success = string.Equals(expected, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, BatchRenameCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read tag tables do not show every rename. Inspect them with plc_read list_tag_tables.");
    }

    /// <summary>rename_objects: the worker reads every name back inside the transaction; that read-back is reported.</summary>
    private WriteOperationVerification ObjectRenameReadBack(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var renames = item.Renames ?? new List<PlcNameRenameInfo>();
        var expected = string.Join("; ", renames.Select(r => $"{r.Name}->{r.NewName}"));
        if (failure is not null || !_results.TryGetValue(item.OperationId, out var written))
            return new(item.OperationId, false, BatchRenameCheck, expected, null, failure is null
                ? $"Operation '{item.OperationId}': no rename result is known."
                : $"Operation '{item.OperationId}' failed with {failure.Category}; the transaction was rolled back unless the failure says otherwise. Inspect the objects with plc_read find_objects.");
        var reported = CanonicalJson.Deserialize<PlcBatchRenameResultInfo>(written.GetRawText());
        var actual = string.Join("; ", reported.Items.Select(i => $"{i.OldName}->{i.Observed ?? "(unread)"}"));
        var success = string.Equals(expected, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, BatchRenameCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the names read back differ from the request. Inspect the objects with plc_read find_objects.");
    }

    /// <summary>Re-reads the interface: every renamed member must carry its new name and its old path must be gone; the new content hash is reported.</summary>
    private async Task<WriteOperationVerification> RenamedMembersReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var isType = item.TypePath is not null;
        var method = isType ? PlcBlockEditMethods.GetTypeInterface : PlcBlockEditMethods.GetBlockInterface;
        var read = await client.PlcBlockEditAsync(method, new PlcBlockEditRequestInfo { BlockPath = item.BlockPath, TypePath = item.TypePath }).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = method }, read);
        if (projected.Result is not { } result)
            return failure is null
                ? Fail(item, RenamedMembersCheck, $"Operation '{item.OperationId}': the interface could not be re-read ({projected.Failure?.Message}).")
                : new(item.OperationId, false, RenamedMembersCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the interface could not be re-read.");
        var (hash, sections) = isType
            ? (CanonicalJson.Deserialize<PlcTypeInterfaceInfo>(result.GetRawText()) is var type ? (type.ContentHash, type.Sections) : default)
            : (CanonicalJson.Deserialize<PlcBlockInterfaceInfo>(result.GetRawText()) is var block ? (block.ContentHash, block.Sections) : default);
        _contentHashes[item.OperationId] = hash;
        if (!_planned.TryGetValue(item.OperationId, out var planned) || planned.Expectations is null)
            return new(item.OperationId, false, RenamedMembersCheck, null, null,
                $"Operation '{item.OperationId}': no planned member state is known; the current content hash is reported.");
        var checks = planned.Expectations.Select(e => e.Check(sections)).ToList();
        var expected = string.Join("; ", checks.Select(c => c.Expected));
        var actual = string.Join("; ", checks.Select(c => c.Actual));
        if (failure is not null)
            return new(item.OperationId, false, RenamedMembersCheck, expected, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current member state and content hash are reported.");
        var success = checks.All(c => c.Success);
        return new(item.OperationId, success, RenamedMembersCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read interface does not show every rename. Inspect it with plc_read get_block_interface or get_type_interface.");
    }

    /// <summary>
    /// A document edit is re-exported (its content hash is reported, usable as the next expectedContentHash) and every
    /// changed text must read back as planned at the same place and language.
    /// </summary>
    private async Task<WriteOperationVerification> DocumentReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var isType = item.TypePath is not null;
        var export = isType
            ? await client.GetTypeContentAsync(item.TypePath!, format: Contracts.Block.SourceFormatNames.Xml).ConfigureAwait(false)
            : await client.GetBlockContentAsync(item.BlockPath!, format: Contracts.Block.SourceFormatNames.Xml).ConfigureAwait(false);
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
            return failure is null
                ? Fail(item, NetworkTextCheck, $"Operation '{item.OperationId}': the block could not be re-exported ({(export.Success ? "the export was empty" : export.Error)}).")
                : new(item.OperationId, false, NetworkTextCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the block could not be re-exported.");
        var hash = ContentHashes.Compute(Contracts.Block.SourceFormatNames.Xml, export.Payload);
        _contentHashes[item.OperationId] = hash;
        if (!_planned.TryGetValue(item.OperationId, out var planned) || planned.TextChanges is null)
            return new(item.OperationId, false, NetworkTextCheck, null, null,
                $"Operation '{item.OperationId}': no planned texts are known; the current content hash is reported.");
        var xml = isType ? export.Payload : PlcBlockBundleText.TryGetSimaticMl(export.Payload, out _, out var found) ? found : null;
        string Describe(IEnumerable<string> parts) => string.Join("; ", parts);
        var expected = Describe(planned.TextChanges.Select(c => $"{c.Where} [{c.Culture}]={c.New}"));
        List<string>? actualParts = null;
        if (xml is not null)
        {
            try
            {
                var document = SimaticMlInterfaceDocument.Parse(xml).Document;
                actualParts = planned.TextChanges.Select(c => $"{c.Where} [{c.Culture}]={SimaticMlNetworkTexts.Current(document, c) ?? "(absent)"}").ToList();
            }
            catch (PlcInterfaceEditException)
            {
            }
        }

        var actual = actualParts is null
            ? xml is null ? $"(not readable: {Contracts.Plc.Interface.SimaticMlExportNotes.MissingInPlanning})" : "(not readable)"
            : Describe(actualParts);
        if (failure is not null)
            return new(item.OperationId, false, NetworkTextCheck, expected, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the current texts and content hash are reported.");
        var success = string.Equals(expected, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, NetworkTextCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-exported block does not show the planned texts. Inspect it with plc_read get_block_content.");
    }

    /// <summary>A new snapshot reads back as the stored snapshot: get_db_snapshot must show the snapshot time the write reported.</summary>
    private async Task<WriteOperationVerification> DbSnapshotReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetDbSnapshot,
            new PlcBlockEditRequestInfo { BlockPath = item.BlockPath, MemberPath = item.MemberPath, MaxElements = 1 }).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = PlcBlockEditMethods.GetDbSnapshot }, read);
        if (projected.Result is not { } result)
            return failure is null
                ? Fail(item, DbSnapshotCheck, $"Operation '{item.OperationId}': the stored snapshot could not be re-read ({projected.Failure?.Message}).")
                : new(item.OperationId, false, DbSnapshotCheck, null, null, $"Operation '{item.OperationId}' failed with {failure.Category}; the stored snapshot could not be re-read.");
        var stored = CanonicalJson.Deserialize<PlcDbSnapshotInfo>(result.GetRawText());
        var actual = stored.HasSnapshot ? $"snapshotDate={stored.SnapshotDate ?? "(none)"} values={stored.TotalValues}" : "no snapshot stored";
        if (failure is not null)
            return new(item.OperationId, false, DbSnapshotCheck, null, actual, $"Operation '{item.OperationId}' failed with {failure.Category}; the stored snapshot is reported.");
        var taken = _results.TryGetValue(item.OperationId, out var written) ? CanonicalJson.Deserialize<PlcDbSnapshotInfo>(written.GetRawText()) : null;
        var expected = taken is null ? null : $"snapshotDate={taken.SnapshotDate ?? "(none)"} values={taken.TotalValues}";
        var success = taken is { HasSnapshot: true } && stored.HasSnapshot && string.Equals(expected, actual, StringComparison.Ordinal);
        return new(item.OperationId, success, DbSnapshotCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the stored snapshot differs from the one the write returned (or none is stored). Inspect it with plc_read get_db_snapshot.");
    }
}
