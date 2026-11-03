using Siemens.Engineering;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Create and delete of PLC data type groups and tag table groups (tags-types.md §2 and its V21 API list:
/// <c>PlcTagTableUserGroupComposition.Create(String)</c>/<c>Find(String)</c>, <c>PlcTagTableUserGroup.Delete()</c>,
/// <c>PlcTypeUserGroupComposition.Create(String)</c>/<c>Find(String)</c>, <c>PlcTypeUserGroup.Delete()</c>) and of
/// software units (software-units.md §2: <c>PlcUnitComposition.Create(String)</c>/<c>Find(String)</c>,
/// <c>PlcUnit.Delete()</c>, rename through the <c>Name</c> attribute). Parents and targets are resolved exactly
/// (case-insensitive, one match per level, never a system group); the parent of a create must exist. The type
/// group <c>Create(String)</c> has no documented return type and is bound at runtime by exact name and argument
/// type. Every mutation is read back: a create must be found by name afterwards, a delete must be gone, a rename
/// must read the new name back (<c>postcondition_failed</c> otherwise).
/// </summary>
internal static class PlcContainerService
{
    private const string TypeGroupCreate = "PlcTypeUserGroupComposition.Create(String) (tags-types.md, V21 API list)";

    public static object Execute(ProjectBase project, string method, PlcSourceFolderRequestInfo request) => method switch
    {
        PlcSourceFolderMethods.CreateTypeGroup => CreateTypeGroup(project, request),
        PlcSourceFolderMethods.DeleteTypeGroup => DeleteTypeGroup(project, request),
        PlcSourceFolderMethods.CreateTagTableGroup => CreateTagTableGroup(project, request),
        PlcSourceFolderMethods.DeleteTagTableGroup => DeleteTagTableGroup(project, request),
        PlcSourceFolderMethods.CreateSoftwareUnit => CreateSoftwareUnit(project, request),
        PlcSourceFolderMethods.DeleteSoftwareUnit => DeleteSoftwareUnit(project, request),
        PlcSourceFolderMethods.RenameSoftwareUnit => RenameSoftwareUnit(project, request),
        _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported container method '{method}'."),
    };

    private static PlcContainerMutationInfo CreateTypeGroup(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var path = UserGroupPath(request.GroupPath);
        var plc = PlcSoftwareLocator.FindUnique(project, path.PlcName);
        var parent = PlcSourceFolderTree.TypeGroup(TypeRoot(plc, path.UnitName), path.Folders.Take(path.Folders.Count - 1).ToList());
        var name = path.Folders[path.Folders.Count - 1];
        LibraryReflection.Invoke(parent.Groups, "Create", new[] { typeof(string) }, new object[] { name }, TypeGroupCreate);
        var created = parent.Groups.Find(name) ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            $"PLC data type group '{path.ToDisplayPath()}' was not found by Find('{name}') after Create; inspect it with plc_read find_objects.");
        return Result(PlcSourceFolderMethods.CreateTypeGroup, PlcSourceFolderKinds.TypeGroup, plc, path.UnitName, path.ToDisplayPath(), created.Name ?? name);
    }

    private static PlcContainerMutationInfo DeleteTypeGroup(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var path = UserGroupPath(request.GroupPath);
        var plc = PlcSoftwareLocator.FindUnique(project, path.PlcName);
        var parent = PlcSourceFolderTree.TypeGroup(TypeRoot(plc, path.UnitName), path.Folders.Take(path.Folders.Count - 1).ToList());
        var group = PlcSourceFolderTree.TypeGroup(parent, new[] { path.Folders[path.Folders.Count - 1] }) as PlcTypeUserGroup
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"'{path.ToDisplayPath()}' is not a PLC data type user group.");
        var name = group.Name ?? string.Empty;
        group.Delete();
        if (parent.Groups.Cast<PlcTypeUserGroup>().Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"PLC data type group '{path.ToDisplayPath()}' still exists after Delete().");
        return Result(PlcSourceFolderMethods.DeleteTypeGroup, PlcSourceFolderKinds.TypeGroup, plc, path.UnitName, path.ToDisplayPath(), name);
    }

    private static PlcContainerMutationInfo CreateTagTableGroup(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var segments = UserFolder(request.FolderPath);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var parent = PlcSourceFolderTree.TagTableGroup(TagRoot(plc, request.UnitName), segments.Take(segments.Count - 1).ToList());
        var name = segments[segments.Count - 1];
        var created = parent.Groups.Create(name);
        var found = parent.Groups.Find(name) ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            $"Tag table folder '{Folder(segments)}' was not found by Find('{name}') after Create; inspect it with plc_read find_objects.");
        return Result(PlcSourceFolderMethods.CreateTagTableGroup, PlcSourceFolderKinds.TagTableGroup, plc, request.UnitName, Folder(segments), found.Name ?? created?.Name ?? name);
    }

    private static PlcContainerMutationInfo DeleteTagTableGroup(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var segments = UserFolder(request.FolderPath);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var parent = PlcSourceFolderTree.TagTableGroup(TagRoot(plc, request.UnitName), segments.Take(segments.Count - 1).ToList());
        var group = PlcSourceFolderTree.TagTableGroup(parent, new[] { segments[segments.Count - 1] }) as PlcTagTableUserGroup
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"Tag table folder '{Folder(segments)}' is not a user group.");
        var name = group.Name ?? string.Empty;
        group.Delete();
        if (parent.Groups.Cast<PlcTagTableUserGroup>().Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Tag table folder '{Folder(segments)}' still exists after Delete().");
        return Result(PlcSourceFolderMethods.DeleteTagTableGroup, PlcSourceFolderKinds.TagTableGroup, plc, request.UnitName, Folder(segments), name);
    }

    private static PlcContainerMutationInfo CreateSoftwareUnit(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var name = UnitName(request);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var units = Units(plc);
        var created = units.Create(name);
        var found = units.Find(name) ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            $"Software unit '{name}' was not found by Find('{name}') after Create; inspect it with browse_project_tree.");
        var actual = found.Name ?? created?.Name ?? name;
        return Result(PlcSourceFolderMethods.CreateSoftwareUnit, PlcSourceFolderKinds.SoftwareUnit, plc, actual, UnitPath(plc, actual), actual);
    }

    private static PlcContainerMutationInfo DeleteSoftwareUnit(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var unit = PlcSourceFolderTree.Unit(plc.Software, UnitName(request));
        var name = unit.Name ?? string.Empty;
        unit.Delete();
        if (Units(plc).Cast<PlcUnit>().Any(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Software unit '{name}' still exists after Delete().");
        return Result(PlcSourceFolderMethods.DeleteSoftwareUnit, PlcSourceFolderKinds.SoftwareUnit, plc, name, UnitPath(plc, name), name);
    }

    private static PlcGroupRenameInfo RenameSoftwareUnit(ProjectBase project, PlcSourceFolderRequestInfo request)
    {
        var newName = string.IsNullOrWhiteSpace(request.NewName)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "'newName' is required.")
            : request.NewName!.Trim();
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var unit = PlcSourceFolderTree.Unit(plc.Software, UnitName(request));
        var oldName = unit.Name ?? string.Empty;
        var after = LibraryReflection.Rename(unit, newName, $"Software unit '{oldName}'");
        return new PlcGroupRenameInfo
        {
            Operation = PlcSourceFolderMethods.RenameSoftwareUnit,
            Kind = PlcSourceFolderKinds.SoftwareUnit,
            PlcName = plc.Software.Name ?? string.Empty,
            DeviceName = plc.DeviceName ?? string.Empty,
            UnitName = oldName,
            Path = UnitPath(plc, oldName),
            NewPath = UnitPath(plc, after),
            OldName = oldName,
            NewName = after,
        };
    }

    private static PlcTypeGroup TypeRoot(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? unitName)
        => unitName is null ? plc.Software.TypeGroup : PlcSourceFolderTree.Unit(plc.Software, unitName).TypeGroup;

    private static PlcTagTableGroup TagRoot(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? unitName)
        => unitName is null ? plc.Software.TagTableGroup : PlcSourceFolderTree.Unit(plc.Software, unitName).TagTableGroup;

    /// <summary>software-units.md: <c>plcSoftware.GetService&lt;PlcUnitProvider&gt;().UnitGroup.Units</c>.</summary>
    private static PlcUnitComposition Units(PlcSoftwareLocator.DiscoveredPlcSoftware plc)
        => plc.Software.GetService<PlcUnitProvider>()?.UnitGroup.Units
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"PLC '{plc.Software.Name}' offers no PlcUnitProvider (software units are unavailable on this PLC).");

    private static PlcGroupPath UserGroupPath(string? groupPath)
    {
        if (!PlcGroupPath.TryParse(groupPath, out var path, out var error))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error!);
        if (path!.Kind != PlcGroupPath.TypesKind)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{groupPath}' is not a PLC data type group path.");
        if (path.IsRoot)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{groupPath}' is the system group of its scope; name a user group below it.");
        return path;
    }

    private static IReadOnlyList<string> UserFolder(string? folderPath)
    {
        var segments = PlcWatchTablePolicy.Segments(folderPath);
        if (segments.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "folderPath must name a user group, for example /Line/Cell; the system group '/' cannot be created or deleted.");
        return segments;
    }

    private static string UnitName(PlcSourceFolderRequestInfo request)
        => string.IsNullOrWhiteSpace(request.UnitName)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "'unitName' is required.")
            : request.UnitName!.Trim();

    private static string Folder(IReadOnlyList<string> segments) => "/" + string.Join("/", segments);

    private static string UnitPath(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string unit) => $"{plc.Software.Name}/Units/{unit}";

    private static PlcContainerMutationInfo Result(string operation, string kind, PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? unitName, string path, string name) => new()
    {
        Operation = operation,
        Kind = kind,
        PlcName = plc.Software.Name ?? string.Empty,
        DeviceName = plc.DeviceName ?? string.Empty,
        UnitName = unitName,
        Path = path,
        Name = name,
    };
}
