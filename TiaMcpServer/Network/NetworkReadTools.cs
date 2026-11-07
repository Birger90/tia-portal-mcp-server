using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkReadTools
{
    private const string ToolName = "network_read";

    [McpServerTool(
        Name = ToolName,
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Run up to 50 dedicated network read operations in one call. Valid operations: read_hardware_config, search_equipment_catalog (query), list_network_objects (objectKinds), inspect_network_object (target; for a deviceItem it returns every hardware parameter with access, supportedTypes and typed value), list_plug_locations (target kind 'device' or 'deviceItem'; the free plug positions and labels from GetPlugLocations), find_module_types (partial typeIdentifier; the matching module versions), find_container_types (typeIdentifier; the container types of a network-view module) and list_io_addresses (deviceItem target; every Address with ioType, startAddress, length, context, processImage, isochronousMode, interruptObNumber; when the item has no addresses of its own, the addresses of its sub-items depth-first, each with the subItemPath it belongs to, e.g. a module whose addresses sit on a sub-item without type identifier). Custom identities (issue #18; not live-tested): get_custom_identity (target: kind device with deviceName, or a deviceItem selector; key) calls CustomIdentityProvider.Get(key) of that device or device item, the provider found among its GetServiceInfos() (not listed: unsupported_capability); observed.state is found (value), notFound (TIA Portal threw CustomIdentityNotFoundException; not a failure) or null. PROFINET topology (issue #10; not live-tested): list_port_connections (target: kind device with deviceName, or a deviceItem selector) lists every port device item of the device or of the item and its sub-items (a device item that offers the NetworkPort service among its GetServiceInfos()) with interfaceOwner (NetworkPort.Interface.OwnedBy) and connectedPorts (NetworkPort.ConnectedPorts, which Siemens marks 'Internal use only'; each partner is named by the project device item whose port equals it, port null when none does; connectedPortsState unreadable when the property cannot be read); cable length and signal delay are attributes of the port device item: read them with inspect_network_object; the write tool sets them as hardware attributes. Transfer areas (issue #11; not live-tested): package G hardware features (not live-tested; target: the deviceItem that offers the feature among its GetServiceInfos(), otherwise unsupported_capability): get_interface_nodes (NetworkInterface.Nodes with each ConnectedSubnet), get_io_controller (IoConnector.GetIoController() of each IO connector of the interface, with the IO system), get_im_partner_port (ImConnection.GetPartnerPort()), get_pc_interface_assignment (PcInterfaceAssignment: PcInterfaceAssignmentMode, HardwareResource, IpcExpansion, SoftwarePlc, GetAvailableIPCExpansions()), get_module_description_update (ModuleDescriptionUpdater.CanUpdate), list_communication_connections (CommunicationManagement.Connections: name, type, validity, local and partner target and subnet, 0-based index order); list_transfer_areas (deviceItem target providing the NetworkInterface) lists NetworkInterface.TransferAreas (I-slave/I-device address mapping) and MulticastableTransferAreas (e.g. DDX) with index, name, the documented properties as text (state/value/runtimeType; collection-valued ones as <Name>Count) and the attributes GetAttributeInfos lists with their access mode; a composition the installation does not offer is null with a message. Reads run independently, so a failing item does not stop later operations. Each operation result is returned as declared JSON, not as a nested JSON string. Large catalog searches can be narrowed with query/maxResults or split into separate network_read calls. list_network_objects uses pageSize (1..200), default 100 on every page. read_hardware_config accepts optional deviceName (case-insensitive station/root device name as in devices[].name, not a PLC/CPU device-item name), plcName (PLC software name, used only for tag matching), includeIoDetails (structured addresses and channels), and includeTagMatches (requires includeIoDetails). For bounded hardware pagination, supply pageSize (1..200) or cursor; cursor alone defaults to 100. Page size counts one combined sequence, devices first, then subnets, while the result keeps separate arrays. On continuation, deviceName, plcName, includeIoDetails, and includeTagMatches must remain unchanged. Detailed output can be large, so filter by deviceName or split into separate calls.")]
    public static Task<CallToolResult> NetworkRead(
        OpennessWorkerClient workerClient,
        NetworkReadOperationExecutor operationExecutor,
        [Description("Ordered list of dedicated network read operations. Each item is { operationId, operation, ...operation parameters }.")] NetworkOperationRequest[] operations,
        CancellationToken cancellationToken = default)
        => BatchReadTool.RunAsync(workerClient, operations, ToolName, NetworkContractVersion.Current,
            NetworkOperationCatalog.ValidateRead, NetworkOperationCatalog.ValidateAccessMode,
            operationExecutor.ExecuteAsync,
            RetryGuidance, cancellationToken);

    /// <summary>
    /// Bounds a batch against the exact <c>network_read</c> response document. Internal so tests can
    /// drive the real wiring — retry tool, per-operation guidance, envelope — at small limits.
    /// </summary>
    internal static StructuredOperationBatch ApplyBudget(
        StructuredOperationBatch batch,
        int? maxItemChars = null,
        int? maxDocumentChars = null)
        => BatchReadTool.ApplyBudget(batch, ToolName, NetworkContractVersion.Current,
            RetryGuidance, maxItemChars, maxDocumentChars);

    private static string RetryGuidance(StructuredOperationItem item) => item.Operation switch
    {
        // A catalog search has its own narrowing knobs, so recommend those before a re-split.
        "search_equipment_catalog" =>
            "Narrow query or maxResults, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call.",

        "read_hardware_config" when IsHardwarePage(item) => HardwarePageProjector.RetryGuidance,

        "read_hardware_config" =>
            "Narrow with deviceName and disable includeIoDetails/includeTagMatches where possible, "
                + "or split the batch: re-run this operationId in its own "
                + $"{ToolName} call.",

        "inspect_network_object" =>
            "Request fewer attributeNames, or split the batch: re-run this operationId in its own "
                + $"{ToolName} call.",

        _ => $"Split the batch: re-run this operationId in its own {ToolName} call.",
    };

    private static bool IsHardwarePage(StructuredOperationItem item)
        => item.Result is { ValueKind: System.Text.Json.JsonValueKind.Object } result
            && result.TryGetProperty("pagination", out var pagination)
            && pagination.ValueKind == System.Text.Json.JsonValueKind.Object;

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, NetworkContractVersion.Current, category, message);
}
