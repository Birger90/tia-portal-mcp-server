# Password sources (environment variables and Windows Credential Manager)

**Language:** English · [Deutsch](password-sources.de.md)

No tool takes a password. Every password input names where the password is:

| Input | Tools |
| --- | --- |
| `passwords` (object: configuration type short name -> source) | `plc_download`, `plc_tia_online` (`go_online`), `plc_station_upload` |
| `passwordVariable` (one source) | `plc_write` `safety_login`, `set_safety_password`, `revoke_safety_password`, `protect_plc_confidential_data`, `change_plc_confidential_data_password` (current), `disable_plc_confidential_data_protection` (optional) |
| `newPasswordVariable` (one source) | `plc_write` `change_plc_confidential_data_password` |
| `accessLevelPasswords` (level → source) | `plc_write` `set_plc_access_level` |
| `passwords` (type → source) on a `plc_read` item | `compare_to_online` |

A source is one of:

| Form | Meaning |
| --- | --- |
| `"PLC1_PW"` or `{ "env": "PLC1_PW" }` | an environment variable of the server process (set it in the MCP client configuration `env`; the worker inherits it). The plain string is the original form and stays valid. |
| `{ "credential": "TiaMcp/<project>/<PLC>/<purpose>" }` | a Windows Credential Manager **generic** credential, read with the documented Win32 `CredReadW` (advapi32, `CRED_TYPE_GENERIC`) only at the call |

One shared rule (`PasswordSources` in Contracts) serves all inputs. The host checks the shape and redacts while the
request is read: a value that is neither a variable name nor a `TiaMcp/` target becomes a fixed placeholder and is refused
without being echoed; an unset environment variable is refused the same way. Credential targets must start with
the active prefix, by default `TiaMcp/` (see below; at most 256 characters, no control characters or `*`), so the server cannot be pointed at an unrelated
credential of the user. The worker resolves the source into a read-only `SecureString`; a credential blob (UTF-16LE, as
`cmdkey /pass` stores it) is copied character by character, then zeroed before `CredFree`. Responses, logs and the audit
record only the source token (`credential:TiaMcp/...` or the variable name), never a value. On an operating system other
than Windows a credential source fails with a clear message (`password_unavailable` for downloads and callbacks,
`unsupported_capability` for the fail-safe writes).

## Credential prefix (owner setting)

`TiaMcp/` is the default prefix. The owner may set another one with the environment variable
`TIA_MCP_CREDENTIAL_PREFIX` in the MCP client configuration (`env`), for example `Plant/Line1/`, or `*` to allow every
generic credential target. Host and worker read it once at start (the worker inherits the host's environment); no tool
call can change it, and an invalid value (blanks at either end, control characters, a `*` inside a prefix, more than
128 characters) refuses startup. The active prefix is printed at startup (`TIA MCP credential prefix: ...`) and
reported by the doctor check `password-sources`. Password values are still never accepted as tool arguments; `*` only
widens which credential *names* a caller may give.

## Storing a password once

The user types the password into his own console; the assistant never sees or handles it:

```cmd
cmdkey /generic:"TiaMcp/MCP_Safety_01/PLC_F1/Safety" /user:tia /pass
```

`cmdkey` prompts for the password without echo. The credential belongs to the Windows user the MCP server runs as.
Update it with the same command; remove it with `cmdkey /delete:"TiaMcp/MCP_Safety_01/PLC_F1/Safety"`.

## Password map file (optional)

`%APPDATA%\TiaMcpServer\passwords.json`, or the path in `TIA_MCP_PASSWORD_MAP`, is read once at startup
(`PasswordMapFile`, `PasswordMapStore`). It maps a PLC and a purpose to a source, so calls need not name it:

```json
{ "version": 1, "entries": [
  { "project": "MCP_Safety_01", "plc": "PLC_F1", "purpose": "safety", "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" },
  { "plc": "PLC_1", "purpose": "ModuleWriteAccessPassword", "env": "PLC1_WRITE_PW" } ] }
```

- `purpose`: `safety` (the three fail-safe password operations), an access level name (`FullAccessIncludingFailsafe`,
  `FullAccess`, `ReadAccess`, `HMIAccess`: `set_plc_access_level` only, for levels more open than the requested one),
  `PlcMasterSecretPassword` (the confidential-data operations and the download configuration of that name) or a
  password configuration type short name (`ModuleWriteAccessPassword`, `ModuleReadAccessPassword`,
  `OnlineReadAccessPassword`, `PasswordReadAccess`, ...; never `TlsVerificationConfiguration`).
- `project` (the bound project's file name without extension) and `plc` (as the call names it) are optional and
  case-insensitive; an omitted member and `"*"` both mean "every". The most specific entry wins
  (`PasswordMapFile.Specificity`): project and PLC, then project (every PLC of it), then PLC (that PLC name in every
  project), then the default for every project and PLC. A named project outranks a named PLC because PLC names such as
  `PLC_1` repeat across projects.
- Exactly one of `env` or `credential` per entry; no repeated project/plc/purpose.
- A source the call gives itself always wins; for downloads, online and upload the map only adds configuration types the
  call answered neither with `passwords` nor with `decisions`. Map entries for downloads and `go_online` match the
  `plcName` argument as given (an omitted name matches entries without `plc`); station upload uses entries without `plc`.
- The schema is closed. Any unknown member refuses the file, and a member whose name looks like a value field
  (containing password, pass, pwd, secret, value, token, pin, blob or key) refuses it with a message saying the map holds
  names only. An invalid map stops the server at startup; the message never repeats a value from the file.

## Password manager (Windows GUI)

`TiaMcpServer.PasswordManager` ("TiaMcp Passwortverwaltung", WinForms, `net10.0-windows`, German UI) ships next to the
server in `password-manager\TiaMcpPasswortverwaltung.exe`; `tia-mcp passwords [--prefix <prefix>] [--map <path>] [--settings <path>]`
starts it (`PasswordManagerCommand`). The user types each password into its masked fields; nothing passes through a
chat, an argument or a file.

- It lists, adds, changes and deletes map entries: purpose (access level, confidential configuration data, safety,
  module/online password configurations, or another configuration type short name) and scope (every project or one
  project, every PLC or one PLC).
- A save writes the secret with `CredWriteW` (`CRED_TYPE_GENERIC`, `CRED_PERSIST_LOCAL_MACHINE`, UTF-16LE as
  `cmdkey` stores it; the unmanaged copy is zeroed) under `<prefix><project or "Alle Projekte">/<plc or "Alle PLCs">/<purpose>`
  and only then the entry (names only, wildcards as `"*"`) to the map, through a temporary file. A changed scope writes a
  new target and offers to delete the old one; deleting an entry offers to delete its credential when no other entry uses it.
- "Test" checks that the credential exists for this Windows user and matches the server's prefix (`CredReadW`; the blob
  is zeroed unread, only its size is checked). Environment entries are checked in the program's own environment only.
- An invalid map is never overwritten. The server only reads credentials; only the password manager writes them.
- The logic (`PasswordMapEditor`, `PasswordPurposes`, `PasswordManagerOptions`, `ICredentialStore`) is in Contracts
  `Shared/PasswordManagement` and tested on Linux (`PasswordManagerTests`); the Win32 store and the UI are in the
  Windows-only project, which is not in `TiaMcpServer.slnx` and is restored, built and copied by `TiaMcpServer.csproj`
  on Windows only (`BuildPasswordManager`, `false` skips it).
- The server reads the map at startup: restart it after adding or removing entries. A changed password under the same
  target applies at once, because credentials are read at the time of the call.
- Its second tab, **Zugriffsmodus**, sets the server's access mode in the server settings file
  (`%APPDATA%\TiaMcpServer\settings.json` or `TIA_MCP_SETTINGS`, `--settings`); logic in `ServerSettingsEditor`
  (Contracts `Shared/PasswordManagement`, tested by `ServerSettingsTests`). See
  [Choosing the access mode in the password manager](installation.md#choosing-the-access-mode-in-the-password-manager).
