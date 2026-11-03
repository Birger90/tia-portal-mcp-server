using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.PlcWebApi;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;

namespace TiaMcpServer.Contracts.Safety;

/// <summary>
/// Central classification of every worker operation by its capability. Both the host
/// (OperationAccessPolicy) and the worker (WorkerOperationAuthorization) consume this
/// single source of truth. An operation not listed here is denied in every mode
/// (deny-by-default).
/// </summary>
public static class OperationPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, OperationCapability> Classifications =
        BuildClassifications();

    /// <summary>
    /// Returns the capability for <paramref name="operation"/>, or null if the operation
    /// is not classified (unknown operations are denied in every mode).
    /// </summary>
    public static OperationCapability? GetCapability(string? operation)
        => !string.IsNullOrWhiteSpace(operation) && Classifications.TryGetValue(operation!, out var cap) ? cap : null;

    /// <summary>
    /// True when <paramref name="operation"/> is allowed under the given access mode.
    /// Read-only mode allows Observe, TemporaryExport, OnlineObserve (<c>compare_to_online</c>, <c>scan_accessible_devices</c>)
    /// and SessionSelection of already-open projects. Read-write and full add Compile,
    /// ProjectMutation, ProjectLifecycle, OnlineControl (<c>plc_download</c>, <c>plc_tia_online</c> and the
    /// S7-1500 Web API control methods) and SimulationControl.
    /// </summary>
    public static bool IsAllowed(McpAccessMode mode, string? operation)
    {
        var cap = GetCapability(operation);
        return cap is not null && IsCapabilityAllowed(mode, cap.Value);
    }

    /// <summary>
    /// Immutable capability presets, shared by discovery and request authorization. Read-write and
    /// full permit the same capabilities; they differ only in how confirmation is satisfied.
    /// OnlineControl is never permitted in read-only; OnlineObserve is permitted in every mode.
    /// </summary>
    public static bool IsCapabilityAllowed(McpAccessMode mode, OperationCapability capability)
        => mode switch
        {
            McpAccessMode.ReadOnly => capability is OperationCapability.Observe
                or OperationCapability.TemporaryExport
                or OperationCapability.SessionSelection
                or OperationCapability.OnlineObserve,
            McpAccessMode.ReadWrite => capability is OperationCapability.Observe
                or OperationCapability.TemporaryExport
                or OperationCapability.SessionSelection
                or OperationCapability.Compile or OperationCapability.ProjectMutation
                or OperationCapability.ProjectLifecycle or OperationCapability.OnlineControl
                or OperationCapability.SimulationControl or OperationCapability.OnlineObserve,
            McpAccessMode.Full => capability is OperationCapability.Observe
                or OperationCapability.TemporaryExport
                or OperationCapability.SessionSelection
                or OperationCapability.Compile or OperationCapability.ProjectMutation
                or OperationCapability.ProjectLifecycle or OperationCapability.OnlineControl
                or OperationCapability.SimulationControl or OperationCapability.OnlineObserve,
            _ => false
        };

    /// <summary>
    /// True when a worker request must carry the exact currently verified
    /// worker/Portal/project identity.
    /// </summary>
    public static bool RequiresExpectedSessionIdentity(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            return true;
        }

        if (ProjectCapabilityCatalog.RequiresBoundProject(operation))
        {
            return true;
        }

        if (string.Equals(operation, "open_project", StringComparison.Ordinal) ||
            string.Equals(operation, "create_project", StringComparison.Ordinal) ||
            string.Equals(operation, "retrieve_project", StringComparison.Ordinal) ||
            // The lifecycle-only non-opening read verifies the empty state after close cleared the
            // project binding. A supplied identity is still checked by worker dispatch.
            string.Equals(operation, "get_basic_project_status", StringComparison.Ordinal))
        {
            return false;
        }

        // PLCSIM worker methods never reach the Openness worker and carry no project identity.
        if (PlcSimMethods.IsPlcSimMethod(operation))
        {
            return false;
        }

        // S7-1500 Web API methods run in the host against a configured controller; no project identity.
        if (PlcWebApiMethods.IsWebApiMethod(operation))
        {
            return false;
        }

        return GetCapability(operation) switch
        {
            OperationCapability.Observe => false,
            OperationCapability.TemporaryExport => false,
            OperationCapability.SessionSelection => false,
            _ => true
        };
    }

    /// <summary>
    /// Every known operation name. Used by tests to verify completeness.
    /// </summary>
    public static IReadOnlyCollection<string> AllOperationNames => new List<string>(Classifications.Keys);

    private static IReadOnlyDictionary<string, OperationCapability> BuildClassifications()
    {
        var dict = new Dictionary<string, OperationCapability>(StringComparer.Ordinal)
        {
            // Observe (read-only safe)
            ["get_project_status"] = OperationCapability.Observe,
            ["list_tia_portal_processes"] = OperationCapability.Observe,
            ["list_server_connections"] = OperationCapability.Observe,
            ["list_server_groups"] = OperationCapability.Observe,
            ["list_server_projects"] = OperationCapability.Observe,
            ["list_local_sessions"] = OperationCapability.Observe,
            ["get_lock_state"] = OperationCapability.Observe,
            ["browse_project_tree_v3_snapshot"] = OperationCapability.Observe,
            ["read_hardware_config"] = OperationCapability.Observe,
            ["read_hardware_page_candidates"] = OperationCapability.Observe,
            ["search_equipment_catalog"] = OperationCapability.Observe,
            ["read_cross_references"] = OperationCapability.Observe,
            ["list_tag_tables"] = OperationCapability.Observe,
            ["list_network_objects"] = OperationCapability.Observe,
            ["inspect_network_object"] = OperationCapability.Observe,
            // Offline download-path listing: reads DownloadProvider.Configuration only, never goes online.
            [PlcDownloadMethods.ListTargets] = OperationCapability.Observe,

            // SessionSelection (all presets, selects an already-open project)
            ["select_portal_project"] = OperationCapability.SessionSelection,

            // TemporaryExport (read-only safe, temporary files with cleanup)
            ["get_block_content"] = OperationCapability.TemporaryExport,
            ["get_type_content"] = OperationCapability.TemporaryExport,

            // Compile (NOT read-only safe)
            ["compile_check"] = OperationCapability.Compile,

            // ProjectLifecycle (NOT read-only safe)
            ["open_project"] = OperationCapability.ProjectLifecycle,
            ["create_project"] = OperationCapability.ProjectLifecycle,
            ["retrieve_project"] = OperationCapability.ProjectLifecycle,
            ["save_project"] = OperationCapability.ProjectLifecycle,
            ["save_project_as"] = OperationCapability.ProjectLifecycle,
            ["archive_project"] = OperationCapability.ProjectLifecycle,
            ["close_project"] = OperationCapability.ProjectLifecycle,

            // ProjectMutation (NOT read-only safe)
            ["update_block_logic"] = OperationCapability.ProjectMutation,
            ["update_type_content"] = OperationCapability.ProjectMutation,
            ["create_block"] = OperationCapability.ProjectMutation,
            ["delete_block"] = OperationCapability.ProjectMutation,
            ["create_block_group"] = OperationCapability.ProjectMutation,
            ["delete_block_group"] = OperationCapability.ProjectMutation,
            ["create_tag_table"] = OperationCapability.ProjectMutation,
            ["delete_tag_table"] = OperationCapability.ProjectMutation,
            ["create_tag"] = OperationCapability.ProjectMutation,
            ["update_tag"] = OperationCapability.ProjectMutation,
            ["delete_tag"] = OperationCapability.ProjectMutation,
            ["create_user_constant"] = OperationCapability.ProjectMutation,
            ["update_user_constant"] = OperationCapability.ProjectMutation,
            ["delete_user_constant"] = OperationCapability.ProjectMutation,
            ["add_network_device"] = OperationCapability.ProjectMutation,
            ["configure_network_device"] = OperationCapability.ProjectMutation,
            ["create_subnet"] = OperationCapability.ProjectMutation,
            ["update_subnet"] = OperationCapability.ProjectMutation,
            ["delete_subnet"] = OperationCapability.ProjectMutation,
            [Network.NetworkIoSystemMethods.CreateIoSystem] = OperationCapability.ProjectMutation,

            // OnlineControl (NOT read-only safe): stops, downloads to and restarts a PLCSIM target.
            [PlcDownloadMethods.Download] = OperationCapability.OnlineControl,

            // Internal non-opening lifecycle probes and verification reads require a writable mode.
            ["probe_project_status_for_lifecycle"] = OperationCapability.ProjectLifecycle,
            ["probe_open_project_rebind"] = OperationCapability.ProjectLifecycle,
            ["get_basic_project_status"] = OperationCapability.ProjectLifecycle,
        };

        foreach (var method in HmiReadMethods.All)
            dict[method] = OperationCapability.Observe;
        foreach (var method in HmiWriteMethods.All)
            dict[method] = OperationCapability.ProjectMutation;
        foreach (var method in VciMethods.Read)
            dict[method] = OperationCapability.Observe;
        dict[VciMethods.Plan] = OperationCapability.Observe;
        foreach (var method in VciMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        foreach (var method in PlcOnlineMethods.ObserveMethods)
            dict[method] = OperationCapability.Observe;
        foreach (var method in PlcOnlineMethods.TemporaryExportMethods)
            dict[method] = OperationCapability.TemporaryExport;
        foreach (var method in PlcOnlineMethods.OnlineObserveMethods)
            dict[method] = OperationCapability.OnlineObserve;
        foreach (var method in PlcOnlineMethods.ProjectMutationMethods)
            dict[method] = OperationCapability.ProjectMutation;
        foreach (var method in PlcOnlineMethods.OnlineControlMethods)
            dict[method] = OperationCapability.OnlineControl;
        foreach (var method in PlcSimMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in PlcSimMethods.Control)
            dict[method] = OperationCapability.SimulationControl;
        foreach (var method in PlcBlockEditMethods.Read)
            dict[method] = OperationCapability.TemporaryExport;
        foreach (var method in PlcBlockEditMethods.Write.Concat(PlcBlockEditMethods.PlannedEdits))
            dict[method] = OperationCapability.ProjectMutation;
        foreach (var method in Network.HardwareModuleMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Network.HardwareModuleMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // Host-only file export through the guarded network_write pipeline: writes outside TIA Portal, so writable modes only.
        dict[Network.HardwareModuleMethods.ExportIoMap] = OperationCapability.ProjectMutation;
        dict[PlcTagMoveMethods.MoveTag] = OperationCapability.ProjectMutation;
        // Host-computed plc_read operations over list_tag_tables and read_hardware_config.
        dict[PlcTagMoveMethods.ReadAddressOccupancy] = OperationCapability.Observe;
        dict[PlcTagMoveMethods.FindFreeAddress] = OperationCapability.Observe;
        // Project and global libraries: reads and write planning observe; every library_write method changes the
        // project, a library or library files (global-library lifecycle items are confirmed by the write domain).
        foreach (var method in LibraryMethods.Read)
            dict[method] = OperationCapability.Observe;
        dict[LibraryMethods.Plan] = OperationCapability.Observe;
        // plc_write planning: reads the worker's block export ledger (V21 textual-interface pitfall); exports nothing.
        dict[PlcBlockExportStateMethods.GetExportState] = OperationCapability.Observe;
        foreach (var method in LibraryMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // Host-computed plc_read list_networks over get_block_content format sd (an export into a temporary directory).
        foreach (var method in Plc.SimaticSd.SimaticSdMethods.Reads)
            dict[method] = OperationCapability.TemporaryExport;
        // Host-computed plc_read program analysis over the tree, read_cross_references and get_block_content.
        foreach (var method in PlcAnalysisMethods.All)
            dict[method] = OperationCapability.Observe;
        // External sources, project-tree folders, file exports and the accessible-device scan. The scan performs
        // live network discovery on an explicitly named PC interface and changes nothing (OnlineObserve, every
        // mode, like compare_to_online); find_objects is a host computation over existing reads; the file exports
        // write outside TIA Portal (ProjectMutation, writable modes only, like export_io_map).
        foreach (var method in PlcSourceFolderMethods.ObserveWorkerReads.Concat(PlcSourceFolderMethods.HostReads))
            dict[method] = OperationCapability.Observe;
        foreach (var method in PlcSourceFolderMethods.OnlineObserveWorkerReads)
            dict[method] = OperationCapability.OnlineObserve;
        foreach (var method in PlcSourceFolderMethods.AllWrites)
            dict[method] = OperationCapability.ProjectMutation;
        // PLC alarms, alarm classes, text lists and ProDiag supervisions: the inventory observes, the workbook reads export
        // to a temporary file that is deleted again, and every write changes the project or writes a caller file.
        foreach (var method in PlcAlarmMethods.ObserveMethods)
            dict[method] = OperationCapability.Observe;
        // The non-mutating planning evidence of plc_write's alarm items (binds members, never calls them).
        dict[PlcAlarmMethods.Plan] = OperationCapability.Observe;
        foreach (var method in PlcAlarmMethods.TemporaryExportMethods)
            dict[method] = OperationCapability.TemporaryExport;
        foreach (var method in PlcAlarmMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // Technology objects (plc_read/plc_write): reads observe or export through a temporary file; every write changes
        // the project, and export_technology_object writes a caller file outside TIA Portal (writable modes only).
        foreach (var method in Plc.TechnologyObjects.TechnologyObjectMethods.Observe)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Plc.TechnologyObjects.TechnologyObjectMethods.TemporaryExport)
            dict[method] = OperationCapability.TemporaryExport;
        foreach (var method in Plc.TechnologyObjects.TechnologyObjectMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // CPU protection (plc_read/plc_write): the read observes the CPU's protection services; the writes lower the
        // protection in the offline project (no password is ever handled).
        foreach (var method in Plc.Security.PlcSecurityMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Plc.Security.PlcSecurityMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // Fail-safe (F-) PLCs: the reads observe the safety administration and the F-block signatures; every write changes the
        // safety offline program's access state or the safety program.
        foreach (var method in Plc.Safety.PlcSafetyMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Plc.Safety.PlcSafetyMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // The project's block-compilation settings (plc_write set_project_compilation_settings, K10): an offline project change.
        dict[Project.ProjectCompilationSettingsMethods.Set] = OperationCapability.ProjectMutation;
        // Offline change detection (plc_read get_plc_checksums, get_fingerprints; issue #6): reads of the offline project.
        foreach (var method in Plc.ChangeDetection.PlcChangeDetectionMethods.Read)
            dict[method] = OperationCapability.Observe;
        // A loadable file written to a caller path (plc_write generate_loadable, issue #26), like the file exports.
        dict[Plc.Loadable.PlcLoadableMethods.GenerateLoadable] = OperationCapability.ProjectMutation;
        // Block know-how and write protection (issue #5): the read observes the block's protection providers, the writes
        // change the offline project with passwords from password sources.
        foreach (var method in Plc.BlockProtection.PlcBlockProtectionMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Plc.BlockProtection.PlcBlockProtectionMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // Project languages and project texts (issue #8): the read observes LanguageSettings; the writes change the project's
        // languages or texts, or write a caller file (the export, like the other file exports).
        foreach (var method in Project.ProjectLanguageMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Project.ProjectLanguageMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // The settings family (issues #15, #13, #12, #14, #21, #25): reads observe services of the offline project; writes change
        // the offline project or write a caller file (printouts, reports, certificate exports).
        foreach (var method in Plc.Settings.PlcSettingsCatalog.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Plc.Settings.PlcSettingsCatalog.ExportingReads)
            dict[method] = OperationCapability.TemporaryExport;
        foreach (var method in Plc.Settings.PlcSettingsCatalog.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // check_activation_test is a plc_read operation, but TestValidity.CheckValidity() is documented only as "Checks the validity
        // of the item" (Siemens.Engineering.SafetyValidation.xml, V21): whether it stores a validity state in the project is not
        // documented, so it is classified conservatively as a write (security review 2026-11-03, U1): read-only refuses it.
        dict[Plc.Settings.PlcSafetyValidationMethods.CheckActivationTest] = OperationCapability.ProjectMutation;
        // OPC UA server interfaces and access rules (issue #24): reads of the offline project; writes change it (export writes a caller file).
        foreach (var method in Plc.OpcUa.PlcOpcUaMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in Plc.OpcUa.PlcOpcUaMethods.Write)
            dict[method] = OperationCapability.ProjectMutation;
        // Issue #16: a folder download writes caller files (no device); the online master secret acts on the connected PLC.
        dict[Plc.DeviceData.PlcDeviceDataMethods.DownloadToFolder] = OperationCapability.ProjectMutation;
        foreach (var method in Plc.DeviceData.PlcDeviceDataMethods.Online)
            dict[method] = OperationCapability.OnlineControl;
        foreach (var method in PlcWebApiMethods.Read)
            dict[method] = OperationCapability.Observe;
        foreach (var method in PlcWebApiMethods.Control)
            dict[method] = OperationCapability.OnlineControl;

        return dict;
    }
}
