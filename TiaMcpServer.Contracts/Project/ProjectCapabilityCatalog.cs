using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Vci;

namespace TiaMcpServer.Contracts.Project;

/// <summary>
/// Project-scoped public tools and batch items. Internal worker probes are represented by their
/// owning public operation; independent Portal/server inventory and catalog searches are omitted.
/// This table describes delivery for a container kind, not access-mode authorization.
/// </summary>
public static class ProjectCapabilityCatalog
{
    private static readonly string[] BasicLocalOperations =
    {
        "bind_project", "open_project", "get_project_status"
    };

    private static readonly string[] StandaloneLifecycleOperations =
    {
        "create_project", "save_project_as", "archive_project", "close_project", "retrieve_project"
    };

    private static readonly string[] StandaloneVciOperations =
    {
        "vci_read", "vci_write"
    };

    /// <summary>Project and global library tools: the project library is reached through <c>Project.ProjectLibrary</c>, so standalone projects only.</summary>
    private static readonly string[] StandaloneLibraryOperations =
    {
        LibraryMethods.ReadTool, LibraryMethods.WriteTool
    };

    /// <summary>
    /// The PLCSIM download and its offline download-path listing (a <c>plc_read</c> operation).
    /// Standalone projects only: downloading from a Multiuser local session is not delivered.
    /// </summary>
    private static readonly string[] StandaloneDownloadOperations =
    {
        PlcDownloadMethods.Download, PlcDownloadMethods.ListTargets
    };

    /// <summary>
    /// The online/offline compare, online state, plc_tia_online and station upload (all touch or list an
    /// online path, like the download). Standalone projects only.
    /// </summary>
    private static readonly string[] StandaloneOnlineOperations = PlcOnlineMethods.StandaloneOnlyOperations.ToArray();

    /// <summary>The common alarm classes (<c>Project.GetService&lt;AlarmClassDataProvider&gt;()</c>): standalone projects only.</summary>
    private static readonly string[] StandaloneAlarmClassOperations = PlcAlarmMethods.StandaloneOnlyOperations.ToArray();

    /// <summary>Reads that run against the common <c>ProjectBase</c> root of any open container.</summary>
    private static readonly string[] ProjectContentReadOperations = new[]
    {
        "browse_project_tree", "plc_read", "network_read", "read_cross_references", "hmi_read",
        "get_block_content", "get_type_content", "list_tag_tables",
        "read_hardware_config", "list_network_objects", "inspect_network_object"
    }
    .Concat(PlcOnlineMethods.ContentReadMethods)
    .ToArray();

    /// <summary>
    /// Content writes, compile and save against <c>ProjectBase</c>; a local session saves through
    /// <c>LocalSession.Save()</c> and never checks in to the Project Server.
    /// </summary>
    private static readonly string[] ProjectContentOperations = new[]
    {
        "plc_write", "network_write", "hmi_write", "compile_check", "save_project"
    }
    .Concat(HmiWriteMethods.All)
    .Concat(new[]
    {
        "update_block_logic", "update_type_content", "create_tag_table", "delete_tag_table",
        "create_tag", "update_tag", "delete_tag", "create_user_constant",
        "update_user_constant", "delete_user_constant", "create_block", "delete_block",
        "create_block_group", "delete_block_group",
        "add_network_device", "configure_network_device", "create_subnet", "update_subnet",
        "delete_subnet", Network.NetworkIoSystemMethods.CreateIoSystem
    })
    .Concat(PlcOnlineMethods.ContentWriteMethods)
    .ToArray();

    private static readonly string[] NonProjectMethods =
    {
        "hello", "select_portal_project", "list_tia_portal_processes", "list_server_connections",
        "list_server_groups", "list_server_projects", "list_local_sessions", "get_lock_state",
        "search_equipment_catalog"
    };

    private static readonly IReadOnlyList<ProjectCapabilityInfo> Standalone = Build(ProjectContainerKinds.StandaloneProject);
    private static readonly IReadOnlyList<ProjectCapabilityInfo> Local = Build(ProjectContainerKinds.LocalSession);
    private static readonly IReadOnlyList<ProjectCapabilityInfo> Server = Build(ProjectContainerKinds.ServerProject);

    public static IReadOnlyList<ProjectCapabilityInfo> Describe(string containerKind)
    {
        var source = containerKind switch
        {
            ProjectContainerKinds.StandaloneProject => Standalone,
            ProjectContainerKinds.LocalSession => Local,
            ProjectContainerKinds.ServerProject => Server,
            _ => throw new ArgumentOutOfRangeException(nameof(containerKind), containerKind, "Unknown project container kind.")
        };

        // DTOs are mutable for JSON; never expose shared catalog instances to a caller.
        return source.Select(item => new ProjectCapabilityInfo
        {
            Operation = item.Operation,
            Applicability = item.Applicability
        }).ToArray();
    }

    /// <summary>
    /// Maps a worker method or public operation to the catalog operation that gates it, or null
    /// when the method is not project-scoped (Portal/server inventory, catalog search).
    /// </summary>
    public static string? OperationFor(string method)
    {
        if (NonProjectMethods.Contains(method, StringComparer.Ordinal)) return null;
        // S7-PLCSIM Advanced methods act on a simulated CPU in a separate worker, never on a project.
        if (PlcSimMethods.IsPlcSimMethod(method)) return null;
        // S7-1500 Web API methods act on a configured controller from the host, never on a project.
        if (PlcWebApi.PlcWebApiMethods.IsWebApiMethod(method)) return null;
        // Siemens documentation reads act on public documentation hosts from the host, never on a project.
        if (Docs.SiemensDocsMethods.IsDocsMethod(method)) return null;
        if (method == "hmi_write" || HmiWriteMethods.All.Contains(method, StringComparer.Ordinal)) return "hmi_write";
        if (method.StartsWith("hmi_", StringComparison.Ordinal) && method != "hmi_read") return "hmi_read";
        // Granular block editing methods are plc_read / plc_write operations and are gated as those tools.
        if (PlcBlockEditMethods.Read.Contains(method, StringComparer.Ordinal)
            || Plc.SimaticSd.SimaticSdMethods.Reads.Contains(method, StringComparer.Ordinal)) return "plc_read";
        if (PlcBlockEditMethods.Write.Contains(method, StringComparer.Ordinal)
            || PlcBlockEditMethods.PlannedEdits.Contains(method, StringComparer.Ordinal)) return "plc_write";
        // Technology object methods are plc_read / plc_write operations and are gated as those tools.
        if (Plc.TechnologyObjects.TechnologyObjectMethods.IsRead(method)) return "plc_read";
        if (Plc.TechnologyObjects.TechnologyObjectMethods.IsWrite(method)) return "plc_write";
        if (Plc.Security.PlcSecurityMethods.IsRead(method)) return "plc_read";
        if (Plc.Security.PlcSecurityMethods.IsWrite(method)) return "plc_write";
        // Offline change-detection reads (checksums, fingerprints) are plc_read operations.
        if (Plc.ChangeDetection.PlcChangeDetectionMethods.IsRead(method)) return "plc_read";
        // The loadable file generation is a plc_write operation on one PLC's software.
        if (Plc.Loadable.PlcLoadableMethods.IsWorkerMethod(method)) return "plc_write";
        // Block know-how and write protection (issue #5) are plc_read / plc_write operations on one block.
        if (Plc.BlockProtection.PlcBlockProtectionMethods.IsRead(method)) return "plc_read";
        if (Plc.BlockProtection.PlcBlockProtectionMethods.IsWrite(method)) return "plc_write";
        // Project languages and project texts (issue #8) are plc_read / plc_write operations on ProjectBase members, so they
        // apply to standalone projects and local sessions alike.
        if (ProjectLanguageMethods.IsRead(method)) return "plc_read";
        if (ProjectLanguageMethods.IsWrite(method)) return "plc_write";
        // The settings family (issues #15, #13, #12, #14, #21, #25) are plc_read / plc_write operations; the project-wide ones need
        // a standalone project, which the worker checks (unsupported_capability otherwise).
        if (Plc.Settings.PlcSettingsCatalog.IsRead(method)) return "plc_read";
        if (Plc.Settings.PlcSettingsCatalog.IsWrite(method)) return "plc_write";
        // The OPC UA configuration of a PLC's software (issue #24).
        if (Plc.OpcUa.PlcOpcUaMethods.IsRead(method)) return "plc_read";
        if (Plc.OpcUa.PlcOpcUaMethods.IsWrite(method)) return "plc_write";
        if (Plc.DeviceData.PlcDeviceDataMethods.IsWorkerMethod(method)) return "plc_write";
        // Fail-safe methods are plc_read / plc_write operations.
        if (Plc.Safety.PlcSafetyMethods.IsRead(method)) return "plc_read";
        if (Plc.Safety.PlcSafetyMethods.IsWrite(method)) return "plc_write";
        if (VciMethods.Read.Contains(method, StringComparer.Ordinal)) return "vci_read";
        if (method == VciMethods.Plan || VciMethods.Write.Contains(method, StringComparer.Ordinal)) return "vci_write";
        if (LibraryMethods.IsReadMethod(method)) return LibraryMethods.ReadTool;
        if (method == LibraryMethods.Plan || LibraryMethods.IsWriteMethod(method)) return LibraryMethods.WriteTool;
        // Hardware-module worker methods are network_read/network_write operations; the planning reads serve network_write.
        if (Network.HardwareModuleMethods.IsWrite(method) || method is Network.HardwareModuleMethods.ProbePlug
            or Network.HardwareModuleMethods.ReadAttributes) return "network_write";
        if (Network.HardwareModuleMethods.IsRead(method)) return "network_read";
        if (method == PlcTagMoveMethods.MoveTag) return "plc_write";
        // External-source, folder and export methods are plc_read/plc_write operations; the scan stays standalone-only.
        if (PlcSourceFolderMethods.IsWorkerMethod(method) && !PlcSourceFolderMethods.StandaloneOnlyOperations.Contains(method, StringComparer.Ordinal))
            return PlcSourceFolderMethods.IsRead(method) ? "plc_read" : "plc_write";

        // Text list, alarm text and supervision methods are plc_read / plc_write operations gated as those tools;
        // the project-wide alarm-class methods are their own standalone-only rows.
        if (PlcAlarmMethods.IsProjectScoped(method)) return method;
        // The project's block-compilation settings: its own standalone-only row (Project.GetService<T>(), like the read).
        if (ProjectCompilationSettingsMethods.IsWorkerMethod(method)) return method;
        if (PlcAlarmMethods.IsReadMethod(method)) return "plc_read";
        if (method == PlcAlarmMethods.Plan || PlcAlarmMethods.IsWriteMethod(method)) return "plc_write";
        // plc_write planning read of the worker's block export ledger.
        if (method == PlcBlockExportStateMethods.GetExportState) return "plc_write";
        return method switch
        {
            "browse_project_tree_v3_snapshot" or "read_hardware_page_candidates" => "browse_project_tree",
            "get_basic_project_status" => "get_project_status",
            "probe_open_project_rebind" or "probe_project_status_for_lifecycle" => "open_project",
            PlcOnlineMethods.GoOnline or PlcOnlineMethods.GoOffline => PlcOnlineMethods.OnlineTool,
            _ => method
        };
    }

    /// <summary>
    /// True when <paramref name="method"/> acts on project content and therefore needs a
    /// worker-verified binding. Status, lifecycle probes, open/create, Portal selection and
    /// Multiuser inventory are exempt; catalog search is gated like every other content read.
    /// </summary>
    public static bool RequiresBoundProject(string method)
        => method == "search_equipment_catalog"
            || OperationFor(method) is { } operation
            && operation is not ("get_project_status" or "open_project" or "create_project" or "retrieve_project");

    public static bool Supports(string containerKind, string operation)
    {
        if (string.IsNullOrWhiteSpace(operation) || OperationFor(operation) is not { } mapped) return false;
        if (containerKind == ProjectContainerKinds.StandaloneProject)
            return Standalone.Any(item => item.Operation == mapped);
        if (containerKind == ProjectContainerKinds.LocalSession)
            return BasicLocalOperations.Contains(mapped, StringComparer.Ordinal)
                || ProjectContentReadOperations.Contains(mapped, StringComparer.Ordinal)
                || ProjectContentOperations.Contains(mapped, StringComparer.Ordinal);
        return false;
    }

    private static IReadOnlyList<ProjectCapabilityInfo> Build(string containerKind)
    {
        var result = new List<ProjectCapabilityInfo>();
        foreach (var operation in BasicLocalOperations)
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = containerKind == ProjectContainerKinds.LocalSession
                    ? ProjectCapabilityApplicabilities.LocalSessionConditional
                    : containerKind == ProjectContainerKinds.StandaloneProject
                        ? ProjectCapabilityApplicabilities.ProjectContent
                        : ProjectCapabilityApplicabilities.Unsupported
            });
        foreach (var operation in ProjectContentReadOperations.Concat(ProjectContentOperations))
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = containerKind == ProjectContainerKinds.ServerProject
                    ? ProjectCapabilityApplicabilities.NotYetDelivered
                    : ProjectCapabilityApplicabilities.ProjectContent
            });
        foreach (var operation in StandaloneLifecycleOperations.Concat(StandaloneVciOperations).Concat(StandaloneDownloadOperations)
                     .Concat(StandaloneOnlineOperations).Concat(StandaloneLibraryOperations).Concat(PlcSourceFolderMethods.StandaloneOnlyOperations)
                     .Concat(StandaloneAlarmClassOperations).Concat(new[] { ProjectCompilationSettingsMethods.Set }))
            result.Add(new ProjectCapabilityInfo
            {
                Operation = operation,
                Applicability = ProjectCapabilityApplicabilities.StandaloneOnly
            });
        return result;
    }
}
