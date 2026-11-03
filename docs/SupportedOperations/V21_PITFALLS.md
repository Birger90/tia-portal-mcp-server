# TIA Portal V21 pitfalls reported by rickgaiser/TiaMcp

**Language:** English · [Deutsch](V21_PITFALLS.de.md)

Roadmap row 22 of the [feature adoption roadmap](../roadmap/competitor-feature-adoption.md) (row 21,
the Portal listing, is in the [project reference](PROJECT_OPERATIONS_SUMMARY.md); its live case is L11
below). Every behaviour below is **reported by rickgaiser/TiaMcp and not verified on V21 here**: no
Siemens source and no live finding of this repository confirms any of them (see
[evidence](#evidence)). The guard and warning texts therefore say "is reported to" until the matching
case of the [live acceptance list](#live-acceptance-list) has been reproduced on V21 and recorded. Not
live-tested.

One V21 pitfall of the SIMATIC SD route is **seen live** by this repository (2026-11-02): an SD import of the main safety
block of an F-runtime group drops the group's reference; see
[pitfall 6](#pitfall-6-sd-import-of-a-main-safety-block-live-2026-11-02) with the workaround the server applies.

Further pitfalls reported by the BetterTIA tools are listed in
[pitfalls reported by BetterTIA](#pitfalls-reported-by-bettertia-not-verified) (von BetterTIA berichtet, nicht
verifiziert); they are documented only, no guard was added for them.

The SIMATIC SD detection logic is adopted from rickgaiser/TiaMcp (AGPL-3.0) under
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md); the guards, the export ledger, the read flags
and the verification comparison are this fork's.

## Summary

| # | Reported pitfall (unverified on V21) | Where this server can hit it | What this server does |
| --- | --- | --- | --- |
| 1 | The SIMATIC SD (`.s7dcl`) import is reported to alter without an error (a) an array access nested inside an index in a LAD/FBD network, (b) a member typed with a project FB/UDT named like a system type (TON, TOF, TP, TONR, CTU, CTD, CTUD, R_TRIG, F_TRIG), (c) AT overlays | `plc_write update_block_logic` with a bundle that has no `.xml` document (the worker then calls `PlcBlockComposition.ImportFromDocuments`); `vci_write sync_to_project` of `SimaticSD` workspace files | info guard `plc_s7dcl_import_may_alter`, an item warning, a `s7dclConstructs` verification entry that fails when the re-export differs; `get_block_content` (xml) warns about such blocks. VCI is not checked (see [left out](#left-out)) |
| 2 | TIA Portal is reported to terminate when it overwrites a block with a textual interface (`S7_EditorMode := "[SCL,TextualInterface]"`) that was exported more than once since the project was opened or the block was last written | every overwrite of an existing block: `update_block_logic` (both formats) and the interface member edits | worker export ledger per binding, info guards `plc_textual_interface_overwrite_may_terminate` and `plc_save_before_write_recommended`, an item warning; `get_block_content` (xml) warns |
| 3 | Exporting a (classic) graphic list and reading `HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText` are reported to crash TIA Portal V21 | `hmi_write export_classic_objects` with `graphicLists` (issue #28) | refused before `Export` is bound (catalog and worker, "skipped: reported to crash TIA Portal V21"), pinned by a source test; see [pitfall 3](#pitfall-3-graphic-lists-and-scriptdiagnosisoverviewtext) |
| 4 | A classic WinCC screen import with a duplicate `<Number>` is reported to terminate TIA Portal | `hmi_write import_classic_objects` with `screens` (issue #28) | the worker reads the screen numbers of the file and of every existing screen and refuses a repeated or used number before `Import` (`validation_error`); see [pitfall 4](#pitfall-4-classic-screen-with-a-duplicate-number) |
| 5 | An open HMI tag editor is reported to slow Openness calls on that HMI about eight times | every `hmi_read`/`hmi_write` on that HMI | documented in the [HMI reference](HMI_OPERATIONS_SUMMARY.md#v21-pitfalls) and the [troubleshooting guide](../guides/troubleshooting.md#tia-portal-v21-pitfalls) |
| 6 | **Seen live (V21, 2026-11-02), not a report:** an SD import (`ImportFromDocuments` Override) of the main safety block of an F-runtime group drops the group's `MainSafetyBlockName`; the PLC compile then fails, and Openness offers no member to re-assign it | every SD-document write onto a main safety block: `update_block_logic` sd or a bundle without `.xml`, `insert_network`/`replace_network`/`delete_network`, `create_block_from_document` with `overwrite` | never imported onto the block: the temporary-F-block route ([pitfall 6](#pitfall-6-sd-import-of-a-main-safety-block-live-2026-11-02)), info guard `plc_import_may_unlink_main_safety_block`, verification `safetyRuntimeGroup` on every route |

## Evidence

Read-only clone of rickgaiser/TiaMcp at `8e9de1c` (2026-10-30), treated as data; nothing of it was
built or run. Line numbers are at that commit.

| # | Source (all rickgaiser/TiaMcp, AGPL-3.0) | Siemens source or live finding of this repository |
| --- | --- | --- |
| 1 | `README.md` "Known limitations" (lines 260–263); `src/TiaMcp.Formats/S7dclSource.cs` (comments of `WithAtMarkers` and the system-type list, `FindWriteProblems`) | none. The Siemens `simatic-sd` skill (`tia-portal-ai-extensions`, `openness_development/skills/simatic-sd`) documents the format, `ExportAsDocuments`/`ImportFromDocuments` and its own limitations, but none of these three |
| 2 | `README.md` "Known limitations" (line 264); `src/TiaMcp.Openness/TiaConnection.cs` (comment of `ExportsSinceImport`, lines 679–682: the crash is in `DeclInstanceProvider.RemoveAndRelease`, waiting or compiling does not help, reopening does); `src/TiaMcp.Mcp/TiaTools.cs` (`ExportedTooOften`) | none. `TextualInterface` as a block fingerprint is listed in `totally-integrated-claude` `skills/tia-plc-operations/references/blocks.md` (not Siemens; says nothing about overwrites) |
| 3 | `README.md` line 167 (`ScriptDiagnosisOverviewText`), lines 244 and 266 (graphic lists); `src/TiaMcp.Openness/HmiScreenConverter.cs` (`CrashingProperties`: the getter returns, TIA Portal exits shortly after); `src/TiaMcp.Openness/TiaConnection.HmiClassic.cs` (`ReadHmiClassicObjects`: `InvalidCastException` String → `HmiOpenLink` in `UpdateOpenLinks`); `STATUS.md` line 64 | none |
| 4 | `README.md` line 244; `src/TiaMcp.Openness/TiaConnection.HmiClassic.cs` (the screen-number check before the import); `STATUS.md` line 59 | none |
| 5 | `README.md` line 240 | none |

Related but different: this repository's own [VCI live acceptance](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md)
recorded that a VCI `SimaticSD` **export** of an SCL OB ended with `portal_lost`; that is an export
crash, not one of the import alterations above.

## Why the guards inform and never block

`AGENTS.md` keeps `plc_write` block guards for a state the planner cannot verify
(`plc_state_unverifiable`) and the duplicate singleton OB (`plc_ob_singleton_exists`); everything else
informs, and the access mode is the human's control. These pitfalls are unverified reports. A block guard would make the SIMATIC SD
route, and (pitfall 2) every overwrite of a textual-interface block through `plc_write`, unwritable
for a reason nobody has reproduced on V21. The guards are listed in the response of a dry run and of
an actual call, so a caller sees them before deciding; nothing asks the user (`plc_write` has
`ConfirmsEveryCall=false`).

## Pitfall 1: SIMATIC SD import

**Write path.** `update_block_logic` with `format:"xml"` imports through SimaticML
(`PlcBlockComposition.Import(FileInfo, ImportOptions.Override)`) whenever the submitted bundle has a
delimited `.xml` document; the `.s7dcl`/`.s7res` companions are then context only and must be
unchanged. Only a delimited bundle **without** an `.xml` document and with an `.s7dcl` document takes
the SIMATIC SD route (`ImportFromDocuments(..., ImportDocumentOptions.Override)`), for example after
`get_block_content` could export only the document package. `update_type_content` imports PLC data
types only as SimaticML or external source, so no `.s7dcl` write path exists for UDTs.

**Detection** (Siemens-free, `TiaMcpServer.Contracts/Plc/Interface/S7dclImportHazards.cs`, adopted):

- a declaration whose type is `_.NAME`, `_."NAME"` or `"NAME"` for one of the nine system type names
  (also as `Array[...] of`), case-insensitive;
- in every network whose attribute list says `S7_Language := "LAD"` or `"FBD"`, a `[` inside another
  `[` outside quoted names and strings;
- an AT overlay of the block **as it is now** (`Member/AttributeList/StringAttribute[@Name="At"]` in
  the SimaticML of the planning read) whose member is still declared in the submitted `.s7dcl`
  declaration part. `.s7dcl` has no AT notation, so the overlay is invisible in the submitted text; a
  block whose planning read has no readable SimaticML cannot be checked for overlays, which the guard
  says.

**Guard and warning.** `plc_s7dcl_import_may_alter` (info) fires when the SIMATIC SD route is taken
and a construct was found or the overlays could not be checked. Its message lists each finding and
recommends the SimaticML route (read `get_block_content` with `format:"xml"`, edit the `.xml`
document, submit the whole bundle). The item result carries a warning with the same recommendation.

**Verification.** The re-export hash cannot show a silent alteration. For an item with findings the
content read-back compares each finding with the re-exported bundle: the member must still be
declared with the same project type spelling, the same-numbered LAD/FBD network must still contain a
nested index, and the overlay must still carry its AT attribute in the re-exported SimaticML. The
verification entry is `check:"s7dclConstructs"` with `expected` (the findings) and `actual`
(`preserved`, `altered` with what the re-export shows, or `unverifiable` when the re-export lacks the
document). Anything but `preserved` makes the entry fail (not verified), so the call's `success` is
false; the new content hash is still reported.

**Read side.** `plc_read get_block_content` with `format:"xml"` adds an item warning when the bundle's
`.s7dcl` has one of the constructs (overlays from the bundle's own SimaticML), and a separate warning
when it declares a textual interface. The content and its hash are unchanged.

## Pitfall 2: textual-interface overwrite

**Export ledger** (worker, `Openness/Block/BlockExportLedger.cs`; counting rules from the report,
code this fork's):

- A new engineering root resets the ledger in `TiaPortalSession.SetActiveContext`. A root this worker
  opened (`open_project`, `create_project`, an `.als21` opened through `LocalSessions.Open`) starts
  every count at a known zero. Any other root (`bind_project` of an already open project, a
  re-selected project) makes every count unknown. Save-as keeps the root and the counts.
- Every export call of a block counts once, attempted ones included: a SimaticML bundle
  (`Export` plus `ExportAsDocuments`, as `get_block_content` xml and the worker's own hash and
  companion checks do), a SimaticML document alone (interface reads and edits, `get_db_values`,
  copy/move), the post-import `ExportAsDocuments` check, external-source generation (only the
  requested block: the dependency closure of a `withDependencies` source export is not counted). An
  unknown count stays unknown.
- A returned import of the block (`update_block_logic`, SimaticML or SIMATIC SD route with a success
  state, or external-source generation) sets its count to a known zero.
- A `vci_write` export, `sync_to_workspace` or `sync_to_project` makes every count unknown (VCI leaves
  are not counted one by one); textual-interface flags stay.
- Keys are the PLC software name and the block name, case-insensitive. A renamed, deleted or
  re-created block keeps the name's count (an over-count). Two blocks of the same name in different
  software units share one count, so a write of one also restarts the other's count (an under-count).
  Not counted: exports by other Openness clients or the TIA Portal UI, technology object exports and
  imports, library operations (`update_project`, type and master copy instantiation).
- The count is message text only: no guard fires or stays silent because of it.
- The ledger also keeps whether the last SimaticML bundle export of the block declared a textual
  interface.

The worker method `get_block_export_state` (Observe, gated as `plc_write`, no public operation) reads
one entry without exporting: `{ plcName, blockName, exportCount, basis, textualInterface }` with
`basis` `opened_by_this_server`, `written_by_this_server` or `unknown`.

**Guards.** For `update_block_logic` and the interface member edits of a block, a textual interface is
detected in the `.s7dcl` of the planning read or of the submitted (or planned) bundle; an
external-source (`format:"source"`) write reads the ledger, whose flag comes from an earlier xml read
in the same binding. Then `plc_textual_interface_overwrite_may_terminate` (info) states the counted
exports (or why the count is unknown) and `plc_save_before_write_recommended` (info) recommends
`save_project` first, because a terminated TIA Portal loses every unsaved change. The item result
carries a warning.

**Finding: the reported condition applies to every overwrite through `plc_write`.** After the
planning read, the worker exports the block again before importing it: the hash re-check
(`get_block_content`-equivalent bundle export) and, for a bundle with companions, the
unchanged-companion check (a second bundle export). So at the import the block has been exported at
least twice since any reopen. Saving, closing and reopening the project resets the count but does not
avoid the reported condition with this write path, and the guard says so. Making such writes safe
would need a write path that exports the block at most once after a reopen (no planning read, one
shared worker export for the hash and companion checks); that is left out until the report is
reproduced (see [left out](#left-out)).

## Pitfall 3: graphic lists and `ScriptDiagnosisOverviewText`

No code path exports a graphic list or reads screen-item properties generically:

- `hmi_read list_text_and_graphic_lists` reads only the names of `HmiSoftware.HmiGraphicLists`
  (Unified); nothing calls an export on them. `hmi_write export_text_lists` exports only the user text lists of
  `HmiSoftware.HmiTextLists` (the export member bound at runtime, `HmiTextListFiles`); graphic lists and system text
  lists are never exported. The reported graphic-list crash is a **classic** WinCC
  export (rickgaiser exports classic graphic lists from `Siemens.Engineering.Hmi.TextGraphicList` and
  reports an `InvalidCastException` in `UpdateOpenLinks`). The classic export `hmi_write export_classic_objects`
  (issue #28, `HmiClassicObjects`) refuses `graphicLists` in the catalog and in the worker before it binds `Export`
  ("skipped: reported to crash TIA Portal V21 …; nothing was written"); listing, importing and deleting graphic lists stay allowed.
- `hmi_read list_screen_items` reads only the typed `Name`, `Left`, `Top`, `Width`, `Height`,
  `Visible` and `Enabled` of each item; there is no generic attribute dump (out of scope in the HMI
  reference). `HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText` is never read.
- `hmi_read validate` with `screenItems` calls `IValidator.Validate()` on each item, which may read
  properties inside TIA Portal; whether that reaches `ScriptDiagnosisOverviewText` is unknown (live
  case L9).

`HmiPitfallSourceTests` pins this on the HMI worker sources (`TiaMcpServer.OpennessWorker/Openness/Hmi`):
it fails when one of them mentions `ScriptDiagnosisOverviewText`, calls any `.Export(` directly or enumerates
attributes generically (`GetAttributeInfos`), when `HmiGraphicLists` is used anywhere but the name
read in `HmiSoftwareInfoReader`, or when a file other than `HmiTextListFiles.cs` and `HmiClassicObjects.cs` binds `"Export"` (the first must not
name graphic or system text lists, the second must refuse graphic lists before it binds `Export`). A future path that needs one of these must skip graphic lists and
that property and report "skipped: reported to crash TIA Portal V21".

## Pitfall 4: classic screen with a duplicate `<Number>`

Applied since issue #28 (classic WinCC, not live-tested; see the [Classic WinCC and Unified extensions](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md)):
before `import_classic_objects` with `classicKind:"screens"` calls `Import`, the worker reads every
`Hmi.Screen.Screen` (`AttributeList/Name`, `Number`) of a private copy of the file (`HmiClassicScreenNumbers`, Contracts)
and the number of every existing screen (generic `GetAttribute("Number")`, falling back to a private export of that
screen), and refuses the item with `validation_error` when a number repeats in the file or another existing screen uses
it (rickgaiser refuses it too; TIA Portal is reported to terminate). A same-named existing screen is the one an
overriding import replaces and does not count. A screen whose number cannot be read is named in a warning and the import
runs. Classic graphic lists are never exported (pitfall 3).

## Pitfall 5: open HMI tag editor

Reported: an open HMI tag editor in TIA Portal ("Show all tags") slows every Openness call on that
HMI, a read or write of its tag tables about eight times. Close the editor while `hmi_read`/`hmi_write`
work on that HMI. Documentation only; the server cannot detect an open editor.

## Pitfall 6: SD import of a main safety block (live 2026-11-02)

Seen live on TIA Portal V21 on 2026-11-02 in the test project `MCP_Safety_01` (PLC_F1, F-runtime group
"F-Ablaufgruppe 1", main safety block `Main_Safety_RTG1`, an F_FBD FB number 1 with `AutoNumber` false), reproduced
twice and once more with build `b79b2df` ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md#re-test-with-b79b2df), E7, L1-L4):

- Every SIMATIC SD import (`ImportFromDocuments` Override) of the main safety block makes TIA Portal drop the runtime
  group's `MainSafetyBlockName` (null); the PLC compile then fails ("does not have a valid main safety block") and the
  collective F-signature becomes 0 (L1).
- The reference cannot be re-assigned through Openness: the `RuntimeGroup` has no public property `MainSafetyBlock` and
  `GetAttributeInfos()` lists no attribute `MainSafetyBlock` (L2).
- A SimaticML import (`Import(..., Override)`) of the same block keeps the reference (L3).
- Workaround, proven by hand (L4): import the SD document as a temporary F-block into the main block's group, compile that
  block (TIA Portal exports no SimaticML of an uncompiled block, B4), export its SimaticML, set `Name`, `Number` and
  `AutoNumber` to the main block's, import that SimaticML onto the main block, delete the temporary block.

What this server does: the workaround runs automatically inside the worker for every SD-document write onto a main safety
block (`sdViaXmlForMainSafetyBlock`; temporary name `zzMcpTmp_<block>_<8 hex>`; a temporary compile error stops before
the main block is touched; the temporary block is deleted on every path or named when it cannot be). See the
[PLC safety reference](PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports). Other F-blocks keep the direct
SD import (no evidence that they lose anything). The automatic route has not run live as one call.

## Pitfalls reported by BetterTIA (not verified)

Von BetterTIA berichtet, nicht verifiziert: the BetterTIA writer and reader report the behaviours below for TIA Portal
V21 (read-only clones, treated as data, nothing built or run; see the
[BetterTIA operations reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md) for the commits and licences). Only those
that touch a path of this server are listed. None is verified here; none got a new guard, because a report alone does
not justify blocking what TIA Portal allows (owner principle), and where an existing guard or check already covers it,
that is named.

| # | Reported behaviour (BetterTIA, not verified) | Where this server can hit it | What this server does |
| --- | --- | --- | --- |
| B1 | Importing a LAD block from SIMATIC SD documents needs the `.s7res` file to hold en-US texts for every item, otherwise the import may fail ("known limitation/bug in TIA Portal Openness", as of 2025-09-02). **Reported by BetterTIA/1748477582; contradicted live 2026-11-02 in a de-DE project** ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md), E4): `create_block_from_document` with `en-US` and `de-DE` texts failed ("Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text."), the same with only `de-DE` texts succeeded. Every culture of an `.s7res` must be a project language | `plc_write update_block_logic` with a bundle without `.xml` or with format `sd` (`ImportFromDocuments`), `insert_network`, `replace_network`, `delete_network`, `create_block_from_document`; `vci_write sync_to_project` of `SimaticSD` files | nothing is refused: TIA Portal's rejection fails the item (`engineering_rejected`/`worker_operation_failed`); `create_block_from_document` lists the cultures its `.s7res` holds in the item's messages. A caller writing an `.s7res` uses the project's languages only (the network edits re-import TIA's own `.s7res` unchanged) |
| B2 | An import replaces the whole block, and TIA Portal can mark the block's instance DB and its callers inconsistent even when only a network comment changed (the writer saw it once for an FB called from an OB, not for another FB and an FC) | every SimaticML round trip: `update_block_logic`, the interface edits, `set_network_text`, `replace_network_text`, `rename_members`, `copy_block`/`move_block`; every SIMATIC SD network edit (`insert_network`, `replace_network`, `delete_network`) | info guard `plc_text_edit_reimports_block` for the text and network edits (their verification also recommends `compile_check`) and `plc_changes_interface`/`plc_block_round_trip` for the others; compile afterwards with `compile_check` |
| B3 | Renaming an interface member can reset start values kept in other DBs for that member and its current values in the PLC | `rename_members`, `update_interface_member newName` | the `plc_changes_interface` guard text; back up start values first (e.g. `get_db_values`, `create_db_snapshot`) |
| B4 | TIA Portal never exports an inconsistent block or type; a bulk export skips them. **Confirmed live on V21 2026-11-02** for the SimaticML export: `get_block_content format xml` of an F-FB just created from an SD document (not compiled) returned only its `.s7dcl`; after `compile_check` of the block the full SimaticML came back ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md#re-test-with-b79b2df), L4b) | every read and round trip that exports (`get_block_content`, the planned edits, `export_blocks`, `move_block`) | an export failure is reported as such (`plc_state_unverifiable` info guard where planning needs the export); since 2026-11-02 (defect D15) a refused SimaticML export is no longer dropped silently: `get_block_content xml` carries the warning "was not exported as SimaticML … run compile_check and read again" with the documents TIA Portal did export, and the planning reads that need the SimaticML name the cause; the main safety block route compiles its temporary block before exporting it; compile the block first |
| B5 | A PLC data type name is unique across the whole PLC: importing a name that exists in another type group fails even with `ImportOptions.Override` | `update_type_content` (imports into the type's own group), `generate_blocks_from_source` with a `targetGroupPath` | nothing extra: TIA Portal's rejection fails the item; import into the group the type already lives in |
| B6 | A know-how-protected F-OB was not recognised as fail-safe by its programming language; the writer also treats every block a safety runtime group names (F-OB, main safety block, its instance DB, the information DB) as fail-safe | the fail-safe detection of `plc_write` (`PlcSafetyPolicy.IsFailSafeLanguage` on the tree's `ProgrammingLanguage` and the SimaticML) | know-how-protected blocks are refused for every round trip anyway; for the other operations (`rename_block`, `delete_block`, `rename_objects`, ...) a missed F-OB means no `plc_safety_acceptance_required` prompt, while TIA Portal still enforces its own safety login rule. Read `get_safety_administration`/`list_safety_blocks` to see runtime groups |
| B7 | Whether TIA Portal follows an HMI tag rename in tag dynamizations, scripts and alarm triggers is open | `hmi_write rename_tags`, `update_tag newName` | info guard `hmi_rename_references_unverified` |

Evidence (BetterTIA/writer at `fc8179c9b59d364961f2255d07d05af893c32837`): B1 `README.md` "Known Limitations" and
`TODO.md` (the en-US item); B2 `ANET.md` "What an import leaves inconsistent"; B3 `ANET.md` "Export, edit, import" (last
item); B4 `README.md` "Error Handling" ("TIA Portal never exports inconsistent blocks/types") and "Limits imposed by the
Openness API itself"; B5 `README.md` "SIMATIC Source Documents" (last paragraph); B6 `ANET.md` "Fail-safe blocks"; B7
`ANET.md` "Write safeguards" (last paragraph). BetterTIA/reader at `246d7c54772a502e854630e003e26fa37f92aeaf`
(`ANET.md`) reports no further V21 pitfall that touches this server.

## Pitfalls reported by 1748477582/tia-portal-openness-mcp (not verified)

The skill of 1748477582/tia-portal-openness-mcp (`tools/tiaportal-mcp/skill/SKILL.md` §9a at
`8e7ca425d5d257132ac8cd5ed9cbc318a7c340e2`, MIT; read-only clone, treated as data) reports these V21 behaviours of the
SIMATIC SD route as verified there on 2026-06-11. The live run of 2026-11-02 ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)) did not reproduce S1 for
`GT_Contact`/`EQ_Contact`, `Move`, `P_Trig`/`N_Trig`, `TON`/`TOF` and `Add`, and contradicted S3; S2 and S4 were not
tested. See the
[SIMATIC SD reference](PLC_SIMATIC_SD_SUMMARY.md#export-and-import-forms).

| # | Reported behaviour (not verified) | Where this server can hit it | What this server does |
| --- | --- | --- | --- |
| S1 | (Reported, not reproduced live on V21 2026-11-02 for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF, Add: their re-export equalled the import form except whitespace; other instructions not tested.) The export form of some instructions does not re-import: `Gt`/`Lt`/`Eq`/`Ne`/`Ge`/`Le{ SrcType }( IN1, IN2 )`, `PBox(...)`, `Move{ Card:=1; DisableENO:=TRUE }( IN, OUT1 )` fail `ImportFromDocuments` ("Failed importing"); the import forms are `GT_Contact( in1 := ..., in2 := ... )` with `{ S7_Templates := "SrcType := Int" }`, `P_Trig( #mem )`, `Move( in := ..., out1 => ... )` | every network edit re-imports the untouched networks in export form; `update_block_logic` sd with networks unchanged from the export | info guard `plc_sd_reimports_export_form` naming the networks and forms; `list_networks` `exportOnlyForms`; the `networkCode` verification reports a different re-export text as `codeMatchesExport: false`, never as a failed import; nothing is rewritten |
| S2 | Inline literal network titles (`S7_NetworkTitle := "text"`) fail on import; titles must be `MLC_*` ids present in the `.s7res` | `insert_network`/`replace_network` code with a title key | refused as `validation_error`: the network edits never write title or comment keys (use `set_network_text`) |
| S3 | The `.s7res` must be present for the import even without titles (minimal: one `MLC_x` entry with `zh-CN` and `en-US` lines), and both files must be UTF-8 with BOM. **Contradicted live 2026-11-02** (E1, E4, E5): TIA Portal V21's own export of a block without texts is only the `.s7dcl` (UTF-8 BOM, CRLF), and an `en-US` line made the import fail in a de-DE project | `create_block_from_document`; `update_block_logic` sd without `.s7res` | since 2026-11-02 `create_block_from_document` accepts a bundle without `.s7res` (the refusal was stricter than TIA Portal, defect E5); a present one is still checked structurally (at most one, same name as the `.s7dcl`) and its cultures are listed; no ids are generated; every staged document is written with exactly one UTF-8 BOM, and a missing BOM is reported in the result's messages |
| S4 | A pure SCL block cannot hold LAD networks; a block with `S7_PreferredLanguage := "LAD"` can hold SCL networks | `insert_network`/`replace_network` with another language | not refused: TIA Portal decides; documented |

## Observed live in this repository: exceptions from TIA Portal callbacks (K27)

Unlike the reports above, this one was **seen live** (2026-11-02, TIA Portal V21, PLCSIM Advanced 8.0, project
`MCP_Safety_01`, PLC_F1 = CPU 1516F-3 PN/DP V2.9 on PLCSIM #1 / `1 X1`, reproduced four times): when the
online-legitimation callback refused the device's TLS certificate by **throwing** (after setting `NonTrusted`),
`plc_download` (with and without `SafetyProgram`) and `plc_tia_online go_online` ended in `portal_lost` ("Access to a
disposed object of type 'Siemens.Engineering.Connection.ConnectionConfiguration'") and the TIA Portal process was gone.
"Online verbinden" in the TIA UI on the same device showed the trust prompt; "Verbindung abbrechen" (the same
`NonTrusted`) left TIA Portal running. The throw is the most likely cause, not a proven one.

What this server does since then: a refused TLS question whose `NonTrusted` could be set returns normally from the
callback and the call fails with the recorded abort (`abort.tls`); it throws only when `NonTrusted` cannot be set (see
the [PLC download reference](PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)). The non-throwing
refusal is not live-tested. **Open risk:** an unanswered download, upload or password configuration still aborts by
throwing from its callback (no documented non-throwing cancel exists); whether that can terminate TIA Portal too has no
live evidence. Answering every presented configuration (`decisions`, `passwords`) avoids the throw.

## Pitfalls reported by asckye/TIA_Portal_Openness_MCP and others (not verified)

Read on 2026-11-02 from read-only clones, treated as data, never built or run: asckye/TIA_Portal_Openness_MCP (MIT) at
`902a86f` (`CHANGELOG.md`, `docs/troubleshooting/openness-limitations.md`, commit `4b17a04`), 1748477582/tia-portal-openness-mcp
(MIT) at `8e7ca42` (`CHANGELOG.md` 2.5.3, commit `5e39e0d`) and heilingbrunner/tiaportal-mcp (MIT) at `72d2ade` (commit
`f4eb4d7`). None is verified here; the upstream-watch report of 2026-11-02 lists them with their sources.

| # | Reported behaviour (not verified) | Where this server can hit it | What this server does |
| --- | --- | --- | --- |
| A1 | Native PLC cross-reference queries (`CrossReferenceService`) have a recorded TIA Portal exit on V21 (asckye keeps them opt-in on V21, asks to save the project before the first query and never retries after an exit; "compiled" does not make the query safe; V21 confirmation "pending / NOT RUN" there) | `read_cross_references`; the program analysis of `plc_read` (`get_call_tree`, `find_unused_blocks`, ...) sweeps cross-references per block folder | nothing extra: a TIA Portal exit becomes `portal_lost` with binding invalidation and no replay (see `AGENTS.md`, Portal loss); save with `save_project` before a large sweep. Live case X1 below |
| A2 | Renaming a WinCC Unified script library type has a measured crash (asckye) | none: this server has no library type rename for HMI script types (`library_write` renames types generically; Unified script types are not distinguished) | documented; a `library_write rename_type` on such a type ends in `portal_lost` if it happens |
| A3 | Answering `ActiveTestCanBeAborted`/`ActiveTestCanPreventDownload` with `AcceptAll` aborts test and debug functions running on the CPU during a download (1748477582 2.5.3, after the manual) | `plc_download` | no built-in default exists for these configurations: the download aborts with `decision_required` and `availableSelections`, so only a caller `decisions` entry can answer them |
| A4 | The OPC UA export needs the PLC offline (heilingbrunner) | `plc_write export_opcua_xml` | stated in the tool description; TIA Portal decides |

## Left out

| Item | Reason |
| --- | --- |
| Refusing the SIMATIC SD route or textual-interface overwrites (rickgaiser refuses both) | Unverified reports; see [why the guards inform](#why-the-guards-inform-and-never-block). |
| Checking `vci_write sync_to_project` of `SimaticSD` workspace files | The files are edited outside the server and imported as a scope inside one transaction; the planner reads no file content and the current SimaticML of each leaf would need one export per leaf (which itself raises pitfall 2's count). Export defaults to SimaticML, so `SimaticSD` files exist only when a caller asked for that format; the VCI reference already says `SimaticSD` export can crash TIA Portal. |
| A write path with at most one export after a reopen | It would drop the planning hash check and the content diff for such blocks and change the worker's preflight; worth doing only once pitfall 2 is reproduced (live cases L5–L7). |
| Textual-interface detection for `format:"source"` without an earlier xml read | External-source text is not known to carry `S7_EditorMode`; guessing would be inventing. |
| `move_block`, `copy_block`, library `update_project` or type instantiation over a textual-interface block | `move_block` deletes and imports (not an overwrite), `copy_block` creates a new block; whether library updates replace blocks in a way the report covers is unknown. Their exports are counted. |
| Detecting an open HMI tag editor | No documented Openness member reports open editors. |

## Live acceptance list

X1 (asckye A1): on a disposable V21 project copy, save, then run `read_cross_references` on a block and
`get_call_tree` over the program; record whether TIA Portal stays up, and the TIA Portal log if it exits.

Each case must be reproduced on V21 (and recorded in an acceptance report) before a guard or warning
text may say "TIA Portal does X" instead of "is reported to". Run every case on a disposable project
copy with unsaved work saved.

| Case | Steps | Expected if the report holds | Then |
| --- | --- | --- | --- |
| L1 | LAD network with `"DB_A".arr["DB_B".structArr[1].idx]`; `get_block_content` xml; submit the bundle **without** its `.xml` document | guard and warning fire; the re-export shows the inner access as one quoted name; `s7dclConstructs` fails | reword pitfall 1a texts |
| L2 | FB with `tmr : _.TONR;` (project FB named `TONR`) through the SIMATIC SD route | re-export declares `TONR_TIME`; `s7dclConstructs` fails | reword 1b |
| L3 | FB with `flagsAt : Word` AT `flags` through the SIMATIC SD route | re-exported SimaticML has no `At` attribute; `s7dclConstructs` fails | reword 1c |
| L4 | L1–L3 bundles **with** the `.xml` document | no `plc_s7dcl_import_may_alter`; constructs unchanged | confirms the recommendation |
| L5 | Textual-interface block, project opened through `open_project`, one `plc_read`, then `update_block_logic` | per the report TIA Portal terminates (`portal_lost`); otherwise record the export count at which it does not | reword pitfall 2; decide on the one-export write path |
| L6 | As L5 after `bind_project` of an already open project | guard says the count is unknown | — |
| L7 | As L5 with `format:"source"` after an earlier xml read | guard fires from the ledger flag | — |
| L8 | Unified HMI with a graphic list: `hmi_read list_text_and_graphic_lists` | names listed, TIA Portal keeps running | confirms pitfall 3 is not hit |
| L9 | Unified screen with an `HmiSystemDiagnosisControl`: `list_screen_items` and `validate` with `screenItems` for that screen | TIA Portal keeps running | if `validate` crashes, skip that item type there and report it |
| L10 | Open the tag editor of an HMI ("Show all tags"); time `hmi_read list_tags` with and without it | about eight times slower with the editor open | reword pitfall 5 |
| L11 | `bind_project list_portals` with two TIA Portals, one attached by another Openness client | no Openness access dialog appears; `otherClientCount` is 1 for that Portal | confirms row 21 |
