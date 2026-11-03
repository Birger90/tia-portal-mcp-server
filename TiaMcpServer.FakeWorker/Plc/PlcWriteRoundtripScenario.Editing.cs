using System.Security;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// The operations adopted from BetterTIA in the <c>plc-write-roundtrip</c> fixture. DB snapshots: the PLC counts as
/// online; create_db_snapshot builds the snapshot export from the DB's current values (the fixture's start values) in
/// the layout BetterTIA/watch reports for V21 and stores it; get_db_snapshot exports the stored one (none at first).
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private readonly Dictionary<string, string> _snapshots = new(Names);
    private int _snapshotCount;

    private PlcDbSnapshotInfo DbSnapshot(string method, PlcBlockEditRequestInfo edit)
    {
        var (plc, _, _, path) = ResolveBlock(edit.BlockPath!);
        var identity = Identity(plc, path);
        if (identity.BlockType is not ("GlobalDB" or "InstanceDB"))
            Throw(WorkerFailureCategories.TargetKindUnsupported, $"'{path}' is a {identity.BlockType}; DB values are read and written on global and instance DBs.");
        var create = method == PlcBlockEditMethods.CreateDbSnapshot;
        if (create)
        {
            var values = DbValues(new PlcBlockEditRequestInfo { BlockPath = edit.BlockPath });
            var lines = values.Values.Where(v => v.Kind == PlcDbValueKinds.Value)
                .Select(v => $"        <Value Path=\"{SecurityElement.Escape(v.Path)}\" Type=\"{SecurityElement.Escape(v.DataType)}\">{SecurityElement.Escape(v.StartValue ?? string.Empty)}</Value>");
            _snapshotCount++;
            _snapshots[path] = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Document>\n  <SW.Interface.Snapshot ID=\"0\">\n    <AttributeList>\n      <Snapshot>\n"
                + string.Join("\n", lines) + $"\n      </Snapshot>\n      <SnapshotDate>2026-11-01T12:00:{_snapshotCount:00}Z</SnapshotDate>\n    </AttributeList>\n  </SW.Interface.Snapshot>\n</Document>\n";
        }

        var xml = _snapshots.TryGetValue(path, out var stored) ? stored
            : "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Document>\n  <SW.Interface.Snapshot ID=\"0\">\n    <AttributeList />\n  </SW.Interface.Snapshot>\n</Document>\n";
        var reading = PlcDbSnapshotDocument.Read(xml, edit.MemberPath, edit.MaxElements ?? PlcDbSnapshotDocument.DefaultMaxValues);
        var info = new PlcDbSnapshotInfo
        {
            Operation = method, Block = identity, OnlineState = create ? "Online" : null, HasSnapshot = reading.HasSnapshot,
            SnapshotDate = reading.SnapshotDate, MemberPath = edit.MemberPath, TotalValues = reading.TotalValues, MatchedValues = reading.MatchedValues,
            Truncated = reading.Truncated, Values = reading.Values, TopLevelMembers = reading.TopLevelMembers,
        };
        if (!reading.HasSnapshot) info.Messages.Add("No snapshot is stored for this DB.");
        return info;
    }

    /// <summary>
    /// rename_tags / rename_objects: names resolved before anything changes (a missing one fails with nothing changed),
    /// the shared <see cref="PlcBatchRenamePlan"/> applied all-or-nothing (a new name starting with RejectedByPortal fails
    /// the transaction, which then rolls back), names read back.
    /// </summary>
    private PlcBatchRenameResultInfo BatchRename(string method, PlcBlockEditRequestInfo edit)
    {
        var plc = FindPlc(edit.PlcName);
        var renames = edit.Renames ?? new List<PlcNameRenameInfo>();
        var items = new List<(PlcBatchRenameItemInfo Info, Func<string> Get, Action<string> Set)>();
        foreach (var rename in renames)
        {
            if (method == PlcBlockEditMethods.RenameTags)
            {
                var found = plc.Tables.SelectMany(t => t.Tags.Select(tag => (Table: t, Tag: tag))).Where(x => Names.Equals(x.Tag.Name, rename.Name)).ToList();
                if (found.Count != 1) Throw(WorkerFailureCategories.TargetNotFound, $"rename_tags: no PLC tag of '{plc.Software}' is named '{rename.Name}'. Nothing was changed.");
                var tag = found[0].Tag;
                items.Add((new PlcBatchRenameItemInfo { Kind = "tag", Path = found[0].Table.FolderPath.TrimEnd('/') + "/" + found[0].Table.Name, OldName = tag.Name, NewName = rename.NewName },
                    () => tag.Name, name => tag.Name = name));
                continue;
            }

            var block = AllGroups(plc.Root).SelectMany(g => g.Blocks.Select((b, i) => (Group: g, Index: i, Block: b))).Where(x => Names.Equals(x.Block.Name, rename.Name)).ToList();
            var type = plc.Types.FindIndex(t => Names.Equals(t, rename.Name));
            if (block.Count + (type >= 0 ? 1 : 0) != 1)
                Throw(block.Count + (type >= 0 ? 1 : 0) == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                    $"rename_objects: '{rename.Name}' does not name exactly one block, PLC data type or technology object. Nothing was changed.");
            if (block.Count == 1)
            {
                var (group, index, _) = block[0];
                items.Add((new PlcBatchRenameItemInfo { Kind = "block", Path = group.Name, OldName = group.Blocks[index].Name, NewName = rename.NewName },
                    () => group.Blocks[index].Name, name => group.Blocks[index] = group.Blocks[index] with { Name = name }));
            }
            else
            {
                items.Add((new PlcBatchRenameItemInfo { Kind = "type", Path = $"{plc.Software}/Types", OldName = plc.Types[type], NewName = rename.NewName },
                    () => plc.Types[type], name => plc.Types[type] = name));
            }
        }

        var steps = PlcBatchRenamePlan.Plan(items.Select(i => i.Info.OldName).ToList(), items.Select(i => i.Info.NewName).ToList(), edit.TemporaryStamp ?? "fake");
        var originals = items.Select(i => i.Get()).ToList();
        var result = new PlcBatchRenameResultInfo { Operation = method, PlcName = plc.Software, Items = items.Select(i => i.Info).ToList() };
        try
        {
            foreach (var (index, name) in steps)
            {
                if (name.StartsWith(RejectedTagName, StringComparison.Ordinal))
                    Throw(WorkerFailureCategories.EngineeringRejected, $"The name '{name}' is not valid.");
                items[index].Set(name);
                result.Steps.Add($"{items[index].Info.Kind} '{items[index].Info.OldName}' -> '{name}'");
            }
        }
        catch (WorkerOperationException)
        {
            for (var i = 0; i < items.Count; i++) items[i].Set(originals[i]);
            throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected,
                $"{method}: a rename was refused; TIA Portal rolled the transaction back; read back afterwards: {string.Join(", ", items.Select(i => $"'{i.Info.OldName}' is named '{i.Get()}'"))}.");
        }

        foreach (var item in items) item.Info.Observed = item.Get();
        return result;
    }

    private static IEnumerable<Group> AllGroups(Group root) => new[] { root }.Concat(root.Groups.SelectMany(AllGroups));

    /// <summary>Tag comments by tag name: Start (en-US, de-DE) and Motor1 (en-US); a text containing RejectedByPortal is refused.</summary>
    private readonly Dictionary<string, List<PlcCommentTextInfo>> _tagComments = new(Names)
    {
        ["Start"] = new() { new() { Culture = "en-US", Text = "Start pump 1" }, new() { Culture = "de-DE", Text = "Pumpe 1 starten" } },
        ["Motor1"] = new() { new() { Culture = "en-US", Text = "Pump 10 motor" } },
    };

    /// <summary>The project's TIA Portal languages: en-US and de-DE are active, fr-FR is known but inactive (TIA Portal returns no comment item for it).</summary>
    private static readonly string[] CatalogueCultures = { "en-US", "de-DE", "fr-FR" };
    private static readonly string[] ActiveCultures = { "en-US", "de-DE" };

    /// <summary>create_tag/update_tag: the culture a comment is written in (the worker's shared rule), or null without a comment.</summary>
    private static string? CommentCulture(WorkerRequest request)
    {
        if (request.Comment is null && request.CommentCulture is null) return null;
        if (request.Comment is null || string.IsNullOrWhiteSpace(request.CommentCulture))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "comment and commentCulture are given together. Nothing was changed.");
        // The worker writes the inactive-culture warning to stderr; the fake models only the outcome (no item for fr-FR).
        var (culture, _, refusal) = PlcTagCommentRules.Resolve(request.CommentCulture!, CatalogueCultures, ActiveCultures);
        if (refusal is not null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, refusal);
        return culture;
    }

    /// <summary>Items.Find(language) has an item only for the active cultures; a text containing RejectedByPortal is refused like a Siemens setter.</summary>
    private (string Culture, string Text) WriteTagComment(Plc plc, string tagName, string culture, string text)
    {
        if (!ActiveCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, PlcTagCommentRules.NoItem(tagName, culture, ActiveCultures));
        if (text.Contains(RejectedTagName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the comment text (scripted).",
                engineeringError: new EngineeringErrorInfo { Type = "EngineeringTargetInvocationException", Message = "scripted comment rejection" });
        if (plc != _plcs[0]) return (culture, text);
        if (!_tagComments.TryGetValue(tagName, out var texts)) _tagComments[tagName] = texts = new();
        texts.RemoveAll(t => Names.Equals(t.Culture, culture));
        if (text.Length > 0) texts.Add(new PlcCommentTextInfo { Culture = culture, Text = text });
        return (culture, text);
    }

    private PlcTagCommentInventoryInfo TagComments(PlcSourceFolderRequestInfo request)
        => new()
        {
            Plcs = _plcs.Where(p => request.PlcName is null || Matches(p, request.PlcName)).Select(p => new PlcTagCommentPlcInfo
            {
                PlcName = p.Software, DeviceName = p.Device,
                Tables = p.Tables.Where(t => request.TableName is null || Names.Equals(t.Name, request.TableName)).Select(t => new PlcTagTableCommentsInfo
                {
                    FolderPath = t.FolderPath, TableName = t.Name,
                    Members = t.Tags.Select(tag => new PlcMemberCommentInfo
                    {
                        Kind = "Tag", Name = tag.Name,
                        Comment = (p == _plcs[0] && _tagComments.TryGetValue(tag.Name, out var texts) ? texts : new()).Select(x => new PlcCommentTextInfo { Culture = x.Culture, Text = x.Text }).ToList(),
                    }).ToList(),
                }).ToList(),
            }).ToList(),
        };

    private PlcTagCommentReplaceResultInfo ReplaceTagComments(PlcBlockEditRequestInfo edit)
    {
        var plc = FindPlc(edit.PlcName);
        var result = new PlcTagCommentReplaceResultInfo { PlcName = plc.Software };
        var pending = new List<(PlcCommentTextInfo Text, PlcTextChangeInfo Change)>();
        foreach (var table in plc.Tables.Where(t => edit.TableName is null || Names.Equals(t.Name, edit.TableName)))
        foreach (var tag in table.Tags)
        {
            result.TagsScanned++;
            if (plc != _plcs[0] || !_tagComments.TryGetValue(tag.Name, out var texts)) continue;
            foreach (var text in texts.Where(x => string.IsNullOrEmpty(edit.Culture) || Names.Equals(x.Culture, edit.Culture)))
            {
                var replaced = SimaticMlNetworkTexts.ReplaceAll(text.Text, edit.Replacements!);
                if (replaced == text.Text) continue;
                var path = $"{table.FolderPath.TrimEnd('/')}/{table.Name}/{tag.Name}";
                pending.Add((text, new PlcTextChangeInfo { Object = path, Where = "comment", Key = path, Culture = text.Culture, Old = text.Text, New = replaced }));
            }
        }

        if (pending.Any(p => p.Change.New.Contains(RejectedTagName, StringComparison.Ordinal)))
            Throw(WorkerFailureCategories.EngineeringRejected, "replace_tag_comments: a text was refused; TIA Portal rolled the transaction back.");
        foreach (var (text, change) in pending)
        {
            text.Text = change.New;
            result.Changes.Add(change);
        }

        if (pending.Count == 0) result.Messages.Add("No comment text in scope contains a find; nothing was changed.");
        return result;
    }

    /// <summary>
    /// Tag table files of the fixture: a dictionary stands for the file system (paths compared case-insensitively). It
    /// starts with C:\Imports\Valves.xml (table Valves, two tags) and C:\Imports\Motors.xml (table Motors, three tags);
    /// an export stores the table there. A file whose name contains "Rejected" is refused by "TIA Portal".
    /// </summary>
    private readonly Dictionary<string, TagTableInfo> _tagTableFiles = new(Names)
    {
        [@"C:\Imports\Valves.xml"] = new TagTableInfo
        {
            Name = "Valves", Tags = { new TagInfo { Name = "Valve1", DataType = "Bool", LogicalAddress = "%Q1.0" }, new TagInfo { Name = "Valve2", DataType = "Bool", LogicalAddress = "%Q1.1" } },
        },
        [@"C:\Imports\Motors.xml"] = new TagTableInfo
        {
            Name = "Motors",
            Tags =
            {
                new TagInfo { Name = "Motor1", DataType = "Bool", LogicalAddress = "%Q0.0" }, new TagInfo { Name = "Motor2", DataType = "Bool", LogicalAddress = "%Q0.1" },
                new TagInfo { Name = "Motor3", DataType = "Bool", LogicalAddress = "%Q0.2" },
            },
        },
    };

    private static TagTableInfo CopyTable(TagTableInfo table)
        => System.Text.Json.JsonSerializer.Deserialize<TagTableInfo>(System.Text.Json.JsonSerializer.Serialize(table))!;

    private static (long Bytes, string Sha256) FileEvidence(TagTableInfo table)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(table));
        return (bytes.LongLength, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
    }

    /// <summary>OPC UA export files of the fixture (paths compared case-insensitively); a path containing "NoProvider" stands for an installation without OpcUaExportProvider.</summary>
    private readonly HashSet<string> _opcUaFiles = new(Names);

    private PlcOpcUaExportResultInfo ExportOpcUaXml(PlcBlockEditRequestInfo edit)
    {
        var plc = FindPlc(edit.PlcName);
        var path = edit.FilePath!.Replace('/', '\\');
        var existed = _opcUaFiles.Contains(path);
        if (existed && edit.Overwrite != true) Throw(WorkerFailureCategories.ValidationError, $"'{path}' already exists; set overwrite:true to replace it. Nothing was written.");
        if (path.Contains("NoProvider", StringComparison.OrdinalIgnoreCase))
            Throw(WorkerFailureCategories.ApiMemberMissing, $"Expected exactly one {PlcOpcUaExport.ProviderTypeName} in Project.HwUtilities, found 0. Nothing was written.");
        _opcUaFiles.Add(path);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"<UANodeSet><!-- {plc.Software} --></UANodeSet>");
        return new PlcOpcUaExportResultInfo
        {
            PlcName = plc.Software, DeviceName = plc.Device, DeviceItemName = "CPU", FilePath = path, FileBytes = bytes.LongLength,
            FileSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), Overwrite = existed,
        };
    }

    private PlcTagTableFileResultInfo ExportTagTable(PlcBlockEditRequestInfo edit)
    {
        var plc = FindPlc(edit.PlcName);
        var folder = Folder(edit.FolderPath);
        var table = plc.Tables.FirstOrDefault(t => Names.Equals(t.FolderPath, folder) && Names.Equals(t.Name, edit.TableName))
            ?? Throw<TagTableInfo>(WorkerFailureCategories.TargetNotFound, $"Tag table '{edit.TableName}' was not found in '{folder}'. Nothing was written.");
        var path = edit.FilePath!.Replace('/', '\\');
        var existed = _tagTableFiles.ContainsKey(path);
        if (existed && edit.Overwrite != true) Throw(WorkerFailureCategories.ValidationError, $"'{path}' already exists; set overwrite:true to replace it. Nothing was written.");
        var copy = CopyTable(table);
        _tagTableFiles[path] = copy;
        var (bytes, sha) = FileEvidence(copy);
        return new PlcTagTableFileResultInfo
        {
            Operation = PlcBlockEditMethods.ExportTagTable, PlcName = plc.Software, FolderPath = folder, Tables = { table.Name }, FilePath = path,
            FileBytes = bytes, FileSha256 = sha, Overwrite = existed, TagCount = table.Tags.Count, UserConstantCount = table.UserConstants.Count,
        };
    }

    private PlcTagTableFileResultInfo ImportTagTable(PlcBlockEditRequestInfo edit)
    {
        var plc = FindPlc(edit.PlcName);
        var folder = Folder(edit.FolderPath);
        var path = edit.FilePath!.Replace('/', '\\');
        if (!_tagTableFiles.TryGetValue(path, out var file)) Throw(WorkerFailureCategories.TargetNotFound, $"'{path}' does not exist. Nothing was imported.");
        var overwrite = edit.Overwrite == true;
        var existing = plc.Tables.FirstOrDefault(t => Names.Equals(t.FolderPath, folder) && Names.Equals(t.Name, file!.Name));
        if (edit.TableName is { } expected && plc.Tables.Any(t => Names.Equals(t.FolderPath, folder) && Names.Equals(t.Name, expected)) && !overwrite)
            Throw(WorkerFailureCategories.ValidationError, $"Tag table '{expected}' already exists in '{folder}'; set overwrite:true to replace it (ImportOptions.Override). Nothing was imported.");
        if (path.Contains("Rejected", StringComparison.OrdinalIgnoreCase))
            Throw(WorkerFailureCategories.EngineeringRejected, "TIA Portal rejected the operation: the import file is not valid SimaticML.");
        if (existing is not null && !overwrite)
            Throw(WorkerFailureCategories.EngineeringRejected, $"TIA Portal rejected the operation: the composition already contains '{existing.Name}'.");
        var before = plc.Tables.Where(t => Names.Equals(t.FolderPath, folder)).Select(t => t.Name).ToList();
        var imported = CopyTable(file!);
        imported.FolderPath = folder;
        imported.IsDefault = existing?.IsDefault ?? false;
        if (existing is not null) plc.Tables[plc.Tables.IndexOf(existing)] = imported;
        else plc.Tables.Add(imported);
        var (tables, failure, warning) = PlcTagTableImportCheck.Imported(null, before, plc.Tables.Where(t => Names.Equals(t.FolderPath, folder)).Select(t => t.Name).ToList(),
            edit.TableName, overwrite, folder);
        if (failure is not null)
        {
            // Rolled back like the worker's transaction.
            if (existing is not null) plc.Tables[plc.Tables.IndexOf(imported)] = existing;
            else plc.Tables.Remove(imported);
            Throw(WorkerFailureCategories.PostconditionFailed, failure + " The transaction was rolled back.");
        }

        var (bytes, sha) = FileEvidence(file!);
        var found = plc.Tables.Where(t => Names.Equals(t.FolderPath, folder) && tables.Contains(t.Name, Names)).ToList();
        var result = new PlcTagTableFileResultInfo
        {
            Operation = PlcBlockEditMethods.ImportTagTable, PlcName = plc.Software, FolderPath = folder, Tables = found.Select(t => t.Name).ToList(), FilePath = path,
            FileBytes = bytes, FileSha256 = sha, Overwrite = overwrite, TagCount = found.Sum(t => t.Tags.Count), UserConstantCount = found.Sum(t => t.UserConstants.Count),
        };
        if (warning is not null) result.Messages.Add(warning);
        return result;
    }
}
