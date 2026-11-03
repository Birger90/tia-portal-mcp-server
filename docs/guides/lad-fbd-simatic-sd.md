# Writing LAD and FBD as SIMATIC SD text (TIA Portal V21)

**Language:** English · [Deutsch](lad-fbd-simatic-sd.de.md)

This guide is for an agent (or person) who writes or changes LAD (KOP) and FBD (FUP) networks through this
server. It collects the SIMATIC SD (`.s7dcl` / `.s7res`) syntax with the evidence for every construct, so that
nothing is written from guesswork. The rows marked **L** were live-verified by this fork on TIA Portal V21 on
2026-11-02 (test project `MCP_Livetest_01`, [live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)): a 15-network LAD/FBD/SCL FB was
imported with `update_block_logic` format `sd` (Override) with 0 compile errors, and its re-export equals the
submitted code for every construct marked **L**; only whitespace differed. Every other row is still unverified.

## Why SIMATIC SD and not SimaticML

Siemens' V21 Openness skill `simatic-sd` (tia-portal-ai-extensions, MIT, Siemens AG, commit `f34f3e2`) calls the
SD round trip (export → text edit → import) "the correct path for adding LAD/FBD/SCL networks to existing blocks
via Openness". A LAD network in SimaticML is a `FlgNet` graph of `Part`, `Access` and `Wire` elements with UIds;
the same network in SD is a few lines of text. SimaticML stays available (`get_block_content` `xml`,
`update_block_logic`) for what SD cannot express (see V21 pitfall 1: AT overlays, some constructs the SD import
is reported to alter).

## Evidence legend

| Mark | Source |
| --- | --- |
| **S** | Siemens skill `simatic-sd` (V21), primary source |
| **E** | Third-party sample in import form that its author reports as imported into V21 and compiled with 0 errors, with the matching SimaticML next to it: `1748477582/tia-portal-openness-mcp` `tools/tiaportal-mcp/skill/lad-cookbook` (MIT, commit `8e7ca42`, `MCPVerify_FC_LAD`, `MCPVerify_FB_LAD_v3`) |
| **D** | Third-party sample of unproven origin: BetterTIA/editor `tests/samples/Demo_Line` (MIT, commit `3d115b3`), asckye/TIA_Portal_Openness_MCP `reference/tool-examples/languages` (MIT, commit `2767520`) |
| **R** | Reported behaviour, not a sample: BetterTIA/writer README/TODO (MIT, commit `fc8179c`) |
| **L** | Live-verified by this fork on TIA Portal V21, 2026-11-02 (import with `update_block_logic` format `sd`, 0 compile errors, re-export equal to the submitted code except whitespace; [live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)) |

A construct marked only **D** may be wrong. Write it, import it, and let TIA Portal's import and `compile_check`
decide; never treat a D row as fact.

## Document structure

```
{
    S7_Optimized := "TRUE";
    S7_PreferredLanguage := "LAD";
    S7_Version := "0.1"
}
FUNCTION_BLOCK "MyFB"
    VAR_INPUT
        Start : Bool;
    END_VAR
    VAR_OUTPUT
        Run : Bool;
    END_VAR

    { S7_Language := "LAD" }
    NETWORK
        RUNG wire#powerrail
            Contact( #Start )
            Coil( #Run )
        END_RUNG
    END_NETWORK
END_FUNCTION_BLOCK
```

| Construct | Notes | Evidence |
| --- | --- | --- |
| Header pragma block with `S7_Optimized`, `S7_PreferredLanguage`, `S7_Version` | block-level fallback language | S |
| Header keys `S7_BlockNumber`, `S7_BlockTitle`, `S7_BlockComment` (ids into `.s7res`) | | E |
| Header key `S7_Safety := "TRUE"` | marks a fail-safe block | D |
| `FUNCTION_BLOCK` / `FUNCTION "Name" : RetType` / `ORGANIZATION_BLOCK` / `DATA_BLOCK` and their `END_` keywords | | S |
| `FUNCTION "Name" : Void` | | D |
| `VAR_INPUT`, `VAR_OUTPUT`, `VAR` (static), `VAR_TEMP` … `END_VAR` | | S (VAR_INPUT), E (the others) |
| Network pragma on one line `{ S7_Language := "LAD" }` immediately before `NETWORK` | must never be separated from its `NETWORK` | S |
| Network pragma as a multi-line block `{ S7_Language := "LAD"; S7_NetworkComment := "MLC_4zs"; S7_NetworkTitle := "MLC_463" }` | keys separated by `;`, the last without | E |
| `S7_NetworkTitle` / `S7_NetworkComment` values are ids of `.s7res` entries | | E |
| `.s7res`: `MultiLingualTexts:` then `- id: MLC_x` and one `<culture>: text` line per language | TIA assigns its own ids to texts it creates (`MLC_4SY`, `MLC_3VL` seen live); caller-chosen ids (`MLC_T1`, `MLC_K1`) were accepted by `create_block_from_document` | E, L (caller-chosen ids, de-DE texts) |
| Every culture in the `.s7res` must be a language of the project | `create_block_from_document` with `en-US` and `de-DE` texts failed live: "Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text."; the same with only `de-DE` texts succeeded (the project's languages are presumably de-DE only, not verified) | L |
| A LAD block import needs en-US texts for every `.s7res` item, otherwise it may fail | reported by BetterTIA (TODO, as of 2025-09-02) and 1748477582; **contradicted live 2026-11-02 in a de-DE project**: a culture the project does not have makes the import fail | R, contradicted |
| No `.s7res` at all | TIA Portal V21's own export of a block without texts is only the `.s7dcl` (UTF-8 BOM, CRLF); `create_block_from_document` accepts a bundle without `.s7res` since 2026-11-02 | L (export) |
| Files may start with a UTF-8 BOM | keep it | E |
| SCL network in a LAD block: `{ S7_Language := "SCL" }` then `NETWORK`, SCL statements, `END_NETWORK` | | S, L |

## LAD (KOP)

A network holds one or more `RUNG … END_RUNG`. `RUNG wire#powerrail` starts at the left power rail. Elements in a
rung are in series, top to bottom = left to right.

| Element | Syntax | Evidence |
| --- | --- | --- |
| Normally open contact | `Contact( #Tag )` / `Contact( "GlobalTag" )` | S, L |
| Normally closed contact | `I_Contact( #Tag )` | R, D, L |
| Coil | `Coil( #Out )` | S, L |
| Set / reset coil | `S_Coil( #Out )`, `R_Coil( #Out )` | E, L |
| Positive / negative edge contact | `P_Trig( #EdgeMemory )`, `N_Trig( #EdgeMemory )` | E, R (P_Trig), D (N_Trig), L (both) |
| Invert result | `Not()` | E, L |
| Comparison contact | `{ S7_Templates := "SrcType := Int" }` then `LT_Contact( in1 := #A, in2 := #B )`; also `GT_Contact`, `EQ_Contact`, `NE_Contact` | E (LT, GT), R (EQ, NE), L (GT, EQ with the `SrcType` template) |
| Move box | `Move( in := 0, out1 => #Target )` | E, R, L (TIA writes an empty line inside the RUNG before `Move(` in its export) |
| Sub / Mul / Div | as Add | R |
| Operand names that need quotes | `Contact( #"A" )`, declared as `"A" : Bool;` | E |
| Add box | `{ S7_Templates := "SrcType := Int" }` then `Add( in1 := #A, in2 := 1, out => #A )` | E, L (TIA exports the template as a multi-line pragma) |
| IEC timer, instance in a multi-instance | `{ S7_Templates := "time_type := Time" }` then `#tonInst.TON( pt := T#500ms, et =>  )` with `tonInst : TON_TIME` in `VAR`; `TOF` with `TOF_TIME` alike | S (TON with template), E (multi-instance), L (TON and TOF multi-instances with the `time_type` template) |
| IEC timer, instance DB | `"DB_T_Rundown".TOF( pt := T#500ms, et =>  )` | S (TON on a DB), D (TOF) |
| An unused output | `et =>  ` (empty after `=>`) | S |

**Parallel branches** use named wires: `wire#wN` inside a rung taps a junction, `RUNG wire#wN` starts a branch there, and `… END_RUNG wire#wN` joins back into it (OR of two contacts from the power rail: E, L; nested branches below: D, L). A rung may end on a wire, and another rung may start on it:

```
RUNG wire#powerrail
    Contact( "Sensor_A" )
    wire#w1
    Contact( "Sensor_B" )
    wire#w2
    Coil( "Output_1" )
END_RUNG
RUNG wire#w1
    Contact( "Sensor_C" )
END_RUNG wire#w2
```

reads as `Sensor_A AND (Sensor_B OR Sensor_C) → Output_1`. Two branches from the power rail joining at `w1`:

```
RUNG wire#powerrail
    Contact( "Button_1" )
    I_Contact( "Button_2" )
END_RUNG wire#w1
```

Several coils or boxes after one wire (`RUNG wire#w1` twice) are parallel outputs.

`{ S7_Templates := "…" }` sits on the line before the instruction it types and must be kept (S).

## FBD (FUP)

`RUNG <first input>` starts a chain; each following element takes the previous result as its first input
(evidence D unless noted; **L** where marked).

| Element | Syntax |
| --- | --- |
| AND / OR / XOR box | `A( in2 := #B )`, `O( in2 := #B, in3 := #C )`, `X( in2 := #B )` (L) |
| Negated input | `in2 := NOT #Fault` (L) |
| Assignment / negated assignment | `Coil( #Out )`, `NOT Coil( #Out )` |
| Set / reset | `S_Coil( #M )`, `R_Coil( #M )` |
| SR box | `S_SR( r1 := #Clear, operand := #Mem )` |
| Edge | `P_Trig( #EdgeMem )`, `N_Trig( #EdgeMem )` |
| Constant start | `RUNG true` / `RUNG TRUE` |
| Box output feeding another box's input | the box rung ends `END_RUNG wire#w1`, the user writes `in2 := wire#w1` |
| Several outputs of one chain | `wire#w2` in the chain, then `RUNG wire#w2` |
| Rung starting at a box (no first input) | `RUNG` alone, then the call |

## Fail-safe programs (F-LAD / F-FBD)

- Fail-safe FBs and FCs are programmed only in FBD or LAD (Siemens "SIMATIC Safety – Configuring and
  Programming", V21). SCL is not available for F-blocks.
- An SD fail-safe block carries `S7_Safety := "TRUE"` in its header (D).
- Safety instructions are called like FBs, with a multi-instance in `VAR`, e.g. `EStop : ESTOP1;` and
  `#EStop.ESTOP1( E_STOP := "Emergency_Stop", ACK_NEC := NOT #Ack_Required, ACK := wire#w1, TIME_DEL :=  ,
  Q => #EStop_OK, Q_DELAY =>  , ACK_REQ => #Ack_Request, DIAG => )`, preceded by an inner pragma
  `{ f_user_card := "1"; f_image_card := "0" }` (D; meaning of the two keys not documented — copy them from an
  export, never invent them). `SFDOOR` follows the same form (D).
- In fail-safe programming EN/ENO cannot be wired (Siemens Distributed Safety Getting Started).
- Every write of a fail-safe block asks for the acceptance notice (`plc_safety_acceptance_required`). Validation,
  acceptance and the printout stay with a qualified person.
- **The main safety block of an F-runtime group is never SD-imported (L, 2026-11-02).** A SIMATIC SD import of it
  makes TIA Portal V21 drop the group's `MainSafetyBlockName` (the PLC compile then fails "does not have a valid main
  safety block"), and Openness cannot re-assign it; the SimaticML import keeps it. So every SD write onto such a block
  (`update_block_logic` sd, `insert_network`, `replace_network`, `delete_network`, `create_block_from_document` with
  `overwrite`) runs, inside one item: the document as a temporary F-block `zzMcpTmp_<block>_<8 hex>` in the block's
  group (without `S7_BlockNumber`, with `S7_Safety := "TRUE"`), `compile_check` of it, its SimaticML with `Name`,
  `Number` and `AutoNumber` set to the main block's, `update_block_logic` xml onto the main block, then the temporary
  block deleted. A compile error of your document stops before the main block changes; the item fails with the compiler
  messages. The result's `mainSafetyBlockRoute` lists the steps. Write the document exactly as for a direct import (the
  declared name stays the main block's). Other F-blocks are imported directly.
- A block just created from an SD document (for example with `create_block_from_document`) exports no SimaticML until
  it is compiled (L, BetterTIA pitfall B4): `get_block_content xml` then returns only the SD documents and a warning;
  run `compile_check` of the block and read again.

## Import form and export form differ (reported; not reproduced live on V21)

The 1748477582 skill (`tools/tiaportal-mcp/skill/SKILL.md`, section 9a, "verified 2026-06-11" by its author)
reports that V21 **exports** some instructions in a form that `ImportFromDocuments` **refuses** with a generic
"Failed importing". **This was not reproduced live on V21 on 2026-11-02** for `GT_Contact`/`EQ_Contact`, `Move`,
`P_Trig`/`N_Trig`, `TON`/`TOF` and `Add`: their re-export equals the import form below, apart from whitespace
(an empty line inside the RUNG before `Move(`, the `Add` template as a multi-line pragma). Other instructions were
not tested, so the report stands for them; author the import form (R):

| Need | Author (import form) | Export form, do not author |
| --- | --- | --- |
| Compare | `{ S7_Templates := "SrcType := DInt" }` + `GT_Contact( in1 := …, in2 := … )`; `LT_`, `EQ_`, `NE_Contact` | `Gt{ SrcType := DInt }( IN1, IN2 )` and `Lt`/`Eq`/`Ne` |
| Move | `Move( in := …, out1 => … )` | `Move{ Card := 1; DisableENO := TRUE }( IN, OUT1 )` |
| Rising edge | `P_Trig( #memBit )` | `PBox( … )` |
| IEC timer (inline template, also reported to import) | `#tonInst.TON{ time_type := Time }( PT := T#500ms, ET => )`; `TOF`, `TP` alike | same |
| FC call / FB call | `"FCName"()` / `"InstanceDB"( p := …, o => … )` | — |
| Symbolic global operand | `Contact( "DB".path.member )` | — |

Consequence (reported): a block exported by TIA and re-imported unchanged may fail if it contains export-only forms.
When an edit through SD fails on networks you did not touch, either rewrite those networks in the import form or use
the SimaticML route for that block.

Further reports from the same source (R): inline literal titles (`S7_NetworkTitle := "text"`) fail — titles must
be `MLC_*` ids present in the `.s7res`; the `.s7res` must exist even without titles (one entry with `zh-CN` and
`en-US`) — **contradicted live 2026-11-02**: TIA Portal's own export of a block without texts has no `.s7res`, and a
culture the project does not have (`en-US` in that de-DE project) made the import fail; both files UTF-8 **with**
BOM (TIA's own export has the BOM, L); a block whose preferred language is SCL cannot hold LAD networks, while a LAD
block can hold SCL networks. `Neg` was not import-verified there.

## Rules that avoid failed imports (Siemens skill unless marked)

1. Keep each network pragma directly before its `NETWORK`.
2. Strip only the `S7_Language` pragma from inserted code, never inner `{ S7_Templates … }` or `{ f_… }` pragmas.
3. One `NETWORK … END_NETWORK` per inserted code.
4. Blank lines inside a `RUNG` body are reported to fail ("File Content has some discrepancies"); TIA Portal V21
   itself writes an empty line inside a RUNG before `Move(` in its export, and that export re-imported live
   (2026-11-02), so this rule is a report, not a fact. Avoid them in code you write.
5. Import with `ImportFromDocuments(…, ImportDocumentOptions.Override)`, never through external sources.
6. When the block has texts, the `.s7res` sits next to the `.s7dcl` for the import; a block without texts needs none
   (TIA's own export has none, L).
7. Every culture in the `.s7res` must be a project language (L: an `en-US` text failed the import in a de-DE
   project). The "keep en-US texts" rule reported by BetterTIA/1748477582 was contradicted live on 2026-11-02 in
   a de-DE project.
8. A re-import can leave instance DBs and callers inconsistent (R): compile afterwards.
9. A SIMATIC SD import of the main safety block of an F-runtime group drops the group's `MainSafetyBlockName`
   (L, 2026-11-02, reproduced; the SimaticML route keeps it, and Openness cannot re-assign it). The server therefore
   routes SD writes onto a main safety block through a temporary F-block and imports SimaticML (see
   [fail-safe programs](#fail-safe-programs-f-lad--f-fbd)); `get_safety_administration` and `compile_check` afterwards
   confirm the reference.

## Workflow with this server

1. `plc_read list_networks` (or `get_block_content` format `sd`) — read the block as text. Each network lists
   `exportOnlyForms` it contains (see above).
2. Write the network in the import form above; copy every unusual form (templates, F-pragmas, instance calls) from
   an export of the same project, converted to the import form where this guide lists one.
3. `plc_write insert_network` / `replace_network` / `delete_network` (optional `expectedContentHash` from the sd
   read), `update_block_logic` with format `sd`, or `create_block_from_document` (`targetGroupPath`, an sd bundle with
   the `.s7dcl` and, when the block has texts, the `.s7res`) for a new block. Info guard
   `plc_sd_reimports_export_form` names untouched networks in a reported export form (not reproduced live for the
   instructions above). The verification reports `codeMatchesExport`; a difference is information, not a failure
   (TIA reformats whitespace).
4. `compile_check`, then `plcsim_test` for behaviour.
5. Look at the networks in the TIA editor once: the import may lay them out differently, and only the editor shows
   what the maintenance staff will see.

## Test block for the live acceptance

[`samples/MCP_SD_Matrix.s7dcl`](samples/MCP_SD_Matrix.s7dcl) with [`samples/MCP_SD_Matrix.s7res`](samples/MCP_SD_Matrix.s7res)
(UTF-8 with BOM, en-US and de-DE texts) is one FB with 15 networks: one per construct row of this guide (series and
NC contact, parallel and nested branch, set/reset, both edges, `Not()`, `GT_Contact`/`EQ_Contact`, `Move`/`Add`,
`TON`/`TOF` multi-instances (`TON_TIME`, `TOF_TIME`), FBD AND/OR/XOR, an SCL network). It was written for this
fork from the rows above.

Results of the live run on 2026-11-02 ([live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md), E1-E7):

- A 15-network LAD/FBD/SCL FB with these constructs was imported with `update_block_logic` format `sd`
  (Override), 0 compile errors; the re-export equals the submitted code for every construct (rows marked **L**);
  only whitespace differs (E2).
- `create_block_from_document` with an `.s7res` holding `en-US` and `de-DE` texts failed in `MCP_Livetest_01`
  ("Cannot import multilingual text with culture 'en-US' …"); with only `de-DE` texts and caller-chosen ids it
  succeeded (E4). In a project without English, remove the `en-US` lines of `MCP_SD_Matrix.s7res` before importing.
- A bundle without `.s7res` was refused by this server's own validation, stricter than TIA Portal (E5); fixed:
  the `.s7res` is optional.
- `get_block_content` format `sd` of a block without texts returns only the `.s7dcl` (UTF-8 BOM, CRLF); a block
  with network title and comment exports an `.s7res` with TIA-generated ids (`MLC_4SY`, `MLC_3VL`) (E1).
- `insert_network`, `replace_network` and `delete_network` chained in one call succeeded with 0 compile errors and
  correct verification; `replace_network` on a block with network title and comment kept the texts (E3).
- `replace_network` on the main safety block of an F-runtime group succeeded and compiled, but TIA Portal dropped
  the group's `MainSafetyBlockName` (E7, reproduced with `b79b2df`, L1); the temporary-F-block workaround kept it
  (L4); see the [safety reference](../SupportedOperations/PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports).
- `create_block_from_document` without `.s7res` created the FC `MCP_SD_OhneTexte` (E5 fixed).

## Open questions for the live acceptance

- Answered 2026-11-02: the D constructs `I_Contact`, `N_Trig`, nested branches and the FBD `A`/`O`/`X` boxes with a
  negated input import unchanged (E2). Still open: `S_SR`, `RUNG true`, box-to-box wires, `SFDOOR`/`ESTOP1`, the
  F-pragmas `f_user_card`/`f_image_card`, `NE_Contact`, `Sub`/`Mul`/`Div`, quoted operand names and `TOF` on an
  instance DB.
- Partly answered: the SD import of a network of an F-block (`Main_Safety_RTG1` in `MCP_Safety_01`) succeeded and
  compiled (E7), but dropped the runtime group reference (L1); a temporary F-FB created from an SD document with
  `S7_Safety := "TRUE"` became an F_FBD FB (L4). Still open: whether `S7_Safety := "TRUE"` is exported for an
  F-block, whether F-FBD and F-LAD behave alike, and the automatic route as one call.
- Answered: TIA assigns ids such as `MLC_4SY`/`MLC_3VL` to texts it creates (E1), and an import accepts
  caller-chosen ids (`MLC_T1`, `MLC_K1`) when their cultures are project languages (E4).
- Open: how the SD import treats a network it cannot draw (TIA error or silent change).
- Open: which other instructions, if any, V21 exports in a form it does not re-import.
