# Roadmap: parity with a PLC programmer's TIA Portal workflow (fork)

Goal of this fork: the MCP server should eventually cover everything a PLC programmer does in
TIA Portal V21. This page maps that workflow to the current tool surface and orders the gaps.
Status legend: **done** (upstream tool exists), **in progress** (this fork, offline only),
**gap** (no tool), **check** (Openness V21 support not yet verified from Siemens sources).

| Area | Programmer task | Status | Tool / note |
| --- | --- | --- | --- |
| Project | create, open, save, save-as, archive, close | done | lifecycle tools |
| Project | Multiuser / Project Server sessions | done (partial) | `bind_project` inventory, local sessions; check-in/terminal ops missing |
| Hardware | add devices from catalog, IP / PN name, subnets, IO systems | done | `network_read` / `network_write` |
| Hardware | insert modules into racks/slots, move / copy / delete / change type, module parameters (filters, diagnostics), I/O start addresses | in progress | `network_read` / `network_write` hardware-module operations (`plug_device_item`, `set_hardware_attributes`, `set_io_address`, ...); not live-tested |
| Hardware | PROFINET / PROFIBUS telegrams, submodule configuration beyond generic attributes | gap / check | telegram members not in the reviewed Siemens sources |
| Hardware | I/O map (address assignment list) export | in progress | `network_write` `export_io_map` (CSV to a caller path); not live-tested |
| PLC software | tag tables, tags, user constants | done | `plc_read` / `plc_write` |
| PLC software | library gaps: export a type version, publish a block/UDT as a type, clean up unused types, harmonize, update a global library from the project, archive/save-as a global library, type and master-copy folder create/delete | gap / check | members named in the sources without a parameter list (or not named); see the left-out table of `LIBRARY_OPERATIONS_SUMMARY.md` |
| PLC software | address assignment list, next free address, move tag between tables | in progress | `plc_read` `read_address_occupancy` / `find_free_address`, `plc_write` `move_tag` (guarded re-create); not live-tested |
| PLC software | SCL, DB, UDT as source; LAD/FBD/STL/GRAPH as SimaticML XML | done | `plc_read` / `plc_write` (`format=source` or `xml`) |
| PLC software | external source files: add (SCL/STL/DB/UDT), generate blocks (None/KeepOnError, into a group), delete, generate source from blocks/types to a file | in progress | `plc_read list_external_sources`, `plc_write import_external_source`/`generate_blocks_from_source`/`delete_external_source`/`export_source_file`; root sources only (groups inside the folder are not supported by Openness); not live-tested, see `PLC_SOURCES_FOLDERS_SUMMARY.md` |
| PLC software | find objects in the project tree, rename block / PLC data type / tag-table / watch-table groups, create/delete PLC data type and tag-table groups | in progress | `plc_read find_objects` (glob, paging), `plc_write rename_*_group`, `create_type_group`/`delete_type_group`/`create_tag_table_group`/`delete_tag_table_group`; group moves gap (no reference documents one); not live-tested |
| PLC software | software units: create, delete, rename | in progress | `plc_write create_software_unit`/`delete_software_unit`/`rename_software_unit` (software-units.md §2); unit properties, relations, publishing and loadable files gap; not live-tested |
| PLC software | export tag tables, watch tables, force tables as CSV; export all blocks/types of a PLC or group to files; tag comments | in progress | `plc_write export_tag_tables_csv` (with comment columns)/`export_watch_tables_csv`/`export_force_tables_csv`/`export_blocks` (existing reads, per-file SHA-256), `plc_read read_tag_comments`; alarm texts and XLSX gap; not live-tested |
| PLC software | edit interface members (FB/FC/OB/global DB/UDT: add, rename, data type, start value, comment, delete) without rewriting the document | in progress | `plc_write` `add/update/delete_interface_member`, `plc_read` `get_block_interface`/`get_type_interface` (SimaticML edit with the export hash; not live-tested) |
| PLC software | DB start values and member attributes at any depth (global and instance DBs, multi-instances, array elements) | in progress | `plc_read` `get_db_values`, `plc_write` `update_db_member` (native `Member.Get/SetAttribute`; not live-tested) |
| PLC software | create instance DBs, rename, renumber, copy and move blocks, move tag tables | in progress | `plc_write` `create_instance_db`, `rename_block`, `renumber_block` (native), `copy_block`, `move_block`, `move_tag_table` (export/import round trip; not live-tested) |
| PLC software | member attributes (retain, accessibility) of FB/UDT interfaces, array element start values in interfaces | gap / check | SimaticML form not evidenced in the allowed sources |
| PLC software | network logic (LAD/FBD/SCL networks) granular editing | gap | next phase; whole documents via `update_block_logic` today |
| PLC software | technology objects: list, typed parameters, create/delete/rename, groups, hardware connections, SimaticML export/import | in progress | `plc_read` `list_technology_objects`, `get_technology_object_parameters`, `get_technology_object_hardware_connection`, `get_technology_object_content`; `plc_write` twelve operations (not live-tested); cam data, interpreter programs, master value couplings, Startdrive telegram connections and a type/version matrix are left out, see `TECHNOLOGY_OBJECTS_SUMMARY.md` |
| PLC software | software units, know-how protection | gap / check | software units can be placed from master copies (`library_write instantiate_master_copy`) |
| PLC software | PLC alarms and text lists: user/system text lists (list, rename, range, multilingual comment, delete, create from a master copy), text list entries, program alarm texts and ProDiag supervisions per language, common alarm classes, XLSX/alarm-class export and import, ProDiag evidence, `AssignedProDiagFB` and the ProDiag alarm information export | in progress | `plc_read` `list_text_lists`, `read_*_workbook` (paged), `get_prodiag_fb`, `get_prodiag_assignment`; `plc_write` `update_text_list`, `delete_text_list`, `update_*_cells` (cells of TIA Portal's XLSX export, hash-checked under ExclusiveAccess, verified by re-export), `export_*`/`import_*` (imports verified by a re-export where documented), `set_prodiag_assignment`, `export_prodiag_info`; `library_write` text list master copies; not live-tested, see `PLC_ALARMS_TEXTLISTS_SUMMARY.md` |
| PLC software | typed text list entries, individual alarm classes, program alarms and supervisions as objects, entry delete, supervision settings DAT, ProDiag FB creation, ProDiag-FB attribute writes | gap / check | no documented V21 object model, member or entry point (ProDiag FB creation belongs to the block domain); see the left-out table of `PLC_ALARMS_TEXTLISTS_SUMMARY.md` |
| PLC software | global libraries, library types, master copies | in progress | `library_read` (libraries, tree, types, versions, instances, update check, compare) / `library_write` (global library open/retrieve/create/save/close, master copy capture/copy/delete/rename/instantiate, type-version instantiate, update project, edit/release/discard/default/delete, renames); not live-tested, see `LIBRARY_OPERATIONS_SUMMARY.md` |
| Build | compile PLC / block | done | `compile_check` |
| Build | compile HMI device | gap / check | |
| Load | download to simulation (PLCSIM softbus interface) | in progress | `plc_download` (not live-tested) |
| Load | download to real hardware | in progress | `plc_download` through any PC interface, controlled by the access mode (read-write prompt, full unattended); caller decisions and environment-variable passwords; not live-tested |
| Online | operating state and mode selector, run/stop of a real CPU | in progress | `plc_online_read` `read_operating_mode`/`read_mode_selector`, `plc_online_write` `change_operating_mode` (S7-1500 Web API; confirmed per call in read-write; not live-tested) |
| Online | diagnostics: diagnostic buffer, alarms (acknowledge), module status and LEDs, CPU type, cycle time/load, system time, syslog | in progress | `plc_online_read` / `plc_online_write` (`acknowledge_alarm`, `flash_leds`, `set_system_time`) over the S7-1500 Web API |
| Online | monitor / modify values | in progress | `plc_online_read` `browse_tags`/`read_tags`, `read_watch_table_live` (a project watch or tag table read offline, its mappable symbols online; not live-tested), `plc_online_write` `write_tags` (Web API, symbolic access); no forcing |
| Online | CPU backup / restore (memory card content) | in progress | `plc_online_write` `create_backup`/`restore_backup` (Web API client helpers) |
| Online | accessible devices on a PC interface (network scan) | in progress | `plc_read scan_accessible_devices` (`OnlineObserve`, every mode, explicit PC interface; not live-tested) |
| Online | go online from TIA Portal, compare online/offline, upload | in progress | Openness: `plc_tia_online` (go online/offline, exact path), `plc_read` `get_online_state` (connection state, not RUN/STOP), `compare_to_online`, `compare_software`, `plc_station_upload`; not live-tested, see `PLC_ONLINE_OPENNESS_SUMMARY.md` |
| Online | watch & force tables | in progress (offline tables) | `plc_read` lists and reads watch/force tables, `plc_write` creates/deletes watch tables and groups and edits watch/force table entries through SimaticML (offline, Openness) |
| Online | force values | gap | neither Openness V21 nor the Web API documents an online force API |
| Test | drive a simulated CPU: run/stop, read/write tags, scenario tests | in progress | `plcsim_read` / `plcsim_write` / `plcsim_test` (PLCSIM Advanced, S7-1500 only) |
| Test | TIA Portal Test Suite (application tests, style guide) | gap / check | see `TESTSUITE_OPERATIONS_SUMMARY.md` |
| HMI | WinCC Unified tags, alarms; read screens | done | `hmi_read` / `hmi_write` |
| HMI | edit Unified screens, WinCC Classic / Advanced | gap / check | |
| Safety | F-CPU / safety program | gap (deliberate) | |
| Drives | Startdrive parameters, download | gap / check | |
| Versioning | VCI workspaces, export/sync | done | `vci_read` / `vci_write` |
| Analysis | cross-references | done | `read_cross_references` |
| Analysis | call structure, call paths, unused blocks, orphaned instance DBs, change impact | in progress | `plc_read` program analysis from the cross-reference reader; only stated relationships, indirect calls invisible; not live-tested |
| Docs | project texts / multilingual export, printouts | gap / check | |
| Security | UMAC users/roles, protection levels, certificates | gap / check | |

## Suggested order

1. Close the engineering loop: `plc_download` to PLCSIM + PLCSIM Advanced control and scenario tests
   (this fork, pending live acceptance once PLCSIM Advanced is installed).
2. Online visibility: operating state, diagnostics, values and control are in progress through the S7-1500
   Web API (`plc_online_read`/`plc_online_write`, pending live acceptance on a test CPU); TIA Portal online
   connection, online/offline compare, station upload and offline watch/force tables are in progress through
   Openness (`plc_tia_online`, `plc_station_upload`, `plc_read`/`plc_write`, pending live acceptance).
3. Hardware completeness: module insertion and parameterisation (schematic → hardware config);
   the hardware-module operations exist offline and need live acceptance, telegrams remain open.
4. Reuse: global libraries, types and master copies are in progress (`library_read`/`library_write`, pending live
   acceptance); type export/publish, cleanup, harmonization and library-to-library updates wait for documented signatures.
5. Siemens TIA Portal Test Suite as a second, Siemens-native test runner.
6. HMI screen editing, drives; safety programs only with a dedicated safety design and live acceptance.
   Technology objects are in progress (`plc_read`/`plc_write`, pending live acceptance; cam data, master value
   couplings and Startdrive connections wait for documented access paths). Real-hardware download exists (owner decision: controlled by the
   access mode) and still needs live acceptance on a disposable CPU.

Every "check" item must be verified against Siemens documentation or the installed V21 Openness
XML/assemblies before it is designed; nothing here claims Openness supports it.
