# What this fork does beyond the original

**Language:** English · [Deutsch](FORK_FEATURES.de.md)

Comparison of **Birger90/tia-portal-mcp-server** (`main` as of 2026-11-02) with the original
**Czarnak/tia-portal-mcp** (`main` 8b41877, 2026-11-02). The numbers are counted from the code of both (tool names,
operation catalogs), not estimated. "Live-tested" means: run on TIA Portal V21 and PLCSIM Advanced 8.0 in the test
projects `MCP_Livetest_01`, `MCP_Safety_01`, `MCP_G2_Test`; evidence in the
[defect catalog](superpowers/acceptance/defect-catalog.md) and the [acceptance reports](README.md). Everything else is
checked offline only (unit tests, FakeWorker).

## Numbers

| | Original | Fork |
| --- | --- | --- |
| MCP tools | 19 | 30 |
| `plc_read` operations | 3 | 54 |
| `plc_write` operations | 14 | 202 |
| `network_read` / `network_write` | 4 / 5 | 11 / 25 |
| `hmi_read` / `hmi_write` | 20 / 9 | 24 / 39 |
| `library_read` / `library_write` | – | 14 / 39 |
| `plc_online_read` / `plc_online_write` (S7-1500 Web API) | – | 18 / 7 |
| `vci_read` / `vci_write` | 3 / 6 | 3 / 6 |

New tools: `retrieve_project` (retrieve a project archive; not live-tested), `plc_download`, `plc_tia_online`, `plc_station_upload`, `plcsim_read`, `plcsim_write`, `plcsim_test`,
`plc_online_read`, `plc_online_write`, `library_read`, `library_write`.

## Features the original does not have

| Area | What the fork does | Live-tested |
| --- | --- | --- |
| Download (`plc_download`) | download to PLCSIM or a real CPU through any PC interface, certificate trust only by a confirmed SHA-256 fingerprint | yes, to PLCSIM with a standard CPU; real hardware no. TIA itself refuses fail-safe downloads through Openness (K33): download those from the TIA UI |
| Online through Openness (`plc_tia_online`, `plc_read`) | go online, go offline, offline/online comparison, watch and force tables, software comparison, hardware comparison offline/online (issue #7) | yes: `go_online`, `go_offline`, `compare_to_online` (identical after download); tables no |
| Loadable files (`plc_write generate_loadable`) | write a loadable file for blocks or software units, for a PLC or PLCSIM | no |
| Block know-how and write protection (`plc_read get_block_protection`, six `plc_write` operations) | set and remove know-how protection, define, enable, disable and change block write protection with passwords from password sources | no |
| Project languages and project texts (`plc_read get_project_languages`, `plc_write set_project_languages`, `export_project_texts`, `import_project_texts`) | manage project languages, export project texts to XLSX for translation and import them back | no |
| Further CPU settings (`plc_read get_cpu_settings`, 20 `plc_write` operations; issue #15) | account locking, PLC password policies, syslog (CPU and project servers), system web pages, default web application, watch/force table access for web server and display, display password from a password source, front panel logo, project language rows, IP accessibility, web DB generation; every change read back | no |
| Web server and OPC UA users (7 `plc_write` operations; issue #13) | create, change and delete the CPU's web server users with permissions, the simple web server users and the OPC UA submodule's users; passwords only from password sources; read with `get_cpu_settings` | no |
| UMAC (`plc_read get_umac`, 19 `plc_write` operations; issue #12) | read whether the project is protected, roles, project and UMC users, engineering and device function rights, anonymous user, password policy; create/change/delete custom roles, project users (passwords from password sources), custom device function rights; assign rights and roles | no |
| CPU certificates (`plc_read get_cpu_certificates`, 11 `plc_write` operations; issue #14) | read the CPU's certificates with SHA-256 fingerprints (to compare with TLS prompts), create from templates, import, export, delete, assign to web server/OPC UA server, dynamic certificate settings; nothing trusted automatically | no |
| Safety printout (`plc_write generate_safety_printout`; issue #21) | the F-CPU's safety printout as a PDF/XPS file, a document for the safety acceptance; the MCP never replaces the acceptance | no |
| Safety Validation Assistant (`plc_read get_safety_validation`, `check_activation_test`, 14 `plc_write` operations; issue #25) | read, check and prepare activation tests, safety functions and conditions, export them and generate the test report; never records a test result, every change under the safety acceptance guard | no |
| OPC UA server interfaces (`get_opcua_configuration`, eleven `plc_write` operations) | server and SIMATIC interfaces, reference namespaces, access restrictions, roles and permissions | no |
| Download to a folder and the online master secret (`download_to_folder`, `set_/reset_plc_master_secret_online`; `plc_download rhTarget`) | download data into a Windows folder, the online PLC's master secret, R/H primary/backup download | no |
| Station upload (`plc_station_upload`) | upload a station from the device into the project | no |
| PLCSIM Advanced (`plcsim_*`) | control instances (stop/run), read and write values, test sequences with expectations and time limits, unsigned types included | yes, including the safety shutdown test 8/8 |
| S7-1500 Web API (`plc_online_*`) | observe and control real CPUs without TIA | no (needs real hardware) |
| Fail-safe programs | list F-blocks, read the safety administration, create an F-runtime group and assign its main safety block, safety login and passwords | partly: listing, reading the administration, editing the main safety block through SD, copying a safety block and moving a safety tag yes; login and passwords no |
| SIMATIC SD (LAD/FBD as text) | insert, replace and delete networks, create a block from a document, also in the main safety block (the F-runtime group link is kept, K14) | yes |
| Blocks in detail | interface members, DB values, rename, renumber, copy, move, instance DB, call tree and analysis, DB snapshot, titles and comments | partly: DB values, renumber and move, interface members, copy, snapshot, titles/comments yes |
| Tags | set comments (K22), move a tag, tables as files | comments and move yes |
| Technology objects | create, delete, read and write parameters, connect hardware, export and import | yes (K18–K20, K23) |
| Hardware modules | plug, move, copy, delete, change type, parameters, I/O addresses, I/O map export, create an IO system, custom identities (key/value) on devices and device items, PROFINET port topology (read, connect, disconnect), transfer areas (read, create, update, delete), delete an IO system, disconnect an IO device, CAx (AutomationML) export and import | partly: plug, I/O addresses, I/O map export, plug locations, IO system yes |
| CPU protection | read protection, set the access level, protect/unprotect confidential data | read, access level, unprotect yes |
| Compile | compile hardware (`scope:"hardware"`), set "support simulation during block compilation" | yes |
| Change detection | software and text list checksums of a PLC, fingerprints of a block or PLC data type (`get_plc_checksums`, `get_fingerprints`) | no |
| Libraries | project and global libraries, types, versions, master copies and their folders, clean-up, harmonize, types from documents, exports, detailed compares | no |
| Alarms and text lists | text lists, alarm texts, supervisions, ProDiag, alarm classes, system diagnostics settings as a file | no |
| External sources, folders, software units | import and generate sources, manage groups and units, file exports | no |
| WinCC Unified | screens, scripts and batch renames in addition to tags and alarms | no |
| OPC UA export | export the CPU's OPC UA XML | no |
| Password manager | Windows program "TiaMcp Passwortverwaltung": passwords in Windows Credential Manager, never in the chat; sets the access mode | no |

## Behavior that differs from the original

- **Never stricter than TIA Portal.** The fork refuses only what TIA itself refuses, or invalid and ambiguous input.
  Where the original blocks, the fork gives a notice and lets TIA decide. Exception: an explicit SIMATIC SD export of
  fail-safe and system blocks is refused, because TIA V21 crashed on it.
- **Clear messages instead of silent waiting.** When TIA waits for the Openness approval, a notice comes after 25 s
  (K37, live). TIA confirmations answered outside a call are reported, with their dialog text, by the next call (K42, not live).
- **Defect catalog.** 42 defects found live, 32 of them fixed and confirmed live.
- **TIA Portal V21 and WinCC Unified only.**

## Limits

- Fail-safe programs cannot be downloaded through Openness (Siemens restriction, K33).
- Querying TO versions (K08) and moving TOs (K21): Siemens offers no documented interface.
- Everything without "yes" in the "Live-tested" column is implemented and tested offline, but not yet confirmed on TIA.
