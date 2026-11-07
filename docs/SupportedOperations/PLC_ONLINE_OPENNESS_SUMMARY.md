# PLC online and offline Openness operations (compare, online state, station upload, watch/force tables)

**Language:** English · [Deutsch](PLC_ONLINE_OPENNESS_SUMMARY.de.md)

This reference covers the Openness V21 features this fork adds around the PLC online workflow:

- online/offline compare and software compare (`plc_read` `compare_to_online`, `compare_software`) and the hardware compare
  (`compare_hardware`, `compare_hardware_to_online`; issue #7);
- the online state and going online/offline (`plc_read` `get_online_state`, tool `plc_tia_online`);
- station upload into the offline project (`plc_read` `list_station_upload_interfaces`, tool
  `plc_station_upload`);
- watch and force tables (`plc_read` `list_watch_and_force_tables`, `read_watch_table`,
  `read_force_table`, `get_watch_table_content`, `get_force_table_content`; `plc_write`
  `create_watch_table`, `delete_watch_table`, `create_watch_table_group`, `delete_watch_table_group`,
  `update_watch_table_content`, `update_force_table_content`);
- the offline instruction upgrade `plc_write` `update_program`.

> **Status: not live-tested.** These features are offline-qualified only: built against the
> compile-only reference stubs, unit-tested on the Siemens-free logic, and exercised end to end
> through the scripted FakeWorker (`plc-online-roundtrip`, `plc-online-confirmation`, `plc-online-tls`). Nothing here has run against TIA Portal.
> The mechanical signature check is `scripts/verify-reference-stubs.ps1` (which compiles the probe
> `reference-stubs/TiaMcpServer.OpennessReferenceProbe/OnlineReferenceSurface.cs`) plus a Windows
> build of the worker and the probe against the installed
> `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`. Every member marked
> *assumed* in the [provenance table](#provenance-of-every-siemens-member-used) must pass that
> real-DLL compile before any live test; the first live checks are listed under
> [live acceptance checks](#live-acceptance-checks).

Tool counts: 13 tools in read-only, 32 in read-write and 32 in full since `siemens_docs` (31 since `tia_inbox`, 30 since `retrieve_project`, 29 since the library tools, 27 when these tools were added; `plc_tia_online` and
`plc_station_upload` are the two writable tools added here; every other operation lives in
`plc_read`/`plc_write`).

**Not the Web API tools.** `plc_online_read`/`plc_online_write` ([Web API reference](PLC_ONLINE_WEBAPI_SUMMARY.md))
talk to a configured S7-1500 CPU over HTTPS from the host; everything here runs through TIA Portal Openness in the
worker on the bound project. The connection tool is named `plc_tia_online` (TIA Portal's online connection to a PLC)
rather than `plc_online_connection`, because `plc_online_*` already names the Web API tool family (AGENTS project
table, installation guide, test harness and a prefix-selecting conformance test, which immediately misclassified
`plc_online_connection` as a Web API tool).

## Access and confirmation

The owner decision for this fork: the AI may do everything the PLC engineer can do in TIA Portal,
controlled by the access mode. Read-only is observation; read-write handles project changes as the
other domain writes do (no prompt) and confirms every online or lifecycle action through
elicitation; full runs everything without prompts. Nothing is hard-blocked except invalid or
ambiguous input.

| Operation / tool | Worker method | Capability | read-only | read-write | full |
| --- | --- | --- | --- | --- | --- |
| `plc_read` `compare_software`, `compare_hardware`, `get_online_state`, `list_station_upload_interfaces`, `list_watch_and_force_tables`, `read_watch_table`, `read_force_table` | same name | `Observe` | yes | yes | yes |
| `plc_read` `get_watch_table_content`, `get_force_table_content` | same name | `TemporaryExport` | yes | yes | yes |
| `plc_read` `compare_to_online`, `compare_hardware_to_online` | same name | `OnlineObserve` (new) | yes | yes | yes |
| `plc_write` watch/force table writes, `update_program` | same name | `ProjectMutation` | no | yes, no prompt | yes |
| `plc_tia_online` (`go_online`, `go_offline`) | `plc_go_online`, `plc_go_offline` | `OnlineControl` | no (not listed) | yes, one prompt per actual call | yes, policy |
| `plc_station_upload` | `plc_station_upload` | `ProjectMutation` | no (not listed) | yes, one prompt per actual call | yes, policy |

### Why `compare_to_online` has its own capability

`PlcSoftware.CompareToOnline()` reaches the target device over the network but changes neither the
device nor the project. The repository's `Observe` was defined as "inspect project data without side
effects", so a network connection to a real CPU does not fit it cleanly; the owner decision says
read-only means observation, which it is. `OperationCapability.OnlineObserve` is therefore appended
last (numeric values of the existing capabilities stay stable) and allowed in every mode. Keeping it
apart from `Observe` makes the network reach explicit in the policy catalog and lets a later policy
tighten it without touching offline reads. `plc_read scan_accessible_devices`
(`ConfigurationPcInterface.GetAccessibleDevices()` on an explicitly named PC interface, live network
discovery, no change) uses the same capability and, like `compare_to_online`, a fixed-No confirmation
scope (a declined dialog fails it); see [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#accessible-devices-scan_accessible_devices).
It counts as non-mutating for the failure classification
(a Siemens `EngineeringTargetInvocationException` is `worker_operation_failed`, never
`engineering_rejected`), and it requires the verified session identity like every content read.

The compare applies no connection path. If the PLC was not online before and is `Online` or
`Connecting` after the compare, whether the compare returned or threw, the worker calls `GoOffline()`
in a `finally` path to restore the earlier state (the source guidance: go offline only for a connection
this application established) and waits up to 10 s for `Offline` (Siemens-free core
`PlcCompareOnlineRun`). `restoreAttempted` says whether it ran; `disconnectedAfterCompare` is true only
when `Offline` was observed within the wait, so a failed or timed-out restore reports `false` with the
final state in `onlineStateAfter` (and a warning: go offline with `plc_tia_online`). While the compare
runs, a fail-closed `OnlineLegitimation` handler is registered on the PLC's connection configuration
(`OnlineProvider.Configuration`) and removed in `finally`: it answers no authentication or password
prompt, and a TLS certificate check (`TlsVerificationConfiguration`) only from the item's
`trustedCertificates` (see [TLS certificate trust](#tls-certificate-trust-trustedcertificates)). Every
presented configuration is recorded in the result's `configurationsSeen`; any refused prompt discards the
compare (`worker_operation_failed`) and the failed item's `result` carries the evidence
(`configurationsSeen`, `abort` with `tls`, the online states; `root` null). Whether `CompareToOnline()`
opens a connection itself, needs an existing one, or raises legitimation through that event at all is not
documented in the sources; go online with `plc_tia_online` first for a deterministic path. Whether TIA
Portal keeps a trust answered during a compare after the call is not documented either.

### Why `plc_station_upload` confirms every call

Station upload is a `ProjectMutation` (it adds a device to the offline project), so it is registered
with the other writable tools and denied in read-only. It also connects to a device at a caller
address, which is an online action under the owner decision, so it confirms every actual read-write
call like `plc_tia_online` and `plc_download` (`ConfirmsEveryCall = true`). The prompt names the address,
mode, PC interface, the consequence (a new, unsaved device) and exactly which callback configurations
the caller answers.

## `plc_read` operations

All require a verified binding. The compare, online-state and upload-interface reads are
standalone-only (like `list_download_targets`); the watch/force table reads are project content and
also work on Multiuser local sessions.

| Operation | Fields | Result |
| --- | --- | --- |
| `compare_to_online` | optional `plcName`, `maxDepth` (1-64, default 16), `maxElements` (1-5000, default 300), `differencesOnly` (default true), `trustedCertificates` (SHA-256 fingerprints the user approved, see [TLS certificate trust](#tls-certificate-trust-trustedcertificates); not live-tested), `passwords` (since 2026-11-01: online password configurations such as `OnlineReadAccessPassword` by type short name → password source, never a value; omitted entries from the server password map; see the [password sources guide](../guides/password-sources.md)) | `PlcCompareResultInfo`: `compareTarget: "online"`, `onlineStateBefore`, `onlineStateAfter`, `restoreAttempted`, `disconnectedAfterCompare`, `rootState`, `identical`, `root`, caps, `elementsListed`, `identicalElementsSkipped`, `complete`, `omission`, `configurationsSeen` (the online legitimation configurations presented, with `tls` for a TLS check), `abort` (always null in a delivered compare; set only in the failed item's evidence) |
| `compare_software` | `compareTarget` `plc` (with `targetName` = other PLC), `project_library`, or `master_copy` (with `targetName` = master copy in the project library's root master copy folder), or `secondary_plc` (with `targetName` = a PLC of the open secondary project `targetProject`; issue #20, not live-tested); optional `plcName` and the caps above; `trustedCertificates` is refused | the same shape; online members are null, `configurationsSeen` is empty and `abort` null |
| `compare_hardware` | `compareTarget` `plc` (with `targetName` = other PLC) or `master_copy` (with `targetName` = master copy in the project library's root master copy folder); optional `plcName`, `hardwareScope` (`device`, default: the PLC's station; `cpu`: its CPU device item; for `plc` the other PLC's object of the same scope) and the caps above; `trustedCertificates` is refused | the same shape as `compare_software`; see [Hardware compare](#hardware-compare) |
| `compare_hardware_to_online` | optional `plcName`, `hardwareScope`, the caps, `trustedCertificates`, `passwords` (exactly as `compare_to_online`) | the same shape as `compare_to_online` |
| `get_online_state` | optional `plcName` (omitted lists every PLC) | `{ plcs[{ plcName, deviceName, onlineProviderAvailable, state, isConfigured, modes[{ name, pcInterfaces[{ name, number, targetInterfaces[{ name }] }] }] }] }` from `OnlineProvider.State`, `Configuration.IsConfigured` and `Configuration.Modes` (offline; never `GetAccessibleDevices()`) |
| `list_station_upload_interfaces` | none | `{ available, modes[{ name, pcInterfaces[{ name, number, targetInterfaces: [] }] }] }` from `StationUploadProvider.Configuration` |
| `list_watch_and_force_tables` | optional `plcName` | per PLC: `groups` (user group paths), `watchTables` and `forceTables` `{ groupPath, name, isConsistent }`, `complete` (5,000 entries at most) |
| `read_watch_table` / `read_force_table` | `tableName`, optional `plcName`, `folderPath` (group path, `/` default), `maxElements` (1-5000, default 1000) | the table with `entries[{ index, name, address, displayFormat, monitorTrigger, modifyIntention, modifyTrigger, modifyValue, unreadableAttributes }]` (force: `forceIntention`, `forceValue` instead of the modify members), `entryCount`, `entriesComplete` |
| `get_watch_table_content` / `get_force_table_content` | `tableName`, optional `plcName`, `folderPath` | `{ format: "xml", content, contentHash, warnings }`: the table's SimaticML (`Export(FileInfo, ExportOptions.None)` into a temporary file, `DocumentInfo` removed like block exports) |

The compare tree is projected depth-first in document order. Each element is
`{ leftName, rightName, comparisonResult, detailedInformation, elements[], elementsTruncated }`;
texts are cut at 1,000 characters. With `differencesOnly` an element whose state is
`ObjectsIdentical` or `FolderContentsIdentical` is skipped and counted (the root is always listed).
When the depth or element cap stops the walk, `complete` is false, `omission` says which cap, and
every cut parent has `elementsTruncated: true`. `identical` is true when the root state is one of the
two identical states. A result over the value budget is omitted whole with guidance (lower the caps).

### Hardware compare

> **Not live-tested** (GitHub issue #7). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.HW.HardwareObject.CompareTo(Siemens.Engineering.HW.IHardwareCompareTarget)` ("Compare the hardware object
> vs the given target", returns `Siemens.Engineering.Compare.CompareResult`) and `T:Siemens.Engineering.HW.IHardwareCompareTarget`
> ("Access to the device/device item in a HW compare scenario", no members). The XML lists no inheritance, so which types implement
> the interface is not documented.

`compare_hardware` (`Observe`) and `compare_hardware_to_online` (`OnlineObserve`, every mode, like `compare_to_online`) compare the
PLC's station (`hardwareScope:"device"`, the `Device` above its CPU device item) or its CPU device item (`"cpu"`); both are
`HardwareObject`s. Worker `Openness/PlcOnline/PlcHardwareCompareService.cs`; the result is the same bounded
`PlcCompareResultInfo` tree as the software compares.

- **Binding.** `CompareTo` is not in the compile stubs: it is bound on the live object by name and an argument assignable from the
  target's runtime type, and the bound parameter must be `IHardwareCompareTarget`. When the object offers that `CompareTo` but
  the target does not implement the interface, the item fails `unsupported_capability` naming the target's runtime type, before
  anything is compared (offline) or contacted (online); no `CompareTo` at all is `api_member_missing`.
- **Offline targets.** `plc`: the other PLC's station or CPU (`targetName` must not resolve to the compared PLC); `master_copy`: the
  root master copy of that name (`MasterCopyFolder.MasterCopies.Find`), compared when TIA Portal's master copy implements the
  interface. The project library itself is not offered (a library is no hardware object). In a project with only one PLC
  (K81, live 2026-11-03) `plc` has no target, so the only offline comparison is against a master copy: put the station or CPU
  into the project library first (`library_write create_master_copy`), or use `compare_hardware_to_online` against the device.
- **Online target.** The CPU's `OnlineProvider` (`GetService<OnlineProvider>()` on the CPU device item) is passed as the target
  only when it implements `IHardwareCompareTarget` at runtime; the check runs before the legitimation handler is attached. A PLC
  without an online provider is `unsupported_capability`. The run is the one of `compare_to_online`
  (`PlcCompareService.RunOnline`): fail-closed legitimation handler (passwords only from `passwords` and the server password map,
  a TLS check only from `trustedCertificates`, never `NonVerified`: the K52 PLCSIM exception does not apply to compares), TIA
  Portal confirmation dialogs answered No, and `Offline` restored with `GoOffline()` when the compare left a connection it opened;
  a refused prompt discards the result with the same evidence.
- **Open question for the live test.** If TIA Portal's online hardware compare target is another object than the `OnlineProvider`,
  the online operation fails `unsupported_capability` without contacting the device; no other object is guessed.

Watch and force table entries are read schema-free: the V21 reference names the `Entries` property
but documents no entry composition type, so the worker reads `Entries` by reflection on the runtime
object (the Siemens object-tree-walking pattern) and each entry's documented attributes through
`IEngineeringObject.GetAttribute(name)`. Strings are returned as text, enums by member name, booleans
as booleans; an attribute that throws or returns a non-scalar is listed in `unreadableAttributes`
and its member is null.

## `plc_write` operations

Same pipeline, ordering, 50-item, stop-at-first-failure and no-rollback rules as every `plc_write`
item, no prompt in read-write. Group paths use `folderPath` (`/` or omitted for the system group).

| Operation | Fields | Siemens call | Notes |
| --- | --- | --- | --- |
| `create_watch_table` | `tableName`, optional `plcName`, `folderPath` | `group.WatchTables.Create(name)` | the group must exist (planned or in the inventory); collisions are Portal's rejection |
| `delete_watch_table` | `tableName`, optional `plcName`, `folderPath` | `PlcWatchTable.Delete()` | info guard `plc_deletes_watch_table` |
| `create_watch_table_group` | `folderPath` (the new group, e.g. `/Line/Cell`), optional `plcName` | `parent.Groups.Create(name)` | the parent must exist |
| `delete_watch_table_group` | `folderPath` (the group), optional `plcName` | `PlcWatchAndForceTableUserGroup.Delete()` | the system group cannot be deleted; info guard lists the tables and groups removed |
| `update_watch_table_content` / `update_force_table_content` | `tableName`, `content`, `expectedContentHash`, optional `plcName`, `folderPath` | `group.WatchTables.Import(file, ImportOptions.Override)` / `group.ForceTables.Import(...)` | `content` is the edited SimaticML from `get_*_table_content`; the hash is checked at planning and again by the worker before the import (`state_changed` when stale); before the import the document must name exactly the addressed table (one table element of the same kind and `AttributeList/Name`, shape taken from the worker's own export of that table, fail closed when the export is unrecognisable; DTDs refused), otherwise `validation_error` and nothing is imported; the import must also return exactly the addressed table, otherwise `postcondition_failed` (the project was changed) |
| `update_program` | optional `plcName` | `PlcSoftware.UpdateProgram()` inside `TiaPortal.ExclusiveAccess` and a project `Transaction` (source a: "within the normal ExclusiveAccess/transaction workflow") | upgrades block instructions to the latest version the PLC firmware supports, offline; invoked by exact runtime lookup (its return type is undocumented); the returned object's public scalar members are read (`returnedMembers`, `returnedType`): a failing state, error-count or success member fails the call and leaves the transaction uncommitted (rolled back); a member that says success gives `outcome: "succeeded"`; nothing interpretable (including a void return) gives `outcome: "uninterpreted"` with a warning (committed, not verified: compile and inspect); the commit is requested only when `Transaction.CanCommit` is true, otherwise the call fails and says the change was rolled back (audit T5, not live-tested). Live-run 2026-11-07 (build 986928f, CPU 1516-3 PN/DP V2.9): the call ran and reported `outcome: "uninterpreted"`, as designed |

Planning reads the PLC's watch-and-force table inventory once per call (only when an item needs it),
resolves update/delete targets (`target_not_found`/`target_ambiguous`), applies earlier items so later
ones see them and carry `dependsOn`, and blocks with `plc_state_unverifiable` when the inventory hit
its cap. Content updates are verified by a re-export (`contentHash`, usable as the next
`expectedContentHash`); the other items are `trusted`.

**Entries are added, changed and removed only through the SimaticML round trip.** The V21 reference
documents no entry composition type, no entry `Create` and no entry `Delete` (only the comment-entry
composition has a `Create`, without documented parameters), so the tool offers no typed entry edits.
Force tables are edited offline only: the reference documents no `Create` on the force table
composition, no `Delete` on a force table, and **no online force API**, so nothing here forces or
modifies values online. (`WatchAndForceTableAccessManager` and the access-rule types in the V21
Step7 module describe web-server access to tables, not forcing.)

## `plc_tia_online`

Registered in read-write and full only; standalone projects only.

| Argument | Default | Meaning |
| --- | --- | --- |
| `action` | required | `go_online` or `go_offline` |
| `plcName` | none | PLC software or device name; optional when the project has exactly one PLC |
| `pcInterface` | none | `go_online` only, **required** `{ name, number }` as `get_online_state` lists it; an explicit `null` is an argument-shape error. No interface is chosen by default, hardware interfaces included. |
| `targetInterface` | none | `go_online` only; optional when the PC interface offers exactly one |
| `mode` | `PN/IE` | `go_online` only |
| `timeoutSeconds` | 30 | 1-120; polling budget for `Online`/`Offline` |
| `passwords` | none | `go_online` only: `{ "<configuration type short name>": "<NAME of an environment variable of the server process>" }`; never a password (see [argument maps](#argument-maps-and-tia-portal-confirmations)) |
| `portalConfirmations` | `decline` | both actions: `decline` or `accept`, see [TIA Portal confirmations](#argument-maps-and-tia-portal-confirmations) |
| `trustedCertificates` | none | `go_online` only (`go_offline` refuses it): SHA-256 fingerprints of the device's TLS certificates that the user approved, see [TLS certificate trust](#tls-certificate-trust-trustedcertificates); omitted or `null` trusts nothing |
| `dryRun` | false | resolve the path and guards without connecting or prompting |

`go_online` plans from `get_online_state`: exact ordinal match of mode, PC interface (name and
number) and target interface, no fallback. The worker re-resolves the same names on the **same
`OnlineProvider` instance** that applies them (target interfaces are bound to the instance they were
enumerated from), registers a fail-closed `OnlineLegitimation` handler, calls
`ConnectionConfiguration.ApplyConfiguration(target)` and, if it returned true, `GoOnline()`, then polls
`OnlineProvider.State` every 200 ms until `Online`, a terminal state (`Incompatible`, `NotReachable`,
`Protected`) or the timeout, because `GoOnline()` returns before the connection is established. A PLC
that is already `Online` is left untouched (`ApplyConfiguration` throws while a connection is active):
the result reports `outcome: "already_online"`, `path: null` and `pathVerified: false` (nothing was applied, and
the existing connection is not verified against the requested path) with a `note` warning. A PLC in
`Connecting` or `Disconnecting` fails `state_changed` before anything is applied. `go_offline` calls
`GoOffline()` (no path needed) and polls for `Offline`; an already offline PLC is reported without a
call.

Online legitimation is answered only by the caller: password configurations by `passwords` entries
(`SetPassword(SecureString)` from the named environment variable, the value never recorded), the TLS
certificate check (`TlsVerificationConfiguration`) only by `trustedCertificates` (`Trusted` when every
presented certificate's SHA-256 fingerprint is listed, otherwise `NonTrusted` and abort with the
certificate evidence; see [TLS certificate trust](#tls-certificate-trust-trustedcertificates)). Every
other presented configuration (for example `OnlineAuthenticationConfiguration`) aborts the connection;
the result's `configurationsSeen` and `abort` name it with its kind and, for a selection, its
`availableSelections`. The prompt in read-write lists the password keys and every trusted fingerprint.

Guards (informational, the prompt names them): `online_hardware_target` when the PC interface is not
`PLCSIM`; `online_disconnects` for `go_offline` (it also closes a connection opened in the TIA Portal
UI).

Result value `PlcOnlineActionResultInfo`: `action`, `plcName`, `deviceName`, `path` (only when a path was applied),
`outcome` (`connected`, `already_online`, `configuration_not_applied`, `not_reached`; go_offline: `disconnected`,
`already_offline`, `not_reached`; null in failure evidence), `pathVerified` (true only for `connected`), `stateBefore`,
`stateAfter`, `goOnlineReturned`, `configurationApplied`, `goalReached`, `timedOut`, `timeoutSeconds`,
`elapsedMilliseconds`, `statesObserved` (distinct consecutive states, at most 50),
`configurationsSeen`, `abort`, `note`, `portalConfirmationPolicy`, `portalConfirmations`,
`portalConfirmationsObserved`, `portalConfirmationsComplete`. Root `success` is true only when the goal state was
reached, nothing was aborted and no TIA Portal confirmation was declined. A goal not reached is `phase: applied`, `success: false`, `isError: false`, the
item `status: failed` (`worker_operation_failed`, naming the final state) with the evidence in
`result.value`; TIA Portal may still be connecting after a timeout (go offline to cancel).

## `plc_station_upload`

Registered in read-write and full only; standalone projects only.

| Argument | Default | Meaning |
| --- | --- | --- |
| `address` | required | device address created on the PC interface (`Addresses.Create(address)`), e.g. `192.168.0.1`; nonblank, at most 64 characters, no control characters or surrounding spaces; the format is TIA Portal's to judge |
| `pcInterface` | required | `{ name, number }` as `list_station_upload_interfaces` lists it |
| `mode` | `PN/IE` | exact connection mode |
| `decisions` | none | `{ "<configuration type short name>": "<enum member name>" }`, e.g. `{ "UploadMissingProducts": "NoAction" }` |
| `passwords` | none | `{ "<configuration type short name>": "<environment variable NAME>" }`, e.g. `{ "ModuleReadAccessPassword": "PLC_READ_PW" }` |
| `portalConfirmations` | `decline` | `decline` or `accept`, see [TIA Portal confirmations](#argument-maps-and-tia-portal-confirmations) |
| `trustedCertificates` | none | SHA-256 fingerprints of the device's TLS certificates that the user approved, see [TLS certificate trust](#tls-certificate-trust-trustedcertificates); omitted or `null` trusts nothing |
| `dryRun` | false | resolve the path and guards without connecting or prompting |

The worker resolves mode and PC interface exactly on the same `StationUploadProvider` that uploads,
creates the address, and calls `StationUpload(address, callback)` (never inside a transaction). Right
before `StationUpload` it subscribes an `OnlineLegitimation` handler on the same provider's
`Configuration` (the `ConnectionConfiguration` instance that resolved the path) and removes it in
`finally`. The upload callback and the legitimation handler share one Siemens-free `PlcCallbackRecorder`,
so both kinds of configuration land in one `configurationsSeen` list with one `abort`:

- the canonical type is the first public type in the configuration's runtime type chain whose
  namespace is `Siemens.Engineering.Upload.Configurations` or (online legitimation)
  `Siemens.Engineering.Online.Configurations`; its short name is the decision key;
- the TLS certificate check (`TlsVerificationConfiguration`) is answered only from `trustedCertificates`
  (never by a decision; a `decisions` or `passwords` key `TlsVerificationConfiguration` is a
  `validation_error`);
- a selection configuration (exactly one public readable and writable enum `CurrentSelection`) is
  answered only by a `decisions` entry whose value is an exact member name of the live enum;
- a password configuration (exactly one public `SetPassword(SecureString)`) is answered only by a
  `passwords` entry naming a set, non-empty environment variable;
- anything else aborts: no entry (`decision_missing`), an entry of the wrong kind
  (`decision_not_applicable`), a non-member selection (`selection_unavailable`), an unset variable
  (`password_unavailable`), a throwing answer (`answer_rejected`; a lost Portal keeps `portal_lost`),
  an unanswerable or foreign type (`unsupported_configuration`), more than 100 configurations
  (`configuration_limit_exceeded`), a TLS certificate not approved (`certificate_not_trusted`), a password
  configuration whose `IsSecureCommunication` is false or unreadable without `allowInsecurePasswordTransfer: true`
  (`insecure_communication`, audit T1, not live-tested: the password is not sent; the flag is recorded as
  `secureCommunication`; `go_online` and `plc_station_upload` take the opt-in, `compare_to_online`/`compare_hardware_to_online`
  do not, see the left-out list). After an abort every later configuration is recorded and throws.
- audit T2 (not live-tested): a selection configuration that would abort with `decision_missing`, `selection_unavailable`
  or `decision_not_applicable`, or arrives after the abort, and whose live enum has the exact documented member
  `NoAction` (e.g. `UploadMissingProducts`) is answered `NoAction` and recorded as (or after) the abort WITHOUT a throw,
  like the refused TLS question (K27); the call still fails with the recorded abort. Everything else still throws.

The abort record carries the `decisionKey`, `kind` and `availableSelections`, so a retry can supply the
exact decision. No check (`true`) decisions are accepted (`validation_error` before anything runs): the
V21 upload namespace documents no check configuration.

Result value `PlcStationUploadResultInfo`: `address`, `path`, `state` (`UploadResult.State` by name),
`errorCount`, `uploadedStationName`, `messages` (schema-free: readable scalar attributes through
`GetAttributeInfos`/`GetAttribute`, nested messages through enumerable engineering-object
properties; at most 8 levels, 500 messages, 20 attributes each), `messagesComplete`,
`messageOmission`, `configurationsSeen`, `abort`, `unusedDecisions`, `projectModified: true` and the four
`portalConfirmation*` members. Success requires a returned result, a state other than `Error`, `errorCount` 0, an
uploaded station, no abort and no declined TIA Portal confirmation; a success warns that the project is not saved (`save_project`), a failure that a partly
uploaded station may have been added (inspect with `browse_project_tree`). Guard
`station_upload_adds_device` (info) names the consequence. The host rejects (`protocol_error`)
evidence whose recorded answers are not exactly the caller's entries or that describes another path.

**Fail-safe CPU (live finding 2026-11-02, TIA Portal V21, CPU 1516F-3 PN/DP on PLCSIM).** TIA Portal uploads an F-CPU,
in the UI and through Openness alike, only when the option "Konsistentes Laden von der F-CPU ermöglichen" in the Safety
Administration is enabled and has been downloaded to the CPU. Without it TIA Portal refuses the upload ("Das konsistente
fehlersichere Laden ... ist nicht aktiviert"). With it, `plc_station_upload` succeeded with 0 errors, and the F-signatures
of the uploaded station were identical to the source (0x455072D1 / 0xB241A1AD / 0x930ED124). `plc_read`
`get_safety_administration` does not report this option: it was not visible through the V21 API as read.

## Argument maps and TIA Portal confirmations

`passwords` (both tools) and `decisions` (`plc_station_upload`) bind through `plc_download`'s argument maps
(`PlcDownloadPasswordMap`, `PlcDownloadDecisionMap`): a duplicate key or a non-scalar value is an argument-shape
error, and a `passwords` value that is not an environment variable name (for example a pasted password), or a
valid name whose variable is not set in the server's environment, is replaced by a redaction placeholder while
binding, so it never reaches the pipeline, the response or the audit (the guarded pipeline audits the requested
items) and validation refuses it without repeating it. A name is recorded only when its variable is set; the
worker's `password_unavailable` detail never repeats the name either. A `decisions` value `true` (a check)
is bound but refused by `plc_station_upload`. A `decisions` or `passwords` key `TlsVerificationConfiguration`
is refused by every tool (`validation_error`: a device certificate is trusted only through
`trustedCertificates`).

`portalConfirmations` works exactly like `plc_download`'s (contract 2.0) and reuses its types: the worker opens
`TiaPortalSession.AnswerConfirmations` around the action, so every TIA Portal `Confirmation` dialog raised until the
action's evidence is collected is answered by the policy and recorded (`PlcDownloadPortalConfirmationLog`, at most
100 listed, all counted; type and the dialog text when readable):

- `decline` (default): answered No; any declined dialog fails the call (`status: failed`,
  `worker_operation_failed`) with the evidence in `result.value.portalConfirmations`;
- `accept`: answered Yes (never in read-only, where the session refuses every confirmation) and recorded; the call
  does not fail because of it and carries a warning.

A dialog raised after the scope closed (for example while a timed-out `go_online` is still connecting) is not in the
call's evidence, but it keeps the call's answer: the scope lingers until the next worker request starts, logs each late
dialog at once and reports them as a warning on that next response. `go_online` also leaves a fail-closed
`OnlineLegitimation` handler registered until then, which refuses (and reports) every late password, TLS or
authentication prompt. The confirmation prompt
in read-write states the policy. `compare_to_online` opens the same scope with a fixed No (a read never accepts a
dialog, so it takes no option): a declined dialog discards the compare and fails the item with the dialog types and
texts in the message; go online with `plc_tia_online` first and answer such dialogs there. The host checks the
evidence like the download does (policy equals the request, counts consistent, every answer matches the policy)
and rejects anything else as `protocol_error`. The evidence of every online action still travels on
`WorkerResponse.PlcDownloadOutcome` as payload text; each domain decodes it with its own strict contract, so
online-action evidence never decodes as download evidence (tested).

## TLS certificate trust (`trustedCertificates`)

> **Not live-tested.** Added after a live finding with `plc_download` (TIA Portal V21, PLCSIM Advanced 8.0:
> the connection failed while TIA Portal's own UI first asks whether to trust the device's unknown
> self-signed certificate). The same rules apply to `plc_download` (see the
> [download reference](PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)). Offline
> qualification only: unit tests of the Siemens-free rule and recorders (`PlcTlsTrustPolicyTests`,
> `PlcCallbackRecorderTlsTests`) and the FakeWorker scenarios `plc-online-tls` and `plc-download-tls`
> (`PlcTlsTrustMcpTests`), which present a stand-in `TlsVerificationConfiguration` with two fixed test
> certificates (a leaf `CN=PLC_1` and its CA) through the production recorders.

`plc_tia_online` `go_online`, `plc_station_upload` and `plc_read` `compare_to_online` register an
`OnlineLegitimation` handler on the `ConnectionConfiguration` they use (`OnlineProvider.Configuration`,
respectively `StationUploadProvider.Configuration`), remove it in `finally` and record every presented
configuration in `configurationsSeen` (source b, "TLS legitimation and online authentication").

- **PLCSIM without a certificate (K52, owner decision 2026-11-02).** Live V21 2026-11-02, `plc_station_upload`
  from a PLCSIM Advanced instance: TIA Portal asked the TLS question three times with `VerificationInfo`
  "certificate not matching" and no certificate at all, so no fingerprint could be checked, and NonTrusted made
  the upload fail. For `go_online` and `plc_station_upload` over the PC interface exactly `PLCSIM`, a question
  with every member readable and zero certificates presented is answered `NonVerified` (continue without a check;
  `tls.selectionSet` `NonVerified`, `tls.source` `plcsimNoCertificate`; `PlcTlsTrustPolicy.AllowsNonVerifiedOnPlcSim`),
  and the host contract accepts that answer only for a PLCSIM path. A presented certificate keeps the fingerprint
  rule; any other PC interface, `plc_download` and `compare_to_online` keep refusing. Not live-tested yet.

- **Argument.** `trustedCertificates`: a JSON array of SHA-256 fingerprints (64 hexadecimal characters,
  case-insensitive; colons and spaces are ignored), at most 16, no repeats after normalization. A
  non-array or a non-string element is an argument-shape error; a malformed fingerprint, a repeat or more than 16 is a `validation_error` before the
  worker is asked. `go_offline` and `compare_software` refuse the argument. The normalized list
  (upper case, no separators, caller order) is part of the effects (`effects.trustedCertificates` of both
  tools), listed in the read-write confirmation prompt (`plc_read` has no prompt) and therefore in the
  audit record; fingerprints are not secrets.
- **Rule** (`PlcTlsTrustPolicy`, Contracts, shared by host, worker and FakeWorker). The evidence is read
  by reflection on the runtime object first: `PlcName` and `VerificationInfo` as text, every
  `Certificates[i].Certificate` (`X509Certificate2`) with SHA-256 over `RawData`, SHA-1 thumbprint,
  subject, issuer, `NotBefore`, `NotAfter`; `CurrentSelection` must be exactly one public read/write
  property of the documented enum `TlsVerificationConfigurationSelection`. `CurrentSelection` becomes
  `Trusted` only when every member was readable, at least one and at most 16 certificates were presented
  and **every** presented certificate's SHA-256 is listed. Otherwise `NonTrusted` is set (never written to
  a missing or foreign `CurrentSelection`). **When `NonTrusted` was set the callback returns normally (K27,
  since 2026-11-02):** the entry is recorded `selected` `NonTrusted`, `abort` is recorded with the evidence,
  TIA Portal fails the connection itself, and `go_online`, `plc_station_upload` and `compare_to_online`
  fail cleanly with `abort.tls` whatever `GoOnline`/`StationUpload`/the compare then does (returns, throws,
  `State` `NotReachable`); `go_online` may still poll `State` until its timeout. Only when `NonTrusted`
  cannot be set does the callback throw, which cancels the operation (source b). Live observation
  2026-11-02 (`MCP_Safety_01`, PLC_F1, PLCSIM #1, see the [PLC download reference](PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)):
  with the former throw `go_online` ended in `portal_lost` and TIA Portal terminated, while
  "Verbindung abbrechen" in the TIA UI (the same NonTrusted) left it running. The non-throwing refusal is
  not live-tested. Whether a throw for an unanswered upload or password configuration can terminate TIA
  Portal too has no live evidence; it is unchanged (no documented non-throwing cancel). A later TLS check
  after an abort is answered `NonTrusted` again without a throw.
  No default trust, no trust-all, never `EnableLegacyCommunication`. A `decisions`/`passwords` entry keyed
  `TlsVerificationConfiguration` is a `validation_error`, so a plain decision can never select `Trusted`.
- **Evidence.** The configuration entry and, for a refusal, the `abort` carry `tls { plcName,
  verificationInfo, certificates[{ sha256, sha1Thumbprint, subject, issuer, notBefore, notAfter,
  approved }], certificatesPresented, certificatesComplete, unreadableMembers, selectionSet, source }`;
  the abort has `decisionKey: "TlsVerificationConfiguration"` and reason `certificate_not_trusted`
  (`decision_not_applicable` when evidence was unreadable or `CurrentSelection` is missing or foreign,
  `selection_unavailable` when the live enum lacks `Trusted`, `answer_rejected` when setting threw). The failure message (`PlcCallbackRecorder.AbortMessage`) lists
  every certificate, tells the agent to have the **user** compare the fingerprints with the device's
  certificate (TIA Portal "Zertifikate anzeigen") and to retry only then with
  `trustedCertificates: [...]` listing every presented fingerprint.
- **Host checks** (`PlcOnlinePayloadContract.ValidateCallbacks`): `protocol_error` when a non-TLS entry
  carries `tls`, an `approved` flag differs from the planned list, `Trusted` is recorded after an abort or
  with evidence that does not allow it, a `NonTrusted` answer comes without `tls.selectionSet: NonTrusted`
  or without an `abort` (it anchors the abort like an aborted entry), the abort's `tls` differs from
  the first aborted or refused entry's, or `certificate_not_trusted` comes with evidence that would allow `Trusted`.
  A delivered `compare_to_online` must have no abort; its failure evidence must have no tree.
- **Late prompts.** The lingering handler `go_online` leaves until the next request has no trusted
  fingerprints: a TLS check raised after the call returned is refused and reported as a warning.

## Caps and budgets

| Cap | Value |
| --- | --- |
| compare depth / elements | 1-64 (default 16) / 1-5000 (default 300) |
| compare and callback text | 1,000 characters |
| watch/force entries per read | 1-5000 (default 1000) |
| tables + groups per PLC listing | 5,000 |
| GoOnline/GoOffline timeout | 1-120 s (default 30), polled every 200 ms; restore after compare 10 s |
| observed online states | 50 |
| callback configurations | 100 (the 101st aborts) |
| decisions + passwords per call | 64; keys and selections are identifiers, variable names at most 128 characters |
| `trustedCertificates` | 16 fingerprints; presented certificates read per TLS check: 16 (more are never trusted) |
| TIA Portal confirmations recorded | 100 listed per call, all counted |
| upload messages | 8 levels, 500 messages, 20 attributes each |

The worker request timeout (`--worker-timeout-minutes`, default 5) bounds every call. The documents
use the effective value/document budgets (`--max-item-chars`, `--max-document-chars`); an oversized
result value is omitted whole with guidance, never cut.

## Provenance of every Siemens member used

Sources (read-only reference data, never executed): **(a)** `totally-integrated-claude`
`skills/tia-plc-operations/references/compare.md`; **(b)** `.../references/online-status.md`;
**(c)** `.../references/download-upload.md`; **(d)** `.../references/watch-force-tables.md`;
**(e)** `skills/tia-networks/references/online-connection-configuration.md` (b, c and e state that they were
checked against the installed V21 XML API documentation; a cites the V21 Openness manual chapter,
and d is an extract of the V21 Step7 API reference); **(f)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/online-and-download/SKILL.md`; **(g)** Siemens `engineering-objects` and
`object-tree-walking` `SKILL.md`; **(h)** the V21 type-name baseline extracted from real DLL metadata
(`api-baselines/v21/base.json`, `step7.json`; type names and assemblies only).

Status: **signature from source** (the source shows the member with its types or call shape);
**name from source, type assumed** (the source names the member; the stub type follows the
repository's composition/group patterns and must pass the real-DLL compile); **runtime lookup** (the
member is reached by exact name at runtime, never compiled in); **existing** (an unchanged existing
stub declaration). Every type below is in the baseline assembly the stubs declare it in (h), pinned
by `PlcOnlineReferenceStubTests`.

| Siemens member | Assembly | Source | Status |
| --- | --- | --- | --- |
| `PlcSoftware.CompareToOnline()` → `CompareResult` | Step7 | a | signature from source |
| `PlcSoftware.CompareTo(ISoftwareCompareTarget)` → `CompareResult`; `PlcSoftware` implements `ISoftwareCompareTarget` | Step7 / Base | a | signature from source |
| `CompareResult.RootElement` → `CompareResultElement` | Base | a | signature from source |
| `CompareResultElement.ComparisonResult` → `CompareResultState` | Base | a | signature from source |
| `CompareResultElement.LeftName`, `.RightName`, `.DetailedInformation` (`string`) | Base | a | name from source, type assumed (`string`; the source interpolates them) |
| `CompareResultElement.Elements` → `CompareResultElementComposition` (enumerates `CompareResultElement`) | Base | a, h | name from source; enumeration from the `foreach` in a; composition type from h |
| `CompareResultState` member names (`ObjectsIdentical`, `FolderContentsIdentical`, …) | Base | a | names from source, compared by name (stub enum has no members) |
| `Project.ProjectLibrary` → `ProjectLibrary`; `ProjectLibrary` as `ISoftwareCompareTarget` | Base | a | name and call shape from source; declaring type `Project` and property type assumed |
| `ProjectLibrary.MasterCopyFolder` → `MasterCopySystemFolder`, `.MasterCopies` → `MasterCopyComposition`, `.Find(string)` → `MasterCopy`; `MasterCopy` as `ISoftwareCompareTarget` | Base | a, h | call chain from source; property types assumed (h names the types) |
| `DeviceItem.GetService<OnlineProvider>()` on the PLC's CPU device item | Base | b, e, f | signature from source; existing `GetService<T>` |
| `OnlineProvider.State` → `OnlineState` | Base | b, f | signature from source |
| `OnlineProvider.GoOnline()` → `OnlineState` | Base | b | signature from source (`OnlineState resultState = onlineProvider.GoOnline();`) |
| `OnlineProvider.GoOffline()` | Base | b, f | name from source, return type assumed `void` (pinned as `Action` in the probe) |
| `OnlineProvider.Configuration` → `ConnectionConfiguration` | Base | b, e, f | signature from source |
| `OnlineState` member names (`Offline`, `Connecting`, `Online`, `Disconnecting`, `Incompatible`, `NotReachable`, `Protected`) | Base | b | names from source, compared by name |
| `ConnectionConfiguration.IsConfigured` (`bool`) | Base | b, e | signature from source |
| `ConnectionConfiguration.ApplyConfiguration(ConfigurationTargetInterface)` → `bool` | Base | e | signature from source (`bool applied = config.ApplyConfiguration(target);`) |
| `ConnectionConfiguration.OnlineLegitimation` (event of `OnlineConfigurationDelegate`) on `OnlineProvider.Configuration` (go_online, compare_to_online) and `StationUploadProvider.Configuration` (station upload) | Base | b | signature from source (`+=`/`-=` of the same delegate, removal in `finally`); that station upload raises its legitimation through its provider's configuration is not stated: assumed from the shared `ConnectionConfiguration` type — verify live |
| `Online.Configurations.TlsVerificationConfiguration` (canonical type by full name in the runtime type chain; never compiled) | Base | b, h | type name in h; runtime lookup |
| `TlsVerificationConfiguration.PlcName`, `.VerificationInfo` | Base | b | runtime lookup (reflection); names from source, types undocumented → read as text; missing or throwing fails closed |
| `TlsVerificationConfiguration.Certificates` (`Online.Security.VerificationCertificateComposition` of `VerificationCertificate`, enumerated) | Base | b, h | runtime lookup; name from the source's callback sample, composition and item type names in h, `IEnumerable` enumeration assumed (not enumerable fails closed) |
| `VerificationCertificate.Certificate` → `X509Certificate2` (`RawData`, `Thumbprint`, `Subject`, `Issuer`, `NotBefore`, `NotAfter` are .NET members) | Base | b, h | runtime lookup; type from source ("exposing the target `X509Certificate2` through `Certificate`") |
| `TlsVerificationConfiguration.CurrentSelection` of type `TlsVerificationConfigurationSelection`; members `Trusted`, `NonTrusted` set by name (`NonVerified` never) | Base | b, h | runtime lookup; property, type and members from source (callback sample and catalogue), type name in h; the property type's full name is compared, members checked with `Enum.IsDefined` |
| `IsSecureCommunication` of `OnlinePasswordConfiguration`/`UploadPasswordConfiguration` | Base | members.txt | runtime lookup (`PlcPasswordChannel`, audit T1): read before every `SetPassword`; false or unreadable without `allowInsecurePasswordTransfer` aborts `insecure_communication`; not live-tested |
| `OnlineAuthenticationConfiguration`, `OnlineCredentials`, `UserType`, `AuthenticationType`, `OnlineAuthenticationConfiguration.IsSecureCommunication`, `EnableLegacyCommunication` | Base | b, h | not used (authentication aborts; `EnableLegacyCommunication` is never read or set) |
| `OnlineConfigurationDelegate` = `void (OnlineConfiguration)`; `Online.Configurations.OnlineConfiguration` | Base | b, h | signature from source |
| `ConnectionConfiguration.Modes`, `ConfigurationMode.Name/.PcInterfaces`, `ConfigurationPcInterface.Name/.Number/.TargetInterfaces`, `ConfigurationTargetInterface.Name` | Base | b, e, f | existing (plc_download) |
| `ConfigurationPcInterface.Addresses` → `ConfigurationAddressComposition`; `.Create(string)` → `ConfigurationAddress` | Base | b, c, e | signature from source |
| `ProjectBase.GetService<StationUploadProvider>()` | Base | c | signature from source (`project.GetService<StationUploadProvider>()`); existing `GetService<T>` |
| `StationUploadProvider.Configuration` → `ConnectionConfiguration` | Base | c | signature from source |
| `StationUploadProvider.StationUpload(ConfigurationAddress, UploadConfigurationDelegate)` → `UploadResult` | Base | c | signature from source |
| `UploadConfigurationDelegate` = `void (UploadConfiguration)`; `Upload.Configurations.UploadConfiguration.Message` | Base | c, h | signature from source; `Message` type assumed `string` |
| `UploadResult.State` (`UploadResultState`), `.ErrorCount` (`int`), `.UploadedStation` (`Device`), `.Messages` (`UploadResultMessageComposition` of `UploadResultMessage`) | Base | c, h | signature from source for `State`/`UploadedStation`; `ErrorCount` type and the message composition's enumeration assumed |
| `UploadResultState.Error` | Base | c | name from source, compared by name |
| `Device.Name` (uploaded station) | Base | existing | existing |
| `UploadResultMessage` attributes through `IEngineeringObject.GetAttributeInfos()` / `EngineeringAttributeInfo.Name` / `GetAttribute(string)`; nested messages through enumerable engineering-object properties | Base | g | runtime lookup (schema-free; no message member is compiled in) |
| Selection callbacks `CurrentSelection` (e.g. `UploadMissingProducts`, members `NoAction`, `TryUpload`) | Base | c (b for the online namespace) | runtime lookup (reflection on the live object; enum parsed by exact member name) |
| Password callbacks `SetPassword(SecureString)` (`UploadPasswordConfiguration` specializations, `OnlinePasswordConfiguration`, `OnlineReadAccessPassword`) | Base | b, c | runtime lookup (reflection; one public method taking `SecureString`) |
| Canonical callback types in `Siemens.Engineering.Upload.Configurations` / `.Online.Configurations` | Base | b, c, h | type names from h; runtime type chain walk |
| `PlcSoftware.WatchAndForceTableGroup` → `PlcWatchAndForceTableSystemGroup` | Step7 | a, d | name from source ("Get the Plc watch table system group"), type assumed from that summary |
| `PlcWatchAndForceTableGroup.WatchTables` (`PlcWatchTableComposition`), `.ForceTables` (`PlcForceTableComposition`), `.Groups` (`PlcWatchAndForceTableUserGroupComposition`), `.Name` | Step7 | d, h | names from source; types from the summaries and h; System/User groups deriving from it assumed (block/tag group pattern) |
| `PlcWatchAndForceTableUserGroup.Delete()`; `PlcWatchAndForceTableUserGroupComposition` enumeration and `.Create(string)` | Step7 | d | name from source; return types assumed |
| `PlcWatchTableComposition` enumeration, `.Create(string)` → `PlcWatchTable`, `.Import(FileInfo, ImportOptions)` → `IList<PlcWatchTable>` | Step7 | d | parameters from source; return types assumed (block-composition pattern) |
| `PlcWatchTable.Name`, `.IsConsistent` (`bool`), `.Export(FileInfo, ExportOptions)`, `.Delete()` | Step7 | d | parameters from source; `IsConsistent` type and `void` returns assumed |
| `PlcForceTableComposition` enumeration, `.Import(FileInfo, ImportOptions)` → `IList<PlcForceTable>`; `PlcForceTable.Name`, `.IsConsistent`, `.Export(FileInfo, ExportOptions)` | Step7 | d | parameters from source; return and property types assumed |
| `PlcWatchTable.Entries`, `PlcForceTable.Entries` | Step7 | d, g | name from source; runtime lookup (reflection; the composition type is undocumented and not declared) |
| Entry attributes `Name`, `Address`, `DisplayFormat`, `MonitorTrigger`, `ModifyIntention`, `ModifyTrigger`, `ModifyValue` (watch) and `ForceIntention`, `ForceValue` (force) | Step7 | d, g | names from source; runtime lookup (`GetAttribute`) |
| `ImportOptions.Override`, `ExportOptions.None` | Base | existing | existing |
| `PlcSoftware.UpdateProgram()` | Step7 | a | name and empty parameter list from source; return type undocumented → runtime lookup (`GetMethod("UpdateProgram", Type.EmptyTypes)`); returned members read by reflection (runtime lookup) |
| `TiaPortal.ExclusiveAccess(string)`, `ExclusiveAccess.Transaction(ITransactionSupport, string)`, `Transaction.CommitOnDispose()` (update_program) | Base | a (workflow), existing | existing (subnet lifecycle, VCI) |
| `EngineeringException`, `EngineeringTargetInvocationException`, `EngineeringObjectDisposedException`, `NonRecoverableException` | Base | existing | existing |
| TIA Portal `Confirmation` event answered through `TiaPortalSession.AnswerConfirmations` (`ConfirmationEventArgs.Result`, `ConfirmationResult.Yes/No`); dialog text via `PlcDownloadService.ReadConfirmationText` | Base | existing (plc_download) | existing; the text is a runtime lookup of an unverified public `Text` string |

Stub conventions that are not Siemens facts: the new stub enums (`CompareResultState`,
`OnlineState`, `UploadResultState`) declare no members (the worker compiles no enum constant), and
the `IEngineeringObject`/`IEngineeringComposition` interface lists follow the existing stub style.
Parameter names in stub signatures are placeholders.

### Left out, and why

- **Global library compare.** The source shows `CompareTo(globalLibrary)` but no way to obtain an
  opened global library; opening one would be a separate lifecycle-like action.
- **Master copies below the root master copy folder.** The source shows only
  `MasterCopyFolder.MasterCopies.Find(name)`; no folder traversal member is used, and the master
  copy's own name is not read back.
- **Hardware compare against an object other than a PLC's station/CPU, a root master copy or the CPU's `OnlineProvider`.**
  `HardwareObject.CompareTo(IHardwareCompareTarget)` is implemented as `compare_hardware`/`compare_hardware_to_online` (issue #7);
  the V21 XML names no implementer of `IHardwareCompareTarget`, so no other target is guessed.
- **`GoOnline(ConfigurationAddress)`, `ApplyConfiguration(ConfigurationAddress)` (gateway/subnet
  paths), `RHOnlineProvider`** are documented but not used: the path is always the exact mode → PC
  interface → target interface the download already uses. Going online to an R/H primary or backup
  (`RHOnlineProvider.GoOnlineToPrimary/GoOnlineToBackup`, `PrimaryState`/`BackupState`, audit N1) needs a new
  `plc_tia_online` field and a path inventory from `RHOnlineProvider.Configuration` in `get_online_state`; it is left for a
  later package. The worker's provider lookup (`RhSystemProviders`: CPU device item, then parents up to the `Device`,
  audit T3) already covers `RHOnlineProvider` by name for it.
- **Online authentication answers** (`OnlineAuthenticationConfiguration`, `OnlineCredentials`,
  `UserType`): no approved authentication policy exists for this tool, so these configurations abort with
  evidence. TLS trust is answered, but only from the user-approved `trustedCertificates` (see
  [TLS certificate trust](#tls-certificate-trust-trustedcertificates)). `EnableLegacyCommunication` is
  never touched (a TLS downgrade). `TlsVerificationConfigurationSelection.NonVerified` is never set.
- **Binding `PlcName` to the target**: the source asks to bind `PlcName`, `VerificationInfo` and every
  certificate to the approved target; here the user-approved fingerprints are that binding, and
  `PlcName`/`VerificationInfo` are reported for the user, not compared with the project's PLC name
  (the device's name in the certificate check need not equal the project's PLC software name).
- **`GetAccessibleDevices()`** (live network discovery) is never called by these operations; only
  `plc_read scan_accessible_devices` calls it, on an explicitly named PC interface (see above and
  [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#accessible-devices-scan_accessible_devices)).
- **Typed entry edits**: no entry composition type, entry `Create` or entry `Delete` is documented;
  entries change only through the SimaticML round trip. Comment entries
  (`PlcTableCommentEntryComposition.Create`, no documented parameters) are not exposed.
- **Force table create/delete and online forcing**: not documented (see above). `ShowInEditor` (UI)
  and `CreateFrom(MasterCopy)` are not used.
- **Upload check decisions** (`true`): no upload check configuration is documented.
  `UploadResult.WarningCount` is not documented and not reported.
- **"Update offline from online" after a compare**: the sources document no such compare action;
  `UpdateProgram()` is the documented instruction upgrade and is exposed as exactly that.
- **`allowInsecurePasswordTransfer` for `compare_to_online`/`compare_hardware_to_online`** (audit T1): a compare
  password over a channel not reported secure aborts `insecure_communication` without an opt-in. The opt-in is left
  out because the host's compare evidence check does not know the merged password list (it validates compare
  evidence against no passwords), so a compare `password_set` entry is already rejected as `protocol_error`
  (open, found while fixing T1). Use `plc_tia_online go_online` with the opt-in first if the user agrees.
- **A `portalConfirmations` option for `compare_to_online`**: a read never accepts a TIA Portal dialog,
  so the compare always declines (and fails when it declined one).

## Known limits and open risks

- Not live-tested; every *assumed* member above can fail the real-DLL compile, and the runtime-lookup
  members (`Entries`, entry attributes, `UpdateProgram`, callback `CurrentSelection`/`SetPassword`,
  message attributes) can be absent or shaped differently at runtime (each fails closed:
  `target_kind_unsupported`, unreadable attributes, or an aborted callback).
- `compare_to_online` may open a connection itself; whether it raises legitimation through the
  connection configuration's `OnlineLegitimation` event (where the fail-closed handler listens) or by
  another route (possibly a TIA Portal UI dialog) is unknown. The restoring `GoOffline` runs only when the
  state was not `Online` before and is `Online` or `Connecting` after; a restore that fails or times out
  leaves TIA Portal online and is reported, not retried.
- TLS trust is not live-tested: whether V21 raises the trust question through these `OnlineLegitimation`
  events (station upload: through its provider's configuration), the runtime shapes of `PlcName`,
  `VerificationInfo` and `Certificates`, and whether TIA Portal keeps a trust after the call are
  unverified. The tool cannot verify that a human checked the fingerprints: `full` has no prompt, and
  `compare_to_online` (`plc_read`) never prompts, so an agent can copy fingerprints from the abort
  evidence and retry on its own; in read-write the `go_online` and `plc_station_upload` prompts list them.
- `go_online` leaves an already-online PLC untouched and cannot verify that connection's path; the result says so
  (`already_online`, `path: null`, `pathVerified: false`) and still counts as success, because the PLC is online.
- A declined TIA Portal confirmation fails the call after the action may already have taken effect (for example the
  PLC may be online); the evidence says what happened, inspect with `get_online_state`. Whether TIA Portal raises such
  dialogs for these actions at all is not documented.
- A timeout of `go_online` leaves TIA Portal possibly still connecting; nothing cancels it
  automatically.
- Planning reads a different provider instance than the action (only names cross that boundary); a
  path change between planning and the action is caught by the worker's exact re-resolution
  (`state_changed`).
- Station upload adds a device even when it later reports errors; there is no rollback. The
  `Addresses.Create` entry stays in the PC interface's configuration.
- The SimaticML import is checked only for naming exactly the addressed table (shape calibrated on the
  worker's own export); the tool cannot validate the rest of the document against a schema. An import
  that still returns other or more tables fails `postcondition_failed` after the project was changed.
- `update_program`'s outcome is interpreted by member names (`State`/`Status`/`Result`, `ErrorCount`,
  `Success`/`Succeeded`); a V21 return type with other members is `uninterpreted`. Whether TIA Portal rolls
  back an `UpdateProgram()` inside an uncommitted transaction completely is not verified.
- Watch-table names and group paths are matched case-insensitively and uniquely by enumeration;
  TIA Portal's own uniqueness rules are not verified.

## Live acceptance checks

Run on a disposable project with PLCSIM (or PLCSIM Advanced) first, then on a lab CPU:

1. `plc_read get_online_state`: the listed modes/PC interfaces/target interfaces match the TIA Portal
   "Go online" dialog; `state` follows the UI.
2. `plc_tia_online go_online` over `PLCSIM`: `statesObserved` ends in `Online`, the TIA Portal UI shows
   the PLC online; then `go_offline` returns to `Offline`. Repeat with the PLC already online
   (expect the "already online" note).
3. `go_online` against an S7-1500 or PLCSIM Advanced instance with TLS and an unknown certificate: confirm
   `configurationsSeen` shows `TlsVerificationConfiguration` with readable `tls` (empty
   `unreadableMembers`), `selectionSet: NonTrusted`, the entry `selected` `NonTrusted` (no throw, K27), that
   TIA Portal stays offline **and keeps running** (`bind_project list_portals`); compare the
   fingerprints with "Zertifikate anzeigen"; retry with every fingerprint in `trustedCertificates` and
   confirm `Trusted` (source `caller`) and `Online`. Repeat for `compare_to_online` and
   `plc_station_upload`, and record whether a trust persists into a later call without the argument.
4. `compare_to_online` with the PLC offline and online; check `disconnectedAfterCompare`, the tree
   against the TIA Portal compare editor, and the caps with a large program.
5. `compare_software` against another PLC, the project library and a root master copy.
5a. `compare_hardware` of two stations and of two CPUs (one with another firmware), against a root master copy of a station, and
   `compare_hardware_to_online` with the PLC offline and online: record which targets implement `IHardwareCompareTarget`.
6. `list_watch_and_force_tables`, `read_watch_table`, `read_force_table`: confirm `Entries` is found
   and every attribute is readable (no `unreadableAttributes`), and the value types (enum names,
   booleans).
7. `get_watch_table_content` → edit (add, change, remove an entry) → `update_watch_table_content`;
   confirm `importedTables` and the table in the UI; repeat for the force table (offline only).
8. `create/delete_watch_table(_group)` and `update_program` on a copy; compile afterwards.
9. `plc_station_upload` from a PLCSIM/lab CPU without decisions: record the abort's
   `decisionKey`/`availableSelections`; retry with the decision; inspect the uploaded device, compile,
   and do not save unless intended.
10. Confirm in read-write that every `plc_tia_online`/`plc_station_upload` call prompts once and that
    declining touches nothing.
11. Provoke a TIA Portal confirmation during `go_online` and `plc_station_upload` (if one exists for the
    target): with the default `decline` the call fails and lists it, with `accept` it is answered Yes and
    listed; confirm the dialog text is captured (`text`, an unverified member) and that `compare_to_online`
    fails instead of accepting.
