# Von rickgaiser/TiaMcp berichtete Fallstricke in TIA Portal V21

**Sprache:** [English](V21_PITFALLS.md) · Deutsch

Roadmap-Zeile 22 der [Roadmap zur Übernahme von Funktionen](../roadmap/competitor-feature-adoption.md)
(Zeile 21, die Portal-Auflistung, steht in der [Projektreferenz](PROJECT_OPERATIONS_SUMMARY.de.md);
ihr Live-Fall ist L11 unten). Jedes Verhalten unten ist **von rickgaiser/TiaMcp berichtet und hier
nicht auf V21 verifiziert**: Keine Siemens-Quelle und kein Live-Befund dieses Repositorys bestätigt
eines davon (siehe [Nachweise](#nachweise)). Die Guard- und Warnungstexte sagen deshalb "is reported
to", bis der passende Fall der [Liste der Live-Abnahme](#liste-der-live-abnahme) auf V21 reproduziert
und festgehalten wurde. Nicht live getestet.

Ein V21-Fallstrick der SIMATIC-SD-Route wurde von diesem Repository **live gesehen** (2026-11-02): Ein
SD-Import des Main-Safety-Bausteins einer F-Ablaufgruppe entfernt die Referenz der Gruppe; siehe
[Fallstrick 6](#fallstrick-6-sd-import-eines-main-safety-bausteins-live-2026-11-02) mit dem
Workaround, den der Server anwendet.

Weitere von den BetterTIA-Tools berichtete Fallstricke sind unter
[von BetterTIA berichtete Fallstricke](#von-bettertia-berichtete-fallstricke-nicht-verifiziert) (von
BetterTIA berichtet, nicht verifiziert) aufgeführt; sie sind nur dokumentiert, für sie wurde kein Guard
hinzugefügt.

Die Erkennungslogik für SIMATIC SD ist von rickgaiser/TiaMcp (AGPL-3.0) gemäß
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.de.md) übernommen; die Guards, das Export-Ledger,
die Lese-Flags und der Verifikationsvergleich stammen von diesem Fork.

## Übersicht

| # | Berichteter Fallstrick (auf V21 nicht verifiziert) | Wo dieser Server darauf stoßen kann | Was dieser Server tut |
| --- | --- | --- | --- |
| 1 | Der Import über SIMATIC SD (`.s7dcl`) verändert Berichten zufolge ohne Fehler (a) einen Array-Zugriff, der in einem Index in einem KOP/FUP-Netzwerk verschachtelt ist, (b) ein Member, das mit einem Projekt-FB/-UDT typisiert ist, der wie ein Systemtyp heißt (TON, TOF, TP, TONR, CTU, CTD, CTUD, R_TRIG, F_TRIG), (c) AT-Überlagerungen | `plc_write update_block_logic` mit einem Bundle, das kein `.xml`-Dokument hat (der Worker ruft dann `PlcBlockComposition.ImportFromDocuments` auf); `vci_write sync_to_project` von `SimaticSD`-Workspace-Dateien | Info-Guard `plc_s7dcl_import_may_alter`, eine Item-Warnung, ein Verifikationseintrag `s7dclConstructs`, der fehlschlägt, wenn der Re-Export abweicht; `get_block_content` (xml) warnt bei solchen Bausteinen. VCI wird nicht geprüft (siehe [Weggelassen](#weggelassen)) |
| 2 | TIA Portal beendet sich Berichten zufolge, wenn es einen Baustein mit textueller Schnittstelle (`S7_EditorMode := "[SCL,TextualInterface]"`) überschreibt, der seit dem Öffnen des Projekts oder dem letzten Schreiben des Bausteins mehr als einmal exportiert wurde | jedes Überschreiben eines vorhandenen Bausteins: `update_block_logic` (beide Formate) und die Bearbeitungen von Schnittstellen-Membern | Export-Ledger des Workers pro Bindung, Info-Guards `plc_textual_interface_overwrite_may_terminate` und `plc_save_before_write_recommended`, eine Item-Warnung; `get_block_content` (xml) warnt |
| 3 | Das Exportieren einer (klassischen) Grafikliste und das Lesen von `HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText` bringen TIA Portal V21 Berichten zufolge zum Absturz | `hmi_write export_classic_objects` mit `graphicLists` (Issue #28) | abgelehnt, bevor `Export` gebunden wird (Katalog und Worker, "skipped: reported to crash TIA Portal V21"), durch einen Quelltexttest festgeschrieben; siehe [Fallstrick 3](#fallstrick-3-grafiklisten-und-scriptdiagnosisoverviewtext) |
| 4 | Ein Import eines klassischen WinCC-Bildes mit doppelter `<Number>` beendet TIA Portal Berichten zufolge | `hmi_write import_classic_objects` mit `screens` (Issue #28) | der Worker liest die Bildnummern der Datei und jedes vorhandenen Bildes und lehnt eine wiederholte oder belegte Nummer vor `Import` ab (`validation_error`); siehe [Fallstrick 4](#fallstrick-4-klassisches-bild-mit-doppelter-number) |
| 5 | Ein geöffneter HMI-Variableneditor verlangsamt Openness-Aufrufe auf diesem HMI Berichten zufolge etwa um das Achtfache | jedes `hmi_read`/`hmi_write` auf diesem HMI | dokumentiert in der [HMI-Referenz](HMI_OPERATIONS_SUMMARY.md#v21-pitfalls) und im [Leitfaden zur Fehlerbehebung](../guides/troubleshooting.md#tia-portal-v21-pitfalls) |
| 6 | **Live gesehen (V21, 2026-11-02), kein Bericht:** Ein SD-Import (`ImportFromDocuments` Override) des Main-Safety-Bausteins einer F-Ablaufgruppe entfernt den `MainSafetyBlockName` der Gruppe; das Übersetzen der PLC schlägt dann fehl, und Openness bietet kein Member, um ihn neu zuzuweisen | jeder Schreibzugriff mit SD-Dokument auf einen Main-Safety-Baustein: `update_block_logic` sd oder ein Bundle ohne `.xml`, `insert_network`/`replace_network`/`delete_network`, `create_block_from_document` mit `overwrite` | nie auf den Baustein importiert: die Route über einen temporären F-Baustein ([Fallstrick 6](#fallstrick-6-sd-import-eines-main-safety-bausteins-live-2026-11-02)), Info-Guard `plc_import_may_unlink_main_safety_block`, Verifikation `safetyRuntimeGroup` auf jeder Route |

## Nachweise

Schreibgeschützter Klon von rickgaiser/TiaMcp bei `8e9de1c` (2026-10-30), als Daten behandelt; nichts
davon wurde gebaut oder ausgeführt. Zeilennummern beziehen sich auf diesen Commit.

| # | Quelle (alle rickgaiser/TiaMcp, AGPL-3.0) | Siemens-Quelle oder Live-Befund dieses Repositorys |
| --- | --- | --- |
| 1 | `README.md` "Known limitations" (Zeilen 260–263); `src/TiaMcp.Formats/S7dclSource.cs` (Kommentare von `WithAtMarkers` und der Systemtypliste, `FindWriteProblems`) | keine. Der Siemens-Skill `simatic-sd` (`tia-portal-ai-extensions`, `openness_development/skills/simatic-sd`) dokumentiert das Format, `ExportAsDocuments`/`ImportFromDocuments` und seine eigenen Einschränkungen, aber keine dieser drei |
| 2 | `README.md` "Known limitations" (Zeile 264); `src/TiaMcp.Openness/TiaConnection.cs` (Kommentar von `ExportsSinceImport`, Zeilen 679–682: Der Absturz liegt in `DeclInstanceProvider.RemoveAndRelease`, Warten oder Übersetzen hilft nicht, erneutes Öffnen schon); `src/TiaMcp.Mcp/TiaTools.cs` (`ExportedTooOften`) | keine. `TextualInterface` als Baustein-Fingerabdruck ist in `totally-integrated-claude` `skills/tia-plc-operations/references/blocks.md` aufgeführt (nicht Siemens; sagt nichts über Überschreiben) |
| 3 | `README.md` Zeile 167 (`ScriptDiagnosisOverviewText`), Zeilen 244 und 266 (Grafiklisten); `src/TiaMcp.Openness/HmiScreenConverter.cs` (`CrashingProperties`: Der Getter kehrt zurück, TIA Portal beendet sich kurz danach); `src/TiaMcp.Openness/TiaConnection.HmiClassic.cs` (`ReadHmiClassicObjects`: `InvalidCastException` String → `HmiOpenLink` in `UpdateOpenLinks`); `STATUS.md` Zeile 64 | keine |
| 4 | `README.md` Zeile 244; `src/TiaMcp.Openness/TiaConnection.HmiClassic.cs` (die Prüfung der Bildnummer vor dem Import); `STATUS.md` Zeile 59 | keine |
| 5 | `README.md` Zeile 240 | keine |

Verwandt, aber anders: Die eigene [VCI-Live-Abnahme](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md)
dieses Repositorys hielt fest, dass ein VCI-**Export** `SimaticSD` eines SCL-OB mit `portal_lost`
endete; das ist ein Absturz beim Export, keine der Veränderungen beim Import oben.

## Warum die Guards informieren und nie blockieren

`AGENTS.md` behält Block-Guards von `plc_write` für einen Zustand, den der Planer nicht verifizieren
kann (`plc_state_unverifiable`), und für den doppelten Singleton-OB (`plc_ob_singleton_exists`); alles
andere informiert, und der Zugriffsmodus ist die Kontrolle des Menschen. Diese Fallstricke sind nicht
verifizierte Berichte. Ein Block-Guard würde die SIMATIC-SD-Route und (Fallstrick 2) jedes
Überschreiben eines Bausteins mit textueller Schnittstelle über `plc_write` unbeschreibbar machen, aus
einem Grund, den niemand auf V21 reproduziert hat. Die Guards werden in der Antwort eines Probelaufs
und eines tatsächlichen Aufrufs aufgeführt, sodass ein Aufrufer sie sieht, bevor er entscheidet;
nichts fragt den Benutzer (`plc_write` hat `ConfirmsEveryCall=false`).

## Fallstrick 1: SIMATIC-SD-Import

**Schreibpfad.** `update_block_logic` mit `format:"xml"` importiert über SimaticML
(`PlcBlockComposition.Import(FileInfo, ImportOptions.Override)`), sobald das übergebene Bundle ein
abgegrenztes `.xml`-Dokument hat; die begleitenden `.s7dcl`/`.s7res` sind dann nur Kontext und müssen
unverändert sein. Nur ein abgegrenztes Bundle **ohne** `.xml`-Dokument und mit einem `.s7dcl`-Dokument
nimmt die SIMATIC-SD-Route (`ImportFromDocuments(..., ImportDocumentOptions.Override)`), zum Beispiel
nachdem `get_block_content` nur das Dokumentpaket exportieren konnte. `update_type_content` importiert
PLC-Datentypen nur als SimaticML oder externe Quelle, sodass für UDTs kein `.s7dcl`-Schreibpfad
existiert.

**Erkennung** (Siemens-frei, `TiaMcpServer.Contracts/Plc/Interface/S7dclImportHazards.cs`, übernommen):

- eine Deklaration, deren Typ `_.NAME`, `_."NAME"` oder `"NAME"` für einen der neun Systemtypnamen ist
  (auch als `Array[...] of`), ohne Beachtung der Groß-/Kleinschreibung;
- in jedem Netzwerk, dessen Attributliste `S7_Language := "LAD"` oder `"FBD"` angibt, ein `[`
  innerhalb eines anderen `[` außerhalb von Namen in Anführungszeichen und Zeichenketten;
- eine AT-Überlagerung des Bausteins **in seinem aktuellen Zustand**
  (`Member/AttributeList/StringAttribute[@Name="At"]` im SimaticML des Planungs-Lesevorgangs), deren
  Member im Deklarationsteil des übergebenen `.s7dcl` noch deklariert ist. `.s7dcl` hat keine
  AT-Notation, sodass die Überlagerung im übergebenen Text unsichtbar ist; ein Baustein, dessen
  Planungs-Lesevorgang kein lesbares SimaticML hat, kann nicht auf Überlagerungen geprüft werden, was
  der Guard angibt.

**Guard und Warnung.** `plc_s7dcl_import_may_alter` (info) wird ausgelöst, wenn die SIMATIC-SD-Route
genommen wird und ein Konstrukt gefunden wurde oder die Überlagerungen nicht geprüft werden konnten.
Seine Meldung listet jeden Befund und empfiehlt die SimaticML-Route (`get_block_content` mit
`format:"xml"` lesen, das `.xml`-Dokument bearbeiten, das ganze Bundle übergeben). Das Item-Ergebnis
trägt eine Warnung mit derselben Empfehlung.

**Verifikation.** Der Hash des Re-Exports kann eine stille Veränderung nicht zeigen. Für ein Item mit
Befunden vergleicht das Zurücklesen des Inhalts jeden Befund mit dem re-exportierten Bundle: Das Member
muss noch mit derselben Schreibweise des Projekttyps deklariert sein, das KOP/FUP-Netzwerk mit
derselben Nummer muss noch einen verschachtelten Index enthalten, und die Überlagerung muss im
re-exportierten SimaticML noch ihr AT-Attribut tragen. Der Verifikationseintrag ist
`check:"s7dclConstructs"` mit `expected` (den Befunden) und `actual` (`preserved`, `altered` mit dem,
was der Re-Export zeigt, oder `unverifiable`, wenn dem Re-Export das Dokument fehlt). Alles außer
`preserved` lässt den Eintrag fehlschlagen (nicht verifiziert), sodass `success` des Aufrufs false ist;
der neue Inhalts-Hash wird trotzdem gemeldet.

**Leseseite.** `plc_read get_block_content` mit `format:"xml"` fügt eine Item-Warnung hinzu, wenn das
`.s7dcl` des Bundles eines der Konstrukte enthält (Überlagerungen aus dem eigenen SimaticML des
Bundles), und eine separate Warnung, wenn es eine textuelle Schnittstelle deklariert. Inhalt und Hash
bleiben unverändert.

## Fallstrick 2: Überschreiben bei textueller Schnittstelle

**Export-Ledger** (Worker, `Openness/Block/BlockExportLedger.cs`; Zählregeln aus dem Bericht, Code von
diesem Fork):

- Ein neuer Engineering-Root setzt das Ledger in `TiaPortalSession.SetActiveContext` zurück. Ein Root,
  den dieser Worker geöffnet hat (`open_project`, `create_project`, ein über `LocalSessions.Open`
  geöffnetes `.als21`), beginnt jede Zählung bei einer bekannten Null. Jeder andere Root (`bind_project`
  eines bereits geöffneten Projekts, ein erneut ausgewähltes Projekt) macht jede Zählung unbekannt.
  Speichern unter behält den Root und die Zählungen.
- Jeder Export-Aufruf eines Bausteins zählt einmal, versuchte eingeschlossen: ein SimaticML-Bundle
  (`Export` plus `ExportAsDocuments`, wie es `get_block_content` xml und die eigenen Hash- und
  Begleitdokument-Prüfungen des Workers tun), ein SimaticML-Dokument allein (Lesen und Bearbeiten der
  Schnittstelle, `get_db_values`, Kopieren/Verschieben), die Prüfung mit `ExportAsDocuments` nach dem
  Import, das Erzeugen einer externen Quelle (nur der angeforderte Baustein: die Abhängigkeitshülle
  eines Quell-Exports mit `withDependencies` wird nicht gezählt). Eine unbekannte Zählung bleibt
  unbekannt.
- Ein zurückgekehrter Import des Bausteins (`update_block_logic`, SimaticML- oder SIMATIC-SD-Route mit
  Erfolgszustand, oder Generierung aus einer externen Quelle) setzt seine Zählung auf eine bekannte
  Null.
- Ein `vci_write`-Export, `sync_to_workspace` oder `sync_to_project` macht jede Zählung unbekannt
  (VCI-Blätter werden nicht einzeln gezählt); die Flags für textuelle Schnittstellen bleiben.
- Schlüssel sind der Name der PLC-Software und der Bausteinname, ohne Beachtung der
  Groß-/Kleinschreibung. Ein umbenannter, gelöschter oder neu erstellter Baustein behält die Zählung des
  Namens (eine Überzählung). Zwei gleichnamige Bausteine in verschiedenen Software Units teilen sich
  eine Zählung, sodass ein Schreibzugriff auf einen davon auch die Zählung des anderen neu startet (eine
  Unterzählung). Nicht gezählt: Exporte durch andere Openness-Clients oder die TIA-Portal-Oberfläche,
  Exporte und Importe von Technologieobjekten, Bibliotheksoperationen (`update_project`, Instanziierung
  von Typen und Kopiervorlagen).
- Die Zählung ist nur Meldungstext: Kein Guard wird ihretwegen ausgelöst oder bleibt ihretwegen still.
- Das Ledger hält außerdem fest, ob der letzte SimaticML-Bundle-Export des Bausteins eine textuelle
  Schnittstelle deklarierte.

Die Worker-Methode `get_block_export_state` (Observe, über `plc_write` abgesichert, keine öffentliche
Operation) liest einen Eintrag, ohne zu exportieren:
`{ plcName, blockName, exportCount, basis, textualInterface }` mit `basis` `opened_by_this_server`,
`written_by_this_server` oder `unknown`.

**Guards.** Für `update_block_logic` und die Bearbeitungen von Schnittstellen-Membern eines Bausteins
wird eine textuelle Schnittstelle im `.s7dcl` des Planungs-Lesevorgangs oder des übergebenen (oder
geplanten) Bundles erkannt; ein Schreibzugriff über eine externe Quelle (`format:"source"`) liest das
Ledger, dessen Flag aus einem früheren xml-Lesevorgang in derselben Bindung stammt. Dann nennt
`plc_textual_interface_overwrite_may_terminate` (info) die gezählten Exporte (oder warum die Zählung
unbekannt ist), und `plc_save_before_write_recommended` (info) empfiehlt zuerst `save_project`, weil ein
beendetes TIA Portal jede ungespeicherte Änderung verliert. Das Item-Ergebnis trägt eine Warnung.

**Befund: Die berichtete Bedingung trifft auf jedes Überschreiben über `plc_write` zu.** Nach dem
Planungs-Lesevorgang exportiert der Worker den Baustein erneut, bevor er ihn importiert: die erneute
Hash-Prüfung (Bundle-Export entsprechend `get_block_content`) und, bei einem Bundle mit
Begleitdokumenten, die Prüfung auf unveränderte Begleitdokumente (ein zweiter Bundle-Export). Beim
Import wurde der Baustein also seit einem erneuten Öffnen mindestens zweimal exportiert. Speichern,
Schließen und erneutes Öffnen des Projekts setzt die Zählung zurück, vermeidet die berichtete Bedingung
mit diesem Schreibpfad aber nicht, und der Guard sagt das. Solche Schreibzugriffe sicher zu machen,
bräuchte einen Schreibpfad, der den Baustein nach einem erneuten Öffnen höchstens einmal exportiert
(kein Planungs-Lesevorgang, ein gemeinsamer Worker-Export für die Hash- und Begleitdokument-Prüfungen);
das ist weggelassen, bis der Bericht reproduziert ist (siehe [Weggelassen](#weggelassen)).

## Fallstrick 3: Grafiklisten und `ScriptDiagnosisOverviewText`

Kein Codepfad exportiert eine Grafikliste oder liest Eigenschaften von Bildobjekten generisch:

- `hmi_read list_text_and_graphic_lists` liest nur die Namen von `HmiSoftware.HmiGraphicLists`
  (Unified); nichts ruft einen Export auf ihnen auf. `hmi_write export_text_lists` exportiert nur die
  Anwender-Textlisten von `HmiSoftware.HmiTextLists` (das Export-Member wird zur Laufzeit gebunden,
  `HmiTextListFiles`); Grafiklisten und System-Textlisten werden nie exportiert. Der berichtete
  Grafiklisten-Absturz ist ein Export aus **klassischem** WinCC (rickgaiser exportiert klassische
  Grafiklisten aus `Siemens.Engineering.Hmi.TextGraphicList` und berichtet eine `InvalidCastException`
  in `UpdateOpenLinks`). Der klassische Export `hmi_write export_classic_objects` (Issue #28, `HmiClassicObjects`)
  lehnt `graphicLists` im Katalog und im Worker ab, bevor er `Export` bindet ("skipped: reported to crash TIA Portal
  V21 …; nothing was written"); Auflisten, Importieren und Löschen von Grafiklisten bleiben erlaubt.
- `hmi_read list_screen_items` liest nur die typisierten Eigenschaften `Name`, `Left`, `Top`, `Width`,
  `Height`, `Visible` und `Enabled` jedes Objekts; es gibt keinen generischen Attribut-Dump (laut
  HMI-Referenz außerhalb des Umfangs). `HmiSystemDiagnosisControl.ScriptDiagnosisOverviewText` wird nie
  gelesen.
- `hmi_read validate` mit `screenItems` ruft auf jedem Objekt `IValidator.Validate()` auf, was
  Eigenschaften innerhalb von TIA Portal lesen kann; ob das `ScriptDiagnosisOverviewText` erreicht, ist
  unbekannt (Live-Fall L9).

`HmiPitfallSourceTests` schreibt das für die Quellen des HMI-Workers fest
(`TiaMcpServer.OpennessWorker/Openness/Hmi`): Der Test schlägt fehl, wenn eine davon
`ScriptDiagnosisOverviewText` erwähnt, irgendein `.Export(` direkt aufruft oder Attribute generisch
aufzählt (`GetAttributeInfos`), wenn `HmiGraphicLists` irgendwo anders als beim Lesen der Namen in
`HmiSoftwareInfoReader` verwendet wird oder wenn eine andere Datei als `HmiTextListFiles.cs` und `HmiClassicObjects.cs` `"Export"`
bindet (die erste darf keine Grafik- oder System-Textlisten nennen, die zweite muss Grafiklisten ablehnen, bevor sie
`Export` bindet). Ein künftiger Pfad, der eines
davon braucht, muss Grafiklisten und diese Eigenschaft überspringen und "skipped: reported to crash TIA
Portal V21" melden.

## Fallstrick 4: klassisches Bild mit doppelter `<Number>`

Angewandt seit Issue #28 (klassisches WinCC, nicht live getestet; siehe [Klassisches WinCC und Unified-Erweiterungen](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md)):
Bevor `import_classic_objects` mit `classicKind:"screens"` `Import` aufruft, liest der Worker jedes `Hmi.Screen.Screen`
(`AttributeList/Name`, `Number`) einer privaten Kopie der Datei (`HmiClassicScreenNumbers`, Contracts) und die Nummer
jedes vorhandenen Bildes (generisches `GetAttribute("Number")`, ersatzweise aus einem privaten Export dieses Bildes) und
lehnt den Eintrag mit `validation_error` ab, wenn sich eine Nummer in der Datei wiederholt oder ein anderes vorhandenes
Bild sie nutzt (rickgaiser lehnt ihn ebenfalls ab; TIA Portal beendet sich Berichten zufolge). Ein gleichnamiges
vorhandenes Bild ist das, das ein überschreibender Import ersetzt, und zählt nicht. Ein Bild, dessen Nummer nicht lesbar
ist, wird in einer Warnung genannt, und der Import läuft. Klassische Grafiklisten werden nie exportiert (Fallstrick 3).

## Fallstrick 5: geöffneter HMI-Variableneditor

Berichtet: Ein geöffneter HMI-Variableneditor in TIA Portal ("Show all tags") verlangsamt jeden
Openness-Aufruf auf diesem HMI, ein Lesen oder Schreiben seiner Variablentabellen etwa um das
Achtfache. Den Editor schließen, während `hmi_read`/`hmi_write` auf diesem HMI arbeiten. Nur
Dokumentation; der Server kann einen geöffneten Editor nicht erkennen.

## Fallstrick 6: SD-Import eines Main-Safety-Bausteins (live 2026-11-02)

Live gesehen in TIA Portal V21 am 2026-11-02 im Testprojekt `MCP_Safety_01` (PLC_F1, F-Ablaufgruppe
"F-Ablaufgruppe 1", Main-Safety-Baustein `Main_Safety_RTG1`, ein F_FBD-FB mit der Nummer 1 und
`AutoNumber` false), zweimal reproduziert und ein weiteres Mal mit dem Build `b79b2df`
([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md#re-test-with-b79b2df), E7, L1-L4):

- Jeder SIMATIC-SD-Import (`ImportFromDocuments` Override) des Main-Safety-Bausteins bringt TIA Portal
  dazu, den `MainSafetyBlockName` der Ablaufgruppe zu verwerfen (null); das Übersetzen der PLC schlägt
  dann fehl ("does not have a valid main safety block"), und die Gesamt-F-Signatur wird 0 (L1).
- Die Referenz kann nicht über Openness neu zugewiesen werden: Die `RuntimeGroup` hat keine öffentliche
  Eigenschaft `MainSafetyBlock`, und `GetAttributeInfos()` listet kein Attribut `MainSafetyBlock` (L2).
- Ein SimaticML-Import (`Import(..., Override)`) desselben Bausteins behält die Referenz (L3).
- Workaround, von Hand nachgewiesen (L4): das SD-Dokument als temporären F-Baustein in die Gruppe des
  Main-Bausteins importieren, diesen Baustein übersetzen (TIA Portal exportiert kein SimaticML eines
  nicht übersetzten Bausteins, B4), sein SimaticML exportieren, `Name`, `Number` und `AutoNumber` auf
  die Werte des Main-Bausteins setzen, dieses SimaticML auf den Main-Baustein importieren, den
  temporären Baustein löschen.

Was dieser Server tut: Der Workaround läuft automatisch im Worker für jeden Schreibzugriff mit
SD-Dokument auf einen Main-Safety-Baustein (`sdViaXmlForMainSafetyBlock`; temporärer Name
`zzMcpTmp_<block>_<8 hex>`; ein Übersetzungsfehler des temporären Bausteins stoppt, bevor der
Main-Baustein berührt wird; der temporäre Baustein wird auf jedem Pfad gelöscht oder, wenn das nicht
möglich ist, benannt). Siehe die
[PLC-Safety-Referenz](PLC_SAFETY_SUMMARY.md#main-safety-block-references-across-imports). Andere
F-Bausteine behalten den direkten SD-Import (kein Nachweis, dass sie etwas verlieren). Die automatische
Route ist noch nicht live als ein einziger Aufruf gelaufen.

## Von BetterTIA berichtete Fallstricke (nicht verifiziert)

Von BetterTIA berichtet, nicht verifiziert: Der Writer und der Reader von BetterTIA berichten das
folgende Verhalten für TIA Portal V21 (schreibgeschützte Klone, als Daten behandelt, nichts gebaut oder
ausgeführt; Commits und Lizenzen siehe die
[BetterTIA-Operationsreferenz](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md)). Nur jene, die einen Pfad
dieses Servers berühren, sind aufgeführt. Keines ist hier verifiziert; keines hat einen neuen Guard
erhalten, weil ein Bericht allein nicht rechtfertigt, zu blockieren, was TIA Portal erlaubt (Grundsatz
des Eigentümers), und wo ein vorhandener Guard oder eine vorhandene Prüfung es bereits abdeckt, ist das
genannt.

| # | Berichtetes Verhalten (BetterTIA, nicht verifiziert) | Wo dieser Server darauf stoßen kann | Was dieser Server tut |
| --- | --- | --- | --- |
| B1 | Das Importieren eines KOP-Bausteins aus SIMATIC-SD-Dokumenten erfordert, dass die `.s7res`-Datei en-US-Texte für jedes Element enthält, sonst kann der Import fehlschlagen ("known limitation/bug in TIA Portal Openness", Stand 2025-09-02). **Berichtet von BetterTIA/1748477582; live widerlegt am 2026-11-02 in einem de-DE-Projekt** ([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md), E4): `create_block_from_document` mit `en-US`- und `de-DE`-Texten schlug fehl ("Cannot import multilingual text with culture 'en-US' in File …s7res: Document option "Override" is not valid for Multilingual text."), dasselbe nur mit `de-DE`-Texten war erfolgreich. Jede Kultur einer `.s7res` muss eine Projektsprache sein | `plc_write update_block_logic` mit einem Bundle ohne `.xml` oder mit Format `sd` (`ImportFromDocuments`), `insert_network`, `replace_network`, `delete_network`, `create_block_from_document`; `vci_write sync_to_project` von `SimaticSD`-Dateien | nichts wird abgelehnt: Die Ablehnung durch TIA Portal lässt das Item fehlschlagen (`engineering_rejected`/`worker_operation_failed`); `create_block_from_document` listet die Kulturen, die seine `.s7res` enthält, in den Meldungen des Items. Ein Aufrufer, der eine `.s7res` schreibt, verwendet nur die Sprachen des Projekts (die Netzwerkbearbeitungen re-importieren TIAs eigene `.s7res` unverändert) |
| B2 | Ein Import ersetzt den ganzen Baustein, und TIA Portal kann den Instanz-DB des Bausteins und seine Aufrufer als inkonsistent markieren, auch wenn sich nur ein Netzwerkkommentar geändert hat (der Writer sah das einmal bei einem von einem OB aufgerufenen FB, nicht bei einem anderen FB und einem FC) | jeder SimaticML-Roundtrip: `update_block_logic`, die Schnittstellenbearbeitungen, `set_network_text`, `replace_network_text`, `rename_members`, `copy_block`/`move_block`; jede SIMATIC-SD-Netzwerkbearbeitung (`insert_network`, `replace_network`, `delete_network`) | Info-Guard `plc_text_edit_reimports_block` für die Text- und Netzwerkbearbeitungen (deren Verifikation empfiehlt außerdem `compile_check`) und `plc_changes_interface`/`plc_block_round_trip` für die anderen; danach mit `compile_check` übersetzen |
| B3 | Das Umbenennen eines Schnittstellen-Members kann Startwerte, die in anderen DBs für dieses Member gehalten werden, und seine Aktualwerte in der PLC zurücksetzen | `rename_members`, `update_interface_member newName` | der Text des Guards `plc_changes_interface`; Startwerte vorher sichern (z. B. `get_db_values`, `create_db_snapshot`) |
| B4 | TIA Portal exportiert nie einen inkonsistenten Baustein oder Typ; ein Massenexport überspringt sie. **Live auf V21 am 2026-11-02 bestätigt** für den SimaticML-Export: `get_block_content format xml` eines gerade aus einem SD-Dokument erstellten F-FB (nicht übersetzt) lieferte nur sein `.s7dcl`; nach `compile_check` des Bausteins kam das vollständige SimaticML zurück ([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md#re-test-with-b79b2df), L4b) | jeder Lesevorgang und Roundtrip, der exportiert (`get_block_content`, die geplanten Bearbeitungen, `export_blocks`, `move_block`) | ein Exportfehler wird als solcher gemeldet (Info-Guard `plc_state_unverifiable`, wo die Planung den Export braucht); seit 2026-11-02 (Defekt D15) wird ein abgelehnter SimaticML-Export nicht mehr still verworfen: `get_block_content xml` trägt die Warnung "was not exported as SimaticML … run compile_check and read again" mit den Dokumenten, die TIA Portal exportiert hat, und die Planungs-Lesevorgänge, die das SimaticML brauchen, nennen die Ursache; die Route für Main-Safety-Bausteine übersetzt ihren temporären Baustein, bevor sie ihn exportiert; den Baustein zuerst übersetzen |
| B5 | Ein PLC-Datentyp-Name ist in der ganzen PLC eindeutig: Das Importieren eines Namens, der in einer anderen Typgruppe existiert, schlägt auch mit `ImportOptions.Override` fehl | `update_type_content` (importiert in die eigene Gruppe des Typs), `generate_blocks_from_source` mit einem `targetGroupPath` | nichts weiter: Die Ablehnung durch TIA Portal lässt das Item fehlschlagen; in die Gruppe importieren, in der der Typ bereits liegt |
| B6 | Ein know-how-geschützter F-OB wurde anhand seiner Programmiersprache nicht als fehlersicher erkannt; der Writer behandelt außerdem jeden Baustein, den eine F-Ablaufgruppe nennt (F-OB, Main-Safety-Baustein, sein Instanz-DB, der Informations-DB), als fehlersicher | die Fehlersicher-Erkennung von `plc_write` (`PlcSafetyPolicy.IsFailSafeLanguage` auf der `ProgrammingLanguage` des Baums und dem SimaticML) | know-how-geschützte Bausteine werden ohnehin für jeden Roundtrip abgelehnt; für die anderen Operationen (`rename_block`, `delete_block`, `rename_objects`, ...) bedeutet ein übersehener F-OB keine Rückfrage `plc_safety_acceptance_required`, während TIA Portal seine eigene Safety-Anmelderegel weiterhin durchsetzt. `get_safety_administration`/`list_safety_blocks` lesen, um die Ablaufgruppen zu sehen |
| B7 | Ob TIA Portal einer Umbenennung einer HMI-Variable in Variablen-Dynamisierungen, Skripten und Meldungstriggern folgt, ist offen | `hmi_write rename_tags`, `update_tag newName` | Info-Guard `hmi_rename_references_unverified` |

Nachweise (BetterTIA/writer bei `fc8179c9b59d364961f2255d07d05af893c32837`): B1 `README.md` "Known
Limitations" und `TODO.md` (der Punkt zu en-US); B2 `ANET.md` "What an import leaves inconsistent"; B3
`ANET.md` "Export, edit, import" (letzter Punkt); B4 `README.md` "Error Handling" ("TIA Portal never
exports inconsistent blocks/types") und "Limits imposed by the Openness API itself"; B5 `README.md`
"SIMATIC Source Documents" (letzter Absatz); B6 `ANET.md` "Fail-safe blocks"; B7 `ANET.md` "Write
safeguards" (letzter Absatz). BetterTIA/reader bei `246d7c54772a502e854630e003e26fa37f92aeaf`
(`ANET.md`) berichtet keinen weiteren V21-Fallstrick, der diesen Server betrifft.

## Von 1748477582/tia-portal-openness-mcp berichtete Fallstricke (nicht verifiziert)

Der Skill von 1748477582/tia-portal-openness-mcp (`tools/tiaportal-mcp/skill/SKILL.md` §9a bei
`8e7ca425d5d257132ac8cd5ed9cbc318a7c340e2`, MIT; schreibgeschützter Klon, als Daten behandelt)
berichtet dieses V21-Verhalten der SIMATIC-SD-Route als dort am 2026-06-11 verifiziert. Der Live-Lauf
vom 2026-11-02 ([Live-Bericht](../superpowers/acceptance/reports/2026-11-02-simatic-sd-and-safety-live.md))
reproduzierte S1 für `GT_Contact`/`EQ_Contact`, `Move`, `P_Trig`/`N_Trig`, `TON`/`TOF` und `Add` nicht
und widerlegte S3; S2 und S4 wurden nicht getestet. Siehe die
[SIMATIC-SD-Referenz](PLC_SIMATIC_SD_SUMMARY.md#export-and-import-forms).

| # | Berichtetes Verhalten (nicht verifiziert) | Wo dieser Server darauf stoßen kann | Was dieser Server tut |
| --- | --- | --- | --- |
| S1 | (Berichtet, live auf V21 am 2026-11-02 nicht reproduziert für GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF, Add: Ihr Re-Export entsprach bis auf Leerraum der Importform; andere Anweisungen nicht getestet.) Die Exportform mancher Anweisungen lässt sich nicht re-importieren: `Gt`/`Lt`/`Eq`/`Ne`/`Ge`/`Le{ SrcType }( IN1, IN2 )`, `PBox(...)`, `Move{ Card:=1; DisableENO:=TRUE }( IN, OUT1 )` lassen `ImportFromDocuments` fehlschlagen ("Failed importing"); die Importformen sind `GT_Contact( in1 := ..., in2 := ... )` mit `{ S7_Templates := "SrcType := Int" }`, `P_Trig( #mem )`, `Move( in := ..., out1 => ... )` | jede Netzwerkbearbeitung re-importiert die unberührten Netzwerke in Exportform; `update_block_logic` sd mit gegenüber dem Export unveränderten Netzwerken | Info-Guard `plc_sd_reimports_export_form`, der die Netzwerke und Formen nennt; `list_networks` `exportOnlyForms`; die Verifikation `networkCode` meldet einen abweichenden Re-Export-Text als `codeMatchesExport: false`, nie als fehlgeschlagenen Import; nichts wird umgeschrieben |
| S2 | Inline als Literal angegebene Netzwerktitel (`S7_NetworkTitle := "text"`) schlagen beim Import fehl; Titel müssen `MLC_*`-IDs sein, die in der `.s7res` vorhanden sind | Code für `insert_network`/`replace_network` mit einem Titelschlüssel | als `validation_error` abgelehnt: Die Netzwerkbearbeitungen schreiben nie Titel- oder Kommentarschlüssel (`set_network_text` verwenden) |
| S3 | Die `.s7res` muss für den Import auch ohne Titel vorhanden sein (minimal: ein `MLC_x`-Eintrag mit Zeilen für `zh-CN` und `en-US`), und beide Dateien müssen UTF-8 mit BOM sein. **Live widerlegt am 2026-11-02** (E1, E4, E5): Der eigene Export von TIA Portal V21 eines Bausteins ohne Texte ist nur das `.s7dcl` (UTF-8 BOM, CRLF), und eine `en-US`-Zeile ließ den Import in einem de-DE-Projekt fehlschlagen | `create_block_from_document`; `update_block_logic` sd ohne `.s7res` | seit 2026-11-02 akzeptiert `create_block_from_document` ein Bundle ohne `.s7res` (die Ablehnung war strenger als TIA Portal, Defekt E5); eine vorhandene wird weiterhin strukturell geprüft (höchstens eine, gleicher Name wie das `.s7dcl`), und ihre Kulturen werden gelistet; es werden keine IDs erzeugt; jedes bereitgestellte Dokument wird mit genau einem UTF-8-BOM geschrieben, und ein fehlendes BOM wird in den Meldungen des Ergebnisses gemeldet |
| S4 | Ein reiner SCL-Baustein kann keine KOP-Netzwerke enthalten; ein Baustein mit `S7_PreferredLanguage := "LAD"` kann SCL-Netzwerke enthalten | `insert_network`/`replace_network` mit einer anderen Sprache | nicht abgelehnt: TIA Portal entscheidet; dokumentiert |

## In diesem Repository live beobachtet: Exceptions aus TIA-Portal-Callbacks (K27)

Anders als die Berichte oben wurde dies **live gesehen** (2026-11-02, TIA Portal V21, PLCSIM Advanced
8.0, Projekt `MCP_Safety_01`, PLC_F1 = CPU 1516F-3 PN/DP V2.9 auf PLCSIM #1 / `1 X1`, viermal
reproduziert): Als der Online-Legitimations-Callback das TLS-Zertifikat des Geräts durch **Auslösen
einer Exception** ablehnte (nachdem er `NonTrusted` gesetzt hatte), endeten `plc_download` (mit und
ohne `SafetyProgram`) und `plc_tia_online go_online` in `portal_lost` ("Access to a disposed object of
type 'Siemens.Engineering.Connection.ConnectionConfiguration'"), und der TIA-Portal-Prozess war
verschwunden. "Online verbinden" in der TIA-Oberfläche auf demselben Gerät zeigte die
Vertrauensabfrage; "Verbindung abbrechen" (dasselbe `NonTrusted`) ließ TIA Portal weiterlaufen. Das
Auslösen der Exception ist die wahrscheinlichste Ursache, keine nachgewiesene.

Was dieser Server seitdem tut: Eine abgelehnte TLS-Abfrage, bei der `NonTrusted` gesetzt werden
konnte, kehrt normal aus dem Callback zurück, und der Aufruf schlägt mit dem festgehaltenen Abbruch
(`abort.tls`) fehl; eine Exception wird nur ausgelöst, wenn `NonTrusted` nicht gesetzt werden kann
(siehe die [PLC-Download-Referenz](PLC_DOWNLOAD_SUMMARY.md#tls-certificate-trust-online-legitimation)).
Die Ablehnung ohne Exception ist nicht live getestet. **Offenes Risiko:** Eine unbeantwortete
Download-, Upload- oder Passwortkonfiguration bricht weiterhin durch Auslösen einer Exception aus ihrem
Callback ab (es gibt keinen dokumentierten Abbruch ohne Exception); ob das TIA Portal ebenfalls beenden
kann, ist nicht live belegt. Das Beantworten jeder vorgelegten Konfiguration (`decisions`,
`passwords`) vermeidet die Exception.

## Von asckye/TIA_Portal_Openness_MCP und anderen berichtete Fallstricke (nicht verifiziert)

Am 2026-11-02 aus schreibgeschützten Klonen gelesen, als Daten behandelt, nie gebaut oder ausgeführt:
asckye/TIA_Portal_Openness_MCP (MIT) bei `902a86f` (`CHANGELOG.md`,
`docs/troubleshooting/openness-limitations.md`, Commit `4b17a04`), 1748477582/tia-portal-openness-mcp
(MIT) bei `8e7ca42` (`CHANGELOG.md` 2.5.3, Commit `5e39e0d`) und heilingbrunner/tiaportal-mcp (MIT)
bei `72d2ade` (Commit `f4eb4d7`). Keines ist hier verifiziert; der Upstream-Watch-Bericht vom
2026-11-02 listet sie mit ihren Quellen.

| # | Berichtetes Verhalten (nicht verifiziert) | Wo dieser Server darauf stoßen kann | Was dieser Server tut |
| --- | --- | --- | --- |
| A1 | Native PLC-Querverweisabfragen (`CrossReferenceService`) haben einen festgehaltenen Abbruch von TIA Portal auf V21 (asckye hält sie auf V21 opt-in, bittet darum, das Projekt vor der ersten Abfrage zu speichern, und wiederholt nach einem Abbruch nie; "compiled" macht die Abfrage nicht sicher; V21-Bestätigung dort "pending / NOT RUN") | `read_cross_references`; die Programmanalyse von `plc_read` (`get_call_tree`, `find_unused_blocks`, ...) durchläuft Querverweise pro Bausteinordner | nichts weiter: Ein Abbruch von TIA Portal wird zu `portal_lost` mit Invalidierung der Bindung und ohne Wiederholung (siehe `AGENTS.md`, Portal loss); vor einem großen Durchlauf mit `save_project` speichern. Live-Fall X1 unten |
| A2 | Das Umbenennen eines Bibliothekstyps für WinCC-Unified-Skripte hat einen gemessenen Absturz (asckye) | keine: Dieser Server hat keine Umbenennung von Bibliothekstypen für HMI-Skripttypen (`library_write` benennt Typen generisch um; Unified-Skripttypen werden nicht unterschieden) | dokumentiert; ein `library_write rename_type` auf einem solchen Typ endet in `portal_lost`, falls es passiert |
| A3 | Das Beantworten von `ActiveTestCanBeAborted`/`ActiveTestCanPreventDownload` mit `AcceptAll` bricht während eines Downloads auf der CPU laufende Test- und Debug-Funktionen ab (1748477582 2.5.3, nach dem Handbuch) | `plc_download` | für diese Konfigurationen gibt es keinen eingebauten Standard: Der Download bricht mit `decision_required` und `availableSelections` ab, sodass nur ein `decisions`-Eintrag des Aufrufers sie beantworten kann |
| A4 | Der OPC-UA-Export benötigt die PLC offline (heilingbrunner) | `plc_write export_opcua_xml` | in der Tool-Beschreibung angegeben; TIA Portal entscheidet |

## Weggelassen

| Punkt | Grund |
| --- | --- |
| Ablehnen der SIMATIC-SD-Route oder des Überschreibens bei textueller Schnittstelle (rickgaiser lehnt beides ab) | Nicht verifizierte Berichte; siehe [warum die Guards informieren](#warum-die-guards-informieren-und-nie-blockieren). |
| Prüfen von `vci_write sync_to_project` für `SimaticSD`-Workspace-Dateien | Die Dateien werden außerhalb des Servers bearbeitet und als ein Scope innerhalb einer Transaktion importiert; der Planer liest keinen Dateiinhalt, und das aktuelle SimaticML jedes Blatts würde einen Export pro Blatt brauchen (was selbst die Zählung von Fallstrick 2 erhöht). Export verwendet standardmäßig SimaticML, sodass `SimaticSD`-Dateien nur existieren, wenn ein Aufrufer dieses Format verlangt hat; die VCI-Referenz sagt bereits, dass ein `SimaticSD`-Export TIA Portal zum Absturz bringen kann. |
| Ein Schreibpfad mit höchstens einem Export nach erneutem Öffnen | Er würde die Hash-Prüfung der Planung und den Inhalts-Diff für solche Bausteine entfallen lassen und den Preflight des Workers ändern; lohnt sich erst, wenn Fallstrick 2 reproduziert ist (Live-Fälle L5–L7). |
| Erkennung textueller Schnittstellen für `format:"source"` ohne früheren xml-Lesevorgang | Von externem Quelltext ist nicht bekannt, dass er `S7_EditorMode` trägt; Raten wäre Erfinden. |
| `move_block`, `copy_block`, Bibliotheks-`update_project` oder Typinstanziierung über einen Baustein mit textueller Schnittstelle | `move_block` löscht und importiert (kein Überschreiben), `copy_block` erzeugt einen neuen Baustein; ob Bibliotheksaktualisierungen Bausteine auf eine Weise ersetzen, die der Bericht abdeckt, ist unbekannt. Ihre Exporte werden gezählt. |
| Erkennen eines geöffneten HMI-Variableneditors | Kein dokumentiertes Openness-Member meldet geöffnete Editoren. |

## Liste der Live-Abnahme

X1 (asckye A1): auf einer Wegwerfkopie eines V21-Projekts speichern, dann `read_cross_references` auf
einem Baustein und `get_call_tree` über das Programm ausführen; festhalten, ob TIA Portal weiterläuft,
und das TIA-Portal-Log, falls es sich beendet.

Jeder Fall muss auf V21 reproduziert (und in einem Abnahmebericht festgehalten) werden, bevor ein
Guard- oder Warnungstext "TIA Portal does X" statt "is reported to" sagen darf. Jeden Fall auf einer
Wegwerfkopie des Projekts ausführen, nachdem ungespeicherte Arbeit gespeichert wurde.

| Fall | Schritte | Erwartet, falls der Bericht zutrifft | Dann |
| --- | --- | --- | --- |
| L1 | KOP-Netzwerk mit `"DB_A".arr["DB_B".structArr[1].idx]`; `get_block_content` xml; das Bundle **ohne** sein `.xml`-Dokument übergeben | Guard und Warnung werden ausgelöst; der Re-Export zeigt den inneren Zugriff als einen einzigen Namen in Anführungszeichen; `s7dclConstructs` schlägt fehl | Texte von Fallstrick 1a umformulieren |
| L2 | FB mit `tmr : _.TONR;` (Projekt-FB namens `TONR`) über die SIMATIC-SD-Route | Re-Export deklariert `TONR_TIME`; `s7dclConstructs` schlägt fehl | 1b umformulieren |
| L3 | FB mit `flagsAt : Word` AT `flags` über die SIMATIC-SD-Route | re-exportiertes SimaticML hat kein Attribut `At`; `s7dclConstructs` schlägt fehl | 1c umformulieren |
| L4 | Bundles aus L1–L3 **mit** dem `.xml`-Dokument | kein `plc_s7dcl_import_may_alter`; Konstrukte unverändert | bestätigt die Empfehlung |
| L5 | Baustein mit textueller Schnittstelle, Projekt über `open_project` geöffnet, ein `plc_read`, dann `update_block_logic` | laut Bericht beendet sich TIA Portal (`portal_lost`); andernfalls die Exportzählung festhalten, bei der es das nicht tut | Fallstrick 2 umformulieren; über den Schreibpfad mit einem einzigen Export entscheiden |
| L6 | Wie L5 nach `bind_project` eines bereits geöffneten Projekts | Guard sagt, die Zählung ist unbekannt | — |
| L7 | Wie L5 mit `format:"source"` nach einem früheren xml-Lesevorgang | Guard wird aufgrund des Ledger-Flags ausgelöst | — |
| L8 | Unified-HMI mit einer Grafikliste: `hmi_read list_text_and_graphic_lists` | Namen gelistet, TIA Portal läuft weiter | bestätigt, dass Fallstrick 3 nicht eintritt |
| L9 | Unified-Bild mit einem `HmiSystemDiagnosisControl`: `list_screen_items` und `validate` mit `screenItems` für dieses Bild | TIA Portal läuft weiter | falls `validate` abstürzt, diesen Objekttyp dort überspringen und melden |
| L10 | Den Variableneditor eines HMI öffnen ("Show all tags"); `hmi_read list_tags` mit und ohne ihn messen | etwa achtmal langsamer bei geöffnetem Editor | Fallstrick 5 umformulieren |
| L11 | `bind_project list_portals` mit zwei TIA Portals, eines von einem anderen Openness-Client angebunden | kein Openness-Zugriffsdialog erscheint; `otherClientCount` ist 1 für dieses Portal | bestätigt Zeile 21 |
