# Fork live acceptance on TIA Portal V21: interim record

Date: 2026-11-01. Status: **interim, not finished.** Download, online, upload, PLCSIM run/stop and
PLCSIM tag access are blocked by the test PC (see "Blocked" below). Everything listed under
"Observed" was seen live; nothing in this record is inferred from offline tests.

## Environment

| Item | Value |
| --- | --- |
| TIA Portal | V21, 2100.0.121.1, UI language German |
| S7-PLCSIM Advanced | API 8.0 (`Siemens.Simatic.Simulation.Runtime.Api.x64` 8.0.0.0, file 80.0.259.0) |
| .NET | runtime 10.0.14, .NET Framework 4.8.1 |
| Build | fork commit `65604eb`, built by the maintainer on the test PC against the real V21 PublicAPI assemblies (the worker's `AssemblyReference.cache` lists them), so every compiled Siemens member passed the real-DLL compile |
| Client | Claude Desktop through the cloud bridge, `--access-mode full` |
| Project | disposable standalone `MCP_Livetest_01` created by `create_project`; CPU 1516-3 PN/DP 6ES7 516-3AN02-0AB0 V2.9 |
| PLCSIM instance | `MCP_Test`, Softbus, created in the Control Panel by the assistant |

## Observed

| # | Area | Result |
| --- | --- | --- |
| 1 | Setup | `fork-verify` failed on a PC with only .NET SDK 8 and Windows PowerShell 5.1; fixed in `fbd3cfb` (PowerShell 7, `install-prerequisites.cmd`) |
| 2 | `doctor` | 11 passed, 1 warning (no binding); Openness group membership present |
| 3 | `plcsim_read probe_api` | every catalog member resolved, including the reference signatures; `ERuntimeErrorCode.OK` = 0, negative values errors, positive values warnings, as assumed; 28/28 operations available |
| 4 | First attach | `get_project_status` waited more than 60 s until the human answered TIA's Openness access dialog |
| 5 | Read-write lifecycle | `create_project` was refused with `access_denied`: Claude Desktop does not support MCP form elicitation, so read-write lifecycle, download and online calls cannot be used with this client; `full` works |
| 6 | Lifecycle | `create_project`, `save_project`, `bind_project list_portals`: OK |
| 7 | Network | catalog search, `find_module_types` (HwUtilities runtime binding), `add_network_device`, `read_hardware_config`, `list_plug_locations`, `inspect_network_object`, `create_subnet`, `configure_network_device` (subnet): OK. Interface and port items have a null `TypeIdentifier` and cannot be selected as `deviceItem`; nodes are selectable through `interfacePath` |
| 8 | PLC software | tag tables, tags (I/Q/M), user constant, SCL FC and OB, GlobalDB, LAD FB in a group, `update_block_logic` with `expectedContentHash` and hash verification, `add_interface_member`, `create_instance_db`, `copy_block`, `rename_block`, `renumber_block`, `update_tag` rename (V21 updated the references in SCL code by itself), `move_tag`, `create_watch_table`, `get_db_values`, `get_block_interface`, `read_address_occupancy`, `find_free_address`: OK |
| 9 | Program analysis | `get_call_tree`, `find_call_paths`, `find_unused_blocks`, `find_orphaned_instance_dbs`, `analyze_change_impact` correct on an OB → FC and OB → FB instance program. V21 reports **both** `Uses` and `UsedBy` for the same call location; the direction handling stays correct |
| 10 | Technology objects | `create_technology_object` with read-back works with version 6.0; 10.0 and 7.0 were rejected for this CPU (FW V2.9) |
| 11 | PLCSIM | `list_instances`, `get_instance` (Softbus, Stop, `CPU1500_Unspecified`): OK |

## Defects found (open)

| # | Defect | Evidence |
| --- | --- | --- |
| D1 | `update_db_member` reports a failure although the change was applied | the host re-reads via Export; after a Retain change the DB is inconsistent until compiled and Export throws "Inconsistent blocks and PLC data types (UDT) cannot be exported" |
| D2 | `move_block` after `renumber_block` in the same call fails `engineering_rejected` | Export of the then inconsistent FC is refused; after `compile_check` the same move succeeds. Earlier items of the call stay applied |
| D3 | Interface edits on different members of one block supersede each other | the first item is marked `superseded` and is not verified individually, although both members were correct in `get_db_values` |
| D4 | Compile reports are inflated and lose details | container nodes with empty text (6-9 rows for one real warning); the import compile report of `update_block_logic` shows "Compiler diagnostic omitted." for every message |
| D5 | `plug_device_item` into the rail reports `postcondition_failed` | the module is plugged at device level (DI 32 at slot 2, DQ 32 at slot 3 in `read_hardware_config`) |
| D6 | `list_io_addresses` returns no addresses for a module | the addresses sit on a sub-item whose `TypeIdentifier` is null, so it cannot be selected |
| D7 | `export_io_map` returns `success:false` although the CSV was written | the diagnostics exceeded the 512-character omission limit |
| D8 | Technology object versions are not discoverable | the caller has to guess the version (see 10) |
| D9 | `compile_check` compiles only the PLC program, not the hardware configuration | it reported 0 errors for `MCP_Livetest_01` while the UI hardware compile during the PLCSIM download had 3 errors (see "Download blocked by CPU protection"). Addressed offline on `feature/sim-security-profile` by `compile_check scope:"hardware"`; not yet verified live |

## Download blocked by CPU protection

Reported by the maintainer from a later live test on 2026-11-01 (not re-observed for this record): the hardware compile of the download to
PLCSIM failed for the CPU created by `create_project` with three errors: access level "Kein Zugriff (kompletter
Schutz)" requires a password; "Vertrauliche PLC-Konfigurationsdaten schützen" is on without a password; the PLC
communication certificate cannot be used without that password. Setting "Vollzugriff (kein Schutz)" and unticking
the confidential-data protection in the CPU properties fixed it, and the MCP `plc_download` then succeeded.
`feature/sim-security-profile` adds `set_plc_access_level`, `disable_plc_confidential_data_protection` and
`get_plc_security` for this ([CPU protection reference](../../../SupportedOperations/PLC_SECURITY_SUMMARY.md));
they are not yet verified live.

## Missing capability found

`get_project_status` reads `isSimulationDuringBlockCompilationEnabled` and
`isVirtualPlcDuringBlockCompilationEnabled`, but no tool can set them, and `create_project` leaves
them `false`. Siemens' PLCSIM Advanced getting-started guide has the user tick "support simulation
during block compilation" before a Softbus download. In this test it was set in the project
properties (Schutz tab) by hand and read back as `true` through `get_project_status`.

## Blocked

`list_download_targets`, `get_online_state` and `list_station_upload_interfaces` return no modes.
This matches TIA Portal itself, not a defect of the tool: TIA's own "Erweitertes Laden" dialog for
PLC_1 offers no PG/PC interface type and reports "Es steht keine Schnittstelle für eine
Online-Verbindung zum konfigurierten Zugriffsknoten zur Verfügung. Die PN/IE-Schnittstellen stehen
nicht zur Verfügung, wenn das Ethernet-Kabel nicht gesteckt oder beschädigt ist." Under
"Online-Zugänge" the `PLCSIM` node has no children, while a real adapter shows "Erreichbare
Teilnehmer aktualisieren". The instance `MCP_Test` is registered and powered (Stop). Enabling the
simulation flag did not change the dialog. Not yet clarified: whether Windows was restarted after
PLCSIM Advanced V8 was installed. Until TIA itself offers the PLCSIM interface, `plc_download`,
`plc_tia_online`, `compare_to_online`, `plc_station_upload` and the PLCSIM run/stop/tag operations
stay unverified live.

## Operational note

Computer use: asking the desktop to open "TIA Portal V21" starts a new TIA Portal instance every
time; to bring the existing window forward, click its taskbar button instead.
