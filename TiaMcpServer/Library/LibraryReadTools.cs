using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

[McpServerToolType]
public class LibraryReadTools
{
    private const string ToolName = LibraryMethods.ReadTool;

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Read the bound standalone project's library and the open global libraries with up to 50 independent operations: "
        + "list_libraries (the project library and every open global library; their library selectors), "
        + "list_global_library_infos (GlobalLibraryInfo descriptors: libraryType, name, path, isOpen), "
        + "get_library_tree (library; optional folderPath, masterCopyFolderPath, content all|types|masterCopies, includeVersions, maxDepth 1-32, offset, limit 1-500: "
        + "type folders, types with versions, state and default flag, and master copy folders and master copies with content types, as one offset/limit page), "
        + "get_type (library, typePath or typeGuid: versions with dependencies, dependents, master copies containing instances, original library), "
        + "find_type_instances (library, type, optional version/versionGuid, plcName: LibraryTypeVersion.FindInstances on the PLC), "
        + "get_instance_info (blockPath or plcTypePath: the library type version a PLC block or data type is an instance of), "
        + "update_check (library; UpdateCheck(project, ReportOutOfDateOnly) messages), "
        + "compare_libraries (library, targetLibrary; CompareToLibrary tree, elements projected schema-free, maxDepth 1-64, maxElements 1-5000). "
        + "get_supported_export_formats (library, type: LibraryType.GetSupportedExportFormats()), "
        + "list_secondary_projects (the projects TIA Portal has open as secondary read-only reference projects: path, name, isPrimary), "
        + "list_portal_settings (optional settingsFolderPath, maxDepth: the TIA Portal settings folders and settings with value, valueType, enumMembers, writable, securityRelevant), "
        + "compare_types (library, type, targetTypePath/targetTypeGuid, optional targetLibrary: LibraryType.CompareTo), "
        + "compare_type_versions (library, version, targetTypePath/targetTypeGuid with targetVersion or targetVersionGuid, optional targetLibrary: LibraryTypeVersion.CompareTo), "
        + "compare_master_copies (library, masterCopyName, masterCopyFolderPath, targetMasterCopyName, targetMasterCopyFolderPath, optional targetLibrary: "
        + "MasterCopy.CompareTo); each lists DetailedCompareResult.Properties (description, status, leftValue, rightValue; maxElements 1-5000). "
        + "get_type also reports doNotUse, setForUpdate, status, namespace, author and comment. "
        + "library is 'project' or an open global library's path; open one with library_write open_global_library. "
        + "get_library_tree lists master copies of the root master copy folder and its user folders (masterCopyFolder entries; masterCopyFolderPath starts "
        + "that walk). Reads never open, save or close libraries or projects.")]
    public static Task<CallToolResult> LibraryRead(
        OpennessWorkerClient workerClient,
        [Description("Ordered library read operations, each with unique operationId and operation.")] LibraryOperationRequest[] operations,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(workerClient, operations, ToolName, LibraryPayloadContract.ContractVersion,
            LibraryCatalog.ValidateRead, LibraryCatalog.ValidateAccessMode,
            async operation => LibraryPayloadContract.ProjectRead(operation,
                await workerClient.LibraryAsync(LibraryMethods.ForOperation(operation.Operation), LibraryCatalog.ToRequest(operation)).ConfigureAwait(false)),
            RetryGuidance, cancellationToken);

    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => BatchReadTool.ApplyBudget(batch, ToolName, LibraryPayloadContract.ContractVersion, RetryGuidance, maxItemChars, maxDocumentChars);

    private static string RetryGuidance(StructuredOperationItem item)
        => item.Operation switch
        {
            LibraryMethods.GetLibraryTree => "Lower limit, narrow folderPath or set includeVersions false, or re-run this operationId in its own library_read call.",
            LibraryMethods.CompareLibraries => "Lower maxDepth or maxElements, or re-run this operationId in its own library_read call.",
            _ => "Re-run this operationId in its own library_read call; for one value above the limit, raise --max-item-chars and --max-document-chars.",
        };

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, LibraryPayloadContract.ContractVersion, category, message);
}
