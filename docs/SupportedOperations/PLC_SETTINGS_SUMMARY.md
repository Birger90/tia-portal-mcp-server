# CPU and project settings, users, certificates, safety documents (`plc_read` / `plc_write`)

**Language:** English · [Deutsch](PLC_SETTINGS_SUMMARY.de.md)

Status: implemented for GitHub issues #15, #13, #12, #14, #21 and #25, **not live-tested**. Offline tests cover validation, the runtime binding against
stand-in types and the FakeWorker path end to end (scenario `plc-settings`).

## Common rules

- **Evidence.** Every member comes from the V21 IntelliSense files (`Siemens.Engineering.Base.xml`, `.Step7.xml`,
  `.Safety.xml`, `.SafetyValidation.xml`, dated 29.10.2025) and the V21 manual "TIA Portal Openness: API for automation
  of engineering workflows" (sections named per operation). No compile stub was added: services are found among the
  owner's `GetServiceInfos()` by exact full name and retrieved with `GetService(Type)`, and when that returns null with the
  documented generic `GetService<T>()` closed over the listed type (`ListedServices.Retrieve`, K76: live 2026-11-03 nine
  CPU services such as `SysLogConfigurationManager`, `SystemWebPagesFeature` and `CertificateManagementConfiguration`
  were listed but `GetService(Type)` returned null; whether the generic path returns them is not live-tested). A service
  listed but not returned by one owner does not stop the search: a later owner (the CPU's device items) that returns it
  is used, and the "unavailable" reason names both paths tried; every property and method is
  bound by exact name (and parameter types) on the live object by the Siemens-free `PlcSettingsRun` (Contracts,
  shared by the worker and the FakeWorker). A member that is missing is `api_member_missing`, a service that is not
  listed (or listed but not returned) `unsupported_capability`, and nothing is changed.
- **Owners.** A CPU service is looked up on the CPU device item that hosts the PLC software (the item `plc_download`
  uses), then on the PLC software; display and submodule services on the CPU, then on its device items depth first.
  Project-wide operations take no `plcName` and need a standalone project (`unsupported_capability` otherwise).
- **Settings.** Writes that set documented properties take `settings`, an object of property name -> value (string,
  number or boolean). Only the property names listed per operation are accepted (`validation_error` before any worker
  call). Values are converted to the live property type (enum members by exact name, `[Flags]` enums as `"A, B"`,
  ISO 8601 dates, range-checked integers), every property is bound and every value converted before the first change,
  the properties are applied in the documented order, and each is read before and after. A readable after value that
  differs from the request fails the item `postcondition_failed` (earlier changes of the item stay). The owner principle
  applies: the server does not check ranges TIA Portal checks; TIA Portal's rejection is the item's failure.
- **Passwords.** Only from password sources (`passwordVariable`: an environment variable NAME, `{ "env": "NAME" }` or
  `{ "credential": "TiaMcp/..." }`); a pasted value is redacted while the request is read and refused. The worker
  resolves the source into a read-only `SecureString` right before the call and disposes it; no password is generated,
  returned, logged or audited. Results name the source token only (`passwordSources`).
- **Guards.** A change that lowers protection fires the info guard `plc_lowers_cpu_protection` (the CPU protection
  guard), one that sets a password `plc_raises_cpu_protection`; the access mode is the human's control. Nothing changes
  the CPU itself: every write changes the offline project and takes effect after `plc_download`.
- **Verification.** `readBack` when the worker read every change back right after the call; `trusted` with an honest
  message when nothing comparable can be read (a password, a list of generated blocks); `file` for written files (their
  SHA-256 after placing them). A failed item's verification entry is never `success: true`.
- **Planning read only for the password policies (security review 2026-11-03, F1; not live-tested).**
  `set_account_locking`, `set_plc_password_policy`, `set_legacy_plc_password_policy` and `set_umac_password_policy` read
  the current values once (`get_cpu_settings` or `get_umac`) and compare each requested value with it: a lower minimum
  length, character count, reuse block, lock duration or retry interval, more failed logins allowed, a longer password
  validity, a protection switched off, a value that cannot be compared, or a current value that cannot be read fires
  `plc_lowers_cpu_protection`; any other write of these operations fires the info guard `plc_changes_password_policy`.
  The effect shows the current value next to the requested one. Every other operation has no planning read: the
  preview (`dryRun:true`) shows the requested values; read the current ones with the read operation of the issue.
- **Further protection guards (F8).** `set_syslog` with any `TransportProtocol` but `TLSServerAndClientAuthentication`
  (mutual TLS) fires `plc_lowers_cpu_protection`; `SysLogAutoAcceptClient=true` fires the acknowledge guard
  `plc_syslog_auto_accepts_certificates` (automatic certificate acceptance: read-write asks the human, full satisfies it
  by policy). `remove_certificate_supported_service` and `set_certificate_manager` fire `plc_lowers_cpu_protection`;
  `update_simple_webserver_user` fires it for every `Permissions` value but `None` (or empty), combined flags included.
  Nothing is refused: TIA Portal allows each of these values.

## Further CPU settings (issue #15)

| Operation | Tool | Fields | Siemens members (V21) |
| --- | --- | --- | --- |
| `get_cpu_settings` | `plc_read` (Observe, every mode) | optional `plcName` | Per CPU: sections `accountLocking`, `syslog` (with `SysLogServerConfiguration`), `syslogAttributes`, `systemWebPages`, `webApplications`, `watchAndForceTableAccess`, `displayWatchAndForceTableAccess`, `displayProtection`, `frontPanelDisplay`, `userDefinedWebPages`, `multilingualSupport`, `ipAccessibility`; project: `plcPasswordPolicy`, `legacyPlcPasswordPolicy`, `projectSyslogServers`. Each section says whether and where the service is offered, its documented properties (projected as text, uninterpreted, with `writable`), compositions and bindable methods |
| `set_account_locking` | `plc_write` | optional `plcName`, `settings`: `EnableLockUserAccountAtRunTime`, `NumberOfFailedLoginAttempts`, `LoginRetryInterval`, `LockAccountForDuration` | `HW.Features.PlcAccountLockingAtRuntimeFeature` (manual "Managing runtime account locking configuration": ranges 1-99, 1-30, 1-60 min; downloaded only with local user management active). `EnableLockUserAccountAtRunTime: false` lowers protection |
| `set_plc_password_policy` | `plc_write` (project) | `settings`: `PasswordPolicyEnabled` | `Security.PlcPasswordPolicyService` (S7-1200/1500; project service, manual "Setting password policy for PLC"). `false` lowers protection |
| `set_legacy_plc_password_policy` | `plc_write` (project) | `settings`: `PasswordPolicyEnabled`, `MinimumLength`, `MinimumNumericCharacterLength`, `MinimumSpecialCharacterLength`, `IncludesLowerCaseAndUpperCaseCharacters` | `Security.LegacyPlcPasswordPolicyService` (S7-300/400, ET 200S, WinAC; ranges 5-8 and 0-8, `PasswordPolicySettingsException` outside). `PasswordPolicyEnabled: false` and `IncludesLowerCaseAndUpperCaseCharacters: false` lower protection |
| `set_syslog` | `plc_write` | optional `plcName`, `settings`: `EnableSystemLogging`, `TransportProtocol` (`SysLogTransportProtocolType`), CPU attributes `SysLogAutoAcceptClient`, `SysLogClientCertificateId`, `SysLogTrustedCertificateIds` | `HW.Features.SysLogConfigurationManager` (S7-1500 FW 3.1+), attributes by `SetAttribute` on the CPU as the manual "Managing Syslog configuration" does. `EnableSystemLogging: false`, `TransportProtocol: UDP`/`None` and `SysLogAutoAcceptClient: true` (certificates accepted automatically at runtime) lower protection |
| `add_syslog_server` / `remove_syslog_server` | `plc_write` | `address`, `port` (add, 0-65535), optional `plcName` | `SysLogServerConfigurationComposition.Create(String, UInt16)`, `.Find(String)`, `SysLogServerConfiguration.Delete()`; read back by `Find` |
| `create_project_syslog_server` / `update_project_syslog_server` / `delete_project_syslog_server` | `plc_write` (project) | `name`, `settings`: `Address`, `Port`, `Tls`, `Comment` | `Security.SyslogServerProvider.Servers` (`SyslogServerComposition.Create(String)`, `.Find(String)`, `SyslogServer.Delete()`). `Tls: false` lowers protection. `AssignedModules` is read only (no documented assignment member) |
| `set_system_web_pages` | `plc_write` | optional `plcName`, `settings`: `EnableSystemWebPages` | `HW.Features.SystemWebPagesFeature` (S7-1500 FW 4.0+; the manual: TIA Portal throws when "Enable Web server via IP address of this interface" is off). `true` fires `plc_lowers_cpu_protection` (more exposed) |
| `set_default_web_application` | `plc_write` | `name`, optional `plcName` | `DefaultWebPagesFeature.WebApplicationConfigurations.Find(String).IsDefault = true` (TIA Portal throws "Web server has not been enabled." when it is off) |
| `set_watch_table_access` / `remove_watch_table_access`, `set_force_table_access` / `remove_force_table_access` | `plc_write` | `tableName`, optional `folderPath`, `access` (`Read`, `Write`; set), `display` (true: the display submodule), optional `plcName` | `HW.Features.WatchAndForceTableAccessManager` (`WatchtableAccessRules`, `ForcetableAccessRules`; `Find(table)`, `Create(table, WatchAndForceTableAccess)`, rule `Access { set; }`, `Delete()`). An existing rule's `Access` is set, a missing one created. `None` is refused by TIA Portal (manual), so it is not offered. Watch table `Write` and every force table rule lower protection |
| `set_display_password` | `plc_write` | `passwordVariable` (a password source), optional `plcName` | `HW.Features.DisplayProtection.SetPassword(SecureString)` on the display submodule (manual "Setting a display password"; software controllers have no display protection). Not readable: verification `trusted`. Fires `plc_raises_cpu_protection`. It fixes the hardware compile warning "Das Display der S7-1500 CPU enthält keinen Passwort-Schutz" |
| `set_front_panel_display` | `plc_write` | optional `plcName`, `filePath` (the logo), `settings`: `UserDefinedLogoActivated`, `AdaptLogoActivated` | `HW.Features.FrontPanelDisplay.SetUserDefinedLogo(FileInfo)` and its two properties |
| `set_plc_project_language` | `plc_write` | `position` (1-based row), `culture` (an active project language), optional `plcName` | CPU attribute `MultilingualSupport` (`TableData.Rows`), row `SetAttribute("ProjectLanguage", Language.Culture)` (manual "Assigning project language to PLC"); read back by `GetAttribute` |
| `set_ip_accessibility` | `plc_write` | `moduleName` (a device item of the CPU's device), optional `plcName` | CPU attribute `PlcAccessCommunicationModule` set to the CP object (manual "Supporting IP Accessibility"); read back by identity. Fires `plc_lowers_cpu_protection` |
| `generate_web_blocks` | `plc_write` | optional `plcName`, `directoryPath` (HTML directory) with `filePath` (default page) and `name` (application), `overwrite` | `HW.Features.WebserverUserDefinedPages.GenerateBlocks(WebDBGenerateOptions)` or `GenerateBlocks(DirectoryInfo, FileInfo, String, WebDBGenerateOptions)`; `overwrite:true` = `Override`. The result lists the returned blocks; verification `trusted`. Info guard `plc_generates_web_blocks` |

Left out (issue #15):

- **Web application `ApplicationType` and `Name`** are read only in the manual; only `IsDefault` is written.
- **Removing the IP accessibility module** (setting `PlcAccessCommunicationModule` to null) is not documented and not offered.
- **Syslog server assignment to modules** (`SyslogServer.AssignedModules`): the XML documents only the getter, no member assigns a module.
- **Display languages** (`DisplayLanguage` of a `MultilingualSupport` row) are read only in the manual.

## Web server and OPC UA users (issue #13)

Read with `get_cpu_settings`: sections `webserverUsers` (`HW.Features.WebserverUserManagement.WebserverUsers`: `UserName`,
`Permissions`), `simpleWebserverUsers` (`SimpleWebserverUserManagement`: `UserName`, `Active`, `Permissions`) and
`opcUaUsers` (`OpcUaUserManagement` of the OPC UA submodule: `UserName`). Passwords are never readable. The manual
("Accessing web server and OPC UA user management"): add, delete and password changes need an activated web server, or an
activated OPC UA server with user name and password authentication; TIA Portal throws otherwise (the item's failure). A
password that violates the password policy is TIA Portal's rejection too. Changes take effect after a download.

| Operation | Fields | Siemens members (V21) |
| --- | --- | --- |
| `create_webserver_user` | `userName`, `permissions` (member names of `WebserverUserPermissions`, combined), `passwordVariable`, optional `plcName` | `WebserverUserComposition.Create(String, WebserverUserPermissions, SecureString)`; read back by `Find` and `Permissions`. Fires `plc_lowers_cpu_protection` (a new user with access) |
| `update_webserver_user` | `userName`, `permissions` and/or `passwordVariable`, optional `plcName` | `WebserverUser.Permissions { set; }` (read back) and `SetPassword(SecureString)`. New permissions fire `plc_lowers_cpu_protection`, a new password `plc_raises_cpu_protection` |
| `delete_webserver_user` | `userName`, optional `plcName` | `WebserverUser.Delete()`; read back by `Find` |
| `update_simple_webserver_user` | `userName`, `settings`: `Active`, `Permissions` and/or `passwordVariable`, optional `plcName` | `SimpleWebserverUser.Active`, `.Permissions`, `.SetPassword(SecureString)`. `Active: true` and `Permissions: ReadWrite` lower protection |
| `create_opcua_user` | `userName`, `passwordVariable`, optional `plcName` | `OpcUaUserComposition.Create(String, SecureString)` of the OPC UA submodule's service (the CPU and its device items are searched); read back by `Find`. Fires `plc_lowers_cpu_protection` |
| `set_opcua_user_password` | `userName`, `passwordVariable`, optional `plcName` | `OpcUaUser.SetPassword(SecureString)`; verification `trusted` (not readable) |
| `delete_opcua_user` | `userName`, optional `plcName` | `OpcUaUser.Delete()` |

Left out (issue #13):

- **Creating or deleting simple web server users:** `SimpleWebserverUserComposition` documents only `Find`, no `Create`;
  `SimpleWebserverUser` documents no `Delete`.
- **Whether `WebserverUserPermissions` is a flags enum** is not documented; several permissions are combined only when the
  live enum is one (otherwise one member is accepted, the rest is a `validation_error`).

## UMAC: roles, project users, function rights, project protection (issue #12)

`UmacConfigurator` is taken from the Project's `GetServiceInfos()` (the Siemens security skill: `Project.GetService<UmacConfigurator>()`),
so the UMAC operations need a standalone project. `UmacDevice` is looked up on the CPU device item, then on its device (the skill:
`device.GetService<UmacDevice>()`, the type of `device` is not named). Every UMAC write fires the info guard
`plc_changes_project_access`: engineering function rights decide who may open and change the project, and the manual
("Modifying project in UMAC") says that in a protected project Openness may change data only with the function right
"Modify project via Openness API" (otherwise only reads). Do not remove the rights this server's session needs.

| Operation | Fields | Siemens members (V21) |
| --- | --- | --- |
| `get_umac` | `plc_read`, optional `plcName` | Project section `umac`: `EngineeringFunctionRights` (not empty = the project is protected, Siemens skill), `CustomRoles` (with `Comment`, `SessionTimeOut`, `AssignedEngineeringRights`), `SystemRoles`, `ProjectUsers` (`AliasName`, `Comment`, `IsActive`, `IsRuntimeSessionTimeoutActive`, `SessionTimeOut`, `ProjectUserAuthenticationType`, `Roles`), `UmcUsers`, `UmcUserGroups`, `CustomDeviceFunctionRights`, `AnonymousUser`; section `umacPasswordPolicy` (`PasswordPolicyConfigurator`); per PLC `umacDevice` with `AvailableDeviceFunctionRights` and every custom and system role's assigned rights (`GetAssignedDeviceFunctionRights(UmacDevice)`, `GetAssignedSystemDeviceFunctionRights(UmacDevice)`) |
| `create_custom_role` / `update_custom_role` / `delete_custom_role` | `name`, `settings`: `Comment`, `SessionTimeOut`; update: and/or `newName` | `CustomRoleComposition.Create(String, String)` (the comment), `CustomRole.SetName(String)`, `.Delete()`, properties read back |
| `assign_engineering_right` / `unassign_engineering_right` | `roleName`, `rightName` | `EngineeringFunctionRights.Find(String)`, `CustomRole.AssignedEngineeringRights.Add/Remove(EngineeringFunctionRight)`; read back. Assigning fires `plc_lowers_cpu_protection` |
| `assign_device_function_right` / `unassign_device_function_right` | `roleName`, `rightName` (name, else Openness identifier), optional `plcName` | `CustomRole.AssignDeviceFunctionRight(UmacDevice, DeviceFunctionRight)` / `UnAssignDeviceFunctionRight`, read back with `GetAssignedDeviceFunctionRights`; effective on the device after a download of the user management data |
| `create_project_user` | `userName`, `passwordVariable`, optional `settings`: `Comment`, `SessionTimeOut` | `ProjectUserComposition.Create(String, SecureString)` (the skill's call); read back by `Find`. Fires `plc_raises_cpu_protection` |
| `update_project_user` | `userName`, any of `settings`, `newName`, `aliasName`, `active`, `runtimeSessionTimeout`, `passwordVariable` | `ProjectUser.SetAliasName`, `Activate()`/`Deactivate()`, `ActivateRuntimeSessionTimeout()`/`DeactivateRuntimeSessionTimeout()`, `SetPassword(SecureString)`, `SetName`; states read back (`AliasName`, `IsActive`, `IsRuntimeSessionTimeoutActive`) |
| `delete_project_user` | `userName` | `ProjectUser.Delete()` |
| `assign_user_role` / `unassign_user_role` | `userName`, `roleName` (custom, else system role), `userKind` (`projectUser` default, `umcUser`, `umcUserGroup`) | `User.Roles` / `UmcUserGroup.Roles` (`RoleAssociation.Add/Remove(Role)`); read back |
| `set_anonymous_user` | `active` | `UmacConfigurator.ActivateAnonymousUser()` / `DeactivateAnonymousUser()`; `AnonymousUser.IsActive` read back when the live object has it. Activating fires `plc_lowers_cpu_protection` |
| `create_custom_device_function_right` / `update_custom_device_function_right` / `delete_custom_device_function_right` | `name`, `group` (create), `settings`: `Comment`, `newName` (update) | `CustomDeviceFunctionRightComposition.Create(String, String, String)` (name, group, comment), `SetName`, `Comment`, `Delete()` |
| `update_umc_user` / `delete_umc_user` | `userName`, `userKind` (`umcUser` default, `umcUserGroup`), update: `active` and/or `newName` | `UmcUser`/`UmcUserGroup` `Activate()`, `Deactivate()`, `SetName(String)`, `Delete()` |
| `set_umac_password_policy` | `settings`: `MinimumLength`, `MinimumNumericCharacterLength`, `MinimumSpecialCharacterLength`, `IncludesLowerCaseAndUpperCaseCharacters`, `EnablePasswordAging`, `PasswordValidity`, `PasswordValidityPrewarningTime`, `MinimumUserPasswordsBlockedForReuse` | `Umac.PasswordPolicyConfigurator` (found among the Project's services; where it hangs is not documented). `IncludesLowerCaseAndUpperCaseCharacters: false` and `EnablePasswordAging: false` lower protection |

Left out (issue #12):

- **Opening a UMAC-protected project with credentials** (`ProjectComposition.Open(FileInfo, UmacDelegate)`,
  `OpenWithUpgrade`/`Retrieve` overloads with `UmacDelegate`, `UmacCredentials.Name`/`.Type`/`.SetPassword`): these belong to
  the lifecycle tools (`open_project`, `retrieve_project`) and their binding/confirmation pipeline; they are not changed by this
  package. Today such a project is opened in the TIA Portal UI (with the user's login) and then bound with `bind_project`.
- **Online UMAC legitimation** (`OnlineAuthenticationConfiguration`, `OnlineCredentials.Name`/`.Type`/`.SetPassword`, manual
  "Supporting UMAC on PLC device"): the online callbacks of `plc_download`, `plc_tia_online` and `compare_to_online` answer only
  password configurations; answering a user name and user type is a change of those tools and is not made here.
- **User management download selection** (`UserManagementDownload` with `UserManagementPreDownloadSelections`, manual
  "Downloading User Management data") is a `plc_download` callback configuration, outside this family.
- **Importing UMC users and groups** from a UMC server (`UmcServerConfigurator`, `UmcCredentials`): the V21 XML lists the types
  but no documented import call was found; only already imported UMC users and groups are changed.
- **System roles** are read only (no documented write member).

## CPU certificates and secure communication (issue #14)

The manual "Managing certificate": the service `LocalCertificateManager` at the PLC device item holds the local store
`LocalCertificateStore`; certificates are created from templates (`GetCertificateTemplate(CertificateUsage)`), imported,
exported, deleted, and assigned through the dynamic attributes `WebserverCertificate` (PLC) and `OpcUaServerCertificate` (OPC UA
submodule; `null` unassigns). **Nothing is trusted automatically**: no operation answers a TLS question or adds a certificate to a
trust list. `get_cpu_certificates` reports, per certificate, the SHA-256 fingerprint over the DER bytes of a temporary
`Export(FileInfo)` (the file is deleted afterwards), the same hash the TLS policy of `plc_download`, `plc_tia_online` and
`plc_station_upload` lists for `trustedCertificates` (K27/K52/K53): the human compares them and decides.

| Operation | Tool | Fields | Siemens members (V21) |
| --- | --- | --- | --- |
| `get_cpu_certificates` | `plc_read` (TemporaryExport, every mode) | optional `plcName` | Sections `localCertificateManager` (`EnableGlobalCertificatesStore`; `Certificates`: `Id`, `SubjectCommonName`, `ValidUntil`, `HasPrivateKey`, `Sha256`, `Sha1Thumbprint`, `Subject`, `Issuer`, `NotBefore`, `NotAfter`, `FingerprintError`), `certificateManagement` (`Usage`, `CertificateExpirationEventActivated`, `RemainingCertificateLifetime`, `CertificateSupportedServices`: `Id`, `ServiceType`, `ServiceGroupName`, `ApplicationUri`, `Guid`), `certificateAssignments` (`WebserverCertificate`, `OpcUaServerCertificate` with Id and fingerprint) |
| `set_certificate_manager` | `plc_write` | optional `plcName`, `settings`: `EnableGlobalCertificatesStore` | `LocalCertificateManager.EnableGlobalCertificatesStore` |
| `create_cpu_certificate` | `plc_write` | `usage` (`CertificateUsage`), optional `settings`: `SubjectCommonName`, `Signature`, `ValidFrom`, `ValidUntil`, `subjectAlternativeNames` (`Type:Value`), `plcName` | `GetCertificateTemplate(CertificateUsage)`, template properties, `SubjectAlternativeNames.Create(SubjectAlternativeNameType, String)`, `Certificates.Create(CertificateTemplate)`; result: the new `Id` and SHA-256. `Signature: Sha1RSA` fires `plc_lowers_cpu_protection` |
| `import_cpu_certificate` | `plc_write` | `filePath`, optional `passwordVariable` (a protected file), `plcName` | `CertificateComposition.Import(FileInfo)` / `Import(FileInfo, SecureString)`; result: `Id` and SHA-256 of the imported certificate. Info guard `plc_imports_certificate` |
| `export_cpu_certificate` | `plc_write` | `certificateId`, `filePath`, optional `overwrite`, `plcName` | `Certificate.Export(FileInfo)` (the manual: any format by extension) under the export file rules (temporary file next to the target, no overwrite without `overwrite:true`, re-hashed; check `file`) |
| `delete_cpu_certificate` | `plc_write` | `certificateId`, optional `plcName` | `Certificate.Delete()`; read back. Fires `plc_lowers_cpu_protection` |
| `assign_cpu_certificate` / `unassign_cpu_certificate` | `plc_write` | `certificateRole` (`webserver`, `opcUaServer`), `certificateId` (assign), optional `plcName` | `SetAttribute("WebserverCertificate" / "OpcUaServerCertificate", certificate or null)` on the PLC or the first device item that answers the attribute; read back by identity. Unassigning fires `plc_lowers_cpu_protection` |
| `set_certificate_management` | `plc_write` | optional `plcName`, `settings`: `Usage` (`TIAPortal`, `Runtime`), `CertificateExpirationEventActivated`, `RemainingCertificateLifetime` (10-90 %) | `HW.Features.CertificateManagementConfiguration` (manual "Managing dynamic certificate settings"; TIA Portal throws for `Runtime` while web server and OPC UA server are off) |
| `add_certificate_supported_service` / `update_certificate_supported_service` / `remove_certificate_supported_service` | `plc_write` | `certificateId` (UInt32), `serviceType` and `name` (the service group name; add), `settings`: `ServiceGroupName` (update), optional `plcName` | `CertificateSupportedServiceComposition.Create(UInt32, String, CertificateSupportedServiceName)`, `.Find(UInt32)`, `ServiceGroupName { set; }`, `Delete()` |

Left out (issue #14):

- **Legacy (non-TLS) communication** (`ConnectionConfiguration.EnableLegacyCommunication`, manual "Supporting secure S7
  communication TLS"): it disables TLS for the connection; this server never sets it (a fixed rule of the online tools). The
  manual's other content of that section (the `OnlineLegitimation` handler, `TlsVerificationConfiguration` with `Trusted`,
  `NonTrusted`, `NonVerified`) is the existing TLS policy of the online tools, unchanged: trust only for listed fingerprints.
- **The global certificate store** (certificates shared by several devices): the V21 XML documents only the switch
  `EnableGlobalCertificatesStore`, no member to read or change the global store.
- **`OpcUaServerCertificateSettings`** (`OpcUaCertificatesAtRuntime`, `OpcUaCertificatesViaDownload`): an enum of a hardware
  attribute whose attribute name is not documented; set it with `network_write set_hardware_attributes` when the attribute is listed.
- **Dynamic certificates during a download** (`SelectiveDeleteDownload`, manual "Managing dynamic certificate settings"): a
  `plc_download` callback configuration, outside this family.

## Safety printout (issue #21)

**The MCP never replaces the safety acceptance.** A safety program must be validated and accepted by a qualified person (SIMATIC
Safety manual); the printout is a document for that acceptance. Generating it accepts nothing, and nothing in this family marks a
safety program, a test or a result as accepted or passed.

| Operation | Fields | Siemens members (V21) |
| --- | --- | --- |
| `generate_safety_printout` | `filePath`, `documentLayout` (e.g. `DocuInfo_ISO_A4_Portrait`, `DocuInfo_Simple_A4_Portrait_Small_Footer`), optional `printer` (`MicrosoftPrintToPdf` default, `MicrosoftXpsDocumentWriter`), `printOption` (`All` default, `Compact`), `plcName`, `overwrite` | `Safety.SafetyPrintout.Print(SafetyPrintoutFilePrinter, FileInfo, String, SafetyPrintoutOption)` of the F-CPU device item ("Creates a Safety Printout for the current device item and prints it to a file ... Returns true on success"; the service is documented for "Plus PLCs", S7-1500/ET 200SP). The printer driver writes a temporary file next to the target that is moved into place under the export file rules and re-hashed (check `file`); `false` fails `postcondition_failed` and places nothing. Info guard `plc_safety_printout_is_no_acceptance`; the result's warning repeats the acceptance notice |

Left out (issue #21): nothing documented. The layouts TIA Portal offers are not listed by Openness; `documentLayout` is passed
as given and TIA Portal decides. K79 (live 2026-11-03: "No Document description object "?" found in System Library or
Project"): a read operation listing the layouts was not built, because V21 documents no member for it (searched
`members.txt` for DocumentInfo, DocumentDescription, DocuInfo, Printout, Printing, print settings and the master copy
folders; only `DocumentInfoOptions` of library exports and `SafetyPrintout.Print` exist). Instead every failed
`generate_safety_printout` (except `validation_error` and `portal_lost`) ends with `PlcSafetyPrintoutNames.LayoutHint`: the
name is a TIA Portal document information object, found in TIA Portal under Documentation settings > Document information
("Dokumentationseinstellungen > Dokumentinformationen", next to the cover pages and frames, "Deckblätter"/"Rahmen") of the
project or of a global library such as the system library; the reference's names are examples. Not live-tested.

## Safety Validation Assistant: activation tests and test report (issue #25)

**The MCP never replaces the safety acceptance.** It reads, prepares (tests, safety functions, conditions) and generates reports;
it never records a test result. `TestState` and `OverallState` are never written (a `settings` key `TestState`, `OverallState` or
`State` is refused before any worker call), and the imports and copies that could carry executed results are not built (see
left out). Every change fires the acknowledge guard `plc_safety_acceptance_required` (the one of the F-PLC writes): read-write asks
once per call, full satisfies it by policy, the audit records which. Exports and reports fire the info guard
`plc_safety_printout_is_no_acceptance`. `SafetyValidationAssistant` is taken from the Project's `GetServiceInfos()` (the XML does
not say where it hangs; standalone projects only); `DeviceQuery`, `TestValidity` and `ActivationTestPrintout` from the
`GetServiceInfos()` of the assistant, the test, the function or the condition.

| Operation | Tool | Fields | Siemens members (V21) |
| --- | --- | --- | --- |
| `get_safety_validation` | `plc_read` | none | Section `safetyValidation`: `ActivationTests` (every test of the root and the groups: `Name`, `Author`, `EvaluationDeviceName`, `OverallState`, `Group` path; nested `SafetyFunctions`: `Name`, `TestName`, `Description`, `TestState`, `IsTraced`; nested `Conditions`: `DeviceName`, `SignalName`, `SignalUsage`, `InitialInput`, `ExecutedInput`, `Response`, `Comment`), `ActivationTestGroups` (paths), `EvaluationDevices` (`DeviceQuery.EvaluationDevices()`) |
| `check_activation_test` | `plc_read` (write capability, refused in read-only: whether `CheckValidity()` stores a state is undocumented; security review U1) | `testName`, optional `functionName`, `position` (a condition) | `TestValidity.CheckValidity()` -> `TestValidationResult` (`State`, `ErrorCount`, `Messages`): a consistency check of the test data, no test result |
| `create_activation_test` / `delete_activation_test` | `plc_write` | `name`, `evaluationDevice` (create; a name from `EvaluationDevices`), optional `folderPath` | `ActivationTestComposition.Create(String, DeviceItem)` of the root or the group, `ActivationTest.Delete()`; read back |
| `change_activation_test_evaluation_device` | `plc_write` | `name`, `evaluationDevice` | `ActivationTest.ChangeEvaluationDevice(DeviceItem)`; `EvaluationDeviceName` reported, not compared (its relation to the device item name is undocumented) |
| `create_activation_test_group` / `delete_activation_test_group` | `plc_write` | `folderPath` (e.g. `/Line1/Cell2`) | `ActivationTestUserGroupComposition.Create(String)` of the parent, `ActivationTestUserGroup.Delete()` |
| `create_safety_function` / `update_safety_function` / `delete_safety_function` | `plc_write` | `testName`, `functionName` (update, delete), `settings`: `Name`, `TestName`, `Description`, `traced` | `SafetyFunctionComposition.Create()`, the properties (bound and read back when the live object has setters), `TraceConfiguration.IsTraced`, `Delete()` |
| `reset_safety_function_test_result` | `plc_write` | `testName`, `functionName` | `SafetyFunction.ResetTestResult()`, `TestState` read back as `NotTested`: it removes a recorded result, the test has to be executed again by the qualified person; it never sets a result |
| `create_safety_function_condition` / `update_safety_function_condition` / `delete_safety_function_condition` | `plc_write` | `testName`, `functionName`, create: `signalDevice` (from `ActivationTest.AvailableDevices()`), `signalUsage` (`InputCondition`, `OperatingMode`, `Response`), `signalName`; update/delete: `position`; `settings`: `InitialInput`, `ExecutedInput`, `Response` (`ConditionValue`), `Comment` | `ConditionComposition.Create(DeviceItem, SignalUsage, String)`, condition properties, `Condition.Delete()` |
| `export_activation_test` | `plc_write` | `testName`, `filePath`, optional `overwrite` | `ActivationTest.Export(FileInfo, ExportOptions.None)` (SimaticML) under the export file rules, check `file` |
| `generate_activation_test_report` | `plc_write` | `testName`, `filePath`, optional `overwrite` | `ActivationTestPrintout.Generate(FileInfo)` of the test (else of the assistant) under the export file rules, check `file` |

Left out (issue #25), by the rule that the MCP never marks a test as passed on behalf of a person:

- **Imports and copies** (`ActivationTestComposition.Import(FileInfo, ActivationTestImportOptions)`, `CreateFrom(ActivationTest)`,
  `CreateFrom(MasterCopy)`, `ActivationTestUserGroupComposition.CreateFrom(MasterCopy)`, `SafetyFunctionComposition.Import`,
  `CreateFrom(SafetyFunction)`): a SimaticML file, a master copy or an existing test can carry executed test states, so the result
  would show tests as passed that nobody executed on this project. Use TIA Portal for them.
- **Setting `TestState`** (whether the live property has a setter or not) and anything else that records a result.
- **`SafetyFunction.Export`** (SimaticML of one function): left out with the imports it pairs with; `export_activation_test`
  exports the whole test.
- **`DocumentInfoOptions`** overloads of `Export`: the options are not documented beyond their type.
