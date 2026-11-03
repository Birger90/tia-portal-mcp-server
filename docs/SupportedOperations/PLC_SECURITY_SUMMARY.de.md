# CPU-Schutz (`plc_read` / `plc_write`)

**Sprache:** [English](PLC_SECURITY_SUMMARY.md) · Deutsch

Status: implementiert auf `feature/sim-security-profile`, am 2026-11-01 auf passwortgeschützte CPUs erweitert (Anfrage des
Eigentümers: passwortgeschützte Projekte müssen vollständig funktionieren), **nicht live getestet**. Offline-Tests decken die Validierung,
die Laufzeitbindung gegen Ersatztypen und den FakeWorker-Pfad von Anfang bis Ende ab.

## Warum

Ein mit `create_project` angelegtes Projekt (CPU 1516-3 PN/DP 6ES7 516-3AN02-0AB0 V2.9) scheiterte beim Übersetzen der Hardware
durch `plc_download` nach PLCSIM mit drei Fehlern (live, 2026-11-01): Die Zugriffsstufe
„Kein Zugriff (kompletter Schutz)“ erfordert ein Passwort; „Vertrauliche PLC-Konfigurationsdaten schützen“ ist
ohne Passwort eingeschaltet; das PLC-Kommunikationszertifikat kann ohne dieses Passwort nicht verwendet werden. Das Setzen von
„Vollzugriff (kein Schutz)“ und das Abwählen des Schutzes vertraulicher Daten in der Oberfläche behob es. Diese
Operationen tun dasselbe über Openness; seit 2026-11-01 schützen sie auch eine CPU (jede Zugriffsstufe, Passwörter der Stufen,
das Passwort für vertrauliche Daten) mit Passwörtern aus Passwortquellen, nie aus Tool-Argumenten.

`compile_check` meldete für dieses Projekt 0 Fehler, weil es nur die PLC-Software übersetzte (Defekt D9);
es hat jetzt `scope: "hardware"` (siehe [Hardware übersetzen](#hardware-übersetzen-compile_check-scope-hardware)).

## Operationen

| Operation | Tool | Capability | Felder | Was sie tut |
| --- | --- | --- | --- | --- |
| `get_plc_security` | `plc_read` | `Observe` (jeder Modus) | optional `plcName` (weggelassen: jede PLC) | Pro CPU: `accessLevel`, `accessControl`, `confidentialDataProtection` (mit `MasterSecretConfiguration` unter `observedMembers`), die Namen der Servicetypen der CPU |
| `set_plc_access_level` | `plc_write` | `ProjectMutation` | `accessLevel` (erforderlich), optional `plcName`, `accessLevelPasswords`, `resetAccessLevelPasswords` | `ResetPassword(level)` für jede zurückzusetzende Stufe, dann `PlcProtectionAccessLevel`, dann `SetPassword(level, SecureString)` für jedes Stufenpasswort; ein fehlgeschlagenes `SetPassword` stellt die vorher gelesene Stufe wieder her. K48 (live V21 2026-11-02, F-CPU): TIA Portal nimmt den Passwortaufruf einer Stufe nur an, solange die aktuelle Stufe weniger offen ist als diese Stufe („Cannot set password for the access level“ für `SetPassword` vor der Stufe und für `ResetPassword(FullAccessIncludingFailsafe)` danach), und jede Stufe braucht ein eigenes Passwort („This password is already being used for another access level“). Die Siemens-Reihenfolge ist nicht dokumentiert; Stufe vor `SetPassword` ist live bestätigt, Zurücksetzen zuerst noch nicht |
| `protect_plc_confidential_data` | `plc_write` | `ProjectMutation` | `passwordVariable` (oder die Map), optional `plcName`, `allConfiguration` | `Protect(SecureString)`; `allConfiguration:true`: `ProtectAllPlcConfigurationWithPassword(SecureString)` oder `ProtectAllPlcConfiguration()` ohne Passwort |
| `change_plc_confidential_data_password` | `plc_write` | `ProjectMutation` | `passwordVariable` (aktuell), `newPasswordVariable`, optional `plcName` | `ChangePassword(SecureString, SecureString)` |
| `disable_plc_confidential_data_protection` | `plc_write` | `ProjectMutation` | optional `plcName`, `passwordVariable`, `allConfiguration` | `Unprotect(SecureString)` mit Passwort, sonst `Unprotect()`; `allConfiguration:true`: `UnprotectAllPlcConfiguration()` (nur der zusätzliche Schutz) |
| `compare_to_online` | `plc_read` | `OnlineObserve` | optional `passwords` (neben den Vergleichsfeldern) | beantwortet Online-Passwortkonfigurationen (`OnlineReadAccessPassword`) einer geschützten CPU aus Passwortquellen und der Map |

Die CPU ist das Geräteelement, das die PLC-Software beherbergt (`PlcSoftwareLocator`, dasselbe Element, das `plc_download`
verwendet). Ein Schreibvorgang mit weggelassenem `plcName` adressiert die einzige PLC; mehrere PLCs scheitern mit `target_ambiguous`.

### Passwörter

Jedes Passwort ist eine **Passwortquelle**, nie ein Wert ([Passwortquellen](../guides/password-sources.de.md)): ein
NAME einer Umgebungsvariablen (`"NAME"` oder `{ "env": "NAME" }`) oder ein generischer Berechtigungsnachweis der Windows-Anmeldeinformationsverwaltung
(`{ "credential": "TiaMcp/..." }`, Präfix vom Server-Eigentümer konfigurierbar). Ein Wert, der keines von beiden ist, wird beim Lesen
des Requests geschwärzt und abgelehnt, ohne wiedergegeben zu werden. Der Worker löst jede Quelle eines Elements in einen schreibgeschützten
`SecureString` auf, **bevor** der erste Siemens-Aufruf erfolgt (eine unbrauchbare Quelle scheitert, ohne dass etwas geändert wurde: `validation_error`,
oder `unsupported_capability` für einen Berechtigungsnachweis außerhalb von Windows), und gibt ihn nach dem Aufruf frei. Ergebnisse und das Audit tragen
nur die Tokens (`result.passwordSources`: Zweck, Quelle, Art).

Die Passwort-Map des Servers (`%APPDATA%\TiaMcpServer\passwords.json`, nur Namen) füllt, was ein Aufruf weglässt:

| Zweck | Verwendet von |
| --- | --- |
| `FullAccessIncludingFailsafe`, `FullAccess`, `ReadAccess`, `HMIAccess` | `set_plc_access_level`: jede Stufe, die offener ist als `accessLevel` (nur diese Zwecke; nie an Downloads gesendet) |
| `PlcMasterSecretPassword` | `protect_plc_confidential_data`, `change_plc_confidential_data_password` (aktuell), `disable_plc_confidential_data_protection` (außer bei `allConfiguration`); auch die gleichnamige Download-Konfiguration |
| `ModuleWriteAccessPassword`, `ModuleReadAccessPassword` (Download, Upload), `OnlineReadAccessPassword` (go_online, compare_to_online), `PasswordReadAccess` (Upload) | die Passwortkonfigurationen, die TIA Portal auslöst, nach Kurzname des Typs |
| `safety` | das fehlersichere `safety_login`, `set_safety_password`, `revoke_safety_password` |

### Zugriffsstufen

`accessLevel` ist eine beliebige Stufe des Schutzdialogs von TIA Portal: `FullAccessIncludingFailsafe` („Vollzugriff inkl.
Fehlersicherheit (kein Schutz)“, F-CPUs), `FullAccess` („Vollzugriff (kein Schutz)“), `ReadAccess` („Lesezugriff“),
`HMIAccess` („HMI-Zugriff“), `NoAccess` („Kein Zugriff (kompletter Schutz)“). `None` ist der nicht gesetzte Wert des Enums und wird
nie gesetzt. Member-Namen beachten Groß-/Kleinschreibung.

Eine geschützte Stufe benötigt Passwörter für die offeneren Stufen: in TIA Portal mindestens `FullAccess` (und
`FullAccessIncludingFailsafe` auf einer F-CPU); Passwörter für `ReadAccess` und `HMIAccess` sind dort optional. Der Server lehnt
eine geschützte Stufe ohne sie nicht ab (TIA Portal entscheidet; das Übersetzen der Hardware meldet ein fehlendes); das Element
warnt, wenn im Aufruf kein `FullAccess`-Passwort gesetzt wurde. Schlüssel von `accessLevelPasswords` und Einträge von `resetAccessLevelPasswords`
sind die vier Passwortstufen (`NoAccess` nimmt keines); eine Stufe darf nicht in einem Element gesetzt und zurückgesetzt werden.

Für eine F-CPU verwendet das passwortfreie Rezept `FullAccessIncludingFailsafe`: Auf einer F-CPU ist `FullAccess` nicht die höchste Stufe,
und TIA Portal fragt weiter nach einem Passwort für die fehlersichere Stufe (Laden und Ändern des Sicherheitsprogramms in der CPU).
Das folgt dem Schutzdialog von TIA Portal und ist nicht live verifiziert (Schritt 11a des
[F-CPU-Live-Testplans](../development/plcsim-fcpu-live-test-plan.md)). Das Offline-Programmpasswort der Safety
([PLC-Safety-Referenz](PLC_SAFETY_SUMMARY.de.md)) ist ein separater Schutz und nicht betroffen.

### Ergebnisse

`get_plc_security` liefert `{ plcs: [{ plcName, deviceName, services, servicesTruncated, accessLevel,
accessControl, confidentialDataProtection }] }`.

- `accessLevel` / `accessControl`: `{ service, property, serviceOffered, serviceType, readable, value,
  writable, availableValues, note }`. Das Lesen von `PlcProtectionAccessLevel` wirft `NullReferenceException`,
  wenn kein Passwort konfiguriert ist (von Siemens dokumentierter Fallstrick); dieser Lesevorgang wird als `readable:false`
  mit dem dokumentierten Hinweis gemeldet, nie als Wert. Andere behebbare Openness-Getter-Fehler sind `readable:false`
  mit ihrem Typ und ihrer Meldung; ein Sitzungsverlust wird weitergegeben. Ein Service, den `GetServiceInfos()` auflistet, aber
  `GetService` nicht liefert, ist `serviceOffered:true`, `readable:false` mit dem Hinweis "GetServiceInfos() lists
  '…', but GetService returned null for it; this part is unavailable through Openness here.", und der Rest des Lesevorgangs
  läuft weiter: Live auf V21 am 2026-11-02 (Defekt D14, [Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md)) scheiterte `get_plc_security plcName PLC_1` auf einer S7-1500,
  weil `PlcAccessControlConfigurationProvider` gelistet war und `GetService` null lieferte; seit der Korrektur werden die
  Zugriffsstufe und der Schutz vertraulicher Daten weiterhin gemeldet. Ein Schreibvorgang, der einen solchen Service benötigt, scheitert
  mit `unsupported_capability` vor jeder Änderung.
- `confidentialDataProtection`: `{ service, serviceOffered, serviceType, protectBindable,
  unprotectBindable, unprotectCandidates, observedMembers, note }`. `MasterSecretConfiguration` („the PLC protection
  type“, Werte undokumentiert) wird mit den anderen lesbaren skalaren Eigenschaften (höchstens 20) in `observedMembers` gelistet,
  so wie gefunden und nicht interpretiert.

Die Schreibvorgänge liefern `{ operation, plcName, deviceName, setting, serviceType, member, requested, before,
beforeReadable, after, afterReadable, readBack, observedBefore, observedAfter, calls, passwordSources, note }`.

- `set_plc_access_level` liest die Stufe vorher und nachher, wo lesbar. Ein lesbares, abweichendes Rücklesen lässt das
  Element mit `postcondition_failed` scheitern; ein nicht lesbares (die `NullReferenceException` oben, erwartet, solange kein
  Stufenpasswort existiert) ist `readBack:"unreadable"` mit einer Warnung, mit `compile_check` scope `hardware` zu bestätigen.
  `calls` listet jedes aufgerufene Member mit seiner Stufe, in Reihenfolge.
- Die Operationen für vertrauliche Daten haben `readBack:"notAvailable"` und melden `observedBefore`/`observedAfter`.

`set_plc_access_level` auf eine ungeschützte Stufe, das Zurücksetzen eines Stufenpassworts und `disable_plc_confidential_data_protection`
lösen den Info-Guard `plc_lowers_cpu_protection` aus; eine geschützte Stufe, `protect_plc_confidential_data` und
`change_plc_confidential_data_password` lösen `plc_raises_cpu_protection` aus. Ein `set_plc_access_level` mit
`readBack:"matched"` wird durch dieses Rücklesen verifiziert (Prüfung `readBack`, erwartet die angeforderte und tatsächlich die zurückgelesene
Stufe; Defekt K24, 2026-11-02: die Verifikation sagte „nothing was read back“ neben `readBack:"matched"`); eine
nicht lesbare Stufe und die Operationen für vertrauliche Daten bleiben `trusted`. Jede Einstellung ist ihr eigener Objektschlüssel.

### Fehler

| Kategorie | Wann |
| --- | --- |
| `validation_error` | `None` oder eine unbekannte Stufe, ein Passwortfeld bei einer anderen Operation, eine Stufe ohne Passwort in `accessLevelPasswords`/`resetAccessLevelPasswords`, eine wiederholte oder sowohl gesetzte als auch zurückgesetzte Stufe, ein fehlendes Passwort für protect/change, eine geschwärzte oder nicht gesetzte Passwortquelle |
| `unsupported_capability` | `GetServiceInfos()` der CPU listet den Service nicht, oder listet ihn, während `GetService` null dafür liefert (D14); eine Berechtigungsnachweis-Quelle außerhalb von Windows. Es wurde nichts geändert |
| `target_not_found` / `target_ambiguous` | die PLC-Auswahl |
| `api_member_missing` | der Service ist gelistet, aber ein Member fehlt oder hat eine andere Signatur (die Meldung listet die gefundenen Signaturen), eine Stufe, die dem Live-Enum fehlt, oder zwei Siemens-Typen mit dem Kurznamen des Konfigurators. Es wurde nichts geändert |
| `engineering_rejected` | TIA Portal hat einen Aufruf abgelehnt (`EngineeringTargetInvocationException` in einer mutierenden Methode), zum Beispiel ein Passwort, das seine Richtlinie ablehnt, oder ein falsches aktuelles Passwort |
| `postcondition_failed` | die zurückgelesene Stufe weicht von der angeforderten ab |

## Rezept: Simulationsprojekt ohne Passwort

```json
plc_write {
  "operations": [
    { "operationId": "access", "operation": "set_plc_access_level", "plcName": "PLC_1", "accessLevel": "FullAccess" },
    { "operationId": "confidential", "operation": "disable_plc_confidential_data_protection", "plcName": "PLC_1" }
  ]
}
```

Dann `compile_check { "scope": "hardware" }`, dann `plc_download`. Wenn das zweite Element mit
`api_member_missing` scheitert, „Vertrauliche PLC-Konfigurationsdaten schützen“ unter „Schutz & Security“ in
den CPU-Eigenschaften von Hand abwählen. Gedacht für PLCSIM-Testprojekte; eine echte CPU wird mit `set_plc_access_level` und `protect_plc_confidential_data` aus Passwortquellen geschützt (unten).

## Hardware übersetzen (`compile_check` scope `hardware`)

`compile_check` nimmt `scope`: `software` (Standard, unverändert: `PlcSoftware.GetService<ICompilable>()` oder
ein Baustein mit `blockPath`; Bericht `scope` `plc`/`block`) oder `hardware` (`GetService<ICompilable>()` auf
dem CPU-Geräteelement jeder PLC oder der PLCs, die `plcName` nennt; Bericht `scope` `hardware`). `hardware` mit
`blockPath` und jeder andere Wert sind `validation_error` vor jedem Worker-Aufruf. Das Übersetzen des Programms kann
keine Hardwarefehler zeigen; vor einem Download beides ausführen.

## Herkunft der API-Member

Jedes Siemens-Member, das die Schutzoperationen verwenden, mit seinem Beleg und der Art der Bindung. „Siemens skill“ ist
`openness_development/skills/security/SKILL.md` des Repositorys der Siemens TIA Portal AI extensions; „V21 manual“ ist die
Member-Liste von „TIA Portal Openness V21 - Functions on Devices (03/2026)“, wie extrahiert in totally-integrated-claude
`skills/tia-devices-general/references/device-attributes.md`. Es wurde kein Stub-Member hinzugefügt; gegen keinen Schutztyp wird
kompiliert.

| Member | Beleg | Bindung |
| --- | --- | --- |
| `IEngineeringServiceProvider.GetServiceInfos()` → `IList<EngineeringServiceInfo>`, `EngineeringServiceInfo.Type` | Siemens-Skill engineering-objects (bereits für Technologieobjekte verwendet) | Reflection (`TechnologyObjectRuntime.ServiceTypes`) |
| `System.IServiceProvider.GetService(Type)` auf dem CPU-Geräteelement | Siemens-Skill engineering-objects | zur Compile-Zeit gebundene .NET-Schnittstelle, erreicht über `TechnologyObjectRuntime.Service` |
| `Siemens.Engineering.HW.Features.PlcAccessLevelProvider` (Service des CPU-`DeviceItem`) | Siemens skill; V21 manual | Reflection: exakter vollständiger Name unter den gelisteten Services |
| `PlcAccessLevelProvider.PlcProtectionAccessLevel` (get/set) | Siemens skill, inkl. des `NullReferenceException`-Fallstricks; V21 manual („To set the protection access level type“) | Reflection: eine öffentliche, nicht indizierte Eigenschaft dieses Namens, deren Typ ein Enum namens `PlcProtectionAccessLevel` ist; öffentlicher Setter erforderlich |
| `PlcAccessLevelProvider.SetPassword(PlcProtectionAccessLevel, SecureString)` | V21 manual („set the password for the specific Access Level Type“) | Reflection: genau ein öffentliches `void SetPassword` mit einem Enum namens `PlcProtectionAccessLevel` und einem `System.Security.SecureString` |
| `PlcAccessLevelProvider.ResetPassword(PlcProtectionAccessLevel)` | V21 manual („Reset the password for the specific Access Level Type“) | Reflection: genau ein öffentliches `void ResetPassword` mit diesem Enum |
| `PlcProtectionAccessLevel`-Member `None`, `FullAccess`, `ReadAccess`, `HMIAccess`, `NoAccess`, `FullAccessIncludingFailsafe` (`Siemens.Engineering.HW` laut V21-Typ-Baseline) | Siemens skill | Reflection: `Enum.Parse` auf dem Live-Parameter-/Eigenschaftstyp, exakter Name mit Beachtung der Groß-/Kleinschreibung |
| `Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider.Configuration` (get) | Siemens skill | Reflection, nur lesend |
| `PlcMasterSecretConfigurator` (Service der CPU, `Siemens.Engineering.HW.Features` laut V21-Typ-Baseline) | Siemens skill; V21 manual | Reflection: exakter Kurzname, Namespace beginnt mit `Siemens.Engineering`, eindeutig |
| `PlcMasterSecretConfigurator.Protect(SecureString)` | Siemens skill; V21 manual | Reflection: genau ein öffentliches `void Protect(SecureString)` |
| `ProtectAllPlcConfiguration()`, `ProtectAllPlcConfigurationWithPassword(SecureString)`, `UnprotectAllPlcConfiguration()` | V21 manual | Reflection: exakter Name und Parameterliste, `void` |
| `Unprotect()`, `Unprotect(SecureString)` | V21 manual (bis 2026-11-01 war für `Unprotect()` nur eine Sekundärquelle bekannt) | Reflection: exakter Name und Parameterliste, `void` |
| `ChangePassword(SecureString, SecureString)` | V21 manual | Reflection: exakter Name und Parameterliste, `void` |
| `MasterSecretConfiguration` (get) | V21 manual („Indicates the PLC protection type“; Typ undokumentiert) | Reflection: unter `observedMembers` gelesen, nicht interpretiert |
| `ICompilable.Compile()` über `DeviceItem.GetService<ICompilable>()` (Hardware übersetzen) | Siemens-Skill blocks; live verwendet von der Qualifizierungsprobe für IO-Systeme (Bericht vom 2026-09-26) | zur Compile-Zeit gebunden (existierender Stub `ICompilable`, `CompilerResult`) |
| Download-/Online-/Upload-Passwortkonfigurationen `ModuleReadAccessPassword`, `ModuleWriteAccessPassword`, `PlcMasterSecretPassword` (Download), `OnlineReadAccessPassword` (online), `ModuleReadAccessPassword`, `ModuleWriteAccessPassword`, `PasswordReadAccess` (Upload) | V21-Typ-Baseline (totally-integrated-claude `api-baselines/v21/base.json`) | nach Kurzname des Typs über die gemeinsamen Callback-Recorder beantwortet (`SetPassword(SecureString)`) |

Die Siemens-freie Bindung ist `PlcSecurityReflection` und die Orchestrierung `PlcSecurityRun`
(`TiaMcpServer.Contracts/Plc/Security`), gemeinsam genutzt vom Worker (`PlcSecurityService`) und den
FakeWorker-Szenarien `plc-security` und `plc-security-no-unprotect`.

## Weggelassen

- `PlcMasterSecretConfigurator.Reset()` („Removes PLC configuration data from the TIA Portal project and the PLC“):
  zerstörerisch über den Schutz hinaus und nicht angefordert; in TIA Portal erledigen.
- `MasterSecretConfiguration` interpretieren: Seine Werte sind nicht dokumentiert; es wird so gemeldet, wie gefunden.
- `PlcAccessControlConfigurationProvider.Configuration` ändern (UMAC / Zugriffssteuerung): hier nur lesend.
- UMAC-Rollen, Projektbenutzer und Engineering-Funktionsrechte (`UmacConfigurator`).
- Eine `create_project`-Option, die das Rezept automatisch anwendet.
- Ein Übersetzungsumfang `hardware_and_software` über `Device.GetService<ICompilable>()` (vom
  Siemens-Skill blocks dokumentiert, in diesem Repository nicht live verwendet); stattdessen `software` und `hardware` ausführen.
- Live-Verifikation jedes obigen Members.
