# PLC-Meldungen, Meldeklassen, Textlisten, Überwachungen und ProDiag (`plc_read`, `plc_write`)

**Sprache:** [English](PLC_ALARMS_TEXTLISTS_SUMMARY.md) · Deutsch

**Nicht live getestet.** Alles auf dieser Seite beruht auf den V21-Quellen, die unter
[Herkunft](#herkunft-jedes-verwendeten-siemens-members) aufgeführt sind, und ist nur offline verifiziert (Unit-Tests, das
FakeWorker-Szenario `plc-alarms-roundtrip`, die nur übersetzte Referenzprobe). Die
[Live-Abnahmeprüfungen](#live-abnahmeprüfungen) sind offen.

Die Operationen decken ab, was die dokumentierte V21-Openness-Oberfläche für PLC-Meldedaten bietet:

- **Textlisten** (`PlcSoftware.PlcAlarmTextlistGroup`): Anwender- und Systemtextlisten mit Name, ID,
  Listenbereich und mehrsprachigem Kommentar auflisten; eine Anwenderliste umbenennen, ihren Bereich und ihren Kommentar pro
  Kultur ändern; eine Anwenderliste löschen; eine aus einer Kopiervorlage anlegen (`library_write`); Einträge über den
  XLSX-Export und -Import von TIA Portal lesen und ändern; ganze Textlisten-Arbeitsmappen exportieren und importieren.
- **Programmmeldetexte** (`PlcAlarmTextProvider`): Texte von Meldungsinstanzen pro Kultur über den XLSX-Export und -Import
  lesen und ändern; Meldetext-Arbeitsmappen exportieren und importieren.
- **Gemeinsame Meldeklassen** (`AlarmClassDataProvider`): exportieren und importieren (projektweit).
- **ProDiag-Überwachungen** (`SupervisionProvider` eines ProDiag-FB): die Zellen der Überwachungs-Arbeitsmappe lesen und ändern;
  Überwachungen exportieren und importieren; Überwachungseinstellungen importieren.
- **ProDiag** (blocks.md Abschnitt 12): die dokumentierten Merkmale eines ProDiag-FB, das Attribut
  `AssignedProDiagFB` von Instanz-DBs, UDT-Instanz-Membern globaler DBs und von Variablen (lesen und
  setzen) sowie der Export der ProDiag-Meldeinformationen des FB (`ExportProDIAGInfo`, CSV).

Systemtextlisten werden über diesen Server nie geändert. Der Zugriff folgt allein dem Zugriffsmodus: Lesen in
jedem Modus, Schreiben in read-write und full; keine Schreibrückfragen.

## Warum `plc_read`/`plc_write` und kein neues Tool-Paar

AGENTS.md leitet PLC-Softwareinhalte über `plc_read`/`plc_write` (Variablen, Bausteine, Beobachtungs- und Force-Tabellen,
granulare Bausteinänderungen) und fügt eine Domäne einem bestehenden Tool hinzu, wenn sie zu dessen Selektoren passt
(Hardwaremodule wurden zu Operationen von `network_read`/`network_write`). Textlisten, Meldetexte,
Überwachungen und ProDiag-Objekte gehören zu einer PLC (`plcName`, ein Bausteinpfad oder eine Variable) und nutzen die
bestehende PLC-Bindung, den Planer, die Guards, die Verifikation und das Audit. Die Tool-Oberfläche bleibt daher
**13/33/33**. Die gemeinsamen Meldeklassen sind der einzige projektweite Teil: Sie werden über
`Project.GetService<AlarmClassDataProvider>()` erreicht, nehmen keinen `plcName`, zeigen im Effektziel einen leeren `plcName`
und werden nur für Standalone-Projekte angeboten (eigene `standaloneOnly`-Capability-Zeilen).
Ein eigenes Paar (nach dem Vorbild der Bibliotheken) war nicht nötig: Bibliotheken reichen über Projekte und globale
Bibliotheksdateien hinweg, diese Operationen nicht.

## Operationen

### `plc_read`

| Operation | Felder | Capability | Ergebnis |
| --- | --- | --- | --- |
| `list_text_lists` | optional `plcName` (weggelassen: jede PLC) | Observe | `{ projectLanguages { active[], editing, reference, complete, messages[] }, plcs[{ plcName, deviceName, userTextLists[], systemTextLists[], complete, messages[] }] }`; jede Liste `{ name, isSystem, id, listRange, comment[{ culture, text }] \| null, unreadable[] }` |
| `read_text_list_workbook` | optional `plcName`, `textListNames`, `cultures` (nur mit `textListNames`), `maxElements`, `sheet`, `startRow`, `startColumn` | TemporaryExport | Arbeitsmappenzellen (unten) |
| `read_alarm_text_workbook` | `exportOption`; optional `plcName`, `cultures`, `maxElements`, `sheet`, `startRow`, `startColumn` | TemporaryExport | Arbeitsmappenzellen |
| `read_supervision_workbook` | `blockPath` (ProDiag-FB); optional `maxElements`, `sheet`, `startRow`, `startColumn` | TemporaryExport | Arbeitsmappenzellen plus `programmingLanguage`, `supervisionCount` |
| `get_prodiag_fb` | `blockPath` | Observe | `{ blockPath, plcName, blockType, programmingLanguage, isProDiag, supervisionCount, isConsistent, hasSupervisionProvider, attributes[{ name, value }], unreadable[] }` |
| `get_prodiag_assignment` | eine Variable (`tableName`, `name`, optional `folderPath`, `plcName`) oder ein Baustein (`blockPath`, optional `memberPath`) | Observe | `{ targetKind, target, plcName, assignedProDiagFb, readable, message }` |

**Arbeitsmappen-Lesevorgänge** exportieren in ein frisches temporäres Verzeichnis, das immer wieder gelöscht wird, lesen die Zellen
und liefern `{ kind, plcName, deviceName, blockPath, programmingLanguage, supervisionCount, cultures[],
textListNames, exportOption, exportState, workbookHash, sheet, startRow, startColumn, sheets[{ name,
rowCount, columnCount, cellCount, rows[{ row, cells[{ cell, kind, value, valueTruncated }],
cellsTruncated, nextStartColumn }], rowsTruncated, nextStartRow }], probes[], complete, messages[] }`.

- `maxElements` begrenzt die Zeilen pro Arbeitsblatt (1-5000, **Standard 50**). Der Lesevorgang endet außerdem am
  Zeichenbudget; geschätzt wird dabei jedes Zeichen außerhalb des druckbaren ASCII und jedes HTML-sensible
  als das sechsstellige `\uXXXX`-Escape, das der kanonische Writer ausgibt. Das Budget ist das
  effektive Wertbudget (standardmäßig 60.000, erhöht mit `--max-item-chars`) abzüglich 4.000 für den Rest
  des Elements; enthält ein `plc_read`-Aufruf mehrere Arbeitsmappen-Lesevorgänge, erhält jeder höchstens einen gleichen Anteil
  am Dokumentbudget (standardmäßig 180.000, `--max-document-chars`) abzüglich 20.000 für die Hülle
  und die übrigen Elemente, abzüglich derselben 4.000 (mit den Standardwerten: 56.000 für einen oder zwei Lesevorgänge, 49.333
  für drei, 12.000 für zehn), nie weniger als 2.000 (ab 27 Lesevorgängen mit den Standardwerten). Ein Leseergebnis
  bleibt daher innerhalb des Wertbudgets; das Dokumentbudget kann dennoch ganze Ergebnisse auslassen (mit
  Hinweisen zum Wiederholen, nie abgeschnitten), wenn die übrigen Elemente des Aufrufs groß sind oder die Untergrenze von 2.000 greift.
- Die erste Zeile einer Seite kommt immer voran, ohne das Budget zu überschreiten: Passt sie allein nicht,
  werden die Zellen geliefert, die vollständig passen (nur die erste Zelle wird abgeschnitten, `valueTruncated`, wenn selbst
  sie nicht passt), und die Zeile meldet `cellsTruncated:true` mit `nextStartColumn`, der ersten nicht gelieferten
  Spalte. Den Rest der Zeile liest man mit `sheet`, `startRow` = diese Zeile und `startColumn` =
  `nextStartColumn`.
- `sheet` liefert nur Zeilen dieses Arbeitsblatts (exakter Name, sonst die eine Übereinstimmung ohne Beachtung der Groß-/Kleinschreibung;
  andernfalls `target_not_found`), ab `startRow` (Standard 1) und `startColumn` (Großbuchstaben A
  bis XFD, Standard A; Zeilen ohne Zelle ab dieser Spalte werden übersprungen); beide setzen `sheet` voraus. Jedes
  Blatt mit verbleibenden Zeilen meldet `rowsTruncated:true` und `nextStartRow`, die Zeilennummer, mit der weitergeblättert
  wird; nicht ausgewählte Blätter melden ihre Anzahlen und `nextStartRow` = ihre erste Zeile.
- Ein Zellwert wird bei 4.000 Zeichen abgeschnitten (`valueTruncated`); `complete` ist false, wenn irgendetwas
  abgeschnitten wurde. `workbookHash` (`cells:sha256:<hex>`) umfasst immer jede Zelle jedes Arbeitsblatts, unabhängig davon, was
  geliefert wurde. `probes` wird nur von den eigenen Planungs- und Verifikations-Lesevorgängen des Servers verwendet.
- Jeder TIA-Portal-Bestätigungsdialog, der während des Exports eines Lesevorgangs erscheint, wird mit **Nein** beantwortet (ein Lesevorgang
  stimmt nie zu) und lässt den Lesevorgang fehlschlagen, weil der Export dann unvollständig sein kann.

### `plc_write`

Jeder Schreibvorgang nimmt optional `portalConfirmations` ([unten](#bestätigungsdialoge-von-tia-portal)).

| Operation | Felder | Was sie tut |
| --- | --- | --- |
| `update_text_list` | `textListName`; optional `plcName`, `newName`, `listRange`, `comment` + `commentCulture` | Benennt zuerst um und liest den Namen zurück (TIA Portal kann eine kollidierende Umbenennung stillschweigend ignorieren, wie bei Variablen beobachtet), setzt dann `ListRange`, dann das Kommentarelement der Kultur. Ein neuer Name, den bereits eine andere Anwender- oder eine Systemliste verwendet (ohne Beachtung der Groß-/Kleinschreibung, wie TIA Portal vergleicht), wird bei der Planung abgelehnt; die Beschreibbarkeit laut `GetAttributeInfos`, das Enum-Member und das Kultur-Element werden alle vor der ersten Änderung geprüft; jedes Member wird zurückgelesen, und ein Fehlschlag nennt die bereits angewendeten Member. |
| `delete_text_list` | `textListName`; optional `plcName` | `PlcAlarmUserTextlist.Delete()`, prüft dann, dass die Liste verschwunden ist. Info-Guard `plc_deletes_text_list`. |
| `update_text_list_cells` | `cellEdits`, `expectedWorkbookHash`, `textListNames` (nur Anwenderlisten); optional `plcName`, `cultures`, `importOption` | Arbeitsmappen-Round-Trip (unten) mit dem gefilterten `ExportToXlsx(file, textListNames, languages)` und `ImportFromXlsx(file, importOption)`. |
| `update_alarm_text_cells` | `exportOption`, `cellEdits`, `expectedWorkbookHash`; optional `plcName`, `cultures` | Round Trip mit `ImportInstanceTextsFromXlsx(file, languages)`. |
| `update_supervision_cells` | `blockPath`, `cellEdits`, `expectedWorkbookHash`; optional `importOption` | Round Trip mit `ImportSupervisionsFromXlsx(file, importOption)`. |
| `export_text_lists` | `filePath` (`.xlsx`); optional `plcName`, `overwrite`, `textListNames`, `cultures` | `ExportToXlsx` in die Datei des Aufrufers. |
| `export_alarm_texts` | `filePath` (`.xlsx`), `exportOption`; optional `plcName`, `overwrite`, `cultures` | `ExportInstanceTextsToXlsx` in die Datei des Aufrufers. |
| `export_alarm_classes` | `filePath` (Endung `.DAT`); optional `overwrite` | `AlarmClassDataProvider.Export`. Nur Standalone. |
| `export_supervisions` | `blockPath`, `filePath` (`.xlsx`); optional `overwrite` | `ExportSupervisionsToXlsx`. |
| `import_text_lists` | `filePath`; optional `plcName`, `importOption` | `ImportFromXlsx` einer privaten Kopie, dann ein Verifikations-Re-Export. Info-Guard `plc_imports_alarm_data`. |
| `import_alarm_texts` | `filePath`; optional `plcName`, `cultures`, `exportOption` (für den Verifikations-Re-Export) | `ImportInstanceTextsFromXlsx`; ohne `exportOption` wird der Import nicht zurückgelesen (`trusted` mit einer Warnung). |
| `import_alarm_classes` | `filePath` | `AlarmClassDataProvider.Import`, dann ein Re-Export, der Byte für Byte verglichen wird. Nur Standalone. |
| `export_system_diagnostics_settings` | `filePath` (beliebige Endung); optional `overwrite` | `SystemdiagnosticsSettingsDataProvider.Export(FileInfo)`. Nur Standalone; siehe [Systemdiagnose-Einstellungen](#systemdiagnose-einstellungen). |
| `import_system_diagnostics_settings` | `filePath` | `SystemdiagnosticsSettingsDataProvider.Import(FileInfo)`, dann ein Re-Export, der Byte für Byte verglichen wird. Nur Standalone. |
| `import_supervisions` | `blockPath`, `filePath`; optional `importOption` | `ImportSupervisionsFromXlsx`, dann ein Verifikations-Re-Export. |
| `import_supervision_settings` | `blockPath`, `filePath`; optional `importOption` | `ImportSupervisionSettingsFromXlsx`; nicht zurückgelesen (kein dokumentierter Export der Einstellungen; `trusted` mit einer Warnung). |
| `set_prodiag_assignment` | `proDiagFb`; das Ziel von `get_prodiag_assignment` | `SetAttribute("AssignedProDiagFB", <FB name>)`, zurückgelesen über den FB-Namen ([ProDiag](#prodiag)). Der FB muss ein FB derselben PLC sein; einer, dessen Sprache nicht ProDiag ist, wird mit einer Warnung weitergereicht. |
| `export_prodiag_info` | `blockPath` (ProDiag-FB), `directoryPath` (darf nicht existieren) | `CodeBlock.ExportProDIAGInfo(DirectoryInfo)` in ein temporäres Geschwisterverzeichnis, das dann an seinen Platz verschoben wird; jede Datei wird mit Größe und SHA-256 aufgeführt. Ein Export, der keine Datei schreibt, schlägt mit `postcondition_failed` fehl und legt kein Verzeichnis an. Ein FB, der nicht ProDiag oder inkonsistent ist, wird mit einer Warnung weitergereicht (das dokumentierte Beispiel exportiert konsistente ProDiag-FBs). |

`library_write` erhält zwei dokumentierte Arten: `create_master_copy` mit der Quellart `textList` (`path` = der
Name der Anwendertextliste, optional `plcName`; `MasterCopies.Create(alarmTextList)` aus dem offiziellen
Skill) und `instantiate_master_copy` mit der Zielart `textLists` (`plcName`;
`PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)`). Das ist der dokumentierte Weg,
eine Anwendertextliste **anzulegen**; siehe die [Bibliotheksreferenz](LIBRARY_OPERATIONS_SUMMARY.de.md).

`importOption` ist `None` oder `Override` (Standard `Override`, die Option, die jedes Importbeispiel in den
Quellen übergibt; die Quellen geben an, dass `ActivateInactiveCultures`/`SkipInactiveCultures` für den Textlistenimport nicht
unterstützt werden). `exportOption` ist ein Member-Name von `PlcAlarmTextXlsxExportOption`:
die Quellen nennen den Typ, aber nicht seine Member, also entscheidet das installierte TIA Portal; ein unbekannter Name
schlägt mit der Liste der installierten Member fehl. `listRange` ist ebenso ein Member-Name des Laufzeittyps
des aktuellen `ListRange`-Werts der Liste, wie `list_text_lists` ihn meldet.

## Systemdiagnose-Einstellungen

> **Nicht live getestet** (GitHub-Issue #19). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.HW.Systemdiagnostics.Settings.SystemdiagnosticsSettingsDataProvider.Export(System.IO.FileInfo)` und
> `.Import(System.IO.FileInfo)` ("export/import system diagnostics project relevant settings", beide liefern
> `SystemdiagnosticsSettingsExportImportResult`), `P:...SystemdiagnosticsSettingsExportImportResult.State` und die Aufzählung
> `SystemdiagnosticsSettingsExportImportResultState` mit `Success` ("Succesful process"), `Warning` ("Succesful process with
> warnings") und `Error` ("Failed process").

`export_system_diagnostics_settings` und `import_system_diagnostics_settings` sind projektweite `plc_write`-Elemente dieser
Familie (`ProjectMutation`, read-write und full; kein `plcName`; eigene Standalone-only-Zeilen im Fähigkeitskatalog, eine
gebundene lokale Sitzung wird in der Planung abgewiesen) und werden genau wie die Meldeklassen behandelt: `plc_alarm_plan` bindet
zuerst alles, der Export wird neben dem Ziel bereitgestellt, an seinen Platz verschoben, neu gehasht und ersetzt eine Datei nie
ohne `overwrite:true`; der Import läuft auf einer privaten Kopie und wird durch einen erneuten Export und einen Byte-Vergleich der
Dateien verifiziert (`importCheck.method` `fileHash`; ein Unterschied ist `inconclusive`, weil das Format nicht dokumentiert ist);
`portalConfirmations` beantwortet Dialoge von TIA Portal.

- **Provider.** Die XML sagt nicht, welches Objekt den Provider anbietet; der Worker holt
  `Siemens.Engineering.HW.Systemdiagnostics.Settings.SystemdiagnosticsSettingsDataProvider` über die `GetServiceInfos()` des
  Projekts mit exaktem vollem Namen und ruft ihn mit `IServiceProvider.GetService(Type)` ab; nicht gelistet oder nicht geliefert
  ist `unsupported_capability`, bevor etwas läuft.
- **Bindung.** Weder der Provider noch der Ergebnistyp stehen in den Compile-Stubs: `Export(FileInfo)` und `Import(FileInfo)`
  werden am Live-Provider über exakten Namen und Parameter gebunden, `State` am Rückgabetyp der gebundenen Methode
  (`PlcAlarmResultReader.BindStateOnly`; die XML dokumentiert kein anderes Ergebnis-Member); fehlt eines, ist das
  `unsupported_capability`.
- **Zustände.** `Success` ist erfolgreich; `Warning` ist erfolgreich mit einer Warnung (die XML nennt es einen erfolgreichen
  Vorgang mit Warnungen, wie bei den Meldeklassen); `Error`, jeder andere Name und ein unlesbarer Zustand scheitern mit
  `worker_operation_failed`.
- **Dateiendung (K46).** TIA Portal V21 zeigte bei einer Meldeklassen-Datei mit falscher Endung einen modalen Dialog. Für die
  Systemdiagnose-Einstellungen nennt weder die XML noch ein Siemens-Beispiel eine Endung, daher wird keine verlangt oder
  abgewiesen; welche TIA Portal erwartet, ist bis zu einem Live-Test unbekannt.
- **Noch nötige Live-Abnahme:** dass das Projekt den Provider listet, welche Endung TIA Portal schreibt und annimmt, der
  Ergebniszustand eines normalen Exports und Imports, und ob ein Re-Export nach einem Import byte-gleich ist.

## Planung (`plc_alarm_plan`)

Jedes Melde-Schreibelement durchläuft zuerst das nicht verändernde `plc_alarm_plan` des Workers (Observe, nur im Worker,
gegated wie `plc_write`), auch in einem dryRun: Es löst PLC, FB, Liste oder Variable auf und bindet jedes Siemens-
Member, das das Element aufrufen wird (Provider-Member über den dokumentierten Namen und die Argumenttypen, Ergebnis-Member am
deklarierten Rückgabetyp des Aufrufs), **ohne eines davon aufzurufen**, sodass ein Member oder ein Ergebnis-`State`, das
sich nicht binden lässt, mit `unsupported_capability` fehlschlägt, bevor etwas läuft. Es meldet den Pfad des Aufrufers so, wie
das Dateisystem des Workers ihn sieht (unten). Schreibt ein früheres Element desselben Aufrufs auf dieselbe PLC, lässt eine
Planungsablehnung, die ein solches Element verursachen kann (eine Liste, die es anlegt, umbenennt oder löscht), die Planung nicht
fehlschlagen; der Worker wiederholt jede Prüfung bei der Mutation. Eine Zellenänderung liest dann dieselbe Arbeitsmappe einmal (die
Probe unten).

## Systemtextlisten

Systemtextlisten werden wie Anwenderlisten gelesen (`list_text_lists`, `read_text_list_workbook`,
`export_text_lists` mit ihren Namen). Sie werden nie über eine Listenoperation oder eine Zellenänderung
geschrieben: `update_text_list` und `delete_text_list` lehnen sie ab, und `update_text_list_cells`
**verlangt** `textListNames` (`validation_error` bei der Validierung) und lehnt eine Systemliste darin ab
(`validation_error` bei der Planung gegen das Inventar und erneut im Worker). Eine Zellenänderung verwendet daher immer die
gefilterte Überladung `ExportToXlsx(file, textListNames, languages)` mit ausschließlich Anwenderlistennamen, sodass die
Arbeitsmappe, die sie wieder importiert, keine Zeilen von Systemlisten enthält. Ihr `expectedWorkbookHash` stammt aus einem
`read_text_list_workbook` mit denselben `textListNames` (und `cultures`); der Hash eines ungefilterten Lesevorgangs
passt nie (`state_changed`). Der Server ergänzt nicht von sich aus jeden Anwenderlistennamen: Ein
ungefilterter Lesevorgang würde dann nie zum Hash einer Zellenänderung passen. **Was der Server nicht sehen kann:** Ein Dateiimport
(`import_text_lists`) kann Zeilen von Systemtextlisten enthalten; der Server interpretiert das
Layout nicht, kann also nicht erkennen, welche Zeilen zu einer Systemliste gehören, und der Import von TIA Portal entscheidet, was er
annimmt.

## Arbeitsmappenzellen

Der Server kennt und interpretiert das XLSX-Spaltenlayout von TIA Portal nicht. Er liest das Office-Open-XML-
Paket (ISO/IEC 29500) nur über dessen dokumentierte Teile: Paketbeziehungen, die Arbeitsmappe und
ihre Beziehungen, die Tabelle der gemeinsamen Zeichenfolgen (shared strings) und die Arbeitsblätter. Zellarten sind `string` (gemeinsamer,
Inline- oder formelfreier `str`-Text), `number`, `boolean`, `error`, `formula` (zwischengespeicherter Wert) und
`date`. Das Escape `_xHHHH_` wird dekodiert. Diagramm- und Dialogblätter werden mit einer Meldung übersprungen. Fehlende
Teile, unbekannte Zelltypen, eine doppelt aufgeführte Zellposition, Teile, die das Paket verlassen, DTDs, mehr als
256 MB entpackt (jeder gelesene **oder kopierte** Teil wird mit seiner deklarierten Größe angerechnet, und kein Teil darf
auf mehr entpacken, als er deklariert) oder mehr als 2.000.000 Zellen schlagen geschlossen fehl
(`worker_operation_failed`).

Eine Zellbearbeitung `{ sheet, cell, expected, value, kind? }` adressiert eine Zelle genau so, wie der Lesevorgang sie geliefert
hat (Blattnamen werden ohne Beachtung der Groß-/Kleinschreibung verglichen, wenn eindeutig; A1-Bezüge in Großbuchstaben). `expected` ist
der aktuelle Wert ("" für eine leere Zelle); `kind` ist `string` oder `number` (Standard: die aktuelle Art,
`string` für eine leere Zelle). Ein Wert darf nur Zeichen enthalten, die XML 1.0 trägt
(`XmlConvert.IsXmlChar`, Surrogatpaare über `IsXmlSurrogatePair`; U+FFFE, U+FFFF, ungepaarte
Surrogate und Steuerzeichen außer Tab, CR und LF werden abgelehnt); dieselbe Regel gilt für einen
Textlistenkommentar. Leere Zellen dürfen beschrieben werden, wodurch Werte und Zeilen hinzukommen (zum Beispiel eine Übersetzung
in einer neuen Kulturspalte oder ein neuer Textlisteneintrag, gemäß der Kopfzeile, die der Lesevorgang geliefert hat). Formel-,
Fehler-, Boolean- und Datumszellen werden abgelehnt. Dieselbe Zelle kann in einem Element nicht zweimal bearbeitet werden.

Ein Patch schreibt implizite Zeilen- und Zellpositionen genau so aus, wie der Reader sie ableitet (eine Zeile ohne
`r` folgt der vorherigen Zeile, eine Zelle ohne `r` der vorherigen Zelle), und lehnt Zeilen oder Zellen ab, die
nicht streng aufsteigend geordnet sind (`validation_error`; ISO/IEC 29500 verlangt die Reihenfolge, und der Platz einer neuen Zelle
wäre mehrdeutig). Eine vorhandene Zeile oder Zelle wird unter allen gesucht, bevor eine neue in Reihenfolge
eingefügt wird. Ein Patch schreibt nur die bearbeiteten Arbeitsblätter neu und die Tabelle der gemeinsamen Zeichenfolgen immer dann, wenn ein
Element angehängt wurde oder sich `count`/`uniqueCount` geändert hat (eine Zahl, die über eine Textzelle geschrieben wird, senkt `count`),
wobei ein identisches einfaches Element wiederverwendet wird; Inline-Zeichenfolgen werden geschrieben, wenn die Arbeitsmappe keine Tabelle der gemeinsamen Zeichenfolgen
hat; die `dimension` des Arbeitsblatts wird aktualisiert. Jeder andere Teil, einschließlich der benutzerdefinierten Dokumenteigenschaft,
die TIA Portal beim Import prüft (`FileContent`), behält seinen **Inhalt** unverändert; das Paket selbst wird
neu geschrieben und neu komprimiert, sodass sich die Container-Bytes unterscheiden.

### Round Trip einer Zellenänderung

1. Planung (auch mit `dryRun:true`): `plc_alarm_plan`, dann ein Probe-Lesevorgang derselben Arbeitsmappe
   (dieselben `plcName`, `blockPath`, `textListNames`, `cultures`, `exportOption`); ein abweichender
   `workbookHash` schlägt mit `state_changed` fehl; ein fehlendes Blatt, eine abgelehnte Zellart oder eine Zelle, deren aktueller
   Wert von `expected` abweicht, schlägt mit `validation_error` fehl. Der Effekt führt jede Bearbeitung als `changes[{
   field: "Sheet!Cell", current, requested }]` auf und löst den Info-Guard `plc_workbook_round_trip` aus.
2. Mutation (Worker), alles innerhalb eines `TiaPortal.ExclusiveAccess`: erneut exportieren, den Hash erneut prüfen
   (`state_changed`, nichts importiert), patchen (Ablehnungen: nichts importiert), die gepatchte
   Arbeitsmappe importieren, erneut exportieren. Jede bearbeitete Zelle muss sich zeigen (Zahlen nach Wert), und **keine andere Zelle darf sich
   geändert haben**: Die gesamten Arbeitsmappen vorher und nachher werden verglichen, und jede nicht angeforderte Änderung schlägt mit
   `postcondition_failed` fehl, wobei die Zellen in `result.unrequestedChanges` aufgeführt werden (höchstens 50,
   `Sheet!Cell: before -> after`). Das Ergebnis enthält `workbookHashBefore`, `workbookHashAfter` und
   jede Bearbeitung mit dem Wert, den der Re-Export zeigt.
3. Verifikation: Das Element liest seine Zellen erneut (`check: "cells"`) und meldet den neuen Arbeitsmappen-Hash als
   `contentHash`, den nächsten `expectedWorkbookHash`.

`ExclusiveAccess` signalisiert TIA Portal eine gesteuerte Aktivität und sperrt die Eingabe in der Oberfläche dieses TIA Portal,
solange sie dauert (ein zweites `ExclusiveAccess`, zum Beispiel eines anderen Tools, lässt das Element fehlschlagen, bevor
sich etwas geändert hat). Es wird keine Transaktion verwendet: Ob die XLSX-Importe innerhalb einer laufen dürfen, ist nicht
dokumentiert (Übersetzen und ProjectText-Importe dürfen es nicht). **Verbleibendes Fenster:** Zwischen der Planungsprobe
und der Mutation wird der Hash innerhalb des exklusiven Bereichs erneut geprüft, sodass eine Änderung in diesem Fenster
abgelehnt wird; für `ExclusiveAccess` ist nicht dokumentiert, dass es andere Openness-Clients, die an dasselbe TIA
Portal angebunden sind, oder einen Multiuser-Server daran hindert, das Projekt während des Round Trips zu ändern; eine solche Änderung würde
sich als nicht angeforderte Änderung zeigen und das Element fehlschlagen lassen, wobei das Projekt geändert ist.

Eine Zellenänderung darf nicht auf ein anderes Element desselben Aufrufs folgen, das das Projekt derselben PLC ändern kann
(jedes Element außer einem Export, derselben oder einer unbekannten PLC; projektweite Meldeklassen-Importe zählen als
unbekannt): Ihr `expectedWorkbookHash` kann diese Änderung nicht enthalten. Sie ist nach erneutem Lesen in einem eigenen Aufruf
auszuführen.

## Dateien

**Host-Prüfung (lexikalisch).** `filePath` (und `directoryPath`) wird von den gemeinsamen
`LocalFilePaths` (Contracts) geprüft, die auch `export_io_map` verwendet, ohne das Dateisystem zu berühren: ein
absoluter Pfad auf einem Laufwerk (`X:\...` oder `X:/...`) oder einer UNC-Freigabe (`\\server\share\...`, erlaubt seit 2026-11-01),
der eine Datei benennt (`.xlsx`; Meldeklassen `.DAT`: live auf V21 am 02.11.2026 ließ ein `.xml`-Name TIA Portal den modalen Fehler 0406:000014 „ungültige Dateiendung“ zeigen, und der Aufruf wartete darauf), höchstens 1.024 Zeichen; abgelehnt werden Geräte- oder NT-
Objektpfade mit beiden Trennzeichen (`\\?\`, `\\.\`, `\??\`, `/??/`), `.`/`..` und leere Segmente, ein
`:` nach dem Laufwerk (alternative Datenströme), ein reservierter Gerätename in einem beliebigen Segment (CON, PRN, AUX,
NUL, COM0-9, LPT0-9 und ihre Formen mit hochgestellten Ziffern, CONIN$, CONOUT$; mit oder ohne
Erweiterung), ein Segment, das auf ein Leerzeichen oder einen Punkt endet, Platzhalter sowie Steuer-, Format- (Bidi-) oder Trenn-
zeichen. **Verbundene Netzlaufwerke sind erlaubt** (Entscheidung des Eigentümers).

**Worker-Belege.** `plc_alarm_plan` meldet den normalisierten vollständigen Pfad, ob er und sein übergeordnetes Verzeichnis
existieren, den `DriveInfo.DriveType` der Wurzel und jedes vorhandene Vorgängerverzeichnis, das ein Link ist (Reparse
Point). Die Planung lehnt ab: ein fehlendes übergeordnetes Verzeichnis, ein Verzeichnis an einem Dateipfad, ein vorhandenes Exportziel
ohne `overwrite:true`, ein vorhandenes Ziel, das selbst ein Link ist (es wird nie ersetzt), eine
fehlende Importdatei (`target_not_found`) und ein vorhandenes `export_prodiag_info`-Verzeichnis. Ein Pfad auf einem
verbundenen Netzlaufwerk oder unterhalb eines Links ist **erlaubt** und löst den Info-Guard
`plc_file_on_network_or_link` mit dem normalisierten Pfad aus; der Worker wiederholt die Prüfung bei der Mutation und
fügt dem Element eine Warnung hinzu.

Ein Export wird von TIA Portal in eine temporäre Datei `.<name>.<guid>.tmp<extension>` im Zielverzeichnis
geschrieben; erst nachdem TIA Portal Erfolg meldet, wird sie an ihren Platz verschoben (`File.Move`, oder
`File.Replace` mit `overwrite:true`, die Link-Prüfung unmittelbar davor wiederholt), zurückgelesen und gehasht
(`bytes`, `sha256`). Eine vorhandene Datei, auch eine zwischenzeitlich angelegte, wird nur mit
`overwrite:true` ersetzt (Info-Guard `plc_export_overwrites_file`). Jeder Fehlschlag löscht die temporäre Datei.
Die Verifikation hasht die Datei erneut (`check: "file"`). Dateiinhalt wird nie geliefert; Pfade von Ergebnisprotokollen
werden genannt, nicht gelesen. `export_prodiag_info` arbeitet genauso mit einem temporären Geschwisterverzeichnis
`.<name>.<guid>.tmp` (es führt nie in ein vorhandenes Verzeichnis zusammen oder ersetzt es: Die exportierten Dateinamen
sind nicht dokumentiert) und wird verifiziert, indem jede aufgeführte Datei erneut gehasht wird (`check: "directory"`). Ein
Export, der zurückkehrt, ohne eine Datei zu schreiben, schlägt mit `postcondition_failed` fehl; sein leeres temporäres
Verzeichnis wird entfernt, und `directoryPath` wird nicht angelegt.

**Importe** übergeben TIA Portal nie die Datei des Aufrufers: Sie wird in das private
temporäre Arbeitsmappenverzeichnis dieses Servers kopiert (gleicher Dateiname), gehasht (`result.bytes`, `result.sha256` sind die der
Kopie), importiert und von dieser Kopie aus verglichen, sodass eine Änderung der Datei des Aufrufers während des Imports
keine Wirkung hat. Die Kopie wird mit dem Verzeichnis gelöscht.

**Reste.** Ein Worker, der abgestürzt ist oder in ein Timeout lief, kann eine temporäre Exportdatei, ein Exportverzeichnis
oder ein Arbeitsmappenverzeichnis zurücklassen. Der nächste Export in dieselbe Datei löscht die
`.<name>.<32 hex>.tmp<ext>`-Reste dieser Datei, und das nächste `export_prodiag_info` in dasselbe Verzeichnis dessen
`.<name>.<32 hex>.tmp`-Verzeichnisse (genau diese Muster, die GUID in hexadezimalen Kleinbuchstaben, wie dieser Server
sie schreibt, Name und Erweiterung ohne Beachtung der Groß-/Kleinschreibung), sobald sie mindestens zwei Stunden alt sind, mit einer
Warnung; jeder Melde-Lauf löscht die Verzeichnisse `%TEMP%\tia-mcp-alarm-<purpose>-<32 hex>` dieses Servers, die
älter als zwei Stunden sind. Links wird nie gefolgt.

## Importe und ihre Verifikation

Ein Import liest eine vorhandene lokale Datei (über eine private Kopie, [oben](#dateien)). Was er ersetzt,
hinzufügt oder beibehält, entscheidet TIA Portal (Info-Guard `plc_imports_alarm_data`). Unmittelbar danach, innerhalb
desselben `ExclusiveAccess`, exportiert der Worker dieselbe Art erneut, wo ein dokumentierter Export existiert, und
meldet `result.importCheck { method, outcome, verified, reason, workbookHashAfter, importedRows,
rowsNotShown, notShown[], importedSha256, reexportSha256 }`:

- `rows` (Textlisten, Meldetexte mit `exportOption`, Überwachungen): Jede Datenzeile der importierten
  Arbeitsmappe (unterhalb der ersten Zeile jedes Blatts, der Kopfzeile) muss im gleichnamigen Blatt des
  Re-Exports erscheinen, egal an welcher Stelle. Spalten werden über den **Kopfzeilentext** zugeordnet (die n-te gleiche Überschrift zur
  n-ten), nicht über die Position, sodass ein Re-Export mit Spalten in anderer Reihenfolge oder mit zusätzlichen Spalten (zum
  Beispiel weiteren Sprachen) dennoch verglichen wird; Werte werden exakt verglichen, Zahlen nach Wert, leere und fehlende
  Zellen gleich (nachgestellte leere Zellen spielen keine Rolle). Importierte Spalten, deren Überschrift dem Re-Export fehlt,
  Blätter, die ihm fehlen, und Werte in Spalten ohne Überschrift werden als nicht verglichen aufgeführt.
- `fileHash` (Meldeklassen): Der Re-Export muss byte-identisch mit der importierten Datei sein.
- `none` (Überwachungseinstellungen: kein dokumentierter Export der Einstellungen; Meldetexte ohne `exportOption`).

`outcome` und der Verifikationseintrag:

| `outcome` | Wann | Verifikationseintrag |
| --- | --- | --- |
| `verified` | jede verglichene Zeile zeigt sich und nichts blieb unverglichen, oder die Meldeklassen-Datei ist identisch | `importedRows` / `fileHash`, success true |
| `contradicted` | eine verglichene importierte Zeile fehlt im Re-Export (TIA Portal hat sie nicht wie importiert übernommen, zum Beispiel hat `ImportOptions.None` einen vorhandenen Eintrag beibehalten, oder es hat einen Wert normalisiert) | `notVerified`, success **false** (Wurzel-`success` false) |
| `inconclusive` | jede verglichene Zeile zeigt sich, aber etwas konnte nicht verglichen werden (ein Layoutunterschied), die Meldeklassen-Datei weicht ab (eine gleichwertige Datei kann anders geschrieben sein), oder der Verifikationsexport schlug fehl, meldete einen Nicht-Erfolgs- oder unlesbaren Zustand, schrieb keine Datei oder eine unlesbare Arbeitsmappe | `trusted`, success true, mit dem Grund |
| `notReadBack` | kein dokumentierter Export (`none`) | `trusted`, success true, Meldung `not read back: ...` |

In jedem Fall war das Element selbst erfolgreich, und **der Import hat das Projekt geändert**; jedes Ergebnis
außer `verified` fügt eine Warnung hinzu, die das sagt und den Grund nennt. Ein Siemens-Fehlschlag des Verifikations-
exports (eine behebbare `EngineeringException`) wird im Worker abgefangen und wird zu einem `inconclusive`-
Ergebnis, nie zu einem fehlgeschlagenen Import; ein verlorenes TIA Portal (eine `NonRecoverableException` oder eine Portal-Freigabe,
die während des Exports beobachtet wird) schlägt weiterhin als `portal_lost` fehl.

## ProDiag

`get_prodiag_fb` liest, was blocks.md über einen ProDiag-FB dokumentiert: `ProgrammingLanguage` (verglichen
mit dem Member-Namen `ProDiag` aus dem dokumentierten Beispiel, `isProDiag`), `FB.Supervisions.Count`
(`SupervisionComposition`; eine `Supervision` hat kein dokumentiertes Member, daher ist die Anzahl nur ein
Vorhandenseinsbeleg), `PlcBlock.IsConsistent`, ob `GetService<SupervisionProvider>()` einen
Provider liefert, und die Attribute `ProDiagVersion`, `InitialValueAcquisition`, `UseCentralTimeStamp`
über `GetAttribute` (unlesbare werden aufgeführt). Die Überwachungsoperationen melden dieselbe
Sprache und Anzahl, und ein FB ohne `SupervisionProvider` schlägt mit `unsupported_capability` fehl und nennt beide.

`AssignedProDiagFB` wird an den drei Objekten gelesen und gesetzt, die blocks.md nennt: einem Instanz-DB
(`blockPath`), einem UDT-Instanz-Member eines globalen DB (`blockPath` + `memberPath`, gefunden mit
`Interface.Members.Find`) und einer Variable (`tableName` + `name`, optional `folderPath` und `plcName`).
blocks.md beschränkt das Attribut auf UDT-Instanzen in globalen DBs und Variablentabellen; bei einem anderen Objekt
entscheidet TIA Portal (`readable:false` mit seiner Meldung, oder ein abgelehnter Schreibvorgang). Der Wert wird **über den FB-
Namen** gelesen: Liefert `GetAttribute` eine Zeichenfolge, ist das der Name; liefert es einen Baustein, wird dessen `Name` verwendet; null oder ""
bedeutet nicht gesetzt; jeder andere Werttyp oder ein behebbarer Siemens-Fehlschlag ist `readable:false` mit dem Grund.
`set_prodiag_assignment` schreibt den Namen des FB so, wie TIA Portal ihn schreibt, und liest ihn zurück: Ein Wert, der
vor dem Schreiben unlesbar ist, zählt als nicht gesetzt (mit einer Warnung); ein lesbarer abweichender Name schlägt mit
`postcondition_failed` fehl; ein unlesbares Zurücklesen lässt `assignedProDiagFbAfter` null mit einer Warnung,
und die Verifikation (`proDiagAssignment`, ein frisches `get_prodiag_assignment`) meldet `notVerified`
mit success false. Ein DB-Member-Pfad darf Arrays indizieren (`memberPath` `UdtArray[0].Valve`, an
`Interface.Members.Find` übergeben, wie blocks.md es für `ArrayUdtMember.ArrayUdtMember[0]` zeigt). Das Aufheben einer
Zuordnung ist nicht dokumentiert und wird nicht angeboten.

## Sprachen und mehrsprachige Texte

Kulturen werden in `LanguageSettings.Languages`, dem Katalog, aufgelöst, zugeordnet über
`Language.Culture.Name` (ohne Beachtung der Groß-/Kleinschreibung, genau eine), weil das dokumentierte gefilterte
`ExportToXlsx`-Beispiel seine Sprachen dort findet (`project.LanguageSettings.Languages.Find(...)`); eine
Kultur, die keine TIA-Portal-Sprache ist, schlägt mit `target_not_found` und der Liste der aktiven Sprachen fehl, und eine, die
im Projekt nicht aktiv ist, wird mit einer Warnung weitergereicht (was TIA Portal damit tut, ist seine
Entscheidung). `list_text_lists` meldet die aktiven Sprachen, die Bearbeitungs- und die Referenzsprache. Weggelassene `cultures`
bedeutet jede aktive Sprache: Für Meldetexte wird jede aktive `Language` übergeben, für einen Textlisten-Lesevorgang
oder -Export ohne `textListNames` wird die ungefilterte Überladung `ExportToXlsx(FileInfo)` ("all activated
project languages") verwendet (die `cultures` der Arbeitsmappe ist dann leer), und mit `textListNames` (immer
für `update_text_list_cells`) erhält die gefilterte Überladung jede aktive `Language`. Der gefilterte Textlistenexport nimmt Namen und Sprachen
zusammen, daher schränkt `cultures` eine Textlisten-Arbeitsmappe nur zusammen mit `textListNames` ein; der Server
erfindet keine Namensliste, und er übergibt die Namen so, wie TIA Portal sie schreibt. Ein Textlistenkommentar ist
`MultilingualText.Items`, ein `{ culture, text }` pro Element; `update_text_list` schreibt das Element von
`commentCulture` und lehnt eine Kultur ohne Element ab (die Sprache zuerst aktivieren).

## Bestätigungsdialoge von TIA Portal

Jeder Schreibvorgang läuft innerhalb eines Bestätigungsbereichs wie `plc_download`, `plc_station_upload` und
`library_write`: Bis die Belege des Elements gesammelt sind, wird jeder TIA-Portal-`Confirmation`-Dialog
durch die `portalConfirmations` des Elements statt durch den sitzungsweiten Handler beantwortet und in
`result.portalConfirmations[{ type, text, answer }]` (höchstens 100) mit `portalConfirmationsObserved`,
`portalConfirmationsComplete` und `portalConfirmationPolicy` festgehalten. `decline` (Standard) antwortet Nein; ein
abgelehnter Dialog lässt das Element fehlschlagen (`worker_operation_failed`, das Ergebnis bleibt erhalten), weil das Ablehnen
verändert haben kann, was der Aufruf getan hat. `accept` antwortet Ja (nie in read-only, wo die Sitzung nicht
zustimmen kann) und fügt eine Warnung hinzu. Ein Dialog, der erscheint, nachdem die Belege gesammelt wurden, behält die Antwort des Aufrufs
und wird als Warnung in der nächsten Antwort gemeldet. Arbeitsmappen-Lesevorgänge antworten immer Nein und schlagen fehl, wenn sie
das mussten.

## Ergebniszustände und Member-Bindung

Die Member der Ergebnisobjekte (`State`, `LogFilePath`, `ErrorCount`, `WarningCount`, `Messages`) werden
vor dem Aufruf am deklarierten Rückgabetyp des Aufrufs gebunden (`PlcAlarmResultReader.Bind`; für die
zur Laufzeit gebundenen Meldetext-Methoden an `MethodInfo.ReturnType`), wobei die am stärksten abgeleitete Deklaration eines Namens
gewinnt, sodass eine verdeckende Neudeklaration nie `AmbiguousMatchException` auslöst. Nur ein Zustands-Member mit dem Namen
exakt `Success` ist erfolgreich (die Quellen nennen `Success` für die Meldeklassen- und Überwachungsergebnisse);
`Warning` wird, mit einer Warnung, für die Meldeklassen akzeptiert, deren Quelle Success / Warning /
Error nennt. Jeder andere Name und ein unlesbarer Zustand lassen das Element mit `worker_operation_failed` fehlschlagen, mit dem
Zustand, der Fehleranzahl, dem Pfad der Protokolldatei und der ersten Meldung; ein Export schreibt dann nichts ins
Ziel, ein Import sagt, dass das Projekt teilweise geändert sein kann.

## Guards, Verifikation, Ablösung

| Guard | Schweregrad | Wann |
| --- | --- | --- |
| `plc_state_unverifiable` | info (TIA Portal entscheidet) | unvollständiges Textlisten-Inventar für ein Update/Löschen, oder eine Arbeitsmappe, die für eine Zellenänderung nicht exportiert werden kann |
| `plc_deletes_text_list` | info | `delete_text_list` |
| `plc_workbook_round_trip` | info | jedes `update_*_cells` |
| `plc_imports_alarm_data` | info | jeder Import |
| `plc_export_overwrites_file` | info | ein Export mit `overwrite:true` auf eine vorhandene Datei |
| `plc_file_on_network_or_link` | info | die Datei oder das Verzeichnis des Aufrufers liegt auf einem verbundenen Netzlaufwerk oder unterhalb eines Links |

Verifikationsprüfungen: `textList` (erneutes Auflisten nach Update/Löschen), `cells`, `file`, `directory`,
`proDiagAssignment` (`notVerified`, wenn unlesbar) und für Importe `importedRows`, `fileHash`,
`notVerified` (contradicted) oder `trusted` (inconclusive, nicht zurückgelesen). Ablösungsschlüssel: Ein
Textlisten-Element besitzt `<PLC>/alarm/textlists/<name>`; ein Textlisten-Element über die ganze Arbeitsmappe (Import, Zellen)
besitzt `<PLC>/alarm/textlists/#workbook` und bietet `<PLC>/alarm/textlists` an (es löst frühere Listen-
Elemente und Arbeitsmappen-Elemente ab); ein Listen-Element bietet seinen Arbeitsmappen-Schlüssel als späteres Element an; Meldetexte besitzen
`<PLC>/alarm/texts`, Überwachungen `<PLC>/alarm/supervision/<blockPath>`, eine Zuordnung
`<PLC>/alarm/prodiag/<target>`, Meldeklassen `project/alarmclasses`, ein Export nur `file/<path>`
(bereichsfrei und kanonisch: der vollständige Pfad, `/` als `\`, kein nachgestelltes Trennzeichen, verglichen
ohne Beachtung der Groß-/Kleinschreibung, sodass dieselbe Datei ein Objekt ist, egal welche PLC oder Schreibweise sie nennt). Eine
spätere Baustein- oder Typänderung an der PLC (`delete_block`, `delete_block_group`, `rename_block_group`,
`update_block_logic`, `update_type_content`, `rename_block`, `renumber_block`, `move_block`,
`copy_block`, `create_block`, `create_instance_db`, `generate_blocks_from_source`, `delete_software_unit`,
`rename_software_unit`, `delete_type_group`, `add_interface_member`, `update_interface_member`,
`delete_interface_member`, `update_db_member`) bietet `<PLC>/alarm/texts`, `<PLC>/alarm/supervision` und `<PLC>/alarm/prodiag` an:
Ein Baustein kann Texte von Meldungsinstanzen, Überwachungen oder eine Zuordnung besitzen, daher löst er vorsichtshalber
frühere Meldetext-, Überwachungs- und Zuordnungs-Elemente dieser PLC ab (Textlisten-Elemente bleiben). Ein späteres
`delete_tag`, `update_tag`, `move_tag`, `delete_tag_table`, `move_tag_table`, `delete_tag_table_group` oder
`rename_tag_table_group` bietet nur
`<PLC>/alarm/supervision` und `<PLC>/alarm/prodiag` an: Eine Variable kann AssignedProDiagFB tragen (blocks.md), sodass
ihr FB sie überwacht; für Texte von Meldungsinstanzen ist nicht dokumentiert, dass sie Variablen referenzieren (alarms.md nennt
keine), daher lösen Variablenänderungen keine Meldetext-Elemente ab. Update- und Löschziele werden
bei der Planung aufgelöst (`target_not_found`, `target_ambiguous`, Systemliste: `validation_error`); nach einem
früheren Import oder einer Zellenänderung desselben Aufrufs wird eine fehlende Liste zu einem abhängigen Element, das
vor seiner Mutation neu geplant wird.

## Fehlerkategorien

Validierungs- und Selektorfehler sind kanonische Wurzelfehler, bevor etwas läuft. Worker-Kategorien:
`target_not_found`, `target_ambiguous`, `target_kind_unsupported` (ein Baustein, der kein FB oder DB ist),
`unsupported_capability` (ein Provider oder Member, den das installierte TIA Portal nicht anbietet, ein Ergebnis,
dessen `State` sich nicht binden lässt, ein nicht beschreibbares Attribut; nichts geändert; auch die Meldeklassen in einer
lokalen Sitzung, bei der Planung abgelehnt, so wie das Worker-Gate sie ablehnt), `state_changed`,
`validation_error`, `postcondition_failed`, `worker_operation_failed` (auch ein abgelehntes
`ExclusiveAccess` und abgelehnte Bestätigungsdialoge); eine Siemens-`EngineeringTargetInvocationException`
während eines Schreibvorgangs ist `engineering_rejected` mit `engineeringError`, wie bei jedem PLC-Schreibvorgang. Ein fehlgeschlagenes Schreib-
element enthält das bis zum Fehlschlag gesammelte Ergebnis mit seinem Bestätigungsprotokoll.

## Herkunft jedes verwendeten Siemens-Members

Quellen (schreibgeschützte Referenzdaten, nie ausgeführt): **(A)** `totally-integrated-claude`
`skills/tia-plc-operations/references/alarms.md` (V21-API-Referenz); **(SV)** `.../supervision.md`;
**(BL)** `.../blocks.md` (Abschnitt 12, ProDiag-FB, und die API-Referenz); **(X)**
`skills/tia-import-export/references/plc-alarms-and-tags.md` (V21-Codebeispiele); **(L)**
`skills/tia-project-general/references/language-settings.md`; **(C)**
`skills/tia-csharp-common/SKILL.md` (exklusiver Zugriff, Transaktionen); **(SK)** Siemens
`tia-portal-ai-extensions/openness_development/skills/libraries-and-alarms/SKILL.md`; **(B)** die V21-
Typnamen-Baseline `api-baselines/v21/step7.json` (nur Typnamen und Assemblies).

Status: **kompiliert** (deklariert in den Step7-Stubs `Alarms.cs` oder `Blocks.cs`, festgeschrieben durch
`PlcAlarmReferenceStubTests` und die Probe `AlarmsReferenceSurface.cs`); **Laufzeit** (die Quelle nennt
das Member und seine Argumenttypen, aber nicht seinen Rückgabetyp: vor dem Aufruf über den exakten Namen und die Argumenttypen
durch `LibraryRuntimeBinding` gebunden, genau ein anwendbares öffentliches Member oder
`unsupported_capability`); **Laufzeit-Eigenschaft** (ohne Typ genannt: die am stärksten abgeleitete öffentliche
Eigenschaft genau dieses Namens, sonst `IEngineeringObject.GetAttribute`; unlesbare Member werden
gemeldet); **generisch** (dokumentierte `IEngineeringObject`/`IEngineeringServiceProvider`-API);
**vorhanden** (ein unveränderter vorhandener Stub).

| Siemens-Member | Quelle | Status |
| --- | --- | --- |
| `PlcSoftware.PlcAlarmTextlistGroup` → `PlcAlarmTextlistGroup` | SK (`plcSoftware.PlcAlarmTextlistGroup.PlcAlarmUserTextlists`), A, B | kompiliert (eine Zeile in `Software.cs`) |
| `PlcAlarmTextlistGroup.PlcAlarmUserTextlists` / `.PlcAlarmSystemTextlists` | A ("Navigator of ... composition"), SK, B | kompiliert |
| `PlcAlarmUserTextlistComposition`, `PlcAlarmSystemTextlistComposition` : `IEnumerable<T>` (`GetEnumerator`) | A, B | kompiliert |
| `PlcAlarmTextlist` (Basis von Anwender- und Systemlisten) | A ("Base class for user and system text lists"), B | kompiliert (nur Typ) |
| `PlcAlarmUserTextlist.Name` (string) | SK (`x.Name == "USER_1"`), A | kompiliert |
| `PlcAlarmUserTextlist.Delete()` | A | kompiliert |
| `PlcAlarmTextlist.ID`, `.ListRange`, `.Comment`; `Name` der Systemliste | A (Typen nicht angegeben) | Laufzeit-Eigenschaft |
| Enum-Member von `ListRange` | A nennt `PlcAlarmTextlistListRange` ohne Member | Laufzeittyp des aktuellen Werts; nach exaktem Namen geparst |
| `IEngineeringObject.GetAttributeInfos/SetAttribute("Name"/"ListRange")` | generisch; L (Attribut-API) | generisch, vorhanden (Umbenennung über `LibraryReflection.Rename`) |
| `MultilingualText.Items`, `MultilingualTextItem.Language/Text` | L | vorhanden |
| `ProjectBase.LanguageSettings.Languages/ActiveLanguages/EditingLanguage/ReferenceLanguage`, `Language.Culture` | L, X (`LanguageSettings.Languages.Find`; per Aufzählung zugeordnet) | vorhanden |
| `PlcSoftware.GetService<PlcAlarmTextListProvider>()` | X | kompiliert (Provider als `IEngineeringService` deklariert) |
| `PlcAlarmTextListProvider.ExportToXlsx(FileInfo)` → `TextListXlsxResult` | X, A | kompiliert |
| `PlcAlarmTextListProvider.ExportToXlsx(FileInfo, IEnumerable<string>, IEnumerable<Language>)` → `TextListXlsxResult` | X, A | kompiliert |
| `PlcAlarmTextListProvider.ImportFromXlsx(FileInfo, ImportOptions)` → `TextListXlsxResult` | X, A | kompiliert |
| `TextListXlsxResult.State`, `.LogFilePath` | A (Typen nicht angegeben) | Laufzeit-Eigenschaft, vor dem Aufruf am deklarierten Typ gebunden |
| `PlcSoftware.GetService<PlcAlarmTextProvider>()` | SK | kompiliert |
| `PlcAlarmTextProvider.ExportInstanceTextsToXlsx(FileInfo, IEnumerable<Language>, PlcAlarmTextXlsxExportOption)` | A (Rückgabetyp nicht angegeben) | Laufzeit (vor dem Aufruf gebunden; Ergebnis-Member an `ReturnType`) |
| `PlcAlarmTextProvider.ImportInstanceTextsFromXlsx(FileInfo, IEnumerable<Language>)` | A (Rückgabetyp nicht angegeben) | Laufzeit (wie oben) |
| `PlcAlarmTextXlsxExportOption` | A, B (keine Member) | kompiliert, Enum ohne Werte; zur Laufzeit nach Namen geparst |
| Ergebnis-`State`, `LogFilePath` der Meldetext-Methoden | A (Typ `PlcAlarmTextXlsxResult`) | Laufzeit-Eigenschaft |
| `Project.GetService<AlarmClassDataProvider>()` | X | kompiliert (an der `ProjectBase`-Wurzel aufgerufen; abgelehnt, sofern es kein `Project` ist) |
| `AlarmClassDataProvider.Export(FileInfo)`, `.Import(FileInfo)` → `AlarmClassExportImportResult` | X, A | kompiliert |
| `AlarmClassExportImportResult.State/ErrorCount/WarningCount/Messages`, Meldung `.Message/.State` | X, A (Typen nicht angegeben) | Laufzeit-Eigenschaft |
| `FB` über `IEngineeringServiceProvider.GetService<SupervisionProvider>()` | X (`proDiagBlock.GetService<SupervisionProvider>()`) | generisch, Provider kompiliert |
| `SupervisionProvider.ExportSupervisionsToXlsx(FileInfo)` → `SupervisionXlsxResult` | X, SV | kompiliert |
| `SupervisionProvider.ImportSupervisionsFromXlsx(FileInfo, ImportOptions)`, `.ImportSupervisionSettingsFromXlsx(FileInfo, ImportOptions)` → `SupervisionXlsxResult` | X, SV | kompiliert |
| `SupervisionXlsxResult.State`, `.LogFilePath` | SV (Typen nicht angegeben) | Laufzeit-Eigenschaft |
| `ImportOptions.None/Override` | X | vorhanden; zur Laufzeit nach Namen geparst |
| `FB.Supervisions` → `SupervisionComposition` (`Count`, `IEnumerable<Supervision>`, `Parent`) | BL (`fb.Supervisions.Count > 0`; API-Referenz), B | kompiliert (`Blocks.cs`) |
| `Supervision` | BL, B (keine Member) | kompiliert (nur Typ, nie gelesen) |
| `PlcBlock.ProgrammingLanguage` verglichen mit dem Member-Namen `ProDiag` | BL (`fb.ProgrammingLanguage == ProgrammingLanguage.ProDiag`, `CreateFB(..., ProgrammingLanguage.ProDiag)`) | vorhandene Eigenschaft; Member nach Namen verglichen |
| `PlcBlock.IsConsistent` (bool) | BL | Laufzeit-Eigenschaft (nur als Beleg) |
| ProDiag-FB-Attribute `ProDiagVersion`, `InitialValueAcquisition`, `UseCentralTimeStamp` | BL (Tabelle GetAttribute/GetAttributes) | generisches `GetAttribute` |
| `AssignedProDiagFB` an Instanz-DB, DB-Member, Variable (`GetAttribute`/`SetAttribute` mit dem FB-Namen) | BL (Abschnitte "Assign ProDiag-FB to IDB", "AssignedProDiagFB on DB member and Tag") | generisch |
| `DataBlock.Interface.Members.Find(string)` | BL (Beispiel) | vorhanden |
| `PlcTagTableGroup.Groups/TagTables.Find`, `PlcTagComposition.Find` | vorhanden (Variablenoperationen) | vorhanden |
| `CodeBlock.ExportProDIAGInfo(DirectoryInfo)` | BL (Rückgabetyp nicht angegeben; das Beispiel ignoriert ihn) | Laufzeit (vor dem Aufruf gebunden, Ergebnis ignoriert) |
| `TiaPortal.ExclusiveAccess(string)` | C | vorhanden |
| `PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)` | A | Laufzeit (Bibliotheksbindung; nicht deklariert) |
| `MasterCopyComposition.Create(IMasterCopySource)` mit einer Anwendertextliste | SK (`MasterCopies.Create(alarmTextList)`) | vorhanden; die Liste wird zur Laufzeit als `IMasterCopySource` geprüft |

Jeder deklarierte Typ ist in der Step7-Baseline (B) und nur in der Step7-Referenz. Die Stub-Enums
deklarieren kein Member. Nach jedem Merge, der `reference-stubs/` berührt, `ref/*.dll` neu erzeugen mit
`pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -Update`.

### Weggelassen, und warum

| Operation | Grund |
| --- | --- |
| Eine Anwendertextliste über den Namen anlegen | Kein dokumentiertes Create-Member: `PlcAlarmUserTextlistComposition` dokumentiert nur `CreateFrom(MasterCopy[, MasterCopyMode])`, angeboten über `library_write`. Ein XLSX-Import kann Listen anlegen; das ist das Importverhalten von TIA Portal, kein dokumentierter Vertrag. |
| Typisierte Textlisteneinträge (Wertebereiche und Texte als Objekte): lesen, hinzufügen, ändern, löschen | Kein Eintragstyp und keine Eintrags-Composition sind dokumentiert. Einträge werden als Zellen des eigenen XLSX-Exports/-Imports von TIA Portal gelesen und geändert, ohne dass der Server das Layout interpretiert. |
| Textlisteneinträge löschen | Für das Entfernen von Zeilen mit anschließendem Import ist nicht dokumentiert, dass es Einträge löscht (die Importsemantik ist die von TIA Portal), und das Entfernen von Zeilen würde jeden folgenden Bezug neu schreiben; nicht angeboten. Texte können mit einer Zellbearbeitung geleert werden; eine ganze Liste kann gelöscht werden. |
| Systemtextlisten ändern | Nach Regel des Eigentümers schreibgeschützt; abgelehnt, bevor etwas läuft (siehe [Systemtextlisten](#systemtextlisten) dazu, was ein Dateiimport dennoch enthalten kann). |
| Einzelne Meldeklassen auflisten, lesen, anlegen, ändern oder löschen | V21 Step7 dokumentiert kein Objektmodell für Meldeklassen, nur `AlarmClassDataProvider.Export/Import` der gemeinsamen Meldeklassen; das exportierte Format ist nicht dokumentiert, daher liest und patcht der Server es nicht. |
| Programmmeldungen (Program_Alarm-Instanzen) als Objekte | In der V21-Baseline ist kein Programmmeldungstyp: `Siemens.Engineering.SW.Alarm` enthält nur Provider, Ergebnisse, Ausnahmen und Textlisten. Texte von Meldungsinstanzen sind nur über den XLSX-Export erreichbar (`read_alarm_text_workbook`). |
| Überwachungen als Objekte (Elemente von `FB.Supervisions`) | `Siemens.Engineering.SW.Blocks.Supervision` und `SupervisionComposition` sind in der Baseline und in blocks.md, aber `Supervision` hat kein dokumentiertes Member; nur `Count` wird gelesen (`get_prodiag_fb`). Überwachungen werden über die Überwachungs-Arbeitsmappe gelesen und geändert. |
| Die ProDiag-FB-Attribute schreiben (`ProDiagVersion`, `InitialValueAcquisition`, `UseCentralTimeStamp`) | blocks.md führt sie nur für GetAttribute/GetAttributes auf und vermerkt, dass `SetAttributes()` nicht mehr als ein Attribut eines ProDiag-FB setzen kann; kein Schreiben ist dokumentiert. Sie werden gelesen. |
| Eine `AssignedProDiagFB`-Zuordnung aufheben | Nur `SetAttribute(..., prodiag.Name)` mit einem FB-Namen ist dokumentiert; was ein leerer Wert bewirkt, nicht. |
| Einen ProDiag-FB anlegen | Dokumentiert (`PlcBlockComposition.CreateFB(name, isAutoNumber, number, ProgrammingLanguage.ProDiag)`), aber das Anlegen von Bausteinen gehört zur Bausteindomäne: `create_block` legt Bausteine über den Import generierter Quellen an (LAD, FBD, STL, SCL, GRAPH), und `CreateFB` ist dort nicht deklariert; das Hinzufügen gehört in diese Domäne und deren Review, nicht in diesen Branch. Der Instanz-DB eines ProDiag-FB wird mit dem vorhandenen `create_instance_db` angelegt (`CreateInstanceDB`, wie es das dokumentierte Beispiel tut). |
| `export_prodiag_info` in ein vorhandenes Verzeichnis | Die exportierten Dateinamen sind nicht dokumentiert, daher könnte ein Zusammenführen in ein Verzeichnis oder dessen Ersetzen Dateien überschreiben oder vermischen; ein neues Verzeichnis ist erforderlich. |
| DAT-Export/-Import der Überwachungseinstellungen (`SupervisionSettingsProvider.Export/Import`) | Die Quellen dokumentieren die Member, aber nicht, woher der Provider bezogen wird (kein `GetService`-Besitzer); nicht geraten. Der XLSX-Import der Einstellungen wird angeboten (nicht zurückgelesen). |
| Textlisten und Meldetexte von Software Units | Die Quellen sagen, das übergeordnete Objekt der Provider "can be Plc or Unit", dokumentieren aber keinen Einstiegspunkt für Units; die PLC-Selektoren von `plc_read`/`plc_write` adressieren PLCs. |
| Behandlung von `TextListNotFoundException` | Ihr Basistyp ist nicht dokumentiert; sie läuft über die gemeinsame Ausnahmeklassifizierung. |
| Projektsprachen ändern (aktivieren, Bearbeitungs-/Referenzsprache) | Projekteinstellungen, keine PLC-Meldedaten; in `list_text_lists` schreibgeschützt gemeldet. |
| Eine Transaktion um Importe und Zellenänderungen | Nicht als mit den XLSX-Importen verträglich dokumentiert (für Übersetzen und ProjectText-Importe ist dokumentiert, dass sie in einer nicht erlaubt sind); nur `ExclusiveAccess` wird verwendet. |
| Melden, ob zwei aufeinanderfolgende Exporte denselben Hash ergeben (volatile Zellen) | Es würde bei jedem Lesevorgang einen zweiten Export kosten; den Live-Abnahmeprüfungen überlassen. Eine volatile Zelle würde jede Zellenänderung mit `state_changed` oder `postcondition_failed` fehlschlagen lassen, nie stillschweigend anwenden. |
| Online-Quittierung von Meldungen | Existiert über die S7-1500 Web API (`plc_online_write acknowledge_alarm`). |
| HMI-Meldungen und HMI-Textlisten | Meldungen von WinCC Unified sind `hmi_read`/`hmi_write`; HMI-Textlisten sind eine eigene HMI-Domäne. |

## Bekannte Grenzen und offene Risiken

- Nichts ist live getestet. Das XLSX-Layout von TIA Portal, seine Toleranz gegenüber einem neu geschriebenen Arbeitsblatt und
  einer neu geschriebenen Tabelle der gemeinsamen Zeichenfolgen und ob `Override` Einträge ändert, hinzufügt oder beibehält, sind unverifiziert; die
  Re-Export-Prüfungen erkennen eine stillschweigend ignorierte Bearbeitung und jede nicht angeforderte Änderung, nicht aber eine semantische Fehldeutung
  einer Spalte.
- Die Member-Namen von `TextListXlsxResultState`/`PlcAlarmTextXlsxResultState` sind undokumentiert: Heißt das
  installierte Erfolgs-Member nicht `Success`, schlägt jeder Export und Import geschlossen fehl.
- `PlcAlarmTextXlsxExportOption` und die Enum-Member von `ListRange` sind undokumentiert: Der Aufrufer erfährt
  sie aus der Fehlermeldung oder aus `list_text_lists`.
- Der Zeilenvergleich eines Imports kann durch einen legitimen Import widerlegt werden (TIA Portal normalisiert einen
  Wert, `ImportOptions.None` behält einen Eintrag bei), sodass ein korrekter Import `notVerified` und Wurzel-
  `success:false` melden kann; Layoutunterschiede machen ihn nur inconclusive (`trusted` mit einer Warnung). Als
  Kopfzeile wird jeweils die erste Zeile jedes Blatts angenommen; ein Re-Export mit einer anderen ersten Zeile ist
  inconclusive, nie contradicted, es sei denn, die Werte einer zugeordneten Spalte weichen ab.
- `ExclusiveAccess` um Importe und Zellenänderungen ist nicht live getestet; ein TIA Portal, das es verweigert,
  lässt jede Zellenänderung und jeden Import fehlschlagen, bevor sich etwas geändert hat.
- Eine große Arbeitsmappe lässt Planung und Verifikation sie jeweils ein weiteres Mal exportieren (Probe-Lesevorgänge), und eine Zellen-
  änderung exportiert sie zweimal und ein Import einmal mehr innerhalb des exklusiven Bereichs.
- Merge-Hinweis: `Software.cs` erhielt eine `PlcSoftware`-Eigenschaft und `Blocks.cs` `FB.Supervisions` mit
  `Supervision`/`SupervisionComposition`; `ref/*.dll` wurden neu erzeugt, und ein Merge mit einer anderen Stub-
  Änderung erfordert die Neuerzeugung erneut (erledigt, als der External-Source-Branch diesen hier gemergt hat).
  `PlcAlarmVerification.AlarmChangingBlockOperations` nennt `generate_blocks_from_source`,
  `delete_software_unit`, `rename_software_unit` und `delete_type_group` jenes Branches, und
  `AssignmentChangingTagOperations` dessen `delete_tag_table_group` und `rename_tag_table_group`; sie
  lösen Melde-Elemente ab wie die Baustein-, Typ- und Variablentabellenänderungen, denen sie ähneln.

## Live-Abnahmeprüfungen

An einem Wegwerf-Standalone-Projekt mit einer S7-1500-PLC, zwei aktiven Sprachen (en-US, de-DE),
mindestens einer Anwender- und einer Systemtextliste, einem FB mit einem Program_Alarm, einem ProDiag-FB mit einem Instanz-DB,
einem globalen DB mit einer UDT-Instanz und einer Variablentabelle mit einer UDT-Variable:

1. `list_text_lists`: Anwender- und Systemlisten mit ID, Listenbereich und beiden Kommentarkulturen; Projektsprachen.
2. `read_text_list_workbook` mit und ohne `textListNames` + `cultures`: Blätter und Kopfzeile so, wie TIA Portal sie exportiert; das Layout auf dieser Seite festhalten.
3. **Volatile Zellen:** jede Arbeitsmappenart zweimal ohne Änderungen lesen; die beiden `workbookHash`-Werte müssen gleich sein (sonst schlägt jede Zellenänderung fehl).
4. `update_text_list` Umbenennung + `listRange` + `comment[de-DE]`; Verifikation `textList` true; die Oberfläche zeigt die Änderung. Eine Umbenennung auf einen vorhandenen Anwender- oder Systemnamen wird bei der Planung abgelehnt.
5. `update_text_list` an einer Systemliste und `update_text_list_cells` mit einer Systemliste in `textListNames`: abgelehnt, nichts geändert.
6. `update_text_list_cells`: einen Eintragstext ändern, einen de-DE-Text in einer leeren Zelle hinzufügen, eine neue Eintragszeile hinzufügen; der Re-Export zeigt alles, und `unrequestedChanges` ist leer; die Oberfläche zeigt sie; der Hash des nächsten Lesevorgangs gleicht dem gemeldeten `contentHash`. Den `ExclusiveAccess`-Dialog während des Round Trips beobachten.
7. Derselbe Aufruf mit dem alten Hash: `state_changed`, nichts importiert.
8. `read_alarm_text_workbook` mit jeder installierten `exportOption`; die Member-Namen festhalten; `update_alarm_text_cells` an einem Meldetext.
9. Blättern: eine Arbeitsmappe mit mehr Zeilen als `maxElements` und als das Budget: `nextStartRow` blättert mit `sheet` + `startRow` bis zum Ende; kein Ergebnis wird ausgelassen. Drei Arbeitsmappen-Lesevorgänge in einem `plc_read`-Aufruf: keiner wird ausgelassen. Eine Zeile, die breiter als das Budget ist (viele Sprachen): `cellsTruncated`/`nextStartColumn` blättern sie mit `startColumn`.
10. `export_text_lists`/`export_alarm_texts`/`export_supervisions`/`export_alarm_classes` in einen lokalen Ordner; ein zweiter Export ohne `overwrite` abgelehnt; mit `overwrite:true` ersetzt; keine temporäre Datei bleibt zurück. Auf einem verbundenen Netzlaufwerk wiederholen: erlaubt, `plc_file_on_network_or_link` wird ausgelöst.
11. `import_text_lists` einer exportierten und einer in Excel bearbeiteten Arbeitsmappe (benutzerdefinierte Eigenschaft erhalten): `importCheck.outcome` `verified`; festhalten, ob die erste Zeile des Re-Exports die Kopfzeile ist, die der Vergleich annimmt, und ob TIA Portal einen Wert normalisiert (was einen korrekten Import `contradicted` machen würde); `import_alarm_classes` der exportierten Datei: `verified` oder `inconclusive` (festhalten, welches); `import_alarm_texts` mit und ohne `exportOption` (`notReadBack`).
12. Ein `\\?\`- und ein `\??\`-Pfad, `C:\NUL\x.xlsx`: abgelehnt vor jedem Worker-Aufruf; ein UNC-Freigabepfad: angenommen.
13. `read_supervision_workbook`/`update_supervision_cells`/`import_supervision_settings` am ProDiag-FB (`notReadBack`, `trusted` mit einer Warnung); ein einfacher FB ergibt `unsupported_capability` mit seiner Sprache und Überwachungsanzahl.
14. Ein Dialog während eines Imports (zum Beispiel ein Import, der fragt, ob überschrieben werden soll): `decline` lässt das Element mit festgehaltenem Dialog fehlschlagen; `accept` antwortet Ja mit einer Warnung.
15. `get_prodiag_fb` am ProDiag-FB und an einem einfachen FB; `get_prodiag_assignment`/`set_prodiag_assignment` am Instanz-DB, am UDT-Member, an einem UDT-Member innerhalb eines Arrays eines globalen DB (`memberPath` wie `Find("ArrayUdtMember.ArrayUdtMember[0]")` in blocks.md) und an der UDT-Variable; festhalten, ob `GetAttribute("AssignedProDiagFB")` eine Zeichenfolge oder einen Baustein liefert und was es liefert, wenn nicht gesetzt; `export_prodiag_info` in ein neues Verzeichnis (die Dateinamen festhalten) und an einem FB, der nichts exportiert (`postcondition_failed`, kein Verzeichnis).
16. Ein späteres `delete_block` des FB im selben Aufruf wie `update_alarm_text_cells`: Die Verifikation des Zellen-Elements ist `superseded`.
17. `library_write create_master_copy` mit der Quelle `textList` und `instantiate_master_copy` in `textLists` mit jedem `masterCopyMode`.
18. Eine lokale Sitzung: Textlistenoperationen funktionieren; Meldeklassen werden bei der Planung abgelehnt (`unsupported_capability`, auch mit `dryRun:true`).
