# Feature adoption from other TIA Portal MCP servers (fork)

Snapshot: 2026-11-01. Compares this fork's tool surface with four other TIA Portal MCP servers and
decides which capabilities to adopt. Updated weekly by the "TIA-MCP Repo-Monitor" scheduled task
(reports under `docs/roadmap/upstream-watch/`).

## Sources and licences

| Server | Version / date read | Licence | What may be reused |
| --- | --- | --- | --- |
| [heilingbrunner/tiaportal-mcp](https://github.com/heilingbrunner/tiaportal-mcp) | README, 2026 | MIT | code with attribution |
| [1748477582/tia-portal-openness-mcp](https://github.com/1748477582/tia-portal-openness-mcp) | v2.6.3, `docs/tool-capability-matrix.md` (232 tools, generated 2026-10-09) | MIT | code with attribution |
| [a4webdev/tiacommander-mcp](https://github.com/a4webdev/tiacommander-mcp) | README tool map, last commit 2026-09-07 | proprietary EULA, binary only | **ideas only** — no code, no text |
| [rickgaiser/TiaMcp](https://github.com/rickgaiser/TiaMcp) | README and STATUS.md at 8e9de1c (2026-10-30) | AGPL-3.0 | code may be adopted (owner decision 2026-11-01; since the same day the fork is published under AGPL-3.0-only) under the rules in [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md) |

Every adopted capability is checked against Siemens sources (installed V21 Openness XML/DLLs,
Siemens' `tia-portal-ai-extensions` skills, the V21-XML-checked references used in this repo); another
server's implementation of a Siemens call is never evidence on its own. Code from rickgaiser/TiaMcp may
be reused under [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md); code from the MIT servers with attribution.

## Already covered by this fork

Project lifecycle and binding; project tree; block/type read and write (SCL/DB/UDT source, other
languages as SimaticML); tag tables, tags (incl. external-access flags), user constants; compile;
cross-references; hardware/network read and device/subnet/IO-system writes; catalog search;
WinCC Unified reads and tag/alarm writes; VCI; download to PLCSIM or hardware (`plc_download`);
S7-PLCSIM Advanced control and scenario tests; S7-1500 Web API online read/write (operating mode,
tags, diagnostics buffer, alarms, modules, backup/restore); Openness online/offline compare, go
online/offline (`plc_tia_online`), station upload (`plc_station_upload`), watch/force tables;
module plug/move/copy/delete/change type, hardware parameters, I/O addresses, address occupancy,
next free address, tag move and I/O map export; block and PLC data type interface members, DB
start values at any depth, instance DBs, block rename/renumber/copy/move and tag table move;
project and global libraries (`library_read`/`library_write`: open/retrieve/create/save/close, tree,
types and versions, instances, update check, compare, master copy capture/copy/delete/rename/instantiate,
type-version instantiate, update project, edit/release/discard/default/delete, renames);
program analysis (call tree, call paths, unused blocks, orphaned instance DBs, change impact) and
live values of a watch or tag table over the S7-1500 Web API (`read_watch_table_live`); PLC text lists,
alarm texts, ProDiag supervisions, ProDiag assignments and the common alarm classes (`plc_read`/`plc_write`: list,
rename, range, comment per culture, delete, create from a master copy, hash-checked XLSX cell updates, export/import to
local paths with import verification, `AssignedProDiagFB`, the ProDiag alarm information export; not live-tested).

## In progress (branches in this fork)

- `feature/external-sources-folders` (rows 7–10, not live-tested): external sources (list, import,
  generate blocks, delete, export as a source file, instantiate an external-source master copy with
  `library_write`), `find_objects` by glob with paging, block / PLC data type / tag-table / watch-table
  group renames, PLC data type and tag-table group create/delete, software unit create/delete/rename,
  tag/watch/force tables as CSV (tag comments per language, also readable with `read_tag_comments`),
  batch export of blocks and PLC data types to a directory, and `scan_accessible_devices`
  (`GetAccessibleDevices()` on a named PC interface). See
  [PLC_SOURCES_FOLDERS_SUMMARY.md](../SupportedOperations/PLC_SOURCES_FOLDERS_SUMMARY.md); its
  left-out table names what is still open with the reason (group moves: no reference documents one;
  external-source groups: not supported by Openness; technology objects in `find_objects`: open
  follow-up (the merged technology-object operations bring their own group operations); alarm-text
  CSV: open follow-up now that the alarm branch is merged; group master copies: `library_write`;
  XLSX: owner decision).

## Candidates to adopt next

Priority is for a PLC programmer's daily work. "Source" names where the Siemens members must come
from before design starts; nothing here claims Openness supports it until that check is done.

| # | Capability | Seen in | Priority | Siemens source to verify first |
| --- | --- | --- | --- | --- |
| 1 | LAD/FBD network editing: read a network as structured text, add/delete/replace networks, insert/update rungs, wire element pins; SCL statement-level edits | tiacommander `blocks_read`/`blocks_write`, 1748477582 `DescribeBlockLogic` | hoch | SimaticML schema in installed `PublicAPI\V21\Schemas`; `tia-import-export/references/plc-blocks.md` |
| 2 | Program analysis: call tree, call paths from OBs, unused blocks, orphaned instance DBs, impact of a change | tiacommander `xref`, 1748477582 `GetCallChain`/`AnalyzeBlockImpact` | in progress | existing cross-reference reader (no new Siemens members) |
| 3 | Library remainder after `feature/libraries`: archive/save-as a global library, export a type version, publish a block/UDT as a type, clean up unused types, harmonize, update a global library from the project, type/master-copy folder create/delete and master copies below the root folder | tiacommander `library` (29 ops), 1748477582 `ImportMasterCopyFromGlobalLibrary` | hoch | the parameter lists in the installed V21 `Siemens.Engineering.Base.xml` (the reviewed sources name these members without them) |
| 4 | Live values of a project watch table or tag table read through the S7 Web API (combine offline table + online read) | tiacommander `live_data.read_watch_table`, 1748477582 `GetPlcLiveValuesS7` | in progress | existing Web API client (`PlcProgram.Read`) |
| 5 | PLC alarm remainder after `feature/alarms-textlists`: typed text list entries (add/update/delete by value range), create a text list by name, individual alarm class objects, program/supervision alarms as objects, supervision settings DAT export/import, software-unit text lists, ProDiag FB creation | tiacommander `alarm_text` (`add_entries`, `delete_entries`, `create_textlist`) | mittel | the installed V21 `Siemens.Engineering.Step7.xml`; the reviewed sources (`tia-plc-operations/references/{alarms,supervision}.md`, `tia-import-export/references/plc-alarms-and-tags.md`, Siemens `libraries-and-alarms`) document no entry type, no create-by-name, no alarm class or alarm object and no `SupervisionSettingsProvider` owner (see `PLC_ALARMS_TEXTLISTS_SUMMARY.md`) |
| 6 | Technology objects: list, create, parameters read/write, hardware connection, export/import | tiacommander `technology_objects`, 1748477582 `GetTechnologyObjects` | in progress | `plc_read`/`plc_write` (`feature/technology-objects`, not live-tested): list, typed parameter reads/writes with read-back, create/delete/rename, groups, hardware connect/disconnect, SimaticML export/import and content round trip; no type/version matrix (no source provides one), no TO compile member; cam data, interpreter programs, master value couplings and Startdrive connections left out, see `TECHNOLOGY_OBJECTS_SUMMARY.md` |
| 7 | External sources: import SCL/STL/DB source files and generate blocks from them | heilingbrunner `CreateExternalSourceFromFile`/`GenerateBlocksFromSource` | in progress | `plc_read list_external_sources`, `plc_write import_external_source`/`generate_blocks_from_source`/`delete_external_source`/`export_source_file` (external-sources.md, blocks.md; not live-tested) |
| 8 | Accessible-device scan (`GetAccessibleDevices`) on a PC interface | tiacommander `diagnostics.scan_devices` | in progress | `plc_read scan_accessible_devices`, `OnlineObserve`, explicit PC interface (online-connection-configuration.md; not live-tested) |
| 9 | Project-tree folder operations: find by glob across a container, rename/move groups | tiacommander `folders`, heilingbrunner `FindInCode`/`WhereUsed` | in progress | `plc_read find_objects`, `plc_write rename_block_group`/`rename_type_group`/`rename_tag_table_group`/`rename_watch_table_group`, `create_type_group`/`delete_type_group`/`create_tag_table_group`/`delete_tag_table_group` (tags-types.md), `create_software_unit`/`delete_software_unit`/`rename_software_unit` (software-units.md); group moves left out (no reference documents a group move) |
| 10 | Exports to files: tag tables, watch tables, alarm texts as CSV/XLSX; batch export of blocks to a directory | tiacommander `*.export_*`, 1748477582 `ExportBlocks` | in progress | `plc_write export_tag_tables_csv` (with `Comment[culture]` columns from `plc_read read_tag_comments`)/`export_watch_tables_csv`/`export_force_tables_csv`/`export_blocks` from existing reads; alarm texts (after `feature/alarms-textlists`) and XLSX left out |
| 11 | WinCC Unified screen editing: screens, screen items, dynamizations, button events/scripts, layouts | 1748477582 `[HMI]` (28 tools) | mittel | `tia-hmi-operations/references/unified-*.md` |
| 12 | Startdrive: drive parameters, telegrams, drive components | 1748477582 `[Drive]` | niedrig | `tia-simatic-drives` references; Siemens `drive-objects`/`parameters`/`telegrams` skills |
| 13 | WinCC Classic/Comfort HMI | 1748477582 | niedrig | `tia-hmi-operations/references/hmi-target.md` |
| 14 | Server ergonomics: "find tools" search, authoring guide, MCP workflow prompts (audit project, edit block) | 1748477582 `FindTools`/`GetAuthoringGuide`/`Workflow_*` | niedrig | none |
| 15 | File-based source-tree workflow: export a scope to disk mirroring the project tree, edit the files, write back only changed items in dependency order, refuse items changed in TIA Portal since the read (manifest), dry-run, read back after writing | rickgaiser `read_source_tree`/`write_source_tree` | hoch | existing import/export routes (SimaticML, SIMATIC SD, external sources); no new Siemens members expected |
| 16 | S7-GRAPH as text: steps, transitions, actions and conditions read as compact text; step/transition attributes and conditions writable | rickgaiser `.graph.il` | mittel | SimaticML GRAPH schema in the `tia-import-export` references |
| 17 | Whole-block STL read/write as AWL text; STL networks embedded in mixed blocks as a read-only sidecar | rickgaiser | mittel | external sources (`GenerateSource`, `.awl`); SimaticML STL network schema |
| 18 | CSV write-back: PLC tag tables (a removed row deletes the tag), WinCC Unified tag tables, HMI alarms and alarm classes | rickgaiser | mittel | existing typed tag/alarm writes; HMI alarm-class members in the `tia-hmi-operations` references |
| 19 | WinCC Unified: screens as JSON from the object model (defaults sampled in a rolled-back transaction), screen groups, faceplate type interfaces, item-type description, HMI tag table groups, alarm-class delete (see also row 11) | rickgaiser | mittel | `tia-hmi-operations` references; transaction API in the Siemens skills |
| 20 | Classic WinCC (Comfort/Advanced/RT Advanced): screens, templates, pop-ups, tag tables, text lists as Openness XML (see also row 13) | rickgaiser | niedrig | `hmi-target.md`; the worker would need `Siemens.Engineering.WinCC.dll` |
| 21 | List running TIA Portal instances without an Openness access prompt (process list plus attached clients); start the TIA Portal GUI with a project | rickgaiser `list_tia_instances`, `tia_connect startPortal` | done in part (`feature/v21-pitfall-guards`, not live-tested) | `bind_project list_portals` already listed without `Attach()` (`TiaPortal.GetProcesses()`, `Id`, `ProjectPath`, `Mode`; no access prompt, handles never disposed); it now adds `otherClientCount` from `TiaPortalProcess.AttachedSessions`. Starting a TIA Portal with UI is left out: the documented `new TiaPortal(TiaPortalMode.WithUserInterface)` would tie the instance to the recycled worker (lifetime on Dispose/exit unverified, conflicting sources), and rickgaiser's start of `Siemens.Automation.Portal.exe` is no Openness API; see `PROJECT_OPERATIONS_SUMMARY.md` |
| 22 | Guards for V21 pitfalls reported by rickgaiser (not verified here): `.s7dcl` import mangles a nested array index in LAD/FBD, replaces a project FB/UDT named like a system type (TON, TOF, TP, TONR, CTU, CTD, CTUD, R_TRIG, F_TRIG) and drops AT overlays; TIA Portal terminates when overwriting a textual-interface block exported more than once since the project was opened; a classic screen with a duplicate `<Number>` terminates TIA Portal; exporting a graphic list crashes it; reading `HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText` crashes it; an open HMI tag editor slows Openness calls about eight times | rickgaiser README "Known limitations" | done (`feature/v21-pitfall-guards`, not live-tested) | informative guards `plc_s7dcl_import_may_alter`, `plc_textual_interface_overwrite_may_terminate`, `plc_save_before_write_recommended`, read warnings, worker export ledger, `s7dclConstructs` verification; graphic lists/`ScriptDiagnosisOverviewText` have no path, classic screens are unsupported, tag editor documented. Every pitfall needs its live case in `V21_PITFALLS.md` before a text says "TIA Portal does"; open: a one-export write path for textual-interface blocks, VCI `SimaticSD` imports |
| 23 | Compiler report an LLM can count: `depth` and a leaf flag per row, an errors-only leaf view, the 200-row cap filled with leaf rows first (TIA Portal propagates an error state with empty text to every ancestor node) | tiacommander `get_compiler_errors`, CHANGELOG v3.42.0 | hoch | `tia-project-general/references/compile.md` §3 |
| 24 | Online-state guard for offline-only work: `compile_check`, content imports and creates, `save_project` and content exports read `OnlineProvider.State` while planning and block or warn with a "go offline" hint | tiacommander `session save`, CHANGELOG v2.33/v2.36/v3.30 | hoch | `compile.md` (devices offline before compiling), `tia-devices-general/references/device-attributes.md`, `tia-plc-operations/references/online-status.md`; live check which V21 operations refuse online |
| 25 | Download readiness: optional compile before `plc_download` (errors stop it, the report goes into the evidence) and a dry-run verdict covering path, online state and compile state | tiacommander `download_check`, `upload_check` | hoch | `tia-plc-operations/references/download-upload.md` ("Before a download"); Siemens `change-detection` skill (`PlcChecksumProvider`) |
| 26 | Multilingual comments of tags and user constants on create/update, external-access flags on `create_tag`, info guard when `delete_tag` loses a comment or flags | tiacommander `tag add_tag`/`update_comment`/`delete_tag` | hoch | `tia-plc-operations/references/tags-types.md` (`PlcTag.Comment`, `PlcUserConstant.Comment`), `tia-project-general/references/language-settings.md` §5 |
| 27 | Objects from documents: create a new block, PLC data type, tag table or watch table from an inline SimaticML or source document (import as new, refuse existing names, return the hash); tag-table SimaticML read and hash-guarded overwrite with tag counts before/after | tiacommander `import_xml_inline`, `udt create`/`import_xml`, `tag import_table`, `watch import_table` | hoch | `tia-import-export/references/plc-blocks.md` §1–§4, `tags-types.md` (`PlcTypeComposition.Import`, `PlcTagTable.Export`, `PlcTagTableComposition.Import`), `tia-plc-operations/references/watch-force-tables.md` |
| 28 | PLC data type lifecycle: delete a type, move it between type groups (transaction round trip with the recovery store) | tiacommander `udt delete`/`move` | mittel | `tags-types.md` (`PlcType.Delete`, `Export`, `PlcTypeComposition.Import`) |
| 29 | `create_block` options: explicit `number`, `memoryLayout` for DBs/FBs, header title/comment/author | tiacommander `db create`, `create_block` | mittel | `plc-blocks.md` §1, `tia-plc-operations/references/blocks.md` (`MemoryLayout`) |
| 30 | Consistency and change markers: block/type `IsConsistent`, modified dates, PLC program checksum and block fingerprints as reads, to see UI-side changes between calls and give evidence-based "compile first" hints | tiacommander `check_consistency`, activity-log limitation | mittel | `blocks.md` (`IsConsistent`, §15 fingerprints), `tags-types.md`, Siemens `change-detection` skill |
| 31 | Ethernet node settings `UseRouter`, `RouterAddress`, `IpProtocolSelection`, `UseIsoProtocol`; renaming a device (station) | tiacommander `hardware set_network_config` | mittel | `tia-networks/references/subnets-and-nodes.md` (Ethernet node table), `device-attributes.md` |
| 32 | Hardware compile: `compile_check` scope for a device (HW + SW) or a device item (HW) | tiacommander `hardware compile_all`/`compile_device` | mittel | `compile.md` §1 |
| 33 | Archive verification: wait boundedly for the archive file after `archive_project`, report path, size and SHA-256, `postcondition_failed` when it never appears | tiacommander CHANGELOG v3.31.0 | mittel | `tia-project-general/references/project-lifecycle.md`; no new member |
| 34 | Rename consequences: info guard on block, TO, tag (`update_tag`) and group renames listing references that name the object; live check whether V21 updates them | tiacommander `rename_block`, `technology_objects rename`, `tag rename` | mittel | `blocks.md`, `tags-types.md`, `technological-objects.md`; existing `read_cross_references` |
| 35 | Safer content writes: keep the pre-import export of content updates in the recovery store and offer an explicit restore item (no silent automatic restore) | tiacommander transactional overwrite (CHANGELOG v3.37.0) | mittel | `tia-csharp-common/SKILL.md` (transactions), `tia-import-export/references/overview.md` (Override) |
| 36 | Chunked reads of documents over the value budget (`offset`/`length` with the whole-document hash) for block, type, table and TO content | tiacommander `admin get_export` | mittel | none (host budget) |
| 37 | Project ↔ online CPU identity check before Web API reads that use project data (project order number against `read_cpu_type`) | tiacommander `live_data read_tag_table` | mittel | Web API client `Plc.ReadCpuType` (PLC online reference provenance), `device-attributes.md` |
| 38 | Watch/force tables as entries (add/update/delete, display format from the tag's data type) composed into the SimaticML with the existing hash; rename and move tables by transaction round trip; export a table to a file | tiacommander `watch` (14 actions) | mittel | `watch-force-tables.md` (entry members, `PlcWatchAndForceTableDisplayFormat`, `Import`); watch-table SimaticML in the installed V21 schemas |
| 39 | Address use from code: a "used in code" mode and double-write conflicts for `read_address_occupancy`/`find_free_address` | tiacommander `get_assignment_list`, `find_next_free` | mittel | cross-reference sources in `blocks.md`/`tags-types.md`; check absolute-address references |
| 40 | Openness binding probe: a `probe_api`-like read listing every runtime-bound member and whether it binds on the installed V21 | tiacommander capability probe (CHANGELOG v2.59.2) | mittel | none (installed assemblies) |
| 41 | TO version drift: compare TO versions before and after `compile_check` and the implicit compile of content writes, warn on change | tiacommander CHANGELOG v3.40.0 | niedrig | `tia-plc-operations/references/technological-objects.md` |
| 42 | Station upload evidence: re-find the created `ConfigurationAddress` before uploading and report it; document that `isConfigured` is not readiness | tiacommander CHANGELOG v2.36/v2.38 | niedrig | `tia-networks/references/online-connection-configuration.md`, `download-upload.md` |
| 43 | Download or go online to an explicitly given IP address, confirmed per call | tiacommander `configure_connection` | niedrig | `download-upload.md` (`ConfigurationAddress`), `online-connection-configuration.md` |
| 44 | Scan annotation: mark each scanned device as matching a project node or not, never filtering | tiacommander `scan_devices` | niedrig | none new (after `feature/external-sources-folders`) |
| 45 | Tree and project summaries: item counts and empty markers in `browse_project_tree`, a one-line project summary in `get_project_status` | tiacommander `folders get_tree`, project shape on connect | niedrig | none (host) |
| 46 | MCP comfort: progress notifications for long calls and human-readable tool titles | tiacommander CHANGELOG v2.13/v3.4 | niedrig | none (MCP) |
| 47 | Openness firewall diagnostics: doctor check and troubleshooting for the permanent allow and the security-exception path | tiacommander user guide | niedrig | Siemens `licensing-and-firewall` skill |
| 48 | Not-found suggestions ("did you mean") of the same kind | tiacommander CHANGELOG v2.47/v3.20 | niedrig | none |
| 49 | Optimistic TO parameter write: optional `expectedValue` | tiacommander `technology_objects set_parameter` | niedrig | none new |
| 50 | Hardware exports: AML/CAx export of a device or project, device list as CSV | tiacommander `export_hardware_map`, `export_csv` | niedrig | `tia-import-export/references/hardware-aml.md` (`CaxProvider`), baseline `step7.json` |
| 51 | Documentation sweep for the pitfalls in the review (Override replaces whole objects, unvalidated tag types/addresses, a failed RUN/STOP read is not STOP, slow first TO read, `isConfigured`, older-firmware attribute rejections, reinitialization in the interface guard text, client configuration examples) | tiacommander CHANGELOG and known limitations | niedrig | `overview.md`, `compile.md`; the rest after live checks |

Rows 23–51 come from the [TiaCommander public-docs review](tiacommander-public-docs-review.md), which
maps every TiaCommander action to this fork's status and lists the reported pitfalls (not verified).

Not adopted: headless-vs-window restrictions and the Python router of 1748477582 (this fork keeps
its own process model), tiacommander's encrypted local credential store (credentials stay in
environment variables named by configuration), and multi-version engines (this fork targets V21
only by owner decision).
