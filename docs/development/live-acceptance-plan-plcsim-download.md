# Live acceptance plan: `plc_download` and S7-PLCSIM Advanced tools

Status: **pending** — both features are offline-qualified only (unit, contract and FakeWorker
tests). Nothing has run against TIA Portal V21, S7-PLCSIM Advanced or a real CPU.

This plan sequences the live work into one session so that every later step builds on evidence
from an earlier one. The per-feature step lists stay authoritative for their details:
[PLC download live checks](../SupportedOperations/PLC_DOWNLOAD_SUMMARY.md#live-acceptance-checks) and
[PLCSIM live steps](../SupportedOperations/PLCSIM_OPERATIONS_SUMMARY.md#live-acceptance-steps-to-run-once-plcsim-advanced-is-installed).

## Test pyramid and current coverage

| Layer | What it proves | Status |
| --- | --- | --- |
| Unit (Linux + Windows) | callback decisions (built-in defaults, caller decisions, environment-variable passwords, reflection answers), request validation, type matching, value parsing/comparison, return-code judgement, reflection member resolution against stand-in types, test-runner timing with a fake clock | done |
| Contract / MCP protocol (FakeWorker, Windows) | tool registration per mode, strict arguments, canonical JSON, payload decoding, guards, confirmation, audit records | done; Linux runs need the FakeWorker symlink described in the review notes |
| Signature (Windows, installed V21) | every Openness member used by the worker exists with the declared signature | **pending** — `scripts/fork-verify.ps1` steps 06/07 |
| Live E2E (Windows, TIA V21 + PLCSIM Advanced) | real behaviour of download callbacks, confirmation dialogs, PLCSIM API signatures, return codes, timing | **pending** — this plan |

Coverage gate: the repository's CI threshold (line rate 0.80) must still pass on Windows.

## Preconditions

1. `scripts/fork-verify.ps1` completed with steps 03–09 at exit 0. Archive `.verify/<timestamp>/`. Prerequisites: .NET 10 SDK, PowerShell 7 (step 04 runs `verify-reference-stubs.ps1` with `pwsh`; Windows PowerShell 5.1 lacks `Path.TrimEndingDirectorySeparator`) and the .NET Framework 4.8 targeting pack; `scripts/install-prerequisites.cmd` installs the first two with winget and opens the Developer Pack download when the targeting pack is missing.
2. S7-PLCSIM Advanced installed and licensed; Runtime Manager running; online access set to
   **PLCSIM** (softbus) in the Control Panel.
3. A **disposable** TIA Portal V21 project containing one S7-1500 CPU (PLCSIM Advanced simulates
   S7-1500 / ET 200SP only). Never use a production project or a project with real hardware online.
4. The reference program below, written and reviewed by a person **before** the session. It is the
   independent oracle: the agent's own test scenarios are not acceptance evidence.

### Reference program (oracle)

| Tag / block | Type | Expected behaviour |
| --- | --- | --- |
| `Start` | Bool input | set by the test |
| `Stop` | Bool input | set by the test |
| `Running` | Bool output | latched by `Start`, reset by `Stop` (stop dominant) |
| `Delayed` | Bool output | TON 500 ms after `Running` becomes true |
| `Count` | Int counter | +1 on each rising edge of `Start` |
| `SetpointReal` / `SetpointLReal` | Real / LReal | copied to `EchoReal` / `EchoLReal` every cycle |
| `Step` | Int in a DB | simple 3-step chain: 0 → 10 on `Start`, 10 → 20 after `Delayed`, 20 → 0 on `Stop` |
| `Text` | String | constant, read-only check |

Write the expected results as a table (input sequence → expected outputs and `Step` values with
tolerances) and keep it under version control next to the project.

## Session order

| # | Step | Pass criterion | Evidence |
| --- | --- | --- | --- |
| 1 | `tia-mcp doctor --verbose` | PLCSIM check finds API folder, DLL and worker | doctor JSON |
| 2 | `plcsim_read probe_api` | every required member resolved; `okMemberFound` and the numeric value of `OK` (`enumMembers` of `ERuntimeErrorCode`) recorded | full probe document |
| 3 | `bind_project`, `plc_read list_download_targets` | exactly one `PLCSIM` PC interface listed for the CPU | read document |
| 4 | `plc_download` with `dryRun:true` (read-write) | preview, both guards reported, no prompt, simulator unchanged | response + audit line |
| 5 | `plc_download` actual (read-write, accept the prompt) | see download live check 1: if declined confirmations make every download fail, **stop** and decide whether `portalConfirmations:"accept"` is appropriate; note any TIA Portal dialog that appears after the call returned (it gets the session-wide answer and is not in the evidence) | response, `configurationsSeen`, `portalConfirmations`, Control Panel screenshot |
| 5a | `plc_download` again after a TLS refusal (the 2026-11-01 finding: "Verbindung mit Baugruppe PLC_1 fehlgeschlagen", TIA Portal's UI asks to trust the instance's self-signed certificate) | without `trustedCertificates` the abort is `certificate_not_trusted` in phase `online_legitimation` with readable `tls`; after the user compared the fingerprints with "Zertifikate anzeigen", the retry with every fingerprint downloads (`Trusted`, source `caller`). See download live check 7; if no `online_legitimation` entry appears, **stop** and record it | response, `configurationsSeen`, `abort.tls`, screenshot of the certificate dialog |
| 6 | `plc_download` with a non-`PLCSIM` interface name, `dryRun:true` only | preview with acknowledge guard `download_to_hardware`, nothing downloaded (no real CPU in this plan) | response + audit line |
| 7 | PLCSIM live steps 4–9 | as listed in the PLCSIM summary | per-step documents |
| 8 | `plcsim_test` running the oracle table | every expected value matches within the written tolerance; other PLCSIM calls wait while it runs (calls are serialized) | scenario document vs oracle table |
| 9 | **Closed loop**: change the program through `plc_write` (for example the TON preset), `compile_check`, `plc_download`, re-run the oracle scenario with the updated expectation | old expectation fails, new one passes | all four documents |
| 10 | PLCSIM live steps 10–14 (failure paths, read-only, audit, Runtime Manager restart, TCP/IP warning) | as listed | per-step documents |

A step fails the session if its pass criterion is not met; record the finding in
`docs/IMPROVEMENT_LOG.md` and correct code or documentation before continuing past it.

## Known gaps after this plan

- Hardware download is allowed (controlled by the access mode) but out of scope here: it needs a disposable real CPU and the hardware steps of the [PLC download live checks](../SupportedOperations/PLC_DOWNLOAD_SUMMARY.md#live-acceptance-checks).
- `plc_download` answers TIA Portal confirmations by its `portalConfirmations` policy only until
  `Download` returns; extending the window to the next request is an open follow-up in
  `docs/IMPROVEMENT_LOG.md`.
- S7-1200 targets: PLCSIM Advanced does not simulate them.
- Virtual-time synchronisation (`RunToNextSyncPoint`, operating modes) is not used; `plcsim_test`
  timing is wall-clock, so tolerances must be generous.
- Only the PLCSIM API version installed on the test machine is qualified; re-run step 2 after any
  PLCSIM Advanced upgrade.
