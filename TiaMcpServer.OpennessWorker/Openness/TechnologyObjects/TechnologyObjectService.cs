using System.Text;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;

/// <summary>
/// Technology objects under <c>PlcSoftware.TechnologicalObjectGroup</c> (always the PLC root; the Siemens skill states
/// that software units have none). Groups and objects are matched by exact enumeration (case-insensitive, unique),
/// never by <c>Find</c>. The compiled surface is the existing stubs (<c>TechnologicalObjectGroup</c>, group <c>Name</c>,
/// <c>Groups</c>, <c>TechnologicalObjects</c>, enumeration, and <c>TechnologicalInstanceDB</c> as an <c>InstanceDB</c>
/// with the inherited <c>Name</c>); every TO-specific member is bound at runtime (<see cref="TechnologyObjectRuntime"/>).
/// Every mutation is read back before it reports success; a write whose read-back disagrees fails
/// <c>postcondition_failed</c> (the project may have been changed).
/// </summary>
internal static class TechnologyObjectService
{
    internal sealed record Located(PlcSoftwareLocator.DiscoveredPlcSoftware Plc, TechnologicalInstanceDBGroup Group, string GroupPath,
        TechnologicalInstanceDB Object, string Name);

    private const string ExportMember = "TechnologicalInstanceDB.Export(FileInfo, ExportOptions)";
    private const string ImportMember = "TechnologicalInstanceDBComposition.Import(FileInfo, ImportOptions)";

    public static TechnologyObjectInventoryInfo List(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var inventory = new TechnologyObjectInventoryInfo();
        if (info.FolderPath is not null && TechnologyObjectPolicy.Segments(info.FolderPath).Count > 0)
        {
            // A group narrows exactly one PLC: an omitted plcName with several PLCs is ambiguous.
            var (plc, group, path) = ResolveGroup(project, info.PlcName, info.FolderPath);
            inventory.Plcs.Add(Inventory(plc, group, path));
            return inventory;
        }

        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, info.PlcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " No technology objects were listed.");
        if (info.PlcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{info.PlcName}' was found in the project.");
        foreach (var plc in plcs) inventory.Plcs.Add(Inventory(plc, plc.Software.TechnologicalObjectGroup, "/"));
        return inventory;
    }

    public static TechnologyObjectParametersInfo GetParameters(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = Locate(project, info.PlcName, info.Name);
        var result = new TechnologyObjectParametersInfo
        {
            PlcName = located.Plc.Software.Name ?? string.Empty, DeviceName = located.Plc.DeviceName ?? string.Empty,
            Name = located.Name, GroupPath = located.GroupPath, ParameterName = info.ParameterName, Prefix = info.Prefix,
            Offset = info.Offset ?? 0, Limit = info.Limit ?? TechnologyObjectPolicy.DefaultParameterLimit,
        };
        if (result.Offset < 0 || result.Limit < 1 || result.Limit > TechnologyObjectPolicy.MaxParameterLimit)
            throw Invalid($"offset must be 0 or more and limit between 1 and {TechnologyObjectPolicy.MaxParameterLimit}.");
        var parameters = TechnologyObjectRuntime.Parameters(located.Object);
        if (info.ParameterName is not null)
        {
            var parameter = TechnologyObjectRuntime.FindParameter(parameters, info.ParameterName)
                ?? throw NoParameter(located, info.ParameterName);
            var (readable, value) = TechnologyObjectRuntime.ParameterValue(parameter);
            result.Parameters.Add(DescribeParameter(TechnologyObjectRuntime.ParameterName(parameter), readable, value));
            result.MatchedCount = 1;
            return result;
        }

        var all = TechnologyObjectRuntime.Items(parameters, "TechnologicalInstanceDB.Parameters");
        result.TotalCount = all.Count;
        var index = 0;
        foreach (var parameter in all)
        {
            var name = TechnologyObjectRuntime.ParameterName(parameter);
            if (info.Prefix is not null && !name.StartsWith(info.Prefix, StringComparison.Ordinal)) continue;
            if (index >= result.Offset && result.Parameters.Count < result.Limit)
            {
                var (readable, value) = TechnologyObjectRuntime.ParameterValue(parameter);
                result.Parameters.Add(DescribeParameter(name, readable, value));
            }

            index++;
        }

        result.MatchedCount = index;
        result.NextOffset = result.Offset + result.Parameters.Count < index ? result.Offset + result.Parameters.Count : null;
        return result;
    }

    /// <summary>The object's SimaticML (<c>Export(FileInfo, ExportOptions.None)</c>), DocumentInfo removed like block exports.</summary>
    public static string GetContent(ProjectBase project, TechnologyObjectRequestInfo info) => ExportText(Locate(project, info.PlcName, info.Name));

    public static (TechnologyObjectWriteResultInfo Result, List<string> Warnings) Write(ProjectBase project, string method, TechnologyObjectRequestInfo info)
    {
        var warnings = new List<string>();
        var result = method switch
        {
            TechnologyObjectMethods.Create => Create(project, info),
            TechnologyObjectMethods.Delete => Delete(project, info),
            TechnologyObjectMethods.Rename => Rename(project, info),
            TechnologyObjectMethods.CreateGroup => CreateGroup(project, info),
            TechnologyObjectMethods.DeleteGroup => DeleteGroup(project, info),
            TechnologyObjectMethods.RenameGroup => RenameGroup(project, info),
            TechnologyObjectMethods.SetParameter => SetParameter(project, info),
            TechnologyObjectMethods.ConnectHardware => TechnologyObjectHardware.Connect(project, info, warnings),
            TechnologyObjectMethods.DisconnectHardware => TechnologyObjectHardware.Disconnect(project, info),
            TechnologyObjectMethods.Export => Export(project, info, warnings),
            TechnologyObjectMethods.Import => Import(project, info, warnings),
            TechnologyObjectMethods.UpdateContent => UpdateContent(project, info),
            _ => throw Invalid($"Unsupported technology object write '{method}'."),
        };
        result.Operation = method;
        return (result, warnings);
    }

    private static TechnologyObjectWriteResultInfo Create(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var name = Required(info.Name, "name");
        var type = Required(info.TechnologyType, "technologyType");
        var version = TechnologyObjectPolicy.ParseVersion(info.Version) ?? throw Invalid($"version '{info.Version}' is not a version such as 10.0.");
        var (plc, group, path) = ResolveGroup(project, info.PlcName, info.FolderPath);
        // Names are unique per PLC (a technology object is a DB): an existing one is refused before Create, so the object
        // found afterwards can only be the created one (the Siemens skill: find an existing TO before creating).
        var existing = new List<(TechnologicalInstanceDBGroup, string Path, TechnologicalInstanceDB)>();
        Collect(plc.Software.TechnologicalObjectGroup, "/", name, existing, 0);
        Check(TechnologyObjectWriteChecks.ExistingObject(name, existing.Count > 0 ? existing[0].Path : null, plc.Software.Name ?? string.Empty));
        var returned = LibraryReflection.Invoke(group.TechnologicalObjects, "Create", new[] { typeof(string), typeof(string), typeof(Version) },
            new object?[] { name, type, version }, "TechnologicalInstanceDBComposition.Create(String, String, Version)");
        var named = Objects(group).Where(o => TechnologyObjectPolicy.Names.Equals(o.Name, name)).ToList();
        Check(TechnologyObjectWriteChecks.Created(name, type, version.ToString(), returned?.GetType().FullName, path, named.Count));
        var created = named[0];
        var result = Result(plc, created.Name ?? name, path);
        result.TechnologyObject = Describe(created, path);
        return result;
    }

    private static TechnologyObjectWriteResultInfo Delete(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = Locate(project, info.PlcName, info.Name);
        TechnologyObjectRuntime.InvokeParameterless(located.Object, "Delete", "Delete() of the TechnologicalInstanceDB (inherited PlcBlock.Delete)");
        Check(TechnologyObjectWriteChecks.Deleted(located.Name, located.GroupPath,
            Objects(located.Group).Any(o => TechnologyObjectPolicy.Names.Equals(o.Name, located.Name))));
        return Result(located.Plc, located.Name, located.GroupPath);
    }

    private static TechnologyObjectWriteResultInfo Rename(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = Locate(project, info.PlcName, info.Name);
        var newName = Required(info.NewName, "newName");
        LibraryReflection.Rename(located.Object, newName, $"Technology object '{located.Name}'");
        var renamed = FindIn(located.Group, newName) ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            $"Technology object '{located.Name}' read back the name '{newName}', but group '{located.GroupPath}' lists no object of that name.");
        var result = Result(located.Plc, renamed.Name ?? newName, located.GroupPath);
        result.TechnologyObject = Describe(renamed, located.GroupPath);
        return result;
    }

    private static TechnologyObjectWriteResultInfo CreateGroup(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var segments = TechnologyObjectPolicy.Segments(info.FolderPath);
        if (segments.Count == 0) throw Invalid("folderPath must name the new group, for example /Axes/Line1.");
        var (plc, parent, parentPath) = ResolveGroup(project, info.PlcName, "/" + string.Join("/", segments.Take(segments.Count - 1)));
        var leaf = segments[segments.Count - 1];
        var before = UserGroups(parent).ToList();
        Check(TechnologyObjectWriteChecks.ExistingGroup(TechnologyObjectPolicy.Combine(parentPath, leaf),
            before.Any(g => TechnologyObjectPolicy.Names.Equals(g.Name, leaf)), plc.Software.Name ?? string.Empty));
        LibraryReflection.Invoke(parent.Groups, "Create", new[] { typeof(string) }, new object?[] { leaf },
            "TechnologicalInstanceDBUserGroupComposition.Create(String)");
        var after = UserGroups(parent).ToList();
        var created = after.Where(g => TechnologyObjectPolicy.Names.Equals(g.Name, leaf)).ToList();
        Check(TechnologyObjectWriteChecks.CreatedGroup(parentPath, leaf, created.Count, before.Count, after.Count));
        return Result(plc, null, TechnologyObjectPolicy.Combine(parentPath, created[0].Name ?? leaf));
    }

    private static TechnologyObjectWriteResultInfo DeleteGroup(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var (plc, group, path) = ResolveUserGroup(project, info);
        // The parent is resolved by path before the delete, so the check below always runs.
        var (_, parent, parentPath) = ResolveGroup(project, info.PlcName, TechnologyObjectPolicy.ParentPath(path));
        var leaf = TechnologyObjectPolicy.Segments(path).Last();
        TechnologyObjectRuntime.InvokeParameterless(group, "Delete", "TechnologicalInstanceDBUserGroup.Delete()");
        Check(TechnologyObjectWriteChecks.DeletedGroup(path, parentPath, UserGroups(parent).Any(g => TechnologyObjectPolicy.Names.Equals(g.Name, leaf))));
        return Result(plc, null, path);
    }

    private static TechnologyObjectWriteResultInfo RenameGroup(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var (plc, group, path) = ResolveUserGroup(project, info);
        var newName = Required(info.NewName, "newName");
        var after = LibraryReflection.Rename(group, newName, $"Technology object group '{path}'");
        return Result(plc, null, TechnologyObjectPolicy.Combine(TechnologyObjectPolicy.ParentPath(path), after));
    }

    private static TechnologyObjectWriteResultInfo SetParameter(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = Locate(project, info.PlcName, info.Name);
        var parameterName = Required(info.ParameterName, "parameterName");
        var parameter = TechnologyObjectRuntime.FindParameter(TechnologyObjectRuntime.Parameters(located.Object), parameterName)
            ?? throw NoParameter(located, parameterName);
        var (readable, current) = TechnologyObjectRuntime.ParameterValue(parameter);
        if (!readable)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"The current value of parameter '{parameterName}' of '{located.Name}' is unreadable, so its type cannot be determined. Nothing was changed.");
        object? requested;
        if (info.TagName is not null || info.TableName is not null)
        {
            if (current is not null and not PlcTag)
                throw Invalid($"Parameter '{parameterName}' of '{located.Name}' holds a {current.GetType().FullName}, not a PlcTag; write it with value. Nothing was changed.");
            requested = ResolveTag(located.Plc.Software, info.TableName, info.TagName);
        }
        else
        {
            if (current is null)
                throw Invalid($"Parameter '{parameterName}' of '{located.Name}' has no current value (null), so its type cannot be determined from the API; "
                    + "only a PlcTag (tableName and tagName) can be assigned to it. Nothing was changed.");
            if (current is PlcTag)
                throw Invalid($"Parameter '{parameterName}' of '{located.Name}' holds a PlcTag; assign another tag with tableName and tagName. Nothing was changed.");
            if (!TechnologyObjectValues.TryConvert(info.Value, current.GetType(), out requested, out var error))
                throw Invalid(TechnologyObjectValues.ParameterRefusal(parameterName, located.Name, error));
        }

        var result = Result(located.Plc, located.Name, located.GroupPath);
        result.PreviousParameter = DescribeParameter(parameterName, true, current);
        TechnologyObjectRuntime.SetParameterValue(parameter, requested);

        // A fresh Find: the read-back never reuses the proxy that was written.
        var reread = TechnologyObjectRuntime.FindParameter(TechnologyObjectRuntime.Parameters(located.Object), parameterName)
            ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"Parameter '{parameterName}' of '{located.Name}' can no longer be found after the write; the project may have been changed.");
        var (rereadable, actual) = TechnologyObjectRuntime.ParameterValue(reread);
        result.Parameter = DescribeParameter(parameterName, rereadable, actual);
        var applied = rereadable && (requested is PlcTag ? Equals(actual, requested) : TechnologyObjectValues.Equivalent(requested, actual));
        if (!applied)
            throw Evidence(result, TechnologyObjectMethods.SetParameter,
                $"Parameter '{parameterName}' of '{located.Name}' reads back {result.Parameter.ValueKind} '{result.Parameter.Value}' ({result.Parameter.ValueType}) "
                + $"after writing {Shown(requested)} over {result.PreviousParameter.ValueKind} '{result.PreviousParameter.Value}'. TIA Portal did not keep the value as "
                + "written; the project may have been changed. Inspect it with get_technology_object_parameters.");
        return result;
    }

    /// <summary>
    /// Exports to a private temporary file, then <see cref="TechnologyObjectFiles.Place"/>: staged as
    /// <c>.&lt;name&gt;.&lt;guid&gt;.tmp</c> next to the target and moved into place (<c>File.Move</c>, or <c>File.Replace</c>
    /// over an existing file with overwrite), so the target is never partially written; the target is re-read and its
    /// SHA-256 must equal the export's.
    /// </summary>
    private static TechnologyObjectWriteResultInfo Export(ProjectBase project, TechnologyObjectRequestInfo info, List<string> warnings)
    {
        var located = Locate(project, info.PlcName, info.Name);
        var target = LocalFile(info.FilePath);
        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory of '{target}' does not exist. Nothing was written.");
        if (Directory.Exists(target)) throw Invalid($"'{target}' is a directory, not a file. Nothing was written.");
        if (File.Exists(target) && !info.Overwrite)
            throw Invalid($"'{target}' already exists; set overwrite:true to replace it. Nothing was written.");
        warnings.AddRange(FileWarnings(target));
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-to-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        byte[] bytes;
        try
        {
            var exported = Path.Combine(temporary, "object.xml");
            ExportConsistencyEvidence.Observe(located.Object, () => PlcExportConsistency.TechnologyObjectHint(located.Plc.Software.Name, located.Name),
                () => LibraryReflection.Invoke(located.Object, "Export", new[] { typeof(FileInfo), typeof(ExportOptions) },
                    new object?[] { new FileInfo(exported), ExportOptions.None }, ExportMember));
            if (!File.Exists(exported))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no export file for technology object '{located.Name}'. Nothing was written.");
            bytes = File.ReadAllBytes(exported);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        if (TechnologyObjectFiles.Place(bytes, target, info.Overwrite, $"The export of '{located.Name}'", warnings) is { } failure)
            throw new WorkerOperationException(failure.Category, failure.Message);
        var result = Result(located.Plc, located.Name, located.GroupPath);
        result.FilePath = target;
        result.FileBytes = bytes.LongLength;
        result.FileSha256 = TechnologyObjectFiles.Sha256(bytes);
        return result;
    }

    private static TechnologyObjectWriteResultInfo Import(ProjectBase project, TechnologyObjectRequestInfo info, List<string> warnings)
    {
        var source = LocalFile(info.FilePath);
        if (!File.Exists(source)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{source}' does not exist. Nothing was imported.");
        warnings.AddRange(FileWarnings(source));
        var (plc, group, path) = ResolveGroup(project, info.PlcName, info.FolderPath);
        var before = Objects(group).Select(o => o.Name ?? string.Empty).ToList();
        var returned = LibraryReflection.Invoke(group.TechnologicalObjects, "Import", new[] { typeof(FileInfo), typeof(ImportOptions) },
            new object?[] { new FileInfo(source), info.Overwrite ? ImportOptions.Override : ImportOptions.None }, ImportMember);
        var result = Result(plc, null, path);
        result.ImportReturnedType = returned?.GetType().FullName;
        var (imported, failure) = TechnologyObjectWriteChecks.Imported(ReturnedNames(returned), before, Objects(group).Select(o => o.Name ?? string.Empty).ToList(),
            name => FindIn(group, name) is not null, info.Overwrite, source, path, result.ImportReturnedType, warnings);
        Check(failure);
        result.ImportedObjects = imported;
        return result;
    }

    private static TechnologyObjectWriteResultInfo UpdateContent(ProjectBase project, TechnologyObjectRequestInfo info)
    {
        var located = Locate(project, info.PlcName, info.Name);
        var exported = ExportText(located);
        PlcWritePreconditions.RequireContentHash(info.ExpectedContentHash, SourceFormatNames.Xml, exported);
        if (TechnologyObjectPolicy.CheckImportDocument(exported, info.Content, located.Name) is { } refusal)
            throw Invalid($"The import was refused before anything changed: {refusal}. Edit the SimaticML from get_technology_object_content of the same object.");
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-to-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        object? returned;
        try
        {
            var file = Path.Combine(temporary, "object.xml");
            File.WriteAllText(file, info.Content, Encoding.UTF8);
            returned = LibraryReflection.Invoke(located.Group.TechnologicalObjects, "Import", new[] { typeof(FileInfo), typeof(ImportOptions) },
                new object?[] { new FileInfo(file), ImportOptions.Override }, ImportMember);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        var result = Result(located.Plc, located.Name, located.GroupPath);
        result.ImportReturnedType = returned?.GetType().FullName;
        var reported = ReturnedNames(returned);
        if (reported is not null)
        {
            result.ImportedObjects = reported;
            if (reported.Count != 1 || !TechnologyObjectPolicy.Names.Equals(reported[0], located.Name))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"The import into group '{located.GroupPath}' returned [{string.Join(", ", reported.Select(n => $"'{n}'"))}] instead of exactly '{located.Name}'. "
                    + "The project was changed; inspect the group with list_technology_objects before retrying.");
        }

        var updated = FindIn(located.Group, located.Name) ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            $"Technology object '{located.Name}' is not in group '{located.GroupPath}' after the import; the project was changed.");
        if (reported is null) result.ImportedObjects = new List<string> { located.Name };
        result.TechnologyObject = Describe(updated, located.GroupPath);
        return result;
    }

    internal static Located Locate(ProjectBase project, string? plcName, string? name)
    {
        var wanted = Required(name, "name");
        var plc = PlcSoftwareLocator.FindUnique(project, plcName);
        var matches = new List<(TechnologicalInstanceDBGroup Group, string Path, TechnologicalInstanceDB Object)>();
        Collect(plc.Software.TechnologicalObjectGroup, "/", wanted, matches, 0);
        return matches.Count switch
        {
            1 => new Located(plc, matches[0].Group, matches[0].Path, matches[0].Object, matches[0].Object.Name ?? wanted),
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No technology object named '{wanted}' was found in PLC '{plc.Software.Name}'."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"Technology object '{wanted}' is ambiguous in PLC '{plc.Software.Name}' (groups {string.Join(", ", matches.Select(m => $"'{m.Path}'"))})."),
        };
    }

    internal static TechnologyObjectInfo Describe(TechnologicalInstanceDB technologyObject, string groupPath)
    {
        var unreadable = new List<string>();
        var number = LibraryReflection.Optional(technologyObject, "Number", unreadable, "Number");
        var info = new TechnologyObjectInfo
        {
            Name = technologyObject.Name ?? string.Empty,
            GroupPath = groupPath,
            Number = number as int?,
            SystemLibElement = LibraryReflection.Display(LibraryReflection.Optional(technologyObject, "OfSystemLibElement", unreadable, "OfSystemLibElement")),
            SystemLibVersion = LibraryReflection.Display(LibraryReflection.Optional(technologyObject, "OfSystemLibVersion", unreadable, "OfSystemLibVersion")),
            RuntimeType = technologyObject.GetType().FullName,
        };
        if (number is not null and not int) unreadable.Add("Number");
        info.Unreadable = unreadable;
        return info;
    }

    internal static TechnologyObjectParameterInfo DescribeParameter(string name, bool readable, object? value)
    {
        if (!readable) return new TechnologyObjectParameterInfo { Name = name, ValueKind = TechnologyObjectValueKinds.Unreadable };
        var (kind, text, _) = TechnologyObjectRuntime.Describe(value);
        return new TechnologyObjectParameterInfo
        {
            Name = name, ValueKind = kind, Value = text, ValueType = value?.GetType().FullName,
            EnumMembers = TechnologyObjectValues.EnumMembers(value?.GetType()),
            EnumMembersTruncated = TechnologyObjectValues.EnumMembersTruncated(value?.GetType()),
            EnumFlags = TechnologyObjectValues.IsFlags(value?.GetType()),
        };
    }

    /// <summary>The unique PLC tag <paramref name="tagName"/> in the tag table named <paramref name="tableName"/> anywhere in the PLC's tag table tree.</summary>
    internal static PlcTag ResolveTag(PlcSoftware software, string? tableName, string? tagName)
    {
        var table = Required(tableName, "tableName");
        var tag = Required(tagName, "tagName");
        var tables = new List<PlcTagTable>();
        CollectTables(software.TagTableGroup, table, tables, 0);
        if (tables.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No tag table named '{table}' was found in PLC '{software.Name}'.");
        if (tables.Count > 1) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{tables.Count} tag tables named '{table}' exist in PLC '{software.Name}'.");
        return tables[0].Tags.Find(tag)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag '{tag}' was not found in tag table '{tables[0].Name}'.");
    }

    internal static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);

    /// <summary>Throws a failed shared check (<see cref="TechnologyObjectWriteChecks"/>) as its category.</summary>
    internal static void Check(TechnologyObjectWriteChecks.Failure? failure)
    {
        if (failure is not null) throw new WorkerOperationException(failure.Category, failure.Message);
    }

    /// <summary>
    /// A postcondition failure that carries the write's before/after state as the failed item's structured result
    /// (through the <c>PlcDownloadOutcome</c> evidence channel, decoded by the host like a successful result).
    /// </summary>
    internal static WorkerOperationException Evidence(TechnologyObjectWriteResultInfo result, string operation, string message)
    {
        result.Operation = operation;
        return new(WorkerFailureCategories.PostconditionFailed, message, plcDownloadOutcome: WorkerJson.SerializePayload(result));
    }

    internal static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw Invalid($"{field} is required.") : value!;

    internal static TechnologyObjectWriteResultInfo Result(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? name, string? groupPath) => new()
    {
        PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty, Name = name, GroupPath = groupPath,
    };

    private static TechnologyObjectPlcInfo Inventory(PlcSoftwareLocator.DiscoveredPlcSoftware plc, TechnologicalInstanceDBGroup group, string path)
    {
        var info = new TechnologyObjectPlcInfo { PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
        Walk(group, path, info);
        return info;
    }

    private static void Walk(TechnologicalInstanceDBGroup group, string path, TechnologyObjectPlcInfo info)
    {
        foreach (var technologyObject in Objects(group))
        {
            if (!Room(info)) return;
            info.TechnologyObjects.Add(Describe(technologyObject, path));
        }

        foreach (var child in UserGroups(group))
        {
            if (!Room(info)) return;
            var childPath = TechnologyObjectPolicy.Combine(path, child.Name ?? string.Empty);
            info.Groups.Add(new TechnologyObjectGroupInfo { Path = childPath, Name = child.Name ?? string.Empty });
            Walk(child, childPath, info);
        }
    }

    private static bool Room(TechnologyObjectPlcInfo info)
    {
        if (info.TechnologyObjects.Count + info.Groups.Count < TechnologyObjectPolicy.MaxObjects) return true;
        info.Complete = false;
        return false;
    }

    private static void Collect(TechnologicalInstanceDBGroup group, string path, string name,
        List<(TechnologicalInstanceDBGroup, string, TechnologicalInstanceDB)> matches, int depth)
    {
        if (depth > 64) throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "The technology object group tree is deeper than 64 levels.");
        foreach (var technologyObject in Objects(group))
            if (TechnologyObjectPolicy.Names.Equals(technologyObject.Name, name)) matches.Add((group, path, technologyObject));
        foreach (var child in UserGroups(group))
            Collect(child, TechnologyObjectPolicy.Combine(path, child.Name ?? string.Empty), name, matches, depth + 1);
    }

    private static TechnologicalInstanceDB? FindIn(TechnologicalInstanceDBGroup group, string name)
    {
        var matches = Objects(group).Where(o => TechnologyObjectPolicy.Names.Equals(o.Name, name)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static IEnumerable<TechnologicalInstanceDB> Objects(TechnologicalInstanceDBGroup group)
        => group.TechnologicalObjects.Cast<TechnologicalInstanceDB>().ToList();

    private static IEnumerable<TechnologicalInstanceDBUserGroup> UserGroups(TechnologicalInstanceDBGroup group)
        => group.Groups.Cast<TechnologicalInstanceDBUserGroup>().ToList();

    private static (PlcSoftwareLocator.DiscoveredPlcSoftware Plc, TechnologicalInstanceDBGroup Group, string Path) ResolveGroup(
        ProjectBase project, string? plcName, string? folderPath)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, plcName);
        TechnologicalInstanceDBGroup current = plc.Software.TechnologicalObjectGroup;
        var path = "/";
        foreach (var segment in TechnologyObjectPolicy.Segments(folderPath))
        {
            var next = TechnologyObjectPolicy.Combine(path, segment);
            var matches = UserGroups(current).Where(g => TechnologyObjectPolicy.Names.Equals(g.Name, segment)).Take(2).ToList();
            if (matches.Count == 0)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Technology object group '{next}' was not found in PLC '{plc.Software.Name}'.");
            if (matches.Count > 1)
                throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Technology object group '{next}' is ambiguous in PLC '{plc.Software.Name}'.");
            current = matches[0];
            path = TechnologyObjectPolicy.Combine(path, matches[0].Name ?? segment);
        }

        return (plc, current, path);
    }

    private static (PlcSoftwareLocator.DiscoveredPlcSoftware Plc, TechnologicalInstanceDBUserGroup Group, string Path) ResolveUserGroup(
        ProjectBase project, TechnologyObjectRequestInfo info)
    {
        if (TechnologyObjectPolicy.Segments(info.FolderPath).Count == 0)
            throw Invalid("folderPath must name a user group, for example /Axes; the system group '/' cannot be deleted or renamed.");
        var (plc, group, path) = ResolveGroup(project, info.PlcName, info.FolderPath);
        return group is TechnologicalInstanceDBUserGroup user ? (plc, user, path)
            : throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"Technology object group '{path}' is not a user group.");
    }

    private static void CollectTables(PlcTagTableGroup group, string name, List<PlcTagTable> tables, int depth)
    {
        if (depth > 64) return;
        foreach (PlcTagTable table in group.TagTables)
            if (TechnologyObjectPolicy.Names.Equals(table.Name, name)) tables.Add(table);
        foreach (PlcTagTableUserGroup child in group.Groups)
            CollectTables(child, name, tables, depth + 1);
    }

    private static string ExportText(Located located)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-to-content-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "object.xml");
            ExportConsistencyEvidence.Observe(located.Object, () => PlcExportConsistency.TechnologyObjectHint(located.Plc.Software.Name, located.Name),
                () => LibraryReflection.Invoke(located.Object, "Export", new[] { typeof(FileInfo), typeof(ExportOptions) },
                    new object?[] { new FileInfo(path), ExportOptions.None }, ExportMember));
            if (!File.Exists(path))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no export file for technology object '{located.Name}'.");
            return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(path));
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }
    }

    /// <summary>The names of an <c>Import</c> return value that is an enumerable of technology objects; null for anything else.</summary>
    private static List<string>? ReturnedNames(object? returned)
        => TechnologyObjectPolicy.ReturnedNames(returned, item => item is TechnologicalInstanceDB technologyObject ? technologyObject.Name ?? string.Empty : null);

    /// <summary>The validated full path: the lexical check, then <see cref="Path.GetFullPath(string)"/>, which must not change it.</summary>
    internal static string LocalFile(string? path)
    {
        var (full, refusal) = TechnologyObjectPolicy.ResolveLocalFile(path, Path.GetFullPath);
        return full ?? throw Invalid(refusal!);
    }

    /// <summary>Network drive and reparse point warnings for a caller file (never a refusal).</summary>
    internal static List<string> FileWarnings(string full)
        => TechnologyObjectPolicy.LocalFileWarnings(full,
            root =>
            {
                try
                {
                    return new DriveInfo(root).DriveType == DriveType.Network;
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            },
            existing =>
            {
                try
                {
                    return File.Exists(existing) || Directory.Exists(existing) ? (File.GetAttributes(existing) & FileAttributes.ReparsePoint) != 0 : null;
                }
                catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    return null;
                }
            });

    private static WorkerOperationException NoParameter(Located located, string parameterName)
        => new(WorkerFailureCategories.TargetNotFound,
            $"Technology object '{located.Name}' has no parameter '{parameterName}' (TechnologicalParameterComposition.Find returned null).");

    private static string Shown(object? value)
        => value is PlcTag tag ? $"PlcTag '{tag.Name}'" : $"'{TechnologyObjectValues.Describe(value).Text}'";
}
