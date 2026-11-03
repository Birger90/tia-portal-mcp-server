# S7-PLCSIM Advanced and fail-safe CPUs: findings and live test plan

Research for the fail-safe operations ([PLC safety reference](../SupportedOperations/PLC_SAFETY_SUMMARY.md)), done on
2026-11-01 without access to the test PC. Nothing here is live-verified.

## Does S7-PLCSIM Advanced V8.0 simulate S7-1500F CPUs?

Yes, as far as the sources go; the V8.0 function manual itself could not be retrieved (support.industry.siemens.com
refused the download from this environment), so the F-CPU statements come from the V6.0 manual and Siemens' product
page, and the V8.0 firmware range from a distributor page.

| Finding | Source |
| --- | --- |
| The supported-CPU table has a "Fail-safe" column listing CPU 1511F-1 PN ... CPU 1518F-4 PN/DP, **CPU 1516F-3 PN/DP** among them, the ET 200SP F, TF, ET 200pro F, Drive Controller TF and Software Controller F CPUs; the table header gives firmware V1.8 to V3.1.1 for V6.0 | S7-PLCSIM Advanced V6.0 UPD1 function manual, edition 02/2024 (A5E37039512-AG), §2.6 Table 2-1, https://cache.industry.siemens.com/dl/files/578/109955578/att_1267815/v1/s7-plcsim_advanced_function_manual_en-US.pdf |
| "To successfully simulate and test a project with fail-safe input and output modules, you need to use safety system version V1.6, V2.0, V2.1, V2.2, V2.3, V2.4, V2.5, or V2.6. Simulation of the fail-safe input and output modules does not work correctly with an older version." | same manual, §2.7.2 Notes |
| "In order to use protection levels, the Web server and the access protection of the F-CPU in the simulation, click on the 'Update password encryption' button" (CPU properties, Protection & Security, Access level) | same manual, §2.7.2 Notes |
| The manual has a section 7.4 "Loading project data of an F-CPU to a standard CPU" (body not retrieved) | same manual, table of contents |
| "Simulate the behavior and firmware of real controllers, such as communication (incl. TCP/IP), safety and web server"; support for "S7-1500 (incl. C, T, F, R, H, Soft, Open, and Drive Controllers)" | Siemens developer portal, https://developer.siemens.com/s7-plcsim-advanced/overview.html |
| V8.0 supports TIA Portal projects V14 to V21 and CPU firmware V1.8 to V4.1 (S7-1200 G2 V4.1 newly added) | distributor summary of the V8.0 release, https://www.infoplc.net/descargas/siemens/software-step7-tiaportal/tia-portal/descarga-s7-plcsim-advanced-demo-trial (secondary) |

## Which CPU for the test

**CPU 1516F-3 PN/DP, article number 6ES7516-3FP03-0AB0**, with the highest firmware the installed TIA Portal V21
catalog offers that is not above V4.1 (the V8.0 range). Evidence for the article: it is the current S7-1500F
1516F-3 PN/DP (distributor listing with firmware V3.0, https://plc-direct.com/products/6es75163fp030ab0; a Siemens
forum post reports such a CPU running firmware V4.0, https://support.industry.siemens.com/forum/ww/en/post/1270943).
The older 6ES7516-3FN02-0AB0 is a fallback if the catalog does not list the FP03 variant. Take the exact
`typeIdentifier` (for example `OrderNumber:6ES7 516-3FP03-0AB0/V3.1`) from `search_equipment_catalog`, never typed by
hand. The existing standard CPU of `MCP_Livetest_01` (6ES7 516-3AN02-0AB0 V2.9) stays as it is.

## Live test plan (MCP_Livetest_01, PLCSIM instance `MCP_Test`)

Run in `--access-mode full` (Claude Desktop has no form elicitation, see the interim report) unless a step says
otherwise. Record every response and audit record. Set two environment variables in the MCP client configuration
before starting the server: `TIA_SAFETY_PW` (the password to set) and `TIA_SAFETY_PW_WRONG` (any other value).

| # | Call | Expected |
| --- | --- | --- |
| 1 | `network_read search_equipment_catalog` query `6ES7 516-3FP03-0AB0` | entries with versions; pick the highest version ≤ V4.1 |
| 2 | `network_write add_network_device` with that `typeIdentifier`, `deviceName` `F_Station`, `deviceItemName` `F_PLC` | device created; `read_hardware_config` shows the CPU |
| 3 | `plc_read get_safety_administration` | `F_PLC` `available:true`; `passwordSet:false` on a fresh CPU (record it); `settings`, `runtimeGroups` (TIA Portal may create a default group and the main safety block when "generation of default failsafe program" is on: compare `globalSettings`), `programSignatures`, `safetyServices`; the standard `PLC_1` `available:false` with a reason. Every `api_member_missing` is a finding for the provenance table |
| 4 | `plc_read list_safety_blocks plcName F_PLC` | the generated F-blocks with language and signatures (record values before compile) |
| 5 | `plc_write set_safety_password passwordVariable TIA_SAFETY_PW` | applied, `passwordSetAfter:true`; acceptance guard acknowledged by policy |
| 6 | `plc_write update_block_logic` of an F-block (xml) without login | `blocked`, `plc_safety_login_required`; no import in the audit |
| 7 | `plc_write safety_login passwordVariable TIA_SAFETY_PW_WRONG` | item fails (record the category: expected `engineering_rejected`), `loggedOn` stays false |
| 8 | `plc_write safety_login passwordVariable TIA_SAFETY_PW` | `loggedOnAfter:true` |
| 9 | `plc_read get_block_interface` of the main safety FB, then `add_interface_member` (Static, Bool) on it | applied; `get_block_interface` shows the member |
| 10 | `plc_write create_safety_runtime_group runtimeGroupName RTG_Test` | applied with `Create(String)`; record which blocks TIA Portal generated (`list_safety_blocks`) |
| 10a | `plc_write replace_network` (SIMATIC SD) on the main safety block of a runtime group, then `get_safety_administration` and `compile_check` of the PLC | a direct SD import drops `MainSafetyBlockName` (live 2026-11-02, [report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md#re-test-with-b79b2df), L1). Expected now: info guard `plc_import_may_unlink_main_safety_block` describing the route; the item result's `mainSafetyBlockRoute` with `route` `sdViaXmlForMainSafetyBlock`, `mainImport` `simaticMl`, `temporaryBlockState` `deleted`; `MainSafetyBlockName` still set; 0 compile errors; no `zzMcpTmp_*` block left. Repeat with a document that does not compile: item failed with the compiler messages, main block unchanged |
| 10b | `plc_write set_safety_runtime_group runtimeGroupName <group> mainSafetyBlockPath <PLC>/<main FB> mainSafetyInstanceDbPath <PLC>/<its IDB>`, and `get_safety_administration` `attributeInfos` | live 2026-11-02 (L2): `api_member_missing`. Record the `attributeInfos` (names, access modes) and whether route `setAttributeName` binds now |
| 11 | `compile_check` for `F_PLC` | the F-program is compiled (live 2026-11-02, E6: "Compile safety program 'Safety Administration'"); record whether `programSignatures` changed (dedicated F-compile is not available) |
| 11a | `plc_read get_plc_security plcName F_PLC`, then `plc_write set_plc_access_level accessLevel FullAccessIncludingFailsafe` and `disable_plc_confidential_data_protection`, then `compile_check scope hardware` ([CPU protection reference](../SupportedOperations/PLC_SECURITY_SUMMARY.md)) | the access level reads back `FullAccessIncludingFailsafe`; the hardware compile has no protection errors. Record whether `FullAccess` alone would leave a fail-safe password prompt at download |
| 12 | `plc_download` to PLCSIM without `decisions` | expected abort `decision_required` for `SafetyProgram` with `availableSelections` (record them) |
| 13 | `plc_download` to PLCSIM with `decisions {"SafetyProgram":"<member from 12>"}` | guard `download_includes_safety_program` acknowledged; download completes; `plcsim_read` instance state |
| 14 | `plc_download` with a non-PLCSIM `pcInterface` and the same decision, `dryRun:true` | `preview` with the acknowledge guards `download_to_hardware` and `download_includes_safety_program` (no block); never run it for real against a machine |
| 15 | `plc_write safety_logoff`, then the step-9 edit again | logoff applied; the edit is `blocked` with `plc_safety_login_required` |
| 15a | `plc_write safety_login`, then `rename_block` and `copy_block` of an F-FB, `update_db_member` of an F-DB, `update_block_logic` of an F-block in source format (if TIA Portal exports one) | each applied or TIA Portal's own rejection recorded (`engineering_rejected`); record which TIA Portal refuses |
| 16 | read-write client with form elicitation (if available): `safety_login` | exactly one prompt with the acceptance notice; decline changes nothing |
| 17 | `plc_write revoke_safety_password passwordVariable TIA_SAFETY_PW` | `passwordSetAfter:false` |
| 18 | after 17: the step-9 edit without `safety_login` | applied (no password, no login needed, as in TIA Portal); acceptance guard acknowledged |

Afterwards: `save_project` only if the project should keep the F-CPU; otherwise close without saving (`network_write` has no device delete; remove `F_Station` in TIA Portal by hand if the project was saved). The safety
program created by this test is test data and must never be downloaded to a real F-CPU.
