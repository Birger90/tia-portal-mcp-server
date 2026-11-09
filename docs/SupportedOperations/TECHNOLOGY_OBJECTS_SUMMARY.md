# Technology objects (`plc_read` / `plc_write`)

**Language:** English · [Deutsch](TECHNOLOGY_OBJECTS_SUMMARY.de.md)

> **Not live-tested.** Every operation below is implemented from the V21 sources named under
> [provenance](#provenance-of-every-siemens-member-used) and exercised offline against the scripted FakeWorker
> scenarios `technology-objects` and `technology-objects-silent` (whose Create, CreateGroup, Connect and Import change
> nothing, so the read-back checks must fail) and Linux unit tests. Every technology-object-specific Siemens member is bound at
> runtime and fails closed with `unsupported_capability` when the installed V21 does not expose it exactly as
> documented. Run the [live acceptance checks](#live-acceptance-checks) before relying on these operations.

Technology objects (TOs) live in `PlcSoftware.TechnologicalObjectGroup`, always at the PLC root: the Siemens
technology-objects skill states that software units have none. A TO is a `TechnologicalInstanceDB`; its parameters
are `TechnologicalParameter`s; its hardware connection goes through the documented connection providers.

## Why plc_read and plc_write

TOs are PLC software content, addressed like tag tables and watch tables: a PLC selector (`plcName`), an object name
and a group path (`folderPath`). The existing PLC tools already carry every mechanism the domain needs (the guarded
single-call pipeline with dry run, info guards, read-back verification and audit; the PLC working state that lets a
later item use an object an earlier item created; the content-hash round trip of the watch/force tables), so the
operations were added there instead of a new tool pair. The tool surface stays **13 tools in read-only, 33 in
read-write and 33 in full** (since `project_server_write`; 13/32/32 since `siemens_docs`; 12/31/31 since `tia_inbox`; 11/30/30 since `retrieve_project`) (`library_read`/`library_write` became a pair because a library selector is not a PLC).
The field set of `PlcOperationRequest` grows; every field is restricted per operation by the catalog
(`PlcOperationCatalog.TechnologyObjects.cs`), so an unused field is a `validation_error`.

## Access

| Operations | Capability | read-only | read-write | full |
| --- | --- | --- | --- | --- |
| `list_technology_objects`, `get_technology_object_parameters`, `get_technology_object_hardware_connection` | `Observe` | yes | yes | yes |
| `get_technology_object_content` (temporary SimaticML export) | `TemporaryExport` | yes | yes | yes |
| the twelve `plc_write` operations, `export_technology_object` included (it writes a caller file outside TIA Portal, like `export_io_map`) | `ProjectMutation` | no | yes, no prompt | yes |

The operations act on the bound project (standalone or local session, like every `plc_read`/`plc_write` operation);
the worker methods use the public operation names (`TechnologyObjectMethods` in
`TiaMcpServer.Contracts/Plc/TechnologyObjects`), parameters travel in `WorkerRequest.TechnologyObject`, and
`ProjectCapabilityCatalog.OperationFor` maps them to `plc_read`/`plc_write`. Nothing is hard-blocked except invalid
or ambiguous input: the technology object guards are informative, and the shared `plc_state_unverifiable` block guard
applies only when planning cannot see the state (an incomplete inventory or a failed planning read, see
[guards](#guards-and-verification)).

## plc_read operations

| Operation | Fields | Result |
| --- | --- | --- |
| `list_technology_objects` | optional `plcName` (omitted: every PLC), optional `folderPath` (a group of one PLC: only its subtree) | `{ plcs[{ plcName, deviceName, groups[{ path, name }], technologyObjects[{ name, groupPath, number, systemLibElement, systemLibVersion, runtimeType, unreadable[] }], complete }] }`; `systemLibElement`/`systemLibVersion` are `OfSystemLibElement`/`OfSystemLibVersion` as text, `number` the DB number; at most 5,000 objects plus groups per PLC (`complete:false` beyond) |
| `get_technology_object_parameters` | `name`; optional `plcName`; `prefix` (ordinal), `offset` (default 0), `limit` (1-2000, default 200) **or** `parameterName` (one exact `Find`) | `{ name, groupPath, parameterName, prefix, totalCount, matchedCount, offset, limit, nextOffset, parameters[{ name, valueKind, valueType, value, enumMembers[], enumMembersTruncated, enumFlags }] }` |
| `get_technology_object_hardware_connection` | `name`; optional `plcName` | `{ services[], interfaces[{ interface, provider, index, runtimeType, isConnected, properties[{ name, kind, value, runtimeType, modulePath }], unreadable[] }], unavailableServices[] }`; `unavailableServices` names each provider that `GetServiceInfos()` lists but `GetService` does not return (its interfaces are missing, the read still succeeds; seen live on V21 2026-11-02 for other services, D13/D14); connect and disconnect fail `unsupported_capability` for such a provider, nothing changed |
| `get_technology_object_content` | `name`; optional `plcName` | `{ format:"xml", content, contentHash, warnings }`: the SimaticML export with `DocumentInfo` removed like block exports; the hash feeds `update_technology_object_content` |

### Parameter values

The V21 reference documents `TechnologicalParameter.Value` without a type, and the Siemens skill says the accepted
type depends on the parameter and that an incompatible assignment throws. The only type evidence the API offers is
the runtime type of the current value, so values are typed by it:

| `valueKind` | Runtime types | `value` text | Writable from `value` |
| --- | --- | --- | --- |
| `boolean` | `System.Boolean` | `true`/`false` | yes (`true`/`false`, case-insensitive) |
| `integer` | `SByte` … `UInt64` | invariant integer | yes, range-checked for exactly that type |
| `real` | `Single`, `Double`, `Decimal` | float/double: the shortest text that parses back to the same bits (closest to the exact value among equally short ones), in .NET 10's `R` layout (plain unless the leading power of ten is below -4 or at least max(digits, 17 / 9), then `d.dddE+XX`); `-0` keeps its sign. Decimal: significand and scale (`1.50`) | see [Real values](#real-values) |
| `string` | `System.String` | the text (cut at 4,000 characters with a marker) | yes, any text of at most 4,000 characters (a longer read-back could not be compared uncut) |
| `enum` | any enum | member name; a combined `[Flags]` value as the names of the members that make it up (`A, B`, taken from the largest value down, listed in ascending order like `Enum.ToString`), any other undefined value as its invariant integer, never the runtime's culture-dependent enum text; cut at 4,000 characters with the marker like every text; `enumMembers` lists the first 200 members, `enumMembersTruncated` says that there are more, `enumFlags` marks a `[Flags]` enum | yes, exact member name; for a `[Flags]` enum distinct exact member names separated by commas, combined bitwise (a repeated name, or the zero member inside a combination, is refused as ambiguous). The host refuses a name only against a complete member list; with a truncated list the worker checks it against the live enum. A refusal names the unknown names and lists the first 200 members, each list cut at 4,000 characters with the marker |
| `plcTag` | `PlcTag` | tag name | only with `tableName` + `tagName` |
| `null` | no current value | null | only a `PlcTag` (`tableName` + `tagName`), because the type cannot be determined |
| `object` | anything else | null (type in `valueType`) | no |
| `unreadable` | reading threw a recoverable Openness error | null | no |

### Real values

The worker (.NET Framework) and the host (.NET 10) parse and format reals differently (the Framework parser is not
correctly rounded, parses `Single` through `Double` and drops the sign of `-0`; its `R`/G16 output differs), so every
real conversion and rendering is done by `TechnologyObjectReals` (Contracts) with exact integer arithmetic
(`BigInteger`) and the IEEE 754 bit layout only, never with a runtime parser or formatter. Tests read the compiled IL
(System.Reflection.Metadata, so the net48 worker is read like any file): `TechnologyObjectReals` and
`TechnologyObjectValues` make no call that parses, formats or converts a real, nor any that could format a boxed real
(`String.Format`, `Concat`/`Join` over objects, `StringBuilder` object appends, interpolation handlers,
`IFormattable.ToString`, `Object.ToString`, `System.Convert`); and no technology object code of Contracts, the worker,
the FakeWorker or the host (methods chosen by their source file in the PDB, lambdas, local functions, iterators and
async state machines included; every technology object source file must contribute IL, and a worker or host build older
than its sources is refused) calls a runtime real parser or formatter or anything else that takes or returns a real,
except `BitConverter` and the core above; outside the FakeWorker's scripted values none boxes a real. Self-tests on
sample methods show each rule firing (in compiler-generated bodies too, and through the source-file selection) and clean
code passing. The results
are checked against .NET 10's IEEE-correct parser and `R` output on random and extreme values and on every power of two;
the shortest text equals .NET 10's `R` except for the two doubles whose `R` text does not round-trip, 2^-25 and 2^-958
(`R` gives the shortest text of the double below), which are pinned to the shortest texts of an independent
implementation (`2.9802322387695312E-08`, `4.1045368012983762E-289`), as is the float 2^-25 (`2.9802322E-08`). So the
.NET Framework worker, running the same integer-only code, produces the same bits and texts. The range of an exponent
is decided before any power of ten is formed (with comparisons that cannot wrap around, for any 64-bit exponent), so
`1e-999999999999` or `1e999999999999` is refused in bounded time and never throws. A `value` longer than 4,000
characters is refused naming the bound (`'value' has 4001 characters; at most 4000 are accepted`) by the host catalog
before any worker call, and by the worker with the same text. The worker is the one authority for range and exactness
against the live type; the host planner only refuses text that is no invariant finite number. A refused value reads
`Parameter '<parameter>' of '<object>': <reason>. Nothing was changed.` alike on the host, in the worker and in the
FakeWorker (`TechnologyObjectValues.ParameterRefusal`).

| Target | Accepted | Refused (`validation_error`) |
| --- | --- | --- |
| `Single`, `Double` | invariant number text whose correctly rounded value (to nearest, ties to even; subnormals; overflow only at or beyond the largest finite value plus half an ulp) is finite, when the text is (as a number) that value's shortest round-trip text or its exact decimal expansion: for the float 0.1 both `0.1` and `0.100000001490116119384765625`; `3.4028235E+38` (`float.MaxValue`); `-0` stays `-0` | any other text, naming the shortest text of the nearest value (`16777217` → `16777216`, float `1e-50` → `0`, `0.10000000149` → `0.1`): `'16777217' is neither the shortest round-trip text nor the exact decimal value of the nearest System.Single; the nearest representable value is 16777216 (its shortest round-trip text). Write that text instead`; or out of range, naming the largest magnitude |
| `Decimal` | a number the type holds exactly (96-bit significand, scale up to 28) | text it cannot hold exactly, naming the truly nearest representable decimal over every scale in the same wording (`... cannot be held exactly by System.Decimal (...); the nearest representable value is 0. Write that text instead`; `1e-40` → `0`, 29 decimals, `7922816251426433759354395033.56` → `7922816251426433759354395033.5`), a magnitude of 2^96 or more (out of range, naming the largest magnitude), and `-0` (a decimal zero's sign is not compared, so it is ambiguous) |

A float or double read-back must have the same bits as the written value (`-0` is not `0`).

## plc_write operations

All of them run through the guarded pipeline: `dryRun:true` previews effects and guards without mutation, 1-50
ordered items, stop at the first failure, no rollback, audit v2 record per call (confirmation `none` in read-write,
`policy` in full). Later items may use objects created earlier in the call (`dependsOn`; re-planned against the real
state just before their mutation). After an `import_technology_object` the imported names are not known before it
runs, so every later TO item of the call depends on it.

| Operation | Fields | Siemens call | Worker read-back |
| --- | --- | --- | --- |
| `create_technology_object` | `name`, `technologyType` (e.g. `TO_PositioningAxis`), `version` (e.g. `6.0`); optional `plcName`, `folderPath` | `TechnologicalInstanceDBComposition.Create(String, String, Version)` on the group's `TechnologicalObjects`, after checking that no TO of the PLC has that name (`validation_error` otherwise) | the group lists exactly one object of that name, which can only be the created one |
| `delete_technology_object` | `name` | `Delete()` | the group no longer lists it |
| `rename_technology_object` | `name`, `newName` | generic `SetAttribute("Name")` after `GetAttributeInfos()` lists `Name` writable | `GetAttribute("Name")` and the group listing |
| `create_technology_object_group` | `folderPath` (the new group) | `TechnologicalInstanceDBUserGroupComposition.Create(String)` on the parent's `Groups`, after checking that the parent has no group of that name | the parent lists exactly one group of that name and one group more than before |
| `delete_technology_object_group` | `folderPath` (a user group) | `TechnologicalInstanceDBUserGroup.Delete()`; the parent is resolved by path before the delete | the parent no longer lists it (always checked) |
| `rename_technology_object_group` | `folderPath`, `newName` | generic `SetAttribute("Name")` with the writability check | `GetAttribute("Name")` |
| `set_technology_object_parameter` | `name`, `parameterName`; `value` **or** `tableName` + `tagName` | `Parameters.Find(name)`, then the `Value` setter with the value converted to exactly the current value's runtime type, or the resolved `PlcTag` | a fresh `Find`: the value must be exactly equal (same type), a `PlcTag` by `Equals`; otherwise `postcondition_failed` with both values |
| `connect_technology_object_hardware` | `name`, `connectionInterface`, `connectionKind` and that overload's fields (below) | the interface is read (`previousInterface`), then the documented `Connect` overload | the interface reads as connected and the overload's documented target property reads back the requested target ([below](#hardware-connections)); otherwise `postcondition_failed` |
| `disconnect_technology_object_hardware` | `name`, `connectionInterface`; optional `interfaceIndex` | `Disconnect()` (not documented for ident) | `IsConnected` must read `false` |
| `export_technology_object` | `name`, `filePath`; optional `overwrite` | `TechnologicalInstanceDB.Export(FileInfo, ExportOptions.None)` to a private temporary file, then placed at `filePath` ([Files](#files)) | the file is re-read: its SHA-256 must equal the export's; length and SHA-256 are reported |
| `import_technology_object` | `filePath`; optional `plcName`, `folderPath`, `overwrite` (`ImportOptions.Override`; default `None`) | `TechnologicalInstanceDBComposition.Import(FileInfo, ImportOptions)` on the group's `TechnologicalObjects` | every reported object is listed in the group; when `Import` returns no list of objects, the objects new in the group are reported (with a warning), and none new without `overwrite` is `postcondition_failed` |
| `update_technology_object_content` | `name`, `content`, `expectedContentHash` (xml, from `get_technology_object_content`) | the export is re-hashed (`state_changed` when it differs), the document must describe exactly the addressed object like the export does (same root element and root children by full XML name, `DocumentInfo` ignored; exactly one object element with exactly one `AttributeList` holding exactly one `Name`, equal to the object's name without trimming), then `Import(file, ImportOptions.Override)` into the object's group | the import returned exactly that object (when it returns a list) and the group lists it |

Objects are addressed by name across the PLC's whole TO tree (case-insensitive, exactly one match; several are
`target_ambiguous`), groups by path (`/` is `TechnologicalObjectGroup`; each segment case-insensitive and unique).
A create whose name is already used by a TO of the PLC (names are unique per PLC: a TO is a DB), or whose group path
already exists, is invalid input: the planner refuses it from the inventory (an earlier delete in the same call frees the
name) and the worker checks again right before `Create`. TIA Portal is expected to reject such a collision itself, but
that is not verified, and the pre-check is what makes the object found afterwards the created one. These pre-checks
and the create, delete, group and import postconditions are Siemens-free rules (`TechnologyObjectWriteChecks`,
Contracts) that the worker and the FakeWorker both call. A name used by a
block that is not a TO is not in the TO inventory; `Create` then fails as TIA Portal reports it. Accepted
type/version pairs depend on the CPU firmware and the installation. The Siemens skill reports that on its V21 setup
`TO_PositioningAxis` needed `10.0` (`1.0`–`9.0`, `11.0` and `12.0` failed); live on V21 on 2026-11-02 (project
MCP_Livetest_01, CPU 1516-3 PN/DP FW V2.9, defect K23) `10.0` was rejected ("The type or version of the library element
'TO_PositioningAxis.10.0' does not exist", `engineering_rejected`) and `6.0` was created, as on 2026-11-01 with the same
CPU firmware ([interim report](../superpowers/acceptance/reports/2026-11-01-fork-live-acceptance-interim.md), row 10).
The server never guesses or probes a version.

### Hardware connections

The Siemens skill obtains `AxisHardwareConnectionProvider` as a service of the TO and warns that writing `Actor.*`
parameters succeeds without connecting the drive. The worker therefore lists the object's services with
`IEngineeringServiceProvider.GetServiceInfos()` (the documented discovery, called before `GetService`), takes each
documented provider whose type name matches exactly, and retrieves it with `System.IServiceProvider.GetService(Type)`:

| `connectionInterface` | Provider (V21 type) | Interface object |
| --- | --- | --- |
| `actor`, `torque` | `Motion.AxisHardwareConnectionProvider` | `ActorInterface`, `TorqueInterface` |
| `sensor` | `Motion.AxisHardwareConnectionProvider` (axis) or `Motion.EncoderHardwareConnectionProvider` (encoder) | `SensorInterface`; a composition is addressed by `interfaceIndex` (zero-based `Item(Int32)` position; required when several exist) |
| `measuringInput` | `Motion.MeasuringInputHardwareConnectionProvider` | the provider itself |
| `outputCam` | `Motion.OutputCamHardwareConnectionProvider` | the provider itself |
| `ident` | `Ident.IdentTechnologicalObjectProvider` | the provider itself (connect only) |

A connect or disconnect resolves only the providers its `connectionInterface` needs, in the order of this table
(`TechnologyObjectPolicy.ProvidersFor`, shared by the host planner, the worker and the FakeWorker): `actor` and
`torque` only the axis provider; `sensor` the axis provider's `SensorInterface` first and the encoder provider only as
the documented alternative, when the axis provider offers no sensor (`PreferredEndpoints`). A provider that
`GetServiceInfos()` lists but `GetService` does not return fails the item (`unsupported_capability`, nothing changed)
only when no needed provider serves the interface. Live V21 2026-11-02 (defect K18, MCP_Livetest_01, `Achse_1`
TO_PositioningAxis 6.0): the object listed `EncoderHardwareConnectionProvider` without `GetService` returning it,
while `AxisHardwareConnectionProvider` served the actor interface; before this rule an actor connect resolved every
provider and failed on the encoder one. The read (`get_technology_object_hardware_connection`) still lists every
provider and reports unavailable ones in `unavailableServices`. Not live-tested after the fix.

`connectionKind` selects the documented overload; the catalog refuses every combination the reference does not
document:

| `connectionKind` | Fields | actor / sensor | torque | measuringInput | outputCam | ident |
| --- | --- | --- | --- | --- | --- | --- |
| `address` | `inputAddress`, `outputAddress` (raw bit addresses, byte address × 8 as the Siemens example derives them), optional `connectOption` (default `Default`) | `Connect(Int32, Int32, ConnectOption)` | same | `Connect(Int32)` with `inputAddress` only | `Connect(Int32)` with `outputAddress` only | – |
| `module` | `modulePath` (exact device item names from the device down) | `Connect(DeviceItem)` | same | `Connect(DeviceItem, Int32)` with `channelIndex` | – | `Connect(DeviceItem)` |
| `modules` | `modulePath` (input), `outputModulePath`; optional `connectOption` (three-argument overload) | `Connect(DeviceItem, DeviceItem[, ConnectOption])` | same | – | – | – |
| `channel` | `modulePath`, `channelNumber`; optional `channelIoType`, `channelType` | `Connect(Channel)` | – | `Connect(Channel)` | `Connect(Channel)` | – |
| `tag` | `tableName`, `tagName` | `Connect(PlcTag)` (analog) | – | – | `Connect(PlcTag)` | – |
| `dbMember` | `dbMemberPath` | `Connect(String)` | same | – | – | – |

`ConnectOption` members are parsed by exact name on the installed enum (only `Default` appears in the sources). The
read reports every documented property of each interface by its runtime type (`deviceItem` with `modulePath`,
`channel` with its device item's `modulePath`, `plcTag`, `integer`, `boolean`, `string`, `enum`); a documented property
missing on the installed type is listed in `unreadable`. Ident documents no `IsConnected`: its state (the effect's
`current`, the replace guard, the read-back) is whether `ConnectedIdentDevice` reads a device.

The Siemens skill: "a successful call is not proof of a real connection"; confirm via `IsConnected`/`InputAddress`/
`OutputAddress`. A connect therefore reads the interface before (`previousInterface` in the result) and after the call
and requires, besides the connected state, that the overload's documented property reads back the requested target
(`TechnologyObjectConnectionEvidence` in Contracts, shared by the worker, the FakeWorker and the host read-back):

| `connectionKind` | Property that must read back the target | Compared (worker; the host compares the rendered values) |
| --- | --- | --- |
| `address` | `InputAddress` and `OutputAddress` (measuring input: `InputAddress`; output cam: `OutputAddress`) | integer equality with the request |
| `module` | `InputOutputModule` (measuring input: `InputModule` and `ChannelIndex`; ident: `ConnectedIdentDevice`) | `Equals` with the resolved `DeviceItem` (host: `modulePath`) |
| `modules` | `InputModule` and `OutputModule` | `Equals` with the resolved device items |
| `channel` | `Channel` | `Equals` with the resolved `Channel` (host: module path, number and the given IoType/Type) |
| `tag` | `OutputTag` | `Equals` with the resolved `PlcTag` (host: tag name) |
| `dbMember` | `PathToDBMember` | ordinal string equality |
| `address` and `modules` with `connectOption` (actor, sensor, torque) | additionally `ConnectOption` ("ConnectOption that has been set when the connection was made"; `Default` when omitted for `address`) | `Equals` with the passed enum value (host: member name) |

A property that reads another value fails `postcondition_failed` naming both; the failed item's `result` then carries
the write result with `previousInterface` and `interface` as structured data (also for a failed disconnect and a
parameter write that did not keep its value), decoded by the host with the same contract as a success. A property that cannot be read is
accepted, with a warning, only when the interface read `IsConnected=false` before the call (then the change to `true`
is evidence of this connect); when it was connected before, or its state was unreadable, or for ident, the connect is
not verifiable and fails `postcondition_failed`. Whether the raw bit addresses read back exactly as passed is a live
acceptance check: if they do not, every `address` connect fails `postcondition_failed` with the read-back values.

### Files

`filePath` is checked lexically (by its text, `TechnologyObjectPolicy.ValidateLocalFilePath`) by the catalog and again
by the worker: an absolute local path on a drive (`C:\...` or `C:/...`), naming a file, without control, format,
wildcard or reserved characters, without a second colon (alternate data streams), without empty, `.` or `..` segments,
without a segment that ends in a dot or a space (Windows drops them silently), and without a reserved device name
(`CON`, `PRN`, `AUX`, `NUL`, `COM0`-`COM9`, `LPT0`-`LPT9` and the superscript `COM¹²³`/`LPT¹²³`, with or without an
extension) in any segment. UNC shares (`\\server\share\...`, `//server/share/...`) are accepted since 2026-11-01
(owner decision); device paths (`\\?\`, `\\.\`, `\??\`) are refused because they bypass Win32 path normalization. `CONIN$`, `CONOUT$` and `CLOCK$` count as reserved names too (`TechnologyObjectPolicy.IsReservedDeviceName`,
the one rule all caller-file checks use). The effect shows the path as the worker uses it (backslash separators). The worker then requires
`Path.GetFullPath` to return exactly that path (case-insensitively; a short `8.3` name, for example, is refused with
the resolved path), and reports what only the file system can tell as warnings, never refusals: a target on a mapped
network drive (`DriveInfo.DriveType == Network`; owner decision: network drives are used as requested), a file that is
itself a reparse point (symbolic link), and an existing ancestor directory that is a reparse point (junction, symbolic
link or mount point).

An export to an existing file needs `overwrite:true` (then the `plc_overwrites_file` guard): the planner checks the
file on the host (host and worker share the machine) and the worker checks again. The export goes to a private
temporary file first; its bytes are staged (`TechnologyObjectFiles`, Contracts) as `.<name>.<guid>.tmp` next to
`filePath` and moved into place, so `filePath` is never partially written. A new file is placed with `File.Move`. An
existing file with `overwrite:true` is swapped with `File.Replace`, keeping the previous file as `.<name>.<guid>.bak`
next to it (.NET Framework has no overwriting `File.Move`). When `ReplaceFile` fails, a previous file it already renamed
to the backup name is put back first; then, for volumes without `ReplaceFile` support (some FAT or SMB volumes; mapped
network drives are allowed), the previous file is renamed to the backup name and the staged file moved in, restoring
the previous file if that fails; a target removed outside the call meanwhile gets the new content as requested. A
failure leaves `filePath` as it was, or restores it from the backup whenever moving it back works, and every failure
message ends with what became of the three files, so both contents can be recovered: `filePath` (not changed, restored
to the previous file, missing, or holding unverified content), the staged file (`the new content is kept at '<path>'`,
removed, or gone) and the backup (`the previous file is kept at '<path>'`, none made, or none left); a test drives every
path (each failing move, a failed restore, a staged file or target removed outside the call, a failed read-back, a
changed file) and checks the files against the message. A backup that cannot be deleted after success is reported as a
warning naming it. The written file is re-read and its SHA-256 must equal the export's (`postcondition_failed`
otherwise, the backup kept and named); the backup is deleted only after that. The leading dot
only sorts the staged and backup files next to the target; they are not hidden. The export directory must exist.

## Guards and verification

| Guard | Severity | Fired by |
| --- | --- | --- |
| `plc_deletes_technology_object` | info | `delete_technology_object` |
| `plc_deletes_technology_object_group_contents` | info | `delete_technology_object_group` of a non-empty group (the effect's `removes.blocks` lists the objects, `removes.groups` the groups) |
| `plc_technology_object_import_replaces` | info | `import_technology_object` with `overwrite:true`, every `update_technology_object_content` (`ImportOptions.Override` is expected to replace a same-name object; not verified live) |
| `plc_overwrites_file` | info | `export_technology_object` with `overwrite:true` over an existing file |
| `plc_technology_parameter_not_hardware_connection` | info | `set_technology_object_parameter` of an `Actor.*` parameter |
| `plc_replaces_hardware_connection` | info | `connect_technology_object_hardware` on an interface that reads as connected (ident: `ConnectedIdentDevice` reads a device); that `Connect` replaces the connection is an expectation (not documented), the read-back checks the requested target |
| `plc_state_unverifiable` (existing) | **block** | an incomplete TO inventory, or a planning read of the parameter or connection that failed (other than not found or ambiguous): the only TO case that blocks, because planning cannot see what the item would change |

Planning reads only public `plc_read` operations: `list_technology_objects` once per PLC and call, the current
parameter (its value becomes the effect's `current`; a value that does not convert to the current type is refused
before anything runs, and so is an unreadable current value) and the current interface (missing or ambiguous interfaces are refused before anything runs).
After the call the shared verifier re-reads through `plc_read`:

| Check | Operations | Compared |
| --- | --- | --- |
| `technologyObjectParameter` | set parameter | `valueKind value` reported by the write against a fresh `get_technology_object_parameters` |
| `technologyObjectConnection` | connect, disconnect | `connected` plus every expected target property (`InputAddress=2048`, `InputOutputModule=Drive_1/Module_1`, ...; an unreadable one counts only when `previousInterface` was not connected), or `not connected` |
| `technologyObject` | create, rename, delete, import | the object's presence and group in `list_technology_objects` (create: also the DB number the create reported); an import that named no object is not verifiable (`success:false`) |
| `technologyObjectGroup` | group create, rename, delete | the group's presence |
| `contentHash` | update content | a re-export's hash (usable as the next `expectedContentHash`) |
| `trusted` | export | the worker re-read the file and compared its SHA-256 with the export's |

Later items on the same object, member or group supersede earlier ones (`superseded`). Technology object names are
unique per PLC (a TO is a DB), so an import supersedes an earlier delete or rename of every name it reports in
`importedObjects`, in any folder, and a later item on an imported name supersedes the import (defect K19, live V21
2026-11-02, MCP_Livetest_01: export, delete and import of `Achse_Test` into `/Achsen` in one call; the delete's
read-back expected `Achse_Test absent`, found `/Achsen/Achse_Test exists` and failed the call although every step had
worked; with the same folder it was already `superseded`). Not live-tested after the fix.

An export of a technology object (`export_technology_object`, `get_technology_object_content`, the content update's
check export) that TIA Portal refuses while the object reads `IsConsistent=false` (live 2026-11-02: after
`set_technology_object_parameter`, a rename or an import) carries the hint to run `compile_check` with the PLC's
`plcName` and retry (defect K20); nothing is compiled automatically. Not live-tested.

The host decodes every worker payload through the worker-payload reader and refuses (`protocol_error`, never echoed) a
payload that contradicts the request or its bounds: more than 100 services or 200 enum members, enum fields on a
non-enum value, a truncation marker on a short list, a connection property not documented for its interface, a
parameter page whose `offset`/`limit`/`parameterName`/`prefix`/`nextOffset` do not answer the request, an export
result whose `filePath` is not the requested path or whose `fileSha256` is not 64 lower-case hex digits, a connect
result for another interface or position or whose `previousInterface` is another endpoint, more than 64 interfaces or a
repeated interface/position, an interface that does not report every documented property exactly once (read, or listed
in `unreadable`), a module path deeper than 32 names, a text value longer than 4,000 characters plus the truncation
marker, an inventory with more than 5,000 objects plus groups or a repeated group, more than 5,000 or repeated
`importedObjects`, an update that names not exactly one object, or a parameter page with repeated names.

## Provenance of every Siemens member used

Sources (read-only reference data, never executed or copied): **(T)** `totally-integrated-claude`
`skills/tia-plc-operations/references/technological-objects.md` (V21-XML-checked: names and parameter types, no return
or property types); **(S)** Siemens `tia-portal-ai-extensions` `openness_development/skills/technology-objects/SKILL.md`;
**(E)** `.../engineering-objects/SKILL.md`; **(W)** `.../object-tree-walking/SKILL.md`; **(B)** the V21 type-name
baselines `api-baselines/v21/step7.json` and `startdrive.json`. The Siemens `parameters` skill covers SINAMICS drive
parameters (`DriveObject.Parameters`, Startdrive), not TO parameters, and was not used.

Status: **existing** (an unchanged existing stub, already used by other features); **runtime** (bound on the live
object by the documented name and argument types through `LibraryRuntimeBinding`: exactly one applicable public
member, otherwise `unsupported_capability` before any call); **runtime lookup** (a property read by its documented
name; informative members degrade to `unreadable`); **generic** (the documented `IEngineeringObject` attribute API).
No stub member was added: the reference documents no return or property type for any TO member, and the skill's type
names (`TechnologicalObject`, `TechnologicalObjectComposition`) are not V21 types (B), so no TO-specific signature is
known well enough to compile against. `ref/*.dll` is unchanged.

| Siemens member | Assembly (B) | Source | Status |
| --- | --- | --- | --- |
| `PlcSoftware.TechnologicalObjectGroup` → `TechnologicalInstanceDBGroup` | Step7 | S, T | existing |
| `TechnologicalInstanceDBGroup.Name`, `.Groups`, `.TechnologicalObjects`; enumeration of `TechnologicalInstanceDBComposition` and `TechnologicalInstanceDBUserGroupComposition` | Step7 | T, S | existing |
| `TechnologicalInstanceDB`, `TechnologicalInstanceDBUserGroup` (type tests) and `TechnologicalInstanceDB.Name` (the existing stub declares `TechnologicalInstanceDB : InstanceDB`, so `Name` binds to `PlcBlock.Name`) | Step7 | T ("Name of the Block"), B | existing |
| `IEngineeringInstance.Parent` (a user group's parent; the device item chain of `modulePath`) | Base | T (`Parent`), E (`DeviceItem.Parent`) | existing |
| `TechnologicalInstanceDB.OfSystemLibElement`, `.OfSystemLibVersion` | Step7 | T | runtime lookup (informative) |
| `Number` on the TO (inherited `PlcBlock.Number` through the existing stub inheritance) | Step7 | existing stub | runtime lookup (informative) |
| `TechnologicalInstanceDB.Parameters`; enumeration; `TechnologicalParameterComposition.Find(String)` | Step7 | T, S | runtime |
| `TechnologicalParameter.Name`, `.Value` (get and the public setter) | Step7 | T, S | runtime |
| `TechnologicalInstanceDBComposition.Create(String, String, Version)` | Step7 | T, S | runtime |
| `TechnologicalInstanceDBComposition.Import(FileInfo, ImportOptions)` (return read as a list of `TechnologicalInstanceDB` when it is one) | Step7 | T | runtime |
| `TechnologicalInstanceDB.Export(FileInfo, ExportOptions)` | Step7 | T | runtime |
| `Delete()` on the TO (not listed under `TechnologicalInstanceDB` in T; inherited `PlcBlock.Delete` per the existing stub inheritance) | Step7 | existing stub | runtime, parameterless |
| `TechnologicalInstanceDBUserGroupComposition.Create(String)`; `TechnologicalInstanceDBUserGroup.Delete()` | Step7 | T, S (`Groups.Create`) | runtime |
| `IEngineeringObject.GetAttributeInfos()`, `GetAttribute("Name")`, `SetAttribute("Name", value)` (renames, through the existing `LibraryReflection.Rename`) | Base | E | generic |
| `IEngineeringServiceProvider.GetServiceInfos()` → `IList<EngineeringServiceInfo>`, `EngineeringServiceInfo.Type` (`System.Type`) | Base (B lists both types) | E (signature, explicit interface implementation, call before `GetService`), W | runtime live check: taken from the interface type itself (reflection on the proxy type returns null, E), and the declaring type, parameterless signature, `IList<Siemens.Engineering.EngineeringServiceInfo>` return, `System.Type`-typed `Type`, a non-null list and every element's type must be as documented, otherwise `unsupported_capability`. Not declared in the stubs: the sources do not say whether `EngineeringServiceInfo` is a class or a struct, which a compiled call would fix in the IL, and adding the member to the stub interface would also change every stub class that implements it |
| `System.IServiceProvider.GetService(Type)` | BCL | E, W | compiled (BCL) |
| `Motion.AxisHardwareConnectionProvider` (a service, S) with `ActorInterface`, `SensorInterface`, `TorqueInterface` | Step7 | S, T | runtime (exact type name among the listed services) |
| `Motion.EncoderHardwareConnectionProvider.SensorInterface` | Step7 | T | runtime |
| `Motion.AxisEncoderHardwareConnectionInterface`: `Channel`, `ConnectOption`, `InputAddress`, `InputModule`, `InputOutputModule`, `IsConnected`, `OutputAddress`, `OutputModule`, `OutputTag`, `PathToDBMember`, `SensorIndexInActorTelegram`; `Connect(Channel)`, `(DeviceItem)`, `(PlcTag)`, `(String)`, `(DeviceItem, DeviceItem)`, `(DeviceItem, DeviceItem, ConnectOption)`, `(Int32, Int32, ConnectOption)`; `Disconnect()` | Step7 | T, S (`Connect(Int32, Int32, ConnectOption.Default)`, `IsConnected`) | runtime |
| `Motion.AxisEncoderHardwareConnectionInterfaceComposition` enumeration | Step7 | T | runtime (non-generic `IEnumerable`) |
| `Motion.TorqueHardwareConnectionInterface`: its eight properties, `Connect(DeviceItem)`, `(String)`, `(DeviceItem, DeviceItem)`, `(DeviceItem, DeviceItem, ConnectOption)`, `(Int32, Int32, ConnectOption)`; `Disconnect()` | Step7 | T | runtime |
| `Motion.MeasuringInputHardwareConnectionProvider`: `Channel`, `ChannelIndex`, `InputAddress`, `InputModule`, `IsConnected`; `Connect(Channel)`, `(Int32)`, `(DeviceItem, Int32)`; `Disconnect()` | Step7 | T | runtime |
| `Motion.OutputCamHardwareConnectionProvider`: `Channel`, `IsConnected`, `OutputAddress`, `OutputTag`; `Connect(Channel)`, `(PlcTag)`, `(Int32)`; `Disconnect()` | Step7 | T | runtime |
| `Ident.IdentTechnologicalObjectProvider.ConnectedIdentDevice`, `.Connect(DeviceItem)` | Step7 | T | runtime |
| `Motion.ConnectOption` (member `Default`; other members by exact name on the installed enum) | Step7 | T (type), S (`Default`) | runtime enum |
| `ExportOptions.None`, `ImportOptions.None`/`.Override`; `Device`/`DeviceItem.Name`, `DeviceItems`, `DeviceItem.Channels`, `Channel.Number`/`IoType`/`Type`; `PlcTag`, `PlcTagTable`, tag table groups, `PlcTagComposition.Find` | Base, Step7 | existing stubs | existing |

## Left out, and why

| Operation | Reason |
| --- | --- |
| Family/version matrix read | No source provides one: the Siemens skill states that no known Openness API enumerates valid type/version combinations and that versions are installation-specific; its one V21 data point (`TO_PositioningAxis` → `10.0`) was contradicted live on 2026-11-02 for a CPU 1516-3 PN/DP FW V2.9 (`10.0` rejected, `6.0` accepted), so the tool description states that the version depends on the CPU firmware. The skill's cascade-probe (try versions until `Create` succeeds) is not implemented: it creates or fails objects by trial in the user's project and guesses versions. |
| Compile a TO | No TO-level compile member is in the sources; compile the PLC software with `compile_check`. |
| Place a TO or a TO user group from a master copy (`CreateFrom(MasterCopy[, MasterCopyMode])` on both compositions) | The root composition is already covered by `library_write instantiate_master_copy` (target `technologyObjects`); placement into a TO subgroup and TO group master copies belong to the library domain and are a follow-up there. |
| `Export(FileInfo, ExportOptions, DocumentInfoOptions)` | The `DocumentInfoOptions` members are not in the sources; the two-argument export is used. |
| Startdrive connection interfaces (`AxisHardwareConnectionSDRProvider`, `EncoderHardwareConnectionSDRProvider`, `*SDRInterface.Connect(Telegram[, ConnectOption])`) | They are in `Siemens.Engineering.Startdrive.dll`, which the worker does not reference, and take `Telegram` objects of Startdrive drive objects (drives are a separate, not yet designed domain). Address, module and DB-member connections cover the documented Step7 path. |
| Disconnect of the ident interface; removing a `PlcTag` from a parameter | Not documented. |
| Moving a TO between groups | No documented member; export, delete and import into the other group are separate items. |
| Interpreter programs (`InterpreterProgramSupport.LoadSource`/`SaveSource`) | The methods are documented, but how the object is obtained from a TO is not stated; deferred. Cam data are now `plc_write save_cam_data`/`load_cam_data` (package G, the TO's `GetServiceInfos()`; see the [V21 API completion reference](V21_API_COMPLETION_SUMMARY.md)). |
| Master value couplings (`SynchronousAxisMasterValues`, `ConveyorTrackingLeadingValues`, `SuperimposingAxes` with `SetPointCoupling`/`ActualValueCoupling`/`DelayedCoupling`, `TechnologicalInstanceDBAssociation.Add/Remove`), interpreter mappings (`InterpreterMappings`, `TOMapping`, `DBMemberMapping`) and `OutputCamMeasuringInputContainer` | How these objects are reached and the coupling property types are not stated in the sources; deferred until live discovery evidence exists. |
| `ShowInEditor` | A UI action; not documented for TOs in the sources. |
| TOs in software units | Not supported by TIA Portal (Siemens skill). |
| Parameter metadata (writability, declared type, limits, units) | No documented member reports it; the runtime type of the current value is the only type evidence. |
| SINAMICS drive parameters (Siemens `parameters` skill) | Startdrive drive objects, not technology objects. |

## Live acceptance checks

1. `list_technology_objects` on a project with axes, encoders, measuring inputs, cams and nested groups: names,
   groups, `number`, and what `systemLibElement`/`systemLibVersion` report compared with the type and version used to
   create the object.
2. `create_technology_object` `TO_PositioningAxis` with the version the CPU firmware accepts (live 2026-11-02, FW V2.9: `6.0`) at the root and in a group; an unavailable version fails
   `engineering_rejected` with the Siemens text; the return value of `Create` (logged only by its effect).
3. `get_technology_object_parameters`: duration of the first and later reads of a large axis, the runtime types of
   common parameters, unreadable parameters, `Find` with names from the listing.
4. `set_technology_object_parameter` for a bool, a real, an integer and an enum parameter, `Actor.*` writes, and a
   `PlcTag` parameter (`_PositionLimits_HW.MinSwitchAddress` from the Siemens example); a read-back difference.
5. `get_technology_object_hardware_connection`: that `GetServiceInfos()` lists `AxisHardwareConnectionProvider` (and the
   other providers) for the matching TO types; the shape of `SensorInterface` (composition or single) for axes and
   encoders; property types.
6. `connect_technology_object_hardware` actor `address` with telegram `StartAddress × 8` (Siemens example) and
   `ConnectOption Default`. Every read-back rule of the evidence table is unproven live and must be checked: whether
   `InputAddress`/`OutputAddress` read back the passed values, whether `ConnectOption` reads back the passed option
   (`Default` included), whether `InputOutputModule`/`InputModule`/`OutputModule`/`ChannelIndex`/`Channel`/`OutputTag`
   read back objects equal (`Equals`) to the passed ones for `module`, `modules`, `channel` and `tag`, whether
   `PathToDBMember` reads back the passed text for `dbMember`; measuring input `Connect(DeviceItem, Int32)`; output cam;
   ident `ConnectedIdentDevice`; `Disconnect()`.
7. `export_technology_object` / `import_technology_object` round trip, `overwrite`, the runtime type of `Import`'s
   return value; `update_technology_object_content` (whether the export has the single `AttributeList/Name` object
   element the document check uses as its template).
8. `rename_technology_object` and `rename_technology_object_group`: whether `GetAttributeInfos()` lists `Name` writable;
   `delete_technology_object` binds `Delete()`; deleting a non-empty group.
9. The same reads and writes on a Multiuser local session.

## Open risks

- The compiled TO access relies on the existing stub's `TechnologicalInstanceDB : InstanceDB` (also used by VCI);
  if the installed hierarchy differed, the first TO access would fail at runtime.
- Provider discovery assumes the providers are listed by `GetServiceInfos()`; if they are not, the hardware
  operations report no interface (`target_kind_unsupported`) instead of guessing another access path. If the installed
  `GetServiceInfos()` differs from the documented signature, every hardware read and write fails `unsupported_capability`.
- Connect verification requires the documented target property (and the passed `ConnectOption`) to read back the
  request exactly; if V21 reports, for example, a different raw bit address, another proxy that is not `Equals` to the
  passed device item, or a different option, those connects fail `postcondition_failed` (after the change was made)
  until the rule is adjusted from live evidence.
- Parameters whose current value is null can only be assigned a `PlcTag`; other null parameters cannot be written
  because their type is unknown.
- The interpretation of `OfSystemLibElement`/`OfSystemLibVersion` as the TO type and version is not verified.
- An import whose return value is not a list of objects identifies new objects only; a replaced object
  (`overwrite:true`) is then not named (warning).
