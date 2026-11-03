# PLC online operations over the S7-1500 Web API (`plc_online_read`, `plc_online_write`)

**Language:** English · [Deutsch](PLC_ONLINE_WEBAPI_SUMMARY.de.md)

> **Not live-tested.** This domain was built and qualified offline only: no S7-1500 CPU or PLCSIM
> Advanced instance with an enabled web server was reachable. The official Siemens client library
> runs unchanged in the tests, but against a scripted HTTP handler, not a PLC. Every client member
> used is listed with its source location in the [provenance table](#provenance). Run the
> [live acceptance steps](#live-acceptance-steps) on a test CPU before relying on these tools,
> and never point a first run at a machine in production.

Two tools observe and change real S7-1500 CPUs online through the SIMATIC S7-1500 Web API
(JSON-RPC over HTTPS). They use Siemens' official .NET client, the NuGet package
[`Siemens.Simatic.S7.Webserver.API`](https://www.nuget.org/packages/Siemens.Simatic.S7.Webserver.API)
**3.3.92** (MIT, source `siemens/simatic-s7-webserver-api`), which the host references directly. They
run inside the host process: no worker, no TIA Portal, no project binding. The one exception is
`plc_online_read` `read_watch_table_live`, which reads a table of the bound project through the
Openness worker first (see [below](#live-values-of-an-offline-table-read_watch_table_live)).

| Tool | Modes | Purpose |
|---|---|---|
| `plc_online_read` | read-only, read-write, full | Up to 50 independent observations of configured PLCs (operating state, tags, diagnostics, alarms, modules, syslog, ...). |
| `plc_online_write` | read-write, full | Up to 50 ordered online changes (operating mode, tag values, alarm acknowledgement, LED flashing, system time, backup and restore) on the guarded single-call pipeline; one confirmation per actual read-write call. |

Server tool counts, these two included, are 10 (read-only), 27 (read-write) and 27 (full).

## Prerequisites

- An S7-1500 CPU whose **web server is enabled in its hardware configuration** in TIA Portal and
  reachable over HTTPS from the machine running the MCP server, with the hardware configuration
  downloaded. The client library's README maps client 3.3.x to S7-1500 firmware **4.1.x**
  (client 3.0.x to 4.0.x, 2.2.x to 3.1.x, 2.1.x to 3.0.x); a CPU with older firmware may answer
  newer methods with Web API error -32601 (`MethodNotFound`), reported as `webapi_rejected`.
  The source states no per-method minimum firmware (see the provenance table).
- **Users and rights** come from the CPU's web server user management in TIA Portal. Each Web API
  method needs the matching right for the logged-in user (or for the anonymous user when the target
  has no user configured). A missing right is `webapi_permission_denied`. Grant only what the AI
  should be able to do; `plc_online_read permissions` lists the rights of the configured user.
- S7-PLCSIM Advanced: the client package lists PLCSIM and PLCSIM Advanced among its tags, but the
  source says nothing more. An instance is reachable only if its CPU has the web server enabled
  and it is attached to a TCP/IP network adapter (not the softbus). Treat it as unverified.
- For the details of the Web API itself (rights per method, session limits, certificate handling on
  the CPU), the client README points to the *SIMATIC S7-1500 Web server* function manual (Siemens
  support entry 59193560).

## Configuration

The human configures the PLCs; the AI only names them. Start the server with
`--plc-webapi-config <absolute path>` (or set `TIA_MCP_PLC_WEBAPI_CONFIG`; the option wins). The
path is resolved once at startup. **An invalid file refuses startup** with every problem listed;
`tia-mcp doctor --plc-webapi-config <path>` reports the same problems without starting the server.
Without a file the tools are still registered and every item answers `target_not_found` with this
instruction.

```json
{
  "targets": [
    {
      "name": "Line1",
      "address": "192.168.0.10",
      "userEnv": "TIA_LINE1_WEBAPI_USER",
      "passwordEnv": "TIA_LINE1_WEBAPI_PASSWORD",
      "certificate": { "validation": "thumbprint", "thumbprint": "3F:A2:...:9C" },
      "requestTimeoutSeconds": 30
    },
    {
      "name": "Lab",
      "address": "plc-lab.plant.local:8443",
      "certificate": { "validation": "system" }
    }
  ]
}
```

| Member | Required | Meaning |
|---|---|---|
| `name` | yes | What tools pass as `target`: exact, case-sensitive, unique, 1-64 characters without control characters or surrounding spaces. |
| `address` | yes | Host name, IPv4 or bracketed IPv6 address, optionally `:port`. No scheme, path, query or user part: the Web API is always `https://<address>/api/jsonrpc`. |
| `userEnv`, `passwordEnv` | together or not at all | **Names** of environment variables of the MCP server process that hold the web server user and password. Omit both for the anonymous user. |
| `certificate.validation` | yes | `system`, `thumbprint` or `none` (below). |
| `certificate.thumbprint` | with `thumbprint` only | SHA-1 (40 hex digits) or SHA-256 (64 hex digits) fingerprint of the PLC's server certificate; spaces and colons are ignored. |
| `requestTimeoutSeconds` | no | Per HTTP request, 1-600, default 30. A backup download is one request; raise it for large backups. |

The file is parsed strictly: camelCase member names only, no comments, no unknown members, at most
64 targets and 1 MiB.

**Credentials** never appear in the file, in tool arguments, in results, in logs or in audit
records. Put the variables in the `env` block of the MCP client's server entry (see
[client configuration](../guides/mcp-client-configuration.md#plc-web-api-targets)). They are read
when the server logs in, not at startup; a missing variable fails the item with
`webapi_authentication_failed` naming only the variable.

**Certificate validation** is chosen per target and applied to that target's own HTTP handler; the
client library's process-wide `ServerCertificateCallback` is never set.

| `validation` | Behaviour |
|---|---|
| `system` | Normal platform validation (chain, host name, validity). Install the PLC certificate or its CA in the trusted store of the account the server runs under. |
| `thumbprint` | Accepts exactly the certificate with the pinned fingerprint, regardless of chain or name, and nothing else. |
| `none` | No validation for this target. Every result for the target carries a warning, and doctor warns: anyone on the network path could impersonate the PLC and read the credentials. |

Connections are direct (system proxy settings are ignored, so credentials never pass a proxy) and
redirects are not followed.

## Sessions

- **Anonymous target** (no `userEnv`): no `Api.Login` is sent; requests carry no token and the CPU
  applies the rights of its anonymous web server user (the client source's
  `ApiAuthenticationMode.Disabled` describes the case where "only Anonymous user is available").
- **Authenticated target**: the first call logs in (`Api.Login`) and keeps the token for later
  calls. When the PLC refuses a **single request** with Web API error 2 (`PermissionDenied`) or
  HTTP 401/403, the token may have expired, so the server logs out (best effort), logs in again with
  freshly read credentials and repeats that request **once**; the PLC does not execute a request it
  refuses for missing permission. A second refusal is `webapi_permission_denied`. Calls made of
  several requests are never repeated as a whole: `create_backup` may repeat its first request
  (`Plc.CreateBackup`) but never the ticket download, and `restore_backup` repeats nothing.
- One HTTP client and session per target, created once even when several calls start together.
  Calls to one target run **one at a time**, login and re-login included (the client library keeps
  the token on the shared HTTP client), so a long call such as a restore delays other calls to that
  target; waiting is cancellable. Different targets run independently. All sessions are logged out
  (bounded to 5 seconds) when the host shuts down.
- Items of one call run in order. `plc_online_write` calls run one at a time (a host-wide lease);
  reads wait only for the target they share with a running call.

## `plc_online_read`

Every item takes `operationId`, `operation` and `target`. Members an operation does not use must be
omitted. Items are independent: a failed item does not stop the others.

| Operation | Members | Client call (Web API method) | Result |
|---|---|---|---|
| `ping` | - | `ApiPingAsync` (`Api.Ping`) | `{ id }` as the PLC returns it |
| `version` | - | `ApiVersionAsync` (`Api.Version`) | `{ apiVersion }` |
| `permissions` | - | `ApiGetPermissionsAsync` (`Api.GetPermissions`) | `{ permissions: [name] }` of the session's user |
| `read_operating_mode` | - | `PlcReadOperatingModeAsync` (`Plc.ReadOperatingMode`) | `{ operatingMode }`, e.g. `run`, `stop`, `startup`, `hold`, `stop_fwupdate` |
| `read_mode_selector` | - | `PlcReadModeSelectorStateAsync` (`Plc.ReadModeSelectorState`) | `{ modeSelector }`: `run`, `stop`, `no_switch`, `unknown` |
| `read_cpu_type` | - | `ApiGetPlcCpuTypeAsync` (`Plc.ReadCpuType`) | `{ productName, orderNumber }` |
| `read_runtime_information` | - | `PlcReadRuntimeInformationAsync` (`Plc.ReadRuntimeInformation`) | load percentages and cycle times in milliseconds |
| `read_system_time` | - | `PlcReadSystemTimeAsync` (`Plc.ReadSystemTime`) | `{ timestamp }` (UTC) |
| `read_project_information` | - | `ProjectReadInformationAsync` (`Project.ReadInformation`) | `{ projectName, versions }` |
| `browse_tags` | `var`, `browseMode` (`children` default, `var`), `type` (`code_blocks`) | `PlcProgramBrowseAsync` / `PlcProgramBrowseCodeBlocksAsync` (`PlcProgram.Browse`) | `{ count, variables }` (name, datatype, hasChildren, dbNumber, address, area, readOnly, maxLength, arrayDimensions) or `{ count, blocks }` |
| `read_tags` | `names` (1-100), `representation` (`simple` default, `raw`) | `PlcProgram.Read`, one request per name | `{ requested, read, tags: [{ name, success, value, failureCategory, failureMessage, webApiError }] }` |
| `read_diagnostic_buffer` | `count` (1-1000, default 50), `language` (default `en-US`) | `ApiDiagnosticBufferBrowseAsync` (`DiagnosticBuffer.Browse`) | header counts and entries (timestamp, status, texts, event ids) |
| `browse_alarms` | `count` (1-1000) **or** `alarmId`, `language` (default `en-US`) | `ApiAlarmsBrowseAsync` (`Alarms.Browse`) | header counts and entries (id, alarm number, status, timestamp, producer, hwid, acknowledgement, texts) |
| `browse_modules` | `hwid`, `browseMode` (`children`, `node`) | `ModulesBrowseAsync` (`Modules.Browse`) | `{ nodes }` tree (hwid, name, class, type, subType, attributes, parent, children; at most 32 levels) |
| `read_module_status` | `hwid` (required), `language` | `ModulesReadStatusAsync` (`Modules.ReadStatus`) | `{ own, subordinate, messages, language }` |
| `read_module_leds` | `hwid` (required) | `ModulesReadLedsAsync` (`Modules.ReadLeds`) | `{ hwid, leds: [{ type, status, colors, periodMs }] }` |
| `browse_syslog` | `count` (1-1000, default 50), `first` | `ApiSyslogBrowseAsync` (`Syslog.Browse`) | `{ countTotal, countLost, entries: [raw] }` |
| `read_watch_table_live` | `table` (a watch table) **or** `tagTable` (a PLC tag table) of the bound project; optional `plcName`, `folderPath` (watch-table group or tag-table folder), `count` (entries, 1-1000, default 100), `representation` | Openness `read_watch_table` / `list_tag_tables` (worker), then `PlcProgram.Read` per distinct mapped name, the `read_tags` path | `{ target, plcName, deviceName, sourceKind, tableName, folderPath, entryCount, entriesComplete, mapped, unmapped, read, entries: [{ index, name, address, webApiName, mapping, unmappedReason, success, value, failureCategory, failureMessage, webApiError }], mappingRules, notes }` |

Notes:

- `read_tags` values are the `result` member exactly as the PLC sent it: the request is built by the
  client's `ApiRequestFactory.GetApiPlcProgramReadRequest` and sent by its `SendPostRequestAsync`,
  the path `PlcProgramReadAsync<T>` takes, but the value is not converted by Newtonsoft (which would
  reinterpret date-like strings). A refused name (for example error 200, address does not exist) is
  recorded and the next name is read; losing the connection or the session stops the item. The item
  succeeds only when every name was read.
- Names are passed as the Web API expects them (`"DB_Line".Speed`, `"Motor_On"`); browse first to
  learn the exact spelling.
- `read_watch_table_live` is described in its own section below.
- Enumerations are the client library's enum member names in snake_case (the naming the library
  itself uses for its converters); timestamps are ISO 8601 UTC.
- Results follow the shared read envelope: `{ tool, contractVersion: "1.0", success, error,
  warnings, batch }`. A failed item carries `failure { category, message }` and, when the PLC sent
  one, `result.webApiError { code, name, plcMessage, httpStatus }`. Oversized values are omitted
  under the usual budget rules (`--max-item-chars`, `--max-document-chars`).

## Live values of an offline table (`read_watch_table_live`)

> **Not live-tested.** Neither the Openness watch-table read nor the Web API read was run against a
> real project and CPU together; the FakeWorker and the in-memory Web API stand-in model payload shapes
> only.

One `plc_online_read` item combines an offline table of the bound project with online values:

1. `target` is looked up in the configuration first (`target_not_found`, nothing else runs).
2. The project binding is checked like every content read: no verified binding is `binding_conflict`
   and nothing is read. The table is read offline through the existing worker methods
   `read_watch_table` (entries `{ name, address, ... }`, at most `count`) or `list_tag_tables`
   (`tagTable`: its tags, then its user constants, at most `count` entries). The PLC's tag inventory is
   read too (`list_tag_tables`), as evidence for the mapping below. An omitted `plcName` that matches
   several PLCs is `target_ambiguous`.
3. Each entry is mapped to a Web API name only by an evidenced rule (the result lists them as
   `mappingRules`):
   - `tagTable`: a tag of a tag table is read as `"Name"`, the client's
     `ApiPlcProgramData.GetNameWithQuotes` form of a root-level variable (Models/ApiPlcProgramData.cs
     230) and this reference's `"Motor_On"` example.
   - `plcTag`: a watch-table entry whose name equals (case-insensitively) a tag of the same PLC, bare
     or in one pair of double quotes, is read as `"Name"` in the inventory's spelling.
   - `webApiNotation`: a watch-table name that already is a documented Web API name is sent
     unchanged: double-quoted segments joined by dots, each optionally followed by an integer index
     list (`"DataTypes"."Bool"`, `"DB"."Arr"[1,2]`: README.md 193-216 and `GetVarNameForMethods` /
     `BuildChildrenFromArrayDimensions`, Models/ApiPlcProgramData.cs 83-140 and 241), or one quoted
     segment followed by exactly one plain identifier (`"DB_Line".Speed`, this reference's example).
   - Everything else is **unmapped** and never sent: absolute addresses (`%MW10`; no documented Web
     API notation), names with `#` or an embedded quote (no documented escaping), bare multi-segment
     names (`DB_Line.Speed`), more than one unquoted member (`"DB".Struct.Member`; only one level is
     documented, write the members quoted), an index after an unquoted member, user constants and
     entries without a name. Each carries `unmappedReason`.
4. Distinct mapped names are read in entry order through the same `PlcProgram.Read` loop as
   `read_tags` (a refused name, for example error 200, is recorded and the next is read; a lost
   connection or session stops). Entries not reached carry `success:false` and a message.

The item succeeds when every mapped name was read; unmapped entries do not fail it but add a warning
with their count. A failed read makes the item `failed` with the result still present, like
`read_tags`. The format of `PlcWatchTableEntry.Name` is not documented by Siemens, which is why only
these rules apply. `target` and `plcName` are paired by the caller: the server does not check that the
CPU behind `target` runs this project's program.

## `plc_online_write`

Every item takes `operationId`, `operation` and `target`; `dryRun` is a root argument.

| Operation | Members | Client call (Web API method) | Guards |
|---|---|---|---|
| `change_operating_mode` | `operatingMode`: `run` or `stop` (the only modes the client and the Web API accept) | `PlcRequestChangeOperatingModeAsync` (`Plc.RequestChangeOperatingMode`) | `operating_mode_stop` for `stop` |
| `write_tags` | `tags` (1-100 `{ name, value }`), `representation` (`simple` default, `raw`) | `PlcProgramWriteAsync` (`PlcProgram.Write`), one request per tag | - |
| `acknowledge_alarm` | `alarmId` (from `browse_alarms`) | `AlarmsAcknowledgeAsync` (`Alarms.Acknowledge`) | - |
| `flash_leds` | `hwid` | `ModulesFlashLedsAsync` (`Modules.FlashLeds`) | - |
| `set_system_time` | `timestamp`: RFC 3339 with `Z` or an offset, 1970-01-01 to 2554-07-21 (the client's own range check) | `PlcSetSystemTimeAsync` (`Plc.SetSystemTime`) | - |
| `create_backup` | `path`: absolute local file in an existing folder; `overwrite` (default `false`: an existing file is a `validation_error`) | `PlcCreateBackupAsync` (`Plc.CreateBackup`), then `DownloadTicketAsync` and `ApiCloseTicketAsync` | - |
| `restore_backup` | `path`: absolute local backup file (at most 1 GiB) | `ApiBackupHandler.RestoreBackupAsync` (`Api.Browse`, `Plc.RestoreBackup`, ticket upload, `Api.Ping`, `Api.Logout`/`Api.Login`) | `restore_backup_replaces_program` |

Backup and restore paths must be absolute paths that name a file: on Windows a drive path
(`C:\...`) or a UNC share (`\\server\share\...`, since 2026-11-01); device paths (`\\?\`, `\\.\`, `\??\`) and paths with
control or Unicode format characters are refused before anything touches the file system.

`online_changes_real_controller` fires on **every** call.

- `write_tags` sends the caller's JSON value unchanged (an integer stays an integer; a date-like
  string stays a string; at most 4,096 characters per value). It stops at the first failed tag;
  earlier tags stay written and the failed item's `result` lists every attempted tag.
- `create_backup` writes the backup through a temporary file in the target folder and moves it in
  place; the result has the path, size and SHA-256. A file that appears between planning and the
  write with `overwrite:false` fails `state_changed` before the PLC is asked.
- `restore_backup` requires a target with `userEnv`/`passwordEnv`: the client's restore helper uses
  that user's password as the restore legitimation and logs in again after every PLC restart. The
  file's size and SHA-256 are part of the planned effect and are checked again just before the
  upload (the size first: a file over 1 GiB is refused without reading it). The helper may first put the PLC into restore mode (a restart), then upload, wait for
  each restart (three minutes, the client's default) and log in again; the whole item is bounded to
  ten minutes. Afterwards check `read_operating_mode` and the diagnostic buffer.

The response is `{ tool, contractVersion: "1.0", phase, success, error, warnings, guards, effects,
batch }`. `effects` lists per item the target, address, certificate validation, authentication kind,
Web API method, a one-line description and the operation's inputs (tags with values, timestamp in
UTC, backup file with size and hash). There is no verification read: an accepted
`Plc.RequestChangeOperatingMode` means the PLC took the request; read the mode to see when the CPU
reached it.

## Access, confirmation and audit

| Mode | `plc_online_read` | `plc_online_write` |
|---|---|---|
| read-only | registered (`Observe`) | not registered; a call is an MCP "unknown tool" error |
| read-write | registered | registered (`OnlineControl`); **every actual call asks the user once** through MCP form elicitation |
| full | registered | registered; runs under policy without a prompt |

`plc_online_write` uses the guarded single-call pipeline with `ConfirmsEveryCall = true`, like the
lifecycle tools and `plc_download`:

1. Argument shape (unknown members, nulls, wrong types) is refused before the tool runs, without
   audit. Then items are validated (1-50 unique `operationId`s, members per operation).
2. Planning is **offline**: target lookup in the configuration (`target_not_found` fails the whole
   call), input normalisation and local file checks. A `dryRun` stops here with `phase: preview`,
   shows the effects and guards, and never contacts a PLC.
3. In read-write, one prompt shows the acknowledge guards first, one line each, then one line per
   item in order: the effect, the target, its address and the Web API method, and the caller's
   `operationId` last. Every caller-supplied value in it (tag names and values, alarm ids, paths,
   target names, operation ids) is quoted as a JSON string literal with control, line-separator and
   Unicode format characters (bidirectional and zero-width ones included) escaped, so no value can
   add a line or reorder the text. A tag value is shown in full up to 512 characters of JSON,
   otherwise as its length and the SHA-256 of its JSON text (compare it with the value in a `dryRun`
   preview's effects). An `operationId` with such characters is refused as `validation_error`
   before planning, as in every tool that confirms each call. Only `accept` with `confirm: true` proceeds; decline, cancel,
   timeout, a failed exchange or a client without form elicitation is `access_denied` and nothing is
   sent. After acceptance the plan is computed again and must be identical (for example the restore
   file's hash, or a backup target that has since appeared); otherwise the call is refused and
   nothing is sent. In full, acknowledge guards are satisfied by policy.
4. Items run in order; the first failure stops the call, later items are `skipped`, nothing is
   rolled back, and a multi-item failure carries `partial_write_no_rollback`.
5. One audit record per entered call (previews and refusals included) is appended to the shared
   audit stream (`writes-yyyy-MM-dd.jsonl` under `%LOCALAPPDATA%\TiaMcpServer\audit`) with
   `recordKind: "plc_online"`, `projectPath: null` and `binding: null`. It contains the requested
   items (tag values, alarm ids, file paths), the response and its hash, the guards with their
   satisfaction (`user` or `policy`) and the confirmation outcome; it never contains credentials.

| Guard | Severity | Fires when |
|---|---|---|
| `online_changes_real_controller` | acknowledge | Every call: the change acts on the running machine immediately and nothing is rolled back. |
| `operating_mode_stop` | acknowledge | `change_operating_mode` with `stop`: the user program stops and the outputs take their configured STOP behaviour. |
| `restore_backup_replaces_program` | acknowledge | `restore_backup`: the PLC is stopped and restarted and its program, configuration and data are replaced. |

There are no block guards; invalid input is a `validation_error`.

## Failure categories

| Category | Meaning |
|---|---|
| `target_not_found` | `target` is not in the configuration (or no file is configured). Read items fail one by one; a write call fails as a whole. |
| `validation_error` | Invalid input (also an existing `create_backup` file without `overwrite`, a missing folder, an anonymous `restore_backup` target, a culture name the platform does not know). |
| `webapi_unreachable` | Connection, DNS or TLS failure (including a certificate rejected under `system` or a pin mismatch under `thumbprint`), the per-request timeout, or a restore that did not finish in time. |
| `webapi_authentication_failed` | `Api.Login` failed (error 100 `LoginFailed`, 102 `PasswordExpired`, or 105 during login), or a credential variable is not set. |
| `webapi_permission_denied` | Web API error 2 or HTTP 401/403, still after one re-login: the user lacks the right. |
| `webapi_rejected` | Any other Web API error (message carries code, name and the PLC's text; `result.webApiError` has them structured), another HTTP status, an unreadable response, or a client-library refusal. |
| `state_changed` | A backup file appeared or the restore file changed after planning, or a backup could not be written locally. |
| `access_denied` | Read-write confirmation was not given (see above), or the mode does not permit the operation. |

The mapping uses the PLC's error code as recorded on the exception by a wrapper around the
library's own `ApiResponseChecker`, the HTTP status and exception types, never message text. The
inner `ApiException` and `InvalidHttpRequestException` texts (which can contain the request) are
never surfaced. When the password sent with a call occurs anywhere in the resulting message or
the PLC's error text, the whole message is replaced by a fixed text with the Web API error code (and
the PLC's text is dropped from `webApiError`); blanking only the occurrences would show where a short
password occurs.

## Provenance

Every client member used, from a read-only clone of `siemens/simatic-s7-webserver-api` at
`3f334fa` ("Bump Microsoft.Extensions.Logging.Abstractions from 10.0.13 to 10.0.14 (#287)"; a
shallow clone). The package 3.3.92 was built from commit `faac56e9` (its nuspec), one dependency
bump earlier; the server compiles against 3.3.92, which confirms that every member below exists in
the package. Paths are relative to `src/Webserver.API/`; `H` is
`Services/RequestHandling/ApiHttpClientRequestHandler.cs`, `F` is
`Services/RequestHandling/ApiRequestFactory.cs` (the Web API method name is the string passed to
`new ApiRequest(...)` at the given line). **Minimum firmware:** the source states none per method;
the only statement is the README compatibility table (README.md 267-279: client 3.3.x for S7-1500
firmware 4.1.x) and the hint of `ApiMethodNotFoundException` (Exceptions/ApiMethodNotFoundException.cs 9:
"check method name and PLC firmware version").

| Used for | Client member (H line) | Web API method (F line) | Min. firmware in source |
|---|---|---|---|
| request handler | `ApiHttpClientRequestHandler(HttpClient, IApiRequestFactory, IApiResponseChecker, IApiRequestSplitter, ILogger)` (85) | - | not stated |
| session | `ApiLoginAsync(user, password, includeWebApplicationCookie, ct)` (2924) | `Api.Login` (198) | not stated |
| session | `ApiLogoutAsync` (469) | `Api.Logout` (211) | not stated |
| `ping` | `ApiPingAsync` (487) | `Api.Ping` (247) | not stated |
| `version` | `ApiVersionAsync` (504) | `Api.Version` (478) | not stated |
| `permissions` | `ApiGetPermissionsAsync` (435) | `Api.GetPermissions` (223) | not stated |
| `read_operating_mode` | `PlcReadOperatingModeAsync` (563) | `Plc.ReadOperatingMode` (354) | not stated |
| `read_mode_selector` | `PlcReadModeSelectorStateAsync` (3542) | `Plc.ReadModeSelectorState` (391) | not stated |
| `read_cpu_type` | `ApiGetPlcCpuTypeAsync` (3865) | `Plc.ReadCpuType` (1394) | not stated |
| `read_runtime_information` | `PlcReadRuntimeInformationAsync` (4279) | `Plc.ReadRuntimeInformation` (1615) | not stated |
| `read_system_time` | `PlcReadSystemTimeAsync` (3033) | `Plc.ReadSystemTime` (767) | not stated |
| `read_project_information` | `ProjectReadInformationAsync` (4315) | `Project.ReadInformation` (1641) | not stated |
| `browse_tags` | `PlcProgramBrowseAsync(mode, string var, ct)` (622) | `PlcProgram.Browse` (262) | not stated |
| `browse_tags` `type: code_blocks` | `PlcProgramBrowseCodeBlocksAsync` (701) | `PlcProgram.Browse` with `type: ["code_blocks"]` (276) | not stated |
| `read_tags` | `ApiRequestFactory.GetApiPlcProgramReadRequest` (F 303) + `SendPostRequestAsync(IApiRequest)` (128), the path of `PlcProgramReadAsync<T>` (746) | `PlcProgram.Read` (308) | not stated |
| `write_tags` | `PlcProgramWriteAsync(string var, object value, mode, ct)` (863) | `PlcProgram.Write` (325) | not stated |
| `read_diagnostic_buffer` | `ApiDiagnosticBufferBrowseAsync` (3657) | `DiagnosticBuffer.Browse` (1205) | not stated |
| `browse_alarms` | `ApiAlarmsBrowseAsync` (3625) | `Alarms.Browse` (1176) | not stated |
| `acknowledge_alarm` | `AlarmsAcknowledgeAsync` (3594) | `Alarms.Acknowledge` (1133) | not stated |
| `browse_modules` | `ModulesBrowseAsync(uint?, ModulesBrowseMode?, ct)` (3991) | `Modules.Browse` (1475) | not stated |
| `read_module_status` | `ModulesReadStatusAsync` (4240) | `Modules.ReadStatus` (1583) | not stated |
| `read_module_leds` | `ModulesReadLedsAsync` (4219) | `Modules.ReadLeds` (1556) | not stated |
| `flash_leds` | `ModulesFlashLedsAsync` (4198) | `Modules.FlashLeds` (1538) | not stated |
| `browse_syslog` | `ApiSyslogBrowseAsync` (3568) | `Syslog.Browse` (1118) | not stated |
| `read_watch_table_live` values | the `read_tags` path above; no further client member | `PlcProgram.Read` (308) | not stated |
| `read_watch_table_live` name rules (evidence only, not called) | `ApiPlcProgramData.GetNameWithQuotes` and `GetVarNameForMethods` (Models/ApiPlcProgramData.cs 230, 241), `BuildChildrenFromArrayDimensions` (83); README.md 193-216 | - | - |
| `change_operating_mode` | `PlcRequestChangeOperatingModeAsync` (586); run/stop only per `ApiRequestParameterChecker.CheckPlcRequestChangeOperatingMode` (Services/RequestHandling/ApiRequestParameterChecker.cs 120) | `Plc.RequestChangeOperatingMode` (378) | not stated |
| `set_system_time` | `PlcSetSystemTimeAsync` (3053); range per `CheckSystemTimeStamp` (ApiRequestParameterChecker.cs 260) | `Plc.SetSystemTime` (782) | not stated |
| `create_backup` | `PlcCreateBackupAsync` (3363), `DownloadTicketAsync(string)` (2676), `ApiCloseTicketAsync(string)` (319) | `Plc.CreateBackup` (973), `Api.CloseTicket` (96) | not stated |
| `restore_backup` | `ApiBackupHandler.RestoreBackupAsync` (Services/Backup/ApiBackupHandler.cs 90), which calls `PlcRestoreBackupAsync` (3383) and `ReLoginAsync` (2857; ApiBackupHandler.cs 139, 144) | `Plc.RestoreBackup` (988) | not stated |
| error mapping | `ApiResponseChecker.CheckHttpResponseForErrors` (ApiResponseChecker.cs 22, throws `InvalidHttpRequestException` at 43) and `CheckResponseStringForErros` (53); `ApiErrorModel.ThrowAccordingException` (Models/Responses/ApiErrorModel.cs 26; `PermissionDenied` 41, `LoginFailed` 49, default `ApiException` 189); codes in `Enums/ApiErrorCode.cs` | - | - |
| anonymous user | `ApiAuthenticationMode.Disabled` "Only Anonymous user is available" (Enums/ApiAuthenticationMode.cs 28); README.md 102 | - | - |
| certificates (not used) | `ServerCertificateCallback.CertificateCallback` is static (Services/ServerCertificateCallback.cs 22) and `ApiStandardServiceFactory` applies it to every client it creates (Services/ApiStandardServiceFactory.cs 195-197), so this server builds its own `HttpClient` per target instead of using the factory | - | - |

What the client offers but these tools do not expose: redundancy ids (every call uses
`StandardPLC`; R/H systems are untested), diagnostic-buffer and alarm filters, `Modules.ReadParameters`
and identification & maintenance data, time settings, files, data logs, web applications,
fail-safe and technology objects, `Api.ChangePassword` and the bulk API. Every operation the
assignment asked for exists in the client and is exposed; `browse_tags` offers the only type filter
the client has (`code_blocks`).

## Diagnostics

`tia-mcp doctor` has the optional check `plc-webapi-config`: it parses the file the server would
load (option, environment or none) with the server's loader, lists every target (name, address,
certificate validation, timeout, anonymous or user) and, for a user, only whether the two named
variables are set. It makes no network call. No file passes; validation `none` or an unset
variable warns; an invalid file fails.

## Live acceptance steps

Run on a test CPU (firmware 4.1 if possible) with a disposable program, in this order, and record
the evidence (tool documents and audit records):

1. `doctor --plc-webapi-config <file>`: targets listed, variables reported as set.
2. `plc_online_read` `ping`, `version`, `permissions` for an anonymous target and for a user target
   (compare `permissions` with the rights configured in TIA Portal).
3. `read_operating_mode`, `read_mode_selector`, `read_cpu_type`, `read_runtime_information`,
   `read_system_time`, `read_project_information` against the TIA Portal online view.
4. `browse_tags` (root, a DB with `browseMode children`, one variable with `var`, `type
   code_blocks`), then `read_tags` in `simple` and `raw` for Bool, Int, Real, String, DTL and an
   unknown name; compare with a watch table.
5. `read_diagnostic_buffer` (`count`, `language`), `browse_alarms` (with a raised test alarm),
   `browse_modules`, `read_module_status`, `read_module_leds`, `browse_syslog`.
6. read-write: `dryRun` (no prompt, no PLC request), then `change_operating_mode stop` (one prompt,
   CPU goes to STOP), `run`, and a declined prompt (nothing changes).
7. `write_tags` on a test DB (integer, real, bool, string), including a failing second tag;
   `acknowledge_alarm`, `flash_leds`, `set_system_time`.
8. `create_backup` (new file, existing file without and with `overwrite`), then `restore_backup` of
   that file on the test CPU. Observe in particular whether the client helper's logout/login after
   the restart succeeds (see open risks).
9. Certificates: `system` with an untrusted certificate fails `webapi_unreachable`; the correct
   `thumbprint` works, a wrong one fails; `none` works and warns.
10. Session: wait past the CPU's session timeout and call again (one re-login); remove a right and
    confirm `webapi_permission_denied`.
11. full mode: no prompt, audit `confirmation.by` is `policy`.
12. A PLCSIM Advanced instance on a TCP/IP adapter with the web server enabled, if available.
13. `read_watch_table_live` with a watch table holding a tag, a DB member, a nested struct member, an
    array element and an absolute address, and with a tag table: record the `name` Openness reports for
    each entry, which entries are mapped, and compare the values with the TIA Portal watch table online.
    Then call it unbound (`binding_conflict`) and with a `target` whose CPU runs another program.

## Open risks

- Not live-tested at all (see the banner).
- `read_watch_table_live`: the Openness entry-name format is undocumented; if it differs from the
  mapped notations, entries stay unmapped (safe, but the read is less useful) until the rules are
  widened with live evidence.
- `restore_backup` relies on the client's helper, which logs out and in again after each restart
  (`ReLoginAsync`, `Api.Logout` with the pre-restart token). Whether the CPU accepts that logout is
  not verified; if it does not, the item fails although the restore may have succeeded, and the
  message says to inspect the PLC. Once the upload has finished, the helper's wait loop for the
  restore-mode restart polls synchronously and leaves only through the expected ticket state, an
  exception or cancellation (ApiBackupHandler.cs 116), which is why the item is bounded to ten minutes.
- The client formats `Plc.SetSystemTime` timestamps with the current culture's calendar
  (`ToString` with a fixed pattern, `ApiRequestFactory.cs` 782). The adapter therefore makes the call
  under the invariant culture (tested under de-DE, th-TH, ar-SA and fa-IR); any other client method
  that formats dates is not used.
- A password that occurs in an error message (also by chance, as a short one does) withholds that
  whole message: the item then shows only the category and the Web API error code.
- Session expiry is detected only by error 2; the CPU's session lifetime and session count limits
  are not managed beyond one session per target.
