using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

[McpServerToolType]
public class NetworkWriteTools
{
    [McpServerTool(Name = "network_write", ReadOnly = false, Destructive = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(NetworkGuardedWriteResponse))]
    [Description("Execute up to 50 ordered network writes against one verified already-open project. Omitted dryRun or dryRun:false executes; use dryRun:true to preview effects and guards without mutation. No confirmation prompt is sent. Operations: add_network_device (typeIdentifier, deviceName), configure_network_device (exact target.deviceName and target.nodeId, changes), create_subnet (subnet.name, subnet.networkType), update_subnet (target.kind='subnet', exact target.subnetId, subnetChanges), delete_subnet (target.kind='subnet', exact target.subnetId), create_io_system (target kind 'node', the CPU's PROFINET node; optional ioSystem { name }; IoController.CreateIoSystem, live-tested once on V21 2026-11-02; the result's subnetId and number are what configure_network_device changes.ioSystem takes to attach an IO device's node), plug_device_item (container target kind 'device' or 'deviceItem', typeIdentifier, positionNumber, optional deviceItemName; TIA Portal may place a module plugged into a rack or rail directly under the device: the result reports placedUnder and the item's own path), move_device_item and copy_device_item (deviceItem target, positionNumber, optional destination; default destination is the current parent), delete_device_item (deviceItem target), change_device_item_type (deviceItem target, typeIdentifier), set_hardware_attributes (target, attributes [{ name, kind, value }], optional attributeErrorPolicy abort|ignore and attributeWriteMode bulk|sequential), set_io_address (deviceItem target, address { ioType, startAddress, length? } as list_io_addresses reports it, a sub-item's address included; addressChanges), set_custom_identity (target device or deviceItem, key, value; CustomIdentityProvider.Set, read back with Get) and remove_custom_identity (target, key; CustomIdentityProvider.Remove, a missing key is target_not_found; read back with Get) (issue #18, not live-tested; the provider is found among the object's GetServiceInfos(), unsupported_capability when not listed), connect_ports and disconnect_ports (issue #10, not live-tested; target: the port deviceItem, partner: the other port deviceItem, both as list_port_connections reports them; NetworkPort.ConnectToPort/DisconnectFromPort(partner), read back from ConnectedPorts; an existing link (connect) or a missing one (disconnect) is reported as an info guard and still passed to TIA Portal, which decides), delete_io_system (target kind 'ioSystem'; IoSystem.Delete() of a PROFINET IO system or a DP master system, re-resolved afterwards, postcondition_failed when still listed; info guard network_deletes_io_system lists the IO devices that are disconnected), disconnect_from_io_system (deviceItem target providing the IO device's NetworkInterface; IoConnector.DisconnectFromIoSystem() of its connected IO connector (several connected or several unconnected connectors: target_ambiguous), read back from ConnectedToIoSystem), create_transfer_area (deviceItem target providing the NetworkInterface, transferArea { collection transferAreas: name, type, optional positionNumber -> TransferAreaComposition.Create; collection multicastable: type, partner (the partner interface's deviceItem), optional name and dataLength -> MulticastableTransferAreaComposition.Create, or templateIndex of a partner's multicastable area -> Create(area, type) }; read back from the composition), update_transfer_area (target, transferArea { collection, index, name as evidence }, attributes as list_transfer_areas lists them; SetAttribute per attribute, read back) and delete_transfer_area (target, transferArea { collection, index, name }; Delete(), the composition must list one area fewer) (issue #11, not live-tested; a DP master system has no separate V21 member: create_io_system on a PROFIBUS DP master node, TIA Portal decides; a DP slave joins with configure_network_device changes.ioSystem), package G hardware features (not live-tested; info guard network_hardware_feature_changes; each read back where a documented read exists): create_subnet_and_connect_node (target interface, newSubnetName, optional nodeName; Node.CreateAndConnectToSubnet(String), ConnectedSubnet read back), disconnect_node_from_subnets (target, optional nodeName; Node.DisconnectFromSubnet(), no subnet afterwards), connect_im_port (target, partner port deviceItem; ImConnection.Connect(DeviceItem), GetPartnerPort read back) and disconnect_im_port (ImConnection.Disconnect()), assign_pc_interface (target, assignmentMode, optional partner = the CPU; PcInterfaceAssignmentMode read back), update_module_description (ModuleDescriptionUpdater.UpdateModuleDescription(); false is postcondition_failed), export_telecontrol_data_points (target, filePath, optional overwrite; TelecontrolManagement.ExportDataPoints, staged, moved into place and re-hashed) and import_telecontrol_data_points (target, filePath; ImportDataPoints of a private copy, data point counts before and after), create_communication_connection (target local interface, partner interface deviceItem, connectionType (the connection type names the field lists), optional nodeName, partnerNodeName; Connections.Create<T>(Node, DeviceItem, Node), one more connection read back) and delete_communication_connection (target, connectionIndex from list_communication_connections, optional connectionName checked first; Connection.Delete(), gone afterwards); export_cax (absolute .aml filePath, optional deviceName (one device; omitted: the whole project), logFilePath and overwrite; CaxProvider.Export(Device|ProjectBase, FileInfo) returning a TransferResult, or with logFilePath the log overload (true/false); the provider is found among the project's GetServiceInfos(); staged next to the target, moved into place and re-hashed; an existing file needs overwrite:true) and import_cax (absolute existing .aml filePath, importOption, optional logFilePath and overwrite (for the log); CaxProvider.Import; an existing logFilePath of either operation needs overwrite:true, its directory link-checked; no rollback, info guard network_cax_import_changes_hardware; the result lists the project's device names before and after) (issue #9, not live-tested; a TransferResult with state Error or false from a log overload is engineering_rejected with the messages) and export_io_map (absolute .csv filePath, optional overwrite, csvDelimiter, deviceName, plcName, includeTagMatches; writes the I/O map file; an existing file needs overwrite:true). Plug, move and copy are checked with CanPlugNew/CanPlugMove/CanPlugCopy during planning; a false answer, an unknown or read-only attribute or a value that does not convert fails as validation_error before anything changes. Hardware-module items read back their own result (identity, attribute values, addresses) and fail postcondition_failed on a mismatch. Omitted change members stay unchanged. Connected subnet deletion removes the listed connections while preserving devices and nodes; listed identities reflect the planning snapshot only. Incomplete deletion consequence evidence is reported (network_state_unverifiable, info) and the item runs; TIA Portal decides. Phases: preview, applied, blocked, error. Stops on the first configuration rejection or other failure, with no rollback or automatic replay. During mutation methods, EngineeringTargetInvocationException returns engineering_rejected; other EngineeringException returns worker_operation_failed, with portal_lost taking priority. Configuration failures carry appliedSettings when available; earlier settings may remain applied. Verification reports trusted or superseded from completed operations without read-back. Re-read affected objects with network_read before retrying. This tool never saves, compiles, downloads, or controls a PLC. A PROFINET device name is only settable once changes.pnDeviceNameAutoGeneration is false; pass both in the same item.")]
    public static Task<CallToolResult> NetworkWrite(
        OpennessWorkerClient workerClient,
        WriteExecution execution,
        [Description("Ordered network write operations.")] NetworkOperationRequest[] operations,
        [Description("True previews effects and guards without mutation; omitted or false executes.")] bool dryRun = false,
        CancellationToken cancellationToken = default)
        => execution.RunAsync(new NetworkWriteDomain(workerClient),
            new WriteCall<NetworkOperationRequest>(
                operations ?? [], dryRun), cancellationToken: cancellationToken);

}

internal static class NetworkWriteToolRegistration
{
    internal const int MaxRepeatedSelectorFieldDescription = 100;

    internal const string RepeatedSelectorNote = " Same selector fields as target; their full rules are under target.";

    /// <summary>
    /// K91: partner and destination repeat the target selector's schema; their field descriptions are shortened to the first
    /// sentence (none when longer than <see cref="MaxRepeatedSelectorFieldDescription"/>), the full rules stay under target.
    /// Types, nullability and nesting are unchanged, and validation never reads descriptions.
    /// </summary>
    internal static JsonNode ShortenRepeatedSelectors(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.PropertyInfo is not { Name: "partner" or "destination" } property
            || property.DeclaringType != typeof(NetworkOperationRequest)
            || schema is not JsonObject node || node["properties"] is not JsonObject fields)
            return schema;
        foreach (var (_, field) in fields)
        {
            if (field is not JsonObject fieldNode || fieldNode["description"] is not JsonValue value
                || !value.TryGetValue<string>(out var text)) continue;
            if (Tools.SchemaDescriptions.FirstSentence(text, MaxRepeatedSelectorFieldDescription) is { } shortText)
                fieldNode["description"] = shortText;
            else fieldNode.Remove("description");
        }

        if (node["description"] is JsonValue own && own.TryGetValue<string>(out var ownText))
            node["description"] = ownText + RepeatedSelectorNote;
        return schema;
    }

    /// <summary>network_write: the nullable current settings, then the shortened repeated selectors.</summary>
    internal static JsonNode TransformWrite(AIJsonSchemaCreateContext context, JsonNode schema)
        => ShortenRepeatedSelectors(context, AllowNullCurrentSettings(context, schema));

    internal static JsonNode AllowNullCurrentSettings(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.TypeInfo.Type == typeof(NetworkWriteEffect)
            && schema is JsonObject effectSchema
            && effectSchema["properties"]?["currentSettings"]?["additionalProperties"] is JsonObject values)
            values["type"] = new JsonArray("string", "null");
        return schema;
    }
}
