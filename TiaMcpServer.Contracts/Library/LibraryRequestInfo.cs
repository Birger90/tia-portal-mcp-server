using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// Parameters of one <c>library_*</c> worker method (<c>WorkerRequest.Library</c>). The host validated the
/// shape; only the members the method documents are read. Enum-valued members carry exact Siemens enum member
/// names, parsed by name at runtime (the stub enums declare no values).
/// </summary>
public sealed class LibraryRequestInfo
{
    /// <summary>The public operation; for <c>library_plan</c> the write operation being planned.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary><c>project</c> for the project library, otherwise the path of an open global library as <c>list_libraries</c> reports it.</summary>
    public string? Library { get; set; }

    /// <summary>copy_master_copy: the destination library; compare_libraries: the library compared with.</summary>
    public string? TargetLibrary { get; set; }

    /// <summary>open_global_library: the library file; retrieve_global_library: the archive file.</summary>
    public string? Path { get; set; }

    /// <summary>retrieve_global_library, create_global_library: the directory the library is written to.</summary>
    public string? TargetDirectory { get; set; }

    /// <summary>create_global_library: the new library's name.</summary>
    public string? Name { get; set; }

    /// <summary><c>OpenMode</c> member name (<c>ReadOnly</c>, <c>ReadWrite</c>).</summary>
    public string? OpenMode { get; set; }

    /// <summary>open_global_library: open with <c>OpenWithUpgrade(FileInfo)</c> instead of <c>Open(FileInfo, OpenMode)</c>.</summary>
    public bool Upgrade { get; set; }

    /// <summary>Type folder path (<c>/</c> or <c>/A/B</c>): tree start, rename_type_folder target, update_project selection.</summary>
    public string? FolderPath { get; set; }

    /// <summary>Type path <c>/Folder/Type</c> (or <c>Type</c> in the root type folder).</summary>
    public string? TypePath { get; set; }

    /// <summary>Type GUID (<c>ILibrary.FindType(Guid)</c>).</summary>
    public string? TypeGuid { get; set; }

    /// <summary>Exact version number text as listed.</summary>
    public string? Version { get; set; }

    /// <summary>Version GUID (<c>ILibrary.FindVersion(Guid)</c>).</summary>
    public string? VersionGuid { get; set; }

    /// <summary>Master copy name in the root master copy folder.</summary>
    public string? MasterCopyName { get; set; }

    /// <summary>rename_* and instantiate_master_copy: the new name.</summary>
    public string? NewName { get; set; }

    /// <summary>find_type_instances, get_instance_info, update_project: the PLC software or device name.</summary>
    public string? PlcName { get; set; }

    /// <summary>get_instance_info: a PLC block path (plc_read grammar).</summary>
    public string? BlockPath { get; set; }

    /// <summary>get_instance_info: a PLC data type path (plc_read grammar).</summary>
    public string? PlcTypePath { get; set; }

    /// <summary>create_master_copy: the project object captured as a master copy.</summary>
    public LibrarySourceInfo? Source { get; set; }

    /// <summary>instantiate_master_copy, instantiate_type_version: where the instance is created.</summary>
    public LibraryTargetInfo? Target { get; set; }

    /// <summary><c>MasterCopyMode</c> member name.</summary>
    public string? MasterCopyMode { get; set; }

    /// <summary><c>UpdatePathsMode</c> member name.</summary>
    public string? UpdatePathsMode { get; set; }

    /// <summary><c>CreateOrReleaseDependenciesMode</c> member name.</summary>
    public string? DependenciesMode { get; set; }

    /// <summary>release_type_version: the released version number (System.Version text).</summary>
    public string? VersionNumber { get; set; }

    /// <summary>release_type_version: the third <c>Release</c> argument.</summary>
    public string? Author { get; set; }

    /// <summary>release_type_version: the fourth <c>Release</c> argument.</summary>
    public string? Comment { get; set; }

    /// <summary><c>UpdateCheckMode</c> member name.</summary>
    public string? UpdateCheckMode { get; set; }

    /// <summary>
    /// Write items: how TIA Portal <c>Confirmation</c> dialogs raised during the mutation are answered
    /// (<c>decline</c>: No, recorded, the item fails; <c>accept</c>: Yes, recorded, a warning). The host always sends it.
    /// </summary>
    public string? PortalConfirmations { get; set; }

    /// <summary>list_portal_settings: the folder to start at; set_portal_setting: the setting's folder (<c>A/B</c>, names case-sensitive as TIA reports them).</summary>
    public string? SettingsFolderPath { get; set; }

    /// <summary>set_portal_setting: the setting's name.</summary>
    public string? SettingName { get; set; }

    /// <summary>set_portal_setting: the new value as text, converted to the runtime type of the current value.</summary>
    public string? SettingValue { get; set; }

    /// <summary>
    /// set_portal_setting mutation: the classification the host planned (and the human confirmed) from the names the worker
    /// resolved in planning; the worker resolves the setting again and refuses the change when it is classified differently
    /// (security review F2). Null on the plan request.
    /// </summary>
    public bool? PlannedSecurityRelevant { get; set; }

    /// <summary>close_secondary_project: the secondary project by its path as list_secondary_projects reports it.</summary>
    public string? SecondaryProject { get; set; }

    /// <summary>Master copy folder path (<c>/</c> or <c>/A/B</c>, below the root master copy folder): where a master copy is found or created, the folder created in or deleted.</summary>
    public string? MasterCopyFolderPath { get; set; }

    /// <summary>copy_master_copy: the destination master copy folder in targetLibrary; compare_master_copies: the compared master copy's folder.</summary>
    public string? TargetMasterCopyFolderPath { get; set; }

    /// <summary>compare_types, compare_type_versions: the compared type (in targetLibrary, else in library).</summary>
    public string? TargetTypePath { get; set; }

    public string? TargetTypeGuid { get; set; }

    /// <summary>compare_type_versions: the compared version.</summary>
    public string? TargetVersion { get; set; }

    public string? TargetVersionGuid { get; set; }

    /// <summary>compare_master_copies: the compared master copy.</summary>
    public string? TargetMasterCopyName { get; set; }

    /// <summary>archive_global_library: <c>LibraryArchivationMode</c> member name.</summary>
    public string? ArchivationMode { get; set; }

    /// <summary>clean_up_library on the project library: <c>CleanUpMode</c> member name.</summary>
    public string? CleanUpMode { get; set; }

    /// <summary>harmonize_project: <c>HarmonizeProjectOptions</c> member names (several only on a [Flags] enum).</summary>
    public List<string>? HarmonizeOptions { get; set; }

    /// <summary>update_library, update_project: <c>ForceUpdateMode</c> member name.</summary>
    public string? ForceUpdateMode { get; set; }

    /// <summary>update_library, update_project: <c>DeleteUnusedVersionsMode</c> member name.</summary>
    public string? DeleteUnusedVersionsMode { get; set; }

    /// <summary>update_library, update_project: <c>StructureConflictResolutionMode</c> member name.</summary>
    public string? StructureConflictResolutionMode { get; set; }

    /// <summary>set_type_do_not_use: the value of <c>LibraryType.DoNotUse</c>.</summary>
    public bool? DoNotUse { get; set; }

    /// <summary>create_*_from_documents: the directory holding the documents; export_type_version_as_documents: the export directory.</summary>
    public string? DocumentDirectory { get; set; }

    /// <summary>The documents' file name without extension.</summary>
    public string? DocumentName { get; set; }

    /// <summary>export_type_version_as_documents: one of <c>LibraryType.GetSupportedExportFormats()</c>.</summary>
    public string? ExportFormat { get; set; }

    /// <summary><c>LibraryImportOptions</c> member name.</summary>
    public string? LibraryImportOptions { get; set; }

    /// <summary><c>LibraryExportOptions</c> member name.</summary>
    public string? LibraryExportOptions { get; set; }

    /// <summary>create_version_from_documents: <c>CreateOptions</c> member name.</summary>
    public string? CreateOption { get; set; }

    /// <summary>export_type_version: <c>ExportOptions</c> member name.</summary>
    public string? ExportOptions { get; set; }

    /// <summary>export_type_version: <c>DocumentInfoOptions</c> member name (selects the three-argument Export overload).</summary>
    public string? DocumentInfoOptions { get; set; }

    /// <summary>export_type_version: the SimaticML file written.</summary>
    public string? FilePath { get; set; }

    /// <summary>export_type_version, export_type_version_as_documents: replace existing files.</summary>
    public bool Overwrite { get; set; }

    /// <summary>get_library_tree: <c>all</c>, <c>types</c> or <c>masterCopies</c>.</summary>
    public string? Content { get; set; }

    public bool IncludeVersions { get; set; } = true;

    public int MaxDepth { get; set; } = LibraryPolicy.DefaultTreeDepth;

    public int MaxElements { get; set; } = LibraryPolicy.DefaultCompareElements;

    public int Offset { get; set; }

    public int Limit { get; set; } = LibraryPolicy.DefaultPageLimit;
}

/// <summary>The project object create_master_copy captures (<c>MasterCopyComposition.Create(IMasterCopySource)</c>).</summary>
public sealed class LibrarySourceInfo
{
    /// <summary>One of <see cref="LibraryPolicy.SourceKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    /// <summary>
    /// The project the source is taken from: null for the bound project, else the path of an open secondary project
    /// (list_secondary_projects) — the documented way to copy objects from a read-only reference project.
    /// </summary>
    public string? Project { get; set; }

    /// <summary>block / plcType: plc_read path; tagTable: <c>/Group/Table</c>; device: the device name; textList: the user text list name.</summary>
    public string Path { get; set; } = string.Empty;
}

/// <summary>Where an instance is created.</summary>
public sealed class LibraryTargetInfo
{
    /// <summary>One of <see cref="LibraryPolicy.TargetKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    /// <summary>Block, type or tag table group path below the PLC's system group (<c>/</c> or <c>/A/B</c>).</summary>
    public string? FolderPath { get; set; }

    /// <summary>deviceItem: the parent device item (the network_read hardware selector).</summary>
    public HardwareObjectSelectorInfo? Parent { get; set; }
}
