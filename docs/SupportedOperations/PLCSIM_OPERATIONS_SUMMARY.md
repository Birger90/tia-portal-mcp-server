# S7-PLCSIM Advanced operations (`plcsim_read`, `plcsim_write`, `plcsim_test`)

**Language:** English · [Deutsch](PLCSIM_OPERATIONS_SUMMARY.de.md)

> **Not live-tested.** This domain was built and qualified offline only: no S7-PLCSIM Advanced
> installation was available. Every Siemens member is bound by reflection at runtime. The member
> *names* in the surface table below are verified against the Siemens manual, and the `IInstance`
> signatures marked *reference signature* against the Siemens developer reference for API 8.0;
> `probe_api` remains the check for the installed version. Run the
> [live acceptance steps](#live-acceptance-steps-to-run-once-plcsim-advanced-is-installed) - with
> `probe_api` first - before relying on these tools. First live observations of 2026-11-02 are
> recorded under [live observations](#live-observations-2026-11-02); they do not replace the
> acceptance steps.

Three tools control S7-PLCSIM Advanced virtual S7-1500 CPUs ("instances") through a separate
PLCSIM worker process. They never touch TIA Portal, the Openness worker or the project binding.

| Tool | Modes | Purpose |
|---|---|---|
| `plcsim_read` | read-only, read-write, full | Up to 50 independent reads: `probe_api`, `list_instances`, `get_instance`, `list_tags`, `read_tags`. |
| `plcsim_write` | read-write, full | Up to 50 ordered control operations: `power_on`, `power_off`, `run`, `stop`, `memory_reset`, `update_tag_list`, `write_tags`, `set_scale_factor`; `dryRun` plans only. |
| `plcsim_test` | read-write, full | One scenario of up to 200 `write`/`wait`/`expect` steps, executed inside the worker; `dryRun` plans only. |

Server tool counts, these three included, are 10 (read-only), 27 (read-write) and 27 (full).

## Prerequisites

- 64-bit Windows with .NET Framework 4.8 (the worker is a net48 x64 process).
- S7-PLCSIM Advanced installed and licensed. The worker loads
  `Siemens.Simatic.Simulation.Runtime.Api.x64.dll` from the installation; no Siemens binary is
  shipped with this server.
- An instance created (and, for tag access, a program downloaded) in the **S7-PLCSIM Advanced
  Control Panel**. These tools never create, register or unregister instances.
- S7-1500 CPUs only (PLCSIM Advanced simulates S7-1500/ET 200SP CPUs).
- To download a program to an instance, set the Control Panel's online access to **PLCSIM**
  (softbus) and download through the `PLCSIM` PC interface: with this server's `plc_download`
  (any PC interface; the `PLCSIM` one reaches the simulation, others are treated as hardware; see the
  [PLC download reference](PLC_DOWNLOAD_SUMMARY.md)) or from TIA Portal itself.

## Process model

```text
TiaMcpServer (net10.0 host)
    |-- openness-worker/TiaMcpServer.OpennessWorker.exe   (TIA Portal Openness)
    '-- plcsim-worker/TiaMcpServer.PlcSimWorker.exe       (S7-PLCSIM Advanced, net48 x64)
```

- `PlcSimWorkerClient` starts the PLCSIM worker lazily on the first PLCSIM call that passes host
  validation and access checks. Tool discovery, argument rejection, validation errors and dry runs
  never start it.
- The worker reuses the Openness wire envelope (`WorkerRequest`/`WorkerResponse`, newline-delimited
  JSON) and `PersistentWorkerTransport`, but answers `hello` with its own protocol
  (`plcsim-v1`, capabilities `plcsim-reflection-api-surface-v1`, `plcsim-exact-instance-name-v1`,
  `plcsim-test-runner-v1`, `plcsim-restart-flag-v1`; the host requires every one, so a worker that
  does not set the restart flag is refused at the handshake). One long-lived process; a timeout or
  crash kills it and the next call starts a fresh one. Parameters travel in `WorkerRequest.PlcSim`.
- Requests are **serialized**: the shared transport admits one PLCSIM call at a time and a waiting
  call has no queue timeout of its own (only the call in progress is bounded by the worker request
  timeout). A running `plcsim_test` scenario therefore blocks every other PLCSIM call, including
  reads and `probe_api`, for up to its `maxDurationMs` (clamped below the worker timeout); a slow
  control call blocks them likewise. Openness tools use their own worker and are not blocked.
- The worker executable is the packaged `plcsim-worker/` folder beside the host. Only when it is
  missing, a development build is used, and only under the nearest ancestor of the host directory
  that contains `TiaMcpServer.slnx` (`TiaMcpServer.PlcSimWorker/bin/Release/net48`, then `Debug`);
  no other parent directory is probed.
- The host also **recycles** the worker when the worker states that a restart is required
  (`WorkerResponse.workerRestartRequired: true`), and after a successful `probe_api` whose typed
  result reports `apiInitializationFailed: true`. The worker sets the flag on every response once
  that process saw a failed API type initializer (for example the static `SimulationRuntimeManager`
  initializer while the Runtime Manager was not running), whether the failure was reported for the
  whole request or for one tag, item or test step: .NET keeps a failed type initializer for the
  lifetime of the process, so only a fresh process can retry. A missing or unloadable DLL
  (`plcsim_api_unavailable` without the flag) does not recycle; the same worker retries the load on
  the next request. The old process is shut down once no concurrent request still uses it; the next
  call starts a new one.
- The worker request timeout is the same `--worker-timeout-minutes` value as the Openness worker.
- The worker writes protocol lines to the real stdout and redirects `Console.Out` to stderr, so
  output from the Siemens API cannot corrupt the protocol stream.

## Safety and access

- Worker methods are prefixed `plcsim_` (flat `OperationPolicyCatalog` namespace). Reads
  (`plcsim_probe_api`, `plcsim_list_instances`, `plcsim_get_instance`, `plcsim_list_tags`,
  `plcsim_read_tags`) are `Observe`. Every other method (`plcsim_power_on`, `plcsim_power_off`,
  `plcsim_run`, `plcsim_stop`, `plcsim_memory_reset`, `plcsim_update_tag_list`,
  `plcsim_write_tags`, `plcsim_set_scale_factor`, `plcsim_run_test`) is the new capability
  `SimulationControl`, appended last to `OperationCapability` (numeric value 7), allowed in
  read-write and full and denied in read-only.
- Enforcement is layered like the Openness tools: write tools are not registered in read-only,
  the host authorizes before starting the worker, and the worker authorizes (with the Openness
  worker's `WorkerOperationAuthorization`) before any PLCSIM API call. Unknown methods are denied.
- PLCSIM methods are not project-scoped: `ProjectCapabilityCatalog.OperationFor` returns null and
  `RequiresExpectedSessionIdentity` is false for them.
- No server elicitation and no confirmation prompt in any mode.
- Audit: every `plcsim_write`/`plcsim_test` call that reaches the tool - dry runs, validation and
  access denials included - appends one record through the guarded-write pipeline's
  `IWriteAuditSink` (`writes-yyyy-MM-dd.jsonl` in the same audit directory), reusing the
  `WriteAuditRecord` shape without the project-binding pipeline: `recordKind:"plcsim"`,
  `projectPath:null`, `binding:null`, each item's `target` is the instance name, confirmation is
  `none` (or `policy` for an actual call in full mode). Argument-shape rejections are not audited.
- Instances are selected by exact, ordinal (case-sensitive) name: the worker calls
  `CreateInterface(name)` and refuses the interface with `target_not_found` unless its `Name`
  equals the request exactly. Each operation releases the interface (`Dispose`) afterwards.
- Writes are not rolled back. `plcsim_write` stops at the first failed item; `write_tags` stops at
  its first failed tag and still returns which tags were written.

## Operations

All items take `operationId` and `operation`; `instance` is required by every operation except
`probe_api` and `list_instances`. Inapplicable members are rejected. Tag `dataType` is one of
`bool`, `int8`, `int16`, `int32`, `int64`, `uint8`, `uint16`, `uint32`, `uint64`, `float`, `double`, `string` and selects the typed
`Read*`/`Write*` method; `string` is read-only because `WriteString` is not a verified member, and the
`uint*` types are read-only because no `WriteUInt*` member was found in the readable part of the
reference. A Byte/USInt tag needs `uint8` (live 2026-11-02: `ReadInt8` on a Byte failed TypeMismatch -29).
Tag names are what the PLCSIM Advanced API addresses (the form `list_tags` returns), without quotes: `DB_Line.Speed`, not
`"DB_Line".Speed` (K56, live 2026-11-02: the quoted form failed `DoesNotExist`). Both are accepted: the worker removes the
outer double quotes of each dot-separated segment before the API call (array indices stay); a quoted segment whose text
contains a dot, a quote or a bracket, or unbalanced quotes, is passed on unchanged. Results echo the name as sent.
Values are strict JSON: `true`/`false`, integral numbers in range, finite numbers; nothing is coerced.
A `float` value is converted to single precision first and refused when it overflows (beyond
`±3.4028235E+38`) or when a non-zero value would round to zero (below the smallest denormal);
denormals and `-0` are kept, and every `float` the server reports reads back unchanged.

| Operation | Tool | Fields | Result |
|---|---|---|---|
| `probe_api` | read | - | API location (source `environment`/`registry`/`none`, version folders, DLL path, existence), load error, assembly/file/product version, `IsInitialized`/`IsRuntimeManagerAvailable`, `apiInitializationFailed` (a type initializer failed in this worker process; the host then recycles the worker), every catalog member with candidates, resolved signature or why it is missing, the operations it is used by, discovered types (SInstanceInfo, STagInfo and property types with their members; for every enum its names in `enumNames` and each name with its numeric value, as decimal text, in `enumMembers`), the error-code member of `SimulationRuntimeException` and `runtimeErrorCode.okMemberFound` (whether the loaded `ERuntimeErrorCode` has a member named exactly `OK`; null when the enum is missing; see [return values](#return-values) for what decides success), and per-operation availability. Succeeds even when the API is missing. |
| `list_instances` | read | - | Reflective projections of `RegisteredInstanceInfo`, plus the two manager flags. |
| `get_instance` | read | `instance` | `name`, `id`, `operatingState`, `operatingMode`, `scaleFactor`, `communicationInterface`, `cpuType`, each read reflectively; a missing or failing member is null with a warning. |
| `list_tags` | read | `instance`, `nameFilter?`, `max?` (1-5000, default 500) | Projections of `TagInfos` (never calls `UpdateTagList`), counts, `truncated`, `tagListStatus` (GetTagListStatus out values, or null). `nameFilter` is a case-insensitive substring and needs `STagInfo.Name`. The host keeps as many leading tags as fit the per-value budget (`budgetTrimmed`, warning). |
| `read_tags` | read | `instance`, `tags` 1-100 `{name, dataType}` | Per tag `status` `read`/`failed`, `value`, structured `error`. |
| `power_on`, `power_off`, `run`, `stop`, `memory_reset` | write | `instance` | Invoked signature, timeout argument (60,000 ms when the `(UInt32)` overload resolves), return type, projected return value, `returnCheck` and `returnCode` (see [return values](#return-values)), `operatingStateBefore`/`After`. A failed return fails the item `plcsim_runtime_error` with this result attached. `power_on` and `run` warn when the instance is not on the softbus. |
| `update_tag_list` | write | `instance` | As above (no timeout overload is declared), plus a warning: an unverified secondary source reports that `UpdateTagList()` can cold-restart the virtual CPU. |
| `write_tags` | write | `instance`, `tags` 1-100 `{name, dataType, value}` | Per tag `written`/`failed`/`skipped`, the typed value written, invoked signature and projected return value. A write whose return signals failure fails that tag `plcsim_runtime_error` (code name in `error.runtimeErrorCode`). A failed tag fails the item (category of the tag error) with the typed result attached. |
| `set_scale_factor` | write | `instance`, `value` > 0 | `requested`, `previous`, `current`. When the loaded `ScaleFactor` is `Single` (the reference declares `double`), a value outside the single-precision range, or one that would round to zero, is `validation_error` and nothing is set; the host cannot know the loaded type, so this check is the worker's. |

### Return values

Control and write results report the API return value. Per the Siemens developer reference for
API 8.0, only `PowerOn` returns `ERuntimeErrorCode`; `PowerOff`, `Run`, `Stop`, `MemoryReset`,
`UpdateTagList` and every `WriteX` return `void` (their failures arrive as exceptions). The
interpretation below is generic, so a version whose methods return codes is judged the same way:

- the loaded API's `ERuntimeErrorCode` enum is judged by the **sign** of the numeric value, as the
  reference describes the native codes ("Errors are represented by negative values, warnings by
  positive values"; on success the return value is `SREC_OK`). When the loaded enum has a member
  named exactly `OK`, its value - and only its value - is success (`returnCheck: "ok"`); zero is then
  not success unless `OK` is zero. Only when no member is named `OK` is zero assumed to be success,
  and the result then carries a warning saying so. Otherwise a negative value is an error
  (`"failed"`, `plcsim_runtime_error` naming the code) and a positive value a warning (`"warning"`:
  the call succeeded, later items still run, and a warning names the code). A value that is neither
  the success value nor signed (any non-success value of an unsigned enum, or zero while `OK` has
  another value) is `"uninterpretable"`, reported with a warning. `returnCode` is the member name read
  from the loaded enum, or the number when the value is undefined. The .NET member name `OK` is
  verified only at runtime (`probe_api` reports `okMemberFound`, and `enumMembers` of
  `ERuntimeErrorCode` shows its numeric value); the numeric value of `SREC_OK` was not available from
  the reference;
- `Boolean`: `false` fails (`returnCode: "False"`), `true` is `"ok"`.

Every other return type, and `void`, is `"notInterpreted"` (a non-void value is reported with a
warning). A failure from a return value is `plcsim_runtime_error`; the call is not rolled back.

**Timeouts and state after.** `PowerOn`, `PowerOff`, `Run`, `Stop` and `MemoryReset` declare the
`(UInt32 in_TimeOut_ms)` overload first (60,000 ms; in the developer reference for API 8.0) and fall
back to the parameterless overload. That fallback may return before the state change completes, so
`operatingStateAfter` may lag; the result then carries `timeoutArgumentMs: null` and a warning.

**Communication interface.** `power_on`, `run` and `plcsim_test` read
`IInstance.CommunicationInterface` and add a warning with its raw value whenever it is not exactly
the name `Softbus` (or cannot be read): a started instance that is not on the PLCSIM softbus can
communicate with real network devices through the PLCSIM virtual Ethernet adapter. The developer
reference lists the native enum (`SRCI_NONE = 0`, `SRCI_SOFTBUS = 1`, `SRCI_TCPIP = 2`), but not the
.NET member names, so the name `Softbus` is unverified (confirm it with `probe_api`, which lists the
enum names of `type of IInstance.CommunicationInterface`); a different spelling only produces the
warning. Nothing is blocked.

### `plcsim_test`

Arguments: `instance`, `steps` (1-200), `continueOnFailure` (default false), `maxDurationMs`
(default 120,000, 1-600,000), `dryRun`.

| Step | Fields |
|---|---|
| `write` | `tag`, `dataType` (writable), `value` |
| `wait` | `ms` 1-60,000 |
| `expect` | `tag`, `dataType`, `comparison` (`equals`, `notEquals`, `greaterThan`, `greaterOrEqual`, `lessThan`, `lessOrEqual`, `between`), `value` or `min`+`max` (inclusive) for `between`, `timeoutMs` 0-60,000 (default 2,000), `pollMs` 10-60,000 (default 50) |

Every step may carry a `name`. `bool` and `string` allow only `equals`/`notEquals`. Integers compare
exactly; `float`/`double` compare as IEEE doubles of the parsed typed value (no tolerance - use
`between`). `expect` polls until the comparison holds or `timeoutMs` elapses.

- A `write` step whose API return signals failure (see [return values](#return-values)) ends in
  `error` with a `plcsim_runtime_error` naming the code; an uninterpretable code adds a warning.
- Results per step: `status` `passed`/`failed`/`error`/`skipped`, `lastObserved`, `attempts`,
  `startOffsetMs`, `elapsedMs`, `message`, structured `error`. Summary counts include expects.
  `result.passed` is true only when every expect passed and no step errored or was skipped;
  top-level `success` equals it. A scenario that ran has MCP `isError:false` even when it failed.
- `continueOnFailure:false` skips every step after the first `failed` or `error` step.
- `maxDurationMs` bounds the whole scenario; reaching it aborts (`aborted`, `abortReason`, the
  current step `error`, the rest `skipped`). It is clamped, with a warning, to the worker request
  timeout minus 30,000 ms (270,000 ms at the default 5 minutes); raise `--worker-timeout-minutes`
  for longer scenarios. The MCP client's own request timeout must also allow the duration.
- **Timing model:** wall-clock against PLCSIM's default operating mode. The runner does not use
  virtual time or synchronize with the CPU cycle (`OperatingMode`, `RunToNextSyncPoint`,
  `ScaleFactor` and sync events are not used by the runner), so expectations need tolerant
  timeouts; a `ScaleFactor` other than 1 changes simulated time relative to these wall-clock
  timeouts.
- If the result does not fit the response budget, step messages and observations of passed and
  skipped steps are dropped first, then long messages are shortened; statuses and errors are
  kept. If even that does not fit, the call returns `result_metadata_too_large` with the summary.

### Envelopes

`plcsim_read` uses the shared batch read envelope (`tool`, `contractVersion:"1.0"`, `success`,
`error`, `warnings`, `batch`). `plcsim_write` returns `{tool, contractVersion, success, phase
(preview|applied|error), dryRun, error, warnings, plan, batch}`; `plcsim_test` returns `{tool,
contractVersion, success, phase (preview|executed|error), dryRun, error, warnings, plan, result}`.
Rejections carry `error` and MCP `isError:true`. All documents are canonical JSON with explicit
nulls; worker payloads are typed and decoded with the required-member worker-payload reader plus
semantic checks, so a malformed payload is `protocol_error` and is never echoed.

## Failure categories

| Category | Meaning |
|---|---|
| `plcsim_api_unavailable` | The API DLL was not found or could not be loaded, or a type initializer of the API failed (a `TypeInitializationException` anywhere in the inner-exception chain, walked up to 32 levels; for example the static `SimulationRuntimeManager` initializer while the Runtime Manager is not running); the message names the problem. Only the type-initializer case sets `workerRestartRequired`, after which the host recycles the PLCSIM worker. |
| `plcsim_api_member_missing` | A member the operation needs did not resolve to a declared candidate; the message names the member and the candidates. |
| `plcsim_runtime_error` | The API raised `SimulationRuntimeException` or `SimulationInitializationException` (without a failed type initializer); the message carries the exception type, `<member> = <ERuntimeErrorCode name> (<number>)` when the exception exposes exactly one public member of type `ERuntimeErrorCode`, and the API message. Also a control or write whose return value signals an error (a negative `ERuntimeErrorCode`, or `false`), naming the code. Result-level `error` objects carry `exceptionType`, `runtimeErrorCode` and `runtimeErrorCodeMember`. |
| `target_not_found` | The API returned an instance whose name differs from the requested name. |
| `worker_operation_failed` | Any other exception (type and message), or a missing/unstartable PLCSIM worker. |
| `worker_timeout`, `worker_crashed` | Transport failures; for control operations the simulated CPU may have changed. |
| `validation_error`, `access_denied`, `protocol_error`, `result_metadata_too_large` | As for the other tools. |

Messages are never parsed: the category comes from the exception type alone.

## API resolution

1. **Directory.** `TIA_MCP_PLCSIM_API_DIR`, when set and nonblank, is the directory holding the DLL
   and disables the registry lookup. It must be an absolute (fully qualified) path; a relative value,
   including a drive-relative `C:dir` or a root-relative `\dir`, is refused with a message naming the
   value, by `doctor` and by the worker alike (no lookup, no load). A registry `Path` that is not
   absolute is refused the same way. Otherwise the `Path` value of
   `HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Siemens\Shared Tools\PLCSIMADV_SimRT` plus
   `API\<highest version subfolder>`; folder names are compared as versions (`6.0`, `5.0`, or a
   plain integer), other names are ignored, and there is no fallback to a lower version.
   `PlcSimApiLocator` (in `TiaMcpServer.Contracts`) is shared by the worker and `doctor`.
2. **Loading.** `Assembly.LoadFile(<dll>)`, as the Siemens manual prescribes. The worker's
   `AssemblyResolve` handler answers only binds whose requesting assembly belongs to the API
   directory (the API DLL, a dependency the handler already resolved, or another assembly whose file
   lies in that directory), and only with a file of the requested simple name in that directory whose
   name also matches the requested version, culture and public key token where the request states
   them; a bind without a requesting assembly, and every other failed bind in the process, is left to
   .NET.
   A failed load is retried on the next request. A failed
   type initializer cannot be retried in the same process; it is `plcsim_api_unavailable` and the
   host restarts the worker.
3. **Binding.** `PlcSimApiSurface` resolves the single `PlcSimApiCatalog` once, by exact ordinal
   name and exact parameter types, through the declared candidates in order. Interface members are
   searched on `IInstance` and its base interfaces. A "C# call" candidate `Name()` matches a
   parameterless overload first, otherwise an overload whose every parameter is optional (invoked
   with the declared defaults, as `x.Name()` would compile). Several matching overloads are
   ambiguous and leave the member unresolved. Nothing outside the catalog is looked up. A binding
   that throws (for example a `TypeLoadException` for a missing dependency) is reported like a failed
   load (`probe_api` `loadError`, `plcsim_api_unavailable`, retried on the next request); only a
   failed type initializer sets the restart flag.
4. **Values.** Enum values are reported by name; no enum value is ever chosen by the server.
   `SInstanceInfo`, `STagInfo`, return values and out parameters are projected generically: all
   public fields and readable properties, primitive/enum/string values, nested objects to depth 2,
   one-dimensional arrays of such objects as JSON arrays of them (each element projected by the
   members its runtime type declares, at the array's own depth, at most 64 elements then one
   `<moreElements:N>` string; an empty array is `[]`, a null array `null`), markers
   (`<unsupported:Type>` for other arrays and collections, `<depthLimit:Type>`,
   `<unreadable:Exception>`) otherwise. `STagInfo.Dimension` is such an array (`SDimension[]`,
   shown by `probe_api`); live on 2026-11-02 it was still reported as `<unsupported:SDimension[]>`
   for every tag (defect K17). The member names of `SDimension` are not assumed anywhere; the array
   projection is offline-tested with stand-in types only and not live-tested. A
   getter that fails with a type initializer still yields `<unreadable:...>`, but also sets the
   worker's restart flag. `list_tags` filters by name before projecting and projects only the
   entries it returns; the readable members of each type are discovered once per process.
5. **Exceptions.** `TargetInvocationException` and `TypeInitializationException` are unwrapped
   before categorization; a `TypeInitializationException` anywhere in the inner-exception chain
   (bounded at 32 levels) makes the failure `plcsim_api_unavailable` and the worker restart-required. Return values are interpreted as described under
   [return values](#return-values). The server never calls `SimulationRuntimeManager.Shutdown()`,
   whose effect on the Runtime Manager is unverified.

## API surface

Provenance: **manual name** - the member name appears in the overview tables of the Siemens
"S7-PLCSIM Advanced API" Programming Manual V6.0 UPD1 (03/2024, A5E53246567-AB); the parameter
list could not be obtained, so the signature is unverified and resolved by `probe_api` at
runtime. **manual signature** - name and parameter list both appear there. **reference
signature** - name, return type and parameter list appear on the `IInstance` page of the Siemens
S7-PLCSIM Advanced developer reference for API 8.0 (`developer.siemens.com/s7-plcsim-advanced`,
C++/CLI notation, `DPRODUCT_VERSION 0x08000000`, DLL subfolder `API\8.0`); signature verified
against Siemens developer reference (API 8.0), while runtime `probe_api` remains the check for the
installed version. **secondary hint** - not in those sources; taken from an unverified secondary
source (an MIT-licensed PLCSIM unit-test worker) and used only if present.

| Member | Kind | Provenance | Declared candidates (in order) | Used by |
|---|---|---|---|---|
| `SimulationRuntimeManager.IsInitialized` | static property | manual name | `{ get; }` | probe_api, list_instances (optional) |
| `SimulationRuntimeManager.IsRuntimeManagerAvailable` | static property | manual name | `{ get; }` | probe_api, list_instances (optional) |
| `SimulationRuntimeManager.RegisteredInstanceInfo` | static property | manual name (type `SInstanceInfo[]`; element members unknown) | `{ get; }` | list_instances |
| `SimulationRuntimeManager.CreateInterface` | static method | manual signature | `CreateInterface(String)` | every instance operation |
| `IInstance.Name` | property | reference signature (`String^ Name { get }`) | `{ get; }` | every instance operation (exact-name check) |
| `IInstance.Dispose` | method | reference signature (IInstance inherits `System::IDisposable`) | `Dispose()` | every instance operation (optional; a warning when missing) |
| `IInstance.ID` | property | reference signature (`INT32 ID { get }`) | `{ get; }` | get_instance (optional) |
| `IInstance.OperatingState` | property | reference signature (`EOperatingState { get }`) | `{ get; }` | get_instance, control before/after (optional) |
| `IInstance.OperatingMode` | property | reference signature (`EOperatingMode`, "Gets or sets") | `{ get; }` | get_instance (optional) |
| `IInstance.ScaleFactor` | property | reference signature (`double`, "Gets or sets"; the setter is resolved at runtime) | `{ get; set; }` (Double or Single) | set_scale_factor; get_instance (optional) |
| `IInstance.CommunicationInterface` | property | reference signature (`ECommunicationInterface { get }`; .NET value name `Softbus` unverified, native `SRCI_SOFTBUS = 1`) | `{ get; }` | get_instance (optional); power_on, run, plcsim_test softbus warning |
| `IInstance.CPUType` | property | reference signature (`ECPUType { get }`) | `{ get; }` | get_instance (optional) |
| `IInstance.TagInfos` | property | reference signature (`array<STagInfo>^ { get }`) | `{ get; }` (array or `IEnumerable<T>`) | list_tags |
| `STagInfo.Name` | data member of the TagInfos element type | secondary hint | `String` field or property | list_tags `nameFilter` only |
| `IInstance.GetTagListStatus` | method | reference signature (`void GetTagListStatus(ETagListDetails% out_TagListDetails, bool% out_IsHMIVisibleOnly)`) | `GetTagListStatus(ref ETagListDetails, ref Boolean)` | list_tags `tagListStatus` (optional) |
| `IInstance.PowerOn` | method | reference signature (`ERuntimeErrorCode PowerOn()` and `PowerOn(UINT32 in_TimeOut_ms)`) | `PowerOn(UInt32)` (60,000 ms), then C# call `PowerOn()` | power_on |
| `IInstance.PowerOff` | method | reference signature (`void PowerOff()` and `PowerOff(UINT32 in_TimeOut_ms)`) | `PowerOff(UInt32)` (60,000 ms), then C# call `PowerOff()` | power_off |
| `IInstance.Run` | method | reference signature (`void Run()` and `Run(UINT32 in_TimeOut_ms)`) | `Run(UInt32)` (60,000 ms), then C# call `Run()` | run |
| `IInstance.Stop` | method | reference signature (`void Stop()` and `Stop(UINT32 in_TimeOut_ms)`) | `Stop(UInt32)` (60,000 ms), then C# call `Stop()` | stop |
| `IInstance.MemoryReset` | method | reference signature (`void MemoryReset()` and `MemoryReset(UINT32 in_TimeOut_ms)`) | `MemoryReset(UInt32)` (60,000 ms), then C# call `MemoryReset()` | memory_reset |
| `IInstance.UpdateTagList` | method | reference signature (`void UpdateTagList()`, plus overloads with `ETagListDetails`, `bool in_IsHMIVisibleOnly`, `String^ in_DataBlockFilterList`) | C# call `UpdateTagList()` (the parameterless overload) | update_tag_list |
| `IInstance.ReadBool` / `ReadInt8` / `ReadInt16` / `ReadInt32` / `ReadInt64` / `ReadFloat` / `ReadDouble` / `ReadString` | methods | reference signature (`bool`/`INT8`/`INT16`/`INT32`/`INT64`/`float`/`double`/`String^` `ReadX(String^ in_Tag)`) | `ReadX(String)` returning `Boolean`/`SByte`/`Int16`/`Int32`/`Int64`/`Single`/`Double`/`String` | read_tags, plcsim_test expect |
| `IInstance.ReadUInt8` / `ReadUInt16` / `ReadUInt32` / `ReadUInt64` | methods | Siemens developer reference, .NET `IInstance` (`UINT8`/`UINT16`/`UINT32`/`UINT64 ReadUIntX(String^ in_Tag)`, checked 2026-11-02) | `ReadUIntX(String)` returning `Byte`/`UInt16`/`UInt32`/`UInt64` | read_tags, plcsim_test expect (read-only: no `WriteUInt*` member found in the readable part of the reference; K32: live a Byte read with `ReadInt8` failed TypeMismatch -29) |
| `IInstance.WriteBool` / `WriteInt8` / `WriteInt16` / `WriteInt32` / `WriteInt64` / `WriteFloat` / `WriteDouble` | methods | reference signature (`void WriteX(String^ in_Tag, <type> in_Value)`) | `WriteX(String, <type>)` (any return type accepted) | write_tags, plcsim_test write |
| `SimulationRuntimeException` | type | manual name (error-code property name unverified) | the single public member typed `ERuntimeErrorCode` | error mapping |
| `SimulationInitializationException` | type | manual name | - | error mapping |
| `ERuntimeErrorCode` | type | manual name (via the exception); reference: negative = error, positive = warning, success = `SREC_OK` (native; value not captured). The .NET name `OK` is verified only at runtime (`okMemberFound`) | enum names and numeric values listed by probe_api (`enumMembers`) | error mapping; control/write return values (only `PowerOn` returns it per the reference) |

The `SimulationRuntimeManager` members, `STagInfo.Name` and the exception members were not checked
against the developer reference. Members named in the manual but not used: `Shutdown()`, `RegisterInstance(...)`,
`CreateInterface(Int32)`, `UnregisterInstance()`, `Info`, `Read()`, `Write()`, the `ReadUInt*`
family (unverified), `RunToNextSyncPoint()`, `IsSendSyncEventInDefaultModeEnabled`,
`StoragePath`, and the setters of `OperatingMode` and `CPUType`.

## Diagnostics

`tia-mcp doctor` reports a `plcsim-advanced` check: the API directory resolution source, the
registry value and version folders, the DLL path and whether it exists, and whether the PLCSIM
worker executable is present. It never loads the DLL or starts a worker. A machine without PLCSIM
Advanced passes (the component is optional); a set override or registry entry without the DLL,
a relative `TIA_MCP_PLCSIM_API_DIR` or registry `Path` (refused, as by the worker), or a missing
worker, is a warning.

## Out of scope

Creating, registering, unregistering or configuring instances; downloads through these three
tools (use `plc_download` through the `PLCSIM` interface); virtual-time or cycle-synchronized testing;
events; area (I/Q/M/DB offset) access; UInt and string writes; S7-1200 (not simulated by PLCSIM
Advanced).

## Live observations (2026-11-02)

Observed live on 2026-11-02 with TIA Portal V21 and S7-PLCSIM Advanced 8.0, instance `MCP_Test`
(CPU 1516, softbus), after `plc_download` of `PLC_1` through the `PLCSIM` interface. These are
observations, not a completed run of the acceptance steps below.

- **Tag list after a download (K26).** Right after `plc_download`, `list_tags` returned no tags
  until `plcsim_write update_tag_list` ran; afterwards the tags were listed. The `list_tags`
  warning that points to `update_tag_list` appeared and is the documented remedy; the server does
  not call `UpdateTagList()` itself (it is reported to be able to cold-restart the CPU, see
  `update_tag_list` above).
- **Offsets in optimized DBs (K26).** For members of an optimized (symbolic-access) DB, PLCSIM
  reported `Offset` 0 for every member. This is the value PLCSIM Advanced returns, not a projection
  error of the server; do not use `Offset` to address optimized DB members.
- **`Dimension` (K17).** `list_tags` reported `Dimension: "<unsupported:SDimension[]>"` for every
  tag; the array projection above addresses this and still needs a live check.
- `plcsim_write` `stop`/`run` and a `plcsim_test` scenario (write a set point of 42.5 and read it
  back, `Taster_Start` → `Motor_Freigabe`) passed.

## Live acceptance steps (to run once PLCSIM Advanced is installed)

1. Install and license S7-PLCSIM Advanced (V6.0 or later). Run `tia-mcp doctor --verbose`; record
   the `plcsim-advanced` evidence (source, version folder, DLL path, worker path).
2. In the Control Panel set online access to **PLCSIM**, create an S7-1500 instance (for example
   `MCP_Test`), and download a small test program through the `PLCSIM` interface (`plc_download`
   or TIA Portal):
   a `Start` Bool, a `Running` Bool set by a 500 ms TON after `Start`, an Int16, a Real, an LReal,
   a SInt, a DInt, an LInt and a String tag.
3. `plcsim_read probe_api` **first**. Archive the full document. Every required member must be
   resolved; record each resolved signature, the SInstanceInfo/STagInfo members, the enum names
   and values (`enumMembers`, in particular the value of `ERuntimeErrorCode.OK`)
   (including the `CommunicationInterface` value for the softbus), the `SimulationRuntimeException`
   error-code member, `okMemberFound` and the DLL file/product version. If any
   member is unresolved, stop and correct `PlcSimApiCatalog` candidates from this evidence before
   continuing.
4. `list_instances`; `get_instance` with the exact name and with a case variant (expect
   `target_not_found`) and with an unknown name (record the `plcsim_runtime_error` code).
5. `plcsim_write` with `dryRun:true` for every operation (no worker call; audit `phase:"preview"`).
6. `stop`, `run`, `power_off`, `power_on`, `memory_reset`: record `invokedSignature`,
   `timeoutArgumentMs`, `returnValue`, `returnCheck` and `operatingStateBefore/After`; confirm in the
   Control Panel. Provoke a failing call (for example `run` on a powered-off instance) and record
   whether it throws or returns a code, and record a `PowerOn` warning code (positive) if one occurs.
7. `update_tag_list`: observe whether the CPU cold-restarts (operating state, retentive values);
   record the finding and revise the warning.
8. `list_tags` with and without `nameFilter` (check `tagListStatus` before and after step 7);
   `read_tags` for every data type; `write_tags` for every writable type and read the values back;
   a write to a missing tag (stop-at-first-failure evidence).
9. `set_scale_factor` 0.5 and back to 1.
10. `plcsim_test`: `Start` true then `expect Running equals true` with `timeoutMs` 2000 (pass), an
    expect that must fail, `continueOnFailure` both ways, and `maxDurationMs: 600000` at the default
    worker timeout (clamp warning).
11. Read-only mode: only `plcsim_read` is listed and works.
12. Audit file: one `recordKind:"plcsim"` line per write/test call with the instance as target.
13. Stop the PLCSIM Runtime Manager during a session and record the failures; start a fresh server
    with the Runtime Manager stopped, run `probe_api` (expect `apiInitializationFailed` only if the
    static initializer fails), start the Runtime Manager and confirm the next call starts a fresh
    worker and succeeds.
14. With an instance whose communication interface is TCP/IP, confirm the `power_on`/`run` warning
    shows the raw value.
