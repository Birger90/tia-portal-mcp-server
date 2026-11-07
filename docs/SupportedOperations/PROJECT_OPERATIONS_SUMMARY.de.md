# TIA-Portal-Projekt- und Portal-Operationen

**Sprache:** [English](PROJECT_OPERATIONS_SUMMARY.md) · Deutsch

## Leseoperationen

| Einstiegspunkt | Operation | Eingaben und Verhalten |
|---|---|---|
| `get_project_status` | `get_project_status` | Keine Argumente; liest Status und Metadaten in jedem Modus, ohne zu öffnen, zu binden oder umzuschalten. |
| `browse_project_tree` | `browse_project_tree` | v3: typisierter `startSelector`, `depth` und `pageSize`; mit `cursor` wird ein unveränderlicher Snapshot fortgesetzt. |
| `compile_check` | `compile_check` | Optional `plcName`, `blockPath` und `scope` (`software` als Standard: das PLC-Programm oder ein Baustein, **keine Hardware**; `hardware`: die Hardwarekonfiguration des CPU-Device-Items jeder PLC, Bericht-`scope` `hardware`, nicht mit `blockPath` kombinierbar); liefert Compilermeldungen. Verfügbar in den Modi read-write und full. Siehe [CPU-Schutz](PLC_SECURITY_SUMMARY.md#hardware-compile-compile_check-scope-hardware). |

### Explizite Bindung (`bind_project`)

`bind_project(projectPath?:string, forceRebind:bool=false, action?:string, portalProcessId?:int,
serverAlias?:string, group?:object, serverProjectName?:string)` ist in allen drei Modi verfügbar.
Eine weggelassene/null-Action oder `action:"bind"` behält das bestehende, unten beschriebene Verhalten und die
JSON-Form bei. Sie ändert die Session-Auswahl des Servers, ohne ein Projekt zu öffnen, anzulegen, zu speichern oder
zu schließen. Nur `open_project`, `create_project` und `retrieve_project` öffnen Projekte; keine Anfrage öffnet implizit eines.
Read-only öffnet, legt an, speichert oder schließt nie, aber eine explizite Bindung kann seine Session umschalten.

Bei einer ungebundenen Session lässt man den Pfad weg, um das einzige geöffnete Projekt zu übernehmen, oder erhält
typisiert `target_not_found`/`target_ambiguous` plus Kandidaten. Für ein Standalone-Projekt wird ein angebotener
absoluter `.ap21`-Pfad angegeben, für eine lokale Session der `.amc21`-Engineering-Pfad des beobachteten typisierten
Owners. Eine optionale exakte `portalProcessId` unterscheidet das Portal eindeutig; eine Abweichung zwischen PID
und Pfad schlägt fail-closed fehl. `.als21`-Dateien und Inventarverzeichnisse sind keine Selektoren für Übernahme,
Status oder Start. Ein anderer konfigurierter, verifizierter oder zuletzt gebundener Pfad erfordert
`forceRebind:true`. Ein verifizierter Aufruf mit demselben Pfad bzw. ohne Pfad prüft den Status erneut, ohne
umzuschalten. Das erneute Anbinden kann den Openness-Zugriffsdialog von TIA anzeigen, den ein Mensch beantworten
muss. Worker-Ownership überdauert kein Detach; ein erneut angebundenes Projekt wird als UI-eigenes behandelt. Die
Bindung lässt UI-Projekte geöffnet, einschließlich ungespeicherter Änderungen. Ein geändertes headless Projekt, bei
dem dieser Worker der einzige Openness-Client ist, blockiert das Detach; dieser Schutz ist implementiert, aber durch
den Live-Lauf vom Oct3 nicht qualifiziert.

Der kanonische Standalone-Envelope (`tool`, `contractVersion:"1.0"`, `success`, `error`, `warnings`, `result`)
verwendet `StandaloneToolOutcome<ProjectBindingResult>`. Sein Wert enthält `transition` (`bound`, `unchanged`,
`switched` oder `none`), `binding` und `previousBinding` ungleich null, einen nullable `project`-Status mit
`metadata:null` und immer vorhandene `portals`. Der Bindungszustand ist `unbound`, `configured_unverified`,
`verified` oder `invalidated`; Pfad und Portal-PID können null sein. Portal-Einträge enthalten `processId`, ein
nullable `projectPath`, `hasUserInterface`, `isBound` und ein nullable `otherClientCount` (angebundene
Openness-Client-Sessions außer dem Worker dieses Servers, aus `TiaPortalProcess.AttachedSessions`; null, wenn nicht
lesbar).

Die Portal-Auflistung (`list_tia_portal_processes`, verwendet von `list_portals` und den Kandidatenlisten;
Roadmap-Zeile 21) liest `TiaPortal.GetProcesses()` und je Prozess `Id`, `ProjectPath`, `Mode` und
`AttachedSessions` (`TiaPortalSession.ProcessId`), alle in den Compile-Stubs deklariert. Sie ruft nie
`TiaPortalProcess.Attach()` auf und gibt nie ein Prozess-Handle frei. Quellen: Der Siemens-Skill
`licensing-and-firewall` (`tia-portal-ai-extensions`) verortet die Openness-Firewall-Abfrage beim Verbinden, also
beim Anlegen oder Anbinden eines `TiaPortal`; der Siemens-Skill `session-and-project` dokumentiert
`GetProcesses()`/`Attach()` und dass das Freigeben eines `GetProcesses()`-Handles in V21 die laufende Instanz
beendet; rickgaiser/TiaMcp (`src/TiaMcp.Openness/TiaConnection.cs`, `ListRunningInstances`, nur als Idee) liest
dieselben Member aus demselben Grund ohne Anbindung. Das Lesen von `AttachedSessions` auf einem nicht angebundenen
Prozess war bereits Teil der Prüfung des temporären Ziels beim expliziten `bind` (vor dessen `Attach()`); dass die
Auflistung allein keinen Dialog auslöst, ist nicht live getestet (Live-Fall L11 der
[V21-Fallstricke](V21_PITFALLS.md#live-acceptance-list)).

Das Starten eines TIA Portal mit Benutzeroberfläche (die zweite Hälfte von Zeile 21, rickgaisers
`tia_connect startPortal`) ist ausgelassen; starte TIA Portal selbst und verwende dann `open_project`. Gründe:

- `new TiaPortal(TiaPortalMode.WithUserInterface)` ist dokumentiert (Siemens-Skill `session-and-project`), aber die
  gestartete Instanz gehört dem Worker dieses Servers, den der Host nach einem Timeout oder Absturz neu startet. Ob
  sie das überlebt, ist nicht verifiziert, und die Quellen widersprechen sich: `totally-integrated-claude`
  `skills/tia-csharp-common` sagt, `Dispose()` einer UI-Instanz trenne nur die Verbindung, rickgaiser
  (`StartPortal`-Kommentar in `TiaConnection.cs`) berichtet, dass eine solche Instanz mit ihrem Owner geschlossen
  wird, einschließlich ungespeicherter Arbeit. Die Compile-Stubs haben außerdem keinen Konstruktor (`TiaPortal` ist
  dort abstrakt).
- rickgaisers eigener Weg startet `Bin\Siemens.Automation.Portal.exe` unterhalb des Installationspfads aus der
  Registry als eigenen Prozess, wartet bis zu fünf Minuten und bindet sich dann an. Das ist keine Openness-API; der
  Pfad der ausführbaren Datei stammt nur aus Nicht-Siemens-Quellen (rickgaiser, die Doctor-Probe von
  `totally-integrated-claude`). Die Wartezeit plus das Öffnen des Projekts kann das Standard-Timeout für
  Worker-Anfragen (5 Minuten) überschreiten, und das folgende `Attach()` kann den Openness-Zugriffsdialog auslösen,
  den ein Mensch beantworten muss.
- In beiden Fällen ist es eine neue Lifecycle-Oberfläche (read-write-Bestätigung, Audit, Dry Run), die hier auf V21
  nicht geprüft werden kann.

Fehlerhafte Eingaben und nicht erzwungene Anfragen mit fremdem Pfad werden vor jeder Worker-Aktivität mit einem
Top-Level-Fehler und null-Ergebnis abgelehnt. Die headless-Ablehnung folgt auf einen Selektions-Roundtrip, dessen
`guard_blocked` auf der Leitung der Host auf ein abgelehntes `binding_conflict` abbildet. Fehlgeschlagene
abgeschlossene Aufrufe behalten einen Wert mit transition `none` und dem Bindungszustand nach dem Aufruf.
`target_not_found`, `target_ambiguous` und `guard_blocked` vor dem Detach bewahren die alte verifizierte Bindung;
andere Worker-, Timeout/Absturz-, Protokoll- oder Nachbedingungsfehler machen sie ungültig. Kandidaten erscheinen
nur aus einer Auflistung in diesem Aufruf; eine fehlgeschlagene Pfadauswahl mit not-found/ambiguous fügt eine
Auflistung hinzu. Doctor meldet Warning für eine ungebundene schreibfähige Session und nennt `bind_project` als
Abhilfe.

Für die explizite Inspektion dient `action:"list_portals"` zur Discovery ohne Anbindung/Übernahme, oder
`list_server_connections`, `list_server_groups`, `list_server_projects`, `list_local_sessions`, `get_lock_state`
für das exakte Project-Server-Inventar. Die Inspektion bewahrt eine intakte Bindung und Cursor, kann eine
bestehende Anbindung nicht umschalten und lehnt Bindungsselektoren ab, selbst wenn sie null/false sind. Ergebnisse
fügen bedingt `result.value.inspection` hinzu, mit beobachteter PID und typisierten nullable Slots;
`transition:"none"` und `project:null`. Das Weglassen des gesamten Werts schließt das Inventar ein. Siehe die
[Multiuser-Referenz](MULTIUSER_OPERATIONS_SUMMARY.de.md) für Selektoren, Beispiele, den Umfang des aktuellen
Benutzers, Grenzen für Sperren/Fehler/Weglassungen und den abgegrenzten Live-Nachweis. Die Discovery in PR3 war
7/17/17; die aktuelle Discovery ist 13/32/32 mit den Standalone-Tools für VCI und Bibliotheken, `plc_download`,
`plc_tia_online`, `plc_station_upload`, den S7-PLCSIM-Advanced-Tools und den S7-1500-Web-API-Tools.

### Identität und Basisstatus lokaler Sessions

PR4 wählt eine bereits geöffnete typisierte `LocalSession` über ihren exakten `.amc21`-Engineering-Pfad in jedem
Zugriffsmodus aus. `get_project_status` und `--project`/`TIA_MCP_PROJECT_PATH` verwenden dieselbe bereits geöffnete
Identität; gewöhnliche Lesevorgänge und Start-Assertions öffnen oder wählen nie einen anderen Owner. Der lokale
Status enthält `isOpen`, `path`, ein nullable `isModified`, `metadata:null` und bedingt `context`. Standalone- und
geschlossener Status lassen `context` weg; erweiterte Standalone-Metadaten bleiben verfügbar. Lokale Felder außer
dem Basisstatus bleiben null, statt erfundene Metadaten zu liefern.

Der lokale `context` beschreibt `containerKind:"localSession"`, `engineeringProjectPath`, ein nullable
`sessionContainerPath`, `openedByWorker`, `sessionMode`, Capabilities, die Remote-Identität und die Beobachtung der
Verbindung. Eine kalte Übernahme hat `openedByWorker:false` und `sessionContainerPath:null`. Nur ein verifiziertes
erfolgreiches exaktes `.als21`-Öffnen hält diese Öffner-Herkunft fest; Detach verliert Ownership/Herkunft. Es gibt
keine umgekehrte Verknüpfung von ALS und Inventar: Der Modus bleibt `unknown`, die Remote-Identität bleibt null, und
die Beobachtung der aktiven Verbindung bleibt `unknown`. Beschreibende Capability-Einträge gewähren keine
Zugriffsmodus-Berechtigung. Lokale Inhalts-Lesevorgänge, PLC/Network-Schreibvorgänge, Übersetzen und
`save_project` sind geliefert (siehe die
[Multiuser-Referenz](MULTIUSER_OPERATIONS_SUMMARY.md#internal-context)). Ein lokales `save_project` verwendet
`LocalSession.Save()` und checkt nie in den Project Server ein. Lokales Anlegen, Speichern unter, Archivieren und
generisches Schließen werden mit `unsupported_capability` abgelehnt, einschließlich Vorschauen.

Der [PR4-Live-Bericht](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md) hält die
abgegrenzte Abnahme durch den Maintainer fest. Zwei beibehaltene UI-Portale stellten lokale Sessions online in
read-write/full wieder her; Offline-Vorschauen in read-write bestanden, während das tatsächliche Öffnen das
Wegklicken von Siemens-Dialogen durch den Bediener erforderte. Nicht interaktives Offline-Öffnen schlug in beiden
getesteten Fixtures fehl. Offline im full-Modus, Start/read-only, headless und Race-Fälle wurden auf diesem
Kandidaten nicht ausgeführt.

### `browse_project_tree` v3

`browse_project_tree` liefert ein kanonisches JSON-Dokument identisch im MCP-Textblock `content` und in `structuredContent`. Die Antwort ist ein versionierter Envelope, und `result.nodes` ist eine flache Sequenz mit Eltern zuerst. Gruppierte und ungruppierte Geräte haben dieselbe `Device`-Form. Gruppen von PLC-Systembausteinen verwenden `nodeType: "SystemBlockFolder"`; enthaltene Bausteine behalten ihren funktionalen Bausteintyp und tragen `details.IsSystemBlock: "true"`. Diese Markierung hält nur die Zugehörigkeit zur TIA-Hierarchie fest und ist keine Aussage über Autor, Hersteller, Bibliothek oder Herkunft.

Jeder PLC-Bausteinknoten kann in seinem `details`-Objekt diese optionalen, standardmäßig aktiven String-Felder enthalten; befüllte Werte werden ohne Opt-in-Anfrage ausgegeben.

| Bausteindetail | Bedeutung |
| --- | --- |
| `HeaderAuthor` | Nicht leeres `PlcBlock.HeaderAuthor` genau so, wie TIA Portal es meldet. |
| `HeaderVersion` | Nicht leerer Wert von `PlcBlock.HeaderVersion.ToString()`. Der Wert wird aufgenommen, wenn die typisierte Openness-Eigenschaft nicht leer ist, unabhängig davon, ob die TIA-UI leer erscheint oder das exportierte SimaticML ein `<HeaderVersion>`-Element enthält. |
| `HeaderFamily` | Nicht leeres `PlcBlock.HeaderFamily` genau so, wie TIA Portal es meldet. |
| `HeaderName` | Nicht leeres `PlcBlock.HeaderName`; getrennt vom `name` des Engineering-Objekts des Knotens. |

Leere Header-Werte werden weggelassen, statt als `null` oder leere Strings ausgegeben zu werden. Das beruht auf dem Wert, den die typisierte Openness-Eigenschaft liefert: Nur `null`, leere oder nur aus Leerraum bestehende Werte werden weggelassen. In der begrenzten Beobachtung des V21-Abnahmeprojekts wurde eine weggelassene SimaticML-Version von der typisierten Eigenschaft als `0.1` bereitgestellt, während das Leeren einer anderen Version `0.0.0.0` erzeugte/bereitstellte. Diese konkreten Werte sind Beobachtungen aus diesem Projekt, keine universellen Siemens-Garantien. Daher bedeutet `details.HeaderVersion`, dass Openness einen typisierten Wert gemeldet hat; es beweist nicht, dass die Version in der TIA-UI oder im XML ausdrücklich befüllt wurde. Konsumenten, die die Semantik des Vorhandenseins eines XML-Elements benötigen, sollten stattdessen einen Export inspizieren. Header-Metadaten sind beschreibende Projektdaten, keine verifizierte Herstellerherkunft. `HeaderName` ändert nie die Selektoridentität, und `IsSystemBlock` beschreibt weiterhin nur die Zugehörigkeit zur Hierarchie. Der `name` des Engineering-Objekts des Knotens und `HeaderName` sind unabhängig; keiner der Werte ist ein Ersatz für den anderen.

Die Felder der ersten Anfrage sind:

| Feld | Verhalten |
| --- | --- |
| `startSelector` | Optionales Array von `{ nodeType, name }`-Segmenten von der Wurzel zum Ziel. `nodeType` beachtet Groß-/Kleinschreibung und stammt aus dem geschlossenen v3-Vokabular; `name` ist ein exakter ordinaler Treffer ohne Beachtung der Groß-/Kleinschreibung unter den direkten Kindern. Null oder mehrere Treffer schlagen fail-closed fehl. |
| `depth` | Optionale maximale zurückgegebene Tiefe ab der ausgewählten Wurzel, inklusive; Minimum `1`. |
| `pageSize` | Optionale angeforderte Knotenanzahl von `1` bis `200`; Standard `100`. Die kanonische Größenprojektion kann weniger vollständige Knoten liefern. |
| `cursor` | Opake prozesslokale Fortsetzung. Ein Aufruf nur mit Cursor ist die normale Fortsetzungsanfrage. |

#### Migration der Anfrage von v2 auf v3

Die v2-Anfrage war:

```json
{ "depth": 2, "startPath": "PLC_1" }
```

Die v3-Anfrage ist:

```json
{
  "depth": 2,
  "startSelector": [
    { "nodeType": "Device", "name": "PLC_1" }
  ]
}
```

Verwende `v3.0.0` oder neuer. Das alte nackte verschachtelte Array wird nicht beibehalten, und die veralteten Projektbaum-Eingaben `startPath` und `deviceName` sind entfernt, statt als Aliase akzeptiert zu werden.

#### Vollständiger Erfolgs-Envelope

```json
{
  "contractVersion": "3.0",
  "status": "succeeded",
  "result": {
    "snapshot": {
      "snapshotId": "0123456789abcdef0123456789abcdef",
      "createdAt": "2026-09-06T12:00:00+00:00",
      "idleExpiresAt": "2026-09-06T12:10:00+00:00",
      "totalNodes": 2
    },
    "query": {
      "projectPath": "C:\\Projects\\Plant.ap21",
      "startSelector": [
        { "nodeType": "Device", "name": "PLC_1" }
      ],
      "depth": 2
    },
    "pagination": {
      "offset": 0,
      "requestedPageSize": 100,
      "returnedCount": 2,
      "nextCursor": null
    },
    "nodes": [
      {
        "nodeId": "n0",
        "parentNodeId": null,
        "sequence": 0,
        "name": "PLC_1",
        "nodeType": "Device",
        "details": {}
      },
      {
        "nodeId": "n1",
        "parentNodeId": "n0",
        "sequence": 1,
        "name": "PLC_1",
        "nodeType": "PlcSoftware",
        "details": {}
      }
    ]
  },
  "failure": null,
  "warnings": []
}
```

Wenn `pagination.nextCursor` nicht null ist, wird mit einer Anfrage nur mit Cursor fortgesetzt und wiederholt, bis er null wird:

```json
{ "cursor": "<pagination.nextCursor>" }
```

Jede erste Anfrage macht einen Worker-Aufruf zur Baumbeobachtung; ein konfigurierter Start in read-write kann zuerst `get_project_status` aufrufen, um die Projektbindung zu verifizieren. Der net48-Worker wendet den restlichen Teilbaum-Selektor und den Tiefenfilter an, nachdem er das ausgewählte Gerät durchlaufen hat. Jede Fortsetzung authentifiziert den Cursor, prüft Bindungs-ID/-Revision und liefert denselben unveränderlichen Snapshot zu einem Zeitpunkt ohne einen weiteren Worker-Aufruf. Eine Bindungsänderung liefert `cursor_binding_mismatch`, auch nach dem Wegschalten und Zurückschalten auf den ursprünglichen Pfad. Der Snapshot übernimmt keine späteren TIA-Änderungen. Cursor sind prozesslokal: Host-Neustart, Ablauf oder Verdrängung aus dem begrenzten Cache liefern `snapshot_unavailable`. Nach einem dieser Ergebnisse ist ein neues Durchsuchen ohne Cursor zu starten. Abfragefelder zusammen mit einem Cursor sind nur erlaubt, wenn sie der erfassten Abfrage entsprechen; eine Abweichung liefert `cursor_filter_mismatch`.

Knoten kommen mit Eltern zuerst, daher kann ein Aufrufer einen Selektor ohne veralteten Pfadtext rekonstruieren: Knoten nach `nodeId` indizieren, beim gewünschten Knoten beginnen, `parentNodeId` bis null folgen, die Kette umkehren und jeden Knoten auf `{ "nodeType": node.nodeType, "name": node.name }` projizieren. Diese zurückgegebene Kette beginnt an der ausgewählten Snapshot-Wurzel, die unterhalb von `Device` liegen kann. Um den vollständigen Selektor zu bilden, wird `result.query.startSelector` ohne sein letztes Segment vorangestellt und dann die rekonstruierte zurückgegebene Kette angehängt. Wenn der anfängliche `startSelector` weggelassen wurde, ist das Präfix leer. Das vermeidet die Verdopplung der ausgewählten Wurzel und bewahrt die Device-Abstammung für Snapshots mit Wurzel bei `PlcSoftware`, `BlockFolder` oder tiefer. Das kombinierte Array wird als `startSelector` übergeben. `nodeId` nicht über Snapshots hinweg speichern; es ist snapshot-lokal.

Jeder Snapshot ist auf 4.000.000 kanonische Zeichen begrenzt. Der Prozess hält höchstens vier Snapshots und insgesamt 16.000.000 Snapshot-Zeichen mit einer gleitenden Leerlauf-Lebensdauer von zehn Minuten. Jede Seite ist auf 60.000 kanonische Zeichen begrenzt und enthält nur vollständige Knoten. `snapshot_too_large`, `result_item_too_large` und `result_metadata_too_large` schlagen ausdrücklich fehl; v3 hängt nie eine Kürzungsmarkierung an. Das veraltete Member `details.Path` ist entfernt.

Alle Projektbaum-Fehler behalten denselben Envelope mit `status: "failed"`, `result: null`, einem `{ category, message }`-Fehler und separaten Warnungen. Eingabe- und Auswahlfehler sind `validation_error`, `invalid_selector`, `target_not_found` und `target_ambiguous`. Strukturell ungültige Selektor-Member verwenden `invalid_selector`; fehlerhafte oder leere Cursor verwenden `invalid_cursor`. Cursor- und Snapshot-Fehler sind `invalid_cursor`, `cursor_filter_mismatch`, `cursor_out_of_range` und `snapshot_unavailable`. Größenfehler sind `snapshot_too_large`, `result_item_too_large` und `result_metadata_too_large`. Übergroße Diagnosen erzeugen einen begrenzten `result_metadata_too_large`-Envelope, ohne die Diagnosen zurückzugeben. Bindungs-/Worker-/Protokollfehler können `binding_conflict`, `worker_operation_failed`, `worker_timeout`, `worker_crashed`, `access_denied` oder `protocol_error` sein.

Openness-Beeinträchtigungen je Item werden im `warnings`-Array des Envelopes festgehalten, statt einen Fehler in einen Erfolg zu verwandeln.

`compile_check` ist eine Standalone-Engineering-Operation. Sie ist nicht als rein lesend gekennzeichnet und wird in den Modi read-write und full angeboten. Der Bericht-`scope` ist `plc`, `block` oder `hardware`; ein unbekanntes `scope`-Argument oder `hardware` mit `blockPath` wird vor jedem Worker-Aufruf mit `validation_error` abgelehnt. Das standardmäßige Übersetzen des Programms übersetzt nie die Hardwarekonfiguration, daher erscheinen Hardwarefehler, die ein Download meldet (Defekt D9), nur mit `scope:"hardware"`.

### Vertrag für Standalone-Status und Übersetzung

`get_project_status` und `compile_check` deklarieren konkrete MCP-Ausgabeschemata und liefern dasselbe kanonische JSON-Dokument im `content`-Text und in `structuredContent`. Beide verwenden die Vertragsversion `1.0` und den Envelope `tool`, `contractVersion`, `success`, `error`, `warnings` und `result`. Eingaben und Zugriffsmodi sind unverändert (C-0 entfernte später `get_project_status.projectPath`). Diese Ausgabeänderung bricht Legacy-Clients: Statt den JSON-String in `payload` zu parsen, ist das Objekt unter `result.value` zu lesen.

`result` enthält vier explizite Member: `status` (`succeeded`, `failed` oder `omitted`), `value`, `failure` und `omission`. Der Wert ist das vollständige `ProjectStatusInfo` (einschließlich `metadata`) oder `CompileCheckReport`. Optionale/nicht verfügbare Daten werden durch explizite Nulls dargestellt, auch innerhalb verschachtelter Metadaten. Ein Lesevorgang ohne Projekt gelingt mit `result.value.isOpen:false` und bindet keine ungebundene Host-Session.

Eine Ablehnung vor der angeforderten Operation setzt `success:false`, einen Top-Level-`{category,message}`-`error`, `result:null` und MCP `isError:true`. Eine versuchte Operation, die fehlschlägt, setzt `success:false`, `error:null` und MCP `isError:false`, mit dem Fehlernachweis in `result.failure` oder Compilerdiagnosen in `result.value`. Die Identitätsvalidierung im Host kann nach dem Übersetzen fehlschlagen; das bleibt ein versuchter Fehlschlag, und die Session-Bindung wird ungültig. Ein Timeout/Absturz lässt das Ergebnis unbekannt; vor der Entscheidung über einen erneuten Versuch ist der aktuelle Projektzustand zu inspizieren.

Beim Übersetzen bedeutet `success`, dass die ausgewählte Übersetzung bestanden hat, einschließlich Berichten nur mit Warnungen. Fehler, nicht verfügbare Compilerergebnisse und unbekannte/unvollständige Zustände bestehen nicht. Compilerfehler behalten den Bericht, zum Beispiel:

```json
{
  "contractVersion": "1.0",
  "error": null,
  "result": {
    "failure": null,
    "omission": null,
    "status": "failed",
    "value": {
      "blockPath": "PLC_1/Blocks/Main",
      "overallState": "Error",
      "plcs": [{
        "deviceName": null,
        "diagnosticNotes": [],
        "errorCount": 1,
        "messages": [{"description": "Invalid expression", "path": "PLC_1/Program blocks/Main", "severity": "Error"}],
        "plcName": "PLC_1",
        "state": "Error",
        "warningCount": 0
      }],
      "scope": "block",
      "totalErrorCount": 1,
      "totalWarningCount": 0
    }
  },
  "success": false,
  "tool": "compile_check",
  "warnings": []
}
```

Der vollständige kanonische Wert hat ein Limit von 60.000 Zeichen; das endgültige Dokument einschließlich escapter Strings, Warnungen und Weglassungs-Metadaten hat ein Limit von 180.000 Zeichen. Übergroße Werte werden als Ganzes ersetzt: `status:omitted`, `value:null`, `success:false`, `error:null` und `isError:false`. `omission` meldet `reason` (`resultExceededItemCharLimit` oder `responseExceededDocumentCharLimit`), `limitChars`, `originalChars`, `retryTool` und `guidance`. Wiederholungen des Übersetzens können `plcName` oder `blockPath` eingrenzen; der Status hat keinen Metadaten-Selektor, daher verweist seine Anleitung darauf, Metadaten zu reduzieren oder sie in TIA Portal zu inspizieren. Ganze Warnungseinträge können entfernt werden, damit das Dokument passt, mit einem begrenzten Hinweis auf die Anzahl. Fehlerprosa wird nur als letztes Mittel gekürzt und ausdrücklich gekennzeichnet.

Compilermeldungen (seit 2026-11-02, Defekt K04; Form und Vertragsversion `1.0` unverändert): TIA Portal liefert
seine Diagnosen als Baum, dessen Containerknoten (die PLC, `Hardware configuration`, ein Rack, ein Bausteinordner)
eine leere oder nur aus Leerraum bestehende Beschreibung haben und den schwersten Schweregrad unter ihnen
wiederholen. Live am 2026-11-02 listete die Hardware-Übersetzung einer fehlersicheren CPU `{"",PLC_F1,Error}`,
`{"",Hardware configuration,Error}`, `{" ",PLC_F1,Error}`, `{" ",Profilschiene_0,Error}` vor den drei echten
Fehlertexten, die einen leeren Pfad hatten. Solche Container werden nicht mehr als Meldungen gemeldet. Jede
gemeldete Meldung hat eine nicht leere Beschreibung (oder eine nicht lesbare, leer gemeldet mit einem
Diagnosehinweis), und ihr `path` ist die Kette der nicht leeren TIA-Portal-Pfade ihrer Container und ihres eigenen,
äußerster zuerst, mit `/` verbunden (zum Beispiel `PLC_F1/Hardware configuration/Profilschiene_0`); ein Pfad, der
dem letzten Segment der Kette entspricht, fügt nichts hinzu, und einer, der bereits mit dem ersten Segment der Kette
und `/` beginnt, ersetzt sie. Die Segmente sind die Werte von TIA Portal, nicht escapt, daher ist der Pfad
Anzeigekontext, kein Selektor. `errorCount`, `warningCount`, die Summen und die Zustände sind die von TIA Portal
und werden nie aus den gelieferten Meldungen neu gezählt. Die Regel ist nur offline getestet (Ersatz-Meldungsbäume);
nicht live getestet.

Strikte Decodierung geht der Budgetierung voraus: Fehlerhafte oder inkonsistente Worker-Erfolgs-Payloads werden zu einem `protocol_error`-Ergebnis mit fester Meldung und werden nie zurückgegeben. Die Diagnosegrenzen des Erzeugers sind davon getrennt: Die `diagnosticNotes` eines Compilerberichts können weggelassene/gekürzte Meldungen offenlegen, und die Anzahlen müssen nicht der Zahl der gelieferten Meldungen entsprechen. Eine bestandene Übersetzung beweist nicht, dass die Diagnosen erschöpfend waren, dass ein Projekt gespeichert oder dass PLC-Code geladen wurde.

### Metadatenoberfläche von `get_project_status`

Wenn ein Standalone-Projekt geöffnet ist, meldet `get_project_status` die Statusfelder plus ein verschachteltes
`metadata`-Objekt mit den erweiterten, rein lesbaren Projektmetadaten:

| Feld | Beschreibung |
| --- | --- |
| `copyright` | Copyright-Text des Projekts, wörtlich aus Openness. |
| `family` | Projektfamilie, wörtlich aus Openness. |
| `comment` | Mehrsprachiger Projektkommentar: Liste `translations` aus `{ culture, text }` in der Reihenfolge, in der Openness sie meldet, unter Bewahrung jeder Übersetzung. `culture` ist der Name der Sprachkultur (zum Beispiel `en-US`). |
| `languageSettings` | `languages` und `activeLanguages` als Listen von Kulturnamen; `editingLanguage` und `referenceLanguage` als Kulturnamen (null, wenn nicht gesetzt). |
| `historyEntries` | Text und Datum/Uhrzeit jedes Verlaufseintrags, in Openness-Reihenfolge, wörtlich und nicht dedupliziert. Begrenzt auf `200` Einträge (älteste zuerst); wenn Openness mehr meldet, ist `historyTruncated` `true`. Wenn der Verlauf nicht gelesen werden konnte, sind sowohl `historyEntries` als auch `historyTruncated` explizit `null` — `historyTruncated` ist nur dann `false`, wenn der Verlauf vollständig gelesen wurde. |
| `usedProducts` | `{ name, version }` für jedes Produkt, das Openness verzeichnet, ohne Ableitung und ohne Deduplizierung. |
| `compilationSettings` | V21-Schalter für die Bausteinübersetzung, gelesen über `PlcSimulationSettingsProvider` und `VirtualPlcSettingsProvider`: `isSimulationDuringBlockCompilationEnabled` und `isVirtualPlcDuringBlockCompilationEnabled`. Ein Wert ist explizit `null`, wenn sein Provider oder Wert nicht verfügbar ist, gemeldet als Antwortwarnung — nie ein erfundenes `false`. Gesetzt werden sie mit `plc_write` `set_project_compilation_settings` (siehe [PLC-Operationen](PLC_OPERATIONS_SUMMARY.md#project-block-compilation-settings); nicht live getestet). |

Alle Metadaten sind in allen drei Zugriffsmodi lesbar; die Metadateninspektion schließt, schaltet um, speichert
oder bestätigt nie etwas. Nicht verfügbare Abschnitte fallen auf eine Warnung und eine `null`-Ausgabe zurück, statt
auf einen erfundenen Standardwert; andere Fehler lassen den Aufruf weiterhin normal fehlschlagen.

Die Antwort von `get_project_status` verwendet das oben beschriebene Budget für das Weglassen des gesamten Werts.
Die Lifecycle-Verifikation nach dem Schreiben (nach allen sechs Operationen) liest nur den einfachen Projektstatus —
sie zählt nie den Verlauf oder die erweiterte Metadatenoberfläche auf.

## Lifecycle-Operationen

Alle sieben Lifecycle-Tools sind in read-write und full verfügbar; die vollständigen Modusanzahlen sind 13/32/32.
Read-write verlangt ein Bestätigungsformular je tatsächlichem Aufruf; full führt unter Policy ohne
Server-Elicitation aus. Block-Guards stoppen einen Aufruf in jedem Modus. Gewöhnliche Lesevorgänge binden, schalten
um oder öffnen nie; für bereits geöffnete Projekte dient `bind_project`, für bewusstes Öffnen open/create.

| Tool | Verhalten | Haupteingaben |
|---|---|---|
| `open_project` | Öffnet ein Standalone-Projekt oder eine vorhandene lokale Session und bindet deren verifizierte Engineering-Identität. | Absoluter `.ap21`- oder vorhandener reiner Datei-`.als21`-`projectPath`; optional `forceRebind`. |
| `create_project` | Legt ein Projekt an und bindet die Session daran. | Absolutes `projectDirectory`, `projectName`; optional `author`, `comment`. |
| `retrieve_project` | Dearchiviert ein Projektarchiv in ein Verzeichnis und bindet die Session an das dearchivierte Projekt (nicht live getestet; siehe [Ein Archiv dearchivieren](#ein-archiv-dearchivieren-retrieve_project)). | Absoluter vorhandener `archivePath`, vorhandenes absolutes `targetDirectory`; optional `upgrade`, `forceRebind`. |
| `save_project` | Speichert das aktive Projekt. | Keine Operationseingaben (wirkt auf das gebundene Projekt). |
| `save_project_as` | Speichert eine Kopie und bindet die Session neu an die Kopie. | `targetDirectory`, `targetName`; `rebind` muss `true` bleiben. |
| `archive_project` | Archiviert ein Projekt. | `archiveDirectory`, `archiveName`; optional `mode`, `saveBeforeArchive`. |
| `close_project` | Schließt das Projekt und hebt die Session-Bindung auf. | Optional `saveBeforeClose`. |

Unterstützte Archivmodi sind `None`, `DiscardRestorableData`, `Compressed` und `DiscardRestorableDataAndCompressed`. Lifecycle-Tools sind Einzel-Tool-Operationen und können nicht in einen Batch aufgenommen werden.

Jede Datei- und Verzeichniseingabe der Lifecycle-Tools (`projectPath`, `projectDirectory`, `targetDirectory`, `archiveDirectory`,
`archivePath`) erhält die gemeinsamen Aufrufer-Pfadregeln der Dateiexporte (Sicherheits-Review 2026-11-03, F5; nicht live getestet):
Win32-Geräte- und NT-Objektpfade (`\\?\`, `\\.\`, `\??\`), reservierte Gerätenamen (`NUL`, `CON`, `COM1` ... mit beliebiger
Endung) und Segmente, die auf Leerzeichen oder Punkt enden, werden mit `validation_error` abgelehnt, im Host
(`LifecycleWriteDomain.PathRefusal`) und erneut im Worker (`ProjectLifecycleService.RequireCallerPathShape`), bevor TIA Portal den
Pfad sieht. UNC-Freigaben bleiben erlaubt.

### Strenge der Argumente

Lifecycle-Wurzelargumente sind strikt. Ein unbekanntes, fehlendes, falsch typisiertes oder ausgemustertes Argument
wird abgelehnt, bevor das Tool läuft, mit dem kanonischen Dokument des Tools (`validation_error`, `isError:true`)
und ohne Audit-Datensatz. JSON-`null` wird für nicht nullable boolesche Argumente abgelehnt (`dryRun`,
`saveBeforeClose`, `forceRebind`, `rebind`, `saveBeforeArchive`); stattdessen das Argument weglassen. Ein
ausgemustertes Argument wie ein Lifecycle-`projectPath` (außer bei `open_project`) wird genauso abgelehnt, ohne
Worker-Anfrage. Nicht nullable String-Argumente akzeptieren weiterhin `null`; siehe das Improvement Log.

### Single-Call-Schreibvorgänge und Dry Runs

Jedes Lifecycle-Tool akzeptiert `dryRun:bool=false` zusammen mit seinen Operationseingaben. Ein normaler Aufruf
wird angewendet, sobald seine Guards bestanden sind. Die öffentlichen Eingaben `confirm` und Safety-Token sind
entfernt. `dryRun:true` löst das Ziel auf und meldet Effekte und jeden ausgelösten Guard als `phase:preview`, ohne
Lifecycle-Mutation, Elicitation oder Token. Ein harter Guard ist in dieser Vorschau sichtbar; ein tatsächlicher
Aufruf liefert `phase:blocked`. Ungültige Eingabe oder ein nicht aufgelöstes Ziel lässt auch einen Dry Run
fehlschlagen. Ein späterer Aufruf löst Zustand und Guards neu auf; die Vorschau stellt keine fortdauernde
Autorisierung dar.

Zum Beispiel lässt sich das Öffnen eines wegwerfbaren Projekts so inspizieren:

```json
{"projectPath":"<absolute .ap21 path>","dryRun":true}
```

Angewendet wird mit denselben Operationseingaben und `dryRun:false` (oder ohne `dryRun`). Effekte nennen die exakte
Quelle und das exakte Ziel und melden, ob die Quelle geschlossen, gespeichert wird oder geöffnet bleibt. Das Öffnen
eines Standalone-Projekts mit demselben Pfad ist idempotent; ein ungebundenes Öffnen hat keine Quelle zum
Schließen. Die Wiederverwendung derselben ALS erfordert eine fortlaufende Verifikation desselben Owners und eine
festgehaltene exakte Öffner-Herkunft; sie öffnet nicht erneut und erwirbt keine kalte Herkunft, und die tatsächliche
Wiederverwendung folgt weiterhin den Bestätigungs-/Audit-Regeln. Erzwungenes Neubinden bewahrt eine UI-eigene
Quelle. Eine worker-eigene geänderte Quelle, die geschlossen würde, wird immer blockiert: Speichere oder schließe sie
zuerst ausdrücklich. Create und Save-as verweigern ein vorhandenes Zielverzeichnis. Archive erfordert ein
vorhandenes Ausgabeverzeichnis außerhalb des Ordners des Quellprojekts und seiner Unterordner.

Eine worker-eigene lokale Session blockiert ein anderes Öffnen, selbst wenn sie sauber ist: Nur eine zukünftige
ausdrückliche Terminal-Session-Operation kann sie freigeben. Eine geliehene lokale Quelle kann nur dann neben einem
Öffner bestehen, wenn die exakte Bewahrung von Quelle/Ziel bewiesen ist; kalte/mehrdeutige ALS-Herkunft blockiert
vor dem Öffnen. `forceRebind:true` setzt diese Blockaden nie außer Kraft. Das Wiederherstellen eines beibehaltenen
leeren UI-Portals erfordert frische Discovery-/Identitätsprüfungen; eine Vorschau übernimmt/öffnet oder schließt
keine Quelle. Generisches `close_project` ist keine Bereinigungsoperation für lokale Sessions. Nach einem
ungewissen Öffner sind exakter AMC-Status und Portal-Inventar zu inspizieren, bevor eine weitere beabsichtigte
Anfrage erfolgt; nie wiederholen.

Die Abnahme auf installiertem V21 beobachtete, dass `create_project` verlangt, dass in diesem TIA-Prozess kein
Projekt geöffnet ist; das aktuelle Projekt ist ausdrücklich zu schließen, bevor ein weiteres angelegt wird. Live
erneut am 2026-11-02 (Standalone `MCP_Livetest_01` geöffnet, eine TIA-Portal-Instanz, englische UI): TIA Portal
verweigerte mit "Unable to open project '...'. Another project is already open." (K30). Seitdem meldet die Planung
`effects.openProject` (nur create_project): `source` `binding` (das verifizierte gebundene Projekt, gelesen von der
Lifecycle-Probe), `portalInventory` (das nicht anbindende `list_tia_portal_processes` eines bereits gestarteten
Workers: das angebundene Portal, sonst das einzige laufende) oder `notChecked` (mit `note`: noch kein Worker
gestartet, was die Planung nie tut, mehrere Portale und keines angebunden, oder eine fehlgeschlagene Auflistung),
plus `portalProcessId`, `openProjectPath` und `isModified`. Ein beobachtetes geöffnetes Projekt löst den
Acknowledge-Guard `create_project_requires_no_open_project` aus (unten). Ein Fehlschlag, dessen Engineering-Meldung
(oder Worker-Fehler nach "TIA Portal operation failed: ") exakt diese englische Meldung ist, wird als
`engineering_rejected` gemeldet und nennt save_project/close_project; wenn das Ziel vorher nicht existierte und
weiterhin nicht existiert, werden die Warnungen "may already have changed" weggelassen, weil nichts angelegt wurde.
Ein anderer Wortlaut (eine andere UI-Sprache) behält die generische Zuordnung; Meldungstext wird nur für diesen
einen erkannten Fall abgeglichen. Siemens lehnt auch das Archivieren eines geänderten Projekts mit
`saveBeforeArchive:false` ab; vor dem Archivieren zuerst speichern oder `saveBeforeArchive:true` verwenden. Dies
sind typisierte Fehlschläge versuchter Operationen, daher ist vor einem neuen Aufruf der Zustand zu inspizieren.

### Klarere Fehlertexte (Audit T6/T9/T11, nicht live getestet)

- `open_project` (und das Öffnen in `bind_project`/beim Neubinden): `ProjectComposition.Open(FileInfo)` dokumentiert
  `MissingProductsException`; das scheitert jetzt mit `worker_operation_failed` und einem Text, der die fehlenden Produkte,
  Optionspakete oder Support Packages nennt und sagt, dass nichts geöffnet wurde, dazu die Siemens-Meldung in
  `engineeringError` (Abgleich über den exakten vollen Namen, `EngineeringFailureHints`). Das UMAC-Öffnen (`UmacDelegate`) und
  das Ereignis `Authentication` bleiben außerhalb dieses Pakets.
- Jede `EngineeringSecurityException` behält ihre Kategorie und bekommt einen Text, der eine Sicherheitsablehnung nennt und,
  für UMAC-geschützte Projekte, das Openness-Funktionsrecht (Name aus einer Fremdanleitung, nicht geprüft).
- `portal_lost` (eine `NonRecoverableException` oder ein verlorenes Portal) und ein Worker, der während eines Lesevorgangs
  stehen blieb, sagen nicht mehr, eine Wiederholung sei sicher: Der Text sagt, denselben Aufruf nicht unbeaufsichtigt zu
  wiederholen und zuerst TIA Portal und seinen Absturzbericht zu prüfen (der Diagnoseordner stammt aus einer Fremdanleitung,
  nicht geprüft). Eine Zeitüberschreitung beim Lesen sagt, dass eine Wiederholung das Projekt nicht ändert, TIA Portal aber
  erneut hängen lassen kann.

### Ein Archiv dearchivieren (`retrieve_project`)

> **Nicht live getestet** (GitHub-Issue #4). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.ProjectComposition.Retrieve(System.IO.FileInfo,System.IO.DirectoryInfo)` ("Retrieves an archived
> project") und `.RetrieveWithUpgrade(System.IO.FileInfo,System.IO.DirectoryInfo)` ("Retrieves a project from an archive and
> upgrades it to the current version"), beide liefern `Siemens.Engineering.Project` und werfen laut Dokumentation
> `EngineeringTargetInvocationException` und `MissingProductsException`.

`retrieve_project(archivePath, targetDirectory, upgrade=false, forceRebind=false, dryRun=false)` ist ein Lifecycle-Tool wie
`create_project`: read-write bestätigt jeden tatsächlichen Aufruf, full läuft unter Policy, `dryRun:true` zeigt eine Vorschau
ohne Dearchivieren. `archivePath` muss eine absolute vorhandene Datei sein (keine Endung wird verlangt: keine ist dokumentiert),
`targetDirectory` ein vorhandenes absolutes Verzeichnis. Eine gebundene Session braucht `forceRebind:true` (wie `open_project`).
Der Worker bindet die dokumentierte Überladung mit zwei Argumenten über exakten Namen und Parametertypen an der Live-
`ProjectComposition` (kein Stub; fehlend: `api_member_missing`), ruft `Retrieve` oder mit `upgrade:true` `RetrieveWithUpgrade`
auf, führt das gelieferte Projekt als vom Worker geöffnet, und der Host bindet an den Pfad, den TIA Portal meldet (er muss in
`targetDirectory` liegen), und verifiziert ihn mit dem Projektstatus, genau wie nach `create_project`. Die Planung meldet
`effects.archivePath`, `effects.destinationDirectory` und `effects.openProject` (wie bei create_project) und löst
`retrieve_project_requires_no_open_project` aus, wenn ein geöffnetes Projekt beobachtet wird. Die erkannte Verweigerung "Unable
to open project '...'. Another project is already open." (K30/K51) wird zu `engineering_rejected`; weil nicht bekannt ist, ob TIA
Portal vor der Verweigerung schon etwas entpackt hat, bleiben die Warnungen "kann sich bereits geändert haben". Die Überladungen
mit drei Argumenten (`UmacDelegate`) und die mit `ProjectOpenMode` sind weggelassen (die UMAC-Zugangsdaten eines geschützten
Projekts gehören nicht zum Umfang). **Noch nötige Live-Abnahme:** was TIA Portal in `targetDirectory` anlegt, welche
Archivendungen es annimmt, der Upgrade-Weg von einer älteren Version und die Verweigerung bei geöffnetem Projekt.

### Sekundärprojekte (Issue #20)

Ein zweites Projekt kann schreibgeschützt neben dem gebundenen geöffnet werden (`ProjectOpenMode.Secondary`), über `library_write`
`open_secondary_project` / `retrieve_secondary_project` / `close_secondary_project`, aufgeführt von `library_read` `list_secondary_projects`,
mit `create_master_copy` `source.project` daraus kopiert und mit `plc_read` `compare_software` `compareTarget: secondary_plc` verglichen
([Bibliotheksreferenz](LIBRARY_OPERATIONS_SUMMARY.de.md#sekundäre-schreibgeschützte-referenz-projekte-issue-20)). Die Bindungsregeln
ändern sich nicht: ein Projekt, dessen `ProjectBase.IsPrimary` false liest, ist nie ein Bindungskandidat, und Inhalts-Tools wirken nur
auf das gebundene Projekt. Nicht live getestet.

### Guards und Bestätigung

| Guard-ID | Operation / Folge | Schweregrad |
| --- | --- | --- |
| `closes_source_project` | Öffnen mit erzwungenem Neubinden schließt ein worker-eigenes Quellprojekt. | `info` |
| `discards_unsaved_source_changes` | Öffnen mit erzwungenem Neubinden würde eine geänderte worker-eigene Quelle schließen. | `block` |
| `discards_unsaved_changes` | Schließen mit `saveBeforeClose:false` würde einen geänderten Projektzustand verwerfen. | `acknowledge` |
| `archive_without_save` | Archivieren mit `saveBeforeArchive:false` beobachtet einen geänderten Projektzustand. | `info` |
| `archive_discards_restorable_data` | Der Archivmodus ist `DiscardRestorableData` oder `DiscardRestorableDataAndCompressed`. | `info` |
| `archive_inside_project_folder` | Das Archivziel ist der Projektordner oder ein Unterordner davon; TIA Portal entscheidet (block bis 2026-11-01). | `info` |
| `target_exists` | Das Zielverzeichnis von Create oder Save-as existiert; TIA Portal entscheidet (block bis 2026-11-01). | `info` |
| `create_project_requires_no_open_project` | create_project, während in dem TIA Portal, das es verwenden würde, ein Projekt geöffnet ist (`effects.openProject`); TIA Portal V21 verweigerte dies live am 2026-11-02. Acknowledge, nicht block: Die Verweigerung wurde einmal beobachtet (Standalone-Projekt, eine TIA-Portal-Instanz); dass TIA Portal immer verweigert (lokale Sessions, andere Portal-Zustände), ist nicht gezeigt, daher entscheidet TIA Portal. Die read-write-Rückfrage zeigt ihn. Als Guard nicht live getestet. | `acknowledge` |
| `retrieve_project_requires_no_open_project` | retrieve_project, während in dem TIA Portal, das es verwenden würde, ein Projekt geöffnet ist (`effects.openProject`, ermittelt wie bei create_project); TIA Portal V21 verweigerte live am 2026-11-02 das Öffnen eines zweiten Projekts bei create_project und open_project. Acknowledge, nicht block: TIA Portal entscheidet. Nicht live getestet. | `acknowledge` |
| `local_session_requires_terminal_operation` | Ein anderes Öffnen würde eine worker-eigene lokale Session ersetzen, auch eine saubere. | `block` |
| `local_session_source_preservation_unproved` | Für eine geliehene lokale Quelle oder ein doppeltes Ziel kann die Bewahrung nicht bewiesen werden. | `block` |

Info-Guards erscheinen in den Warnungen; Block-Guards können in keinem Modus übergangen werden. Jeder tatsächliche
Lifecycle-Aufruf in read-write fragt einmal, auch Aufrufe nur mit Info-Guards und Aufrufe ohne Guards. Die Antwort
muss `accept` mit booleschem `confirm:true` sein. Nicht unterstützte Capability, Ablehnung, Abbruch, Timeout,
Transportfehler oder Annahme ohne diesen booleschen Wert verweigert mit `access_denied`. Full erfüllt
Acknowledge-Guards per Policy und führt keine Elicitation durch. Block-Guards und Dry Runs fragen nie nach. Nach der
Annahme löst der Server Ziele/Effekte/Guards neu auf; Änderungen können die angenommene Aktion nicht erweitern.

Für ein beabsichtigtes Verwerfen ungespeicherter Änderungen dient `{"saveBeforeClose":false}`; die Bestätigung folgt
dem Modus. Das frühere Agenten-Bestätigungs-Array ist entfernt. Dry Runs melden den Acknowledge-Guard mit
`acknowledged:false` in read-write und `true` in full, mit null-Guard-Audit-Erfüllung und
`confirmation:none/not_requested`. Die angewendete Erfüllung ist `user` oder `policy`. Eine vom Client
zurückgegebene Annahme beweist nicht, dass ein Mensch einen Dialog gesehen hat. Eine read-write-Ablehnung nach einer
Elicitation-Antwort behält `confirmation:user/declined`; Vorschauen und Guard-Verweigerungen vor der Rückfrage
verwenden `none/not_requested`. Siemens-Dialoge sind von der MCP-Elicitation getrennt; eine
Policy-Bestätigung in full belegt nicht die Abwesenheit von Hersteller-UI.

### Strukturierte Lifecycle-Antwort

Alle sieben Tools deklarieren ein konkretes Ausgabeschema mit `tool`, `contractVersion:"1.0"`, `success`, `error`,
`warnings`, `phase`, `guards`, `effects`, `result` und `verification`. Eine kanonische Serialisierung liefert
sowohl den MCP-Text als auch `structuredContent`. Objekte direkt lesen; die alten JSON-Strings `operationResult`
und `verification.result` sind entfernt.

`phase` ist `preview`, `applied`, `blocked` oder `error`. `effects` ist ein typisiertes Objekt mit
`sourceProjectPath`, `destinationProjectPath`, `destinationDirectory`, `sourceStatus`, `sourceOpenedByWorker`,
`willCloseSource`, `savesSource`, `rebindsToDestination`, `targetExists`, `archiveMode` und `archivePath`.
Null-Member bewahren nicht verfügbare oder nicht anwendbare Nachweise; Quell-/Zielfelder unterscheiden das aktuelle
Projekt von einer angeforderten Kopie oder einem neuen Projekt.

`result` und `verification` verwenden typisierte Ergebnisse mit `status`, `value`, `failure` und `omission`.
`result.value` enthält die Lifecycle-Wurzel (`success`, `operation`, `projectPath` und den Basis-`project`-Status).
Die Verifikation ist null für Vorschauen, Ablehnungen und Mutationsfehler; andernfalls enthält ihr Wert das
Basis-`ProjectStatusInfo`, ohne erweiterte Metadaten aufzuzählen. Open/Create verifizieren das gebundene Ziel;
Save/Archive verifizieren die Quelle; Save-as verifiziert seine neu gebundene Kopie; Close verifiziert, dass kein
Projekt geöffnet ist. Guard-`acknowledged` ist ein boolescher Wert für Acknowledge-Guards und explizit null für
Info-/Block-Guards.

Eine Ablehnung vor der Mutation setzt `success:false`, einen Top-Level-`{category,message}`-`error` und MCP
`isError:true`. Ein Fehlschlag einer versuchten Mutation oder Verifikation behält `error:null`, `isError:false` und
`success:false`, mit typisiertem Fehlernachweis. Eine erfolgreiche Mutation bleibt sichtbar, wenn ihre Verifikation
fehlschlug; das ist kein Ergebnis für eine sichere Wiederholung. Fehlerhafte Worker-Erfolge werden zu begrenzten
`protocol_error`-Ergebnissen, ohne die abgelehnte Payload zurückzugeben.

Jeder Ergebnis-/Verifikationswert ist auf 60.000 kanonische Zeichen begrenzt und das vollständige Dokument auf
180.000. Übergroßer Nachweis wird als Ganzes weggelassen, mit `status:omitted`, `value:null` und gemessener
`omission`-Anleitung (`resultExceededItemCharLimit` für das Wertlimit, `responseExceededDocumentCharLimit` für das
Gesamtlimit). Eine Weglassung allein macht eine erfolgreiche Ausführung/Verifikation nicht zu einem Fehlschlag,
daher kann `success:true` mit weggelassenem Nachweis einhergehen. Die strikte Payload-Validierung läuft vor diesen
Budgets. Um den Nachweis wiederzuerlangen, `retryTool:get_project_status` folgen oder die Zielartefakte
inspizieren; eine Mutation nicht allein deshalb wiederholen, weil ihr zurückgegebener Wert weggelassen wurde. Ein
großer Quellstatus oder das vollständige Effekte-Objekt kann mit einer ausdrücklichen Warnung null werden. Wenn
übergroße Guard-Meldungen die Dokumentgrenze überschreiten würden, werden sie durch eine ausdrückliche Markierung
für weggelassenen Nachweis neben der Folgebeschreibung des Katalogs ersetzt; Guard-IDs, Schweregrade,
Operations-IDs, Bestätigungszustand, Phase und Urteil bleiben intakt. Ganze Warnungseinträge können mit einem
Hinweis auf die Anzahl der Weglassungen entfernt werden. Der Server liefert nie partielles JSON und stellt gekürzte
Pfadnachweise nie als vollständige Zielidentität dar. Wenn der zurückgegebene Nachweis weggelassen ist, sind der
aktuelle Projektstatus und die Zielartefakte zu inspizieren. Fehlerprosa kann als letztes Mittel mit einem
Kürzungshinweis begrenzt werden; ihre Kategorie und die Einordnung der Antwort als Ablehnung oder versuchter
Fehlschlag bleiben intakt.

Jeder Aufruf hängt einen Audit-Datensatz für abgesicherte Schreibvorgänge an, einschließlich Vorschauen und
Ablehnungen, an `%LOCALAPPDATA%\TiaMcpServer\audit\writes-yyyy-MM-dd.jsonl`. Der Datensatz bewahrt das exakte
zurückgegebene kanonische Dokument/den Hash, die angeforderte Operation, die vorbereitete Bindung, das Ziel, die
Guards und die Herkunft der Bestätigung. Audit v2 hält die Bestätigung des Aufrufs als `user`, `policy` oder `none`
fest. Kein Tool fügt ein Safety-Token hinzu oder verbraucht eines: Network-, PLC-, HMI- und VCI-Schreibvorgänge
verwenden dieselbe abgesicherte Pipeline und denselben Audit-Strom, und das Legacy-Schreib-Audit wurde ausgemustert.

Migration vom ausgemusterten Token-Ablauf: Client-Schleifen aus Vorschau/Token/Anwenden für diese sechs Tools
entfernen, alte öffentliche Bestätigungs-/Token-Argumente und Agenten-Bestätigungs-Arrays entfernen, `dryRun` nach
Bedarf verwenden und typisierte Ausgaben lesen. Der entfernte Startschalter wird abgelehnt mit:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Die gültige [Lifecycle-Matrix vom 2026-10-01](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md)
wurde für diesen Kandidaten durch die Änderung von Stufen/Bestätigung/Bindung ungültig und durch den
[Live-Lauf vom 2026-10-03](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md)
ersetzt. Alle drei Funktionsgruppen bestanden; der Maintainer meldete in read-only keinen neuen TIA-Dialog, womit
die separate menschliche Beobachtung abgeschlossen ist. Der Bericht bewahrt die Grenzen und Ausschlüsse zu
Laufzeit/Client/Artefakten/Wiederherstellung.

### MCP-Client-Hinweise

Alle sieben Lifecycle-Tools geben mutierende MCP-Hinweise an: `readOnlyHint: false` und `destructiveHint: true`.
`open_project` gibt `openWorldHint: true` an, weil das Öffnen einer lokalen Session ihren Project Server
kontaktieren kann; die anderen fünf geben `openWorldHint: false` an. Diese clientseitigen Hinweise umgehen das
Sicherheitsmodell nicht. Ein Client kann für ein destruktives Tool trotzdem seine eigene Berechtigungsabfrage
anzeigen, auch bei einem Dry Run. Die Server-Elicitation fragt einmal je tatsächlichem Lifecycle-Aufruf in
read-write.

## Sicherheit und Session-Bindung

- Bindungsvorbereitung und Mutation/Verifikation/Audit verwenden eine festgelegte Revision; veraltete Revisionen schlagen fail-closed fehl, statt eine Operation auf ein anderes Projekt umzuleiten.
- Open/Create dürfen ungebunden beginnen. Andere Lifecycle-Schreibvorgänge erfordern die passende aktive Quelle; die Wiederherstellung verankert genau diese Quelle, bevor es weitergeht.
- `save_project_as` erfordert Neubinden, weil Siemens `SaveAs` das aktive Projekt auf die Kopie umschaltet.
- Eine Archivausgabe wird abgelehnt, wenn das Archivverzeichnis im Projektordner liegt.
- Nach einem Timeout oder Worker-Absturz ist der aktuelle Projektzustand zu inspizieren, bevor entschieden wird, ob ein weiterer Aufruf sicher ist; der Server wiederholt Lifecycle-Schreibvorgänge nicht automatisch.
- `get_project_status` nimmt keine Argumente, bindet nicht und schaltet nie um. Inhalts-Tools wirken nur auf das gebundene Projekt; ohne verifizierte Bindung (ein konfiguriertes `--project` wird zuerst über den Status verankert) schlagen sie fail-closed mit `binding_conflict` fehl; zuerst `bind_project` aufrufen. Mit `bind_project` wird ein bereits geöffnetes Projekt ausgewählt, mit `open_project` eines bewusst geöffnet.

## Aktuelle Grenzen

Die aktuelle Projektoberfläche bietet nicht:

- `OpenWithUpgrade`, `Retrieve`, `RetrieveWithUpgrade` oder das Löschen von Projekten.
- Spracheinstellungen des Projekts und Import/Export mehrsprachiger Texte (das Lesen ist über die Metadaten von `get_project_status` verfügbar; das Bearbeiten nicht).
- Vollständige Bearbeitung von Projektattributen (Copyright, Familie, Kommentar und Spracheinstellungen sind heute rein lesbar).
- UMAC-Delegates, Authentifizierungsereignisse oder die ausdrückliche Auswahl von primärem/sekundärem `ProjectOpenMode`.
- Portal-Einstellungen, Diagnoseeinstellungen oder die Verwaltung des Suchindex.
- VCI-Bibliotheksbereiche, das Verbinden nicht verknüpfter Dateien, Workspace-Gruppen und VCI-Einstellungen.
  Standalone-Operationen für Workspace, Mapping, Export und Synchronisation stehen in der
  [VCI-Referenz](VCI_OPERATIONS_SUMMARY.de.md).
- Multiuser-Server-Mutation, Markierungen, Verwerfen und Commit. Gelieferte lokale Auswahl/Öffnen/Status,
  Inhalts-Lese-/Schreibvorgänge, Übersetzen, lokales Speichern und Nachweisgrenzen stehen in der
  [Multiuser-Referenz](MULTIUSER_OPERATIONS_SUMMARY.de.md).
