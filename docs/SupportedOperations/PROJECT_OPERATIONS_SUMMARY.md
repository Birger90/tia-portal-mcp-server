# TIA Portal Project and Portal Operations

**Language:** English · [Deutsch](PROJECT_OPERATIONS_SUMMARY.de.md)

## Read operations

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `get_project_status` | `get_project_status` | No arguments; reads status and metadata without opening, binding, or switching in any mode. |
| `browse_project_tree` | `browse_project_tree` | v3: typed `startSelector`, `depth`, and `pageSize`; use `cursor` to continue an immutable snapshot. |
| `compile_check` | `compile_check` | Optional `plcName`, `blockPath` and `scope` (`software` default: the PLC program or one block, **no hardware**; `hardware`: the hardware configuration of each PLC's CPU device item, report `scope` `hardware`, not combinable with `blockPath`); returns compiler messages. Available in read-write and full modes. See [CPU protection](PLC_SECURITY_SUMMARY.md#hardware-compile-compile_check-scope-hardware). |

### Explicit binding (`bind_project`)

`bind_project(projectPath?:string, forceRebind:bool=false, action?:string, portalProcessId?:int,
serverAlias?:string, group?:object, serverProjectName?:string)` is available in all three modes.
Omitted/null action or `action:"bind"` retains the existing behavior and JSON shape described below.
It changes server session selection without opening, creating, saving or closing a project.
Only `open_project`, `create_project` and `retrieve_project` open projects; no request implicitly opens one.
Read-only never opens, creates, saves or closes, but explicit binding can switch its session.

With an unbound session, omit the path to adopt the sole open project or receive typed
`target_not_found`/`target_ambiguous` plus candidates. Supply an advertised absolute `.ap21` path
for a standalone project or the observed typed owner's `.amc21` engineering path for a local
session. An optional exact `portalProcessId` disambiguates the Portal; PID/path mismatch fails
closed. `.als21` files and inventory directories are not adoption/status/startup selectors.
A different configured, verified, or last-bound path requires
`forceRebind:true`. A verified same-path/no-path call rechecks status without switching.
Reattachment may show TIA's Openness access dialog, which a human must answer. Worker ownership
does not survive detach; a reattached project is treated as UI-owned. Binding leaves UI projects
open, including unsaved changes. A modified headless project with this worker as its sole
Openness client blocks detach; that protection is implemented but unqualified by the Oct3 live run.

The canonical standalone envelope (`tool`, `contractVersion:"1.0"`, `success`, `error`, `warnings`,
`result`) uses `StandaloneToolOutcome<ProjectBindingResult>`. Its value contains `transition`
(`bound`, `unchanged`, `switched`, or `none`), non-null `binding` and `previousBinding`, nullable
`project` status with `metadata:null`, and always-present `portals`. Binding state is `unbound`,
`configured_unverified`, `verified`, or `invalidated`; path and Portal PID can be null. Portal entries
contain `processId`, nullable `projectPath`, `hasUserInterface`, `isBound` and nullable
`otherClientCount` (attached Openness client sessions other than this server's worker, from
`TiaPortalProcess.AttachedSessions`; null when unreadable).

The Portal listing (`list_tia_portal_processes`, used by `list_portals` and the candidate listings;
roadmap row 21) reads `TiaPortal.GetProcesses()` and, per process, `Id`, `ProjectPath`, `Mode` and
`AttachedSessions` (`TiaPortalSession.ProcessId`), all declared in the compile stubs. It never calls
`TiaPortalProcess.Attach()` and never disposes a process handle. Sources: the Siemens
`licensing-and-firewall` skill (`tia-portal-ai-extensions`) places the Openness firewall prompt at
connecting, that is creating or attaching a `TiaPortal`; the Siemens `session-and-project` skill
documents `GetProcesses()`/`Attach()` and that disposing a `GetProcesses()` handle tears down the live
instance in V21; rickgaiser/TiaMcp (`src/TiaMcp.Openness/TiaConnection.cs`, `ListRunningInstances`,
idea only) reads the same members without attaching for the same reason. Reading `AttachedSessions` on
a process that is not attached was already part of the temporary-target check of explicit `bind`
(before its `Attach()`); that the listing alone raises no dialog is not live-tested (live case L11 of
[V21 pitfalls](V21_PITFALLS.md#live-acceptance-list)).

Starting a TIA Portal with its user interface (the second half of row 21, rickgaiser's
`tia_connect startPortal`) is left out; start TIA Portal yourself, then use `open_project`. Reasons:

- `new TiaPortal(TiaPortalMode.WithUserInterface)` is documented (Siemens `session-and-project` skill),
  but the started instance belongs to this server's worker, which the host recycles after a timeout or
  crash. Whether it survives that is unverified and the sources disagree: `totally-integrated-claude`
  `skills/tia-csharp-common` says `Dispose()` of a UI instance only disconnects, rickgaiser
  (`StartPortal` comment in `TiaConnection.cs`) reports that such an instance closes with its owner,
  unsaved work included. The compile stubs also have no constructor (`TiaPortal` is abstract there).
- rickgaiser's own route starts `Bin\Siemens.Automation.Portal.exe` below the registry install path
  as a process of its own, waits up to five minutes and then attaches. That is no Openness API; the
  executable path comes only from non-Siemens sources (rickgaiser, the `totally-integrated-claude`
  doctor probe). The wait plus the project open can exceed the default worker request timeout
  (5 minutes), and the `Attach()` that follows can raise the Openness access dialog a human must
  answer.
- Either way it is a new lifecycle surface (read-write confirmation, audit, dry run) that cannot be
  checked on V21 here.

Bad input and unforced foreign-path requests reject with top-level error and null result before
worker activity. The headless refusal follows one selection round trip, whose wire `guard_blocked`
the host maps to rejected `binding_conflict`. Failed completed calls retain a value with transition
`none` and binding state after the call. Pre-detach `target_not_found`, `target_ambiguous`, and
`guard_blocked` preserve the old verified binding; other worker, timeout/crash, protocol or
postcondition failures invalidate it. Candidates appear only from a listing in that call; a failed
path selection with not-found/ambiguous adds one listing. Doctor reports Warning for an unbound
writable session and names `bind_project` as remediation.

For explicit inspection, use `action:"list_portals"` to discover without attachment/adoption,
or `list_server_connections`, `list_server_groups`, `list_server_projects`, `list_local_sessions`,
`get_lock_state` for exact Project Server inventory. Inspection preserves healthy binding and
cursors, cannot switch an existing attachment, and rejects binding selectors even when null/false.
Results add conditional `result.value.inspection`, with observed PID and typed nullable slots;
`transition:"none"` and `project:null`. Whole-value omission includes the inventory. See the
[Multiuser reference](MULTIUSER_OPERATIONS_SUMMARY.md) for selectors, examples, current-user scope,
lock/error/omission limits and scoped live evidence. PR3 discovery was 7/17/17;
current discovery is 13/32/32 with the standalone VCI and library tools, `plc_download`, `plc_tia_online`, `plc_station_upload`, the S7-PLCSIM Advanced tools and the S7-1500 Web API tools.

### Local-session identity and basic status

PR4 selects an already-open typed `LocalSession` by its exact `.amc21` engineering path in
every access mode. `get_project_status` and `--project`/`TIA_MCP_PROJECT_PATH` use that same
already-open identity; ordinary reads and startup assertions never open or select another owner.
Local status contains `isOpen`, `path`, nullable `isModified`, `metadata:null`, and conditional
`context`. Standalone and closed status omit `context`; standalone extended metadata remains
available. Local fields other than basic status remain null rather than fabricated metadata.

Local `context` describes `containerKind:"localSession"`, `engineeringProjectPath`, nullable
`sessionContainerPath`, `openedByWorker`, `sessionMode`, capabilities, remote identity and
connection observation. Cold adoption has `openedByWorker:false` and `sessionContainerPath:null`.
Only a verified successful exact `.als21` open records that opener provenance; detach loses
ownership/provenance. No reverse ALS/inventory join is available: mode remains `unknown`, remote
identity remains null, and active connection observation remains `unknown`. Descriptive capability
entries grant no access-mode permission. Local content reads, PLC/Network writes, compilation
and `save_project` are delivered (see the
[Multiuser reference](MULTIUSER_OPERATIONS_SUMMARY.md#internal-context)). A local
`save_project` uses `LocalSession.Save()` and never checks in to the Project Server. Local
create, save-as, archive and generic close reject with `unsupported_capability`, including previews.

The [PR4 live report](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md)
records scoped maintainer acceptance. Two retained UI Portals recovered local sessions online in
read-write/full; offline read-write previews passed, while actual opening required operator
dismissal of Siemens dialogs. Noninteractive offline opening failed in both tested fixtures.
Full-mode offline, startup/read-only, headless and race cases were not executed on this candidate.

### `browse_project_tree` v3

`browse_project_tree` returns one canonical JSON document identically in the MCP `content` text block and `structuredContent`. The response is a versioned envelope, and `result.nodes` is a flat parent-first sequence. Grouped and ungrouped devices have the same `Device` shape. PLC system-block groups use `nodeType: "SystemBlockFolder"`; contained blocks keep their functional block type and carry `details.IsSystemBlock: "true"`. That marker records TIA hierarchy membership only and is not an author, vendor, library, or provenance claim.

Every PLC block node may include these optional, default-on string fields in its `details` object; populated values are emitted without an opt-in request.

| Block detail | Meaning |
| --- | --- |
| `HeaderAuthor` | Non-blank `PlcBlock.HeaderAuthor` exactly as reported by TIA Portal. |
| `HeaderVersion` | Non-blank `PlcBlock.HeaderVersion.ToString()` value. The value is included when the typed Openness property is non-blank, regardless of whether the TIA UI appears blank or the exported SimaticML contains a `<HeaderVersion>` element. |
| `HeaderFamily` | Non-blank `PlcBlock.HeaderFamily` exactly as reported by TIA Portal. |
| `HeaderName` | Non-blank `PlcBlock.HeaderName`; separate from the node's engineering-object `name`. |

Blank header values are omitted rather than emitted as `null` or empty strings. This is based on the value returned by the typed Openness property: only `null`, empty, or whitespace values are omitted. In the bounded V21 acceptance-project observation, an omitted SimaticML version was exposed by the typed property as `0.1`, while clearing another version produced/exposed `0.0.0.0`. Those concrete values are observations from that project, not universal Siemens guarantees. Therefore, `details.HeaderVersion` means that Openness reported a typed value; it does not prove that the version was explicitly populated in the TIA UI or in XML. Consumers that require XML element-presence semantics should inspect an export instead. Header metadata is descriptive project data, not verified vendor provenance. `HeaderName` never changes selector identity, and `IsSystemBlock` continues to describe hierarchy membership only. The node's engineering-object `name` and `HeaderName` are independent; neither value is a fallback for the other.

The initial-request fields are:

| Field | Behavior |
| --- | --- |
| `startSelector` | Optional root-to-target array of `{ nodeType, name }` segments. `nodeType` is case-sensitive and drawn from the closed v3 vocabulary; `name` is an exact ordinal-ignore-case match among direct children. Zero or multiple matches fail closed. |
| `depth` | Optional maximum returned depth from the selected root, inclusive; minimum `1`. |
| `pageSize` | Optional requested node count from `1` through `200`; default `100`. Canonical size projection may return fewer complete nodes. |
| `cursor` | Opaque process-local continuation. A cursor-only call is the normal continuation request. |

#### v2-to-v3 request migration

The v2 request was:

```json
{ "depth": 2, "startPath": "PLC_1" }
```

The v3 request is:

```json
{
  "depth": 2,
  "startSelector": [
    { "nodeType": "Device", "name": "PLC_1" }
  ]
}
```

Use `v3.0.0` or newer. The old bare nested array is not retained, and the legacy `startPath` and `deviceName` project-tree inputs are removed instead of accepted as aliases.

#### Complete success envelope

```json
{
  "contractVersion": "3.0",
  "status": "succeeded",
  "result": {
    "snapshot": {
      "snapshotId": "0123456789abcdef0123456789abcdef",
      "createdAt": "2026-09-06T12:00:00+00:00",
      "idleExpiresAt": "2026-09-06T12:10:00+00:00",
      "totalNodes": 2
    },
    "query": {
      "projectPath": "C:\\Projects\\Plant.ap21",
      "startSelector": [
        { "nodeType": "Device", "name": "PLC_1" }
      ],
      "depth": 2
    },
    "pagination": {
      "offset": 0,
      "requestedPageSize": 100,
      "returnedCount": 2,
      "nextCursor": null
    },
    "nodes": [
      {
        "nodeId": "n0",
        "parentNodeId": null,
        "sequence": 0,
        "name": "PLC_1",
        "nodeType": "Device",
        "details": {}
      },
      {
        "nodeId": "n1",
        "parentNodeId": "n0",
        "sequence": 1,
        "name": "PLC_1",
        "nodeType": "PlcSoftware",
        "details": {}
      }
    ]
  },
  "failure": null,
  "warnings": []
}
```

When `pagination.nextCursor` is non-null, continue with a cursor-only request and repeat until it becomes null:

```json
{ "cursor": "<pagination.nextCursor>" }
```

Each initial request makes one tree-observation worker call; configured read-write startup may first call `get_project_status` to verify the project binding. The net48 worker applies the residual subtree selector and depth filter after walking the selected device. Each continuation authenticates the cursor, checks binding ID/revision, and serves the same immutable point-in-time snapshot without another worker call. A binding change returns `cursor_binding_mismatch`, including after switching away and back to the original path. The snapshot does not incorporate later TIA edits. Cursors are process-local: host restart, expiry, or bounded-cache eviction returns `snapshot_unavailable`. Start a new cursor-free browse after either outcome. Supplying query fields with a cursor is allowed only when they match the captured query; a mismatch returns `cursor_filter_mismatch`.

Nodes are parent-first, so a caller can reconstruct a selector without legacy path text: index nodes by `nodeId`, start at the desired node, follow `parentNodeId` to null, reverse the chain, and project every node to `{ "nodeType": node.nodeType, "name": node.name }`. That returned chain begins at the selected snapshot root, which may be below `Device`. To build the complete selector, prepend `result.query.startSelector` with its final segment removed, then append the reconstructed returned chain. When the initial `startSelector` was omitted, the prefix is empty. This avoids duplicating the selected root while preserving Device ancestry for snapshots rooted at `PlcSoftware`, `BlockFolder`, or deeper. Submit the combined array as `startSelector`. Do not persist `nodeId` across snapshots; it is snapshot-local.

Every snapshot is limited to 4,000,000 canonical characters. The process retains at most four snapshots and 16,000,000 aggregate snapshot characters with a ten-minute sliding idle lifetime. Every page is limited to 60,000 canonical characters and contains complete nodes only. `snapshot_too_large`, `result_item_too_large`, and `result_metadata_too_large` fail explicitly; v3 never appends a truncation marker. The legacy `details.Path` member is removed.

All project-tree failures retain the same envelope with `status: "failed"`, `result: null`, a `{ category, message }` failure, and separate warnings. Input and selection failures are `validation_error`, `invalid_selector`, `target_not_found`, and `target_ambiguous`. Structurally invalid selector members use `invalid_selector`; malformed or empty cursors use `invalid_cursor`. Cursor and snapshot failures are `invalid_cursor`, `cursor_filter_mismatch`, `cursor_out_of_range`, and `snapshot_unavailable`. Size failures are `snapshot_too_large`, `result_item_too_large`, and `result_metadata_too_large`. Oversized diagnostics produce a bounded `result_metadata_too_large` envelope without echoing the diagnostics. Binding/worker/protocol failures may be `binding_conflict`, `worker_operation_failed`, `worker_timeout`, `worker_crashed`, `access_denied`, or `protocol_error`.

Per-item Openness degradation is retained in the envelope `warnings` array rather than changing a failure into success.

`compile_check` is a standalone engineering operation. It is not marked read-only and is exposed in read-write and full modes. The report `scope` is `plc`, `block` or `hardware`; an unknown `scope` argument or `hardware` with `blockPath` is rejected with `validation_error` before any worker call. The default program compile never compiles the hardware configuration, so hardware errors a download reports (defect D9) appear only with `scope:"hardware"`.

### Standalone status and compilation contract

`get_project_status` and `compile_check` declare concrete MCP output schemas and return the same canonical JSON document in `content` text and `structuredContent`. Both use contract version `1.0` and the envelope `tool`, `contractVersion`, `success`, `error`, `warnings`, and `result`. Inputs and access modes are unchanged (C-0 later removed `get_project_status.projectPath`). This output change breaks legacy clients: replace parsing the JSON string in `payload` with reading the object at `result.value`.

`result` contains four explicit members: `status` (`succeeded`, `failed`, or `omitted`), `value`, `failure`, and `omission`. The value is the complete `ProjectStatusInfo` (including `metadata`) or `CompileCheckReport`. Optional/unavailable data is represented by explicit nulls, including within nested metadata. A no-project read succeeds with `result.value.isOpen:false` and does not bind an unbound host session.

Rejection before the requested operation sets `success:false`, a top-level `{category,message}` `error`, `result:null`, and MCP `isError:true`. An attempted operation that fails sets `success:false`, `error:null`, and MCP `isError:false`, with failure evidence in `result.failure` or compiler diagnostics in `result.value`. Host identity validation can fail after compilation; that remains an attempted failure and the session binding is invalidated. A timeout/crash leaves the outcome unknown; inspect current project state before deciding whether to retry.

For compilation, `success` means the selected compilation passed, including warning-only reports. Errors, unavailable compiler results, and unknown/incomplete states do not pass. Compiler errors retain the report, for example:

```json
{
  "contractVersion": "1.0",
  "error": null,
  "result": {
    "failure": null,
    "omission": null,
    "status": "failed",
    "value": {
      "blockPath": "PLC_1/Blocks/Main",
      "overallState": "Error",
      "plcs": [{
        "deviceName": null,
        "diagnosticNotes": [],
        "errorCount": 1,
        "messages": [{"description": "Invalid expression", "path": "PLC_1/Program blocks/Main", "severity": "Error"}],
        "plcName": "PLC_1",
        "state": "Error",
        "warningCount": 0
      }],
      "scope": "block",
      "totalErrorCount": 1,
      "totalWarningCount": 0
    }
  },
  "success": false,
  "tool": "compile_check",
  "warnings": []
}
```

The complete canonical value has a 60,000-character limit; the final document, including escaped strings, warnings and omission metadata, has a 180,000-character limit. Oversized values are replaced whole: `status:omitted`, `value:null`, `success:false`, `error:null`, and `isError:false`. `omission` reports `reason` (`resultExceededItemCharLimit` or `responseExceededDocumentCharLimit`), `limitChars`, `originalChars`, `retryTool`, and `guidance`. Compile retries can narrow `plcName` or `blockPath`; status has no metadata selector, so its guidance directs users to reduce metadata or inspect it in TIA Portal. Whole warning entries may be removed to fit the document, with a bounded count notice. Failure prose is shortened only as a last resort and marked explicitly.

Compiler messages (since 2026-11-02, defect K04; shape and contract version `1.0` unchanged): TIA Portal returns
its diagnostics as a tree whose container nodes (the PLC, `Hardware configuration`, a rack, a block folder) have an
empty or blank description and repeat the worst severity below them. Live on 2026-11-02 the hardware compile of a
fail-safe CPU listed `{"",PLC_F1,Error}`, `{"",Hardware configuration,Error}`, `{" ",PLC_F1,Error}`,
`{" ",Profilschiene_0,Error}` before the three real error texts, which had an empty path. Such containers are no
longer reported as messages. Every reported message has a non-blank description (or an unreadable one, reported
empty with a diagnostic note) and its `path` is the chain of the non-blank TIA Portal paths of its containers and
its own, outermost first, joined with `/` (for example `PLC_F1/Hardware configuration/Profilschiene_0`); a path
equal to the chain's last segment adds nothing, and one that already starts with the chain's first segment and `/`
replaces it. The segments are TIA Portal's values, not escaped, so the path is display context, not a selector.
`errorCount`, `warningCount`, the totals and the states are TIA Portal's and never recounted from the delivered
messages. The rule is offline-tested only (stand-in message trees); not live-tested.

Strict decoding precedes budgeting: malformed or inconsistent worker success payloads become a fixed-message `protocol_error` outcome and are never echoed. Producer diagnostic limits are separate: a compiler report's `diagnosticNotes` can disclose omitted/shortened messages, and counts need not equal the number of delivered messages. A passing compilation does not prove diagnostics were exhaustive, a project was saved, or PLC code was downloaded.

### `get_project_status` metadata surface

When a standalone project is open, `get_project_status` reports the status fields plus a nested `metadata`
object carrying the extended read-only project metadata:

| Field | Description |
| --- | --- |
| `copyright` | Project copyright text, verbatim from Openness. |
| `family` | Project family, verbatim from Openness. |
| `comment` | Multilingual project comment: `translations` list of `{ culture, text }` in the order Openness reports them, preserving every translation. `culture` is the language culture name (for example `en-US`). |
| `languageSettings` | `languages` and `activeLanguages` as culture-name lists; `editingLanguage` and `referenceLanguage` culture names (null when unset). |
| `historyEntries` | Text and date-time of each history entry, in Openness order, verbatim and not deduplicated. Capped at `200` entries (oldest first); when Openness reports more, `historyTruncated` is `true`. When history could not be read, both `historyEntries` and `historyTruncated` are explicit `null` — `historyTruncated` is `false` only when history was read completely. |
| `usedProducts` | `{ name, version }` for every product Openness records, no inference and no deduplication. |
| `compilationSettings` | V21 block-compilation toggles read through `PlcSimulationSettingsProvider` and `VirtualPlcSettingsProvider`: `isSimulationDuringBlockCompilationEnabled` and `isVirtualPlcDuringBlockCompilationEnabled`. A value is explicit `null` when its provider or value is unavailable, reported as a response warning — never a fabricated `false`. Set them with `plc_write` `set_project_compilation_settings` (see [PLC operations](PLC_OPERATIONS_SUMMARY.md#project-block-compilation-settings); not live-tested). |

All metadata is readable in all three access modes; metadata inspection never closes, switches, saves, or
confirms anything. Unavailable sections degrade to a warning and `null` output rather than a
fabricated default; unrelated errors still fail the call normally.

The `get_project_status` response uses the whole-value omission budget described above.
Lifecycle post-write verification (after all six operations) reads
the plain project status only — it never enumerates history or the extended metadata surface.

## Lifecycle operations

All seven lifecycle tools are available in read-write and full; the complete mode counts are
13/32/32. Read-write requires one confirmation form per actual call; full executes under policy
without server elicitation. Block guards stop a call in every mode. Ordinary reads never bind,
switch or open; use `bind_project` for already-open projects and open/create for deliberate opening.

| Tool | Behavior | Main inputs |
|---|---|---|
| `open_project` | Opens a standalone project or existing local session and binds its verified engineering identity. | Absolute `.ap21` or existing file-only `.als21` `projectPath`; optional `forceRebind`. |
| `create_project` | Creates a project and binds the session to it. | Absolute `projectDirectory`, `projectName`; optional `author`, `comment`. |
| `retrieve_project` | Retrieves a project archive into a directory and binds the session to the retrieved project (not live-tested; see [Retrieving an archive](#retrieving-an-archive-retrieve_project)). | Absolute existing `archivePath`, existing absolute `targetDirectory`; optional `upgrade`, `forceRebind`. |
| `save_project` | Saves the active project. | No operation inputs (acts on the bound project). |
| `save_project_as` | Saves a copy and rebinds the session to the copy. | `targetDirectory`, `targetName`; `rebind` must remain `true`. |
| `archive_project` | Archives a project. | `archiveDirectory`, `archiveName`; optional `mode`, `saveBeforeArchive`. |
| `close_project` | Closes the project and clears the session binding. | Optional `saveBeforeClose`. |

Supported archive modes are `None`, `DiscardRestorableData`, `Compressed`, and `DiscardRestorableDataAndCompressed`. Lifecycle tools are single-tool operations and cannot be included in a batch.

Every lifecycle file and directory input (`projectPath`, `projectDirectory`, `targetDirectory`, `archiveDirectory`,
`archivePath`) gets the shared caller-path rules of the file exports (security review 2026-11-03, F5; not live-tested): Win32
device and NT object paths (`\\?\`, `\\.\`, `\??\`), reserved device names (`NUL`, `CON`, `COM1` ... with any extension) and
segments ending in a space or a dot are refused with `validation_error`, on the host (`LifecycleWriteDomain.PathRefusal`) and
again in the worker (`ProjectLifecycleService.RequireCallerPathShape`), before TIA Portal sees the path. UNC shares stay allowed.

### Argument strictness

Lifecycle root arguments are strict. An unknown, missing, mistyped or retired argument is
rejected before the tool runs with the tool's canonical document (`validation_error`,
`isError:true`) and no audit record. JSON `null` is rejected for non-nullable boolean arguments
(`dryRun`, `saveBeforeClose`, `forceRebind`, `rebind`, `saveBeforeArchive`); omit the argument
instead. A retired argument such as a lifecycle `projectPath` (other than on `open_project`) is rejected the same way, with no worker request. Non-nullable string
arguments still accept `null`; see the improvement log.

### Single-call writes and dry runs

Every lifecycle tool accepts `dryRun:bool=false` with its operation inputs. A normal call
applies once its guards pass. The public `confirm` and safety-token inputs are removed.
`dryRun:true` resolves the target and reports effects and every fired guard as `phase:preview`,
without lifecycle mutation, elicitation, or a token. A hard guard is visible in that preview;
an actual call returns `phase:blocked`. Invalid input or an unresolved target still fails a dry run.
A later call resolves fresh state and guards; the preview provides no continuing authorization.

For example, inspect opening a disposable project with:

```json
{"projectPath":"<absolute .ap21 path>","dryRun":true}
```

Apply with the same operation inputs and `dryRun:false` (or omit `dryRun`). Effects name the exact
source and destination and report whether the source will close, be saved, or stay open. Same-path
standalone open is idempotent; an unbound open has no source to close. Same-ALS reuse requires
continuous verification of the same owner and recorded exact opener provenance; it does not
reopen or acquire cold provenance, and actual reuse still follows confirmation/audit rules.
Force-rebinding preserves a UI-owned
source. A worker-owned modified source that would close is always blocked: save or close it explicitly
first. Create and save-as refuse an existing destination directory. Archive requires an existing
output directory outside the source project's folder and descendants.

A worker-owned local session blocks a different open even when clean: only a future explicit
terminal-session operation can relinquish it. A borrowed local source can coexist with an opener
only when exact source/destination preservation is proved; cold/ambiguous ALS provenance blocks
before opening. `forceRebind:true` never overrides these blocks. Recovering a retained empty UI
Portal requires fresh discovery/identity checks; a preview does not adopt/open or close a source.
Generic `close_project` is not a local-session cleanup operation. After an uncertain opener,
inspect exact AMC status and Portal inventory before another intentional request; never replay.

Installed V21 acceptance observed that `create_project` requires no project open in that TIA
process; close the current project explicitly before creating another. Live again on 2026-11-02
(standalone `MCP_Livetest_01` open, one TIA Portal instance, English UI): TIA Portal refused with
"Unable to open project '...'. Another project is already open." (K30). Since then planning
reports `effects.openProject` (create_project only): `source` `binding` (the verified bound project,
read by the lifecycle probe), `portalInventory` (the non-attaching `list_tia_portal_processes` of an
already started worker: the attached Portal, else the only running one) or `notChecked` (with
`note`: no worker started yet, which planning never does, several Portals and none attached, or a
failed listing), plus `portalProcessId`, `openProjectPath` and `isModified`. An observed open project
fires the acknowledge guard `create_project_requires_no_open_project` (below). A failure whose
engineering message (or worker error after "TIA Portal operation failed: ") is exactly that English
message is reported as `engineering_rejected` naming save_project/close_project; when the
destination did not exist before and still does not, the "may already have changed" warnings are
dropped because nothing was created. Another wording (another UI language) keeps the generic
mapping; message text is matched only for this one recognised case. Siemens also rejects archive
of a modified project with `saveBeforeArchive:false`; save first or use `saveBeforeArchive:true`
before archiving. These are typed
attempted-operation failures, so inspect state before a new call.

### Clearer failure texts (audit T6/T9/T11, not live-tested)

- `open_project` (and the open inside `bind_project`/rebind): `ProjectComposition.Open(FileInfo)` documents
  `MissingProductsException`; it now fails `worker_operation_failed` with a text that names the missing products,
  optional packages or support packages and says nothing was opened, plus the Siemens message in `engineeringError`
  (matched by exact full name, `EngineeringFailureHints`). The UMAC open (`UmacDelegate`) and the `Authentication` event stay
  out of this package.
- Any `EngineeringSecurityException` keeps its category and gets a text that names a security refusal and, for
  UMAC-protected projects, the Openness function right (name from a third-party guide, not verified).
- `portal_lost` (a `NonRecoverableException` or a lost Portal) and a worker that stopped during a read no longer say a
  retry is safe: the text says not to repeat the same call unattended and to check TIA Portal and its crash report first
  (the diagnostics folder is named from a third-party guide, not verified). A read timeout says a retry changes nothing in
  the project but may hang TIA Portal again.

### Retrieving an archive (`retrieve_project`)

> **Not live-tested** (GitHub issue #4). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.ProjectComposition.Retrieve(System.IO.FileInfo,System.IO.DirectoryInfo)` ("Retrieves an archived
> project") and `.RetrieveWithUpgrade(System.IO.FileInfo,System.IO.DirectoryInfo)` ("Retrieves a project from an archive and
> upgrades it to the current version"), both returning `Siemens.Engineering.Project`, both documented to throw
> `EngineeringTargetInvocationException` and `MissingProductsException`.

`retrieve_project(archivePath, targetDirectory, upgrade=false, forceRebind=false, dryRun=false)` is a lifecycle tool like
`create_project`: read-write confirms every actual call, full runs under policy, `dryRun:true` previews without retrieving.
`archivePath` must be an absolute existing file (no extension is required: none is documented), `targetDirectory` an existing
absolute directory. A bound session needs `forceRebind:true` (like `open_project`). The worker binds the documented
two-argument overload by exact name and parameter types on the live `ProjectComposition` (no stub; missing:
`api_member_missing`), calls `Retrieve` or, with `upgrade:true`, `RetrieveWithUpgrade`, tracks the returned project as
worker-opened and the host binds to the path TIA Portal reports (it must lie inside `targetDirectory`), then verifies it with
the project status, exactly as after `create_project`. Planning reports `effects.archivePath`, `effects.destinationDirectory`
and `effects.openProject` (as for create_project) and fires `retrieve_project_requires_no_open_project` when a project is
observed open. The recognised refusal "Unable to open project '...'. Another project is already open." (K30/K51) maps to
`engineering_rejected`; because it is not known whether TIA Portal extracted anything before refusing, the "may already have
changed" warnings stay. The three-argument overloads with `UmacDelegate` and the `ProjectOpenMode` overloads are left out
(the UMAC credentials of a protected project are not in scope). **Live acceptance still required:** what TIA Portal creates in
`targetDirectory`, which archive extensions it accepts, the upgrade path from an older version, and the refusal while a project
is open.

### Secondary projects (issue #20)

A second project can be opened read-only next to the bound one (`ProjectOpenMode.Secondary`) through `library_write`
`open_secondary_project` / `retrieve_secondary_project` / `close_secondary_project`, listed by `library_read` `list_secondary_projects`,
copied from with `create_master_copy` `source.project` and compared with `plc_read` `compare_software` `compareTarget: secondary_plc`
([library reference](LIBRARY_OPERATIONS_SUMMARY.md#secondary-read-only-reference-projects-issue-20)). The binding rules do not change: a
project whose `ProjectBase.IsPrimary` reads false is never a binding candidate, and content tools act only on the bound project.
Not live-tested.

### Guards and confirmation

| Guard ID | Operation / consequence | Severity |
| --- | --- | --- |
| `closes_source_project` | Force-rebind open closes a worker-owned source project. | `info` |
| `discards_unsaved_source_changes` | Force-rebind open would close a modified worker-owned source. | `block` |
| `discards_unsaved_changes` | Close with `saveBeforeClose:false` would discard modified project state. | `acknowledge` |
| `archive_without_save` | Archive with `saveBeforeArchive:false` observes modified project state. | `info` |
| `archive_discards_restorable_data` | Archive mode is `DiscardRestorableData` or `DiscardRestorableDataAndCompressed`. | `info` |
| `archive_inside_project_folder` | Archive destination is the project folder or a descendant; TIA Portal decides (block until 2026-11-01). | `info` |
| `target_exists` | Create or save-as destination directory exists; TIA Portal decides (block until 2026-11-01). | `info` |
| `create_project_requires_no_open_project` | create_project while a project is open in the TIA Portal it would use (`effects.openProject`); TIA Portal V21 refused this live on 2026-11-02. Acknowledge, not block: the refusal was observed once (standalone project, one TIA Portal instance); that TIA Portal always refuses (local sessions, other Portal states) is not shown, so TIA Portal decides. The read-write prompt shows it. Not live-tested as a guard. | `acknowledge` |
| `retrieve_project_requires_no_open_project` | retrieve_project while a project is open in the TIA Portal it would use (`effects.openProject`, observed as for create_project); TIA Portal V21 refused opening a second project for create_project and open_project live on 2026-11-02. Acknowledge, not block: TIA Portal decides. Not live-tested. | `acknowledge` |
| `local_session_requires_terminal_operation` | A different open would replace a worker-owned local session, including a clean one. | `block` |
| `local_session_source_preservation_unproved` | A borrowed local source or duplicate destination cannot be proved preserved. | `block` |

Info guards appear in warnings; block guards cannot be overridden in any mode. Every actual
read-write lifecycle call asks once, including info-only calls and calls with no guards. The reply
must be `accept` with boolean `confirm:true`. Unsupported capability, decline, cancel, timeout,
transport failure, or acceptance without that boolean denies with `access_denied`. Full satisfies
acknowledge guards by policy and does not elicit. Block guards and dry runs never prompt. After
acceptance the server re-resolves targets/effects/guards; changes cannot expand the accepted action.

For an intentional dirty discard, use `{"saveBeforeClose":false}`; confirmation follows the mode.
The former agent confirmation array is removed. Dry runs report acknowledge guard
`acknowledged:false` in read-write and `true` in full, with null guard audit satisfaction and
`confirmation:none/not_requested`. Applied satisfaction is `user` or `policy`.
Client-returned acceptance does not prove that a human saw a dialog.
Read-write refusal after an elicitation response retains `confirmation:user/declined`; previews
and pre-prompt guard denials use `none/not_requested`. Siemens dialogs are separate from MCP
elicitation; full policy confirmation does not establish absence of vendor UI.

### Structured lifecycle response

All seven tools declare a concrete output schema with `tool`, `contractVersion:"1.0"`, `success`,
`error`, `warnings`, `phase`, `guards`, `effects`, `result`, and `verification`. One canonical
serialization supplies both MCP text and `structuredContent`. Read objects directly; the old
`operationResult` and `verification.result` JSON strings are removed.

`phase` is `preview`, `applied`, `blocked`, or `error`. `effects` is one typed object, with
`sourceProjectPath`, `destinationProjectPath`, `destinationDirectory`, `sourceStatus`,
`sourceOpenedByWorker`, `willCloseSource`, `savesSource`, `rebindsToDestination`, `targetExists`,
`archiveMode`, and `archivePath`. Null members retain unavailable or inapplicable evidence;
source/destination fields distinguish the current project from a requested copy or new project.

`result` and `verification` use typed outcomes with `status`, `value`, `failure`, and `omission`.
`result.value` contains the lifecycle root (`success`, `operation`, `projectPath`, and basic
`project` status). Verification is null for previews, rejections, and mutation failures; otherwise
its value contains basic
`ProjectStatusInfo`, without enumerating extended metadata. Open/create verify the bound destination;
save/archive verify the source; save-as verifies its rebound copy; close verifies no open project.
Guard `acknowledged` is a boolean for acknowledge guards and explicit null for info/block guards.

Pre-mutation rejection sets `success:false`, top-level `{category,message}` `error`, and MCP
`isError:true`. An attempted mutation or verification failure keeps `error:null`, `isError:false`,
and `success:false`, with typed failure evidence. A successful mutation remains visible if its
verification failed; this is not a safe-replay result. Malformed worker successes become bounded
`protocol_error` outcomes without echoing the rejected payload.

Each result/verification value is limited to 60,000 canonical characters and the complete document
to 180,000. Oversized evidence is omitted whole, with `status:omitted`, `value:null`, and measured
`omission` guidance (`resultExceededItemCharLimit` for the value limit,
`responseExceededDocumentCharLimit` for the total limit). An omission alone does not turn successful execution/verification into a
failure, so `success:true` can accompany omitted evidence. Strict payload validation runs before
these budgets. Follow `retryTool:get_project_status` or inspect the destination artifacts to recover
evidence; do not repeat a mutation merely because its returned value was omitted. A large source
status or the complete effects object may become null with an explicit warning. When oversized
guard messages would exceed the document cap, they are replaced with an explicit evidence-omission
marker alongside the catalog's consequence description; guard IDs, severities, operation IDs,
acknowledgement state, phase, and verdict remain intact. Whole
warning entries can be removed with an omission-count notice. The server never returns partial
JSON or presents shortened path evidence as a complete target identity. Inspect current project
status and destination artifacts when the returned evidence is omitted.
Failure prose can be bounded as a last resort with a shortening notice; its category and the
response's rejection-versus-attempted-failure classification remain intact.

Each call appends one guarded-write audit record, including previews and refusals, to
`%LOCALAPPDATA%\TiaMcpServer\audit\writes-yyyy-MM-dd.jsonl`. The record retains the exact returned
canonical document/hash, requested operation, prepared binding, target, guards, and acknowledgement
provenance. Audit v2 records call confirmation by `user`, `policy`, or `none`. No tool adds or consumes a safety token: Network, PLC, HMI and VCI writes use the same guarded pipeline and
audit stream, and the legacy write audit was retired.

Migrating from the retired token flow: remove client preview/token/apply loops for these
six tools, remove old public confirmation/token arguments and agent confirmation arrays, use
`dryRun` as needed, and read typed outputs. The removed startup switch is rejected with:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

The valid [2026-10-01 lifecycle matrix](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md)
was invalidated for this candidate by the tier/confirmation/binding change and replaced by the
[2026-10-03 live run](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md).
All three functional groups passed; the maintainer reported no new TIA dialog in read-only,
completing the separate human observation. The report preserves runtime/client/artifact/restoration limits and exclusions.

### MCP client hints

All seven lifecycle tools advertise mutating MCP hints: `readOnlyHint: false` and
`destructiveHint: true`. `open_project` advertises `openWorldHint: true` because opening a local
session can contact its Project Server; the other five advertise `openWorldHint: false`.
These client-facing hints do not bypass the safety model. A client may still show its own permission prompt for a destructive
tool, including a dry run. Server elicitation asks once per actual read-write lifecycle call.

## Safety and session binding

- Binding preparation and mutation/verification/audit use one pinned revision; stale revisions fail closed instead of redirecting an operation to another project.
- Open/create may start unbound. Other lifecycle writes require the appropriate active source; recovery grounds that exact source before proceeding.
- `save_project_as` requires rebinding because Siemens `SaveAs` switches the active project to the copy.
- Archive output is rejected when the archive directory is inside the project folder.
- After a timeout or worker crash, inspect the current project state before deciding whether another call is safe; the server does not automatically retry lifecycle writes.
- `get_project_status` takes no arguments, is non-binding and never switches. Content tools act on the bound project only; with no verified binding (a configured `--project` is grounded by status first) they fail closed with `binding_conflict`; call `bind_project` first. Use `bind_project` to select an already-open project, or `open_project` to open one deliberately.

## Current limits

The current project surface does not provide:

- `OpenWithUpgrade`, `Retrieve`, `RetrieveWithUpgrade`, or project deletion.
- Project language settings and multilingual text import/export (reading is exposed through `get_project_status` metadata; editing is not).
- Full project attribute editing (copyright, family, comment, and language settings are read-only today).
- UMAC delegates, authentication events, or explicit primary/secondary `ProjectOpenMode` selection.
- Portal settings, diagnostics settings, or search-index administration.
- VCI library scopes, connecting unlinked files, workspace groups and VCI settings. Standalone
  workspace, mapping, export and sync operations are in the [VCI reference](VCI_OPERATIONS_SUMMARY.md).
- Multiuser server mutation, markings, discard and commit. Delivered local
  selection/open/status, content reads/writes, compile, local save and evidence limits are in the
  [Multiuser reference](MULTIUSER_OPERATIONS_SUMMARY.md).
