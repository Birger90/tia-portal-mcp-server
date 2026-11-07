# TIA Portal PLC Import and Export

**Language:** English · [Deutsch](IMPORT_EXPORT_OPTIONS_SUMMARY.de.md)

## Operation surface

The MCP provides a focused PLC round-trip interface through four operations (reads through `plc_read`, writes through `plc_write`):

| Operation | Direction | Default format | Purpose |
|---|---|---|---|
| `get_block_content` | Read | `xml` | Reads one existing PLC block. |
| `update_block_logic` | Write | `xml` | Updates one existing PLC block from supplied document content. |
| `get_type_content` | Read | `source` | Reads one existing PLC data type. |
| `update_type_content` | Write | `source` | Updates one existing PLC data type from supplied document content. |

The `format` field accepts `xml` and `source`, case-insensitively. Block and type reads use `plc_read`; writes use `plc_write`, passing the document as `content` together with `expectedContentHash`, the `contentHash` from a `plc_read` of the same object in the same format. Content changed since that read fails with `state_changed`. A document over the value budget (60,000 characters by default; raise it with `--max-item-chars`, see [response size limits](../guides/mcp-client-configuration.md#response-size-limits)) is omitted from the read, has no hash, and cannot be written.

## Supported formats

### PLC blocks

- `format="xml"` is the default. Reads return a controlled bundle containing sanitized SimaticML and SIMATIC SD `.s7dcl`/`.s7res` documents.
- `format="source"` returns Siemens external-source text for global DBs (`.db`) and for SCL-language FB, FC and OB blocks (`.scl`).
- Instance DBs, array DBs, and FB/FC/OB blocks written in LAD, FBD, GRAPH or STL use the XML route; they are not eligible for the external-source route and are refused with a message naming the addressed block.

### PLC data types

- `format="source"` is the default and returns `.udt` text.
- `format="xml"` returns SimaticML.

## Update contract

Updates are strict updates to an existing object:

- The target path must resolve to an existing block or data type.
- The declaration in the submitted content must match the addressed object name.
- Import does not create, rename, delete, or upsert objects.
- XML block imports use `ImportOptions.Override`.
- External-source generation uses `GenerateBlockOption.None`.
- Root groups, user groups, and software-unit-owned external-source groups are resolved according to the addressed path.

For block updates, the worker attempts validation, target import or generation, compile observation,
and an independent fresh postcondition read in order, then reports which stages were established. A
proven refusal before target invocation leaves import, compile, and final read `not_started`. If
target invocation starts but its return cannot be established, commitment is `unknown`; compile and
any unestablished final read are `unavailable`. On the normal-return path, the worker compiles the
affected scope and checks the postcondition by freshly resolving and re-exporting the block.
External-source global DB updates on that path compile the PLC because changing a DB declaration can
affect dependent blocks.

### Block-update outcome evidence

`plc_write` reports the non-atomic result of each `update_block_logic` item as an import outcome
object. A succeeded item's `result` is `{ format, importOutcome }`; a failed content import carries
the same object as `failure.blockImportOutcome`. `blockImportOutcome` is a conditional member on
every structured tool's failure schema and is omitted from unrelated items. The host requires
worker capability `typed-block-import-outcome-v1` before it sends engineering requests, so an older
worker is rejected during the capability handshake.

The object uses these fields and closed values:

| Field | Values and meaning |
|---|---|
| `importStage` | `not_started`, `completed`, or `unknown`. |
| `importResultState` | `success`, `non_success`, or `unavailable`. A normally returned `Blocks.Import` or `GenerateBlocksFromSource` call is `success`; a normally returned SIMATIC SD result maps to `success` or `non_success`; a call that does not return is `unavailable`. |
| `targetMutationCommitted` | Nullable Boolean. `false` proves refusal before the target import/generation call; `true` means that call returned normally; `null` means commitment is unknown after invocation started or transport became ambiguous. Presence evidence alone never determines this field. |
| `compileStage` | `not_started`, `succeeded`, `failed`, or `unavailable`. Warning-only reports are `succeeded`. `unavailable` is based on structured target/session/invocation evidence and takes precedence over `failed`, which takes precedence over `succeeded`. |
| `compileReport` | A nullable bounded copy of the whole aggregate compile report with TIA Portal's own message texts and paths (the `compile_check` messages: container nodes are not messages, paths carry the container chain). It is null when compilation did not start and may be null when no report could be established; a partial report is retained when available. Full aggregate error/warning totals are preserved even when detail is omitted. |
| `compileDetailsOmitted` | `true` only when a PLC, message, note, or string detail was actually omitted or shortened by the bounded projection (or the worker's compile report itself was already incomplete). |
| `finalReadStage` | `not_started`, `succeeded`, or `unavailable`. |
| `targetPresent` | Nullable Boolean. It is `true` or `false` when the independent fresh resolver establishes presence or absence, and null only when resolution itself is unavailable or unreliable. A later export/read failure can leave `finalReadStage=unavailable` while preserving `targetPresent=true`. Presence does not imply import commitment. |
| `contentRelation` | Always `unknown` in this contract. The final export is not claimed to equal either the requested content or the prior content. |
| `temporarySourceState` | `not_applicable`, `not_created`, `removed`, `residue_possible`, or `unknown`. XML uses `not_applicable`; source updates distinguish refusal before `CreateFromFile`, confirmed removal, possible residue, and ambiguous creation/cleanup. No temporary node name or path is exposed. |

The import fields form a closed matrix: `not_started` requires
`targetMutationCommitted=false` and `importResultState=unavailable`; `unknown` requires a null
commitment and `unavailable`; `completed` requires `true` and either `success` or `non_success`.
A normally returned target call is therefore conservative evidence that the addressed in-memory
target may have changed. It does **not** mean the project was saved, downloaded, persisted outside
the open engineering session, plant-accepted, or made byte-equivalent to the request.

Compile evidence aggregates the existing selected scope: the exact block target when `blockPath`
is supplied, the named PLC when only `plcName` is supplied, or all discovered PLCs in deterministic
order otherwise. The outcome retains at most 8 PLC entries, 20 message rows, 8 diagnostic notes,
256 decoded characters per string, and 6,000 decoded diagnostic characters across message text,
paths, and notes (`BlockImportOutcomeLimits`, shared by the worker projection and the host
validator). The complete serialized `blockImportOutcome` is limited to 12,000 UTF-16
characters, including JSON escape expansion and metadata. Within these bounds the messages are
TIA Portal's own texts (control characters replaced by blanks, trimmed) and the report keeps its
`blockPath`. Notes come first; messages are chosen by severity (errors, then warnings, then
information, each in report order) and delivered in report order; only what does not fit is
omitted, and only then is `compileDetailsOmitted` true. Until 2026-11-02 every message text was
replaced by "Compiler diagnostic omitted." and every path by an empty string, so
`compileDetailsOmitted` was always true (defect K04, live 2026-11-02: four short information
messages after `set_network_text`); the I2 design had treated compiler text like exception text,
although `compile_check` returns the same texts. Offline-tested only; not live-tested.

The sequential call remains non-atomic. Processing stops at the first failed item and later items
are marked `skipped`; the server does not retry, roll back, compensate, save, or download.
Unsafe exception-derived message text is deliberately replaced by bounded summaries. The pinned
project binding, access policy, guards, and audit of the guarded pipeline apply unchanged.

### Preview evidence

Each `update_block_logic` and `update_type_content` item's `plc_write` effect carries a structured
`contentDiff` object, in previews and actual calls alike. It compares the current exact-format text
read at planning with the submitted `content`; it does not predict Siemens post-write state and is
evidence only, not part of the hash check. Like is compared with like (since 2026-11-02, defect K31:
live, one contact added by plain XML showed 157 changed lines because the whole export bundle, with
its `--- FILE:` headers and `.s7dcl` part, was compared): in format `xml`, a submitted document
without `--- FILE:` delimiters is compared with the `.xml` document of the current export bundle
only, and `contentDiff.currentDocument` names that document (absent otherwise; the current side's
hash and counts are then that document's; a trailing line break the bundle layout adds is not
counted when the submitted document has none). A submitted bundle is compared with the whole
bundle, and every other format with the whole export. Not live-tested.

- Each eligible operation has at most 40 excerpt lines and 8,192 excerpt characters per side.
- When a changed span exceeds 40 lines, each excerpt contains the first 20 plus last 20 lines.
- Each displayed line is limited to 512 characters.
- The complete call is limited to 320 excerpt lines and 32,768 excerpt characters in request
  order. After that excerpt budget is exhausted, every eligible entry still retains its raw hashes,
  counts, and equality flags.
- Raw SHA-256, raw character count, and raw line count use the original text. For line-window
  comparison only, line-ending normalization maps CRLF and CR to LF; it performs no other
  normalization. The response also reports `rawTextEqual`, `normalizedLinesEqual`, and
  `lineEndingOnly`; a line-ending-only difference has unequal raw text but equal normalized lines.
- Every other operation has `contentDiff: null`.

## V21 pitfalls

Reported by rickgaiser/TiaMcp, not verified on V21; see [V21 pitfalls](V21_PITFALLS.md).

- A bundle submitted to `update_block_logic` **without** a delimited `.xml` document but with an
  `.s7dcl` document is imported through SIMATIC SD (`ImportFromDocuments`); this is the only `.s7dcl`
  write path of `plc_write` (`update_type_content` never imports `.s7dcl`). That import is reported to
  alter an array access inside an index in a LAD/FBD network, a member typed with a project FB/UDT named
  like a system type and AT overlays without an error. Planning fires the informative guard
  `plc_s7dcl_import_may_alter` with the constructs found, the item carries a warning, and the
  verification entry `s7dclConstructs` compares each construct with the re-export and fails when it
  differs. `get_block_content` (`xml`) warns about such blocks.
- Overwriting a block with a textual interface is reported to terminate TIA Portal after more than one
  export since the project was opened or the block was last written. The worker counts its exports per
  block; planning fires `plc_textual_interface_overwrite_may_terminate` and
  `plc_save_before_write_recommended` (both informative). This write path itself exports the block more
  than once before the import.

## Format matrix

| Capability | Status |
|---|---|
| PLC block SimaticML/XML read and update | Supported |
| PLC block SIMATIC SD `.s7dcl`/`.s7res` bundle handling | Partial: supported as controlled bundle context |
| PLC block external-source `.db` exchange | Partial: global DBs |
| PLC block external-source `.scl` exchange | Partial: SCL-language FB/FC/OB |
| LAD/FBD/GRAPH/STL FB/FC/OB external-source exchange, instance DBs and array DBs | Not supported; use the XML route |
| PLC data type SimaticML/XML exchange | Supported |
| PLC data type `.udt` exchange | Supported |
| Existing-object update | Supported |
| Create, rename, delete, or upsert through import | Not supported |
| Root, user-group, and software-unit target routing | Supported |
| Caller-selected Openness `ExportOptions` | Partial: XML export uses `None` |
| Caller-selected Openness `ImportOptions` | Partial: imports use `Override` |
| `SWImportOptions` for structural changes or missing references | Not supported |
| Know-how-protected block exchange | Partial: generic export may expose only the public interface or fail; protected import is unavailable |
| Dedicated failsafe block exchange | Not supported; compatible consistent F-blocks may use the generic XML route |
| System-block exchange | Not supported |
| Tag tables, tags, and constants through import/export | One tag table as a SimaticML file: `plc_write export_tag_table` / `import_tag_table` (not live-tested; see [BetterTIA operations reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions)). CSV export (not import) through `plc_write export_tag_tables_csv` |
| External source management (list, `CreateFromFile` import, `GenerateBlocksFromSource` with `None`/`KeepOnError` into a group, delete, `GenerateSource` of several blocks/types to a caller file) | Supported (not live-tested); see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#external-sources) |
| Batch export of all blocks/types of a PLC or group to a directory (SimaticML documents or sources, per-file SHA-256) | Supported (not live-tested) through `plc_write export_blocks`, built on the reads above |
| Technology objects | SimaticML through `plc_write` `export_technology_object`, `import_technology_object` and `update_technology_object_content`, and `plc_read` `get_technology_object_content`; see [TECHNOLOGY_OBJECTS_SUMMARY.md](TECHNOLOGY_OBJECTS_SUMMARY.md) (not live-tested) |
| Watch tables and force tables | SimaticML through `plc_read` `get_watch_table_content`/`get_force_table_content` and `plc_write` `update_watch_table_content`/`update_force_table_content` (`Import(..., ImportOptions.Override)` behind `expectedContentHash`); see [PLC_ONLINE_OPENNESS_SUMMARY.md](PLC_ONLINE_OPENNESS_SUMMARY.md) (not live-tested); CSV export (not import) through `plc_write` `export_watch_tables_csv`/`export_force_tables_csv` |
| PLC alarm and ProDiag exchange | Supported (not live-tested): `plc_write` `export_/import_alarm_texts`, `export_/import_alarm_classes`, `export_/import_text_lists`, `export_/import_supervisions`, `import_supervision_settings`, `export_prodiag_info`, `export_/import_system_diagnostics_settings`; see [PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.md#files) and [ProDiag](PLC_ALARMS_TEXTLISTS_SUMMARY.md#prodiag) |
| Hardware CAx (AutomationML) exchange | Supported (not live-tested): `network_write` `export_cax`/`import_cax`; see [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#cax-data-automationml) |
| Project-text exchange | Supported (not live-tested): `plc_write` `export_project_texts`/`import_project_texts` (`.xlsx`); see [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md#project-languages-and-project-texts-plc_readplc_write) |
| Graphics exchange | Not supported: V21 documents `GraphicsProvider` but not which object offers it; see [HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md#left-out-issue-28) |
| HMI import/export | Not supported; see [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md) |
| Generic arbitrary-object XML import/export | Not supported |

## Relationship to the Openness API

TIA Portal Openness supports additional PLC, HMI, project, hardware, alarm, and text exchange APIs. This MCP surface intentionally exposes only the PLC block and PLC data-type routes described above. It does not provide a generic proxy for arbitrary `Import` and `Export` calls.
