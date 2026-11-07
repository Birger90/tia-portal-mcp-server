using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.WatchAndForceTables;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// Watch and force tables under <c>PlcSoftware.WatchAndForceTableGroup</c>, and update_program.
/// Groups and tables are matched by exact enumeration (case-insensitive, unique), never <c>Find</c>.
/// Entries are read schema-free: the <c>Entries</c> property by reflection and each entry's documented
/// attributes through <c>IEngineeringObject.GetAttribute</c>, because the V21 reference documents no
/// entry composition type, no entry Create and no entry Delete. Entries are therefore added, changed
/// and removed only through the documented SimaticML round trip (<c>Export</c>, caller edit,
/// <c>Import(..., ImportOptions.Override)</c>) guarded by the content hash. Force tables are only read
/// and offline-edited: nothing here forces values online.
/// </summary>
public static class PlcWatchTableService
{
    private const string ContentFormat = SourceFormatNames.Xml;

    public static PlcWatchTableInventoryInfo List(ProjectBase project, string? plcName)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, plcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " No watch or force tables were listed.");
        if (plcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");

        var inventory = new PlcWatchTableInventoryInfo();
        foreach (var plc in plcs)
        {
            var info = new PlcWatchTablePlcInfo { PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            Walk(plc.Software.WatchAndForceTableGroup, "/", info);
            inventory.Plcs.Add(info);
        }

        return inventory;
    }

    public static PlcWatchTableReadInfo ReadWatchTable(ProjectBase project, string? plcName, string? tableName, string? groupPath, int maxEntries)
    {
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = WatchTable(group, path, tableName);
        var info = new PlcWatchTableReadInfo
        {
            PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty, GroupPath = path,
            Name = table.Name ?? string.Empty, IsConsistent = Consistent(() => table.IsConsistent),
        };
        var (entries, complete) = ReadEntries(table, maxEntries, (index, entry) => PlcWatchTablePolicy.WatchEntry(index, name => Attribute(entry, name)));
        info.Entries = entries;
        info.EntryCount = entries.Count;
        info.EntriesComplete = complete;
        return info;
    }

    public static PlcForceTableReadInfo ReadForceTable(ProjectBase project, string? plcName, string? tableName, string? groupPath, int maxEntries)
    {
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = ForceTable(group, path, tableName);
        var info = new PlcForceTableReadInfo
        {
            PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty, GroupPath = path,
            Name = table.Name ?? string.Empty, IsConsistent = Consistent(() => table.IsConsistent),
        };
        var (entries, complete) = ReadEntries(table, maxEntries, (index, entry) => PlcWatchTablePolicy.ForceEntry(index, name => Attribute(entry, name)));
        info.Entries = entries;
        info.EntryCount = entries.Count;
        info.EntriesComplete = complete;
        return info;
    }

    /// <summary>SimaticML of one watch table (<c>Export(FileInfo, ExportOptions.None)</c>), DocumentInfo removed like block exports.</summary>
    public static string ExportWatchTable(ProjectBase project, string? plcName, string? tableName, string? groupPath)
    {
        var (_, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = WatchTable(group, path, tableName);
        return Export(table.Name, file => table.Export(file, ExportOptions.None));
    }

    public static string ExportForceTable(ProjectBase project, string? plcName, string? tableName, string? groupPath)
    {
        var (_, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = ForceTable(group, path, tableName);
        return Export(table.Name, file => table.Export(file, ExportOptions.None));
    }

    public static PlcWatchTableMutationResultInfo CreateWatchTable(ProjectBase project, string? plcName, string? tableName, string? groupPath)
    {
        RequireName(tableName, "tableName");
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var created = group.WatchTables.Create(tableName);
        return Result(PlcOnlineMethods.CreateWatchTable, plc, path, created?.Name ?? tableName);
    }

    public static PlcWatchTableMutationResultInfo DeleteWatchTable(ProjectBase project, string? plcName, string? tableName, string? groupPath)
    {
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = WatchTable(group, path, tableName);
        var name = table.Name;
        table.Delete();
        return Result(PlcOnlineMethods.DeleteWatchTable, plc, path, name);
    }

    public static PlcWatchTableMutationResultInfo CreateGroup(ProjectBase project, string? plcName, string? groupPath)
    {
        var segments = PlcWatchTablePolicy.Segments(groupPath);
        if (segments.Count == 0) throw Invalid("folderPath must name the new group, for example /Line/Cell.");
        var parentPath = "/" + string.Join("/", segments.Take(segments.Count - 1));
        var (plc, parent, _) = ResolveGroup(project, plcName, parentPath);
        parent.Groups.Create(segments[segments.Count - 1]);
        return Result(PlcOnlineMethods.CreateWatchTableGroup, plc, PlcWatchTablePolicy.NormalizeGroupPath(groupPath), null);
    }

    public static PlcWatchTableMutationResultInfo DeleteGroup(ProjectBase project, string? plcName, string? groupPath)
    {
        if (PlcWatchTablePolicy.Segments(groupPath).Count == 0) throw Invalid("The system group '/' cannot be deleted; name a user group.");
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var user = group as PlcWatchAndForceTableUserGroup ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetKindUnsupported, $"Watch-and-force table group '{path}' is not a user group.");
        user.Delete();
        return Result(PlcOnlineMethods.DeleteWatchTableGroup, plc, path, null);
    }

    public static PlcWatchTableMutationResultInfo UpdateWatchTableContent(ProjectBase project, string? plcName, string? tableName,
        string? groupPath, string? content, string? expectedContentHash)
    {
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = WatchTable(group, path, tableName);
        var name = table.Name;
        var exported = Export(name, file => table.Export(file, ExportOptions.None));
        PlcWritePreconditions.RequireContentHash(expectedContentHash, ContentFormat, exported);
        RequireDocumentNames(exported, content, name);
        var imported = Import(name, content, file => group.WatchTables.Import(file, ImportOptions.Override).Select(t => t.Name));
        return Imported(PlcOnlineMethods.UpdateWatchTableContent, plc, path, name, imported);
    }

    public static PlcWatchTableMutationResultInfo UpdateForceTableContent(ProjectBase project, string? plcName, string? tableName,
        string? groupPath, string? content, string? expectedContentHash)
    {
        var (plc, group, path) = ResolveGroup(project, plcName, groupPath);
        var table = ForceTable(group, path, tableName);
        var name = table.Name;
        var exported = Export(name, file => table.Export(file, ExportOptions.None));
        PlcWritePreconditions.RequireContentHash(expectedContentHash, ContentFormat, exported);
        RequireDocumentNames(exported, content, name);
        var imported = Import(name, content, file => group.ForceTables.Import(file, ImportOptions.Override).Select(t => t.Name));
        return Imported(PlcOnlineMethods.UpdateForceTableContent, plc, path, name, imported);
    }

    /// <summary>
    /// <c>PlcSoftware.UpdateProgram()</c>: the V21 reference documents the name and the empty parameter
    /// list but not the return type, so it is invoked by exact runtime lookup instead of being compiled in.
    /// Source (a) runs it "within the normal ExclusiveAccess/transaction workflow", so it runs inside
    /// <c>TiaPortal.ExclusiveAccess</c> and a project <c>Transaction</c> that is committed only when the
    /// returned object does not indicate failure (<see cref="PlcUpdateProgramOutcome"/>): a failing state,
    /// error count or success member fails the call and leaves the transaction uncommitted (TIA Portal rolls
    /// an uncommitted transaction back); nothing interpretable is reported as <c>uninterpreted</c> with a warning. The commit
    /// is requested only when <c>Transaction.CanCommit</c> is true (audit T5, like <c>PlcRoundTrip</c>); otherwise the call fails
    /// and says the change was rolled back. Not live-tested.
    /// </summary>
    public static PlcWatchTableMutationResultInfo UpdateProgram(ProjectBase project, string? plcName, TiaPortal? portal)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, plcName);
        var method = plc.Software.GetType().GetMethod(PlcOnlineMethods.UpdateProgramMemberName, BindingFlags.Public | BindingFlags.Instance,
            null, Type.EmptyTypes, null) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"PlcSoftware.{PlcOnlineMethods.UpdateProgramMemberName}() was not found on PLC '{plc.Software.Name}' in this TIA Portal installation. Nothing was changed.");
        if (portal is null)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "update_program needs the attached TIA Portal for its exclusive access and transaction. Nothing was changed.");
        var result = Result(PlcOnlineMethods.UpdateProgram, plc, "/", null);
        using (var access = portal.ExclusiveAccess("plc_write update_program"))
        using (var transaction = access.Transaction(project, "update_program"))
        {
            object? returned;
            try
            {
                returned = method.Invoke(plc.Software, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            var interpretation = PlcUpdateProgramOutcome.Interpret(returned, PlcOnlineService.IsFatal);
            if (interpretation.Outcome == PlcUpdateProgramOutcome.Failed)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"PlcSoftware.UpdateProgram() on PLC '{plc.Software.Name}' reported failure: {interpretation.Reason} The transaction was not committed, "
                    + "so TIA Portal rolls the change back; compile the PLC and inspect it before retrying.");
            // Audit T5/K88: PlcUpdateProgramOutcome swallows getter exceptions of the returned object inside this transaction; TIA Portal
            // may then refuse the commit. Commit only when CanCommit, otherwise report the rollback instead of claiming success.
            if (!transaction.CanCommit)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"PlcSoftware.UpdateProgram() on PLC '{plc.Software.Name}' returned ({interpretation.Outcome}), but TIA Portal reports that the transaction "
                    + "cannot be committed (Transaction.CanCommit false), so it was not committed and TIA Portal rolls the change back. "
                    + "Compile the PLC and inspect it before retrying.");
            transaction.CommitOnDispose();
            result.ReturnedType = returned?.GetType().FullName;
            result.Outcome = interpretation.Outcome;
            result.ReturnedMembers = interpretation.Members;
            if (interpretation.Outcome == PlcUpdateProgramOutcome.Uninterpreted)
                Console.Error.WriteLine($"update_program: {interpretation.Reason} The call raised no error and was committed, but its effect is not "
                    + "verified: compile the PLC and inspect the blocks before saving.");
        }

        return result;
    }

    private static void Walk(PlcWatchAndForceTableGroup group, string path, PlcWatchTablePlcInfo info)
    {
        foreach (PlcWatchTable table in group.WatchTables)
        {
            if (!Room(info)) return;
            info.WatchTables.Add(new PlcWatchTableSummaryInfo { GroupPath = path, Name = table.Name ?? string.Empty, IsConsistent = Consistent(() => table.IsConsistent) });
        }

        foreach (PlcForceTable table in group.ForceTables)
        {
            if (!Room(info)) return;
            info.ForceTables.Add(new PlcWatchTableSummaryInfo { GroupPath = path, Name = table.Name ?? string.Empty, IsConsistent = Consistent(() => table.IsConsistent) });
        }

        foreach (PlcWatchAndForceTableUserGroup child in group.Groups)
        {
            if (!Room(info)) return;
            var childPath = path == "/" ? "/" + child.Name : path + "/" + child.Name;
            info.Groups.Add(childPath);
            Walk(child, childPath, info);
        }
    }

    private static bool Room(PlcWatchTablePlcInfo info)
    {
        if (info.WatchTables.Count + info.ForceTables.Count + info.Groups.Count < PlcWatchTablePolicy.MaxTables) return true;
        info.Complete = false;
        return false;
    }

    internal static (PlcSoftwareLocator.DiscoveredPlcSoftware Plc, PlcWatchAndForceTableGroup Group, string Path) ResolveGroup(ProjectBase project, string? plcName, string? groupPath)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, plcName);
        PlcWatchAndForceTableGroup current = plc.Software.WatchAndForceTableGroup;
        var path = "/";
        foreach (var segment in PlcWatchTablePolicy.Segments(groupPath))
        {
            var matches = current.Groups.Cast<PlcWatchAndForceTableUserGroup>()
                .Where(g => string.Equals(g.Name, segment, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            var next = path == "/" ? "/" + segment : path + "/" + segment;
            if (matches.Count == 0)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"Watch-and-force table group '{next}' was not found in PLC '{plc.Software.Name}'.");
            if (matches.Count > 1)
                throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                    $"Watch-and-force table group '{next}' is ambiguous in PLC '{plc.Software.Name}'.");
            current = matches[0];
            path = path == "/" ? "/" + matches[0].Name : path + "/" + matches[0].Name;
        }

        return (plc, current, path);
    }

    internal static PlcWatchTable WatchTable(PlcWatchAndForceTableGroup group, string path, string? name)
        => Single(group.WatchTables.Cast<PlcWatchTable>().Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)),
            $"Watch table '{name}' in group '{path}'", name);

    internal static PlcForceTable ForceTable(PlcWatchAndForceTableGroup group, string path, string? name)
        => Single(group.ForceTables.Cast<PlcForceTable>().Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)),
            $"Force table '{name}' in group '{path}'", name);

    private static T Single<T>(IEnumerable<T> matches, string what, string? name)
    {
        RequireName(name, "tableName");
        var found = matches.Take(2).ToList();
        return found.Count switch
        {
            1 => found[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{what} was not found."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{what} is ambiguous."),
        };
    }

    private static (List<TEntry> Entries, bool Complete) ReadEntries<TEntry>(object table, int maxEntries, Func<int, IEngineeringObject, TEntry> project)
    {
        if (maxEntries < 1 || maxEntries > PlcWatchTablePolicy.MaxEntries)
            throw Invalid($"maxElements must be between 1 and {PlcWatchTablePolicy.MaxEntries}.");
        var entries = PlcWatchTablePolicy.Entries(table) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"The table type '{table.GetType().FullName}' exposes no enumerable '{PlcWatchTablePolicy.EntriesPropertyName}' property in this TIA Portal installation.");
        var list = new List<TEntry>();
        foreach (var entry in entries)
        {
            if (list.Count >= maxEntries) return (list, false);
            if (entry is not IEngineeringObject engineering)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"A table entry of type '{entry?.GetType().FullName}' is not an engineering object; its attributes cannot be read.");
            list.Add(project(list.Count, engineering));
        }

        return (list, true);
    }

    private static (bool Ok, object? Value) Attribute(IEngineeringObject entry, string name)
    {
        try
        {
            return (true, entry.GetAttribute(name));
        }
        catch (EngineeringException ex) when (ex is not EngineeringObjectDisposedException)
        {
            return (false, null);
        }
    }

    private static bool? Consistent(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (EngineeringException ex) when (ex is not EngineeringObjectDisposedException)
        {
            return null;
        }
    }

    private static string Export(string? name, Action<FileInfo> export)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-watch-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "table.xml");
            export(new FileInfo(path));
            if (!File.Exists(path))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no export file for table '{name}'.");
            return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(path));
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }
    }

    private static List<string> Import(string? name, string? content, Func<FileInfo, IEnumerable<string>> import)
    {
        if (string.IsNullOrEmpty(content)) throw Invalid("content is required.");
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-watch-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "table.xml");
            File.WriteAllText(path, content, Encoding.UTF8);
            return import(new FileInfo(path)).Select(n => n ?? string.Empty).ToList();
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }
    }

    /// <summary>Refuses (validation_error, nothing imported) a document that does not name exactly the addressed table.</summary>
    private static void RequireDocumentNames(string exported, string? content, string name)
    {
        if (PlcWatchTablePolicy.CheckImportDocument(exported, content, name) is { } refusal)
            throw Invalid($"The import was refused before anything changed: {refusal}. Edit the SimaticML from get_*_table_content of the same table.");
    }

    /// <summary>The import must have replaced exactly the addressed table; anything else is reported as a postcondition failure.</summary>
    private static PlcWatchTableMutationResultInfo Imported(string operation, PlcSoftwareLocator.DiscoveredPlcSoftware plc, string path, string? name, List<string> imported)
    {
        var result = Result(operation, plc, path, name);
        result.ImportedTables = imported;
        if (imported.Count != 1 || !string.Equals(imported[0], name, StringComparison.OrdinalIgnoreCase))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"The import into group '{path}' returned [{string.Join(", ", imported.Select(n => $"'{n}'"))}] instead of exactly table '{name}'. "
                + "The project was changed; inspect the group with list_watch_and_force_tables before retrying.");
        return result;
    }

    private static PlcWatchTableMutationResultInfo Result(string operation, PlcSoftwareLocator.DiscoveredPlcSoftware plc, string path, string? tableName) => new()
    {
        Operation = operation,
        PlcName = plc.Software.Name ?? string.Empty,
        GroupPath = path,
        TableName = tableName,
    };

    private static void RequireName(string? name, string field)
    {
        if (string.IsNullOrWhiteSpace(name)) throw Invalid($"{field} is required.");
    }

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}
