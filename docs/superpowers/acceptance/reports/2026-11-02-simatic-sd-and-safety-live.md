# SIMATIC SD and fail-safe live test on TIA Portal V21

Date: 2026-11-02. Status: **scoped live run, findings fixed offline.** Everything under "Live evidence" was seen live
on TIA Portal V21 in the test projects `MCP_Livetest_01` (standard S7-1500) and `MCP_Safety_01` (F-CPU); nothing in that
section is inferred from offline tests. The fixes listed afterwards were made on branch `fix/live-findings-1` and are
verified offline only (unit tests and the FakeWorker scenarios); they have not run against TIA Portal yet.

Access mode: `full` (no prompts; fail-safe writes fired `plc_safety_acceptance_required`, acknowledged by policy).

## Live evidence

| # | Observation |
| --- | --- |
| E1 | `get_block_content format sd` of a block without texts returns only the `.s7dcl` (UTF-8 BOM, CRLF), **no** `.s7res`. A block with network title and comment exports an `.s7res` with TIA-generated ids (`MLC_4SY`, `MLC_3VL`). |
| E2 | `update_block_logic format sd` (Override) imported a 15-network LAD/FBD/SCL FB with 0 compile errors; the re-export equals the submitted code for every construct: `Contact`, `I_Contact`, `Coil`, `S_Coil`, `R_Coil`, `P_Trig`, `N_Trig`, `Not()`, `GT_Contact`/`EQ_Contact` with the `S7_Templates` `SrcType` template, `Move( in, out1 )`, `Add`, `TON`/`TOF` multi-instances with the `time_type` template, parallel and nested wire branches, FBD `A`/`O`/`X` with a `NOT` input, an SCL network. Only whitespace differs: TIA writes an empty line inside the RUNG before `Move(` and the `Add` template as a multi-line pragma. The reported "export form differs from import form" (`Gt{…}`, `Move{…}`, `PBox`) was **not** reproduced on V21 for these instructions. |
| E3 | `insert_network`, `replace_network` and `delete_network` chained in one call: all succeeded, 0 compile errors, verification correct. `replace_network` on a block that has a network title and comment (TIA's own `.s7res`) succeeded and the texts survived. |
| E4 | `create_block_from_document` with an `.s7res` holding `en-US` and `de-DE` texts failed: `EngineeringTargetInvocationException` "Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text." The same with only `de-DE` texts and caller-chosen ids (`MLC_T1`, `MLC_K1`) **succeeded**. The test project's languages are presumably de-DE only; this was not verified. The reported "LAD import needs en-US for every `.s7res` item" is contradicted in this project; a culture the project does not have makes the import fail. |
| E5 | `create_block_from_document` without any `.s7res` was **refused by this server's own host validation** ("content must hold exactly one .s7res document"): stricter than TIA Portal, whose own export has no `.s7res` (E1). Defect. |
| E6 | `compile_check plcName` on the F-CPU compiles the safety program too (message "Compile safety program 'Safety Administration'"). |
| E7 | **Critical.** `replace_network` (SD Override import) on the main safety block `Main_Safety_RTG1` of F-runtime group "F-Ablaufgruppe 1" succeeded and the block compiled, but afterwards `get_safety_administration` shows the runtime group attribute `MainSafetyBlockName` = null (before: "Main_Safety_RTG1"; `MainSafetyBlockIDbName` stayed "Main_Safety_RTG1_DB"), and the PLC compile fails: "The F-runtime group "F-Ablaufgruppe 1" does not have a valid main safety block." The collective F-signature became 0. The SD Override import evidently replaces the block object and TIA Portal drops the runtime group's reference. |
| E7b | After restoring the project (closed without saving): `set_network_text` (the SimaticML/XML import route, Override) on the same main safety block `Main_Safety_RTG1` succeeded, and afterwards `get_safety_administration` still shows `MainSafetyBlockName` = "Main_Safety_RTG1" and `MainSafetyBlockIDbName` = "Main_Safety_RTG1_DB". The XML route does not unlink the runtime group; only the SIMATIC SD route (`ImportFromDocuments` Override) did (E7). |
| E8 | `plc_read list_safety_blocks` failed: "GetServiceInfos() lists 'Siemens.Engineering.Safety.SafetySignatureProvider', but GetService returned null for it." Defect D13: the listing must not fail because one optional service is not retrievable. |
| E9 | The access mode `full` worked (no prompts); the F-write fired `plc_safety_acceptance_required` (acknowledged by policy), as designed. |
| D14 | `plc_read get_plc_security plcName PLC_1` (S7-1500, `MCP_Livetest_01`) failed: "GetServiceInfos() lists 'Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider', but GetService returned null for it." Same kind as D13. |

### Reads confirmed working live

`read_tag_comments`, `find_unused_blocks`, `get_call_tree`, `analyze_change_impact` (it correctly found that
`Zyklus_SCL` calls `FC_Zaehlen`), `find_objects`, `list_external_sources`, `get_block_interface`, `get_db_values`,
`list_tag_tables`, and `get_safety_administration` (E7, E7b).

## Fixes (offline only)

| Finding | Change | Where |
| --- | --- | --- |
| E5 | The `.s7res` of `create_block_from_document` is optional on host, worker and FakeWorker; a present one is still checked structurally (at most one, same name as the `.s7dcl`); the worker writes no `.s7res` file when there is none. | `SimaticSdBlockDocument`, `PlcBlockEditService.Documents.cs`, FakeWorker |
| E4 | Nothing is refused. The item's messages list the cultures an `.s7res` holds and say that each must be a project language, quoting the live error. Every doc and message that said en-US is required now says "reported by BetterTIA/1748477582; contradicted live 2026-11-02 in a de-DE project". | `SimaticSdBlockDocument.CultureNote`; [V21 pitfalls](../../../SupportedOperations/V21_PITFALLS.md) B1/S3, [SIMATIC SD reference](../../../SupportedOperations/PLC_SIMATIC_SD_SUMMARY.md), [guide](../../../guides/lad-fbd-simatic-sd.md), AGENTS.md, README |
| E2 | The export-form report is downgraded to "reported, not reproduced live on V21 2026-11-02 for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF, Add" in the guard `plc_sd_reimports_export_form`, its planning texts, the `networkCode` note and the docs; the guard stays informative for the instructions not tested. The guide marks every E2 construct **L** and softens "no blank lines inside a RUNG" to a report. | `PlcGuardDefinitions.SimaticSd.cs`, `PlcSdNetworkEditPlanner`, `PlcWritePlanner`, guide |
| E7 | New `plc_write` operation `set_safety_runtime_group` (runtimeGroupName, mainSafetyBlockPath, optional plcName, mainSafetyInstanceDbPath): binds the assignment on the live `RuntimeGroup` (a writable `MainSafetyBlock`/`MainSafetyBlockIDB` property assignable from `PlcBlock`, else a `GetAttributeInfos()`-writable attribute set with `SetAttribute`), reports the route, reads back `MainSafetyBlockName`/`MainSafetyBlockIDbName`; `api_member_missing` when neither route exists, `postcondition_failed` when the read-back differs. Every import route that replaces an F-block records the runtime groups naming it, re-reads them after the import and re-assigns a lost reference (warning); one it cannot restore fails the item's verification (`safetyRuntimeGroup`) with the repair. Info guard `plc_import_may_unlink_main_safety_block` for SD-route imports of a main safety block (E7b: the XML route kept the link). | `PlcSafetyRuntimeGroupBinding`, `PlcMainSafetyLinks` (Contracts), `PlcSafetyService`, `PlcMainSafetyBlockLinkGuard` (worker), `PlcSafetyPlanning`, `PlcVerificationPolicy.Safety.cs`, FakeWorker; [safety reference](../../../SupportedOperations/PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports) |
| E8 (D13) | `list_safety_blocks` reports a listed-but-unretrievable `SafetySignatureProvider` as `signatureState: "unreadable"` with the reason in `problem`; the listing succeeds. | `PlcSafetyService.ServiceDetailed`, `PlcSafetyBlockInfo.SignatureState` |
| D14 | A listed service that `GetService` does not return no longer fails a read: `get_plc_security` reports that setting unavailable and returns the rest; `get_technology_object_hardware_connection` lists such providers in `unavailableServices`; a write that needs the service fails `unsupported_capability`, nothing changed. | `TechnologyObjectRuntime.TryService`, `ListedServices`, `PlcSecurityRun`, `PlcSecurityReflection`, `TechnologyObjectHardware` |
| E6 | Documented: `compile_check` of an F-CPU compiles the safety program. | [safety reference](../../../SupportedOperations/PLC_SAFETY_SUMMARY.md), README |

## Not covered by this run

- `set_safety_runtime_group` and the automatic re-link have not run live: which binding route the live `RuntimeGroup`
  offers is unknown.
- Whether `move_block` or `copy_block` of a main safety block keeps the runtime group reference.
- `create_block_from_document` without `.s7res` and with `overwrite`; an F-block import with a safety password set.
- The export form of instructions other than those listed in E2.

## Re-test with b79b2df

Same day, same projects, build `b79b2df` (the fixes above). Everything in this section was seen live.

### Live evidence

| # | Observation |
| --- | --- |
| L1 | Any SIMATIC SD import (`ImportFromDocuments` Override) of the **main safety block** `Main_Safety_RTG1` (F_FBD FB, number 1, `AutoNumber` false, IDB `Main_Safety_RTG1_DB`) of F-runtime group "F-Ablaufgruppe 1" in `MCP_Safety_01` (PLC_F1) makes TIA Portal drop the group's `MainSafetyBlockName` (null); the PLC compile then fails ("does not have a valid main safety block"). Reproduced twice, including with `b79b2df`. |
| L2 | The automatic re-link and `set_safety_runtime_group` cannot repair it: the live `RuntimeGroup` has no public property `MainSafetyBlock`, and `GetAttributeInfos()` lists no attribute `MainSafetyBlock` → `api_member_missing`. The MCP reported this honestly: the item's verification `safetyRuntimeGroup` was `success: false` with the repair text. |
| L3 | A SimaticML import (`update_block_logic` xml, Override) of the same main safety block **keeps** the reference: seen with `set_network_text` and with a full `update_block_logic` xml. |
| L4 | Workaround, done by hand with existing operations, ending with 0 compile errors, the reference intact and the collective F-signature valid: (a) `create_block_from_document` of a temporary F-FB (unique name, header `S7_Safety := "TRUE"`, the desired SD content with interface and networks) into the main block's group: TIA Portal created an F_FBD FB (auto number 2); (b) `compile_check blockPath <temp>`, **required**: before compiling, `get_block_content format xml` of the temporary block silently returned only the `.s7dcl` (no `.xml`), because TIA Portal does not export an inconsistent block as SimaticML (BetterTIA pitfall B4, now confirmed live; defect D15: the read did not say so); (c) `get_block_content format xml` of the temporary block returned the full SimaticML (FlgNet etc.); (d) in that SimaticML `<Name>` was set to the main block's name, `<Number>` to its number and `<AutoNumber>` to its value (the temporary export had no AutoNumber element; the main export had `<AutoNumber>false</AutoNumber>` as the first `AttributeList` child, which was inserted), everything else (interface, networks, texts) kept, and imported with `update_block_logic format xml` onto the main block with its current xml hash; (e) `delete_block` of the temporary block; (f) compile: 0 errors; `get_safety_administration`: `MainSafetyBlockName` still set. |
| D13 | Confirmed fixed: `list_safety_blocks` returned 30 F-blocks with signatures; the standard OB `Main` reported `signatureState: "unreadable"` (it offers no F-signature service). |
| D14 | Confirmed fixed: `get_plc_security` returned access level `FullAccess`, master secret `None`, and the access-control part as unavailable instead of failing. |
| E5 | Confirmed fixed: `create_block_from_document` without `.s7res` created the FC `MCP_SD_OhneTexte`. |

### Changes after the re-test (offline only)

| Finding | Change | Where |
| --- | --- | --- |
| L1-L4 | The workaround is automatic and internal: an SD-document write onto a main safety block (`update_block_logic` sd or a bundle without `.xml`, `insert_network`/`replace_network`/`delete_network`, `create_block_from_document` with `overwrite`) runs L4 a-f in one worker call: deterministic temporary name `zzMcpTmp_<block>_<8 hex>` (refused if it exists), the temporary `.s7dcl` without `S7_BlockNumber` and with `S7_Safety := "TRUE"` when missing, compile of the one temporary block, stop before the main block (temporary block deleted, item failed with the compiler messages) on a compile error or a missing SimaticML, the Name/Number/AutoNumber transplant, the existing xml import onto the main block, deletion of the temporary block on every path (a block that cannot be deleted is named). The item result's `mainSafetyBlockRoute` (`route: "sdViaXmlForMainSafetyBlock"`, temporary block, `mainImport: "simaticMl"`, steps) says what was done; a failed item carries it as its result. Other F-blocks keep the direct SD route; the post-import check `safetyRuntimeGroup` stays on every route. | `PlcMainSafetyBlockSdRoute`, `SimaticMlBlockIdentity` (Contracts), `PlcMainSafetyBlockSdRouteWorker`, `MainSafetyBlockSdUpdate`, `PlcBlockEditService.Documents.cs` (worker), `PlcMainSafetyBlockRouteContract`, `PlcSafetyPlanning`, guard `plc_import_may_unlink_main_safety_block` (host), FakeWorker `PlcWriteRoundtripScenario.MainSafetyRoute.cs` |
| L2 | `set_safety_runtime_group` documents the live result; before giving up it tries `SetAttribute("MainSafetyBlockName"/"MainSafetyBlockIDbName", <name>)` only when `GetAttributeInfos()` lists that name as writable, and its failure lists every attribute with its access mode. `get_safety_administration` reports `attributeInfos` (name, access mode) of every runtime group. | `PlcSafetyRuntimeGroupBinding`, `PlcSafetyService`, `PlcSafetyRuntimeGroupInfo.AttributeInfos` |
| D15 | A SimaticML export that TIA Portal refuses is no longer dropped silently: `get_block_content xml` (and every request that exports the block) carries the warning "was not exported as SimaticML: TIA Portal does not export inconsistent blocks …; run compile_check and read again" with the documents TIA Portal did export; the planning reads that need the SimaticML name the cause. | `BlockExporterXmlBundle`, `SimaticMlExportNotes`, `PlcDocumentEditPlanner`, `PlcInterfaceEditPlanner`, `PlcVerificationPolicy.Editing.cs`, `InstanceDbXml` |

### Not covered

- The automatic route as one call has not run live; nor the name-attribute route (iii) of `set_safety_runtime_group`, or what `attributeInfos` the live group reports.
- Whether TIA Portal accepts a temporary F-block import while a safety offline program password is set (the login rule applies as for the main block).

## Addendum: K27, TIA Portal terminates on an F-CPU connection through PLCSIM (2026-11-02)

Seen live, reproduced four times, TIA Portal V21, PLCSIM Advanced 8.0, project `MCP_Safety_01`, PLC_F1 = CPU 1516F-3 PN/DP
(6ES7 516-3FN02-0AB0, V2.9), PC interface PLCSIM #1, target interface `1 X1`, a fresh PLCSIM Advanced instance `MCP_Safety`
(CPU1500_Unspecified, Stop; `MCP_Test` powered off). Build with the former TLS refusal (throw from the callback).

| # | Step | Observation |
| --- | --- | --- |
| K27.1 | `plc_download` without decisions | `portal_lost` "Access to a disposed object of type 'Siemens.Engineering.Connection.ConnectionConfiguration'"; afterwards no TIA Portal process (`bind_project list_portals` → `portals: []`); the instance unchanged (still CPU1500_Unspecified: nothing transferred). |
| K27.2 | `plc_download` with `decisions: { SafetyProgram: ConsistentDownload }` | the same crash. |
| K27.3 | `plc_tia_online go_online` (no download at all) | the same crash. |
| K27.4 | Control without the MCP: "Online verbinden" on PLC_F1 in the TIA UI, same instance | TIA Portal shows the TLS trust prompt "'PLC_F1' ist möglicherweise kein vertrauenswürdiges Gerät" (the IP address does not match the certificate; unknown self-signed certificate). "Verbindung abbrechen" (= `NonTrusted`): TIA Portal keeps running; the online status says the connection failed because the device is not trustworthy. The standard CPU PLC_1 of `MCP_Livetest_01` never showed this prompt; download and online worked there through the MCP. |

Most likely cause (not proven): the recorder set `NonTrusted` and then threw `PlcDownloadAbortedException` /
`PlcCallbackAbortedException` from inside TIA Portal's online-legitimation callback. An earlier run (same day, instance
`MCP_Test`, CPU1516, Run) ended the same way; the PLCSIM instance type was not the cause.

Fix (branch `fix/catalog-life`, offline only, not live-tested): a refused TLS question whose `NonTrusted` could be set
returns normally from the callback; the abort and `abort.tls` are recorded, and plc_download, plc_tia_online,
plc_station_upload and compare_to_online fail cleanly with that abort whatever TIA Portal then does. The callback throws
only when `NonTrusted` cannot be set. Live re-check: repeat K27.1 and K27.3 and confirm TIA Portal keeps running and the
call returns `worker_operation_failed` with `abort.tls` (entry `selected` `NonTrusted`, source `policy` for plc_download).
