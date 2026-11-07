# TIA Portal PLC Operations

**Language:** English · [Deutsch](PLC_OPERATIONS_SUMMARY.de.md)

## Supported read operations

| Entry point | Operation | Inputs | Behavior |
|---|---|---|---|
| `browse_project_tree` | `browse_project_tree` | Optional `depth`, `startSelector`, `pageSize`; continuation `cursor` | Locates PLC software, blocks, groups, types, and other project objects; cursors reject binding changes. |
| `plc_read` | `get_block_content` | Required `blockPath`; optional `format`, `withDependencies` | Reads an existing block as XML/SimaticML (default), an eligible external source, or its SIMATIC SD documents (`format: "sd"`, see [PLC_SIMATIC_SD_SUMMARY.md](PLC_SIMATIC_SD_SUMMARY.md); not live-tested). When TIA Portal exports no SimaticML (it does not export inconsistent blocks, e.g. one not compiled since it was created from an SD document; confirmed live on V21 2026-11-02), the xml result holds the SD documents TIA Portal did export and a warning names the cause ("run compile_check and read again"; defect D15, before which the missing `.xml` was silent). See [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md). |
| `plc_read` | `get_type_content` | Required `typePath`; optional `format`, `withDependencies` | Reads an existing PLC type as `.udt` source (default) or XML/SimaticML. |
| `plc_read` | `list_tag_tables` | Optional `plcName`, `folderPath`, `tableName` | Lists PLC tag tables and the exposed tag and constant information. |
| `plc_read` | `read_address_occupancy` | Optional `plcName`, `areas`, `includeHardware` | Assignment list of one PLC: declared I/Q/M tags (width = the data type's documented length, cross-checked with the address spelling) plus the PLC's hardware I/O addresses, tag overlaps, I/Q tags outside hardware, tags without an absolute address and unsized tags (data type without a documented length; they make the result incomplete). See [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#plc_read-operations). |
| `plc_read` | `find_free_address` | Required `area`, `dataType`; optional `plcName`, `startByte`, `endByte`, `alignBytes`, `requireHardware` | Lowest address of the area that no declared tag uses (optionally inside a hardware range); `startByte`/`endByte` 0–65535; incomplete when an unsized tag lies in the area. See [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#plc_read-operations). |
| `plc_read` | `get_block_interface` | Required `blockPath` | Block identity (canonical path, type, number, language, know-how protection, instance-of FB), the xml `contentHash` and the interface as a typed member tree. See [Granular block editing](#granular-block-editing). |
| `plc_read` | `get_type_interface` | Required `typePath` | The same for a PLC data type (section `None`). |
| `plc_read` | `get_db_values` | Required `blockPath`; optional `memberPath`, `withAttributes` | Start values of a global or instance DB at every depth, read with `Interface.Members.Find`. |
| `plc_read` | `get_call_tree`, `find_call_paths`, `find_unused_blocks`, `find_orphaned_instance_dbs`, `analyze_change_impact` | See [Program analysis](#program-analysis-plc_read) | Host-computed, read-only analysis over the block inventory and the cross-reference reader; not live-tested. |
| `plc_read` | `list_external_sources`, `read_tag_comments`, `find_objects`, `scan_accessible_devices` | See [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md) | External sources of the PLC and its software units; multilingual tag and user-constant comments; objects by glob with paging; accessible devices on an explicitly named PC interface (`OnlineObserve`, every mode); not live-tested. |
| `plc_read` | `list_text_lists`, `read_text_list_workbook`, `read_alarm_text_workbook`, `read_supervision_workbook`, `get_prodiag_fb`, `get_prodiag_assignment` | See the [PLC alarms and text lists reference](PLC_ALARMS_TEXTLISTS_SUMMARY.md) | User and system text lists with ID, range and comment per culture; text list entries, alarm instance texts and ProDiag supervisions as uninterpreted cells of TIA Portal's XLSX export with a `workbookHash`, paged within the value budget; ProDiag FB evidence and `AssignedProDiagFB`; the matching writes (`update_text_list`, `delete_text_list`, `update_*_cells`, `export_*`, `import_*`, `set_prodiag_assignment`, `export_prodiag_info`) are in the same reference; not live-tested. |
| `plc_read` | `get_plc_checksums`, `get_fingerprints` | Optional `plcName`; exactly one of `blockPath` or `typePath` | Offline change detection: the PLC's software and text list checksums, the fingerprints of one block or PLC data type, as text; see [Offline change detection](#offline-change-detection-plc_read); not live-tested. |
| `plc_read` | `get_block_protection` | Required `blockPath` | Know-how and write protection state of one block and which protection members bind; see [Block know-how and write protection](#block-know-how-and-write-protection-plc_readplc_write). Not live-tested. |
| `plc_read` | `get_project_languages` | None | Active, editing, reference and supported project languages and the text categories; see [Project languages and project texts](#project-languages-and-project-texts-plc_readplc_write). Not live-tested. |
| `plc_read` | `describe_operations` | Optional `operationNames` (1–3 PLC read or write operation names) | The operation reference: required and optional fields, the full field rules and the reference text of each named operation, or without names the index of every operation with its fields; no bound project needed. See [Operation reference](#operation-reference-describe_operations). |
| `read_cross_references` | `read_cross_references` | Required `target`; optional `filter`, `maxResults` | Reads the cross-references of one project-tree target. Filters are `AllObjects`, `ObjectsWithReferences` (default), `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | `compile_check` | Optional `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

`plc_read` and `read_cross_references` are registered in every access mode. `execute_read_batch` was
retired; use these two tools instead. Tree browsing and compilation are standalone tools. `compile_check`
and `plc_write` are available in read-write and full modes, and behave the same in both. PLC run/stop
(`start_plc`/`stop_plc`) was removed; no mode controls a PLC.

Use `bind_project` in any mode to adopt or switch to an already-open project; a different binding
requires `forceRebind:true`. Ordinary PLC reads never bind, switch or open. Only `open_project` and
`create_project` open projects. Lifecycle runs in read-write with one confirmation form per actual
call, or in full without server elicitation; block guards stop the call in every mode. The complete
tool counts are 13/32/32. Read-only never opens, creates, saves or closes a project. Downloading a PLC to
PLCSIM or real hardware is the separate `plc_download` tool; see the [PLC download reference](PLC_DOWNLOAD_SUMMARY.md).
Going online/offline and station upload are the separate `plc_tia_online` and `plc_station_upload` tools; see the [PLC online Openness reference](PLC_ONLINE_OPENNESS_SUMMARY.md).
Simulated CPUs are driven by the `plcsim_*` tools; see the [PLCSIM operations reference](PLCSIM_OPERATIONS_SUMMARY.md).

## PLC reads (`plc_read`)

`plc_read(operations)` takes at most 50 items. Each has a unique, non-blank `operationId` of at most
256 characters, an `operation`, and that operation's parameters. Items run
in caller order and independently: a failed item does not stop the others. Unknown fields are
rejected. The response is one structured document (`contractVersion` `1.0`, root `warnings` array,
explicit nulls), delivered identically as text and `structuredContent`. `isError` is true only for a
rejection before anything ran.

- `get_block_content` and `get_type_content` return `{ format, content, contentHash, warnings }`.
  `format` is `xml` or `source` (blocks also `sd`, with a conditional `failSafe: true` for an `S7_Safety` header); blocks default to `xml` and types to `source`. `withDependencies`
  adds the dependency closure to a `source` export; that document is context only and cannot be
  written back.
- `contentHash` is `xml:sha256:<hex>`, `source:sha256:<hex>` or `sd:sha256:<hex>` over the exact served text. It is an
  explicit `null` when `withDependencies` is true.
- `list_tag_tables` returns `{ isComplete, plcs[] }`, each PLC naming its software and device and
  carrying its `tables[]`. An omitted `plcName` reads every PLC, including PLCs in device groups.
  Tags carry `externalAccessible`, `externalVisible` and `externalWritable` (`null` when unreadable);
  user constants carry a readability marker; anything that could not be read sets `isComplete:false`
  with a message (the first 50 messages are kept, then one summary entry). `tableName` keeps one table
  and `folderPath` the tables directly in one folder (written as the inventory emits it, `/` or
  `/Line/Cell`); both are case-insensitive and combine with `plcName`. A narrowing that matches no
  table fails the item with `target_not_found`, unless part of the tree was unreadable.
- `list_download_targets` (optional `plcName`; omitted reads every PLC) returns
  `{ plcs[{ plcName, deviceName, downloadable, modes[{ name, pcInterfaces[{ name, number,
  targetInterfaces[{ name }] }] }] }] }`: the offline download path read from
  `DownloadProvider.Configuration`, never by going online or scanning the network. Standalone
  projects only. It feeds `plc_download`; see the [PLC download reference](PLC_DOWNLOAD_SUMMARY.md).
- `compare_to_online`, `compare_software`, `compare_hardware`, `compare_hardware_to_online`, `get_online_state`, `list_station_upload_interfaces`,
  `list_watch_and_force_tables`, `read_watch_table`, `read_force_table`, `get_watch_table_content` and
  `get_force_table_content`, and the `plc_write` watch/force table operations and `update_program`, are
  documented in the [PLC online Openness reference](PLC_ONLINE_OPENNESS_SUMMARY.md) (not live-tested).
- Content tools act on the bound project only; with no verified binding (a configured `--project` is grounded by status first) they fail closed with `binding_conflict`; call `bind_project` first.
  An unbound call returns one root error, never per-item failures. Reads never bind, switch or open a project.

Budget: a value over the value budget or a document over the response budget (60,000 and 180,000
characters by default) is never cut. The affected item `result` is omitted whole with an omission
record and narrowing guidance, so it has no hash. A single block or type export over the value budget
therefore cannot be read through `plc_read`, and cannot be updated through `plc_write`, which needs
the hash, until the server is restarted with `--max-item-chars` at or above the omission's
`originalChars` and `--max-document-chars` above that with headroom; read such a block in its own
call. See [response size limits](../guides/mcp-client-configuration.md#response-size-limits). One PLC
of roughly 400 tags already exceeds the default value budget: narrow `list_tag_tables` with
`plcName`, `folderPath` or `tableName`.

## Operation reference (`describe_operations`)

Not live-tested (it reads nothing from TIA Portal). Claude Desktop refused `plc_read` and `plc_write` while their
tool listing carried the full per-operation text ("Claude can't use 2 tools from Tia portal because of a problem with
that connector", defect K91): the input schemas were 77–79 KB and the descriptions 20,590 and 49,996 characters. The
tool descriptions now carry a summary, every operation name and, for `plc_write`, every acknowledge and block guard
and the safety acceptance rule; the item schema keeps every field, type, enum and nesting level, but each field
description is shortened to its first sentence (none when that is longer than 160 characters). Every tool stays at
most 8,000 description characters and 30 KB of input schema, with no deeper nesting than before.

The full text moved to `plc_read` `describe_operations` (`Observe`, every access mode; a call of only
`describe_operations` items needs no bound project):

- `operationNames` (1–3 names per item, several items per call): per operation its `tool`, `requiredFields`,
  `optionalFields`, `fields` (each field's documentation for this operation: the parts of a shared field description
  that name only other operations are left out and `trimmed` is true; a part that speaks of a group of operations, such
  as "every technology object operation except ...", or a shorthand such as `add/update/delete_interface_member` that
  covers the operation stays), `reference` (every segment of the former tool description that names the operation) and
  `sectionNotes` (segments that name no operation and belong to it: a clause after "; " continuing one of its segments,
  or a sentence of a paragraph that names it; K93: a note is no longer attached to every operation of a long paragraph).
- without `operationNames`: `index`, every PLC read and write operation with its fields as one text
  (`"blockPath; optional: format, withDependencies"`).
- always: `readToolRules` and `writeToolRules` (the tool-wide rules) and `referenceDocument`, this page.

Unknown, repeated or blank names and more than 3 names are a `validation_error`; `operationNames` is refused on
every other operation. Validation of every other operation is unchanged: the schema descriptions are never read by it.

## Program analysis (`plc_read`)

> **Not live-tested.** Built and qualified offline against fixture data shaped like the
> `read_cross_references` reader's output. No recorded TIA Portal run shows a block call graph through
> the cross-reference service, so the direction rule below is inferred, not observed for blocks. Run
> the [live acceptance steps](#program-analysis-live-acceptance-steps) before relying on the results.

Five `plc_read` operations analyse one PLC's program. They are computed in the host from existing
reads only, need no Siemens member the repository does not already call, and change nothing
(`Observe`, every access mode; the binding rules of every content read apply):

| Operation | Inputs | Result |
|---|---|---|
| `get_call_tree` | `blockPath` of an OB, FB or FC (a data block is `target_kind_unsupported`); optional `maxDepth` (1-50, default 10), `maxElements` (nodes, 1-2000, default 100), `timeoutSeconds` | Pre-order `nodes[{ id, parentId, depth, block, evidence, instances, cycle, expandedElsewhere, childrenOmitted }]` of the call and multi-instance relations from the root, the instance DBs each node uses (`instances`), callees outside the inventory (instructions such as `TON`) as leaf nodes with `inInventory:false`, and `undirectedCallEvidence`. A callee already on the path is a `cycle`; one expanded earlier in the tree is `expandedElsewhere`; neither is expanded again. |
| `find_call_paths` | `blockPath` (any block); optional `maxDepth` (hops, 1-50, default 20), `maxElements` (paths, 1-1000, default 50), `timeoutSeconds` | Every simple path from an OB to the block: `paths[{ blocks[], relations[] }]` (`relations[i]` joins the relations of hop i with `+`), the OBs reached (`roots`), `directCallers` and the target's undirected evidence. Interior hops are call or multi-instance relations; for a data block (DB or type) the last hop may be any directed relation. A path search also stops after 200,000 steps (`pathSteps`). |
| `find_unused_blocks` | optional `plcName`, `maxElements` (listed blocks, 1-5000, default 500), `timeoutSeconds` | Blocks no OB reaches over any directed relation: `unused[]` with category `unreferenced` or `referencedOnlyByUnreachableBlocks` (with `referencedBy`), `usedOnlyOutsideProgram[]` (no reachable block, but objects outside the inventory such as HMI tags use it) and `undetermined[]` (shares undirected evidence with a reachable block, or is reached only through an undetermined block: over a directed relation or further undirected evidence from one); `roots`, counts. Any undetermined block makes the result `complete:false` with an `evidence.limits` line (the listing does not decide those blocks). An incomplete result carries a warning that it is not proof that blocks can be deleted. |
| `find_orphaned_instance_dbs` | optional `plcName`, `maxElements` (instance-DB exports, 1-1000, default 200), `timeoutSeconds` | For each instance DB, reads its SimaticML export (`get_block_content` `xml`) and takes `AttributeList/InstanceOfName` of `SW.Blocks.InstanceDB`. `orphans[]`: `instanceOfUnreachable` (the FB exists but no OB reaches it), with `dbReachable`. `unresolvedInstanceOf[]` (with `unresolvedInstanceOfCount`, at most 200 listed; not orphans and not counted in `orphanCount`): `instanceOfNotInInventory` (neither a block nor a PLC data type of the PLC: a system FB/SFB or system data type such as an IEC timer type, which the inventory does not list, or a deleted FB; the analysis cannot tell which). `notChecked[]`: `notAddressable` (system block folder), `exportFailed`, `noInstanceOfName`, `notAnInstanceDbDocument`, `instanceOfAmbiguous` (the name matches several blocks or PLC data types, e.g. across software units; never resolved, carries `instanceOfName`), `maxElements`, `timeoutSeconds`. `checkedCount` counts orphan, unresolved and resolved DBs; any `notChecked` entry makes the result incomplete. |
| `analyze_change_impact` | exactly one target: `blockPath`, `typePath`, or `tableName` + `name` (a PLC tag; optional `plcName`, `folderPath`); optional `maxDepth` (1-50, default 10), `maxElements` (users, 1-5000, default 500), `timeoutSeconds` | `users[{ object, depth, via, relations, accesses }]`: depth 1 are direct users, deeper ones depend on `via`; `affectedRoots` (OBs among them), `externalUsers` of the target (HMI or other-device objects) and its undirected evidence. A type target also sweeps the type folders (type-in-type and type-in-DB use); a tag target reads the tag's own cross-references. |

### Evidence and rules

Each analysis item reads, once per `plc_read` call and shared by every analysis item of the call:
one project-tree snapshot (the block, type and tag-table inventory `browse_project_tree` shows), one
`read_cross_references` sweep (filter `ObjectsWithReferences`, no cap) per root block folder of the
PLC and of each software unit, and only when needed the type folders, the tag's own query and the
instance-DB exports. The target is resolved against the inventory first, so a wrong path costs no
sweep. A failed evidence read fails the item with its category (fail closed, no partial graph).
Every result carries `complete`, `truncation` (`maxDepth`, `maxElements`, `timeoutSeconds`,
`pathSteps`) and `evidence { crossReferenceQueries[], matchedSourceCount, unmatchedSourceCount,
ambiguousReferenceCount, directedEdgeCount, undirectedEvidenceCount, complete, rules[], limits[] }`.

A relationship is used only when a cross-reference location states it:

- **Direction** comes from the location's `referenceType` only: `UsedBy` means the reference object uses
  the queried object, `Uses` the reverse. Both sides are merged (`evidence.reportedAs` lists `uses`
  and/or `usedBy`). Any other reference type states no direction: the pair is reported as undirected
  evidence and never traversed. `UsedBy` for users is the semantics recorded for HMI tags (2026-10-13
  spike, 2026-10-12 acceptance) and relied on by HMI write planning; `Uses` is its V21 counterpart and
  has no recorded block-level run.
- **Relation** comes from `access`: `Call`, `UC`, `CC` are calls (`Call` was recorded on a system FB,
  2026-10-05); `Multiinstance`/`MultiinstanceAndSymbol` a multi-instance declaration (recorded as
  `Multiinstance`; whether the declared instance is actually called is not distinguishable);
  `InstanceDB`/`InstanceAndSymbol` an instance-DB use and `Parameterinstance(AndSymbol)` a parameter
  instance; anything else a plain `reference`. Call trees and paths follow `call` and `multiInstance`;
  reachability and impact follow every directed relation.
- **Identity**: sources are matched by exact name (case-insensitive) against the blocks or types of the
  folder that was swept, references against the whole inventory. A name that matches several objects
  (duplicate names across software units or namespaces) is not resolved and makes the evidence
  incomplete. A reference whose `device` differs from the queried object's is outside the inventory
  (HMI objects). Child sources (DB members, interface members) are attributed to their top-level source.
- `complete` is false when the tree skipped nodes, a query was incomplete, a source matched no or
  several inventory objects, a name was ambiguous, or a cap was hit; for `find_unused_blocks` also when a
  block is `undetermined`, and for `find_orphaned_instance_dbs` when an instance DB was not checked
  (including an ambiguous `InstanceOfName`).

Limits: indirect calls and accesses are invisible (block parameters `BLOCK_FB`/`BLOCK_FC`,
ANY/VARIANT/DB_ANY pointers, DB numbers computed at runtime, `OPN` with a variable, instructions that
address blocks by number), so an unreachable block is a candidate, not proof; every OB is a root
whether its event occurs or not; an FB whose single-instance calls were recorded only against its
instance DB (no location naming the FB) would show as unreachable, although the recorded 2026-10-05
run shows `Call` locations on an FB's own query; whether uncompiled edits are reflected in the
cross-references is not verified; that every instance-DB export carries `InstanceOfName` (the documented V21 `InstanceDB`
attribute "the block name of the father instance (FB/SFB/UDT/SDT)") is not verified, and one without it
is `notChecked`, never guessed. `timeoutSeconds` (1-600, default 60) bounds the traversal and the start
of further instance-DB exports; the evidence reads themselves always complete, each bounded by the
worker request timeout. A result over the value budget is omitted whole with narrowing guidance (lower
`maxElements` or `maxDepth`); each result carries a fixed evidence section of about 3,000 characters.
A large PLC's sweep payload is not capped between worker and host.

### Program analysis live acceptance steps

1. On a disposable project with a known call structure (OB1 calling an FB with an instance DB and a
   multi-instance, an FC chain with recursion, an unreachable FC, a DB used only by an HMI tag, an STL
   `UC`/`CC` call, a call through a `BLOCK_FC` parameter), run `read_cross_references` on the root block
   folder and record which locations carry `Uses`, `UsedBy` and other reference types, and their
   access names.
2. Compare `get_call_tree` of OB1 and `find_call_paths` of the FB with the TIA Portal call structure
   view; check `instances` against the instance DBs, and that the `BLOCK_FC` call is absent.
3. Compare `find_unused_blocks` with the TIA Portal cross-reference "unused objects" filter, and
   `find_orphaned_instance_dbs` after deleting an FB that still has an instance DB (listed in
   `unresolvedInstanceOf`, like the instance DB of an IEC timer); confirm that the instance DB export
   carries `InstanceOfName`.
4. `analyze_change_impact` for a block, a PLC data type nested in another and used by a DB, and a tag
   used by a block and an HMI screen.
5. Repeat on a project with software units, including a name that exists in two units.

## Compiler diagnostics (`compile_check`)

Compilation can change TIA Portal's in-memory project state. A returned report is not evidence of
saving, downloading, or PLC/plant acceptance. `compile_check` finds PLCs at the project root and in
device groups. Without `plcName` it compiles every PLC; it compiles nothing if a device item cannot be
read, because that item could hide a PLC. A `blockPath` without a PLC qualifier requires a single PLC
in the project and otherwise fails with `target_ambiguous`.

Each PLC result uses `plcName` for the actual PLC software name and `deviceName` for the containing
hardware device. The input `plcName` selector continues accepting either name. The same identities
are returned for PLC and selected-block compilation, including expected compiler failures.

Nested compiler messages are flattened in deterministic parent-before-child order. Each row uses
that message's own description, path, and severity. Siemens-reported `state`, `errorCount`, and
`warningCount` are preserved whenever a compiler result is available; they are not recomputed from
the displayed rows and remain available on handled root-diagnostic collection access failures.

One projection budget is shared across all selected PLCs:

- Maximum message depth: 16, counting roots as depth 1.
- Maximum projected message count: 200.
- Maximum length of each description and path: 1,024 UTF-16 code units.
- Maximum combined description/path text: 32,000 UTF-16 code units.

The serialized compile report is additionally kept below 60,000 characters, including JSON
escaping. Trailing message rows may be removed to meet that bound without changing reported totals
or identities. Each affected PLC receives one sanitized omission note in `diagnosticNotes` when
diagnostics are shortened, omitted, or recoverably unreadable. For nested diagnostic access,
recoverable failures are expected exact `InvalidOperationException` access failures, not derived
exceptions. Infrastructure or interruption failures—including session loss, I/O, cancellation,
format errors, derived invalid-operation exceptions, and other unexpected faults—fail the operation
rather than return typed partial success. If metadata alone exceeds the response limit, the
operation fails with bounded guidance: compilation may have run, so inspect the current in-memory
project state before deciding whether to retry.

When an expected compiler invocation failure leaves no compiler result, the typed PLC result keeps
both identities, reports `state: "Error"`, and includes a sanitized unavailable-details note.
Its zero counts are fallback values, not evidence of an error-free compilation.

## Cross-reference coverage (`read_cross_references`)

`read_cross_references(target, filter?, maxResults?)` reads one target per call.
`target` is `{ path: [{ nodeType, name }, ...], member?: { kind, name } }`: copy the `path` segments
from `browse_project_tree` output (matching is case-insensitive as in the tree; siblings that differ
only in case are `target_ambiguous`). `member` addresses leaves the tree does not model, under a
`TagTable` path, with `kind` one of `Tag`, `SystemConstant` or `UserConstant` (exact case). `filter`
and `maxResults` (1 or greater) are validated before any worker call.

Provisionally, `target` may instead be `{ hmi: { hmiName, tagName } }` for one top-level WinCC
Unified HMI tag; it may be replaced by a unified HMI selector once alarms or screens need
cross-references. `hmiName` is required (software or device name, case-insensitive) and `tagName`
matches case-insensitively. A dotted UDT member name (`Motor_1.Speed`) fails `target_not_found` with
the guidance to name only the top-level tag; an ambiguous HMI is `target_ambiguous` and a Classic
HMI `target_kind_unsupported`. A target carries either a non-empty `path` (with optional `member`) or
`hmi`: both, neither (including an empty `path` without `hmi`) or a blank `hmiName`/`tagName` is
`validation_error`. Filters, `maxResults`, budgets and the result shape are unchanged, and the
target echo repeats the `hmi` selector.

- A path ending at a leaf (an OB, FB, FC, GlobalDB, InstanceDB or ArrayDB block, a system block, or a
  PLC type) or any `member` is one query on that object.
- A path ending at a container (`Device`, `PlcSoftware`, `SoftwareUnit`, a block, system-block,
  tag-table or type folder, or `TagTable`) sweeps every owner beneath it. A container with no owners
  is an empty, complete success; if owners exist and none of their queries succeeds, the call fails
  with `worker_operation_failed`.
- Zero matches is `target_not_found`; a leaf without the cross-reference service is
  `target_kind_unsupported`, which is distinct from "no references".

The result is one typed document: the target echo, `filter`, completeness fields (`isComplete`,
`ownerQueryCount`, `successfulOwnerQueryCount`), totals and the recursive
`sources[] -> references[] -> locations[]` hierarchy. `referencedAs` is `{ name, typeName }` of the
referenced object, and `access` and `referenceType` are V21 member names. Overlapping results from
different owners are not deduplicated. A complete successful sweep can have zero sources.

Unavailable owner services, traversal skips and a `maxResults` cut retain the available data and set
`isComplete:false` with messages. An incomplete `UnusedObjects` result is not an authoritative
unused-object audit and must not be used as proof that objects can safely be deleted.

Budget: sources are dropped whole from the tail until the report value fits 60,000 characters and
the document 180,000. The report then carries `isComplete:false`, `omittedSourceCount` and narrowing
guidance (`filter`, `maxResults`, a narrower `path`, or a `member`). When not even one whole source
fits, the first source is kept and trimmed inside (child sources, then references, then locations),
with one message counting what was omitted; the omitted tail cannot be paged.

### Unverified owner kinds

Only kinds whose owners were verified live (2026-10-05, V21) ship as members or endpoints. WinCC
Unified `HmiTag` ships through the provisional `hmi` selector above (live 2026-10-12, see the
[completion report](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md)).
Not shipped, and unverified for this tool: technology objects (the cross-reference service works,
but the project tree has no node type for them), `DeviceItem`,
`Subnet` and `Node` (no service observed), technology-object instance DBs and WinCC Classic HMI tags
(not present in the spike project). `Device`, `PlcSoftware`, `SoftwareUnit` and `TagTable` are not
owners themselves; they only enumerate the owners beneath them. See Appendix B of the
[design spec](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-05-plc-read-write-and-cross-references-design.md).

## PLC writes (`plc_write`)

`plc_write(operations, dryRun=false)` runs up to 50 PLC software writes in caller order against one
verified, already-open project. Omitted `dryRun` or `dryRun:false` executes; `dryRun:true` returns
`phase:preview` with effects, `dependsOn` and guards, without mutation. No confirmation prompt is sent
in any mode. Each item has a unique, non-blank `operationId` of at most 256 characters; the tool acts on the
bound project only (an item `projectPath` is rejected as `validation_error`). The tool never
saves, downloads, or controls a PLC.

| Operation | Required inputs | Optional inputs |
|---|---|---|
| `update_block_logic` | `blockPath`, `content`, `expectedContentHash` | `format` (`xml` by default; `source` for global DBs and SCL blocks; `sd` for the SIMATIC SD documents, see [PLC_SIMATIC_SD_SUMMARY.md](PLC_SIMATIC_SD_SUMMARY.md)) |
| `update_type_content` | `typePath`, `content`, `expectedContentHash` | `format` (`source` by default) |
| `create_block` | `blockPath`, `blockType` | `language`, `obEventClass` |
| `delete_block` | `blockPath` | — |
| `create_block_group` / `delete_block_group` | `blockPath` | — |
| `create_tag_table` / `delete_tag_table` | `tableName` | `plcName`, `folderPath` |
| `create_tag` | `tableName`, `name`, `dataType` | `plcName`, `folderPath`, `logicalAddress`, `comment`+`commentCulture` |
| `update_tag` | `tableName`, `name` | `plcName`, `folderPath`, `newName`, `dataType`, `logicalAddress`, `externalAccessible`, `externalVisible`, `externalWritable`, `comment`+`commentCulture` |
| `delete_tag` | `tableName`, `name` | `plcName`, `folderPath` |
| `move_tag` | `tableName`, `name`, `targetTableName` | `plcName`, `folderPath`, `targetFolderPath` |
| `create_user_constant` | `tableName`, `name`, `dataType`, `value` | `plcName`, `folderPath` |
| `update_user_constant` | `tableName`, `name` | `plcName`, `folderPath`, `dataType`, `value` |
| `delete_user_constant` | `tableName`, `name` | `plcName`, `folderPath` |
| `add_interface_member` | `memberPath`, `dataType`, `blockPath`+`section` or `typePath` | `startValue`, `comment`+`commentCulture`, `insertBefore`, `expectedContentHash` |
| `update_interface_member` | `memberPath`, `blockPath`+`section` or `typePath` | `newName`, `dataType`, `startValue`, `clearStartValue`, `comment`+`commentCulture`, `expectedContentHash` (at least one change) |
| `delete_interface_member` | `memberPath`, `blockPath`+`section` or `typePath` | `expectedContentHash` |
| `update_db_member` | `blockPath`, `memberPath` | `startValue`, `externalAccessible`, `externalVisible`, `externalWritable`, `retain`, `setPoint` (at least one) |
| `create_instance_db` | `blockPath` (the new DB), `instanceOfName` | `number` |
| `rename_block` | `blockPath`, `newName` | — |
| `renumber_block` | `blockPath` | exactly one of `number` or `autoNumber:true` |
| `copy_block` | `blockPath`, `newName` | `targetGroupPath`, `number` |
| `move_block` | `blockPath`, `targetGroupPath` | — |
| `move_tag_table` | `tableName`, `targetFolderPath` | `plcName`, `folderPath` |
| `import_external_source`, `generate_blocks_from_source`, `delete_external_source`, `export_source_file` | see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#external-sources) | external sources (not live-tested) |
| `rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group` | `blockPath` / `typePath` / `folderPath` of the group, `newName` | `plcName`, `unitName` (tag-table groups); not live-tested |
| `create_type_group`, `delete_type_group`, `create_tag_table_group`, `delete_tag_table_group` | `typePath` / `folderPath` of the group | `plcName`, `unitName` (tag-table groups); see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#creating-and-deleting-plc-data-type-groups-and-tag-table-groups); not live-tested |
| `create_software_unit`, `delete_software_unit`, `rename_software_unit` | `unitName` (rename: `newName`) | `plcName`; see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#software-units); not live-tested |
| `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv`, `export_blocks` | see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#file-exports) | file exports from existing reads (not live-tested) |
| `export_tag_table`, `import_tag_table` | `tableName` + `filePath` (export), `filePath` (import) | `plcName`, `folderPath`, `overwrite`, `tableName` (import); one tag table as a SimaticML file, see the [BetterTIA operations reference](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions); not live-tested |
| `export_opcua_xml` | `filePath` | `plcName`, `overwrite`; the OPC UA data of the PLC's CPU as XML through `Project.HwUtilities.Find("OPCUAExportProvider")` and `OpcUaExportProvider.Export(DeviceItem, FileInfo)` (Siemens V21 reference, `plc-blocks.md` §13), bound at runtime: a missing provider or member fails `api_member_missing` and nothing is written. The export goes to a temporary file, is staged next to the target, moved into place and re-hashed like `export_tag_table`; an existing file needs `overwrite:true` (info guard `plc_export_overwrites_file`). Verification `trusted` (no read-back). TIA Portal is reported to need the PLC offline (heilingbrunner/tiaportal-mcp, not verified). Idea from heilingbrunner/tiaportal-mcp (MIT); not live-tested |

`move_tag` is a guarded re-create, not a native move (Openness V21 documents none): a temporary
tag is created in the target table with the source's data type, address, external-access flags
and multilingual comment, the source is deleted, the copy takes the original name and everything is
read back. Other attributes are not copied; the info guard `plc_move_tag_recreates` states this
before execution. The steps run in one ExclusiveAccess transaction that a failure rolls back (see
[Granular block editing](#db-values-instance-dbs-rename-renumber-copy-move)). See
[HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#plc_write-move_tag); not live-tested.

`create_block` accepts `FB`, `FC`, `OB`, or `GlobalDB`. Applicable block types support `LAD`, `FBD`, `STL`, `SCL`, and `GRAPH`. `GRAPH` is not supported for `OB`. `STL` FB, FC and OB creation works.

OB creation takes `obEventClass`, a closed set of 15 names, omitted meaning `ProgramCycle`:
`ProgramCycle`, `Startup`, `TimeOfDay`, `TimeDelayInterrupt`, `CyclicInterrupt`,
`HardwareInterrupt`, `Status`, `Update`, `Profile`, `TimeErrorInterrupt`, `DiagnosticErrorInterrupt`,
`PullOrPlugOfModules`, `RackOrStationFailure`, `ProgrammingError` and `IOAccessError`. The server
picks the OB number: a singleton class gets its base number; a multi-instance class gets its base
number if no OB in the PLC holds it, otherwise the lowest free number from 123 to 32767 (only OB
numbers count, including OBs in software units and system groups). `dryRun` shows the planned
`number` and `obEventClass` in the effect. Each applied `create_block` result carries `number` (the
created block's number; null for other block operations) and `obEventClass` (null for non-OB), both
always written. An unknown `obEventClass` is `validation_error` listing the valid names; so is
`obEventClass` with a non-OB `blockType`. A class the CPU does not support fails the item with TIA
Portal's own message (for example "Cannot create an organization block of type
'ProgrammingError'." on an S7-1200). The 2026-10-09
[live acceptance report](../superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md)
records the V21 run.

`create_block`, `create_block_group` and `delete_block_group` take a deterministic group path or the
two-segment `PLC/Name` (the PLC root group); a one-segment path is `validation_error`.
`update_block_logic` and `delete_block` resolve the block uniquely across the PLC, so a missing block
is `target_not_found`. The PLC itself is resolved uniquely across every device, including grouped
devices; several matches are `target_ambiguous`.

### Content writes

`update_block_logic` and `update_type_content` take the whole document as `content`, together with
`expectedContentHash`: the `contentHash` from a `plc_read` of the same object in the same format.
If the object changed since that read, the call fails with `state_changed` before anything runs; a
change between planning and import fails that item with `state_changed`. A document over the value
budget (60,000 characters by default) is omitted from `plc_read`, has no hash, and cannot be updated through
`plc_write`. A source declaring more than one object (a `withDependencies` read) is refused.
Block updates import into the existing block, compile the affected PLC scope, and verify the result
by re-export (the verification `contentHash`, below); type updates require a declaration whose name matches the target. Import/update never
creates, renames, deletes, or upserts the addressed object. A preview can carry a bounded
`contentDiff` of current versus requested text (see the
[import/export options](IMPORT_EXPORT_OPTIONS_SUMMARY.md)). A failed `update_block_logic` import
reports `blockImportOutcome` on the item's failure; that member is conditional on every structured tool's
failure schema.

### Guards

Guards are evaluated for every item before the first mutation. Block guards stop the call in every
mode (a dry run lists them and still returns `preview`); info guards become warnings. Guards cover
consequence and unreadable-evidence checks, plus the retained singleton-OB check. Name collisions, existing blocks, existing groups and
tables, and the default tag table are not guarded: planning does not check creates. Scoped live cases show native
rejections, with the exact namespace directions and limits below (see [Failures from TIA Portal](#failures-from-tia-portal)).

| Guard | Severity | Fires when |
|---|---|---|
| `plc_state_unverifiable` | info | The PLC inventory is incomplete, the update or delete target or what a delete removes is unreadable, the user-constant value an update replaces is unreadable, or the content cannot be exported. Unreadable evidence is never treated as empty; since 2026-11-01 it informs and the item runs, TIA Portal decides (owner decision: never stricter than TIA Portal). |
| `plc_ob_singleton_exists` | block | `create_block` creates an OB of a singleton event class and the PLC already holds that class's OB number, or an earlier item in the same call creates it. The message names the class, the number and the existing block. |
| `plc_deletes_block` | info | `delete_block` removes the block and its content. |
| `plc_deletes_group_contents` | info | `delete_block_group` removes every block and group inside it; the effect lists them. `delete_type_group`, `delete_tag_table_group` and `delete_software_unit` fire it too when the group or unit is not empty (see the [sources and folders reference](PLC_SOURCES_FOLDERS_SUMMARY.md)). |
| `plc_deletes_table_contents` | info | `delete_tag_table` removes its tags and user constants. |
| `plc_address_overlap` | info | The requested logical address is already used by another tag; TIA Portal allows the overlap. |
| `plc_changes_interface` | info | An interface member edit re-imports the block or PLC data type; instance DBs, multi-instances and callers stay inconsistent until compiled, and Portal may refuse a structural change of an FB that has instances. A rename or delete is not a refactoring: references to the member in the block's code and in other blocks are not updated. |
| `plc_block_round_trip` | info | `copy_block`/`move_block` import the SimaticML export inside one ExclusiveAccess transaction that a failure rolls back: a new object identity and no library-type link; should the rollback not restore a moved block it is re-imported, and its export is kept in a recovery folder if that fails too. |
| `plc_moves_tag_table` | info | `move_tag_table` exports the table, then deletes and imports it into the target folder inside one ExclusiveAccess transaction, with the same re-import and recovery-folder fallbacks; the effect counts its tags and user constants. |
| `plc_move_tag_recreates` | info | `move_tag` re-creates the tag in the target table inside one ExclusiveAccess transaction that a failure rolls back; attributes other than data type, address, external-access flags and comment are not copied. |
| `plc_generates_from_source` | info | `generate_blocks_from_source` creates or overwrites every block and PLC data type the external source declares; `None` deletes what it generated on an error, `KeepOnError` keeps objects with errors ([sources and folders reference](PLC_SOURCES_FOLDERS_SUMMARY.md)). |
| `plc_deletes_external_source` | info | `delete_external_source` removes the source node only; blocks and PLC data types generated from it stay. |
| `plc_renames_group` | info | A group rename (`rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group`) or `rename_software_unit` changes the paths of everything inside; names and references between objects do not change. |
| `plc_export_overwrites_files` | info | A file export (`export_source_file`, the CSV exports, `export_blocks`) replaces existing files because `overwrite:true` was requested. |
| `plc_s7dcl_import_may_alter` | info | `update_block_logic` takes the SIMATIC SD route (a bundle without `.xml` but with `.s7dcl`) and the `.s7dcl` has a construct the V21 import is reported to alter (nested index in LAD/FBD, project type named like a system type, AT overlay), or the overlays could not be checked. See [V21 pitfalls](V21_PITFALLS.md#pitfall-1-simatic-sd-import). |
| `plc_textual_interface_overwrite_may_terminate` | info | `update_block_logic` or an interface member edit overwrites a block with a textual interface; TIA Portal V21 is reported to terminate on such an overwrite after more than one export. The message gives the worker's export count. See [V21 pitfalls](V21_PITFALLS.md#pitfall-2-textual-interface-overwrite). |
| `plc_save_before_write_recommended` | info | Fires with `plc_textual_interface_overwrite_may_terminate`: save the project first, a terminated TIA Portal loses unsaved changes. |

`plc_ob_singleton_exists` stayed because Portal's own rejection of a duplicate singleton OB was never
observed: `create_block` picks the OB number itself, and an `update_block_logic` that declares a
duplicate number or event class is refused before any import, so no `plc_write` input reaches Portal
with one. The second `TimeErrorInterrupt` OB was blocked by the guard, also within one call.

The guards `plc_name_collision`, `plc_block_exists`, `plc_default_tag_table` and
`plc_attribute_unreadable` were removed with write contract `2.0`. Deleting the default tag table
is now Portal's rejection: `engineering_rejected` with "The operation is not valid because of the
current status of object" (the message does not say "default"). An unreadable external-access flag
on `update_tag` is no longer a guard; the request reaches Portal.

### Failures from TIA Portal

When Portal itself rejects a mutation, the item fails with a typed category and the Openness
exception, never a message parsed by the server:

- `engineering_rejected`: the worker method was classified as a mutation and Openness threw
  `EngineeringTargetInvocationException` (a duplicate tag, table, block or group; a delete Portal
  refuses). Classification covers the whole method, including connection, identity and target checks before a native write; it does not prove a setter or create was reached. The failure carries `engineeringError`.
- `worker_operation_failed`: any other Siemens `EngineeringException`. It also carries
  `engineeringError` when the exception reached the worker's shared catch.
- Reads never produce `engineering_rejected`. A Siemens exception during a planning read keeps its
  read category (`worker_operation_failed`, `target_not_found`, and so on) at the call level.
- `portal_lost` wins over both: if the Portal attachment was disposed during the item, the category
  is `portal_lost` with no `engineeringError`, and the binding and cursors are invalidated.

`engineeringError` is `{ "type", "message", "inner": [ { "type", "message" } ] }`: the exception type
name, its message verbatim, and the inner-exception chain outermost first (`inner` is always an
array, empty when there is no inner exception). The contract bounds a message at 4,000 characters
and the chain at 8 entries; longer text is cut without a marker. The member is conditional: absent
on failures that did not come from Siemens and on host-side rejections. Messages are in the Portal
UI language, so callers must not parse them; the live run used an English UI. Captured example,
`create_tag` with a name the table already holds (item 1 of 1; the call is `phase:"applied"`,
`success:false`, `error:null`, `isError:false`):

```json
{
  "operationId": "l1-01",
  "operation": "create_tag",
  "status": "failed",
  "failure": {
    "category": "engineering_rejected",
    "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
    "engineeringError": {
      "inner": [],
      "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
      "type": "EngineeringTargetInvocationException"
    }
  }
}
```

Other live captures have the same shape and `inner:[]`: a duplicate tag table
(`PlcTagTableComposition`), a duplicate block group (`PlcBlockUserGroupComposition`), a duplicate
block (`create_block` is an XML import, so the message mentions `Import` of
`PlcBlockComposition`, a line and a position) and deleting the default tag table. When item N of M
is rejected, items before N are `applied`, item N is `engineering_rejected`, later items are
`skipped` with `earlierOperationFailed`, and nothing is rolled back. See the
[live cases](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-26-plc-write-native-rejections-live-cases.md).

[Additional probes](../superpowers/acceptance/reports/inputs/2026-10-26-plc-write-collision-namespace-probes.md) demonstrated four more native rejection directions on one disposable project: creating a tag in a second table against an existing tag name, creating a user constant against that tag name, creating a tag against an existing FC name, and creating a table in an existing child folder against an existing root-table name. The reverse cross-kind directions, cross-group block duplicates and arbitrary folder layouts remain unverified. The source tag and empty tables were checked after the probes; the FC remained readable, without a before/after hash comparison. Cleanup restored the observed inventories and typed tree; unsaved edit history remained.

Portal does not throw for every collision. A rename through `update_tag` to a name another tag in
the table holds is silently ignored. The worker applies the rename first, reads `Name` back and, if
Portal kept the old name, fails the item `postcondition_failed` before any other property is
applied ("No other property was changed"). Review identified a separate unsupported-flag risk, not exercised live: an
`update_tag` that requests an external-access flag the tag cannot take fails mid-update, after a
rename or data-type change in the same item has already applied; there is no rollback.

### Execution, verification and audit

Binding-gate failures preserve their original category, including `worker_timeout`, `worker_crashed`,
`portal_lost` and `protocol_error`, in the top-level error and audit. A gate denial stops before planning
or mutation; a missing, mismatched or changed binding is `binding_conflict`.

Items run sequentially. Later items may use objects created earlier in the same call; they carry
`dependsOn` and are re-planned just before their own mutation. Creates are not pre-checked, so a
create into a missing parent block group or tag-table folder fails at mutation (Portal or the
worker rejects the item) instead of in pre-flight, and a later delete of an object that was never
placed fails the whole call as `target_not_found`. Update and delete targets are still resolved
before the first mutation, so a mistyped path fails the whole call `target_not_found` before
anything runs. Every planning refusal works this way (also an edit that changes nothing, such as a
`replace_tag_comments` without a match): planning checks all items before the first one runs, so
the refusal of one item rejects the whole call, and in a call of several items its message says that
no item ran (defect K25, live V21 2026-11-02; behaviour kept, message fixed). The first failure stops the call and later items are `skipped`. There is no
rollback or automatic replay: re-read affected objects with `plc_read` before retrying. Phases are
`preview`, `applied`, `blocked` and `error`; `isError` is true only for a rejection before anything
ran, and a call that ran and failed returns `success:false` with `error:null`. Budgets are 60,000
characters per value and 180,000 per document by default; whole values are omitted, never cut. Every
entered call appends one audit v2 record to `writes-yyyy-MM-dd.jsonl`, with the actual item
statuses and confirmation `none` in read-write and `policy` in full.

Verification (write contract `2.0`; `plc_read` stays `1.0`) reads back only content:

- `trusted`: a completed non-content item without a read-back. The Openness call returned without rejection and nothing
  was read back. The `exists`, `absent`, `values` and `observed` checks of contract `1.0` are gone.
- `tag`: `create_tag`, `delete_tag`, `move_tag` and an `update_tag` that wrote a comment re-read the tag table
  (`list_tag_tables`): the tag exists (or is gone, for a delete; a move also compares the data type and address the
  worker reported) and, when a comment was written, `read_tag_comments` shows its text in that culture (defects K22,
  K24, 2026-11-02; not live-tested).
- `block` for `delete_block`: the deleted path is re-read with `get_block_interface`, which must answer
  `target_not_found`. Block names are unique per PLC, so a later item that creates the same name in any group
  supersedes the delete (the K19 rule for blocks).
- `readBack`: `set_plc_access_level` whose worker read `PlcProtectionAccessLevel` back and found the requested level
  (`readBack:"matched"`); `expected` is the requested and `actual` the read-back level. An unreadable level stays
  `trusted` (nothing could be read).
- `unverifiable` (success true): a completed write whose own worker read-back matched (`update_db_member`,
  `rename_block`, `renumber_block`, `copy_block`, `move_block`, `create_instance_db`) but whose host re-read TIA Portal
  refused because the object is inconsistent until compiled (defect K01/K20); `expected`/`actual` are the request and
  the worker's read-back, and the message carries the compile hint below. Other read-backs refused that way (a content
  re-export, an interface re-read) stay failed entries with the hint.
- `contentHash`: a completed `update_block_logic`/`update_type_content` item is re-exported and its
  hash reported (`contentHash` and `actual`). A failed content item also reports the current hash,
  since a failed import may have committed.
- `superseded`: an item whose object a later item changed or may have changed; only a later item with a read-back (a content
  hash or a re-read) verifies the final state. A later trusted item says the object's final state is not verified (inspect it
  with `plc_read`); the source, folder and unit operations name what they mean for it instead (see
  [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#planning-guards-and-verification)). A short-path (`PLC/Name`) `update_block_logic` target is not resolved through the tree, so
  conservatively any later block item of the same name, or any `delete_block_group` on that PLC,
  supersedes it, and it gets no content check. If the later item failed without verification, final state is uncertain; inspect it with `plc_read`.
  Member edits supersede per member (since 2026-11-02, defect K03: live report of 2026-11-01, two interface edits on different members of one
  block marked the first `superseded` although both members were right): `add_interface_member`,
  `update_interface_member` (old and new name), `delete_interface_member` and `update_db_member` are superseded only by
  a later write of the same or an enclosing member, or by a later whole-object item (content update, document or network
  edit, rename, delete, group delete); a later interface edit still supersedes earlier whole-object items of its block or
  type, a later `update_db_member` does not. An edit of another member gets its own read-back in the final read
  (`interfaceMember` or `dbMember`, each with the block's final hash). Offline-tested only; not live-tested.
- A failed non-content item has no verification entry (nothing to verify). Inspect the object with
  `plc_read` before retrying.
- `interfaceMember`, `dbMember`, `block` and `tagTable`: the granular block edits re-read the edited
  member, DB value, resulting block or tag tables and compare them with the request (`expected` and
  `actual` describe both; `contentHash` carries the re-read's hash where one exists). A failed
  granular item reports the observed state. See [Granular block editing](#granular-block-editing).

Inconsistent objects (defect K20, live V21 2026-11-01/02: "Inconsistent blocks and PLC data types (UDT) cannot be
exported" after a Retain change, a renumber or a technology object change): the server never compiles on its own.
When an export of a block (any format, the SimaticML read behind `get_block_interface`, `get_db_values` and the
`copy_block`/`move_block` round trips) or of a technology object throws and the object's documented
`PlcBlock.IsConsistent` reads false, the worker appends a hint starting "TIA Portal exports only consistent objects:"
that names `compile_check` with `blockPath` (blocks) or `plcName` (technology objects) to the failure, or adds it as a
warning when the request still succeeded. The hint comes from that typed evidence, never from the Siemens message,
which follows the Portal UI language. A `move_block` after a `renumber_block` of the same block in one call (D2/K02)
still fails: the export is the move's own first step, not a planning or verification read; its failure now carries the
hint. PLC data types are not covered (their consistency attribute is not checked). Not live-tested.

The verification `contentHash` equals the hash the next `plc_read` of that object returns and can be
used as the next `expectedContentHash`. It cannot be computed from the submitted content: the export
carries a generated id, and four identical `update_block_logic` writes gave four different hashes.
Take it from the verification or a fresh read.

### Known limits

- The address-overlap check is an exact string match (`%MW0` and `%M0.0` are not detected as
  overlapping), at info severity.
- Two content updates of the same block in one call cannot both plan.
- Tags created earlier in the same call are assumed to have all external-access flags true for
  planning.
- With an incomplete inventory, a selector miss is `plc_state_unverifiable` (block), not
  `target_not_found`.
- Duplicate tag-table names were tested within one folder and, in an additional probe, child-folder creation against an existing root name. `plc_write` cannot create
  tag-table folders; arbitrary pairs of child folders were not exercised.
- Review identified, without live exercise, that an `update_tag` requesting an unsupported external-access flag can fail after a rename or
  data-type change in the same item has applied (no rollback).
- Portal's own rejection of a duplicate singleton OB was never observed (see the guard note above).
- Content importers (`update_block_logic`, `update_type_content`) still flatten import failures to
  `worker_operation_failed` without `engineeringError`; see the
  [improvement log](../IMPROVEMENT_LOG.md).
- Created OBs use TIA Portal defaults: no event parameters (cyclic time, phase offset, priority,
  time-of-day schedule, hardware-interrupt triggers) and no renumbering of existing blocks. See the
  [improvement log](../IMPROVEMENT_LOG.md).
- `SynchronousCycle` is not offered: it does not compile until an isochronous IO system is assigned,
  which no tool here configures. Motion Control, redundancy and ProDiag OBs are created by TIA Portal.
- A multi-instance OB number can differ from the plan if another OB takes it in between; the result
  `number` is authoritative. A singleton number taken in between fails the item with `state_changed`.

## Offline change detection (`plc_read`)

> **Not live-tested** (GitHub issue #6). Evidence: the V21 IntelliSense file `Siemens.Engineering.Step7.xml`, members
> `P:Siemens.Engineering.SW.PlcChecksumProvider.Software` ("Software checksum"), `P:...PlcChecksumProvider.TextLists`
> ("Text lists checksum"), `M:Siemens.Engineering.SW.FingerprintProvider.GetFingerprints` ("Read Fingerprint", returns
> `IList<Siemens.Engineering.SW.Fingerprint>`), `P:Siemens.Engineering.SW.Fingerprint.Id` and `.Value`.

Two `plc_read` operations (every mode; `Observe`; standalone projects and local sessions). Names in
`PlcChangeDetectionMethods` (Contracts `Plc/ChangeDetection`), parameters in `WorkerRequest.PlcChangeDetection`, dispatch
`Program.PlcChangeDetection.cs`, worker `Openness/Plc/ChangeDetection/PlcChangeDetectionService.cs`, host
`TiaMcpServer/Plc/ChangeDetection/`, FakeWorker scenario `plc-change-detection`.

- **`get_plc_checksums`** (optional `plcName`; omitted reads every PLC): per PLC `{ plcName, deviceName, available,
  unavailable, software, textLists }`. The XML does not say which object offers `PlcChecksumProvider`; the worker takes it from
  the `PlcSoftware`'s `GetServiceInfos()` by exact full name and retrieves it with `IServiceProvider.GetService(Type)`. Not
  listed, or listed but not returned, is `available: false` with the reason in `unavailable` (the other PLCs are still read).
  The XML gives no type for the two checksums, so each is projected as text: `{ state, value, runtimeType, error }` with
  `state` `value`, `empty`, `null`, `unreadable` (a recoverable Siemens exception, named in `error`) or `missing` (no readable
  property of that name on the live provider). Byte sequences are written as hexadecimal, other values invariantly. An empty
  or null checksum means, per the Siemens TIA Portal V21 skill change-detection (not the XML), that the software was not
  compiled or was changed since the last compile; the result's `note` says so.
- **`get_fingerprints`** (exactly one of `blockPath` or `typePath`): `FingerprintProvider.GetFingerprints()` of that block or
  PLC data type, the provider found among the object's `GetServiceInfos()` the same way. Not listed or not returned is
  `unsupported_capability`; a provider without `GetFingerprints()` is `api_member_missing`; a null or non-collection return is
  `worker_operation_failed`. The result `{ objectKind, path, plcName, collectionType, fingerprints[] }` holds every
  `Fingerprint`'s `id` and `value` in the same text shape (the XML lists the `FingerprintId` fields `Code`, `Comments`,
  `Interface`, `LibraryType`, `Texts`, `Alarms`, `Supervisions`, `TechnologyObject`, `Events`, `TextualInterface`,
  `Properties`, `ProgramCode`, but the type of `Fingerprint.Id` is not stated, so nothing is assumed).
- **No stub member** was added: every member is bound at runtime by the Siemens-free `PlcChangeDetectionRun` and
  `RuntimeServiceValues` (Contracts `Shared`), shared with the FakeWorker.
- **Left out:** the online `Siemens.Engineering.FingerprintData.FingerprintDataProvider`
  (`GetFingerprintData(ConfigurationAddress, OnlineConfigurationDelegate)`, `Configuration`): it reads fingerprint data from
  a device online (a connection configuration and online callbacks), which these offline reads do not do.
- **Live acceptance still required:** which objects list the two providers, the runtime types of the checksums, `Id` and
  `Value`, and whether the checksums are empty after a change and filled after `compile_check`.

## Loadable files (`plc_write`)

> **Not live-tested** (GitHub issue #26). Evidence: the V21 IntelliSense file `Siemens.Engineering.Step7.xml`,
> `M:Siemens.Engineering.SW.Loader.LoadableProvider.GenerateLoadable(System.IO.FileInfo,System.Collections.Generic.IEnumerable{Siemens.Engineering.SW.Blocks.PlcBlock},Siemens.Engineering.SW.Loader.TargetOption)`
> ("Generates a loadable file for blocks in the non unit program"), the overload with `IEnumerable{Siemens.Engineering.SW.Units.PlcUnit}`
> ("Generates a loadable file for software units") and `T:Siemens.Engineering.SW.Loader.TargetOption` with `None` ("Throws an
> exception"), `Plc` and `PlcSim`.

`plc_write` operation `generate_loadable` (read-write and full; `ProjectMutation`, it writes a caller file; no prompt) takes
`filePath`, `target` (`Plc` or `PlcSim`; `None` is documented to throw and is not offered), exactly one of `blockPaths` (1-200
blocks of the non-unit program of one PLC) or `unitNames` (1-200 software units), optional `plcName` (default: the PLC the
block paths name, else the only PLC) and `overwrite`. Names in `PlcLoadableMethods` (Contracts `Plc/Loadable`), parameters in
`WorkerRequest.PlcLoadable`, dispatch `Program.PlcLoadable.cs`, worker `Openness/Plc/Loadable/PlcLoadableService.cs`, host
`TiaMcpServer/Plc/Loadable/` (test glob), FakeWorker `plc-write-roundtrip` (PLC_2 offers no provider).

- **Provider and binding.** The XML does not say which object offers `LoadableProvider`; the worker takes it from the
  `PlcSoftware`'s `GetServiceInfos()` by exact full name (not listed or not returned: `unsupported_capability`). The blocks
  (`BlockTargetResolver`) or software units are resolved first (`target_not_found`); then the Siemens-free `PlcLoadableRun`
  (Contracts) binds the one `GenerateLoadable` overload with parameters `FileInfo`, `IEnumerable<T>` of exactly the element's
  full name and the enum of exactly the `TargetOption` full name, and parses `target` by member name; anything missing is
  `api_member_missing`, nothing written. No stub member was added. The return value is undocumented and ignored (its type is
  named in `result.signature`).
- **File.** The caller-file rules of the alarm exports (`PlcAlarmFiles`): absolute drive or UNC path checked lexically, an
  existing file only with `overwrite:true` (info guard `plc_export_overwrites_file` when it is set) and never through a link,
  the call writes into a temporary file next to the target, which is then moved into place and re-hashed. A call that leaves
  no file at the path it was given fails `postcondition_failed`. No extension is required: none is documented.
- **Result and verification.** `{ plcName, overload (blocks|units), objects[], target, signature, filePath, overwritten, bytes,
  sha256, messages }`; verification check `file` reports the worker's re-hash.
- **Live acceptance still required:** that the PlcSoftware lists the provider, what the call writes (one file, its
  extension), whether it needs a compiled program, and that the file loads into a PLC or PLCSIM.

## Block know-how and write protection (`plc_read`/`plc_write`)

> **Not live-tested** (GitHub issue #5). Evidence: the V21 IntelliSense files `Siemens.Engineering.Step7.xml`
> (`T:Siemens.Engineering.SW.Blocks.PlcBlockProtectionProvider` with `Protect(System.Security.SecureString)` "Sets protection for
> the underlying object" and `Unprotect(System.Security.SecureString)` "Removes protection";
> `T:Siemens.Engineering.SW.Blocks.PlcBlockWriteProtectionProvider` with `IsDefined` "Is password defined", `IsProtected` "Is
> write protection enabled", `Define(SecureString)`, `Protect(SecureString)`, `Unprotect(SecureString)`,
> `Change(SecureString, SecureString)`; `P:Siemens.Engineering.SW.Blocks.PlcBlock.IsKnowHowProtected`) and
> `Siemens.Engineering.Base.xml` (`ProtectionProviderBase.GetInvalidPasswordCharacters()`), plus the Siemens blocks skill
> ("Protect Block with Password": `block.GetService<PlcBlockProtectionProvider>()`, check the invalid characters before `Protect`).

Operations (names in `PlcBlockProtectionMethods`, Contracts `Plc/BlockProtection`; parameters `WorkerRequest.PlcBlockProtection`;
dispatch `Program.PlcBlockProtection.cs`; worker `Openness/Plc/BlockProtection/PlcBlockProtectionService.cs`; host
`TiaMcpServer/Plc/BlockProtection/` (test glob); FakeWorker `plc-write-roundtrip`):

| Operation | Tool, capability | Inputs | Siemens member | Read-back |
|---|---|---|---|---|
| `get_block_protection` | `plc_read`, `Observe` (every mode) | `blockPath` | `PlcBlock.IsKnowHowProtected`, `IsDefined`, `IsProtected`, `GetInvalidPasswordCharacters()` | – |
| `set_block_know_how_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockProtectionProvider.Protect(SecureString)` | `IsKnowHowProtected` = true |
| `remove_block_know_how_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` (current) | `PlcBlockProtectionProvider.Unprotect(SecureString)` | `IsKnowHowProtected` = false |
| `define_block_write_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockWriteProtectionProvider.Define(SecureString)` | `IsDefined` = true |
| `set_block_write_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockWriteProtectionProvider.Protect(SecureString)` | `IsProtected` = true |
| `remove_block_write_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockWriteProtectionProvider.Unprotect(SecureString)` | `IsProtected` = false |
| `change_block_write_protection_password` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` (current), `newPasswordVariable` | `PlcBlockWriteProtectionProvider.Change(SecureString, SecureString)` | `IsDefined` = true (the new password itself is not readable) |

- **Passwords.** Only password sources, never values: an environment variable NAME or `{ "credential": "TiaMcp/..." }`
  (`PasswordSources`, the same redaction at binding as every password input). An omitted `passwordVariable` comes from the
  server password map with purpose `KnowHowProtection` (know-how writes) or `BlockWriteProtection` (write-protection writes;
  for the change the current password) for the PLC the block path names; `newPasswordVariable` is never taken from the map.
  The password manager offers both purposes ("Know-how-Schutz (Bausteine)", "Schreibschutz (Bausteine)"). The map's block
  protection purposes are never offered as download or online password answers. Results and the audit carry only the source
  tokens (`result.passwordSources[] = { role, purpose, kind, source }`).
- **Binding.** No protection type is compiled against and no stub member was added. Both providers are found among the
  block's `GetServiceInfos()` by exact full name (the XML does not say which object offers the write-protection provider);
  not listed or not returned is `unsupported_capability`. Every member is bound on the live provider by exact name and
  `SecureString` parameters (Siemens-free `PlcBlockProtectionRun`, shared with the FakeWorker); missing is
  `api_member_missing`. Everything is bound before the first call, so a refusal changes nothing.
- **Invalid characters.** A password the call sets anew (the know-how password, the defined write-protection password, the new
  password of a change) is checked against the provider's own `GetInvalidPasswordCharacters()` first, as the Siemens skill
  recommends; a password containing one fails `validation_error` with nothing changed, and the message lists TIA Portal's
  characters but never which one the password holds. Without a bindable `GetInvalidPasswordCharacters()` the check is skipped
  with a message and TIA Portal decides.
- **Planning and guards.** One planning read (`get_block_protection`) shows the current state in the effect and tells whether
  the block is a fail-safe block (ProgrammingLanguage `F_...`). A missing or ambiguous block refuses the call before anything
  runs; an otherwise unreadable read informs (`plc_state_unverifiable`) and the item runs; when the PLC is an F-CPU or its
  safety administration cannot be read, the acceptance guard `plc_safety_acceptance_required` fires too (fail safe, security
  review 2026-11-03 F7; read-write confirms; no login block for a block of unknown kind). A fail-safe block follows TIA
  Portal's safety login rule: the acceptance guard, and the login block guard only when a safety password is set and the
  session is not logged on; the worker re-checks it before the change (`PlcSafetyGate`). Info guards
  `plc_sets_block_protection` (set, define, change: keep the source available) and `plc_lowers_block_protection` (remove).
- **Verification.** The worker reads the documented state property back right after the call; a readable value that differs
  fails `postcondition_failed`; a matched read-back is the item's verification (check `blockProtection`); an unreadable one is
  `readBack: unreadable` and stays `trusted`. A later item on the same block and the same protection supersedes an earlier one.
- **Left out.** No V21 member reads a password back or tells which password is set, so a change is verified only by `IsDefined`.
  Know-how protection of PLC data types: the reference lists `PlcType.IsKnowHowProtected` but no protection provider for types,
  so it is not offered. Online behaviour (download of protected blocks) is TIA Portal's.
- **Live acceptance still required:** which object lists `PlcBlockWriteProtectionProvider`, whether `Protect` needs a compiled
  or consistent block, what TIA Portal rejects (already protected, wrong password), and that a protected block still compiles
  and downloads.

## Project languages and project texts (`plc_read`/`plc_write`)

> **Not live-tested** (GitHub issue #8). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`:
> `P:Siemens.Engineering.ProjectBase.LanguageSettings`; `LanguageSettings.Languages` ("The collection of all supported
> languages"), `.ActiveLanguages`, `.EditingLanguage`, `.ReferenceLanguage`; `LanguageAssociation.Add(Language)`,
> `.Remove(Language)`, `.Find(CultureInfo)`; `LanguageComposition.Find(CultureInfo)`; `Language.Culture`;
> `P:Siemens.Engineering.ProjectBase.TextCategories`, `TextCategory.Identifier`/`.Name`, `TextCategoryComposition.Find(String)`;
> `M:Siemens.Engineering.ProjectBase.ExportProjectTexts(System.IO.FileInfo,System.Globalization.CultureInfo,System.Globalization.CultureInfo)`
> and `ExportProjectTexts(FileInfo, Language, IEnumerable{Language}, IEnumerable{TextCategory}, IEnumerable{IEngineeringObject})`
> ("Export project text to an xlsx file"); `M:Siemens.Engineering.ProjectBase.ImportProjectTexts(System.IO.FileInfo,System.Boolean)`
> returning `ProjectTextResult` (`Path`, `State`: `ProjectTextResultState` `Error`, `Info`, `Warning`).

Project-wide operations (no `plcName`); `LanguageSettings` and the text export/import are `ProjectBase` members, so they run on
standalone projects and local sessions alike (gated as `plc_read`/`plc_write`). Names in `ProjectLanguageMethods` (Contracts
`Project/ProjectLanguages.cs`), parameters `WorkerRequest.ProjectLanguages`, dispatch `Program.ProjectLanguages.cs`, worker
`Openness/Project/ProjectLanguagesService.cs`, host `TiaMcpServer/Plc/Project/` (test glob), FakeWorker `plc-write-roundtrip`
(`PlcWriteRoundtripScenario.ProjectLanguages.cs`, `ProjectLanguageDoubles.cs`). Every member is bound at runtime by the
Siemens-free `ProjectLanguagesRun`; a missing one is `api_member_missing` with nothing changed. No stub member was added.

| Operation | Tool, capability | Inputs | Behaviour |
|---|---|---|---|
| `get_project_languages` | `plc_read`, `Observe` (every mode) | none | `state { active[], editing, reference }` (culture names), `supported[]` (`LanguageSettings.Languages`), `textCategories[] { identifier, name }`, `notes` for unreadable parts. |
| `set_project_languages` | `plc_write`, `ProjectMutation` | at least one of `addLanguages`, `removeLanguages` (1-64 culture names each), `editingLanguage`, `referenceLanguage` | Every culture is resolved with `Languages.Find(CultureInfo)` first (unsupported: `target_not_found`, nothing changed); then `ActiveLanguages.Add` per addition, `ReferenceLanguage` and `EditingLanguage` are assigned, then `ActiveLanguages.Remove` per removal; read back (`postcondition_failed` on a mismatch); check `projectLanguages`. |
| `export_project_texts` | `plc_write`, `ProjectMutation` (writes a caller file) | `filePath` (`.xlsx`), `sourceLanguage`, `targetLanguages` (1-64); optional `textCategories`, `deviceNames`, `overwrite` | One target and neither filter: `ExportProjectTexts(FileInfo, CultureInfo, CultureInfo)`; otherwise the filtered overload with the named (or every) text category and the named (or every) device of the project (`Devices` and device groups). Staged next to the target, moved into place, re-hashed (check `file`). |
| `import_project_texts` | `plc_write`, `ProjectMutation` | `filePath` (`.xlsx`); optional `updateSourceLanguage` (default false) | `ImportProjectTexts(FileInfo, Boolean)` of a private copy; then a verification export (check `importedRows`). |

- **Editing and reference language.** The XML documents both properties but no setter; the worker assigns them only through a
  public writable property of exactly that name accepting the `Language` (else `api_member_missing`, nothing changed, since
  everything is bound before the first call). Order: additions, reference, editing, removals, so a call can switch the editing
  language away from a language it removes. TIA Portal decides whether a language in use can be removed and what happens to
  its texts. Info guard `plc_changes_project_languages`; the effect shows the current state from one planning read
  (`get_project_languages`; unreadable informs with `plc_state_unverifiable`). Texts in a language that the project does not
  have fail (live E4); add the language with `set_project_languages` first.
- **Export file.** The documented format is XLSX, so `filePath` must end in `.xlsx` (host and worker). The alarm exports'
  caller-file rules apply (`PlcAlarmFiles`: no overwrite without `overwrite:true`, info guard `plc_export_overwrites_file`,
  never through a link, a temporary file next to the target, re-hash). The export's return type is not documented: when it
  returns an object, its `State` and `Path` are read as text (`result.outcome`); `Error` fails, `Warning` is a message. Without
  `deviceNames` or `textCategories` the filtered overload gets every device and category (listed in the result) because the
  reference does not say what an empty collection means.
- **Import and verification.** The import reads a private copy of the file (`bytes`, `sha256` are that copy's); `State`
  `Error` or an unreadable state fails the item (the project may already have changed), `Warning` is a message, `Info`
  succeeds. A verification export follows (filtered overload: the editing language as source, every other active language as
  target, every category and device) and its rows are compared with the imported rows by header and value
  (`XlsxCellWorkbook.CompareRows`, extra columns allowed). Every row shown is `importCheck.outcome: verified` (check
  `importedRows`); anything else is `inconclusive` (or `notReadBack` without a second active language) with the reason in
  `result.messages`, and the item stays `trusted`: the export's scope is not documented to equal the imported file's, and with
  `updateSourceLanguage:false` the source-language column is not imported. Info guard `plc_imports_project_texts`.
- **TIA Portal notifications (K75).** Live 2026-11-03 TIA Portal showed a modal information message after the export
  ("Export erfolgreich abgeschlossen. (0032:000002)") and the Openness call waited until a human clicked OK. The worker now
  handles every `Notification` (`NotificationEventArgs.IsHandled = true`, for every operation, see ARCHITECTURE §4); the
  three project language writes list the ones handled during the call in `result.portalNotifications`
  (`{ caption, text, detailText, icon, handled }`), and each is also a warning. Not live-tested.
- **Left out.** Nothing of the documented surface. Not offered: reading or writing single multilingual texts (other operations
  already write comments per culture), and parsing the workbook layout (it is TIA Portal's and undocumented).
- **Live acceptance still required:** whether `EditingLanguage`/`ReferenceLanguage` are writable on V21, the workbook layout
  (header row), what the export returns, what an import with `updateSourceLanguage` false changes, and that the verification
  export compares by the same headers.

## OPC UA server interfaces and access rules (`plc_read`, `plc_write`)

> **Not live-tested** (GitHub issue #24). Evidence: the V21 IntelliSense file `Siemens.Engineering.Step7.xml`, namespace
> `Siemens.Engineering.SW.OpcUa` (`OpcUaProvider.CommunicationGroup`, `OpcUaCommunicationGroup.ServerInterfaceGroup`,
> `ServerInterfaceGroup.ServerInterfaces/SimaticInterfaces/ReferenceNamespaces/AccessControl`, the compositions' `Create`,
> `ServerInterface.Export/Import/Delete/Enabled`, `SimaticInterface.Export/Delete/Enabled/UseStringNodeIds`,
> `ReferenceNamespace.Export/Import/Delete/Enabled/GenerateNodes/GeneratedInterfaceName/UseStringNodeIds`,
> `AccessControl.NamespaceAccessRestrictions/RoleMappings`, `NamespaceAccessRestriction.*`, `RoleMappingComposition.Create(String, String)`,
> `.AddStandardOpcUaRole(String)`, `RoleMapping.Delete/SetRoleName/SetDefiningNamespace/SetProjectRole/NamespacePermissions`,
> `NamespacePermission.SetPermission(String, Boolean)` and its boolean properties).

The XML does not say which object offers `OpcUaProvider`; the worker takes it from the `PlcSoftware`'s `GetServiceInfos()` by
exact full name (not listed: the read reports `providerAvailable:false` with a note, a write fails `unsupported_capability`).
Every member below it is read or called at runtime by its documented name and parameter types; a missing one fails
`api_member_missing` before anything changes. No stub member was added. Names in `PlcOpcUaMethods` (Contracts `Plc/OpcUa`),
parameters in `WorkerRequest.PlcOpcUa`, dispatch `Program.PlcOpcUa.cs`, worker `Openness/Plc/OpcUa/PlcOpcUaService.cs`, host
`TiaMcpServer/Plc/OpcUa/` (test glob), FakeWorker `plc-write-roundtrip` (an in-memory model; PLC_2 offers no provider).

`plc_read` `get_opcua_configuration` (every mode; optional `plcName`, omitted reads every PLC) returns per PLC the server
interfaces (companion specifications and own interfaces), SIMATIC interfaces and reference namespaces (`name`, `enabled`,
`author`, `comment`, `creationTime`, `lastModified`, `useStringNodeIds`, `generateNodes`, `generatedInterfaceName`), the namespace
access restrictions (`namespaceUri`, `namespaceIndex`, `sessionRequired`, `signingRequired`, `encryptionRequired`,
`applyRestrictionsToBrowse`) and the role mappings (`name`, `definedInNamespace`, `projectRole`, `roleId`, `namespacePermissions`
with `browse`, `read`, `write`, `call`, `receiveEvents`, `readRolePermissions`); an unreadable member is null and listed in
`unreadable`, at most 1,000 entries per list.

`plc_write` operations (read-write and full; `ProjectMutation`, no prompt; every item takes optional `plcName`, default the only PLC):

| Operation | Fields | Siemens call | Read-back |
| --- | --- | --- | --- |
| `create_opcua_interface` | `interfaceKind` (`server`, `simatic`, `referenceNamespace`), `interfaceName`; `filePath` (required for `referenceNamespace`; for `server` an XML imported right after the create; refused for `simatic`) | `ServerInterfaceComposition.Create(String)`, `SimaticInterfaceComposition.Create(String)`, `ReferenceNamespaceComposition.Create(String, FileInfo)` | listed by name afterwards |
| `delete_opcua_interface` | `interfaceKind`, `interfaceName` | `Delete()` | no longer listed |
| `export_opcua_interface` | `interfaceKind`, `interfaceName`, `filePath`, `overwrite` | `Export(FileInfo)` into a staged file moved into place | re-hashed (`sha256`) |
| `import_opcua_interface` | `interfaceKind` (`server` or `referenceNamespace`), `interfaceName`, `filePath` | `Import(FileInfo)` of a private copy | a re-export compared byte for byte with the imported file: equal is verified, different (TIA Portal may normalize the XML) or failed is `notVerified` |
| `set_opcua_interface` | `interfaceKind`, `interfaceName`; `enabled`, `useStringNodeIds` (simatic, referenceNamespace), `generateNodes` (referenceNamespace), `newName` | `SetAttribute` after a `GetAttributeInfos` writability check (the XML does not say these properties are settable; TIA Portal decides through the access mode it reports) | `GetAttribute` per value |
| `set_opcua_namespace_restriction` | `namespaceUri`; `sessionRequired`, `signingRequired`, `encryptionRequired`, `applyRestrictionsToBrowse` | as above | as above |
| `create_opcua_role` | `roleName`, `definingNamespace` | `RoleMappingComposition.Create(String, String)` | listed by name |
| `add_opcua_standard_role` | `roleName` (a standard OPC UA role) | `RoleMappingComposition.AddStandardOpcUaRole(String)` | listed by name |
| `delete_opcua_role` | `roleName` | `RoleMapping.Delete()` | no longer listed |
| `set_opcua_role` | `roleName`; `newRoleName`, `definingNamespace`, `projectRole` | `SetDefiningNamespace`, `SetProjectRole`, `SetRoleName` (in this order) | `DefinedInNamespace`, `ProjectRole`, `Name` |
| `set_opcua_namespace_permission` | `roleName`, `namespaceUri`, `permission`, `enabled` | `NamespacePermission.SetPermission(String, Boolean)` | the property of that name when it is one of `Browse`, `Read`, `Write`, `Call`, `ReceiveEvents`, `ReadRolePermissions`; another name is passed through (the names `SetPermission` accepts are not documented) and is `notVerified` |

Guards (info): `plc_opcua_changes_server` on every change (it reaches the CPU with the next download; one guard per id, the
messages of an item joined, so `import_opcua_interface` keeps its own note, security review F9),
`plc_opcua_lowers_access` when a restriction flag is set to `false` or a permission is granted, `plc_export_overwrites_file`
on an export with `overwrite:true`. Result `{ operation, plcName, target, calls[], interfaceAfter, restrictionAfter, roleAfter,
filePath, bytes, sha256, reExportSha256, evidence, note }`; verification check `opcUa` (`verified`, `deleted` and `file` are
success, `notReadBack` is success false). Supersession: per interface, restriction, role (and permission) and export file.

**Left out:** `OpcUaProvider` is found only on the PlcSoftware (where else it hangs is not documented); `Author`/`Comment` of
interfaces are read only (not documented as settable); a SIMATIC interface has no documented `Import`. **Live acceptance still
required:** that the PlcSoftware lists the provider, which attributes `GetAttributeInfos` reports writable, the permission names
`SetPermission` takes, and whether an import re-exports byte-identically.

## Download to a folder and the online master secret (`plc_write`)

> **Not live-tested** (GitHub issue #16). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`:
> `DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)` and `RHDownloadProvider.Download(DirectoryInfo,
> DownloadConfigurationDelegate)` ("Downloads hardware and software into a folder"), `OnlineProvider.SetPlcMasterSecret(SecureString)`,
> `OnlineProvider.ResetPlcMasterSecret()`, `OnlineProvider.State`; `RHDownloadProvider.DownloadToPrimary/DownloadToBackup(IConfiguration,
> DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)` for `plc_download rhTarget`.

Names in `PlcDeviceDataMethods` (Contracts `Plc/DeviceData`), parameters in `WorkerRequest.PlcDeviceData`, dispatch
`Program.PlcDeviceData.cs`, worker `Openness/Plc/DeviceData/PlcDeviceDataService.cs`, host `TiaMcpServer/Plc/DeviceData/` (test glob),
FakeWorker `plc-write-roundtrip` (an in-memory model: `C:\Download` exists, `C:\DownloadFull` is not empty, `C:\DownloadDialog` raises one TIA Portal dialog, PLC_1 is online and has an R/H provider, PLC_2 neither).
`RHDownloadProvider` is found among the CPU device item's `GetServiceInfos()` by exact full name; members not on the stubs are bound
at runtime (`api_member_missing` before any call).

| Operation | Fields | Capability, guard | Siemens call | Verification |
| --- | --- | --- | --- | --- |
| `download_to_folder` | `directoryPath` (an existing absolute directory; one that is not empty is refused unless `overwrite:true`); optional `plcName`, `rhSystem` (true: the R/H system's `RHDownloadProvider`), `overwrite`, `portalConfirmations` | `ProjectMutation`, info guard `plc_download_to_folder_writes_files` (with `overwrite:true` also `plc_export_overwrites_file`); no device is touched; the directory passes `PlcFileTargets.InspectDirectory` (links, junctions, network locations reported) | `Download(DirectoryInfo, DownloadConfigurationDelegate)`; download configurations get the built-in `plc_download` answers through the same recorder, any other one aborts (`worker_operation_failed` with the abort reason) | `deviceData`: the files the call wrote (last write time since the call), re-hashed (at most 1,000); none is `postcondition_failed` |
| `set_plc_master_secret_online` | `passwordVariable` (an environment variable NAME or `{ "credential": "TiaMcp/..." }`, never the secret); optional `plcName`, `portalConfirmations` | `OnlineControl` (read-write and full), acknowledge guard `plc_master_secret_online` | `OnlineProvider.SetPlcMasterSecret(SecureString)` only while `OnlineProvider.State` reads `Online` (connect first with `plc_tia_online go_online`; otherwise `state_changed`, nothing changed) | `deviceData` notVerified (success false): no documented member reads the master secret back |
| `reset_plc_master_secret_online` | optional `plcName`, `portalConfirmations` | as above | `OnlineProvider.ResetPlcMasterSecret()` under the same online check | as above |
| `load_snapshot_as_actual_values`, `load_start_values_as_actual_values` (package G) | `blockPath` of a DB; optional `portalConfirmations` | `OnlineControl`, acknowledge guard `plc_loads_actual_values_online` | `ValueService.LoadSnapshotAsActualValues()` / `LoadStartValuesAsActualValues()` of the DB interface under the same online check | notVerified; see the [V21 API completion reference](V21_API_COMPLETION_SUMMARY.md#actual-values-of-a-running-plc-plc_write) |

The acknowledge guard makes `plc_write` confirm every call with an online master secret item (`ConfirmsEveryCall`; read-write asks
the human with a prompt naming the PLC and the password source, full runs it under policy). The secret is read by the worker from
the password source and passed as a `SecureString`; it never appears in the call, the result or the audit.

**TIA Portal dialogs (security review 2026-11-03, F6; not live-tested).** All three operations run inside
`TiaPortalSession.AnswerConfirmations` like `plc_download`: every TIA Portal `Confirmation` raised until the evidence is collected
is answered by the item's `portalConfirmations` (`decline`, the default: No, recorded, the item fails; `accept`: Yes, recorded, a
warning) and listed in the result (`portalConfirmations`, `portalConfirmationsObserved`); a dialog raised after the scope closed is
reported on the next response.

**Already covered by `plc_download`:** user management data during a download is the download configuration `UserManagementDownload`
(selections `KeepOnlineUserManagementData`, `UpdateUserManagementDataButKeepOnlinePassword`, `DownloadAllUserManagementDataResetToProject`):
answer it with `decisions: { "UserManagementDownload": "..." }`; the master secret password asked during a download is the password
configuration `PlcMasterSecretPassword` (`passwords`). An R/H system downloads to its primary or backup CPU with `plc_download
rhTarget: "primary" | "backup"` ([PLC download](PLC_DOWNLOAD_SUMMARY.md)). The offline master secret configuration stays with the
`*_plc_confidential_data_*` operations.

**Left out:** downloading with VoT (no V21 member was found in the IntelliSense files); setting an R/H system online
(`RHOnlineProvider.GoOnlineToPrimary/GoOnlineToBackup`; `plc_tia_online` keeps the PLC's `OnlineProvider`); the download overloads
with a `ConfigurationAddress`; caller `decisions` for `download_to_folder`; the F-program download, which TIA Portal V21 refuses through
Openness (K33), is not worked around. **Live acceptance still required:** which configurations a folder download raises, which files it
writes, whether the CPU device item lists `RHDownloadProvider`, and how the online master secret calls behave.

## Project block-compilation settings

> **Not live-tested** (K10, 2026-11-02). Siemens' PLCSIM Advanced getting-started asks for "Simulation während
> Bausteinübersetzung unterstützen" before a Softbus download; `get_project_status` reported it `false` for
> `MCP_Safety_01` (live 2026-11-02), and no tool could set it (see the
> [interim live report](../superpowers/acceptance/reports/2026-11-01-fork-live-acceptance-interim.md)).

`plc_write` operation `set_project_compilation_settings` (read-write and full; `ProjectMutation`; project-wide like the
alarm classes: no `plcName`, standalone projects only, `unsupported_capability` for a local session) takes
`simulationDuringBlockCompilation` and/or `virtualPlcDuringBlockCompilation` (`true`/`false`; at least one; omitted leaves a
setting unchanged). The worker method carries the public name (`ProjectCompilationSettingsMethods` in Contracts `Project`,
parameters in `WorkerRequest.ProjectCompilationSettings`, dispatch `Program.ProjectSettings.cs`, worker
`Openness/Project/ProjectCompilationSettingsService.cs`, host `TiaMcpServer/Plc/Project/`).

- **Mechanism.** The same providers `get_project_status` reads (`Project.GetService<PlcSimulationSettingsProvider>()` /
  `GetService<VirtualPlcSettingsProvider>()`, member list of the Siemens V21 reference:
  `IsSimulationDuringBlockCompilationEnabled`, `IsVirtualPlcDuringBlockCompilationEnabled`). Whether these members are writable
  on V21 is not documented (the V20 `Project` properties of the same names were read/write), so the write is bound on the
  live provider by the Siemens-free `ProjectCompilationSettingBinding` (Contracts): (i) a public writable `Boolean` property of
  exactly that name, (ii) an attribute of that name that `GetAttributeInfos()` lists as writable, set with `SetAttribute`;
  neither is `api_member_missing`. Every requested setting is bound before the first write, so a missing route changes
  nothing; a provider `GetService` does not return is `unsupported_capability`. No stub member was added (the setter is
  reached by reflection).
- **Result and verification.** `result.settings[]` = `{ setting, provider, member, requested, before, after, route,
  signature }`; `after` is read with the property getter right after the write (the mechanism the status read uses); a
  different value fails `postcondition_failed`. Verification check `compilationSettings` compares `after` with the request.
  Planning makes no read: the effect lists the requested values (`current` null); read the current ones with
  `get_project_status`.
- **Live acceptance still required:** which route binds on V21 (`route`), that `after` and a following `get_project_status`
  agree, that the TIA UI checkbox follows, whether the project must be compiled afterwards, and whether a PLCSIM Advanced
  Softbus download then succeeds.

## Granular block editing

These operations let an agent change one interface member, one DB value or one block property
without rewriting a whole document. They run in the same `plc_read`/`plc_write` contracts
(`1.0`/`2.0`), budgets, ordering, binding, guard and audit rules as the other PLC operations.
**Not live-tested**: every operation below is qualified offline only (Linux unit tests over the
repository's V21 SimaticML fixtures and FakeWorker scenarios); see [Live acceptance](#live-acceptance-still-required).

### Reads

- `get_block_interface` returns `{ block, format:"xml", contentHash, documentElement, isSafety,
  sections[], messages[] }`. `block` is `{ plcName, blockPath, name, blockType, number, autoNumber,
  programmingLanguage, isKnowHowProtected, instanceOfName }`; `blockPath` is canonical
  (`PLC/Blocks/.../Name`), unreadable attributes are `null`. `contentHash` equals the hash of a
  `get_block_content` xml read of the same state, so it can be used as `expectedContentHash`. Each
  section is `{ name, members[] }` and each member `{ name, path, dataType, startValue, informative,
  comments[{ culture, text }], attributes[{ name, value }], otherElements[], members[] }`: only the
  modeled SimaticML elements are interpreted, every other XML attribute of a `Member` is listed
  verbatim in `attributes` and every other child element by name in `otherElements`.
- `get_type_interface` returns `{ plcName, typePath, name, format, contentHash, documentElement,
  sections[], messages[] }` for a PLC data type (`contentHash` matches `get_type_content` xml).
- `get_db_values` returns `{ block, memberPath, isComplete, messages[], values[{ path, dataType, kind,
  startValue, message, attributes }] }` for a global or instance DB (array DBs and code blocks are
  `target_kind_unsupported`). The value paths come from the SimaticML documents: the DB's own
  sections for a global DB, the FB's `Input`/`Output`/`InOut`/`Static` sections (or the PLC data
  type's members) named by `InstanceOfName` for an instance DB; `Struct` members recurse, a quoted
  data type (PLC data type or multi-instance FB) is exported and expanded, and arrays with integer
  literal bounds expand to `name[i]`/`name[i,j]` (at most 256 elements per array; a `memberPath`
  into an array, e.g. `Big[70000].X`, enumerates only that element however large the array is).
  Each `value` entry is then read natively with `Interface.Members.Find(path)` and
  `GetAttribute("StartValue")`; `withAttributes:true` also reads `ExternalAccessible`,
  `ExternalVisible`, `ExternalWritable`, `Retain` and `SetPoint`. `kind` is `value`, `unexpanded`
  (a system type, symbolic array bounds, a recursive type or a name containing `.`; its `message`
  says why) or `missing` (`Find` returned nothing). At most 400 entries are returned; then
  `isComplete:false` and a message ask to narrow with `memberPath`. A `memberPath` the documents do
  not enumerate (for example inside a system type) is looked up directly with `Find`.

### Interface member edits

`add_interface_member`, `update_interface_member` and `delete_interface_member` address a block
(`blockPath` and `section`: `Input`, `Output`, `InOut`, `Static`, `Temp`, `Constant`, `Return`, as
the export lists them) or a PLC data type (`typePath`, section `None`), and a dotted `memberPath`
inside the section (`Settings.Limits.Max`; a name containing `.` goes in double quotes; no array
indices). Planning reads `get_block_interface` (know-how-protected, instance and array DBs are
refused as `target_kind_unsupported`), exports the document in `xml`, checks the optional
`expectedContentHash`, and applies the edit to the SimaticML document with a namespace-aware XML
model that leaves every node it does not touch unchanged (whitespace, declaration and line-break
style included). The dry run reports the field changes (`member`, `name`, `dataType`, `startValue`,
`comment[<culture>]`) and a bounded `contentDiff`. The mutation imports exactly the planned document
through `update_block_logic` (blocks; the other bundle documents are unchanged) or
`update_type_content` (types) with the hash of the export it was computed from, so a change in between
fails with `state_changed`; the block route compiles and reports `importOutcome` like
`update_block_logic`. The result is `{ format:"xml", section, memberPath, importOutcome, typeImport }`.
Verification re-reads the interface (`check:"interfaceMember"`) and compares the member with the
requested state (start value and comment text compared without case and surrounding blanks; a
rename's new name exactly, so a case-only rename Portal ignored fails); its
`contentHash` is the next `expectedContentHash`. A later edit of the same object in one call previews
against the earlier planned document, carries `dependsOn` and is re-planned against the real state
before its own mutation (an `expectedContentHash` on such a later item is refused).

Fail-safe blocks (`ProgrammingLanguage` starting `F_`) follow TIA Portal's safety login rule (a login only when the
safety program has a password; [PLC safety reference](PLC_SAFETY_SUMMARY.md#f-block-changes)). Refused in planning
(nothing is imported): documents other than `SW.Blocks.FB/FC/OB/GlobalDB` and `SW.Types.PlcStruct`, interface namespaces
other than `.../SW/Interface/v5`, informative (system) members, the `Input` section of an OB, a
member name that already exists in the parent (case-insensitive), adding inside a member that is not
`Struct`, a start value on a `Struct`, array or quoted-type member, renaming or deleting `Return`, and
inserting a new `Comment` or `StartValue` into a member that carries elements this model does not
order (`otherElements` is not empty). A data type change refuses a member that carries content of
its old type the model does not order (nested `Sections` of a multi-instance, `Subelement`, any other
`otherElements`) or a `Version` attribute, and a member with a start value needs `startValue` (a
literal of the new type) or `clearStartValue:true` in the same item: the old start value is neither
kept nor dropped silently.

A rename or delete is **not a refactoring**: references to the member in the block's own code, in
other blocks and in instance DBs are not updated; the next compile reports them (guard
`plc_changes_interface` says so on every interface edit).

### DB values, instance DBs, rename, renumber, copy, move

- `update_db_member` sets `startValue` (passed as text to `Member.SetAttribute("StartValue", …)`)
  and/or the boolean attributes `ExternalAccessible`, `ExternalVisible`, `ExternalWritable`,
  `Retain`, `SetPoint` on `DataBlock.Interface.Members.Find(memberPath)` of a global or instance DB,
  at any depth including array elements and multi-instance paths. Every attribute is read before and
  after (as invariant-culture text); one whose value Portal kept fails the item `postcondition_failed`
  (attributes set before it stay applied; an equal number in another spelling, `1.50` for `1.5`, is
  not "kept"). Verification re-reads with `get_db_values` (`check:"dbMember"`): a start value passes
  when the stored text equals the request without case and blanks, or, for `SInt`…`ULInt` and
  `Real`/`LReal` members only, when both parse as the same number (invariant culture). Other
  spellings fail, in particular time, date and typed literals (`T#1s` against `T#1000ms`), since no
  source states Portal's normalization rules for them. A write of one member does not supersede
  writes of other members.
- `create_instance_db` calls `PlcBlockComposition.CreateInstanceDB(name, isAutoNumber, number,
  instanceOfName)` on the target group; a fixed `number` switches automatic numbering off. Name,
  `InstanceOfName` and number are read back.
- `rename_block` sets `Name` through `SetAttribute("Name", …)` and reads it back; `renumber_block`
  sets `AutoNumber` (and `Number`) through `SetAttribute` and reads both back.
- `copy_block`, `move_block`, `move_tag_table` and `move_tag` run their mutating steps inside one
  `TiaPortal.ExclusiveAccess(string)` and one `ExclusiveAccess.Transaction(project, string)`:
  `CommitOnDispose()` is requested only after every step and its read-back succeeded and
  `CanCommit` is true, so any failure before that rolls the whole round trip back. Whether TIA Portal
  accepts a block or tag-table import inside a transaction is **not live-tested**; if it refuses, the
  item fails with the original unchanged.
- `copy_block` exports the block's SimaticML, sets `Name`, removes `Number` (the import then assigns
  one) and, in the transaction, imports it into the source group or `targetGroupPath` with
  `ImportOptions.None`, applies a `number` like `renumber_block` and reads the copy back; a mismatch
  rolls the copy back. `move_block` exports the block, then deletes it, imports the export into
  `targetGroupPath` and reads it back in the transaction. Both refuse know-how-protected blocks (or
  unreadable protection: the SimaticML export of a protected block carries only the interface and Openness offers no
  protected import, so this route cannot carry its code), apply the safety login rule to fail-safe blocks, stay within one PLC and software scope, and report a
  new object identity: a library-type link is not carried over, and a moved block's instance DBs and
  callers resolve it by name again at the next compile.
- `move_tag_table` exports the table (`PlcTagTable.Export`), then deletes it and imports it into the
  target folder (`PlcTagTableComposition.Import`, bound by its documented parameter list before the
  delete) in the transaction, comparing tag and user-constant counts. The default tag table is
  refused (TIA Portal does not delete it). A table holding a tag that reports `IsSafety=true` moves under the
  safety login rule (the same rule as `move_tag`; refused only when the PLC's safety program has a password and
  the session is not logged on).
- After a failed move the original's location is probed. Rolled back (the usual case): nothing else
  happens. Not back in the source: the export is re-imported there. That failing too: the export is
  written to a recovery folder `%LOCALAPPDATA%\TiaMcpServer\recovery\<UTC timestamp>-<guid>\`
  (beside the audit folder; the system temp folder only when that is not writable) as
  `document.xml` plus a `recovery.json` sidecar with the original name, kind, PLC, source and target
  and the failure; the file names are fixed, so object names never become paths. When no recovery
  folder can be written, a move_tag_table export already on disk is kept instead of being cleaned
  up. The failed item's `result` is `{ operation, objectKind, objectName, sourcePath, targetPath,
  transaction ("rolled_back"|"outcome_unknown"), originalState ("unchanged"|"restored_by_reimport"|
  "missing"|"moved_to_target"|"unknown"; copy_block: "copy_absent"|"copy_present"), recovery
  { directory, documentPath, metadataPath, location } | null, recoveryError, messages[] }`, and the
  failure message repeats the outcome and any path. Verification of a failed `copy_block` re-reads
  where the copy would be, not the source.
- These results are `{ operation, sourceBlockPath, block, messages[] }` (block operations),
  `{ operation, block, memberPath, changes[{ attribute, previous, requested, observed }] }`
  (`update_db_member`) and `{ operation, plcName, tableName, sourceFolderPath, targetFolderPath,
  tagCount, userConstantCount }` (`move_tag_table`). Verification re-reads the resulting block
  (`check:"block"`, with its content hash) or the tag tables (`check:"tagTable"`).

### Provenance

| Member or construct | Use | Source |
| --- | --- | --- |
| `DataBlock.Interface`, `PlcBlockInterface.Members`, `MemberComposition.Find(string)`, `Member.Name` | DB value paths, start values | totally-integrated-claude `tia-plc-operations/references/blocks.md` (V21 API reference), `tia-import-export/references/plc-blocks.md` §9; Siemens `blocks` skill (`InstanceDB/GlobalDB.Interface.Members`) |
| `Member.GetAttribute/SetAttribute("StartValue" / "ExternalAccessible" / "ExternalVisible" / "ExternalWritable" / "Retain" / "SetPoint")` | `get_db_values`, `update_db_member` | `plc-blocks.md` §9 (attribute names; the value types of `Retain`/`SetPoint` are not stated there and are assumed boolean) |
| `PlcBlockComposition.CreateInstanceDB(string, bool, int, string)` returning `InstanceDB` | `create_instance_db` | `blocks.md` §12 and API reference, `plc-blocks.md` §8 |
| `GetAttribute("InstanceOfName")`, `GetAttribute/SetAttribute("AutoNumber")`, `SetAttribute("Number")` | identity, renumbering | `blocks.md` §3 and §12 |
| `SetAttribute("Name", …)` on a block | `rename_block` | Siemens `blocks` skill (renaming a generated block) |
| `PlcBlock.IsKnowHowProtected` | refusals | `blocks.md` V21 API reference |
| `PlcBlock.Export`, `PlcType.Export`, `PlcBlockComposition.Import(FileInfo, ImportOptions)`, `ImportOptions.None` | SimaticML round trips | `plc-blocks.md` §1–§4, `overview.md` §4 |
| `PlcTagTable.Export(FileInfo, ExportOptions)`, `PlcTagTable.IsDefault`, `PlcTagTableComposition.Import(FileInfo, ImportOptions)` | `move_tag_table` | `tags-types.md` (V21 API reference) |
| "If no block number is in the XML, it is assigned automatically" | `copy_block` | `plc-blocks.md` §1 |
| `TiaPortal.ExclusiveAccess(string)`, `ExclusiveAccess.Transaction(ITransactionSupport, string)`, `Transaction.CanCommit`, `Transaction.CommitOnDispose()`; rollback when disposed without commit or after an exception | copy/move transactions | totally-integrated-claude `tia-csharp-common/SKILL.md` (Exclusive access, Transactions, destructive-operation safety), `tia-devices-general/SKILL.md` (execution pattern step 6); Siemens `engineering-objects` skill; existing use in `SubnetLifecycleService` and `VciWriter` |
| `PlcTag` attribute `IsSafety` | `move_tag_table` refusal | the same documented attribute `move_tag` reads (`TagMutationService`) |
| "Instances are exported without type info and become detached on re-import" | round-trip guard | `overview.md` §3 |
| `ProgrammingLanguage` prefix `F_` marks fail-safe blocks | safety login rule and acceptance guard | `plc-blocks.md` §6 |
| `Sections` (`.../SW/Interface/v5`), `Section Name`, `Member Name/Datatype/Informative`, `Comment/MultiLanguageText Lang`, `StartValue`, nested `Member`, `AttributeList/Name`, `Number`, `ProgrammingLanguage` | XML model | repository fixtures `AnalogInputSettings.xml`, `get_block_content.ob-lad*.bundle.txt`, `ObFixtures/*.xml`, `BlockSourceGenerator`; Siemens `plc-data-types` skill (quoted UDT and `Array[..] of` data types) |
| Type names `Siemens.Engineering.SW.Blocks.Interface.Member/MemberComposition/PlcBlockInterface` | stubs | `api-baselines/v21/step7.json` |

### Not provided (no evidenced construct or member)

- Retain, accessibility or other member attributes of FB/FC/OB/UDT interface members (their
  SimaticML form is not evidenced; DB members take them natively through `update_db_member`).
- Start values of array elements or of members inside a PLC data type instance in a block or type
  interface (`Subelement` is not evidenced); DB values take them through `update_db_member`.
- Creating or deleting interface sections, adding members inside array or UDT-typed members, and
  editing instance or array DB interfaces (an instance DB's interface is its FB's).
- Imports that accept structural changes (`SWImportOptions.IgnoreStructuralChanges`) — the
  existing importer path is reused unchanged.
- A native block or tag-table move: none is documented, so moves are export/delete/import with the
  limits above; copy and move across software units or PLCs.
- Network logic: LAD/FBD/SCL bodies are never edited by these operations.

### Live acceptance still required

1. `get_block_interface`/`get_type_interface` on FB, FC, OB, global DB, instance DB and UDT exports;
   `contentHash` equals `get_block_content`/`get_type_content`.
2. Each interface edit on an FB, FC (`Return` data type), OB (`Temp`), global DB and UDT, including
   nested `Struct` members, comments in two cultures and `clearStartValue`; compile result and
   re-read; an FB with instance DBs (structural-change behaviour).
3. `get_db_values` and `update_db_member` on global and instance DBs: nested struct, UDT member,
   array element, multi-dimensional array, multi-instance and system-type (`IEC_TIMER`) paths;
   numeric start values passed as text; `Retain`/`SetPoint` value types; a value Portal normalizes.
4. `create_instance_db` with automatic and fixed numbers; for an FC (expected rejection).
5. `rename_block` (including a colliding name), `renumber_block` (number and `autoNumber`, OB ranges).
6. `copy_block` and `move_block` for FB/FC/DB/OB, into another group, failure of the target import
   (that the transaction rollback restores the source; the re-import and recovery-folder fallbacks),
   the know-how-protected refusal, a fail-safe block under the safety login rule, library-type instances.
7. `move_tag_table` between folders, of the default table (refusal), of a table with an F-tag
   (refusal) and with a colliding target (rollback).
8. That block and tag-table imports are accepted inside an ExclusiveAccess transaction at all, and
   that `move_tag`'s re-create commits and rolls back as one unit.

## Tag safety acceptance boundary

`plc_write` replaced the token-bound tag safety snapshots. Tag, table and constant writes plan
against the tag inventory their guards need: an unreadable constant value blocks, table deletion
and address overlaps are info. Name collisions and the default tag table are no longer guarded;
The recorded duplicate/default-delete cases failed as `engineering_rejected`; this is scoped evidence, not a guarantee for every namespace collision. The 2026-10-07
[validation report](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md)
records the original offline and live acceptance of this path, and the 2026-10-26
[native rejections report](../superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md)
records the contract `2.0` change.

Historical: PR 5 bound typed tag snapshots to preview/apply tokens. Its
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
covers that token flow only and does not qualify `plc_write`. Multilingual per-tag comment binding
and public `list_tag_tables` completeness changes remain deferred.

## Project-tree safety acceptance boundary

`plc_write` replaced the token-bound project-tree safety snapshots. Block and group writes plan
against a project-tree snapshot only where a guard needs it (deletes and OB creates): deletes are
info, the recorded same-target duplicate block create failed as `engineering_rejected`, and an unreadable node makes
the snapshot incomplete, which blocks as `plc_state_unverifiable` instead of hiding what a delete
removes. The
[validation report](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md) records
the offline and live acceptance of this path.

Historical: PR 6 bound typed project-tree snapshots to preview/apply tokens. Its
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md)
covers that token flow only and makes no save, persistence, plant, or hardware claim.

## Current limits

The current surface does not provide:

- Generic online/offline status or connection configuration.
- Compare-to-online, program upload/download, or `UpdateProgram` workflows.
- Software Unit lifecycle or administration operations; block operations within existing Software Units are supported. Safety units, safety administration, safety signatures, and safety validation are not provided.
- PLC alarm, text list and ProDiag features beyond the [PLC alarms and text lists reference](PLC_ALARMS_TEXTLISTS_SUMMARY.md), which lists every left-out operation with its reason (for example typed text list entries, individual alarm classes and program alarms as objects).
- Technology object features beyond the [technology object reference](TECHNOLOGY_OBJECTS_SUMMARY.md) (cam data, interpreter programs, master value couplings, Startdrive telegram connections). Watch and force tables are covered by the [PLC online Openness reference](PLC_ONLINE_OPENNESS_SUMMARY.md).
- OPC UA server configuration, communication groups, access control, or role mapping.
- System blocks, know-how protection workflows, webserver pages, block fingerprints, or loadable files.
