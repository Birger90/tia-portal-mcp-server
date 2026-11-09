# Claude in TIA: hand work to Claude from inside TIA Portal

**Language:** English · [Deutsch](CLAUDE_IN_TIA.de.md)

> **Status: not live-tested.** Everything on this page is offline-qualified only: the Add-In compiles against a
> compile-only stub of the V21 Add-In API, the inbox/outbox contract, the MCP tool `tia_inbox` and the bridge logic are
> unit-tested on Linux. Nothing has run in TIA Portal or with an installed Claude Code CLI. The
> [unverified list](#unverified) names every assumption; the [one-time setup](#one-time-setup-on-the-engineering-pc)
> is the first live check.

## What it does

The TIA Portal V21 Add-In "Claude in TIA" adds a context menu **Claude** to the project tree, the Devices & Networks
editor, the project library and the global library. Its entries hand the selected objects to Claude:

| Menu entry | Action (`action`) | What Claude is asked to do |
| --- | --- | --- |
| An Claude übergeben… | `handover` | look at the objects and do what the request text says, or ask in the reply |
| Erklären | `explain` | explain what the objects do; read only |
| Prüfen | `review` | list errors, risks and style problems; read only |
| Überarbeiten | `revise` | revise the objects within the server's access mode and report every change |
| Dokumentieren | `document` | write documentation; change comments only when asked |
| Claude-Antworten anzeigen | — | show unread replies in a TIA Portal message box and mark them read |

The Add-In cannot call Claude. Claude acts only through the MCP server, so the hand-over goes through files:

1. A click writes a **request** to `%APPDATA%\TiaMcpServer\inbox\<id>.json`: project path, source, action, the selected
   objects (kind, name, MCP address hints, the raw parent chain of V21 type names and names) and the time. **No project
   data** (block content, values, passwords) is copied.
2. Claude reads it with the MCP tool **`tia_inbox`** — when the user says "schau in TIA" (MCP has no push to Claude), or
   automatically through the optional **Claude bridge** (below).
3. Claude does the work with the other tools of the server (the access mode still decides what may change) and finishes
   with `tia_inbox reply_request`, which writes `%APPDATA%\TiaMcpServer\outbox\<id>.json`.
4. In TIA Portal, **Claude → Claude-Antworten anzeigen** shows the replies and marks them read.

There is no free-text field in TIA Portal: the V21 Add-In API documents no text-input dialog (only
`MessageBoxProvider.ShowNotification`/`ShowConfirmation`), so the menu offers fixed actions. Additional instructions go
into the chat ("schau in TIA und mach X"); the request's `text` member exists for a later version.

## Request, reply and status files

Shared, Siemens-free source: `TiaMcpServer.Contracts/Shared/ClaudeInbox` (compiled into the Add-In as well; a dependency-free
JSON writer/reader, because the Add-In runs in TIA Portal's add-in sandbox). Closed schemas: every member is written
(`null` when not set), unknown members are refused.

| File | Schema | Members |
| --- | --- | --- |
| `inbox\<id>.json` | `tia-mcp-claude-request/1` | `id` (`yyyyMMddTHHmmssZ-xxxxxxxx`), `createdUtc`, `source` (`projectTree`, `devicesAndNetworks`, `projectLibrary`, `globalLibrary`), `action`, `text` (≤ 4000), `projectPath`, `selection[]` (≤ 50: `kind`, `name`, `chain[]` (≤ 32: `typeNames[]`, `name`), `address`), `addInVersion` |
| `outbox\<id>.json` | `tia-mcp-claude-reply/1` | `id`, `repliedUtc`, `status` (`done`/`failed`), `text` (≤ 60000), `from` (`claude`/`bridge`), `readUtc` |
| `outbox\<id>.status.json` | `tia-mcp-claude-status/1` | `id`, `state` (`queued`, `running`, `finished`, `failed`, `timedOut`, `skipped`), `updatedUtc`, `message` |

Every file is written through a temporary file and moved into place. A request has at most one reply: a second
`reply_request` is refused (`already_answered`). The root is `%APPDATA%\TiaMcpServer`; the server and the bridge accept
`TIA_MCP_INBOX_ROOT` instead (the Add-In always uses `%APPDATA%`).

`address` holds the hints the MCP tools take, built from the chain by `ClaudeSelectionAddressing`:
`plcName`, `unitName`, `blockPath` (`PLC_1/Blocks/Folder/Block`, `PLC_1/Units/U/Blocks/...`; a block group's path),
`typePath` (`PLC_1/Types/Folder/Type`), `tableName` + `folderPath` (`/`, `/Line/Cell`), `deviceName`, `deviceItemName`,
`library` (`project` or the global library's path), `libraryTypePath` (`/Folder/Type`), `masterCopyName` +
`masterCopyFolderPath`. They are hints: Claude confirms them with `browse_project_tree`, `plc_read`, `network_read` or
`library_read` before use. An object of another kind (HMI screens, technology objects, ...) is `other` with no address;
its chain still names it.

## MCP tool `tia_inbox`

Available in **every** access mode (13/33/33 tools). It touches neither TIA Portal nor the project nor the binding; its
only write is the reply file (annotations: not read-only, not destructive, closed world). Batch of up to 50 operations,
contract `1.0`, envelope `OperationBatchReadResponse`:

| Operation | Fields | Result |
| --- | --- | --- |
| `list_requests` | optional `includeAnswered` | `inboxDirectory`, `outboxDirectory`, `pendingCount`, `requests[]` (`id`, `createdUtc`, `source`, `action`, `text`, `projectPath`, `selectionCount`, `answered`, `bridgeState`, `error`) |
| `get_request` | `id` | the whole request plus `instruction` (what the action asks), `reply`, `bridgeState` |
| `reply_request` | `id`, `text`, optional `status` (`done`, `failed`) | `id`, `status`, `repliedUtc`, `replyFile` |

Failures: unknown id `target_not_found`, a second reply `state_changed`, an unusable request file `validation_error`.
The tool description tells Claude to check the inbox when the user says "schau in TIA" / "look in TIA".

## Claude bridge (optional, off by default)

`TiaMcpClaudeBridge.exe` (project `TiaMcpServer.ClaudeBridge`, net10.0, Siemens-free) starts Claude **without a chat**
for every new request:

- watches the inbox (FileSystemWatcher plus a rescan every 30 s), one request at a time, one instance per user;
- re-reads the settings before each pass: switching it on or off needs no restart;
- records each id in `%APPDATA%\TiaMcpServer\bridge\state.json` **before** the run, so no request ever runs twice
  (also after a crash); an unreadable state file stops the bridge rather than risk a repeat;
- skips (status `skipped`) requests older than 24 hours, invalid request files and requests answered meanwhile;
- runs the configured command without a shell (argument list), the prompt on standard input, with the time limit
  (status `timedOut`, process tree killed);
- if the run ends without a `tia_inbox` reply, writes the CLI's final output (the `result` member of the JSON output,
  else the raw output; `failed` with the error output when the run failed) as the reply (`from: bridge`);
- logs to `%LOCALAPPDATA%\TiaMcpServer\claude-bridge\bridge.log` (rotated at 1 MB).

The prompt (`ClaudeBridgePrompt`) contains the request, tells Claude to use only the TIA MCP tools, to call
`tia_inbox get_request`, never to open/close/save a project or download/go online unless the request asks for exactly
that, and to finish with `tia_inbox reply_request` in German.

### Settings (`settings.json` version 2, member `claudeBridge`)

Edited in **TiaMcp Passwortverwaltung → Zugriffsmodus → Claude-Brücke** (Brücke an/aus, Befehl, Befehlsvorlage,
Zeitlimit, erlaubte Werkzeuge, Autostart). The settings file is version 2 when the bridge section is set (version 1
otherwise); the server reads both versions and ignores `claudeBridge`.

```json
{ "version": 2, "accessMode": "read-write",
  "claudeBridge": { "enabled": false, "command": "claude",
    "arguments": ["-p", "--output-format", "json", "--tools", "", "--allowedTools", "{allowedTools}", "--permission-prompts", "none"],
    "timeoutMinutes": 20, "allowedTools": ["mcp__tia-portal__bind_project", "...", "mcp__tia-portal__vci_write"] } }
```

Placeholders in `arguments`: `{allowedTools}` (the list joined by commas), `{prompt}` (then the prompt is an argument,
not standard input), `{requestId}`, `{inboxRoot}`. Defaults: off; the allow-list holds the 33 tools of this server as
`mcp__tia-portal__<tool>` and **no built-in Claude Code tool** (`--tools ""` removes them; no shell, no file editing, no
web). The flags are those `claude --help` of Claude Code **2.1.317** lists (`-p/--print`, `--output-format json`,
`--tools`, `--allowedTools`, `--permission-prompts none`: "anything that would prompt is denied automatically");
they are **not verified** on the PC — check `claude --help` there and adjust the template in the password manager.

Autostart: the checkbox writes `TiaMcp Claude-Bruecke.cmd` (`start "" /min "<bridge>"`) into the user's Startup folder
(a `.cmd`, not a `.lnk`: K44) and removes it when cleared.

## One-time setup on the engineering PC

1. **Install the Add-In** from a source checkout (PowerShell, .NET SDK 10, TIA Portal V21 installed):
   `scripts\install-claude-in-tia.ps1` (add `-InstallBridge -BridgeAutostart` for the bridge). The script builds
   `TiaMcpServer.AddIn` against the installed V21 assemblies, validates `AddInPublisherConfig.xml` against the installed
   `Siemens.Engineering.AddIn.Publisher.xsd`, runs `Siemens.Engineering.AddIn.Publisher.exe --configuration ...
   --outfile ClaudeInTia.addin --logfile ... --verbose` and copies the `.addin` to
   `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns` (folder name unverified; the task card can add the file from
   anywhere). Manual equivalent: build with `/p:UseTiaPortalReferenceStubs=false /p:TiaAddInReferenceDir=<folder with
   Siemens.Engineering.Base.dll and Siemens.Engineering.AddIn(.Base).dll>`, then run the Publisher with the configuration
   next to the DLL.
2. **Activate** in TIA Portal: task card **Add-Ins** → "Claude in TIA" → activate; TIA Portal shows the requested
   permissions (TIA read access, file access) and asks to confirm.
3. **MCP server in Claude**: Claude Desktop as in the [installation guide](../guides/installation.md); for the bridge also
   Claude Code: `claude mcp add -s user tia-portal -- "<path>\TiaMcpServer.exe"` (user scope, because the bridge runs in its
   own working folder; add the server's arguments after the path), then log in to Claude Code once (`claude`).
4. **Bridge** (optional): TiaMcp Passwortverwaltung → Zugriffsmodus → Claude-Brücke → "Brücke an", check the command
   template against `claude --help`, save; tick Autostart or start `TiaMcpClaudeBridge.exe` once.

The Windows CI workflow uploads the Add-In built against the stubs (`claude-in-tia-addin-<sha>`) with the Publisher
configuration and the install script. That DLL is a compile check only: install the one the script builds against the
installed TIA Portal (the Publisher needs TIA Portal and does not run in CI).

## Provenance of every Siemens member used

Sources: **(X)** the V21 IntelliSense file `Siemens.Engineering.AddIn.Base.xml` (names, namespaces, parameter types; no
return types); **(H)** the V21 online help (docs.tia.siemens.cloud, it-it/v21: "Programmazione del View Provider",
"Programmazione dei menu di scelta rapida", "API Messagebox", "API di feedback", "File di configurazione",
"Generazione dell'Add-In dal file DLL"); **(B)** `Siemens.Engineering.Base.xml`. Status: **compiled** (declared in the stub
`reference-stubs/Siemens.Engineering.AddIn.Base`), **assumed** (the compiled signature takes a detail neither X nor H
shows; the first real-DLL build checks it), **runtime** (bound by name on the live object, no stub member), **existing**
(already in the Base stub).

Stub types (each pinned by `ClaudeInTiaAddInContractTests`): `MenuItem`, `ContextMenuAddIn`, `ContextMenuAddInRoot`,
`ChildItemFactory`, `ActionItem`, `ActionItem<T>`, `MenuSelectionProvider`, `MenuSelectionProvider<T>`,
`ProjectTreeAddInProvider`, `DevicesAndNetworksAddInProvider`, `ProjectLibraryTreeAddInProvider`,
`GlobalLibraryTreeAddInProvider`, `MessageBoxProvider`, `FeedbackProvider`.

| Siemens member | Source | Status |
| --- | --- | --- |
| `ContextMenuAddIn` `.ctor(string)`, `BuildContextMenuItems(ContextMenuAddInRoot)` (protected, overridden) | X, H | compiled; abstract/protected assumed |
| `ContextMenuAddInRoot.Items` → `ChildItemFactory` | X (property), H (`addInRootSubmenu.Items.AddActionItem`) | compiled; type assumed |
| `ChildItemFactory.AddActionItem<T>(string, ActionItem<T>.OnClickDelegate)` | X, H | compiled; return type assumed `void` (unused) |
| `ActionItem`, `ActionItem<T>`, `ActionItem<T>.OnClickDelegate` = `void (MenuSelectionProvider<T>)` | X (types), H (`Entry1_OnClick(MenuSelectionProvider<IEngineeringObject>)`) | compiled; delegate signature from H |
| `MenuItem` (base of `ActionItem`) | X | compiled; base relation assumed |
| `MenuSelectionProvider`, `MenuSelectionProvider<T>` | X | compiled (no members) |
| `MenuSelectionProvider.GetSelection()` | X (no return type) | runtime: the parameterless non-generic `GetSelection`, result enumerated as `IEngineeringObject` |
| `ProjectTreeAddInProvider`, `DevicesAndNetworksAddInProvider`, `ProjectLibraryTreeAddInProvider`, `GlobalLibraryTreeAddInProvider`: `.ctor()`, `GetContextMenuAddIns()` → `IEnumerable<ContextMenuAddIn>` | X, H (`protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()`) | compiled; the derived providers take `TiaPortal` in their constructor (assumed: TIA Portal passes it; H shows `TiaPortal` passed into a context menu) |
| `MessageBoxProvider` (an `IEngineeringService`) via `TiaPortal.GetService<MessageBoxProvider>()` | X, H (`m_TiaPortal.GetService<MessageBoxProvider>`), B (`TiaPortal.GetService``1`) | compiled; `IEngineeringService` assumed from the `GetService` constraint |
| `MessageBoxProvider.ShowNotification(NotificationIcon, string, string, string)` | X, H (returns void) | runtime (enum member by name: `Information`, `Success`, `Warning`, `Error`, all in B) |
| `FeedbackProvider` via `TiaPortal.GetService<FeedbackProvider>()`, `FeedbackProvider.Log(NotificationIcon, string)` | X, H | compiled type; `Log` runtime (fallback when no message box) |
| `IEngineeringObject.GetAttribute("Name")`, `IEngineeringInstance.Parent`, `ProjectBase.Path`, `TiaPortal.Projects` | B | existing |
| `GlobalLibrary.Path` | B (`P:...GlobalLibrary.Path`, type not shown) | runtime (property `Path`, `FileSystemInfo` or `string`) |
| Type names compared as text (`...SW.Blocks.PlcBlock`, `...PlcBlockUserGroup`, `...SW.PlcSoftware`, `...SW.Units.PlcUnit`, `...SW.Types.PlcType`, `...SW.Tags.PlcTagTable`, `...HW.Device`, `...HW.DeviceItem`, `...Library.Types.LibraryType`, `...Library.MasterCopies.MasterCopy`, ...) | B, Step7 XML (every name is a documented `T:` entry) | runtime type names (`GetType()` and base types), no member call |

Not used although documented: `ActionItem` update-status delegates (signature not shown), icons (`AddActionItemWithIcon`),
submenus, check-box/radio items, `ProgressProvider`, `Siemens.Engineering.AddIn.Utilities.Process` and
`ProcessStartPermission` (the Add-In starts no process: the bridge is a separate program).

## Permissions

`AddInPublisherConfig.xml` requests `TIA.ReadOnly` (the Add-In only reads names and parents of the selected objects) and
`System.Security.Permissions.FileIOPermission` for the inbox/outbox (element `PermissionType`, validated against the
installed V21 `Siemens.Engineering.AddIn.Publisher.xsd` on 2026-11-07, K97; the earlier `FileIOPermission-UNRESTRICTED`
spelling was rejected by the schema). The code touches only `%APPDATA%\TiaMcpServer\inbox` and `...\outbox`
(pinned by `ClaudeInTiaAddInContractTests`). No `UnrestrictedPermissions`, no process start, no network.

## Unverified

- Assembly file and identity of the Add-In API: the XML names `Siemens.Engineering.AddIn.Base`, the help names
  `Siemens.Engineering.AddIn.dll`; the project takes whichever exists; the stub assumes version 21.0.0.0 and the Openness
  public key token. Namespaces follow the XML (`Siemens.Engineering.AddIn`, `.Menu`); the help shows
  `Siemens.Engineering.AddIn.Base(.Menu)`.
- The provider constructors with `TiaPortal`, the assumed signatures in the provenance table, and whether the
  Add-In sandbox allows the runtime binding by reflection (public members only).
- `AddInPublisherConfig.xml`: namespace `http://www.siemens.com/automation/Openness/AddIn/Publisher/V21` (the help shows
  `.../Vxx` with two spellings), the permission element spelling, and whether `FeedbackProvider`/`MessageBoxProvider`
  need more permissions.
- The user add-in folder `%APPDATA%\Siemens\Automation\Portal V21\UserAddIns`.
- Claude Code: installed and logged in on the PC, the CLI flags of the default template, the JSON output member
  `result`/`is_error`, that `--tools ""` removes every built-in tool and that `--permission-prompts none` denies what the
  allow-list does not cover, that `mcp__tia-portal__<tool>` matches the MCP server name chosen in `claude mcp add`.

## Left out

| Item | Why |
| --- | --- |
| Free-text input in TIA Portal | V21 documents no add-in text-input dialog; a WinForms dialog inside the sandbox is undocumented |
| Claude reacting by itself without the bridge | MCP has no server-to-client push that starts a conversation |
| Copying block content into the request | by design: the request carries names only; Claude reads content through the tools under the access mode |
| Signing the `.addin` | needs the owner's certificate (`Certificates` in the configuration, `--certificatepassword`); an unsigned add-in shows TIA Portal's warning |

## Live acceptance checks

1. `install-claude-in-tia.ps1` builds against the real V21 assemblies (any compile error is an assumed signature) and the
   configuration validates against the installed XSD; the Publisher creates `ClaudeInTia.addin`.
2. TIA Portal lists and activates the Add-In; the menu **Claude** appears in all four places.
3. "Prüfen" on a block writes a request whose `blockPath` `plc_read` accepts; "Prüfen" on a device, a library type and a
   master copy give usable `deviceName`/`libraryTypePath`/`masterCopyName`.
4. `tia_inbox list_requests` shows it; `reply_request` answers; "Claude-Antworten anzeigen" shows the reply in a message
   box and a second click says "Keine neuen Antworten".
5. Bridge on: a new request starts `claude -p` once (log, status `running` → `finished`), the reply arrives; a restart of
   the bridge does not run it again; with Claude Code not logged in the reply says why (`failed`).
