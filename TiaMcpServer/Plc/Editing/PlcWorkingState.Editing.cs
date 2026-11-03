using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// The operations adopted from BetterTIA in the planner's working state. A DB snapshot targets its block (the worker
/// resolves it, like update_block_logic) and touches nothing another item depends on.
/// </summary>
internal sealed partial class PlcWorkingState
{
    public const string BatchKind = "PlcBatch";

    private static bool IsEditingOperation(string operation)
        => operation == PlcBlockEditMethods.CreateDbSnapshot || PlcBlockEditMethods.BatchWrites.Contains(operation, StringComparer.Ordinal)
            || PlcBlockEditMethods.CallerFileWrites.Contains(operation, StringComparer.Ordinal);

    private PlcWriteEffect ResolveEditing(PlcOperationRequest item, List<FiredGuard> guards)
    {
        switch (item.Operation)
        {
            case PlcBlockEditMethods.RenameTags:
            {
                // Tag names are unique per PLC across its tag tables: every tag is resolved by name before anything changes.
                var changes = new List<PlcFieldChange>();
                foreach (var rename in item.Renames!)
                {
                    var found = _tables.SelectMany(t => t.Tags.Where(tag => Names.Equals(tag.Name, rename.Name)).Select(tag => (Table: t, Tag: tag))).ToList();
                    if (found.Count == 0)
                        throw NotFound(item, $"no tag named '{rename.Name}' was found in the tag tables of PLC '{_plc}'.", !_inventoryComplete);
                    if (found.Count > 1)
                        throw new ResolutionException(Contracts.Worker.WorkerFailureCategories.TargetAmbiguous,
                            $"Operation '{item.OperationId}': {found.Count} tags are named '{rename.Name}' ({string.Join(", ", found.Select(f => TablePath(f.Table)))}).");
                    changes.Add(new PlcFieldChange($"tag {TablePath(found[0].Table)}/{found[0].Tag.Name}", found[0].Tag.Name, rename.NewName));
                }

                guards.Add(Fire(BatchRenameRuns, item, $"rename_tags renames {changes.Count} tag(s) of PLC '{_plc}' in one transaction; blocks access tags by name and re-bind them at the next compile, HMI tags bound to a renamed tag keep the old PLC tag name."));
                return Effect(item, BatchTarget(item, _plc, _device), changes, null);
            }
            case PlcBlockEditMethods.ReplaceTagComments:
                // The planner previews the changed texts from read_tag_comments; a named table must exist.
                if (item.TableName is not null) _ = RequireTable(item);
                return Effect(item, BatchTarget(item, _plc, _device), Array.Empty<PlcFieldChange>(), null);
            case PlcBlockEditMethods.ExportTagTable:
            {
                var table = RequireTable(item);
                var (refusal, overwrite) = PlcTechnologyObjectPlanning.ExportFileCheck(item, TablePath(table), File.Exists);
                if (refusal is not null) throw new ResolutionException(Contracts.Worker.WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {refusal}");
                if (overwrite is not null) guards.Add(Fire(ExportOverwritesFile, item, overwrite));
                return Effect(item, TableTarget(table), Change("file", null, Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.NormalizeLocalFilePath(item.FilePath!)), null);
            }
            case PlcBlockEditMethods.ExportOpcUaXml:
            {
                // The worker resolves the CPU device item that hosts the software; the planner checks the file only.
                var (refusal, overwrite) = PlcTechnologyObjectPlanning.ExportFileCheck(item, $"the OPC UA data of PLC '{_plc}'", File.Exists);
                if (refusal is not null) throw new ResolutionException(Contracts.Worker.WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {refusal}");
                if (overwrite is not null) guards.Add(Fire(ExportOverwritesFile, item, overwrite));
                return Effect(item, BatchTarget(item, _plc, _device), Change("file", null, Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.NormalizeLocalFilePath(item.FilePath!)), null);
            }
            case PlcBlockEditMethods.ImportTagTable:
            {
                var folder = NormalizeFolder(item.FolderPath);
                var file = Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.NormalizeLocalFilePath(item.FilePath!);
                if (item.TableName is null)
                {
                    if (item.Overwrite == true)
                        guards.Add(Fire(ImportReplacesTagTable, item, $"import_tag_table imports '{file}' into '{folder}' of PLC '{_plc}' with ImportOptions.Override; a table of the same name as the file's is replaced (pass tableName to have it named and checked)."));
                    return Effect(item, BatchTarget(item, _plc, _device), Change("import", null, file), folder);
                }

                var existing = _tables.FirstOrDefault(t => Names.Equals(t.Folder, folder) && Names.Equals(t.Name, item.TableName));
                if (existing is not null && item.Overwrite != true)
                    throw new ResolutionException(Contracts.Worker.WorkerFailureCategories.ValidationError,
                        $"Operation '{item.OperationId}': tag table '{TablePath(existing)}' already exists; set overwrite:true to replace it (ImportOptions.Override).");
                if (existing is not null)
                    guards.Add(Fire(ImportReplacesTagTable, item, $"import_tag_table replaces '{TablePath(existing)}' ({existing.Tags.Count} tags, {existing.Constants.Count} user constants) with the content of '{file}'."));
                return Effect(item, new(PlcNameRules.TagTable, _plc, _device, folder, existing?.Name ?? item.TableName, null, null, null),
                    Change("import", existing is null ? null : TablePath(existing), file), folder);
            }
            case PlcBlockEditMethods.RenameObjects:
            {
                // Blocks, PLC data types and technology objects are resolved by the worker (types and TOs are not in the tree).
                var changes = item.Renames!.Select(r => new PlcFieldChange($"name {r.Name}", r.Name, r.NewName)).ToArray();
                guards.Add(Fire(BatchRenameRuns, item, $"rename_objects renames {changes.Length} block(s), PLC data type(s) or technology object(s) of PLC '{_plc}' in one transaction; callers, instance DBs and users of a renamed type re-bind by name at the next compile, HMI tags keep their PLC paths."));
                return Effect(item, BatchTarget(item, _plc, _device), changes, null);
            }
            default:
            {
                var address = BlockAddress.Parse(item.BlockPath!);
                var path = address.IsDeterministic ? PlannedPath(address) : $"{_plc}/{address.BlockName}";
                guards.Add(Fire(StoresDbSnapshot, item,
                    $"create_db_snapshot reads the actual values of '{path}' from the online PLC '{_plc}' and stores them as its snapshot in the offline project."));
                return Effect(item, new(PlcNameRules.Block, _plc, _device, null, null, address.BlockName, path, null),
                    Change("snapshot", null, "actual values of the online PLC"), null);
            }
        }
    }

    private void ApplyEditing(PlcOperationRequest item)
    {
        // A snapshot changes neither the block's identity nor its exported content.
        switch (item.Operation)
        {
            case PlcBlockEditMethods.RenameTags:
            {
                // Resolved against the names before the batch, so a swap renames each tag once.
                var resolved = item.Renames!.Select(r => (Rename: r, Table: _tables.FirstOrDefault(t => t.Tags.Any(tag => Names.Equals(tag.Name, r.Name))))).ToList();
                foreach (var (rename, table) in resolved)
                {
                    Touch(CpuKey(rename.Name), item);
                    Touch(CpuKey(rename.NewName), item);
                    if (table is null) continue;
                    var index = table.Tags.FindIndex(tag => Names.Equals(tag.Name, rename.Name) && !RenamedIn(item, tag));
                    if (index < 0) continue;
                    var renamed = System.Text.Json.JsonSerializer.Deserialize<TagInfo>(System.Text.Json.JsonSerializer.Serialize(table.Tags[index]))!;
                    renamed.Name = rename.NewName;
                    table.Tags[index] = renamed;
                    _renamedTags.Add(renamed);
                }

                _renamedTags.Clear();
                break;
            }
            case PlcBlockEditMethods.ImportTagTable when item.TableName is not null:
            {
                // Later items may use the imported table; its tags are known only after the import (re-planned then).
                var folder = NormalizeFolder(item.FolderPath);
                if (!_tables.Any(t => Names.Equals(t.Folder, folder) && Names.Equals(t.Name, item.TableName)))
                    _tables.Add(new Table(folder, item.TableName, new(), new()));
                Touch(TableKey(item.TableName), item);
                break;
            }
            case PlcBlockEditMethods.RenameObjects:
                foreach (var rename in item.Renames!)
                {
                    Touch(CpuKey(rename.Name), item);
                    Touch(CpuKey(rename.NewName), item);
                    Touch(TypeKey(rename.Name), item);
                    Touch(TypeKey(rename.NewName), item);
                }

                break;
        }
    }

    /// <summary>rename_objects: whether the tree shows an F-block (ProgrammingLanguage F_...) among the named objects.</summary>
    private bool RenamesFailSafeBlock(PlcOperationRequest item)
        => item.Renames is { } renames && _unitRoots.Values.Prepend(_root).SelectMany(DescendantBlocks)
            .Any(b => renames.Any(r => Names.Equals(r.Name, b.Block.Name)) && Contracts.Plc.Safety.PlcSafetyPolicy.IsFailSafeLanguage(b.Block.Language));

    private readonly HashSet<TagInfo> _renamedTags = new(ReferenceEqualityComparer.Instance);

    private bool RenamedIn(PlcOperationRequest item, TagInfo tag) => _renamedTags.Contains(tag);

    private static PlcTargetIdentity BatchTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => new(BatchKind, plcName, deviceName, null, null, item.Operation, null, null);

    /// <summary>The requested identity of a batch item whose PLC could not be verified.</summary>
    private static PlcTargetIdentity? UnresolvedEditingTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => PlcBlockEditMethods.BatchWrites.Contains(item.Operation, StringComparer.Ordinal)
            || item.Operation == PlcBlockEditMethods.ImportTagTable && item.TableName is null
            ? BatchTarget(item, plcName, deviceName) : null;

    /// <summary>A batch item depends on the latest earlier item that touched any of its old names.</summary>
    private static IEnumerable<string> EditingKeys(PlcTargetIdentity target, PlcOperationRequest? item)
        => target.Kind == BatchKind && item?.Renames is { } renames
            ? renames.SelectMany(r => new[] { CpuKey(r.Name), TypeKey(r.Name) })
            : Array.Empty<string>();
}
