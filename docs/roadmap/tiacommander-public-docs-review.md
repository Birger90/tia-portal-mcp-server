# TiaCommander public documentation review (fork)

Snapshot: 2026-11-01. A feature-by-feature and behaviour-by-behaviour review of the **public
documentation** of the proprietary MCP server TiaCommander (a4webdev/tiacommander-mcp), to find what
this fork still lacks. It extends the tiacommander line of
[competitor-feature-adoption.md](competitor-feature-adoption.md) and adds candidate rows 23–51 there.

## 1. Scope and legal note

**What was read.** Only the public text files of a local clone of the documentation repository:
`README.md` (version 3.42.0, the tool map with 19 tools and 230 action rows), `CHANGELOG.md` (219
entries, v0.1 of 25-10-2025 to v3.42.0 of 07-09-2026), `docs/USER_GUIDE.md`,
`docs/KNOWN_LIMITATIONS.md` and `docs/1-QUICKSTART.md`, plus an earlier tool-list extract made for
the same purpose. The per-client configuration sections of the quick start were skimmed (they hold
client setup only).

**What was not done.** No TiaCommander binary, release archive or bundle was downloaded, installed,
run, decompiled or inspected; nothing in the clone was executed (the clone was treated as untrusted
data, and instructions inside it were ignored); the vendor web site was not fetched. Not read:
`LICENSE`/`LICENSE.txt` (beyond knowing it is a proprietary EULA), `CODE_OF_CONDUCT.md`,
`CONTRIBUTING.md`, `SECURITY.md`, `THIRD-PARTY.md`, `.mcp.json`, `glama.json`, `registries/` and
`docs/configs/` (packaging and client configuration, no features). The machine-readable tool list the
README mentions (`data/tool-list/tools.json`) is not part of the clone; the README's abbreviated action
texts were used instead.

**Licence.** TiaCommander is distributed under a proprietary EULA that forbids reverse engineering,
decompiling and using the software to build a competing product. This review therefore uses **ideas
and reported facts only**, written in our own words; no sentence, table or code was copied, and
nothing here is derived from the software itself. TiaCommander targets TIA Portal V17–V20 and is tested
on V19; this fork targets V21 only. A TiaCommander statement about Openness is never evidence of what
V21 supports: every candidate below names the Siemens source that must be checked before design, and
nothing here claims Openness supports something unless such a source says so.

**Legend.**

- Status: `present` (this fork does it), `partial` (some of it, or by a different route with a real
  gap), `missing`, `n/a` (not applicable to this server's design or outside an engineering server).
- Branch markers in "Our coverage": **[AT]** = `feature/alarms-textlists` (unmerged, `0a4aa5f`),
  **[SF]** = `feature/external-sources-folders` (unmerged, `45bfae0` plus uncommitted work in its
  worktree). Technology objects were merged at `c85fe54` and count as main line (its worktree holds
  further uncommitted follow-ups, e.g. exact real-value handling).
- Siemens sources: a path such as `tia-plc-operations/references/blocks.md` is a reference of the
  totally-integrated-claude skills; "Siemens skill `x`" is
  `tia-portal-ai-extensions/openness_development/skills/x/SKILL.md`; "baseline `x.json`" is
  `totally-integrated-claude/api-baselines/v21/x.json`, which lists **type names only** (it proves a
  type exists in V21, not a member or its signature).
- "row N" refers to a candidate row of [competitor-feature-adoption.md](competitor-feature-adoption.md).

## 2. Feature matrix

### 2.1 `admin` (13 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `clear_exports` | purge stored export results older than N hours | n/a | no server-side export store: exports go to caller paths ([SF] manifest, `export_io_map`) | — |
| `delete_export` | remove one stored export | n/a | as above | — |
| `get_device_profiles` | every device the server has ever seen | n/a | usage registry, not engineering data | — |
| `get_export` | page through a stored export (offset/length, raw) | partial | values over the budget are omitted whole (`--max-item-chars`); no chunked read → row 36 | none (host budget) |
| `get_recent_errors` | last failed calls with device context | partial | audit v2 records every entered write call incl. failures; failed reads are not logged; not proposed beyond the server log | — |
| `get_system_info` | OS, .NET, TIA version, uptime | partial | `tia-mcp doctor` (CLI, not an MCP tool) | — |
| `get_stats` | call statistics per tool/action | n/a | usage telemetry; not adopted | — |
| `get_version` | server version, database path/size | partial | MCP `initialize` server info; doctor host/worker version check | — |
| `list_exports` | recent stored exports | n/a | no export store | — |
| `open_file` | open a file with the Windows default application | n/a | desktop action outside an engineering server; the client opens files | — |
| `reset_device_catalog` | rebuild the local hardware catalog cache | n/a | no catalog cache; `search_equipment_catalog` queries TIA Portal live | — |
| `save_export` | write a stored export to a file | n/a | no export store | — |
| `search_device_catalog` | search a cached catalog without TIA Portal | partial | `network_read search_equipment_catalog` needs a running TIA Portal; offline cache not adopted | — |

### 2.2 `alarm_text` (15 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_entries` | add value-range/text entries to a text list | partial | [AT] entries change only as workbook cells (`update_text_list_cells`); typed entries left out (no documented entry type) | `tia-plc-operations/references/alarms.md` |
| `create_textlist` | create a text list, optionally with entries | partial | [AT] only from a master copy (`library_write instantiate_master_copy` target `textLists`); no documented create member | `alarms.md` (`PlcAlarmUserTextlistComposition`) |
| `delete_entries` | delete entries by start value | missing | [AT] left out: import semantics for removed rows undocumented | `alarms.md`; Siemens skill `libraries-and-alarms` |
| `delete_textlist` | delete a user text list | present | [AT] `delete_text_list` | — |
| `export_alarm_classes` | export alarm classes to a file | present | [AT] `export_alarm_classes` | — |
| `export_alarm_data` | alarm texts / text lists / classes as CSV or XLSX | partial | [AT] XLSX through TIA Portal's own export; CSV of alarm data left out ([SF] notes the CSV writer can be reused after merge) | none new |
| `export_alarm_texts` | alarm instance texts to XLSX | present | [AT] `export_alarm_texts` | — |
| `export_textlists` | text lists to XLSX | present | [AT] `export_text_lists` | — |
| `get_entries` | read a text list's entries | present | [AT] `read_text_list_workbook` (cells, paged) | — |
| `import_alarm_classes` | import alarm classes | present | [AT] `import_alarm_classes` (standalone) | — |
| `import_alarm_texts` | import alarm instance texts | present | [AT] `import_alarm_texts` | — |
| `import_textlists` | import text lists from XLSX | present | [AT] `import_text_lists` | — |
| `list_textlists` | list system and user text lists | present | [AT] `list_text_lists` | — |
| `update_entries` | change entries by start value | present | [AT] `update_text_list_cells` (hash-guarded cell edits) | — |
| `update_textlist_comment` | change a text list comment | present | [AT] `update_text_list` (`comment` + culture) | — |

### 2.3 `blocks_read` (17 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `check_consistency` | consistency flag of the blocks | missing | `IsConsistent` is read for watch/force tables only → row 30 | `tia-plc-operations/references/blocks.md` (`IsConsistent`); Siemens skill `change-detection` |
| `compile_all` | compile a PLC | present | `compile_check` (`plcName`) | — |
| `compile_block` | compile one block | present | `compile_check` (`blockPath`) | — |
| `export_all_xml` | every block as XML files, folders mirrored | present | [SF] `plc_write export_blocks` (manifest, `maxElements`) | — |
| `export_source` | SCL/STL source inline or to a directory | present | `plc_read get_block_content format:source` (inline); [SF] `export_source_file` | — |
| `export_xml_file` | block XML inline or to a file | present | `get_block_content` (xml, inline); [SF] `export_blocks` | — |
| `export_xml_inline` | parsed summary of the XML | partial | `get_block_interface` (typed interface tree); no network-level summary → row 1 | `tia-import-export/references/plc-blocks.md` |
| `get_all_interfaces_summary` | member counts per section for all blocks | missing | not proposed: composable from `get_block_interface` at one export per block | — |
| `get_compiler_errors` | errors only | partial | `compile_check` returns all rows with severity; no errors-only/leaf view → row 23 | `tia-project-general/references/compile.md` §3 |
| `get_compiler_messages` | full message tree | partial | flattened parent-before-child rows without depth or leaf marker → row 23; reading the last result without recompiling has no documented member | `compile.md` §3 |
| `get_details` | type, number, language, interface size, consistency | partial | `browse_project_tree` header details, `get_block_interface` identity (type, number, language, know-how); consistency missing → row 30 | `blocks.md` |
| `get_edit_capabilities` | per network: language and which edit routes apply | missing | → row 1 | SimaticML schema in installed `PublicAPI\V21\Schemas`; `plc-blocks.md` |
| `get_element_pins` | pins of a call/instruction with expected types and wiring | missing | → row 1 | as row 1 |
| `get_interface` | full interface | present | `get_block_interface` | — |
| `get_network` | a LAD network as a structured rung model | missing | → row 1 | as row 1 |
| `get_xml_raw` | raw XML | present | `get_block_content` xml | — |
| `list` | block table with filters | present | `browse_project_tree`; [SF] `find_objects` (kind `block`) | — |

### 2.4 `blocks_write` (26 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_interface_member` | add a member with impact report | present | `plc_write add_interface_member`; guard `plc_changes_interface`; `analyze_change_impact` | — |
| `add_multi_instance_member` | add an FB-typed static member | partial | `add_interface_member` with an FB data type; whether the import builds the multi-instance structure is unverified (live check 2 of the PLC reference) | `plc-blocks.md`; SimaticML multi-instance `Sections` in the installed schemas |
| `add_network` | insert an empty network | missing | → row 1 | as row 1 |
| `copy_block` | duplicate under a new name | present | `copy_block` (transaction, `targetGroupPath`, `number`) | — |
| `create_block` | create a block with interface and logic in one call | partial | `create_block` makes an empty block; content needs a read and `update_block_logic` → row 27 | `plc-blocks.md` §1–§4 (`Import`) |
| `create_fb` | new FB | present | `create_block` FB (LAD, FBD, STL, SCL, GRAPH) | — |
| `create_fc` | new FC | present | `create_block` FC; return type through `update_interface_member` (`Return`) | — |
| `create_ob` | new OB | present | `create_block` OB with `obEventClass` (15 classes live-accepted) | — |
| `delete_block` | delete a block (instance DBs with force) | present | `delete_block` (info guard) | — |
| `delete_interface_member` | remove a member | present | `delete_interface_member` | — |
| `delete_network` | remove a network | missing | → row 1 | as row 1 |
| `delete_rung` | remove a rung | missing | → row 1 | as row 1 |
| `delete_scl_statement` | remove one SCL statement | missing | → row 1 | as row 1 |
| `import_xml_file` | import a block from a file | partial | no file input for block content; [SF] `import_external_source` + `generate_blocks_from_source` for sources → rows 15, 27 | `plc-blocks.md`, `tia-plc-operations/references/external-sources.md` |
| `import_xml_inline` | import a block from inline XML (create or overwrite) | partial | `update_block_logic` overwrites existing blocks only; new-object import → row 27 | `plc-blocks.md` §1 |
| `insert_rung` | insert one rung | missing | → row 1 | as row 1 |
| `move_block` | move to another folder | present | `move_block` (transaction, recovery folder) | — |
| `populate_network` | fill a network with rungs | missing | → row 1 | as row 1 |
| `remove_multi_instance_member` | remove an FB-typed member | present | `delete_interface_member` | — |
| `rename_block` | rename/renumber | present | `rename_block`, `renumber_block`; reference consequences → row 34 | — |
| `replace_network` | replace a network's title/comment | missing | → row 1 | as row 1 |
| `split_network` | split an SCL network at statements | missing | → row 1 | as row 1 |
| `update_interface_member` | change or rename a member | present | `update_interface_member` | — |
| `update_network` | network title/comment | missing | → row 1 | as row 1 |
| `update_network_element` | retarget a call, swap instance DB, rewire pins | missing | → row 1 | as row 1 |
| `update_rung` | replace one rung | missing | → row 1 | as row 1 |

### 2.5 `db` (11 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_member` | add a member to a global DB | present | `add_interface_member` (global DB section) | — |
| `create` | new global DB (number, layout, author, folder) | partial | `create_block` GlobalDB into a group; number via `renumber_block`; memory layout and header not settable → row 29 | `blocks.md` (`MemoryLayout`); `plc-blocks.md` §1 |
| `create_instance_db` | instance DB of an FB | present | `create_instance_db` | — |
| `delete` | delete a DB | present | `delete_block` | — |
| `delete_member` | remove a member | present | `delete_interface_member` | — |
| `get_member` | read a member at any depth | present | `get_db_values` (`memberPath`) | — |
| `get_structure` | member list with types and start values | present | `get_block_interface`, `get_db_values withAttributes` | — |
| `list` | global and instance DBs with paths | present | `browse_project_tree`; [SF] `find_objects` | — |
| `set_start_value` | start values at any depth, several at once | present | `update_db_member` (one member per item, up to 50 items) | — |
| `update_member` | type, start value, comment | present | `update_interface_member` | — |
| `update_member_comment` | comment only | present | `update_interface_member` (`comment`, `commentCulture`) | — |

### 2.6 `diagnostics` (7 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `compare_online_offline` | per-object online/offline compare | present | `plc_read compare_to_online` | — |
| `configure_connection` | set up the download connection, optionally to another IP | partial | `plc_tia_online go_online` applies the exact configured path; a target IP other than the project's → row 43 | `tia-networks/references/online-connection-configuration.md`; `tia-plc-operations/references/download-upload.md` |
| `get_device_ip` | project-configured IP of a device | present | `network_read read_hardware_config` / `inspect_network_object` | — |
| `get_plc_status` | connection state plus live RUN/STOP | partial | `get_online_state` (connection state, `isConfigured`) and `plc_online_read read_operating_mode` (S7-1500 Web API) are separate; no S7-1200 operating mode | `tia-plc-operations/references/online-status.md` |
| `go_offline` | disconnect | present | `plc_tia_online go_offline` | — |
| `go_online` | connect | present | `plc_tia_online go_online` | — |
| `scan_devices` | accessible-device scan, annotated in/not in project | partial | [SF] `scan_accessible_devices` (explicit PC interface); no project annotation → row 44 | `tia-networks/references/online-connection-configuration.md` |

### 2.7 `download_upload` (4 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `download_check` | silent pre-flight with a verdict, including compile | partial | `plc_download dryRun:true` resolves path and guards; no compile or online-state check → row 25 | `download-upload.md` ("Before a download", step 3) |
| `download_to_device` | download with scope, stop/start and reinit consent | present | `plc_download` (scopes `hardware_and_software`/`software`/`software_only_changes`, `startAfterDownload`, caller `decisions`); hardware-only scope and an explicit IP not offered (row 43) | — |
| `upload_check` | upload pre-flight | partial | `list_station_upload_interfaces` and `plc_station_upload dryRun:true` | `download-upload.md` |
| `upload_station` | upload a station into the project | present | `plc_station_upload` | — |

### 2.8 `folders` (5 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `create_folder` | create nested groups in a container | present | `create_block_group`, `create_watch_table_group`, `create_technology_object_group`; [SF] `create_type_group`, `create_tag_table_group`; external-source groups left out by [SF] (documented as unsupported) | — |
| `delete_folder` | delete a group, contents only with consent | present | the matching delete operations with guard `plc_deletes_group_contents` | — |
| `find` | glob search across a container | present | [SF] `find_objects` (kinds, paging) | — |
| `get_tree` | one-call tree with counts | partial | `browse_project_tree` (paged snapshot) without item counts or empty markers → row 45 | none (host) |
| `rename_folder` | rename a group | present | `rename_technology_object_group`; [SF] block, type, tag-table and watch-table group renames (writability checked at runtime) | — |

### 2.9 `get_info` and `open_manager` (standalone)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `get_info` | capabilities, version, installed TIA versions, client count | partial | `get_project_status`, doctor; no workflow hint → row 14 | — |
| `open_manager` | open the vendor GUI | n/a | no GUI | — |

### 2.10 `hardware` (14 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `compile_all` | compile all hardware | missing | `compile_check` compiles PLC software only → row 32 | `compile.md` §1 (`Device`, `DeviceItem`) |
| `compile_device` | compile one device | missing | → row 32 | `compile.md` §1 |
| `dump_catalog` | copy the catalog into a local database | n/a | no local cache | — |
| `export_csv` | device list to CSV | missing | → row 50 | none (host) |
| `export_hardware_map` | AML/CAx data of a device as a flat table | missing | → row 50 | `tia-import-export/references/hardware-aml.md` (`CaxProvider`); baseline `step7.json` (`Siemens.Engineering.Cax.CaxProvider`) |
| `export_io_map` | I/O map with tag assignments, CSV or XLSX | present | `network_write export_io_map` (CSV); XLSX left out (owner decision on an OOXML writer) | — |
| `export_xlsx` | device list to Excel | missing | XLSX left out (owner decision) | — |
| `get_device` | one device's configuration | present | `read_hardware_config` (`deviceName`) | — |
| `get_full_config` | all devices | present | `read_hardware_config` (paged) | — |
| `get_io_map` | I/O address map | present | `read_hardware_config includeIoDetails/includeTagMatches`, `list_io_addresses` | — |
| `get_network` | subnets, nodes, IO systems | present | `list_network_objects`, `inspect_network_object` | — |
| `get_rack_slot_details` | rack/slot topology with addresses | present | `read_hardware_config`, `list_plug_locations` | — |
| `search_catalog` | live catalog search | present | `search_equipment_catalog` | — |
| `set_network_config` | IP, mask, router, IP mode, ISO, PN name, station name | partial | `configure_network_device` (IP, mask, PN name, auto-generation, subnet, IO system); router, IP protocol selection, ISO and station rename missing → row 31 | `tia-networks/references/subnets-and-nodes.md` (Ethernet node attributes); `tia-devices-general/references/device-attributes.md` |

### 2.11 `library` (29 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_to_multi_instance_fb` | add a library type as a multi-instance member | missing | left out in the library reference (interface editing, not a library API); see `add_multi_instance_member` | as `add_multi_instance_member` |
| `archive_global_library` | archive a global library | missing | → row 3 | `tia-project-general/references/library-operations.md`; baseline `base.json` (`LibraryArchivationMode`) |
| `close_global_library` | close | present | `library_write close_global_library` (refused with this server's unsaved changes) | — |
| `compare_to_target` | compare two libraries | present | `library_read compare_libraries` (schema-free tree) | — |
| `create_global_library` | create | present | `create_global_library` | — |
| `create_library_folder` | create a type or master-copy folder | missing | → row 3 | `library-operations.md`; Siemens skill `global-library` |
| `delete_library_folder` | delete a folder | missing | → row 3 | as above |
| `delete_master_copy` | delete | present | `delete_master_copy` | — |
| `delete_unused_types` | clean up unused types with preview | missing | → row 3 | `library-operations.md` (`CleanUpMode`); baseline `base.json` |
| `export_type_xml` | export a type version | missing | → row 3 | `library-operations.md` |
| `find` | glob search in a library | missing | left out: client-side over `get_library_tree`; not proposed | — |
| `get_info` | edit mode, version, counts, root folders | partial | `list_libraries`, `list_global_library_infos` | Siemens skill `global-library` |
| `get_master_copy` | master copy metadata and content manifest | partial | `get_library_tree` root master copies with `contentTypes`; master copies below the root folder → row 3 | Siemens skill `change-detection` (`MasterCopy.ContentDescriptions`) |
| `get_type` | type metadata, versions, optional XML | present | `get_type`; XML body → row 3 (type export) | — |
| `get_tree` | recursive tree | present | `get_library_tree` (paged) | — |
| `instantiate_library_type` | instantiate a type version | present | `instantiate_type_version` | — |
| `instantiate_master_copy` | instantiate a master copy | present | `instantiate_master_copy` (PLC groups, TOs, units, devices) | — |
| `list_libraries` | project and open global libraries | present | `list_libraries` | — |
| `open_global_library` | open, or list a root folder | present | `open_global_library`; listing a root folder n/a (explicit paths) | — |
| `promote_to_global` | copy types to a global library | missing | → row 3 (`UpdateLibrary`); master copies: `copy_master_copy` | `library-operations.md` |
| `publish_block_as_master_copy` | block as master copy | present | `create_master_copy` (source kind `block`) | — |
| `publish_plc_type_as_master_copy` | UDT as master copy | present | `create_master_copy` (source kind `plcType`) | — |
| `reload_global_library` | re-read a library changed outside the session | partial | close + open as two items (unsaved-change guard); no reload member documented | `library-operations.md` |
| `rename_library_folder` | rename a type or master-copy folder | partial | `rename_type_folder`; master-copy folders → row 3 | `library-operations.md` |
| `rename_master_copy` | rename | present | `rename_master_copy` | — |
| `rename_type` | rename | present | `rename_type` | — |
| `save_global_library` | save | present | `save_global_library` | — |
| `update_check` | preview of a project update | present | `update_check` | — |
| `update_project` | apply type updates | present | `update_project` (default mode; mode overloads → row 3) | — |

### 2.12 `live_data` (11 actions)

TiaCommander reads runtime data through a helper process that speaks the S7 protocol itself (a
third-party S7CommPlus driver, TLS or plaintext, S7-1200 and S7-1500). This fork uses Siemens' official
S7-1500 Web API client instead (owner decision); that difference is not adopted.

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `connect` | attach to a CPU by IP, optional password | partial | `plc_online_read`/`plc_online_write` sessions per configured target (S7-1500, PLCSIM Advanced with web server); no S7-1200 path | — (no Siemens runtime-data source for S7-1200 among the references) |
| `delete_credential` | remove a stored password | n/a | credentials are environment variables named in the target file (owner rule) | — |
| `disconnect` | release the session | n/a | sessions are managed per target, logout on shutdown | — |
| `get_status` | readiness, connection, identity | present | `ping`, `version`, `permissions`, `read_cpu_type` | — |
| `list_credentials` | stored passwords (never the secret) | n/a | as `delete_credential` | — |
| `list_readable` | what the CPU exposes, per root | present | `browse_tags` | — |
| `read_db` | live values of a DB with quality codes | partial | `browse_tags` + `read_tags` (up to 100 names per item); not proposed separately (composable) | — |
| `read_diagnostics` | identity, protection, alarms, diagnostic buffer, operating state | present | `read_cpu_type`, `permissions`, alarms, diagnostic buffer, `read_operating_mode`, runtime information | — |
| `read_tag_table` | live values of a project tag table, refused on CPU mismatch | partial | `read_watch_table_live` (`tagTable`); no project/CPU identity check → row 37 | Web API client `Plc.ReadCpuType` (PLC online reference provenance); `device-attributes.md` |
| `read_watch_table` | symbols or a project watch table, live | present | `read_watch_table_live` (`table`), `read_tags` | — |
| `set_credential` | store a password encrypted | n/a | not adopted (environment variables only) | — |

### 2.13 `session` (16 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `archive` | archive with modes, timestamp, save first | present | `archive_project` (four modes, `saveBeforeArchive`); archive file not verified → row 33 | `tia-project-general/references/project-lifecycle.md` |
| `close_project` | close with prompt/save/discard/auto | present | `close_project` (`saveBeforeClose`; discard is an acknowledge guard) | — |
| `configure` | default roots, HTTP listener, TIA version binding | n/a | explicit absolute paths, stdio only, V21 only | — |
| `connect` | attach to a running TIA Portal, load the first project | present | `bind_project` (never picks the first project) | — |
| `create` | create a project, launching TIA if needed | present | `create_project` | — |
| `disconnect` | release the handle, optionally terminate TIA | n/a | the server never terminates TIA Portal | — |
| `get_project` | name, path, version, device count | present | `get_project_status` | — |
| `get_state` | readiness level and guidance | present | `get_project_status`, `binding_conflict`, doctor | — |
| `info` | version, tool count, workflow hint | partial | MCP server info; workflow hint → row 14 | — |
| `is_dirty` | unsaved changes | present | `get_project_status` `isModified` | — |
| `launch` | start TIA Portal without a project | missing | → row 21 | as row 21 |
| `list_archives` | list `.zap*` files under a root | missing | not proposed: a file listing, no Openness call | — |
| `list_devices` | devices of the project | present | `read_hardware_config`, `browse_project_tree` | — |
| `open` | open a project, or list a root folder | present | `open_project` (explicit path) | — |
| `save` | save, refused while online | present | `save_project`; online check → row 24 | — |
| `save_as` | save a copy | present | `save_project_as` | — |

### 2.14 `tag` (16 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_tag` | add a tag with address and comment | partial | `create_tag` (no comment, no external-access flags) → row 26 | `tia-plc-operations/references/tags-types.md` (`PlcTag.Comment`); `tia-project-general/references/language-settings.md` §5 |
| `create_table` | new table in a folder | present | `create_tag_table` (`folderPath`) | — |
| `delete_table` | delete a table and its tags | present | `delete_tag_table` (info guard) | — |
| `delete_tag` | delete, refused when comment/flags would be lost | present | `delete_tag`; loss guard → row 26 | — |
| `export_tag_table_data` | CSV/XLSX/XML export of a table | partial | [SF] `export_tag_tables_csv`; table SimaticML read → row 27 | `tags-types.md` (`PlcTagTable.Export`) |
| `find_next_free` | next free address, declared or used | partial | `find_free_address` (declared tags only); "used in code" → row 39 | `blocks.md`/`tags-types.md` cross references |
| `get_assignment_list` | I/Q/M occupancy, declared/used/conflicts | partial | `read_address_occupancy` (declared tags, hardware, overlaps) → row 39 | as above |
| `get_table_details` | tags with types, addresses, comments | partial | `list_tag_tables` (no comments); [SF] `read_tag_comments` | — |
| `import_table` | import a table from XML, with before/after counts | missing | → row 27 | `tags-types.md` (`PlcTagTableComposition.Import`) |
| `list_tables` | tables with paths and counts | present | `list_tag_tables` | — |
| `move` | move one tag keeping comment and flags | present | `move_tag` (transaction) | — |
| `move_table` | move a table between folders | present | `move_tag_table` (transaction) | — |
| `rename` | always refused (V19 has no tag rename) | present | `update_tag newName` (V21: `Name` writable from V20, read back); reference consequences → row 34 | — |
| `search` | substring search across tables | present | [SF] `find_objects` (kind `tag`, glob); main line: `list_tag_tables` | — |
| `set_access` | the three external-access flags | present | `update_tag` | — |
| `update_comment` | change a tag comment | missing | → row 26 | `tags-types.md`; `language-settings.md` §5 |

### 2.15 `technology_objects` (15 action rows; the README row for `import` is empty)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `capabilities` | documented family/version/firmware matrix | missing | left out: no source provides such a matrix | Siemens skill `technology-objects` (states none exists) |
| `compile` | compile a TO, versions before/after | missing | left out: no TO compile member; `compile_check` compiles the PLC; version-change check → row 41 | `tia-plc-operations/references/technological-objects.md` |
| `connect_hardware` | rewire a channel | present | `connect_technology_object_hardware` | — |
| `create` | create with fixed family/version | present | `create_technology_object` | — |
| `create_from_master_copy` | create from a master copy | present | `library_write instantiate_master_copy` (`technologyObjects`, root group) | — |
| `delete` | delete | present | `delete_technology_object` | — |
| `export` | SimaticML export | present | `export_technology_object`, `get_technology_object_content` | — |
| `find_parameter` | one parameter with type and access | present | `get_technology_object_parameters parameterName` | — |
| `get_hardware_connection` | wiring report | present | `get_technology_object_hardware_connection` | — |
| `get_parameters` | typed parameters, prefix filter, summary | present | `get_technology_object_parameters` (`prefix`, paging) | — |
| `import` | SimaticML import | present | `import_technology_object`, `update_technology_object_content` | — |
| `list` | objects with type, version, parent | present | `list_technology_objects` | — |
| `rename` | rename (references not updated) | present | `rename_technology_object`; reference consequences → row 34 | — |
| `set_parameter` | write one parameter with expected current value | present | `set_technology_object_parameter` (exact type, read-back); optimistic check → row 49 | — |
| `show_in_editor` | open the object in the TIA editor | missing | left out (UI action, not documented for TOs); not proposed | `ShowInEditor` is documented for blocks, types, tag and watch tables only (`blocks.md`, `tags-types.md`, `watch-force-tables.md`) |

### 2.16 `udt` (11 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_member` | add a member | present | `add_interface_member` (`typePath`) | — |
| `create` | new UDT, optionally with members and folder | partial | main line: none; [SF] `.udt` file through `import_external_source` + `generate_blocks_from_source` → row 27 | `tags-types.md` (`PlcTypeComposition.Import`); `external-sources.md` |
| `delete` | delete, refused while referenced | missing | → row 28 | `tags-types.md` (`PlcType.Delete`) |
| `delete_member` | remove a member | present | `delete_interface_member` | — |
| `export_xml` | export | present | `get_type_content` (xml or source) | — |
| `get_structure` | members with types, comments, flags | present | `get_type_interface` | — |
| `import_xml` | import (create or overwrite) | partial | `update_type_content` overwrites existing types only → row 27 | `tags-types.md` |
| `list` | UDTs with paths and dates | present | `browse_project_tree`; [SF] `find_objects` (kind `type`) | — |
| `move` | move between folders | missing | → row 28 | `tags-types.md` (`Export`, `Import`, `Delete`) |
| `update_member` | type, initial value, comment | present | `update_interface_member` | — |
| `update_member_comment` | comment only | present | `update_interface_member` | — |

### 2.17 `watch` (14 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `add_entry` | add an entry with display format and values | partial | `update_watch_table_content` / `update_force_table_content` (whole SimaticML document) → row 38 | `tia-plc-operations/references/watch-force-tables.md` (entry members, `PlcWatchAndForceTableDisplayFormat`) |
| `clear_table` | remove all entries | partial | as `add_entry` | as above |
| `create_table` | new watch table in a folder | present | `create_watch_table` | — |
| `delete_entry` | remove an entry by address or symbol | partial | as `add_entry` | as above |
| `delete_table` | delete | present | `delete_watch_table` | — |
| `export_force_data` | force table to CSV/XLSX | present | [SF] `export_force_tables_csv` (CSV; XLSX left out) | — |
| `export_table` | table XML to a file | partial | `get_watch_table_content`/`get_force_table_content` (inline) → row 38 | `watch-force-tables.md` (`Export`) |
| `export_watch_data` | watch table to CSV/XLSX | present | [SF] `export_watch_tables_csv` | — |
| `get_entries` | list entries | present | `read_watch_table`, `read_force_table` | — |
| `import_table` | import from XML (create or overwrite) | partial | overwrite through the content updates; new-table import → row 27 | `watch-force-tables.md` (`Import`) |
| `list_tables` | watch and force tables with counts | present | `list_watch_and_force_tables` | — |
| `move_table` | move between folders | missing | → row 38 | `watch-force-tables.md` (`Export`, `Import`, `Delete`) |
| `rename_table` | rename | missing | → row 38 | as above |
| `update_entry` | change an entry | partial | as `add_entry` | as `add_entry` |

### 2.18 `xref` (6 actions)

| TiaCommander action | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `find_call_paths` | call paths from OBs to a block | present | `plc_read find_call_paths` | — |
| `find_callers` | who references a block or a tag | present | `read_cross_references` (blocks, tags) | — |
| `find_orphaned_instance_dbs` | instance DBs without a valid FB | present | `find_orphaned_instance_dbs` | — |
| `find_unused` | blocks not reached from an OB | present | `find_unused_blocks` | — |
| `get_call_tree` | forward call tree | present | `get_call_tree` | — |
| `get_references` | cross references with filter | present | `read_cross_references` (`filter`) | — |

### 2.19 Surface named outside the tool map

| Item | Idea | Status | Our coverage | Siemens source to check |
| --- | --- | --- | --- | --- |
| `admin get_activity_log` | timeline of every call (hidden by default since v3.10) | partial | audit v2 for entered write calls; changes made in the TIA UI are invisible to both → row 30 | Siemens skill `change-detection` |
| `diagnostics setup_diag_read` / `snapshot_read` | GET_DIAG collector block plus manual snapshot (retired) | n/a | runtime diagnostics come from the Web API | — |
| `download_to_device connectionVariant` | choose how the connection is established (diagnostic) | n/a | the exact configured path is used, no fallback | — |
| `session configure preferredTiaBinding` | pick one of several installed TIA versions | n/a | V21 only | — |

### 2.20 Notable features from README, user guide, quick start and changelog

| Feature | Status | Our coverage / reason | Siemens source to check |
| --- | --- | --- | --- |
| Grouped meta-tools with an `action` discriminator (19 tools) | n/a | 29 tools with typed operation batches; different design | — |
| LAD/FBD block creation from a JSON rung model with a validated instruction set and JSON-path errors | missing | → row 1 | as row 1 |
| Instruction catalogue in the tool description | missing | → rows 1, 14 | — |
| Auto-compile after create/import (`autoCompile`) | partial | `update_block_logic` compiles the affected scope; `create_block` and other writes never compile; `compile_check` separately | — |
| Lowest free block number on create | present | TIA Portal assigns; OB numbers chosen by the server | — |
| Transactional overwrite: export before import, restore when it fails | partial | moves/copies are transactional with a recovery folder; content updates are not → row 35 | `tia-csharp-common/SKILL.md` (transactions); `tia-import-export/references/overview.md` |
| Impact report (callers, instance DBs) before an interface edit | present | `plc_changes_interface` info guard, `analyze_change_impact` (no confirmation gate by design) | — |
| Three input and output modes (file, inline, export reference), export store with paging and 24 h expiry | partial | inline content plus selected file operations; no store; rows 15, 27, 36 | — |
| Configured default roots for projects, archives, exports, libraries | n/a | explicit absolute paths | — |
| Manager GUI (terminal, client config, paths, credentials, help) | n/a | CLI `tia-mcp install`/`doctor` | — |
| Self-update with backup and rollback | n/a | `dotnet tool update` | — |
| Licence registration with offline grace | n/a | — | — |
| Telemetry of errors, usage, AI client and device types | n/a | not adopted | — |
| Several TIA versions with a capability probe of the API surface | partial | V21 only; a probe of runtime-bound members → row 40 | — |
| Degraded mode distinguishing "TIA missing" from "Openness missing" | present | doctor checks (installation, assemblies, group) | — |
| Openness group check at start-up; "Siemens TIA Engineer" group requirement | partial | doctor checks `Siemens TIA Openness`; no Siemens source among our references mentions a "TIA Engineer" group, so it is not adopted | — |
| Several AI clients sharing one server and one TIA Portal (takeover, busy timeout, confirmation phrase while shared) | n/a | one server per client; binding identity checks; see pitfall 59 | — |
| Live data through a third-party S7 protocol driver (S7-1200 and S7-1500) | partial | S7-1500 Web API (official Siemens client); third-party protocol not adopted | — |
| Encrypted credential store per project or global | n/a | not adopted (environment variables named by configuration) | — |
| Confirmation strings for download, upload and type cleanup | present | access mode plus elicitation and acknowledge guards | — |
| MCP annotations, titles, server metadata, cancellation, ping | partial | annotations, read cancellation; no human-readable tool titles → row 46 | — |
| Unknown-argument notes and lenient type coercion | present | stricter: unknown or mistyped arguments are rejected, naming the field and the valid ones | — |
| Short device names, ambiguity lists, "did you mean", wrong-kind detection | partial | PLC or device name accepted, ambiguity lists candidates; no suggestions on not-found → row 48 | — |
| Folder-qualified names everywhere | present | paths everywhere | — |
| Uniform item envelope, reconciling totals, empty-folder markers, loud truncation notices | partial | structured JSON with `complete`/omission; no counts or markers → row 45 | — |
| Project shape summary on connect/open | missing | → row 45 | — |
| State levels with graceful guidance before a project is open | present | `get_project_status`, `binding_conflict` | — |
| Download/upload pre-flight verdicts | partial | dry runs → row 25 | `download-upload.md` |
| Automatic PC-interface selection preferring physical adapters | n/a | the exact interface is required | — |
| File-name convention and duplicate suffixes for exports | n/a | caller paths, no silent overwrite | — |
| XLSX exports | missing | owner decision on an OOXML writer ([SF] left out) | — |
| Shipped block/text-list templates | n/a | `BlockSourceGenerator`; text lists from master copies [AT] | — |
| One-click client bundle and configurations for eight clients; Copilot agent-mode hint | partial | `tia-mcp install` for Claude Code, Codex, OpenCode, MiMoCode → row 51 | — |
| Per-process logs, retention, debug switch, secret masking | partial | server log and redaction of secrets; retention not compared | — |
| Three-tier error classification and exception type names | present | failure categories decided by exception type, `engineeringError.type` | — |
| Update notification injected as an instruction to the AI agent | n/a | not adopted: a server should not steer the agent's behaviour | — |
| Roadmap items "HMI, PLCSIM, wider live data" | present | this fork already has WinCC Unified reads/writes, S7-PLCSIM Advanced and the Web API | — |

## 3. Reported TIA Portal / Openness behaviour and pitfalls

Every row is **reported by TiaCommander, not verified** here, and was observed on V17–V20 (mostly
V19); several are TiaCommander's own defects that still point at a trap. "Our handling" was checked
against this fork's code and docs.

| # | Reported behaviour (reported by TiaCommander, not verified) | Reported in | Our current handling | Proposal |
| --- | --- | --- | --- | --- |
| 1 | The compiler result is a tree: an error state is propagated to every ancestor (device, folder, summary) whose text is empty, so naive flattening shows about twice as many "error" rows as real diagnostics | CHANGELOG v3.42.0 | `CompileReportProjection.Flatten` keeps every node parent-before-child with its own severity, no depth or leaf marker; the 200-row cap counts container rows too; totals are Siemens' own counts | Row 23: `depth`, `hasChildren`/leaf flag, an errors-only leaf view, cap leaf rows first; doc note "count with `errorCount`"; live check on a project with nested errors (`compile.md` §3) |
| 2 | A warning-only compile was reported as a failure | v3.29.0 | warning-only passes (`PROJECT_OPERATIONS_SUMMARY.md`) | none |
| 3 | Compiling can silently lower a technology object's version even when it was not touched | v3.40.0 | not detected; `compile_check` and the implicit compile of `update_block_logic` do not look at TOs | Row 41: compare `systemLibVersion` before/after, warn; live check |
| 4 | The consistency flag can lag behind a compile | tool map (`check_consistency`) | consistency not exposed for blocks | Row 30: expose it as a hint with a fresh read after compiling; prefer the PLC checksum for "compiled since last change" (Siemens skill `change-detection`) |
| 5 | Exports and interface reads of inconsistent blocks/UDTs fail or are unreliable; a UDT created, imported or moved must be compiled before it can be exported | tool map, v2.51.1 | an export failure is a plain `worker_operation_failed` | Row 30: on an export failure read `IsConsistent` (never the message) and add a "compile first" hint only when it is false; live check |
| 6 | Several consecutive DB member edits can leave the DB export-inconsistent; TiaCommander compiles and retries once | v2.39.0, v2.57.0 | `update_db_member` verifies with `get_db_values`, which exports the documents | Live check; if confirmed, compile the DB scope before the verification read or report `notVerified` with the reason, never a silent retry |
| 7 | An import with Override replaces the whole object: anything absent from the document is reset to defaults | v3.39.2 | content writes need a full document read with its hash; interface edits patch the export; Siemens' `overview.md` describes Override as delete and recreate | Row 51: say so in the descriptions of `update_block_logic`, `update_type_content`, the watch/force content updates and `update_technology_object_content` |
| 8 | Overwrites lost block number, memory layout, author, title/comment, header family/name and attributes such as the IEC check, and dropped the folder placement | v3.38.5–v3.39.1, v2.21.0 | the xml route round-trips the full export; the source route regenerates from source text, whose effect on number and attributes is unverified | Live check of `format:source` updates; compare number, memory layout, header fields and group before/after and warn on any change |
| 9 | An SCL block created into a folder lands at the root and must be moved, which needs a consistent project | v3.23.0 | source updates use `GenerateBlocksFromSource(userGroup, …)`; `create_block` creates in the requested group | covered by the existing live checks |
| 10 | An OB requested as a cyclic interrupt came back as a program cycle OB | v3.38.9 | all 15 event classes live-accepted on V21 (2026-10-09) | none |
| 11 | Openness cannot create STL blocks | v2.45.2 | contradicted on V21: STL FB/FC/OB creation was live-verified | none (V19 statement) |
| 12 | A PLC tag cannot be renamed (V19); delete and re-add leaves every reference on the old name | KNOWN_LIMITATIONS, v3.17.0 | `update_tag newName` (V21 `Name` writable from V20, `tags-types.md`), rename read back | Row 34: live check whether code references follow a native rename (compile plus `read_cross_references` on both names); doc note |
| 13 | Group names are read-only except for TO groups | tool map, v3.22.0 | [SF] renames with a `GetAttributeInfos` writability check (tag-table groups writable from V20 per `tags-types.md`) | covered by [SF] live check 6 |
| 14 | There is no native move; export, delete and import with restore on failure | KNOWN_LIMITATIONS, v2.50.0 | `move_block`, `move_tag_table`: transaction plus recovery folder | UDT and watch-table moves (rows 28, 38) must reuse `PlcRoundTrip` and `PlcRecoveryStore` |
| 15 | A watch-table rename by round trip destroyed the table when TIA rejected the new name, and the advertised backup had already been deleted | v3.17.1 | no watch-table rename | Row 38: only through the transaction and recovery store; never delete before the import is proven |
| 16 | Table names may contain slash, angle brackets, a trailing dot and long text; a double quote is rejected | v3.17.1 | table names are not pre-validated beyond selector rules | Live check; keep TIA Portal as the judge, document what `/` does to path selectors |
| 17 | Watch-table entries for a tag-owned address are stored under the tag name, so matching by absolute address fails | v3.15.0 | the SimaticML round trip is unaffected; `read_watch_table_live` reports absolute-address entries as unmapped | Doc note in the PLC online reference; row 38 entry edits must match by name and address |
| 18 | Overwriting a watch table inside a folder created a second table at the root | v3.38.4 | the content update imports into the table's own group | Live check that Override replaces in place |
| 19 | A tag's data type and address are stored as given and not validated; the compiler reports bad values | v3.19.3 | `create_tag`/`update_tag` pass text through without compiling | Row 51: doc note and the recommendation to run `compile_check` |
| 20 | Deleting a tag loses its comment and flags; delete plus add is not a rename | v3.16.0 | `delete_tag` has no loss guard; `move_tag` keeps comment and flags | Row 26: info guard naming what a delete loses |
| 21 | A brand-new UDT cannot be imported into a folder (V19); one created from a library type or master copy lands in its folder | KNOWN_LIMITATIONS, v2.48.0 | no UDT create on the main line; library instantiation targets a type group | Row 27: live check `PlcTypeComposition.Import` into a user group |
| 22 | An empty STRUCT is rejected, so a new UDT needs a placeholder member | tool map (`udt create`) | n/a until a UDT create exists | Row 27 design note |
| 23 | A member named like a reserved word (e.g. `name`) is lost unless quoted in source | v3.26.0 | the xml route is unaffected; source text is the caller's | Row 51: note for `format:source` |
| 24 | `Constant` members must not carry remanence; some sections do not exist on some block types | v3.18.1, v3.18.2 | edits only target sections present in the export and emit no remanence attribute | none |
| 25 | A library type that wraps a UDT instantiates only into PLC data types | v3.21.0 | `instantiate_type_version` requires a type group for PLC data type versions | none |
| 26 | Renaming a master copy keeps the wrapped object's name | v3.22.0 | documented in the library reference | none |
| 27 | A name containing `/` or `\` cannot be addressed afterwards | v3.22.0 | library object names and [SF] group names refuse `/` (library also `\` for file names) | none |
| 28 | Master copies have no export (V19) | v3.21.0 | no master-copy content export; `contentTypes` only | none |
| 29 | While a device is online, save is refused, block create/import fail late, and exports fail with a misleading parse error | v2.33.0, v2.36.0, v3.30.0, v3.38.1 | no online-state check before save, compile, imports or exports; `compile.md` says every device must be offline before compiling | Row 24: plan-time `OnlineProvider.State` read; block or info guard with a "go offline" hint; live check which V21 operations refuse online |
| 30 | A programmatically created connection address is not always registered; `IsConfigured` can be true with no address; the configuration may not survive an online/offline cycle | v2.36.0, v2.38.0 | `plc_station_upload` uses `Addresses.Create` without re-reading it; `get_online_state` reports `isConfigured` | Row 42: re-find the created address and report it; doc note that `isConfigured` is not readiness |
| 31 | Downloads need explicit consent to stop the CPU and to reinitialize data blocks, and some prompts cannot be answered | v2.31.0, v2.32.0, v2.39.0 | callback decisions, `download_stops_plc` acknowledge guard, abort with `availableSelections` | none |
| 32 | A download should be preceded by a compile; the download's own compile gives poor diagnostics | v2.33.0 | no compile before `plc_download`; `download-upload.md` also recommends compiling first | Row 25 |
| 33 | The firmware of the real CPU cannot be read through Openness, so a firmware mismatch is hard to diagnose | KNOWN_LIMITATIONS | Web API `read_cpu_type` gives the order number; firmware not compared | Row 37: compare project order number with the online CPU |
| 34 | S7-1200 V4.5+ projects fail to compile with an empty-password message until the protection level is set | v2.17.2 | none | Live check, then a troubleshooting note (row 51) |
| 35 | Live RUN/STOP reads fail about 4% of the time around mode changes; a failed read must never be shown as STOP; a subscription disturbs ordinary reads | v3.13.1 | `read_operating_mode` returns a failure category and never infers a mode | Row 51: doc note "a failed read is not STOP" |
| 36 | The symbol list is read once per connection, so symbols downloaded later are not found | v3.15.3, v3.28.0 | Web API reads resolve names per request | none |
| 37 | A live connection belongs to the session that opened it | v3.19.2 | one Web API session per target per server process; several servers mean several sessions | Doc note on CPU session limits after a live check |
| 38 | An accessible-device scan is a broadcast that reaches other subnets; a found address does not prove which device it is | v3.12.0 | [SF] scan lists devices unannotated | Row 44 |
| 39 | A scan needs an open project | KNOWN_LIMITATIONS | [SF] scan needs the bound project and a PLC's online provider | none |
| 40 | Openness has no DCP assignment of IP address or PROFINET name | KNOWN_LIMITATIONS | none; no V21 reference among ours names such a member | none (no source) |
| 41 | The project tree does not refresh after a PROFINET name change and download | KNOWN_LIMITATIONS | n/a | none |
| 42 | An archive could be reported missing while the file was still being written | v3.31.0 | `archive_project` verifies the project status, not the archive file | Row 33 |
| 43 | Terminating TIA Portal closed the first enumerated instance, possibly someone else's | v3.19.0, v3.19.4 | the server never terminates TIA Portal and never picks the first instance | none |
| 44 | Lenient argument coercion selected another physical PLC for an upload and turned boolean words into false | v3.19.0, v3.18.9 | strict schema validation, no coercion | none |
| 45 | The Openness access dialog appears on first attach and after a rebuild; a permanent allow needs administrator rights | USER_GUIDE, QUICKSTART | documented dialog; `EngineeringSecurityException` maps to access denied | Row 47 (Siemens skill `licensing-and-firewall`) |
| 46 | Group membership needs a reboot, not only a new login | QUICKSTART | doctor checks the group | Row 51: note in the installation guide after a live check |
| 47 | The first TO parameter read takes several seconds; later reads are fast until the link goes idle | tool map (`find_parameter`) | TO live check 3 measures it | Doc note in the TO reference |
| 48 | TO parameters come in bare/underscore pairs, only one of which accepts writes | tool map (`get_parameters`) | parameters listed as found | Live check; annotate pairs only with evidence |
| 49 | A TO's family and version are fixed at creation; exports are version-gated | tool map | documented installation-specific versions | none |
| 50 | Renaming a block or TO does not update references by name | tool map | no note on `rename_block`, `rename_technology_object` | Row 34 |
| 51 | An interface change resets actual values of affected instance DBs at the next download | v2.58.0 | `plc_changes_interface` names inconsistent instances, not the reinitialization | Row 51: extend the guard text |
| 52 | Element identifiers in a network are renumbered on reimport | v2.55.0 | n/a today | Design note for row 1 |
| 53 | A tag name containing a dot must be quoted | v3.33.0 | quoting documented for `memberPath`; Web API mapping rules | none |
| 54 | Older CPU firmware rejects some SimaticML attributes and instruction versions (template attributes on S7-1200 V3.0) | v2.17.3, v2.18.1, v3.13.2 | `BlockSourceGenerator` templates are fixed | Live check `create_block` on an older S7-1200; keep generated documents minimal |
| 55 | Instance DBs of system blocks cannot be exported | tool map (`db get_member`) | `get_db_values` marks system types `unexpanded` | none |
| 56 | Lookups that ignore folders miss foldered objects | v2.41.0–v2.51.0 | paths everywhere | none |
| 57 | Two symbols with the same name crashed a live read | v3.9.0 | one request per name | none |
| 58 | Asking each module once which attributes it has made hardware reads about 2.5 times faster than reading a fixed list and absorbing errors | v3.7.1 | `GetAttributeInfos` is used by the hardware inspection | Measure during the next hardware live run; no change proposed |
| 59 | Several clients on one TIA Portal can discard each other's work; TIA Portal runs one operation at a time | v3.3.0, v3.4.0 | one server per client; binding identity re-checks before mutations | Doc note; row 21 lists attached sessions |
| 60 | Hand-made changes in the TIA UI are invisible to any call log | KNOWN_LIMITATIONS | the same for our audit | Row 30 (checksums, modified dates, fingerprints) |
| 61 | Text list creation failed on a device with no text list yet | v2.19.2 | [AT] creates from master copies only | Add to [AT] live checks |
| 62 | Errors had "compile first" appended even when the cause was the online state | v3.30.0 | categories come from exception types; no guessed remedies | Keep: hints only from read evidence (rows 24, 30) |
| 63 | Secrets reached a debug log; TLS session keys were written to disk | v2.61.1, v3.5.0 | environment-variable names only, redaction | none |

## 4. Ergonomics for an LLM client

| # | Idea | Status | Our coverage / proposal |
| --- | --- | --- | --- |
| E1 | Compiler output that counts like the compiler does (leaf rows, errors-only view) | partial | row 23 |
| E2 | Reading results larger than one response in pages | missing | values over 60,000 characters are omitted whole until a restart with larger limits → row 36 |
| E3 | Inputs from files or a previous export instead of inline text | partial | some file operations ([SF], TOs, alarms) → rows 15, 27 |
| E4 | Server-side export store with retrieval and expiry | n/a | files at caller paths with manifest and hashes |
| E5 | Argument errors naming the argument and the accepted ones | present | catalog validation lists valid fields |
| E6 | "Did you mean" on not-found names | missing | row 48 |
| E7 | Counts, empty markers and a one-line project summary | missing | row 45 |
| E8 | Pre-flight verdicts before download/upload | partial | dry runs → row 25 |
| E9 | Progress notifications during long calls; human-readable tool titles | missing | row 46 |
| E10 | Detecting changes made in the TIA UI between calls | missing | row 30 |
| E11 | Before/after values with what to send to undo | present | `update_db_member` `changes[]`, TO results carry before/after |
| E12 | Hints only when evidence supports them | present | categories by exception type; keep for rows 24, 30 |
| E13 | Recent failures and statistics | partial | write audit; not proposed |
| E14 | Whole-DB live read in one call | partial | `browse_tags` + `read_tags`; not proposed |
| E15 | Member counts for all blocks in one call | missing | not proposed (composable) |
| E16 | Open exported files, list archives | n/a | client-side |
| E17 | Workflow hint / server instructions | missing | row 14 |

## 5. Prioritised proposals (rows 23–51)

None of these is in rows 1–22. Each needs the named Siemens source checked first; where a source
only names a type, the members must come from the installed V21 assemblies or a live check.

| # | Capability | Priority | Siemens source to verify first |
| --- | --- | --- | --- |
| 23 | Compiler report that an LLM can count: `depth` and a leaf flag on every row, an errors-only leaf view, the row cap filled with leaf rows first, a note that container rows carry no text | hoch | `tia-project-general/references/compile.md` §3 (recursive `Messages`) |
| 24 | Online-state guard for offline-only work: `compile_check`, content imports and creates, `save_project`, content exports read `OnlineProvider.State` while planning and block or warn with a "go offline" hint | hoch | `compile.md` (devices offline before compiling), `tia-devices-general/references/device-attributes.md` (offline for writes), `tia-plc-operations/references/online-status.md` |
| 25 | Download readiness: optional compile before `plc_download` (result in the evidence; errors stop the download) and a dry-run verdict covering path, online state and compile state | hoch | `tia-plc-operations/references/download-upload.md` ("Before a download"), Siemens skill `change-detection` (`PlcChecksumProvider`) |
| 26 | Multilingual comments for tags and user constants on create/update, external-access flags on `create_tag` (improvement log), and an info guard when `delete_tag` loses a comment or flags | hoch | `tags-types.md` (`PlcTag.Comment`, `PlcUserConstant.Comment`), `language-settings.md` §5 (`MultilingualText.Items`) |
| 27 | Objects from documents: create a new block, PLC data type, tag table or watch table from an inline SimaticML or source document (import as new, refuse existing names, return the hash); tag-table SimaticML read and hash-guarded overwrite reporting tag counts before/after and dropped tags | hoch | `tia-import-export/references/plc-blocks.md` §1–§4, `tags-types.md` (`PlcTypeComposition.Import`, `PlcTagTable.Export`, `PlcTagTableComposition.Import`), `watch-force-tables.md` (`Import`) |
| 28 | PLC data type lifecycle: delete a type and move it between type groups (transaction round trip with the recovery store) | mittel | `tags-types.md` (`PlcType.Delete`, `Export`, `PlcTypeComposition.Import`) |
| 29 | `create_block` options: explicit `number`, `memoryLayout` for DBs and FBs, header title/comment/author | mittel | `plc-blocks.md` §1 (number in the document), `blocks.md` (`MemoryLayout`, block attributes) |
| 30 | Consistency and change markers: block/type `IsConsistent`, `ModifiedDate`/`InterfaceModifiedDate`, the PLC program checksum and block fingerprints as reads; used to see UI-side changes between calls and to give evidence-based "compile first" hints | mittel | `blocks.md` (`IsConsistent`, §15 fingerprints), `tags-types.md` (`PlcType` dates), Siemens skill `change-detection` |
| 31 | Ethernet node settings beyond today's: `UseRouter`, `RouterAddress`, `IpProtocolSelection`, `UseIsoProtocol`; renaming a device (station) | mittel | `tia-networks/references/subnets-and-nodes.md` (Ethernet node table); `device-attributes.md` for the device name |
| 32 | Hardware compile: `compile_check` scope for a device (hardware and software) or a device item (hardware) with the same projection | mittel | `compile.md` §1 |
| 33 | Archive verification: after `archive_project`, wait boundedly for the archive file and report path, size and SHA-256; `postcondition_failed` when it never appears | mittel | `project-lifecycle.md` (`Archive`); no new member |
| 34 | Rename consequences: info guard on `rename_block`, `rename_technology_object`, `update_tag` renames and group renames listing references that name the object, plus a live check whether V21 updates them | mittel | `blocks.md`, `tags-types.md`, `technological-objects.md`; existing `read_cross_references` |
| 35 | Safer content writes: keep the pre-import export of `update_block_logic`/`update_type_content`/table content updates in the recovery store and offer an explicit restore item; no silent automatic restore | mittel | `tia-csharp-common/SKILL.md` (transactions), `overview.md` (Override deletes and recreates) |
| 36 | Chunked reads of oversized documents: `offset`/`length` paging for block, type, table and TO content with the whole-document hash, so a large block can be read without a restart (writing stays row 15) | mittel | none (host budget) |
| 37 | Project ↔ online CPU identity: before Web API reads that use project data, compare the project CPU's order number with `read_cpu_type` and warn or refuse on mismatch | mittel | Web API client `Plc.ReadCpuType` (PLC online reference provenance), `device-attributes.md` (type identifier) |
| 38 | Watch/force tables as entries: add, update and delete entries (name or address, display format from the tag's data type, modify/force values) composed into the SimaticML and imported with the existing hash; rename and move tables by transaction round trip; export a table to a file | mittel | `watch-force-tables.md` (entry members, `PlcWatchAndForceTableDisplayFormat`, `Import`); watch-table SimaticML in the installed V21 schemas |
| 39 | Address use from code: a "used in code" mode and double-write conflicts for `read_address_occupancy`/`find_free_address` | mittel | cross-reference sources in `blocks.md`/`tags-types.md`; check that absolute-address references are reported |
| 40 | Openness binding probe: a `probe_api`-like read listing every runtime-bound member (library, TOs, alarms, hardware modules, download) and whether it binds on the installed V21 | mittel | none (installed assemblies) |
| 41 | TO version drift: compare TO versions before and after `compile_check` and the implicit compile of content writes; warn on any change | niedrig | `technological-objects.md` |
| 42 | Station upload evidence: re-find the created `ConfigurationAddress` before uploading and report it; document that `isConfigured` is not readiness | niedrig | `tia-networks/references/online-connection-configuration.md` (`ConfigurationAddressComposition`), `download-upload.md` |
| 43 | Download or go online to an explicitly given IP address (e.g. a factory-new CPU), confirmed per call | niedrig | `download-upload.md` (`ConfigurationAddress`), `online-connection-configuration.md` |
| 44 | Scan annotation: mark each scanned device as matching a project node (IP, PROFINET name) or not, never filtering | niedrig | none new (depends on [SF]) |
| 45 | Tree and project summaries: item counts and empty markers in `browse_project_tree`, a one-line project summary in `get_project_status` | niedrig | none (host) |
| 46 | MCP comfort: progress notifications for long calls (compile, download, scans, waiting for the Openness dialog) and human-readable tool titles | niedrig | none (MCP) |
| 47 | Openness firewall diagnostics: doctor check and troubleshooting text for the permanent allow and the security-exception path | niedrig | Siemens skill `licensing-and-firewall` |
| 48 | Not-found suggestions: a few bounded "did you mean" candidates of the same kind | niedrig | none |
| 49 | Optimistic TO parameter write: optional `expectedValue`, refused when the current value differs | niedrig | none new |
| 50 | Hardware exports: AML/CAx export of a device or project, device list as CSV | niedrig | `hardware-aml.md` (`CaxProvider`), baseline `step7.json` |
| 51 | Documentation sweep for the reported pitfalls: Override replaces whole objects, unvalidated tag types/addresses, a failed RUN/STOP read is not STOP, slow first TO read, `isConfigured`, older-firmware attribute rejections, the S7-1200 protection-level compile message, reboot after group membership, reinitialization in the interface guard text, client configuration examples | niedrig | `overview.md`, `compile.md`; the rest after live checks |

## 6. Left out, and why

- Not proposed although missing: `get_all_interfaces_summary`, `list_archives`, library `find`,
  whole-DB live read, recent-error/statistics tools (composable or client-side); `show_in_editor`
  (UI-only, nobody sees it in agent use).
- Not adopted: the export store, default roots, GUI, self-update, licence, telemetry, multi-version
  binding, the credential store, the third-party S7 protocol driver, shared-server multi-client
  routing, the update notice addressed to the agent, and the "Siemens TIA Engineer" group (no Siemens
  source among ours).
- Not analysed: TiaCommander's actual implementation (licence) and its machine-readable tool list
  (not in the clone).
