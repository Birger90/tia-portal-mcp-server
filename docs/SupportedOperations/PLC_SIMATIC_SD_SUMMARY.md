# SIMATIC SD: LAD, FBD and SCL networks as text (`plc_read`, `plc_write`)

**Language:** English · [Deutsch](PLC_SIMATIC_SD_SUMMARY.de.md)

Partly live-tested on TIA Portal V21 on 2026-11-02 ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md), E1-E7): `get_block_content` sd,
`update_block_logic` sd with a 15-network LAD/FBD/SCL FB, `insert_network`/`replace_network`/`delete_network` chained in
one call and `create_block_from_document` (with `de-DE` texts; failed with an `en-US` culture the project lacks) ran
live; a re-test with `b79b2df` the same day confirmed the optional `.s7res` (E5) and showed the main safety block
findings L1-L4. Everything else, and every change made after those runs (the culture note, the main safety block route,
the D15 warning), is verified offline only (unit tests over real V21 exports and the `plc-write-roundtrip` FakeWorker
scenario).

SIMATIC SD is TIA Portal V21's block document format: `PlcBlock.ExportAsDocuments` writes a block as an `.s7dcl`
document (header pragma, declaration, interface, networks) and an `.s7res` document (the multilingual texts), and
`PlcBlockComposition.ImportFromDocuments(DirectoryInfo, string, ImportDocumentOptions.Override)` imports them. The
Siemens V21 skill `simatic-sd` (MIT, Siemens AG, `openness_development/skills/simatic-sd/SKILL.md`) calls this "the
correct path for adding LAD/FBD/SCL networks to existing blocks via Openness". These operations make it a network-level
editing path, so an agent can change LAD (KOP), FBD (FUP) and F-LAD/F-FBD logic without a SimaticML (FlgNet XML) round
trip. No tool was added (13/32/32 stay); every operation is a `plc_read` or `plc_write` operation.

For the LAD/FBD syntax itself, with evidence per construct, see [Writing LAD and FBD as SIMATIC SD](../guides/lad-fbd-simatic-sd.md).

## Operations

| Operation | Tool | Fields | What it does |
| --- | --- | --- | --- |
| `get_block_content` `format: "sd"` | `plc_read` | `blockPath` | Only the SIMATIC SD documents of `ExportAsDocuments` (`.s7dcl` first, `.s7res` when TIA Portal writes one), delimited like the xml bundle (`--- FILE: Name.s7dcl ---`). `contentHash` is `sd:sha256:<hex>` over that text, computed like the other formats and equal to what the next read returns. `failSafe: true` appears when the `.s7dcl` header declares `S7_Safety := "TRUE"`. Whatever TIA Portal refuses to export fails with its error or result state; nothing is stricter. |
| `list_networks` | `plc_read` | `blockPath` | Host computation over `get_block_content sd`: `blockKind`, `blockName`, `preferredLanguage`, `failSafe`, every header pragma entry (`S7_PreferredLanguage`, `S7_Safety`, `S7_BlockNumber`, ... none dropped), and per network `number` (1-based, document order), `language` and `languageSource` (`pragma` from `S7_Language`; `detected` by §5 of the Siemens skill; null when ambiguous), every pragma entry, `titleId`/`title` and `commentId`/`comment` (`S7_NetworkTitle`/`S7_NetworkComment` ids resolved from the `.s7res`, one text per culture), `code` (the body without its common indentation) and `exportOnlyForms` (see [Export and import forms](#export-and-import-forms)). `contentHash` is the sd hash. |
| `update_block_logic` `format: "sd"` | `plc_write` | `blockPath`, `content`, `expectedContentHash` (sd) | Imports an sd bundle with `ImportFromDocuments(..., Override)`: exactly one `.s7dcl` declaring the target block, optionally its `.s7res`, no `.xml`. A stale hash is `state_changed`; another declared name is a `validation_error` (update_block_logic never renames or creates). The older implicit route (format xml with a bundle without `.xml`) is unchanged. |
| `insert_network` | `plc_write` | `blockPath`, `code`, optional `language`, `position`, `expectedContentHash` (sd) | Inserts one network before network `position` (1-based; one past the last or omitted appends) with the pragma `{ S7_Language := "<language>" }`. |
| `replace_network` | `plc_write` | `blockPath`, `networkNumber`, `code`, optional `language`, `expectedContentHash` (sd) | Replaces one network's body. Its pragma stays with every key (`S7_NetworkTitle`, `S7_NetworkComment`, unknown keys); only the `S7_Language` value changes when `language` differs. An edit that changes nothing is refused. |
| `delete_network` | `plc_write` | `blockPath`, `networkNumber`, `expectedContentHash` (sd) | Removes one network with its pragma. Comment lines above the pragma stay. |
| `create_block_from_document` | `plc_write` | `targetGroupPath`, `content`, optional `overwrite` | Imports an sd bundle (`.s7dcl` declaring the new block and, optionally, its `.s7res` of the same name) into a deterministic block group (`PLC_1/Blocks`, `PLC_1/Blocks/Line`, as `copy_block` takes it). The `.s7res` is optional since 2026-11-02: TIA Portal's own export of a block without texts has none (E1), and the earlier refusal was stricter than TIA Portal (E5). A present `.s7res` is checked structurally (at most one, same name) and the item's messages list its cultures: every culture must be a project language (E4, live: "Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text."). A block of the declared name anywhere in the PLC refuses the item unless `overwrite: true` (then `Override`, info guard `plc_sd_document_replaces_block`). |

### `code`

The accepted forms are those of §6 of the Siemens skill: a RUNG body (several RUNGs, parallel branches with
`wire#wN` and `END_RUNG wire#wN` included), a full `NETWORK ... END_NETWORK` with or without the pragma before it
(single-line `{ S7_Language := "LAD" }` or the multi-line block), or SCL statements. Only the outer wrappers and the
`S7_Language` pragma are stripped; inner pragmas such as `{ S7_Templates := "time_type := Time" }` and
`{ f_user_card := "1"; f_image_card := "0" }` stay; blank lines are dropped (the skill reports that an extra blank line
inside a RUNG makes the import fail; TIA Portal V21 itself exported an empty line inside a RUNG before `Move(` live on
2026-11-02, and that export re-imported, so the rule is a report). The caller's relative indentation is kept and the body is indented like the
document's networks. Do not wrap the code in quotes (§9).

`language` is written as given (`LAD`, `FBD`, `SCL`, `STL`, ...); TIA Portal decides. Omitted, `insert_network` takes
the code's own `S7_Language` pragma, else the §5 detection (`RUNG wire#` is LAD, `RUNG "..."` or `RUNG <number>` is
FBD, no RUNG is SCL); when neither applies it is a `validation_error` asking for `language`. `replace_network` keeps
the network's language when it is omitted.

Refused as `validation_error`, because each would corrupt the edited document: more than one NETWORK in one code (§10),
unbalanced or nested RUNG/END_RUNG, unbalanced NETWORK/END_NETWORK, NETWORK or END_NETWORK not on a line of their own,
anything other than a pragma before NETWORK or anything after END_NETWORK, an `S7_Language` inside the body, a
language name that is not an identifier (it is written into a quoted pragma value), and the pragma keys
`S7_NetworkTitle`/`S7_NetworkComment`: titles and comments are `.s7res` ids, the network edits never touch the
`.s7res` (§10), and inline literal titles are reported (1748477582/tia-portal-openness-mcp, not verified) to fail on
import. Set texts afterwards with `set_network_text`. A network number or position outside the block is
`target_not_found`. LAD/FBD/SCL semantics are never judged: TIA Portal's import and compile decide.

### Planning, chaining and verification

The network edits follow `set_network_text`: planned host-side on the `get_block_content sd` export
(`PlcSdNetworkEditPlanner`), the mutation imports exactly the planned bundle through `update_block_logic` format sd
against the export's hash. Several items on one block in one call preview against the earlier planned document and
carry `dependsOn`; they are re-planned against the real state right before their own import, so a later item numbers
the networks as the earlier items left them. `expectedContentHash` is optional (sd); it cannot describe a document an
earlier item of the call changes and is then refused. A network edit of a block that `create_block_from_document`
creates earlier in the call is planned at its turn.

Verification re-exports the block in sd and reports its `contentHash` (usable as the next `expectedContentHash`).
Check `networkCode` succeeds when the import succeeded and the re-export has the expected network count and, for
insert/replace, a network at the expected position in the expected language. The submitted code is compared with
the re-exported network after normalization (lines trimmed, blank lines dropped) and reported in the message as
`codeMatchesExport: true|false`, with both texts in `expected`/`actual`: V21 is reported to export some instructions in
a form that differs from the import form (below; not reproduced live for the instructions tested), and TIA Portal
reformats whitespace (an empty line before `Move(`, the `Add` template as a multi-line pragma, E2), so a different text
is information, not a failed import. `delete_network`
reports `remainingNetworksMatchExport`. When the V21 `.s7dcl` construct pitfalls were found in the planned document,
their comparison with the re-export makes the entry fail as `s7dclConstructs` does for update_block_logic. Every
successful entry recommends `compile_check`: an import can leave instance DBs and callers inconsistent (BetterTIA B2,
info guard `plc_text_edit_reimports_block`). `create_block_from_document` is verified by re-exporting the reported
block in sd (check `createdBlock`: the re-export declares the name; `contentHash` sd).

### Fail-safe blocks

An `.s7dcl` header `S7_Safety := "TRUE"` (the V21 export of a fail-safe FB in the BetterTIA sample `FB_Safety`)
marks a fail-safe block for every SD write (`update_block_logic` sd, the three network edits,
`create_block_from_document`), in addition to `ProgrammingLanguage F_*` and the SimaticML language. The rules are the
xml route's: the acknowledge guard `plc_safety_acceptance_required` fires (read-write asks once, full runs under
policy), the block guard `plc_safety_login_required` stops the call only when the safety program has a password and
the session is not logged on (`safety_login` earlier in the call counts), and the worker gate
(`PlcSafetyGate.RequireImportPermitted`) checks again before the import. `create_block_from_document` with
`overwrite` also counts an existing F-block of the declared name. `get_block_content sd` and `list_networks` report
`failSafe: true`.

### Main safety blocks of F-runtime groups

Live on V21 (2026-11-02, E7 and the re-test with `b79b2df`, L1-L4): every SD import (`ImportFromDocuments` Override)
of `Main_Safety_RTG1`, the main safety block of F-runtime group "F-Ablaufgruppe 1", succeeded but left the group's
`MainSafetyBlockName` null (`MainSafetyBlockIDbName` stayed), the PLC compile failed ("The F-runtime group
"F-Ablaufgruppe 1" does not have a valid main safety block.") and the collective F-signature became 0 (L1); Openness
offers no member to re-assign it (L2); the SimaticML route (`Import(..., Override)`) kept both names (L3); a temporary
F-block, compiled, exported as SimaticML, renamed and renumbered to the main block and imported onto it as SimaticML,
kept the reference with 0 compile errors (L4).

Since then an SD-document write onto a main safety block (`update_block_logic` sd or a bundle without `.xml`, the
network edits, `create_block_from_document` with `overwrite`) is **never imported onto that block**: the worker runs L4
in the same item (temporary block `zzMcpTmp_<block>_<8 hex>` in the block's group, compiled with the compile path for
one block, SimaticML transplant of Name/Number/AutoNumber, `update_block_logic` xml onto the main block with its current
hash, temporary block deleted). A temporary compile error or a missing SimaticML stops before the main block is touched
and fails the item with the compiler messages; the temporary block is deleted on every path or named when it cannot be.
The item result's `mainSafetyBlockRoute` (`route: "sdViaXmlForMainSafetyBlock"`, `temporaryBlockName`, `mainImport:
"simaticMl"`, `steps`, `notes`) says what was done; a failed item carries it as its `result`. The info guard
`plc_import_may_unlink_main_safety_block` describes the route at planning. The verification `networkCode` /
`contentHash` / `createdBlock` re-export the main block in sd as before. Every import route still re-reads the groups
afterwards and fails the item's verification (`safetyRuntimeGroup`) when a reference is lost. Other F-blocks keep the
direct SD import. See the [safety reference](PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports). The
route is verified offline only.

## Export and import forms

**Not reproduced live on V21 on 2026-11-02** for `GT_Contact`/`EQ_Contact` (with `SrcType` template), `Move`,
`P_Trig`/`N_Trig`, `TON`/`TOF` multi-instances and `Add`: the re-export of a 15-network FB equalled the import form
except whitespace (E2). The report below therefore stands only for instructions not tested; the guard stays
informative for them.

The third-party skill of 1748477582/tia-portal-openness-mcp (skill §9a, verified there on V21 2026-06-11; reported,
not verified here) states that the export grammar differs from the import grammar for some instructions: an export
writes `Gt{ SrcType := Int }( IN1, IN2 )` (likewise `Lt`, `Eq`, `Ne`, `Ge`, `Le`), `PBox(...)` (and `NBox`) and
`Move{ Card:=1; DisableENO:=TRUE }( IN, OUT1 )`, which fail `ImportFromDocuments` with a generic "Failed importing";
the import forms are `GT_Contact( in1 := ..., in2 := ... )` with `{ S7_Templates := "SrcType := Int" }`,
`P_Trig( #mem )` and `Move( in := ..., out1 => ... )`. Every network edit re-imports the networks it does not touch
in their export form, as `set_network_text` does with SimaticML. This server does not rewrite export forms (no
mapping is evidenced). Instead the info guard `plc_sd_reimports_export_form` names every untouched network that
holds one of these forms (for `update_block_logic` sd: the submitted networks that are unchanged from the current
export), and `list_networks` lists them per network in `exportOnlyForms`. If TIA Portal refuses such an import,
rewrite those networks in the import form with `replace_network`, or change texts through the SimaticML route
(`set_network_text`).

Other reported rules (same source): the `.s7res` must be present even without texts (minimal:
`MultiLingualTexts:` with one `- id: MLC_x` entry and `zh-CN`/`en-US` lines) — contradicted live on 2026-11-02: TIA
Portal's own export of a block without texts has none (E1), and an `en-US` culture the project lacks failed the import
(E4); `create_block_from_document` accepts a bundle without `.s7res` since then (E5) and never generates ids; both
files UTF-8 with BOM (every staged document is written
with exactly one BOM, a document without one is reported in the result's messages); a pure SCL block cannot hold LAD
networks, while a block with `S7_PreferredLanguage := "LAD"` can hold SCL networks (not refused: TIA Portal decides);
the call forms `"FCName"()`, `"InstanceDB"( p := ..., o => ... )` and the inline template form
`#tonInst.TON{ time_type := Time }( PT := T#500ms, ET => )` beside the Siemens pragma form are kept intact by the
parser. BetterTIA (B1 in [V21 pitfalls](V21_PITFALLS.md#pitfalls-reported-by-bettertia-not-verified)) reports that a LAD
import can need an `en-US` text for every `.s7res` item: reported by BetterTIA/1748477582; contradicted live 2026-11-02
in a de-DE project, where an `en-US` text made the import fail and `de-DE` texts alone succeeded (the project's
languages are presumably de-DE only, not verified).

## Text handling

The `.s7dcl` model is `SimaticSdDocument` (Contracts, Siemens-free). A leading UTF-8 BOM is kept as U+FEFF in the
text and in the hash (the worker reads the exported files as bytes, `SimaticSdFileText`), CRLF or LF line endings
are kept, and untouched lines are byte-identical. A network's pragma is the nearest non-blank line before its
`NETWORK` line (the single-line form) or the multi-line block ending there; inserting or deleting keeps each pragma
immediately before its `NETWORK` (§4, §7; the skill's "duplicate pragma" import failure). Pragma entries are parsed
`key := value` separated by `;` outside quotes and kept in order with their raw values, so unknown keys survive every
rewrite; a changed value is replaced in place. The `.s7res` reader (`SimaticSdResources`) reads plain, quoted and
block YAML scalars. The import's result messages (`DocumentImportResultForBlocks.Messages`, documented by the skill
only as `result.Messages.Select(m => m.Message)`) are read by reflection and reported as warnings prefixed
`SIMATIC SD import:` (one line, at most 256 characters) on an update, and in the failure message of
`create_block_from_document`.

## Provenance of the Siemens members

| Member | Evidence | Use |
| --- | --- | --- |
| `PlcBlock.ExportAsDocuments(DirectoryInfo, string)` → `DocumentExportResult` | Siemens skill simatic-sd §2, §8; compile stubs (`reference-stubs/Siemens.Engineering.Step7/Blocks.cs`) | `get_block_content sd`, every planning and verification export |
| `DocumentExportResult.State`, `.ExportedDocuments` | compile stubs (`Software.cs`); already used by the xml bundle export | export result and file list |
| `PlcBlockComposition.ImportFromDocuments(DirectoryInfo, string, ImportDocumentOptions)` → `DocumentImportResultForBlocks` | Siemens skill §3, §8; compile stubs | `update_block_logic` sd and the implicit route; `create_block_from_document` |
| `ImportDocumentOptions.Override` | Siemens skill §3; compile stubs (the only member) | every SD import (`create_block_from_document` without `overwrite` checks for an existing block first) |
| `DocumentImportResult.State`, `DocumentResultState.Success` | Siemens skill §3; compile stubs | success test (anything else fails the import) |
| `DocumentImportResultForBlocks.Messages`, its elements' `Message` | Siemens skill §3 (`result.Messages.Select(m => m.Message)`); not in the compile stubs (types undocumented) | bound at runtime by exact name (`SimaticSdImportMessages`); an unreadable member yields a note, never a failure of its own |
| `PlcBlockComposition.Find(string)`, `PlcBlockGroup.Blocks`, `PlcBlock.Name` | compile stubs; already used | `create_block_from_document` read-back |

No stub member was added and nothing else was bound.

## Left out

- Rewriting export forms into import forms: no reliable mapping is evidenced (guard and docs instead).
- Writing network titles or comments through the SD route: they are `.s7res` ids (§10); `set_network_text` (SimaticML)
  does it.
- Generating `.s7res` ids or a minimal `.s7res` for `create_block_from_document`: the caller supplies the document
  (none is needed for a block without texts).
- Refusing `.s7res` cultures that are not project languages: TIA Portal refuses them itself (E4); the item lists the
  cultures instead, and reading the project languages for every create would add a read for a check TIA Portal makes.
- Forwarding TIA Portal's SD import messages in the closed failure summary of `update_block_logic`: the import outcome
  contract stays closed; the messages travel as controlled warnings instead.
- SD for PLC data types (`get_type_content`/`update_type_content`): the Siemens skill documents `ExportAsDocuments` and
  `ImportFromDocuments` for blocks only, so `sd` is a block format.
- Live acceptance still open: `create_block_from_document` with `overwrite`, an F-block import with a safety password
  set, the automatic main safety block route as one call, and the export forms of instructions not tested on
  2026-11-02 (`create_block_from_document` without `.s7res` passed live in the re-test; `set_safety_runtime_group` is
  `api_member_missing` on V21, L2). Done on 2026-11-02 ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)): the network edits on LAD/FBD/SCL
  networks and on an F-block, the export/import form for the instructions listed above, and the `.s7res` culture rule.
