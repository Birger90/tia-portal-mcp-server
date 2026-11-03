using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Library;

/// <summary>One library_read or library_write item. Only the fields an operation documents are accepted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class LibraryOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;

    [Description("'project' for the bound project's library, or the path of an open global library exactly as list_libraries reports it.")]
    public string? Library { get; set; }

    [Description("copy_master_copy, update_library: the destination library; compare_*: the library compared with (compare_types/_type_versions/_master_copies: omitted means library) (same selector form as library).")]
    public string? TargetLibrary { get; set; }

    [Description("open_global_library: absolute path of the library file (for a system or corporate library the path list_global_library_infos returns; it is opened by its GlobalLibraryInfo, read-only); retrieve_global_library: absolute path of the archive (.zalNN); open_secondary_project: the project file (.apNN); retrieve_secondary_project: the project archive.")]
    public string? Path { get; set; }

    [Description("retrieve_global_library, create_global_library, archive_global_library, save_global_library_as: absolute directory the library or archive is written to.")]
    public string? TargetDirectory { get; set; }

    [Description("create_global_library: the new library's name; archive_global_library: the archive file name (targetName).")]
    public string? Name { get; set; }

    [Description("open_global_library, retrieve_global_library: OpenMode member name ReadOnly (default) or ReadWrite.")]
    public string? OpenMode { get; set; }

    [Description("open_global_library: true opens with OpenWithUpgrade (upgrades an older library in place; openMode not allowed); retrieve_global_library: true retrieves with RetrieveWithUpgrade(FileInfo, DirectoryInfo, OpenMode). Default false.")]
    public bool? Upgrade { get; set; }

    [Description("Type folder path below the type folder root ('/' or '/A/B'): get_library_tree start; rename_/delete_type_folder target; parent of create_type_folder and create_type_from_documents; selection of update_project, update_library, clean_up_library, harmonize_project.")]
    public string? FolderPath { get; set; }

    [Description("Library type path '/Folder/Type' (or 'Type' in the root type folder).")]
    public string? TypePath { get; set; }

    [Description("Library type GUID (ILibrary.FindType); alternative to typePath.")]
    public string? TypeGuid { get; set; }

    [Description("Exact version number as listed (e.g. 1.0.2). Optional when the type has exactly one version.")]
    public string? Version { get; set; }

    [Description("Version GUID (ILibrary.FindVersion); alternative to version.")]
    public string? VersionGuid { get; set; }

    [Description("Name of a master copy (in masterCopyFolderPath, omitted: the root master copy folder).")]
    public string? MasterCopyName { get; set; }

    [Description("rename_*: the new name; create_type_folder, create_master_copy_folder: the new folder's name; instantiate_master_copy: rename the single created object after placement.")]
    public string? NewName { get; set; }

    [Description("find_type_instances, update_project, harmonize_project: PLC software or device name, optional when the project has exactly one PLC; create_*_from_documents: the PLC whose software is the targetEnvironment (omitted: the overload without one).")]
    public string? PlcName { get; set; }

    [Description("get_instance_info: a PLC block path as plc_read uses it; edit_type_version: the instance passed to Edit(LibraryTypeInstanceInfo) (types that need an instance to be edited).")]
    public string? BlockPath { get; set; }

    [Description("get_instance_info: a PLC data type path as plc_read uses it; edit_type_version: the instance passed to Edit(LibraryTypeInstanceInfo).")]
    public string? PlcTypePath { get; set; }

    [Description("create_master_copy: the project object to capture: { kind: block|plcType|tagTable|device|textList, path, plcName (tagTable and textList only), project (optional: the path of an open secondary project to copy from; omitted: the bound project) }; a textList path is the user text list name.")]
    public LibrarySourceInfo? Source { get; set; }

    [Description("instantiate_*: where to create the instance: { kind, plcName, folderPath, parent }. kind blockGroup|typeGroup|tagTableGroup|technologyObjects|softwareUnits|externalSources|textLists (PLC; folderPath for the groups; textLists creates a user text list), devices (project), deviceItem (parent = network_read hardware selector).")]
    public LibraryTargetInfo? Target { get; set; }

    [Description("instantiate_master_copy into a PLC target: MasterCopyMode member name ThrowIfExists (default), Rename or Replace.")]
    public string? MasterCopyMode { get; set; }

    [Description("instantiate_type_version: UpdatePathsMode member name ThrowIfPathsConflict (default), KeepExistingPathsInTarget or UpdatePathsInTarget.")]
    public string? UpdatePathsMode { get; set; }

    [Description("release_type_version: CreateOrReleaseDependenciesMode member name; only DoNotAutomaticallyCreateOrReleaseDependencies (default) is documented.")]
    public string? DependenciesMode { get; set; }

    [Description("release_type_version: the released version number (System.Version text, e.g. 2.0.0).")]
    public string? VersionNumber { get; set; }

    [Description("release_type_version: the third Release argument (the source example passes an author).")]
    public string? Author { get; set; }

    [Description("release_type_version: the fourth Release argument (the source example passes a comment).")]
    public string? Comment { get; set; }

    [Description("update_check: UpdateCheckMode member name; only ReportOutOfDateOnly (default) is documented.")]
    public string? UpdateCheckMode { get; set; }

    [Description("library_write items: how TIA Portal confirmation dialogs raised while the item runs are answered: decline (default: No, "
        + "recorded, the item fails) or accept (Yes, recorded, a warning). Recorded in result.value.portalConfirmations.")]
    public string? PortalConfirmations { get; set; }

    [Description("list_portal_settings: the TIA Portal settings folder to start at ('A/B'; omitted: every top-level folder); set_portal_setting: the folder of the setting. Names as list_portal_settings reports them.")]
    public string? SettingsFolderPath { get; set; }

    [Description("set_portal_setting: the setting's name (TiaPortalSetting.Name).")]
    public string? SettingName { get; set; }

    [Description("set_portal_setting: the new value as text: true/false, a number, an enum member name (list_portal_settings lists enumMembers) or text, converted to the type of the current value.")]
    public string? SettingValue { get; set; }

    [Description("close_secondary_project: the secondary project's path exactly as list_secondary_projects reports it.")]
    public string? SecondaryProject { get; set; }

    [Description("Master copy folder below the root master copy folder ('/' or '/A/B'): where the master copy of create/copy/delete/rename/instantiate/compare_master_copies is (create_master_copy: created), the parent of create_master_copy_folder, the folder of delete_/rename_master_copy_folder, the start of get_library_tree's master copy walk. Omitted: the root.")]
    public string? MasterCopyFolderPath { get; set; }

    [Description("copy_master_copy: the destination master copy folder in targetLibrary; compare_master_copies: the folder of targetMasterCopyName. Omitted: the root.")]
    public string? TargetMasterCopyFolderPath { get; set; }

    [Description("compare_types, compare_type_versions: the compared type in targetLibrary (omitted: library) by path; alternative targetTypeGuid.")]
    public string? TargetTypePath { get; set; }

    [Description("compare_types, compare_type_versions: the compared type by GUID.")]
    public string? TargetTypeGuid { get; set; }

    [Description("compare_type_versions: the compared version number (optional when the compared type has one version); alternative targetVersionGuid.")]
    public string? TargetVersion { get; set; }

    [Description("compare_type_versions: the compared version by GUID.")]
    public string? TargetVersionGuid { get; set; }

    [Description("compare_master_copies: the compared master copy (in targetLibrary, omitted: library).")]
    public string? TargetMasterCopyName { get; set; }

    [Description("archive_global_library: LibraryArchivationMode None, Compressed (default), DiscardRestorableData or DiscardRestorableDataAndCompressed.")]
    public string? ArchivationMode { get; set; }

    [Description("clean_up_library on the project library: CleanUpMode PreserveDefaultVersionOfUnusedTypes (default) or DeleteUnusedTypes; not valid for a global library (its CleanUpLibrary takes no mode).")]
    public string? CleanUpMode { get; set; }

    [Description("harmonize_project: HarmonizeProjectOptions members HarmonizePaths and/or HarmonizeNames (both only if the live enum is [Flags]).")]
    public string[]? HarmonizeOptions { get; set; }

    [Description("update_library; update_project from a global library: ForceUpdateMode ForceSetAnyUpdatedVersionAsDefault, NoDefaultVersionChange or SetOnlyHigherUpdatedVersionAsDefault (give all three modes or none).")]
    public string? ForceUpdateMode { get; set; }

    [Description("update_library, update_project: DeleteUnusedVersionsMode AutomaticallyDelete or DoNotDelete (update_project from the project library takes only this mode).")]
    public string? DeleteUnusedVersionsMode { get; set; }

    [Description("update_library; update_project from a global library: StructureConflictResolutionMode CancelIfStructureConflicts, RetainStructure or UpdateStructure.")]
    public string? StructureConflictResolutionMode { get; set; }

    [Description("set_type_do_not_use: the new LibraryType.DoNotUse value.")]
    public bool? DoNotUse { get; set; }

    [Description("create_type_from_documents, create_version_from_documents: absolute directory holding the documents; export_type_version_as_documents: absolute export directory (must exist).")]
    public string? DocumentDirectory { get; set; }

    [Description("The documents' file name without extension (fileNameWithoutExtension).")]
    public string? DocumentName { get; set; }

    [Description("export_type_version_as_documents: an export format from get_supported_export_formats.")]
    public string? ExportFormat { get; set; }

    [Description("create_*_from_documents: LibraryImportOptions None (default), ActivateInactiveCultures or SkipInactiveCultures.")]
    public string? LibraryImportOptions { get; set; }

    [Description("export_type_version_as_documents: LibraryExportOptions None (default), WithLibraryVersionInfoFile or OnlyLibraryVersionInfoFile.")]
    public string? LibraryExportOptions { get; set; }

    [Description("create_version_from_documents: CreateOptions None (default: a new version) or Override (overwrite the current in-work/in-test version).")]
    public string? CreateOption { get; set; }

    [Description("export_type_version: ExportOptions None (default), WithDefaults or WithReadOnly.")]
    public string? ExportOptions { get; set; }

    [Description("export_type_version: DocumentInfoOptions None, All, CreatedTimeStamp, ExportSetting or InstalledProducts (selects the overload with document info).")]
    public string? DocumentInfoOptions { get; set; }

    [Description("export_type_version: absolute path of the SimaticML file written.")]
    public string? FilePath { get; set; }

    [Description("export_type_version, export_type_version_as_documents: replace existing files (default false: an existing file is refused).")]
    public bool? Overwrite { get; set; }

    [Description("get_library_tree: all (default), types or masterCopies.")]
    public string? Content { get; set; }

    [Description("get_library_tree: list every version of each type. Default true.")]
    public bool? IncludeVersions { get; set; }

    [Description("get_library_tree: 1-32 (default 16); compare_libraries: 1-64 (default 16).")]
    public int? MaxDepth { get; set; }

    [Description("compare_libraries: 1-5000 elements (default 300).")]
    public int? MaxElements { get; set; }

    [Description("get_library_tree: plain offset paging, default 0; no snapshot.")]
    public int? Offset { get; set; }

    [Description("get_library_tree: 1-500 entries, default 200.")]
    public int? Limit { get; set; }
}
