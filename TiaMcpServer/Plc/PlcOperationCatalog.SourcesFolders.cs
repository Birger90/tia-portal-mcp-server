using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using static TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods;

namespace TiaMcpServer.Plc;

/// <summary>
/// Catalog entries and shape rules of the external-source, project-tree folder, file-export and accessible-device
/// operations. Everything here is checked before any worker call; the worker re-checks paths and names.
/// The members are methods, not static fields, because they run inside the initializers of the main part of
/// this partial class, whose order relative to this file's field initializers is not defined.
/// </summary>
public static partial class PlcOperationCatalog
{
    public const int MaxExportObjects = 2000;
    public const int DefaultExportObjects = 500;
    public const int MaxSourceObjects = 500;
    public const int MaxFindLimit = 1000;
    public const int DefaultFindLimit = 100;
    public const int MaxPatternLength = 256;

    /// <summary>The find_objects kinds, in the order matches of different kinds are listed.</summary>
    public static IReadOnlyList<string> FindKinds { get; } = Array.AsReadOnly(new[]
    {
        "software_unit", "block", "block_group", "type", "type_group", "tag_table", "tag_table_group", "tag", "user_constant",
        "watch_table", "force_table", "watch_table_group", "external_source",
    });

    public static IReadOnlyList<string> CsvDelimiters { get; } = Array.AsReadOnly(new[] { ",", ";", "\t" });

    private static IReadOnlyDictionary<string, Spec> WithSourceFolderSpecs(Dictionary<string, Spec> specs)
    {
        var csvOptional = new[] { "plcName", "tableName", "folderPath", "overwrite", "csvDelimiter", "rawCells" };
        specs[ListExternalSources] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[ReadTagComments] = Read(Array.Empty<string>(), new[] { "plcName", "tableName", "folderPath" });
        specs[ScanAccessibleDevices] = Read(new[] { "mode" }, new[] { "plcName", "pcInterface", "maxElements" });
        specs[FindObjects] = Read(new[] { "pattern" }, new[] { "plcName", "kinds", "offset", "limit" });
        specs[ImportExternalSource] = Write(new[] { "filePath" }, new[] { "plcName", "unitName", "sourceName" });
        specs[GenerateBlocksFromSource] = Write(new[] { "sourceName" }, new[] { "plcName", "unitName", "generateOption", "targetGroupPath" });
        specs[DeleteExternalSource] = Write(new[] { "sourceName" }, new[] { "plcName", "unitName" });
        specs[ExportSourceFile] = Write(new[] { "filePath" }, new[] { "blockPaths", "typePaths", "withDependencies", "overwrite" });
        specs[RenameBlockGroup] = Write(new[] { "blockPath", "newName" }, Array.Empty<string>());
        specs[RenameTypeGroup] = Write(new[] { "typePath", "newName" }, Array.Empty<string>());
        specs[RenameTagTableGroup] = Write(new[] { "folderPath", "newName" }, new[] { "plcName", "unitName" });
        specs[RenameWatchTableGroup] = Write(new[] { "folderPath", "newName" }, new[] { "plcName" });
        specs[CreateTypeGroup] = Write(new[] { "typePath" }, Array.Empty<string>());
        specs[DeleteTypeGroup] = Write(new[] { "typePath" }, Array.Empty<string>());
        specs[CreateTagTableGroup] = Write(new[] { "folderPath" }, new[] { "plcName", "unitName" });
        specs[DeleteTagTableGroup] = Write(new[] { "folderPath" }, new[] { "plcName", "unitName" });
        specs[CreateSoftwareUnit] = Write(new[] { "unitName" }, new[] { "plcName" });
        specs[DeleteSoftwareUnit] = Write(new[] { "unitName" }, new[] { "plcName" });
        specs[RenameSoftwareUnit] = Write(new[] { "unitName", "newName" }, new[] { "plcName" });
        specs[ExportTagTablesCsv] = Write(new[] { "filePath" }, csvOptional);
        specs[ExportWatchTablesCsv] = Write(new[] { "filePath" }, csvOptional);
        specs[ExportForceTablesCsv] = Write(new[] { "filePath" }, csvOptional);
        specs[ExportBlocks] = Write(new[] { "directoryPath" }, new[] { "plcName", "blockPath", "typePath", "format", "overwrite", "maxElements" });
        return specs;
    }

    private static (string Name, Func<PlcOperationRequest, string?> Get)[] SourceFolderStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("filePath", op => op.FilePath),
        ("directoryPath", op => op.DirectoryPath),
        ("sourceName", op => op.SourceName),
        ("unitName", op => op.UnitName),
        ("generateOption", op => op.GenerateOption),
        ("csvDelimiter", op => op.CsvDelimiter),
        ("pattern", op => op.Pattern),
        ("mode", op => op.Mode),
    };

    private static (string Name, Func<PlcOperationRequest, bool> IsSet)[] SourceFolderOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("blockPaths", op => op.BlockPaths is not null),
        ("typePaths", op => op.TypePaths is not null),
        ("overwrite", op => op.Overwrite is not null),
        ("rawCells", op => op.RawCells is not null),
        ("kinds", op => op.Kinds is not null),
        ("offset", op => op.Offset is not null),
        ("limit", op => op.Limit is not null),
        ("pcInterface", op => op.PcInterface is not null),
    };

    private static void ValidateSourceFolderItem(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (!IsRead(operation.Operation) && !IsWrite(operation.Operation)) return;
        foreach (var (name, value) in new[] { ("unitName", operation.UnitName), ("sourceName", operation.SourceName), ("newName", operation.NewName), ("mode", operation.Mode) })
        {
            if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Trim().Length != value.Length || value.Any(char.IsControl)))
                errors.Add($"{prefix}: '{name}' must be nonblank, without surrounding blanks or control characters, when supplied.");
        }

        switch (operation.Operation)
        {
            case ScanAccessibleDevices:
                if (operation.PcInterface is null)
                    errors.Add($"{prefix} is missing required field(s): pcInterface. There is no default PC interface; name the one to scan through as get_online_state lists it.");
                else if (string.IsNullOrEmpty(operation.PcInterface.Name))
                    errors.Add($"{prefix}: 'pcInterface.name' must be nonblank.");
                if (operation.MaxElements is < 1 or > PlcAccessibleDeviceProjection.MaxDevices)
                    errors.Add($"{prefix}: maxElements must be between 1 and {PlcAccessibleDeviceProjection.MaxDevices}.");
                break;
            case FindObjects:
                if (operation.Pattern is { Length: > MaxPatternLength })
                    errors.Add($"{prefix}: pattern must be at most {MaxPatternLength} characters.");
                if (operation.Kinds is { } kinds)
                {
                    if (kinds.Count == 0) errors.Add($"{prefix}: kinds must not be empty when supplied; omit it to search every kind.");
                    var unknown = kinds.Where(kind => kind is null || !FindKinds.Contains(kind, StringComparer.Ordinal)).ToArray();
                    if (unknown.Length > 0) errors.Add($"{prefix}: unknown kinds ({string.Join(", ", unknown.Select(k => $"'{k}'"))}); valid kinds: {string.Join(", ", FindKinds)}.");
                    if (kinds.Distinct(StringComparer.Ordinal).Count() != kinds.Count) errors.Add($"{prefix}: kinds must not repeat a kind.");
                }

                if (operation.Offset is < 0) errors.Add($"{prefix}: offset must not be negative.");
                if (operation.Limit is < 1 or > MaxFindLimit) errors.Add($"{prefix}: limit must be between 1 and {MaxFindLimit}.");
                break;
            case ImportExternalSource:
                SourceFile(operation, prefix, errors);
                break;
            case GenerateBlocksFromSource:
                if (operation.GenerateOption is { } option && !GenerateBlockOptions.Contains(option, StringComparer.Ordinal))
                    errors.Add($"{prefix}: generateOption must be exactly one of {string.Join(", ", GenerateBlockOptions)} (received '{option}').");
                if (operation.TargetGroupPath is { } target && !PlcGroupPath.TryParse(target, out _, out var targetError))
                    errors.Add($"{prefix}: 'targetGroupPath': {targetError}");
                break;
            case ExportSourceFile:
                SourceFile(operation, prefix, errors);
                SourceObjects(operation, prefix, errors);
                break;
            case RenameBlockGroup:
                UserGroup(operation.BlockPath, PlcGroupPath.BlocksKind, "blockPath", "renamed", prefix, errors);
                GroupName(operation.NewName, "newName", prefix, errors);
                break;
            case RenameTypeGroup:
                UserGroup(operation.TypePath, PlcGroupPath.TypesKind, "typePath", "renamed", prefix, errors);
                GroupName(operation.NewName, "newName", prefix, errors);
                break;
            case CreateTypeGroup:
            case DeleteTypeGroup:
                UserGroup(operation.TypePath, PlcGroupPath.TypesKind, "typePath", operation.Operation == CreateTypeGroup ? "created" : "deleted", prefix, errors);
                if (operation.TypePath is { } typeGroupPath && typeGroupPath.IndexOf('\\') >= 0)
                    errors.Add($"{prefix}: 'typePath' must not contain '\\'.");
                break;
            case RenameTagTableGroup:
            case RenameWatchTableGroup:
                if (operation.FolderPath is not null && PlcWatchTablePolicy.Segments(operation.FolderPath).Count == 0)
                    errors.Add($"{prefix}: folderPath must name a user group, for example /Line/Cell; the system group '/' cannot be renamed.");
                GroupName(operation.NewName, "newName", prefix, errors);
                break;
            case CreateTagTableGroup:
            case DeleteTagTableGroup:
                if (operation.FolderPath is not null && PlcWatchTablePolicy.Segments(operation.FolderPath).Count == 0)
                    errors.Add($"{prefix}: folderPath must name a user group, for example /Line/Cell; the system group '/' cannot be {(operation.Operation == CreateTagTableGroup ? "created" : "deleted")}.");
                if (operation.FolderPath is { } tagFolder && tagFolder.IndexOf('\\') >= 0)
                    errors.Add($"{prefix}: 'folderPath' must not contain '\\'.");
                break;
            case CreateSoftwareUnit:
            case DeleteSoftwareUnit:
                GroupName(operation.UnitName, "unitName", prefix, errors);
                break;
            case RenameSoftwareUnit:
                GroupName(operation.UnitName, "unitName", prefix, errors);
                GroupName(operation.NewName, "newName", prefix, errors);
                break;
            case ExportTagTablesCsv:
            case ExportWatchTablesCsv:
            case ExportForceTablesCsv:
                if (PlcFileTargets.PathShapeError("filePath", operation.FilePath) is { } csvPathError) errors.Add($"{prefix}: {csvPathError}");
                else if (!string.Equals(PlcFileTargets.Extension(operation.FilePath!), ".csv", StringComparison.Ordinal))
                    errors.Add($"{prefix}: 'filePath' must end in .csv.");
                if (operation.CsvDelimiter is { } delimiter && !CsvDelimiters.Contains(delimiter, StringComparer.Ordinal))
                    errors.Add($"{prefix}: 'csvDelimiter' must be ',', ';' or a tab character.");
                break;
            case ExportBlocks:
                if (PlcFileTargets.PathShapeError("directoryPath", operation.DirectoryPath) is { } directoryError) errors.Add($"{prefix}: {directoryError}");
                if (operation.BlockPath is not null && operation.TypePath is not null)
                    errors.Add($"{prefix}: give at most one of blockPath (a block group) and typePath (a PLC data type group); omit both to export the whole PLC.");
                if (operation.BlockPath is { } blockGroup) ScopeGroup(blockGroup, PlcGroupPath.BlocksKind, "blockPath", operation.PlcName, prefix, errors);
                if (operation.TypePath is { } typeGroup) ScopeGroup(typeGroup, PlcGroupPath.TypesKind, "typePath", operation.PlcName, prefix, errors);
                if (operation.MaxElements is < 1 or > MaxExportObjects)
                    errors.Add($"{prefix}: maxElements must be between 1 and {MaxExportObjects}.");
                break;
        }
    }

    private static void SourceFile(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (PlcFileTargets.PathShapeError("filePath", operation.FilePath) is { } error)
        {
            errors.Add($"{prefix}: {error}");
            return;
        }

        var allowed = operation.UnitName is null ? PlcFileTargets.SourceExtensions : PlcFileTargets.UnitSourceExtensions;
        var extension = PlcFileTargets.Extension(operation.FilePath!);
        if (!allowed.Contains(extension, StringComparer.Ordinal))
            errors.Add($"{prefix}: 'filePath' must end in {string.Join(", ", allowed)}{(operation.UnitName is null ? string.Empty : " for a software unit")} (received '{(extension.Length == 0 ? "no extension" : extension)}').");
    }

    private static void SourceObjects(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        var blocks = operation.BlockPaths ?? Array.Empty<string>();
        var types = operation.TypePaths ?? Array.Empty<string>();
        if (blocks.Count + types.Count == 0)
        {
            errors.Add($"{prefix}: export_source_file needs at least one blockPaths or typePaths entry.");
            return;
        }

        if (blocks.Count + types.Count > MaxSourceObjects)
            errors.Add($"{prefix}: blockPaths and typePaths together must not exceed {MaxSourceObjects} entries.");
        foreach (var path in blocks)
        {
            try { _ = BlockAddress.Parse(path); }
            catch (ArgumentException exception) { errors.Add($"{prefix}: blockPaths: {exception.Message}"); }
        }

        foreach (var path in types)
        {
            try { _ = PlcTypeAddress.Parse(path); }
            catch (ArgumentException exception) { errors.Add($"{prefix}: typePaths: {exception.Message}"); }
        }

        if (blocks.Concat(types).Where(path => path is not null).GroupBy(path => path.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            errors.Add($"{prefix}: blockPaths and typePaths must not repeat an object.");
    }

    private static void UserGroup(string? path, string kind, string member, string verb, string prefix, List<string> errors)
    {
        if (path is null) return;
        if (!PlcGroupPath.TryParse(path, out var group, out var error))
            errors.Add($"{prefix}: '{member}': {error}");
        else if (group!.Kind != kind)
            errors.Add($"{prefix}: '{member}' must be a {(kind == PlcGroupPath.BlocksKind ? "block group path such as PLC_1/Blocks/Line" : "PLC data type group path such as PLC_1/Types/Motion")}.");
        else if (group.IsRoot)
            errors.Add($"{prefix}: '{member}' names the system group of its scope; only user groups can be {verb}.");
    }

    private static void ScopeGroup(string path, string kind, string member, string? plcName, string prefix, List<string> errors)
    {
        if (!PlcGroupPath.TryParse(path, out var group, out var error))
            errors.Add($"{prefix}: '{member}': {error}");
        else if (group!.Kind != kind)
            errors.Add($"{prefix}: '{member}' must be a {(kind == PlcGroupPath.BlocksKind ? "block group path such as PLC_1/Blocks" : "PLC data type group path such as PLC_1/Types")}.");
        else if (plcName is not null && !PlcNameRules.Comparer.Equals(plcName, group.PlcName))
            errors.Add($"{prefix}: '{member}' names PLC '{group.PlcName}' but plcName is '{plcName}'; give one of them, or the same PLC.");
    }

    private static void GroupName(string? name, string member, string prefix, List<string> errors)
    {
        if (name is not null && name.IndexOfAny(new[] { '/', '\\' }) >= 0)
            errors.Add($"{prefix}: {member} '{name}' must not contain '/' or '\\'.");
    }
}
