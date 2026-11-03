# Fail-safe (F-) PLC operations (`plc_read`, `plc_write`, `plc_download`)

**Language:** English · [Deutsch](PLC_SAFETY_SUMMARY.de.md)

Partly live-tested on TIA Portal V21 on 2026-11-02 (test project `MCP_Safety_01`, [live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)):
`get_safety_administration`, `compile_check` of the F-CPU (E6), the acknowledge guard in `full` mode (E9), an SD network
edit of the main safety block (E7) and `list_safety_blocks` (failed: defect D13, fixed since); re-tested with `b79b2df`
the same day (L1-L4 in the live report: the SD import still unlinks the main safety block, `set_safety_runtime_group` is
`api_member_missing` on V21, a hand-made temporary-F-block workaround keeps the link). The automatic temporary-F-block
route that now implements that workaround, the name-attribute route of `set_safety_runtime_group` and `attributeInfos`
are verified offline only. Every Siemens.Engineering.Safety member is
bound at runtime; nothing here is compiled against `Siemens.Engineering.Safety.dll`, which belongs to the separately
installed STEP 7 Safety option and may be absent.

> **Safety acceptance.** A safety program (F-program) and its access protection must be validated and accepted by a
> qualified person (safety acceptance as described in the SIMATIC Safety manual, with the safety printout and the
> collective F-signature). This MCP server never replaces the safety acceptance; nothing it reports (compile results,
> signatures, read-backs) is acceptance evidence. Every fail-safe write carries this notice in its guard, its prompt and
> its result.

The surface stays 11/30/30: the operations belong to `plc_read` and `plc_write` because the safety program is PLC
software. Method names are in `PlcSafetyMethods` (Contracts), parameters in `WorkerRequest.PlcSafety`, dispatch in
`TiaMcpServer.OpennessWorker/Program.PlcSafety.cs`, host code in `TiaMcpServer/Plc/Safety/`.

## Operations

| Tool | Operation | Fields | What it does |
| --- | --- | --- | --- |
| `plc_read` | `get_safety_administration` | optional `plcName` (omitted: every PLC) | Per PLC: whether its CPU device item offers `SafetyAdministration`; `passwordSet` (`IsSafetyOfflineProgramPasswordSet`), `loggedOn` (`IsLoggedOnToSafetyOfflineProgram`); `settings` (`SafetySystemVersion`, `ActivationOfFChangeHistory`, `CreateDriverInstanceDataBlocksWithoutPrefix`, `SafetyModeCanBeDisabled`), `applicableSafetySystemVersions`, `blockNumbers` (`AssignmentOfBlockNumbers`); every F-runtime group with `MainSafetyBlockName`, `MainSafetyBlockIDbName`, `FOBName`, `FOBEventClass`, `InfoDbName`, `MaximumCycleTime`, `WarnCycleTime`, `PreProcessingName`, `PostProcessingName` and `attributeInfos` (every attribute `GetAttributeInfos()` lists, with its `accessMode`, so a live read shows what is writable; null when the group offers none or the call failed, the reason in `problems`); `programSignatures` (collective F-signatures, decimal and hex); the `Siemens.Engineering.Safety.*` services the CPU lists; the safety unit names; `problems` (one line per missing or unreadable member). Also TIA Portal's `GlobalSettings` getters (`SafetyModificationsPossible`, `UsernameForFChangeHistory`, `GenerationOfDefaultFailsafeProgram`, `ManagementOfFailsafeInSoftwareUnitsEnvironment`). Every value has `state` `read`, `api_member_missing` or `unreadable`. |
| `plc_read` | `list_safety_blocks` | optional `plcName`, `offset`, `limit` (1-2000, default 200) | Every block (program blocks with user and system groups, software units, safety units) that lists `SafetySignatureProvider` or whose `ProgrammingLanguage` starts with `F_`: path, number, type, language, know-how protection, signatures and `signatureState` (`read`; `unreadable` with the reason in `problem` when the provider is listed but `GetService` returns null or the read throws; null without a provider). D13 (live 2026-11-02): `GetServiceInfos()` listed `SafetySignatureProvider` and `GetService` returned null, which failed the whole listing; now that block's signatures are reported unavailable and the listing succeeds. |
| `plc_write` | `safety_login` | `passwordVariable`, optional `plcName` | `LoginToSafetyOfflineProgram(SecureString)`; read back `IsLoggedOnToSafetyOfflineProgram == true`. Already logged on: not called, warning. No password set: `validation_error`. |
| `plc_write` | `safety_logoff` | optional `plcName` | `LogoffFromSafetyOfflineProgram()`; read back `false`. |
| `plc_write` | `set_safety_password` | `passwordVariable` (the new password), optional `plcName` | `SetSafetyOfflineProgramPassword(SecureString)` when none is set; read back `IsSafetyOfflineProgramPasswordSet == true`. |
| `plc_write` | `revoke_safety_password` | `passwordVariable` (the current password), optional `plcName` | `RevokeSafetyOfflineProgramPassword(SecureString)` when one is set; read back `false`. |
| `plc_write` | `create_safety_runtime_group` | `runtimeGroupName`, optional `plcName`, `mainSafetyBlockPath`, `mainSafetyInstanceDbPath` (block paths with the PLC name) | `RuntimeGroupComposition.Create(String)` (TIA Portal generates the main safety block), `Create(String, PlcBlock)` (an F-FC) or `Create(String, PlcBlock, PlcBlock)` (an F-FB and its IDB); under the login rule below (a login only when a safety password is set); an existing name is refused; read back with `Find`. |
| `plc_write` | `set_safety_runtime_group` | `runtimeGroupName` (an existing group), `mainSafetyBlockPath`, optional `plcName`, `mainSafetyInstanceDbPath` | Assigns the main safety block (and the IDB of an F-FB) of an existing F-runtime group: the repair when a group lost its main safety block (E7). The assignment is bound on the live `RuntimeGroup`, in this order, and the result names the route (`assignments[].route`, `signature`): (i) a public writable property `MainSafetyBlock` / `MainSafetyBlockIDB` whose type is assignable from `PlcBlock`; (ii) an attribute of that name that `GetAttributeInfos()` lists as writable, set with `SetAttribute(name, block)`; (iii) only when `GetAttributeInfos()` lists `MainSafetyBlockName` / `MainSafetyBlockIDbName` with a writable access mode (`Write`, `ReadWrite`), `SetAttribute(thatName, blockName)` with the block name as a string (route `setAttributeName`, not live-tested). None: `api_member_missing`, nothing changed (both members and both blocks are bound and resolved before the first call); the message lists every attribute `GetAttributeInfos()` reported with its access mode. **Live on V21 (2026-11-02, L2):** the `RuntimeGroup` has no public property `MainSafetyBlock` and `GetAttributeInfos()` lists no attribute `MainSafetyBlock`: `api_member_missing`. Repair a lost reference in TIA Portal Safety Administration. Each assignment is read back with `GetAttribute("MainSafetyBlockName")` / `GetAttribute("MainSafetyBlockIDbName")`, which must equal the block name, else `postcondition_failed`. Login rule and acceptance guard as `create_safety_runtime_group`; an unknown group is `target_not_found` at planning. Not live-tested. |
| `plc_download` | `decisions: { "SafetyProgram": "<member>" }` | | Answers the F-program download configuration `Siemens.Engineering.Safety.Download.Configurations.SafetyProgram` (no default answer). PLCSIM and real controllers alike: acknowledge guard `download_includes_safety_program`; a real controller also fires `download_to_hardware`, like every hardware download. Read-write confirms the call, full runs under policy. **Live 2026-11-02 (V21, CPU 1516F-3 PN/DP V2.9, PLCSIM): TIA Portal refuses the download itself** ("Laden oder Überladen fehlersicherer Daten mit Openness ist nicht erlaubt.", `EngineeringTargetInvocationException` from `DownloadProvider.Download`), so the F-program is downloaded in the TIA Portal UI; the MCP is not stricter and passes TIA's refusal through. |

`passwordVariable` names the password source, like `plc_download` `passwords` ([password sources](../guides/password-sources.md)):
an environment variable NAME (`"NAME"` or `{ "env": "NAME" }`) or a Windows Credential Manager generic credential
(`{ "credential": "TiaMcp/..." }`); when it is omitted, the server's password map may supply it (purpose `safety`). It is
redacted while the request is read: a value that is neither, or names a variable that is not set, never reaches a
response or the audit and is refused as `validation_error`. The worker resolves the source into a read-only
`SecureString` (`PasswordSources.Resolve`) and disposes it after the call; a credential source off Windows fails
`unsupported_capability`. Results carry the source token only.

## F-block changes

An F-block is a block whose `ProgrammingLanguage` starts with `F_` (F_LAD, F_FBD, F_DB, ...), or a SimaticML document
that declares such a language.

**Rule (owner decision 2026-11-01: the server is not stricter than TIA Portal).** Every operation TIA Portal offers for
an F-block runs here too. A login is required only when the PLC's safety offline program has a password and the
session is not logged on, which is TIA Portal's own rule; without a password TIA Portal allows F-program changes
without login, and so does the server. What the server cannot judge (the PLC offers no `SafetyAdministration`, or the
access state is unreadable) is passed to TIA Portal with the info guard `plc_safety_tia_decides`; TIA Portal's own
rejection is the item's failure (`engineering_rejected` where the worker method reports it, see AGENTS.md).

| Operation on an F-block | Rule |
| --- | --- |
| `update_block_logic` (xml or source) | login rule; the host detects an F-block from the SimaticML (xml) or from `get_block_interface` (source), the worker reads the block's language itself before the import |
| `add/update/delete_interface_member` | login rule; planned and imported through `update_block_logic`, whose worker gate checks the login again |
| `update_db_member` (F-DB) | login rule (the host reads the language from `get_db_values`) |
| `delete_block`, `rename_block`, `renumber_block` | login rule (the host reads the language from the project tree) |
| `copy_block`, `move_block` | login rule; the SimaticML export/import round trip runs as for any block (the generic XML route TIA Portal offers for consistent F-blocks, see [import/export options](IMPORT_EXPORT_OPTIONS_SUMMARY.md)); TIA Portal's rejection rolls the transaction back |
| `delete_block_group` holding F-blocks | login rule and acceptance guard when the tree shows an F-block inside the group |

"Logged on" is `PlcSafetyPolicy.Decide`: a password is set and `IsLoggedOnToSafetyOfflineProgram` is true. The only
refusal is `LoginRequired` (password set, not logged on, and no earlier `safety_login` of the call): block guard
`plc_safety_login_required` before anything runs, and the worker refuses (`target_kind_unsupported`) right before the
change. The host decides with the planned state of the call (an earlier `safety_login`, `safety_logoff`,
`set_safety_password` or `revoke_safety_password` of the same call is applied in order), the worker decides with the
live state right before the change; the planned state never authorizes anything by itself.

## Main safety block references across imports

Live on TIA Portal V21, 2026-11-02 (test project `MCP_Safety_01`, F-runtime group "F-Ablaufgruppe 1", main safety block
`Main_Safety_RTG1`, an F_FBD FB number 1 with `AutoNumber` false, IDB `Main_Safety_RTG1_DB`):

- **L1 (E7, reproduced twice, also with `b79b2df`).** Every SIMATIC SD import (`ImportFromDocuments` Override) of the
  main safety block makes TIA Portal drop the group's `MainSafetyBlockName` (null; `MainSafetyBlockIDbName` stays); the
  PLC compile then fails "The F-runtime group "F-Ablaufgruppe 1" does not have a valid main safety block." and the
  collective F-signature becomes 0.
- **L2.** The group cannot be re-linked through Openness: no public property `MainSafetyBlock`, no attribute
  `MainSafetyBlock` in `GetAttributeInfos()` (`api_member_missing`, reported honestly by the item verification
  `safetyRuntimeGroup`).
- **L3 (E7b).** A SimaticML import (`update_block_logic` xml, Override; `set_network_text` and a full document) of the same
  block keeps the reference.
- **L4.** Done by hand with existing operations, ending with 0 compile errors and the reference intact: the SD document as
  a temporary F-FB (`create_block_from_document`, header `S7_Safety := "TRUE"`, auto number 2), `compile_check` of that
  block (required: before it, `get_block_content xml` returned only the `.s7dcl`, BetterTIA pitfall B4), its SimaticML
  with `Name`, `Number` and `AutoNumber` set to the main block's (`<AutoNumber>false</AutoNumber>` inserted as the first
  `AttributeList` child), `update_block_logic xml` onto the main block with its current hash, `delete_block` of the
  temporary block.

What the server does (verified offline only):

- **The route (L4, automatic).** An SD-document write onto the main safety block (`MainSafetyBlockName`, not the IDB) of
  an F-runtime group, i.e. `update_block_logic` format sd or a bundle without `.xml`, `insert_network`, `replace_network`,
  `delete_network` (mutated as `update_block_logic` sd) and `create_block_from_document` with `overwrite` onto such a
  block, is never imported onto that block. In one worker call (`MainSafetyBlockSdUpdate`,
  `PlcMainSafetyBlockSdRouteWorker`, Siemens-free `PlcMainSafetyBlockSdRoute.Run`, shared with the FakeWorker): the usual
  preflight (documents, declared name, login rule, hash in the request's format); a temporary name
  `zzMcpTmp_<block>_<8 hex>` (the hex digits from the SHA-256 of the name and the documents, so it is deterministic; a
  block of that name anywhere in the PLC refuses the item, nothing changed); the `.s7dcl` with that name, without a header
  `S7_BlockNumber` (the temporary block must not claim the main block's number) and with `S7_Safety := "TRUE"` added when
  the header has no `S7_Safety`; `ImportFromDocuments` Override into the main block's group; `compile_check` of the
  temporary block through the existing compile path for one block; its SimaticML; `Name`, `Number` and `AutoNumber`
  set to the main block's (read from the main block's own SimaticML export, else from its live attributes) by the
  Siemens-free `SimaticMlBlockIdentity` (those three elements only; every other character stays); the existing
  `update_block_logic` xml import onto the main block with its current xml hash; the temporary block deleted (only
  afterwards: deleting it before the main import made TIA Portal V21 terminate live on 2026-11-02, K36). A compile
  error or a missing SimaticML stops before the main block is touched: the temporary block is deleted and the item fails
  with the compiler messages (`worker_operation_failed`). Every failure path deletes the temporary block; one that cannot
  be deleted is named in a warning and in the evidence (`temporaryBlockState: "left"`), never left silently. The item
  result carries `mainSafetyBlockRoute` (`route: "sdViaXmlForMainSafetyBlock"`, `temporaryBlockName`, `temporaryBlockPath`,
  `temporaryBlockState`, `temporaryCompile`, `mainImport: "simaticMl"`, `mainNumber`, `mainAutoNumber`, `identitySource`,
  `steps`, `notes`); a failed item carries the same object as its `result` (through the `PlcDownloadOutcome` evidence
  channel). For `create_block_from_document` the main block keeps its group (a different `targetGroupPath` is reported).
  The steps run one after another without a transaction, as live: a transaction would roll the main import back together
  with the temporary block, and compile and export inside an open transaction are not verified.
- **Other F-blocks** (not a main safety block, including the IDB of one) keep the direct SD import: there is no evidence
  that they lose anything. When the worker cannot read the runtime groups before the write it says so in a warning and
  imports directly.
- **Planning.** For an SD-route import of a block that the planning read shows as the main safety block (or IDB) of a
  runtime group, the info guard `plc_import_may_unlink_main_safety_block` names the groups; for a main safety block its
  text describes the route and its six steps (a direct SD import drops the reference, live 2026-11-02). The item runs.
- **Safety net on every route** (`update_block_logic` xml, sd and source, and therefore every planned edit;
  `create_block_from_document` with `overwrite`): before importing an F-block the worker records the runtime groups whose
  `MainSafetyBlockName` or `MainSafetyBlockIDbName` names it; after the import (also after a failed one) it re-reads them
  and tries to re-assign a lost reference with the `set_safety_runtime_group` mechanism (on V21 not possible, L2). Every
  lost reference becomes a warning with the repair (TIA Portal Safety Administration). The host then reads
  `get_safety_administration` again; a recorded reference that no longer names the block makes the item's verification
  entry `success: false` with check `safetyRuntimeGroup`.

Not covered: `move_block` and `copy_block` (a move imports into another group and deletes the original; whether TIA
Portal keeps the group reference then is untested, and the round trip runs in its own transaction), and imports
through `vci_write`. Check `get_safety_administration` after those. The automatic route has not run live as one call.

## Guards, confirmation and audit

| Guard | Severity | Fired by |
| --- | --- | --- |
| `plc_safety_acceptance_required` | acknowledge | every fail-safe write and every F-block change; the message carries the acceptance notice |
| `plc_safety_login_required` | block | an F-block change or runtime group create on a PLC whose safety program has a password while the session is not logged on (planned state) |
| `plc_safety_tia_decides` | info | a fail-safe write or F-block change whose access state could not be read, or whose PLC offers no `SafetyAdministration`: TIA Portal decides |
| `plc_import_may_unlink_main_safety_block` | info | an SD-route import of a block that the planning read shows as the main safety block (or IDB) of an F-runtime group (L1/E7; the SimaticML route kept the reference live, L3/E7b); for a main safety block the text describes the temporary-F-block route the worker takes instead of the SD import |

`plc_write` confirms a call (`ConfirmsEveryCall`) exactly when it contains a fail-safe write or planning found an F-block
change, like `library_write` with its lifecycle items: read-write asks the human once (form elicitation; a client
without it is refused with `access_denied`), full satisfies the guard by policy, a dry run never asks. The prompt lists
each fail-safe item with its PLC and changes, every caller value quoted. Unsafe operationIds are refused for such calls.
Ordinary `plc_write` calls still ask nothing. A dependent item that is re-planned keeps the acceptance its planning pass
fired; an acceptance guard that only the re-plan fires is late and is blocked in read-write. Audit records carry
`confirmation.by` `user` or `policy`; passwords never appear.

## Evidence for every Siemens member

All members are reflection-bound (`PlcSafetyApiCatalog`, `PlcSafetyReflection`, worker `PlcSafetyService`): services
are found among `IEngineeringServiceProvider.GetServiceInfos()` by exact type name, or through the documented generic
`GetService<T>()` closed over the type loaded by name; properties by exact name; methods by exact name and argument
types (`LibraryRuntimeBinding`). A missing member is `api_member_missing` (a read value state, a write failure
category). No stub was added.

| Member | Evidence | Source |
| --- | --- | --- |
| `DeviceItem.GetService<SafetyAdministration>()` (CPU item with `SoftwareContainer`) | Siemens primary | `tia-portal-ai-extensions` `plc-safety-administration/SKILL.md` §2 |
| `SafetyAdministration.Settings`, `.RuntimeGroups` | Siemens primary | `plc-safety-administration/SKILL.md` §1, §3, §5; `libraries-and-alarms/SKILL.md` |
| `SafetySettings.SafetySystemVersion`, `GetApplicableSafetySystemVersions()`, `AssignmentOfBlockNumbers` (`ManagementMode`, `FromDB`..`ToFC`) | Siemens primary | `plc-safety-administration/SKILL.md` §3, §4 |
| `RuntimeGroupComposition` enumeration, `Find(String)`, `Create(String, PlcBlock, PlcBlock)`; `RuntimeGroup.Name` | Siemens primary | `plc-safety-administration/SKILL.md` §5; `libraries-and-alarms/SKILL.md` |
| `RuntimeGroup` attributes `MainSafetyBlockName`, `MainSafetyBlockIDbName` (`GetAttribute`) | Siemens primary | `libraries-and-alarms/SKILL.md`; `engineering-objects/SKILL.md` |
| `RuntimeGroup.MainSafetyBlock`, `RuntimeGroup.MainSafetyBlockIDB` (`PlcBlock`) in the object model; "Call `SetAttribute` directly on the `RuntimeGroup` object" | Siemens primary (member names and the SetAttribute rule; the attribute path and the writability are **not** documented) | `plc-safety-administration/SKILL.md` §1 (object model), §5 ("Setting attributes on a RuntimeGroup"). Used by `set_safety_runtime_group` and the automatic re-link, bound at runtime as a writable property, a `GetAttributeInfos()`-writable attribute, or a `GetAttributeInfos()`-writable `MainSafetyBlockName`/`MainSafetyBlockIDbName` set to the block name (`PlcSafetyRuntimeGroupBinding`), never guessed. Live V21 2026-11-02 (L2): neither the property nor the attribute `MainSafetyBlock` exists |
| `IEngineeringObject.GetAttributeInfos()` (`Name`, `AccessMode`), `SetAttribute(String, Object)` | Siemens primary; compile stubs | `engineering-objects/SKILL.md`; already used by the renames |
| `PlcBlock.GetService<SafetySignatureProvider>()` | Siemens primary (no member names) | `change-detection/SKILL.md` |
| `PlcUnitGroup.SafetyUnits` | Siemens primary | `libraries-and-alarms/SKILL.md` |
| `SafetyAdministration.ProgramSignatures`, `IsSafetyOfflineProgramPasswordSet`, `IsLoggedOnToSafetyOfflineProgram`, `LoginToSafetyOfflineProgram(SecureString)`, `LogoffFromSafetyOfflineProgram()`, `SetSafetyOfflineProgramPassword(SecureString)`, `RevokeSafetyOfflineProgramPassword(SecureString)` | V21 installed-API baseline, third-party use | Czarnak/totally-integrated-claude `api-baselines/v21/safety.json` and `skills/tia-plc-operations/references/safety-administration.md` (extracted from an installed V21 `Siemens.Engineering.Safety.dll` and its XML documentation); asckye/TIA_Portal_Openness_MCP `SafetyManagementAdapter.cs` (MIT, compiled against V21) |
| `SafetySettings.ActivationOfFChangeHistory`, `CreateDriverInstanceDataBlocksWithoutPrefix`, `SafetyModeCanBeDisabled`; `RuntimeGroupComposition.Create(String)`, `Create(String, PlcBlock)`; `RuntimeGroup.FOBName`, `FOBEventClass`, `InfoDbName`, `MaximumCycleTime`, `WarnCycleTime`, `PreProcessingName`, `PostProcessingName` | V21 installed-API baseline | as above |
| `SafetySignatureProvider.Signatures`, `SafetySignature.Type`, `.Value` (integral) | V21 baseline, third-party use | as above |
| `TiaPortal.GetService<GlobalSettings>()` and its parameterless getters | V21 baseline, third-party use | as above (setters are one-argument overloads and are never called) |
| `Siemens.Engineering.Safety.Download.Configurations.SafetyProgram.CurrentSelection` (`SafetyProgramSelections`) | V21 baseline; member `ConsistentDownload` from third-party use | baseline type list; asckye `DownloadPromptPolicy.cs`. The member is read live (`availableSelections`) |

Members marked baseline or third-party are not verified against a Siemens primary publication: the Siemens V21
Openness manual pages for them could not be retrieved (support.industry.siemens.com refused the download, and
docs.tia.siemens.cloud did not expose the safety chapter to the fetcher). They are reported as `api_member_missing` if
an installation lacks them.

## Left out, and why

| Left out | Reason |
| --- | --- |
| A dedicated safety compile | No compile member exists in the Safety assembly's type list; the third-party implementation states "F compile is not in the PublicAPI". `compile_check` runs the ordinary `ICompilable.Compile` of the PLC software, and on an F-CPU that compiles the safety program too: live on V21 on 2026-11-02 (E6) `compile_check plcName` reported "Compile safety program 'Safety Administration'". |
| `SafetyPrintout.Print` | Listed in the baseline, but writing a printout needs a printer driver choice and file handling that the evidence does not pin (`SafetyPrintoutFilePrinter`, `SafetyPrintoutOption` members); its presence is reported in `safetyServices`. |
| `SafetySettings` / `AssignmentOfBlockNumbers` / `SafetySystemVersion` writes, `RuntimeGroup` attribute writes other than the main safety block and its IDB (`set_safety_runtime_group`) and `Delete`, `GenerateGlobalFIOStatusBlock`, `CleanSystemGeneratedObjects`, `SafetyBaseIdProvider.GenerateBaseId`, `GlobalSettings` setters | Changes of the safety configuration with consequences the sources do not describe (range checks, regenerated F-system blocks, TIA Portal-wide effects); read-only here. |
| `SafetyValidationAssistant` (activation tests, safety functions) | A separate assembly (`Siemens.Engineering.SafetyValidation`) whose workflow is part of the acceptance itself. |
| Creating F-blocks with `create_block` | No verified SimaticML template for F-languages; `create_block` still refuses `F_*` languages. Use `create_safety_runtime_group` without a main block (TIA Portal generates it) or import through TIA Portal. |
| `create_instance_db` for an F-FB | Not checked; TIA Portal's own protection applies. |

## Live acceptance

Done on 2026-11-02: see the [live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md) (E6-E9, and the re-test with `b79b2df`: L1-L4,
D13/D14/E5 confirmed fixed). Open: the automatic temporary-F-block route as one call, the `attributeInfos` the live
`RuntimeGroup` reports (and whether route (iii) of `set_safety_runtime_group` exists), and the rest of the
[PLCSIM Advanced F-CPU live test plan](../development/plcsim-fcpu-live-test-plan.md).
