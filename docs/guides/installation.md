# Installation

**Language:** English · [Deutsch](installation.de.md)

How to install the TIA Portal MCP server, verify it, and register it with an MCP client.
For a condensed quick start, see the [README](../../README.md).

## Requirements

- Windows
- Siemens TIA Portal V21 installed
- TIA Portal Openness installed and enabled
- Current Windows user is a member of the `Siemens TIA Openness` user group
- Stable .NET 10 SDK (`10.0.400` or newer in the .NET 10 feature bands) for `dotnet tool install`
- .NET Framework 4.8 Runtime for the Openness worker

Source builds additionally need:

- Stable .NET SDK 10.0.400 or newer in the .NET 10 feature bands. The repo's `global.json`
  requires 10.0.400, rolls forward through stable .NET 10 feature bands, and excludes prerelease SDKs.
- .NET Framework 4.8 Developer Pack or targeting pack

By default, source builds expect Openness DLLs here:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Local developer builds prefer real TIA Portal V21 assemblies from `TiaPortalV21Dir`. You can override that path with the `TiaPortalV21Dir` MSBuild property or environment variable. It must point to the folder containing `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll`.

The repo contains source-owned, generated compile-time reference stubs in `ref/`, declared under `reference-stubs/`, so CI can build and package the MCP server without installing TIA Portal. They preserve the V21 assembly identity using a public-only key and are never runtime substitutes. See [building from source](../development/building.md#generated-openness-references) for default verification and explicit artifact regeneration. Stub and installed-reference builds are compile evidence; live TIA behavior requires separate acceptance. Those stubs are fallback-only when a local TIA install is not found. To force stub references for CI/package builds:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
```

To force local TIA references:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

During build, the worker prints the selected reference directory:

```text
TIA Openness compile references: C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48 (UseTiaPortalReferenceStubs=false)
```

Local sessions support exact observed `.amc21` adoption, already-open startup assertions and
basic status; writable `open_project` accepts an exact existing `.als21` file. Local content,
compile, save, close/discard/commit and server mutation remain deferred. The
[Multiuser reference](../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md) records scoped
live acceptance: offline read-write actual opening required Siemens dialog dismissal in both
tested fixtures; noninteractive/full-offline support is not established by that run.

## Install

```powershell
dotnet tool install -g TiaMcpServer
```

Run the installed server:

```powershell
tia-mcp
```

To bind an MCP server process to a specific project, pass `--project` or set `TIA_MCP_PROJECT_PATH`:

```powershell
tia-mcp --project C:\Projects\Line.ap21
$env:TIA_MCP_PROJECT_PATH = 'C:\Projects\Line.ap21'
tia-mcp
# Synthetic example: assert the engineering path of an already-open local session
tia-mcp --project 'C:\Disposable\A\Engineering\A.amc21'
```

The server refuses to start (exit code 1) when the `--project` or `TIA_MCP_PROJECT_PATH` value is not
an absolute `.ap21` or `.amc21` path.

`--project` starts as a configured-but-unverified assertion. Before any guarded write preview, the
host performs a read-only status check and accepts the binding only when the worker reports a complete,
matching identity: worker process, TIA Portal PID, project generation, and canonical project path.
`bind_project` explicitly adopts an already-open project; `open_project` opens one, and
`create_project` creates one. Only open/create can open a project. Ordinary unbound reads do
not silently bind the session, and no request implicitly opens a project.

The worker never chooses the first running Portal or first open project. It selects an exact project
path when one was supplied, or a sole candidate when there is genuinely only one. Multiple possible
Portals/projects fail with `target_ambiguous` before Attach or mutation. A later path, PID, worker, or
project-generation mismatch fails with `binding_conflict` and invalidates the binding; call
`bind_project` with `forceRebind=true` for an already-open project, or start a new MCP session. `get_project_status`
is read-only, takes no arguments and is non-binding: do not use it to switch projects. Content tools act on the bound project only; with no verified binding (a configured `--project` is grounded by status first) they fail closed with `binding_conflict`; call `bind_project` first. Use `bind_project` for adopting or
switching to an already-open project; use `open_project` only for opening one.

Call `bind_project` without a path to adopt the sole open project or receive the candidate list;
with multiple open projects, supply an advertised absolute `.ap21` or typed-owner `.amc21` path
and optional exact `portalProcessId`. `.als21` is an explicit writable opening input only;
an inventory directory is neither an opener nor a binding selector. A different configured,
verified, or last-bound path requires `forceRebind:true`. Reattachment may show TIA's Openness
access dialog, which a human must answer. Worker ownership is not carried across a detach; after
reattachment the project is treated as UI-owned. Binding itself never opens, creates, saves or closes.

### Version flag

Run `tia-mcp --version` (or `tia-mcp -v`) to print the host version and exit without starting the MCP server.

### Doctor command

Run `tia-mcp doctor` to validate the runtime environment before using the MCP server. It checks the operating system, .NET runtimes, TIA Portal installation, Openness assemblies, user group membership, worker executable, host/worker version compatibility, running TIA Portal processes, and project-binding configuration.

Doctor is non-invasive: it does not start the MCP host, attach to TIA Portal, open a project, or inspect project content. For an explicit binding it verifies that the value is an absolute path to an existing `.ap21` file. Process detection uses the Windows process list, so with multiple TIA Portal processes Doctor cannot prove which process has that file open; it reports that uncertainty instead of passing silently.

```powershell
tia-mcp doctor
tia-mcp doctor --json
tia-mcp doctor --verbose
tia-mcp doctor --project C:\Projects\Line.ap21
```

Options:

- `--json` - emit a single JSON document to stdout.
- `--verbose` - include diagnostic evidence for each check.
- `--project` - validate the exact absolute path of an existing `.ap21` project without opening or attaching to TIA Portal.
- `--plc-webapi-config` - parse a PLC Web API target file (default: `TIA_MCP_PLC_WEBAPI_CONFIG`) and list its targets, reporting only whether each target's credential variables are set. Doctor never contacts a PLC; an invalid file fails, because the server refuses to start with it.

Project-selection diagnostics are access-mode aware:

- no project binding is a Warning in every mode, with remediation naming `bind_project`;
- an invalid, relative, non-`.ap21`, or missing project path is a failure;
- multiple TIA Portal processes are a warning when an explicit binding is configured, because Doctor cannot verify the live match without attaching;
- multiple TIA Portal processes with no binding are a Warning in every mode; select the intended open project with `bind_project`.

Even an existing local project path remains a Doctor warning because Doctor deliberately does not
Attach and cannot prove which Portal has it open. Before using project tools, open the exact project
in the intended TIA Portal process. Runtime identity checks remain the authority for accepting or
rejecting a request.

Doctor's file-binding diagnostic still accepts only existing `.ap21` files. Runtime supports
already-open `.amc21` assertions, but Doctor does not validate them or attach to prove a local
owner. Inspect the runtime `bind_project`/status result for local-session identity.

Exit codes: `0` (no blocking failures), `1` (one or more checks failed), `2` (invalid arguments).

### Register with an MCP client

The `tia-mcp install` command registers the TIA Portal MCP server with a supported MCP client by invoking the client's native CLI. It does not edit configuration files directly.

Supported clients: Claude Code, Codex, OpenCode, MiMoCode.

```powershell
tia-mcp install claude-code
tia-mcp install codex
tia-mcp install opencode
tia-mcp install mimocode
```

Aliases: `claude` (Claude Code), `mimo` (MiMoCode).

Options:

- `--name <name>` - server registration name (default: `tia-portal`).
- `--access-mode <mode>` - access mode: `read-only`, `read-write`, or `full` (default: `read-only`).
- `--tia-project <path>` - bind to a specific TIA Portal project; must be an absolute `.ap21` or `.amc21` path.
- `--server-path <path>` - explicit path to the `tia-mcp` executable.
- `--dry-run` - print the install command without executing.
- `--json` - emit JSON output (not supported with MiMoCode).

Examples:

```powershell
# Register with Codex using read-write mode
tia-mcp install codex --access-mode read-write

# Register with Claude Code bound to a specific project
tia-mcp install claude-code --tia-project C:\Projects\Line.ap21

# Preview the install command without running it
tia-mcp install codex --dry-run

# JSON output for automation
tia-mcp install codex --json
```

MiMoCode uses interactive mode and will prompt for values. The `--json` flag is not supported with MiMoCode.

Exit codes: `0` (success), `1` (general failure), `2` (invalid arguments), `3` (unsupported client), `4` (client not found), `5` (tia-mcp executable not found), `6` (native command failed), `7` (verification failed), `8` (unsupported option combination).

### Access modes

The server supports three access modes, enforced at discovery, host dispatch, and worker dispatch:

- **read-only** - thirteen tools: eleven observation tools (including `plcsim_read`, `plc_online_read`, `library_read` and `siemens_docs`, the Siemens documentation reader that needs no TIA Portal), `tia_inbox` ("Claude in TIA") plus `bind_project`; no
  compile, edits, simulation control, download, online changes, going online, station upload, or lifecycle. `plc_read`
  `compare_to_online` (an Openness online compare that changes nothing) and `scan_accessible_devices` (live network
  discovery through one explicitly named PC interface, changes nothing) are observation and work in every mode.
- **read-write** (server startup default) - thirty-two tools: the read-only surface plus
  `compile_check`, `network_write`, `plc_write`, `hmi_write`, `vci_write`, `library_write`, `plcsim_write`, `plcsim_test`, all seven lifecycle tools, `plc_download`
  (to PLCSIM or to real hardware), `plc_online_write` (configured S7-1500 CPUs), `plc_tia_online` (TIA Portal go online/offline)
  and `plc_station_upload`. Every actual lifecycle, download, online-write, online-connection or upload call, and every `library_write`
  call that opens, retrieves, creates, saves or closes a global library, requires one confirmation form; the download prompt states when
  the target is a real controller. The other domain write calls (library content items included) and the PLCSIM tools never prompt.
- **full** - the same thirty-two tools and operations; lifecycle, download, online writes, online connections, uploads and global library lifecycle run without
  server elicitation, so the AI can download to a real controller (and answer its download configurations) or change a configured CPU
  online without asking. Unknown operations remain denied.

The access mode is the control over downloads and online writes: there is no separate hardware block. Start in
`read-only` to forbid them, `read-write` to approve each call, `full` only when the AI may
download or change PLCs unattended. Passwords that a download needs (for example a CPU write-protection password)
are passed as environment variables in the client configuration (`env`), never in a tool call; see
[MCP client configuration](mcp-client-configuration.md#plc-download-passwords). The same applies to the
Web API credentials of `plc_online_*` targets ([PLC Web API targets](mcp-client-configuration.md#plc-web-api-targets))
and to the online and upload passwords of `plc_tia_online` and `plc_station_upload`.

**Migration:** read-write clients can save, close, and use all lifecycle tools with confirmation.
Select `--access-mode full` for lifecycle without server elicitation. The Openness PLC run/stop
(`start_plc`/`stop_plc`) and the generic batch tools (`preview_write_batch`, `apply_write_batch`)
were removed; PLC writes use `plc_write`, and a configured real CPU's operating mode is changed with
`plc_online_write change_operating_mode` over the S7-1500 Web API (see
[PLC Web API targets](mcp-client-configuration.md#plc-web-api-targets)).
The install command still defaults
to read-only. To preview registration without changing client configuration:

```powershell
tia-mcp install codex --access-mode full --dry-run
```

`--project` configures an assertion; content tools take no `projectPath` and never open or switch projects in
any mode. Bind an already-open project explicitly with `bind_project` before writable operations.

Enable read-only mode:

```powershell
tia-mcp --access-mode read-only
tia-mcp --read-only
$env:TIA_MCP_ACCESS_MODE = 'read-only'
tia-mcp
```

Configuration precedence: CLI argument (`--access-mode`, `--read-only`, `--read-write`) > environment variable
`TIA_MCP_ACCESS_MODE` > server settings file > default (read-write).

The mode is resolved once at startup and cannot be changed during the process lifetime. There is no MCP tool that changes the access mode at runtime.

### Choosing the access mode in the password manager

Instead of `--access-mode` in the client configuration, the owner can choose the mode in the Windows program
"TiaMcp Passwortverwaltung" (`tia-mcp passwords`, tab **Zugriffsmodus**): three radio buttons ("Nur lesen (read-only)",
"Lesen und schreiben, mit Rückfrage (read-write)", "Voll, ohne Rückfrage (full)"), the stored value (or
"nicht gesetzt – Standard read-write"), **Speichern** and **Einstellung entfernen**. It writes the server settings file:

- Location: `%APPDATA%\TiaMcpServer\settings.json`, or the path in `TIA_MCP_SETTINGS` (set it in the client's `env`
  and start the program with `tia-mcp passwords --settings <same path>`).
- Content (closed schema, version 1, names only): `{ "version": 1, "accessMode": "read-only" }`; `accessMode` is optional
  (omitted: not set) and is exactly `read-only`, `read-write` or `full`. Unknown members, another version, a wrongly
  spelled mode, an unreadable file or a `TIA_MCP_SETTINGS` path that does not exist refuse startup with a message
  naming the file, even when a CLI argument or `TIA_MCP_ACCESS_MODE` decides the mode. A missing default file is no
  setting.
- The server reads it once at startup and never writes it; only the password manager does (through a temporary file in
  the same folder, never over a file the server would refuse). No MCP tool reads or changes it.
- A `--access-mode` argument or `TIA_MCP_ACCESS_MODE` in the client configuration takes precedence, so remove them when
  the program should decide; `tia-mcp install` still writes `--access-mode` (default `read-only`). A change takes effect
  when the MCP server restarts (quit and restart Claude Desktop).
- Startup logs `TIA MCP access mode source: cli | environment (TIA_MCP_ACCESS_MODE) | settingsFile (<path>) | default`
  and `TIA MCP settings file: <path> | none`; `tia-mcp doctor` prints `Access mode: <MODE> (<source>)` and
  `Settings file: <path> | none`, and `doctor --json` adds `accessModeSource` and `settingsFile` after `accessMode`.

With the settings file, a Claude Desktop entry needs no `args` at all:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp"
    }
  }
}
```

In read-only mode, the server exposes exactly eight MCP tools:

- `bind_project` — adopt or switch to an already-open project without project mutation.
- `get_project_status` — read active project metadata without opening or switching projects.
- `browse_project_tree` — browse a canonical paged v3 snapshot using `startSelector`, `depth`, and `pageSize`; continue with `cursor`.
- `plc_read` — run PLC block, type and tag-table reads in a batch.
- `read_cross_references` — read the cross-references of one project-tree target.
- `network_read` — run dedicated network reads in a batch.
- `hmi_read` — run read-only WinCC Unified HMI reads in a batch.
- `vci_read` — inspect standalone VCI workspaces, mappings and supported formats.

The following operations are **not available** in read-only mode:

- `compile_check` (invokes the Siemens compilation API)
- All project lifecycle operations (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`)
- All data mutations (`plc_write`, `network_write`, `hmi_write` and `vci_write`)

In read-only mode, the server operates only on an already-open project. It never opens, creates, saves or closes a project. Explicit `bind_project` can switch the selected session; ordinary reads cannot. Content tools act on the bound project only and fail closed with `binding_conflict` when unbound. Project-tree cursors reject binding changes with `cursor_binding_mismatch`, even after switching back to the original path.

Read-only mode is a security boundary enforced at three layers:

1. Project mutation, engineering, and lifecycle tools are not registered in MCP discovery; session selection remains available.
2. The host-side `OperationAccessPolicy` rejects prohibited operations before the worker process is started.
3. The worker-side `WorkerOperationAuthorization` independently rejects prohibited operations even if a raw worker request bypasses the host.

MCP client configuration example:

```json
{
  "mcpServers": {
    "tia-portal-read-only": {
      "command": "tia-mcp",
      "args": ["--access-mode", "read-only"]
    }
  }
}
```

The `tia-mcp doctor` command reports the active access mode and its source (`cli`, `environment`, `settingsFile`,
`default`).

### User confirmation by access mode

The removed startup switch fails with this exact migration message:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Every actual lifecycle, `plc_download` and `plc_online_write` call in read-write requires one form elicitation `accept` with boolean
`confirm:true`, including calls with no guards or only info guards. Missing capability, decline,
cancel, timeout, or transport failure denies with `access_denied`. Full uses policy confirmation
without server elicitation. Block guards stop a call in every mode, and `dryRun:true` never elicits.
Lifecycle accepts `dryRun` with its operation inputs; the former agent confirmation array is removed.
After accepted elicitation the server resolves fresh state under the same binding lease before
dispatch. Audit v2 records confirmation by `user`, `policy`, or `none`; acknowledge guard satisfaction
is `user`, `policy`, or null. `plc_write` and `network_write` use guarded writes without server
elicitation: preview with `dryRun:true`; omitted `dryRun` executes. No tool uses safety tokens.

An elicitation client's accepted response does not prove that a person saw a dialog. Keep
destructive tools out of client auto-approve lists to require client permission prompts on every
call. See the [lifecycle reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
for dry-run examples, lifecycle/local-source guards, and the staged major-release input/output migration.

The package includes the `openness-worker` and `plcsim-worker` folders and required non-Siemens dependencies, plus the host's MIT-licensed S7-1500 Web API client `Siemens.Simatic.S7.Webserver.API.dll` from nuget.org. It intentionally excludes `Siemens.Engineering*.dll` and every S7-PLCSIM Advanced assembly; those are loaded from the local TIA Portal and (optional) S7-PLCSIM Advanced installations at runtime. `tia-mcp doctor` reports where the PLCSIM Advanced API would be loaded from (`TIA_MCP_PLCSIM_API_DIR` or the registry) without loading it.

### Optional: Claude in TIA (TIA Portal Add-In)

The Add-In "Claude in TIA" adds a context menu **Claude** in TIA Portal that hands the selected objects to Claude through
the MCP tool `tia_inbox`; the optional Claude bridge starts Claude without a chat. Not live-tested. One-time setup:
run `scripts\install-claude-in-tia.ps1` from a source checkout (add `-InstallBridge -BridgeAutostart` for the bridge),
activate the Add-In in TIA Portal's task card **Add-Ins**, and for the bridge register the server with Claude Code
(`claude mcp add -s user tia-portal -- "<path>\TiaMcpServer.exe"`) and switch the bridge on in TiaMcp Passwortverwaltung,
tab **Zugriffsmodus**. Details, unverified points and the request format: [Claude in TIA](../SupportedOperations/CLAUDE_IN_TIA.md).
