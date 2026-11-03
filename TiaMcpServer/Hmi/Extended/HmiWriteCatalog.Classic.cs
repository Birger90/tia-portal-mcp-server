using TiaMcpServer.Contracts.Hmi;

namespace TiaMcpServer.Hmi;

/// <summary>Shape rules of the Classic WinCC writes (issue #28; members in <see cref="HmiClassicKinds"/>).</summary>
public static partial class HmiWriteCatalog
{
    public static readonly IReadOnlySet<string> ClassicOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "export_classic_objects", "import_classic_objects", "delete_classic_object", "create_classic_folder", "delete_classic_folder",
    };

    public const int MaxClassicObjectNames = 200;

    private static void ValidateClassic(HmiWriteOperationRequest r, Action<string> error)
    {
        if (!ClassicOperations.Contains(r.Operation)) return;
        if (r.ClassicKind is null) return;
        if (HmiClassicKinds.Find(r.ClassicKind) is not { } kind)
        {
            error($"'classicKind' must be one of: {string.Join(", ", HmiClassicKinds.Names)}.");
            return;
        }

        if (r.TableName is not null && !kind.NeedsTable) error("'tableName' is only valid with classicKind tags.");
        if (r.Operation == "import_classic_objects" && kind.NeedsTable && r.TableName is null) error("'tableName' is required to import classic tags (the tag table receiving them).");
        if (kind.IsSingleton && (r.GroupPath is not null || r.ObjectNames is not null))
            error($"classicKind {kind.Name} is a single object: 'groupPath' and 'objectNames' are not valid.");
        else if (r.GroupPath is not null && !kind.HasUserFolders && !kind.NeedsTable)
            error($"'groupPath' is not valid for classicKind {kind.Name}, which has no user folders.");
        if (r.Operation is "create_classic_folder" or "delete_classic_folder" && !kind.HasUserFolders)
            error($"classicKind {kind.Name} has no user folders; folders exist for {string.Join(", ", HmiClassicKinds.All.Where(k => k.HasUserFolders).Select(k => k.Name))}.");
        if (r.Operation == "create_classic_folder" && r.GroupPath is { } path && !ValidName(path.Split('/')[^1]))
            error("The new folder name (last groupPath segment) is invalid.");
        if (r.Operation == "delete_classic_object" && !kind.CanDelete)
            error($"V21 Openness documents no Delete() for classic {kind.Name}.");
        if (r.Operation == "export_classic_objects" && !kind.CanExport)
            error(HmiClassicKinds.GraphicListExportSkipped);
        if (r.ObjectNames is { } names && (names.Count is 0 or > MaxClassicObjectNames || names.Any(string.IsNullOrWhiteSpace)
                || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count))
            error($"'objectNames' must hold 1 to {MaxClassicObjectNames} distinct nonblank names.");
        foreach (var (name, value) in new[] { ("directoryPath", r.DirectoryPath), ("filePath", r.FilePath) })
            if (value is not null && Contracts.Plc.TechnologyObjects.TechnologyObjectPolicy.ValidateLocalFilePath(value) is { } refusal) error($"'{name}' {refusal}");
    }
}
