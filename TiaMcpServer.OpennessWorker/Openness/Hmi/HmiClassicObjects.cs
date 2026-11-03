using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Classic WinCC objects (Comfort/Advanced panels, <c>Siemens.Engineering.Hmi.HmiTarget</c>; issue #28): list, SimaticML
/// export and import, delete, user folders. The worker has no compile reference to <c>Siemens.Engineering.WinCC.dll</c>;
/// every member below is documented in the V21 <c>Siemens.Engineering.WinCC.xml</c> and bound at runtime by exact name and
/// argument types on the live object (<see cref="HmiClassicKinds"/> names the properties): the system folders
/// (<c>HmiTarget.TagFolder</c>, <c>ScreenFolder</c>, <c>ScreenTemplateFolder</c>, <c>ScreenPopupFolder</c>,
/// <c>ScreenSlideinFolder</c>, <c>VBScriptFolder</c>), the compositions on the target (<c>TextLists</c>,
/// <c>GraphicLists</c>, <c>Connections</c>, <c>Cycles</c>), <c>Folders</c> / <c>Create(String)</c> of user folders,
/// <c>Export(FileInfo, ExportOptions)</c>, the compositions' <c>Import(FileInfo, ImportOptions)</c>, <c>Delete()</c>,
/// <c>HmiTarget.ImportScreenGlobalElements</c>/<c>ImportScreenOverview</c>. A missing member fails
/// <c>api_member_missing</c> before anything is written or changed.
/// V21 pitfalls (V21_PITFALLS.md): a graphic list is never exported (pitfall 3: "skipped: reported to crash TIA Portal
/// V21"); a screen import whose screen number another screen already uses, or which repeats a number, is refused before
/// <c>Import</c> (pitfall 4: reported to terminate TIA Portal). The existing numbers are read with the generic
/// <c>GetAttribute("Number")</c> and, when that is refused, from a private export of the screen.
/// </summary>
public static class HmiClassicObjects
{
    private const string FoldersProperty = "Folders";
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private sealed class Entry
    {
        public Entry(object item, string name, string? folderPath, string? tableName, object container)
        {
            Item = item;
            Name = name;
            FolderPath = folderPath;
            TableName = tableName;
            Container = container;
        }

        public object Item { get; }
        public string Name { get; }
        public string? FolderPath { get; }
        public string? TableName { get; }

        /// <summary>The composition holding the item (re-enumerated for a delete read-back).</summary>
        public object Container { get; }
    }

    // ---- read ---------------------------------------------------------------------------------------

    public static HmiClassicObjectListInfo List(ProjectBase project, HmiQueryInfo query)
    {
        var kind = KindOf(query.ClassicKind);
        var (device, softwareName, target) = HmiSoftwareLocator.FindUniqueClassic(project, query.HmiName);
        var info = new HmiClassicObjectListInfo
        {
            HmiName = softwareName, DeviceName = device, Kind = kind.Name, GroupPath = query.GroupPath, TableName = query.TableName,
        };
        if (kind.IsSingleton)
        {
            if (Value(target, kind.ItemsProperty, required: true) is { } single)
                info.Objects.Add(new HmiClassicObjectInfo { Name = kind.Name, TypeName = single.GetType().Name });
            return info;
        }

        var entries = Entries(target, kind, query.GroupPath, query.TableName, info.Messages, info.Folders);
        info.Objects = entries
            .OrderBy(e => e.TableName ?? e.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new HmiClassicObjectInfo
            {
                Name = e.Name, FolderPath = e.FolderPath, TableName = e.TableName,
                IsSystemObject = SystemObject(e, info.Messages), TypeName = e.Item.GetType().Name,
            }).ToList();
        info.Folders = info.Folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        info.IsComplete = info.Messages.Count == 0;
        return info;
    }

    // ---- writes -------------------------------------------------------------------------------------

    public static HmiWriteOutcomeInfo Write(ProjectBase project, string method, HmiWriteRequestInfo write)
    {
        var kind = KindOf(write.ClassicKind);
        var (_, softwareName, target) = HmiSoftwareLocator.FindUniqueClassic(project, write.HmiName);
        return method switch
        {
            HmiClassicMethods.ExportObjects => Export(target, softwareName, kind, write),
            HmiClassicMethods.ImportObjects => Import(target, softwareName, kind, write),
            HmiClassicMethods.DeleteObject => Delete(target, kind, write),
            HmiClassicMethods.CreateFolder => CreateFolder(target, kind, write),
            HmiClassicMethods.DeleteFolder => DeleteFolder(target, kind, write),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported classic HMI worker method '{method}'."),
        };
    }

    internal static HmiWriteOutcomeInfo Export(object target, string softwareName, HmiClassicKind kind, HmiWriteRequestInfo write)
    {
        if (!kind.CanExport)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, HmiClassicKinds.GraphicListExportSkipped);
        var directory = LocalPath(write.DirectoryPath, "directoryPath");
        if (!Directory.Exists(directory))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory '{directory}' does not exist. Nothing was written.");

        List<(object Item, string Name)> selected;
        if (kind.IsSingleton)
        {
            var single = Value(target, kind.ItemsProperty, required: true)
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"HMI '{softwareName}' has no {kind.Name}. Nothing was written.");
            selected = new List<(object, string)> { (single, kind.Name) };
        }
        else
        {
            var entries = Entries(target, kind, write.GroupPath, write.TableName, messages: null, folders: null);
            selected = write.ObjectNames is { Count: > 0 } names
                ? names.Select(name => { var e = Single(entries, name, kind); return (e.Item, e.Name); }).ToList()
                : entries.Select(e => (e.Item, e.Name)).ToList();
            if (selected.Count == 0)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"HMI '{softwareName}' has no {kind.Name} in the selected scope. Nothing was written.");
        }

        var targets = selected.Select(s => (s.Item, s.Name, Path: Target(directory, s.Name, kind))).ToList();
        if (targets.GroupBy(t => t.Path, Names).FirstOrDefault(g => g.Count() > 1) is { } clash)
            throw Invalid($"Several {kind.Name} would be written to '{clash.Key}'; export them by objectNames in separate calls. Nothing was written.");
        foreach (var (_, _, path) in targets)
        {
            if (Directory.Exists(path)) throw Invalid($"'{path}' is a directory, not a file. Nothing was written.");
            if (File.Exists(path) && write.Overwrite != true) throw Invalid($"'{path}' already exists; set overwrite:true to replace it. Nothing was written.");
        }

        // Bound on every object before the first export, so a missing member writes nothing.
        var bound = targets.Select(t => (t.Item, t.Name, t.Path, Method: Bind(t.Item, "Export", new[] { typeof(FileInfo), typeof(ExportOptions) }))).ToList();
        var outcome = new HmiWriteOutcomeInfo { ObjectName = softwareName };
        var warnings = new List<string>();
        var temporary = TemporaryDirectory("export");
        try
        {
            foreach (var (item, name, path, method) in bound)
            {
                var bytes = ExportBytes(method, item, temporary, $"{kind.Name} '{name}'", outcome);
                if (TechnologyObjectFiles.Place(bytes, path, write.Overwrite == true, $"The export of {kind.Name} '{name}'", warnings) is { } failure)
                    throw new WorkerOperationException(failure.Category, $"{failure.Message} Files already written: {Written(outcome)}.");
                outcome.AppliedProperties.Add($"{kind.Name} {name} -> {path} bytes={bytes.LongLength} sha256={TechnologyObjectFiles.Sha256(bytes)}");
            }
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        outcome.AppliedProperties.AddRange(warnings.Select(w => "warning " + w));
        return outcome;
    }

    internal static HmiWriteOutcomeInfo Import(object target, string softwareName, HmiClassicKind kind, HmiWriteRequestInfo write)
    {
        var source = LocalPath(write.FilePath, "filePath");
        if (!File.Exists(source)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{source}' does not exist. Nothing was imported.");
        var options = write.Overwrite == true ? ImportOptions.Override : ImportOptions.None;
        object owner;
        MethodInfo method;
        if (kind.IsSingleton)
        {
            owner = target;
            method = Bind(target, kind.SingletonImport!, new[] { typeof(FileInfo), typeof(ImportOptions) });
        }
        else
        {
            owner = Composition(target, kind, write.GroupPath, write.TableName);
            method = Bind(owner, "Import", new[] { typeof(FileInfo), typeof(ImportOptions) });
        }

        var before = kind.IsSingleton ? new List<string>() : ItemNames(owner, kind);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = softwareName };
        var temporary = TemporaryDirectory("import");
        try
        {
            var copy = Path.Combine(temporary, "import.xml");
            File.Copy(source, copy);
            var bytes = File.ReadAllBytes(copy);
            outcome.AppliedProperties.Add($"import {source} bytes={bytes.LongLength} sha256={TechnologyObjectFiles.Sha256(bytes)} options={options}");
            if (HmiClassicKinds.IsNumberedScreen(kind.Name)) CheckScreenNumbers(target, copy, outcome, temporary);
            Invoke(method, owner, new FileInfo(copy), options);
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        if (kind.IsSingleton)
        {
            outcome.AppliedProperties.Add($"(the {kind.Name} were imported; Openness offers no read of their content, so the import is not read back)");
            return outcome;
        }

        var created = ItemNames(owner, kind).Where(name => !before.Contains(name, Names)).ToList();
        if (created.Count == 0 && write.Overwrite != true)
            throw HmiWriteRun.Failure(new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"The import reported no error but no new {kind.Name} object appeared in HMI '{softwareName}'. Inspect it with hmi_read list_classic_objects."), outcome);
        outcome.AppliedProperties.AddRange(created.Select(name => $"{kind.Name} {name}"));
        if (created.Count == 0) outcome.AppliedProperties.Add($"(no new {kind.Name} object: with overwrite an existing one was replaced; which one is not reported by TIA Portal)");
        return outcome;
    }

    internal static HmiWriteOutcomeInfo Delete(object target, HmiClassicKind kind, HmiWriteRequestInfo write)
    {
        if (!kind.CanDelete)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"V21 Openness documents no Delete() for classic {kind.Name}; nothing was changed.");
        var name = Required(write.ObjectName, "objectName");
        var entry = Single(Entries(target, kind, write.GroupPath, write.TableName, messages: null, folders: null), name, kind);
        var delete = Bind(entry.Item, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = entry.Name };
        try
        {
            HmiWriteRun.Set(outcome, "Delete", () => Invoke(delete, entry.Item));
            if (ItemNames(entry.Container, kind).Contains(entry.Name, Names))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{kind.Name} '{entry.Name}' is still present after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
    }

    internal static HmiWriteOutcomeInfo CreateFolder(object target, HmiClassicKind kind, HmiWriteRequestInfo write)
    {
        RequireFolders(kind);
        var path = Segments(Required(write.GroupPath, "groupPath"));
        var name = path[path.Count - 1];
        var parent = FolderAt(Root(target, kind), path.Take(path.Count - 1).ToList());
        var folders = Value(parent, FoldersProperty, required: true)
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The {kind.Name} folder has no Folders composition.");
        var create = Bind(folders, "Create", new[] { typeof(string) });
        var outcome = new HmiWriteOutcomeInfo { ObjectName = string.Join("/", path) };
        try
        {
            HmiWriteRun.Set(outcome, "Create", () => Invoke(create, folders, name));
            if (!Children(parent).Any(f => Names.Equals(NameOf(f, null), name)))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The folder '{name}' does not appear after Create(\"{name}\").");
            outcome.AppliedProperties.Add("readBack present");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
    }

    internal static HmiWriteOutcomeInfo DeleteFolder(object target, HmiClassicKind kind, HmiWriteRequestInfo write)
    {
        RequireFolders(kind);
        var path = Segments(Required(write.GroupPath, "groupPath"));
        var parent = FolderAt(Root(target, kind), path.Take(path.Count - 1).ToList());
        var folder = FolderAt(parent, new List<string> { path[path.Count - 1] });
        var delete = Bind(folder, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = string.Join("/", path) };
        try
        {
            HmiWriteRun.Set(outcome, "Delete", () => Invoke(delete, folder));
            if (Children(parent).Any(f => Names.Equals(NameOf(f, null), path[path.Count - 1])))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The folder '{string.Join("/", path)}' is still present after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
    }

    // ---- V21 pitfall 4 ------------------------------------------------------------------------------

    /// <summary>Refuses an import whose screen numbers repeat or collide with an existing screen of another name.</summary>
    private static void CheckScreenNumbers(object target, string file, HmiWriteOutcomeInfo outcome, string temporary)
    {
        List<(string Name, int Number)> incoming;
        try
        {
            incoming = HmiClassicScreenNumbers.Read(XDocument.Load(file));
        }
        catch (System.Xml.XmlException exception)
        {
            throw Invalid($"'{file}' is not an XML document ({exception.Message}). Nothing was imported.");
        }

        if (incoming.Count == 0) return;
        var existing = new List<(string Name, int Number)>();
        var unknown = new List<string>();
        foreach (var entry in Entries(target, HmiClassicKinds.Find(HmiClassicKinds.Screens)!, null, null, messages: null, folders: null))
        {
            if (ScreenNumber(entry, temporary) is { } number) existing.Add((entry.Name, number));
            else unknown.Add(entry.Name);
        }

        var conflicts = HmiClassicScreenNumbers.Conflicts(incoming, existing);
        if (conflicts.Count > 0)
            throw Invalid("The import is refused: " + string.Join("; ", conflicts)
                + ". A classic screen import with a duplicate screen number is reported to terminate TIA Portal (V21 pitfall 4). Change the <Number> in the file. Nothing was imported.");
        outcome.AppliedProperties.Add($"screenNumbers checked ({incoming.Count} in the file, {existing.Count} existing)");
        if (unknown.Count > 0)
            outcome.AppliedProperties.Add($"warning the screen number of {string.Join(", ", unknown.Select(n => $"'{n}'"))} could not be read; a duplicate with it is not excluded (V21 pitfall 4)");
    }

    private static int? ScreenNumber(Entry entry, string temporary)
    {
        if (entry.Item is IEngineeringObject engineering)
        {
            try
            {
                var value = engineering.GetAttribute("Number");
                if (value is not null && int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var number)) return number;
            }
            catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
            {
                // Fall back to the documented export below.
            }
        }

        var export = LibraryRuntimeBinding.Find(entry.Item.GetType(), "Export", new[] { typeof(FileInfo), typeof(ExportOptions) }, out _);
        if (export is null) return null;
        try
        {
            var bytes = ExportBytes(export, entry.Item, temporary, $"screen '{entry.Name}'", null);
            using var stream = new MemoryStream(bytes);
            return HmiClassicScreenNumbers.Read(XDocument.Load(stream)).Where(s => Names.Equals(s.Name, entry.Name)).Select(s => (int?)s.Number).FirstOrDefault();
        }
        catch (Exception exception) when (exception is EngineeringException or WorkerOperationException or System.Xml.XmlException or IOException)
        {
            return null;
        }
    }

    // ---- traversal ----------------------------------------------------------------------------------

    private static List<Entry> Entries(object target, HmiClassicKind kind, string? groupPath, string? tableName, List<string>? messages, List<string>? folders)
    {
        var result = new List<Entry>();
        if (kind.NeedsTable)
        {
            var tables = Entries(target, HmiClassicKinds.Find(HmiClassicKinds.TagTables)!, groupPath, null, messages, folders);
            if (tableName is not null)
            {
                tables = tables.Where(t => Names.Equals(t.Name, tableName)).ToList();
                if (tables.Count == 0)
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No classic tag table '{tableName}' was found{(groupPath is null ? string.Empty : $" in folder '{groupPath}'")}.");
            }

            foreach (var table in tables)
            {
                var tags = Value(table.Item, kind.ItemsProperty, required: true)!;
                foreach (var tag in Items(tags))
                    if (NameOf(tag, kind.KeyProperty, messages) is { } name) result.Add(new Entry(tag, name, table.FolderPath, table.Name, tags));
            }

            return result;
        }

        var root = Root(target, kind);
        var start = groupPath is null ? root : FolderAt(root, Segments(groupPath));
        Walk(start, kind, groupPath, result, messages, folders, depth: 0);
        return result;
    }

    private static void Walk(object folder, HmiClassicKind kind, string? path, List<Entry> result, List<string>? messages, List<string>? folders, int depth)
    {
        var items = Value(folder, kind.ItemsProperty, required: true)!;
        foreach (var item in Items(items))
            if (NameOf(item, kind.KeyProperty, messages) is { } name) result.Add(new Entry(item, name, path, null, items));
        if (!kind.HasUserFolders || depth >= 32) return;
        foreach (var child in Children(folder))
        {
            if (NameOf(child, null, messages) is not { } name) continue;
            var childPath = path is null ? name : path + "/" + name;
            folders?.Add(childPath);
            Walk(child, kind, childPath, result, messages, folders, depth + 1);
        }
    }

    private static object Root(object target, HmiClassicKind kind)
        => kind.RootProperty is null ? target
            : Value(target, kind.RootProperty, required: true)
              ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"HmiTarget.{kind.RootProperty} returned no folder.");

    /// <summary>The composition an import of <paramref name="kind"/> goes into: the tag table's Tags, the folder's composition, or the target's.</summary>
    private static object Composition(object target, HmiClassicKind kind, string? groupPath, string? tableName)
    {
        if (kind.NeedsTable)
        {
            var table = Single(Entries(target, HmiClassicKinds.Find(HmiClassicKinds.TagTables)!, groupPath, null, null, null), Required(tableName, "tableName"),
                HmiClassicKinds.Find(HmiClassicKinds.TagTables)!);
            return Value(table.Item, kind.ItemsProperty, required: true)!;
        }

        var folder = groupPath is null ? Root(target, kind) : FolderAt(Root(target, kind), Segments(groupPath));
        return Value(folder, kind.ItemsProperty, required: true)!;
    }

    private static object FolderAt(object root, IReadOnlyList<string> segments)
    {
        var current = root;
        var walked = new List<string>();
        foreach (var segment in segments)
        {
            walked.Add(segment);
            var matches = Children(current).Where(f => Names.Equals(NameOf(f, null), segment)).ToList();
            current = matches.Count switch
            {
                1 => matches[0],
                0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The folder '{string.Join("/", walked)}' was not found."),
                _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{matches.Count} folders match '{string.Join("/", walked)}'."),
            };
        }

        return current;
    }

    private static IEnumerable<object> Children(object folder)
        => Value(folder, FoldersProperty, required: false) is { } folders ? Items(folders) : Enumerable.Empty<object>();

    private static List<string> ItemNames(object composition, HmiClassicKind kind)
        => Items(composition).Select(item => NameOf(item, kind.KeyProperty)).OfType<string>().ToList();

    private static IEnumerable<object> Items(object composition)
    {
        if (composition is not IEnumerable enumerable)
            throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, $"{composition.GetType().FullName} is not an enumerable composition. Nothing was changed.");
        var list = new List<object>();
        try
        {
            foreach (var item in enumerable)
                if (item is not null) list.Add(item);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The composition {composition.GetType().Name} could not be read: {exception.Message}");
        }

        return list;
    }

    private static string? NameOf(object item, string? keyProperty, List<string>? messages = null)
    {
        try
        {
            return Value(item, keyProperty ?? "Name", required: true)?.ToString();
        }
        catch (EngineeringException exception) when (messages is not null && OpennessExceptions.IsRecoverable(exception))
        {
            messages.Add($"The {keyProperty ?? "Name"} of a {item.GetType().Name} could not be read and it is left out: {exception.Message}");
            return null;
        }
    }

    private static bool? SystemObject(Entry entry, List<string> messages)
    {
        var property = LibraryRuntimeBinding.Property(entry.Item.GetType(), "IsSystemObject", out _);
        if (property is null || property.PropertyType != typeof(bool)) return null;
        try
        {
            return (bool)Value(entry.Item, "IsSystemObject", required: true)!;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            messages.Add($"IsSystemObject of '{entry.Name}' could not be read: {exception.Message}");
            return null;
        }
    }

    private static Entry Single(IReadOnlyList<Entry> entries, string name, HmiClassicKind kind)
    {
        var matches = entries.Where(e => Names.Equals(e.Name, name)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No classic {kind.Name} object '{name}' was found in the selected scope. Use hmi_read list_classic_objects."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"{matches.Count} classic {kind.Name} objects are named '{name}' ({string.Join(", ", matches.Select(m => m.TableName ?? m.FolderPath ?? "(top level)"))}); narrow with groupPath or tableName."),
        };
    }

    // ---- members, files -------------------------------------------------------------------------------

    private static HmiClassicKind KindOf(string? name)
        => HmiClassicKinds.Find(name)
           ?? throw Invalid($"'classicKind' must be one of: {string.Join(", ", HmiClassicKinds.Names)}.");

    private static void RequireFolders(HmiClassicKind kind)
    {
        if (!kind.HasUserFolders)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"Classic {kind.Name} have no user folders in V21 Openness; folders exist for {string.Join(", ", HmiClassicKinds.All.Where(k => k.HasUserFolders).Select(k => k.Name))}. Nothing was changed.");
    }

    private static List<string> Segments(string path)
    {
        var segments = path.Split(new[] { '/' }, StringSplitOptions.None).ToList();
        if (segments.Count == 0 || segments.Any(string.IsNullOrWhiteSpace)) throw Invalid("'groupPath' must be '/'-separated folder names with no empty segment.");
        return segments;
    }

    private static byte[] ExportBytes(MethodInfo export, object item, string temporary, string what, HmiWriteOutcomeInfo? outcome)
    {
        var file = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".xml");
        Invoke(export, item, new FileInfo(file), ExportOptions.None);
        if (!File.Exists(file))
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"TIA Portal reported no error but produced no export file for {what}." + (outcome is null ? string.Empty : $" Files already written: {Written(outcome)}."));
        var bytes = File.ReadAllBytes(file);
        File.Delete(file);
        return bytes;
    }

    /// <summary>The file of one object: its name as the file name, refused when it is not a valid file name segment.</summary>
    private static string Target(string directory, string name, HmiClassicKind kind)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0
            || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
            throw Invalid($"The {kind.Name} object '{name}' has a name that is not a valid file name; export the others by objectNames. Nothing was written.");
        return Path.Combine(directory, name + ".xml");
    }

    private static string TemporaryDirectory(string purpose)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tia-mcp-hmi-classic-{purpose}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Offline tests on a non-Windows host only: accept a rooted path of that host, which the Windows path rules refuse.
    /// Never set by the worker.
    /// </summary>
    internal static bool AcceptHostRootedPathsForTests { get; set; }

    private static string LocalPath(string? path, string field)
    {
        if (AcceptHostRootedPathsForTests && path is not null && Path.IsPathRooted(path) && !OperatingSystemIsWindows()) return Path.GetFullPath(path);
        var (full, refusal) = TechnologyObjectPolicy.ResolveLocalFile(path, Path.GetFullPath);
        return full ?? throw Invalid(refusal!.Replace("filePath", field));
    }

    private static bool OperatingSystemIsWindows() => Path.DirectorySeparatorChar == '\\';

    private static string Written(HmiWriteOutcomeInfo outcome) => outcome.AppliedProperties.Count == 0 ? "none" : string.Join("; ", outcome.AppliedProperties);

    private static object? Value(object owner, string property, bool required)
    {
        var info = LibraryRuntimeBinding.Property(owner.GetType(), property, out var error);
        if (info is null)
            return required ? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, $"{owner.GetType().Name}.{property}: {error} Nothing was changed.") : null;
        try
        {
            return info.GetValue(owner);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo Bind(object target, string name, Type[] argumentTypes)
        => LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"{target.GetType().Name}.{name}({string.Join(", ", argumentTypes.Select(t => t.Name))}): {error} Nothing was changed.");

    private static void Invoke(MethodInfo method, object target, params object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw Invalid($"'{field}' is required.") : value!;

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}
