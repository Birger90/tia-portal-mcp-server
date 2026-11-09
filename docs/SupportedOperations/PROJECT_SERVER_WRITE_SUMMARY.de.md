# Project-Server-Verwaltung (`project_server_write`)

**Sprache:** [English](PROJECT_SERVER_WRITE_SUMMARY.md) · Deutsch

`project_server_write` ist die schreibende Oberfläche auf Portal-Ebene für TIA Portal Multiuser (Entscheidung des
Eigentümers vom 2026-11-09). Sie wirkt auf die Project-Server-Verbindungen einer TIA-Portal-Instanz
(`TiaPortal.ProjectServers`) und auf die Server, die diese Verbindungen erreichen. Sie wirkt nie auf ein gebundenes Projekt
und braucht keine Bindung. Die lesende Seite bleibt `bind_project` mit seinen Inventaraktionen (siehe die
[Multiuser-Referenz](MULTIUSER_OPERATIONS_SUMMARY.de.md)); `bind_project` bleibt in jedem Modus rein lesend.

> **Status: nicht live getestet.** Gebaut gegen die reinen Kompilier-Stubs, mit Unit-Tests der Host-Domäne
> (`ProjectServerWriteDomainTests`) und durchgängig über den geskripteten FakeWorker geprüft (`ProjectServerWriteMcpTests`,
> `TIA_MCP_FAKE_WORKER_PROJECT_SERVERS=1`). Nichts davon lief gegen TIA Portal oder einen Project Server. Jedes
> Siemens-Member unten wird zur Laufzeit über seinen dokumentierten Namen und seine Parameterliste gebunden; stellt das
> installierte V21 es nicht bereit, scheitert das Element mit `unsupported_capability`, und nichts wird aufgerufen.

Tool-Anzahlen: 13 Tools in read-only, 33 in read-write und 33 in full seit `project_server_write` (vorher 13/32/32). In
read-only wird das Tool nicht angeboten.

## Verantwortung des Aufrufers: welcher Server

Das Tool wählt nie einen Server aus. Jedes Element nennt die Verbindung über ihren exakten Alias (wie `bind_project`
`list_server_connections` ihn meldet), und der Aufruf wirkt nur auf diesen Server. **Niemals auf dem Project Server eines
Kunden oder eines anderen Teams verwenden, wenn der Benutzer diesen Server nicht ausdrücklich für diese Aufgabe nennt.**
Ein hochgeladenes Projekt sehen alle Benutzer dieses Servers; das Löschen einer lokalen Sitzung kann nicht eingecheckte
Änderungen verlieren. Das MCP kann einen Testserver nicht von einem produktiven unterscheiden; für die richtige Wahl sind
der Agent und der Benutzer verantwortlich.

## Operationen

| Operation | Felder | Siemens-Member (V21 `Siemens.Engineering.Base.xml`) | Guard | Rücklesen |
| --- | --- | --- | --- | --- |
| `create_server_connection` | `serverAlias` (neu), `protocol` `Https`/`Http`, `host`, `port` 1-65535 | `ProjectServerComposition.Create(String alias, Protocol, String host, Int32 port)` → `ProjectServer` | info `project_server_creates_connection`; info `project_server_alias_exists`, wenn der Alias schon gelistet ist (TIA Portal entscheidet) | `list_server_connections`: Alias genau einmal mit Host und Port gelistet (Prüfung `serverConnection`); das Protokoll hat kein dokumentiertes Lesen |
| `set_server_connection` | `serverAlias`; `host`, `port` und/oder `protocol` | `ProjectServer.SetProtocol(Protocol)`, `.SetHostName(String)`, `.SetPort(Int32)` in dieser Reihenfolge | info `project_server_changes_connection` | Host/Port wie oben; eine reine Protokolländerung bleibt `trusted` (kein dokumentiertes Lesen) |
| `delete_server_connection` | `serverAlias` | `ProjectServer.DeleteConnection()` | **acknowledge** `project_server_deletes_connection` | der Alias ist nicht mehr gelistet |
| `add_project_to_server` | `serverAlias`, `group`, `projectPath` (vorhandene `.ap21`) | `ProjectServer.AddProjectToServer(FileInfo)` (Wurzel) oder `ProjectServerGroup.AddProjectToServer(FileInfo)` (benannte Gruppe) | **acknowledge** `project_server_uploads_project` | `list_server_projects` dieser Gruppe listet ein Projekt, das vor dem Aufruf nicht gelistet war (Prüfung `serverProject`; TIA Portal vergibt den Namen, der Dateiname wird als Erwartung gemeldet) |
| `create_local_session` | `serverAlias`, `group`, `serverProjectName`, `sessionName`, `sessionDirectory` (absolut), `sessionMode` `Multiuser` (Standard)/`Exclusive` | `ProjectServer.CreateLocalSession(ServerProjectInfo, String, DirectoryInfo, SessionCreationMode)` → `LocalSessionInfo` | info `project_server_creates_local_session` | `list_local_sessions` listet die zurückgegebene `SessionId` unter dem zurückgegebenen Pfad `ProjectFileInfo` (Prüfung `localSession`) |
| `delete_local_session_from_server` | `serverAlias`, `group`, `serverProjectName`, `sessionId` | `ProjectServer.DeleteLocalSessionFromServer(ServerProjectInfo, LocalSessionInfo)` (die `LocalSessionInfo` stammt aus `GetLocalSessions` mit exakter Id) | **acknowledge** `project_server_deletes_local_session`; info `project_server_bound_session`, wenn die Sitzung das gebundene Projekt ist | die Id ist nicht mehr gelistet |

`group` ist `{"isRoot":true,"name":null}` für die Server-Wurzel oder `{"isRoot":false,"name":"Exakt"}`, genau wie bei
`bind_project`. Ein Feld, das eine Operation nicht dokumentiert, wird abgewiesen (`validation_error`). Jedes Element nimmt
außerdem `portalConfirmations` (unten).

Enums werden über ihre dokumentierten vollen Namen gebunden: `Siemens.Engineering.Multiuser.Protocol` (`Https`, `Http`) und
`Siemens.Engineering.Multiuser.SessionCreationMode` (`Multiuser`, `Exclusive`). Die an
`CreateLocalSession`/`DeleteLocalSessionFromServer` übergebene `ServerProjectInfo` ist die, die `GetServerProjects()` der
Wurzel oder der benannten `ProjectServerGroup` für den exakten Projektnamen liefert, so wie die Inventarlesungen sie auflösen.

### Bindung an eine erzeugte lokale Sitzung

`create_local_session` liefert `sessionId` und `sessionPath` (die `.als21`-Datei). Die Bindung daran ist ein eigener,
abgesicherter `open_project`-Aufruf mit diesem Pfad: Bindungswechsel bleiben bei `open_project`/`bind_project`, die
Quellprojekt, Eigentümerschaft und Erhaltungsregeln prüfen. Innerhalb von `project_server_write` würden diese Prüfungen
umgangen, deshalb bindet das Tool nicht.

### Beispiele

```json
{ "operations": [
  { "operationId": "conn", "operation": "create_server_connection", "serverAlias": "TestServer",
    "protocol": "Https", "host": "mu-test.example", "port": 8735 },
  { "operationId": "upload", "operation": "add_project_to_server", "serverAlias": "TestServer",
    "group": { "isRoot": true, "name": null }, "projectPath": "D:\\Projekte\\Linie7\\Linie7.ap21" }
], "dryRun": true }
```

```json
{ "operations": [
  { "operationId": "session", "operation": "create_local_session", "serverAlias": "TestServer",
    "group": { "isRoot": true, "name": null }, "serverProjectName": "Linie7",
    "sessionName": "Linie7_bs", "sessionDirectory": "D:\\Sitzungen" }
] }
```

## TIA Portal, Bindung und Zugriffsmodus

- **Welches TIA Portal.** Mit einer verifizierten Bindung wirkt der Aufruf im TIA Portal des gebundenen Projekts (eine
  andere `portalProcessId` wird abgewiesen, bevor etwas gesendet wird), und die Identität der Bindung wird gesendet und vom
  Worker vor und nach jedem Aufruf erneut geprüft. Ohne Bindung nennt die optionale `portalProcessId` das TIA Portal
  (`bind_project list_portals`); ohne Angabe nimmt der Worker das TIA Portal, mit dem er verbunden ist, oder das einzige
  laufende.
- **Keine Bindung nötig.** Die Bindungsstrategie fixiert, was die Sitzung hält (ungebunden, konfiguriert oder verifiziert);
  sie verankert, übernimmt, öffnet oder wechselt nie ein Projekt. Die Fähigkeitstabelle des Projektcontainers gilt nicht
  (`IWriteDomain.IsPortalScoped`).
- **Zugriffsmodus.** Read-only: Das Tool wird nicht angeboten, und jede Worker-Methode `project_server_*` wird verweigert
  (`OperationCapability.ProjectLifecycle`). Read-write: Ein Aufruf, der einen acknowledge-Guard auslöst (lokale Sitzung
  löschen, Verbindung löschen, Projekt hochladen), fragt den Benutzer einmal über ein Formular; die übrigen Elemente fragen
  nie. Full: Die acknowledge-Guards gelten per Richtlinie als erfüllt, keine Rückfrage. `dryRun:true` plant und meldet
  Guards und Auswirkungen, ohne den Server aufzurufen und ohne Rückfrage.
- **Zugangsdaten.** Keines der dokumentierten Member nimmt Zugangsdaten entgegen, deshalb hat das Tool kein Passwortfeld.
  Die Anmeldung am Project Server übernimmt TIA Portal selbst (seine Multiuser-Benutzerverwaltung); ein dabei angezeigter
  Dialog ist eine TIA-Portal-Bestätigung und wird über `portalConfirmations` beantwortet.

## Planung, Ausführung und Prüfung

1. **Validierung** (ohne Worker-Aufruf): bekannte Operation, nur ihre dokumentierten Felder, Pflichtfelder, Namen von
   Protokoll und Sitzungsmodus, Portbereich, stimmige `group`, absolute Pfade mit den gemeinsamen Regeln für
   Aufruferpfade (`PlcFileTargets.PathShapeError`), `projectPath` eine vorhandene `.ap21`, `sessionName` ein gültiger
   Dateiname.
2. **Planung** liest nur das vorhandene Inventar: einmal `list_server_connections`, dann je Server-Element
   `list_server_groups` (benannte Gruppe), `list_server_projects` und bei Sitzungselementen `list_local_sessions`. Ein
   fehlender Alias, eine fehlende Gruppe, ein fehlendes Projekt oder eine fehlende Sitzung weist den ganzen Aufruf ab
   (`target_not_found`, „No item of this call ran“). Die Planung bildet die Verbindungsänderungen des eigenen Aufrufs nach:
   Ein späteres Element darf eine Verbindung verwenden, die ein früheres anlegt oder ändert; seine Gruppe, sein Projekt
   oder seine Sitzung wird dann erst bei der Ausführung aufgelöst (info `project_server_resolved_at_mutation`). Dasselbe
   gilt für eine Sitzung, die ein früheres Element anlegt, und für ein Projekt, das ein früheres Element hochlädt.
3. **Bestätigung** (read-write mit acknowledge-Guards): Die Rückfrage nennt die Zeilen der acknowledge-Guards, das TIA
   Portal, in dem der Aufruf wirkt, und je Element eine Zeile mit Ziel, `portalConfirmations`-Richtlinie und `operationId`;
   alle Werte des Aufrufers stehen in Anführungszeichen und sind maskiert (`ConfirmationText`). Nach der Antwort wird der
   Aufruf erneut geplant; ein geändertes Inventar weist ihn ab (`binding_conflict`, nichts geändert).
4. **Ausführung**: Die Elemente laufen der Reihe nach (Worker-Methoden `project_server_<operation>`, Parameter in
   `WorkerRequest.ProjectServer`), und der Aufruf hält beim ersten Fehler ohne Rücknahme oder Wiederholung an. Eine
   `MultiuserException` (der dokumentierte Fehler jedes Multiuser-Aufrufs) oder eine
   `EngineeringTargetInvocationException` ist die Ablehnung durch TIA Portal: `engineering_rejected` mit `engineeringError`.
5. **Prüfung** liest über dieselben Inventarlesungen zurück (Tabelle oben). Ein fehlgeschlagenes Element wird ebenfalls
   zurückgelesen und ist nie verifiziert (K58). Schlüssel: die Verbindung, darunter der Serverinhalt; eine spätere
   Änderung oder Löschung der Verbindung ersetzt daher frühere Elemente auf ihr (mit dem Hinweis, dass das serverseitige
   Ergebnis darüber nicht mehr geprüft ist).
6. **Audit**: ein Audit-v2-Datensatz je Aufruf (auch Trockenläufe und Abweisungen) wie bei jedem abgesicherten Schreiben;
   Bestätigung `user`, `policy` oder `none`.

### Bestätigungsdialoge von TIA Portal

Jedes Element nimmt `portalConfirmations` wie `library_write`: Bis die Belege des Elements gesammelt sind, wird jede
`Confirmation` von TIA Portal mit Nein (`decline`, Standard; aufgezeichnet, das Element schlägt fehl) oder Ja (`accept`;
aufgezeichnet, eine Warnung) beantwortet. Der Worker öffnet `TiaPortalSession.AnswerConfirmations` mit einem
`PlcDownloadPortalConfirmationLog` (Eigentümer = Worker-Methode) und schließt es, bevor er die Belege in
`ProjectServerWriteResultInfo` kopiert (Fehlerbelege über `WorkerResponse.PlcDownloadOutcome`); der geschlossene Bereich
besteht bis zur nächsten Anfrage fort, die späte Dialoge als Warnungen meldet.

## Ergebnis

Das `result` jedes Elements ist `ProjectServerWriteResultInfo`: `operation`, `serverAlias`, `applied` (die gemachten
dokumentierten Aufrufe in Reihenfolge), `hostAfter`/`portAfter` (nach create/set vom `ProjectServer` gelesen),
`sessionId`/`sessionPath` (lokale Sitzung anlegen/löschen), `portalConfirmationPolicy`, `portalConfirmations`,
`portalConfirmationsObserved`, `portalConfirmationsComplete`. Die Antwortwurzel ist das gemeinsame Dokument der
abgesicherten Schreibvorgänge (`tool`, `contractVersion` `1.0`, `phase`, `success`, `error`, `warnings`, `guards`,
`effects`, `batch`, `verification`, `omission`).

## Umsetzung

| Teil | Ort |
| --- | --- |
| Namen, Anfrage- und Ergebnisverträge | `TiaMcpServer.Contracts/Multiuser/ProjectServerWriteInfos.cs` (`ProjectServerMethods`, `ProjectServerRequestInfo`, `ProjectServerWriteResultInfo`) |
| Richtlinie / Fähigkeit | `OperationPolicyCatalog` (`ProjectLifecycle`, keine erwartete Sitzungsidentität nötig), `ProjectCapabilityCatalog.OperationFor` (null: nicht projektbezogen) |
| Worker | `Openness/Multiuser/ProjectServerWriteService.cs`, Verteilung `Program.ProjectServer.cs` über `MultiuserPortalReadDispatch.Run` (reine Portal-Anbindungsprüfungen) |
| Host | `TiaMcpServer/ProjectServers/` (Tool, Bindungsstrategie, Domäne, Guards, Prüfrichtlinie, Budget), Client `OpennessWorkerClient.ProjectServer.cs` |
| FakeWorker | `TiaMcpServer.FakeWorker/Multiuser/ProjectServerScenario.cs` (`TIA_MCP_FAKE_WORKER_PROJECT_SERVERS=1`; Marker `Rejected.ap21`, Sitzungsname `AskDialog`) |

## Ausgelassen

| Member | Grund |
| --- | --- |
| `LocalSession.CloseAndCommit(String)` | Ein Einchecken in den Server aus der gebundenen lokalen Sitzung: ein Lifecycle-Schritt auf der Bindung, keine Serververwaltung; Änderungen für den Benutzer einzuchecken braucht eine eigene Entscheidung des Eigentümers. |
| `LocalSessionComposition.OpenServerProject(FileInfo)` | Öffnet eine lokale Sitzung; das Öffnen bleibt bei `open_project` (`.als21`), das die Bindungsregeln besitzt. |
| `LocalSession.IsUptoDate()`, `LockStateProvider.*` | Lesend; `get_lock_state` zeigt die Sperre bereits, `IsUptoDate` gehört zu einer künftigen Lesung des Sitzungszustands. |
| Löschen von Serverprojekten, Anlegen von Gruppen, Benutzerverwaltung | Kein dokumentiertes V21-Member. |

## Live-Abnahme (offen)

Gegen einen Test-Project-Server, den der Benutzer nennt, in read-write: Verbindung anlegen, listen, Port ändern, löschen
(Rückfrage erscheint); eine kleine `.ap21` hochladen (Rückfrage erscheint) und mit `list_server_projects` finden; eine
lokale Sitzung anlegen und mit `open_project` öffnen; eine andere lokale Sitzung löschen (Rückfrage erscheint); ein von TIA
Portal angezeigter Dialog mit `portalConfirmations` decline und accept; eine `MultiuserException` (etwa ein vorhandener
Alias) als `engineering_rejected` gemeldet.
