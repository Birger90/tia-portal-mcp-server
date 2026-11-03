# TIA Portal Device Operations

**Language:** English · [Deutsch](DEVICES_OPERATIONS_SUMMARY.de.md)

## Supported operations

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `network_read` | `read_hardware_config` | Recursively discovers devices both at project root and in nested device groups, then reads device items, network interfaces, nodes, subnets, and IO systems. Optional `deviceName` filter, `plcName`, opt-in structured I/O extraction (`includeIoDetails`, `includeTagMatches`), and bounded pagination (`pageSize`, `cursor`) — see [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md). |
| `network_read` | `search_equipment_catalog` | Requires `query`; accepts bounded `maxResults`; returns catalog type identifiers for candidate devices. |
| `network_write` (guarded) | `add_network_device` | Requires an exact catalog `typeIdentifier` and `deviceName`; accepts optional `deviceItemName`. |
| `network_write` (guarded) | `configure_network_device` | Requires `target: { deviceName, nodeId }` naming an existing node and `changes` with at least one of `ipAddress`, `subnetMask`, `pnDeviceNameAutoGeneration`, `pnDeviceName`, `subnet: { subnetId }`, and `ioSystem: { subnetId, number }`. |
| `network_read` | `list_plug_locations`, `find_module_types`, `find_container_types`, `list_io_addresses` | Free plug positions of a device or device item, module versions and container types from `ModuleInformationProvider`, and the `Address` objects of a device item. See [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md). |
| `network_write` (guarded) | `plug_device_item`, `move_device_item`, `copy_device_item`, `delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`, `export_io_map` | Rack/slot modules (`CanPlug*`/`Plug*`), delete, change type, typed hardware-parameter writes (bulk `SetAttributes` with `AttributeDelegate` or sequential), I/O start addresses and the I/O map CSV export. See [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md); not live-tested. |
| `browse_project_tree` | `browse_project_tree` | Recursively discovers the same direct and grouped devices as flat `Device` nodes; accepts optional `startSelector`, `depth`, `pageSize`, and `cursor`. |

`add_network_device` and `configure_network_device` are data writes. Preview with `dryRun:true`; execute with `dryRun:false` or omitted `dryRun`, without server elicitation. Exact already-open verified binding is required; no tokens, rollback or automatic replay. The catalog identifier and device name are validated before the worker performs the change.

## Device data model

The hardware read path is an inspection surface. The write path is limited to catalog-based device creation and selected device/network identity fields:

- Device creation uses a catalog `typeIdentifier`.
- `deviceItemName` applies to creation and defaults to `deviceName` when omitted; a blank or whitespace value is a `validation_error`.
- Hardware results can contain nested device items and network metadata.
- Hardware parameters are read with `inspect_network_object` (deviceItem targets) and written with `set_hardware_attributes` (device or device item; names, access and types come from `GetAttributeInfos`).

## Current limits

The current surface does not provide:

- Device-group creation, deletion, rename, or moving devices between groups. Existing grouped devices are discovered by read operations but group folders are not exposed as tree nodes.
- Plugging into a device or module created earlier in the same `network_write` call (one hardware
  snapshot plans the call).
- Channel or address-controller writes, `AssignProcessImageToOrganizationBlock`, hardware
  identifiers and diagnostics data. I/O start addresses, process image, isochronous mode and
  interrupt OB number are writable with `set_io_address`.
- Startdrive `DriveItemHardwareModule.ChangeType`, extension-rack connections, PSC export and the
  other omissions listed in [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#omitted-and-why).
- A device-level software-container API; PLC blocks, tags, and constants use the PLC operations described in [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md).

## Related operations

Use [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) for network identity configuration and [README.md](README.md) for batch safety and response warnings.
