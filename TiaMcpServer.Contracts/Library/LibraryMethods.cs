namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// Public operation and worker method names of the project and global library tools. Every public
/// <c>library_read</c>/<c>library_write</c> operation has the worker method <c>library_&lt;operation&gt;</c>
/// (<see cref="ForOperation"/>), because <c>OperationPolicyCatalog</c> is one flat namespace. The names feed
/// <c>OperationPolicyCatalog</c>, <c>ProjectCapabilityCatalog</c> and the worker dispatch switch.
/// </summary>
public static class LibraryMethods
{
    public const string ReadTool = "library_read";
    public const string WriteTool = "library_write";

    /// <summary>Non-mutating planning read of one <c>library_write</c> item (Observe).</summary>
    public const string Plan = "library_plan";

    public const string Prefix = "library_";

    // library_read (Observe)
    public const string ListLibraries = "list_libraries";
    public const string ListGlobalLibraryInfos = "list_global_library_infos";
    public const string GetLibraryTree = "get_library_tree";
    public const string GetLibraryType = "get_type";
    public const string FindTypeInstances = "find_type_instances";
    public const string GetInstanceInfo = "get_instance_info";
    public const string UpdateCheck = "update_check";
    public const string CompareLibraries = "compare_libraries";
    public const string GetSupportedExportFormats = "get_supported_export_formats";
    public const string CompareTypes = "compare_types";
    public const string CompareTypeVersions = "compare_type_versions";
    public const string CompareMasterCopies = "compare_master_copies";
    public const string ListSecondaryProjects = "list_secondary_projects";

    /// <summary>TIA Portal settings (issue #27): <c>TiaPortal.SettingsFolders</c>, Portal-wide, not project content.</summary>
    public const string ListPortalSettings = "list_portal_settings";

    // library_write: global library lifecycle (ProjectMutation; read-write confirms the call)
    public const string OpenGlobalLibrary = "open_global_library";
    public const string RetrieveGlobalLibrary = "retrieve_global_library";
    public const string CreateGlobalLibrary = "create_global_library";
    public const string SaveGlobalLibrary = "save_global_library";
    public const string CloseGlobalLibrary = "close_global_library";
    public const string ArchiveGlobalLibrary = "archive_global_library";
    public const string SaveGlobalLibraryAs = "save_global_library_as";

    // library_write: read-only secondary (reference) projects next to the bound project (issue #20; lifecycle, confirmed)
    public const string OpenSecondaryProject = "open_secondary_project";
    public const string RetrieveSecondaryProject = "retrieve_secondary_project";
    public const string CloseSecondaryProject = "close_secondary_project";

    // library_write: library content and project instantiation (ProjectMutation; no prompt)
    public const string CreateMasterCopy = "create_master_copy";
    public const string CopyMasterCopy = "copy_master_copy";
    public const string DeleteMasterCopy = "delete_master_copy";
    public const string RenameMasterCopy = "rename_master_copy";
    public const string InstantiateMasterCopy = "instantiate_master_copy";
    public const string InstantiateTypeVersion = "instantiate_type_version";
    public const string UpdateProject = "update_project";
    public const string RenameType = "rename_type";
    public const string RenameTypeFolder = "rename_type_folder";
    public const string EditTypeVersion = "edit_type_version";
    public const string ReleaseTypeVersion = "release_type_version";
    public const string DiscardTypeVersion = "discard_type_version";
    public const string SetDefaultTypeVersion = "set_default_type_version";
    public const string DeleteTypeVersion = "delete_type_version";

    // library_write: added from the V21 API description (issue #17; ProjectMutation; no prompt)
    public const string CleanUpLibrary = "clean_up_library";
    public const string HarmonizeProject = "harmonize_project";
    public const string UpdateLibrary = "update_library";
    public const string DeleteType = "delete_type";
    public const string SetTypeDoNotUse = "set_type_do_not_use";
    public const string CreateTypeFolder = "create_type_folder";
    public const string DeleteTypeFolder = "delete_type_folder";
    public const string CreateMasterCopyFolder = "create_master_copy_folder";
    public const string DeleteMasterCopyFolder = "delete_master_copy_folder";
    public const string RenameMasterCopyFolder = "rename_master_copy_folder";
    public const string CreateTypeFromDocuments = "create_type_from_documents";
    public const string CreateVersionFromDocuments = "create_version_from_documents";
    public const string ExportTypeVersion = "export_type_version";
    public const string ExportTypeVersionAsDocuments = "export_type_version_as_documents";

    /// <summary>TIA Portal settings (issue #27): <c>TiaPortalSetting.Value</c> of one setting; Portal-wide.</summary>
    public const string SetPortalSetting = "set_portal_setting";

    public static IReadOnlyList<string> ReadOperations { get; } = Array.AsReadOnly(new[]
    {
        ListLibraries, ListGlobalLibraryInfos, GetLibraryTree, GetLibraryType, FindTypeInstances, GetInstanceInfo, UpdateCheck, CompareLibraries,
        GetSupportedExportFormats, CompareTypes, CompareTypeVersions, CompareMasterCopies, ListSecondaryProjects, ListPortalSettings,
    });

    /// <summary>Global library lifecycle items: they create, open, save or close library files outside the project.</summary>
    public static IReadOnlyList<string> LifecycleOperations { get; } = Array.AsReadOnly(new[]
    {
        OpenGlobalLibrary, RetrieveGlobalLibrary, CreateGlobalLibrary, SaveGlobalLibrary, CloseGlobalLibrary, ArchiveGlobalLibrary, SaveGlobalLibraryAs,
        OpenSecondaryProject, RetrieveSecondaryProject, CloseSecondaryProject,
    });

    /// <summary>The secondary-project lifecycle items (they open or close a read-only reference project, never a library).</summary>
    public static IReadOnlyList<string> SecondaryProjectOperations { get; } = Array.AsReadOnly(new[] { OpenSecondaryProject, RetrieveSecondaryProject, CloseSecondaryProject });

    public static bool IsSecondaryProject(string? operation) => operation is not null && SecondaryProjectOperations.Contains(operation, StringComparer.Ordinal);

    public static IReadOnlyList<string> ContentOperations { get; } = Array.AsReadOnly(new[]
    {
        CreateMasterCopy, CopyMasterCopy, DeleteMasterCopy, RenameMasterCopy, InstantiateMasterCopy, InstantiateTypeVersion,
        UpdateProject, RenameType, RenameTypeFolder, EditTypeVersion, ReleaseTypeVersion, DiscardTypeVersion,
        SetDefaultTypeVersion, DeleteTypeVersion, CleanUpLibrary, HarmonizeProject, UpdateLibrary, DeleteType, SetTypeDoNotUse,
        CreateTypeFolder, DeleteTypeFolder, CreateMasterCopyFolder, DeleteMasterCopyFolder, RenameMasterCopyFolder,
        CreateTypeFromDocuments, CreateVersionFromDocuments, ExportTypeVersion, ExportTypeVersionAsDocuments, SetPortalSetting,
    });

    /// <summary>Operations that write files outside the project and libraries (exports): their paths are caller files.</summary>
    public static IReadOnlyList<string> FileExportOperations { get; } = Array.AsReadOnly(new[] { ExportTypeVersion, ExportTypeVersionAsDocuments });

    public static IReadOnlyList<string> WriteOperations { get; } = Array.AsReadOnly(LifecycleOperations.Concat(ContentOperations).ToArray());

    /// <summary>Worker methods of the read operations.</summary>
    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(ReadOperations.Select(ForOperation).ToArray());

    /// <summary>Worker methods of the write operations.</summary>
    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(WriteOperations.Select(ForOperation).ToArray());

    public static string ForOperation(string operation) => Prefix + operation;

    /// <summary>The public operation of a worker method, or null when it is not a library read/write method.</summary>
    public static string? OperationOf(string? method)
        => method is not null && method.StartsWith(Prefix, StringComparison.Ordinal)
            && (Read.Contains(method, StringComparer.Ordinal) || Write.Contains(method, StringComparer.Ordinal))
            ? method.Substring(Prefix.Length) : null;

    public static bool IsReadMethod(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWriteMethod(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => method == Plan || IsReadMethod(method) || IsWriteMethod(method);

    public static bool IsLifecycle(string? operation) => operation is not null && LifecycleOperations.Contains(operation, StringComparer.Ordinal);
}
