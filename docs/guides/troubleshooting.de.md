# Fehlerbehebung

**Sprache:** [English](troubleshooting.md) · Deutsch

Häufige Fehler und ihre Ursachen sowie an einer echten Installation verifiziertes Verhalten von TIA Portal V21,
das überraschend, aber erwartet ist.

## Fehlerbehebung

- `System.Runtime.Remoting.RemotingException` / `TypeLoadException` in .NET 10: Siemens Openness muss im net48-Worker laufen. Baue die Solution neu und stelle sicher, dass die Host-Ausgabe `openness-worker\TiaMcpServer.OpennessWorker.exe` enthält.
- Openness-DLL nicht gefunden: Prüfe, ob TIA Portal V21 installiert ist, und setze `TiaPortalV21Dir` auf den Ordner `PublicAPI\V21\net48`, wenn dein Installationspfad vom Standard abweicht.
- Der Build verwendet `ref/` auf einem Entwicklerrechner: Prüfe, ob `TiaPortalV21Dir` auf den lokalen V21-Ordner `PublicAPI\V21\net48` zeigt, oder erzwinge lokale Referenzen mit `/p:UseTiaPortalReferenceStubs=false`.
- Keine laufende TIA-Portal-Instanz: Starte TIA Portal V21, bevor du Werkzeuge aufrufst, die sich an das aktuelle Projekt anhängen.
- `worker_timeout`: Der Worker hat die konfigurierte Anfragegrenze überschritten (standardmäßig fünf Minuten). Siehe [Worker-Timeout](mcp-client-configuration.md#worker-timeout), um sie zu erhöhen und clientseitig Spielraum zu lassen. Das Ergebnis einer Änderung mit Timeout kann unbekannt sein; untersuche das aktuelle Projekt und das Lifecycle-/Schreib-Audit, bevor du es erneut versuchst.
- Zugriff verweigert oder Fehler beim Anhängen: Stelle sicher, dass der Windows-Benutzer zur Benutzergruppe `Siemens TIA Openness` gehört, und melde dich dann ab und wieder an.
- `dotnet` wählt das falsche SDK: Installiere ein stabiles .NET 10 SDK (`10.0.400` oder neuer in den .NET-10-Feature-Bands) oder passe `global.json` an ein lokal installiertes stabiles .NET-10-Feature-Band an.
- `get_block_content` auf einer S7-300/S7-400-CPU gibt eine Warnung zurück, dass der s7dcl-Rung-Text nicht verfügbar ist: erwartet. Diese CPU-Familien unterstützen den Siemens-Dokumentexport überhaupt nicht. Das Simatic-ML-XML-Dokument wird über eine separate API exportiert und ist vorhanden, daher ist die Nutzlast für das Lesen und für `update_block_logic` vollständig; nur der ergänzende, menschenlesbare Rung-Text fehlt.
- `get_block_content` oder `get_type_content` gibt `status:omitted` mit `reason:"resultExceededItemCharLimit"` zurück: Der Export ist größer als das Wertbudget (standardmäßig 60.000 Zeichen), daher hat er keinen `contentHash` und kann nicht aktualisiert werden. Starte den Server mit `--max-item-chars` (oder `TIA_MCP_MAX_ITEM_CHARS`) auf oder über dem gemeldeten `originalChars` neu, erhöhe `--max-document-chars` mit Spielraum darüber und lies den Baustein in einem eigenen Aufruf. Siehe [Grenzen der Antwortgröße](mcp-client-configuration.md#response-size-limits).

### Fehler beim gebundenen Projekt

- `binding_conflict` bei einem Inhalts-Lesevorgang, -Schreibvorgang oder Übersetzen: Es gibt keine verifizierte Projektbindung. Inhaltswerkzeuge wirken nur auf das gebundene Projekt; ohne verifizierte Bindung (ein konfiguriertes `--project` wird zuerst durch den Status verankert) schlagen sie geschlossen mit `binding_conflict` fehl; rufe zuerst `bind_project` auf. Nach einer Abweichung von Pfad, PID oder Generation rufe `bind_project` mit `forceRebind:true` auf.

### Fehler beim Cursor der Hardware-Paginierung

- `invalid_cursor`: Der Cursor ist fehlerhaft, hat eine nicht unterstützte Form/Version, hat die Signaturprüfung nicht bestanden oder stammt aus einem früheren MCP-Host-Prozess. Host-Neustarts invalidieren absichtlich jeden Hardware-Cursor. Beginne erneut ohne den Cursor.
- `cursor_filter_mismatch`: `deviceName`, `plcName`, `includeIoDetails` oder `includeTagMatches` haben sich geändert. Wiederhole die unveränderte Anfrage am selben Cursor oder beginne eine neue Folge mit den neuen Feldern; kombiniere nie den alten Cursor mit geänderten Feldern.
- `cursor_binding_mismatch`: Die Bindung/der Pfad des Hosts hat sich geändert, oder die Live-Sitzung des Workers hat sich geändert. Bestätige das aktuelle Projekt und beginne eine neue Folge.
- `cursor_snapshot_mismatch`: Passende Geräte/Subnetze oder ihre stabile Reihenfolge haben sich geändert. Beginne eine neue Folge gegen den neuen Projekt-Snapshot.
- `cursor_out_of_range`: Der gespeicherte kombinierte Offset ist nicht mehr gültig. Beginne eine neue Folge.
- `protocol_error`: Die Worker-Antwort hat ihre maßgebliche Sitzungsidentität weggelassen oder fehlerhafte/inkohärente Seitenbelege zurückgegeben. Verwende die Seite nicht; untersuche die Diagnosen von Host/Worker, bevor du von vorn beginnst.

Eine weggelassene Hardware-Seite ist nicht fortgeschritten. Bei `hardwarePageDiagnosticsExceededItemCharLimit` oder
`hardwarePageEntityExceededItemCharLimit` wiederhole die unveränderte Anfrage am selben Cursor oder
beginne eine neue Folge mit engeren Filtern oder weniger Detailoptionen. Eine Seite kann weniger
Entitäten als `pageSize` enthalten, weil der Host nur das größte vollständige kanonische Präfix mit höchstens
60.000 Zeichen behält; fahre fort, bis `nextCursor` fehlt. Das unabhängige Antwortbudget (standardmäßig 180.000 Zeichen)
kann ebenfalls eine vollständige Seite weglassen, daher setze große Hardware-Lesevorgänge in einen eigenen Aufruf.

## Verifiziertes Verhalten von TIA Portal V21

Das Abnahmeprotokoll von Phase 5 dokumentiert die verifizierten Hinweise zur Wiederherstellung für diese zuvor
problematischen Pfade. Round Trips von `update_block_logic` mit mehreren Dokumenten sind verifiziert: Reiche das
exportierte SIMATIC-ML-Dokumentbündel über `plc_write` mit seinem `expectedContentHash` ein, erwarte einen Import, gefolgt von Übersetzen
und Verifikation durch erneuten Export, und behandle eine Ablehnung wegen Struktur/unsicherem Dokument als Ergebnis ohne Änderung.
Ein bearbeitetes Bündel wird ebenso übersetzt und erneut exportiert. Wiederhole einen Schreibvorgang mit
unsicherem Worker-Ergebnis nicht automatisch; untersuche stattdessen den aktuellen Baustein.

SCL-Aufrufe von `create_block` sind verifiziert: Die erzeugte SCL-Quelle enthält eine nicht leere Übersetzungseinheit,
der angeforderte Baustein wird an seinem angeforderten Pfad aufgelöst, und `compile_check` bestätigt, dass er sich übersetzen lässt. Derselbe
abgesicherte `plc_write`-Ablauf gilt für das Anlegen von SCL- und GlobalDB-Bausteinen.

Lesevorgänge von S7-300/S7-400-Bausteinen sind an einer CPU 314C-2 PN/DP (`6ES7 314-6EH04-0AB0/V3.3`) verifiziert.
`PlcBlock.ExportAsDocuments` wird von diesen CPU-Familien grundsätzlich abgelehnt, aber `PlcBlock.Export`
erzeugt das maßgebliche Simatic-ML-XML für GlobalDB-, InstanceDB-, AWL-FC- und KOP-FC-Bausteine auf
derselben CPU. `get_block_content` ist daher mit `format=xml` erfolgreich und trägt eine Warnung, die
das fehlende Dokumentpaket nennt. `format=source` bleibt auf globale Datenbausteine und
FB/FC/OB in der Sprache SCL beschränkt.

`save_project_as` mit `rebind: false` wird vorab mit einer `validation_error`-Antwort abgelehnt,
vor dem Dispatch von Siemens `SaveAs`. Abgesicherte Lifecycle-Ablehnungen werden auditiert. `rebind: true` ist erforderlich;
siehe die [Lifecycle-Referenz](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations).
Ein unterstütztes SaveAs bindet Host und Worker neu an den
vom Worker gemeldeten Pfad des kopierten Projekts; verifiziere das mit einem anschließenden Status- oder Leseaufruf.

## Fallstricke in TIA Portal V21

Von rickgaiser/TiaMcp berichtet, auf V21 nicht verifiziert; Details, Guards und die Liste für die Live-Abnahme stehen in
[V21-Fallstricke](../SupportedOperations/V21_PITFALLS.de.md).

- **HMI-Aufrufe sind sehr langsam:** Schließe einen geöffneten HMI-Variableneditor („Show all tags“) in TIA Portal; er
  verlangsamt Berichten zufolge Openness-Aufrufe auf diesem HMI etwa um das Achtfache.
- **`plc_s7dcl_import_may_alter` in einer `plc_write`-Antwort:** Das Bündel hat kein `.xml`-Dokument, daher wird der
  Baustein über SIMATIC SD importiert. Reiche das Bündel stattdessen mit seinem bearbeiteten SimaticML-Dokument (`.xml`)
  ein. Ein fehlschlagender `s7dclConstructs`-Verifikationseintrag bedeutet, dass der erneute Export von dem abweicht, was
  eingereicht wurde; untersuche den Baustein vor allem anderen.
- **`plc_textual_interface_overwrite_may_terminate`:** Speichere zuerst das Projekt
  (`plc_save_before_write_recommended`). Wenn TIA Portal sich beendet, endet der Aufruf mit `portal_lost`;
  starte TIA Portal neu, öffne das gespeicherte Projekt erneut und binde erneut. Jedes Überschreiben über `plc_write`
  exportiert den Baustein vorher mehr als einmal, daher vermeidet das erneute Öffnen allein den berichteten Zustand nicht.

## Fehler im abgesicherten Lifecycle

- Lokal `unsupported_capability`: Lokale Sitzungen unterstützen Übernahme, Öffnen, Basisstatus,
  Inhalts-Lesevorgänge und -Schreibvorgänge, Übersetzen und `save_project`; die nur für Standalone verfügbaren Operationen Anlegen/Speichern unter/Archivieren/Schließen
  bleiben nicht verfügbar, auch in full oder in einem Dry Run. Verwende nicht das generische `close_project`, um eine lokale Sitzung aufzuräumen.
- Ungültiger lokaler Selektor: Verwende den beobachteten typisierten `.amc21`-Besitzerpfad für Bind/Status/Start;
  ein existierender, rein dateibasierter `.als21`-Pfad gehört zu `open_project`. Inventarverzeichnisse und
  erratene Beziehungen zwischen Dateinamen können weder einen Besitzer noch die Herkunft seines Öffners auflösen.
- `local_session_requires_terminal_operation`: Ein anderes Öffnen würde eine worker-eigene
  lokale Quelle ersetzen. Es blockiert auch im sauberen Zustand und mit force/full. Bewahre sie, bis eine separat
  autorisierte, unterstützte abschließende Operation verfügbar ist.
- `local_session_source_preservation_unproved` oder Ablehnung wegen mehrdeutigem Besitzer/Headless: Erhaltung
  oder exakte doppelte Identität ist nicht bewiesen. Untersuche den exakten Portal-/AMC-Zustand und behalte eine UI/einen Client;
  setze dich nicht darüber hinweg, entsorge keinen einzigen Headless-Besitzer und rekonstruiere keine ALS-Herkunft.
- Das Öffnen eines ALS offline kann einen Verbindungsdialog von Siemens/TIA anzeigen. Beide eingegrenzten Öffnungen in read-write
  wurden nach dem Wegklicken durch den Bediener abgeschlossen; die ursprünglichen nicht interaktiven Prüfungen schlugen fehl. Die
  MCP-Bestätigungsabfrage ist davon getrennt. Untersuche nach einem Timeout oder einem unbeantworteten Dialog das Ziel, bevor du eine weitere Anfrage stellst;
  die Bestätigung per Richtlinie in full garantiert nicht, dass kein Herstellerdialog erscheint.
- Ein generischer Fehler des Project-Server-Inventars ist kein typisierter Beweis für
  Nichtverfügbarkeit/Authentifizierungsfehler. Ein gesunder lokaler Status beweist weder Konnektivität zum entfernten System noch den Sitzungsmodus; explizite
  Beobachtungen von Endpunkten können die derzeit nicht verknüpfte aktive entfernte Identität nicht befüllen.

- `plcsim_api_unavailable`: Der PLCSIM-Worker fand keine ladbare S7-PLCSIM-Advanced-API. Führe
  `tia-mcp doctor --verbose` (Prüfung `plcsim-advanced`) und `plcsim_read` `probe_api` aus; installiere
  PLCSIM Advanced oder setze `TIA_MCP_PLCSIM_API_DIR` auf den absoluten Pfad des Ordners, der
  `Siemens.Simatic.Simulation.Runtime.Api.x64.dll` enthält (ein relativer Wert wird abgelehnt). `plcsim_api_member_missing`: Der installierten API
  fehlt ein Member mit einer deklarierten Signatur; `probe_api` listet auf, was aufgelöst wurde und was
  stattdessen gefunden wurde. `plcsim_runtime_error`: Die API hat ihre eigene Ausnahme ausgelöst (Fehlercode in der Meldung);
  prüfe, ob die Instanz im Control Panel existiert und ihr Name exakt übereinstimmt. Siehe die
  [PLCSIM-Referenz](../SupportedOperations/PLCSIM_OPERATIONS_SUMMARY.md#failure-categories).
- `access_denied`, das Elicitation nennt: Jeder tatsächliche Lifecycle-Aufruf in read-write erfordert eine
  Formular-Annahme mit booleschem `confirm:true`, auch ohne Guards oder nur mit Info-Guards. Ablehnen, Abbrechen,
  Timeout, nicht unterstützte Capability oder Transportfehler blockieren die Änderung. Verwende einen unterstützenden Client;
  full führt Lifecycle per Richtlinie ohne Server-Elicitation aus.
- `guard_blocked`: Untersuche `guards`. Ein existierendes Ziel beim Anlegen/Speichern unter, ein Archiv innerhalb des
  Projektordners oder ein erzwungenes Schließen einer geänderten worker-eigenen Quelle blockiert in jedem Modus. Ein
  Schließen ohne Speichern bei ungespeicherten Änderungen löst den Guard `discards_unsaved_changes` mit Schweregrad Acknowledge aus, erfüllt durch die Annahme
  des Clients in read-write oder per Richtlinie in full. Ein Dry Run zeigt den Guard ohne Änderung oder Rückfrage.
- `binding_conflict` oder Ablehnung wegen geänderten Zustands nach einer Rückfrage: Das Ziel oder die vorbereitete Revision
  hat sich geändert. Untersuche die aktuelle Projektidentität bzw. den Zustand und stelle einen neuen, beabsichtigten Aufruf.
- `portal_lost`: Der Worker hat seine Verbindung zu TIA Portal verloren. Diese Kategorie wird
  domänenübergreifend verwendet, wenn eine fatale Openness-Ausnahme die Ausführungsgrenze erreicht
  oder ein entkommender Engineering-Fehler auf eine unerwartete Entsorgung des Portals folgt, die
  während dieser Ausführung beobachtet wurde. Gewöhnliche Engineering-Fehler behalten `worker_operation_failed`;
  der Text einer Fehlermeldung allein belegt keinen Portalverlust.
  Untersuche das Portal und jede versuchte Änderung, starte das gewünschte Ziel bei Bedarf neu bzw. öffne es erneut
  und rufe dann explizit `bind_project` auf, bevor du den Inhaltszugriff fortsetzt.
  Bindungsbelege und alte Cursor werden invalidiert; die fehlgeschlagene Anfrage wird nie wiederholt.
  Bestehende typisierte Fehler nach einer Änderung und Bereinigungswarnungen können ihre eigenen
  Kategorien behalten, daher untersuche auch deren Ergebnisbelege. Zur Änderung B-16/Q3 gibt es einen
  [Verifikationsbericht](../superpowers/acceptance/reports/2026-10-20-b16-q3-worker-portal-loss-live-verification.md)
  mit eingegrenzten Belegen zu Unterbrechung und Wiederherstellung für Network, PLC, VCI und lokale Sitzungen.
  Der Kandidat `e804e6f` deckte zwei HMI-Lücken auf: Locator-Fehler können nach der Entsorgung
  `worker_operation_failed` behalten und die Invalidierung im Host verzögern; frische
  HMI-Lesevorgänge nach der Wiederherstellung des Portals können an einem entsorgten, zwischengespeicherten Projekt scheitern. Ein
  Neustart des Workers mit anschließendem expliziten erneuten Binden stellte im dokumentierten Lauf die HMI-Lesevorgänge wieder her.
  Offline-Korrekturen vom 2026-10-20 bewahren die Ausnahmen der strikten Discovery und leeren den
  Locator-Cache bei einem anderen Root-Handle, ohne den alten nativen Proxy zu vergleichen.
  Der installierte Stand `2fb5e9a` bestand den eingegrenzten HMI-Lese-Nachtest: `portal_lost`, sofortige
  Ablehnung des alten Cursors und frische Lesevorgänge nach Neustart/erneutem Öffnen des Portals und explizitem
  Binden, durchgehend mit demselben Worker-Prozess. Das ist ein Fixture und ein
  Unterbrechungszeitpunkt; breitere Fehlerorte und native Unterbrechungen von Schreibvorgängen
  bleiben ungetestet.
  Der Maintainer hat die Live-Tests von B-16/Q3 am 2026-10-20 innerhalb des dokumentierten
  Umfangs als abgeschlossen akzeptiert, unter Beibehaltung dieser Grenzen.
- `success:false` mit `error:null` und MCP-`isError:false`: Dispatch oder Verifikation wurde
  versucht. Lies die typisierten Fehler in `result` und `verification`; einer fehlgeschlagenen Verifikation kann eine erfolgreiche Änderung
  vorausgehen. Untersuche nach Timeout, Absturz, Verbindungsabbruch oder unsicherer Änderung
  das Projekt und die Zielartefakte, bevor du es erneut versuchst. Lifecycle-Schreibvorgänge werden nie
  automatisch wiederholt.

Das frühere Bestätigungs-Array des Agenten für Lifecycle und die öffentlichen Eingaben `confirm` und Safety-Token sind entfernt.
Network verwendet jetzt `dryRun:true` für die Vorschau und führt standardmäßig aus, ohne Server-Elicitation, ebenso `plc_write`; kein Werkzeug verwendet Safety-Tokens. Entfernte Root-Eingaben von Network schlagen vor dem Eintritt mit dem kanonischen Dokument des Werkzeugs fehl (`validation_error`, `isError:true`), ohne Audit-Eintrag. Ablehnungen nach dem Eintritt sind kanonisch und werden einmal auditiert; untersuche typisierte Teil-, Verifikations- und Auslassungsergebnisse, bevor du es erneut versuchst. Der entfernte Startschalter schlägt fehl mit:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Verwende `bind_project`, um ein bereits geöffnetes Projekt zu übernehmen oder zu ihm zu wechseln; ein anderer konfigurierter oder
zuvor gebundener Pfad erfordert `forceRebind:true`. Keine Anfrage öffnet implizit ein Projekt: Nur
`open_project` und `create_project` können eines öffnen. Der Openness-Zugriffsdialog von TIA kann beim
erneuten Anhängen erscheinen und erfordert eine menschliche Antwort; er ist von der Lifecycle-Elicitation getrennt. Die
Worker-Besitzerschaft geht über ein Detach verloren. Read-only öffnet, erstellt, speichert oder schließt nie, aber ein explizites
Binden kann seine Sitzung wechseln. Die Doctor-Warnung (Warning) für eine ungebundene Sitzung nennt `bind_project` als Abhilfe.
`cursor_binding_mismatch` beim Projektbaum bedeutet, dass sich die Bindungs-ID/-Revision geändert hat; beginne ein Durchsuchen ohne Cursor,
auch nach der Rückkehr zum ursprünglichen Pfad.

Der [Live-Bericht vom 2026-10-03](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md)
ersetzt für diesen Kandidaten die gültigen älteren Lifecycle-Belege vom 2026-10-01. Alle drei funktionalen
Gruppen haben bestanden, einschließlich der Ablehnungen von Bestätigungen in read-write und der Audit-Herkunft per Richtlinie in full. Der
Maintainer meldete in read-only keinen neuen TIA-Dialog und schloss damit die separate menschliche Beobachtung ab. Offline-/FakeWorker-Prüfungen
und programmatische Abnahme beweisen nicht, dass ein Mensch einen Dialog gesehen hat, und begründen keine Hardware-Abnahme.
