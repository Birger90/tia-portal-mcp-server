# CPU- und Projekteinstellungen, Benutzer, Zertifikate, Safety-Unterlagen (`plc_read` / `plc_write`)

**Sprache:** [English](PLC_SETTINGS_SUMMARY.md) · Deutsch

Stand: umgesetzt für die GitHub-Issues #15, #13, #12, #14, #21 und #25, **nicht live getestet**. Offline-Tests decken die Prüfung, die Laufzeitbindung
an Ersatztypen und den FakeWorker-Weg Ende zu Ende ab (Szenario `plc-settings`).

## Gemeinsame Regeln

- **Belege.** Jeder Member stammt aus den V21-IntelliSense-Dateien (`Siemens.Engineering.Base.xml`, `.Step7.xml`,
  `.Safety.xml`, `.SafetyValidation.xml`, Stand 29.10.2025) und dem V21-Handbuch „TIA Portal Openness: API for automation
  of engineering workflows“ (Abschnitte je Operation genannt). Es wurde kein Compile-Stub ergänzt: Dienste werden unter
  `GetServiceInfos()` des Besitzers über den genauen vollständigen Namen gefunden und mit `GetService(Type)` geholt, und wenn
  das null liefert, mit dem dokumentierten generischen `GetService<T>()`, geschlossen über den gelisteten Typ
  (`ListedServices.Retrieve`, K76: live am 2026-11-03 waren neun CPU-Dienste wie `SysLogConfigurationManager`,
  `SystemWebPagesFeature` und `CertificateManagementConfiguration` gelistet, aber `GetService(Type)` lieferte null; ob der
  generische Weg sie liefert, ist nicht live getestet). Ein Dienst, den ein Besitzer listet, aber nicht liefert, beendet die
  Suche nicht: ein späterer Besitzer (die Geräteelemente der CPU), der ihn liefert, wird genommen, und der Grund
  „nicht verfügbar“ nennt beide versuchten Wege; jede
  Eigenschaft und Methode wird über den genauen Namen (und die Parametertypen) am Laufzeitobjekt gebunden, durch das
  Siemens-freie `PlcSettingsRun` (Contracts, von Worker und FakeWorker gemeinsam genutzt). Ein fehlender Member ist
  `api_member_missing`, ein nicht gelisteter (oder gelisteter, aber nicht gelieferter) Dienst `unsupported_capability`,
  und nichts wird geändert.
- **Besitzer.** Ein CPU-Dienst wird am CPU-Device-Item gesucht, das die PLC-Software trägt (dasselbe Item wie bei
  `plc_download`), danach an der PLC-Software; Display- und Submodul-Dienste an der CPU, danach an ihren Device-Items
  (Tiefensuche). Projektweite Operationen nehmen kein `plcName` und brauchen ein Einzelprojekt (sonst `unsupported_capability`).
- **Einstellungen.** Schreiboperationen, die dokumentierte Eigenschaften setzen, nehmen `settings`: ein Objekt
  Eigenschaftsname -> Wert (Zeichenkette, Zahl oder Wahrheitswert). Nur die je Operation genannten Namen sind erlaubt
  (`validation_error` vor jedem Worker-Aufruf). Die Werte werden in den Laufzeittyp der Eigenschaft umgewandelt
  (Enum-Member über den genauen Namen, `[Flags]`-Enums als `"A, B"`, Datum nach ISO 8601, Ganzzahlen mit Bereichsprüfung);
  jede Eigenschaft wird gebunden und jeder Wert umgewandelt, bevor sich etwas ändert; die Eigenschaften werden in der
  dokumentierten Reihenfolge gesetzt und jeweils davor und danach gelesen. Liest sich ein Wert danach anders als
  angefordert, scheitert der Eintrag mit `postcondition_failed` (frühere Änderungen des Eintrags bleiben). Es gilt der
  Grundsatz des Eigentümers: Der Server prüft keine Bereiche, die TIA Portal prüft; die Ablehnung durch TIA Portal ist
  der Fehler des Eintrags.
- **Passwörter.** Nur aus Passwortquellen (`passwordVariable`: der NAME einer Umgebungsvariablen, `{ "env": "NAME" }` oder
  `{ "credential": "TiaMcp/..." }`); ein eingefügter Wert wird schon beim Lesen der Anfrage geschwärzt und abgelehnt. Der
  Worker löst die Quelle erst direkt vor dem Aufruf in einen schreibgeschützten `SecureString` auf und verwirft ihn
  danach; kein Passwort wird erzeugt, zurückgegeben, protokolliert oder ins Audit geschrieben. Ergebnisse nennen nur
  die Quelle (`passwordSources`).
- **Guards.** Eine Änderung, die den Schutz senkt, löst den Info-Guard `plc_lowers_cpu_protection` aus (der Guard des
  CPU-Schutzes), eine, die ein Passwort setzt, `plc_raises_cpu_protection`; die Kontrolle des Menschen ist der
  Zugriffsmodus. Nichts ändert die CPU selbst: Jede Schreiboperation ändert das Offline-Projekt und wirkt nach `plc_download`.
- **Prüfung.** `readBack`, wenn der Worker jede Änderung direkt nach dem Aufruf zurückgelesen hat; `trusted` mit
  ehrlicher Meldung, wenn nichts Vergleichbares lesbar ist (ein Passwort, eine Liste erzeugter Bausteine); `file` für
  geschriebene Dateien (ihr SHA-256 nach dem Ablegen). Der Prüfeintrag eines gescheiterten Eintrags ist nie `success: true`.
- **Planungs-Lesen nur bei den Passwortrichtlinien (Sicherheits-Review 2026-11-03, F1; nicht live getestet).**
  `set_account_locking`, `set_plc_password_policy`, `set_legacy_plc_password_policy` und `set_umac_password_policy` lesen
  die aktuellen Werte einmal (`get_cpu_settings` oder `get_umac`) und vergleichen jeden angeforderten Wert damit: eine
  kleinere Mindestlänge, Zeichenzahl, Wiederverwendungssperre, Sperrdauer oder Wiederholungszeit, mehr erlaubte
  Fehlanmeldungen, eine längere Passwortgültigkeit, ein abgeschalteter Schutz, ein nicht vergleichbarer Wert oder ein
  nicht lesbarer aktueller Wert lösen `plc_lowers_cpu_protection` aus; jede andere Schreiboperation dieser Operationen
  den Info-Guard `plc_changes_password_policy`. Die Wirkung zeigt den aktuellen neben dem angeforderten Wert. Alle anderen
  Operationen lesen nicht vorab: Die Vorschau (`dryRun:true`) zeigt die angeforderten Werte; die aktuellen liefert die
  Leseoperation des jeweiligen Issues.
- **Weitere Schutz-Guards (F8).** `set_syslog` mit einem `TransportProtocol` außer `TLSServerAndClientAuthentication`
  (gegenseitiges TLS) löst `plc_lowers_cpu_protection` aus; `SysLogAutoAcceptClient=true` den Bestätigungs-Guard
  `plc_syslog_auto_accepts_certificates` (automatische Zertifikatsannahme: read-write fragt den Menschen, full erfüllt
  ihn per Richtlinie). `remove_certificate_supported_service` und `set_certificate_manager` lösen
  `plc_lowers_cpu_protection` aus; `update_simple_webserver_user` für jeden `Permissions`-Wert außer `None` (oder leer),
  kombinierte Flags eingeschlossen. Nichts wird verweigert: TIA Portal erlaubt jeden dieser Werte.

## Weitere CPU-Einstellungen (Issue #15)

| Operation | Werkzeug | Felder | Siemens-Member (V21) |
| --- | --- | --- | --- |
| `get_cpu_settings` | `plc_read` (Observe, jeder Modus) | optional `plcName` | Je CPU die Abschnitte `accountLocking`, `syslog` (mit `SysLogServerConfiguration`), `syslogAttributes`, `systemWebPages`, `webApplications`, `watchAndForceTableAccess`, `displayWatchAndForceTableAccess`, `displayProtection`, `frontPanelDisplay`, `userDefinedWebPages`, `multilingualSupport`, `ipAccessibility`; Projekt: `plcPasswordPolicy`, `legacyPlcPasswordPolicy`, `projectSyslogServers`. Jeder Abschnitt nennt, ob und wo der Dienst angeboten wird, seine dokumentierten Eigenschaften (als Text, ungedeutet, mit `writable`), Kompositionen und bindbaren Methoden |
| `set_account_locking` | `plc_write` | optional `plcName`, `settings`: `EnableLockUserAccountAtRunTime`, `NumberOfFailedLoginAttempts`, `LoginRetryInterval`, `LockAccountForDuration` | `HW.Features.PlcAccountLockingAtRuntimeFeature` (Handbuch „Managing runtime account locking configuration“: Bereiche 1-99, 1-30, 1-60 min; geladen nur bei aktiver lokaler Benutzerverwaltung). `EnableLockUserAccountAtRunTime: false` senkt den Schutz |
| `set_plc_password_policy` | `plc_write` (Projekt) | `settings`: `PasswordPolicyEnabled` | `Security.PlcPasswordPolicyService` (S7-1200/1500; Projektdienst, Handbuch „Setting password policy for PLC“). `false` senkt den Schutz |
| `set_legacy_plc_password_policy` | `plc_write` (Projekt) | `settings`: `PasswordPolicyEnabled`, `MinimumLength`, `MinimumNumericCharacterLength`, `MinimumSpecialCharacterLength`, `IncludesLowerCaseAndUpperCaseCharacters` | `Security.LegacyPlcPasswordPolicyService` (S7-300/400, ET 200S, WinAC; Bereiche 5-8 und 0-8, außerhalb `PasswordPolicySettingsException`). `PasswordPolicyEnabled: false` und `IncludesLowerCaseAndUpperCaseCharacters: false` senken den Schutz |
| `set_syslog` | `plc_write` | optional `plcName`, `settings`: `EnableSystemLogging`, `TransportProtocol` (`SysLogTransportProtocolType`), CPU-Attribute `SysLogAutoAcceptClient`, `SysLogClientCertificateId`, `SysLogTrustedCertificateIds` | `HW.Features.SysLogConfigurationManager` (S7-1500 ab FW 3.1), Attribute per `SetAttribute` an der CPU wie im Handbuch „Managing Syslog configuration“. `EnableSystemLogging: false`, `TransportProtocol: UDP`/`None` und `SysLogAutoAcceptClient: true` (Zertifikate zur Laufzeit automatisch angenommen) senken den Schutz |
| `add_syslog_server` / `remove_syslog_server` | `plc_write` | `address`, `port` (beim Hinzufügen, 0-65535), optional `plcName` | `SysLogServerConfigurationComposition.Create(String, UInt16)`, `.Find(String)`, `SysLogServerConfiguration.Delete()`; Rücklesen per `Find` |
| `create_project_syslog_server` / `update_project_syslog_server` / `delete_project_syslog_server` | `plc_write` (Projekt) | `name`, `settings`: `Address`, `Port`, `Tls`, `Comment` | `Security.SyslogServerProvider.Servers` (`SyslogServerComposition.Create(String)`, `.Find(String)`, `SyslogServer.Delete()`). `Tls: false` senkt den Schutz. `AssignedModules` nur lesbar (kein dokumentierter Member zum Zuordnen) |
| `set_system_web_pages` | `plc_write` | optional `plcName`, `settings`: `EnableSystemWebPages` | `HW.Features.SystemWebPagesFeature` (S7-1500 ab FW 4.0; laut Handbuch wirft TIA Portal, wenn „Webserver über IP-Adresse dieser Schnittstelle aktivieren“ aus ist). `true` löst `plc_lowers_cpu_protection` aus (mehr erreichbar) |
| `set_default_web_application` | `plc_write` | `name`, optional `plcName` | `DefaultWebPagesFeature.WebApplicationConfigurations.Find(String).IsDefault = true` (TIA Portal wirft „Web server has not been enabled.“, wenn er aus ist) |
| `set_watch_table_access` / `remove_watch_table_access`, `set_force_table_access` / `remove_force_table_access` | `plc_write` | `tableName`, optional `folderPath`, `access` (`Read`, `Write`; beim Setzen), `display` (true: das Display-Submodul), optional `plcName` | `HW.Features.WatchAndForceTableAccessManager` (`WatchtableAccessRules`, `ForcetableAccessRules`; `Find(table)`, `Create(table, WatchAndForceTableAccess)`, `Access { set; }` der Regel, `Delete()`). Bei vorhandener Regel wird `Access` gesetzt, sonst eine angelegt. `None` lehnt TIA Portal ab (Handbuch) und wird nicht angeboten. `Write` für Beobachtungstabellen und jede Forcetabellen-Regel senken den Schutz |
| `set_display_password` | `plc_write` | `passwordVariable` (eine Passwortquelle), optional `plcName` | `HW.Features.DisplayProtection.SetPassword(SecureString)` am Display-Submodul (Handbuch „Setting a display password“; Software-Controller haben keinen Display-Schutz). Nicht lesbar: Prüfung `trusted`. Löst `plc_raises_cpu_protection` aus. Behebt die Warnung beim Hardware-Übersetzen „Das Display der S7-1500 CPU enthält keinen Passwort-Schutz“ |
| `set_front_panel_display` | `plc_write` | optional `plcName`, `filePath` (das Logo), `settings`: `UserDefinedLogoActivated`, `AdaptLogoActivated` | `HW.Features.FrontPanelDisplay.SetUserDefinedLogo(FileInfo)` und seine zwei Eigenschaften |
| `set_plc_project_language` | `plc_write` | `position` (Zeile ab 1), `culture` (eine aktive Projektsprache), optional `plcName` | CPU-Attribut `MultilingualSupport` (`TableData.Rows`), Zeile `SetAttribute("ProjectLanguage", Language.Culture)` (Handbuch „Assigning project language to PLC“); Rücklesen per `GetAttribute` |
| `set_ip_accessibility` | `plc_write` | `moduleName` (ein Device-Item im Gerät der CPU), optional `plcName` | CPU-Attribut `PlcAccessCommunicationModule` auf das CP-Objekt gesetzt (Handbuch „Supporting IP Accessibility“); Rücklesen über die Identität. Löst `plc_lowers_cpu_protection` aus |
| `generate_web_blocks` | `plc_write` | optional `plcName`, `directoryPath` (HTML-Verzeichnis) mit `filePath` (Startseite) und `name` (Anwendung), `overwrite` | `HW.Features.WebserverUserDefinedPages.GenerateBlocks(WebDBGenerateOptions)` oder `GenerateBlocks(DirectoryInfo, FileInfo, String, WebDBGenerateOptions)`; `overwrite:true` = `Override`. Das Ergebnis listet die gelieferten Bausteine; Prüfung `trusted`. Info-Guard `plc_generates_web_blocks` |

Ausgelassen (Issue #15):

- **`ApplicationType` und `Name` einer Webanwendung** sind laut Handbuch nur lesbar; nur `IsDefault` wird geschrieben.
- **Das Entfernen des Moduls für die IP-Erreichbarkeit** (`PlcAccessCommunicationModule` auf null) ist nicht dokumentiert und wird nicht angeboten.
- **Die Zuordnung von Syslog-Servern zu Modulen** (`SyslogServer.AssignedModules`): Die XML dokumentiert nur den Getter, kein Member ordnet ein Modul zu.
- **Display-Sprachen** (`DisplayLanguage` einer `MultilingualSupport`-Zeile) sind laut Handbuch nur lesbar.

## Webserver- und OPC-UA-Benutzer (Issue #13)

Lesen mit `get_cpu_settings`: Abschnitte `webserverUsers` (`HW.Features.WebserverUserManagement.WebserverUsers`: `UserName`,
`Permissions`), `simpleWebserverUsers` (`SimpleWebserverUserManagement`: `UserName`, `Active`, `Permissions`) und `opcUaUsers`
(`OpcUaUserManagement` des OPC-UA-Submoduls: `UserName`). Passwörter sind nie lesbar. Laut Handbuch („Accessing web server and
OPC UA user management“) brauchen Anlegen, Löschen und Passwortänderungen einen aktivierten Webserver bzw. einen aktivierten
OPC-UA-Server mit Benutzername-/Passwort-Anmeldung; sonst wirft TIA Portal (der Fehler des Eintrags). Ein Passwort, das gegen
die Passwortrichtlinie verstößt, lehnt ebenfalls TIA Portal ab. Änderungen wirken nach einem Laden.

| Operation | Felder | Siemens-Member (V21) |
| --- | --- | --- |
| `create_webserver_user` | `userName`, `permissions` (Member-Namen von `WebserverUserPermissions`, kombiniert), `passwordVariable`, optional `plcName` | `WebserverUserComposition.Create(String, WebserverUserPermissions, SecureString)`; Rücklesen über `Find` und `Permissions`. Löst `plc_lowers_cpu_protection` aus (ein neuer Benutzer mit Zugriff) |
| `update_webserver_user` | `userName`, `permissions` und/oder `passwordVariable`, optional `plcName` | `WebserverUser.Permissions { set; }` (zurückgelesen) und `SetPassword(SecureString)`. Neue Rechte lösen `plc_lowers_cpu_protection` aus, ein neues Passwort `plc_raises_cpu_protection` |
| `delete_webserver_user` | `userName`, optional `plcName` | `WebserverUser.Delete()`; Rücklesen über `Find` |
| `update_simple_webserver_user` | `userName`, `settings`: `Active`, `Permissions` und/oder `passwordVariable`, optional `plcName` | `SimpleWebserverUser.Active`, `.Permissions`, `.SetPassword(SecureString)`. `Active: true` und `Permissions: ReadWrite` senken den Schutz |
| `create_opcua_user` | `userName`, `passwordVariable`, optional `plcName` | `OpcUaUserComposition.Create(String, SecureString)` des Dienstes am OPC-UA-Submodul (CPU und ihre Device-Items werden durchsucht); Rücklesen über `Find`. Löst `plc_lowers_cpu_protection` aus |
| `set_opcua_user_password` | `userName`, `passwordVariable`, optional `plcName` | `OpcUaUser.SetPassword(SecureString)`; Prüfung `trusted` (nicht lesbar) |
| `delete_opcua_user` | `userName`, optional `plcName` | `OpcUaUser.Delete()` |

Ausgelassen (Issue #13):

- **Anlegen oder Löschen einfacher Webserver-Benutzer:** `SimpleWebserverUserComposition` dokumentiert nur `Find`, kein
  `Create`; `SimpleWebserverUser` dokumentiert kein `Delete`.
- **Ob `WebserverUserPermissions` ein Flags-Enum ist,** ist nicht dokumentiert; mehrere Rechte werden nur kombiniert, wenn das
  Laufzeit-Enum eines ist (sonst ist genau ein Member erlaubt, der Rest ist ein `validation_error`).

## UMAC: Rollen, Projektbenutzer, Funktionsrechte, Projektschutz (Issue #12)

`UmacConfigurator` wird unter `GetServiceInfos()` des Projekts gefunden (Siemens-Skill security: `Project.GetService<UmacConfigurator>()`),
die UMAC-Operationen brauchen also ein Einzelprojekt. `UmacDevice` wird am CPU-Device-Item gesucht, danach am Gerät (Skill:
`device.GetService<UmacDevice>()`, der Typ von `device` ist nicht genannt). Jede UMAC-Schreiboperation löst den Info-Guard
`plc_changes_project_access` aus: Engineering-Funktionsrechte entscheiden, wer das Projekt öffnen und ändern darf, und laut
Handbuch („Modifying project in UMAC“) darf Openness in einem geschützten Projekt nur mit dem Funktionsrecht „Modify project via
Openness API“ Daten ändern (sonst nur lesen). Die Rechte, die die Sitzung dieses Servers braucht, nicht entfernen.

| Operation | Felder | Siemens-Member (V21) |
| --- | --- | --- |
| `get_umac` | `plc_read`, optional `plcName` | Projektabschnitt `umac`: `EngineeringFunctionRights` (nicht leer = Projekt geschützt, Siemens-Skill), `CustomRoles` (mit `Comment`, `SessionTimeOut`, `AssignedEngineeringRights`), `SystemRoles`, `ProjectUsers` (`AliasName`, `Comment`, `IsActive`, `IsRuntimeSessionTimeoutActive`, `SessionTimeOut`, `ProjectUserAuthenticationType`, `Roles`), `UmcUsers`, `UmcUserGroups`, `CustomDeviceFunctionRights`, `AnonymousUser`; Abschnitt `umacPasswordPolicy` (`PasswordPolicyConfigurator`); je PLC `umacDevice` mit `AvailableDeviceFunctionRights` und den zugewiesenen Rechten jeder eigenen und Systemrolle (`GetAssignedDeviceFunctionRights(UmacDevice)`, `GetAssignedSystemDeviceFunctionRights(UmacDevice)`) |
| `create_custom_role` / `update_custom_role` / `delete_custom_role` | `name`, `settings`: `Comment`, `SessionTimeOut`; Ändern: und/oder `newName` | `CustomRoleComposition.Create(String, String)` (der Kommentar), `CustomRole.SetName(String)`, `.Delete()`, Eigenschaften zurückgelesen |
| `assign_engineering_right` / `unassign_engineering_right` | `roleName`, `rightName` | `EngineeringFunctionRights.Find(String)`, `CustomRole.AssignedEngineeringRights.Add/Remove(EngineeringFunctionRight)`; zurückgelesen. Zuweisen löst `plc_lowers_cpu_protection` aus |
| `assign_device_function_right` / `unassign_device_function_right` | `roleName`, `rightName` (Name, sonst Openness-Kennung), optional `plcName` | `CustomRole.AssignDeviceFunctionRight(UmacDevice, DeviceFunctionRight)` / `UnAssignDeviceFunctionRight`, zurückgelesen mit `GetAssignedDeviceFunctionRights`; wirksam am Gerät nach Laden der Benutzerverwaltungsdaten |
| `create_project_user` | `userName`, `passwordVariable`, optional `settings`: `Comment`, `SessionTimeOut` | `ProjectUserComposition.Create(String, SecureString)` (der Aufruf des Skills); Rücklesen über `Find`. Löst `plc_raises_cpu_protection` aus |
| `update_project_user` | `userName`, beliebig aus `settings`, `newName`, `aliasName`, `active`, `runtimeSessionTimeout`, `passwordVariable` | `ProjectUser.SetAliasName`, `Activate()`/`Deactivate()`, `ActivateRuntimeSessionTimeout()`/`DeactivateRuntimeSessionTimeout()`, `SetPassword(SecureString)`, `SetName`; Zustände zurückgelesen (`AliasName`, `IsActive`, `IsRuntimeSessionTimeoutActive`) |
| `delete_project_user` | `userName` | `ProjectUser.Delete()` |
| `assign_user_role` / `unassign_user_role` | `userName`, `roleName` (eigene, sonst Systemrolle), `userKind` (`projectUser` Standard, `umcUser`, `umcUserGroup`) | `User.Roles` / `UmcUserGroup.Roles` (`RoleAssociation.Add/Remove(Role)`); zurückgelesen |
| `set_anonymous_user` | `active` | `UmacConfigurator.ActivateAnonymousUser()` / `DeactivateAnonymousUser()`; `AnonymousUser.IsActive` zurückgelesen, wenn das Laufzeitobjekt es hat. Aktivieren löst `plc_lowers_cpu_protection` aus |
| `create_custom_device_function_right` / `update_custom_device_function_right` / `delete_custom_device_function_right` | `name`, `group` (Anlegen), `settings`: `Comment`, `newName` (Ändern) | `CustomDeviceFunctionRightComposition.Create(String, String, String)` (Name, Gruppe, Kommentar), `SetName`, `Comment`, `Delete()` |
| `update_umc_user` / `delete_umc_user` | `userName`, `userKind` (`umcUser` Standard, `umcUserGroup`), Ändern: `active` und/oder `newName` | `UmcUser`/`UmcUserGroup` `Activate()`, `Deactivate()`, `SetName(String)`, `Delete()` |
| `set_umac_password_policy` | `settings`: `MinimumLength`, `MinimumNumericCharacterLength`, `MinimumSpecialCharacterLength`, `IncludesLowerCaseAndUpperCaseCharacters`, `EnablePasswordAging`, `PasswordValidity`, `PasswordValidityPrewarningTime`, `MinimumUserPasswordsBlockedForReuse` | `Umac.PasswordPolicyConfigurator` (unter den Diensten des Projekts gesucht; wo er hängt, ist nicht dokumentiert). `IncludesLowerCaseAndUpperCaseCharacters: false` und `EnablePasswordAging: false` senken den Schutz |

Ausgelassen (Issue #12):

- **Öffnen eines UMAC-geschützten Projekts mit Anmeldedaten** (`ProjectComposition.Open(FileInfo, UmacDelegate)`, die Überladungen
  von `OpenWithUpgrade`/`Retrieve` mit `UmacDelegate`, `UmacCredentials.Name`/`.Type`/`.SetPassword`): Das gehört zu den
  Lebenszyklus-Werkzeugen (`open_project`, `retrieve_project`) und deren Bindungs- und Bestätigungsablauf und wird in diesem Paket
  nicht geändert. Heute wird ein solches Projekt in der TIA-Portal-Oberfläche (mit der Anmeldung des Benutzers) geöffnet und dann
  mit `bind_project` gebunden.
- **Online-Anmeldung über UMAC** (`OnlineAuthenticationConfiguration`, `OnlineCredentials.Name`/`.Type`/`.SetPassword`, Handbuch
  „Supporting UMAC on PLC device“): Die Online-Rückrufe von `plc_download`, `plc_tia_online` und `compare_to_online` beantworten nur
  Passwortkonfigurationen; Benutzername und Benutzertyp zu beantworten ist eine Änderung dieser Werkzeuge und erfolgt hier nicht.
- **Auswahl beim Laden der Benutzerverwaltung** (`UserManagementDownload` mit `UserManagementPreDownloadSelections`, Handbuch
  „Downloading User Management data“) ist eine Rückruf-Konfiguration von `plc_download`, außerhalb dieser Familie.
- **Import von UMC-Benutzern und -Gruppen** von einem UMC-Server (`UmcServerConfigurator`, `UmcCredentials`): Die V21-XML nennt die
  Typen, ein dokumentierter Importaufruf wurde nicht gefunden; nur bereits importierte UMC-Benutzer und -Gruppen werden geändert.
- **Systemrollen** sind nur lesbar (kein dokumentierter Schreib-Member).

## CPU-Zertifikate und sichere Kommunikation (Issue #14)

Handbuch „Managing certificate“: Der Dienst `LocalCertificateManager` am PLC-Device-Item hält den lokalen Speicher
`LocalCertificateStore`; Zertifikate werden aus Vorlagen (`GetCertificateTemplate(CertificateUsage)`) erzeugt, importiert,
exportiert, gelöscht und über die dynamischen Attribute `WebserverCertificate` (PLC) und `OpcUaServerCertificate`
(OPC-UA-Submodul; `null` hebt die Zuordnung auf) zugeordnet. **Nichts wird automatisch vertraut**: Keine Operation beantwortet
eine TLS-Frage oder nimmt ein Zertifikat in eine Vertrauensliste auf. `get_cpu_certificates` meldet je Zertifikat den
SHA-256-Fingerabdruck über die DER-Bytes eines temporären `Export(FileInfo)` (die Datei wird danach gelöscht), denselben Hash,
den die TLS-Regel von `plc_download`, `plc_tia_online` und `plc_station_upload` für `trustedCertificates` listet (K27/K52/K53):
Der Mensch vergleicht und entscheidet.

| Operation | Werkzeug | Felder | Siemens-Member (V21) |
| --- | --- | --- | --- |
| `get_cpu_certificates` | `plc_read` (TemporaryExport, jeder Modus) | optional `plcName` | Abschnitte `localCertificateManager` (`EnableGlobalCertificatesStore`; `Certificates`: `Id`, `SubjectCommonName`, `ValidUntil`, `HasPrivateKey`, `Sha256`, `Sha1Thumbprint`, `Subject`, `Issuer`, `NotBefore`, `NotAfter`, `FingerprintError`), `certificateManagement` (`Usage`, `CertificateExpirationEventActivated`, `RemainingCertificateLifetime`, `CertificateSupportedServices`: `Id`, `ServiceType`, `ServiceGroupName`, `ApplicationUri`, `Guid`), `certificateAssignments` (`WebserverCertificate`, `OpcUaServerCertificate` mit Id und Fingerabdruck) |
| `set_certificate_manager` | `plc_write` | optional `plcName`, `settings`: `EnableGlobalCertificatesStore` | `LocalCertificateManager.EnableGlobalCertificatesStore` |
| `create_cpu_certificate` | `plc_write` | `usage` (`CertificateUsage`), optional `settings`: `SubjectCommonName`, `Signature`, `ValidFrom`, `ValidUntil`, `subjectAlternativeNames` (`Typ:Wert`), `plcName` | `GetCertificateTemplate(CertificateUsage)`, Eigenschaften der Vorlage, `SubjectAlternativeNames.Create(SubjectAlternativeNameType, String)`, `Certificates.Create(CertificateTemplate)`; Ergebnis: die neue `Id` und SHA-256. `Signature: Sha1RSA` löst `plc_lowers_cpu_protection` aus |
| `import_cpu_certificate` | `plc_write` | `filePath`, optional `passwordVariable` (geschützte Datei), `plcName` | `CertificateComposition.Import(FileInfo)` / `Import(FileInfo, SecureString)`; Ergebnis: `Id` und SHA-256 des importierten Zertifikats. Info-Guard `plc_imports_certificate` |
| `export_cpu_certificate` | `plc_write` | `certificateId`, `filePath`, optional `overwrite`, `plcName` | `Certificate.Export(FileInfo)` (laut Handbuch jedes Format über die Endung) nach den Regeln für Exportdateien (temporäre Datei neben dem Ziel, kein Überschreiben ohne `overwrite:true`, neu gehasht; Prüfung `file`) |
| `delete_cpu_certificate` | `plc_write` | `certificateId`, optional `plcName` | `Certificate.Delete()`; zurückgelesen. Löst `plc_lowers_cpu_protection` aus |
| `assign_cpu_certificate` / `unassign_cpu_certificate` | `plc_write` | `certificateRole` (`webserver`, `opcUaServer`), `certificateId` (Zuordnen), optional `plcName` | `SetAttribute("WebserverCertificate" / "OpcUaServerCertificate", Zertifikat oder null)` an der PLC bzw. am ersten Device-Item, das das Attribut kennt; Rücklesen über die Identität. Aufheben löst `plc_lowers_cpu_protection` aus |
| `set_certificate_management` | `plc_write` | optional `plcName`, `settings`: `Usage` (`TIAPortal`, `Runtime`), `CertificateExpirationEventActivated`, `RemainingCertificateLifetime` (10-90 %) | `HW.Features.CertificateManagementConfiguration` (Handbuch „Managing dynamic certificate settings“; TIA Portal wirft für `Runtime`, solange Webserver und OPC-UA-Server aus sind) |
| `add_certificate_supported_service` / `update_certificate_supported_service` / `remove_certificate_supported_service` | `plc_write` | `certificateId` (UInt32), `serviceType` und `name` (Name der Dienstgruppe; Hinzufügen), `settings`: `ServiceGroupName` (Ändern), optional `plcName` | `CertificateSupportedServiceComposition.Create(UInt32, String, CertificateSupportedServiceName)`, `.Find(UInt32)`, `ServiceGroupName { set; }`, `Delete()` |

Ausgelassen (Issue #14):

- **Alte Kommunikation ohne TLS** (`ConnectionConfiguration.EnableLegacyCommunication`, Handbuch „Supporting secure S7
  communication TLS“): Sie schaltet TLS für die Verbindung ab; dieser Server setzt sie nie (feste Regel der Online-Werkzeuge). Der
  übrige Inhalt dieses Abschnitts (Handler `OnlineLegitimation`, `TlsVerificationConfiguration` mit `Trusted`, `NonTrusted`,
  `NonVerified`) ist die bestehende TLS-Regel der Online-Werkzeuge, unverändert: Vertrauen nur für gelistete Fingerabdrücke.
- **Der globale Zertifikatsspeicher** (Zertifikate mehrerer Geräte): Die V21-XML dokumentiert nur den Schalter
  `EnableGlobalCertificatesStore`, keinen Member zum Lesen oder Ändern des globalen Speichers.
- **`OpcUaServerCertificateSettings`** (`OpcUaCertificatesAtRuntime`, `OpcUaCertificatesViaDownload`): ein Enum eines
  Hardware-Attributs, dessen Attributname nicht dokumentiert ist; setzen mit `network_write set_hardware_attributes`, wenn das
  Attribut gelistet ist.
- **Dynamische Zertifikate beim Laden** (`SelectiveDeleteDownload`, Handbuch „Managing dynamic certificate settings“): eine
  Rückruf-Konfiguration von `plc_download`, außerhalb dieser Familie.

## Safety-Ausdruck (Issue #21)

**Der MCP ersetzt nie die Safety-Abnahme.** Ein Sicherheitsprogramm muss von einer befähigten Person validiert und abgenommen
werden (Handbuch SIMATIC Safety); der Ausdruck ist eine Unterlage für diese Abnahme. Ihn zu erzeugen nimmt nichts ab, und nichts in
dieser Familie markiert ein Sicherheitsprogramm, einen Test oder ein Ergebnis als abgenommen oder bestanden.

| Operation | Felder | Siemens-Member (V21) |
| --- | --- | --- |
| `generate_safety_printout` | `filePath`, `documentLayout` (z. B. `DocuInfo_ISO_A4_Portrait`, `DocuInfo_Simple_A4_Portrait_Small_Footer`), optional `printer` (`MicrosoftPrintToPdf` Standard, `MicrosoftXpsDocumentWriter`), `printOption` (`All` Standard, `Compact`), `plcName`, `overwrite` | `Safety.SafetyPrintout.Print(SafetyPrintoutFilePrinter, FileInfo, String, SafetyPrintoutOption)` des F-CPU-Device-Items („Creates a Safety Printout for the current device item and prints it to a file ... Returns true on success“; der Dienst ist für „Plus PLCs“ dokumentiert, S7-1500/ET 200SP). Der Druckertreiber schreibt eine temporäre Datei neben dem Ziel, die nach den Regeln für Exportdateien an ihren Platz verschoben und neu gehasht wird (Prüfung `file`); `false` scheitert mit `postcondition_failed` und legt nichts ab. Info-Guard `plc_safety_printout_is_no_acceptance`; die Warnung des Ergebnisses wiederholt den Abnahmehinweis |

Ausgelassen (Issue #21): nichts Dokumentiertes. Die Layouts, die TIA Portal anbietet, listet Openness nicht; `documentLayout` wird
so übergeben, wie es angegeben ist, und TIA Portal entscheidet. K79 (live am 2026-11-03: „No Document description object "?"
found in System Library or Project“): eine Leseoperation, die die Layouts listet, wurde nicht gebaut, weil V21 keinen Member dafür
dokumentiert (in `members.txt` gesucht nach DocumentInfo, DocumentDescription, DocuInfo, Printout, Printing, Druckeinstellungen und
den Kopiervorlagen-Ordnern; es gibt nur `DocumentInfoOptions` der Bibliotheksexporte und `SafetyPrintout.Print`). Stattdessen endet
jedes gescheiterte `generate_safety_printout` (außer `validation_error` und `portal_lost`) mit `PlcSafetyPrintoutNames.LayoutHint`:
der Name ist eine Dokumentinformation von TIA Portal, zu finden unter „Dokumentationseinstellungen > Dokumentinformationen“ (neben
den Deckblättern und Rahmen) des Projekts oder einer globalen Bibliothek wie der Systembibliothek; die Namen der Referenz sind
Beispiele. Nicht live getestet.

## Safety Validation Assistant: Aktivierungstests und Testbericht (Issue #25)

**Der MCP ersetzt nie die Safety-Abnahme.** Er liest, bereitet vor (Tests, Sicherheitsfunktionen, Bedingungen) und erzeugt
Berichte; er trägt nie ein Testergebnis ein. `TestState` und `OverallState` werden nie geschrieben (ein `settings`-Schlüssel
`TestState`, `OverallState` oder `State` wird vor jedem Worker-Aufruf abgelehnt), und Importe und Kopien, die ausgeführte
Ergebnisse mitbringen könnten, werden nicht angeboten (siehe Ausgelassen). Jede Änderung löst den Bestätigungs-Guard
`plc_safety_acceptance_required` aus (denselben wie bei F-PLC-Schreiboperationen): Lesen-Schreiben fragt einmal je Aufruf, Voll
erfüllt ihn per Richtlinie, das Audit hält fest, wie. Exporte und Berichte lösen den Info-Guard `plc_safety_printout_is_no_acceptance`
aus. `SafetyValidationAssistant` wird unter `GetServiceInfos()` des Projekts gefunden (die XML sagt nicht, wo er hängt; nur
Einzelprojekte); `DeviceQuery`, `TestValidity` und `ActivationTestPrintout` unter `GetServiceInfos()` des Assistenten, des Tests,
der Funktion oder der Bedingung.

| Operation | Werkzeug | Felder | Siemens-Member (V21) |
| --- | --- | --- | --- |
| `get_safety_validation` | `plc_read` | keine | Abschnitt `safetyValidation`: `ActivationTests` (jeder Test der Wurzel und der Gruppen: `Name`, `Author`, `EvaluationDeviceName`, `OverallState`, Pfad `Group`; darunter `SafetyFunctions`: `Name`, `TestName`, `Description`, `TestState`, `IsTraced`; darunter `Conditions`: `DeviceName`, `SignalName`, `SignalUsage`, `InitialInput`, `ExecutedInput`, `Response`, `Comment`), `ActivationTestGroups` (Pfade), `EvaluationDevices` (`DeviceQuery.EvaluationDevices()`) |
| `check_activation_test` | `plc_read` (Schreibfähigkeit, in read-only abgelehnt: ob `CheckValidity()` einen Zustand speichert, ist nicht dokumentiert; Sicherheits-Review U1) | `testName`, optional `functionName`, `position` (eine Bedingung) | `TestValidity.CheckValidity()` -> `TestValidationResult` (`State`, `ErrorCount`, `Messages`): eine Konsistenzprüfung der Testdaten, kein Testergebnis |
| `create_activation_test` / `delete_activation_test` | `plc_write` | `name`, `evaluationDevice` (Anlegen; ein Name aus `EvaluationDevices`), optional `folderPath` | `ActivationTestComposition.Create(String, DeviceItem)` der Wurzel oder Gruppe, `ActivationTest.Delete()`; zurückgelesen |
| `change_activation_test_evaluation_device` | `plc_write` | `name`, `evaluationDevice` | `ActivationTest.ChangeEvaluationDevice(DeviceItem)`; `EvaluationDeviceName` gemeldet, nicht verglichen (sein Bezug zum Namen des Device-Items ist nicht dokumentiert) |
| `create_activation_test_group` / `delete_activation_test_group` | `plc_write` | `folderPath` (z. B. `/Line1/Cell2`) | `ActivationTestUserGroupComposition.Create(String)` des Elternteils, `ActivationTestUserGroup.Delete()` |
| `create_safety_function` / `update_safety_function` / `delete_safety_function` | `plc_write` | `testName`, `functionName` (Ändern, Löschen), `settings`: `Name`, `TestName`, `Description`, `traced` | `SafetyFunctionComposition.Create()`, die Eigenschaften (gebunden und zurückgelesen, wenn das Laufzeitobjekt Setter hat), `TraceConfiguration.IsTraced`, `Delete()` |
| `reset_safety_function_test_result` | `plc_write` | `testName`, `functionName` | `SafetyFunction.ResetTestResult()`, `TestState` als `NotTested` zurückgelesen: entfernt ein eingetragenes Ergebnis, der Test muss von der befähigten Person erneut ausgeführt werden; setzt nie ein Ergebnis |
| `create_safety_function_condition` / `update_safety_function_condition` / `delete_safety_function_condition` | `plc_write` | `testName`, `functionName`, Anlegen: `signalDevice` (aus `ActivationTest.AvailableDevices()`), `signalUsage` (`InputCondition`, `OperatingMode`, `Response`), `signalName`; Ändern/Löschen: `position`; `settings`: `InitialInput`, `ExecutedInput`, `Response` (`ConditionValue`), `Comment` | `ConditionComposition.Create(DeviceItem, SignalUsage, String)`, Eigenschaften der Bedingung, `Condition.Delete()` |
| `export_activation_test` | `plc_write` | `testName`, `filePath`, optional `overwrite` | `ActivationTest.Export(FileInfo, ExportOptions.None)` (SimaticML) nach den Regeln für Exportdateien, Prüfung `file` |
| `generate_activation_test_report` | `plc_write` | `testName`, `filePath`, optional `overwrite` | `ActivationTestPrintout.Generate(FileInfo)` des Tests (sonst des Assistenten) nach den Regeln für Exportdateien, Prüfung `file` |

Ausgelassen (Issue #25), nach der Regel, dass der MCP nie für eine Person einen Test als bestanden markiert:

- **Importe und Kopien** (`ActivationTestComposition.Import(FileInfo, ActivationTestImportOptions)`, `CreateFrom(ActivationTest)`,
  `CreateFrom(MasterCopy)`, `ActivationTestUserGroupComposition.CreateFrom(MasterCopy)`, `SafetyFunctionComposition.Import`,
  `CreateFrom(SafetyFunction)`): Eine SimaticML-Datei, eine Kopiervorlage oder ein vorhandener Test kann ausgeführte Testzustände
  enthalten; das Ergebnis würde Tests als bestanden zeigen, die an diesem Projekt niemand ausgeführt hat. Dafür TIA Portal nutzen.
- **`TestState` setzen** (ob die Laufzeiteigenschaft einen Setter hat oder nicht) und alles andere, was ein Ergebnis einträgt.
- **`SafetyFunction.Export`** (SimaticML einer Funktion): zusammen mit den zugehörigen Importen ausgelassen; `export_activation_test`
  exportiert den ganzen Test.
- **Überladungen von `Export` mit `DocumentInfoOptions`:** Die Optionen sind über ihren Typ hinaus nicht dokumentiert.
