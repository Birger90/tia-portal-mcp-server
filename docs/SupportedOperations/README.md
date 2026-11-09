# Supported TIA Portal Operations

**Language:** English · [Deutsch](README.de.md)

This directory is the reference for the public operation surface of `tia-portal-mcp`. It describes the MCP tools, operation names, supported data formats, safety rules, and current capability boundaries.

The MCP surface is intentionally narrower than the complete TIA Portal Openness V21 API. A capability listed as a current limit is outside the server contract; it is not a statement that the underlying Openness API cannot perform that action.

## Operation model

### PLC read and write tools

PLC reads run through `plc_read` (up to 50 independent operations; a failed item does not stop the
remaining items) and the standalone `read_cross_references`. PLC writes run through `plc_write`
(up to 50 ordered operations against one project; the first failure stops the call, with no
rollback). See the [PLC operations summary](PLC_OPERATIONS_SUMMARY.md#plc-writes-plc_write).
The generic batch tools `preview_write_batch`, `apply_write_batch` and `execute_read_batch` were
retired.

Every item contains an `operationId`, an `operation` name, and the fields for that operation. Read and write operation names are separate; project-lifecycle operations are not valid items.

#### Read operations

`plc_read` supports `get_block_content`, `get_type_content`, `list_tag_tables`, `list_download_targets` (the offline download path for `plc_download`), `read_address_occupancy` and `find_free_address` (see [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md)),
plus the online/offline compare, online-state, station-upload-interface and watch/force table reads of the [PLC online Openness reference](PLC_ONLINE_OPENNESS_SUMMARY.md), `list_external_sources`, `read_tag_comments`, `find_objects` and `scan_accessible_devices` ([PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md)), the text list, alarm text, supervision and ProDiag reads of the [PLC alarms and text lists reference](PLC_ALARMS_TEXTLISTS_SUMMARY.md) and the technology object reads `list_technology_objects`, `get_technology_object_parameters`, `get_technology_object_hardware_connection` and `get_technology_object_content` ([technology object reference](TECHNOLOGY_OBJECTS_SUMMARY.md)) the CPU protection read `get_plc_security` ([CPU protection reference](PLC_SECURITY_SUMMARY.md); not live-tested) and the DB snapshot read `get_db_snapshot` ([BetterTIA operations reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md); not live-tested), and the SIMATIC SD reads `get_block_content` format `sd` and `list_networks` ([SIMATIC SD reference](PLC_SIMATIC_SD_SUMMARY.md); not live-tested), and the change-detection reads `get_plc_checksums` and `get_fingerprints` ([offline change detection](PLC_OPERATIONS_SUMMARY.md#offline-change-detection-plc_read); not live-tested), and the block protection read `get_block_protection` ([block know-how and write protection](PLC_OPERATIONS_SUMMARY.md#block-know-how-and-write-protection-plc_readplc_write); not live-tested), and the project language read `get_project_languages` ([project languages and project texts](PLC_OPERATIONS_SUMMARY.md#project-languages-and-project-texts-plc_readplc_write); not live-tested); The settings family adds `get_cpu_settings`, `get_umac`, `get_cpu_certificates`, `get_safety_validation` and `check_activation_test` (see [PLC_SETTINGS_SUMMARY.md](PLC_SETTINGS_SUMMARY.md)).
`read_cross_references` is a standalone tool. `execute_read_batch` was retired.
Hardware/catalog reads use `network_read`. WinCC Unified HMI reads use `hmi_read`; tag-table/tag/alarm writes use `hmi_write`. See [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md).

Project binding, status, project-tree browsing, and compilation are separate tools: `bind_project`, `get_project_status`, `browse_project_tree`, and `compile_check`. The first three are available in every mode; compile, lifecycle, `plc_download`, `plc_online_write`, `plc_tia_online` and `plc_station_upload` are available in read-write and full. Full adds no tools or operations; the Openness PLC run/stop (`start_plc`/`stop_plc`) was removed, and a real CPU's operating mode changes only through `plc_online_write change_operating_mode` over the Web API. See [Installation](../guides/installation.md#access-modes) for the 13/33/33 surfaces and confirmation policy.

`plc_download` downloads the bound standalone project's PLC to PLC simulation (the `PLCSIM` PC interface) or to a real controller (any other PC interface), confirming every actual read-write call; the access mode is the only control over hardware downloads. See the [PLC download reference](PLC_DOWNLOAD_SUMMARY.md); it is not live-tested.

`plc_tia_online` (go online/offline on the exact download path) and `plc_station_upload` (upload a station into the offline project) are available in read-write and full, confirm every actual read-write call and answer TIA Portal confirmations by `portalConfirmations` like `plc_download`; `compare_to_online` uses the new `OnlineObserve` capability (every mode). These Openness tools are distinct from the Web API tools `plc_online_read`/`plc_online_write`. See the [PLC online Openness reference](PLC_ONLINE_OPENNESS_SUMMARY.md); it is not live-tested.

`library_read` (every mode) reads the project library and the open global libraries (tree, types, versions, instances, update check, compare); `library_write` (read-write and full) opens, retrieves, creates, saves and closes global libraries (one prompt per read-write call that contains such an item), captures, copies, deletes, renames and instantiates master copies, instantiates type versions, updates the project and edits, releases, discards, sets as default or deletes type versions. See the [library reference](LIBRARY_OPERATIONS_SUMMARY.md).

#### Write operations

`plc_write` supports:

`update_block_logic`, `update_type_content`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group`, `create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `move_tag`, `create_user_constant`, `update_user_constant`, `delete_user_constant`, the watch/force table writes (`create_watch_table`, `delete_watch_table`, `create_watch_table_group`, `delete_watch_table_group`, `update_watch_table_content`, `update_force_table_content`) and `update_program` ([PLC online Openness reference](PLC_ONLINE_OPENNESS_SUMMARY.md#plc_write-operations)), the text list, alarm text, alarm class, supervision and ProDiag writes (`update_text_list`, `delete_text_list`, `update_text_list_cells`, `update_alarm_text_cells`, `update_supervision_cells`, the `export_*` and `import_*` alarm operations, `set_prodiag_assignment`, `export_prodiag_info`; [PLC alarms and text lists reference](PLC_ALARMS_TEXTLISTS_SUMMARY.md), not live-tested), the external-source, group, software-unit and file-export writes (`import_external_source`, `generate_blocks_from_source`, `delete_external_source`, `export_source_file`, `rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group`, `create_type_group`, `delete_type_group`, `create_tag_table_group`, `delete_tag_table_group`, `create_software_unit`, `delete_software_unit`, `rename_software_unit`, `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv`, `export_blocks`; [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md)) and the technology object writes `create_technology_object`, `delete_technology_object`, `rename_technology_object`, `create_technology_object_group`, `delete_technology_object_group`, `rename_technology_object_group`, `set_technology_object_parameter`, `connect_technology_object_hardware`, `disconnect_technology_object_hardware`, `export_technology_object`, `import_technology_object` and `update_technology_object_content` ([technology object reference](TECHNOLOGY_OBJECTS_SUMMARY.md); not live-tested), and the CPU protection writes `set_plc_access_level`, `protect_plc_confidential_data`, `change_plc_confidential_data_password` and `disable_plc_confidential_data_protection` with passwords from password sources ([CPU protection reference](PLC_SECURITY_SUMMARY.md); not live-tested), and the operations adopted from BetterTIA, `create_db_snapshot`, `set_network_text`, `replace_network_text`, `rename_members`, `rename_tags`, `rename_objects`, `replace_tag_comments` ([BetterTIA operations reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md); not live-tested), and the tag table files `export_tag_table`, `import_tag_table` ([same reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions); not live-tested), and the SIMATIC SD writes `update_block_logic` format `sd`, `insert_network`, `replace_network`, `delete_network` and `create_block_from_document` ([SIMATIC SD reference](PLC_SIMATIC_SD_SUMMARY.md); not live-tested). Content updates take `content` together with `expectedContentHash` from a `plc_read`. Network provisioning/configuration, rack/slot modules, hardware parameters, I/O addresses and the I/O map export use `network_write`; see [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md). The settings family adds the issue #15 writes `set_account_locking`, `set_plc_password_policy`, `set_legacy_plc_password_policy`, `set_syslog`, `add_syslog_server`, `remove_syslog_server`, `create_project_syslog_server`, `update_project_syslog_server`, `delete_project_syslog_server`, `set_system_web_pages`, `set_default_web_application`, `set_watch_table_access`, `remove_watch_table_access`, `set_force_table_access`, `remove_force_table_access`, `set_display_password`, `set_front_panel_display`, `set_plc_project_language`, `set_ip_accessibility`, `generate_web_blocks` and the issue #13 writes `create_webserver_user`, `update_webserver_user`, `delete_webserver_user`, `update_simple_webserver_user`, `create_opcua_user`, `set_opcua_user_password`, `delete_opcua_user` and the issue #12 writes `create_custom_role`, `update_custom_role`, `delete_custom_role`, `assign_engineering_right`, `unassign_engineering_right`, `assign_device_function_right`, `unassign_device_function_right`, `create_project_user`, `update_project_user`, `delete_project_user`, `assign_user_role`, `unassign_user_role`, `set_anonymous_user`, `create_custom_device_function_right`, `update_custom_device_function_right`, `delete_custom_device_function_right`, `update_umc_user`, `delete_umc_user`, `set_umac_password_policy` and the issue #14 writes `set_certificate_manager`, `create_cpu_certificate`, `import_cpu_certificate`, `export_cpu_certificate`, `delete_cpu_certificate`, `assign_cpu_certificate`, `unassign_cpu_certificate`, `set_certificate_management`, `add_certificate_supported_service`, `update_certificate_supported_service`, `remove_certificate_supported_service` and the issue #21 write `generate_safety_printout` and the issue #25 writes `create_activation_test`, `delete_activation_test`, `change_activation_test_evaluation_device`, `create_activation_test_group`, `delete_activation_test_group`, `create_safety_function`, `update_safety_function`, `delete_safety_function`, `reset_safety_function_test_result`, `create_safety_function_condition`, `update_safety_function_condition`, `delete_safety_function_condition`, `export_activation_test`, `generate_activation_test_report` (see [PLC_SETTINGS_SUMMARY.md](PLC_SETTINGS_SUMMARY.md)).

`hmi_write` supports create/update/delete for Unified tag tables, tags and discrete/analog alarms;
see the [nine-operation reference](HMI_OPERATIONS_SUMMARY.md#writes-hmi_write), plus nine screen, script and batch
rename writes adopted from BetterTIA ([BetterTIA operations reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#wincc-unified-screens-scripts-and-batch-renames-hmi_write); not live-tested)
and the user text list files `export_text_lists` / `import_text_lists` (members bound at runtime, not documented by
Siemens for Unified; [same reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions); not live-tested),
and the Classic WinCC writes `export_classic_objects`, `import_classic_objects`, `delete_classic_object`, `create_classic_folder`,
`delete_classic_folder` with the read `list_classic_objects` ([classic and Unified extensions](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md); not live-tested),
and the Unified event and dynamization writes `set_event_script`, `delete_event_script`, `set_dynamization_script`,
`set_dynamization_flashing`, `delete_dynamization` with the reads `list_screen_item_events`, `list_screen_item_dynamizations` ([same reference](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md#wincc-unified-events-and-dynamizations-issue-29); not live-tested),
and the Unified audit class, runtime setting and plant view writes `create_audit_class`, `delete_audit_class`, `set_audit_class_property`,
`set_runtime_setting`, `create_plant_view`, `delete_plant_view`, `create_plant_view_node`, `delete_plant_view_node`,
`set_plant_object_interface_property` with the read `list_plant_views` ([same reference](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md#wincc-unified-audit-classes-runtime-settings-plant-views-issue-29); not live-tested).

### Project lifecycle tools

The server also provides six single-purpose lifecycle tools:

| Tool | Behavior |
|---|---|
| `open_project` | Opens a project and binds the MCP session to it. |
| `create_project` | Creates a project and binds the session to it. |
| `save_project` | Saves the active project. |
| `save_project_as` | Saves a copy and rebinds the session to the copy. |
| `archive_project` | Archives a project to the requested location. |
| `close_project` | Closes the active project and clears the session binding. |

`get_project_status`, `browse_project_tree`, and `compile_check` are standalone project tools rather than batch operations.

### PLC online tools (S7-1500 Web API)

`plc_online_read` (every mode) and `plc_online_write` (read-write and full) observe and change real
S7-1500 CPUs online through Siemens' official Web API client, inside the host. The PLCs are listed by
the user in a `--plc-webapi-config` target file with their credential variables and certificate
rules; tools only name a `target`. `plc_online_write` confirms every actual read-write call. They are
not live-tested yet. See the [PLC online reference](PLC_ONLINE_WEBAPI_SUMMARY.md) for prerequisites
in TIA Portal, the file format, operations, guards, provenance and live acceptance steps.

### Siemens documentation (`siemens_docs`)

`siemens_docs` (every mode, no TIA Portal needed) searches and reads the official Siemens documentation on
docs.tia.siemens.cloud and docs.industrial-operations-x.siemens.cloud: `search`, `read_page` (Markdown, paged),
`get_toc`, `list_publications`. Only the allowed hosts are contacted; references are resolved to ids, never fetched
as given. Not live-tested on the Windows PC; the endpoints were checked live from the cloud container. See the
[Siemens documentation reference](SIEMENS_DOCS_SUMMARY.md).

### S7-PLCSIM Advanced tools

`plcsim_read` (every mode), `plcsim_write` and `plcsim_test` (read-write and full) control
S7-PLCSIM Advanced virtual S7-1500 CPUs through a separate PLCSIM worker; they never touch TIA
Portal or the project binding. They are not live-tested yet. See the
[PLCSIM operations reference](PLCSIM_OPERATIONS_SUMMARY.md) for operations, the reflective API
surface and its provenance, prerequisites and the live acceptance steps.

## Write safety

Lifecycle, Network, PLC and HMI writes, `plc_download` and `plc_online_write` use guarded single-call writes. No tool uses safety tokens.

- `plc_online_write` previews with `dryRun:true` without contacting a PLC; every actual read-write call asks once (all items in one prompt), full runs under policy, and each entered call is audited with `recordKind:"plc_online"`. See the [PLC online reference](PLC_ONLINE_WEBAPI_SUMMARY.md#access-confirmation-and-audit).

- PLC writes preview with `dryRun:true`; omitted `dryRun` executes, with zero server elicitation. A duplicate singleton OB and the safety login rule (a safety password set, not logged on) block the call; unreadable evidence, deletes and address overlaps are info, and TIA Portal decides the rest. Content writes need `expectedContentHash`; stale content fails with `state_changed`. See the [PLC reference](PLC_OPERATIONS_SUMMARY.md#plc-writes-plc_write).
- Lifecycle uses `dryRun` with operation inputs and no public confirmation array or token. Every actual read-write call asks once through form elicitation; full runs under policy without server elicitation. Block guards refuse in every mode; dry runs do not mutate or elicit. See the [lifecycle reference](PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).
- Network previews with `dryRun:true`; `dryRun:false` or omitted dryRun executes by default, with zero server elicitation. Exact already-open verified binding, complete guards and typed postchecks apply; stop on failure, no batch rollback or automatic replay. See the [Network contract](NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope) for pre-entry SDK rejection versus canonical entered denials.
- HMI previews with `dryRun:true`; omitted dryRun executes tag-table/tag/alarm writes without elicitation. Existing connections, complete delete evidence and typed cleanup/partial outcomes apply on standalone and local-session roots. See the [HMI reference](HMI_OPERATIONS_SUMMARY.md#writes-hmi_write).
- A multi-item write is sequential rather than transactional. Application stops at the first failure; completed items remain applied and later items are marked `skipped`.
- Audit JSONL lives under `%LOCALAPPDATA%\TiaMcpServer\audit`. Lifecycle, Network, PLC and HMI audit v2 record every entered call, including previews and refusals, with confirmation by `user`, `policy`, or `none`; SDK rejection before entry has no write audit. The legacy write audit stream was retired.

Read responses may include `warnings` for partial or degraded data. Hardware reads also provide payload-level `messages` for unreadable members. Callers should treat these fields as part of the result contract rather than filling missing values locally.

## Area reference

| Area | Reference |
|---|---|
| Project and portal lifecycle | [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md) |
| Devices | [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) |
| PLC software | [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md) |
| PLC download to PLCSIM or real hardware (`plc_download`) | [PLC_DOWNLOAD_SUMMARY.md](PLC_DOWNLOAD_SUMMARY.md) |
| PLC online over the S7-1500 Web API (`plc_online_read`, `plc_online_write`) | [PLC_ONLINE_WEBAPI_SUMMARY.md](PLC_ONLINE_WEBAPI_SUMMARY.md) |
| PLC online over Openness: compare, online state, go online/offline, station upload, watch/force tables (`plc_tia_online`, `plc_station_upload`) | [PLC_ONLINE_OPENNESS_SUMMARY.md](PLC_ONLINE_OPENNESS_SUMMARY.md) |
| S7-PLCSIM Advanced (`plcsim_read`, `plcsim_write`, `plcsim_test`) | [PLCSIM_OPERATIONS_SUMMARY.md](PLCSIM_OPERATIONS_SUMMARY.md) |
| Siemens documentation (`siemens_docs`) | [SIEMENS_DOCS_SUMMARY.md](SIEMENS_DOCS_SUMMARY.md) |
| HMI | [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md), [HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md) |
| Standalone VCI workspaces (`vci_read`, `vci_write`) | [VCI_OPERATIONS_SUMMARY.md](VCI_OPERATIONS_SUMMARY.md) |
| Project and global libraries, types, master copies (`library_read`, `library_write`) | [LIBRARY_OPERATIONS_SUMMARY.md](LIBRARY_OPERATIONS_SUMMARY.md) |
| "Claude in TIA": TIA Portal Add-In, inbox/outbox hand-over to Claude (`tia_inbox`), Claude bridge | [CLAUDE_IN_TIA.md](CLAUDE_IN_TIA.md) |
| MCP resources (embedded reference documents, operation reference) and prompts (workflows) | [MCP_RESOURCES_PROMPTS_SUMMARY.md](MCP_RESOURCES_PROMPTS_SUMMARY.md) |
| External sources, project-tree folders (find, rename, type and tag-table group create/delete), software units (create, delete, rename), tag comments, CSV and batch file exports, accessible-device scan | [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md) |
| PLC alarms, alarm classes, text lists, ProDiag supervisions and assignments (`plc_read`, `plc_write`) | [PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.md) |
| Technology objects: list, parameters, hardware connections, export/import (`plc_read`, `plc_write`) | [TECHNOLOGY_OBJECTS_SUMMARY.md](TECHNOLOGY_OBJECTS_SUMMARY.md) |
| CPU protection: every access level with level passwords, confidential configuration data protect/change/unprotect from password sources, hardware compile (`plc_read`, `plc_write`, `compile_check`) | [PLC_SECURITY_SUMMARY.md](PLC_SECURITY_SUMMARY.md) |
| CPU and project settings (issue #15), web server and OPC UA users (issue #13), UMAC (issue #12), CPU certificates (issue #14), safety printout (issue #21), Safety Validation Assistant (issue #25): account locking, password policies, syslog, web pages, watch/force table access, display password, front panel, project language, IP accessibility (`plc_read`, `plc_write`) | [PLC_SETTINGS_SUMMARY.md](PLC_SETTINGS_SUMMARY.md) |
| V21 API completion (package G): documented V21 methods added to the existing tools, with the left-out list (`plc_write`, ...) | [V21_API_COMPLETION_SUMMARY.md](V21_API_COMPLETION_SUMMARY.md) |
| Fail-safe (F-) PLCs: safety administration, F-block signatures, safety login and password, F-runtime groups, F-program download answer (`plc_read`, `plc_write`, `plc_download`) | [PLC_SAFETY_SUMMARY.md](PLC_SAFETY_SUMMARY.md) |
| TIA Portal V21 pitfalls reported by rickgaiser/TiaMcp: guards, read flags, export ledger, live acceptance list | [V21_PITFALLS.md](V21_PITFALLS.md) |
| SIMATIC SD (.s7dcl/.s7res): sd reads, `list_networks`, network insert/replace/delete, `update_block_logic` sd, `create_block_from_document` (`plc_read`, `plc_write`) | [PLC_SIMATIC_SD_SUMMARY.md](PLC_SIMATIC_SD_SUMMARY.md) |
| Networks and topology | [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) |
| SIMATIC drives / Startdrive | [SIMATIC_DRIVES_OPERATIONS_SUMMARY.md](SIMATIC_DRIVES_OPERATIONS_SUMMARY.md) |
| PLC import/export formats | [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md) |
| Multiuser inventory through `bind_project`, passive context and acceptance boundary | [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md) |
| Project Server administration (`project_server_write`): connections, upload, local sessions | [PROJECT_SERVER_WRITE_SUMMARY.md](PROJECT_SERVER_WRITE_SUMMARY.md) |
| Teamcenter | [TEAMCENTER_OPERATIONS_SUMMARY.md](TEAMCENTER_OPERATIONS_SUMMARY.md) |
| TestSuite | [TESTSUITE_OPERATIONS_SUMMARY.md](TESTSUITE_OPERATIONS_SUMMARY.md) |

## Runtime requirements

The Openness worker is the only process that loads Siemens assemblies. Using the server requires Windows, TIA Portal V21 with Openness enabled, membership in the Siemens TIA Openness user group, and the supported .NET runtimes. The worker communicates with the .NET 10 host over newline-delimited JSON and is supervised across timeouts and crashes. The framework-dependent global tool requires a supported .NET 10 runtime; the separate self-contained `win-x64` archive includes the host runtime. Both layouts retain the .NET Framework 4.8 Openness worker.

This reference describes the software contract. A successful build or automated test does not replace validation against the target TIA Portal V21 installation, project, device configuration, or hardware.
