// The batch rename sequence follows BetterTIA/writer, src/TiaMcpServer/Siemens/Portal.AnetFork.cs (RenameTags,
// RenameObjects, RenameBatch), commit fc8179c9b59d364961f2255d07d05af893c32837, MIT License, Copyright (c) 2025
// J.Heilingbrunner; see THIRD_PARTY_NOTICES.md. Re-written for this worker: generic Name attribute with writability
// check and read-back, one ExclusiveAccess transaction, TIA Portal's fail-safe rule instead of allowFailsafe.
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// rename_tags and rename_objects. Every object is resolved by name before anything changes (tags across all tag
/// tables of the PLC, whose names are unique per PLC; blocks, PLC data types and technology objects of the PLC, a name
/// found in more than one kind is ambiguous). <see cref="PlcBatchRenamePlan"/> orders the setter calls so swaps,
/// rotations and chains work; each step uses the generic attribute API (<c>GetAttributeInfos()</c> must list a writable
/// <c>Name</c>, <c>SetAttribute("Name")</c>, <c>GetAttribute("Name")</c> must read it back, because TIA Portal silently
/// ignored a colliding tag rename before). All steps run in one <c>ExclusiveAccess</c> transaction: a failed step rolls
/// every rename back, temporary names included. Fail-safe tags (<c>IsSafety</c>) and F-blocks follow TIA Portal's
/// safety login rule (<see cref="Safety.PlcSafetyGate"/>), checked for every object before the transaction.
/// </summary>
internal static class PlcBatchEditService
{
    private sealed class Renamable
    {
        public PlcBatchRenameItemInfo Info { get; } = new();

        public IEngineeringObject Target { get; init; } = null!;
    }

    public static PlcBatchRenameResultInfo RenameTags(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var renames = RequireRenames(request);
        var discovered = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var plc = discovered.Software;
        var tags = TagTables(plc.TagTableGroup, "/").SelectMany(t => t.Table.Tags.Select(tag => (t.Folder, t.Table, Tag: tag))).ToList();
        var items = new List<Renamable>();
        var missing = new List<string>();
        foreach (var rename in renames)
        {
            var found = tags.Where(t => string.Equals(t.Tag.Name, rename.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0) { missing.Add(rename.Name); continue; }
            if (found.Count > 1)
                throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                    $"rename_tags: {found.Count} tags are named '{rename.Name}' ({string.Join(", ", found.Select(f => $"{f.Folder}{f.Table.Name}"))}). Nothing was changed.");
            var (folder, table, tag) = found[0];
            if (SafetyFlag(tag) is true)
                Safety.PlcSafetyGate.RequireSafetyTagChangePermitted(project, plc.Name, $"rename_tags of '{tag.Name}'");
            var item = new Renamable { Target = (IEngineeringObject)tag };
            (item.Info.Kind, item.Info.Path, item.Info.OldName, item.Info.NewName) = ("tag", $"{folder}{table.Name}", tag.Name, rename.NewName);
            items.Add(item);
        }

        if (missing.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"rename_tags: no PLC tag of '{plc.Name}' is named {string.Join(", ", missing.Select(m => $"'{m}'"))}. Nothing was changed.");
        return Run(portal, project, PlcBlockEditMethods.RenameTags, plc.Name, items, request.TemporaryStamp);
    }

    public static PlcBatchRenameResultInfo RenameObjects(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var renames = RequireRenames(request);
        var discovered = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var plc = discovered.Software;
        var blocks = Blocks(plc.BlockGroup, plc.Name + "/Blocks").ToList();
        var types = Types(plc.TypeGroup, plc.Name + "/Types").ToList();
        var technology = TechnologyObjects(plc.TechnologicalObjectGroup, "/").ToList();
        var items = new List<Renamable>();
        var missing = new List<string>();
        foreach (var rename in renames)
        {
            bool Named(string? name) => string.Equals(name, rename.Name, StringComparison.OrdinalIgnoreCase);
            var found = blocks.Where(b => Named(b.Block.Name)).Select(b => ("block", b.Path, (IEngineeringObject)b.Block, (object)b.Block))
                .Concat(types.Where(t => Named(t.Type.Name)).Select(t => ("type", t.Path, (IEngineeringObject)t.Type, (object)t.Type)))
                .Concat(technology.Where(t => Named(t.Object.Name)).Select(t => ("technologyObject", t.Path, (IEngineeringObject)t.Object, (object)t.Object)))
                .ToList();
            if (found.Count == 0) { missing.Add(rename.Name); continue; }
            if (found.Count > 1)
                throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                    $"rename_objects: '{rename.Name}' names {found.Count} objects ({string.Join(", ", found.Select(f => $"{f.Item1} {f.Item2}"))}). Nothing was changed.");
            var (kind, path, target, live) = found[0];
            if (live is PlcBlock block)
                Safety.PlcSafetyGate.RequireChangePermitted(project, plc.Name, block, PlcBlockEditMethods.RenameObjects);
            var item = new Renamable { Target = target };
            (item.Info.Kind, item.Info.Path, item.Info.OldName, item.Info.NewName) = (kind, path, NameOf(target), rename.NewName);
            items.Add(item);
        }

        if (missing.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"rename_objects: PLC '{plc.Name}' has no block, PLC data type or technology object named {string.Join(", ", missing.Select(m => $"'{m}'"))}. Nothing was changed.");
        return Run(portal, project, PlcBlockEditMethods.RenameObjects, plc.Name, items, request.TemporaryStamp);
    }

    /// <summary>
    /// replace_tag_comments (adopted from the writer's EditTagComments): every <c>MultilingualTextItem</c> of the
    /// <c>Comment</c> of every tag in scope (one table, or every tag table of the PLC) whose text the one-pass
    /// replacement changes is set through <c>MultilingualTextItem.Text</c> and read back, all in one ExclusiveAccess
    /// transaction (a failure rolls every text back). Comments are texts: no safety gate applies, TIA Portal decides.
    /// </summary>
    public static PlcTagCommentReplaceResultInfo ReplaceTagComments(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var replacements = request.Replacements ?? new List<PlcTextReplacementInfo>();
        try
        {
            _ = SimaticMlNetworkTexts.Map(replacements);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"replace_tag_comments: {exception.Message} Nothing was changed.");
        }

        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName).Software;
        var folder = "/" + string.Join("/", (request.FolderPath ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        var tables = TagTables(plc.TagTableGroup, "/")
            .Where(t => request.TableName is null || string.Equals(t.Table.Name, request.TableName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Folder.TrimEnd('/'), folder.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (request.TableName is not null && tables.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"replace_tag_comments: tag table '{request.TableName}' was not found in '{folder}' of PLC '{plc.Name}'. Nothing was changed.");
        var result = new PlcTagCommentReplaceResultInfo { PlcName = plc.Name };
        var pending = new List<(MultilingualTextItem Item, PlcTextChangeInfo Change)>();
        foreach (var (tableFolder, table) in tables)
        {
            foreach (PlcTag tag in table.Tags)
            {
                result.TagsScanned++;
                var objectPath = $"{tableFolder}{table.Name}/{tag.Name}";
                foreach (MultilingualTextItem item in tag.Comment.Items)
                {
                    var culture = item.Language?.Culture?.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(request.Culture) && !string.Equals(culture, request.Culture, StringComparison.OrdinalIgnoreCase)) continue;
                    var old = item.Text ?? string.Empty;
                    var replaced = SimaticMlNetworkTexts.ReplaceAll(old, replacements);
                    if (replaced == old) continue;
                    pending.Add((item, new PlcTextChangeInfo { Object = objectPath, Where = "comment", Key = objectPath, Culture = culture, Old = old, New = replaced }));
                }
            }
        }

        if (pending.Count == 0)
        {
            result.Messages.Add("No comment text in scope contains a find; nothing was changed.");
            return result;
        }

        var outcome = PlcRoundTrip.Run(portal, project, $"replace_tag_comments ({pending.Count} texts)", () =>
        {
            foreach (var (item, change) in pending)
            {
                item.Text = change.New;
                var observed = item.Text ?? string.Empty;
                if (!string.Equals(observed, change.New, StringComparison.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"replace_tag_comments: the comment [{change.Culture}] of '{change.Object}' reads back as '{observed}', not '{change.New}'.");
                result.Changes.Add(change);
            }

            return result;
        });
        if (outcome.State == PlcRoundTrip.TransactionState.Committed) return outcome.Result!;
        result.Changes.Clear();
        throw PlcRoundTrip.RolledBackFailure(outcome, () => $"{pending.Count} comment text(s) were to change; read them with plc_read read_tag_comments.");
    }

    private static PlcBatchRenameResultInfo Run(TiaPortal portal, ProjectBase project, string operation, string plcName, List<Renamable> items, string? stamp)
    {
        List<(int Index, string Name)> steps;
        try
        {
            steps = PlcBatchRenamePlan.Plan(items.Select(i => i.Info.OldName).ToList(), items.Select(i => i.Info.NewName).ToList(),
                string.IsNullOrWhiteSpace(stamp) ? Guid.NewGuid().ToString("N").Substring(0, 8) : stamp!);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{operation}: {exception.Message} Nothing was changed.");
        }

        var result = new PlcBatchRenameResultInfo { Operation = operation, PlcName = plcName, Items = items.Select(i => i.Info).ToList() };
        if (steps.Count == 0)
        {
            foreach (var item in items) item.Info.Observed = NameOf(item.Target);
            result.Messages.Add("Every object already carries the requested name; nothing was changed.");
            return result;
        }

        var outcome = PlcRoundTrip.Run(portal, project, $"{operation} ({items.Count} objects)", () =>
        {
            foreach (var (index, name) in steps)
            {
                var item = items[index];
                LibraryReflection.Rename(item.Target, name, $"{item.Info.Kind} '{item.Info.OldName}' ({item.Info.Path})");
                result.Steps.Add($"{item.Info.Kind} '{item.Info.OldName}' -> '{name}'");
            }

            foreach (var item in items)
            {
                item.Info.Observed = NameOf(item.Target);
                if (!string.Equals(item.Info.Observed, item.Info.NewName, StringComparison.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"{operation}: {item.Info.Kind} '{item.Info.OldName}' reads back as '{item.Info.Observed}', not '{item.Info.NewName}'.");
            }

            return result;
        });
        if (outcome.State == PlcRoundTrip.TransactionState.Committed) return outcome.Result!;
        throw PlcRoundTrip.RolledBackFailure(outcome, () => string.Join(", ", items.Select(i =>
        {
            string name;
            try
            {
                name = NameOf(i.Target);
            }
            catch (Exception exception) when (exception is not NonRecoverableException)
            {
                name = $"(unreadable: {exception.Message})";
            }

            return $"{i.Info.Kind} '{i.Info.OldName}' is named '{name}'";
        })) + ".");
    }

    private static List<PlcNameRenameInfo> RequireRenames(PlcBlockEditRequestInfo request)
    {
        var renames = request.Renames ?? new List<PlcNameRenameInfo>();
        if (renames.Count is 0 or > PlcBatchRenamePlan.MaxRenames)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"renames must hold 1 to {PlcBatchRenamePlan.MaxRenames} {{ name, newName }} entries.");
        if (renames.Any(r => string.IsNullOrWhiteSpace(r?.Name) || string.IsNullOrWhiteSpace(r?.NewName)))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Every rename needs a nonblank name and newName.");
        return renames;
    }

    private static string NameOf(IEngineeringObject target) => target.GetAttribute("Name")?.ToString() ?? string.Empty;

    private static bool? SafetyFlag(PlcTag tag)
    {
        try
        {
            return ((IEngineeringObject)tag).GetAttribute("IsSafety") as bool?;
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    private static IEnumerable<(string Folder, PlcTagTable Table)> TagTables(PlcTagTableGroup group, string folder)
    {
        foreach (PlcTagTable table in group.TagTables) yield return (folder, table);
        foreach (PlcTagTableUserGroup child in group.Groups)
            foreach (var nested in TagTables(child, $"{folder}{child.Name}/")) yield return nested;
    }

    private static IEnumerable<(string Path, PlcBlock Block)> Blocks(PlcBlockGroup group, string path)
    {
        foreach (PlcBlock block in group.Blocks) yield return ($"{path}/{block.Name}", block);
        foreach (PlcBlockUserGroup child in group.Groups)
            foreach (var nested in Blocks(child, $"{path}/{child.Name}")) yield return nested;
    }

    private static IEnumerable<(string Path, PlcType Type)> Types(PlcTypeGroup group, string path)
    {
        foreach (PlcType type in group.Types) yield return ($"{path}/{type.Name}", type);
        foreach (PlcTypeUserGroup child in group.Groups)
            foreach (var nested in Types(child, $"{path}/{child.Name}")) yield return nested;
    }

    private static IEnumerable<(string Path, TechnologicalInstanceDB Object)> TechnologyObjects(TechnologicalInstanceDBGroup group, string path)
    {
        foreach (TechnologicalInstanceDB technology in group.TechnologicalObjects) yield return ($"{path}{technology.Name}", technology);
        foreach (TechnologicalInstanceDBUserGroup child in group.Groups)
            foreach (var nested in TechnologyObjects(child, $"{path}{child.Name}/")) yield return nested;
    }
}
