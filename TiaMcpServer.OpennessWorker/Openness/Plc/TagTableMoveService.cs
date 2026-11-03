using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// move_tag_table: no native move is documented, so the table is exported
/// (<c>PlcTagTable.Export(FileInfo, ExportOptions)</c>), then deleted and imported into the target folder
/// (<c>PlcTagTableComposition.Import(FileInfo, ImportOptions)</c>) inside one ExclusiveAccess
/// transaction (<see cref="PlcRoundTrip"/>). The default tag table is refused (TIA Portal does not delete it); a table
/// holding a tag that reports <c>IsSafety=true</c> falls under the safety login rule (refused only when a safety
/// password is set and the session is not logged on), and the import member is bound
/// before the delete. When the rollback does not bring the table back it is re-imported into the
/// source folder; when that fails the export is kept as a recovery file, and the temporary export is
/// never deleted while it is the only copy.
/// </summary>
internal static class TagTableMoveService
{
    public static PlcTagTableMoveResultInfo Move(TiaPortal portal, ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var tableName = Require(request.TableName, "tableName");
        var targetFolder = TagTargetResolver.NormalizeFolderPath(Require(request.TargetFolderPath, "targetFolderPath"));
        var sourceFolder = TagTargetResolver.NormalizeFolderPath(request.FolderPath);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName).Software;
        var source = Folder(plc, sourceFolder);
        var target = Folder(plc, targetFolder);
        var table = source.TagTables.Find(tableName)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag table '{tableName}' was not found in '{sourceFolder}'.");
        if (table.IsDefault)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{tableName}' is the default tag table; TIA Portal does not delete it, so it cannot be moved.");
        if (target.Equals(source))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Tag table '{tableName}' is already in '{targetFolder}'.");
        if (HasSafetyTags(table))
            Safety.PlcSafetyGate.RequireSafetyTagChangePermitted(project, request.PlcName, $"move_tag_table of '{tableName}'");

        // Bound before anything changes: a missing member must not be discovered after the delete.
        var importIntoTarget = BindImport(target.TagTables);
        var importIntoSource = BindImport(source.TagTables);
        var name = table.Name;
        var tagCount = table.Tags.Count();
        var constantCount = table.UserConstants.Count();
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-tag-table-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var keepDirectory = false;
        try
        {
            var file = new FileInfo(Path.Combine(directory, "table.xml"));
            table.Export(file, ExportOptions.None);
            var xml = File.ReadAllText(file.FullName);
            var outcome = PlcRoundTrip.Run(portal, project, $"move_tag_table '{name}' to '{targetFolder}'", () =>
            {
                table.Delete();
                importIntoTarget(file);
                var moved = target.TagTables.Find(name)
                    ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The import reported no error but '{name}' is not in '{targetFolder}'.");
                var result = new PlcTagTableMoveResultInfo
                {
                    PlcName = plc.Name, TableName = moved.Name, SourceFolderPath = sourceFolder, TargetFolderPath = targetFolder,
                    TagCount = moved.Tags.Count(), UserConstantCount = moved.UserConstants.Count(),
                };
                if (result.TagCount != tagCount || result.UserConstantCount != constantCount)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                        $"The imported '{name}' holds {result.TagCount} tags and {result.UserConstantCount} user constants (before: {tagCount} and {constantCount}).");
                return result;
            });
            if (outcome.State == PlcRoundTrip.TransactionState.Committed) return outcome.Result!;

            var subject = new PlcRoundTrip.Subject
            {
                Operation = PlcBlockEditMethods.MoveTagTable,
                ObjectKind = "tagTable",
                ObjectName = name,
                PlcName = plc.Name,
                SourcePath = $"{plc.Name}{(sourceFolder == "/" ? "/" : sourceFolder + "/")}{name}",
                TargetPath = targetFolder,
                Document = xml,
            };
            var failure = PlcRoundTrip.MoveFailure(outcome, subject,
                sourcePresent: () => source.TagTables.Find(name) is not null,
                targetPresent: () => target.TagTables.Find(name) is not null,
                restore: () => importIntoSource(file),
                existingDocumentPath: file.FullName,
                out keepDirectory);
            throw failure;
        }
        finally
        {
            if (!keepDirectory) TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }
    }

    /// <summary>
    /// Whether a tag of the table reports the documented attribute <c>IsSafety=true</c> (then the safety login rule
    /// applies, as for move_tag). An unreadable attribute (standard CPUs need not expose it) does not count.
    /// </summary>
    internal static bool HasSafetyTags(PlcTagTable table)
    {
        foreach (PlcTag tag in table.Tags)
        {
            try
            {
                if (((IEngineeringObject)tag).GetAttribute("IsSafety") as bool? == true) return true;
            }
            catch (EngineeringException)
            {
                // Unreadable: not counted.
            }
        }

        return false;
    }

    internal static PlcTagTableGroup Folder(PlcSoftware plc, string folderPath)
    {
        PlcTagTableGroup group = plc.TagTableGroup;
        foreach (var segment in folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            group = group.Groups.Find(segment)
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag table folder '{folderPath}' was not found.");
        return group;
    }

    /// <summary>
    /// The documented <c>PlcTagTableComposition.Import(FileInfo, ImportOptions)</c>, bound by its
    /// parameter list because the references do not state its return type (nothing here uses it).
    /// </summary>
    internal static Action<FileInfo> BindImport(object composition) => BindImport(composition, ImportOptions.None, out _);

    /// <summary>The same binding with the caller's options; the returned value (type undocumented) is passed to <paramref name="returned"/> per call.</summary>
    internal static Action<FileInfo> BindImport(object composition, ImportOptions options, out Func<object?> returned)
    {
        object? last = null;
        returned = () => last;
        var import = composition.GetType().GetMethod("Import", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(FileInfo), typeof(ImportOptions) }, null)
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "PlcTagTableComposition.Import(FileInfo, ImportOptions) is not available in this TIA Portal version; nothing was changed.");
        return file =>
        {
            try
            {
                last = import.Invoke(composition, new object[] { file, options });
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
            }
        };
    }

    private static string Require(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{field} is required.")
            : value!;
}
