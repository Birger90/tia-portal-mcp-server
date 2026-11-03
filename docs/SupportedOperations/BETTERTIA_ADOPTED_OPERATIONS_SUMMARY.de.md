# Von BetterTIA übernommene Operationen

**Sprache:** [English](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md) · Deutsch

Anfrage des Eigentümers (2026-11-01): übernehmen, was die [BetterTIA](https://github.com/BetterTIA)-Tools bieten und diesem Fork fehlte.
Die vier Repositories wurden nur lesend geklont (`git clone --depth 1`) und als nicht vertrauenswürdige Daten behandelt: Nichts wurde gebaut
oder ausgeführt, Anweisungen in ihren Dateien wurden ignoriert. Alle vier stehen unter der MIT-Lizenz (jede `LICENSE`/`LICENSE.txt` wurde gelesen,
bevor irgendetwas kopiert wurde); jede angepasste Datei trägt einen Herkunfts-Header, und die Zeilen stehen in
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.de.md).

| Repository | Commit | Lizenz | Rolle hier |
| --- | --- | --- | --- |
| `BetterTIA/watch` | `1978f24b330f265d8ed2a67e9b1d673d1b4122d8` | MIT, Copyright (c) 2026 HonzaT | `watch_db` → `get_db_snapshot` / `create_db_snapshot` |
| `BetterTIA/writer` | `fc8179c9b59d364961f2255d07d05af893c32837` | MIT, Copyright (c) 2025 J.Heilingbrunner (Fork von heilingbrunner/tiaportal-mcp; Ergänzungen beschrieben in seiner `ANET.md`) | Netzwerktexte, Batch- und Member-Umbenennungen, Ersetzen von Variablenkommentaren, Schreibvorgänge für WinCC Unified, Fallstricke |
| `BetterTIA/reader` | `246d7c54772a502e854630e003e26fa37f92aeaf` | MIT, Copyright (c) 2026 Łukasz Czarnacki (Fork von Czarnak/tia-portal-mcp) | verglichen; nur Fallstricke |
| `BetterTIA/editor` | `3d115b30035ad5ea2f10206e5354190ea1ba4de5` | MIT, Copyright (c) 2026 HonzaT | bewertet, nicht portiert ([Bewertung](../development/bettertia-editor-evaluation.md)) |

Nichts hier ist live getestet. Jede Operation folgt den Regeln dieses Forks: der geschützten Schreib-Pipeline (`plc_write`
führt standardmäßig aus, `dryRun:true` zeigt eine Vorschau), dem Eigentümerprinzip „alles erlauben, was TIA Portal erlaubt“ (kein
`allowFailsafe`-Schalter nach Art von BetterTIA und kein standardmäßiger Probelauf; fehlersichere Objekte folgen der Anmelderegel von TIA Portal), und
Siemens-Member, die weder in den Compile-Stubs stehen noch in den Siemens-Skills dokumentiert sind, werden zur Laufzeit über
den exakten Namen und die Argumenttypen gebunden und schlagen mit `api_member_missing` fehl, ohne dass etwas geändert wurde.

## DB-Snapshots der Istwerte (`plc_read get_db_snapshot`, `plc_write create_db_snapshot`)

Übernommen aus `watch_db` (`src/BtiaWatch/OpennessPortal.cs` `ExportDbSnapshot`, `src/BtiaWatch/WatchTools.cs`
`FormatSnapshot`).

| Operation | Tool | Fähigkeit | Felder | Was sie tut |
| --- | --- | --- | --- | --- |
| `get_db_snapshot` | `plc_read` | `TemporaryExport` (jeder Modus) | `blockPath` (Global- oder Instanz-DB), optional `memberPath`, `maxElements` (1-20000, Standard 2000) | Exportiert den im Offline-Projekt gespeicherten Snapshot (`InterfaceSnapshot.Export(FileInfo, ExportOptions.WithReadOnly)` in eine temporäre Datei, die gelöscht wird) und liefert `{ hasSnapshot, snapshotDate, totalValues, matchedValues, truncated, values [{ path, value, dataType }], topLevelMembers }`. |
| `create_db_snapshot` | `plc_write` | `ProjectMutation` | dieselben | Erfordert, dass die PLC online ist (`OnlineProvider.State` `Online`, z. B. nach `plc_tia_online go_online`); ruft `ValueService.CreateSnapshot()` auf, das die Istwerte der Online-PLC übernimmt und sie als Snapshot des DB im Offline-Projekt speichert, und liefert dann den Export wie oben. Info-Guard `plc_stores_db_snapshot`. Verifikation `dbSnapshot`: `get_db_snapshot` muss den Snapshot-Zeitpunkt und die Anzahl der Werte zeigen, die der Schreibvorgang geliefert hat. |

Warum ein Schreibvorgang: TIA Portal speichert den Snapshot im Offline-Projekt (das Projekt erscheint als geändert), daher ist es kein
Lesevorgang im Sinne des Modus read-only. Nichts wird in die PLC geschrieben, und kein Startwert ändert sich: Die Member von
`ValueService`, die Werte laden, werden nie gebunden (TIA Portal bietet an, Snapshot-Werte als Startwerte zu übernehmen oder sie
in die PLC zu laden; siehe Weggelassen unten).

`memberPath` behält die Member, die gleich dem Pfad sind oder darunter liegen (`A.B` passt auf `A.B`, `A.B.C` und `A.B[1]`, nie
auf `A.BC`), ohne Beachtung der Groß-/Kleinschreibung, wie es `watch_db` tut; ein Filter, der auf nichts passt, listet die Member der obersten Ebene.

### Siemens-Member

| Member | Bindung | Nachweis |
| --- | --- | --- |
| `ValueService` an `PlcBlock.Interface` | Dienst, gefunden unter `GetServiceInfos()` über den exakten Kurznamen in einem `Siemens.Engineering`-Namespace, abgerufen mit `IServiceProvider.GetService(Type)` | BetterTIA/watch `OpennessPortal.cs` (`db.Interface.GetService<ValueService>()`), laut seiner README live gegen V21 mit einer realen PLC geprüft; nicht in den Siemens-Skills oder den Compile-Stubs |
| `ValueService.CreateSnapshot()` | `LibraryRuntimeBinding.Find(type, "CreateSnapshot", [])` | dasselbe; die README berichtet, dass es offline ohne Fehler zurückkehrt und den Snapshot leer lässt, daher lehnt `create_db_snapshot` eine Offline-PLC ab (`validation_error`, nichts geändert) |
| `InterfaceSnapshot` am Baustein | wie `ValueService`, am Baustein selbst | dasselbe (`db.GetService<InterfaceSnapshot>()`; "on the interface it is null (spike, V21)") |
| `InterfaceSnapshot.Export(FileInfo, ExportOptions)` | `LibraryRuntimeBinding.Find(type, "Export", [FileInfo, ExportOptions])` | dasselbe |
| `ExportOptions.WithReadOnly` | per Name auf dem Live-Enum geparst (das Stub-Enum deklariert nur `None`) | dasselbe ("ExportOptions.None leaves it out (live test, V21)") |
| `OnlineProvider.State` am CPU-Geräteelement | kompiliert (Stub) | bereits von `get_online_state` verwendet |

Das Export-Layout (`AttributeList/Snapshot/.../Value[@Path,@Type]` und `AttributeList/SnapshotDate`) ist dasjenige, das
BetterTIA/watch parst; `PlcDbSnapshotDocument` (Contracts, Siemens-frei) liest nur diese Elemente. Ein Export
ohne `Snapshot`-Attribut wird als `hasSnapshot:false` mit einer Meldung gemeldet, nie als leere Werteliste.

### Weggelassen

- **Instanz-DBs von Technologieobjekten** (`PID_Compact`, Achsen): BetterTIA/watch berichtet, dass V21 `Interface` auf
  `TechnologicalInstanceDB` verweigert, sodass keine Istwerte über Openness kommen; `blockPath` löst nur Programmbausteine auf.
- **Laden von Snapshot-Werten als Startwerte oder in die PLC**: TIA Portal bietet beides an, aber keine Quelle nennt die
  `ValueService`-Member; BetterTIA bindet sie absichtlich nie. Nicht geraten.
- **`watch_online`**: `plc_tia_online` geht bereits online/offline auf dem exakten Download-Pfad mit Bestätigung pro
  Aufruf; seine Positivliste `BTIA_WATCH_ALLOWED_TARGETS` wird durch den Zugriffsmodus ersetzt (Entscheidung des Eigentümers).
- **`watch_screen`** (Screenshot des TIA-Editors mit Beobachtung): siehe [Weggelassen](#insgesamt-weggelassen).

## Netzwerktitel und -kommentare (`plc_write set_network_text`, `replace_network_text`)

Übernommen aus `SetNetworkText` / `ReplaceNetworkText` des Writers (`src/TiaMcpServer/Siemens/SimaticMl.cs`, Region
"network titles and comments", und `Portal.AnetFork.cs` `EditNetworkTexts`).

| Operation | Felder | Was sie tut |
| --- | --- | --- |
| `set_network_text` | `blockPath` (OB, FB oder FC), `networkNumber` (ab 1), `title` und/oder `comment`, optional `culture`, `expectedContentHash` (xml) | Setzt den Titel und/oder Kommentar eines Netzwerks. Ohne `culture` muss der Text in genau einer Sprache existieren; mit ihr wird eine Sprache, für die das Netzwerk kein Element hat, hinzugefügt (ein `MultilingualTextItem` mit der nächsten freien hexadezimalen ID), so wie TIA Portal einen Benutzer einen Text in jeder Projektsprache eingeben lässt. |
| `replace_network_text` | `blockPath`, `replacements` (1-100 `{ find, replace }`), optional `culture` (weggelassen: jede Sprache), `textScope` `networks` (Standard) oder `all`, `expectedContentHash` | Ersetzt jedes `find` in einem Durchgang (wörtlich, mit Beachtung der Groß-/Kleinschreibung, das längste zuerst, sodass `Pump 1`→`Pump 10` mit `Pump 10`→`Pump 1` tauscht und `Pump 10` nie von `Pump 1` verschluckt wird) in jedem Netzwerktitel und -kommentar; `all` deckt außerdem den Bausteintitel und -kommentar sowie die Texte von GRAPH-Schrittkette, Schritten und Transitionen ab. Schritt- und Transitionsnamen sind Schnittstellen-Member und bleiben. |

Openness hat keinen Setter für Netzwerktexte, daher sind beide vom Host geplante Dokumentbearbeitungen wie die Bearbeitungen von Schnittstellen-Membern
(`PlcDocumentEditPlanner`): Die Bausteinidentität wird gelesen (`get_block_interface`; know-how-geschützte Bausteine werden abgelehnt,
ihr Export trägt nur die Schnittstelle), das xml-Bündel wird exportiert (oder das Dokument, das ein früheres Element des Aufrufs
geplant hat, wird verwendet, mit `dependsOn` und einer Neuplanung vor der Mutation), das SimaticML-Dokument wird mit dem
Siemens-freien `SimaticMlNetworkTexts` bearbeitet und über `update_block_logic` gegen den Hash des Exports importiert, sodass eine Änderung
zwischendurch mit `state_changed` fehlschlägt. Ein Element, das nichts ändert (Texte bereits wie angefordert, kein find getroffen), wird abgelehnt,
bevor irgendetwas läuft, weil das erneute Importieren eines unveränderten Bausteins seinen Instanz-DB und seine Aufrufer trotzdem inkonsistent machen kann.
F-Bausteine folgen der Regel von TIA Portal (`plc_safety_acceptance_required`, eine Anmeldung nur, wenn ein Safety-Passwort gesetzt ist),
wie jede andere Änderung an einem F-Baustein. Info-Guard `plc_text_edit_reimports_block`; die V21-Fallstrick-Guards eines SimaticML-Überschreibens
gelten. Verifikation `networkText`: Der Baustein wird erneut exportiert, jeder geänderte Text muss an derselben
Stelle und in derselben Sprache zurückgelesen werden, und der neue `contentHash` wird gemeldet (verwendbar als nächster `expectedContentHash`).

Das SimaticML-Modell (ein Netzwerk ist eine `SW.Blocks.CompileUnit` in der `ObjectList` des Bausteins, ab 1 in
Dokumentreihenfolge nummeriert; Titel und Kommentar sind `MultilingualText`-Objekte `Title`/`Comment` mit einem `MultilingualTextItem` pro Kultur;
GRAPH-Texte sind `MultiLanguageText`-Elemente mit `Lang`) ist das des Writers und entspricht den
V21-SimaticML-Fixtures dieses Forks. Über den bestehenden Export und Import hinaus wird kein Siemens-Member aufgerufen.

Weggelassen: die Listen `Dependents` / `InconsistentAfter` des Writers (aus den Querverweisen) — vorher
`plc_read analyze_change_impact` und danach `compile_check` verwenden; sein Standard `dryRun=true` und `allowFailsafe`
(dieser Fork führt standardmäßig aus und folgt der F-Regel von TIA Portal).

## Member-Umbenennungen (`plc_write rename_members`)

Übernommen aus `RenameMembers` des Writers (`SimaticMl.cs` Region "member rename", `Portal.AnetFork.cs`).

Felder: `blockPath` (ein Codebaustein oder Global-DB) oder `typePath` (ein PLC-Datentyp), `memberRenames` (1-100
`{ memberPath, newName, section? }`), optional `expectedContentHash` (xml). `section` darf bei einem Baustein weggelassen werden, wenn
genau ein Abschnitt den Pfad enthält.

Was TIA Portal tut, und daher was diese Operation tut: Das Umbenennen eines Members im Bausteineditor benennt es in der
Schnittstelle um und aktualisiert den eigenen Code des Bausteins. Openness hat keinen Member-Setter, daher ist die Operation eine vom Host geplante
Dokumentbearbeitung wie `set_network_text` (`PlcDocumentEditPlanner` mit dem Siemens-freien `SimaticMlMemberRenames`):

- jede Umbenennung wird mit dem bestehenden Bearbeitungsmodell auf die Schnittstelle angewendet, zuerst unter temporären Namen und dann unter den
  endgültigen, sodass Vertauschungen, Rotationen und Ketten in einem Element funktionieren und ein Name, den ein anderes Member hält (in irgendeinem Abschnitt eines
  Bausteins), abgelehnt wird, bevor irgendetwas läuft; ein Member und eines seiner eigenen Member können nicht in einem Element umbenannt werden;
- die eigenen Zugriffe des Objekts werden aktualisiert: jedes `Access Scope="LocalVariable"`-Symbol und jede
  `Instance Scope="LocalVariable"`, deren führende Komponenten ein umbenanntes Member nennen (ohne Beachtung der Groß-/Kleinschreibung, abgeglichen mit
  den Namen vor der Bearbeitung, sodass eine Vertauschung nicht doppelt umbenennt), in LAD-, FBD-, STL-, SCL- und GRAPH-Netzwerken, sowie die lokalen
  Operanden (`#name`), die GRAPH-Aktionen als Text halten (die Muster `Segment`/`Operand` des Writers); Kommentare und Titel sind
  Texte und bleiben (eine Erwähnung wird als Warnung gemeldet, `plc_state_unverifiable` info);
- Zugriffe in **anderen** Bausteinen werden nicht aktualisiert: TIA Portal schreibt sie ebenfalls nicht um (die Aufrufer und Leser der
  Member eines FB oder DB werden inkonsistent, bis sie bearbeitet und übersetzt werden). Das bausteinübergreifende Umschreiben des Writers (jeder
  Baustein und Typ der PLC exportiert, ein Deklarationsauflöser über DBs, Instanz-DBs, Multiinstanzen und PLC-Datentypen,
  Formalparameter bei Aufrufen) wurde nicht übernommen: Es ist nicht das, was TIA Portal tut, es wurde von seinen Autoren nicht gegen ein laufendes TIA
  Portal verifiziert, und es würde jeden betroffenen Baustein erneut importieren. Vorher `plc_read analyze_change_impact` lesen und
  danach `compile_check` ausführen;
- Instanz- und Array-DBs werden abgelehnt (die Member des FB umbenennen); know-how-geschützte Bausteine werden abgelehnt.

Info-Guard `plc_changes_interface` mit der Meldung oben; F-Bausteine folgen der Regel von TIA Portal; die Verifikation
`renamedMembers` liest die Schnittstelle erneut (`get_block_interface` / `get_type_interface`): Jedes Member muss seinen neuen
Namen exakt tragen, und sein alter Pfad muss verschwunden sein (außer eine andere Umbenennung des Elements übernimmt ihn), und der neue `contentHash`
wird gemeldet.

Weggelassen vom Writer: seine Liste `HmiDependents` (HMI-Variablen, deren PLC-Pfad über ein umbenanntes Member führt) — die Bindung der HMI-Variable
wird mit `hmi_write update_tag plcTag` geändert; `summaryOnly`-Berichtsdateien; `allowFailsafe`.

## Batch-Umbenennungen (`plc_write rename_tags`, `rename_objects`)

Übernommen aus `RenameTags` / `RenameObjects` des Writers (`Portal.AnetFork.cs` Region "batch rename",
`Siemens/BatchRename.cs`).

| Operation | Felder | Objekte |
| --- | --- | --- |
| `rename_tags` | optional `plcName`, `renames` (1-200 `{ name, newName }`) | PLC-Variablen nach Name über jede Variablentabelle der PLC hinweg (Variablennamen sind pro PLC eindeutig; der Planer löst sie im Variableninventar auf, sodass eine fehlende Variable `target_not_found` ist, bevor irgendetwas läuft, und spätere Elemente des Aufrufs die neuen Namen sehen) |
| `rename_objects` | optional `plcName`, `renames` | Bausteine, PLC-Datentypen und Technologieobjekte der PLC nach Name; ein Name, der in mehr als einer Art gefunden wird, ist `target_ambiguous` (vom Worker aufgelöst) |

Jedes Objekt wird aufgelöst, bevor sich etwas ändert. `PlcBatchRenamePlan` (Contracts, `BatchRename.Plan` des Writers)
ordnet die Setter-Aufrufe: Ein Objekt, dessen neuen Namen ein anderes Objekt des Batches noch hält, wird unter einem temporären
Namen geparkt (`zzTmp_<stamp>_<i>`, ein frischer Stempel pro Versuch) und erhält seinen endgültigen Namen zuletzt, sodass Vertauschungen, Rotationen und Ketten
funktionieren; eine Umbenennung nur in der Groß-/Kleinschreibung ist kein Konflikt; ein zweimal angegebener Name wird abgelehnt. Jeder Schritt ist
`SetAttribute("Name")` nach einer Beschreibbarkeitsprüfung mit `GetAttributeInfos()`, mit Rücklesung über `GetAttribute("Name")` (das
gemeinsame `LibraryReflection.Rename`, auch von den Gruppenumbenennungen verwendet), und alle Schritte laufen in einer
`ExclusiveAccess`-Transaktion (`PlcRoundTrip.Run`): Ein fehlgeschlagener Schritt (TIA Portal lehnt einen Namen ab, eine abweichende Rücklesung) rollt jede
Umbenennung zurück, temporäre Namen eingeschlossen, und der Fehlschlag nennt danach den Namen jedes Objekts.

Unterschiede zum Writer, nach dem Eigentümerprinzip: keine Kollisions-Vorprüfung gegen Namen außerhalb des Batches (TIA
Portal entscheidet; eine stillschweigend ignorierte Umbenennung lässt die Rücklesung fehlschlagen und wird zurückgerollt), kein `dryRun`-Standard und kein
`allowFailsafe`; fehlersichere Variablen (`IsSafety`) und F-Bausteine folgen der Safety-Anmelderegel von TIA Portal (Worker-Gate vor der
Transaktion; ein in `rename_objects` genannter F-Baustein, den der Projektbaum zeigt, löst den Abnahme-Guard aus wie
`rename_block`); know-how-geschützte Bausteine werden nicht abgelehnt (TIA Portal benennt sie um). Info-Guard `plc_batch_rename`.
Verifikation `batchRename`: `rename_tags` liest die Variablentabellen erneut (jeder neue Name vorhanden, jeder alte Name, den keine
Umbenennung übernimmt, verschwunden); `rename_objects` meldet die Namen, die die Transaktion zurückgelesen hat.

Siemens-Member: `PlcSoftware.TagTableGroup`/`BlockGroup`/`TypeGroup`/`TechnologicalObjectGroup` mit ihren `Groups`,
`TagTables`/`Tags`, `Blocks`, `Types`, `TechnologicalObjects` (kompiliert, in den Stubs); `IEngineeringObject`
`GetAttributeInfos`/`SetAttribute`/`GetAttribute("Name")` (generischer Vertrag, bereits von den Gruppenumbenennungen verwendet);
`GetAttribute("IsSafety")` (bereits von `move_tag` verwendet); `TiaPortal.ExclusiveAccess` / `Transaction` (bereits verwendet von
`move_tag`, `copy_block`, `move_block`).

Weggelassen vom Writer: die Querverweissuche nach fehlersicheren Verwendern (`UsedByFailsafe`; stattdessen gilt die eigene Regel von TIA Portal),
`summaryOnly`-Berichtsdateien, Variablen von Software Units (nur die eigenen Variablentabellen der PLC werden durchsucht,
wie beim Writer), Benutzerkonstanten.

## Ersetzen von Variablenkommentaren (`plc_write replace_tag_comments`)

Übernommen aus `ReplaceTagComments` / `EditTagComments` des Writers (`Portal.AnetFork.cs` Region "tag comments").

Felder: optional `plcName`, optional `tableName` (mit `folderPath`) für eine Tabelle, `replacements` (1-100
`{ find, replace }`, die Ersetzung in einem Durchgang mit dem längsten zuerst wie bei `replace_network_text`), optional `culture` (weggelassen:
jede Sprache). Umfang: die Variablen der eigenen Variablentabellen der PLC (Software Units und Benutzerkonstanten sind nicht abgedeckt,
wie beim Writer).

Der Planer zeigt die Änderung vorab aus dem öffentlichen Lesevorgang `read_tag_comments`: Der Effekt listet jeden Kommentartext, der sich
ändert (`comment /<folder>/<table>/<tag> [<culture>]`, alt und neu); ein Element, das nichts ändert, wird abgelehnt, ein
nicht lesbarer oder unvollständiger Lesevorgang löst `plc_state_unverifiable` (info) aus, und das Element läuft. Der Worker wendet dieselbe
Ersetzung auf die Live-Texte an: Jedes `MultilingualTextItem` von `PlcTag.Comment.Items`, dessen Text sich ändert, wird
über `MultilingualTextItem.Text` gesetzt und zurückgelesen, alles in einer `ExclusiveAccess`-Transaktion (ein Fehlschlag rollt jeden Text
zurück). Kommentare sind Texte, kein Inhalt des Sicherheitsprogramms: Es gilt kein Safety-Gate, TIA Portal entscheidet. Die Verifikation
`tagComments` liest `read_tag_comments` erneut: Jeder Text, den der Worker als geändert gemeldet hat, muss so zurückgelesen werden, wie er geschrieben wurde.

Siemens-Member: `PlcTag.Comment` (`MultilingualText`), `MultilingualText.Items`, `MultilingualTextItem.Language.Culture`
und `.Text` (get/set) — alle in den Compile-Stubs und bereits von `read_tag_comments` und `move_tag` verwendet.

`replace_tag_comments` ändert nur bestehende Texte: Ein Element ohne Treffer wird abgelehnt (und weil die Planung es
ablehnt, läuft der ganze Aufruf nicht; die Meldung sagt das). Der Kommentar einer Variable wird direkt mit `create_tag`/`update_tag`
`comment`+`commentCulture` gesetzt (Defekt K22, 2026-11-02; `SetTagComment` des Writers): Das Textelement dieser Kultur wird so
gefunden, wie der Siemens-Skill (tia-project-general, Spracheinstellungen §5) einen mehrsprachigen Text schreibt
(`LanguageSettings.Languages` → `MultilingualText.Items.Find(Language)` → `MultilingualTextItem.Text`), und zurückgelesen;
eine inaktive Projektsprache wird mit einer Warnung weitergereicht (TIA Portal entscheidet), eine Kultur, für die TIA Portal kein Textelement hat,
schlägt fehl (kein Member, das eines anlegt, ist dokumentiert; `create_tag` löscht dann die Variable, die es angelegt hat), und eine Kultur,
die keine Sprache von TIA Portal ist, wird abgelehnt, bevor sich etwas ändert. Die Verifikation `tag` liest die Variablentabelle und
`read_tag_comments` erneut. Nicht live getestet.

## WinCC-Unified-Bilder, Skripte und Batch-Umbenennungen (`hmi_write`)

Übernommen aus den Unified-Tools des Writers (`Portal.AnetFork.Hmi.cs`, `Portal.AnetFork.HmiScreens.cs`, `ANET.md`
Abschnitte "WinCC Unified" und "WinCC Unified screens and screen items"). Neun neue `hmi_write`-Operationen (Worker-Methoden
`hmi_<name>`, `HmiScreenMethods` in Contracts; `ProjectMutation` wie jeder HMI-Schreibvorgang); das Tool hatte dann 18, und hat 20 mit den [Textlisten-Dateien](#variablentabellen-und-wincc-unified-textlisten-als-dateien-ergänzungen-des-eigentümers).

| Operation | Felder | Was sie tut |
| --- | --- | --- |
| `create_screen` | `screenName`, optional `groupPath`, `width`, `height` | `HmiScreenComposition.Create(name)` in der Wurzel oder in einer Bildgruppe, dann `Width`/`Height`; ein fehlgeschlagener Setter löscht das neue Bild wieder (die gemeinsame Create-Bereinigung von `hmi_write`). Openness bietet keine Bildkopie und keine Vorlage (Writer, per Reflection über V21), daher ist ein neues Bild leer. |
| `delete_screen` | `screenName`, optional `groupPath` | `Delete()` (Info-Guard `hmi_deletes_screen`: Bildfenster, Navigation und Skripte, die es nennen, werden nicht geändert) |
| `rename_screens`, `rename_tags` | `renames` (1-200 `{ name, newName }`) | Bilder oder HMI-Variablen nach Name, Vertauschungen und Rotationen über temporäre Namen (`PlcBatchRenamePlan`), jeder Schritt `SetAttribute("Name")` mit Rücklesung, alles in einer `ExclusiveAccess`-Transaktion, die jeder Fehlschlag zurückrollt. Info-Guard `hmi_rename_references_unverified`: Ob TIA Portal einer umbenannten HMI-Variable in Dynamisierungen, Skripten und Meldungstriggern folgt, ist offen (der Writer führt es als nicht verifiziert), und Bildnamen in Skripten und Bildfenstern werden nicht geändert. |
| `create_screen_item` | `screenName`, `itemName`, `itemType`, optional `left`, `top`, `width`, `height` | `ScreenItems.Create<T>(name)` mit `T` dem öffentlichen, nicht abstrakten Bildobjekttyp, dessen Kurzname genau `itemType` ist (z. B. `HmiButton`, `HmiIOField`, `HmiText`, `HmiRectangle`, `HmiCircle`; der Typ wird nie geraten), dann die Geometrie; ein fehlgeschlagener Setter löscht das Objekt wieder. Ein Kreis hat statt einer Box einen Mittelpunkt und einen Radius: diese mit `set_screen_item_property` setzen. |
| `delete_screen_item` | `screenName`, `itemName` | `Delete()` |
| `set_screen_item_property` | `screenName`, optional `itemName` (weggelassen: das Bild), `propertyName`, `value`, optional `culture` | `SetAttribute(propertyName, value)` mit dem Wert, umgewandelt in genau den Laufzeittyp des aktuellen Werts (String, Bool, Zahl, Name eines Enum-Members, `System.Drawing.Color` aus `#RRGGBB`/`#AARRGGBB`); ein `MultilingualText` übernimmt den Text von `culture`; Rücklesung. `ScriptDiagnosisOverviewText` wird abgelehnt ("skipped: reported to crash TIA Portal V21", V21-Fallstrick 3). |
| `set_dynamization_tag` | `screenName`, optional `itemName`, `propertyName`, `tagName` | setzt `Tag` der `TagDynamization` der Eigenschaft, oder legt eine mit `Dynamizations.Create<TagDynamization>(propertyName)` an, wenn die Eigenschaft keine hat (der Writer setzte nur bestehende); eine Skript-Dynamisierung an der Eigenschaft wird abgelehnt; Rücklesung |
| `replace_in_scripts` | `replacements` (1-100), optional `screenName` (+ `groupPath`) | die Ersetzung in einem Durchgang von `replace_network_text` über `ScriptCode` und `GlobalDefinitionAreaScriptCode` jedes Ereignis-Handlers, Eigenschaftsereignis-Handlers und jeder Skript-Dynamisierung der Bilder und ihrer Objekte (vier Objektebenen tief, wie der Writer sie durchläuft); jedes geschriebene Skript wird zurückgelesen; die `appliedProperties` des Ergebnisses nennen jedes geänderte Skript |

Siemens-Member. Kompiliert (in den Stubs): `HmiSoftware.Screens`/`ScreenGroups`/`Tags.Find`,
`HmiScreenComposition.Find`, `HmiScreenGroup.Screens`/`Groups`, `HmiScreen.ScreenItems`, `HmiScreenItemBase.Name`,
`IEngineeringObject.GetAttribute`/`SetAttribute`, `MultilingualText.Items`. Zur Laufzeit über den exakten Namen und die
Argumenttypen gebunden (`LibraryRuntimeBinding`), `api_member_missing`, bevor sich etwas ändert: `HmiScreenComposition.Create(String)`,
`HmiScreenItemBaseComposition.Create<T>(String)`, `Delete()` von Bildern und Objekten, die Eigenschaften `Dynamizations`,
`EventHandlers` und `PropertyEventHandlers`, `Dynamizations.Create<TagDynamization>(String)`, das `Script` des Handlers
und `ScriptCode`/`GlobalDefinitionAreaScriptCode` des Skripts, `Color.FromArgb(Int32)`. Der Nachweis für alle
ist BetterTIA/writer (dort per Reflection über die V21-Assemblies geprüft, nicht gegen TIA Portal ausgeführt); die
Siemens-Skills dokumentieren nichts von der Unified-Bild-API. Im Einklang mit V21-Fallstrick 3 zählt der HMI-Worker Attribute nie
generisch auf, daher wird die Beschreibbarkeit von Attributen nicht vorab geprüft: TIA Portal entscheidet, und jeder Wert wird zurückgelesen.

Bereits abgedeckt, daher nicht hinzugefügt: `SetHmiTagPlcTags` des Writers (HMI-Variable → PLC-Variable) ist `hmi_write update_tag` mit
`plcTag`/`connection`; sein einzelnes `RenameHmiTags`/`RenameHmiScreen` ist der Batch mit einem Eintrag (oder `update_tag
newName`); `SetHmiItemAttribute` ist `set_screen_item_property`; `CompileHmi` ist das Übersetzen von TIA Portal (von
Openness auf der HMI-Software nicht angeboten, wie der Writer festgestellt hat).

Weggelassen: `CreateHmiScreenItems`-Batches aus JSON mit `events` (ein Ereignis-Handler, mit seinem Skript angelegt):
`EventHandlers.Create(enum)` pro Objekttyp und die Skriptzuweisung sind doppelt unverifiziert (der Writer führt
"whether an event script written through `IHmiScript.ScriptCode` compiles" als offen) — bestehende Skripte mit
`replace_in_scripts` bearbeiten; Dynamisierungen außer über Variablen; Faceplates, Grafikanzeigen und andere Objekttypen über das hinaus, was der
Aufrufer exakt nennt (jeder öffentliche Typ funktioniert, wenn er existiert); die Verwendungslisten des Writers (Skriptzeilen, die ein
umbenanntes Bild nennen) — `hmi_read` und `replace_in_scripts` verwenden; Bildkopien und Vorlagen (laut dem
Writer nicht in Openness V21).

## Variablentabellen und WinCC-Unified-Textlisten als Dateien (Ergänzungen des Eigentümers)

Eine nachfolgende Anfrage des Eigentümers (2026-11-01) verlangte Export und Import von WinCC-Unified-Textlisten und den Export
von PLC-Variablentabellen in eine Datei. Ein versionsspezifischer TIA MCP von Gangdolf Automation (ein MIT-Fork von heilingbrunner/tiaportal-mcp) soll
`ExportHmiTextLists`/`ImportHmiTextLists` und `ExportPlcTags` anbieten; sein Quellcode ist nicht veröffentlicht, und nichts wurde
von dieser Website heruntergeladen. Beide Funktionen unten sind nur aus den Siemens-Openness-Skills und den
Compile-Stubs implementiert; nichts ist von irgendeinem Dritten kopiert.

| Operation | Felder | Was sie tut |
| --- | --- | --- |
| `plc_write export_tag_table` | `tableName`, optional `folderPath`, `plcName`; `filePath`, optional `overwrite` | `PlcTagTable.Export(FileInfo, ExportOptions.None)` in eine private temporäre Datei, dann neben dem Ziel bereitgestellt, an ihren Platz verschoben und neu gehasht (`TechnologyObjectFiles.Place`, der Schreiber des Technologieobjekt-Exports); eine bestehende Datei wird abgelehnt, außer bei `overwrite:true` (Info-Guard `plc_export_overwrites_file`). Das Ergebnis trägt die Größe und den SHA-256 der Datei sowie die Anzahl der Variablen und Benutzerkonstanten der Tabelle. Der CSV-Export `export_tag_tables_csv` bleibt; dies ist die SimaticML-Datei, die TIA Portal wieder importieren kann. |
| `plc_write import_tag_table` | `filePath`, optional `folderPath`, `plcName`, `tableName`, `overwrite` | `PlcTagTableComposition.Import(FileInfo, ImportOptions)` einer privaten Kopie der Datei in den Ordner, innerhalb einer `ExclusiveAccess`-Transaktion (`PlcRoundTrip`), sodass eine fehlgeschlagene Nachbedingung sie zurückrollt; `overwrite:true` verwendet `ImportOptions.Override` (Info-Guard `plc_import_replaces_tag_table`). `tableName` nennt die Tabelle, die die Datei enthält: dann darf sie ohne `overwrite` noch nicht existieren (Planer und Worker), muss danach im Ordner sein und ist die verifizierte Tabelle; ohne ihn stammen die importierten Namen aus dem Rückgabewert des Imports, wenn dieser eine Tabelle oder Tabellen ist, sonst aus den neuen Namen des Ordners. Das Überschreiben einer Tabelle, die eine Variable mit `IsSafety=true` enthält, folgt der Safety-Anmelderegel (wie `move_tag_table`). Verifiziert durch erneutes Lesen der Variablentabellen (Prüfung `tagTable`: jede importierte Tabelle mit den gemeldeten Anzahlen von Variablen und Benutzerkonstanten). Spätere Elemente des Aufrufs dürfen einen importierten `tableName` verwenden. |
| `hmi_write export_text_lists` | `directoryPath` (bestehend), optional `textListNames` (1-200), `overwrite` | eine `<name>.xml` pro WinCC-Unified-Benutzertextliste von `HmiSoftware.HmiTextLists` (alle oder die genannten): `Export(FileInfo, ExportOptions.None)` auf jeder `HmiTextList` in eine temporäre Datei, platziert wie beim Variablentabellen-Export; ein Listenname, der kein gültiger Dateiname ist, wird abgelehnt; jedes Ziel wird vor dem ersten Export geprüft. `appliedProperties` listet jede Datei mit ihrer Größe und ihrem SHA-256. |
| `hmi_write import_text_lists` | `filePath`, optional `overwrite` | `Import(FileInfo, ImportOptions)` auf `HmiSoftware.HmiTextLists` mit einer privaten Kopie (`ImportOptions.Override` bei `overwrite:true`); ohne `overwrite` muss eine neue Liste erscheinen (sonst `postcondition_failed`); `appliedProperties` nennt die neuen Listen. |

Siemens-Member. Variablentabellen: `PlcTagTable.Export(FileInfo, ExportOptions)` ist kompiliert (Stub) und zusammen mit
`TagTableCollection.Import(FileInfo, ImportOptions)` im Siemens-Skill `openness_development/skills/tags-and-tagtables` dokumentiert
("Export TagTable to XML and Re-Import"); `move_tag_table` verwendet bereits beide. Der Import wird zur Laufzeit über seine
Parameterliste gebunden (die Referenz nennt keinen Rückgabetyp), bevor sich etwas ändert. `ExportOptions.None` und
`ImportOptions.None`/`Override` sind Stub-Member.

WinCC-Unified-Textlisten: Kein Siemens-Dokument beschreibt einen Datei-Export oder -Import für Unified-Textlisten (die Skills in
`tia-portal-ai-extensions` erwähnen nur Textlisten von PLC-Meldungen, und der Stub `HmiTextList` hat nur `Name`). Die beiden
Member sind eine Hypothese nach dem SimaticML-Muster der dokumentierten Kompositionen und werden zur Laufzeit über den
exakten Namen und die Argumenttypen an den Live-Typen gebunden, bevor die erste Datei geschrieben oder irgendetwas importiert wird; eine
Installation ohne sie schlägt mit `api_member_missing` fehl, ohne dass etwas geschrieben oder geändert wurde (Info-Guard
`hmi_text_list_file_unverified` bei jedem solchen Element). System-Textlisten und Grafiklisten werden nie angefasst (V21-Fallstrick
3, siehe [V21-Fallstricke](V21_PITFALLS.md#pitfall-3-graphic-lists-and-scriptdiagnosisoverviewtext)); `HmiPitfallSourceTests`
legt fest, dass `HmiTextListFiles` die einzige HMI-Worker-Datei ist, die einen Export bindet. Wie bei jedem `hmi_write`-Element ist die
Verifikation `trusted`/`superseded`; die Listen mit `hmi_read list_text_and_graphic_lists` prüfen.

Geprüft und bereits abgedeckt: `ExportPlcTags` entspricht `plc_write export_tag_tables_csv` (CSV) und jetzt
`export_tag_table` (SimaticML). Weggelassen: ein XLSX-Textlisten-Export für Unified (kein dokumentierter Provider); der XLSX-Export/Import
von PLC-Textlisten (`plc_write export_text_lists`/`import_text_lists`) existiert bereits für PLC-Textlisten.

## Insgesamt weggelassen

- **`watch_screen`** (`ShowInEditor()`, Strg+T "Monitor all", Aufnahme mit `PrintWindow` als MCP-Bildinhalt): nicht
  übernommen. Es passt nicht sicher zum Worker-Modell: Der Worker ist ein kopfloser stdio-Prozess, der ein TIA Portal bedienen kann,
  das ohne Benutzeroberfläche gestartet wurde (kein Hauptfenster), das Tastenkürzel braucht TIA Portal als Vordergrundfenster
  (Fokus-Diebstahl, Tastendrücke an das Fenster, das gerade vorne ist, wenn die Aktivierung fehlschlägt, ein Umschalter, den BetterTIA nur über
  MSAA lesen kann), Strg+T in einem Offline-Editor bringt TIA Portal dazu, seinen eigenen Dialog "Go online" zu öffnen und sich über die zuletzt verwendete
  Schnittstelle zu verbinden, und jedes strukturierte Tool liefert ein kanonisches JSON-Dokument, ohne Bildinhalt im Vertrag. Eine
  Aufnahme nur des Fensters (ohne Tasten) würde TIA Portal trotzdem in den Vordergrund holen und wurde nicht als einen neuen
  Inhaltstyp wert beurteilt. `ShowInEditor` bleibt weggelassen wie in der [Technologieobjekt-Referenz](TECHNOLOGY_OBJECTS_SUMMARY.de.md).
