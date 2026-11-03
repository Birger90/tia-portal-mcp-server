# External Sources, Project-Tree Folders, Software Units, File Exports and the Accessible-Device Scan

**Language:** English · [Deutsch](PLC_SOURCES_FOLDERS_SUMMARY.de.md)

Operations added to `plc_read` and `plc_write` for adoption roadmap rows 7–10
([competitor-feature-adoption.md](../roadmap/competitor-feature-adoption.md)). **Not live-tested**:
everything below is verified offline (Linux unit tests and the `source-folders` FakeWorker fixture,
which models payload shapes only) and needs the [live acceptance list](#live-acceptance-list).

| Tool | Operations |
|---|---|
| `plc_read` | `list_external_sources`, `read_tag_comments`, `find_objects`, `scan_accessible_devices` |
| `plc_write` | `import_external_source`, `generate_blocks_from_source`, `delete_external_source`, `export_source_file`, `rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group`, `create_type_group`, `delete_type_group`, `create_tag_table_group`, `delete_tag_table_group`, `create_software_unit`, `delete_software_unit`, `rename_software_unit`, `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv`, `export_blocks` |
| `library_write` | `instantiate_master_copy` target kind `externalSources` (a master copy into the PLC's external sources) |

The tool counts stay 11/30/30. Method names are `PlcSourceFolderMethods` in
`TiaMcpServer.Contracts/Plc`; worker parameters travel in `WorkerRequest.PlcSourceFolder`; the worker
dispatch is one line plus `Program.SourcesFolders.cs`.

## Access modes and capabilities

| Operations | Capability | Modes |
|---|---|---|
| `list_external_sources`, `read_tag_comments` | `Observe` (worker reads) | every mode |
| `find_objects` | `Observe` (host computation over existing reads, no worker method) | every mode |
| `scan_accessible_devices` | `OnlineObserve` (worker read) | every mode, standalone projects only |
| the fifteen worker writes | `ProjectMutation` | read-write, full |
| the four file exports | `ProjectMutation` (host computation, no worker method) | read-write, full |

**Decision for the scan (owner decision: the access mode is the only control).**
`ConfigurationPcInterface.GetAccessibleDevices()` performs live network discovery through one PC
interface and changes neither a device nor the project. AGENTS.md defines `OnlineObserve` exactly so
("reaches the device but changes neither device nor project") and allows it in every mode, read-only
included; `compare_to_online` is classified the same way. There is no allowlist of interfaces or
networks: the calling agent names the mode and PC interface `{ name, number }` (no default, no
fallback, exact ordinal match on the PLC's own `OnlineProvider.Configuration`), and the human controls
the scan only through the access mode. The reference asks for the interface and discovery scope to be
authorized; that authorization is the agent's request under the chosen mode. Like the other
online-path operations it is standalone-only (`ProjectCapabilityCatalog` standalone list). It runs in a
fixed-No TIA Portal confirmation scope like `compare_to_online`: every dialog raised during the call is
answered No (never the session-wide Yes of read-write/full) and recorded, also while the result is read;
a declined dialog fails the item (`worker_operation_failed`, reported with the call's own failure when
`GetAccessibleDevices()` or reading the returned devices also failed; a lost Portal stays `portal_lost`); a
dialog after the call keeps the No until the next request starts.

**File exports are writes.** They change nothing in TIA Portal but write files at caller paths, so
they are `ProjectMutation` and run through the guarded `plc_write` pipeline (preview with
`dryRun:true`, audit), exactly like `network_write export_io_map`.

## External sources

Each scope — the PLC and each software unit — owns its own `ExternalSourceGroup`
(`PlcExternalSourceSystemGroup`). Only its root sources are addressed: the blocks reference states that
accessing groups inside the "External source files" folder is not supported. `unitName` selects a
software unit's sources; omitted means the PLC's own.

| Operation | Input | Siemens call | Result |
|---|---|---|---|
| `list_external_sources` | optional `plcName` (omitted: every PLC) | `ExternalSources` enumeration, `PlcExternalSource.Name`, `PlcUnitProvider` units | `{ plcs[{ plcName, deviceName, sources[{ name, unitName }] }], complete, messages }` |
| `import_external_source` | `filePath` (absolute local `.scl`, `.awl`, `.db`, `.udt`; a unit takes `.scl`, `.db`, `.udt` only), optional `sourceName` (default: the file name with extension), `plcName`, `unitName` | `ExternalSources.CreateFromFile(name, path)`, then `Find(name)` | `{ operation, plcName, deviceName, unitName, sourceName, filePath, readBackConfirmed }` |
| `generate_blocks_from_source` | `sourceName`, optional `generateOption` (`None` default, `KeepOnError`), `targetGroupPath` (a block group or PLC data type group of the source's own scope; omitted or the root: the scope root), `plcName`, `unitName` | `Find(name)`, then `GenerateBlocksFromSource(option)`, `(PlcBlockUserGroup, option)` or `(PlcTypeUserGroup, option)` | `{ plcName, deviceName, unitName, sourceName, generateOption, targetGroupPath, generated[{ name, kind }] }` in the returned order |
| `delete_external_source` | `sourceName`, optional `plcName`, `unitName` | `Find(name)`, `PlcExternalSource.Delete()`, `Find(name)` again | like import (`filePath` null) |
| `export_source_file` | `filePath` (`.scl`, `.awl`, `.db` or `.udt`), `blockPaths` and/or `typePaths` (1–500, one PLC, one scope), optional `withDependencies`, `overwrite` | `ExternalSourceGroup.GenerateSource(objects, FileInfo, GenerateOptions.None/WithDependencies)` of the objects' own scope | `{ filePath, overwritten, bytes, sha256, withDependencies, plcName, unitName, objects[] }` |

Generation semantics are Siemens': existing blocks and PLC data types of the names the source declares
are overwritten; with `None` an error makes TIA Portal throw and delete what it generated, with
`KeepOnError` no exception is thrown and objects with errors are kept (compile them with
`compile_check`). The server does not compile after generation. `generated[].kind` is `OB`, `FB`,
`FC`, `GlobalDB`, `InstanceDB`, `ArrayDB`, `Block`, `Type`, or the runtime type name of anything else.
`GenerateBlockOption.None` is the compiled member the existing source updates already pass;
`KeepOnError` has no documented numeric value and is taken from the installed assembly by its exact
member name (fails closed with `unsupported_capability` when absent).

`export_source_file` lets TIA Portal write the file into a fresh staging directory next to the target
(`GenerateSource` refuses an existing file), then moves it into place with the shared writer described under file exports (`File.Move`, or
`File.Replace` with a backup with `overwrite:true`), re-reads it and compares the SHA-256. Siemens documents STL and SCL blocks and
`.udt` for PLC data types; the existing global-DB source read uses `.db`. Whatever else TIA Portal
refuses fails the item (`engineering_rejected` or `worker_operation_failed`).

## Project-tree folders

### `find_objects`

`pattern` (required, `*` any run, `?` one character, whole name, case-insensitive), optional `plcName`
(omitted: every PLC), `kinds` (subset of `block`, `block_group`, `type`, `type_group`, `tag_table`,
`tag_table_group`, `watch_table`, `force_table`, `watch_table_group`, `external_source`; omitted: all),
`offset` (default 0), `limit` (1–1000, default 100). Kinds also include `software_unit` (from the
tree) and `tag` and `user_constant` (from `list_tag_tables`: the PLC's own tag tables; the tag inventory
reads no software-unit tag table, so unit tags are not found); tags and user constants carry `tableName`
and `folderPath` of their table and their data type in `objectType`. It is computed in the host from existing reads,
each at most once per item and only for the kinds asked for: the project-tree snapshot (blocks, block
groups, PLC data types and groups, tag tables and folders, of the PLC and its software units),
`list_watch_and_force_tables` and `list_external_sources`. A failed read fails the item; it never
becomes an empty list.

Result: `{ pattern, kinds, offset, limit, totalMatches, nextOffset, complete, messages, matches[{ kind,
name, plcName, deviceName, unitName, path, folderPath, objectType, isSystem }] }`, sorted by PLC,
kind (the order above), unit, path and name; `nextOffset` is null on the last page. `path` is the
plc_read path for blocks, block groups, PLC data types and type groups (null for system blocks and
groups, which have none); tables and groups carry `folderPath` (for a table its folder, for a group the
group itself, `"/"` the root). `complete` is false when tree nodes, the watch-table inventory or the
external-source inventory could not be read completely (`messages` says which). Paging re-reads per
call; it is not a snapshot.

### Group renames

| Operation | Target | Kind |
|---|---|---|
| `rename_block_group` | `blockPath` of a block user group, e.g. `PLC_1/Blocks/Line` or `PLC_1/Units/U1/Blocks/Line` | `BlockGroup` |
| `rename_type_group` | `typePath` of a PLC data type user group, e.g. `PLC_1/Types/Motion` | `TypeGroup` |
| `rename_tag_table_group` | `folderPath` of a tag table folder, optional `unitName`, `plcName` | `TagTableGroup` |
| `rename_watch_table_group` | `folderPath` of a watch-and-force table group, optional `plcName` | `WatchTableGroup` |

All take `newName` (no `/` or `\`). The group is resolved exactly (case-insensitive, one match per
level, never the system group) and renamed through the documented generic attribute API:
`GetAttributeInfos()` must list a writable `Name`, then `SetAttribute("Name", newName)` and
`GetAttribute("Name")` must read the new name back (`postcondition_failed` otherwise; Portal silently
ignored a colliding tag rename before). The blocks reference documents exactly this for block groups;
for the other groups it is the generic `IEngineeringObject` contract, and an installation that does not
list `Name` as writable fails closed with `unsupported_capability` before anything changes. Result:
`{ operation, kind, plcName, deviceName, unitName, path, newPath, oldName, newName }`.

### Creating and deleting PLC data type groups and tag table groups

| Operation | Target | Siemens call |
|---|---|---|
| `create_type_group` | `typePath` of the new group, e.g. `PLC_1/Types/Motion/Drives` or `PLC_1/Units/U1/Types/Motion` | parent `PlcTypeGroup.Groups.Create(name)` (bound at runtime, see provenance), then `Find(name)` |
| `delete_type_group` | `typePath` of a PLC data type user group | `PlcTypeUserGroup.Delete()`, then the parent's groups must no longer list it |
| `create_tag_table_group` | `folderPath` of the new folder, e.g. `/Line/Spare`, optional `unitName`, `plcName` | parent `PlcTagTableUserGroupComposition.Create(name)`, then `Find(name)` |
| `delete_tag_table_group` | `folderPath` of a tag table user folder, optional `unitName`, `plcName` | `PlcTagTableUserGroup.Delete()`, then the parent's groups must no longer list it |

The parent of a create is resolved exactly (case-insensitive, one match per level) and must exist; a
create is not checked for a duplicate while planning (Portal rejects it, like every other create), and
the system group can neither be created nor deleted. A create that `Find(name)` cannot see afterwards,
and a delete whose group is still listed, fail `postcondition_failed`. Result: `{ operation, kind,
plcName, deviceName, unitName, path, name }` (`kind` `TypeGroup` or `TagTableGroup`). Deleting a group
that is not empty fires the info guard `plc_deletes_group_contents` (as `delete_block_group` does) with
the PLC data types or tag tables (and, for the PLC's own tag tables, the tag and user-constant counts)
and subgroups it removes; the subgroups are also the effect's `removes.groups`. A part of the tree inside
the group that could not be read fires the block guard `plc_state_unverifiable`.

## Software units

| Operation | Input | Siemens call (software-units.md §2) |
|---|---|---|
| `create_software_unit` | `unitName`, optional `plcName` | `plcSoftware.GetService<PlcUnitProvider>().UnitGroup.Units.Create(name)`, then `Find(name)` |
| `delete_software_unit` | `unitName`, optional `plcName` | `PlcUnit.Delete()`, then the units must no longer list it |
| `rename_software_unit` | `unitName`, `newName`, optional `plcName` | `SetAttribute("Name", newName)` after a `GetAttributeInfos` writability check, read back with `GetAttribute("Name")` |

Names must not contain `/` or `\`. A PLC without a `PlcUnitProvider` fails `target_kind_unsupported`.
Create and delete return `{ operation, kind:"SoftwareUnit", plcName, deviceName, unitName, path, name }`
(`path` `PLC/Units/Name`); the rename returns the group-rename shape with `kind:"SoftwareUnit"`. Deleting a
unit fires `plc_deletes_group_contents` when it holds blocks, block groups, PLC data types, type groups,
tag tables, tag table folders or external sources (its blocks and block groups are the effect's
`removes`); a rename fires `plc_renames_group` because every `PLC/Units/Name/...` path inside changes.
Relations of other units to the unit (`PlcUnitRelation.RelatedObject`) are neither checked nor
updated. Later items of the call that address the unit (by `blockPath`, `typePath` or `unitName`) carry
`dependsOn` on the create, rename or delete.

## Tag comments (`read_tag_comments`)

Optional `plcName` (omitted: every PLC), `tableName`, `folderPath` — the filters of `list_tag_tables` —
over the PLC's own tag tables and those of its software units (`unit.TagTableGroup`, software-units.md;
`unitName` is null for the PLC's own). Result: `{ complete, messages, plcs[{ plcName, deviceName,
tables[{ unitName, folderPath, tableName, members[{ kind, name, comment }] }] }] }` with `kind` `Tag` or
`UserConstant` and `comment` one `{ culture, text }` per item of the multilingual text with a non-empty
text (`MultilingualText.Items`, `Language.Culture`), or null when it could not be read (a message says
why and `complete` is false). A culture that cannot be read is keyed `(unreadable language #n)` (n the
item's position) with a message, so two unreadable languages never collapse into one key. `PlcTag.Comment` is a typed `MultilingualText`; `PlcUserConstant.Comment` is
listed without a type, so it is read by its exact name at runtime and accepted only as a
`MultilingualText`. A filter that matches no table fails `target_not_found`.

## File exports

Common rules: absolute drive paths or, since 2026-11-01 (owner decision), UNC shares (`\\server\share\...`);
`\\?\` and `\\.\` device paths are refused at validation because they bypass Win32 path normalization; every path segment must be a name Windows can keep: no device name (the stem before the
first dot, trailing blanks removed, is not CON, PRN, AUX, NUL, COM0–COM9, LPT0–LPT9, COM¹²³, LPT¹²³,
CONIN$ or CONOUT$, case-insensitive), no segment ending in a dot or blank, no `.` or `..` segment
(`PlcFileTargets.SegmentError`, the one rule for these operations; `SafeFileSegment` applies the same
device-name rule when it makes project names file-safe). **Locations (owner decision):** a mapped
network drive (`DriveType.Network`) is allowed and the response carries a warning with the normalized
path; on Windows the target directory's final path is resolved (`GetFinalPathNameByHandle`), and a local
directory that resolves through a junction or symbolic link to a UNC share (`\\?\UNC\...`) is written to and
reported, like a final path elsewhere (a warning) (the same check applies to
`network_write export_io_map`). The directory must exist; an existing file is refused unless `overwrite:true` (checked
while planning, so `dryRun` reports it, and again at the move, which never replaces a file that
appeared meanwhile); each file is written to a temporary file in its own directory and moved into
place. A new file is placed with `File.Move`. An existing file (only with `overwrite:true`) is swapped in
with `File.Replace`, which keeps the previous file as a `.<name>.<guid>.bak` sibling; where ReplaceFile
fails (some FAT or SMB volumes, errors such as 1, 50, 1175–1177 or `PlatformNotSupported`) a previous file
that ReplaceFile already renamed aside is restored first, then the previous file is renamed to the backup
name and the new file moved in (on Windows `MoveFileExW` with `MOVEFILE_REPLACE_EXISTING |
MOVEFILE_WRITE_THROUGH`, a flushed rename, never a copy), restoring the previous file when the move fails.
Nothing is ever copied over a destination, and on any failure of the swap the previous file and the new
content are kept and both paths named (an `export_source_file` failure also keeps its staging
directory). The written bytes are re-read and their SHA-256 reported; the backup is deleted only after the
hash matched. A re-read that fails or differs says so precisely: the file was written but not verified,
it is in place, and the previous file is kept at the named backup (`postcondition_failed`). The same
writer and file-name rules serve `network_write export_io_map`. Files are UTF-8 with BOM (the
encoding TIA Portal writes SimaticML and sources in, and the one the block import stages with).
Every export acts on one PLC (an omitted `plcName` needs exactly one PLC in the project).

### CSV exports

`export_tag_tables_csv` (from `list_tag_tables`), `export_watch_tables_csv` and
`export_force_tables_csv` (from `list_watch_and_force_tables`, then `read_watch_table`/`read_force_table`
per table with the 5,000-entry cap): `filePath` ending in `.csv`, optional `plcName`, `tableName`
(one table; for watch/force tables in `folderPath`, default `/`), `folderPath` (tables directly in that
folder or group), `csvDelimiter` (`,` default, `;`, tab), `rawCells`, `overwrite`. RFC 4180 quoting,
CRLF line ends, one header row. Unless `rawCells:true`, a data cell beginning with `=`, `+`, `-`, `@`,
a tab or a carriage return, or with their full-width forms U+FF1D `＝`, U+FF0B `＋`, U+FF0D `－`, U+FF20 `＠`
— also after leading spaces, tabs, CR, LF or no-break spaces — gets a leading `'` so spreadsheets do not
evaluate it (the shared `HardwareIoMapCsv` rule of `export_io_map`); note this also prefixes negative
numbers such as `-5`. A `folderPath` that names no watch-and-force table group of the PLC fails
`target_not_found` (like the tag CSV), unless an incomplete inventory may hide it. The tag export covers
the PLC's own tag tables only (the tag inventory `list_tag_tables` reads no software-unit table).

| Export | Columns |
|---|---|
| tag tables | `RowType` (`tag`/`constant`), `Plc`, `Device`, `Folder`, `Table`, `TableIsDefault`, `Name`, `DataType`, `LogicalAddress`, `Value`, `ExternalAccessible`, `ExternalVisible`, `ExternalWritable`, then one `Comment[<culture>]` column per comment language `read_tag_comments` returns (ordinal order) |
| watch tables | `Plc`, `Device`, `Group`, `Table`, `TableIsConsistent`, `Index`, `Name`, `Address`, `DisplayFormat`, `MonitorTrigger`, `ModifyIntention`, `ModifyTrigger`, `ModifyValue`, `UnreadableAttributes` (`\|`-separated) |
| force tables | as watch tables with `ForceIntention`, `ForceValue` instead of the three modify columns |

Result: `{ filePath, overwritten, delimiter, rawCells, neutralizedCells, plcCount, tableCount,
emptyTableCount, rowCount, bytes, sha256, complete, messages }`. `complete` is false when a read
reported incomplete evidence (inventory incomplete, a table above the entry cap); the file holds what
was read. The tag export reads the comments with `read_tag_comments` (same filters) after
`list_tag_tables`; a failed comment read fails the item, an unreadable comment leaves its cells empty
and makes `complete` false, and a tag the comment read does not list (the tables changed between the two
reads) is counted in a message.

### `export_blocks`

`directoryPath`, optional `plcName`, `blockPath` (a block group, e.g. `PLC_1/Blocks` or
`PLC_1/Blocks/Line`) or `typePath` (a PLC data type group), `format` (`xml` default, `source`),
`maxElements` (1–2000, default 500), `overwrite`. Without a group the whole PLC is exported: blocks and
PLC data types of the PLC and its software units. One project-tree read lists the objects; each one is
read with the existing `get_block_content`/`get_type_content` in the requested format and every
document of the read is written: the xml read's documents (the SimaticML `.xml` and, when present,
the SIMATIC SD `.s7dcl`/`.s7res` companions, `DocumentInfo` removed as in every read), split at their
`--- FILE: name ---` markers by the block import's rules (no undeclared text before the first marker, no
malformed marker line, no empty or repeated document: `protocol_error` otherwise), or the one source
document, written whole (a source line that looks like a marker is content). The directory mirrors the tree: `Blocks/<folders>/<name>.xml`, `Types/...`,
`Units/<unit>/Blocks/...`; every segment is made Windows-safe (`< > : " / \ | ? *` and control
characters become `_`, trailing dots and blanks too, reserved device names get a leading `_`).

Refused before anything is written: more objects than `maxElements`, two objects whose files would
collide after sanitizing (case-insensitive), a missing group, existing primary files without
`overwrite:true`. System blocks are not exported (they are not user content and have no plc_read
path; a message counts them). With `format:"source"` only global DBs (`.db`), SCL FB/FC/OB (`.scl`)
and PLC data types (`.udt`) are exported, every other object is listed in `skipped` with the rule. An
object whose read fails is listed in `skipped` with the failure category and the export continues,
except for `portal_lost`, `binding_conflict`, `access_denied`, `protocol_error`, `state_changed`,
`worker_timeout` and `worker_crashed`, which stop the item. A write failure stops the item and its
message lists the files already written (they remain; there is no rollback). An export with
`overwrite:true` replaces the files it writes and leaves every other file in the directory alone: files
of objects that were deleted or renamed since an earlier export stay (the server never deletes caller
files); the manifest says which files belong to this export.

Last, the manifest `tia-mcp-export-manifest.json` is written into the directory (UTF-8 with BOM,
deterministic indented JSON: `format`, `plcName`, `deviceName`, `scope`, `complete`, `files[{ relativePath,
objectPath, kind, bytes, sha256 }]`, `skipped[{ objectPath, kind, reason, failureCategory }]` in export
order); an existing manifest is refused like every file unless `overwrite:true`. Result: `{ directoryPath,
format, overwrite, plcName, deviceName, scope, objectCount, fileCount, manifestPath, manifestBytes,
manifestSha256, files[{ objectPath, kind, relativePath, bytes, sha256, overwritten }], filesTruncated,
skippedCount, skipped[{ objectPath, kind, reason, failureCategory }], skippedTruncated, complete, messages }`.
`files` and `skipped` list at most the first 50 entries and at most 20,000 (files) and 10,000 (skipped)
serialized characters, a listed `reason` is cut at 200 characters and at most 20 messages are listed
(`filesTruncated`/`skippedTruncated`); the manifest lists everything in full, so the result stays below
the 60,000-character item budget at the 2,000-object cap whatever the path and reason lengths. `complete` is false when an object was skipped or tree nodes in scope were unreadable.
A document followed by another in the xml bundle may end with one newline the bundle format added.

## Accessible devices (`scan_accessible_devices`)

`mode` and `pcInterface { name, number }` (exactly as `get_online_state` lists them; both required),
optional `plcName`, `maxElements` (device cap 1–1000, default 256). The worker resolves the mode and PC
interface on the PLC's `OnlineProvider.Configuration` and calls `GetAccessibleDevices()` inside the
fixed-No confirmation scope; the server applies nothing to the connection configuration and calls no
`ApplyConfiguration` or `GoOnline` (whether TIA Portal opens a connection internally for the discovery
is not documented). The call runs within the worker request timeout (`--worker-timeout-minutes`, 5–60,
default 5); a scan that exceeds it fails `worker_timeout` and the worker is restarted, like any timed-out
request. Result: `{ plcName, deviceName, mode,
pcInterfaceName, pcInterfaceNumber, deviceCount, truncated, maxDevices, devices[{ name, deviceSeries,
address, macAddress }] }`.

The member and the device members are documented without types, so they are bound at runtime
(`PlcAccessibleDeviceProjection` in Contracts, Siemens-free): exactly one public parameterless
`GetAccessibleDevices`, an enumerable result, every element of the exact type
`Siemens.Engineering.Connection.ConfigurationAccessibleDevice` (or derived), the four members read on
that type by their documented names (`Name`, `DeviceSeries`, `Address`, `MACAddress`). A value is
reported only when it is text or an enum value that a member names (a defined value, or a `[Flags]`
combination whose every bit is named; a zero that no member names is undefined); an undefined enum value and any other runtime type fail closed
with `unsupported_capability` naming it, so nothing is rendered by a guessed conversion.

## Planning, guards and verification

`plc_write` plans these items from public reads only: the external sources (only for
`generate_blocks_from_source`, `delete_external_source` and the software unit delete and rename), the PLC's project tree (the block, type and
tag-table renames, the type and tag-table group deletes, the software unit deletes and renames, and
`export_blocks`) and the watch-table inventory (`rename_watch_table_group`). Update and delete targets
(a source to generate from or delete, a group or unit to rename or delete) must exist — missing is
`target_not_found`, ambiguous `target_ambiguous`, unreadable evidence informs with
`plc_state_unverifiable` (TIA Portal decides) — and creates (`import_external_source`, the group and unit creates) are not
checked for collisions (Portal rejects them). The source file of an import must exist at planning. An
external source read that fails with `worker_operation_failed` does not fail the plan: the inventory is
empty and incomplete, so a source to generate from or delete and the sources a unit delete removes are
unverifiable (`plc_state_unverifiable` blocks), and the unit rename guard says the inventory is
incomplete; a lost Portal, binding or protocol failure still stops the plan. Earlier items of a call are
applied, so a later item may use a source imported or a group renamed earlier and carries `dependsOn`
(it is re-planned against real state before its mutation).

Info guards: `plc_generates_from_source`, `plc_deletes_external_source`, `plc_renames_group`,
`plc_export_overwrites_files`, and the shared `plc_deletes_group_contents` for non-empty group and unit
deletes. Verification is `trusted` (the worker reads back imports, creates, deletes and renames; the host
re-reads every written file) or `superseded`. The verification keys (`PlcObjectKeys`, also used by the
existing PLC writes) split every path and folder into segments and mark the last segment of a leaf
(block, PLC data type, tag table, watch or force table) with a control character: a group's or folder's
key prefixes everything inside it at any depth, and a leaf never prefixes a container or object of the
same name (block `Line` beside group `Line`). A rename, create or delete owns the group's path (a rename
its old and new path); a software unit's keys prefix the keys of its blocks, PLC data types, external
sources and tag table folders; a unit's tag table folders have their own key namespace. As a later item,
`generate_blocks_from_source` offers the keys of every block and PLC data type of its PLC: it may
overwrite objects the planner cannot name, so earlier block and type items of the PLC are reported
`superseded` instead of with a content hash it may have made stale (block and PLC data type content
items only, whatever path form they used; container operations, source imports and other generations
are not superseded by it). Block group, PLC data type group and software unit operations own their key in
a separate container namespace, so a later operation on the same or an enclosing container supersedes
them while content items do not. When the later item is trusted without read-back, the message says the
final state is not verified instead of "verified there". A superseded item names what the
later item means for it: a rename says the object is not re-verified and where to re-read it
(`re-read it at '<new path>'`), a delete that it is gone, a generation that it may have been overwritten.

A later item depends (`dependsOn`) on the latest earlier item that touched its own key, one of its
containers, or — new with these operations — the software unit its path names, a tag table folder above
its table, a PLC data type group above its type, or the target group of `generate_blocks_from_source`.
Like `delete_block_group`, a group delete depends on items that touched the group or a group above it,
not on items inside it; its plan still lists what those items created.

## Provenance

| Member | Source | Status |
|---|---|---|
| `PlcSoftware.ExternalSourceGroup`, `PlcUnit.ExternalSourceGroup` (`PlcExternalSourceSystemGroup`) | blocks.md §10; software-units.md §7 and member list; software-hierarchy SKILL.md | existing stubs |
| `PlcExternalSourceComposition.CreateFromFile(String, String)` → `PlcExternalSource` | external-sources.md API reference; blocks SKILL.md (`CreateFromFile(sourceFile.Name, sourceFile.FullName)`) | existing stub, typed call, not live-tested |
| `PlcExternalSourceComposition.Find(String)` → `PlcExternalSource` | external-sources.md API reference; blocks.md §13 (typed example) | **stub added**, typed call |
| `PlcExternalSourceComposition` enumeration | external-sources.md (`GetEnumerator`); software-units.md §7 | existing stub |
| `PlcExternalSource.Name` | external-sources.md ("The name of the Plc external source"); type `string` as for every `Name` member of these references (precedent `PlcWatchAndForceTableGroup.Name`) | **stub added** (getter) |
| `PlcExternalSource.GenerateBlocksFromSource(GenerateBlockOption)`, `(PlcBlockUserGroup, GenerateBlockOption)`, `(PlcTypeUserGroup, GenerateBlockOption)` → `IList<IEngineeringObject>` | external-sources.md; blocks.md §10, §18 | existing stubs, typed calls |
| `GenerateBlockOption.None`, `.KeepOnError` | blocks.md §10 (semantics) | `None` existing (precedent: BlockImporter); `KeepOnError` **by exact name at runtime** (no documented value) |
| `PlcExternalSource.Delete()` | external-sources.md; blocks.md §13 | existing stub |
| `PlcExternalSourceSystemGroup.GenerateSource(IEnumerable<IGenerateSource>, FileInfo, GenerateOptions)`; `GenerateOptions.None`/`WithDependencies` | external-sources.md; blocks.md §10; software-units.md §7 | existing stubs (precedent: the source reads) |
| `PlcBlockUserGroup`, `PlcTypeUserGroup`, `PlcTagTableUserGroup`, `PlcWatchAndForceTableUserGroup` group walks (`Groups`, `Name`) | blocks.md §1, §4; software-units.md §3; watch-force-tables.md | existing stubs |
| Group rename: `IEngineeringObject.GetAttributeInfos()`, `EngineeringAttributeInfo.Name`/`.AccessMode`, `SetAttribute("Name", …)`, `GetAttribute("Name")` | blocks.md §4 (block groups: `SetAttribute("Name", …)`); engineering-objects SKILL.md (attribute read/write) | existing stubs; shared `LibraryReflection.Rename` |
| `OnlineProvider.Configuration`, `ConnectionConfiguration.Modes`, `ConfigurationMode.Name`/`.PcInterfaces`, `ConfigurationPcInterface.Name`/`.Number` | online-connection-configuration.md §1–2; online-and-download SKILL.md | existing stubs |
| `ConfigurationPcInterface.GetAccessibleDevices()` | online-connection-configuration.md §2, §5 (return type undocumented) | **invoked by runtime lookup** (exactly one public parameterless method); not stubbed |
| `ConfigurationAccessibleDevice` (type), `.Name`, `.DeviceSeries`, `.Address`, `.MACAddress` | online-connection-configuration.md §5 (member types undocumented); base.json type list | **read by exact name at runtime**, text or enum values only; not stubbed |
| `PlcTagTableUserGroupComposition.Create(String)` → `PlcTagTableUserGroup` | tags-types.md §2 (typed example `PlcTagTableUserGroup myGroup = groupComposition.Create(...)`) and V21 API list | **stub added**, typed call |
| `PlcTagTableUserGroupComposition.Find(String)` | tags-types.md §1, §2 | existing stub |
| `PlcTagTableUserGroup.Delete()` | tags-types.md §2 (`group.Delete()`), V21 API list ("Deletes this instance") | **stub added** |
| `PlcTypeUserGroupComposition.Create(String)` | tags-types.md V21 API list ("Create the user folder for the PLC data type collection"; no return type, no example) | **bound at runtime** by exact name and `(String)` (`LibraryReflection.Invoke`, `unsupported_capability` when absent); not stubbed; the return value is ignored and the group is read back with `Find` |
| `PlcTypeUserGroupComposition.Find(String)` → `PlcTypeUserGroup` | blocks.md §18 (typed example `PlcTypeUserGroup typeFolder = plc.TypeGroup.Groups.Find("Group_1")`), tags-types.md V21 API list | **stub added**, typed call |
| `PlcTypeUserGroup.Delete()` | tags-types.md V21 API list ("Deletes this instance") | **stub added** |
| `PlcSoftware.GetService<PlcUnitProvider>()`, `PlcUnitProvider.UnitGroup.Units` | software-units.md access entry point | existing stubs |
| `PlcUnitComposition.Create(String)` → `PlcUnit`, `PlcUnitComposition.Find(String)` → `PlcUnit` | software-units.md §1–§2 (typed examples `PlcUnit unit = ...Units.Find("Unit_1")`, `PlcUnit unit1 = unitComposition.Create("Unit_1")`), V21 API list | **stubs added**, typed calls |
| `PlcUnit.Delete()` | software-units.md §2 and V21 API list | **stub added** |
| Software unit rename: `SetAttribute("Name", …)` | software-units.md §2 (one of the three documented ways; `Name` Read/Write) | existing generic stubs; shared `LibraryReflection.Rename` |
| `PlcTag.Comment` (`MultilingualText`), `MultilingualText.Items`, `MultilingualTextItem.Language`/`.Text`, `Language.Culture` | tags-types.md V21 API list; precedent `move_tag` (TagMutationService) | existing stubs |
| `PlcUserConstant.Comment` | tags-types.md V21 API list ("The comment of the user constant"; no type) | **read by exact name at runtime**, accepted only as `MultilingualText`; not stubbed |
| `PlcExternalSourceComposition.CreateFrom(MasterCopy, MasterCopyMode)` (`library_write` target `externalSources`) | external-sources.md V21 API list | **bound at runtime** like the other `instantiate_master_copy` targets (`LibraryReflection.Invoke` by exact parameter types); not stubbed |
| `PlcUnitBase.TagTableGroup` (unit tag tables in `read_tag_comments`) | software-units.md (sub-objects of `PlcUnit`, API list) | existing stub |
| TIA Portal confirmation scope of the scan (`ConfirmationEventArgs`, answered No) | existing `TiaPortalSession.AnswerConfirmations` (compare_to_online precedent) | existing code, no new member |
| Not Siemens: `CreateFileW` + `GetFinalPathNameByHandleW` (kernel32), `DriveInfo.DriveType` | Win32 / .NET documentation | final-path and mapped-drive check of file targets (Windows only) |
| CSV, `export_blocks` | existing reads `list_tag_tables`, `list_watch_and_force_tables`, `read_watch_table`, `read_force_table`, `browse_project_tree`, `get_block_content`, `get_type_content`; the tag CSV also `read_tag_comments` | no new Siemens member |

The compile-time members are exercised by
`reference-stubs/TiaMcpServer.OpennessReferenceProbe/SourceFolderReferenceSurface.cs`; building the
probe against the installed V21 PublicAPI checks the declared return and parameter types. The scope
follows rows 7–10 of [competitor-feature-adoption.md](../roadmap/competitor-feature-adoption.md); the two
competitor tool lists behind those rows were consulted for ideas only (operation names and scope, no
code, no text, no Siemens member taken from them).

## Left out, and why

| Operation | Reason |
|---|---|
| External source user groups (create, delete, list or generate sources inside them) | blocks.md §10 and §13 state twice that accessing groups inside the "External source files" folder is not supported; the V21 API list does name `PlcExternalSourceGroup.Groups`, `PlcExternalSourceUserGroupComposition.Create(String)` (summary: "Creates a MasterCopy"), `Find` and `PlcExternalSourceUserGroup.Delete`, but the manual's statement and the contradictory summary leave no documented use |
| Moving any group (block, type, tag table, watch table, technology object, software unit) | the whole `skills/*/references/` tree was searched: the only documented move member is `HardwareObject.PlugMove(DeviceItem, Int32)` (device items, tia-devices-general); no group composition or user group documents a move, and no reference describes moving a group as a sequence of documented steps |
| Technology objects and technology object groups in `find_objects` | the group create, delete and rename and the object listing are the technology object operations (`create_technology_object_group`, `delete_technology_object_group`, `rename_technology_object_group`, `list_technology_objects`; [TECHNOLOGY_OBJECTS_SUMMARY.md](TECHNOLOGY_OBJECTS_SUMMARY.md)), merged with this feature; `find_objects` does not list them yet (it reads the project tree, the watch-table inventory and the external sources; adding the `list_technology_objects` inventory as a further evidence read is an open follow-up). Find technology objects with `list_technology_objects` meanwhile |
| Group master copies into group compositions (`PlcBlockUserGroupComposition`, `PlcTypeUserGroupComposition`, `PlcTagTableUserGroupComposition`, `PlcWatchAndForceTableUserGroupComposition`, `TechnologicalInstanceDBUserGroupComposition` `.CreateFrom(MasterCopy[, MasterCopyMode])`) | documented in the V21 API lists, but master-copy instantiation is `library_write`'s domain, whose targets are object compositions; a group target kind is a library change (its own left-out table lists the neighbouring gaps) |
| Software unit properties and relations: `Author`, `NamespacePreset`, `Comment`, relations create/delete/modify, `Access` (publish), loadable file generation, unit master copies | documented in software-units.md (the PlcUnit properties table, §4–§6, §8, §10–§11); they are unit content, not containers, and outside rows 7–10. Unit master copies are `library_write`'s existing `softwareUnits` target |
| Compiling after `generate_blocks_from_source` | not left out: the server never compiles implicitly; `compile_check` does it |
| Alarm texts / text lists as CSV | the alarm and text-list operations are merged ([PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.md)): they read and export TIA Portal's XLSX workbook (`read_*_workbook` as uninterpreted cells, `export_text_lists`, `export_alarm_texts`); a CSV projection of those cells is not implemented and remains an open follow-up that can reuse the CSV writer |
| Software-unit tag tables in the tag CSV, and unit tags/user constants in `find_objects` | the CSV and the `tag`/`user_constant` kinds use the PLC tag inventory (`list_tag_tables`, a shared read with its own contract), which reads the PLC's own tag tables only; reading unit tag tables (`unit.TagTableGroup`, software-units.md) there changes that shared contract, an owner decision. Unit tag-table comments are read (`read_tag_comments`, `unitName` per table); unit tag tables and folders are found as `tag_table`/`tag_table_group` |
| `PlcUnit.Export()` | software-units.md lists `Export()` returning void without parameters; every documented SimaticML export of the reference takes `(FileInfo, ExportOptions)`, so the call shape is undocumented |
| Named value type documents (`unit.TypeGroup.Documents`, `PlcDocument`, software-units.md §9) | §9 documents only `Documents.Find(name)` and a read-only `Name`; no enumeration, export or content member, and the project tree does not list them, so `find_objects` cannot search them |
| Software unit alarm text lists in the unit-delete guard | `PlcUnitBase.PlcAlarmTextlistGroup` is listed (software-units.md API list), but the merged text list reads (`list_text_lists`, [PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.md)) cover the PLC's own `PlcSoftware.PlcAlarmTextlistGroup` only, not a unit's; the guard message says they are not listed |
| XLSX exports | no Siemens member is involved; it needs an OOXML writer (a new dependency or a hand-written package), an owner decision. CSV with UTF-8 BOM opens in every spreadsheet program |
| Applying an online path or going online from the scan | not left out: that is `plc_tia_online`; the scan only lists |

## Known limits and open risks

- Nothing here is live-tested; the FakeWorker fixture shows payload shapes, not Siemens behavior.
- `PlcExternalSourceComposition.Find` case rules are undocumented; planning matches `sourceName`
  exactly (ordinal) and the worker uses `Find`.
- Whether `CreateFromFile` keeps a reference to the file or copies it, and how TIA Portal handles a
  source file that is UTF-8 without BOM, are unverified (blocks.md: UTF-8 with BOM is required when
  the source contains special characters).
- `GenerateSource` with mixed blocks and PLC data types, or with an extension not matching the
  objects, is Siemens' decision; the outcome is unverified.
- `GetAccessibleDevices()` may take long on a large network; it runs within the worker request
  timeout, and a scan that exceeds it fails `worker_timeout` and restarts the worker (the binding then
  needs `bind_project` again, like after any worker restart). Whether it needs permissions or raises TIA
  Portal dialogs is unknown; a raised dialog is answered No and fails the item.
- The group renames assume `Name` is listed as writable for type, tag-table and watch-table groups;
  tags-types.md states tag-table group names are writable from V20 on (for type and watch-table groups no
  reference says), so the runtime check decides. One competitor tool list reports that its folder rename
  works only for technology-object groups; if V21 reports `Name` read-only for block, type, tag-table or
  watch-table groups, the renames fail closed with `unsupported_capability` and nothing changes. Live
  acceptance item 6 settles this.
- The location check resolves the final path of the target directory once, at planning and again just
  before the write; a junction changed in between is not seen. A local directory symbolic link (or
  junction) that points at a mapped drive letter resolves to the share's UNC path and is written to with a warning
  (UNC shares are allowed since 2026-11-01). Mapped drives and shares warn but are written to:
  the temporary-file-and-rename write is atomic only as far as the file server makes it.
- `find_objects` and `export_blocks` read the whole project tree; a very large project can hit the
  snapshot limit (`snapshot_too_large`).
- `PlcTypeUserGroupComposition.Create(String)` has no documented return type; it is bound at runtime and
  the result is confirmed only by `Find(name)`. Whether a type group create or delete is refused while
  types in it are used, and what a non-empty group delete does to blocks using its types, is Siemens'
  behavior and unverified.
- Deleting a software unit removes everything in it; whether TIA Portal refuses it while other units
  relate to it, and whether a unit rename updates other units' relations, is unverified. Neither is
  checked by the planner.
- The comment columns of the tag CSV depend on the languages found, so two exports of different projects
  can have different headers.

## Live acceptance list

On a disposable standalone project with one S7-1500 PLC and one software unit, in `full` mode:

1. `list_external_sources`; `import_external_source` an SCL FB file (with and without `sourceName`),
   a `.udt`, a `.db` and an `.awl` STL block into the PLC and a `.scl` into the unit; import the same
   name again (record Portal's rejection); a UNC share path (accepted) and a `\\?\` path (refused).
2. `generate_blocks_from_source` with `None` into the root, into a block user group and (for the
   `.udt`) into a type user group; a source with an error once with `None` (nothing kept) and once with
   `KeepOnError` (objects kept); check `generated` and compile.
3. `delete_external_source`; confirm the generated blocks remain.
4. `export_source_file` for two SCL blocks with and without `withDependencies`, a UDT to `.udt`, a
   unit block (unit scope), to an existing file without and with `overwrite`; compare the file with a
   manual "Generate source from blocks".
5. `find_objects` with `*`, `?`, kinds and paging on a project with nested groups, system blocks,
   watch tables and external sources.
6. `rename_block_group`, `rename_type_group`, `rename_tag_table_group` (PLC and unit) and
   `rename_watch_table_group`; rename to an existing sibling's name (record the read-back result);
   record whether `Name` is listed as writable for each group kind.
7. `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv` (all tables, one
   table, a folder); open them in a spreadsheet; check neutralized cells and `rawCells`.
8. `export_blocks` for the whole PLC in `xml` and `source`, for a group, with know-how protected and
   fail-safe blocks present; re-import one exported `.xml` with `update_block_logic`.
9. `scan_accessible_devices` on the PLCSIM interface and on a real Ethernet adapter with devices on
   the wire, in read-only mode; record the runtime types of `DeviceSeries`, `Address`, `MACAddress`
   and the duration.
10. `create_type_group` (PLC root, nested, unit) and `delete_type_group` (empty; with a type used by a
    block; record Portal's behavior); record the runtime return type of `PlcTypeUserGroupComposition.Create`.
11. `create_tag_table_group` and `delete_tag_table_group` (PLC and unit, empty and with tables); a
    duplicate create (record Portal's rejection).
12. `create_software_unit`, `rename_software_unit` (record whether `Name` is listed writable and whether
    relations of another unit follow the rename) and `delete_software_unit` of a unit with blocks, types,
    tag tables, external sources and a relation from another unit.
13. `read_tag_comments` on tags and user constants with comments in two project languages, an empty
    comment and a user constant (record the runtime type of `PlcUserConstant.Comment`); the tag CSV with
    those comments.
14. `library_write instantiate_master_copy` of an external-source master copy with `target.kind:
    "externalSources"`, each `masterCopyMode`.
15. File locations: each export into a mapped network drive (warning, file written), into a local
    directory that is a junction or directory symbolic link to a share (refused), into a junction to
    another local volume (warning); a `filePath` with `CON`, `COM¹`, `nul .txt`, a trailing dot (refused
    at validation); `overwrite:true` onto a share that does not support `ReplaceFile` (rename-aside fallback with
    `MoveFileExW`; the previous file is kept as `.bak` until the hash matched).
16. `scan_accessible_devices` while a TIA Portal dialog appears (for example the first access to an
    interface): the item fails with the declined dialog listed; in read-write mode the session-wide Yes
    is not used.
17. `export_blocks` of more than 50 objects: `filesTruncated:true`, the manifest lists every file with
    the hashes the files read back; a second export with `overwrite:true` after deleting a block leaves
    the deleted block's old file in place and lists only the current files in the manifest.
