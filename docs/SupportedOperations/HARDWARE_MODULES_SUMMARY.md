# Hardware Modules, Hardware Parameters, I/O Addresses and Tag Extras

**Language:** English · [Deutsch](HARDWARE_MODULES_SUMMARY.de.md)

Status: **implemented offline, not live-tested.** Every Siemens member below was taken from the
V21 documentation sources listed under [Provenance](#provenance); none was verified against an
installed TIA Portal V21. The FakeWorker fixture models object access only and is no evidence of
Siemens behavior. Run the [live acceptance list](#live-acceptance-list) on a disposable project
before relying on these operations.

The operations extend existing tools; they added no tool (the counts were 10/27/27, 11/30/30 since the library tools, 12/31/31 since `tia_inbox`, 13/32/32 since `siemens_docs`):

| Tool | Operations added |
|---|---|
| `network_read` | `list_plug_locations`, `find_module_types`, `find_container_types`, `list_io_addresses` |
| `network_write` | `plug_device_item`, `move_device_item`, `copy_device_item`, `delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`, `export_io_map` |
| `plc_read` | `read_address_occupancy`, `find_free_address` |
| `plc_write` | `move_tag` |

Reading hardware parameters already existed: `inspect_network_object` on a `deviceItem` target
returns every modeled and dynamic attribute with `access`, `supportedTypes` and a typed `value`.
Setting a tag's external-access flags already existed as `update_tag` (`externalAccessible`,
`externalVisible`, `externalWritable`); no second operation was added.

## Access modes

Reads are `Observe` (every mode). The hardware writes, `move_tag` and `export_io_map` are
`ProjectMutation`: read-write and full only, no confirmation prompt, guarded single-call pipeline
(validation, verified binding, planning reads, guards, `dryRun`, mutation, verification, one audit
record), exactly like the other `network_write`/`plc_write` items. `export_io_map` writes a file
outside TIA Portal; it is classified `ProjectMutation` so read-only never writes anything. There is
no block guard: only invalid or ambiguous input, an unreadable inventory and TIA Portal's own
feasibility answers stop a call.

## Targets

Hardware-module items name their object with `target`:

- `{ "kind": "deviceItem", "deviceName", "itemPath": [{ index, name, positionNumber, typeIdentifier }, ...] }`
  — the selector `read_hardware_config` and `list_network_objects` return; resolved by exact index
  path and verified against `name` and `positionNumber`, and against `typeIdentifier` when it is given
  (no name-only fallback). `typeIdentifier` is optional: built-in items (a CPU's interfaces, ports, card
  reader, display, channel sub-items) have none, are reported with `typeIdentifier: null` and are
  selectable; a request may pass `null`, `""` or omit it (K95, not live-tested);
- `{ "kind": "device", "deviceName" }` — only for `list_plug_locations`, `plug_device_item` (the
  container), `set_hardware_attributes` and the custom identity operations, and for `destination`.

One hardware snapshot (`read_hardware_config`) plans the call, as for every `network_write` item:
an item cannot target a device or module created by an earlier item of the same call. Run a second
call after re-reading.

## network_read operations

| Operation | Input | Result |
|---|---|---|
| `list_plug_locations` | `target` (device or deviceItem) | `{ target, plugLocations[{ positionNumber, label }], messages }` from `HardwareObject.GetPlugLocations()` ("information about free slots") |
| `find_module_types` | `typeIdentifier` (partial: `OrderNumber:` ≥1 part, `GSD:` ≥2 parts, `System:` ≥1 part) | `{ partialTypeIdentifier, moduleTypes[] }` from `ModuleInformationProvider.FindModuleTypes` |
| `find_container_types` | `typeIdentifier` | `{ typeIdentifier, containerTypes[] }` from `ModuleInformationProvider.FindContainerTypes` (network view modules only, per the documentation) |
| `list_io_addresses` | `target` (deviceItem) | `{ target, addresses[{ index, subItemPath[{ index, name, positionNumber, typeIdentifier }], ioType, startAddress, length, context, processImage, isochronousMode, interruptObNumber }], messages }`; an unreadable or inapplicable member is null with a message |
| `list_port_connections` | `target` (device or deviceItem) | `{ target, ports[{ port, interfaceOwner, connectedPortsState, connectedPorts[{ port, runtimeType }], messages }], messages }` from `NetworkPort.ConnectedPorts` of every port device item; see [Port topology](#port-topology) |
| `list_transfer_areas` | `target` (deviceItem providing the `NetworkInterface`) | `{ target, transferAreas[], multicastableTransferAreas[], messages }`; see [IO systems, DP master systems and transfer areas](#io-systems-dp-master-systems-and-transfer-areas) |
| `get_custom_identity` | `target` (device or deviceItem), `key` | `{ target, key, observed { state, value }, providerType }` from `CustomIdentityProvider.Get(key)`; `state` `found` (with `value`), `notFound` (TIA Portal threw `CustomIdentityNotFoundException`; a successful read, not a failure) or `null` (Get returned null). See [Custom identities](#custom-identities). |

When the selected item has no `Addresses` of its own, `list_io_addresses` collects the addresses of
its sub-items depth-first (each sub-item's own addresses before its children's, at most 16 levels) and
reports each with the relative `subItemPath` it came from; `subItemPath` is empty for the item's own
addresses and `index` runs across the whole listing. Live 2026-11-02 (TIA Portal V21, K06): the
S7-1500 module `DI_32` (`OrderNumber:6ES7 521-1BL00-0AB0/V2.2`) has no addresses of its own; they sit
on its sub-item `DI_32/DI_32`, whose `TypeIdentifier` is null, so that sub-item cannot be selected as a
`deviceItem` target. The sub-item collection is not live-tested yet.

`ModuleInformationProvider` is found by enumerating `project.HwUtilities`; a container without it
fails `unsupported_capability`.

## network_write operations

| Operation | Required | Optional | Siemens calls |
|---|---|---|---|
| `plug_device_item` | `target` (container), `typeIdentifier`, `positionNumber` | `deviceItemName` (omitted: `""`, TIA Portal names the item) | `CanPlugNew`, `PlugNew` |
| `move_device_item` | `target`, `positionNumber` | `destination` (default: the current parent) | `CanPlugMove`, `PlugMove` |
| `copy_device_item` | `target`, `positionNumber` | `destination` (default: the current parent) | `CanPlugCopy`, `PlugCopy` |
| `delete_device_item` | `target` | — | `DeviceItem.Delete()` |
| `change_device_item_type` | `target`, `typeIdentifier` | — | `DeviceItem.ChangeType(String)` |
| `set_hardware_attributes` | `target`, `attributes` (1–100, unique names) | `attributeErrorPolicy` (`abort` default, `ignore`), `attributeWriteMode` (`bulk` default, `sequential`) | `SetAttributes(IEnumerable<KeyValuePair<string, object>>, AttributeDelegate)` or `SetAttribute` per attribute |
| `set_io_address` | `target`, `address { ioType, startAddress, length? }`, `addressChanges { startAddress?, processImage?, isochronousMode?, interruptObNumber? }` | — | `Address.StartAddress` setter; `SetAttribute("ProcessImage" / "IsochronousMode" / "InterruptObNumber")` |
| `set_custom_identity` | `target` (device or deviceItem), `key`, `value` (any string, also empty) | — | `CustomIdentityProvider.Set(String, String)`, read back with `Get` |
| `remove_custom_identity` | `target`, `key` | — | `CustomIdentityProvider.Remove(String)`, read back with `Get` |
| `connect_ports` | `target` (port deviceItem), `partner` (port deviceItem) | — | `NetworkPort.ConnectToPort(NetworkPort)`, read back from `ConnectedPorts` |
| `disconnect_ports` | `target`, `partner` | — | `NetworkPort.DisconnectFromPort(NetworkPort)`, read back from `ConnectedPorts` |
| `create_transfer_area`, `update_transfer_area`, `delete_transfer_area`, `delete_io_system`, `disconnect_from_io_system` | see [IO systems, DP master systems and transfer areas](#io-systems-dp-master-systems-and-transfer-areas) | | |
| `export_cax`, `import_cax` | see [CAx data (AutomationML)](#cax-data-automationml) | | |
| `export_io_map` | `filePath` (local absolute, `.csv`) | `overwrite`, `csvDelimiter` (`,` `;` tab), `rawCells` (default false), `deviceName`, `plcName`, `includeTagMatches` (default true) | none (host file write over `read_hardware_config` with `includeIoDetails`) |

Attribute values are `{ name, kind, value }` with `kind` `string`, `boolean`, `integer`, `number`
or `enum` (exact member name). The worker converts each value to a type the attribute's
`EngineeringAttributeInfo.SupportedTypes` declares: `integer` to the first declared integral type
that holds it (or a floating type when none is integral), `number` only to a floating type, `enum`
to the one declared enum type defining the member. Writing `null` is not supported.

### Planning and guards

Planning adds worker reads to the call's hardware snapshot:

- plug, move, copy: `probe_hardware_plug` evaluates `CanPlugNew`/`CanPlugMove`/`CanPlugCopy`; the
  effect carries `canPlug`. `false` fails the call as `validation_error` before any mutation (the
  mutation re-checks it directly before `PlugNew`/`PlugMove`/`PlugCopy`). For `plug_device_item` on a
  `device` target the message adds that modules usually plug into the rack item and, when the device
  has exactly one top-level item whose type identifier starts `System:Rack`, gives that item's
  `deviceItem` selector (several rack items are listed by name). This changes only the message; what
  TIA Portal allows is unchanged. Live 2026-11-02 (K29): `list_plug_locations` on device `ET200SP_1`
  listed positions 1-10 free, `CanPlugNew` on the device was false for an F-DI at position 1, and
  plugging into the rack item `Baugruppenträger_0` (`System:Rack.ET200SP`) worked.
- `set_hardware_attributes`: `read_hardware_attributes` returns the current typed values and a
  conversion verdict per value; an unknown, read-only or non-convertible attribute fails the call
  as `validation_error`. The effect lists current and requested values.
- `set_io_address`: `list_io_addresses` selects the address by `ioType`, `startAddress` and optional
  `length` among the same addresses it lists (the item's own, or its sub-items' when it has none), so a
  sub-item's address is a valid target. No match is `target_not_found`; several matches (for example
  two sub-items with the same start) are `validation_error` naming every candidate with its sub-item and
  length; add `address.length`. A sub-item address puts `subItemPath` into the effect's current
  settings. A start-address change also reads the device's I/O map with tag matches (of the owning
  sub-item).
- `export_io_map`: the path must be a local absolute path, checked lexically at validation and again
  before the write by the shared `LocalFilePaths` (Contracts), which the PLC alarm exports use too:
  the `\\?\` / `\\.\` device forms and the NT object path `\??\` with either separator (UNC shares
  `\\server\share\...` are allowed since 2026-11-01), alternate data streams (a `:` after the drive), `.`/`..` segments,
  reserved device names in any segment (CON, PRN, AUX, NUL, COM0-9, LPT0-9, CONIN$, CONOUT$, with or
  without an extension), segments ending in a space or a dot and control, bidi or wildcard characters are
  refused; mapped network drives are allowed. At validation every path segment also follows the
  file-name rules of the PLC file exports (no Windows device name such as `CON` or `COM1`, no segment
  ending in a dot or blank, no `.`/`..` segment; `PlcFileTargets.PathShapeError`); a path both rule sets
  refuse reports both reasons. The directory must exist, the path must not be a directory and an
  existing file is refused unless `overwrite:true`.

| Guard | Severity | Fires when |
|---|---|---|
| `network_deletes_device_item` | info | `delete_device_item`: names the sub-item count and connected nodes that lose their subnet connection; tags and program references are not changed |
| `network_changes_device_item_type` | info | `change_device_item_type`: ChangeType reports no details of what it changed |
| `network_io_address_tags_not_rewired` | info | `set_io_address` with `startAddress`: lists the matched tags in the current range; TIA Portal does not rewire tags and may change the coupled range |
| `network_attribute_errors_ignored` | info | `set_hardware_attributes` with `attributeErrorPolicy:"ignore"` (partial update) |
| `network_export_overwrites_file` | info | `export_io_map` replaces an existing file |

### Results and verification

Each mutation reads back its own result in the worker and fails `postcondition_failed` when the
result cannot be confirmed; an unreadable value is never treated as a match:

- plug/move/copy return the resulting item's forwardable identity (walked up through `Parent`) and
  `placedUnder`, where it actually landed. It must be a direct child of the container (or
  destination) at the requested position; `PlugNew` must also carry the requested name when one was
  given. When the container is a top-level item of its device (a rack or rail), an item that TIA
  Portal placed directly under the device instead is accepted when its position, name (if given) and
  type identifier (the requested one for plug, the source's for move/copy; compared ignoring case)
  match: `placedUnder` is then the device (empty `itemPath`), `result.itemPath` is the item's own
  device-level path and a message says so. Live 2026-11-02 (TIA Portal V21, K05): plugging into the
  S7-1500 rail `Profilschiene_0` (`OrderNumber:6ES7 590-1***0-0AA0`, slot 4) and into the ET 200SP rack
  `Baugruppenträger_0` (`System:Rack.ET200SP`, slot 1, F-DI `6ES7 136-6BA01-0CA0/V2.0`) placed the module
  at `PLC_1/DI_32_Test` and `ET200SP_1/F-DI_1`; the previous check reported `postcondition_failed` for a
  plugged module. When `PlugNew`'s return value cannot be read, the item is searched: exactly one item
  that did not exist before, under the container or (rack level) under the device, with the requested
  type identifier, name and position is accepted. A null return with no such item, an unreadable
  position or parent chain, another position or another parent fails. A moved item must also no
  longer resolve at its original exact path. The device-level acceptance is not live-tested yet.
- delete re-resolves the exact path and must not find the item.
- change type re-resolves the item (from the return value, else by position) and must report a
  readable type identifier that is not the old one.
- attribute writes read every attribute back (`applied`, `ignored` under policy ignore,
  `notApplied` fails). When the AttributeDelegate cannot give TIA Portal the caller's answer
  (`CurrentSelection` missing, read-only or rejecting the value), it records the problem instead of
  throwing into TIA Portal; SetAttributes then continues with Portal's own default for that
  attribute, and the item fails `unsupported_capability` after SetAttributes returns, with the
  read-back of every requested attribute in the warnings.
- address writes read the same address collection as `list_io_addresses` before and after and report
  `changedIndexes` (coupled ranges included). The written address is identified by its owning
  sub-item (`subItemPath`), its `ioType`, the requested (or unchanged) start address and its original
  length, never by its index in `Addresses`; no candidate or several candidates fail.

Siemens failures keep the shared classification (`engineering_rejected` with `engineeringError`
during a mutation method) and carry the read-back of the attributes or addresses in the item
warnings; earlier attributes or address members may remain applied (no rollback). Pipeline verification is `trusted`
or `superseded`; superseding keys are hierarchical per device item, so a later delete of a
container supersedes earlier items on its sub-items.

`export_io_map` writes through the shared file writer of the PLC file exports (`PlcFileTargets.Write`): a
temporary file in the target directory moved into place; an existing file is swapped in with `File.Replace`
keeping the previous file as a `.bak` sibling until the re-read hash matches (where ReplaceFile fails, the
previous file is renamed aside and the new one moved in, never copied; a failed swap keeps and names both
copies), see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md). The written file is
re-read and the result reports `sha256`, byte and row counts, `rawCells`, `neutralizedCells` and
`discoveryComplete`; a re-read that fails or differs is `postcondition_failed` (the file is in place, the
kept backup is named). Diagnostics longer than the 512-character cap are replaced by a summary and
reported in the root `omission` (`diagnosticDetailsOmitted`); since 2026-11-02 that alone no longer
turns the call into `success:false` (live 2026-11-02, K07: the CSV of 4,850 bytes was written and its
`sha256` returned, but 2,362 characters of diagnostics made the call report failure). CSV: UTF-8 with
BOM, CRLF, RFC 4180 quoting; columns `RowType, Device, ItemPath, Item, TypeIdentifier,
PositionNumber, AddressIoType, StartAddress, Length, AddressContext, Controllers, ChannelNumber,
ChannelIoType, ChannelType, LogicalAddress, TagName, TagDataType, TagLogicalAddress, TagTable,
TagFolder`; one `address` row per Address and one `channel` row per channel and matched tag.
Values are those of `read_hardware_config`, with one change: a cell that begins with `=`, `+`, `-`,
`@`, a tab or a carriage return is prefixed with a single quote, so spreadsheet programs show it as
text instead of evaluating it as a formula (CSV/formula injection). The result counts these cells in
`neutralizedCells`; `rawCells:true` writes every value exactly as read.

## Custom identities

> **Not live-tested** (GitHub issue #18). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.CustomIdentity.CustomIdentityProvider.Set(System.String,System.String)` ("The user defined key and
> value to set"), `.Get(System.String)` (returns `System.String`), `.Remove(System.String)` ("Throws
> `CustomIdentityNotFoundException` if CustomIdentity for a given key is not found") and
> `T:Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException`.

User-defined key/value pairs on a device or device item: `get_custom_identity` (`network_read`, every mode, `Observe`) and
`set_custom_identity` / `remove_custom_identity` (`network_write`, read-write and full, `ProjectMutation`; no prompt, no guard).
Worker partial `Openness/Network/HardwareModuleService.CustomIdentity.cs`; request fields `key`/`value`
(`HardwareModuleRequestInfo.CustomIdentityKey`/`CustomIdentityValue`); FakeWorker `hardware-modules` (the DQ module offers no
provider).

- **Provider.** The XML does not say which objects offer `CustomIdentityProvider`; the worker takes it from the target's
  `GetServiceInfos()` by exact full name and retrieves it with `IServiceProvider.GetService(Type)`. Not listed, or listed but
  not returned, is `unsupported_capability`, nothing changed. For a write that already fails in planning, which reads the key
  first through `get_custom_identity`.
- **Calls.** `Get`, `Set` and `Remove` are bound at runtime by exact name and `String` parameters by the Siemens-free
  `CustomIdentityRun` (Contracts `Network/HardwareModules/CustomIdentity.cs`, shared with the FakeWorker); a missing member is
  `api_member_missing`. `CustomIdentityNotFoundException` is recognised by its exact full name (also on a base type). How `Get`
  answers a missing key is not documented: the result reports `notFound` for that exception and `null` for a null return.
- **Writes.** The planning effect shows `currentSettings.state`/`value` and `requestedSettings.key`/`value` (or `removed`).
  `set_custom_identity` reads the key back with `Get` and fails `postcondition_failed` unless it reads the requested value;
  `remove_custom_identity` fails `target_not_found` when TIA Portal throws `CustomIdentityNotFoundException` (the key does
  not exist; reported, not a crash) and `postcondition_failed` when `Get` still finds the key. The result carries `before`
  and `after`; the worker's read-back is the verification (K78: check `customIdentity`, expected `key=value` or `key absent`,
  actual from `after`), superseding per device/item and key: a set followed by a remove of the same key is superseded by the
  verified remove, whose own entry checks that the key is absent.
- **Live acceptance still required:** which devices and device items list the provider, what `Get` does for a missing key,
  whether a value survives save/close/open and whether empty values are accepted.

## Port topology

> **Not live-tested** (GitHub issue #10). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`,
> `T:Siemens.Engineering.HW.Features.NetworkPort` ("Represents a port on a device item"), `M:...NetworkPort.ConnectToPort(NetworkPort)`,
> `M:...NetworkPort.DisconnectFromPort(NetworkPort)`, `P:...NetworkPort.ConnectedPorts` (summary "Internal use only") and
> `P:...NetworkPort.Interface`; the Siemens skill `devices-and-hardware` (section "Port-to-Port Topology Connections") retrieves the
> service with `deviceItem.GetService<NetworkPort>()` and guards `ConnectToPort` with `ConnectedPorts.Contains`.

PROFINET cabling between ports: `list_port_connections` (`network_read`, every mode, `Observe`) and `connect_ports` /
`disconnect_ports` (`network_write`, read-write and full, `ProjectMutation`; no prompt). Worker partial
`Openness/Network/HardwareModuleService.Ports.cs`; Siemens-free calls `PortTopologyRun` (Contracts
`Network/HardwareModules/PortTopology.cs`, shared with the FakeWorker); new request field `partner`
(`HardwareModuleRequestInfo.Partner`); FakeWorker `hardware-modules` (CPU interface `PROFINET interface_1` with `Port_1` and
`Port_2`).

- **Service.** `NetworkPort` is not in the compile stubs. A port device item is a device item that lists
  `Siemens.Engineering.HW.Features.NetworkPort` among its `GetServiceInfos()`; the service is retrieved with
  `IServiceProvider.GetService(Type)`. `ConnectToPort`, `DisconnectFromPort`, `ConnectedPorts` and `Interface` are bound at runtime
  by exact name (a missing method is `api_member_missing`).
- **Read.** `list_port_connections` takes a `device` or `deviceItem` target and lists every port device item of the device, or of the
  item and its sub-items: `{ target, ports[{ port, interfaceOwner, connectedPortsState, connectedPorts[{ port, runtimeType }],
  messages }], messages }`. `interfaceOwner` is `NetworkPort.Interface.OwnedBy`. The XML documents no `Parent` of the port
  service, so a partner from `ConnectedPorts` is named by the project device item whose port service equals it
  (`object.Equals`); `port` is null when no device item does (for example a port of another project). `connectedPortsState` is
  `unreadable` when `ConnectedPorts` is missing or throws a recoverable error. K77 (live 2026-11-03, CPU 1516-3 PN/DP): the
  call failed `protocol_error` because integrated interfaces and ports read `TypeIdentifier` null and the worker then sent an
  identity without a device name; a null `TypeIdentifier` is now the `typeIdentifier: null` path segment `read_hardware_config`
  also reports (K95: the selector accepts null, `""` or no member, so `port` and `interfaceOwner` can be fed back as targets), and an item whose selector cannot be read is null (`interfaceOwner`, partner
  `port`) or, for the port itself, the device name and item name without a path, each with a message. Not live-tested.
- **Non-port items (K83, not live-tested).** TIA Portal V21 lists `NetworkPort` among the services of every device item (CPU, card
  reader, display, OPC UA, DP and PN interfaces) but returns it only on ports. The read no longer adds one message per such item:
  one top-level message counts them and names at most 10. An explicitly named connect/disconnect item still gets its own message.
- **Writes.** `connect_ports` and `disconnect_ports` take `target` (the port device item) and `partner` (the other port device item,
  kind `deviceItem`). Planning reads `list_port_connections` of the target: a target without the port service fails
  `unsupported_capability` before any change; the effect carries `currentSettings.connected` and `linkedPorts`. An existing link
  (connect) fires the info guard `network_port_link_exists` (the Siemens skill reports that `ConnectToPort` then throws), a missing
  or unreadable link (disconnect) `network_port_link_missing`; the item still runs and TIA Portal decides (a rejection is
  `engineering_rejected`). The worker reads `ConnectedPorts` back: a link that does not read back as requested is
  `postcondition_failed`; an unreadable `ConnectedPorts` leaves `connectedAfter` null with a message (trusted, not verified).
  A port and its partner are one link: a later item on the same pair supersedes an earlier one.
- **Cable attributes.** The manual's "Attributes of port inter-connection" and "Configurable attributes of a port-to-port
  connection" (cable length, signal delay) are attributes of the port device item, not of `NetworkPort`: `inspect_network_object`
  on the port item lists them with access and supported types, `set_hardware_attributes` writes them. No attribute name is
  assumed by the server.
- **Live acceptance still required:** whether `ConnectedPorts` lists a link set in the UI and one set by `ConnectToPort`, what
  TIA Portal throws for an existing link and whether a partner in another station resolves to its device item.

## IO systems, DP master systems and transfer areas

> **Not live-tested** (GitHub issue #11). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`:
> `M:Siemens.Engineering.HW.IoSystem.Delete`, `M:...IoConnector.DisconnectFromIoSystem`, `P:...IoConnector.ConnectedToIoSystem`,
> `P:...Features.NetworkInterface.TransferAreas` / `.MulticastableTransferAreas`, `M:...TransferAreaComposition.Create(String,
> TransferAreaType)` / `Create(String, TransferAreaType, Int32)`, `M:...MulticastableTransferAreaComposition.Create(NetworkInterface,
> TransferAreaType)` / `(NetworkInterface, TransferAreaType, String)` / `(NetworkInterface, TransferAreaType, String, Int32)` /
> `(MulticastableTransferArea, TransferAreaType)`, the properties of `TransferArea` (`Name`, `Type`, `Direction`, `PositionNumber`,
> `ExtendedPositionNumber`, `LocalToPartnerLength`, `PartnerToLocalLength`, `LocalAddresses`, `PartnerAddresses`,
> `TransferAreaMappingRules`) and `MulticastableTransferArea` (`Name`, `Type`, `Direction`, `DataLength`, `Comment`, `Addresses`,
> `PartnerTransferAreas`), `Delete()` of both, and the 30 `TransferAreaType` members. The Siemens skill `devices-and-hardware`
> ("Create Multicast Transfer Area") shows the two multicastable `Create` forms.

Worker partial `Openness/Network/HardwareModuleService.NetworkSystems.cs`; Siemens-free transfer area logic `TransferAreaRun`
(Contracts `Network/HardwareModules/NetworkSystems.cs`, shared with the FakeWorker); request fields `transferArea` and `partner`;
FakeWorker `hardware-modules` (CPU interfaces `PROFINET interface_1` (an I-device in IO system 100 of subnet `subnet-pn-1`) and
`PROFINET interface_2`). None of these members is in the compile stubs; each is bound at runtime by exact name and argument
types (a missing one is `api_member_missing`); the interface is the device item's `GetService<NetworkInterface>()`.

| Operation | Tool | Input | Siemens calls |
|---|---|---|---|
| `list_transfer_areas` | `network_read` | `target` (deviceItem providing the `NetworkInterface`) | `NetworkInterface.TransferAreas`, `.MulticastableTransferAreas`, the documented properties, `GetAttributeInfos()` |
| `create_transfer_area` | `network_write` | `target`, `transferArea { collection, type, name?, positionNumber?, dataLength?, templateIndex? }`, `partner` (multicastable) | the `Create` overload the fields select (below) |
| `update_transfer_area` | `network_write` | `target`, `transferArea { collection, index, name }`, `attributes` | `SetAttribute` per attribute, read back with `GetAttribute` |
| `delete_transfer_area` | `network_write` | `target`, `transferArea { collection, index, name }` | `TransferArea.Delete()` / `MulticastableTransferArea.Delete()` |
| `delete_io_system` | `network_write` | `target { kind: "ioSystem", subnetId, number, ioSystemIndex?, ioSystemName? }` | `IoSystem.Delete()` |
| `disconnect_from_io_system` | `network_write` | `target` (deviceItem providing the IO device's `NetworkInterface`) | `IoConnector.DisconnectFromIoSystem()` |

- **Reading transfer areas.** `{ target, transferAreas[], multicastableTransferAreas[], messages }`; each area is `{ collection,
  index, name, properties[{ name, value { state, value, runtimeType, error } }], attributes[{ name, accessMode }], runtimeType }`.
  The XML gives no property types, so each documented property is projected as text; collection-valued ones are reported as
  `<Name>Count`. `attributes` is what `GetAttributeInfos()` lists (what `update_transfer_area` may set). A composition the
  installation does not offer is null with a message.
- **Creating.** Collection `transferAreas` (I-slave / I-device address mapping): `name` and `type` required, optional
  `positionNumber` → `TransferAreaComposition.Create(name, type[, positionNumber])`. Collection `multicastable`: `type` and
  `partner` (the device item providing the partner `NetworkInterface`) required → `Create(partnerInterface, type)`, with `name`
  → `Create(partnerInterface, type, name)`, with `name` and `dataLength` → `Create(partnerInterface, type, name, length)`; or
  `templateIndex` (an area of the partner's `MulticastableTransferAreas`) → `Create(area, type)`. `type` must be one of the
  documented `TransferAreaType` members; the installed enum decides at runtime. The created area must be listed by the
  re-read composition (`postcondition_failed` otherwise); a name TIA Portal changed is a message.
- **Updating and deleting.** The area is selected by `index` with its current `name` as evidence (planning and worker:
  `target_evidence_mismatch` when the name differs). `update_transfer_area` checks each attribute against
  `GetAttributeInfos()` (access mode, `SupportedTypes` conversion as for `set_hardware_attributes`), writes them one by one and
  reads each back (`postcondition_failed` unless every attribute reads back the written value). `delete_transfer_area` requires the
  composition to list one area fewer afterwards and fires the info guard `network_deletes_transfer_area`. Indexes shift after a
  delete: a later item on the same composition supersedes.
- **IO systems.** `delete_io_system` takes the IO system selector `read_hardware_config` reports (subnets[].ioSystems[].selector).
  Planning resolves it in the snapshot and fires the info guard `network_deletes_io_system` with the IO devices that are
  disconnected; the worker resolves it exactly, reads name, number and connected IO devices, calls `IoSystem.Delete()` and
  re-resolves by subnet, number and name (`postcondition_failed` when still listed). It deletes PROFINET IO systems and DP
  master systems alike, as far as TIA Portal lists them in the subnet's `IoSystems`. `disconnect_from_io_system` takes the IO
  device's interface item, chooses the one IO connector that is connected (several connected, or several and none connected:
  `target_ambiguous`; a single unconnected connector is still passed to TIA Portal), calls `DisconnectFromIoSystem()` and reads
  `ConnectedToIoSystem` back (must be null); info guard `network_disconnects_io_connector`.
- **DP master systems.** V21 documents no separate member that creates a DP master system (the names found are enum values
  such as `DpOperatingMode.DpMaster`). `create_io_system` calls the generic `IoController.CreateIoSystem(System.String)` on the
  first IO controller of the selected node's interface; on a PROFIBUS DP master node TIA Portal decides whether that yields a DP
  master system. A DP slave's IO connector joins it with `configure_network_device` `changes.ioSystem` (`IoConnector.ConnectToIoSystem`),
  like a PROFINET IO device. Not live-tested on a DP interface (the test project's PLC_F1 has X3 DP).
- **Left out.** "Modifying CCDX transfer areas" beyond what `GetAttributeInfos()` offers as writable: no other modifying member is
  documented. `TransferAreaMappingRule` editing (`Begin`, `End`) and the transfer areas' address objects: read as counts only;
  their write semantics are not documented. Renaming or renumbering IO systems: `IoSystem.Name`/`Number` are documented without
  setters.
- **Live acceptance still required:** which interfaces offer which composition, the `TransferAreaType` members each accepts, what
  `GetAttributeInfos()` lists for an area, and whether `IoSystem.Delete()` removes a DP master system.

## CAx data (AutomationML)

> **Not live-tested** (GitHub issue #9). Evidence: the V21 IntelliSense file `Siemens.Engineering.Step7.xml`,
> `T:Siemens.Engineering.Cax.CaxProvider` ("Service Provider for CAx Export/Import operations") with `Export(Device, FileInfo)`,
> `Export(ProjectBase, FileInfo)` (→ `TransferResult`), `Export(Device, FileInfo, FileInfo)`, `Export(ProjectBase, FileInfo, FileInfo)`,
> `Export(Project, FileInfo, FileInfo)` (third parameter the log file, → `Boolean`), `Import(FileInfo, CaxImportOptions)`
> (→ `TransferResult`), `Import(FileInfo, FileInfo, CaxImportOptions)` (→ `Boolean`); `CaxImportOptions` (`MoveToParkingLot`,
> `OverwriteTiaDevice`, `RetainTiaDevice`); `TransferResult` (`State`, `ErrorCount`, `WarningCount`, `Messages`),
> `TransferResultMessage` (`Message`, `State`, `DateTime`, `ErrorCount`, `WarningCount`, `Messages`), `TransferResultState`
> (`Success`, `Information`, `Warning`, `Error`). TIA Portal help V20 "CAx-Daten exportieren und importieren": format AutomationML,
> extension `.aml`.

`export_cax` and `import_cax` (`network_write`, read-write and full, `ProjectMutation`; no prompt). Worker partial
`Openness/Network/HardwareModuleService.Cax.cs`; Siemens-free projection `CaxRun` (Contracts `Network/HardwareModules/Cax.cs`);
request fields `filePath`, `deviceName`, `overwrite` (existing; since F4 also for the import's log) and `logFilePath`, `importOption` (new); FakeWorker
`hardware-modules` (an import of a file containing `Fail` is rejected).

| Operation | Required | Optional | Siemens calls |
|---|---|---|---|
| `export_cax` | `filePath` (absolute, `.aml`) | `deviceName` (one device; omitted: the project), `logFilePath`, `overwrite` | `CaxProvider.Export(Device \| ProjectBase, FileInfo[, FileInfo])` |
| `import_cax` | `filePath` (absolute, `.aml`, must exist), `importOption` | `logFilePath`, `overwrite` (for the log) | `CaxProvider.Import(FileInfo[, FileInfo], CaxImportOptions)` |

- **Provider.** The XML does not say which object offers `CaxProvider`; the worker takes it from the project's `GetServiceInfos()`
  by exact full name (not listed or not returned: `unsupported_capability`). It works on the bound container (`ProjectBase`), so a
  local Multiuser session is passed as such (the documented `Export(ProjectBase, ...)` "Single/Multi user Project").
- **Overloads.** Without `logFilePath` the overload returning a `TransferResult` is called; its `State`, `ErrorCount`, `WarningCount`
  and the message tree (`message`, `state`, `dateTime`, nested `messages`; at most 200 messages and 8 levels, `messagesOmitted`
  counts the rest) are returned as text (the XML gives no property types). With `logFilePath` the overload with a log file is
  called; it returns only true/false (`succeeded`) and the log is re-read (`log { path, exists, length, sha256 }`). The overload
  is bound by exact name and the documented parameter types (`Device` or `ProjectBase`, `FileInfo`, the runtime
  `CaxImportOptions` enum); a missing one is `api_member_missing`.
- **Failure.** A `TransferResult` with state `Error`, or `false` from a log overload, is TIA Portal's rejection:
  `engineering_rejected` with up to 50 messages as warnings; an import adds which devices may already have been added or removed.
- **Export verification.** An existing file is refused unless `overwrite:true` (then the info guard
  `network_cax_export_overwrites_file` fires). Since the security review 2026-11-03 (F4; not live-tested) TIA Portal writes the
  export into a staging folder next to the target; the file is moved into place with `PlcFileTargets.Commit` (an existing file
  replaced with a backup that is deleted after a matching re-hash) and re-read and hashed (`file { path, exists, length, sha256 }`);
  no file written is `postcondition_failed` and `filePath` stays unchanged. That re-hash is the verification (K78: check `file`,
  actual with length and `sha256`). Further files TIA Portal writes next to the export are kept in the staging folder and named
  in a warning.
- **Log file (F4).** An existing `logFilePath` is refused for `export_cax` and `import_cax` unless `overwrite:true` (planner and
  worker); its directory must exist, device paths and reserved names are refused by the catalog, and the worker reports a link,
  junction or network location on the way (`PlcFileTargets.InspectDirectory`), as for the file itself.
- **Import.** Planning requires the file to exist (`target_not_found`) and always fires the info guard
  `network_cax_import_changes_hardware` (hardware changes, no rollback; use a test project). The result lists the project's device
  names before and after with `addedDevices` and `removedDevices`; the imported hardware itself is not read back (verification is
  `trusted` with that evidence). An import can touch any device, so it supersedes every earlier hardware item of the call.
- **Left out.** `Export(Project, FileInfo, FileInfo)` is not called separately: it is the standalone form of the documented
  `Export(ProjectBase, FileInfo, FileInfo)`, which covers both. Choosing what an AML file contains (EPLAN mapping, parking-lot
  folder contents) has no Openness member.
- **Live acceptance still required:** whether the project lists `CaxProvider`, an export of the project and of one device, an import
  with each `CaxImportOptions` member, and what a failed import reports.

## plc_read operations

| Operation | Input | Result |
|---|---|---|
| `read_address_occupancy` | optional `plcName`, `areas` (subset of I, Q, M), `includeHardware` (default true) | `{ plcName, deviceName, isComplete, hardwareIncluded, areas[{ area, entries[], usedBytes }], overlaps[], tagsOutsideHardware[], unparsedTags[], unsizedTags[], widthMismatches[], messages }` |
| `find_free_address` | `area` (I, Q, M), `dataType` | optional `plcName`, `startByte` (0), `endByte` (65535; both 0–65535), `alignBytes` (1, 2, 4, 8), `requireHardware` (I/Q only) → `{ found, address, startByte, startBit, bitLength, ... isComplete, unsizedTags[], messages }` |

Both are computed in the host from `list_tag_tables` and, when hardware is needed,
`read_hardware_config` with `includeIoDetails`. Exactly one PLC must match (`target_not_found` /
`target_ambiguous`). Tag addresses are parsed from `%<area>[B|W|D]<byte>` or `%<area><byte>.<bit>`
(international I/Q/M; the German E/A letters map to I/Q); symbolic, DB, `:P` and other spellings
are listed in `unparsedTags`. Hardware addresses belong to the PLC when the address's controller
association names the PLC's device.

A tag's width is its **data type's documented length**, cross-checked against the address
spelling: the larger of the two is used, and a difference is listed in `widthMismatches` (an LReal at
`%MB100` occupies 64 bits, not 8). The lengths come from the TIA Portal V21 information system
("Data types"): Bool 1; Byte, Char, SInt, USInt 8; Word, WChar, Int, UInt 16; DWord, DInt, UDInt, Real,
TOD/Time_Of_Day 32; LWord, LInt, ULInt, LReal, LTOD/LTime_Of_Day, DT/Date_And_Time, LDT/Date_And_LTime
64; DTL 96 (12 bytes). Any other type (PLC data types, arrays, strings, structures, and TIME, LTIME,
S5TIME and DATE, whose length could not be confirmed from those pages) is **unsized**: the tag is
listed in `unsizedTags` with its start, it occupies at least its spelled width, entries,
`usedBytes` and overlaps omit it, and `isComplete` is false in both operations because an address
after its start may lie inside it.

`find_free_address` places the types with a documented 1/8/16/32-bit length and a spelling: Bool,
Byte, Char, SInt, USInt, Word, WChar, Int, UInt, DWord, DInt, UDInt, Real, TOD/Time_Of_Day. "Free"
means no declared tag overlaps the interval; with `requireHardware` the interval must lie inside one
hardware address range of the PLC. `startByte` and `endByte` are 0–65535. The search sweeps the
sorted, merged occupied intervals (each step jumps past one interval, so the cost depends on the
number of tags, not on the byte range) and stops when the call is cancelled. An incomplete inventory
or an unsized tag in the area sets `isComplete:false` with a message.

## plc_write `move_tag`

`move_tag { tableName, name, targetTableName, plcName?, folderPath?, targetFolderPath? }`. Openness
V21 documents no member that moves a `PlcTag` between tables and tag names are unique per PLC, so
this is a **guarded re-create**, stated by the info guard `plc_move_tag_recreates`:

1. create a temporary tag (`__mcp_move_<12 hex>`) in the target table with the source's
   `DataTypeName` and `LogicalAddress`, copy the readable `ExternalAccessible`, `ExternalVisible`,
   `ExternalWritable`, and copy the multilingual **comment**: for every non-empty
   `MultilingualTextItem { Language, Text }` of the source's `PlcTag.Comment.Items`, the new tag's
   item of the same language (`Comment.Items.Find(Language)`) receives the text, which is read back
   at once;
2. delete the source tag;
3. rename the temporary tag to the original name and read back name, type, address, flags and
   comment in the target table plus the absence in the source table.

The three steps run inside one ExclusiveAccess transaction (see the
[PLC operations reference](PLC_OPERATIONS_SUMMARY.md#db-values-instance-dbs-rename-renumber-copy-move)):
a failure at any step, including a comment that does not read back, rolls all of them back, so the
source stays unchanged and no temporary tag is left; the failure message names the failed step and
the tag tables' state read back after the rollback (not live-tested). A tag with `IsSafety=true` moves under the
safety login rule (refused only when the safety program has a password and the session is not logged on). A language the new tag has no text item for, an unreadable comment and any attribute
outside data type, address, the three flags and the comment are not copied; the result lists them in
`notCopied`, and `steps` names the languages copied. Run `compile_check` afterwards so blocks
re-bind the symbol.

## Provenance

| Member | Source | Status |
|---|---|---|
| `HardwareObject.CanPlugNew(String, String, Int32)` → `bool`; `PlugNew(String, String, Int32)` → `DeviceItem` | device-item-operations.md §4 (sample types); hardware-parameters.md API reference of `HardwareObject` | stubbed, typed call, not live-tested |
| `HardwareObject.CanPlugMove(DeviceItem, Int32)`, `PlugMove(DeviceItem, Int32)` | device-item-operations.md §5; hardware-parameters.md | stubbed, typed call, not live-tested |
| `HardwareObject.CanPlugCopy(DeviceItem, Int32)`, `PlugCopy(DeviceItem, Int32)` | device-item-operations.md §6; hardware-parameters.md | stubbed, typed call, not live-tested |
| `HardwareObject.GetPlugLocations()` → `IList<PlugLocation>` | device-item-operations.md §9 (sample type); hardware-parameters.md | stubbed, typed call, not live-tested |
| `Siemens.Engineering.HW.Extensions.PlugLocation` (type), `.PositionNumber`, `.Label` | device-item-operations.md §9 and API reference; base.json type list | type stubbed; properties read by reflection (types undocumented) |
| `DeviceItem.Delete()` | device-item-operations.md §7; device-creation.md API reference | stubbed (`void`), not live-tested |
| `DeviceItem.ChangeType(String)` | device-item-operations.md §8; device-creation.md API reference | **invoked by reflection by exact signature** (return type undocumented); not stubbed |
| `ModuleInformationProvider.FindContainerTypes(String)` → `string[]`, `FindModuleTypes(String)` → `string[]` | device-item-operations.md §9; base.json type list | stubbed, typed call |
| `Project.HwUtilities` (`HardwareUtilityComposition`) | device-item-operations.md §9 | **read by reflection** (declaring type undocumented) and enumerated |
| `IEngineeringObject.SetAttributes(IEnumerable<KeyValuePair<string, object>>, AttributeDelegate)`; `AttributeDelegate(AttributeConfiguration)`; `AttributeConfiguration.Name`, `.Message`, `.CurrentSelection`; `AttributeChoiceSelection.Abort`/`.Ignore` | device-item-operations.md §10; device-attributes.md §4; base.json type list | **bound by reflection** (exact parameter types, delegate built from the runtime type, members set by name); not stubbed |
| `IEngineeringObject.GetAttributeInfos()`, `GetAttribute`, `SetAttribute`; `EngineeringAttributeInfo.Name`, `.AccessMode`, `.SupportedTypes` | existing stubs; device-attributes.md §5 | existing |
| `Address.StartAddress` (Int32, read/write) | addresses-and-channels.md §2 | setter added to the stub |
| `Address` dynamic attributes `Context`, `ProcessImage`, `IsochronousMode`, `InterruptObNumber` | addresses-and-channels.md §2–3 | existing `GetAttribute`/`SetAttribute` |
| `PlcTagComposition.Create(String, String, String)`, `Find`, `PlcTag.Delete()`, `.Name`, `.DataTypeName`, `.LogicalAddress`, `.ExternalAccessible/Visible/Writable` | tags-types.md §4; Siemens tags-and-tagtables SKILL.md | existing stubs |
| `PlcTag` attribute `IsSafety` | tags-types.md §4 (attribute table) | read with `GetAttribute` (refusal only) |
| `CustomIdentityProvider.Get(String)` → `String`, `Set(String, String)`, `Remove(String)`; `CustomIdentityNotFoundException` | `Siemens.Engineering.Base.xml` (V21 IntelliSense) | **bound by reflection** by exact name and parameters; the provider found among `GetServiceInfos()`; not stubbed, not live-tested |
| `NetworkPort` (service), `.ConnectToPort(NetworkPort)`, `.DisconnectFromPort(NetworkPort)`, `.ConnectedPorts` ("Internal use only"), `.Interface`; `NetworkInterface.OwnedBy` | `Siemens.Engineering.Base.xml` (V21 IntelliSense); Siemens skill devices-and-hardware | service found among `GetServiceInfos()`, members **bound by reflection**; `OwnedBy` existing stub; not live-tested |
| `IoSystem.Delete()`, `IoConnector.DisconnectFromIoSystem()`, `NetworkInterface.TransferAreas`/`.MulticastableTransferAreas`, the `Create` overloads, properties and `Delete()` of `TransferArea`/`MulticastableTransferArea`, `TransferAreaType` | `Siemens.Engineering.Base.xml` (V21 IntelliSense); Siemens skill devices-and-hardware | **bound by reflection** by exact name and argument types; `GetService<NetworkInterface>`, `IoConnectors`, `ConnectedToIoSystem` existing stubs; not live-tested |
| `CaxProvider.Export`/`Import` (all documented overloads), `CaxImportOptions`, `TransferResult`, `TransferResultMessage`, `TransferResultState` | `Siemens.Engineering.Step7.xml` (V21 IntelliSense); TIA Portal help V20 "CAx-Daten exportieren und importieren" | provider found among the project's `GetServiceInfos()`, **bound by reflection**; not stubbed, not live-tested |
| `PlcTag.Comment` (`MultilingualText`) | tags-types.md (`PlcTag`: "The multilingual comment of the PlcTag"); language-settings.md §5 (MultilingualText model) | stubbed (getter), typed call, not live-tested |
| `MultilingualText.Items`, `MultilingualTextItem.Language` / `.Text` (get/set), `MultilingualTextItemComposition.Find(Language)` | language-settings.md §5 (reading and writing a multilingual text) | `Items`, `Language`, `Text` existing stubs; `Find(Language)` stubbed |
| Data type lengths (Bool … DTL, see `plc_read`) | TIA Portal V21 information system, "Data types" pages (docs.tia.siemens.cloud, the BOOL … DTL pages) | not an API member; values in `PlcAddressSpace`; TIME, LTIME, S5TIME, DATE not confirmed and left unsized |

The compile-time members are exercised by
`reference-stubs/TiaMcpServer.OpennessReferenceProbe/HardwareModulesReferenceSurface.cs`; building
the probe against the installed V21 PublicAPI checks the declared return and parameter types.

## Omitted, and why

- `DriveItemHardwareModule.ChangeType` (Startdrive), motor/encoder projection, `CreateWithItem`
  variants and master copies: SINAMICS-specific or outside this scope.
- `ModuleInformationProvider.GetTypeIdentifierNormalized`: return type undocumented and not needed.
- `HardwareCatalog.Find(String, String)` (compatible/pluggable filter) and `HwIdentifiers`: not
  needed for the requested scope; `search_equipment_catalog` is unchanged.
- `Address.AssignProcessImageToOrganizationBlock(OB)`: needs block resolution across the tools; not
  implemented.
- "Change device" of a whole station: no documented `Device.ChangeType`; change the head/CPU item
  with `change_device_item_type`.
- PSC export, front-panel logo, GSD `GetPrmData`/`SetPrmData`: not in the requested scope. The extension-rack
  `ImConnection`, telecontrol data point files, subnets of a node, the IO controller of a connector, the PC interface
  assignment, module description updates and communication connections were added by package G (see the
  [V21 API completion reference](V21_API_COMPLETION_SUMMARY.md#hardware-features-network_read--network_write)).
- Moving a tag natively: no documented member (see `move_tag`); `move_tag` re-creates it.
- Placing 64-bit and wider types with `find_free_address` (LReal, LWord, LInt, ULInt, LTOD, DT,
  LDT, DTL): no I/Q/M spelling for those widths is documented in the sources. Existing tags of those
  types are sized by their documented length in both operations.
- Lengths of TIME, LTIME, S5TIME and DATE: the information-system pages fetched for this change were
  cut off before their length tables, so these types stay unsized rather than guessed.
- Device-group operations and moving devices between groups: unchanged gap.

## Live acceptance list

On a disposable standalone project with one S7-1500 station, in `full` mode:

1. `list_plug_locations` on the rack; `plug_device_item` a DI module at a free position (dry run,
   then actual); confirm with `read_hardware_config`. Repeat on an S7-1500 rail and an ET 200SP rack
   (K05): the item must succeed with `placedUnder` the device and `result.itemPath` the device-level
   path. On the ET 200SP device itself, a refused `CanPlugNew` must name the rack item (K29).
2. `plug_device_item` at an occupied position → `validation_error` (CanPlugNew false).
3. `move_device_item` to another free slot; `copy_device_item`; `delete_device_item` the copy.
4. `change_device_item_type` to another firmware version of the same module; check the reported
   type identifier.
5. `inspect_network_object` on the DI module, then `set_hardware_attributes` with one documented
   parameter in `bulk` mode, again in `sequential` mode, and once with an invalid value under
   `abort` and `ignore`; record the AttributeDelegate behavior.
6. `list_io_addresses`; `set_io_address` to a new input start address; check the coupled output
   range and that tags were not rewired. Repeat with the S7-1500 DI 32 module
   (`6ES7 521-1BL00-0AB0/V2.2`, K06): its addresses must be listed with `subItemPath` `DI_32` and be
   writable through the module's selector.
7. `find_module_types` / `find_container_types` for the module's order number.
8. `export_io_map` to a new file, again without `overwrite` (refused), then with `overwrite:true`;
   give a device a comment-like name starting with `=` and open the CSV in a spreadsheet; try a UNC
   share path (accepted) and a `\\?\` path (refused).
9. `read_address_occupancy` and `find_free_address` (M Int, I Bool with `requireHardware`), with an
   LReal tag at `%MB100` and a PLC-data-type tag in the area; check how TIA Portal spells the LReal
   address and record it.
10. `move_tag` between two tables with a comment in two project languages; verify name, type,
   address, flags and both comment texts; `compile_check`.
11. `set_hardware_attributes` in `bulk` mode with a value TIA Portal rejects; record the
   `AttributeConfiguration` members and whether `CurrentSelection` takes `Abort`/`Ignore`.
12. Repeat 1 and 10 on a Multiuser local session.
13. `list_port_connections` on two PROFINET stations; `connect_ports` port 1 of the CPU with port 1 of the ET 200SP (dry run, then
   actual), compare with the topology view, `connect_ports` again (record the rejection), `disconnect_ports`; set the cable length
   of a port with `inspect_network_object` / `set_hardware_attributes`.
14. `list_transfer_areas` on an I-device interface and on a DDX-capable interface; `create_transfer_area` (transferAreas `CD`,
   multicastable `DDX` with a partner), `update_transfer_area` one listed attribute, `delete_transfer_area`; `disconnect_from_io_system`
   an ET 200SP, `delete_io_system`; `create_io_system` on the X3 DP node of PLC_F1 and `delete_io_system` of the result.
15. `export_cax` of the project and of one device (with and without `logFilePath`), open the .aml; `import_cax` it into a fresh
   test project with each `importOption`; import a broken file and record the reported messages.
