using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.PlcOnline;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using static TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods;

namespace TiaMcpServer.Plc;

/// <summary>Typed result of the three CSV exports; every member is always written.</summary>
public sealed class PlcCsvExportResultInfo
{
    public string FilePath { get; set; } = string.Empty;

    /// <summary>True when an existing file was replaced (only with overwrite:true).</summary>
    public bool Overwritten { get; set; }

    public string Delimiter { get; set; } = ",";

    public bool RawCells { get; set; }

    /// <summary>Cells prefixed with a single quote because they began with =, +, -, @, a tab or a carriage return.</summary>
    public int NeutralizedCells { get; set; }

    public int PlcCount { get; set; }

    public int TableCount { get; set; }

    /// <summary>Tables that contributed no row (they have no tags, constants or entries).</summary>
    public int EmptyTableCount { get; set; }

    /// <summary>Data rows below the header row.</summary>
    public int RowCount { get; set; }

    public long Bytes { get; set; }

    /// <summary>SHA-256 (lower-case hex) of the file as read back after the write.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>False when a read reported incomplete evidence (named in <see cref="Messages"/>); the file holds what was read.</summary>
    public bool Complete { get; set; }

    public List<string> Messages { get; set; } = new();
}

/// <summary>Typed result of export_blocks; every member is always written.</summary>
public sealed class PlcBlockExportResultInfo
{
    public string DirectoryPath { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public bool Overwrite { get; set; }

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>The exported group path, or null for the whole PLC (blocks and PLC data types of the PLC and its software units).</summary>
    public string? Scope { get; set; }

    /// <summary>Objects in scope (exported plus skipped).</summary>
    public int ObjectCount { get; set; }

    public int FileCount { get; set; }

    /// <summary>
    /// The manifest written last into the export directory (<see cref="PlcFileExports.ManifestFileName"/>): every
    /// written file with its relative path, object, kind, bytes and SHA-256, and every skipped object.
    /// </summary>
    public string ManifestPath { get; set; } = string.Empty;

    public long ManifestBytes { get; set; }

    /// <summary>SHA-256 of the manifest as read back after the write.</summary>
    public string ManifestSha256 { get; set; } = string.Empty;

    /// <summary>The first <see cref="PlcFileExports.MaxListedFiles"/> written files; the manifest lists all of them.</summary>
    public List<PlcExportedFileInfo> Files { get; set; } = new();

    /// <summary>True when <see cref="Files"/> lists fewer files than <see cref="FileCount"/>.</summary>
    public bool FilesTruncated { get; set; }

    public int SkippedCount { get; set; }

    /// <summary>The first <see cref="PlcFileExports.MaxListedFiles"/> skipped objects; the manifest lists all of them.</summary>
    public List<PlcExportSkipInfo> Skipped { get; set; } = new();

    public bool SkippedTruncated { get; set; }

    /// <summary>False when an object was skipped or the project tree had unreadable nodes in scope.</summary>
    public bool Complete { get; set; }

    public List<string> Messages { get; set; } = new();
}

public sealed class PlcExportedFileInfo
{
    /// <summary>The block or PLC data type path, as plc_read addresses it.</summary>
    public string ObjectPath { get; set; } = string.Empty;

    /// <summary><c>block</c> or <c>type</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The file, relative to the export directory, with '/' separators.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public bool Overwritten { get; set; }
}

public sealed class PlcExportSkipInfo
{
    /// <summary>The object path, or the block name for a system block (which has no plc_read path).</summary>
    public string ObjectPath { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>The read's failure category when the object could not be read; null for an object that is ineligible by rule.</summary>
    public string? FailureCategory { get; set; }
}

/// <summary>One object export_blocks plans to write, with its primary file (relative segments, sanitized).</summary>
internal sealed record PlcBlockExportObject(string Kind, string ObjectPath, string Name, IReadOnlyList<string> Directories, string Extension, string? SkipReason);

/// <summary>
/// The objects of one export_blocks item: the PLC's (or the group's) non-system blocks and PLC data types from
/// one project-tree inventory, each with its primary file below the export directory. The directory layout
/// mirrors the project tree (<c>Blocks/Folder/Name.xml</c>, <c>Types/...</c>, <c>Units/Unit/Blocks/...</c>);
/// every segment is made Windows-safe by <see cref="PlcFileTargets.SafeFileSegment"/>. Siemens-free.
/// </summary>
internal sealed class PlcBlockExportPlan
{
    public string PlcName { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public string? Scope { get; init; }

    public string Format { get; init; } = SourceFormatNames.Xml;

    public List<PlcBlockExportObject> Objects { get; } = new();

    public List<string> Messages { get; } = new();

    public bool TreeComplete { get; init; } = true;

    /// <summary>
    /// The plan, or the call-stopping refusal: the scope group missing, more objects than the cap, or two
    /// objects whose files would collide after sanitizing (compared case-insensitively, like Windows).
    /// </summary>
    public static (PlcBlockExportPlan? Plan, string? Category, string? Error) Build(PlcScopeInventory inventory, PlcOperationRequest op)
    {
        var format = PlcFormatNames.Normalize(ExportBlocks, op.Format);
        PlcGroupPath? group = null;
        if ((op.BlockPath ?? op.TypePath) is { } scopePath) PlcGroupPath.TryParse(scopePath, out group, out _);
        if (group is not null && group.UnitName is not null && !inventory.Units.Contains(group.UnitName))
            return (null, WorkerFailureCategories.TargetNotFound, $"Software unit '{group.UnitName}' was not found in PLC '{inventory.PlcName}'. Nothing was written.");
        if (group is not null && !group.IsRoot && !inventory.Items.Any(item =>
                item.Kind == (group.Kind == PlcGroupPath.BlocksKind ? "block_group" : "type_group") && !item.IsSystem
                && PlcNameRules.Comparer.Equals(item.Unit ?? string.Empty, group.UnitName ?? string.Empty)
                && item.Folders.SequenceEqual(group.Folders, PlcNameRules.Comparer)))
            return (null, WorkerFailureCategories.TargetNotFound, $"Group '{group.ToDisplayPath()}' was not found in PLC '{inventory.PlcName}'. Nothing was written.");

        var plan = new PlcBlockExportPlan
        {
            PlcName = inventory.PlcName,
            DeviceName = inventory.DeviceName,
            Scope = group?.ToDisplayPath(),
            Format = format,
            TreeComplete = inventory.SkippedNodeCount == 0,
        };
        if (inventory.SkippedNodeCount > 0)
            plan.Messages.Add($"{inventory.SkippedNodeCount} project-tree nodes of PLC '{inventory.PlcName}' could not be read; objects among them are not exported.");

        var systemBlocks = 0;
        foreach (var item in inventory.Items)
        {
            if (item.Kind is not ("block" or "type")) continue;
            // System blocks (instructions TIA Portal adds) are not user content and have no plc_read path.
            if (item.IsSystem)
            {
                if (group is null || group.Kind == PlcGroupPath.BlocksKind) systemBlocks++;
                continue;
            }

            var kindSegment = item.Kind == "block" ? PlcGroupPath.BlocksKind : PlcGroupPath.TypesKind;
            if (group is not null && (group.Kind != kindSegment
                    || !PlcNameRules.Comparer.Equals(item.Unit ?? string.Empty, group.UnitName ?? string.Empty)
                    || item.Folders.Count < group.Folders.Count
                    || !item.Folders.Take(group.Folders.Count).SequenceEqual(group.Folders, PlcNameRules.Comparer)))
                continue;
            var path = inventory.PathOf(item.Unit, kindSegment, item.Folders, item.Name);
            var directories = new List<string>();
            if (item.Unit is not null) directories.AddRange(new[] { "Units", PlcFileTargets.SafeFileSegment(item.Unit).Name });
            directories.Add(kindSegment);
            directories.AddRange(item.Folders.Select(folder => PlcFileTargets.SafeFileSegment(folder).Name));
            string? skip = null;
            string extension;
            if (path is null)
            {
                skip = "A name segment of its path contains '/' or surrounding blanks, so it has no plc_read path.";
                extension = ".xml";
            }
            else if (format == SourceFormatNames.Xml)
            {
                extension = ".xml";
            }
            else
            {
                extension = SourceExtension(item) ?? string.Empty;
                if (extension.Length == 0)
                    skip = $"format source is available for global DBs, SCL FB/FC/OB and PLC data types only; this is {(item.NodeType ?? "a block")} ({item.Language ?? "unknown language"}). Use format xml.";
            }

            plan.Objects.Add(new PlcBlockExportObject(item.Kind, path ?? item.Name, item.Name, directories, extension, skip));
        }

        if (systemBlocks > 0) plan.Messages.Add($"{systemBlocks} system blocks are not exported (they are not user content and have no plc_read path).");
        var max = op.MaxElements ?? PlcOperationCatalog.DefaultExportObjects;
        if (plan.Objects.Count > max)
            return (null, WorkerFailureCategories.ValidationError,
                $"The export scope holds {plan.Objects.Count} objects, more than maxElements {max} (at most {PlcOperationCatalog.MaxExportObjects}). Raise maxElements or narrow blockPath/typePath. Nothing was written.");
        var collisions = plan.Objects.Where(o => o.SkipReason is null)
            .GroupBy(o => o.RelativePrimary(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        if (collisions.Count > 0)
            return (null, WorkerFailureCategories.ValidationError,
                $"These objects would write the same file after making their names Windows-safe: {string.Join("; ", collisions.Take(10).Select(g => $"{g.Key} <- {string.Join(", ", g.Select(o => o.ObjectPath))}"))}. Export their groups separately. Nothing was written.");
        return (plan, null, null);
    }

    /// <summary>The existing primary files (and the manifest) that would be replaced; empty when none exist.</summary>
    public IReadOnlyList<string> ExistingPrimaryFiles(string directory)
        => Objects.Where(o => o.SkipReason is null).Select(o => o.RelativePrimary()).Append(PlcFileExports.ManifestFileName)
            .Where(relative => File.Exists(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)))).ToList();

    private static string? SourceExtension(PlcScopeInventory.Item item)
    {
        if (item.Kind == "type") return ".udt";
        if (item.NodeType == ProjectTreeNodeTypes.GlobalDb) return ".db";
        return item.NodeType is ProjectTreeNodeTypes.Fb or ProjectTreeNodeTypes.Fc or ProjectTreeNodeTypes.Ob
            && string.Equals(item.Language, "SCL", StringComparison.OrdinalIgnoreCase) ? ".scl" : null;
    }
}

internal static class PlcBlockExportObjectExtensions
{
    public static string RelativePrimary(this PlcBlockExportObject item)
        => string.Join("/", item.Directories.Append(PlcFileTargets.SafeFileSegment(item.Name + item.Extension).Name));
}

/// <summary>
/// The plc_write file exports, host computations over existing reads that write files outside TIA Portal:
/// export_tag_tables_csv (<c>list_tag_tables</c>), export_watch_tables_csv / export_force_tables_csv
/// (<c>list_watch_and_force_tables</c>, then <c>read_watch_table</c>/<c>read_force_table</c> per table) and
/// export_blocks (one project-tree read, then <c>get_block_content</c>/<c>get_type_content</c> per object). No
/// Siemens member is added. Every file goes through a temporary file in its own directory and is moved into
/// place; an existing file is refused unless overwrite:true; the written bytes are re-read and hashed. CSV is
/// RFC 4180 with CRLF line ends, UTF-8 with BOM, and a leading single quote neutralizes cells a spreadsheet
/// could evaluate as a formula (unless rawCells).
/// </summary>
public static class PlcFileExports
{
    /// <summary>The manifest export_blocks writes last into its directory.</summary>
    public const string ManifestFileName = "tia-mcp-export-manifest.json";

    /// <summary>
    /// Files (and skipped objects) listed in the export_blocks result at most; the rest are in the manifest. The listed
    /// entries are also bounded by <see cref="MaxListedFileChars"/> and <see cref="MaxListedSkipChars"/> and a listed
    /// reason by <see cref="MaxListedReasonChars"/>, so the result stays below the item value budget (60,000 characters
    /// by default) at the 2,000-object cap whatever the path and reason lengths.
    /// </summary>
    public const int MaxListedFiles = 50;

    public const int MaxListedFileChars = 20_000;

    public const int MaxListedSkipChars = 10_000;

    public const int MaxListedReasonChars = 200;

    public const int MaxListedMessages = 20;

    public static readonly IReadOnlyList<string> TagColumns = new[]
    {
        "RowType", "Plc", "Device", "Folder", "Table", "TableIsDefault", "Name", "DataType", "LogicalAddress", "Value",
        "ExternalAccessible", "ExternalVisible", "ExternalWritable",
    };

    public static readonly IReadOnlyList<string> WatchColumns = new[]
    {
        "Plc", "Device", "Group", "Table", "TableIsConsistent", "Index", "Name", "Address", "DisplayFormat", "MonitorTrigger",
        "ModifyIntention", "ModifyTrigger", "ModifyValue", "UnreadableAttributes",
    };

    public static readonly IReadOnlyList<string> ForceColumns = new[]
    {
        "Plc", "Device", "Group", "Table", "TableIsConsistent", "Index", "Name", "Address", "DisplayFormat", "MonitorTrigger",
        "ForceIntention", "ForceValue", "UnreadableAttributes",
    };

    private static readonly string[] AbortingCategories =
    {
        WorkerFailureCategories.PortalLost, WorkerFailureCategories.BindingConflict, WorkerFailureCategories.AccessDenied,
        WorkerFailureCategories.ProtocolError, WorkerFailureCategories.WorkerTimeout, WorkerFailureCategories.WorkerCrashed,
        WorkerFailureCategories.StateChanged,
    };

    public static Task<WorkerCallResult> ExecuteAsync(OpennessWorkerClient client, PlcOperationRequest operation, CancellationToken cancellationToken = default)
        => operation.Operation switch
        {
            ExportTagTablesCsv => TagTablesAsync(client, operation, cancellationToken),
            ExportWatchTablesCsv => WatchTablesAsync(client, operation, force: false, cancellationToken),
            ExportForceTablesCsv => WatchTablesAsync(client, operation, force: true, cancellationToken),
            ExportBlocks => BlocksAsync(client, operation, cancellationToken),
            _ => Task.FromResult(WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"'{operation.Operation}' is not a file export.")),
        };

    /// <summary>The file checks shared by planning and the write; null when the target is usable.</summary>
    internal static string? FilePrecheck(PlcOperationRequest operation)
        => PlcFileTargets.PathShapeError("filePath", operation.FilePath) ?? PlcFileTargets.WriteTargetError(operation.FilePath!, operation.Overwrite == true)
            ?? PlcFileTargets.InspectDirectory(Path.GetDirectoryName(operation.FilePath!)!).Refusal;

    internal static string? DirectoryPrecheck(PlcOperationRequest operation)
        => PlcFileTargets.PathShapeError("directoryPath", operation.DirectoryPath)
            ?? (Directory.Exists(operation.DirectoryPath) ? PlcFileTargets.InspectDirectory(operation.DirectoryPath!).Refusal
                : $"The directory '{operation.DirectoryPath}' does not exist. Nothing was written.");

    /// <summary>The location warning of a write into <paramref name="directory"/> (mapped network drive, junction or link), or null.</summary>
    internal static string? LocationWarning(string? directory)
        => string.IsNullOrEmpty(directory) ? null : PlcFileTargets.InspectDirectory(directory!).Warning;

    private static IReadOnlyList<string> WithLocationWarning(IReadOnlyList<string> warnings, string? directory, string operation)
        => LocationWarning(directory) is { } warning ? warnings.Append($"{operation}: {warning}").ToList() : warnings;

    // ------------------------------------------------------------------
    // CSV
    // ------------------------------------------------------------------

    private static async Task<WorkerCallResult> TagTablesAsync(OpennessWorkerClient client, PlcOperationRequest op, CancellationToken cancellationToken)
    {
        if (FilePrecheck(op) is { } refusal) return Refused(op, refusal);
        var read = await client.ListTagTablesAsync(op.PlcName, tableName: op.TableName, folderPath: op.FolderPath).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = op.OperationId, Operation = "list_tag_tables" }, read);
        if (projected.Result is not { } element)
            return WorkerCallResult.Fail(projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed,
                $"{op.Operation}: the tag tables could not be read: {projected.Failure?.Message} No file was written.", read.Warnings);
        var inventory = CanonicalJson.Deserialize<PlcTagInventoryInfo>(element.GetRawText());
        var warnings = read.Warnings.ToList();
        var commentRequest = new PlcOperationRequest
        {
            OperationId = op.OperationId, Operation = ReadTagComments, PlcName = op.PlcName, TableName = op.TableName, FolderPath = op.FolderPath,
        };
        var commentRead = await PlcSourceFolderOperations.InvokeReadAsync(client, commentRequest).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        warnings.AddRange(commentRead.Warnings);
        if (!commentRead.Success)
            return WorkerCallResult.Fail(commentRead.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"{op.Operation}: the tag comments could not be read: {commentRead.Error} No file was written.", warnings);
        PlcTagCommentInventoryInfo commentInventory;
        try
        {
            commentInventory = CanonicalJson.Deserialize<PlcTagCommentInventoryInfo>(PlcSourceFolderOperations.DecodeRead(commentRequest, commentRead.Payload).GetRawText());
        }
        catch (JsonException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, $"{op.Operation}: the tag comment payload did not match its contract. No file was written.", warnings);
        }

        var comments = new Dictionary<string, List<PlcCommentTextInfo>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var plc in commentInventory.Plcs)
            foreach (var table in plc.Tables)
                // The tag inventory lists the PLC's own tables only; software-unit tables are not exported.
                foreach (var member in table.UnitName is null ? table.Members : new List<PlcMemberCommentInfo>())
                    comments[CommentKey(plc.PlcName, plc.DeviceName, table.FolderPath, table.TableName, member.Kind, member.Name)] = member.Comment;
        var cultures = comments.Values.Where(c => c is not null).SelectMany(c => c!).Select(c => c.Culture).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var messages = inventory.Messages.Concat(commentInventory.Messages).ToList();
        var complete = inventory.IsComplete && commentInventory.Complete;
        var csv = new Csv(Delimiter(op), op.RawCells == true);
        csv.Row(TagColumns.Concat(cultures.Select(c => $"Comment[{c}]")).ToList());
        int tables = 0, empty = 0, missing = 0;
        IEnumerable<string> CommentCells(string plc, string? device, string folder, string table, string kind, string name)
        {
            if (!comments.TryGetValue(CommentKey(plc, device ?? string.Empty, folder, table, kind, name), out var comment))
            {
                missing++;
                return cultures.Select(_ => string.Empty);
            }

            return cultures.Select(culture => comment?.FirstOrDefault(entry => entry.Culture == culture)?.Text ?? string.Empty);
        }

        foreach (var plc in inventory.Plcs)
            foreach (var table in plc.Tables)
            {
                tables++;
                if (table.Tags.Count + table.UserConstants.Count == 0) empty++;
                foreach (var tag in table.Tags)
                    csv.Row(new[]
                    {
                        "tag", plc.PlcName, plc.DeviceName ?? string.Empty, table.FolderPath, table.Name, Flag(table.IsDefault), tag.Name, tag.DataType, tag.LogicalAddress,
                        string.Empty, Flag(tag.ExternalAccessible), Flag(tag.ExternalVisible), Flag(tag.ExternalWritable),
                    }.Concat(CommentCells(plc.PlcName, plc.DeviceName, table.FolderPath, table.Name, "Tag", tag.Name)).ToList());
                foreach (var constant in table.UserConstants)
                    csv.Row(new[]
                    {
                        "constant", plc.PlcName, plc.DeviceName ?? string.Empty, table.FolderPath, table.Name, Flag(table.IsDefault), constant.Name, constant.DataType,
                        string.Empty, constant.Value ?? string.Empty, string.Empty, string.Empty, string.Empty,
                    }.Concat(CommentCells(plc.PlcName, plc.DeviceName, table.FolderPath, table.Name, "UserConstant", constant.Name)).ToList());
            }

        if (missing > 0)
        {
            complete = false;
            messages.Add($"{missing} tags or user constants had no entry in the comment read (the tag tables changed between the two reads?); their comment cells are empty.");
        }

        return WriteCsv(op, csv, inventory.Plcs.Count, tables, empty, complete, messages, warnings);
    }

    private static string CommentKey(string plc, string device, string folder, string table, string kind, string name)
        => string.Join("\n", plc, device, folder, table, kind, name);

    private static async Task<WorkerCallResult> WatchTablesAsync(OpennessWorkerClient client, PlcOperationRequest op, bool force, CancellationToken cancellationToken)
    {
        if (FilePrecheck(op) is { } refusal) return Refused(op, refusal);
        var warnings = new List<string>();
        var list = await client.ListWatchAndForceTablesAsync(op.PlcName).ConfigureAwait(false);
        warnings.AddRange(list.Warnings);
        if (!list.Success)
            return WorkerCallResult.Fail(list.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"{op.Operation}: the watch and force tables could not be listed: {list.Error} No file was written.", warnings);
        PlcWatchTableInventoryInfo inventory;
        try
        {
            inventory = PlcOnlinePayloadContract.DecodeWatchTables(list.Payload);
        }
        catch (JsonException)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, $"{op.Operation}: {PlcOnlinePayloadContract.ProtocolFailureMessage} No file was written.", warnings);
        }

        if (op.PlcName is not null && inventory.Plcs.Count == 0)
            return WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound, $"{op.Operation}: PLC '{op.PlcName}' matches no PLC software. No file was written.", warnings);
        var messages = new List<string>();
        var complete = true;
        var group = op.FolderPath is null ? null : PlcWatchTablePolicy.NormalizeGroupPath(op.FolderPath);
        // Like the tag CSV (list_tag_tables with folderPath): a group no PLC has is target_not_found, unless an
        // incomplete inventory may hide it.
        if (group is not null && group != "/" && !inventory.Plcs.Any(p => p.Groups.Contains(group, PlcNameRules.Comparer)))
        {
            if (inventory.Plcs.All(p => p.Complete))
                return WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound,
                    $"{op.Operation}: the watch-and-force table group '{group}' was not found{(op.PlcName is null ? string.Empty : $" in PLC '{op.PlcName}'")}. No file was written.", warnings);
            messages.Add($"The watch-and-force table group '{group}' was not found, but the inventory is incomplete, so it may exist.");
        }

        var csv = new Csv(Delimiter(op), op.RawCells == true);
        csv.Row(force ? ForceColumns : WatchColumns);
        int tables = 0, empty = 0;
        var kind = force ? "force table" : "watch table";
        foreach (var plc in inventory.Plcs)
        {
            if (!plc.Complete)
            {
                complete = false;
                messages.Add($"The watch-and-force table inventory of PLC '{plc.PlcName}' is incomplete; tables beyond its cap are not exported.");
            }

            var candidates = (force ? plc.ForceTables : plc.WatchTables)
                .Where(t => op.TableName is null
                    ? group is null || PlcNameRules.Comparer.Equals(t.GroupPath, group)
                    : PlcNameRules.Comparer.Equals(t.Name, op.TableName) && PlcNameRules.Comparer.Equals(t.GroupPath, group ?? "/"))
                .ToList();
            foreach (var table in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var method = force ? PlcOnlineMethods.ReadForceTable : PlcOnlineMethods.ReadWatchTable;
                var read = await client.ReadWatchOrForceTableAsync(method, plc.PlcName, table.Name, table.GroupPath, PlcWatchTablePolicy.MaxEntries).ConfigureAwait(false);
                warnings.AddRange(read.Warnings);
                if (!read.Success)
                    return WorkerCallResult.Fail(read.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                        $"{op.Operation}: {kind} '{table.GroupPath}/{table.Name}' of PLC '{plc.PlcName}' could not be read: {read.Error} No file was written.", warnings);
                tables++;
                try
                {
                    if (force)
                    {
                        var info = CanonicalJson.Deserialize<PlcForceTableReadInfo>(PlcOnlinePayloadContract.NormalizeForceTable(read.Payload).GetRawText());
                        if (!info.EntriesComplete) { complete = false; messages.Add($"Force table '{info.GroupPath}/{info.Name}' has more than {PlcWatchTablePolicy.MaxEntries} entries; the first ones are exported."); }
                        if (info.Entries.Count == 0) empty++;
                        foreach (var entry in info.Entries)
                            csv.Row(new[]
                            {
                                info.PlcName, info.DeviceName, info.GroupPath, info.Name, Flag(info.IsConsistent), entry.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                entry.Name ?? string.Empty, entry.Address ?? string.Empty, entry.DisplayFormat ?? string.Empty, entry.MonitorTrigger ?? string.Empty,
                                Flag(entry.ForceIntention), entry.ForceValue ?? string.Empty, string.Join("|", entry.UnreadableAttributes),
                            });
                    }
                    else
                    {
                        var info = CanonicalJson.Deserialize<PlcWatchTableReadInfo>(PlcOnlinePayloadContract.NormalizeWatchTable(read.Payload).GetRawText());
                        if (!info.EntriesComplete) { complete = false; messages.Add($"Watch table '{info.GroupPath}/{info.Name}' has more than {PlcWatchTablePolicy.MaxEntries} entries; the first ones are exported."); }
                        if (info.Entries.Count == 0) empty++;
                        foreach (var entry in info.Entries)
                            csv.Row(new[]
                            {
                                info.PlcName, info.DeviceName, info.GroupPath, info.Name, Flag(info.IsConsistent), entry.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                entry.Name ?? string.Empty, entry.Address ?? string.Empty, entry.DisplayFormat ?? string.Empty, entry.MonitorTrigger ?? string.Empty,
                                Flag(entry.ModifyIntention), entry.ModifyTrigger ?? string.Empty, entry.ModifyValue ?? string.Empty, string.Join("|", entry.UnreadableAttributes),
                            });
                    }
                }
                catch (JsonException)
                {
                    return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError, $"{op.Operation}: {PlcOnlinePayloadContract.ProtocolFailureMessage} No file was written.", warnings);
                }
            }
        }

        if (op.TableName is not null && tables == 0)
            return WorkerCallResult.Fail(WorkerFailureCategories.TargetNotFound,
                $"{op.Operation}: {kind} '{op.TableName}' was not found in group '{group ?? "/"}'{(op.PlcName is null ? string.Empty : $" of PLC '{op.PlcName}'")}. No file was written.", warnings);
        return WriteCsv(op, csv, inventory.Plcs.Count, tables, empty, complete, messages, warnings);
    }

    private static WorkerCallResult WriteCsv(PlcOperationRequest op, Csv csv, int plcs, int tables, int empty, bool complete, List<string> messages, IReadOnlyList<string> warnings)
    {
        PlcWrittenFile written;
        try
        {
            written = PlcFileTargets.Write(op.FilePath!, PlcFileTargets.Utf8WithBom(csv.Text), op.Overwrite == true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var exists = op.Overwrite != true && File.Exists(op.FilePath);
            return WorkerCallResult.Fail(exists ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.WorkerOperationFailed,
                $"{op.Operation}: the file '{op.FilePath}' could not be written: {exception.Message}" + (exists ? " It exists; pass overwrite:true to replace it." : string.Empty), warnings);
        }
        catch (PlcFileNotVerifiedException exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.PostconditionFailed, $"{op.Operation}: {exception.Message}", warnings);
        }

        warnings = WithLocationWarning(warnings, Path.GetDirectoryName(op.FilePath!), op.Operation);
        var result = new PlcCsvExportResultInfo
        {
            FilePath = op.FilePath!,
            Overwritten = written.Replaced,
            Delimiter = Delimiter(op),
            RawCells = op.RawCells == true,
            NeutralizedCells = csv.Neutralized,
            PlcCount = plcs,
            TableCount = tables,
            EmptyTableCount = empty,
            RowCount = csv.Rows - 1,
            Bytes = written.Bytes,
            Sha256 = written.Sha256,
            Complete = complete,
            Messages = messages,
        };
        return WorkerCallResult.Ok(WorkerJson.SerializePayload(result), warnings);
    }

    private static string Delimiter(PlcOperationRequest op) => op.CsvDelimiter ?? ",";

    private static string Flag(bool? value) => value switch { true => "true", false => "false", null => string.Empty };

    private static WorkerCallResult Refused(PlcOperationRequest op, string message)
        => WorkerCallResult.Fail(WorkerFailureCategories.ValidationError, $"{op.Operation}: {message}");

    /// <summary>RFC 4180 rows with CRLF line ends and formula-lead neutralization (shared with export_io_map).</summary>
    private sealed class Csv(string delimiter, bool rawCells)
    {
        private readonly StringBuilder _builder = new();
        private int _neutralized;

        public int Rows { get; private set; }

        public int Neutralized => _neutralized;

        public string Text => _builder.ToString();

        public void Row(IReadOnlyList<string> cells)
        {
            for (var index = 0; index < cells.Count; index++)
            {
                if (index > 0) _builder.Append(delimiter);
                // The header row is constant text and never neutralized.
                var value = Rows == 0 ? cells[index] : HardwareIoMapCsv.Neutralize(cells[index] ?? string.Empty, rawCells, ref _neutralized);
                _builder.Append(HardwareIoMapCsv.Quote(value, delimiter));
            }

            _builder.Append("\r\n");
            Rows++;
        }
    }

    // ------------------------------------------------------------------
    // export_blocks
    // ------------------------------------------------------------------

    private static readonly Regex BundleDelimiter = new(@"^--- FILE: (?<name>.+) ---(?:\r?\n|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex BundleDelimiterCandidate = new(@"^--- FILE:", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The documents of one get_block_content/get_type_content read. A <c>source</c> read is one document, written
    /// whole (a source line that looks like a bundle marker is content). An <c>xml</c> read is split at its
    /// "--- FILE: name ---" markers by the rules of the block import's bundle parser: no undeclared text before the
    /// first marker, no malformed marker line, no empty or repeated document; without markers it is one document.
    /// </summary>
    internal static (IReadOnlyList<(string Name, string Content)>? Documents, string? Error) Documents(string content, string primaryName, string format)
    {
        if (format != SourceFormatNames.Xml) return (new[] { (primaryName, content) }, null);
        foreach (Match candidate in BundleDelimiterCandidate.Matches(content))
        {
            var marker = BundleDelimiter.Match(content, candidate.Index);
            if (!marker.Success || marker.Index != candidate.Index) return (null, "the xml read contains a malformed '--- FILE:' marker line");
        }

        var markers = BundleDelimiter.Matches(content);
        if (markers.Count == 0) return (new[] { (primaryName, content) }, null);
        if (!string.IsNullOrWhiteSpace(content[..markers[0].Index])) return (null, "the xml read has undeclared content before its first '--- FILE:' marker");
        var documents = new List<(string, string)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < markers.Count; index++)
        {
            var start = markers[index].Index + markers[index].Length;
            var end = index + 1 < markers.Count ? markers[index + 1].Index : content.Length;
            var name = markers[index].Groups["name"].Value.Trim();
            var body = content[start..end];
            if (string.IsNullOrWhiteSpace(body)) return (null, $"the xml read declares the document '{name}' without content");
            if (!names.Add(name)) return (null, $"the xml read declares the document '{name}' twice");
            documents.Add((name, body));
        }

        return (documents, null);
    }

    /// <summary>Reads the PLC's project tree and builds the plan (also used by planning).</summary>
    internal static async Task<(PlcBlockExportPlan? Plan, WorkerCallResult? Failure, IReadOnlyList<string> Warnings)> PlanBlocksAsync(OpennessWorkerClient client, PlcOperationRequest op)
    {
        var call = await client.BrowseProjectTreeV3SnapshotAsync().ConfigureAwait(false);
        var warnings = call.WorkerResult.Warnings.ToList();
        if (!call.WorkerResult.Success)
            return (null, WorkerCallResult.Fail(call.WorkerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"{op.Operation}: the project tree could not be read: {call.WorkerResult.Error} Nothing was written.", warnings), warnings);
        ProjectTreeObservation tree;
        try
        {
            tree = ProjectTreeWorkerPayloadContract.Decode(call.WorkerResult, null, null);
        }
        catch (ProjectTreeProtocolException exception)
        {
            return (null, WorkerCallResult.Fail(exception.Category, $"{op.Operation}: {exception.Message}", warnings), warnings);
        }

        return PlanFrom(tree, op, warnings);
    }

    internal static (PlcBlockExportPlan? Plan, WorkerCallResult? Failure, IReadOnlyList<string> Warnings) PlanFrom(ProjectTreeObservation tree, PlcOperationRequest op, IReadOnlyList<string> warnings)
    {
        string? selector = op.PlcName;
        if ((op.BlockPath ?? op.TypePath) is { } scopePath && PlcGroupPath.TryParse(scopePath, out var group, out _)) selector ??= group!.PlcName;
        var inventories = PlcScopeInventory.Select(tree, selector);
        if (inventories.Count != 1)
            return (null, WorkerCallResult.Fail(inventories.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                inventories.Count == 0
                    ? $"{op.Operation}: {(selector is null ? "the project has no PLC software" : $"PLC '{selector}' matches no PLC software")}. Nothing was written."
                    : $"{op.Operation}: {inventories.Count} PLCs match ({string.Join(", ", inventories.Select(i => $"'{i.PlcName}'"))}); name one with plcName. Nothing was written.", warnings), warnings);
        var (plan, category, error) = PlcBlockExportPlan.Build(inventories[0], op);
        return plan is null
            ? (null, WorkerCallResult.Fail(category!, $"{op.Operation}: {error}", warnings), warnings)
            : (plan, null, warnings);
    }

    private static async Task<WorkerCallResult> BlocksAsync(OpennessWorkerClient client, PlcOperationRequest op, CancellationToken cancellationToken)
    {
        if (DirectoryPrecheck(op) is { } refusal) return Refused(op, refusal);
        var (plan, failure, planWarnings) = await PlanBlocksAsync(client, op).ConfigureAwait(false);
        if (plan is null) return failure!;
        var warnings = planWarnings.ToList();
        var directory = op.DirectoryPath!;
        var overwrite = op.Overwrite == true;
        if (!overwrite && plan.ExistingPrimaryFiles(directory) is { Count: > 0 } existing)
            return WorkerCallResult.Fail(WorkerFailureCategories.ValidationError,
                $"{op.Operation}: {existing.Count} target files exist already ({string.Join(", ", existing.Take(10))}{(existing.Count > 10 ? ", ..." : string.Empty)}); pass overwrite:true to replace them. Nothing was written.", warnings);

        var result = new PlcBlockExportResultInfo
        {
            DirectoryPath = directory,
            Format = plan.Format,
            Overwrite = overwrite,
            PlcName = plan.PlcName,
            DeviceName = plan.DeviceName,
            Scope = plan.Scope,
            ObjectCount = plan.Objects.Count,
            Messages = plan.Messages.ToList(),
        };
        foreach (var item in plan.Objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.SkipReason is not null)
            {
                result.Skipped.Add(new PlcExportSkipInfo { ObjectPath = item.ObjectPath, Kind = item.Kind, Reason = item.SkipReason, FailureCategory = null });
                continue;
            }

            var read = item.Kind == "block"
                ? await client.GetBlockContentAsync(item.ObjectPath, format: plan.Format).ConfigureAwait(false)
                : await client.GetTypeContentAsync(item.ObjectPath, format: plan.Format).ConfigureAwait(false);
            warnings.AddRange(read.Warnings);
            if (!read.Success || string.IsNullOrEmpty(read.Payload))
            {
                var category = read.Success ? WorkerFailureCategories.WorkerOperationFailed : read.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
                if (AbortingCategories.Contains(category))
                    return WorkerCallResult.Fail(category, $"{op.Operation}: reading '{item.ObjectPath}' failed with {category}: {read.Error} {Written(result, directory)}", warnings);
                result.Skipped.Add(new PlcExportSkipInfo
                {
                    ObjectPath = item.ObjectPath, Kind = item.Kind, FailureCategory = category,
                    Reason = read.Success ? "The read returned no content." : read.Error ?? "The read failed.",
                });
                continue;
            }

            var primary = item.Name + item.Extension;
            var (documents, documentError) = Documents(read.Payload, primary, plan.Format);
            if (documents is null)
                return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                    $"{op.Operation}: the read of '{item.ObjectPath}' cannot be split into documents: {documentError}. {Written(result, directory)}", warnings);
            foreach (var (name, content) in documents)
            {
                if (name.Length == 0 || name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Any(char.IsControl) || PlcFileTargets.SegmentError(name) is not null)
                    return WorkerCallResult.Fail(WorkerFailureCategories.ProtocolError,
                        $"{op.Operation}: the read of '{item.ObjectPath}' names a document '{name}' that is not a plain file name. {Written(result, directory)}", warnings);
                var relative = string.Join("/", item.Directories.Append(PlcFileTargets.SafeFileSegment(name).Name));
                var full = Path.Combine(new[] { directory }.Concat(relative.Split('/')).ToArray());
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    var written = PlcFileTargets.Write(full, PlcFileTargets.Utf8WithBom(content), overwrite);
                    result.Files.Add(new PlcExportedFileInfo
                    {
                        ObjectPath = item.ObjectPath, Kind = item.Kind, RelativePath = relative,
                        Bytes = written.Bytes, Sha256 = written.Sha256, Overwritten = written.Replaced,
                    });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    var exists = !overwrite && File.Exists(full);
                    return WorkerCallResult.Fail(exists ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.WorkerOperationFailed,
                        $"{op.Operation}: '{full}' could not be written: {exception.Message}{(exists ? " It exists; pass overwrite:true to replace it." : string.Empty)} {Written(result, directory)}", warnings);
                }
                catch (PlcFileNotVerifiedException exception)
                {
                    return WorkerCallResult.Fail(WorkerFailureCategories.PostconditionFailed, $"{op.Operation}: {exception.Message} {Written(result, directory)}", warnings);
                }
            }
        }

        result.FileCount = result.Files.Count;
        result.SkippedCount = result.Skipped.Count;
        result.Complete = plan.TreeComplete && result.Skipped.Count == 0;
        var manifestPath = Path.Combine(directory, ManifestFileName);
        try
        {
            var manifest = PlcFileTargets.Write(manifestPath, PlcFileTargets.Utf8WithBom(Manifest(result)), overwrite);
            (result.ManifestPath, result.ManifestBytes, result.ManifestSha256) = (manifestPath, manifest.Bytes, manifest.Sha256);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var exists = !overwrite && File.Exists(manifestPath);
            return WorkerCallResult.Fail(exists ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.WorkerOperationFailed,
                $"{op.Operation}: the manifest '{manifestPath}' could not be written: {exception.Message}{(exists ? " It exists; pass overwrite:true to replace it." : string.Empty)} {Written(result, directory)}", warnings);
        }
        catch (PlcFileNotVerifiedException exception)
        {
            return WorkerCallResult.Fail(WorkerFailureCategories.PostconditionFailed, $"{op.Operation}: {exception.Message} {Written(result, directory)}", warnings);
        }

        Bound(result);
        return WorkerCallResult.Ok(WorkerJson.SerializePayload(result), WithLocationWarning(warnings, directory, op.Operation));
    }

    /// <summary>
    /// Bounds the listed part of an export_blocks result after the manifest (which keeps everything) was written: at most
    /// <see cref="MaxListedFiles"/> files and skipped objects, within <see cref="MaxListedFileChars"/> /
    /// <see cref="MaxListedSkipChars"/> serialized characters, reasons cut at <see cref="MaxListedReasonChars"/>, at most
    /// <see cref="MaxListedMessages"/> messages.
    /// </summary>
    internal static void Bound(PlcBlockExportResultInfo result)
    {
        var files = Listed(result.Files, MaxListedFileChars, file => file);
        var skipped = Listed(result.Skipped, MaxListedSkipChars, skip => new PlcExportSkipInfo
        {
            ObjectPath = skip.ObjectPath, Kind = skip.Kind, FailureCategory = skip.FailureCategory,
            Reason = skip.Reason.Length <= MaxListedReasonChars ? skip.Reason : skip.Reason[..MaxListedReasonChars] + "... (full text in the manifest)",
        });
        result.FilesTruncated = files.Count < result.Files.Count;
        result.SkippedTruncated = skipped.Count < result.Skipped.Count;
        (result.Files, result.Skipped) = (files, skipped);
        if (result.Messages.Count > MaxListedMessages)
        {
            var omitted = result.Messages.Count - MaxListedMessages;
            result.Messages = result.Messages.Take(MaxListedMessages).Append($"... and {omitted} more messages.").ToList();
        }
    }

    private static List<T> Listed<T>(IReadOnlyList<T> entries, int budget, Func<T, T> project)
    {
        var listed = new List<T>();
        var used = 0;
        foreach (var entry in entries.Take(MaxListedFiles))
        {
            var projected = project(entry);
            var size = WorkerJson.SerializePayload(projected).Length + 1;
            if (used + size > budget) break;
            listed.Add(projected);
            used += size;
        }

        return listed;
    }

    /// <summary>
    /// The manifest: deterministic, indented JSON (files and skipped objects in export order) with every written
    /// file's relative path, object, kind, bytes and SHA-256. Written last, so it lists exactly what is on disk.
    /// </summary>
    internal static string Manifest(PlcBlockExportResultInfo result)
    {
        var manifest = new
        {
            format = result.Format,
            plcName = result.PlcName,
            deviceName = result.DeviceName,
            scope = result.Scope,
            complete = result.Complete,
            files = result.Files.Select(f => new { relativePath = f.RelativePath, objectPath = f.ObjectPath, kind = f.Kind, bytes = f.Bytes, sha256 = f.Sha256 }),
            skipped = result.Skipped.Select(s => new { objectPath = s.ObjectPath, kind = s.Kind, reason = s.Reason, failureCategory = s.FailureCategory }),
        };
        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
    }

    private static string Written(PlcBlockExportResultInfo result, string directory)
        => result.Files.Count == 0
            ? "No file was written."
            : $"{result.Files.Count} files were already written below '{directory}' and remain (no rollback): {string.Join(", ", result.Files.Take(10).Select(f => f.RelativePath))}{(result.Files.Count > 10 ? ", ..." : string.Empty)}.";
}
