# TIA-Portal-MCP-Server

**Language:** English · [Deutsch](https://github.com/Birger90/tia-portal-mcp-server/blob/main/README.de.md)

[![Build Status](https://img.shields.io/github/actions/workflow/status/Czarnak/tia-portal-mcp/ci.yml?branch=main&style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/actions)
[![Codecov](https://img.shields.io/codecov/c/github/Czarnak/tia-portal-mcp?style=flat-square)](https://codecov.io/gh/Czarnak/tia-portal-mcp)
[![GitHub Release](https://img.shields.io/github/v/release/Czarnak/tia-portal-mcp?style=flat-square)](https://github.com/Czarnak/tia-portal-mcp/releases)
[![.NET SDK](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![TIA Portal](https://img.shields.io/badge/TIA_Portal-V21-009999.svg?style=flat-square&logo=siemens)](https://new.siemens.com/global/en/products/automation/industry-software/automation-software/tia-portal.html)
[![MCP](https://img.shields.io/badge/MCP-Ready-000000.svg?style=flat-square)](https://modelcontextprotocol.io/)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg?style=flat-square)](https://github.com/Birger90/tia-portal-mcp-server/blob/main/LICENSE)
![NuGet Downloads](https://img.shields.io/nuget/dt/TiaMcpServer)

> The Build, Codecov, Release and NuGet badges show the state of the original project
> Czarnak/tia-portal-mcp, not that of this fork. This fork is licensed under AGPL-3.0, see
> [License notes for this fork](#license-notes-for-this-fork).

MCP server for Siemens SIMATIC TIA Portal V21. It lets MCP clients and AI agents inspect and edit a running TIA Portal project through the Siemens Openness API.

The current state covers:

- project discovery and project lifecycle (open, create, save, archive, close),
- export and import of PLC blocks, reading tag tables and guarded writes to the PLC software,
- hardware and network analysis, hardware catalog search and guarded creation of network devices,
- WinCC Unified HMI: reading and guarded writing of tag tables, tags and alarms,
- VCI workspaces (Version Control Interface) for standalone projects,
- cross-references as well as compiling and checking with compiler diagnostics,
- guarded PLC download to PLCSIM or real hardware (controlled by the access mode),
- via Openness: offline/online comparison, going online and offline, station upload, and editing watch and force tables,
- S7-PLCSIM Advanced: control of virtual CPUs and test scenarios in the worker,
- real S7-1500 CPUs: monitoring and control through the SIMATIC S7-1500 Web API.

Download, simulation and online tools are not live-tested yet.

## Tools

The server exposes 13 tools in `read-only` mode, 33 in `read-write` (startup default) and the same 33 in `full`.

`read-write` permits:
- changes in the project and compiling,
- project lifecycle calls,
- PLC download to PLCSIM or hardware,
- online changes to configured S7-1500 CPUs,
- going online and offline as well as station upload,
- simulation control in S7-PLCSIM Advanced.

The user is asked once per actual lifecycle, download, online-write, online-connection or upload call. Additionally, once per `library_write` call that opens, creates, retrieves, saves or closes a global library. And once per `project_server_write` call that deletes a local session or a server connection or uploads a project. `full` adds no tools or operations; it only runs these calls without asking.

Almost all writing tools run through the same guarded pipeline with exactly one call; there are no safety tokens. This applies to `plc_write`, `network_write`, `hmi_write`, `vci_write`, `library_write`, `plc_download`, `plc_online_write`, `plc_tia_online`, `plc_station_upload`, `project_server_write` and the six lifecycle tools. Only the PLCSIM tools are exempt.

There is no RUN/STOP through TIA Portal. Real controllers are changed in only two ways:
- by `plc_download`,
- by `plc_online_write`, and only for controllers that the user has listed in a configuration file.

### Reading PLCs and cross-references

- `plc_read` runs up to 50 PLC read operations in one call. All results are structured JSON with `contractVersion` `1.0`. `describe_operations` returns the fields and full rules of any `plc_read`/`plc_write` operation, without a bound project ([operation reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#operation-reference-describe_operations)).
  - **General:**
    - `get_block_content`, `get_type_content` and `list_tag_tables`; `get_block_content` with `format:"sd"` returns only the SIMATIC SD documents (`.s7dcl`, `.s7res`) with an `sd:sha256` hash and `failSafe` for F-blocks ([SIMATIC SD reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.md); not live-tested yet).
    - `list_networks` shows the networks of a block from the SD export: number, language (pragma `S7_Language` or detected as in the Siemens skill), all pragma keys, title and comment per language from the `.s7res`, the code and the `contentHash` for the network operations (not live-tested yet).
    - `list_download_targets` returns the offline download path for `plc_download`, read without going online.
    - `read_address_occupancy` / `find_free_address` show the I/Q/M address occupancy from declared tags and hardware addresses.
    - `get_plc_security` shows per CPU the access level, the access control and the service for „Vertrauliche PLC-Konfigurationsdaten schützen“ (protect confidential PLC configuration data, with `MasterSecretConfiguration`), to clarify errors during hardware compilation and download ([CPU protection reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.md); not live-tested). A service that the CPU lists but that `GetService` does not return (seen live with `PlcAccessControlConfigurationProvider`, defect D14) no longer makes the whole query fail: that part is then reported as not readable, the rest is returned.
  - **Online access through Openness** ([PLC online reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_OPENNESS_SUMMARY.md)):
    - `compare_to_online`; a TLS certificate check during it is accepted only through the item's `trustedCertificates` (as with `plc_download`), online password prompts of a protected CPU through `passwords` (password sources) or the mapping file; otherwise the comparison is discarded with evidence.
    - `compare_software` compares with another PLC, the project library or a master copy; the comparison tree is limited in depth and count.
    - `compare_hardware` and `compare_hardware_to_online` compare the station or CPU hardware with another PLC's, a master copy or the device online (issue #7, not live-tested; same TLS and password rules as `compare_to_online`; V21 does not document which objects can be hardware compare targets, so an unsuitable one is reported, never guessed).
    - `get_online_state`, `list_station_upload_interfaces` and `list_watch_and_force_tables`.
    - `read_watch_table`, `read_force_table`, `get_watch_table_content` and `get_force_table_content`.
  - **Interfaces and data blocks:**
    - `get_block_interface` / `get_type_interface` return typed trees of the interface members with the xml `contentHash`.
    - `get_db_values` returns start values of global and instance DBs at any depth.
    - `get_db_snapshot` returns the snapshot of the actual values of a DB stored in the offline project (path, value, data type, timestamp; filterable with `memberPath`). Adopted from BetterTIA/watch `watch_db` ([BetterTIA operations reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md); not live-tested).
  - **Program analysis** ([program analysis reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#program-analysis-plc_read); not live-tested):
    - `get_call_tree`, `find_call_paths`, `find_unused_blocks`, `find_orphaned_instance_dbs` and `analyze_change_impact`.
    - Computed from the block inventory and the cross-reference reader.
    - Only relations that a cross-reference location explicitly names are used.
    - The results are bounded, partial results are marked, indirect calls are not visible.
  - **External sources, search and device scan** ([external sources and folders reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SOURCES_FOLDERS_SUMMARY.md); not live-tested):
    - `list_external_sources` lists the external sources of a PLC.
    - `read_tag_comments` returns the multilingual comments of tags and user constants.
    - `find_objects` searches with glob patterns in blocks, groups, types, tag, watch and force tables as well as external sources, and pages.
    - `scan_accessible_devices` calls `GetAccessibleDevices()` on an explicitly named PC interface. The search in the network is live but changes nothing; it is available in every access mode, for standalone projects only.
  - **Text lists and alarms** ([PLC alarms and text lists reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ALARMS_TEXTLISTS_SUMMARY.md); not live-tested):
    - `list_text_lists` returns user and system text lists with ID, range and multilingual comment as well as the project languages.
    - `read_text_list_workbook`, `read_alarm_text_workbook` and `read_supervision_workbook` read TIA Portal's XLSX export as uninterpreted cells. They return a `workbookHash` and page through `sheet`/`startRow`/`startColumn` within the value budget.
    - `get_prodiag_fb` and `get_prodiag_assignment` return ProDiag evidence and `AssignedProDiagFB`.
  - **Technology objects** ([technology objects reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/TECHNOLOGY_OBJECTS_SUMMARY.md); not live-tested):
    - `list_technology_objects`, `get_technology_object_hardware_connection` and `get_technology_object_content`.
    - `get_technology_object_parameters` types every value by its runtime type and pages.
  - **Fail-safe controllers (F-CPU)** ([F-program reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SAFETY_SUMMARY.md); not live-tested):
    - `get_safety_administration` reads per CPU the password protection, login state, safety system version, F-runtime groups (with `attributeInfos`: every attribute that `GetAttributeInfos()` reports, with its access mode), collective signatures and safety units; it reports missing Openness members as `api_member_missing`.
    - `list_safety_blocks` lists the F-blocks with their signatures.
  - **Structure of a request:**
    - Every item has a unique `operationId`, an `operation` name and the parameters of that operation.
    - The items run independently of each other; a failing item does not stop the others.
    - A value over the value budget (default 60,000 characters) is omitted entirely with hints for narrowing it down, never truncated.
- `read_cross_references` reads the cross-references of a target in the project tree: a block, type, tag or constant, or a PLC, software unit or folder. For the last three, all owners below are traversed.
  - Arguments: `target`, `filter`, `maxResults`.
  - Both readers are available in every access mode and work only on the bound project.
  - They never open or switch a project. Without a verified binding they fail closed with `binding_conflict`; then call `bind_project` first.

### Reading and writing HMIs

- `hmi_read` runs up to 50 read operations on WinCC Unified HMIs in one call.
  - Covered are devices, tags, connections, alarms, logs, screens and screen items, faceplates, navigation, runtime settings, script modules, text and graphic lists, languages and `validate`.
  - Registered in every access mode; paging uses `offset`/`limit` (default 100).
  - Results are structured JSON with `contractVersion` `1.1`.
  - Classic WinCC HMIs (Comfort/Advanced panels) are read with `list_classic_objects` (issue #28, not live-tested; [classic and Unified extensions](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md)); the other operations refuse them.
  - See the [HMI overview](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md).
- `hmi_write(operations, dryRun=false)` creates, updates or deletes WinCC Unified tag tables, tags as well as discrete and analog alarms.
  - Nine operations for tables, tags and alarms (plus nine for screens, scripts and renames, see below) in read-write/full, up to 50 items in the caller's order.
  - The target is a verified standalone project or a local session; write contract `2.0`.
  - `dryRun:true` shows a preview.
  - Execution stops at the first failure, without rolling back the batch and without a server prompt.
  - An `EngineeringTargetInvocationException` during the change becomes `engineering_rejected`, unless `portal_lost` takes precedence.
  - Typed Siemens failures carry `engineeringError`, except with `portal_lost`. Partial results exist only when a writer was allocated.
  - After writes, check the actual state, run the public `validate` and compile the HMI device in TIA Portal.
  - Fields, three guards and shared verification are described in the [HMI reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_OPERATIONS_SUMMARY.md#writes-hmi_write).
  - The [qualification report](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md) records the completed qualification within the recorded scope R1/R2/R3.
  - **Screens, scripts and batch renames, adopted from BetterTIA** (not live-tested; [BetterTIA operations reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md)): `create_screen` / `delete_screen`, `rename_screens` and `rename_tags` (several screens or HMI tags, with swaps, in one transaction), `create_screen_item` (Openness type name, e.g. `HmiButton`) / `delete_screen_item`, `set_screen_item_property` (value in the type of the current value, colors as `#RRGGBB`), `set_dynamization_tag` (set or create a tag dynamization) and `replace_in_scripts` (`{ find, replace }` in all scripts). If an Openness member is missing, the item fails with `api_member_missing` without changing anything. The PLC tag of an HMI tag is still set by `update_tag` (`plcTag`).
  - **Unified events and dynamizations** (issue #29, not live-tested; [classic and Unified extensions](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md)): `set_event_script` / `delete_event_script` (event and property event handlers with their script), `set_dynamization_script` (with trigger), `set_dynamization_flashing`, `delete_dynamization`; reads `list_screen_item_events` and `list_screen_item_dynamizations`. Shapes and widgets are created with `create_screen_item` by their Openness type name.
  - **Unified audit classes, runtime settings and plant views** (issue #29, not live-tested; [same reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md#wincc-unified-audit-classes-runtime-settings-plant-views-issue-29)): `create_audit_class` / `delete_audit_class` / `set_audit_class_property`, `set_runtime_setting`, `create_plant_view` / `delete_plant_view`, `create_plant_view_node` / `delete_plant_view_node`, `set_plant_object_interface_property`; read `list_plant_views`.
  - **Classic WinCC HMIs** (issue #28, not live-tested; [classic and Unified extensions](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md)): `export_classic_objects` / `import_classic_objects` (SimaticML files of tag tables, tags, screens, templates, pop-ups, slide-ins, VB scripts, text and graphic lists, connections, cycles, global elements and the screen overview), `delete_classic_object`, `create_classic_folder` / `delete_classic_folder`. Every member is bound at runtime (the worker still does not reference `Siemens.Engineering.WinCC.dll`); graphic lists are never exported and a screen import with a duplicate screen number is refused (V21 pitfalls 3 and 4).
  - **WinCC Unified text lists as files** (not live-tested): `export_text_lists` writes one `<Name>.xml` per user text list into an existing directory (all or `textListNames`), `import_text_lists` imports a file (with `overwrite:true` through `ImportOptions.Override`). Siemens does not document these members for Unified text lists; they are bound at runtime, and if they are missing, the item fails with `api_member_missing` without writing or changing anything (info guard `hmi_text_list_file_unverified`). Graphic lists and system text lists are never exported (V21 pitfall 3).

### Version Control Interface

- `vci_read` inspects, in every mode, the workspaces of standalone projects, paged mappings, optionally the raw status and the supported leaf formats.
- `vci_write(operations, dryRun=false)` creates and deletes workspaces in read-write/full, exports leaf scopes, synchronizes in both directions and deletes mappings.
  - Protection against a known crash: an explicitly requested export in SIMATIC SD format is refused for F-blocks and blocks in system folders, because TIA Portal V21 crashed three times doing this in tests by Czarnak. SimaticML still works for these blocks.
  - Executes by default; `dryRun:true` shows a preview.
  - Stops after the first failed item, without rollback across items and without a server prompt.
  - Save the project so that workspaces and mappings are kept; check the current state before a retry.
  - The [VCI reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md) describes formats, path guards, synchronization decisions, recovery after Portal loss and the limits of the acceptance.

### Libraries (not live-tested yet)

- `library_read` runs, in every mode, up to 50 independent read operations. It reads the library of the bound standalone project and the open global libraries:
  - `list_libraries`, `list_global_library_infos`,
  - `get_library_tree` (type folders, types with versions and state, master copy folders and master copies; paged with offset/limit),
  - `get_type` (also DoNotUse, status, namespace), `find_type_instances`, `get_instance_info`, `update_check`, `compare_libraries`,
  - `get_supported_export_formats`, `compare_types`, `compare_type_versions`, `compare_master_copies` (detailed compares).
- `library_write(operations, dryRun=false)` in read-write/full:
  - **Global libraries:** `open_global_library`, `retrieve_global_library` (also with upgrade), `create_global_library`, `save_global_library`, `close_global_library`, `archive_global_library`, `save_global_library_as`. In read-write, the user is asked once per call that contains one of them.
  - **Master copies:** `create_master_copy`, `copy_master_copy`, `delete_master_copy`, `rename_master_copy`, `instantiate_master_copy` (PLC groups, technology objects, software units, external sources, user text lists, devices, device items).
  - **Master copy folders:** `create_master_copy_folder`, `delete_master_copy_folder`, `rename_master_copy_folder`; every master copy item takes `masterCopyFolderPath`.
  - **Types:** `instantiate_type_version`, `update_project` (also with the update modes), `rename_type`, `rename_type_folder`, `edit_type_version`, `release_type_version`, `discard_type_version`, `set_default_type_version`, `delete_type_version`, `delete_type`, `set_type_do_not_use`, `create_type_folder`, `delete_type_folder`.
  - **Secondary projects (issue #20):** `open_secondary_project`, `retrieve_secondary_project`, `close_secondary_project` open a project read-only next to the bound one (never bound); copy from it with `create_master_copy` `source.project`, compare with `plc_read` `compare_software` `compareTarget: secondary_plc`, list with `library_read` `list_secondary_projects`.
  - **TIA Portal settings (issue #27):** `library_read` `list_portal_settings`, `library_write` `set_portal_setting` (TiaPortal.SettingsFolders; the whole TIA Portal instance; every change asks in read-write, a security-relevant one also fires an acknowledge guard).
  - **Library maintenance (issue #17):** `clean_up_library`, `harmonize_project`, `update_library`, `create_type_from_documents`, `create_version_from_documents`, `export_type_version`, `export_type_version_as_documents`.
  - The guards are informative. Exception: a close is refused as long as the library has changes that this server has not saved.
  - TIA Portal confirmation dialogs are answered through `portalConfirmations` (default `decline`) and recorded.
  - Most Siemens members are bound only at runtime and fail closed when they are missing.
  - See the [library reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md).

### S7-PLCSIM Advanced (not live-tested yet)

- `plcsim_read` runs, in every mode, up to 50 independent read operations on virtual S7-1500 CPUs of S7-PLCSIM Advanced:
  - `probe_api` shows where the PLCSIM Advanced API was found and how every API member used by the server resolves. Run this operation first.
  - `list_instances`, `get_instance`, `list_tags` and typed `read_tags`.
- `plcsim_write(operations, dryRun=false)` in read-write/full: `power_on`, `power_off`, `run`, `stop`, `memory_reset`, `update_tag_list`, typed `write_tags` and `set_scale_factor`.
  - The instance is selected by its exact name.
  - Instances are created in the PLCSIM Advanced Control Panel, never by the server.
- `plcsim_test` runs, in read-write/full, a scenario with up to 200 `write`/`wait`/`expect` steps in the PLCSIM worker.
  - Per step there is status, last observed value, attempts and times.
  - The times are wall-clock time; there is no synchronization with virtual time.

These tools run in a separate PLCSIM worker that starts on the first call. They touch neither TIA Portal nor the project binding.

The PLCSIM Advanced API is loaded from the installation at runtime and bound by reflection. A member that is missing in the installed version fails with `plcsim_api_member_missing`.

Simulation control is the `SimulationControl` capability; it is denied in read-only. Every `plcsim_write`/`plcsim_test` call is logged. See the [PLCSIM reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLCSIM_OPERATIONS_SUMMARY.md) with the live acceptance steps.

### S7-1500 CPUs online through the Web API (not live-tested yet)

- `plc_online_read` runs, in every mode, up to 50 independent observations of real S7-1500 CPUs. This also applies to PLCSIM Advanced instances with the web server enabled.
  - Covered are `ping`, `version`, `permissions`, operating state and mode selector switch, CPU type, runtime information, system time, project information, `browse_tags`, `read_tags`, diagnostic buffer, alarms, modules (status, LEDs) and syslog.
  - In addition there is `read_watch_table_live` (not live-tested): a watch or tag table of the bound project is read offline, then its symbols are read online. Only names that can be mapped by a documented rule are read; the others are listed as not mappable.
- `plc_online_write` runs, in read-write/full, up to 50 ordered online changes through the guarded pipeline:
  - `change_operating_mode` (RUN/STOP), `write_tags`, `acknowledge_alarm`, `flash_leds`, `set_system_time`, `create_backup` and `restore_backup`.
  - read-write asks once per actual call; full runs under policy.
  - `dryRun:true` shows a preview without contacting a controller.

Both use the official Siemens client `Siemens.Simatic.S7.Webserver.API` in the host.

The user defines the controllers, their addresses, certificate rules and the environment variables with the credentials in a file. It is passed with `--plc-webapi-config` (or `TIA_MCP_PLC_WEBAPI_CONFIG`).

The tools name only a configured `target`. Credentials never appear in arguments, results or the log. See the [PLC online reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_WEBAPI_SUMMARY.md) with the prerequisites in TIA Portal and the live acceptance steps.

### Siemens documentation (not live-tested on the Windows PC yet)

- `siemens_docs` reads, in every mode and without TIA Portal, the official Siemens documentation on `docs.tia.siemens.cloud` (TIA Portal, STEP 7, WinCC, Openness, hardware) and `docs.industrial-operations-x.siemens.cloud` (SIMATIC AX, Industrial Edge):
  - `search` (full text, filters for product, version and language; each hit names the other versions of the same topic),
  - `read_page` (one topic as Markdown, in pages of up to 40,000 characters),
  - `get_toc` (outline of a publication or of a chapter) and `list_publications`.
- Only the allowed hosts are contacted (`--docs-hosts`); references are resolved to ids and never fetched as given; no redirects, a minimum interval per host, timeouts and size caps.
- It is an own C# port of [Czarnak/siemens-docs-mcp](https://github.com/Czarnak/siemens-docs-mcp) (MIT); the Python package is not installed. See the [Siemens documentation reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/SIEMENS_DOCS_SUMMARY.md) with the live endpoint checks.

### Writing PLCs

`plc_write(operations, dryRun=false)` runs up to 50 writes to the PLC software in the caller's order. The target is exactly one verified, already open project.

**Operations:**
- **Blocks, types and groups:** `update_block_logic`, `update_type_content`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`.
- **Tags and constants:** `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag` (`create_tag` and `update_tag` set the tag comment of one language with `comment` and `commentCulture`, read back; not live-tested), `move_tag` (guarded re-creation in another table), `create_user_constant` / `update_user_constant` / `delete_user_constant`.
- **Watch and force tables, program:**
  - `create_watch_table` / `delete_watch_table`, `create_watch_table_group` / `delete_watch_table_group`.
  - `update_watch_table_content` / `update_force_table_content` as an offline round trip through SimaticML; forcing online is not possible.
  - `update_program`.
- **Granular block edits** (not live-tested; know-how-protected blocks are refused for copy/move because their SimaticML export contains only the interface; fail-safe blocks follow TIA Portal's login rule, see below):
  - `add_interface_member` / `update_interface_member` / `delete_interface_member`: the exported SimaticML document is edited and imported again with the hash of the export.
  - `update_db_member`: DB start values and member attributes at any depth.
  - `create_instance_db`, `rename_block`, `renumber_block`, `copy_block`, `move_block` and `move_tag_table`.
- **LAD/FBD/SCL networks as text through SIMATIC SD** (live-tested on V21 on 2 Nov 2026: SD export, `update_block_logic` sd, the three network operations and `create_block_from_document`, see the [live report of 2 Nov 2026](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md); [SIMATIC SD reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SIMATIC_SD_SUMMARY.md)): the route that the Siemens V21 skill "simatic-sd" names for LAD/FBD/SCL networks in existing blocks.
  - `insert_network` (code, optional `language` and `position`), `replace_network` (`networkNumber`, code; title, comment and all other pragma keys are kept) and `delete_network`: planned on the SD export and imported hash-checked through `update_block_logic` with `format:"sd"` (`ImportFromDocuments` with `ImportDocumentOptions.Override`); the `.s7res` remains unchanged. Several items on the same block in one call are re-planned one after another.
  - Code may be a RUNG body, a whole `NETWORK … END_NETWORK` (with or without pragma) or SCL; inner pragmas such as `{ S7_Templates := "…" }` are preserved. Only what would destroy the document is refused (more than one network, unbalanced RUNG/END_RUNG, title keys); TIA Portal decides on the LAD semantics.
  - Verified by re-export (`networkCode`: count, position, language). Whether the export shows the same text is reported as `codeMatchesExport` in the message – it is reported that V21 exports some instructions differently from how they are imported; live on 2 Nov 2026 on V21 this was not reproduced for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF and Add (only whitespace differed). The info guard `plc_sd_reimports_export_form` remains for untested instructions; compile with `compile_check` afterwards.
  - `create_block_from_document` creates a new block in `targetGroupPath` from `.s7dcl` and optionally `.s7res` (a block without texts needs no `.s7res`; TIA Portal's own export then has none either); a block with the same name anywhere in the PLC is replaced only with `overwrite:true`.
  - Every language in the `.s7res` must be a project language: live, an import with `en-US` texts failed in a German project („Cannot import multilingual text with culture 'en-US' …“), with only `de-DE` it succeeded. The rule "en-US for every entry" (reported by BetterTIA/1748477582) is thereby refuted live; the result names the contained languages.
  - A header pragma `S7_Safety := "TRUE"` marks an F-block: the same rules apply as for the fail-safe controllers below.
- **Adopted from BetterTIA** (not live-tested; [BetterTIA operations reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md)):
  - `set_network_text` sets the title and/or comment of a network (number from 1, optional `culture`; a language that is still missing is created), `replace_network_text` replaces `{ find, replace }` pairs in one pass (literally, longest match first, swaps possible) in all network titles and comments, with `textScope:"all"` also in the block title/comment and in GRAPH texts. Openness has no setter for this: the exported SimaticML is edited and imported again hash-checked through `update_block_logic`; verification is by re-export (info guard `plc_text_edit_reimports_block`, compile afterwards).
  - `rename_members` renames several interface members of a block or PLC data type together (swaps possible) and, like the TIA Portal editor, adjusts the accesses in its own code (LAD/FBD/STL/SCL/GRAPH). It does not change accesses in other blocks, HMI tags and watch tables – TIA Portal does not either; read `analyze_change_impact` beforehand, compile afterwards. Verified by re-reading the interface.
  - `rename_tags` renames PLC tags across all tag tables of a PLC, `rename_objects` renames blocks, PLC data types and technology objects by name – several at once, with swaps and rotations through temporary names, all in one `ExclusiveAccess` transaction: if a step fails, everything is rolled back. Fail-safe tags and F-blocks follow TIA Portal's login rule (info guard `plc_batch_rename`).
  - `replace_tag_comments` replaces `{ find, replace }` pairs in the multilingual comments of the PLC tags (one table or all, optionally only one language) in one transaction; the preview lists every changed text, verification is by re-reading with `read_tag_comments`. It changes only existing texts; the first comment of a tag is set by `update_tag` with `comment` and `commentCulture`.
  - `create_db_snapshot` takes, with the PLC connected online, a snapshot of the actual values of a DB (`ValueService.CreateSnapshot()`), which TIA Portal stores in the offline project, and returns it. Nothing is written to the PLC, start values remain unchanged (info guard `plc_stores_db_snapshot`).
- **External sources, groups, software units and file exports** (not live-tested; [external sources and folders reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SOURCES_FOLDERS_SUMMARY.md)):
  - External sources: `import_external_source`, `generate_blocks_from_source`, `delete_external_source` and `export_source_file`.
  - Renaming groups: `rename_block_group`, `rename_type_group`, `rename_tag_table_group` and `rename_watch_table_group`.
  - Creating and deleting groups: `create_type_group` / `delete_type_group`, `create_tag_table_group` / `delete_tag_table_group`.
  - Software units: `create_software_unit`, `delete_software_unit` and `rename_software_unit`.
  - File exports: `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv` and `export_blocks`.
  - Tag tables as SimaticML files: `export_tag_table` (one table through `PlcTagTable.Export` into a file, with hash) and `import_tag_table` (import of a private copy of the file in one transaction; with `overwrite:true` through `ImportOptions.Override`, info guard `plc_import_replaces_tag_table`; with `tableName`, the table is checked before and after). Both Siemens members are documented in the Siemens skill "tags-and-tagtables"; not live-tested ([reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions)).
  - OPC UA export: `export_opcua_xml` writes the OPC UA data of the CPU of a PLC as an XML file (`OpcUaExportProvider.Export`, bound at runtime; idea from heilingbrunner/tiaportal-mcp, MIT; not live-tested).
  - For `export_source_file` and the file exports:
    - absolute paths on a drive or a UNC share (`\\server\freigabe\...`); only device paths (`\\?\`, `\\.\`) are refused, because they bypass Windows path normalization,
    - mapped network drives are allowed and reported with a warning,
    - no overwriting without `overwrite:true`,
    - every written file is read again and checked by its SHA-256 hash.
- **PLC alarms** (not live-tested; [PLC alarms and text lists reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ALARMS_TEXTLISTS_SUMMARY.md)):
  - `update_text_list` / `delete_text_list`; system text lists are refused.
  - `update_text_list_cells` / `update_alarm_text_cells` / `update_supervision_cells`: hash-checked cell changes on TIA Portal's XLSX export. They are imported again under `ExclusiveAccess` and verified by a re-export. Any change that was not requested makes the item fail.
  - `export_*` / `import_*` for text lists, alarm texts, the shared alarm classes, ProDiag supervisions and the project's system diagnostics settings (`export_system_diagnostics_settings` / `import_system_diagnostics_settings`, project-wide, not live-tested; the provider is found among the project's services at runtime):
    - absolute paths on a drive or a UNC share; only device paths (`\\?\`, `\\.\`, `\??\`) are refused,
    - no overwriting without `overwrite:true`,
    - mapped network drives are allowed and reported,
    - imports are verified by a re-export where one is documented.
  - `set_prodiag_assignment` and `export_prodiag_info`.
  - TIA Portal dialogs are answered by `portalConfirmations`, by default with decline.
- **Technology objects** (not live-tested; [technology objects reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/TECHNOLOGY_OBJECTS_SUMMARY.md)):
  - `create_technology_object` / `delete_technology_object` / `rename_technology_object`.
  - Groups: `create_technology_object_group` / `delete_technology_object_group` / `rename_technology_object_group`.
  - `set_technology_object_parameter`: converted to the type of the current value and read back.
  - `connect_technology_object_hardware` / `disconnect_technology_object_hardware`: documented `Connect` overloads, verified by reading back the connection state and the requested target.
  - `export_technology_object` / `import_technology_object`: SimaticML files at local paths.
  - `update_technology_object_content`.
- **Loadable files** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#loadable-files-plc_write)): `plc_write` `generate_loadable` writes a loadable file for blocks (`blockPaths`) or software units (`unitNames`) of one PLC, target `Plc` or `PlcSim` (`LoadableProvider.GenerateLoadable`, found at runtime among the PLC software's services); the file follows the export file rules (no overwrite without `overwrite:true`, re-hashed).
- **Block know-how and write protection** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#block-know-how-and-write-protection-plc_readplc_write)): `plc_read` `get_block_protection`; `plc_write` `set_block_know_how_protection`, `remove_block_know_how_protection`, `define_block_write_protection`, `set_block_write_protection`, `remove_block_write_protection`, `change_block_write_protection_password` (`PlcBlockProtectionProvider`, `PlcBlockWriteProtectionProvider`, found at runtime among the block's services); passwords only from password sources (environment variable, Windows credential, password map purposes `KnowHowProtection`/`BlockWriteProtection`), checked against TIA Portal's invalid characters, verified by reading the protection state back; fail-safe blocks follow the safety login rule.
- **Project languages and project texts** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-languages-and-project-texts-plc_readplc_write)): `plc_read` `get_project_languages`; `plc_write` `set_project_languages` (activate and deactivate languages, set the editing and reference language, read back), `export_project_texts` (XLSX for translators: source and target languages, optional text categories and devices) and `import_project_texts` (the translated file, verified by a re-export whose rows must show every imported row).
- **Further CPU settings** (issue #15; not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.md)): `plc_read` `get_cpu_settings` and 20 `plc_write` operations for account locking, the PLC password policies, syslog (CPU and project servers), system web pages, the default web application, watch/force table access for web server and display, the display password (only from a password source), the front panel logo, the project language rows, IP accessibility and the web DB generation. Changes that lower protection fire `plc_lowers_cpu_protection`; every change is read back.
- **Web server and OPC UA users** (issue #13; not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.md#web-server-and-opc-ua-users-issue-13)): `create_webserver_user`, `update_webserver_user`, `delete_webserver_user`, `update_simple_webserver_user`, `create_opcua_user`, `set_opcua_user_password`, `delete_opcua_user`; passwords only from password sources (`passwordVariable`), users and permissions read with `get_cpu_settings`; effective after a download.
- **UMAC: roles, project users, function rights, project protection** (issue #12; not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.md#umac-roles-project-users-function-rights-project-protection-issue-12)): `plc_read` `get_umac` (whether the project is protected, roles, users, rights per PLC) and 19 `plc_write` operations for custom roles, engineering and device function rights, project users (passwords only from password sources), role assignments, the anonymous user, UMC users and the UMAC password policy; every item fires the info guard `plc_changes_project_access`. Opening a UMAC-protected project with credentials and online UMAC logins are not part of it (see the reference).
- **CPU certificates** (issue #14; not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.md#cpu-certificates-and-secure-communication-issue-14)): `plc_read` `get_cpu_certificates` lists the CPU's certificates with their SHA-256 fingerprints, the values the TLS prompts of `plc_download`/`plc_tia_online`/`plc_station_upload` show for `trustedCertificates`; 11 `plc_write` operations create, import, export, delete and assign certificates and set the dynamic certificate management. Nothing is trusted automatically, legacy communication without TLS is never enabled.
- **Safety printout** (issue #21; not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.md#safety-printout-issue-21)): `plc_write` `generate_safety_printout` writes the F-CPU's safety printout (`SafetyPrintout.Print`, PDF or XPS printer driver, layout, `All`/`Compact`) to a file. It is a document for the safety acceptance by a qualified person; the MCP never replaces that acceptance.
- **Safety Validation Assistant** (issue #25; not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SETTINGS_SUMMARY.md#safety-validation-assistant-activation-tests-and-test-report-issue-25)): `get_safety_validation`, `check_activation_test` and 14 `plc_write` operations prepare activation tests, safety functions and conditions, export tests and generate the test report. The MCP never records a test result (no `TestState`, no imports or copies that could carry results); every change fires the acknowledge guard `plc_safety_acceptance_required`.
- **OPC UA server interfaces** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#opc-ua-server-interfaces-and-access-rules-plc_read-plc_write)): `plc_read` `get_opcua_configuration` reads the server interfaces (companion specifications, own interfaces), SIMATIC interfaces, reference namespaces, namespace access restrictions and role mappings of each PLC; `plc_write` creates, deletes, exports, imports and enables them and maintains the restrictions, roles and namespace permissions (`OpcUaProvider`, found at runtime among the PLC software's services).
- **Download to a folder, online master secret, R/H download** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#download-to-a-folder-and-the-online-master-secret-plc_write)): `plc_write` `download_to_folder` writes a PLC's (or R/H system's) download data into a Windows folder without a device (`DownloadProvider.Download(DirectoryInfo, ...)`, files re-hashed); `set_plc_master_secret_online`/`reset_plc_master_secret_online` set or delete the master secret of the online PLC (`OnlineProvider.SetPlcMasterSecret`/`ResetPlcMasterSecret`; secret only from a password source; acknowledge guard, read-write asks every call; not readable back, so notVerified); `plc_download` `rhTarget` downloads to the primary or backup CPU of an R/H system, under the existing download guards and confirmation. User management data during a download is the `UserManagementDownload` decision. Package G adds `load_snapshot_as_actual_values` / `load_start_values_as_actual_values` (actual values of a DB in the online PLC, acknowledge guard, confirmed in read-write).
- **V21 API completion (package G)** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/V21_API_COMPLETION_SUMMARY.md)): the documented V21 Openness methods a name comparison with the API description found unused, in the existing tools: Multiuser markings (`get_markings`, `get_mark_state`, `mark_objects`, `unmark_objects`), `protect_project`, UMC users and groups (`create_offline_umc_user(_group)`, `import_umc_user`, `get_umc_server_user`, `check_umc_consistency`), engineering objects by path (`get_object_identifier`, `find_object_by_identifier`, `get_creation_infos`, `get_linked_tags`), cam data (`save_cam_data`, `load_cam_data`), actual values of an online DB (`load_snapshot_as_actual_values`, `load_start_values_as_actual_values`), hardware features in `network_read`/`network_write` (subnets of a node, IO controller, IM connection, PC interface assignment, module description, telecontrol data points, communication connections) and `hmi_write` `resize_screen`, `set_log_duration`, `set_opcua_alarm_type_node`; the left-out list names every method not built and why.
- **Offline change detection** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#offline-change-detection-plc_read)): `plc_read` `get_plc_checksums` reads the software and text list checksums of each PLC (`PlcChecksumProvider`; empty means not compiled or changed since the last compile), `get_fingerprints` the fingerprints of one block or PLC data type (`FingerprintProvider.GetFingerprints()`). The providers are found at runtime among the object's services; values are reported as text with their runtime type.
- **Project compilation settings** (not live-tested; [reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-block-compilation-settings)): `set_project_compilation_settings` sets „Simulation während Bausteinübersetzung unterstützen“ (support simulation during block compilation; `simulationDuringBlockCompilation`, according to Siemens required before a Softbus download to PLCSIM Advanced) and the support for virtual PLCs (`virtualPlcDuringBlockCompilation`) through the same services that `get_project_status` reads. Project-wide, without `plcName`, standalone projects only; the Siemens member is bound at runtime (`api_member_missing` when neither a property nor a writable attribute exists) and read back.
- **CPU protection, also for password-protected projects** (not live-tested; [CPU protection reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.md)): changes the offline project, effective after `plc_download`. Passwords come only from password sources (environment variable, Windows credential, mapping file), never as values; the server generates none.
  - `set_plc_access_level` sets every level of the TIA Portal dialog: `FullAccessIncludingFailsafe`, `FullAccess`, `ReadAccess`, `HMIAccess`, `NoAccess`. The passwords of the more open levels come from `accessLevelPasswords` (for example `{ "FullAccess": { "credential": "TiaMcp/Projekt/PLC_1/Vollzugriff" } }`) or from the mapping file (purpose = name of the level); `resetAccessLevelPasswords` removes level passwords.
  - `protect_plc_confidential_data` protects „Vertrauliche PLC-Konfigurationsdaten“ (confidential PLC configuration data) with a password (`Protect`, with `allConfiguration` additionally all loadable configuration data), `change_plc_confidential_data_password` changes it, `disable_plc_confidential_data_protection` turns the protection off (`Unprotect` with or without password). Purpose in the mapping file: `PlcMasterSecretPassword`.
  - Weaker protection triggers the info guard `plc_lowers_cpu_protection`, stronger protection `plc_raises_cpu_protection`. If a Siemens member is missing, the item fails with `api_member_missing` without changing anything.
  - `plc_download`, `plc_tia_online`, `plc_station_upload` and now also `compare_to_online` (`passwords`) answer every TIA Portal password prompt (module read/write access, `PlcMasterSecretPassword`, online read access) from password sources and the mapping file.
- **Fail-safe controllers (F-CPU)** (partly live-tested on 2 Nov 2026, see the [live report of 2 Nov 2026](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md); [F-program reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SAFETY_SUMMARY.md)):
  - `safety_login` / `safety_logoff`, `set_safety_password` / `revoke_safety_password`; `passwordVariable` always names only the source of the password (environment variable or Windows Credential Manager entry, see [Passwords](#passwords-environment-variable-or-windows-credential-manager)), never the password.
  - The server is not stricter than TIA Portal: F-blocks can be changed with all operations that TIA Portal offers for them (`update_block_logic` as SimaticML, source or SIMATIC SD, the SD network operations and `create_block_from_document`, interface editing, `update_db_member`, `delete_block`, `rename_block`, `renumber_block`, `copy_block`, `move_block`). A login (`safety_login`) is only needed when the safety program has a password; without a password TIA Portal allows the change without login, and so does the server. What the server cannot judge (no SafetyAdministration, state not readable) is decided by TIA Portal (info guard `plc_safety_tia_decides`); if TIA Portal refuses, the operation fails with its message.
  - `create_safety_runtime_group` creates an F-runtime group (same login rule); `set_safety_runtime_group` assigns the main safety block (and, for an F-FB, its instance DB) to an existing F-runtime group. The Openness member for this is undocumented and is bound at runtime; live on V21 (2 Nov 2026) there was neither the property nor the attribute `MainSafetyBlock` (`api_member_missing`). Before giving up, the server tries `SetAttribute("MainSafetyBlockName", Name)`, but only when `GetAttributeInfos()` reports this attribute as writable, and names all reported attributes with their access mode in the error message (checked offline). Otherwise, repair in TIA Portal (Safety Administration).
  - Live on 2 Nov 2026 (twice, also with `b79b2df`): every SIMATIC SD import of the main safety block removes the F-runtime group's reference (`MainSafetyBlockName` empty, the PLC no longer compiles); the SimaticML import keeps it. Therefore the server never imports an SD document directly onto the main safety block (`update_block_logic` sd, `insert_network`/`replace_network`/`delete_network`, `create_block_from_document` with `overwrite`), but in one step: as a temporary F-block `zzMcpTmp_<Baustein>_<8 Hex>` in its group, compiles it, exports it as SimaticML, inserts the name, number and AutoNumber of the main safety block, imports that as SimaticML onto the main safety block and deletes the temporary block. If the temporary block does not compile, the main safety block remains unchanged and the operation fails with the compiler messages. The result names the route (`mainSafetyBlockRoute`, `route: "sdViaXmlForMainSafetyBlock"`). Proven live by hand, checked automatically only offline. After every import the server still checks the F-runtime groups (`safetyRuntimeGroup`); info guard `plc_import_may_unlink_main_safety_block`.
  - `list_safety_blocks` no longer fails when a block offers `SafetySignatureProvider` but `GetService` does not return it (seen live, defect D13): the signatures of this block are then reported as `unreadable`.
  - Every F-change carries the guard `plc_safety_acceptance_required`: in read-write the server asks once, in full it is satisfied by policy. A safety program must be validated and accepted by a qualified person; the MCP server never replaces the safety acceptance.
  - `plc_download` answers the F-program prompt `SafetyProgram` for PLCSIM and for real F-CPUs. Towards real hardware it counts like any hardware download (guard `download_to_hardware`) and additionally carries the guard `download_includes_safety_program` with the acceptance notice: read-write asks on every call, full runs under policy.

**Flow:**
- `dryRun:true` shows effects and guards without changing anything. No mode asks.
- The phases are `preview`, `applied`, `blocked` and `error`.
- Execution stops at the first failure and is never rolled back. Re-read with `plc_read` before a retry.
- Later items may use objects that earlier items of the same call created.

**Name collisions:**
- Creates are not checked for collisions before the change. The exceptions are technology objects and their groups; there, existing names are refused.
- Live cases within the scoped coverage showed:
  - the refusal of duplicate tags, tables, blocks and groups,
  - the refusal to delete the default tag table,
  - four further namespace directions.
- This is no guarantee for every collision.

**Failures, guards and verification:**
- A native `EngineeringTargetInvocationException` in a mutating worker method becomes `engineering_rejected`. Type, bounded message and inner chain are in `engineeringError`, in the language of the Portal UI.
- Guards cover the consequences of deletions, address overlaps and unreadable evidence.
- The single-OB check is kept because the native protection was not demonstrated.
- Verification reads back content only (`contentHash`), otherwise `trusted` (write contract `2.0`). See the [PLC reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md#failures-from-tia-portal).

**Content updates** (`update_block_logic`, `update_type_content`):
- They receive the document as `content` together with `expectedContentHash`, the `contentHash` from a `plc_read` of the same object in the same format.
- If the content was changed since that read, the call is refused with `state_changed`.
- A document over the value budget is omitted when read, has no hash and cannot be changed through `plc_write`.
- The budgets are 60,000 characters per value and 180,000 per response by default. They can be raised at startup with `--max-item-chars`/`--max-document-chars` (see [response sizes](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/mcp-client-configuration.md#response-size-limits)).
- `preview_write_batch`, `apply_write_batch` and `execute_read_batch` were removed; their operations now live in `plc_write` and `plc_read`.

**Warnings:** Every operation result can carry a `warnings` array. It contains non-fatal notices about limitations from the TIA Openness worker. A populated `warnings` means that the result may be incomplete.

**Formats and hashes when reading:**
- Available `plc_read` operations of this group: `get_block_content`, `get_type_content`, `list_tag_tables`, `list_download_targets`, `get_block_interface`, `get_type_interface`, `get_db_values` and `get_db_snapshot`.
- `get_block_content` / `update_block_logic` and `get_type_content` / `update_type_content` accept a `format` field.
  - `format=source` exists for global data blocks, PLC data types and FB/FC/OB in SCL.
  - `format=sd` (blocks only) reads and writes the SIMATIC SD documents `.s7dcl`/`.s7res` (`ExportAsDocuments` / `ImportFromDocuments`); what TIA Portal does not export fails with its message (live-tested on 2 Nov 2026; a block without texts returns only the `.s7dcl`).
  - All other block languages stay with `format=xml`.
- `withDependencies` (`plc_read` only, default `false`) lets TIA Portal include the object's dependencies.
  - The result declares several objects and serves **only as context**.
  - A write refuses every source with more than one object, and the read carries a corresponding warning.
  - Without the field you get a document that you can edit and write back.
- `get_block_content` and `get_type_content` additionally return a `contentHash` (`xml:sha256:<hex>`, `source:sha256:<hex>` or `sd:sha256:<hex>`).
  - It is computed over exactly the text in `result` and labeled with the returned format.
  - This lets a later guarded write detect whether the document has changed since the read.
  - It is absent for reads with `withDependencies`, for failed reads and for results that were truncated or omitted because of their size.

### Network operations

`network_read` and `network_write` both declare an MCP output schema. They return one canonical JSON document, identical as the `content` text block and as `structuredContent`, never as a nested JSON string in an outer envelope. `network_read` uses contract version `1.0`, `network_write` `2.0`; both have warning arrays at the root and explicit null values. The exact envelopes are described in [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

- `network_read` runs up to 50 network read operations. The read operations run independently of each other.
  - Operations: `read_hardware_config`, `search_equipment_catalog`, `list_network_objects`, `inspect_network_object` (also every hardware parameter of a device item).
  - Hardware modules: `list_plug_locations`, `find_module_types`, `find_container_types`, `list_io_addresses`, `get_custom_identity` (a user-defined key on a device or device item; not live-tested) `list_port_connections` (the PROFINET port links of a device or device item; not live-tested) and `list_transfer_areas` (the transfer areas of an interface; not live-tested).
  - Catalog searches can be limited with `query` and `maxResults`.
  - `read_hardware_config` accepts an optional `deviceName` filter and optionally `plcName` for tag matching.
  - Structured I/O data is returned only on request: `includeIoDetails` for addresses and channels, `includeTagMatches` for exact PLC tags per channel. Detailed I/O output can become large; therefore filter by `deviceName` or split it across several calls.
- `network_write` previews or executes up to 50 network writes.
  - `add_network_device`: flat `typeIdentifier`/`deviceName`, because it names something that does not exist yet.
  - `configure_network_device`: nested `target: { deviceName, nodeId }` plus `changes: { ipAddress?, subnetMask?, pnDeviceNameAutoGeneration?, pnDeviceName?, subnet?: { subnetId }, ioSystem?: { subnetId, number } }`. A null value in `changes` leaves that setting unchanged.
  - `create_subnet`: `subnet: { name, networkType }`, only for PROFIBUS additionally `highestAddress`/`transmissionSpeed`.
  - `update_subnet`: `target: { kind: "subnet", subnetId }` plus `subnetChanges` with at least one member.
  - `delete_subnet`: `target: { kind: "subnet", subnetId }`, connected or not.
  - `create_io_system` (not live-tested): creates a PROFINET IO system on the PROFINET node of a CPU, `target: { kind: "node", deviceName, nodeId, interfacePath }` as `read_hardware_config` reports it, optionally `ioSystem: { name }`. `configure_network_device` `changes.ioSystem` takes `subnetId` and `number` from the result in the next call to assign an IO device (e.g. ET 200SP).
  - Hardware modules (not live-tested; see the [hardware modules reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/HARDWARE_MODULES_SUMMARY.md)): `plug_device_item`, `move_device_item`, `copy_device_item`, `delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`, `set_custom_identity`, `remove_custom_identity` (custom identities, read back with `Get`), `connect_ports`, `disconnect_ports` (PROFINET topology, read back from `ConnectedPorts`), `create_transfer_area`, `update_transfer_area`, `delete_transfer_area`, `delete_io_system`, `disconnect_from_io_system` (issue #11; V21 has no separate member to create a DP master system, `create_io_system` on a DP master node lets TIA Portal decide), `export_cax`, `import_cax` (CAx data as AutomationML, issue #9) and `export_io_map`.
  - **Execution:**
    - `dryRun:true` shows a preview; `dryRun:false` or a missing `dryRun` executes, without a server prompt in read-write/full.
    - Legacy fields `confirm` and `acknowledge`, unknown root members and a non-boolean `dryRun` are refused before the tool call.
    - Writes need a verified binding to exactly the already open project.
    - They run one after another and stop at the first failure; later items are marked as skipped, completed writes are not rolled back.
    - The failed item carries a warning: this and every earlier operation of the same call may already have changed the TIA state. Re-read with `network_read` before a retry instead of blindly re-running the batch.

`configure_network_device` selects exactly one device with exactly one node and exactly one subnet/IO tuple, without falling back to the first match.
- Applied settings are checked immediately and at the end; unreadable evidence cannot pass.
- Requested omissions make the item fail; the typed partial results are kept.
- Deleting connected subnets is only a notice. An incomplete inventory of the consequences is reported (`network_state_unverifiable`, informative); the item runs, TIA Portal decides.
- The results carry `phase:applied` even when execution or verification fails (`error:null`, MCP `isError:false`).
- Check the current state before a retry. An omission in the output can report `success:false` for the delivery while the actual execution succeeded.
- Guards, partial results and recovery after omissions are described in the [network contract](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope).

`read_hardware_config` additionally reports unreadable members in a `messages` array.
- Name and type identifier fields of devices and modules omit unreadable values instead of returning placeholders such as `0` or an empty string.
- Some subordinate name fields still fall back to an empty string; the error is then in `messages`.
- Hardware configuration data is engineering evidence, not confirmation of a commissioned plant.

`read_hardware_config` can return structured I/O data on request.
- With `includeIoDetails: true`, every device item carries an `ioDetails` object:
  - `addresses[]` with Openness `Address` evidence: `ioType`, byte-based `startAddress`, `length`, dynamic `context` and ordered `controllerNames`.
  - `channels[]` with `number`, `ioType`, `type`, bit-based `channelAddressBits`, `channelWidthBits` and a formatted `logicalAddress` such as `%I4.0`/`%IW64`. The latter appears only when the evidence is present and aligned.
- With `includeTagMatches: true` (requires `includeIoDetails`), every channel carries `tagMatches[]`:
  - They are resolved unambiguously against the tag tables of exactly one PLC.
  - `plcName` selects the PLC by its exact name; otherwise the matching exists only when exactly one PLC exists.
  - A tag matches a channel only when its normalized absolute I/O interval equals that of the channel. Matching never happens across controllers.
- A normal read without switches returns no `ioDetails`.
- Exact request and response shapes as well as the units are described in [docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

Large hardware reads can page with `pageSize` (`1..200`) or `cursor`.
- The pages count devices first, then subnets in a fixed order; the two public arrays stay separate.
- Because of the canonical size limit, a page can return fewer complete entries than requested.
- Follow `pagination.nextCursor` until it is absent or null. Do not change the project, filter and detail switches while doing so.
- Requests without both fields use the non-paged path.
- Cursors are valid only in the running process and do not survive a host restart.
- Recovery and omissions are described in the [network reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#hardware-configuration-pagination).

### Project tools

- `bind_project` adopts an already open project in every mode with the default action `bind`.
  - Without a path, the only open project is adopted or the candidates are listed. To switch, set `forceRebind:true`.
  - The explicit `list_portals` discovers without adopting.
  - Five inventory actions for the Project Server inspect the persistently attached instance without changing the project binding.
  - Re-attaching can show TIA's Openness access dialog, which a human must answer.
  - See [Multiuser inventory and acceptance limits](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md).
- `project_server_write(operations, dryRun=false, portalProcessId)` in read-write/full (not live-tested): the Project Server (Multiuser) administration of one TIA Portal instance, without a bound project.
  - `create_server_connection`, `set_server_connection` (host, port, protocol), `delete_server_connection`, `add_project_to_server` (root or group), `create_local_session` (then bind with `open_project` and the returned `.als21`), `delete_local_session_from_server`.
  - Deleting a local session or a connection and uploading a project are acknowledge guards: read-write asks once per call, full runs under policy. No item takes a password. The caller names every server; never use it on a server the user did not name.
  - Read back through the `bind_project` inventory; see the [Project Server reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_SERVER_WRITE_SUMMARY.md).
- `get_project_status` shows the metadata of the active project without opening, binding or switching projects; it takes no arguments.
- `browse_project_tree` browses a canonical, paged snapshot of the project tree (v3).
  - Optionally with typed header data of PLC blocks in the `details` of the block nodes: author, version, family and header name. Enabled by default are the fields `HeaderAuthor`, `HeaderVersion`, `HeaderFamily` and `HeaderName`.
  - Parameters: typed `startSelector`, `depth` and `pageSize`; continue with the returned opaque `cursor`.
- `compile_check` compiles and returns the compiler messages; available in read-write and full.
  - `scope` `software` (default) compiles only the PLC program (or, with `blockPath`, one block), **not** the hardware configuration. Errors that only show up during download (e.g. a protection level without password) remain invisible this way.
  - `scope` `hardware` compiles the hardware configuration of the CPU of every PLC (report with `scope` `hardware`). Run both before `plc_download`.
- `plc_download` downloads the PLC of the bound standalone project to a PLC simulation (PC interface `PLCSIM`) or to a real controller (any other PC interface). Not live-tested; see the [download reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_DOWNLOAD_SUMMARY.md).
  - **Access mode:** Only in read-write and full; the access mode is the human's control.
    - read-write asks once per actual call; the prompt says when the target is real hardware.
    - full runs under policy.
    - `dryRun:true` shows a preview without touching the target.
  - **Guards:** There is no blocking guard; `download_to_hardware` and `download_stops_plc` must be acknowledged.
  - **Path:** Mode, PC interface `{ name, number }` and target interface are resolved exactly from `plc_read` `list_download_targets`, without fallback.
  - **Download configurations** of TIA Portal are recognized by their exact type and answered:
    - by built-in defaults (`TargetForSoftware` is `PlcSimulationAdvanced` on PLCSIM, otherwise `CPU`),
    - by the caller's `decisions` (selection name or `true`, keyed by the type's short name),
    - by `passwords` (names of environment variables or Windows credentials, never the password itself; see [Passwords](#passwords-environment-variable-or-windows-credential-manager)).
    - An unanswered configuration aborts and names what a retry needs.
  - **TLS certificate** (not live-tested; [download reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)):
    - If TIA Portal asks during connection setup through `OnlineLegitimation` (phase `online_legitimation`) whether the device's certificate should be trusted (`TlsVerificationConfiguration`), this is accepted only with `trustedCertificates`.
    - `trustedCertificates` are SHA-256 fingerprints that the human has checked: 64 hex characters each, case-insensitive, colons and spaces are removed, at most 16.
    - `Trusted` only when every presented certificate is listed; otherwise `NonTrusted` and abort with evidence in `abort.tls` (since K27 without an exception in TIA Portal's callback: live on 2026-11-02 TIA Portal terminated when this callback threw an exception; not live-tested) (per certificate fingerprint, subject, issuer, valid from/to; plus `plcName` and `verificationInfo`).
    - The human compares the fingerprints, for example with „Zertifikate anzeigen“ in TIA Portal; only then retry with `trustedCertificates`. The read-write prompt lists the fingerprints.
    - No default trust, no "trust everything", `EnableLegacyCommunication` is never set; a `decisions` entry cannot choose `Trusted`.
  - **Confirmation dialogs:**
    - Dialogs that appear during the call (until `Download` returns and the evidence is collected) are answered with No and make the call fail. With `portalConfirmations:"accept"` they are answered with Yes. In both cases they are recorded.
    - A dialog afterwards, for instance after an early return of `Download`, keeps the call's answer until the next worker request starts. It is then reported as a warning about a late confirmation (and in the server log), not in the download evidence.
  - `completionVerified` is always false.
- `plc_tia_online`: `go_online` / `go_offline` for a PLC of the bound standalone project. Only in read-write and full. Not live-tested; see the [PLC online reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_OPENNESS_SUMMARY.md).
  - `go_online` takes the exact path (mode, required PC interface `{ name, number }`, target interface) from `plc_read` `get_online_state`.
  - It applies it to the same online provider and polls the online state until `timeoutSeconds`.
  - Online passwords come only from named environment variables. A TLS certificate check is accepted only through `trustedCertificates`, as with `plc_download`. Any other legitimation (such as authentication) aborts with evidence.
  - TIA Portal confirmation dialogs are handled as with `plc_download`.
  - These are not the S7-1500 Web API tools (`plc_online_*`).
  - read-write asks once per actual call; full runs under policy.
- `plc_station_upload` uploads a station from a device address into the offline project. This is a project change; only in read-write and full, one prompt per actual read-write call. Not live-tested; see the [PLC online reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_OPENNESS_SUMMARY.md#plc_station_upload).
  - Upload configurations are answered only by the caller's `decisions` (type short name to enum member name) and `passwords` (names of environment variables).
  - An unanswered configuration aborts and lists its available selections.
  - The online legitimation of the connection (online passwords, TLS certificate through `trustedCertificates`) is answered as with `plc_tia_online`.
  - `portalConfirmations` works as with `plc_tia_online`.
- `open_project` / `create_project` / `retrieve_project` / `save_project` / `save_project_as` / `archive_project` / `close_project` are guarded single-call lifecycle writes, in read-write and full. `retrieve_project` (not live-tested) retrieves a project archive into a directory (`upgrade:true` for `RetrieveWithUpgrade`) and binds to the retrieved project.
  - read-write asks once per actual call; full runs without a server prompt.
  - `dryRun:true` shows effects and guards without change and without a prompt.
  - These tools offer structured output. They accept no public confirmation list and no safety token, and remain individual tools.

**Local sessions (Multiuser):**
- Local sessions use the observed typed `.amc21` path for adoption, basic status and startup checks on already open sessions.
- A writable `open_project` accepts an exactly existing `.als21` file.
- On a bound local session, the following work:
  - the readers `browse_project_tree`, `plc_read`, `network_read`, `read_cross_references` and `hmi_read`,
  - in addition `plc_write`, `network_write`, `hmi_write`, `compile_check` and `save_project`.
- A local `save_project` saves the session through `LocalSession.Save()` and never checks in to the Project Server.
- Create, save as, archive, close and other terminal operations remain unavailable.
- The scoped PR4 acceptance covers recovery with a preserved Portal in the online case and UI-assisted opening in the offline case with read-write. The tested offline openings required closing Siemens dialogs.
- See [Local session support and limits](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md).

`get_project_status` and `compile_check` offer structured output schemas with contract version `1.0`.
- Text and `structuredContent` contain the same canonical document; the typed data is under `result.value`.
- Compiler errors set `success:false` but keep the diagnostics, with MCP `isError:false`.
- Values that are too large are omitted entirely, with hints for a retry.
- See the [response contract for status and compilation](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#standalone-status-and-compilation-contract).

Callers of the project tree need `v3.0.0` or newer. The v2 input `startPath` and the bare nested array response were removed, not kept as aliases. The migration request, the complete response envelope, the reconstruction of selectors, resuming, the limits and recovery are described in the [project reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#browse_project_tree-v3).

### Claude in TIA: hand work to Claude from inside TIA Portal (not live-tested yet)

- The TIA Portal V21 Add-In "Claude in TIA" adds a context menu **Claude** to the project tree, Devices & Networks and the project and global library: An Claude übergeben…, Erklären, Prüfen, Überarbeiten, Dokumentieren, and Claude-Antworten anzeigen.
- A click writes a request (names and MCP address hints of the selected objects, no project data) into `%APPDATA%\TiaMcpServer\inbox`. Claude reads it with the tool `tia_inbox` (every mode) when you say "schau in TIA", works with the other tools under the access mode and answers with `tia_inbox reply_request`; the answer appears in TIA Portal.
- Optional: the Claude bridge (`TiaMcpClaudeBridge.exe`, off by default, switched on in the password manager) starts a headless Claude Code run per request, one at a time, never twice, with only the TIA tools allowed.
- One-time setup: `scripts\install-claude-in-tia.ps1` builds the Add-In against the installed TIA Portal, runs the Siemens Publisher and copies the `.addin`; then activate it in the task card Add-Ins. See the [Claude in TIA reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/CLAUDE_IN_TIA.md).

### Resources and prompts (not live-tested yet)

- Besides the tools the server offers MCP **resources** and **prompts**, in every access mode, without TIA Portal and without reading anything from your machine.
- Resources: every reference document of `docs/SupportedOperations`, the README and FORK_FEATURES in English and German, embedded at build time (`tia-mcp://docs/en/PLC_OPERATIONS_SUMMARY.md`, `tia-mcp://docs/de/...`, `text/markdown`), and the operation reference `tia-mcp://operations`, `tia-mcp://operations/{tool}`, `tia-mcp://operations/{tool}/{operation}` (for `plc_read`/`plc_write` the same entry as `describe_operations`).
- Prompts: `explain_block`, `review_block`, `revise_block_safely` (read with hash, plan, dry run, write with `expectedContentHash`, `compile_check`; never save or download without you), `document_project`, `check_hardware`, `safety_overview` (read-only; the acceptance stays with a qualified person), `download_to_plcsim` (targets, dry run, certificate fingerprints only after you approved them), `handle_tia_inbox`. Read-only lists the six that need no write tool.
- See the [resources and prompts reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/MCP_RESOURCES_PROMPTS_SUMMARY.md).

### Simulation project without password (not live-tested yet)

A project created with `create_project` can fail at hardware compilation when downloading to PLCSIM: the access level „Kein Zugriff (kompletter Schutz)“ (no access, complete protection) and „Vertrauliche PLC-Konfigurationsdaten schützen“ (protect confidential PLC configuration data) require a password that the PLC communication certificate also needs. For a test project without password, one call is enough:

```json
plc_write {
  "operations": [
    { "operationId": "zugriff", "operation": "set_plc_access_level", "plcName": "PLC_1", "accessLevel": "FullAccess" },
    { "operationId": "vertraulich", "operation": "disable_plc_confidential_data_protection", "plcName": "PLC_1" }
  ]
}
```

Then check with `compile_check` with `scope` `hardware`, and only then call `plc_download`. If the second item fails with `api_member_missing`, this TIA Portal installation does not offer `Unprotect()`; then clear the checkbox by hand in the CPU properties under „Schutz & Security“ (protection & security). Intended only for simulation and test projects, never for a real controller that needs protection.

**Real project with password protection:** Store the passwords once (see [Passwords](#passwords-environment-variable-or-windows-credential-manager)), then for example:

```json
plc_write {
  "operations": [
    { "operationId": "zugriff", "operation": "set_plc_access_level", "plcName": "PLC_1", "accessLevel": "HMIAccess",
      "accessLevelPasswords": { "FullAccess": { "credential": "TiaMcp/Anlage/PLC_1/Vollzugriff" }, "ReadAccess": { "credential": "TiaMcp/Anlage/PLC_1/Lesezugriff" } } },
    { "operationId": "vertraulich", "operation": "protect_plc_confidential_data", "plcName": "PLC_1",
      "passwordVariable": { "credential": "TiaMcp/Anlage/PLC_1/Vertraulich" } }
  ]
}
```

During the later `plc_download`, `passwords` (or the mapping file with `ModuleWriteAccessPassword`, `PlcMasterSecretPassword`) answer the password prompts. Details and the provenance of every Siemens member are in the [CPU protection reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.md).

**F-CPU (for example CPU 1516F-3 PN/DP):** Here use `accessLevel` `FullAccessIncludingFailsafe` („Vollzugriff inkl. Fehlersicherheit (kein Schutz)“, full access incl. fail-safe, no protection) instead of `FullAccess`. On an F-CPU, „Vollzugriff (kein Schutz)“ (full access, no protection) is not the top level: for fail-safe access (downloading and changing the safety program in the CPU), TIA Portal then still requires a password for the level „Vollzugriff inkl. Fehlersicherheit“, and exactly that is supposed to be dropped in a simulation project without password. This is the behavior of the TIA Portal dialog, not checked live here (step in the [F-CPU test plan](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/plcsim-fcpu-live-test-plan.md)). Independently of this, the password of the safety program (offline program, `set_safety_password`/`safety_login`, see [Passwords](#passwords-environment-variable-or-windows-credential-manager)) remains a separate protection layer; when downloading to PLCSIM, the safety program is downloaded along only with `decisions` `SafetyProgram`, and even a simulated safety program does not replace a safety acceptance.

## Write safety

**Flow and guards:**
- Every writing tool goes through, in one call: validate inputs, prepare the exact binding, resolve targets, evaluate guards, mutate, verify and audit.
- `plc_write`, `network_write`, `hmi_write` and `vci_write` register only blocking and informative guards and therefore never ask. The only exception: `plc_write` calls with F-operations (guard `plc_safety_acceptance_required`) ask once in read-write.
- For every writing tool, `dryRun:true` returns the phase `phase:preview`, without change and without a prompt.

**Prompts per access mode:**
- In read-write, every actual lifecycle, `plc_download`, `plc_tia_online` and `plc_station_upload` call needs a form prompt (elicitation) with explicit consent and boolean `confirm:true`. This also applies to calls with only informative guards or none at all.
- Unsupported clients, decline, cancel, timeout or transport failure deny with `access_denied`.
- Full mode satisfies acknowledge guards by policy, without a server prompt.
- Blocking guards stop the call in every mode. They now exist only where TIA Portal or Openness cannot carry out the change itself or where a safety rule applies (principle: the server is not stricter than TIA Portal; the complete list is in the [improvement log](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/IMPROVEMENT_LOG.md#tia-alignment-audit-2026-11-01)).
- `plc_online_write` follows the same rule: one prompt per actual read-write call with all online changes, in full mode under policy.
- Confirmation follows the access mode; the old startup switch was removed.
- `plc_download` has no blocking guard. Whether the AI may download, also to real hardware, is controlled solely by the access mode the server is started with: read-only never, read-write with one prompt per actual download, full without a prompt.

**Choosing the access mode:**
- Easiest in the program „TiaMcp Passwortverwaltung“, tab **Zugriffsmodus** (access mode; see [Password manager](#password-manager-windows-program-tiamcp-passwortverwaltung)). It stores the choice in `%APPDATA%\TiaMcpServer\settings.json` (or in the file from `TIA_MCP_SETTINGS`); the MCP client configuration then needs no `--access-mode`, `"args"` can be omitted entirely.
- Order at startup: argument `--access-mode` (or `--read-only`/`--read-write`) before the environment variable `TIA_MCP_ACCESS_MODE` before the settings file before the default read-write.
- The server reads the file only at startup and never writes it; no MCP tool can read or change it. A change takes effect after a restart of the MCP server (quit Claude Desktop completely and start it again).
- An invalid settings file prevents startup, even when an argument or the environment variable sets the mode. At startup the server reports the mode with its source (`cli`, `environment`, `settingsFile`, `default`); `tia-mcp doctor` shows it as well (`--json`: `accessModeSource`, `settingsFile`).
- Guards, requests, responses and recovery are described in the [lifecycle reference](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).

**Consent depends on the MCP client.**
- A prompt proves only that the client returned an accepting answer, not that a human saw it.
- Project-changing and lifecycle tools keep cautious hints about destructive effects. Anyone who wants an approval on every call does not add them to the client's auto-approval lists.
- `plc_write`, `network_write`, `hmi_write` and `vci_write` use `dryRun:true` as preview and execute by default, without token and without server prompt. The client's approval prompts remain independent of this.

**Binding and audit:**
- The lifecycle uses a pinned binding and checks at the transition:
  - Open and create can start unbound.
  - Save as binds to the resulting copy.
  - Close releases the binding.
- The lifecycle logs every call, including dry runs and refusals, in `writes-yyyy-MM-dd.jsonl` under `%LOCALAPPDATA%\TiaMcpServer\audit`. Audit v2 records the confirmation as `user`, `policy` or `none`.
- `plc_write`, `network_write`, `hmi_write` and `vci_write` also write one audit v2 record per entered call, including previews and refusals. The confirmation is `none` in read-write and `policy` in full.
- An argument refusal by the SDK before the call produces no write record. The old write audit stream was removed.

**Choice of TIA instance and project:**
- The worker never binds to the first enumerated TIA Portal and never chooses the first open project.
- It requires an exactly matching path or a genuinely single candidate. Several possible targets fail with `target_ambiguous` before anything is attached or changed.
- The server does not start when `--project` or `TIA_MCP_PROJECT_PATH` is not an absolute `.ap21` or `.amc21` path.
- A configured `--project` path is ready for writing only once a matching worker identity has been observed.
- If the identity deviates later, there is `binding_conflict`, and the session stays invalid until it is explicitly rebound.

**Opening and switching:**
- There is no implicit open: only `open_project`, `create_project` and `retrieve_project` open a project. For an already open project, use `bind_project`.
- Worker ownership is lost on detach; a re-attached project counts as owned by the UI.
- read-only never opens, creates, saves or closes a project, but an explicit binding may switch the selected session.
- Project tree cursors refuse binding changes, even a switch away and back to the same path.

**Pinned binding for writes:**
- `plc_write`, `network_write`, `hmi_write` and `vci_write` hold one verified project binding across planning, ordered mutation, verification and audit. A concurrent rebind therefore cannot redirect a planned write.
- Network and HMI do not re-plan per item and do not read back in the worker.
- HMI plans from a single initial set of public read evidence per HMI; these reads run one after another and are not atomic.
- Both domains record completed items as `trusted` or `superseded`.
- Before any execution, `plc_write` blocks only a duplicate singleton OB and F-changes on a password-protected safety program without login. Unreadable evidence, deletions and address overlaps are reported as notices; TIA Portal decides on everything else.

**Failure shapes:**
- A lifecycle refusal has a top-level `error` with `{category,message}` and MCP `isError:true`.
- An attempted write or a failed verification, by contrast, has `success:false`, `error:null` and `isError:false`, with typed failure evidence in `result` or `verification`.
- A change can have succeeded despite a failed verification; check the state before a retry.
- `save_project_as` still requires `rebind:true`, and warnings stay separate from success/failure.

**State of the migration:** The removed lifecycle inputs and the structured outputs are breaking changes, earmarked for the final major release of the redesign. The migrations for Network and PLC are implemented; the safety token core and the generic batch tools are removed. This step publishes no package.

### Passwords: environment variable or Windows Credential Manager

No tool accepts a password. `plc_download`, `plc_tia_online`, `plc_station_upload` (`passwords`) and the F-operations of `plc_write` (`passwordVariable`) name only the source, in one of three forms:

- `"PLC1_PW"` or `{ "env": "PLC1_PW" }`: the name of an environment variable that is set in the MCP client's configuration (as before).
- `{ "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" }`: the target of a generic Windows credential. The server reads it only at the moment of the call with `CredReadW` into a `SecureString`, overwrites the buffer and never returns, logs or audits the value, only the target name and the kind of source. Target names must start with the configured prefix (default `TiaMcp/`, see below); the server then cannot read the user's other credentials (Git, browser, VPN). On operating systems other than Windows, this source is refused with a clear error message.

Store a password once (the user types it into their own console; Claude never sees or processes it):

```cmd
cmdkey /generic:"TiaMcp/MCP_Safety_01/PLC_F1/Safety" /user:tia /pass
```

`cmdkey` prompts for the password without echoing it. The entry applies to the Windows user under which the MCP server runs; change it with the same command, remove it with `cmdkey /delete:"TiaMcp/MCP_Safety_01/PLC_F1/Safety"`.

**Credential prefix (operator setting):** The default is `TiaMcp/`. Anyone who wants a different prefix sets the variable `TIA_MCP_CREDENTIAL_PREFIX` under `env` in the MCP client's configuration, for example to `Anlage/Linie1/`; `*` allows every generic credential target. No tool call can change the prefix; an invalid value (leading or trailing whitespace, control characters, `*` inside a prefix, more than 128 characters) prevents startup. The active prefix appears in the startup output (`TIA MCP credential prefix: ...`) and in the doctor check `password-sources`. Passwords themselves are still not accepted as an argument by any tool.

**Network paths:** All file inputs and outputs (exports, imports, archives, I/O list, VCI workspaces, backups, path of the mapping file) accept UNC shares (`\\server\freigabe\...`) like drive paths. Only device paths (`\\?\`, `\\.\`, `\??\`) are refused, because Windows passes them on without path normalization (no resolution of `.`/`..`, no handling of reserved names) and the checks would therefore not take effect.

Anyone who does not want to state the source in every call creates a mapping file: `%APPDATA%\TiaMcpServer\passwords.json` (or the path in `TIA_MCP_PASSWORD_MAP`). It contains only names, never values:

```json
{ "version": 1, "entries": [
  { "project": "MCP_Safety_01", "plc": "PLC_F1", "purpose": "safety", "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" },
  { "plc": "PLC_1", "purpose": "ModuleWriteAccessPassword", "env": "PLC1_WRITE_PW" } ] }
```

`purpose` is `safety` for `safety_login`, `set_safety_password` and `revoke_safety_password`, the name of an access level (`FullAccessIncludingFailsafe`, `FullAccess`, `ReadAccess`, `HMIAccess`) for `set_plc_access_level`, `PlcMasterSecretPassword` for the confidential configuration data (protection and download) or the short name of the password configuration (for example `ModuleWriteAccessPassword`, `OnlineReadAccessPassword`) for download, online, compare and upload. `project` (project name without extension) and `plc` are optional; omitted or `"*"` means "for all". The most specific entry wins: project and PLC before project (all PLCs of the project) before PLC (this PLC name in every project) before `"*"`/`"*"` (default for all projects and PLCs). A value given in the call always wins. A file with unknown fields or with fields that look like a value (`password`, `secret`, `value`, ...) prevents the server from starting.

### Password manager (Windows program "TiaMcp Passwortverwaltung")

Instead of maintaining `cmdkey` and the mapping file by hand, there is a small Windows program that is installed next to the server (`password-manager\TiaMcpPasswortverwaltung.exe`). The user types passwords only into its window; no password passes through the chat, the command line or a file. In the **Zugriffsmodus** (access mode) tab you also choose whether the server only reads, writes with confirmation or works fully (see below).

**Starting:**

```powershell
tia-mcp passwords
```

The command starts the program from the tool's installation folder. Anyone who has set a custom prefix (`TIA_MCP_CREDENTIAL_PREFIX`) or a custom mapping file (`TIA_MCP_PASSWORD_MAP`) in the MCP client configuration passes the same values: `tia-mcp passwords --prefix "Anlage/Linie1/" --map "\\server\freigabe\passwords.json"`. Without these options the console's environment variables apply, otherwise `TiaMcp/` and `%APPDATA%\TiaMcpServer\passwords.json`. The program runs on Windows only and needs the .NET 10 Desktop Runtime (included in the .NET SDK for Windows, otherwise install it separately). It is produced only when building on Windows; a Linux build does not contain it.

**Usage:**
- The list shows per entry the purpose, the project („Für alle Projekte“ (for all projects) or a project name), the PLC („alle PLCs“ (all PLCs) or a name), the source (name of the credential or of the environment variable) and the result of the last test. It never shows a password.
- **Hinzufügen…** (Add…): choose the purpose, namely access level (full access, full access including fail-safe, read access, HMI access), confidential PLC configuration data, safety (F-program), module/online password (write/read access, online read access, upload) or another password configuration by short name. Then choose the scope: „Für alle Projekte“ (for all projects) or one project, each for all PLCs or a specific PLC. Finally enter the password twice. The fields are masked; „anzeigen“ (show) reveals the input.
- **Ändern…** (Change…): adjust the scope or purpose and enter a new password. The stored password is never read back. If the scope stays the same, the existing credential is overwritten; otherwise a new one is created, and the program asks whether it should delete the old one.
- **Löschen** (Delete): removes the entry from the mapping file and asks whether the credential should be deleted too, if no other entry uses it.
- **Test** / **Alle testen** (Test all): checks whether the credential exists for this Windows user and matches the server's prefix, without showing or comparing the value. For entries with an environment variable it only checks the program's own environment; the server sees the environment of its MCP client.

**What is stored:**
- The password ends up as a generic Windows credential (`CredWriteW`, persistence `CRED_PERSIST_LOCAL_MACHINE`: this user on this machine, no roaming) under `<Präfix><Projekt>/<PLC>/<Zweck>`, for example `TiaMcp/Anlage_01/PLC_1/FullAccess` or `TiaMcp/Alle Projekte/Alle PLCs/Safety`.
- Into the mapping file the program writes only project, PLC (`"*"` for all), purpose and the name of the credential. It writes through a temporary file, so that a half-written file never exists.
- It does not overwrite an invalid mapping file. Custom entries with `env` are kept.
- Passwords appear neither in the file nor in any log. The server itself only reads credentials; only this program can write them.
- The server reads the mapping file at startup. After new or deleted entries, restart the MCP server (or the MCP client). A changed password under the same name takes effect immediately, because the server reads the credential only at the time of the call.

**Tab „Zugriffsmodus“ (access mode):**
- Three options, each with one line of explanation: **Nur lesen (read-only)** (read only; the server never changes, opens, saves or closes a project and never writes to a PLC or simulation), **Lesen und schreiben, mit Rückfrage (read-write)** (read and write, with confirmation; every lifecycle, download and online call is confirmed once) and **Voll, ohne Rückfrage (full)** (full, without confirmation; the same calls without confirmation).
- Displayed is the stored value or „nicht gesetzt – Standard read-write“ (not set – default read-write). **Speichern** (Save) writes the choice, **Einstellung entfernen** (Remove setting) resets to the default. The program writes `settings.json` through a temporary file and never overwrites an invalid file.
- The file contains only `{ "version": 1, "accessMode": "read-only" }` (closed schema: unknown fields or another version prevent startup).
- An `--access-mode` argument or `TIA_MCP_ACCESS_MODE` in the MCP client configuration takes precedence; the tab points this out. The change takes effect after a restart of Claude Desktop or the MCP server.
- A custom file (`TIA_MCP_SETTINGS` in the client configuration) is passed to the program: `tia-mcp passwords --settings "D:\TiaMcp\settings.json"`.

## Limits (Siemens / TIA Openness)

What this server cannot do has two causes: either Siemens offers no interface (**Siemens**), or this fork decided against it (**Fork**). Points that come only from the documentation or from observation are marked accordingly. Most tools are not live-tested yet; this is stated with each tool above.

### Commissioning and online operation

- **No program status, no breakpoints, no single step** (Siemens, not verified): we found no interface for this in the Openness reference V21. Actual values are available only through the DB snapshot (`get_db_snapshot`/`create_db_snapshot`), PLCSIM Advanced (`plcsim_read`), the Web API (`plc_online_read`, only S7-1500 with the web server enabled) or watch tables through `read_watch_table_live`.
- **No online forcing** (Siemens): no online API is documented for force tables. They are only edited offline.
- **Axis optimization, PID tuning, trace** (Siemens, not verified): we found no Openness interface for this. Technology objects can be created, parameterized and interconnected, but not commissioned.
- **Download without proof of completion** (Siemens): `Download()` can return before the end in V21. `completionVerified` is therefore always `false`; check the state afterwards with `compare_to_online` or the Web API.
- **TLS certificates** (Siemens; trust only by fingerprint is Fork): TIA Portal asks for trust for every PLCSIM instance and every CPU with a self-signed certificate. Either confirm once in the TIA dialog or pass the SHA-256 fingerprints as `trustedCertificates`. There is no "trust everything".
- **PLCSIM instances** (Fork): instances are created in the PLCSIM Advanced Control Panel, not by the server. `plcsim_test` measures wall-clock time, not virtual time.

### Safety

- **Acceptance, validation and printout remain manual work** (standard and Siemens): a safety program must be validated and accepted by a qualified person. The server never replaces this; every fail-safe write shows the notice `plc_safety_acceptance_required`.
- **No separate F-compile** (Siemens): there is no documented F-compile function. `compile_check` of the PLC compiles the safety program along with it on an F-CPU (live on 2 Nov 2026: message „Compile safety program 'Safety Administration'“).
- **Writing F-logic** (partly live-tested): for `create_block` with F_FBD/F_LAD we have no verified SimaticML template. A SIMATIC SD network import into an F-block succeeded live on 2 Nov 2026, but removed the F-runtime group's reference for the main safety block; therefore SD changes to the main safety block go through a temporary F-block and SimaticML (see above); afterwards check with `get_safety_administration` and `compile_check`. A block freshly created from an SD document is exported by TIA Portal as SimaticML only after compiling; `get_block_content` xml now reports this as a warning instead of silently omitting the `.xml`.
- **Safety password** (Siemens): if an offline password is set, TIA Portal requires a login. The server logs in only with passwords from the password sources, never with made-up ones.

### Programming languages and content

- **Know-how-protected blocks** (Siemens): their export contains only the interface. Reading or changing the content is not possible; the server refuses round trips for them.
- **LAD/FBD and GRAPH** (Siemens): graphical languages exist only as SimaticML XML. This works but is cumbersome and error-prone; GRAPH especially. SCL and text sources are the easier route.
- **Entries in watch and force tables** (Siemens): no object API for individual entries is documented; changes only through the SimaticML round trip of the whole table.
- **Text list entries, alarm texts, ProDiag supervisions** (Siemens): no documented object model; only through TIA Portal's XLSX export and import.
- **Undocumented Siemens members** (Fork): are bound at runtime by their exact name. If one is missing in the installed version, the call fails with `api_member_missing` without changing anything. Nothing is guessed.

### TIA Portal itself

- **Modal dialogs block Openness** (Siemens, observed in the live test): as long as a dialog is open in the TIA UI, Openness calls hang. Observed were the dialog for assigning an IP to the PG/PC interface (hidden behind other windows) and the certificate dialog. While the UI's download or online dialog was open, no `DownloadProvider` was available either (observed; that the UI occupies the CPU during this is an assumption). Confirmation dialogs that TIA Portal reports through Openness are answered by `portalConfirmations`; the server does not see dialogs of the UI.
- **One project per TIA instance** (Siemens, observed): `create_project` and `open_project` fail as long as another project is open (`retrieve_project` maps the same refusal). First `close_project`.
- **The server does not see what the UI is currently showing** (Siemens): Openness knows no selection, no active window and no editor content that has not been applied yet.
- **Projects with CPU protection** (Siemens): access levels and the protection of confidential data need passwords. The server sets and removes them only with passwords from the password sources. For simulation projects, see the section "Simulation project without password" above.

### Decisions of this fork

- **TIA Portal V21 only** (Fork): other versions are not supported and not tested.
- **Classic WinCC through files** (Fork): classic HMIs (Comfort/Advanced, `HmiTarget`) are listed, exported, imported and deleted by the classic operations (SimaticML, members bound at runtime, not live-tested); every other HMI operation is WinCC Unified only and fails with `target_kind_unsupported` on a classic HMI.
- **Not stricter than TIA Portal** (Fork): the server refuses only what TIA Portal itself refuses, or invalid and ambiguous input. Everything else is a notice; TIA Portal decides.
- **Passwords never in the chat** (Fork): tools accept only names of environment variables or credentials, never password values. The server also generates no passwords.

## Architecture

TIA Portal V21 ships its Openness API as .NET Framework 4.8 assemblies. These use .NET Framework remoting APIs that do not run properly in a .NET 10 process.

The project therefore uses two processes for TIA Portal and optionally a third for S7-PLCSIM Advanced:

- `TiaMcpServer`: the .NET 10 MCP server over stdio and host of the .NET global tool. It also runs the S7-1500 Web API client for `plc_online_read`/`plc_online_write` and the Siemens documentation reader `siemens_docs` in its own process (without a worker).
- `TiaMcpServer.OpennessWorker`: a worker process under .NET Framework 4.8 that loads `Siemens.Engineering.*` and talks to TIA Portal.
- `TiaMcpServer.PlcSimWorker`: an x64 worker under .NET Framework 4.8 that starts only on the first PLCSIM tool call. It loads the S7-PLCSIM Advanced API from its installation with `Assembly.LoadFile` and binds it by reflection.

**Communication between host and workers:**
- The MCP host keeps a persistent worker process under .NET Framework 4.8 that is attached to TIA Portal.
- It exchanges newline-delimited JSON over stdin/stdout. Requests run one after another, and the worker restarts automatically after a crash or a timeout.
- The PLCSIM worker uses the same envelope and the same transport with its own handshake.

**Siemens DLLs:**
- Siemens Openness and PLCSIM Advanced DLLs are never copied into this repository or the NuGet package. The workers load them from the local installations of TIA Portal V21 and S7-PLCSIM Advanced.
- The only Siemens assembly in the package is the MIT-licensed Web API client `Siemens.Simatic.S7.Webserver.API` from nuget.org, a dependency of the host.

## Quick start

Install the server as a .NET global tool, check the environment and register it with an MCP client:

```powershell
dotnet tool install -g TiaMcpServer
tia-mcp doctor
tia-mcp install claude-code
```

`tia-mcp doctor` checks before connecting:
- the Windows version and the .NET runtimes,
- the TIA Portal installation and the Openness assemblies,
- the group membership and the compatibility of host and worker.

It also reports where the optional S7-PLCSIM Advanced API would be loaded from and which PLC Web API targets the optional `--plc-webapi-config` file contains, without contacting them. Best run it first; it says exactly which prerequisite is missing.

| Prerequisite | Note |
| --- | --- |
| Windows | Windows only; the Openness API does not exist on any other system |
| Siemens TIA Portal V21 | with Openness installed and enabled |
| Group `Siemens TIA Openness` | the current Windows user must be a member |
| Framework-dependent global tool: stable .NET 10 SDK 10.0.400 or newer | provides `dotnet tool install` and the supported .NET 10 runtime |
| .NET Framework 4.8 Runtime | needed by the Openness worker process |

**Installation variants:**
- The framework-dependent global tool `tia-mcp` needs a supported .NET 10 runtime.
- The separate self-contained `win-x64` archive brings the host runtime along.
- With neither variant does the NuGet package `ModelContextProtocol` need to be installed.

Custom .NET integrations that compile directly against the C# MCP SDK must read the migration notes for ModelContextProtocol 2.2. Then retest protocol negotiation, tool schemas and structured results. This requirement is independent of the v3 migration of `browse_project_tree` described above.

**Supported clients for `tia-mcp install`:** Claude Code, Codex, OpenCode, MiMoCode.
- Servers are registered in **read-only** mode by default (thirteen tools).
- `--access-mode read-write` unlocks, with one prompt per actual call (thirty-two tools):
  - changes, compiling and simulation control,
  - lifecycle,
  - PLC download (PLCSIM or hardware),
  - online changes, going online and offline, station upload,
  - the lifecycle of global libraries.
- `--access-mode full` runs these calls without a server prompt (the same thirty-two tools).
- Anyone who prefers to choose the mode in the program omits `--access-mode` from the client configuration and sets it in `tia-mcp passwords`, tab **Zugriffsmodus** (access mode) ([Choosing the access mode](#write-safety)). An `--access-mode` entered by `tia-mcp install` takes precedence over this setting.

Binding to a specific project, all installation options and the complete description of the access modes are in the [installation guide](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/installation.md). How to build from source instead of installing the published tool is described under [Building from source](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/building.md).

## Documentation

Most linked documents also have a German version (`.de.md`, language switch at the top). What this fork does beyond the original, with the live-test status of each feature: [What this fork does beyond the original](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/FORK_FEATURES.md).

**Using the server**

- [Installation](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/installation.md): install, check with `doctor`, register with a client, access modes
- [MCP client configuration](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/mcp-client-configuration.md): client configuration, addressing of block paths and response sizes
- [Troubleshooting](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/guides/troubleshooting.md): common errors and verified behavior of TIA Portal V21
- [Supported operations](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/README.md): every operation by area, with parameters
- [PLC online through the Web API](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_ONLINE_WEBAPI_SUMMARY.md): `plc_online_read`/`plc_online_write`: target file, credentials, certificates, confirmation, provenance
- [Claude in TIA](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/CLAUDE_IN_TIA.md): TIA Portal Add-In, `tia_inbox`, Claude bridge, one-time setup, provenance
- [Siemens documentation](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/SIEMENS_DOCS_SUMMARY.md): `siemens_docs`: operations, references, host allowlist and limits, live endpoint checks
- [CPU protection](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_SECURITY_SUMMARY.md): access level and confidential configuration data for simulation projects without password, hardware compilation, provenance of every member

**Understanding the design**

- [Architecture](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/ARCHITECTURE.md): two-process design, enforcement of access rights, write safety, the canonical JSON interface

**Building and contributing**

- [Contributing](https://github.com/Birger90/tia-portal-mcp-server/blob/main/CONTRIBUTING.md): workflow, conventions for branches and commits
- [Building from source](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/building.md): build, test, coverage, run locally
- [Local testing in the MCP sandbox](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/local-mcp-testing.md): the test loop with the MCP Inspector
- [Packaging](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/development/packaging.md): build the package, install a local build as `tia-mcp`

**Direction**

- [Roadmap](https://github.com/Birger90/tia-portal-mcp-server/blob/main/ROADMAP.md): fundamental priorities
- [Network operations roadmap](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/roadmap/network-operations.md): step-by-step delivery of the network tools
- [Export/import format roadmap](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/roadmap/export-import-format.md): exchange in source format
- [JSON contract roadmap](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/roadmap/json-contract.md): one structured output contract for all tools
- [Improvement log](https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/IMPROVEMENT_LOG.md): open items and completed development work

## Contributing

Contributions are welcome. Workflow and environment setup are in [CONTRIBUTING.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/CONTRIBUTING.md). Architecture and build reference are in [AGENTS.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/AGENTS.md).

## License notes for this fork

This fork as a whole is licensed under the **GNU Affero General Public License v3.0** (AGPL-3.0-only, see [LICENSE](https://github.com/Birger90/tia-portal-mcp-server/blob/main/LICENSE)). Reason: it contains code from [rickgaiser/TiaMcp](https://github.com/rickgaiser/TiaMcp), which is licensed under AGPL-3.0. Which files these are is listed in [THIRD_PARTY_NOTICES.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/THIRD_PARTY_NOTICES.md).

The base code comes from [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) and is licensed under MIT. Its license notice is kept in [LICENSE-MIT](https://github.com/Birger90/tia-portal-mcp-server/blob/main/LICENSE-MIT).

Anyone who conveys this fork or offers it to others as a network service must provide the complete source code under AGPL-3.0. This is not legal advice.

## Security

How to report security vulnerabilities is described in [SECURITY.md](https://github.com/Birger90/tia-portal-mcp-server/blob/main/SECURITY.md).

## Further tools

- [TIA Portal V21 Git Add-In](https://github.com/Czarnak/tia-git-addin)
- [Claude Code / Codex / Gemini plugin for development with TIA Portal](https://github.com/Czarnak/totally-integrated-claude)
