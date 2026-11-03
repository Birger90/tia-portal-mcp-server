using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.WatchAndForceTables;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Openness.PlcOnline;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// rename_block_group, rename_type_group, rename_tag_table_group and rename_watch_table_group. The group is
/// resolved exactly (case-insensitive, one match per level, never the system root group) and renamed through
/// the documented generic attribute API: <c>GetAttributeInfos()</c> must list a writable <c>Name</c>, then
/// <c>SetAttribute("Name", newName)</c>, then <c>GetAttribute("Name")</c> must read the new name back
/// (<c>postcondition_failed</c> otherwise; Portal silently ignored a colliding tag rename before). The blocks
/// reference documents exactly this for block groups; for the other groups it is the generic contract of
/// <c>IEngineeringObject</c>, and an installation that does not list Name as writable fails closed with
/// <c>unsupported_capability</c> before anything changes.
/// </summary>
internal static class PlcGroupRenameService
{
    public static PlcGroupRenameInfo Rename(ProjectBase project, string method, PlcSourceFolderRequestInfo request)
    {
        var newName = string.IsNullOrWhiteSpace(request.NewName)
            ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "'newName' is required.")
            : request.NewName!.Trim();
        return method switch
        {
            PlcSourceFolderMethods.RenameBlockGroup => RenameBlockGroup(project, request, newName),
            PlcSourceFolderMethods.RenameTypeGroup => RenameTypeGroup(project, request, newName),
            PlcSourceFolderMethods.RenameTagTableGroup => RenameTagTableGroup(project, request, newName),
            PlcSourceFolderMethods.RenameWatchTableGroup => RenameWatchTableGroup(project, request, newName),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported group rename '{method}'."),
        };
    }

    private static PlcGroupRenameInfo RenameBlockGroup(ProjectBase project, PlcSourceFolderRequestInfo request, string newName)
    {
        var path = UserGroupPath(request.GroupPath, PlcGroupPath.BlocksKind);
        var plc = PlcSoftwareLocator.FindUnique(project, path.PlcName);
        PlcBlockGroup root = path.UnitName is null ? plc.Software.BlockGroup : PlcSourceFolderTree.Unit(plc.Software, path.UnitName).BlockGroup;
        var group = BlockTargetResolver.ResolveBlockGroupPath(root, path.Folders).Group as PlcBlockUserGroup
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"'{path.ToDisplayPath()}' is not a block user group.");
        var oldName = group.Name ?? string.Empty;
        var after = LibraryReflection.Rename(group, newName, $"Block group '{path.ToDisplayPath()}'");
        return Result(PlcSourceFolderMethods.RenameBlockGroup, PlcNameRules.BlockGroup, plc, path.UnitName, path.ToDisplayPath(), path.RenamedDisplayPath(after), oldName, after);
    }

    private static PlcGroupRenameInfo RenameTypeGroup(ProjectBase project, PlcSourceFolderRequestInfo request, string newName)
    {
        var path = UserGroupPath(request.GroupPath, PlcGroupPath.TypesKind);
        var plc = PlcSoftwareLocator.FindUnique(project, path.PlcName);
        PlcTypeGroup root = path.UnitName is null ? plc.Software.TypeGroup : PlcSourceFolderTree.Unit(plc.Software, path.UnitName).TypeGroup;
        var group = PlcSourceFolderTree.TypeGroup(root, path.Folders) as PlcTypeUserGroup
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"'{path.ToDisplayPath()}' is not a PLC data type user group.");
        var oldName = group.Name ?? string.Empty;
        var after = LibraryReflection.Rename(group, newName, $"PLC data type group '{path.ToDisplayPath()}'");
        return Result(PlcSourceFolderMethods.RenameTypeGroup, PlcSourceFolderKinds.TypeGroup, plc, path.UnitName, path.ToDisplayPath(), path.RenamedDisplayPath(after), oldName, after);
    }

    private static PlcGroupRenameInfo RenameTagTableGroup(ProjectBase project, PlcSourceFolderRequestInfo request, string newName)
    {
        var segments = UserFolder(request.FolderPath);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        PlcTagTableGroup root = request.UnitName is null ? plc.Software.TagTableGroup : PlcSourceFolderTree.Unit(plc.Software, request.UnitName).TagTableGroup;
        var group = PlcSourceFolderTree.TagTableGroup(root, segments) as PlcTagTableUserGroup
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"Tag table folder '{Folder(segments)}' is not a user group.");
        var oldName = group.Name ?? string.Empty;
        var after = LibraryReflection.Rename(group, newName, $"Tag table folder '{Folder(segments)}'");
        return Result(PlcSourceFolderMethods.RenameTagTableGroup, PlcSourceFolderKinds.TagTableGroup, plc, request.UnitName, Folder(segments),
            Folder(segments.Take(segments.Count - 1).Append(after).ToList()), oldName, after);
    }

    private static PlcGroupRenameInfo RenameWatchTableGroup(ProjectBase project, PlcSourceFolderRequestInfo request, string newName)
    {
        _ = UserFolder(request.FolderPath);
        var (plc, resolved, path) = PlcWatchTableService.ResolveGroup(project, request.PlcName, request.FolderPath);
        var group = resolved as PlcWatchAndForceTableUserGroup ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetKindUnsupported, $"Watch-and-force table group '{path}' is not a user group.");
        var oldName = group.Name ?? string.Empty;
        var after = LibraryReflection.Rename(group, newName, $"Watch-and-force table group '{path}'");
        var parent = path.Substring(0, path.LastIndexOf('/'));
        return Result(PlcSourceFolderMethods.RenameWatchTableGroup, PlcSourceFolderKinds.WatchTableGroup, plc, null, path,
            (parent.Length == 0 ? string.Empty : parent) + "/" + after, oldName, after);
    }

    private static PlcGroupPath UserGroupPath(string? groupPath, string kind)
    {
        if (!PlcGroupPath.TryParse(groupPath, out var path, out var error))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error!);
        if (path!.Kind != kind)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{groupPath}' is not a {(kind == PlcGroupPath.BlocksKind ? "block" : "PLC data type")} group path.");
        if (path.IsRoot)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{groupPath}' is the system group of its scope; only user groups can be renamed.");
        return path;
    }

    private static IReadOnlyList<string> UserFolder(string? folderPath)
    {
        var segments = PlcWatchTablePolicy.Segments(folderPath);
        if (segments.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "folderPath must name a user group, for example /Line/Cell; the system group '/' cannot be renamed.");
        return segments;
    }

    private static string Folder(IReadOnlyList<string> segments) => "/" + string.Join("/", segments);

    private static PlcGroupRenameInfo Result(string operation, string kind, PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? unitName,
        string path, string newPath, string oldName, string newName) => new()
    {
        Operation = operation,
        Kind = kind,
        PlcName = plc.Software.Name ?? string.Empty,
        DeviceName = plc.DeviceName ?? string.Empty,
        UnitName = unitName,
        Path = path,
        NewPath = newPath,
        OldName = oldName,
        NewName = newName,
    };
}
