# Import und Export von PLC-Inhalten in TIA Portal

**Sprache:** [English](IMPORT_EXPORT_OPTIONS_SUMMARY.md) · Deutsch

## Operationsoberfläche

Der MCP bietet eine fokussierte Schnittstelle für PLC-Round-Trips über vier Operationen (Lesen über `plc_read`, Schreiben über `plc_write`):

| Operation | Richtung | Standardformat | Zweck |
|---|---|---|---|
| `get_block_content` | Lesen | `xml` | Liest einen existierenden PLC-Baustein. |
| `update_block_logic` | Schreiben | `xml` | Aktualisiert einen existierenden PLC-Baustein aus geliefertem Dokumentinhalt. |
| `get_type_content` | Lesen | `source` | Liest einen existierenden PLC-Datentyp. |
| `update_type_content` | Schreiben | `source` | Aktualisiert einen existierenden PLC-Datentyp aus geliefertem Dokumentinhalt. |

Das Feld `format` akzeptiert `xml` und `source`, ohne Beachtung der Groß-/Kleinschreibung. Lesevorgänge für Bausteine und Typen verwenden `plc_read`; Schreibvorgänge verwenden `plc_write` und übergeben das Dokument als `content` zusammen mit `expectedContentHash`, dem `contentHash` aus einem `plc_read` desselben Objekts im selben Format. Seit diesem Lesevorgang geänderter Inhalt scheitert mit `state_changed`. Ein Dokument über dem Wertbudget (standardmäßig 60.000 Zeichen; mit `--max-item-chars` erhöhen, siehe [Grenzen der Antwortgröße](../guides/mcp-client-configuration.md#response-size-limits)) wird im Lesevorgang weggelassen, hat keinen Hash und kann nicht geschrieben werden.

## Unterstützte Formate

### PLC-Bausteine

- `format="xml"` ist der Standard. Lesevorgänge liefern ein kontrolliertes Bundle mit bereinigtem SimaticML und SIMATIC-SD-Dokumenten `.s7dcl`/`.s7res`.
- `format="source"` liefert Siemens-Text externer Quellen für globale DBs (`.db`) und für FB-, FC- und OB-Bausteine in der Sprache SCL (`.scl`).
- Instanz-DBs, Array-DBs und FB/FC/OB-Bausteine, die in LAD, FBD, GRAPH oder STL geschrieben sind, verwenden die XML-Route; sie kommen für die Route über externe Quellen nicht infrage und werden mit einer Meldung abgelehnt, die den adressierten Baustein nennt.

### PLC-Datentypen

- `format="source"` ist der Standard und liefert `.udt`-Text.
- `format="xml"` liefert SimaticML.

## Update-Vertrag

Updates sind strikte Updates eines existierenden Objekts:

- Der Zielpfad muss zu einem existierenden Baustein oder Datentyp aufgelöst werden.
- Die Deklaration im übermittelten Inhalt muss mit dem Namen des adressierten Objekts übereinstimmen.
- Der Import legt keine Objekte an, benennt sie nicht um, löscht sie nicht und führt kein Upsert durch.
- XML-Bausteinimporte verwenden `ImportOptions.Override`.
- Die Generierung aus externen Quellen verwendet `GenerateBlockOption.None`.
- Root-Gruppen, Benutzergruppen und Gruppen externer Quellen, die einer Software Unit gehören, werden gemäß dem adressierten Pfad aufgelöst.

Bei Baustein-Updates versucht der Worker der Reihe nach Validierung, Import oder Generierung des Ziels, Beobachtung des Übersetzens
und einen unabhängigen frischen Lesevorgang zur Nachbedingung und meldet dann, welche Stufen festgestellt wurden. Eine
nachgewiesene Ablehnung vor dem Aufruf am Ziel lässt Import, Übersetzen und abschließendes Lesen auf `not_started`. Beginnt der
Aufruf am Ziel, aber seine Rückkehr kann nicht festgestellt werden, ist das Commitment `unknown`; Übersetzen und
jedes nicht festgestellte abschließende Lesen sind `unavailable`. Auf dem Pfad mit normaler Rückkehr übersetzt der Worker den
betroffenen Umfang und prüft die Nachbedingung, indem er den Baustein frisch auflöst und erneut exportiert.
Updates globaler DBs aus externen Quellen übersetzen auf diesem Pfad die PLC, weil eine Änderung der DB-Deklaration
abhängige Bausteine betreffen kann.

### Belege zum Ergebnis von Baustein-Updates

`plc_write` meldet das nicht atomare Ergebnis jedes `update_block_logic`-Elements als
Importergebnis-Objekt. Das `result` eines erfolgreichen Elements ist `{ format, importOutcome }`; ein fehlgeschlagener Inhaltsimport trägt
dasselbe Objekt als `failure.blockImportOutcome`. `blockImportOutcome` ist ein bedingtes Member im
Fehlerschema jedes strukturierten Tools und wird bei nicht zugehörigen Elementen weggelassen. Der Host verlangt die
Worker-Fähigkeit `typed-block-import-outcome-v1`, bevor er Engineering-Requests sendet, sodass ein älterer
Worker während des Capability-Handshakes abgelehnt wird.

Das Objekt verwendet diese Felder und geschlossenen Werte:

| Feld | Werte und Bedeutung |
|---|---|
| `importStage` | `not_started`, `completed` oder `unknown`. |
| `importResultState` | `success`, `non_success` oder `unavailable`. Ein normal zurückgekehrter Aufruf von `Blocks.Import` oder `GenerateBlocksFromSource` ist `success`; ein normal zurückgekehrtes SIMATIC-SD-Ergebnis wird auf `success` oder `non_success` abgebildet; ein Aufruf, der nicht zurückkehrt, ist `unavailable`. |
| `targetMutationCommitted` | Nullbarer Boolean. `false` beweist eine Ablehnung vor dem Import-/Generierungsaufruf am Ziel; `true` bedeutet, dass dieser Aufruf normal zurückkehrte; `null` bedeutet, dass das Commitment unbekannt ist, nachdem der Aufruf begonnen hat oder der Transport mehrdeutig wurde. Belege zur Anwesenheit allein bestimmen dieses Feld nie. |
| `compileStage` | `not_started`, `succeeded`, `failed` oder `unavailable`. Berichte nur mit Warnungen sind `succeeded`. `unavailable` beruht auf strukturierten Belegen zu Ziel/Sitzung/Aufruf und hat Vorrang vor `failed`, das Vorrang vor `succeeded` hat. |
| `compileReport` | Eine nullbare begrenzte Kopie des gesamten aggregierten Übersetzungsberichts mit TIA Portals eigenen Meldungstexten und Pfaden (die Meldungen von `compile_check`: Containerknoten sind keine Meldungen, Pfade tragen die Containerkette). Er ist null, wenn das Übersetzen nicht begann, und kann null sein, wenn kein Bericht festgestellt werden konnte; ein Teilbericht wird beibehalten, wenn verfügbar. Die vollständigen aggregierten Summen von Fehlern/Warnungen bleiben erhalten, auch wenn Details weggelassen werden. |
| `compileDetailsOmitted` | `true` nur, wenn ein PLC-, Meldungs-, Hinweis- oder String-Detail durch die begrenzte Projektion tatsächlich weggelassen oder gekürzt wurde (oder der Übersetzungsbericht des Workers selbst bereits unvollständig war). |
| `finalReadStage` | `not_started`, `succeeded` oder `unavailable`. |
| `targetPresent` | Nullbarer Boolean. Er ist `true` oder `false`, wenn der unabhängige frische Resolver Anwesenheit oder Abwesenheit feststellt, und nur dann null, wenn die Auflösung selbst nicht verfügbar oder unzuverlässig ist. Ein späterer Export-/Lesefehler kann `finalReadStage=unavailable` hinterlassen und dabei `targetPresent=true` erhalten. Anwesenheit impliziert kein Import-Commitment. |
| `contentRelation` | In diesem Vertrag immer `unknown`. Es wird nicht behauptet, dass der abschließende Export dem angeforderten oder dem vorherigen Inhalt gleicht. |
| `temporarySourceState` | `not_applicable`, `not_created`, `removed`, `residue_possible` oder `unknown`. XML verwendet `not_applicable`; Quell-Updates unterscheiden Ablehnung vor `CreateFromFile`, bestätigtes Entfernen, mögliche Rückstände und mehrdeutiges Anlegen/Aufräumen. Kein temporärer Knotenname oder Pfad wird offengelegt. |

Die Importfelder bilden eine geschlossene Matrix: `not_started` erfordert
`targetMutationCommitted=false` und `importResultState=unavailable`; `unknown` erfordert ein
null-Commitment und `unavailable`; `completed` erfordert `true` und entweder `success` oder `non_success`.
Ein normal zurückgekehrter Aufruf am Ziel ist daher ein konservativer Beleg, dass sich das adressierte Ziel im Speicher
geändert haben kann. Er bedeutet **nicht**, dass das Projekt gespeichert, geladen, außerhalb
der offenen Engineering-Sitzung persistiert, von der Anlage abgenommen oder byte-gleich mit dem Request gemacht wurde.

Belege zum Übersetzen aggregieren den bestehenden ausgewählten Umfang: das exakte Bausteinziel, wenn `blockPath`
angegeben ist, die benannte PLC, wenn nur `plcName` angegeben ist, oder andernfalls alle gefundenen PLCs in deterministischer
Reihenfolge. Das Ergebnis behält höchstens 8 PLC-Einträge, 20 Meldungszeilen, 8 Diagnosehinweise,
256 dekodierte Zeichen pro String und 6.000 dekodierte Diagnosezeichen über Meldungstext,
Pfade und Hinweise hinweg (`BlockImportOutcomeLimits`, gemeinsam genutzt von der Projektion im Worker und dem Validator
im Host). Das vollständige serialisierte `blockImportOutcome` ist auf
12.000 UTF-16-Zeichen begrenzt, einschließlich der Erweiterung durch JSON-Escapes und Metadaten. Innerhalb dieser Grenzen sind die Meldungen
TIA Portals eigene Texte (Steuerzeichen durch Leerzeichen ersetzt, getrimmt), und der Bericht behält seinen
`blockPath`. Hinweise kommen zuerst; Meldungen werden nach Schweregrad ausgewählt (Fehler, dann Warnungen, dann
Informationen, jeweils in Berichtsreihenfolge) und in Berichtsreihenfolge ausgeliefert; nur was nicht passt, wird
weggelassen, und nur dann ist `compileDetailsOmitted` true. Bis 2026-11-02 wurde jeder Meldungstext
durch "Compiler diagnostic omitted." und jeder Pfad durch einen leeren String ersetzt, sodass
`compileDetailsOmitted` immer true war (Defekt K04, live 2026-11-02: vier kurze
Informationsmeldungen nach `set_network_text`); das I2-Design hatte Compilertext wie Ausnahmetext behandelt,
obwohl `compile_check` dieselben Texte liefert. Nur offline getestet; nicht live getestet.

Der sequenzielle Aufruf bleibt nicht atomar. Die Verarbeitung stoppt beim ersten fehlgeschlagenen Element, und spätere Elemente
werden als `skipped` markiert; der Server wiederholt nicht, rollt nicht zurück, kompensiert nicht, speichert nicht und lädt nicht.
Unsicherer, aus Ausnahmen abgeleiteter Meldungstext wird bewusst durch begrenzte Zusammenfassungen ersetzt. Das fixierte
Projekt-Binding, die Zugriffs-Policy, die Guards und das Audit der abgesicherten Pipeline gelten unverändert.

### Belege in der Vorschau

Der `plc_write`-Effekt jedes `update_block_logic`- und `update_type_content`-Elements trägt ein strukturiertes
`contentDiff`-Objekt, in Vorschauen und tatsächlichen Aufrufen gleichermaßen. Es vergleicht den aktuellen Text im exakten Format,
der bei der Planung gelesen wurde, mit dem übermittelten `content`; es sagt den Siemens-Zustand nach dem Schreiben nicht voraus und ist
nur ein Beleg, nicht Teil der Hash-Prüfung. Gleiches wird mit Gleichem verglichen (seit 2026-11-02, Defekt K31:
live zeigte ein per einfachem XML hinzugefügter Kontakt 157 geänderte Zeilen, weil das ganze Export-Bundle mit
seinen `--- FILE:`-Headern und dem `.s7dcl`-Teil verglichen wurde): Im Format `xml` wird ein übermitteltes Dokument
ohne `--- FILE:`-Trennzeilen nur mit dem `.xml`-Dokument des aktuellen Export-Bundles
verglichen, und `contentDiff.currentDocument` benennt dieses Dokument (andernfalls abwesend; Hash und Zahlen der aktuellen Seite
sind dann die dieses Dokuments; ein abschließender Zeilenumbruch, den das Bundle-Layout hinzufügt, wird nicht
gezählt, wenn das übermittelte Dokument keinen hat). Ein übermitteltes Bundle wird mit dem ganzen
Bundle verglichen und jedes andere Format mit dem ganzen Export. Nicht live getestet.

- Jede infrage kommende Operation hat höchstens 40 Auszugszeilen und 8.192 Auszugszeichen pro Seite.
- Wenn ein geänderter Abschnitt 40 Zeilen überschreitet, enthält jeder Auszug die ersten 20 plus die letzten 20 Zeilen.
- Jede angezeigte Zeile ist auf 512 Zeichen begrenzt.
- Der vollständige Aufruf ist auf 320 Auszugszeilen und 32.768 Auszugszeichen in
  Request-Reihenfolge begrenzt. Nachdem dieses Auszugsbudget erschöpft ist, behält jeder infrage kommende Eintrag weiterhin seine rohen Hashes,
  Zahlen und Gleichheitsflags.
- Roher SHA-256, rohe Zeichenzahl und rohe Zeilenzahl verwenden den Originaltext. Nur für den Vergleich der Zeilenfenster
  bildet die Normalisierung der Zeilenenden CRLF und CR auf LF ab; sie führt keine andere
  Normalisierung durch. Die Antwort meldet außerdem `rawTextEqual`, `normalizedLinesEqual` und
  `lineEndingOnly`; ein Unterschied nur in den Zeilenenden hat ungleichen Rohtext, aber gleiche normalisierte Zeilen.
- Jede andere Operation hat `contentDiff: null`.

## V21-Fallstricke

Berichtet von rickgaiser/TiaMcp, auf V21 nicht verifiziert; siehe [V21-Fallstricke](V21_PITFALLS.de.md).

- Ein an `update_block_logic` übermitteltes Bundle **ohne** abgegrenztes `.xml`-Dokument, aber mit einem
  `.s7dcl`-Dokument wird über SIMATIC SD (`ImportFromDocuments`) importiert; das ist der einzige Schreibpfad
  für `.s7dcl` von `plc_write` (`update_type_content` importiert nie `.s7dcl`). Von diesem Import wird berichtet, dass er
  einen Array-Zugriff innerhalb eines Index in einem LAD/FBD-Netzwerk, ein Member, das mit einem Projekt-FB/-UDT typisiert ist, der wie ein
  Systemtyp benannt ist, und AT-Überlagerungen ohne Fehler verändert. Die Planung löst den informativen Guard
  `plc_s7dcl_import_may_alter` mit den gefundenen Konstrukten aus, das Element trägt eine Warnung, und der
  Verifikationseintrag `s7dclConstructs` vergleicht jedes Konstrukt mit dem erneuten Export und schlägt fehl, wenn es
  abweicht. `get_block_content` (`xml`) warnt vor solchen Bausteinen.
- Von einem Überschreiben eines Bausteins mit textueller Schnittstelle wird berichtet, dass es TIA Portal beendet, wenn seit dem Öffnen des Projekts oder dem letzten
  Schreiben des Bausteins mehr als ein Export erfolgt ist. Der Worker zählt seine Exporte pro
  Baustein; die Planung löst `plc_textual_interface_overwrite_may_terminate` und
  `plc_save_before_write_recommended` aus (beide informativ). Dieser Schreibpfad selbst exportiert den Baustein vor dem Import mehr
  als einmal.

## Formatmatrix

| Fähigkeit | Status |
|---|---|
| Lesen und Aktualisieren von PLC-Bausteinen als SimaticML/XML | Unterstützt |
| Behandlung von SIMATIC-SD-Bundles `.s7dcl`/`.s7res` für PLC-Bausteine | Teilweise: als kontrollierter Bundle-Kontext unterstützt |
| Austausch von PLC-Bausteinen über externe Quellen `.db` | Teilweise: globale DBs |
| Austausch von PLC-Bausteinen über externe Quellen `.scl` | Teilweise: FB/FC/OB in der Sprache SCL |
| Austausch von LAD/FBD/GRAPH/STL-FB/FC/OB über externe Quellen, Instanz-DBs und Array-DBs | Nicht unterstützt; die XML-Route verwenden |
| Austausch von PLC-Datentypen als SimaticML/XML | Unterstützt |
| Austausch von PLC-Datentypen als `.udt` | Unterstützt |
| Update existierender Objekte | Unterstützt |
| Anlegen, Umbenennen, Löschen oder Upsert über den Import | Nicht unterstützt |
| Zielrouting für Root, Benutzergruppe und Software Unit | Unterstützt |
| Vom Aufrufer gewählte Openness-`ExportOptions` | Teilweise: XML-Export verwendet `None` |
| Vom Aufrufer gewählte Openness-`ImportOptions` | Teilweise: Importe verwenden `Override` |
| `SWImportOptions` für strukturelle Änderungen oder fehlende Referenzen | Nicht unterstützt |
| Austausch know-how-geschützter Bausteine | Teilweise: Der generische Export kann nur die öffentliche Schnittstelle offenlegen oder scheitern; geschützter Import ist nicht verfügbar |
| Eigener Austausch fehlersicherer Bausteine | Nicht unterstützt; kompatible konsistente F-Bausteine können die generische XML-Route verwenden |
| Austausch von Systembausteinen | Nicht unterstützt |
| Variablentabellen, Variablen und Konstanten über Import/Export | Eine Variablentabelle als SimaticML-Datei: `plc_write export_tag_table` / `import_tag_table` (nicht live getestet; siehe [Referenz der BetterTIA-Operationen](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions)). CSV-Export (kein Import) über `plc_write export_tag_tables_csv` |
| Verwaltung externer Quellen (Auflisten, Import mit `CreateFromFile`, `GenerateBlocksFromSource` mit `None`/`KeepOnError` in eine Gruppe, Löschen, `GenerateSource` mehrerer Bausteine/Typen in eine Datei des Aufrufers) | Unterstützt (nicht live getestet); siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#external-sources) |
| Batch-Export aller Bausteine/Typen einer PLC oder Gruppe in ein Verzeichnis (SimaticML-Dokumente oder Quellen, SHA-256 pro Datei) | Unterstützt (nicht live getestet) über `plc_write export_blocks`, aufgebaut auf den obigen Lesevorgängen |
| Technologieobjekte | SimaticML über `plc_write` `export_technology_object`, `import_technology_object` und `update_technology_object_content` sowie `plc_read` `get_technology_object_content`; siehe [TECHNOLOGY_OBJECTS_SUMMARY.md](TECHNOLOGY_OBJECTS_SUMMARY.de.md) (nicht live getestet) |
| Beobachtungstabellen und Forcetabellen | SimaticML über `plc_read` `get_watch_table_content`/`get_force_table_content` und `plc_write` `update_watch_table_content`/`update_force_table_content` (`Import(..., ImportOptions.Override)` hinter `expectedContentHash`); siehe [PLC_ONLINE_OPENNESS_SUMMARY.md](PLC_ONLINE_OPENNESS_SUMMARY.de.md) (nicht live getestet); CSV-Export (kein Import) über `plc_write` `export_watch_tables_csv`/`export_force_tables_csv` |
| Austausch von PLC-Meldungen und ProDiag | Unterstützt (nicht live getestet): `plc_write` `export_/import_alarm_texts`, `export_/import_alarm_classes`, `export_/import_text_lists`, `export_/import_supervisions`, `import_supervision_settings`, `export_prodiag_info`, `export_/import_system_diagnostics_settings`; siehe [PLC_ALARMS_TEXTLISTS_SUMMARY.de.md](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md#dateien) und [ProDiag](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md#prodiag) |
| Austausch von Hardware-CAx (AutomationML) | Unterstützt (nicht live getestet): `network_write` `export_cax`/`import_cax`; siehe [HARDWARE_MODULES_SUMMARY.de.md](HARDWARE_MODULES_SUMMARY.de.md#cax-daten-automationml) |
| Austausch von Projekttexten | Unterstützt (nicht live getestet): `plc_write` `export_project_texts`/`import_project_texts` (`.xlsx`); siehe [PLC_OPERATIONS_SUMMARY.de.md](PLC_OPERATIONS_SUMMARY.de.md#projektsprachen-und-projekttexte-plc_readplc_write) |
| Austausch von Grafiken | Nicht unterstützt: V21 dokumentiert `GraphicsProvider`, aber nicht, welches Objekt ihn anbietet; siehe [HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md#weggelassen-issue-28) |
| HMI-Import/-Export | Nicht unterstützt; siehe [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.de.md) |
| Generischer XML-Import/-Export beliebiger Objekte | Nicht unterstützt |

## Beziehung zur Openness-API

TIA Portal Openness unterstützt weitere APIs zum Austausch von PLC-, HMI-, Projekt-, Hardware-, Meldungs- und Textdaten. Diese MCP-Oberfläche stellt absichtlich nur die oben beschriebenen Routen für PLC-Bausteine und PLC-Datentypen bereit. Sie bietet keinen generischen Proxy für beliebige `Import`- und `Export`-Aufrufe.
