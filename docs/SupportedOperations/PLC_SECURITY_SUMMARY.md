# CPU protection (`plc_read` / `plc_write`)

**Language:** English · [Deutsch](PLC_SECURITY_SUMMARY.de.md)

Status: implemented on `feature/sim-security-profile`, extended to password-protected CPUs on 2026-11-01 (owner
request: password-protected projects must work fully), **not live-tested**. Offline tests cover validation,
the runtime binding against stand-in types and the FakeWorker path end to end.

## Why

A project created by `create_project` (CPU 1516-3 PN/DP 6ES7 516-3AN02-0AB0 V2.9) failed the hardware
compile of `plc_download` to PLCSIM with three errors (live, 2026-11-01): the access level
"Kein Zugriff (kompletter Schutz)" requires a password; "Vertrauliche PLC-Konfigurationsdaten schützen" is
on without a password; the PLC communication certificate cannot be used without that password. Setting
"Vollzugriff (kein Schutz)" and unticking the confidential-data protection in the UI fixed it. These
operations do the same through Openness; since 2026-11-01 they also protect a CPU (every access level, level
passwords, the confidential-data password) with passwords taken from password sources, never from tool arguments.

`compile_check` reported 0 errors for that project because it compiled only the PLC software (defect D9);
it now has `scope: "hardware"` (see [Hardware compile](#hardware-compile-compile_check-scope-hardware)).

## Operations

| Operation | Tool | Capability | Fields | What it does |
| --- | --- | --- | --- | --- |
| `get_plc_security` | `plc_read` | `Observe` (every mode) | optional `plcName` (omitted: every PLC) | Per CPU: `accessLevel`, `accessControl`, `confidentialDataProtection` (with `MasterSecretConfiguration` among `observedMembers`), the CPU's service type names |
| `set_plc_access_level` | `plc_write` | `ProjectMutation` | `accessLevel` (required), optional `plcName`, `accessLevelPasswords`, `resetAccessLevelPasswords` | `ResetPassword(level)` for each level to reset, then `PlcProtectionAccessLevel`, then `SetPassword(level, SecureString)` for each level password; a failed `SetPassword` restores the level read before. K48 (live V21 2026-11-02, F-CPU): TIA Portal accepts a level's password call only while the current level is less open than that level ("Cannot set password for the access level" for `SetPassword` before the level and for `ResetPassword(FullAccessIncludingFailsafe)` after it), and every level needs its own password ("This password is already being used for another access level"). The Siemens order is not documented; level then `SetPassword` is live-confirmed, reset first is not yet |
| `protect_plc_confidential_data` | `plc_write` | `ProjectMutation` | `passwordVariable` (or the map), optional `plcName`, `allConfiguration` | `Protect(SecureString)`; `allConfiguration:true`: `ProtectAllPlcConfigurationWithPassword(SecureString)`, or `ProtectAllPlcConfiguration()` without a password |
| `change_plc_confidential_data_password` | `plc_write` | `ProjectMutation` | `passwordVariable` (current), `newPasswordVariable`, optional `plcName` | `ChangePassword(SecureString, SecureString)` |
| `disable_plc_confidential_data_protection` | `plc_write` | `ProjectMutation` | optional `plcName`, `passwordVariable`, `allConfiguration` | `Unprotect(SecureString)` with a password, else `Unprotect()`; `allConfiguration:true`: `UnprotectAllPlcConfiguration()` (only the additional protection) |
| `compare_to_online` | `plc_read` | `OnlineObserve` | optional `passwords` (besides the compare fields) | answers online password configurations (`OnlineReadAccessPassword`) of a protected CPU from password sources and the map |

The CPU is the device item that hosts the PLC software (`PlcSoftwareLocator`, the same item `plc_download`
uses). A write with an omitted `plcName` addresses the only PLC; several PLCs fail `target_ambiguous`.

### Passwords

Every password is a **password source**, never a value ([password sources](../guides/password-sources.md)): an
environment variable NAME (`"NAME"` or `{ "env": "NAME" }`) or a Windows Credential Manager generic credential
(`{ "credential": "TiaMcp/..." }`, prefix configurable by the server owner). A value that is neither is redacted while
the request is read and refused without being echoed. The worker resolves every source of an item into a read-only
`SecureString` **before** the first Siemens call (an unusable source fails with nothing changed: `validation_error`,
or `unsupported_capability` for a credential off Windows) and disposes it after the call. Results and the audit carry
only the tokens (`result.passwordSources`: purpose, source, kind).

The server password map (`%APPDATA%\TiaMcpServer\passwords.json`, names only) fills what a call omits:

| Purpose | Used by |
| --- | --- |
| `FullAccessIncludingFailsafe`, `FullAccess`, `ReadAccess`, `HMIAccess` | `set_plc_access_level`: every level more open than `accessLevel` (only these purposes; never sent to downloads) |
| `PlcMasterSecretPassword` | `protect_plc_confidential_data`, `change_plc_confidential_data_password` (current), `disable_plc_confidential_data_protection` (unless `allConfiguration`); also the download configuration of the same name |
| `ModuleWriteAccessPassword`, `ModuleReadAccessPassword` (download, upload), `OnlineReadAccessPassword` (go_online, compare_to_online), `PasswordReadAccess` (upload) | the password configurations TIA Portal raises, by type short name |
| `safety` | the fail-safe `safety_login`, `set_safety_password`, `revoke_safety_password` |

### Access levels

`accessLevel` is any level of the TIA Portal protection dialog: `FullAccessIncludingFailsafe` ("Vollzugriff inkl.
Fehlersicherheit (kein Schutz)", F-CPUs), `FullAccess` ("Vollzugriff (kein Schutz)"), `ReadAccess` ("Lesezugriff"),
`HMIAccess` ("HMI-Zugriff"), `NoAccess` ("Kein Zugriff (kompletter Schutz)"). `None` is the enum's unset value and is
never set. Member names are case-sensitive.

A protected level needs passwords for the more open levels: in TIA Portal at least `FullAccess` (and
`FullAccessIncludingFailsafe` on an F-CPU); `ReadAccess` and `HMIAccess` passwords are optional there. The server does
not refuse a protected level without them (TIA Portal decides; the hardware compile reports a missing one); the item
warns when no `FullAccess` password was set in the call. `accessLevelPasswords` keys and `resetAccessLevelPasswords`
entries are the four password levels (`NoAccess` takes none); a level may not be set and reset in one item.

For an F-CPU the password-free recipe uses `FullAccessIncludingFailsafe`: on an F-CPU `FullAccess` is not the top level,
and TIA Portal keeps asking for a password for the fail-safe level (loading and changing the safety program in the CPU).
This follows the TIA Portal protection dialog and is not live-verified (step 11a of the
[F-CPU live test plan](../development/plcsim-fcpu-live-test-plan.md)). The safety offline program password
([PLC safety reference](PLC_SAFETY_SUMMARY.md)) is a separate protection and is not affected.

### Results

`get_plc_security` returns `{ plcs: [{ plcName, deviceName, services, servicesTruncated, accessLevel,
accessControl, confidentialDataProtection }] }`.

- `accessLevel` / `accessControl`: `{ service, property, serviceOffered, serviceType, readable, value,
  writable, availableValues, note }`. Reading `PlcProtectionAccessLevel` throws `NullReferenceException`
  when no password is configured (Siemens-documented gotcha); that read is reported as `readable:false`
  with the documented note, never as a value. Other recoverable Openness getter errors are `readable:false`
  with their type and message; session loss propagates. A service that `GetServiceInfos()` lists but
  `GetService` does not return is `serviceOffered:true`, `readable:false` with the note "GetServiceInfos() lists
  '…', but GetService returned null for it; this part is unavailable through Openness here." and the rest of the read
  goes on: live on V21 on 2026-11-02 (defect D14, [live report](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)) `get_plc_security plcName PLC_1` on an S7-1500
  failed because `PlcAccessControlConfigurationProvider` was listed and `GetService` returned null; since the fix the
  access level and the confidential-data protection are still reported. A write that needs such a service fails
  `unsupported_capability` before any change.
- `confidentialDataProtection`: `{ service, serviceOffered, serviceType, protectBindable,
  unprotectBindable, unprotectCandidates, observedMembers, note }`. `MasterSecretConfiguration` ("the PLC protection
  type", values undocumented) is listed with the other readable scalar properties (at most 20) in `observedMembers`,
  as found and not interpreted.

The writes return `{ operation, plcName, deviceName, setting, serviceType, member, requested, before,
beforeReadable, after, afterReadable, readBack, observedBefore, observedAfter, calls, passwordSources, note }`.

- `set_plc_access_level` reads the level before and after where readable. A readable read-back that differs fails the
  item `postcondition_failed`; an unreadable one (the `NullReferenceException` above, expected while no level
  password exists) is `readBack:"unreadable"` with a warning to confirm with `compile_check` scope `hardware`.
  `calls` lists every member called with its level, in order.
- The confidential-data operations have `readBack:"notAvailable"` and report `observedBefore`/`observedAfter`.

`set_plc_access_level` to an unprotected level, a level password reset and `disable_plc_confidential_data_protection`
fire the info guard `plc_lowers_cpu_protection`; a protected level, `protect_plc_confidential_data` and
`change_plc_confidential_data_password` fire `plc_raises_cpu_protection`. A `set_plc_access_level` with
`readBack:"matched"` is verified by that read-back (check `readBack`, expected the requested and actual the read-back
level; defect K24, 2026-11-02: the verification said "nothing was read back" next to `readBack:"matched"`); an
unreadable level and the confidential-data operations stay `trusted`. Each setting is its own object key.

### Failures

| Category | When |
| --- | --- |
| `validation_error` | `None` or an unknown level, a password field on another operation, a level without password in `accessLevelPasswords`/`resetAccessLevelPasswords`, a repeated or both set and reset level, a missing password for protect/change, a redacted or unset password source |
| `unsupported_capability` | the CPU's `GetServiceInfos()` does not list the service, or lists it while `GetService` returns null for it (D14); a credential source off Windows. Nothing was changed |
| `target_not_found` / `target_ambiguous` | the PLC selection |
| `api_member_missing` | the service is listed but a member is absent or has another signature (the message lists the signatures found), a level the live enum lacks, or two Siemens types with the configurator's short name. Nothing was changed |
| `engineering_rejected` | TIA Portal rejected a call (`EngineeringTargetInvocationException` in a mutation method), for example a password its policy refuses or a wrong current password |
| `postcondition_failed` | the level read back differs from the requested one |

## Recipe: simulation project without password

```json
plc_write {
  "operations": [
    { "operationId": "access", "operation": "set_plc_access_level", "plcName": "PLC_1", "accessLevel": "FullAccess" },
    { "operationId": "confidential", "operation": "disable_plc_confidential_data_protection", "plcName": "PLC_1" }
  ]
}
```

Then `compile_check { "scope": "hardware" }`, then `plc_download`. When the second item fails
`api_member_missing`, untick "Vertrauliche PLC-Konfigurationsdaten schützen" under "Schutz & Security" in
the CPU properties by hand. Meant for PLCSIM test projects; a real CPU is protected with `set_plc_access_level` and `protect_plc_confidential_data` from password sources (below).

## Hardware compile (`compile_check` scope `hardware`)

`compile_check` takes `scope`: `software` (default, unchanged: `PlcSoftware.GetService<ICompilable>()`, or
one block with `blockPath`; report `scope` `plc`/`block`) or `hardware` (`GetService<ICompilable>()` on
the CPU device item of every PLC, or the PLCs `plcName` names; report `scope` `hardware`). `hardware` with
`blockPath` and any other value are `validation_error` before any worker call. The program compile cannot
show hardware errors; run both before a download.

## API member provenance

Every Siemens member the protection operations use, with its evidence and how it is bound. "Siemens skill" is
`openness_development/skills/security/SKILL.md` of the Siemens TIA Portal AI extensions repository; "V21 manual" is the
member list of "TIA Portal Openness V21 - Functions on Devices (03/2026)" as extracted in totally-integrated-claude
`skills/tia-devices-general/references/device-attributes.md`. No stub member was added; no protection type is compiled
against.

| Member | Evidence | Binding |
| --- | --- | --- |
| `IEngineeringServiceProvider.GetServiceInfos()` → `IList<EngineeringServiceInfo>`, `EngineeringServiceInfo.Type` | Siemens engineering-objects skill (already used for technology objects) | reflection (`TechnologyObjectRuntime.ServiceTypes`) |
| `System.IServiceProvider.GetService(Type)` on the CPU device item | Siemens engineering-objects skill | compile-bound .NET interface, reached through `TechnologyObjectRuntime.Service` |
| `Siemens.Engineering.HW.Features.PlcAccessLevelProvider` (service of the CPU `DeviceItem`) | Siemens skill; V21 manual | reflection: exact full name among the listed services |
| `PlcAccessLevelProvider.PlcProtectionAccessLevel` (get/set) | Siemens skill, incl. the `NullReferenceException` gotcha; V21 manual ("To set the protection access level type") | reflection: one public non-indexed property of that name whose type is an enum named `PlcProtectionAccessLevel`; public setter required |
| `PlcAccessLevelProvider.SetPassword(PlcProtectionAccessLevel, SecureString)` | V21 manual ("set the password for the specific Access Level Type") | reflection: exactly one public `void SetPassword` with an enum named `PlcProtectionAccessLevel` and a `System.Security.SecureString` |
| `PlcAccessLevelProvider.ResetPassword(PlcProtectionAccessLevel)` | V21 manual ("Reset the password for the specific Access Level Type") | reflection: exactly one public `void ResetPassword` with that enum |
| `PlcProtectionAccessLevel` members `None`, `FullAccess`, `ReadAccess`, `HMIAccess`, `NoAccess`, `FullAccessIncludingFailsafe` (`Siemens.Engineering.HW` per the V21 type baseline) | Siemens skill | reflection: `Enum.Parse` on the live parameter/property type, exact case-sensitive name |
| `Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider.Configuration` (get) | Siemens skill | reflection, read only |
| `PlcMasterSecretConfigurator` (service of the CPU, `Siemens.Engineering.HW.Features` per the V21 type baseline) | Siemens skill; V21 manual | reflection: exact short name, namespace starting with `Siemens.Engineering`, unique |
| `PlcMasterSecretConfigurator.Protect(SecureString)` | Siemens skill; V21 manual | reflection: exactly one public `void Protect(SecureString)` |
| `ProtectAllPlcConfiguration()`, `ProtectAllPlcConfigurationWithPassword(SecureString)`, `UnprotectAllPlcConfiguration()` | V21 manual | reflection: exact name and parameter list, `void` |
| `Unprotect()`, `Unprotect(SecureString)` | V21 manual (until 2026-11-01 only a secondary source was known for `Unprotect()`) | reflection: exact name and parameter list, `void` |
| `ChangePassword(SecureString, SecureString)` | V21 manual | reflection: exact name and parameter list, `void` |
| `MasterSecretConfiguration` (get) | V21 manual ("Indicates the PLC protection type"; type undocumented) | reflection: read among `observedMembers`, uninterpreted |
| `ICompilable.Compile()` via `DeviceItem.GetService<ICompilable>()` (hardware compile) | Siemens blocks skill; used live by the IO-system qualification probe (2026-09-26 report) | compile-bound (existing stub `ICompilable`, `CompilerResult`) |
| Download/online/upload password configurations `ModuleReadAccessPassword`, `ModuleWriteAccessPassword`, `PlcMasterSecretPassword` (download), `OnlineReadAccessPassword` (online), `ModuleReadAccessPassword`, `ModuleWriteAccessPassword`, `PasswordReadAccess` (upload) | V21 type baseline (totally-integrated-claude `api-baselines/v21/base.json`) | answered by type short name through the shared callback recorders (`SetPassword(SecureString)`) |

The Siemens-free binding is `PlcSecurityReflection` and the orchestration `PlcSecurityRun`
(`TiaMcpServer.Contracts/Plc/Security`), shared by the worker (`PlcSecurityService`) and the FakeWorker
scenarios `plc-security` and `plc-security-no-unprotect`.

## Left out

- `PlcMasterSecretConfigurator.Reset()` ("Removes PLC configuration data from the TIA Portal project and the PLC"):
  destructive beyond protection and not requested; do it in TIA Portal.
- Interpreting `MasterSecretConfiguration`: its values are not documented; it is reported as found.
- Changing `PlcAccessControlConfigurationProvider.Configuration` (UMAC / access control): read only here.
- UMAC roles, project users and engineering function rights (`UmacConfigurator`).
- A `create_project` option that applies the recipe automatically.
- A `hardware_and_software` compile scope through `Device.GetService<ICompilable>()` (documented by the
  Siemens blocks skill, not used live in this repository); run `software` and `hardware` instead.
- Live verification of every member above.
