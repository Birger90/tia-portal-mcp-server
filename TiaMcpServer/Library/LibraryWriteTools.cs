using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

[McpServerToolType]
public class LibraryWriteTools
{
    [McpServerTool(Name = LibraryMethods.WriteTool, ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(LibraryGuardedWriteResponse))]
    [Description("Execute up to 50 ordered library writes on the bound standalone project's library and the open global libraries. "
        + "Global library lifecycle: open_global_library (path; openMode ReadOnly|ReadWrite or upgrade:true; a system or corporate library path from list_global_library_infos is opened read-only with Open(GlobalLibraryInfo), without openMode ReadWrite or upgrade), retrieve_global_library (archive path, "
        + "targetDirectory, openMode, upgrade:true for RetrieveWithUpgrade), create_global_library (targetDirectory, name), save_global_library (library), "
        + "close_global_library (library), archive_global_library (library, targetDirectory, name = archive file name, archivationMode None|Compressed (default)|"
        + "DiscardRestorableData|DiscardRestorableDataAndCompressed), save_global_library_as (library, targetDirectory). "
        + "Secondary (read-only reference) projects next to the bound project (issue #20, not live-tested): open_secondary_project (path, upgrade), "
        + "retrieve_secondary_project (path = archive, targetDirectory, upgrade), close_secondary_project (secondaryProject); ProjectOpenMode.Secondary, the binding "
        + "never changes and a secondary project (or one whose IsPrimary is unreadable) is never a binding candidate; copy objects from it with create_master_copy source.project and instantiate them, "
        + "compare with plc_read compare_software compareTarget secondary_plc. Lifecycle items: read-write confirms the call. "
        + "TIA Portal settings (issue #27, not live-tested; the whole TIA Portal instance): set_portal_setting (settingsFolderPath, settingName, settingValue as text, "
        + "converted to the current value's type and read back); read-write confirms every call with a set_portal_setting; a security-relevant setting (by the "
        + "folder and setting names TIA Portal resolves in planning: security, certificates, communication, encryption, passwords, trust, accept, allow, legacy, TLS, access) "
        + "also fires the acknowledge guard library_portal_security_setting (full satisfies it by policy); a setting that classifies differently at the change fails state_changed. "
        + "Content: create_master_copy (library, source {kind block|plcType|tagTable|device|textList, path, plcName}), copy_master_copy (library, masterCopyName, "
        + "targetLibrary), delete_master_copy, rename_master_copy (newName), instantiate_master_copy (library, masterCopyName, target {kind "
        + "blockGroup|typeGroup|tagTableGroup|technologyObjects|softwareUnits|externalSources|devices|deviceItem|textLists, plcName, folderPath, parent}, masterCopyMode, newName), "
        + "instantiate_type_version (library, typePath/typeGuid, version/versionGuid, target blockGroup|typeGroup, updatePathsMode), update_project "
        + "(library, typePath/typeGuid or folderPath, plcName), rename_type, rename_type_folder, edit_type_version (optional blockPath or plcTypePath of an instance: Edit(LibraryTypeInstanceInfo)), release_type_version (versionNumber, "
        + "author, comment, dependenciesMode), discard_type_version, set_default_type_version, delete_type_version; update_project also takes "
        + "forceUpdateMode, deleteUnusedVersionsMode, structureConflictResolutionMode (global library: all three or none; project library: deleteUnusedVersionsMode only). "
        + "Also (V21 API description, not live-tested): clean_up_library (library, typePath/typeGuid or folderPath; cleanUpMode on the project library), "
        + "harmonize_project (library, selection, harmonizeOptions [HarmonizePaths, HarmonizeNames], plcName), update_library (library, selection, targetLibrary, "
        + "optional all three modes), delete_type, set_type_do_not_use (doNotUse), create_type_folder (folderPath parent, newName), delete_type_folder "
        + "(folderPath), create_master_copy_folder (masterCopyFolderPath parent, newName), delete_master_copy_folder, rename_master_copy_folder "
        + "(masterCopyFolderPath, newName), create_type_from_documents (documentDirectory, documentName, folderPath, plcName as targetEnvironment, "
        + "libraryImportOptions), create_version_from_documents (type, documentDirectory, documentName, createOption None|Override, plcName, libraryImportOptions), "
        + "export_type_version (version, filePath, exportOptions, documentInfoOptions, overwrite), export_type_version_as_documents (version, documentDirectory, "
        + "documentName, exportFormat from get_supported_export_formats, libraryExportOptions, overwrite). Master copy items take masterCopyFolderPath "
        + "(copy_master_copy also targetMasterCopyFolderPath); omitted means the root master copy folder. Omitted dryRun or false executes; true previews targets and guards. "
        + "Guards are informative, except that close_global_library is refused while the library has changes this server made and did not save "
        + "(Close() offers no save-or-discard choice: save_global_library first). Every item takes portalConfirmations: TIA Portal confirmation "
        + "dialogs raised while it runs are answered No (decline, default; recorded, the item fails) or Yes (accept; recorded, a warning). "
        + "Read-write asks the user to confirm each actual call that contains a lifecycle item; content items never prompt; "
        + "full runs without prompts. Global library changes persist only through save_global_library; project changes through save_project. "
        + "Verification: open/retrieve/create/close are read back in list_libraries, create/copy/delete_master_copy in get_library_tree (masterCopies); "
        + "save_global_library stays trusted (no documented read shows a saved state); the other items are read back inside their worker call. "
        + "Stops at the first failed item without rollback across items or automatic replay; inspect with library_read before retrying.")]
    public static Task<CallToolResult> LibraryWrite(OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Ordered library writes.")] LibraryOperationRequest[] operations,
        [Description("True previews without mutation or confirmation; omitted or false executes.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
        => execution.RunAsync(new LibraryWriteDomain(workerClient), new WriteCall<LibraryOperationRequest>(operations ?? [], dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            bindingStrategy: null, cancellationToken);
}
