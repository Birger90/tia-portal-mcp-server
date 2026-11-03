using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

public static class NetworkGuardDefinitions
{
    public const string ConnectedDelete = "network_delete_connected_subnet";
    public const string Unverifiable = "network_state_unverifiable";
    public const string DeletesDeviceItem = "network_deletes_device_item";
    public const string ChangesDeviceItemType = "network_changes_device_item_type";
    public const string AddressTagsNotRewired = "network_io_address_tags_not_rewired";
    public const string AttributeErrorsIgnored = "network_attribute_errors_ignored";
    public const string ExportOverwritesFile = "network_export_overwrites_file";
    public const string IoSystemCreateUnverified = "network_io_system_create_unverified";
    public const string IoSystemNodeUnconnected = "network_io_system_node_unconnected";
    public const string IoSystemAlreadyAssigned = "network_io_system_already_assigned";
    public const string PortLinkExists = "network_port_link_exists";
    public const string PortLinkMissing = "network_port_link_missing";
    public const string DeletesTransferArea = "network_deletes_transfer_area";
    public const string DeletesIoSystem = "network_deletes_io_system";
    public const string DisconnectsIoConnector = "network_disconnects_io_connector";
    public const string CaxExportOverwritesFile = "network_cax_export_overwrites_file";
    public const string CaxImportChangesHardware = "network_cax_import_changes_hardware";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(ConnectedDelete, WriteGuardSeverities.Info, "Deleting this subnet removes the listed node connections; devices and nodes are preserved."),
        new WriteGuardDefinition(Unverifiable, WriteGuardSeverities.Info, "Required Network consequence evidence could not be verified; the item runs and TIA Portal decides."),
        new WriteGuardDefinition(DeletesDeviceItem, WriteGuardSeverities.Info, "Deleting the device item removes it, its sub-items and their nodes' subnet connections; tags are not changed."),
        new WriteGuardDefinition(ChangesDeviceItemType, WriteGuardSeverities.Info, "ChangeType reports no details of what it changed; parameters, addresses and sub-items may change."),
        new WriteGuardDefinition(AddressTagsNotRewired, WriteGuardSeverities.Info, "A start-address change does not rewire assigned PLC tags and may change the coupled input/output range."),
        new WriteGuardDefinition(AttributeErrorsIgnored, WriteGuardSeverities.Info, "Policy 'ignore' lets TIA Portal skip a rejected attribute and apply the others (partial update)."),
        new WriteGuardDefinition(ExportOverwritesFile, WriteGuardSeverities.Info, "export_io_map replaces an existing file because overwrite:true was requested."),
        new WriteGuardDefinition(IoSystemCreateUnverified, WriteGuardSeverities.Info, "create_io_system (live-tested once on V21, 2026-11-02): IoController.CreateIoSystem is bound at runtime and the result is read back from the subnet."),
        new WriteGuardDefinition(IoSystemNodeUnconnected, WriteGuardSeverities.Info, "The IO controller's node is not connected to a subnet in the planning snapshot; TIA Portal decides whether an IO system can be created."),
        new WriteGuardDefinition(IoSystemAlreadyAssigned, WriteGuardSeverities.Info, "The node's interface already belongs to an IO system in the planning snapshot; TIA Portal decides whether another one can be created."),
        new WriteGuardDefinition(PortLinkExists, WriteGuardSeverities.Info, "The ports are already linked in the planning read; the Siemens skill reports that ConnectToPort then throws. The item runs and TIA Portal decides."),
        new WriteGuardDefinition(PortLinkMissing, WriteGuardSeverities.Info, "The ports are not linked in the planning read (or the link is unreadable); the item runs and TIA Portal decides."),
        new WriteGuardDefinition(DeletesTransferArea, WriteGuardSeverities.Info, "Deleting a transfer area removes its address mapping; partner areas and PLC tags are not changed by the server."),
        new WriteGuardDefinition(DeletesIoSystem, WriteGuardSeverities.Info, "Deleting an IO system disconnects its IO devices; the devices, their nodes and the subnet remain."),
        new WriteGuardDefinition(DisconnectsIoConnector, WriteGuardSeverities.Info, "The device item's IO connector leaves its IO system; the device and its subnet connection remain."),
        new WriteGuardDefinition(CaxExportOverwritesFile, WriteGuardSeverities.Info, "export_cax lets TIA Portal write over an existing file because overwrite:true was requested."),
        new WriteGuardDefinition(CaxImportChangesHardware, WriteGuardSeverities.Info, "import_cax adds or changes hardware in the project with no rollback; the item runs and TIA Portal decides.")
    }, "Network");

}
