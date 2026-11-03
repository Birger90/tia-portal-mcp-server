# PLC-Online-Operationen über die S7-1500 Web API (`plc_online_read`, `plc_online_write`)

**Sprache:** [English](PLC_ONLINE_WEBAPI_SUMMARY.md) · Deutsch

> **Nicht live getestet.** Dieser Bereich wurde nur offline gebaut und qualifiziert: Weder eine
> S7-1500-CPU noch eine PLCSIM-Advanced-Instanz mit aktiviertem Webserver war erreichbar. Die
> offizielle Siemens-Clientbibliothek läuft in den Tests unverändert, jedoch gegen einen geskripteten
> HTTP-Handler, nicht gegen eine PLC. Jedes verwendete Client-Member ist mit seiner Fundstelle im
> Quelltext in der [Herkunftstabelle](#herkunft) aufgeführt. Vor dem Einsatz dieser Tools die
> [Schritte der Live-Abnahme](#schritte-der-live-abnahme) auf einer Test-CPU ausführen und einen
> ersten Lauf nie auf eine Maschine in Produktion richten.

Zwei Tools beobachten und ändern reale S7-1500-CPUs online über die SIMATIC S7-1500 Web API
(JSON-RPC über HTTPS). Sie verwenden den offiziellen .NET-Client von Siemens, das NuGet-Paket
[`Siemens.Simatic.S7.Webserver.API`](https://www.nuget.org/packages/Siemens.Simatic.S7.Webserver.API)
**3.3.92** (MIT, Quelle `siemens/simatic-s7-webserver-api`), das der Host direkt referenziert. Sie
laufen im Host-Prozess: kein Worker, kein TIA Portal, keine Projektbindung. Die einzige Ausnahme ist
`plc_online_read` `read_watch_table_live`, das zuerst eine Tabelle des gebundenen Projekts über den
Openness-Worker liest (siehe [unten](#live-werte-einer-offline-tabelle-read_watch_table_live)).

| Tool | Modi | Zweck |
|---|---|---|
| `plc_online_read` | read-only, read-write, full | Bis zu 50 voneinander unabhängige Beobachtungen konfigurierter PLCs (Betriebszustand, Variablen, Diagnose, Meldungen, Module, Syslog, ...). |
| `plc_online_write` | read-write, full | Bis zu 50 geordnete Online-Änderungen (Betriebsart, Variablenwerte, Meldungsquittierung, LED-Blinken, Systemzeit, Sicherung und Wiederherstellung) über die abgesicherte Einzelaufruf-Pipeline; eine Bestätigung pro tatsächlichem read-write-Aufruf. |

Die Tool-Anzahlen des Servers, diese beiden eingeschlossen, betragen 10 (read-only), 27 (read-write)
und 27 (full).

## Voraussetzungen

- Eine S7-1500-CPU, deren **Webserver in ihrer Hardwarekonfiguration** in TIA Portal **aktiviert
  ist**, die vom Rechner, auf dem der MCP-Server läuft, über HTTPS erreichbar ist und deren
  Hardwarekonfiguration geladen wurde. Die README der Clientbibliothek ordnet Client 3.3.x der
  S7-1500-Firmware **4.1.x** zu (Client 3.0.x der 4.0.x, 2.2.x der 3.1.x, 2.1.x der 3.0.x); eine CPU
  mit älterer Firmware kann neuere Methoden mit dem Web-API-Fehler -32601 (`MethodNotFound`)
  beantworten, gemeldet als `webapi_rejected`. Der Quelltext nennt keine Mindestfirmware pro Methode
  (siehe Herkunftstabelle).
- **Benutzer und Rechte** stammen aus der Benutzerverwaltung des CPU-Webservers in TIA Portal. Jede
  Methode der Web API benötigt das passende Recht für den angemeldeten Benutzer (oder für den anonymen
  Benutzer, wenn für das Ziel kein Benutzer konfiguriert ist). Ein fehlendes Recht ergibt
  `webapi_permission_denied`. Nur das gewähren, was die KI tun können soll;
  `plc_online_read permissions` listet die Rechte des konfigurierten Benutzers.
- S7-PLCSIM Advanced: Das Clientpaket führt PLCSIM und PLCSIM Advanced unter seinen Tags auf, der
  Quelltext sagt jedoch nichts weiter. Eine Instanz ist nur erreichbar, wenn bei ihrer CPU der
  Webserver aktiviert ist und sie an einen TCP/IP-Netzwerkadapter angebunden ist (nicht an den
  Softbus). Als nicht verifiziert behandeln.
- Für Details der Web API selbst (Rechte pro Methode, Sitzungsgrenzen, Zertifikatsbehandlung auf der
  CPU) verweist die Client-README auf das Funktionshandbuch *SIMATIC S7-1500 Web server*
  (Siemens-Support-Eintrag 59193560).

## Konfiguration

Der Mensch konfiguriert die PLCs; die KI nennt sie nur beim Namen. Den Server mit
`--plc-webapi-config <absolute path>` starten (oder `TIA_MCP_PLC_WEBAPI_CONFIG` setzen; die Option hat
Vorrang). Der Pfad wird einmal beim Start aufgelöst. **Eine ungültige Datei verhindert den Start**,
wobei jedes Problem aufgelistet wird; `tia-mcp doctor --plc-webapi-config <path>` meldet dieselben
Probleme, ohne den Server zu starten. Ohne Datei sind die Tools trotzdem registriert, und jedes Item
antwortet mit `target_not_found` und diesem Hinweis.

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

| Member | Erforderlich | Bedeutung |
|---|---|---|
| `name` | ja | Was Tools als `target` übergeben: exakt, Groß-/Kleinschreibung beachtend, eindeutig, 1-64 Zeichen ohne Steuerzeichen oder umgebende Leerzeichen. |
| `address` | ja | Hostname, IPv4- oder in eckige Klammern gesetzte IPv6-Adresse, optional `:port`. Kein Schema, Pfad, Query- oder Benutzerteil: Die Web API ist immer `https://<address>/api/jsonrpc`. |
| `userEnv`, `passwordEnv` | beide zusammen oder keines | **Namen** von Umgebungsvariablen des MCP-Server-Prozesses, die Benutzer und Passwort des Webservers enthalten. Für den anonymen Benutzer beide weglassen. |
| `certificate.validation` | ja | `system`, `thumbprint` oder `none` (siehe unten). |
| `certificate.thumbprint` | nur bei `thumbprint` | SHA-1- (40 Hex-Ziffern) oder SHA-256-Fingerabdruck (64 Hex-Ziffern) des Serverzertifikats der PLC; Leerzeichen und Doppelpunkte werden ignoriert. |
| `requestTimeoutSeconds` | nein | Pro HTTP-Anfrage, 1-600, Standard 30. Ein Sicherungs-Download ist eine einzige Anfrage; für große Sicherungen erhöhen. |

Die Datei wird strikt geparst: nur Member-Namen in camelCase, keine Kommentare, keine unbekannten
Member, höchstens 64 Ziele und 1 MiB.

**Zugangsdaten** erscheinen nie in der Datei, in Tool-Argumenten, in Ergebnissen, in Logs oder in
Audit-Einträgen. Die Variablen in den `env`-Block des Servereintrags im MCP-Client eintragen (siehe
[Client-Konfiguration](../guides/mcp-client-configuration.md#plc-web-api-targets)). Sie werden gelesen,
wenn sich der Server anmeldet, nicht beim Start; eine fehlende Variable lässt das Item mit
`webapi_authentication_failed` fehlschlagen, wobei nur die Variable genannt wird.

**Zertifikatsprüfung** wird pro Ziel gewählt und auf den eigenen HTTP-Handler dieses Ziels
angewendet; der prozessweite `ServerCertificateCallback` der Clientbibliothek wird nie gesetzt.

| `validation` | Verhalten |
|---|---|
| `system` | Normale Plattformprüfung (Kette, Hostname, Gültigkeit). Das PLC-Zertifikat oder seine CA im vertrauenswürdigen Speicher des Kontos installieren, unter dem der Server läuft. |
| `thumbprint` | Akzeptiert genau das Zertifikat mit dem hinterlegten Fingerabdruck, unabhängig von Kette oder Name, und sonst nichts. |
| `none` | Keine Prüfung für dieses Ziel. Jedes Ergebnis für das Ziel trägt eine Warnung, und doctor warnt: Jeder auf dem Netzwerkpfad könnte sich als die PLC ausgeben und die Zugangsdaten lesen. |

Verbindungen sind direkt (System-Proxyeinstellungen werden ignoriert, sodass Zugangsdaten nie über
einen Proxy laufen), und Weiterleitungen werden nicht verfolgt.

## Sitzungen

- **Anonymes Ziel** (kein `userEnv`): Es wird kein `Api.Login` gesendet; Anfragen tragen kein Token,
  und die CPU wendet die Rechte ihres anonymen Webserver-Benutzers an (`ApiAuthenticationMode.Disabled`
  im Client-Quelltext beschreibt den Fall, in dem "only Anonymous user is available").
- **Authentifiziertes Ziel**: Der erste Aufruf meldet sich an (`Api.Login`) und behält das Token für
  spätere Aufrufe. Lehnt die PLC eine **einzelne Anfrage** mit dem Web-API-Fehler 2
  (`PermissionDenied`) oder HTTP 401/403 ab, ist das Token möglicherweise abgelaufen; der Server meldet
  sich deshalb ab (nach bestem Bemühen), meldet sich mit frisch gelesenen Zugangsdaten erneut an und
  wiederholt diese Anfrage **einmal**; eine Anfrage, die die PLC wegen fehlender Berechtigung ablehnt,
  führt sie nicht aus. Eine zweite Ablehnung ergibt `webapi_permission_denied`. Aufrufe, die aus
  mehreren Anfragen bestehen, werden nie als Ganzes wiederholt: `create_backup` darf seine erste
  Anfrage (`Plc.CreateBackup`) wiederholen, nie aber den Ticket-Download, und `restore_backup`
  wiederholt nichts.
- Ein HTTP-Client und eine Sitzung pro Ziel, einmal erzeugt, auch wenn mehrere Aufrufe gleichzeitig
  beginnen. Aufrufe an ein Ziel laufen **nacheinander**, Anmeldung und erneute Anmeldung eingeschlossen
  (die Clientbibliothek hält das Token auf dem gemeinsamen HTTP-Client), sodass ein langer Aufruf wie
  eine Wiederherstellung andere Aufrufe an dieses Ziel verzögert; das Warten ist abbrechbar.
  Verschiedene Ziele laufen unabhängig voneinander. Alle Sitzungen werden beim Herunterfahren des Hosts
  abgemeldet (begrenzt auf 5 Sekunden).
- Items eines Aufrufs laufen der Reihe nach. `plc_online_write`-Aufrufe laufen nacheinander (eine
  hostweite Lease); Lesezugriffe warten nur auf das Ziel, das sie mit einem laufenden Aufruf teilen.

## `plc_online_read`

Jedes Item nimmt `operationId`, `operation` und `target`. Member, die eine Operation nicht verwendet,
müssen weggelassen werden. Items sind unabhängig: Ein fehlgeschlagenes Item hält die anderen nicht an.

| Operation | Member | Client-Aufruf (Web-API-Methode) | Ergebnis |
|---|---|---|---|
| `ping` | - | `ApiPingAsync` (`Api.Ping`) | `{ id }` so, wie die PLC es zurückgibt |
| `version` | - | `ApiVersionAsync` (`Api.Version`) | `{ apiVersion }` |
| `permissions` | - | `ApiGetPermissionsAsync` (`Api.GetPermissions`) | `{ permissions: [name] }` des Benutzers der Sitzung |
| `read_operating_mode` | - | `PlcReadOperatingModeAsync` (`Plc.ReadOperatingMode`) | `{ operatingMode }`, z. B. `run`, `stop`, `startup`, `hold`, `stop_fwupdate` |
| `read_mode_selector` | - | `PlcReadModeSelectorStateAsync` (`Plc.ReadModeSelectorState`) | `{ modeSelector }`: `run`, `stop`, `no_switch`, `unknown` |
| `read_cpu_type` | - | `ApiGetPlcCpuTypeAsync` (`Plc.ReadCpuType`) | `{ productName, orderNumber }` |
| `read_runtime_information` | - | `PlcReadRuntimeInformationAsync` (`Plc.ReadRuntimeInformation`) | Lastanteile in Prozent und Zykluszeiten in Millisekunden |
| `read_system_time` | - | `PlcReadSystemTimeAsync` (`Plc.ReadSystemTime`) | `{ timestamp }` (UTC) |
| `read_project_information` | - | `ProjectReadInformationAsync` (`Project.ReadInformation`) | `{ projectName, versions }` |
| `browse_tags` | `var`, `browseMode` (`children` Standard, `var`), `type` (`code_blocks`) | `PlcProgramBrowseAsync` / `PlcProgramBrowseCodeBlocksAsync` (`PlcProgram.Browse`) | `{ count, variables }` (name, datatype, hasChildren, dbNumber, address, area, readOnly, maxLength, arrayDimensions) oder `{ count, blocks }` |
| `read_tags` | `names` (1-100), `representation` (`simple` Standard, `raw`) | `PlcProgram.Read`, eine Anfrage pro Name | `{ requested, read, tags: [{ name, success, value, failureCategory, failureMessage, webApiError }] }` |
| `read_diagnostic_buffer` | `count` (1-1000, Standard 50), `language` (Standard `en-US`) | `ApiDiagnosticBufferBrowseAsync` (`DiagnosticBuffer.Browse`) | Kopfzähler und Einträge (Zeitstempel, Status, Texte, Ereignis-IDs) |
| `browse_alarms` | `count` (1-1000) **oder** `alarmId`, `language` (Standard `en-US`) | `ApiAlarmsBrowseAsync` (`Alarms.Browse`) | Kopfzähler und Einträge (ID, Meldungsnummer, Status, Zeitstempel, Erzeuger, hwid, Quittierung, Texte) |
| `browse_modules` | `hwid`, `browseMode` (`children`, `node`) | `ModulesBrowseAsync` (`Modules.Browse`) | `{ nodes }`-Baum (hwid, name, class, type, subType, attributes, parent, children; höchstens 32 Ebenen) |
| `read_module_status` | `hwid` (erforderlich), `language` | `ModulesReadStatusAsync` (`Modules.ReadStatus`) | `{ own, subordinate, messages, language }` |
| `read_module_leds` | `hwid` (erforderlich) | `ModulesReadLedsAsync` (`Modules.ReadLeds`) | `{ hwid, leds: [{ type, status, colors, periodMs }] }` |
| `browse_syslog` | `count` (1-1000, Standard 50), `first` | `ApiSyslogBrowseAsync` (`Syslog.Browse`) | `{ countTotal, countLost, entries: [raw] }` |
| `read_watch_table_live` | `table` (eine Beobachtungstabelle) **oder** `tagTable` (eine PLC-Variablentabelle) des gebundenen Projekts; optional `plcName`, `folderPath` (Gruppe der Beobachtungstabelle oder Ordner der Variablentabelle), `count` (Einträge, 1-1000, Standard 100), `representation` | Openness `read_watch_table` / `list_tag_tables` (Worker), dann `PlcProgram.Read` pro eindeutigem zugeordnetem Namen, der `read_tags`-Pfad | `{ target, plcName, deviceName, sourceKind, tableName, folderPath, entryCount, entriesComplete, mapped, unmapped, read, entries: [{ index, name, address, webApiName, mapping, unmappedReason, success, value, failureCategory, failureMessage, webApiError }], mappingRules, notes }` |

Hinweise:

- `read_tags`-Werte sind das `result`-Member genau so, wie die PLC es gesendet hat: Die Anfrage wird
  von `ApiRequestFactory.GetApiPlcProgramReadRequest` des Clients erstellt und von seinem
  `SendPostRequestAsync` gesendet, dem Pfad, den `PlcProgramReadAsync<T>` nimmt, der Wert wird aber
  nicht von Newtonsoft konvertiert (das datumsähnliche Zeichenketten umdeuten würde). Ein abgelehnter
  Name (zum Beispiel Fehler 200, Adresse existiert nicht) wird festgehalten und der nächste Name
  gelesen; der Verlust der Verbindung oder der Sitzung beendet das Item. Das Item ist nur erfolgreich,
  wenn jeder Name gelesen wurde.
- Namen werden so übergeben, wie die Web API sie erwartet (`"DB_Line".Speed`, `"Motor_On"`); zuerst
  browsen, um die genaue Schreibweise zu erfahren.
- `read_watch_table_live` wird unten in einem eigenen Abschnitt beschrieben.
- Aufzählungswerte sind die Enum-Member-Namen der Clientbibliothek in snake_case (die Benennung, die
  die Bibliothek selbst für ihre Konverter verwendet); Zeitstempel sind ISO 8601 UTC.
- Ergebnisse folgen der gemeinsamen Lese-Hülle: `{ tool, contractVersion: "1.0", success, error, warnings, batch }`.
  Ein fehlgeschlagenes Item trägt `failure { category, message }` und, wenn die PLC einen gesendet
  hat, `result.webApiError { code, name, plcMessage, httpStatus }`. Übergroße Werte werden nach den
  üblichen Budgetregeln weggelassen (`--max-item-chars`, `--max-document-chars`).

## Live-Werte einer Offline-Tabelle (`read_watch_table_live`)

> **Nicht live getestet.** Weder das Lesen der Beobachtungstabelle über Openness noch das Lesen über
> die Web API wurde gemeinsam gegen ein reales Projekt und eine reale CPU ausgeführt; der FakeWorker
> und der In-Memory-Ersatz der Web API bilden nur die Form der Nutzdaten nach.

Ein `plc_online_read`-Item verbindet eine Offline-Tabelle des gebundenen Projekts mit Online-Werten:

1. `target` wird zuerst in der Konfiguration nachgeschlagen (`target_not_found`, sonst läuft nichts).
2. Die Projektbindung wird wie bei jedem Lesen von Inhalten geprüft: Ohne verifizierte Bindung ergibt
   sich `binding_conflict`, und nichts wird gelesen. Die Tabelle wird offline über die vorhandenen
   Worker-Methoden `read_watch_table` (Einträge `{ name, address, ... }`, höchstens `count`) oder
   `list_tag_tables` (`tagTable`: ihre Variablen, dann ihre Anwenderkonstanten, höchstens `count`
   Einträge) gelesen. Der Variablenbestand der PLC wird ebenfalls gelesen (`list_tag_tables`), als
   Nachweis für die Zuordnung unten. Ein weggelassener `plcName`, auf den mehrere PLCs passen, ergibt
   `target_ambiguous`.
3. Jeder Eintrag wird nur durch eine belegte Regel einem Web-API-Namen zugeordnet (das Ergebnis
   listet sie als `mappingRules`):
   - `tagTable`: Eine Variable einer Variablentabelle wird als `"Name"` gelesen, die Form
     `ApiPlcProgramData.GetNameWithQuotes` des Clients für eine Variable auf Wurzelebene
     (Models/ApiPlcProgramData.cs 230) und das Beispiel `"Motor_On"` dieser Referenz.
   - `plcTag`: Ein Eintrag der Beobachtungstabelle, dessen Name (ohne Beachtung der
     Groß-/Kleinschreibung) einer Variablen derselben PLC entspricht, ohne oder in einem Paar doppelter
     Anführungszeichen, wird als `"Name"` in der Schreibweise des Bestands gelesen.
   - `webApiNotation`: Ein Name der Beobachtungstabelle, der bereits ein dokumentierter Web-API-Name
     ist, wird unverändert gesendet: in doppelte Anführungszeichen gesetzte Segmente, durch Punkte
     verbunden, jedes optional gefolgt von einer ganzzahligen Indexliste (`"DataTypes"."Bool"`,
     `"DB"."Arr"[1,2]`: README.md 193-216 und `GetVarNameForMethods` /
     `BuildChildrenFromArrayDimensions`, Models/ApiPlcProgramData.cs 83-140 und 241), oder ein in
     Anführungszeichen gesetztes Segment, gefolgt von genau einem einfachen Bezeichner
     (`"DB_Line".Speed`, das Beispiel dieser Referenz).
   - Alles andere ist **nicht zugeordnet** und wird nie gesendet: absolute Adressen (`%MW10`; keine
     dokumentierte Web-API-Notation), Namen mit `#` oder einem eingebetteten Anführungszeichen (kein
     dokumentiertes Escaping), mehrteilige Namen ohne Anführungszeichen (`DB_Line.Speed`), mehr als ein
     Member ohne Anführungszeichen (`"DB".Struct.Member`; nur eine Ebene ist dokumentiert, die Member in
     Anführungszeichen schreiben), ein Index nach einem Member ohne Anführungszeichen,
     Anwenderkonstanten und Einträge ohne Namen. Jeder davon trägt `unmappedReason`.
4. Eindeutige zugeordnete Namen werden in Eintragsreihenfolge über dieselbe `PlcProgram.Read`-Schleife
   wie bei `read_tags` gelesen (ein abgelehnter Name, zum Beispiel Fehler 200, wird festgehalten und der
   nächste gelesen; eine verlorene Verbindung oder Sitzung beendet den Vorgang). Nicht erreichte
   Einträge tragen `success:false` und eine Meldung.

Das Item ist erfolgreich, wenn jeder zugeordnete Name gelesen wurde; nicht zugeordnete Einträge lassen
es nicht fehlschlagen, fügen aber eine Warnung mit ihrer Anzahl hinzu. Ein fehlgeschlagener
Lesevorgang macht das Item `failed`, wobei das Ergebnis weiterhin vorhanden ist, wie bei `read_tags`.
Das Format von `PlcWatchTableEntry.Name` ist von Siemens nicht dokumentiert, weshalb nur diese Regeln
gelten. `target` und `plcName` werden vom Aufrufer gepaart: Der Server prüft nicht, ob auf der CPU
hinter `target` das Programm dieses Projekts läuft.

## `plc_online_write`

Jedes Item nimmt `operationId`, `operation` und `target`; `dryRun` ist ein Wurzelargument.

| Operation | Member | Client-Aufruf (Web-API-Methode) | Guards |
|---|---|---|---|
| `change_operating_mode` | `operatingMode`: `run` oder `stop` (die einzigen Betriebsarten, die Client und Web API akzeptieren) | `PlcRequestChangeOperatingModeAsync` (`Plc.RequestChangeOperatingMode`) | `operating_mode_stop` bei `stop` |
| `write_tags` | `tags` (1-100 `{ name, value }`), `representation` (`simple` Standard, `raw`) | `PlcProgramWriteAsync` (`PlcProgram.Write`), eine Anfrage pro Variable | - |
| `acknowledge_alarm` | `alarmId` (aus `browse_alarms`) | `AlarmsAcknowledgeAsync` (`Alarms.Acknowledge`) | - |
| `flash_leds` | `hwid` | `ModulesFlashLedsAsync` (`Modules.FlashLeds`) | - |
| `set_system_time` | `timestamp`: RFC 3339 mit `Z` oder einem Offset, 1970-01-01 bis 2554-07-21 (die eigene Bereichsprüfung des Clients) | `PlcSetSystemTimeAsync` (`Plc.SetSystemTime`) | - |
| `create_backup` | `path`: absolute lokale Datei in einem vorhandenen Ordner; `overwrite` (Standard `false`: eine vorhandene Datei ist ein `validation_error`) | `PlcCreateBackupAsync` (`Plc.CreateBackup`), dann `DownloadTicketAsync` und `ApiCloseTicketAsync` | - |
| `restore_backup` | `path`: absolute lokale Sicherungsdatei (höchstens 1 GiB) | `ApiBackupHandler.RestoreBackupAsync` (`Api.Browse`, `Plc.RestoreBackup`, Ticket-Upload, `Api.Ping`, `Api.Logout`/`Api.Login`) | `restore_backup_replaces_program` |

Sicherungs- und Wiederherstellungspfade müssen absolute Pfade sein, die eine Datei bezeichnen: unter
Windows ein Laufwerkspfad (`C:\...`) oder eine UNC-Freigabe (`\\server\share\...`, seit 2026-11-01);
Gerätepfade (`\\?\`, `\\.\`, `\??\`) und Pfade mit Steuer- oder Unicode-Formatzeichen werden
abgelehnt, bevor irgendetwas das Dateisystem berührt.

`online_changes_real_controller` wird bei **jedem** Aufruf ausgelöst.

- `write_tags` sendet den JSON-Wert des Aufrufers unverändert (eine Ganzzahl bleibt eine Ganzzahl;
  eine datumsähnliche Zeichenkette bleibt eine Zeichenkette; höchstens 4.096 Zeichen pro Wert). Es hält
  bei der ersten fehlgeschlagenen Variable an; frühere Variablen bleiben geschrieben, und das `result`
  des fehlgeschlagenen Items listet jede versuchte Variable.
- `create_backup` schreibt die Sicherung über eine temporäre Datei im Zielordner und verschiebt sie an
  ihren Platz; das Ergebnis enthält Pfad, Größe und SHA-256. Eine Datei, die zwischen Planung und
  Schreiben auftaucht, lässt das Item bei `overwrite:false` mit `state_changed` fehlschlagen, bevor die
  PLC angefragt wird.
- `restore_backup` erfordert ein Ziel mit `userEnv`/`passwordEnv`: Der Wiederherstellungshelfer des
  Clients verwendet das Passwort dieses Benutzers als Legitimation für die Wiederherstellung und meldet
  sich nach jedem Neustart der PLC erneut an. Größe und SHA-256 der Datei sind Teil der geplanten
  Wirkung und werden unmittelbar vor dem Upload erneut geprüft (zuerst die Größe: eine Datei über
  1 GiB wird abgelehnt, ohne sie zu lesen). Der Helfer kann die PLC zuerst in den
  Wiederherstellungsmodus versetzen (ein Neustart), dann hochladen, auf jeden Neustart warten (drei
  Minuten, der Standard des Clients) und sich erneut anmelden; das ganze Item ist auf zehn Minuten
  begrenzt. Danach `read_operating_mode` und den Diagnosepuffer prüfen.

Die Antwort ist `{ tool, contractVersion: "1.0", phase, success, error, warnings, guards, effects, batch }`.
`effects` listet pro Item Ziel, Adresse, Zertifikatsprüfung, Art der Authentifizierung, Web-API-Methode,
eine einzeilige Beschreibung und die Eingaben der Operation (Variablen mit Werten, Zeitstempel in UTC,
Sicherungsdatei mit Größe und Hash). Es gibt keinen Verifikations-Lesevorgang: Ein akzeptiertes
`Plc.RequestChangeOperatingMode` bedeutet, dass die PLC die Anforderung angenommen hat; die Betriebsart
lesen, um zu sehen, wann die CPU sie erreicht hat.

## Zugriff, Bestätigung und Audit

| Modus | `plc_online_read` | `plc_online_write` |
|---|---|---|
| read-only | registriert (`Observe`) | nicht registriert; ein Aufruf ist ein MCP-Fehler "unknown tool" |
| read-write | registriert | registriert (`OnlineControl`); **jeder tatsächliche Aufruf fragt den Benutzer einmal** über MCP-Form-Elicitation |
| full | registriert | registriert; läuft unter Policy ohne Rückfrage |

`plc_online_write` verwendet die abgesicherte Einzelaufruf-Pipeline mit `ConfirmsEveryCall = true`,
wie die Lifecycle-Tools und `plc_download`:

1. Die Argumentform (unbekannte Member, Nullwerte, falsche Typen) wird abgelehnt, bevor das Tool
   läuft, ohne Audit. Dann werden die Items validiert (1-50 eindeutige `operationId`s, Member pro
   Operation).
2. Die Planung erfolgt **offline**: Nachschlagen des Ziels in der Konfiguration (`target_not_found`
   lässt den ganzen Aufruf fehlschlagen), Normalisierung der Eingaben und lokale Dateiprüfungen. Ein
   `dryRun` endet hier mit `phase: preview`, zeigt die Wirkungen und Guards und kontaktiert nie eine
   PLC.
3. In read-write zeigt eine Rückfrage zuerst die Acknowledge-Guards, je eine Zeile, dann eine Zeile
   pro Item in Reihenfolge: die Wirkung, das Ziel, seine Adresse und die Web-API-Methode und zuletzt
   die `operationId` des Aufrufers. Jeder vom Aufrufer gelieferte Wert darin (Variablennamen und
   -werte, Meldungs-IDs, Pfade, Zielnamen, Operations-IDs) wird als JSON-String-Literal zitiert, wobei
   Steuer-, Zeilentrenner- und Unicode-Formatzeichen (bidirektionale und solche mit Nullbreite
   eingeschlossen) escaped werden, sodass kein Wert eine Zeile hinzufügen oder den Text umordnen kann.
   Ein Variablenwert wird bis zu 512 Zeichen JSON vollständig angezeigt, andernfalls als seine Länge
   und der SHA-256 seines JSON-Texts (mit dem Wert in den Wirkungen einer `dryRun`-Vorschau
   vergleichen). Eine `operationId` mit solchen Zeichen wird vor der Planung als `validation_error`
   abgelehnt, wie in jedem Tool, das jeden Aufruf bestätigt. Nur `accept` mit `confirm: true` setzt
   fort; Ablehnen, Abbrechen, Zeitüberschreitung, ein fehlgeschlagener Austausch oder ein Client ohne
   Form-Elicitation ergibt `access_denied`, und nichts wird gesendet. Nach der Annahme wird der Plan
   erneut berechnet und muss identisch sein (zum Beispiel der Hash der Wiederherstellungsdatei oder ein
   Sicherungsziel, das inzwischen aufgetaucht ist); andernfalls wird der Aufruf abgelehnt und nichts
   gesendet. In full werden Acknowledge-Guards durch die Policy erfüllt.
4. Items laufen der Reihe nach; der erste Fehler beendet den Aufruf, spätere Items sind `skipped`,
   nichts wird zurückgerollt, und ein Fehler bei mehreren Items trägt `partial_write_no_rollback`.
5. Ein Audit-Eintrag pro begonnenem Aufruf (Vorschauen und Ablehnungen eingeschlossen) wird an den
   gemeinsamen Audit-Strom (`writes-yyyy-MM-dd.jsonl` unter `%LOCALAPPDATA%\TiaMcpServer\audit`)
   angehängt, mit `recordKind: "plc_online"`, `projectPath: null` und `binding: null`. Er enthält die
   angeforderten Items (Variablenwerte, Meldungs-IDs, Dateipfade), die Antwort und ihren Hash, die
   Guards mit ihrer Erfüllung (`user` oder `policy`) und das Ergebnis der Bestätigung; er enthält nie
   Zugangsdaten.

| Guard | Schweregrad | Wird ausgelöst, wenn |
|---|---|---|
| `online_changes_real_controller` | acknowledge | Jeder Aufruf: Die Änderung wirkt sofort auf die laufende Maschine, und nichts wird zurückgerollt. |
| `operating_mode_stop` | acknowledge | `change_operating_mode` mit `stop`: Das Anwenderprogramm stoppt, und die Ausgänge nehmen ihr konfiguriertes STOP-Verhalten an. |
| `restore_backup_replaces_program` | acknowledge | `restore_backup`: Die PLC wird gestoppt und neu gestartet, und ihr Programm, ihre Konfiguration und ihre Daten werden ersetzt. |

Es gibt keine Block-Guards; ungültige Eingaben sind ein `validation_error`.

## Fehlerkategorien

| Kategorie | Bedeutung |
|---|---|
| `target_not_found` | `target` ist nicht in der Konfiguration (oder es ist keine Datei konfiguriert). Lese-Items schlagen einzeln fehl; ein Schreibaufruf schlägt als Ganzes fehl. |
| `validation_error` | Ungültige Eingabe (auch eine vorhandene `create_backup`-Datei ohne `overwrite`, ein fehlender Ordner, ein anonymes `restore_backup`-Ziel, ein Kulturname, den die Plattform nicht kennt). |
| `webapi_unreachable` | Verbindungs-, DNS- oder TLS-Fehler (einschließlich eines unter `system` abgelehnten Zertifikats oder eines nicht übereinstimmenden Fingerabdrucks unter `thumbprint`), die Zeitüberschreitung pro Anfrage oder eine Wiederherstellung, die nicht rechtzeitig fertig wurde. |
| `webapi_authentication_failed` | `Api.Login` ist fehlgeschlagen (Fehler 100 `LoginFailed`, 102 `PasswordExpired` oder 105 während der Anmeldung), oder eine Zugangsdaten-Variable ist nicht gesetzt. |
| `webapi_permission_denied` | Web-API-Fehler 2 oder HTTP 401/403, auch noch nach einer erneuten Anmeldung: Dem Benutzer fehlt das Recht. |
| `webapi_rejected` | Jeder andere Web-API-Fehler (die Meldung enthält Code, Name und den Text der PLC; `result.webApiError` enthält sie strukturiert), ein anderer HTTP-Status, eine nicht lesbare Antwort oder eine Ablehnung durch die Clientbibliothek. |
| `state_changed` | Eine Sicherungsdatei ist nach der Planung aufgetaucht oder die Wiederherstellungsdatei hat sich geändert, oder eine Sicherung konnte lokal nicht geschrieben werden. |
| `access_denied` | Die read-write-Bestätigung wurde nicht gegeben (siehe oben), oder der Modus erlaubt die Operation nicht. |

Die Zuordnung verwendet den Fehlercode der PLC, wie ihn ein Wrapper um den bibliothekseigenen
`ApiResponseChecker` an der Exception festhält, den HTTP-Status und Exception-Typen, nie Meldungstext.
Die Texte der inneren `ApiException` und `InvalidHttpRequestException` (die die Anfrage enthalten
können) werden nie ausgegeben. Kommt das mit einem Aufruf gesendete Passwort irgendwo in der
resultierenden Meldung oder im Fehlertext der PLC vor, wird die ganze Meldung durch einen festen Text
mit dem Web-API-Fehlercode ersetzt (und der Text der PLC wird aus `webApiError` entfernt); nur die
Vorkommen zu schwärzen würde zeigen, wo ein kurzes Passwort vorkommt.

## Herkunft

Jedes verwendete Client-Member, aus einem schreibgeschützten Klon von
`siemens/simatic-s7-webserver-api` bei `3f334fa` ("Bump Microsoft.Extensions.Logging.Abstractions from
10.0.13 to 10.0.14 (#287)"; ein flacher Klon). Das Paket 3.3.92 wurde aus dem Commit `faac56e9` gebaut
(laut seiner nuspec), einen Abhängigkeits-Bump früher; der Server kompiliert gegen 3.3.92, was
bestätigt, dass jedes Member unten im Paket existiert. Pfade sind relativ zu `src/Webserver.API/`; `H`
ist `Services/RequestHandling/ApiHttpClientRequestHandler.cs`, `F` ist
`Services/RequestHandling/ApiRequestFactory.cs` (der Name der Web-API-Methode ist die Zeichenkette, die
in der angegebenen Zeile an `new ApiRequest(...)` übergeben wird). **Mindestfirmware:** Der Quelltext
nennt keine pro Methode; die einzige Aussage ist die Kompatibilitätstabelle der README (README.md
267-279: Client 3.3.x für S7-1500-Firmware 4.1.x) und der Hinweis von `ApiMethodNotFoundException`
(Exceptions/ApiMethodNotFoundException.cs 9: "check method name and PLC firmware version").

| Verwendet für | Client-Member (Zeile in H) | Web-API-Methode (Zeile in F) | Mindestfirmware im Quelltext |
|---|---|---|---|
| Request-Handler | `ApiHttpClientRequestHandler(HttpClient, IApiRequestFactory, IApiResponseChecker, IApiRequestSplitter, ILogger)` (85) | - | nicht angegeben |
| Sitzung | `ApiLoginAsync(user, password, includeWebApplicationCookie, ct)` (2924) | `Api.Login` (198) | nicht angegeben |
| Sitzung | `ApiLogoutAsync` (469) | `Api.Logout` (211) | nicht angegeben |
| `ping` | `ApiPingAsync` (487) | `Api.Ping` (247) | nicht angegeben |
| `version` | `ApiVersionAsync` (504) | `Api.Version` (478) | nicht angegeben |
| `permissions` | `ApiGetPermissionsAsync` (435) | `Api.GetPermissions` (223) | nicht angegeben |
| `read_operating_mode` | `PlcReadOperatingModeAsync` (563) | `Plc.ReadOperatingMode` (354) | nicht angegeben |
| `read_mode_selector` | `PlcReadModeSelectorStateAsync` (3542) | `Plc.ReadModeSelectorState` (391) | nicht angegeben |
| `read_cpu_type` | `ApiGetPlcCpuTypeAsync` (3865) | `Plc.ReadCpuType` (1394) | nicht angegeben |
| `read_runtime_information` | `PlcReadRuntimeInformationAsync` (4279) | `Plc.ReadRuntimeInformation` (1615) | nicht angegeben |
| `read_system_time` | `PlcReadSystemTimeAsync` (3033) | `Plc.ReadSystemTime` (767) | nicht angegeben |
| `read_project_information` | `ProjectReadInformationAsync` (4315) | `Project.ReadInformation` (1641) | nicht angegeben |
| `browse_tags` | `PlcProgramBrowseAsync(mode, string var, ct)` (622) | `PlcProgram.Browse` (262) | nicht angegeben |
| `browse_tags` `type: code_blocks` | `PlcProgramBrowseCodeBlocksAsync` (701) | `PlcProgram.Browse` mit `type: ["code_blocks"]` (276) | nicht angegeben |
| `read_tags` | `ApiRequestFactory.GetApiPlcProgramReadRequest` (F 303) + `SendPostRequestAsync(IApiRequest)` (128), der Pfad von `PlcProgramReadAsync<T>` (746) | `PlcProgram.Read` (308) | nicht angegeben |
| `write_tags` | `PlcProgramWriteAsync(string var, object value, mode, ct)` (863) | `PlcProgram.Write` (325) | nicht angegeben |
| `read_diagnostic_buffer` | `ApiDiagnosticBufferBrowseAsync` (3657) | `DiagnosticBuffer.Browse` (1205) | nicht angegeben |
| `browse_alarms` | `ApiAlarmsBrowseAsync` (3625) | `Alarms.Browse` (1176) | nicht angegeben |
| `acknowledge_alarm` | `AlarmsAcknowledgeAsync` (3594) | `Alarms.Acknowledge` (1133) | nicht angegeben |
| `browse_modules` | `ModulesBrowseAsync(uint?, ModulesBrowseMode?, ct)` (3991) | `Modules.Browse` (1475) | nicht angegeben |
| `read_module_status` | `ModulesReadStatusAsync` (4240) | `Modules.ReadStatus` (1583) | nicht angegeben |
| `read_module_leds` | `ModulesReadLedsAsync` (4219) | `Modules.ReadLeds` (1556) | nicht angegeben |
| `flash_leds` | `ModulesFlashLedsAsync` (4198) | `Modules.FlashLeds` (1538) | nicht angegeben |
| `browse_syslog` | `ApiSyslogBrowseAsync` (3568) | `Syslog.Browse` (1118) | nicht angegeben |
| `read_watch_table_live` Werte | der `read_tags`-Pfad oben; kein weiteres Client-Member | `PlcProgram.Read` (308) | nicht angegeben |
| `read_watch_table_live` Namensregeln (nur als Nachweis, nicht aufgerufen) | `ApiPlcProgramData.GetNameWithQuotes` und `GetVarNameForMethods` (Models/ApiPlcProgramData.cs 230, 241), `BuildChildrenFromArrayDimensions` (83); README.md 193-216 | - | - |
| `change_operating_mode` | `PlcRequestChangeOperatingModeAsync` (586); nur run/stop laut `ApiRequestParameterChecker.CheckPlcRequestChangeOperatingMode` (Services/RequestHandling/ApiRequestParameterChecker.cs 120) | `Plc.RequestChangeOperatingMode` (378) | nicht angegeben |
| `set_system_time` | `PlcSetSystemTimeAsync` (3053); Bereich laut `CheckSystemTimeStamp` (ApiRequestParameterChecker.cs 260) | `Plc.SetSystemTime` (782) | nicht angegeben |
| `create_backup` | `PlcCreateBackupAsync` (3363), `DownloadTicketAsync(string)` (2676), `ApiCloseTicketAsync(string)` (319) | `Plc.CreateBackup` (973), `Api.CloseTicket` (96) | nicht angegeben |
| `restore_backup` | `ApiBackupHandler.RestoreBackupAsync` (Services/Backup/ApiBackupHandler.cs 90), das `PlcRestoreBackupAsync` (3383) und `ReLoginAsync` (2857; ApiBackupHandler.cs 139, 144) aufruft | `Plc.RestoreBackup` (988) | nicht angegeben |
| Fehlerzuordnung | `ApiResponseChecker.CheckHttpResponseForErrors` (ApiResponseChecker.cs 22, wirft `InvalidHttpRequestException` in 43) und `CheckResponseStringForErros` (53); `ApiErrorModel.ThrowAccordingException` (Models/Responses/ApiErrorModel.cs 26; `PermissionDenied` 41, `LoginFailed` 49, Standard `ApiException` 189); Codes in `Enums/ApiErrorCode.cs` | - | - |
| anonymer Benutzer | `ApiAuthenticationMode.Disabled` "Only Anonymous user is available" (Enums/ApiAuthenticationMode.cs 28); README.md 102 | - | - |
| Zertifikate (nicht verwendet) | `ServerCertificateCallback.CertificateCallback` ist statisch (Services/ServerCertificateCallback.cs 22), und `ApiStandardServiceFactory` wendet ihn auf jeden Client an, den sie erzeugt (Services/ApiStandardServiceFactory.cs 195-197); daher baut dieser Server statt der Factory einen eigenen `HttpClient` pro Ziel | - | - |

Was der Client bietet, diese Tools aber nicht bereitstellen: Redundanz-IDs (jeder Aufruf verwendet
`StandardPLC`; R/H-Systeme sind ungetestet), Filter für Diagnosepuffer und Meldungen,
`Modules.ReadParameters` sowie Identifikations- und Wartungsdaten, Zeiteinstellungen, Dateien,
Datenlogs, Webanwendungen, fehlersichere Objekte und Technologieobjekte, `Api.ChangePassword` und die
Bulk-API. Jede Operation, die der Auftrag verlangte, existiert im Client und ist bereitgestellt;
`browse_tags` bietet den einzigen Typfilter, den der Client hat (`code_blocks`).

## Diagnose

`tia-mcp doctor` hat die optionale Prüfung `plc-webapi-config`: Sie parst die Datei, die der Server
laden würde (Option, Umgebung oder keine), mit dem Lader des Servers, listet jedes Ziel (Name, Adresse,
Zertifikatsprüfung, Timeout, anonym oder Benutzer) und meldet bei einem Benutzer nur, ob die beiden
genannten Variablen gesetzt sind. Sie macht keinen Netzwerkaufruf. Ohne Datei besteht sie; die
Zertifikatsprüfung `none` oder eine nicht gesetzte Variable ergibt eine Warnung; eine ungültige Datei
lässt sie fehlschlagen.

## Schritte der Live-Abnahme

Auf einer Test-CPU (möglichst Firmware 4.1) mit einem Wegwerfprogramm in dieser Reihenfolge ausführen
und die Nachweise festhalten (Tool-Dokumente und Audit-Einträge):

1. `doctor --plc-webapi-config <file>`: Ziele aufgelistet, Variablen als gesetzt gemeldet.
2. `plc_online_read` `ping`, `version`, `permissions` für ein anonymes Ziel und für ein Benutzerziel
   (`permissions` mit den in TIA Portal konfigurierten Rechten vergleichen).
3. `read_operating_mode`, `read_mode_selector`, `read_cpu_type`, `read_runtime_information`,
   `read_system_time`, `read_project_information` mit der Online-Ansicht von TIA Portal abgleichen.
4. `browse_tags` (Wurzel, ein DB mit `browseMode children`, eine Variable mit `var`, `type code_blocks`),
   dann `read_tags` in `simple` und `raw` für Bool, Int, Real, String, DTL und einen unbekannten Namen;
   mit einer Beobachtungstabelle vergleichen.
5. `read_diagnostic_buffer` (`count`, `language`), `browse_alarms` (mit einer ausgelösten
   Testmeldung), `browse_modules`, `read_module_status`, `read_module_leds`, `browse_syslog`.
6. read-write: `dryRun` (keine Rückfrage, keine PLC-Anfrage), dann `change_operating_mode stop` (eine
   Rückfrage, CPU geht in STOP), `run` und eine abgelehnte Rückfrage (nichts ändert sich).
7. `write_tags` auf einem Test-DB (Ganzzahl, Real, Bool, String), einschließlich einer
   fehlschlagenden zweiten Variable; `acknowledge_alarm`, `flash_leds`, `set_system_time`.
8. `create_backup` (neue Datei, vorhandene Datei ohne und mit `overwrite`), dann `restore_backup`
   dieser Datei auf der Test-CPU. Insbesondere beobachten, ob die Ab- und erneute Anmeldung des
   Client-Helfers nach dem Neustart gelingt (siehe offene Risiken).
9. Zertifikate: `system` mit einem nicht vertrauenswürdigen Zertifikat schlägt mit
   `webapi_unreachable` fehl; der richtige `thumbprint` funktioniert, ein falscher schlägt fehl; `none`
   funktioniert und warnt.
10. Sitzung: über das Sitzungs-Timeout der CPU hinaus warten und erneut aufrufen (eine erneute
    Anmeldung); ein Recht entfernen und `webapi_permission_denied` bestätigen.
11. full-Modus: keine Rückfrage, Audit `confirmation.by` ist `policy`.
12. Eine PLCSIM-Advanced-Instanz an einem TCP/IP-Adapter mit aktiviertem Webserver, falls verfügbar.
13. `read_watch_table_live` mit einer Beobachtungstabelle, die eine Variable, ein DB-Member, ein
    Member einer verschachtelten Struktur, ein Array-Element und eine absolute Adresse enthält, und mit
    einer Variablentabelle: festhalten, welchen `name` Openness für jeden Eintrag meldet und welche
    Einträge zugeordnet sind, und die Werte mit der Beobachtungstabelle in TIA Portal online
    vergleichen. Dann ungebunden aufrufen (`binding_conflict`) und mit einem `target`, dessen CPU ein
    anderes Programm ausführt.

## Offene Risiken

- Überhaupt nicht live getestet (siehe Hinweiskasten oben).
- `read_watch_table_live`: Das Format der Eintragsnamen in Openness ist undokumentiert; weicht es von
  den zugeordneten Notationen ab, bleiben Einträge nicht zugeordnet (sicher, aber das Lesen ist weniger
  nützlich), bis die Regeln mit Live-Nachweisen erweitert werden.
- `restore_backup` verlässt sich auf den Helfer des Clients, der sich nach jedem Neustart ab- und
  wieder anmeldet (`ReLoginAsync`, `Api.Logout` mit dem Token von vor dem Neustart). Ob die CPU diese
  Abmeldung akzeptiert, ist nicht verifiziert; falls nicht, schlägt das Item fehl, obwohl die
  Wiederherstellung gelungen sein kann, und die Meldung fordert dazu auf, die PLC zu untersuchen.
  Sobald der Upload abgeschlossen ist, fragt die Warteschleife des Helfers für den Neustart in den
  Wiederherstellungsmodus synchron ab und verlässt sie nur über den erwarteten Ticket-Zustand, eine
  Exception oder einen Abbruch (ApiBackupHandler.cs 116), weshalb das Item auf zehn Minuten begrenzt
  ist.
- Der Client formatiert Zeitstempel für `Plc.SetSystemTime` mit dem Kalender der aktuellen Kultur
  (`ToString` mit festem Muster, `ApiRequestFactory.cs` 782). Der Adapter führt den Aufruf deshalb unter
  der invarianten Kultur aus (getestet unter de-DE, th-TH, ar-SA und fa-IR); keine andere
  Client-Methode, die Datumswerte formatiert, wird verwendet.
- Ein Passwort, das in einer Fehlermeldung vorkommt (auch zufällig, wie es bei einem kurzen geschehen
  kann), unterdrückt die ganze Meldung: Das Item zeigt dann nur die Kategorie und den
  Web-API-Fehlercode.
- Der Ablauf einer Sitzung wird nur an Fehler 2 erkannt; Sitzungslebensdauer und Grenzen der
  Sitzungsanzahl der CPU werden über eine Sitzung pro Ziel hinaus nicht verwaltet.
