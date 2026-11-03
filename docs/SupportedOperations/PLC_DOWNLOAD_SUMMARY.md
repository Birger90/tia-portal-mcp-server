# PLC download (`plc_download`, `plc_read list_download_targets`)

**Language:** English · [Deutsch](PLC_DOWNLOAD_SUMMARY.de.md)

`plc_download` downloads the bound standalone project's PLC either to a **PLC simulation** through
the `PLCSIM` PC interface or to a **real controller** through any other PC interface TIA Portal
lists. The human controls it through the access mode the server is started with: `read-only` offers
no download, `read-write` asks the user once for every actual download (MCP form elicitation), and
`full` downloads without prompts. There is no hard block on hardware downloads; the only refusals
are invalid, ambiguous or unresolvable input. The offline download path is listed by the
`plc_read` operation `list_download_targets`.

> **Status: not live-tested.** This feature is offline-qualified only (build against the
> compile-only reference stubs, unit tests, FakeWorker protocol tests). It has never run against
> TIA Portal, PLCSIM or a real CPU. Testing it needs TIA Portal V21 with PLCSIM or PLCSIM Advanced
> installed (and, for hardware, a disposable CPU that may be stopped). The mechanical signature
> checks are `scripts/verify-reference-stubs.ps1` (which also compiles the reference probe
> `reference-stubs/TiaMcpServer.OpennessReferenceProbe/DownloadReferenceSurface.cs`) and a build of
> the worker and probe against the installed
> `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48` on Windows. Every member
> marked *assumed* below must pass that real-DLL compile or the first live check before it is
> relied on. The first live checks are listed under [live acceptance checks](#live-acceptance-checks).

## Tool contract

### `plc_read` operation `list_download_targets`

Available in every access mode (`Observe`) on a verified standalone project. Optional `plcName`
(software or device name, case-insensitive); omitted reads every PLC. The result is
`{ plcs: [{ plcName, deviceName, downloadable, modes: [{ name, pcInterfaces: [{ name, number,
targetInterfaces: [{ name }] }] }] }] }`. It reads only `DownloadProvider.Configuration`
(modes → PC interfaces → target interfaces) from the offline project. It never calls
`GetAccessibleDevices()` (live network discovery), never applies a configuration and never goes
online. A PLC whose CPU device item offers no `DownloadProvider` reports `downloadable: false` and
no modes. A payload that does not decode is `protocol_error`.

### `plc_download`

Registered in read-write and full only (`OnlineControl`); absent from read-only discovery and
denied by host and worker authorization in read-only. Standalone projects only: a Multiuser local
session returns `unsupported_capability`.

| Argument | Default | Meaning |
| --- | --- | --- |
| `plcName` | none | PLC software or device name. Optional (omitted or explicit `null`) when the project has exactly one PLC; several matches are `target_ambiguous` with the candidates. |
| `pcInterface` | `{ name: "PLCSIM", number: 1 }` | Exact PC interface from `list_download_targets`. `PLCSIM` is the simulation interface; **any other name is real hardware**. When supplied, both members are required, `number` must be a JSON integer, unknown members are rejected. An explicit `"pcInterface": null` is an argument-shape error (the input schema advertises a non-null object): only omission selects the default. When it is omitted and the mode offers more than one PC interface named `PLCSIM` (whatever their numbers), the call is `target_ambiguous` before anything is downloaded. |
| `targetInterface` | none | Exact target interface. Optional (omitted or explicit `null`) only when the PC interface offers exactly one; otherwise `target_ambiguous` lists the candidates. |
| `mode` | `PN/IE` | Exact connection mode name. |
| `scope` | `hardware_and_software` | `hardware_and_software` → `DownloadOptions` `Hardware, Software`; `software` → `Software`; `software_only_changes` → `SoftwareOnlyChanges`. `Software` is never combined with `SoftwareOnlyChanges`; `None` is never used. |
| `startAfterDownload` | `true` | Answer the post-download `StartModules` decision with `StartModule` (true) or `NoAction` (false, CPU stays in STOP). |
| `passwords` sources | | Each value is an environment variable NAME, `{ "env": "NAME" }` or `{ "credential": "TiaMcp/..." }` (Windows Credential Manager generic credential); see [password sources](../guides/password-sources.md). The server's password map can add entries the call omits. |
| `decisions` | none | Caller answers for download configurations, overriding the built-in defaults; see [callback configurations](#callback-configurations-defaults-and-caller-decisions). Object: key = configuration type short name in `Siemens.Engineering.Download.Configurations` (exact, case-sensitive; the `decisionKey` reported in the evidence), value = the selection enum member name (exact, case-sensitive) for a selection configuration, or `true` for a check configuration. Explicit `null` means omitted. |
| `passwords` | none | Password configurations answered from environment variables. Object: key = password configuration type short name (`ModuleWriteAccessPassword`, `ModuleReadAccessPassword`, `BlockBindingPassword`, `PlcMasterSecretPassword` or any other password configuration), value = the **name** of an environment variable that holds the password, never the password itself. Explicit `null` means omitted. See [passwords](#passwords). |
| `portalConfirmations` | `decline` | TIA Portal `Confirmation` dialogs raised during the download: `decline` answers each No, records it and fails the call; `accept` answers each Yes and records it. See [confirmation dialogs](#tia-portal-confirmation-dialogs-during-a-download). |
| `trustedCertificates` | none | SHA-256 fingerprints of the device's TLS certificates that **the user** checked and approved: a JSON array of strings, each 64 hexadecimal characters (case-insensitive; colons and spaces are ignored), at most 16, no repeats after normalization. The only way the TLS certificate check of the download's connection (`TlsVerificationConfiguration`, online legitimation) is answered `Trusted`; omitted, explicit `null` or empty trusts nothing. See [TLS certificate trust](#tls-certificate-trust-online-legitimation). Not live-tested. |
| `rhTarget` | none | R/H system only (issue #16, not live-tested): `primary` or `backup` downloads to that CPU through `RHDownloadProvider.DownloadToPrimary`/`DownloadToBackup(IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)`, found among the CPU device item's `GetServiceInfos()` by exact full name and bound at runtime; the path is re-resolved on `RHDownloadProvider.Configuration`. Same decisions, passwords, guards and confirmation as a normal download; the prompt and `effects.rhTarget` name the CPU. A PLC without the provider fails `unsupported_capability` before anything is downloaded. Omitted: `DownloadProvider.Download`. |
| `dryRun` | `false` | Resolve the PLC, path and guards and return `phase: preview` without touching the target or prompting. |

Argument-shape errors (unknown root or nested members, nulls for non-nullable values, an explicit
`pcInterface: null`, wrong types, a `decisions` value that is neither a string nor `true`, a
`passwords` value that is not a string, a duplicate key in either object, nested values, a
`trustedCertificates` that is not an array or holds a non-string or `null` element) are the
canonical `validation_error` document with no audit record. An explicit `null` for `plcName`,
`targetInterface`, `decisions`, `passwords` or `trustedCertificates` means omitted, as for the optional selectors of the
other tools (for example `compile_check`'s `plcName`); a `null` *inside* a supplied `pcInterface`
(`{ name: null, number: 1 }`) is an entered `validation_error`. Entered rejections (domain
validation, binding, planning) are canonical root errors with one audit record.

Entered validation of `decisions` and `passwords` (shared with the worker as
`PlcDownloadPolicy.ValidateDecisions`): keys are identifiers (letters, digits, underscores, at most
256 characters) and unique across both objects; a full type name is refused with the short name to
use; at most 64 entries each; a selection is a non-blank identifier; `StartModules` is refused (use
`startAfterDownload`); `TlsVerificationConfiguration` is refused in both objects (a device certificate is
trusted only through `trustedCertificates`, never by a decision or password); a password value is an environment variable name (ASCII letters, digits and
underscores, not starting with a digit). **Unknown keys are allowed**: configuration types may only
appear at runtime. An invalid `passwords` value is never echoed: it is replaced by a fixed
placeholder while the arguments are bound, so it reaches neither the response nor the audit. A
valid variable name that is not set (or empty) in the server's environment may also be a pasted
password: it is replaced by a second placeholder while binding and refused before anything is listed or
downloaded, naming only the configuration (`passwords.<type>: the environment variable named for this
configuration is not set`). A variable name reaches the response and the audit only when it is set.
`trustedCertificates` entries that are not fingerprints (`trustedCertificates[i] is not a SHA-256
fingerprint`), repeats after normalization (`trustedCertificates[i] repeats trustedCertificates[j]`) and
more than 16 entries are entered `validation_error`s (`PlcTlsTrustPolicy.NormalizeList`, Contracts).

Resolution is exact and fallback-free: every level matches ordinal names (and the PC interface
number); a missing level is `target_not_found` with the configured candidates, a duplicate or an
unchosen multi-target interface is `target_ambiguous`. A default number is never used to pick one
of several PC interfaces with the same name (`PlcDownloadPolicy.IsImplicitInterfaceNumberAmbiguous`,
which applies to every interface name; because a supplied `pcInterface` always carries its number,
only the omitted `PLCSIM` default can reach it today). Planning uses only `list_download_targets`.
The worker then re-resolves the exact same names on the **same `DownloadProvider` instance** that
performs `Download(...)`, repeats the same-name check (the request carries whether the caller
supplied the number), requires exactly one match per level, and fails `state_changed` or
`target_ambiguous` (nothing downloaded) if the path changed. It never falls back to another
interface or target.

### What the `PLCSIM` name check does and does not prove

The guard `download_to_hardware`, the `simulationTarget` effect and the default `TargetForSoftware`
answer compare the **name** of the selected PC interface entry of TIA Portal's offline download
configuration (`ConfigurationPcInterface.Name`, ordinal `PLCSIM`). That proves only that TIA Portal
listed a PC interface entry with exactly that name. It does **not** prove:

- that the entry is TIA Portal's simulation interface rather than another adapter carrying the same
  name (that PC interface names are assigned by TIA Portal and not user-editable is assumed, not
  verified);
- that a simulator (S7-PLCSIM or S7-PLCSIM Advanced) is running, or which instance answers at the
  target interface; nothing is discovered online (`GetAccessibleDevices()` is never called);
- that the instance reached cannot itself communicate with real devices: an S7-PLCSIM Advanced
  instance whose communication interface is not the softbus is reached through a (virtual) Ethernet
  adapter rather than this interface, and `plcsim_write` `power_on`/`run` and `plcsim_test` warn
  when an instance is not on the softbus (see the [PLCSIM reference](PLCSIM_OPERATIONS_SUMMARY.md));
- conversely, that a non-`PLCSIM` interface reaches a real controller (it may reach a PLCSIM
  Advanced instance on a virtual adapter); the hardware guard is conservative.

Response (contract `2.0`): `{ tool, contractVersion, success, error, warnings, phase, guards,
effects, result }`. `effects` holds the resolved PLC, device, `path { mode, pcInterface { name,
number }, targetInterface }`, whether the target interface was selected automatically, scope,
`DownloadOptions` names, `startAfterDownload`, `simulationTarget` (the PC interface is exactly
`PLCSIM`), the caller `decisions` (`[{ type, selection, check }]`, ordinal by type), `passwords`
(`[{ type, variable }]`, variable **names** only), `portalConfirmations` and `trustedCertificates`
(the normalized fingerprints, upper case without separators, in caller order; empty when none). `result` is a
`{ status, value, failure, omission }` outcome whose `value` is the typed projection of the
download:

- `state` (`DownloadResultState` name), `errorCount`, `warningCount` — null when `Download`
  returned no result (aborted or failed);
- `messages` — recursive `{ message, state, messages[] }`, at most 8 levels and 500 messages,
  text cut at 1,000 characters, with `messagesComplete` and `messageOmission`;
- `configurationsSeen` — every callback configuration `{ phase, type, canonicalType, decisionKey,
  kind, message, availableSelections, decision, selection, source, passwordVariable }` (plus `tls`
  for the TLS certificate check), answered or not. `phase` is `pre_download`, `post_download` or
  `online_legitimation` (presented through the connection's `OnlineLegitimation`; `message` is
  always null there). `type` is the runtime type name; `canonicalType` the public
  `Siemens.Engineering.Download.Configurations` type (online legitimation:
  `Siemens.Engineering.Online.Configurations` type) it exactly is (or null); `decisionKey` its
  short name, the key for `decisions`/`passwords`; `kind` is `selection`, `check`, `password` or
  `other` (what the live object exposes); `availableSelections` the live `CurrentSelection` enum
  member names of a selection configuration (at most 64; empty otherwise); `decision` is
  `selected`, `checked`, `password_set` or `aborted`; `selection` the member set; `source`
  `default`, `caller` or `policy` (a TLS question refused with `NonTrusted`, K27); `passwordVariable` the environment variable **name** a password came from;
- `abort` — `{ phase, type, canonicalType, decisionKey, kind, message, availableSelections,
  reason, detail }` (plus `tls` when it is the TLS certificate check) of the configuration that
  stopped the download, or null (see [abort reasons](#abort-reasons));
- `portalConfirmationPolicy` (`decline` or `accept`), `portalConfirmations` — the TIA Portal
  `Confirmation` dialogs raised while the download ran `{ type, text, answer }`, `answer` `No` under
  decline and `Yes` under accept (empty when none; the first 100 are listed, text cut at 1,000
  characters; `text` is null unless the event arguments expose a readable public `Text` string, an
  unverified member); `portalConfirmationsObserved` is the total answered and
  `portalConfirmationsComplete` is false when more were answered than are listed;
- `plcName`, `deviceName`, `path`, `scope`, `downloadOptions`, `startAfterDownload`;
- `completionVerified: false` and `completionNote`. Siemens documents that V21 `Download()` may
  return before the device finished processing the download; the tool never claims the PLC is
  running. A worker payload claiming `completionVerified: true` is rejected as `protocol_error`.

Root `success` is true only when the download returned, `state` is not `Error`, `errorCount` is 0,
no configuration was aborted and no TIA Portal confirmation was declined (accepted confirmations do
not fail the call; they add a warning). An attempted download that failed is `phase: applied`,
`success: false`, `error: null`, `isError: false`, with the evidence in `result.value` and the
failure in `result.failure`; inspect the PLC or simulator before retrying. There is no rollback or
replay. When the value exceeds the configured value budget, download messages are shed first (the
state, counts, configurations and abort record are kept); only then is the whole value omitted.

## Safety model

- **The human controls the AI through the access mode.** `read-only`: no download (the tool is not
  registered, and host and worker deny `plc_download`). `read-write`: every actual download asks the
  user once. `full`: no prompts. Nothing else blocks a download to hardware.
- **Capability.** The worker operation `plc_download` is `OperationCapability.OnlineControl`,
  allowed in read-write and full only. `list_download_targets` is `Observe`. Both require the exact
  verified session identity.
- **Pipeline.** `PlcDownloadWriteDomain` runs through `WriteExecution` as one single-item guarded
  write: validate, verified binding, pinned lease, plan, guards, dry-run stop, confirmation,
  re-plan after the prompt, mutate, compose, one audit v2 record per call (every phase).
- **Confirmation.** `ConfirmsEveryCall = true`, like lifecycle: every actual read-write call asks
  the user through form elicitation (`accept` with `confirm: true`); decline, cancel, timeout,
  transport failure or a client without elicitation deny with `access_denied` before any download.
  The prompt (after the acknowledge guard lines) opens with the target kind, for a hardware
  interface **"Confirm plc_download to a REAL controller (NOT the PLCSIM simulation interface; the
  machine it controls may stop or move)"**, and whether the CPU is restarted, then names the PLC,
  device and exact path (quoted and escaped, like every name in a confirmation prompt), lists the built-in answers (with the effective `TargetForSoftware`
  answer), every caller decision, every password variable name, the TLS sentence (without
  `trustedCertificates`: "A TLS certificate check of the device (TlsVerificationConfiguration) aborts the
  download: no certificate is trusted"; with them: every normalized fingerprint, and that a check is
  answered `Trusted` only when every presented certificate is listed, otherwise `NonTrusted` and abort),
  and how TIA Portal confirmations will be answered. After acceptance the path is re-planned and must be unchanged, or the call
  stops as `binding_conflict` without downloading. Full runs under policy without server
  elicitation. Dry runs never prompt.
- **Guards** (closed production catalog; three acknowledge guards and one block guard, both added for the F-program configuration):

  | Guard | Severity | Fires when |
  | --- | --- | --- |
  | `download_to_hardware` | acknowledge | The resolved PC interface name is not exactly `PLCSIM` (ordinal): the download goes to a real controller and the machine it controls may stop or move; `TargetForSoftware` is answered `CPU` unless the caller decides otherwise. Satisfied by the read-write prompt or the full-mode policy. |
  | `download_stops_plc` | acknowledge | Every download: the CPU may be stopped (StopModules is answered `StopAll` unless the caller decides otherwise) and is restarted only when `startAfterDownload` is true. Satisfied by the read-write prompt or the full-mode policy. |
  | `download_includes_safety_program` | acknowledge | `decisions` answers `SafetyProgram` (the F-program configuration), on PLCSIM or a real controller (which also fires `download_to_hardware`); the message carries the safety acceptance notice ([PLC safety reference](PLC_SAFETY_SUMMARY.md)). There is no block guard: an F-program download to an F-CPU is confirmed like any hardware download (owner decision 2026-11-01). |

  The acknowledge guards are legal only because this domain confirms every call; their definitions
  are registered like lifecycle's, not through `WriteGuardCatalog.NonInteractive` (which refuses
  acknowledge severity). A dry run reports both with `acknowledged: false` in read-write.
- **Worker defense in depth.** The worker refuses a blank PLC/device/mode/interface/target, an
  unknown scope or portal policy, malformed decisions or passwords (all through the Siemens-free,
  unit-tested `PlcDownloadPolicy.ValidateWorkerRequest`, which the FakeWorker also uses), several
  equally named PC interfaces without an explicit number, or a path that no longer resolves
  exactly, before calling `Download`. It requires the worker-internal confirmation fence. It no
  longer checks for `PLCSIM`. `Download` never runs inside a `Transaction` or `ExclusiveAccess`.
- **Errors.** A Siemens `EngineeringException` from `Download` is classified by
  `WorkerExecution.Classify` with the mutation flag set (`OnlineControl` counts as mutating):
  `EngineeringTargetInvocationException` → `engineering_rejected`, other engineering errors →
  `worker_operation_failed`, both with `engineeringError`. `NonRecoverableException` or an
  observed Portal disposal → `portal_lost`, which wins and invalidates the binding. A `portal_lost`
  download failure message also says that TIA Portal may have terminated (K27: after such a failure
  on 2026-11-02 no TIA Portal process was left), to check with `bind_project` action `list_portals`,
  and to start TIA Portal, re-open the project (`open_project`) and bind it again; nothing is
  restarted or re-opened automatically. Every failure
  after `Download` was invoked carries the typed evidence in `WorkerResponse.PlcDownloadOutcome`
  (payload text decoded host-side by the strict required-member reader; malformed evidence is
  `protocol_error` and is not echoed).

## Callback configurations: defaults and caller decisions

Both delegates and the connection's online legitimation handler are fail-closed and run the same Siemens-free decision loop,
`PlcDownloadCallbackRecorder` (Contracts), which the worker and the FakeWorker share and which is
unit-tested without Siemens assemblies. The answers come from `PlcDownloadDecisionSet`; the live
objects are read and answered by `PlcDownloadReflectionAccess` (Contracts, Siemens-free; the worker
injects only the compiled `DownloadConfiguration.Message` read and `Environment.GetEnvironmentVariable`).

- **Exact type match.** The canonical type is the first *public* type in the configuration's
  runtime type chain (`GetType()`, then `BaseType`) whose namespace is
  `Siemens.Engineering.Download.Configurations` (nested types never match). Its short name is the
  `decisionKey`. The one exception is the exact public type `Siemens.Engineering.Safety.Download.Configurations.SafetyProgram`
  (V21 installed-API baseline), whose key is `SafetyProgram` and which has no default answer. Non-public or other-namespace implementation types are walked past; a public
  subclass of a defaulted type in that namespace is its own type (an `is` check would have matched
  it) and needs its own decision. That the live objects' type chain contains the public V21 type is
  assumed — verify live. A configuration without such a type has no key and aborts
  (`unsupported_configuration`).
- **Answer order.** A `passwords` entry for the key, else a `decisions` entry for the key (in any
  phase), else the built-in default for the phase, else abort (`decision_required`). Caller decisions
  override the defaults. The TLS certificate check of the `online_legitimation` phase never follows
  this order (see [TLS certificate trust](#tls-certificate-trust-online-legitimation)).
- **Online legitimation.** The connection's `OnlineLegitimation` configurations are a third phase of
  the same loop (`online_legitimation`; canonical types of `Siemens.Engineering.Online.Configurations`).
- **Applying an answer, generically, by reflection on the runtime object** (never a compiled cast):
  - a selection name: the object must expose exactly one public readable and writable enum property
    `CurrentSelection`; the name is parsed against **that property's own enum type** by exact,
    case-sensitive member name (`Enum.IsDefined`; numbers and flag combinations are refused) and set;
  - `true`: the object must expose exactly one public readable and writable `bool` property
    `Checked`, which is set to true (`DownloadCheckConfiguration.Checked`);
  - a password: see [passwords](#passwords).
  A missing or ambiguous member fails closed (`decision_not_applicable` with a `detail`); an unknown
  selection name fails closed (`selection_unavailable`, the abort lists `availableSelections`); an
  exception from the setter or `SetPassword` is unwrapped from reflection and recorded as
  `decision_rejected`, reporting the setter's engineering error (`engineeringError`) when it was
  one; a `NonRecoverableException` or disposed-object exception is rethrown unchanged after
  recording, so the shared classification still applies (`portal_lost` for a lost Portal) with the
  evidence attached.
- **Evidence.** Every configuration is recorded with its `kind` (reflection: `SetPassword(SecureString)`
  → `password`, `bool Checked` → `check`, enum `CurrentSelection` → `selection`, else `other`), its
  live `availableSelections`, and every applied answer with its `source` (`default` | `caller` | `policy`, the last only for a TLS question answered `NonTrusted`).
- **After an abort** every later callback is recorded as aborted with the first abort's reason and
  throws again; nothing more is applied (a later TLS question is the exception: `NonTrusted` without a
  throw, see [TLS certificate trust](#tls-certificate-trust-online-legitimation)). **Risk (K27):**
  throwing from TIA Portal's online-legitimation callback was observed live (2026-11-02) right before TIA
  Portal terminated; whether a throw from the pre- and post-download delegates (an unanswered selection,
  check or password configuration) can terminate TIA Portal as well has no live evidence. No documented
  non-throwing way to cancel those configurations exists, so their behaviour is unchanged; answer every
  configuration the download presents (`decisions`, `passwords`) to avoid the throw. The first abort stays in `abort`. A download presenting more
  than 100 configurations is aborted (`configuration_limit_exceeded`; at most 101 entries are
  recorded).

Built-in defaults (unchanged allowlist, now overridable):

| Phase | Configuration type | Default answer |
| --- | --- | --- |
| pre-download | `StopModules` | `StopAll` |
| pre-download | `ConsistentBlocksDownload` | `ConsistentDownload` |
| pre-download | `AlarmTextLibrariesDownload` | `ConsistentDownload` |
| pre-download | `OverwriteSystemData` | `Overwrite` |
| pre-download | `TargetForSoftware` | `PlcSimulationAdvanced` when the PC interface is exactly `PLCSIM`, otherwise `CPU` |
| post-download | `StartModules` | `StartModule` when `startAfterDownload`, else `NoAction` (not overridable through `decisions`) |
| either | anything else, or a defaulted type in the other phase | caller decision or password entry; without one the download aborts (`decision_required`) |
| online legitimation | `TlsVerificationConfiguration` | none: `Trusted` only from `trustedCertificates` (every presented certificate listed), otherwise `NonTrusted` and abort; see [TLS certificate trust](#tls-certificate-trust-online-legitimation) |
| online legitimation | any other online configuration | none: a `passwords` entry for a password configuration, otherwise abort |

Examples of configuration types a caller may decide (catalogue in source (b); enum members are
read from the live object, so `availableSelections` is authoritative): `DataBlockReinitialization`
(`StopPlcAndReinitialize`, `NoAction`), `DataBlockReinitializationOrKeepActualValues`,
`DifferentTargetConfiguration` (`AcceptAll`, `NoAction`), `ResetModule` (`DeleteAll`, `NoAction`),
`InitializeMemory`, `OverwriteOnMemoryCard`, `ExpandDownload`, `ProtectionLevelChanged`,
`UserManagementDownload`, `WaitOnReboot`; check configurations such as `CheckBeforeDownload`,
`UpgradeTargetDevice`, `DowngradeTargetDevice`, `OverwriteHmiData`, `FitHmiComponents`,
`DownloadCertificate` take `true`. Several of them have serious consequences (reset, memory
initialization, reinitialized data blocks, firmware change): in read-write the human sees each
caller decision in the prompt before anything happens.

### Abort reasons

| Reason | Meaning | Retry with |
| --- | --- | --- |
| `decision_required` | The configuration has a `decisionKey` but no default, decision or password entry. | `decisions: { "<decisionKey>": "<one of availableSelections>" }` (selection), `true` (check), or `passwords` (password kind) |
| `unsupported_configuration` | Its type chain has no public type of the configurations namespace, so no key exists. | nothing; report it |
| `selection_unavailable` | The selection (default or caller) is not a member of the live enum. | one of `availableSelections` |
| `decision_not_applicable` | The live object lacks the member the answer needs (`CurrentSelection`, `Checked`, `SetPassword`); `detail` says which. | an answer of the right kind |
| `password_unavailable` | The named environment variable is not set or empty in the worker; `detail` does not repeat the name (it may be a pasted password). | set the variable in the MCP client configuration and restart |
| `decision_rejected` | Setting the answer threw (TIA Portal rejected it). | inspect `engineeringError` |
| `configuration_limit_exceeded` | More than 100 configurations. | nothing; report it |
| `certificate_not_trusted` | `online_legitimation` only: the TLS certificate check presented no certificate, more than 16, or at least one whose SHA-256 is not in `trustedCertificates`; `NonTrusted` was set and `abort.tls` holds the evidence. | `trustedCertificates` listing every presented fingerprint, **only after the user checked them** |

For the TLS certificate check, `decision_not_applicable` means its evidence could not be read completely
or `CurrentSelection` is missing or not a `TlsVerificationConfigurationSelection` (nothing was set),
`selection_unavailable` that the live enum lacks `Trusted`, and `decision_rejected` that setting `Trusted`
or `NonTrusted` threw; none of them can be retried with an argument.

The failure message (`PlcDownloadPolicy.AbortMessage`, shared by worker, FakeWorker and host) spells
out the retry, for example `Retry with decisions: { "DataBlockReinitialization": "<selection>" }
using one of: StopPlcAndReinitialize, NoAction`. An abort is reported as an attempted failure
(`worker_operation_failed`) with the evidence; a post-download abort means the download was already
transferred and the CPU may be stopped.

The host independently rejects (`protocol_error`) evidence in which an applied entry is not exactly
what the planned decision set prescribes for its canonical type and phase (selection, kind of
answer, `source`, `passwordVariable`; for `StartModules`, the answer `startAfterDownload`
requires; a default where the caller decided), an applied entry follows an aborted one, an aborted
entry carries an answer, `decisionKey` is not the short name of `canonicalType`, a `canonicalType`
is outside the namespace, `availableSelections` is listed for another kind, the `abort` does not
describe the first aborted entry, or its reason does not fit the plan (for example
`selection_unavailable` where nothing was decided, `unsupported_configuration` with a key, or
`configuration_limit_exceeded` before the 101st entry). For the TLS certificate check it also rejects
evidence in which an entry other than an `online_legitimation` `TlsVerificationConfiguration` carries
`tls`, an `approved` flag differs from membership in the planned `trustedCertificates`, `Trusted` is
recorded without source `caller`, after an abort or with evidence that does not allow it (an unreadable
member, no or too many certificates, one not approved), a `NonTrusted` answer is recorded without source
`policy`, without `tls.selectionSet: NonTrusted` or without an `abort` (it anchors the abort like the first
aborted entry; no download configuration may be answered after it), the abort's
`tls` differs from the first aborted (or refused) entry's, `certificate_not_trusted` comes with evidence that would
allow `Trusted`, or an `online_legitimation` entry carries a `message`.

## Passwords

`passwords` maps a password configuration type short name to the **name** of an environment
variable. The worker process inherits the server's environment, which the user sets in the MCP
client configuration (`"env": { "PLC_WRITE_PW": "..." }` next to `"command"`), so the password never
passes through the model or the tool arguments. For a matching configuration the worker reads the
variable from its own environment, copies it into a read-only `SecureString`, calls
`SetPassword(SecureString)` on the live object (by reflection, exactly one public instance method
with that single parameter) and disposes the `SecureString`. A missing or empty variable aborts with
`password_unavailable`, naming the variable (never a value); the host refuses it already at
validation. Passwords never appear in results, logs or the audit: the evidence records only
`passwordVariable` (the name) and the effects list `{ type, variable }`. Without a matching
`passwords` entry a password configuration aborts (`decision_required`, kind `password`). The
environment string itself cannot be zeroed in managed memory (see limits).

## TLS certificate trust (online legitimation)

> **Not live-tested.** Built for a live finding (TIA Portal V21, PLCSIM Advanced 8.0): `plc_download` to
> PLCSIM failed with "Verbindung mit Baugruppe PLC_1 fehlgeschlagen" and no `configurationsSeen`, while
> TIA Portal's own download first asks whether to trust the device's unknown self-signed certificate.
> The download never subscribed `ConnectionConfiguration.OnlineLegitimation`. Whether V21 raises that
> question through the download provider's `OnlineLegitimation` during `Download(...)` is not stated by
> any source; the [first live check](#live-acceptance-checks) decides. Offline qualification: unit
> tests (`PlcTlsTrustPolicyTests`, `PlcCallbackRecorderTlsTests`) and the FakeWorker scenario
> `plc-download-tls` (`PlcTlsTrustMcpTests`, a stand-in `TlsVerificationConfiguration` with two fixed test
> certificates presented through the production recorder).

- **Registration.** The worker reads `DownloadProvider.Configuration` once; that same
  `ConnectionConfiguration` instance resolves the path and carries the handler. One
  `OnlineConfigurationDelegate` is subscribed to its `OnlineLegitimation` event immediately before
  `Download(...)` and the same delegate is removed in `finally` (source: online-status.md, "TLS
  legitimation and online authentication"). Every configuration presented there goes through the same
  `PlcDownloadCallbackRecorder` as the download configurations, phase `online_legitimation`, and is
  recorded in `configurationsSeen`. Its canonical type is the first public type of
  `Siemens.Engineering.Online.Configurations` in the runtime type chain; the short name is the
  `decisionKey`. Online configurations document no `Message`, so `message` is null in this phase.
- **The trust question** (`TlsVerificationConfiguration`) is never answered by a default, a decision or
  a password. The shared Siemens-free `PlcTlsTrustPolicy` (Contracts) first reads the evidence by
  reflection on the runtime object: `PlcName` and `VerificationInfo` as text (their types are
  undocumented), and for every item of `Certificates` its `Certificate` (`X509Certificate2`): the
  SHA-256 over `RawData`, the SHA-1 `Thumbprint`, `Subject`, `Issuer`, `NotBefore` and `NotAfter` (UTC). It
  also checks that `CurrentSelection` is exactly one public readable and writable property of the
  documented enum type `TlsVerificationConfigurationSelection`. `CurrentSelection` is set to `Trusted`
  (source `caller`) only when every member could be read, at least one and at most 16 certificates were
  presented and the SHA-256 fingerprint of **every** presented certificate is in `trustedCertificates`.
  Otherwise `NonTrusted` is set (only when `CurrentSelection` has its documented type; nothing is
  written to a missing or foreign one). **When `NonTrusted` was set the callback returns normally
  (K27, since 2026-11-02):** the entry is recorded as `selected` `NonTrusted` with source `policy`,
  `abort` is recorded with the evidence (`certificate_not_trusted`, or `decision_not_applicable` /
  `selection_unavailable` / `decision_rejected` when evidence or the `Trusted` answer failed), and TIA
  Portal fails the connection itself, like "Verbindung abbrechen" in its own trust prompt. Whatever
  `Download(...)` then does (returns, throws an `EngineeringException`), the recorded abort decides: the
  worker reports `worker_operation_failed` with `abort.tls`, never success. Only when `NonTrusted` could
  not be set does the callback still throw (source d: "A callback exception cancels the operation"),
  the only remaining refusal and a known risk (below). A later TLS question after an abort is never
  trusted and is answered `NonTrusted` again without a throw where possible.

  > **Live observation K27 (2026-11-02, TIA Portal V21, PLCSIM Advanced 8.0, project `MCP_Safety_01`,
  > PLC_F1 = CPU 1516F-3 PN/DP 6ES7 516-3FN02-0AB0 V2.9, PC interface PLCSIM #1, target `1 X1`, fresh
  > PLCSIM instance `MCP_Safety`, CPU1500_Unspecified, Stop).** With the former throw from the TLS
  > callback, `plc_download` without decisions, `plc_download` with `{ SafetyProgram: ConsistentDownload }`
  > and `plc_tia_online go_online` (no download at all) each ended in `portal_lost` ("Access to a disposed
  > object of type 'Siemens.Engineering.Connection.ConnectionConfiguration'") and TIA Portal was no longer
  > running (`bind_project list_portals` → no portals); the instance was unchanged (nothing transferred).
  > The same "Online verbinden" in the TIA UI showed the trust prompt ("'PLC_F1' ist möglicherweise kein
  > vertrauenswürdiges Gerät": IP address does not match the certificate, unknown self-signed certificate);
  > "Verbindung abbrechen" left TIA Portal running and the online status said the device is not
  > trustworthy. The standard CPU PLC_1 of `MCP_Livetest_01` never showed the prompt. That the throw
  > caused the termination is the most likely explanation, not a proven cause.
  >
  > **Re-test with the non-throwing refusal (2026-11-02, build d111e8c):** `plc_tia_online go_online` on
  > PLC_F1 without `trustedCertificates` returned `worker_operation_failed` with `abort.tls` (the
  > fingerprints) and TIA Portal kept running; with the fingerprint the user had checked
  > (`trustedCertificates`) the connection was trusted. **A fail-safe download through Openness is refused
  > by TIA Portal itself:** `plc_download` of PLC_F1 to PLCSIM with the trusted certificate failed with an
  > `EngineeringTargetInvocationException` from `DownloadProvider.Download` whose message was "Laden oder
  > Überladen fehlersicherer Daten mit Openness ist nicht erlaubt." (loading fail-safe data with Openness is
  > not allowed). So `decisions: { SafetyProgram: ... }` was never reached on V21 for this F-CPU; the
  > F-program had to be downloaded in the TIA Portal UI ("Erweitertes Laden", PLCSIM, F-CPU identified by
  > its serial number), after which the MCP drove the test (`plcsim_test`, `compare_to_online` identical,
  > F-collective signature online = offline). Whether other F-CPUs, firmware versions or a standard-only
  > change of an F-CPU can be downloaded through Openness is not tested. There is no default trust, no trust-all option and
  no way to skip the check; `EnableLegacyCommunication` is never read or set (the source: setting it
  disables TLS).
- **Online passwords** presented in this phase (`OnlinePasswordConfiguration`, `OnlineReadAccessPassword`:
  `SetPassword(SecureString)`) are answered from `passwords` like download passwords (key = type short
  name, value = environment variable name). There is no default for any online configuration; anything
  else, for example `OnlineAuthenticationConfiguration` (credentials through `OnlineCredentials`, not
  answered by this tool), aborts with `decision_required` or `decision_not_applicable`.

The `tls` evidence (on the entry in `configurationsSeen` and, for a refusal, on `abort`):

| Member | Meaning |
| --- | --- |
| `plcName`, `verificationInfo` | `PlcName` and `VerificationInfo` as text (null when null or unreadable) |
| `certificates[]` | per readable certificate, in presentation order: `sha256` (64 upper-case hex characters over the DER bytes: the value `trustedCertificates` takes), `sha1Thumbprint` (only for viewers that show nothing else; never accepted as a fingerprint), `subject`, `issuer`, `notBefore`, `notAfter` (ISO 8601 UTC; an expired certificate is reported, not refused: the user decides), `approved` (its `sha256` is in `trustedCertificates`) |
| `certificatesPresented`, `certificatesComplete` | how many the device presented (one more than 16 when it presented more) and whether every one was read and listed |
| `unreadableMembers` | members that were missing, ambiguous, threw or (`CurrentSelection`) do not have the documented type; trust is never given then |
| `selectionSet`, `source` | `Trusted` with source `caller`, `NonTrusted` with source null (the `configurationsSeen` entry then has selection `NonTrusted`, source `policy`; K27), or null when nothing could be set (the callback threw) |

The abort message (`PlcDownloadPolicy.AbortMessage`) lists every certificate with its SHA-256, subject,
issuer and validity, says whether `NonTrusted` was set, tells the agent not to decide itself but to show
the fingerprints to the user and have them compare them with the device's own certificate (for example
in TIA Portal's trust prompt with "Zertifikate anzeigen" / "Show certificates"; where a viewer shows only
a SHA-1 thumbprint, with `sha1Thumbprint`), and gives the exact retry
`trustedCertificates: ["<sha256 of certificate 1>", "<sha256 of certificate 2>"]` (every presented
certificate must be listed). The intended flow is: call without `trustedCertificates`, read
`result.value.abort.tls`, let the user check, retry with every fingerprint. In read-write the retry's
prompt lists the fingerprints, so the user approves them once more before anything connects; the audit
record keeps the requested arguments and the response, both with the fingerprints (fingerprints are
not secrets).

## TIA Portal confirmation dialogs during a download

The worker's `TiaPortalSession` answers TIA Portal `Confirmation` events session-wide: `Yes` in
read-write and full, `No` in read-only. While `plc_download` runs, a scope opened around the
worker method (never nested; disposed in a `finally` as a safety net) answers **every**
confirmation with the request's policy before recording it in `portalConfirmations`: `No` for
`decline` (default) and `Yes` for `accept` (`Yes` only when the worker's access mode allows TIA
confirmations, which every mode that can download does). A dialog TIA Portal raises before
`Download` returns and the evidence is collected therefore gets the caller's policy, never the
session-wide answer; a later one gets the same answer until the next worker request starts (see the end of this section).

Before the evidence is copied, the download closes the scope; closing waits for a recording in
progress, so every confirmation answered in the scope is in the evidence and counted in
`portalConfirmationsObserved`. Under `decline` any confirmation fails the call
(`worker_operation_failed` with the evidence) even when the download itself reported
`state: Success` with no errors; the failure message says why and suggests
`portalConfirmations: "accept"`. Answering `No` may have made TIA Portal skip or cancel part of the
download, so inspect the PLC or simulator. Under `accept` the call is not failed because of them;
a warning reports how many were answered Yes. Whether V21 raises such dialogs during `Download` is
unverified (see the [first live check](#live-acceptance-checks)). A confirmation raised **after**
the scope closed (V21 `Download()` may return before the device finished, so a late dialog from the
same download is possible) is not in the evidence, but it is never answered by the session-wide `Yes`:
the closed scope lingers with the call's answer until the next worker request starts
(`TiaPortalSession.EndLingeringConfirmations`, called before every request; a new scope also ends it),
logs each late dialog at once (between requests worker stderr reaches the server log) and reports
them as a warning on the response of that next request, whatever tool it belongs to. Operations
without a scope keep the session-wide policy unchanged.

## Provenance of every Siemens member used

Sources (read-only reference data, never executed): **(a)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/online-and-download/SKILL.md`; **(b)** `totally-integrated-claude`
`skills/tia-plc-operations/references/download-upload.md` and
`skills/tia-networks/references/online-connection-configuration.md` (stated as checked against the
installed V21 XML documentation); **(c)** the V21 type-name baseline extracted from real DLL metadata
(`api-baselines/v21/base.json`, `step7.json`; type names and assemblies only, no signatures);
**(d)** `totally-integrated-claude` `skills/tia-plc-operations/references/online-status.md`, sections
"TLS legitimation and online authentication" and "Installed V21 online configuration type catalogue"
(the only source used for the online legitimation members).

Status values: **type name verified in V21 baseline** (c lists the type in that assembly);
**signature from V21-XML-checked reference** (b shows the member or call shape); **assumed — verify
by real-DLL compile** (neither a nor b shows the member; added to the stubs only because the brief
fixes it); **reflection** (bound at runtime on the live object, never compiled; a missing member
fails closed with an abort reason, so a wrong assumption cannot crash the worker but can abort).

| Siemens member | Assembly (c) | Source | Status |
| --- | --- | --- | --- |
| `Siemens.Engineering.Download.DownloadProvider` (type, an `IEngineeringService`) | Base | a, b, c | type name verified in V21 baseline; service role from `GetService<DownloadProvider>()` in a/b |
| `DeviceItem.GetService<DownloadProvider>()` on the PLC's CPU device item (existing `HardwareObject.GetService<T>`; the device item is the one whose `SoftwareContainer` carries the `PlcSoftware`, the repo's existing locator route) | Base | a, b | signature from V21-XML-checked reference |
| `DownloadProvider.Configuration` → `ConnectionConfiguration` | Base | b | signature from V21-XML-checked reference |
| `DownloadProvider.Download(IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)` → `DownloadResult` | Base | a, b, c | signature from V21-XML-checked reference |
| `DownloadConfigurationDelegate` = `void (DownloadConfiguration)` | Base | b, c | type name verified in V21 baseline; signature from V21-XML-checked reference (method-group callbacks in b) |
| `ConnectionConfiguration.Modes` (`ConfigurationModeComposition`, enumerated) | Base | b, c | signature from V21-XML-checked reference |
| `ConfigurationMode.Name`, `.PcInterfaces` (`ConfigurationPcInterfaceComposition`, enumerated) | Base | b, c | signature from V21-XML-checked reference |
| `ConfigurationPcInterface.Name`, `.Number` (`int`), `.TargetInterfaces` (`ConfigurationTargetInterfaceComposition`, enumerated) | Base | b, c | signature from V21-XML-checked reference |
| `ConfigurationTargetInterface.Name`; converts to `IConfiguration` | Base | a, b, c | signature from V21-XML-checked reference |
| `IConfiguration` (marker interface; no members used) | Base | b, c | type name verified in V21 baseline |
| `DownloadOptions` members `Hardware`, `Software`, `SoftwareOnlyChanges` (parsed by name) | Base | a, b, c | type name verified in V21 baseline; member names from V21-XML-checked reference |
| `DownloadResult.State` (`DownloadResultState`), `.ErrorCount`, `.WarningCount`, `.Messages` (`DownloadResultMessageComposition`) | Base | b, c | signature from V21-XML-checked reference |
| `DownloadResultState.Error` (compared by name) | Base | b, c | signature from V21-XML-checked reference |
| `DownloadResultMessageComposition` enumerates `DownloadResultMessage` | Base | c | type names verified in V21 baseline; enumeration assumed — verify by real-DLL compile |
| `DownloadResultMessage.Message` (`string`), `.State` (`DownloadResultState`), `.Messages` (`DownloadResultMessageComposition`) | Base | c (type only) | assumed — verify by real-DLL compile |
| `Download.Configurations.DownloadConfiguration` (callback base) `.Message` (compiled cast in the worker) | Base | b, c | signature from V21-XML-checked reference |
| `CurrentSelection` (public read/write enum property) on **any** live configuration, set by exact enum member name of the property's own type | Base/Step7 | b | **reflection**. Shown in b for `StopModules`, `ConsistentBlocksDownload`, `TargetForSoftware`, `StartModules` (and compiled for the six defaulted types by the reference probe); for every other selection type assumed from the selection pattern of b's catalogue — a type without it aborts `decision_not_applicable` |
| `StopModulesSelections.StopAll`, `StartModulesSelections.StartModule`/`NoAction`, `ConsistentBlocksDownloadSelections.ConsistentDownload`, `AlarmTextLibrariesDownloadSelections.ConsistentDownload`, `OverwriteSystemDataSelections.Overwrite`, `TargetForSoftwareSelections.PlcSimulationAdvanced` and `.CPU` (default answers, by name) | Base/Step7 | b, c | member names from V21-XML-checked reference (b shows `TargetForSoftwareSelections.CPU` in its callback example and both members in its catalogue); checked at runtime with `Enum.IsDefined` (`selection_unavailable` otherwise) |
| `DownloadCheckConfiguration.Checked` (public read/write `bool`), set to true | Base | b | **reflection**; member name from V21-XML-checked reference ("Base check configuration exposing `Checked`"); the `bool` type is assumed — a non-`bool` or missing member aborts `decision_not_applicable` |
| `DownloadPasswordConfiguration.SetPassword(SecureString)` (public instance method, one `System.Security.SecureString` parameter; return type not constrained) | Base | b | **reflection**; signature from V21-XML-checked reference ("Base password configuration with `IsSecureCommunication` and `SetPassword(SecureString)`"); a missing method aborts `decision_not_applicable` |
| The six defaulted types and the generic answers need no cast: the canonical type is read by name from the type chain, and every answer is applied by reflection on the runtime object | — | — | design (the earlier compiled casts to the six types are gone; the probe still compiles their `CurrentSelection`) |
| The live configuration's runtime type chain contains its public V21 type in `Siemens.Engineering.Download.Configurations` (exact type match) | — | none | assumed — verify live; a mismatch aborts (fail-closed), it never answers |
| `EngineeringException`, `EngineeringObjectDisposedException`, `NonRecoverableException` | Base | existing stubs | unchanged existing declarations |
| `TiaPortal.Confirmation` event, `ConfirmationEventArgs.Result` (set to `ConfirmationResult.No` or `.Yes`) | Base | existing stubs | unchanged existing declarations, used by the session-wide handler; the download scope only changes the answer |
| `ConfirmationEventArgs` public `Text` (`string`) property, read by reflection for `portalConfirmations.text` | Base | none | **not declared in the stubs; unverified**. Never compiled in: absent or unreadable gives `text: null` |
| `ConnectionConfiguration.OnlineLegitimation` (event of `OnlineConfigurationDelegate`), subscribed on the instance `DownloadProvider.Configuration` returned, before `Download`, and removed in `finally` | Base | d | signature from source (`config.OnlineLegitimation += legitimation;` and `-=` in `finally`); existing stub declaration (used by `plc_tia_online` before). That the download raises its legitimation through **this** provider's configuration is not stated by d: assumed from the shared `ConnectionConfiguration` type — verify live |
| `Online.OnlineConfigurationDelegate` = `void (OnlineConfiguration)`; `Online.Configurations.OnlineConfiguration` (base callback configuration) | Base | c, d | type names verified in V21 baseline; existing stub declarations |
| `Online.Configurations.TlsVerificationConfiguration` (canonical type, matched by full name in the runtime type chain; never compiled) | Base | c, d | type name verified in V21 baseline; **reflection** |
| `TlsVerificationConfiguration.PlcName`, `.VerificationInfo` | Base | d | **reflection**; names from d ("bind `PlcName`, `VerificationInfo`"), types undocumented → read as text (`string`, else `ToString()`); missing or throwing fails closed (no trust) |
| `TlsVerificationConfiguration.Certificates`, enumerated (`Online.Security.VerificationCertificateComposition` of `VerificationCertificate`) | Base | c, d | **reflection**; name from d (`tls.Certificates` in the callback sample), composition type names verified in V21 baseline ("read-only collection of verification certificates"); that it enumerates as `IEnumerable` is assumed — not enumerable fails closed |
| `VerificationCertificate.Certificate` → `System.Security.Cryptography.X509Certificates.X509Certificate2` | Base | c, d | **reflection**; member and type from d ("wrapper exposing the target `X509Certificate2` through `Certificate`"); another type or null fails closed |
| `X509Certificate2.RawData`, `.Thumbprint`, `.Subject`, `.Issuer`, `.NotBefore`, `.NotAfter` | .NET | — | .NET base class library, not Siemens |
| `TlsVerificationConfiguration.CurrentSelection` of type `Online.Configurations.TlsVerificationConfigurationSelection`; members `Trusted` and `NonTrusted` set by name (`NonVerified` never) | Base | c, d | **reflection**; property, type and members from d (callback sample `tls.CurrentSelection = … Trusted : … NonTrusted`; catalogue "`NonVerified`, `Trusted`, or `NonTrusted`"), type name verified in V21 baseline; the property type's full name is compared and the members checked with `Enum.IsDefined`; a missing or foreign property is never written |
| `Online.Configurations.OnlinePasswordConfiguration.SetPassword(SecureString)`, `OnlineReadAccessPassword` (its specialization) | Base | c, d | **reflection** (the generic password access); type names verified in V21 baseline |
| `OnlineAuthenticationConfiguration` (`GetSupportedAuthenticationTypes()`, `OnlineCredentials`, `IsSecureCommunication`), `OnlineCredentials`, `UserType`, `AuthenticationType` | Base | c, d | **not used**: such a configuration is recorded and aborts |
| `EnableLegacyCommunication` | Base | d | **never read or set** (d: `true` disables TLS) |

No stub was added or changed for this feature's decision support: `Checked`, `SetPassword` and the
generic `CurrentSelection` are bound by reflection, not compiled, so they are not in the stubs and
the real-DLL compile does not check them; the first live checks do. The TLS trust adds no stub either:
it compiles only the existing `OnlineLegitimation` event and `OnlineConfigurationDelegate`; every
`TlsVerificationConfiguration` member is read or set by reflection.

Stub conventions that are not Siemens facts: the stub enums declare **no members and no values**
(the worker compiles no enum constant; it uses `Enum.IsDefined`/`Enum.Parse`/`ToString()` by name at
runtime), their `int` underlying type follows the stub file convention, and the
`IEngineeringObject`/`IEngineeringComposition` interface lists follow the existing stub style. None
of these affect the worker's compiled calls. Parameter names in the stub `Download` signature are
placeholders.

Deliberately not used: `GetAccessibleDevices()`, `ApplyConfiguration`, the composition `Find`
methods (exact enumeration is used instead, so ordinal uniqueness does not depend on `Find`
semantics), the `Download` overload with a `ConfigurationAddress`, `DeviceItemClassifications.CPU`
(the stub enum has no member values, and compiling an invented constant would be wrong at runtime),
`DownloadPasswordConfiguration.IsSecureCommunication`, `DownloadSelectionConfiguration`,
`OnlineProvider`, `RHDownloadProvider`, `Download(DirectoryInfo, ...)`, `EnableLegacyCommunication`,
`OnlineAuthenticationConfiguration`/`OnlineCredentials` (online authentication is not answered) and
`TlsVerificationConfigurationSelection.NonVerified`.

## Known limits and open risks

- `rhTarget` (issue #16) is not live-tested: the path is planned from `list_download_targets` (the `DownloadProvider` inventory) and
  must resolve by the same names on `RHDownloadProvider.Configuration`; whether an R/H CPU offers both providers is unverified.
  Downloading to a folder is `plc_write download_to_folder` ([PLC operations](PLC_OPERATIONS_SUMMARY.md)).

- Not live-tested, neither against PLCSIM nor against hardware; every *assumed* and **reflection**
  member above can fail live (reflection fails closed with an abort reason).
- **Hardware downloads are allowed.** The only protection is the access mode the human chose and,
  in read-write, the prompt. In `full` an agent can download to a real controller, choose
  consequential decisions (reset, reinitialize, firmware upgrade) and accept TIA Portal dialogs
  without any human interaction. Keep `plc_download` out of client auto-approve lists if the client
  prompt is wanted as well.
- The Siemens SKILL notes that target interfaces bind to the provider instance; the worker resolves
  and downloads on one instance, but planning reads a different instance (only names cross that
  boundary). A Portal UI change between planning and the download is caught only by the worker's
  exact re-resolution.
- V21 `Download()` may return before the device finished; there is no completion check, and the
  worker timeout (`--worker-timeout-minutes`, default 5) bounds a long download. A timeout or crash
  during a download leaves the target state unknown.
- TIA Portal confirmations during a download are answered by the policy; whether V21 raises them
  during `Download`, what declining (or accepting) them does to the download, and the dialog text
  member are unverified. A confirmation raised after the scope closed (for example after an early
  return of `Download`) keeps the call's answer until the next request starts; it is reported as a
  warning on that next response (which may belong to an internal or another tool's call, so the
  server log is the reliable record), not in the download's evidence.
- The `PLCSIM` check is a name check only; see
  [what it does and does not prove](#what-the-plcsim-name-check-does-and-does-not-prove).
- `TargetForSoftware` defaults to `PlcSimulationAdvanced` on `PLCSIM`; behavior with plain PLCSIM
  (non-Advanced) is unverified (a caller decision `TargetForSoftware: "CPU"` overrides it).
- Caller decisions apply to every configuration of that type in either phase; a type presented
  several times gets the same answer each time.
- A password is held as a managed `string` (the environment value) before it is copied into the
  `SecureString`; that string cannot be zeroed and lives until garbage collection in the worker. A
  password mistakenly passed as a *valid-looking* variable name is redacted like an invalid one unless a
  variable of that name happens to be set in the server's environment (then it is recorded as that
  variable's name).
- Reflection on live Openness objects (possibly remoting proxies) is unverified; the existing
  confirmation `Text` read already relies on reflection in the same process.
- **TLS trust is not live-tested.** Whether V21 presents the trust question through
  `DownloadProvider.Configuration.OnlineLegitimation` during `Download(...)` (rather than another route,
  for example a UI dialog, or not at all through Openness) is unverified, and so are the runtime shapes
  of `PlcName`, `VerificationInfo` and `Certificates`. If it is not raised there, the download fails as
  before, now with whatever `configurationsSeen` shows.
- The fingerprints are the only binding to the device: `PlcName` and `VerificationInfo` are reported
  for the human, not compared with the project's PLC. The tool cannot verify that a human checked the
  fingerprints: in `full` there is no prompt, so an agent can copy them from the abort evidence and
  retry; in read-write the prompt lists them. Keep `plc_download` out of client auto-approve lists.
- Whether TIA Portal keeps the trust after the call (for example in a certificate store of the project
  or the engineering station) is not documented.
- A callback exception that Siemens swallows (download continues) is still reported as an abort, but
  the target state then depends on Siemens; the evidence records it. A disposed-object failure
  after the tool's own abort keeps the shared classification (`portal_lost` when the Portal was
  lost) and its message still starts with the abort explanation.

## Live acceptance checks

Run these first, in order, on a disposable project with PLCSIM or PLCSIM Advanced, and only then on
a disposable hardware CPU:

1. **Confirmation dialogs during a normal download.** Download a minimal PLC with default
   arguments in read-write. If the result is `success: false` with `state: Success`, `errorCount: 0`
   and `portalConfirmationsObserved > 0`, V21 routinely raises confirmations during `Download`:
   record each `portalConfirmations` entry (type, text) and the simulator state, then decide whether
   `portalConfirmations: "accept"` is appropriate for that dialog.
2. Record the runtime type of every callback configuration (`configurationsSeen[].type`) against
   its `canonicalType`, `kind` and `availableSelections` to confirm the exact type match and the
   reflection members; an unexpected `unsupported_configuration` or `decision_not_applicable` here
   means the live objects differ from the assumptions.
3. Confirm `list_download_targets` shows exactly one `PLCSIM` PC interface; with several, an
   omitted `pcInterface` must fail `target_ambiguous`.
4. Provoke a configuration without a default (for example a changed data block layout →
   `DataBlockReinitialization`), confirm the abort lists the live enum members, retry with a
   decision and confirm `source: caller`.
5. On a CPU with a write-protection password: confirm the abort asks for
   `passwords: { "ModuleWriteAccessPassword": ... }`, then retry with an environment variable set in
   the client configuration; confirm the password appears nowhere in the response, the worker
   stderr or the audit file.
6. Hardware: dry run first (both acknowledge guards, `TargetForSoftware` default `CPU` in the
   prompt), then an actual read-write download with the prompt accepted.
7. **TLS trust** (PLCSIM Advanced 8.0 instance with a self-signed certificate, the live finding;
   K27: repeat the 2026-11-02 case PLC_F1 of `MCP_Safety_01` on PLCSIM #1 / `1 X1` first and confirm
   that TIA Portal **keeps running** (`bind_project list_portals` still lists it) and the call fails
   cleanly with `abort.tls`, the TLS entry `selected` `NonTrusted` source `policy`; do the same for
   `plc_tia_online go_online`):
   download without `trustedCertificates` and confirm `configurationsSeen` now shows an
   `online_legitimation` `TlsVerificationConfiguration` entry and the abort carries `tls` with readable
   `plcName`, `verificationInfo` and certificates (empty `unreadableMembers`) and `selectionSet:
   NonTrusted`; compare `sha256`/`sha1Thumbprint` with TIA Portal's "Zertifikate anzeigen"; retry with
   every fingerprint and confirm `Trusted` (source `caller`) and the download. If no such entry appears,
   record the failure and the TIA Portal UI behaviour: the question is not raised through this event.
   Also record whether the trust persists (a second download without `trustedCertificates`).
