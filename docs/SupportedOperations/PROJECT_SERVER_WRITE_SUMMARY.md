# Project Server administration (`project_server_write`)

**Language:** English · [Deutsch](PROJECT_SERVER_WRITE_SUMMARY.de.md)

`project_server_write` is the Portal-level write surface for TIA Portal Multiuser (owner decision 2026-11-09). It acts on
the Project Server connections of one TIA Portal instance (`TiaPortal.ProjectServers`) and on the servers those
connections reach. It never acts on a bound project and needs no binding. The read side stays `bind_project` with its
inventory actions (see the [Multiuser reference](MULTIUSER_OPERATIONS_SUMMARY.md)); `bind_project` stays read-only in every
mode.

> **Status: not live-tested.** Built against the compile-only reference stubs, unit-tested on the host domain
> (`ProjectServerWriteDomainTests`) and run end to end through the scripted FakeWorker (`ProjectServerWriteMcpTests`,
> `TIA_MCP_FAKE_WORKER_PROJECT_SERVERS=1`). Nothing here has run against TIA Portal or a Project Server. Every Siemens
> member below is bound at runtime by its documented name and parameter list; if the installed V21 does not expose it, the
> item fails `unsupported_capability` and nothing is called.

Tool counts: 13 tools in read-only, 33 in read-write and 33 in full since `project_server_write` (13/32/32 before). The
tool is not offered in read-only.

## Caller responsibility: which server

The tool never chooses a server. Every item names the connection by its exact alias (as `bind_project`
`list_server_connections` reports it), and the call acts on that server only. **Never use it on a customer's or another
team's Project Server unless the user explicitly names that server for this task.** Uploading a project makes it visible
to every user of that server; deleting a local session can lose changes that were not checked in. The MCP cannot tell a
test server from a production one; the agent and the user are responsible for picking the right one.

## Operations

| Operation | Fields | Siemens member (V21 `Siemens.Engineering.Base.xml`) | Guard | Read-back |
| --- | --- | --- | --- | --- |
| `create_server_connection` | `serverAlias` (new), `protocol` `Https`/`Http`, `host`, `port` 1-65535 | `ProjectServerComposition.Create(String alias, Protocol, String host, Int32 port)` → `ProjectServer` | info `project_server_creates_connection`; info `project_server_alias_exists` when the alias is already listed (TIA Portal decides) | `list_server_connections`: alias listed once with host and port (check `serverConnection`); the protocol has no documented read |
| `set_server_connection` | `serverAlias`; `host`, `port` and/or `protocol` | `ProjectServer.SetProtocol(Protocol)`, `.SetHostName(String)`, `.SetPort(Int32)` in this order | info `project_server_changes_connection` | host/port as above; a protocol-only change stays `trusted` (no documented read) |
| `delete_server_connection` | `serverAlias` | `ProjectServer.DeleteConnection()` | **acknowledge** `project_server_deletes_connection` | the alias is no longer listed |
| `add_project_to_server` | `serverAlias`, `group`, `projectPath` (existing `.ap21`) | `ProjectServer.AddProjectToServer(FileInfo)` (root) or `ProjectServerGroup.AddProjectToServer(FileInfo)` (named group) | **acknowledge** `project_server_uploads_project` | `list_server_projects` of that group lists a project that was not listed before the call (check `serverProject`; TIA Portal names it, the file name is reported as the expectation) |
| `create_local_session` | `serverAlias`, `group`, `serverProjectName`, `sessionName`, `sessionDirectory` (absolute), `sessionMode` `Multiuser` (default)/`Exclusive` | `ProjectServer.CreateLocalSession(ServerProjectInfo, String, DirectoryInfo, SessionCreationMode)` → `LocalSessionInfo` | info `project_server_creates_local_session` | `list_local_sessions` lists the returned `SessionId` at the returned `ProjectFileInfo` path (check `localSession`) |
| `delete_local_session_from_server` | `serverAlias`, `group`, `serverProjectName`, `sessionId` | `ProjectServer.DeleteLocalSessionFromServer(ServerProjectInfo, LocalSessionInfo)` (the `LocalSessionInfo` is taken from `GetLocalSessions` by exact id) | **acknowledge** `project_server_deletes_local_session`; info `project_server_bound_session` when the session is the bound project | the id is no longer listed |

`group` is `{"isRoot":true,"name":null}` for the server root or `{"isRoot":false,"name":"Exact"}`, exactly as in
`bind_project`. A field an operation does not document is refused (`validation_error`). Every item also takes
`portalConfirmations` (below).

Enums are bound by their documented full names: `Siemens.Engineering.Multiuser.Protocol` (`Https`, `Http`) and
`Siemens.Engineering.Multiuser.SessionCreationMode` (`Multiuser`, `Exclusive`). The `ServerProjectInfo` passed to
`CreateLocalSession`/`DeleteLocalSessionFromServer` is the one `GetServerProjects()` of the root or the named
`ProjectServerGroup` returns for the exact project name, as the inventory reads resolve it.

### Binding to a created local session

`create_local_session` returns `sessionId` and `sessionPath` (the `.als21` file). Binding to it is a separate, guarded
`open_project` call with that path: binding changes stay with `open_project`/`bind_project`, which check the source
project, ownership and preservation rules. Doing it inside `project_server_write` would bypass those checks, so the tool
does not bind.

### Examples

```json
{ "operations": [
  { "operationId": "conn", "operation": "create_server_connection", "serverAlias": "TestServer",
    "protocol": "Https", "host": "mu-test.example", "port": 8735 },
  { "operationId": "upload", "operation": "add_project_to_server", "serverAlias": "TestServer",
    "group": { "isRoot": true, "name": null }, "projectPath": "D:\\Projects\\Line7\\Line7.ap21" }
], "dryRun": true }
```

```json
{ "operations": [
  { "operationId": "session", "operation": "create_local_session", "serverAlias": "TestServer",
    "group": { "isRoot": true, "name": null }, "serverProjectName": "Line7",
    "sessionName": "Line7_bs", "sessionDirectory": "D:\\Sessions" }
] }
```

## TIA Portal, binding and access mode

- **Which TIA Portal.** With a verified binding the call acts in the bound project's TIA Portal (a different
  `portalProcessId` is refused before anything is sent) and the binding's identity is sent and re-checked by the worker
  before and after each call. Without a binding the optional `portalProcessId` names the TIA Portal (`bind_project
  list_portals`); omitted, the worker uses the TIA Portal it is attached to or the only one running.
- **No binding needed.** The binding strategy pins whatever the session holds (unbound, configured or verified); it never
  grounds, adopts, opens or switches a project. The project container's capability table does not apply
  (`IWriteDomain.IsPortalScoped`).
- **Access mode.** Read-only: the tool is not offered and every `project_server_*` worker method is denied
  (`OperationCapability.ProjectLifecycle`). Read-write: a call that fires an acknowledge guard (delete local session, delete
  connection, upload project) asks the user once through form elicitation; the other items never prompt. Full: the
  acknowledge guards are satisfied by policy, no prompt. `dryRun:true` plans and reports guards and effects without
  calling the server and without a prompt.
- **Credentials.** None of the documented members takes credentials, so the tool takes no password field. Authentication
  to the Project Server is TIA Portal's own (its Multiuser user management); a dialog it raises is a TIA Portal confirmation
  and is answered by `portalConfirmations`.

## Planning, execution and verification

1. **Validation** (no worker call): known operation, only its documented fields, required fields, protocol/session mode
   names, port range, coherent `group`, absolute paths with the shared caller-path rules (`PlcFileTargets.PathShapeError`),
   `projectPath` an existing `.ap21`, `sessionName` a valid file name.
2. **Planning** reads only the existing inventory: `list_server_connections` once, then per server item
   `list_server_groups` (named group), `list_server_projects` and, for session items, `list_local_sessions`. A missing
   alias, group, project or session refuses the whole call (`target_not_found`, "No item of this call ran"). Planning
   simulates the call's own connection changes: a later item may use a connection an earlier item creates or changes; its
   group, project or session is then resolved only when it runs (info `project_server_resolved_at_mutation`). The same
   applies to a session an earlier item creates and to a project an earlier item uploads.
3. **Confirmation** (read-write with acknowledge guards): the prompt lists the acknowledge guard lines, the TIA Portal
   the call acts in and one line per item with its target, its `portalConfirmations` policy and its `operationId`, all
   caller values quoted and escaped (`ConfirmationText`). After the reply the call is planned again; a changed inventory
   refuses the call (`binding_conflict`, nothing changed).
4. **Execution**: items run in order (worker methods `project_server_<operation>`, parameters in
   `WorkerRequest.ProjectServer`) and the call stops at the first failure without rollback or replay. A
   `MultiuserException` (the documented error of every Multiuser call) or an `EngineeringTargetInvocationException` is
   TIA Portal's refusal: `engineering_rejected` with `engineeringError`.
5. **Verification** reads back through the same inventory reads (table above). A failed item is read back too and is
   never verified (K58). Keys: the connection, then the server content below it, so a later change or delete of the
   connection supersedes earlier items on it (with a message that the server-side result is no longer verified through it).
6. **Audit**: one audit v2 record per call (dry runs and refusals included) like every guarded write; confirmation `user`,
   `policy` or `none`.

### TIA Portal confirmation dialogs

Every item takes `portalConfirmations` like `library_write`: until the item's evidence is collected, every TIA Portal
`Confirmation` is answered No (`decline`, default; recorded, the item fails) or Yes (`accept`; recorded, a warning). The
worker opens `TiaPortalSession.AnswerConfirmations` with a `PlcDownloadPortalConfirmationLog` (owner = worker method),
closes it before copying the evidence into `ProjectServerWriteResultInfo` (failure evidence on
`WorkerResponse.PlcDownloadOutcome`); the closed scope lingers until the next request, which reports late dialogs as
warnings.

## Result

Each item's `result` is `ProjectServerWriteResultInfo`: `operation`, `serverAlias`, `applied` (the documented calls made,
in order), `hostAfter`/`portAfter` (read from the `ProjectServer` after create/set), `sessionId`/`sessionPath`
(create/delete local session), `portalConfirmationPolicy`, `portalConfirmations`, `portalConfirmationsObserved`,
`portalConfirmationsComplete`. The response root is the shared guarded-write document (`tool`, `contractVersion` `1.0`,
`phase`, `success`, `error`, `warnings`, `guards`, `effects`, `batch`, `verification`, `omission`).

## Implementation map

| Part | Location |
| --- | --- |
| Names, request and result contracts | `TiaMcpServer.Contracts/Multiuser/ProjectServerWriteInfos.cs` (`ProjectServerMethods`, `ProjectServerRequestInfo`, `ProjectServerWriteResultInfo`) |
| Policy / capability | `OperationPolicyCatalog` (`ProjectLifecycle`, no expected session identity required), `ProjectCapabilityCatalog.OperationFor` (null: not project-scoped) |
| Worker | `Openness/Multiuser/ProjectServerWriteService.cs`, dispatch `Program.ProjectServer.cs` through `MultiuserPortalReadDispatch.Run` (Portal-only attachment checks) |
| Host | `TiaMcpServer/ProjectServers/` (tool, binding strategy, domain, guards, verification policy, budget), client `OpennessWorkerClient.ProjectServer.cs` |
| FakeWorker | `TiaMcpServer.FakeWorker/Multiuser/ProjectServerScenario.cs` (`TIA_MCP_FAKE_WORKER_PROJECT_SERVERS=1`; markers `Rejected.ap21`, session name `AskDialog`) |

## Left out

| Member | Reason |
| --- | --- |
| `LocalSession.CloseAndCommit(String)` | A check-in to the server from the bound local session: it is a lifecycle step on the binding, not server administration, and committing changes for the user needs its own owner decision. |
| `LocalSessionComposition.OpenServerProject(FileInfo)` | Opens a local session; opening stays with `open_project` (`.als21`), which owns the binding rules. |
| `LocalSession.IsUptoDate()`, `LockStateProvider.*` | Reads; `get_lock_state` already exposes the lock, `IsUptoDate` belongs to a future session-state read. |
| Server projects' deletion, group creation, user management | No documented V21 member. |

## Live acceptance checks (to do)

Against a test Project Server the user names, in read-write: create a connection, list it, change its port, delete it
(prompt shown); upload a small `.ap21` (prompt shown) and find it with `list_server_projects`; create a local session and
open it with `open_project`; delete another local session (prompt shown); a dialog raised by TIA Portal with
`portalConfirmations` decline and accept; a `MultiuserException` (for example an existing alias) reported as
`engineering_rejected`.
