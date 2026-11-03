using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Library;

/// <summary>
/// Field specifications and validation of every library_read/library_write operation. Validation runs before
/// any worker call: unknown or misplaced fields, missing selectors, invalid names, paths, enum member names and
/// caps are argument errors (<c>validation_error</c>), never guards.
/// </summary>
public static class LibraryCatalog
{
    private sealed record Spec(string[] Required, string[] Optional, Selector Selector = Selector.None);

    private enum Selector { None, Type, Version, TypeOrFolder }

    private const string LibraryField = "library";

    private const string PortalConfirmationsField = "portalConfirmations";

    /// <summary>The update mode fields of update_library and update_project (declared before the specs that use them).</summary>
    private static readonly string[] UpdateModes = ["forceUpdateMode", "deleteUnusedVersionsMode", "structureConflictResolutionMode"];

    /// <summary>Every write item also takes <c>portalConfirmations</c>: each mutation runs in a TIA confirmation scope.</summary>
    private static Spec Write(string[] required, string[] optional, Selector selector = Selector.None)
        => new(required, [.. optional, PortalConfirmationsField], selector);

    private static readonly IReadOnlyDictionary<string, Spec> Reads = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        [LibraryMethods.ListLibraries] = new([], []),
        [LibraryMethods.ListGlobalLibraryInfos] = new([], []),
        [LibraryMethods.GetLibraryTree] = new([LibraryField], ["folderPath", "masterCopyFolderPath", "content", "includeVersions", "maxDepth", "offset", "limit"]),
        [LibraryMethods.GetLibraryType] = new([LibraryField], [], Selector.Type),
        [LibraryMethods.FindTypeInstances] = new([LibraryField], ["plcName"], Selector.Version),
        [LibraryMethods.GetInstanceInfo] = new([], ["blockPath", "plcTypePath"]),
        [LibraryMethods.UpdateCheck] = new([LibraryField], ["updateCheckMode"]),
        [LibraryMethods.CompareLibraries] = new([LibraryField, "targetLibrary"], ["maxDepth", "maxElements"]),
        [LibraryMethods.GetSupportedExportFormats] = new([LibraryField], [], Selector.Type),
        [LibraryMethods.ListSecondaryProjects] = new([], []),
        [LibraryMethods.ListPortalSettings] = new([], ["settingsFolderPath", "maxDepth"]),
        [LibraryMethods.CompareTypes] = new([LibraryField], ["targetLibrary", "targetTypePath", "targetTypeGuid", "maxElements"], Selector.Type),
        [LibraryMethods.CompareTypeVersions] = new([LibraryField], ["targetLibrary", "targetTypePath", "targetTypeGuid", "targetVersion", "targetVersionGuid", "maxElements"], Selector.Version),
        [LibraryMethods.CompareMasterCopies] = new([LibraryField, "masterCopyName", "targetMasterCopyName"],
            ["masterCopyFolderPath", "targetLibrary", "targetMasterCopyFolderPath", "maxElements"]),
    };

    private static readonly IReadOnlyDictionary<string, Spec> Writes = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        [LibraryMethods.OpenGlobalLibrary] = Write(["path"], ["openMode", "upgrade"]),
        [LibraryMethods.RetrieveGlobalLibrary] = Write(["path", "targetDirectory"], ["openMode", "upgrade"]),
        [LibraryMethods.CreateGlobalLibrary] = Write(["targetDirectory", "name"], []),
        [LibraryMethods.SaveGlobalLibrary] = Write([LibraryField], []),
        [LibraryMethods.CloseGlobalLibrary] = Write([LibraryField], []),
        [LibraryMethods.ArchiveGlobalLibrary] = Write([LibraryField, "targetDirectory", "name"], ["archivationMode"]),
        [LibraryMethods.SaveGlobalLibraryAs] = Write([LibraryField, "targetDirectory"], []),
        [LibraryMethods.OpenSecondaryProject] = Write(["path"], ["upgrade"]),
        [LibraryMethods.RetrieveSecondaryProject] = Write(["path", "targetDirectory"], ["upgrade"]),
        [LibraryMethods.CloseSecondaryProject] = Write(["secondaryProject"], []),
        [LibraryMethods.CreateMasterCopy] = Write([LibraryField, "source"], ["masterCopyFolderPath"]),
        [LibraryMethods.CopyMasterCopy] = Write([LibraryField, "masterCopyName", "targetLibrary"], ["masterCopyFolderPath", "targetMasterCopyFolderPath"]),
        [LibraryMethods.DeleteMasterCopy] = Write([LibraryField, "masterCopyName"], ["masterCopyFolderPath"]),
        [LibraryMethods.RenameMasterCopy] = Write([LibraryField, "masterCopyName", "newName"], ["masterCopyFolderPath"]),
        [LibraryMethods.InstantiateMasterCopy] = Write([LibraryField, "masterCopyName", "target"], ["masterCopyFolderPath", "masterCopyMode", "newName"]),
        [LibraryMethods.InstantiateTypeVersion] = Write([LibraryField, "target"], ["updatePathsMode"], Selector.Version),
        [LibraryMethods.UpdateProject] = Write([LibraryField], ["plcName", .. UpdateModes], Selector.TypeOrFolder),
        [LibraryMethods.RenameType] = Write([LibraryField, "newName"], [], Selector.Type),
        [LibraryMethods.RenameTypeFolder] = Write([LibraryField, "folderPath", "newName"], []),
        [LibraryMethods.EditTypeVersion] = Write([LibraryField], ["blockPath", "plcTypePath"], Selector.Version),
        [LibraryMethods.ReleaseTypeVersion] = Write([LibraryField, "versionNumber"], ["author", "comment", "dependenciesMode"], Selector.Version),
        [LibraryMethods.DiscardTypeVersion] = Write([LibraryField], [], Selector.Version),
        [LibraryMethods.SetDefaultTypeVersion] = Write([LibraryField], [], Selector.Version),
        [LibraryMethods.DeleteTypeVersion] = Write([LibraryField], [], Selector.Version),
        [LibraryMethods.CleanUpLibrary] = Write([LibraryField], ["cleanUpMode"], Selector.TypeOrFolder),
        [LibraryMethods.HarmonizeProject] = Write([LibraryField, "harmonizeOptions"], ["plcName"], Selector.TypeOrFolder),
        [LibraryMethods.UpdateLibrary] = Write([LibraryField, "targetLibrary"], UpdateModes, Selector.TypeOrFolder),
        [LibraryMethods.DeleteType] = Write([LibraryField], [], Selector.Type),
        [LibraryMethods.SetTypeDoNotUse] = Write([LibraryField, "doNotUse"], [], Selector.Type),
        [LibraryMethods.CreateTypeFolder] = Write([LibraryField, "newName"], ["folderPath"]),
        [LibraryMethods.DeleteTypeFolder] = Write([LibraryField, "folderPath"], []),
        [LibraryMethods.CreateMasterCopyFolder] = Write([LibraryField, "newName"], ["masterCopyFolderPath"]),
        [LibraryMethods.DeleteMasterCopyFolder] = Write([LibraryField, "masterCopyFolderPath"], []),
        [LibraryMethods.RenameMasterCopyFolder] = Write([LibraryField, "masterCopyFolderPath", "newName"], []),
        [LibraryMethods.CreateTypeFromDocuments] = Write([LibraryField, "documentDirectory", "documentName"], ["folderPath", "plcName", "libraryImportOptions"]),
        [LibraryMethods.CreateVersionFromDocuments] = Write([LibraryField, "documentDirectory", "documentName"], ["plcName", "createOption", "libraryImportOptions"], Selector.Type),
        [LibraryMethods.ExportTypeVersion] = Write([LibraryField, "filePath"], ["exportOptions", "documentInfoOptions", "overwrite"], Selector.Version),
        [LibraryMethods.SetPortalSetting] = Write(["settingsFolderPath", "settingName", "settingValue"], []),
        [LibraryMethods.ExportTypeVersionAsDocuments] = Write([LibraryField, "documentDirectory", "documentName", "exportFormat"], ["libraryExportOptions", "overwrite"], Selector.Version),
    };


    public static IReadOnlyList<string> ReadOperationNames { get; } = Reads.Keys.ToArray();

    public static IReadOnlyList<string> WriteOperationNames { get; } = Writes.Keys.ToArray();

    public static OperationValidation ValidateRead(IReadOnlyList<LibraryOperationRequest>? operations)
        => Validate(operations, Reads, Writes, LibraryMethods.ReadTool, LibraryMethods.WriteTool);

    public static OperationValidation ValidateWrite(IReadOnlyList<LibraryOperationRequest>? operations)
        => Validate(operations, Writes, Reads, LibraryMethods.WriteTool, LibraryMethods.ReadTool);

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<LibraryOperationRequest> operations, McpAccessMode mode)
        => operations.Where(item => item?.Operation is not null && (Reads.ContainsKey(item.Operation) || Writes.ContainsKey(item.Operation))
                && !OperationPolicyCatalog.IsAllowed(mode, LibraryMethods.ForOperation(item.Operation)))
            .Select(item => $"Operation '{item.Operation}' (operationId '{item.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.").ToArray();

    private static OperationValidation Validate(IReadOnlyList<LibraryOperationRequest>? operations,
        IReadOnlyDictionary<string, Spec> own, IReadOnlyDictionary<string, Spec> other, string tool, string otherTool)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError) { errors.Add(itemError); continue; }
            void Error(string message) => errors.Add($"Operation '{item.Operation}' (operationId '{item.OperationId}'): {message}");
            if (string.IsNullOrWhiteSpace(item.Operation) || !own.TryGetValue(item.Operation, out var spec))
            {
                Error(item.Operation is not null && other.ContainsKey(item.Operation) ? $"use {otherTool}." : $"unknown {tool} operation.");
                continue;
            }

            ValidateItem(item, spec, Error);
        }

        return errors.Count == 0 ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    private static (string Name, object? Value)[] Fields(LibraryOperationRequest item) => new (string, object?)[]
    {
        (LibraryField, item.Library), ("targetLibrary", item.TargetLibrary), ("path", item.Path), ("targetDirectory", item.TargetDirectory),
        ("name", item.Name), ("openMode", item.OpenMode), ("upgrade", item.Upgrade), ("folderPath", item.FolderPath),
        ("typePath", item.TypePath), ("typeGuid", item.TypeGuid), ("version", item.Version), ("versionGuid", item.VersionGuid),
        ("masterCopyName", item.MasterCopyName), ("newName", item.NewName), ("plcName", item.PlcName), ("blockPath", item.BlockPath),
        ("plcTypePath", item.PlcTypePath), ("source", item.Source), ("target", item.Target), ("masterCopyMode", item.MasterCopyMode),
        ("updatePathsMode", item.UpdatePathsMode), ("dependenciesMode", item.DependenciesMode), ("versionNumber", item.VersionNumber),
        ("author", item.Author), ("comment", item.Comment), ("updateCheckMode", item.UpdateCheckMode), ("content", item.Content),
        ("includeVersions", item.IncludeVersions), ("maxDepth", item.MaxDepth), ("maxElements", item.MaxElements),
        ("offset", item.Offset), ("limit", item.Limit), (PortalConfirmationsField, item.PortalConfirmations),
        ("masterCopyFolderPath", item.MasterCopyFolderPath), ("targetMasterCopyFolderPath", item.TargetMasterCopyFolderPath),
        ("targetTypePath", item.TargetTypePath), ("targetTypeGuid", item.TargetTypeGuid), ("targetVersion", item.TargetVersion),
        ("targetVersionGuid", item.TargetVersionGuid), ("targetMasterCopyName", item.TargetMasterCopyName), ("archivationMode", item.ArchivationMode),
        ("cleanUpMode", item.CleanUpMode), ("harmonizeOptions", item.HarmonizeOptions), ("forceUpdateMode", item.ForceUpdateMode),
        ("deleteUnusedVersionsMode", item.DeleteUnusedVersionsMode), ("structureConflictResolutionMode", item.StructureConflictResolutionMode),
        ("doNotUse", item.DoNotUse), ("documentDirectory", item.DocumentDirectory), ("documentName", item.DocumentName),
        ("exportFormat", item.ExportFormat), ("libraryImportOptions", item.LibraryImportOptions), ("libraryExportOptions", item.LibraryExportOptions),
        ("createOption", item.CreateOption), ("exportOptions", item.ExportOptions), ("documentInfoOptions", item.DocumentInfoOptions),
        ("filePath", item.FilePath), ("overwrite", item.Overwrite), ("secondaryProject", item.SecondaryProject),
        ("settingsFolderPath", item.SettingsFolderPath), ("settingName", item.SettingName), ("settingValue", item.SettingValue),
    };

    private static readonly string[] TypeFields = ["typePath", "typeGuid"];
    private static readonly string[] VersionFields = ["typePath", "typeGuid", "version", "versionGuid"];
    private static readonly string[] TypeOrFolderFields = ["typePath", "typeGuid", "folderPath"];

    private static void ValidateItem(LibraryOperationRequest item, Spec spec, Action<string> error)
    {
        var selectorFields = spec.Selector switch
        {
            Selector.Type => TypeFields,
            Selector.Version => VersionFields,
            Selector.TypeOrFolder => TypeOrFolderFields,
            _ => Array.Empty<string>(),
        };
        foreach (var (name, value) in Fields(item))
        {
            if (value is null) { if (spec.Required.Contains(name)) error($"'{name}' is required."); continue; }
            if (!spec.Required.Contains(name) && !spec.Optional.Contains(name) && !selectorFields.Contains(name))
                error($"'{name}' is not valid for this operation.");
        }

        SelectorRules(item, spec.Selector, error);
        Library(item.Library, LibraryField, error);
        Library(item.TargetLibrary, "targetLibrary", error);
        if (item.Library is not null && item.TargetLibrary is not null && LibraryPolicy.SamePath(item.Library, item.TargetLibrary))
            error("targetLibrary must name a different library than library.");
        // create_global_library's name becomes a directory and file name below targetDirectory; object names do not.
        if (item.Name is not null && LibraryPolicy.LibraryFileNameError(item.Name) is { } fileNameError) error($"name {fileNameError}");
        Name(item.NewName, "newName", error);
        Name(item.MasterCopyName, "masterCopyName", error);
        File(item.Path, "path", error);
        File(item.TargetDirectory, "targetDirectory", error);
        Folder(item.FolderPath, "folderPath", error);
        Member(item.OpenMode, "openMode", LibraryPolicy.OpenModes, error);
        Member(item.MasterCopyMode, "masterCopyMode", LibraryPolicy.MasterCopyModes, error);
        Member(item.UpdatePathsMode, "updatePathsMode", LibraryPolicy.UpdatePathsModes, error);
        Member(item.DependenciesMode, "dependenciesMode", LibraryPolicy.DependenciesModes, error);
        Member(item.UpdateCheckMode, "updateCheckMode", LibraryPolicy.UpdateCheckModes, error);
        Member(item.Content, "content", LibraryPolicy.TreeContents.Values, error);
        Member(item.PortalConfirmations, PortalConfirmationsField, PlcDownloadPolicy.PortalConfirmationPolicies.All, error);
        Member(item.ArchivationMode, "archivationMode", LibraryPolicy.ArchivationModes, error);
        Member(item.CleanUpMode, "cleanUpMode", LibraryPolicy.CleanUpModes, error);
        Member(item.ForceUpdateMode, "forceUpdateMode", LibraryPolicy.ForceUpdateModes, error);
        Member(item.DeleteUnusedVersionsMode, "deleteUnusedVersionsMode", LibraryPolicy.DeleteUnusedVersionsModes, error);
        Member(item.StructureConflictResolutionMode, "structureConflictResolutionMode", LibraryPolicy.StructureConflictResolutionModes, error);
        Member(item.LibraryImportOptions, "libraryImportOptions", LibraryPolicy.LibraryImportOptions, error);
        Member(item.LibraryExportOptions, "libraryExportOptions", LibraryPolicy.LibraryExportOptions, error);
        Member(item.CreateOption, "createOption", LibraryPolicy.CreateOptions, error);
        Member(item.ExportOptions, "exportOptions", LibraryPolicy.ExportOptions, error);
        Member(item.DocumentInfoOptions, "documentInfoOptions", LibraryPolicy.DocumentInfoOptions, error);
        if (item.HarmonizeOptions is { } harmonize)
        {
            if (harmonize.Length is < 1 or > 2 || harmonize.Distinct(StringComparer.Ordinal).Count() != harmonize.Length)
                error($"harmonizeOptions must name 1 or 2 distinct members of {string.Join(", ", LibraryPolicy.HarmonizeOptions)}.");
            foreach (var option in harmonize) Member(option, "harmonizeOptions", LibraryPolicy.HarmonizeOptions, error);
        }

        Folder(item.MasterCopyFolderPath, "masterCopyFolderPath", error);
        Folder(item.TargetMasterCopyFolderPath, "targetMasterCopyFolderPath", error);
        Name(item.TargetMasterCopyName, "targetMasterCopyName", error);
        File(item.DocumentDirectory, "documentDirectory", error);
        File(item.FilePath, "filePath", error);
        File(item.SecondaryProject, "secondaryProject", error);
        Text(item.SettingsFolderPath, "settingsFolderPath", error);
        Text(item.SettingName, "settingName", error);
        Text(item.SettingValue, "settingValue", error, allowEmpty: true);
        if (item.DocumentName is not null && LibraryPolicy.LibraryFileNameError(item.DocumentName) is { } documentError) error($"documentName {documentError}");
        Text(item.ExportFormat, "exportFormat", error);
        TargetSelectorRules(item, error);
        Text(item.PlcName, "plcName", error);
        Text(item.BlockPath, "blockPath", error);
        Text(item.PlcTypePath, "plcTypePath", error);
        Text(item.Author, "author", error, allowEmpty: true);
        Text(item.Comment, "comment", error, allowEmpty: true);
        if (item.VersionNumber is not null && !LibraryPolicy.IsVersionNumber(item.VersionNumber))
            error("versionNumber must be a version with two to four numeric components, for example 2.0.0.");
        if (item.Offset < 0) error("offset must be nonnegative.");
        if (item.Limit is < 1 or > LibraryPolicy.MaxPageLimit) error($"limit must be 1 to {LibraryPolicy.MaxPageLimit}.");
        if (item.MaxElements is < 1 or > LibraryPolicy.MaxCompareElements) error($"maxElements must be 1 to {LibraryPolicy.MaxCompareElements}.");
        var maxDepth = item.Operation == LibraryMethods.CompareLibraries ? LibraryPolicy.MaxCompareDepth : LibraryPolicy.MaxTreeDepth;
        if (item.MaxDepth is { } depth && (depth < 1 || depth > maxDepth)) error($"maxDepth must be 1 to {maxDepth}.");

        switch (item.Operation)
        {
            case LibraryMethods.GetInstanceInfo when (item.BlockPath is null) == (item.PlcTypePath is null):
                error("name exactly one of blockPath or plcTypePath.");
                break;
            case LibraryMethods.EditTypeVersion when item.BlockPath is not null && item.PlcTypePath is not null:
                error("name at most one instance (blockPath or plcTypePath) for Edit(LibraryTypeInstanceInfo).");
                break;
            case LibraryMethods.OpenGlobalLibrary:
                if (item.Upgrade == true && item.OpenMode is not null) error("openMode is not valid with upgrade:true (OpenWithUpgrade takes no OpenMode).");
                if (LibraryPolicy.IsArchive(item.Path)) error("path names an archived library (.zalNN); use retrieve_global_library.");
                break;
            case LibraryMethods.SaveGlobalLibrary or LibraryMethods.CloseGlobalLibrary when LibraryPolicy.IsProject(item.Library):
                error("names a global library; the project library is saved and closed with the project.");
                break;
            case LibraryMethods.RenameTypeFolder or LibraryMethods.DeleteTypeFolder
                when item.FolderPath is not null && LibraryPolicy.ParseFolderPath(item.FolderPath, out _) is { Count: 0 }:
                error("folderPath must name a user type folder; the root type folder cannot be renamed or deleted.");
                break;
            case LibraryMethods.RenameMasterCopyFolder or LibraryMethods.DeleteMasterCopyFolder
                when item.MasterCopyFolderPath is not null && LibraryPolicy.ParseFolderPath(item.MasterCopyFolderPath, out _) is { Count: 0 }:
                error("masterCopyFolderPath must name a master copy user folder; the root master copy folder cannot be renamed or deleted.");
                break;
            case LibraryMethods.ArchiveGlobalLibrary or LibraryMethods.SaveGlobalLibraryAs when LibraryPolicy.IsProject(item.Library):
                error("names a user global library; the project library is archived and saved with the project.");
                break;
            case LibraryMethods.CleanUpLibrary when item.CleanUpMode is not null && item.Library is not null && !LibraryPolicy.IsProject(item.Library):
                error("cleanUpMode is valid for the project library only (UserGlobalLibrary.CleanUpLibrary takes no CleanUpMode).");
                break;
            case LibraryMethods.UpdateProject when LibraryPolicy.IsProject(item.Library) && (item.ForceUpdateMode is not null || item.StructureConflictResolutionMode is not null):
                error("the project library's UpdateProject overload takes only deleteUnusedVersionsMode (no forceUpdateMode or structureConflictResolutionMode).");
                break;
            case LibraryMethods.UpdateProject when !LibraryPolicy.IsProject(item.Library) && ModeCount(item) is 1 or 2:
            case LibraryMethods.UpdateLibrary when ModeCount(item) is 1 or 2:
                error("give forceUpdateMode, deleteUnusedVersionsMode and structureConflictResolutionMode together (the overload takes all three) or none.");
                break;
            case LibraryMethods.CreateMasterCopy when item.Source is not null:
                SourceRules(item.Source, error);
                break;
            case LibraryMethods.InstantiateMasterCopy when item.Target is not null:
                TargetRules(item.Target, LibraryPolicy.TargetKinds.MasterCopyTargets, error);
                if (item.MasterCopyMode is not null && !LibraryPolicy.TargetKinds.TakesMasterCopyMode(item.Target.Kind))
                    error($"masterCopyMode is not valid for target kind {item.Target.Kind} (its documented CreateFrom takes no MasterCopyMode).");
                break;
            case LibraryMethods.InstantiateTypeVersion when item.Target is not null:
                TargetRules(item.Target, LibraryPolicy.TargetKinds.TypeVersionTargets, error);
                break;
        }
    }

    private static void SelectorRules(LibraryOperationRequest item, Selector selector, Action<string> error)
    {
        if (item.TypePath is not null && item.TypeGuid is not null) error("name a type by typePath or typeGuid, not both.");
        if (item.TypePath is not null && LibraryPolicy.ParseTypePath(item.TypePath, out var typeError) is null) error($"typePath: {typeError}");
        if (item.TypeGuid is not null && !LibraryPolicy.IsGuid(item.TypeGuid)) error("typeGuid must be a GUID.");
        if (item.VersionGuid is not null && !LibraryPolicy.IsGuid(item.VersionGuid)) error("versionGuid must be a GUID.");
        if (item.Version is not null && (string.IsNullOrWhiteSpace(item.Version) || item.Version.Length > LibraryPolicy.MaxNameLength))
            error("version must be a nonblank version number as listed.");
        var hasType = item.TypePath is not null || item.TypeGuid is not null;
        switch (selector)
        {
            case Selector.Type when !hasType:
                error("name the type with typePath or typeGuid.");
                break;
            case Selector.Version:
                if (item.Version is not null && item.VersionGuid is not null) error("name a version by version or versionGuid, not both.");
                if (item.Version is not null && !hasType) error("version needs the type (typePath or typeGuid).");
                if (!hasType && item.VersionGuid is null) error("name the type (typePath or typeGuid) or the version (versionGuid).");
                break;
            case Selector.TypeOrFolder when (hasType ? 1 : 0) + (item.FolderPath is null ? 0 : 1) != 1:
                error("select exactly one type (typePath or typeGuid) or one type folder (folderPath).");
                break;
        }
    }

    private static int ModeCount(LibraryOperationRequest item)
        => (item.ForceUpdateMode is null ? 0 : 1) + (item.DeleteUnusedVersionsMode is null ? 0 : 1) + (item.StructureConflictResolutionMode is null ? 0 : 1);

    /// <summary>The compared object of compare_types, compare_type_versions (target* selectors).</summary>
    private static void TargetSelectorRules(LibraryOperationRequest item, Action<string> error)
    {
        if (item.TargetTypePath is not null && item.TargetTypeGuid is not null) error("name the compared type by targetTypePath or targetTypeGuid, not both.");
        if (item.TargetTypePath is not null && LibraryPolicy.ParseTypePath(item.TargetTypePath, out var typeError) is null) error($"targetTypePath: {typeError}");
        if (item.TargetTypeGuid is not null && !LibraryPolicy.IsGuid(item.TargetTypeGuid)) error("targetTypeGuid must be a GUID.");
        if (item.TargetVersionGuid is not null && !LibraryPolicy.IsGuid(item.TargetVersionGuid)) error("targetVersionGuid must be a GUID.");
        if (item.TargetVersion is not null && (string.IsNullOrWhiteSpace(item.TargetVersion) || item.TargetVersion.Length > LibraryPolicy.MaxNameLength))
            error("targetVersion must be a nonblank version number as listed.");
        var hasType = item.TargetTypePath is not null || item.TargetTypeGuid is not null;
        switch (item.Operation)
        {
            case LibraryMethods.CompareTypes when !hasType:
                error("name the compared type with targetTypePath or targetTypeGuid.");
                break;
            case LibraryMethods.CompareTypeVersions:
                if (item.TargetVersion is not null && item.TargetVersionGuid is not null) error("name the compared version by targetVersion or targetVersionGuid, not both.");
                if (item.TargetVersion is not null && !hasType) error("targetVersion needs the compared type (targetTypePath or targetTypeGuid).");
                if (!hasType && item.TargetVersionGuid is null) error("name the compared type (targetTypePath or targetTypeGuid) or version (targetVersionGuid).");
                break;
        }
    }

    private static void SourceRules(LibrarySourceInfo source, Action<string> error)
    {
        if (!LibraryPolicy.SourceKinds.Values.Contains(source.Kind, StringComparer.Ordinal))
            error($"source.kind must be one of {string.Join(", ", LibraryPolicy.SourceKinds.Values)}.");
        Text(source.Path, "source.path", error);
        if (source.Path is null) error("source.path is required.");
        if (source.PlcName is not null && !LibraryPolicy.SourceKinds.TakesPlcName(source.Kind))
            error("source.plcName is valid for kinds tagTable and textList only (block and PLC data type paths carry the PLC; devices belong to the project).");
        Text(source.PlcName, "source.plcName", error);
        if (source.Project is not null && LibraryPolicy.FilePathError(source.Project) is { } projectError)
            error($"source.project must be the absolute path of an open secondary project; it {projectError}");
        if (source.Kind == LibraryPolicy.SourceKinds.TagTable && source.Path is not null && LibraryPolicy.ParseTypePath(source.Path, out var tableError) is null)
            error($"source.path: {tableError}");
    }

    private static void TargetRules(LibraryTargetInfo target, IReadOnlyList<string> kinds, Action<string> error)
    {
        if (!kinds.Contains(target.Kind, StringComparer.Ordinal))
        {
            error($"target.kind must be one of {string.Join(", ", kinds)}.");
            return;
        }

        if (target.PlcName is not null && !LibraryPolicy.TargetKinds.NeedsPlc(target.Kind)) error($"target.plcName is not valid for kind {target.Kind}.");
        Text(target.PlcName, "target.plcName", error);
        if (target.FolderPath is not null && !LibraryPolicy.TargetKinds.HasFolder(target.Kind)) error($"target.folderPath is not valid for kind {target.Kind}.");
        Folder(target.FolderPath, "target.folderPath", error);
        if (target.Kind == LibraryPolicy.TargetKinds.DeviceItem)
        {
            if (target.Parent is null) error("target.parent is required for kind deviceItem.");
            else
            {
                Text(target.Parent.DeviceName, "target.parent.deviceName", error);
                if (target.Parent.ItemPath is not { Count: > 0 } || target.Parent.ItemPath.Any(segment => segment is null || string.IsNullOrWhiteSpace(segment.Name)))
                    error("target.parent.itemPath must name the parent device item with nonblank segments.");
            }
        }
        else if (target.Parent is not null) error($"target.parent is valid for kind deviceItem only.");
    }

    private static void Library(string? library, string field, Action<string> error)
    {
        if (library is null || LibraryPolicy.IsProject(library)) return;
        if (LibraryPolicy.FilePathError(library) is { } pathError) error($"{field} must be 'project' or the absolute path of an open global library; it {pathError}");
    }

    private static void Name(string? value, string field, Action<string> error)
    {
        if (value is not null && LibraryPolicy.NameError(value) is { } nameError) error($"{field} {nameError}");
    }

    private static void File(string? value, string field, Action<string> error)
    {
        if (value is not null && LibraryPolicy.FilePathError(value) is { } pathError) error($"{field} {pathError}");
    }

    private static void Folder(string? value, string field, Action<string> error)
    {
        if (value is not null && LibraryPolicy.ParseFolderPath(value, out var folderError) is null) error($"{field}: {folderError}");
    }

    private static void Member(string? value, string field, IReadOnlyList<string> members, Action<string> error)
    {
        if (value is not null && !members.Contains(value, StringComparer.Ordinal)) error($"{field} must be one of {string.Join(", ", members)}.");
    }

    private static void Text(string? value, string field, Action<string> error, bool allowEmpty = false)
    {
        if (value is null) return;
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || value.Length > LibraryPolicy.MaxPathLength || value.Any(char.IsControl))
            error($"{field} must be {(allowEmpty ? "" : "nonblank ")}text of at most {LibraryPolicy.MaxPathLength} characters without control characters.");
    }

    /// <summary>The worker request of one item; omitted options take the documented defaults.</summary>
    public static LibraryRequestInfo ToRequest(LibraryOperationRequest item) => new()
    {
        Operation = item.Operation, Library = item.Library, TargetLibrary = item.TargetLibrary, Path = item.Path,
        TargetDirectory = item.TargetDirectory, Name = item.Name, OpenMode = item.OpenMode, Upgrade = item.Upgrade ?? false,
        FolderPath = item.FolderPath, TypePath = item.TypePath, TypeGuid = item.TypeGuid, Version = item.Version,
        VersionGuid = item.VersionGuid, MasterCopyName = item.MasterCopyName, NewName = item.NewName, PlcName = item.PlcName,
        BlockPath = item.BlockPath, PlcTypePath = item.PlcTypePath,
        Source = item.Source is null ? null : new LibrarySourceInfo { Kind = item.Source.Kind, PlcName = item.Source.PlcName, Path = item.Source.Path, Project = item.Source.Project },
        Target = item.Target is null ? null : new LibraryTargetInfo
        {
            Kind = item.Target.Kind, PlcName = item.Target.PlcName, FolderPath = item.Target.FolderPath,
            Parent = item.Target.Parent is null ? null : new Contracts.Network.HardwareObjectSelectorInfo
            {
                DeviceName = item.Target.Parent.DeviceName,
                ItemPath = item.Target.Parent.ItemPath?.Select(segment => new Contracts.Network.DeviceItemPathSegmentInfo
                {
                    Index = segment.Index, Name = segment.Name, PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier,
                }).ToList(),
            },
        },
        MasterCopyMode = item.MasterCopyMode, UpdatePathsMode = item.UpdatePathsMode, DependenciesMode = item.DependenciesMode,
        VersionNumber = item.VersionNumber, Author = item.Author, Comment = item.Comment, UpdateCheckMode = item.UpdateCheckMode,
        Content = item.Content, IncludeVersions = item.IncludeVersions ?? true,
        MaxDepth = item.MaxDepth ?? (item.Operation == LibraryMethods.CompareLibraries ? LibraryPolicy.DefaultCompareDepth : LibraryPolicy.DefaultTreeDepth),
        MaxElements = item.MaxElements ?? LibraryPolicy.DefaultCompareElements,
        Offset = item.Offset ?? 0, Limit = item.Limit ?? LibraryPolicy.DefaultPageLimit,
        PortalConfirmations = LibraryMethods.IsWriteMethod(LibraryMethods.ForOperation(item.Operation)) ? PortalConfirmationPolicy(item) : null,
        MasterCopyFolderPath = item.MasterCopyFolderPath, TargetMasterCopyFolderPath = item.TargetMasterCopyFolderPath,
        TargetTypePath = item.TargetTypePath, TargetTypeGuid = item.TargetTypeGuid, TargetVersion = item.TargetVersion,
        TargetVersionGuid = item.TargetVersionGuid, TargetMasterCopyName = item.TargetMasterCopyName,
        ArchivationMode = item.Operation == LibraryMethods.ArchiveGlobalLibrary ? item.ArchivationMode ?? LibraryPolicy.DefaultArchivationMode : item.ArchivationMode,
        CleanUpMode = item.Operation == LibraryMethods.CleanUpLibrary && LibraryPolicy.IsProject(item.Library) ? item.CleanUpMode ?? LibraryPolicy.DefaultCleanUpMode : item.CleanUpMode,
        HarmonizeOptions = item.HarmonizeOptions?.ToList(), ForceUpdateMode = item.ForceUpdateMode,
        DeleteUnusedVersionsMode = item.DeleteUnusedVersionsMode, StructureConflictResolutionMode = item.StructureConflictResolutionMode,
        DoNotUse = item.DoNotUse, DocumentDirectory = item.DocumentDirectory, DocumentName = item.DocumentName, ExportFormat = item.ExportFormat,
        LibraryImportOptions = item.Operation is LibraryMethods.CreateTypeFromDocuments or LibraryMethods.CreateVersionFromDocuments
            ? item.LibraryImportOptions ?? LibraryPolicy.DefaultLibraryImportOptions : item.LibraryImportOptions,
        LibraryExportOptions = item.Operation == LibraryMethods.ExportTypeVersionAsDocuments ? item.LibraryExportOptions ?? LibraryPolicy.DefaultLibraryExportOptions : item.LibraryExportOptions,
        CreateOption = item.Operation == LibraryMethods.CreateVersionFromDocuments ? item.CreateOption ?? LibraryPolicy.DefaultCreateOption : item.CreateOption,
        ExportOptions = item.Operation == LibraryMethods.ExportTypeVersion ? item.ExportOptions ?? LibraryPolicy.DefaultExportOptions : item.ExportOptions,
        DocumentInfoOptions = item.DocumentInfoOptions, FilePath = item.FilePath, Overwrite = item.Overwrite ?? false,
        SecondaryProject = item.SecondaryProject,
        SettingsFolderPath = item.SettingsFolderPath, SettingName = item.SettingName, SettingValue = item.SettingValue,
    };

    /// <summary>The <c>portalConfirmations</c> policy a write item runs under (omitted: <c>decline</c>).</summary>
    public static string PortalConfirmationPolicy(LibraryOperationRequest item)
        => item.PortalConfirmations ?? PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>The library an item acts on, for ordering and supersession: global libraries by normalized path.</summary>
    public static string? LibraryKey(LibraryOperationRequest item)
        => item.Operation is LibraryMethods.OpenSecondaryProject ? SecondaryKey(item.Path)
            : item.Operation is LibraryMethods.RetrieveSecondaryProject ? null
            : item.Operation is LibraryMethods.CloseSecondaryProject ? SecondaryKey(item.SecondaryProject)
            : item.Operation is LibraryMethods.OpenGlobalLibrary ? Key(item.Path)
            : item.Operation is LibraryMethods.RetrieveGlobalLibrary or LibraryMethods.CreateGlobalLibrary ? null
            : Key(item.Library);

    /// <summary>The key of a secondary project (its own namespace: a secondary project is never a library).</summary>
    public static string? SecondaryKey(string? path) => path is null ? null : "SECONDARY\n" + LibraryPolicy.NormalizePath(path).ToUpperInvariant();

    public static string? Key(string? library)
        => library is null ? null : LibraryPolicy.IsProject(library) ? LibraryPolicy.ProjectLibrary : LibraryPolicy.NormalizePath(library).ToUpperInvariant();
}
