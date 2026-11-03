using System.Collections;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// export_tag_table / import_tag_table: one tag table as a SimaticML file at a caller path. Both members are documented
/// by the Siemens tags-and-tagtables skill (<c>PlcTagTable.Export(FileInfo, ExportOptions)</c>, compiled against the
/// stub; <c>TagTableCollection.Import(FileInfo, ImportOptions)</c>, bound at runtime by its parameter list as for
/// move_tag_table because the reference states no return type). The export goes to a private temporary file and is
/// placed with <see cref="TechnologyObjectFiles.Place(byte[], string, bool, string, List{string})"/> (staged next to the
/// target, moved into place, re-hashed). The import copies the caller's file privately first and imports the copy inside
/// one ExclusiveAccess transaction, so a failed postcondition rolls it back. Caller paths follow the technology object
/// rules (absolute drive or UNC share, lexical checks, network drives and links reported, never refused).
/// </summary>
internal static class TagTableFileService
{
    public static PlcTagTableFileResultInfo Export(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var tableName = Require(request.TableName, "tableName");
        var folder = TagTargetResolver.NormalizeFolderPath(request.FolderPath);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName).Software;
        var table = TagTableMoveService.Folder(plc, folder).TagTables.Find(tableName)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag table '{tableName}' was not found in '{folder}' of PLC '{plc.Name}'. Nothing was written.");
        var target = TechnologyObjectService.LocalFile(request.FilePath);
        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory of '{target}' does not exist. Nothing was written.");
        if (Directory.Exists(target)) throw TechnologyObjectService.Invalid($"'{target}' is a directory, not a file. Nothing was written.");
        var overwrite = request.Overwrite == true;
        var existed = File.Exists(target);
        if (existed && !overwrite) throw TechnologyObjectService.Invalid($"'{target}' already exists; set overwrite:true to replace it. Nothing was written.");
        var messages = TechnologyObjectService.FileWarnings(target);
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-tag-table-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        byte[] bytes;
        try
        {
            var exported = Path.Combine(temporary, "table.xml");
            table.Export(new FileInfo(exported), ExportOptions.None);
            if (!File.Exists(exported))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no export file for tag table '{table.Name}'. Nothing was written.");
            bytes = File.ReadAllBytes(exported);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        if (TechnologyObjectFiles.Place(bytes, target, overwrite, $"The export of tag table '{table.Name}'", messages) is { } failure)
            throw new WorkerOperationException(failure.Category, failure.Message);
        return new PlcTagTableFileResultInfo
        {
            Operation = PlcBlockEditMethods.ExportTagTable, PlcName = plc.Name, FolderPath = folder, Tables = { table.Name },
            FilePath = target, FileBytes = bytes.LongLength, FileSha256 = TechnologyObjectFiles.Sha256(bytes), Overwrite = existed,
            TagCount = table.Tags.Count(), UserConstantCount = table.UserConstants.Count(), Messages = messages,
        };
    }

    public static PlcTagTableFileResultInfo Import(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var source = TechnologyObjectService.LocalFile(request.FilePath);
        if (!File.Exists(source)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{source}' does not exist. Nothing was imported.");
        var folder = TagTargetResolver.NormalizeFolderPath(request.FolderPath);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName).Software;
        var group = TagTableMoveService.Folder(plc, folder);
        var overwrite = request.Overwrite == true;
        var expected = string.IsNullOrWhiteSpace(request.TableName) ? null : request.TableName!.Trim();
        if (expected is not null && group.TagTables.Find(expected) is { } existing)
        {
            if (!overwrite)
                throw TechnologyObjectService.Invalid($"Tag table '{existing.Name}' already exists in '{folder}'; set overwrite:true to replace it (ImportOptions.Override). Nothing was imported.");
            if (TagTableMoveService.HasSafetyTags(existing))
                Safety.PlcSafetyGate.RequireSafetyTagChangePermitted(project, request.PlcName, $"import_tag_table over '{existing.Name}'");
        }

        // Bound before anything changes, like move_tag_table.
        var import = TagTableMoveService.BindImport(group.TagTables, overwrite ? ImportOptions.Override : ImportOptions.None, out var returned);
        var messages = TechnologyObjectService.FileWarnings(source);
        var before = Names(group);
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-tag-table-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var copy = Path.Combine(temporary, "table.xml");
            File.Copy(source, copy);
            var bytes = File.ReadAllBytes(copy);
            var outcome = PlcRoundTrip.Run(portal, project, $"import_tag_table '{source}' into '{folder}'", () =>
            {
                import(new FileInfo(copy));
                var (tables, failure, warning) = PlcTagTableImportCheck.Imported(ReturnedNames(returned()), before, Names(group), expected, overwrite, folder);
                if (failure is not null) throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, failure);
                if (warning is not null) messages.Add(warning);
                var found = tables.Select(name => group.TagTables.Find(name)).Where(t => t is not null).ToList();
                return new PlcTagTableFileResultInfo
                {
                    Operation = PlcBlockEditMethods.ImportTagTable, PlcName = plc.Name, FolderPath = folder, Tables = found.Select(t => t!.Name).ToList(),
                    FilePath = source, FileBytes = bytes.LongLength, FileSha256 = TechnologyObjectFiles.Sha256(bytes), Overwrite = overwrite,
                    TagCount = found.Sum(t => t!.Tags.Count()), UserConstantCount = found.Sum(t => t!.UserConstants.Count()), Messages = messages,
                };
            });
            if (outcome.State == PlcRoundTrip.TransactionState.Committed) return outcome.Result!;
            throw PlcRoundTrip.RolledBackFailure(outcome, () => $"the tag tables of '{folder}' now are [{string.Join(", ", Names(group).Select(n => $"'{n}'"))}]");
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }
    }

    private static List<string> Names(PlcTagTableGroup group)
    {
        var names = new List<string>();
        foreach (PlcTagTable table in group.TagTables) names.Add(table.Name);
        return names;
    }

    /// <summary>The names of an <c>Import</c> return value that is a tag table or an enumerable of tag tables; null for anything else.</summary>
    private static List<string>? ReturnedNames(object? value)
    {
        if (value is PlcTagTable single) return new List<string> { single.Name };
        if (value is not IEnumerable items || value is string) return null;
        var names = new List<string>();
        foreach (var item in items)
        {
            if (item is not PlcTagTable table) return null;
            names.Add(table.Name);
        }

        return names;
    }

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{field} is required.")
            : value!;
}
