# TIA-Portal-PLC-Operationen

**Sprache:** [English](PLC_OPERATIONS_SUMMARY.md) · Deutsch

## Unterstützte Leseoperationen

| Einstiegspunkt | Operation | Eingaben | Verhalten |
|---|---|---|---|
| `browse_project_tree` | `browse_project_tree` | Optional `depth`, `startSelector`, `pageSize`; Fortsetzungs-`cursor` | Findet PLC-Software, Bausteine, Gruppen, Typen und andere Projektobjekte; Cursor lehnen Bindungsänderungen ab. |
| `plc_read` | `get_block_content` | Erforderlich `blockPath`; optional `format`, `withDependencies` | Liest einen vorhandenen Baustein als XML/SimaticML (Standard), als zulässige externe Quelle oder als seine SIMATIC-SD-Dokumente (`format: "sd"`, siehe [PLC_SIMATIC_SD_SUMMARY.md](PLC_SIMATIC_SD_SUMMARY.de.md); nicht live getestet). Wenn TIA Portal kein SimaticML exportiert (inkonsistente Bausteine exportiert es nicht, z. B. einen, der seit seiner Erstellung aus einem SD-Dokument nicht übersetzt wurde; live bestätigt auf V21 am 2026-11-02), enthält das xml-Ergebnis die SD-Dokumente, die TIA Portal exportiert hat, und eine Warnung nennt die Ursache ("run compile_check and read again"; Defekt D15, vor dem das fehlende `.xml` stillschweigend fehlte). Siehe [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.de.md). |
| `plc_read` | `get_type_content` | Erforderlich `typePath`; optional `format`, `withDependencies` | Liest einen vorhandenen PLC-Typ als `.udt`-Quelle (Standard) oder XML/SimaticML. |
| `plc_read` | `list_tag_tables` | Optional `plcName`, `folderPath`, `tableName` | Listet PLC-Variablentabellen und die offengelegten Informationen zu Variablen und Konstanten auf. |
| `plc_read` | `read_address_occupancy` | Optional `plcName`, `areas`, `includeHardware` | Belegungsliste einer PLC: deklarierte I/Q/M-Variablen (Breite = die dokumentierte Länge des Datentyps, gegengeprüft mit der Schreibweise der Adresse) sowie die Hardware-E/A-Adressen der PLC, Überschneidungen von Variablen, I/Q-Variablen außerhalb der Hardware, Variablen ohne absolute Adresse und Variablen ohne Größe (Datentyp ohne dokumentierte Länge; sie machen das Ergebnis unvollständig). Siehe [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#plc_read-operations). |
| `plc_read` | `find_free_address` | Erforderlich `area`, `dataType`; optional `plcName`, `startByte`, `endByte`, `alignBytes`, `requireHardware` | Niedrigste Adresse des Bereichs, die keine deklarierte Variable nutzt (optional innerhalb eines Hardwarebereichs); `startByte`/`endByte` 0–65535; unvollständig, wenn eine Variable ohne Größe im Bereich liegt. Siehe [HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#plc_read-operations). |
| `plc_read` | `get_block_interface` | Erforderlich `blockPath` | Bausteinidentität (kanonischer Pfad, Typ, Nummer, Sprache, Know-how-Schutz, Instanz-von-FB), der xml-`contentHash` und die Schnittstelle als typisierter Member-Baum. Siehe [Granulare Bausteinbearbeitung](#granulare-bausteinbearbeitung). |
| `plc_read` | `get_type_interface` | Erforderlich `typePath` | Dasselbe für einen PLC-Datentyp (Abschnitt `None`). |
| `plc_read` | `get_db_values` | Erforderlich `blockPath`; optional `memberPath`, `withAttributes` | Startwerte eines Global- oder Instanz-DB in jeder Tiefe, gelesen mit `Interface.Members.Find`. |
| `plc_read` | `get_call_tree`, `find_call_paths`, `find_unused_blocks`, `find_orphaned_instance_dbs`, `analyze_change_impact` | Siehe [Programmanalyse](#programmanalyse-plc_read) | Im Host berechnete, nur lesende Analyse über das Bausteininventar und den Querverweisleser; nicht live getestet. |
| `plc_read` | `list_external_sources`, `read_tag_comments`, `find_objects`, `scan_accessible_devices` | Siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.de.md) | Externe Quellen der PLC und ihrer Software Units; mehrsprachige Kommentare von Variablen und Anwenderkonstanten; Objekte per Glob mit Seitenaufteilung; erreichbare Geräte an einer ausdrücklich benannten PC-Schnittstelle (`OnlineObserve`, jeder Modus); nicht live getestet. |
| `plc_read` | `list_text_lists`, `read_text_list_workbook`, `read_alarm_text_workbook`, `read_supervision_workbook`, `get_prodiag_fb`, `get_prodiag_assignment` | Siehe die [Referenz PLC-Meldungen und Textlisten](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md) | Anwender- und System-Textlisten mit ID, Bereich und Kommentar je Kultur; Textlisteneinträge, Instanztexte von Meldungen und ProDiag-Überwachungen als uninterpretierte Zellen des XLSX-Exports von TIA Portal mit einem `workbookHash`, seitenweise innerhalb des Wertbudgets; ProDiag-FB-Nachweise und `AssignedProDiagFB`; die passenden Schreibzugriffe (`update_text_list`, `delete_text_list`, `update_*_cells`, `export_*`, `import_*`, `set_prodiag_assignment`, `export_prodiag_info`) stehen in derselben Referenz; nicht live getestet. |
| `plc_read` | `get_plc_checksums`, `get_fingerprints` | Optional `plcName`; genau eines von `blockPath` oder `typePath` | Offline-Änderungserkennung: die Prüfsummen von Software und Textlisten der PLC und die Fingerprints eines Bausteins oder PLC-Datentyps, als Text; siehe [Offline-Änderungserkennung](#offline-änderungserkennung-plc_read); nicht live getestet. |
| `plc_read` | `get_block_protection` | Pflicht `blockPath` | Know-how- und Schreibschutzzustand eines Bausteins und welche Schutz-Member binden; siehe [Know-how-Schutz und Schreibschutz von Bausteinen](#know-how-schutz-und-schreibschutz-von-bausteinen-plc_readplc_write). Nicht live getestet. |
| `plc_read` | `get_project_languages` | Keine | Aktive, Bearbeitungs-, Referenz- und unterstützte Projektsprachen und die Textkategorien; siehe [Projektsprachen und Projekttexte](#projektsprachen-und-projekttexte-plc_readplc_write). Nicht live getestet. |
| `plc_read` | `describe_operations` | Optional `operationNames` (1–3 Namen von PLC-Lese- oder Schreiboperationen) | Die Operationsreferenz: Pflicht- und optionale Felder, die vollständigen Feldregeln und der Referenztext jeder genannten Operation, ohne Namen der Index aller Operationen mit ihren Feldern; kein gebundenes Projekt nötig. Siehe [Operationsreferenz](#operationsreferenz-describe_operations). |
| `read_cross_references` | `read_cross_references` | Erforderlich `target`; optional `filter`, `maxResults` | Liest die Querverweise eines Projektbaumziels. Filter sind `AllObjects`, `ObjectsWithReferences` (Standard), `ObjectsWithoutReferences` und `UnusedObjects`. |
| `compile_check` | `compile_check` | Optional `plcName`, `blockPath` | Übersetzt die PLC oder den ausgewählten Bausteinbereich und gibt Compilermeldungen zurück. |

`plc_read` und `read_cross_references` sind in jedem Zugriffsmodus registriert. `execute_read_batch` wurde
außer Dienst gestellt; stattdessen diese beiden Tools verwenden. Baumdurchsuchung und Übersetzen sind eigenständige Tools. `compile_check`
und `plc_write` sind in den Modi read-write und full verfügbar und verhalten sich in beiden gleich. PLC-Run/Stop
(`start_plc`/`stop_plc`) wurde entfernt; kein Modus steuert eine PLC.

`bind_project` dient in jedem Modus dazu, ein bereits geöffnetes Projekt zu übernehmen oder zu ihm zu wechseln; eine andere Bindung
erfordert `forceRebind:true`. Gewöhnliche PLC-Lesezugriffe binden, wechseln oder öffnen nie. Nur `open_project` und
`create_project` öffnen Projekte. Der Lebenszyklus läuft in read-write mit einem Bestätigungsformular pro tatsächlichem
Aufruf oder in full ohne Server-Elicitation; Block-Guards beenden den Aufruf in jedem Modus. Die vollständigen
Tool-Zahlen sind 13/33/33. Read-only öffnet, erstellt, speichert oder schließt nie ein Projekt. Das Laden einer PLC in
PLCSIM oder reale Hardware ist das separate Tool `plc_download`; siehe die [PLC-Download-Referenz](PLC_DOWNLOAD_SUMMARY.de.md).
Online/offline gehen und Station-Upload sind die separaten Tools `plc_tia_online` und `plc_station_upload`; siehe die [Referenz PLC online über Openness](PLC_ONLINE_OPENNESS_SUMMARY.de.md).
Simulierte CPUs werden von den `plcsim_*`-Tools gesteuert; siehe die [Referenz der PLCSIM-Operationen](PLCSIM_OPERATIONS_SUMMARY.de.md).

## PLC-Lesezugriffe (`plc_read`)

`plc_read(operations)` nimmt höchstens 50 Einträge. Jeder hat eine eindeutige, nicht leere `operationId` von höchstens
256 Zeichen, eine `operation` und die Parameter dieser Operation. Einträge laufen
in der Reihenfolge des Aufrufers und unabhängig voneinander: ein fehlgeschlagener Eintrag hält die anderen nicht an. Unbekannte Felder werden
abgelehnt. Die Antwort ist ein strukturiertes Dokument (`contractVersion` `1.0`, Wurzel-Array `warnings`,
explizite Nullwerte), das identisch als Text und als `structuredContent` geliefert wird. `isError` ist nur bei einer
Ablehnung wahr, bevor irgendetwas lief.

- `get_block_content` und `get_type_content` geben `{ format, content, contentHash, warnings }` zurück.
  `format` ist `xml` oder `source` (Bausteine auch `sd`, mit einem bedingten `failSafe: true` für einen `S7_Safety`-Header); Bausteine sind standardmäßig `xml` und Typen `source`. `withDependencies`
  fügt einem `source`-Export die Abhängigkeitshülle hinzu; dieses Dokument dient nur als Kontext und kann nicht
  zurückgeschrieben werden.
- `contentHash` ist `xml:sha256:<hex>`, `source:sha256:<hex>` oder `sd:sha256:<hex>` über den exakt gelieferten Text. Es ist ein
  explizites `null`, wenn `withDependencies` wahr ist.
- `list_tag_tables` gibt `{ isComplete, plcs[] }` zurück; jede PLC nennt ihre Software und ihr Gerät und
  trägt ihre `tables[]`. Ein weggelassenes `plcName` liest jede PLC, einschließlich PLCs in Gerätegruppen.
  Variablen tragen `externalAccessible`, `externalVisible` und `externalWritable` (`null`, wenn nicht lesbar);
  Anwenderkonstanten tragen eine Lesbarkeitsmarkierung; alles, was nicht gelesen werden konnte, setzt `isComplete:false`
  mit einer Meldung (die ersten 50 Meldungen werden behalten, dann ein zusammenfassender Eintrag). `tableName` behält eine Tabelle
  und `folderPath` die Tabellen direkt in einem Ordner (so geschrieben, wie das Inventar ihn ausgibt, `/` oder
  `/Line/Cell`); beide unterscheiden nicht zwischen Groß- und Kleinschreibung und lassen sich mit `plcName` kombinieren. Eine Einschränkung, die keine
  Tabelle trifft, lässt den Eintrag mit `target_not_found` fehlschlagen, es sei denn, ein Teil des Baums war nicht lesbar.
- `list_download_targets` (optional `plcName`; weggelassen liest jede PLC) gibt
  `{ plcs[{ plcName, deviceName, downloadable, modes[{ name, pcInterfaces[{ name, number,
  targetInterfaces[{ name }] }] }] }] }` zurück: den Offline-Downloadpfad, gelesen aus
  `DownloadProvider.Configuration`, nie durch Online-Gehen oder Scannen des Netzwerks. Nur eigenständige
  Projekte. Es speist `plc_download`; siehe die [PLC-Download-Referenz](PLC_DOWNLOAD_SUMMARY.de.md).
- `compare_to_online`, `compare_software`, `compare_hardware`, `compare_hardware_to_online`, `get_online_state`, `list_station_upload_interfaces`,
  `list_watch_and_force_tables`, `read_watch_table`, `read_force_table`, `get_watch_table_content` und
  `get_force_table_content` sowie die `plc_write`-Operationen für Beobachtungs-/Forcetabellen und `update_program` sind
  in der [Referenz PLC online über Openness](PLC_ONLINE_OPENNESS_SUMMARY.de.md) dokumentiert (nicht live getestet).
- Inhaltstools arbeiten nur auf dem gebundenen Projekt; ohne verifizierte Bindung (ein konfiguriertes `--project` wird zuerst über den Status verankert) scheitern sie nach dem Fail-closed-Prinzip mit `binding_conflict`; zuerst `bind_project` aufrufen.
  Ein ungebundener Aufruf gibt einen Wurzelfehler zurück, nie Fehler pro Eintrag. Lesezugriffe binden, wechseln oder öffnen nie ein Projekt.

Budget: ein Wert über dem Wertbudget oder ein Dokument über dem Antwortbudget (standardmäßig 60.000 und 180.000
Zeichen) wird nie abgeschnitten. Das `result` des betroffenen Eintrags wird als Ganzes weggelassen, mit einem Auslassungsdatensatz
und Hinweisen zur Einschränkung, und hat daher keinen Hash. Ein einzelner Baustein- oder Typexport über dem Wertbudget
kann deshalb nicht über `plc_read` gelesen und nicht über `plc_write` aktualisiert werden, das
den Hash benötigt, bis der Server mit `--max-item-chars` mindestens gleich dem `originalChars` der Auslassung
und `--max-document-chars` darüber mit Reserve neu gestartet wird; einen solchen Baustein in einem eigenen
Aufruf lesen. Siehe [Grenzen der Antwortgröße](../guides/mcp-client-configuration.md#response-size-limits). Eine PLC
mit rund 400 Variablen überschreitet bereits das Standard-Wertbudget: `list_tag_tables` mit
`plcName`, `folderPath` oder `tableName` einschränken.

## Operationsreferenz (`describe_operations`)

Nicht live getestet (liest nichts aus TIA Portal). Claude Desktop verweigerte `plc_read` und `plc_write`, solange ihre
Werkzeugliste den vollständigen Text je Operation trug („Claude can't use 2 tools from Tia portal because of a problem
with that connector“, Defekt K91): die Eingabeschemata hatten 77–79 KB, die Beschreibungen 20.590 und 49.996 Zeichen.
Die Werkzeugbeschreibungen tragen jetzt eine Zusammenfassung, jeden Operationsnamen und bei `plc_write` jeden
Acknowledge- und Block-Guard und die Regel zur Sicherheitsabnahme; das Eintragsschema behält jedes Feld, jeden Typ,
jedes Enum und jede Schachtelungsebene, kürzt aber jede Feldbeschreibung auf ihren ersten Satz (keine, wenn er länger als
160 Zeichen ist). Jedes Werkzeug bleibt bei höchstens 8.000 Zeichen Beschreibung und 30 KB Eingabeschema, ohne tiefere
Schachtelung als vorher.

Der vollständige Text liegt jetzt bei `plc_read` `describe_operations` (`Observe`, jeder Zugriffsmodus; ein Aufruf nur
aus `describe_operations`-Einträgen braucht kein gebundenes Projekt):

- `operationNames` (1–3 Namen je Eintrag, mehrere Einträge je Aufruf): je Operation `tool`, `requiredFields`,
  `optionalFields`, `fields` (die Beschreibung jedes Felds für diese Operation: Teile einer gemeinsamen Feldbeschreibung,
  die nur andere Operationen nennen, fallen weg und `trimmed` ist true; ein Teil, der von einer Gruppe von Operationen
  spricht, etwa „every technology object operation except ...“, oder eine Kurzform wie `add/update/delete_interface_member`,
  die die Operation umfasst, bleibt), `reference` (jeder Abschnitt der bisherigen Werkzeugbeschreibung, der die Operation
  nennt) und `sectionNotes` (Abschnitte, die keine Operation nennen und zu ihr gehören: ein Satzteil nach „; “, der einen
  ihrer Abschnitte fortsetzt, oder ein Satz eines Absatzes, der sie nennt; K93: eine Notiz hängt nicht mehr an jeder
  Operation eines langen Absatzes).
- ohne `operationNames`: `index`, jede PLC-Lese- und Schreiboperation mit ihren Feldern als ein Text
  (`"blockPath; optional: format, withDependencies"`).
- immer: `readToolRules` und `writeToolRules` (die werkzeugweiten Regeln) und `referenceDocument`, diese Seite.

Unbekannte, doppelte oder leere Namen und mehr als 3 Namen sind ein `validation_error`; `operationNames` wird bei jeder
anderen Operation abgelehnt. Die Validierung aller anderen Operationen ist unverändert: sie liest die Schemabeschreibungen nie.

## Programmanalyse (`plc_read`)

> **Nicht live getestet.** Offline gebaut und qualifiziert gegen Fixture-Daten, die wie die Ausgabe des
> `read_cross_references`-Lesers geformt sind. Kein aufgezeichneter TIA-Portal-Lauf zeigt einen Bausteinaufrufgraphen über
> den Querverweisdienst, daher ist die Richtungsregel unten erschlossen, für Bausteine nicht beobachtet. Vor dem Verlassen auf die Ergebnisse
> die [Schritte der Live-Abnahme](#schritte-der-live-abnahme-der-programmanalyse) ausführen.

Fünf `plc_read`-Operationen analysieren das Programm einer PLC. Sie werden im Host ausschließlich aus vorhandenen
Lesezugriffen berechnet, benötigen kein Siemens-Member, das das Repository nicht bereits aufruft, und ändern nichts
(`Observe`, jeder Zugriffsmodus; es gelten die Bindungsregeln jedes Inhaltslesezugriffs):

| Operation | Eingaben | Ergebnis |
|---|---|---|
| `get_call_tree` | `blockPath` eines OB, FB oder FC (ein Datenbaustein ist `target_kind_unsupported`); optional `maxDepth` (1-50, Standard 10), `maxElements` (Knoten, 1-2000, Standard 100), `timeoutSeconds` | `nodes[{ id, parentId, depth, block, evidence, instances, cycle, expandedElsewhere, childrenOmitted }]` in Pre-Order der Aufruf- und Multiinstanzbeziehungen ab der Wurzel, die Instanz-DBs, die jeder Knoten nutzt (`instances`), aufgerufene Elemente außerhalb des Inventars (Anweisungen wie `TON`) als Blattknoten mit `inInventory:false` und `undirectedCallEvidence`. Ein aufgerufenes Element, das bereits auf dem Pfad liegt, ist ein `cycle`; eines, das früher im Baum aufgeklappt wurde, ist `expandedElsewhere`; keines von beiden wird erneut aufgeklappt. |
| `find_call_paths` | `blockPath` (beliebiger Baustein); optional `maxDepth` (Sprünge, 1-50, Standard 20), `maxElements` (Pfade, 1-1000, Standard 50), `timeoutSeconds` | Jeder einfache Pfad von einem OB zum Baustein: `paths[{ blocks[], relations[] }]` (`relations[i]` verbindet die Beziehungen von Sprung i mit `+`), die erreichten OBs (`roots`), `directCallers` und die ungerichteten Nachweise des Ziels. Innere Sprünge sind Aufruf- oder Multiinstanzbeziehungen; für einen Datenbaustein (DB oder Typ) darf der letzte Sprung jede gerichtete Beziehung sein. Eine Pfadsuche stoppt außerdem nach 200.000 Schritten (`pathSteps`). |
| `find_unused_blocks` | optional `plcName`, `maxElements` (aufgeführte Bausteine, 1-5000, Standard 500), `timeoutSeconds` | Bausteine, die kein OB über irgendeine gerichtete Beziehung erreicht: `unused[]` mit Kategorie `unreferenced` oder `referencedOnlyByUnreachableBlocks` (mit `referencedBy`), `usedOnlyOutsideProgram[]` (kein erreichbarer Baustein, aber Objekte außerhalb des Inventars wie HMI-Variablen nutzen ihn) und `undetermined[]` (teilt ungerichtete Nachweise mit einem erreichbaren Baustein oder wird nur über einen unbestimmten Baustein erreicht: über eine gerichtete Beziehung oder weitere ungerichtete Nachweise von einem solchen); `roots`, Zählwerte. Jeder unbestimmte Baustein macht das Ergebnis `complete:false` mit einer Zeile in `evidence.limits` (die Auflistung entscheidet über diese Bausteine nicht). Ein unvollständiges Ergebnis trägt eine Warnung, dass es kein Beweis dafür ist, dass Bausteine gelöscht werden können. |
| `find_orphaned_instance_dbs` | optional `plcName`, `maxElements` (Instanz-DB-Exporte, 1-1000, Standard 200), `timeoutSeconds` | Liest für jeden Instanz-DB seinen SimaticML-Export (`get_block_content` `xml`) und nimmt `AttributeList/InstanceOfName` von `SW.Blocks.InstanceDB`. `orphans[]`: `instanceOfUnreachable` (der FB existiert, aber kein OB erreicht ihn), mit `dbReachable`. `unresolvedInstanceOf[]` (mit `unresolvedInstanceOfCount`, höchstens 200 aufgeführt; keine Waisen und nicht in `orphanCount` gezählt): `instanceOfNotInInventory` (weder ein Baustein noch ein PLC-Datentyp der PLC: ein System-FB/SFB oder Systemdatentyp wie ein IEC-Timer-Typ, den das Inventar nicht aufführt, oder ein gelöschter FB; die Analyse kann nicht unterscheiden, welcher). `notChecked[]`: `notAddressable` (Systembausteinordner), `exportFailed`, `noInstanceOfName`, `notAnInstanceDbDocument`, `instanceOfAmbiguous` (der Name passt auf mehrere Bausteine oder PLC-Datentypen, z. B. über Software Units hinweg; nie aufgelöst, trägt `instanceOfName`), `maxElements`, `timeoutSeconds`. `checkedCount` zählt verwaiste, nicht aufgelöste und aufgelöste DBs; jeder `notChecked`-Eintrag macht das Ergebnis unvollständig. |
| `analyze_change_impact` | genau ein Ziel: `blockPath`, `typePath` oder `tableName` + `name` (eine PLC-Variable; optional `plcName`, `folderPath`); optional `maxDepth` (1-50, Standard 10), `maxElements` (Nutzer, 1-5000, Standard 500), `timeoutSeconds` | `users[{ object, depth, via, relations, accesses }]`: Tiefe 1 sind direkte Nutzer, tiefere hängen von `via` ab; `affectedRoots` (die OBs darunter), `externalUsers` des Ziels (HMI- oder Objekte anderer Geräte) und seine ungerichteten Nachweise. Ein Typziel durchsucht zusätzlich die Typordner (Verwendung Typ-in-Typ und Typ-in-DB); ein Variablenziel liest die eigenen Querverweise der Variable. |

### Nachweise und Regeln

Jeder Analyseeintrag liest, einmal pro `plc_read`-Aufruf und gemeinsam für alle Analyseeinträge des Aufrufs:
einen Projektbaum-Snapshot (das Inventar aus Bausteinen, Typen und Variablentabellen, das `browse_project_tree` zeigt), einen
`read_cross_references`-Durchlauf (Filter `ObjectsWithReferences`, keine Obergrenze) pro Wurzel-Bausteinordner der
PLC und jeder Software Unit, und nur bei Bedarf die Typordner, die eigene Abfrage der Variable und die
Instanz-DB-Exporte. Das Ziel wird zuerst gegen das Inventar aufgelöst, sodass ein falscher Pfad keinen
Durchlauf kostet. Ein fehlgeschlagener Nachweis-Lesezugriff lässt den Eintrag mit seiner Kategorie fehlschlagen (Fail-closed, kein Teilgraph).
Jedes Ergebnis trägt `complete`, `truncation` (`maxDepth`, `maxElements`, `timeoutSeconds`,
`pathSteps`) und `evidence { crossReferenceQueries[], matchedSourceCount, unmatchedSourceCount,
ambiguousReferenceCount, directedEdgeCount, undirectedEvidenceCount, complete, rules[], limits[] }`.

Eine Beziehung wird nur genutzt, wenn eine Querverweisstelle sie angibt:

- **Richtung** kommt ausschließlich aus dem `referenceType` der Stelle: `UsedBy` bedeutet, dass das Referenzobjekt
  das abgefragte Objekt nutzt, `Uses` das Umgekehrte. Beide Seiten werden zusammengeführt (`evidence.reportedAs` führt `uses`
  und/oder `usedBy` auf). Jeder andere Referenztyp gibt keine Richtung an: das Paar wird als ungerichteter
  Nachweis gemeldet und nie durchlaufen. `UsedBy` für Nutzer ist die für HMI-Variablen aufgezeichnete Semantik (Spike vom 2026-10-13,
  Abnahme vom 2026-10-12), auf die sich die HMI-Schreibplanung stützt; `Uses` ist das V21-Gegenstück und
  hat keinen aufgezeichneten Lauf auf Bausteinebene.
- **Beziehung** kommt aus `access`: `Call`, `UC`, `CC` sind Aufrufe (`Call` wurde an einem System-FB aufgezeichnet,
  2026-10-05); `Multiinstance`/`MultiinstanceAndSymbol` eine Multiinstanzdeklaration (aufgezeichnet als
  `Multiinstance`; ob die deklarierte Instanz tatsächlich aufgerufen wird, ist nicht unterscheidbar);
  `InstanceDB`/`InstanceAndSymbol` eine Instanz-DB-Verwendung und `Parameterinstance(AndSymbol)` eine
  Parameterinstanz; alles andere eine einfache `reference`. Aufrufbäume und Pfade folgen `call` und `multiInstance`;
  Erreichbarkeit und Auswirkung folgen jeder gerichteten Beziehung.
- **Identität**: Quellen werden per exaktem Namen (ohne Unterscheidung von Groß-/Kleinschreibung) gegen die Bausteine oder Typen des
  durchsuchten Ordners abgeglichen, Referenzen gegen das gesamte Inventar. Ein Name, der auf mehrere Objekte passt
  (doppelte Namen über Software Units oder Namespaces hinweg), wird nicht aufgelöst und macht die Nachweise
  unvollständig. Eine Referenz, deren `device` sich von dem des abgefragten Objekts unterscheidet, liegt außerhalb des Inventars
  (HMI-Objekte). Untergeordnete Quellen (DB-Member, Schnittstellen-Member) werden ihrer Quelle auf oberster Ebene zugeordnet.
- `complete` ist falsch, wenn der Baum Knoten übersprungen hat, eine Abfrage unvollständig war, eine Quelle auf kein oder
  mehrere Inventarobjekte passte, ein Name mehrdeutig war oder eine Obergrenze erreicht wurde; bei `find_unused_blocks` außerdem, wenn ein
  Baustein `undetermined` ist, und bei `find_orphaned_instance_dbs`, wenn ein Instanz-DB nicht geprüft wurde
  (einschließlich eines mehrdeutigen `InstanceOfName`).

Grenzen: indirekte Aufrufe und Zugriffe sind unsichtbar (Bausteinparameter `BLOCK_FB`/`BLOCK_FC`,
ANY/VARIANT/DB_ANY-Pointer, zur Laufzeit berechnete DB-Nummern, `OPN` mit einer Variablen, Anweisungen, die
Bausteine über ihre Nummer ansprechen), daher ist ein nicht erreichbarer Baustein ein Kandidat, kein Beweis; jeder OB ist eine Wurzel,
ob sein Ereignis eintritt oder nicht; ein FB, dessen Einzelinstanzaufrufe nur gegen seinen
Instanz-DB aufgezeichnet wurden (keine Stelle, die den FB nennt), würde als nicht erreichbar erscheinen, obwohl der aufgezeichnete Lauf vom 2026-10-05
`Call`-Stellen in der eigenen Abfrage eines FB zeigt; ob nicht übersetzte Änderungen in den
Querverweisen sichtbar sind, ist nicht verifiziert; dass jeder Instanz-DB-Export `InstanceOfName` trägt (das dokumentierte
V21-`InstanceDB`-Attribut "the block name of the father instance (FB/SFB/UDT/SDT)"), ist nicht verifiziert, und einer ohne es
ist `notChecked`, nie geraten. `timeoutSeconds` (1-600, Standard 60) begrenzt die Traversierung und den Start
weiterer Instanz-DB-Exporte; die Nachweis-Lesezugriffe selbst laufen immer vollständig, jeder begrenzt durch den
Request-Timeout des Workers. Ein Ergebnis über dem Wertbudget wird als Ganzes weggelassen, mit Hinweisen zur Einschränkung (niedrigeres
`maxElements` oder `maxDepth`); jedes Ergebnis trägt einen festen Nachweisabschnitt von etwa 3.000 Zeichen.
Die Durchlauf-Payload einer großen PLC ist zwischen Worker und Host nicht begrenzt.

### Schritte der Live-Abnahme der Programmanalyse

1. Auf einem Wegwerfprojekt mit bekannter Aufrufstruktur (OB1 ruft einen FB mit einem Instanz-DB und einer
   Multiinstanz auf, eine FC-Kette mit Rekursion, ein nicht erreichbarer FC, ein DB, der nur von einer HMI-Variable genutzt wird, ein
   AWL-Aufruf `UC`/`CC`, ein Aufruf über einen `BLOCK_FC`-Parameter) `read_cross_references` auf dem Wurzel-Bausteinordner
   ausführen und aufzeichnen, welche Stellen `Uses`, `UsedBy` und andere Referenztypen tragen, und ihre
   Zugriffsnamen.
2. `get_call_tree` von OB1 und `find_call_paths` des FB mit der Ansicht der Aufrufstruktur in TIA Portal
   vergleichen; `instances` gegen die Instanz-DBs prüfen und dass der `BLOCK_FC`-Aufruf fehlt.
3. `find_unused_blocks` mit dem Querverweisfilter "unused objects" von TIA Portal vergleichen, und
   `find_orphaned_instance_dbs` nach dem Löschen eines FB, der noch einen Instanz-DB hat (aufgeführt in
   `unresolvedInstanceOf`, wie der Instanz-DB eines IEC-Timers); bestätigen, dass der Instanz-DB-Export
   `InstanceOfName` trägt.
4. `analyze_change_impact` für einen Baustein, einen PLC-Datentyp, der in einem anderen verschachtelt ist und von einem DB genutzt wird, und eine Variable,
   die von einem Baustein und einem HMI-Bild genutzt wird.
5. Auf einem Projekt mit Software Units wiederholen, einschließlich eines Namens, der in zwei Units existiert.

## Compilerdiagnose (`compile_check`)

Das Übersetzen kann den In-Memory-Projektzustand von TIA Portal ändern. Ein zurückgegebener Bericht ist kein Nachweis für
Speichern, Laden oder PLC-/Anlagenabnahme. `compile_check` findet PLCs auf der Projektwurzel und in
Gerätegruppen. Ohne `plcName` übersetzt es jede PLC; es übersetzt nichts, wenn ein Geräteelement nicht
gelesen werden kann, weil dieses Element eine PLC verbergen könnte. Ein `blockPath` ohne PLC-Qualifizierer erfordert eine einzige PLC
im Projekt und schlägt sonst mit `target_ambiguous` fehl.

Jedes PLC-Ergebnis nutzt `plcName` für den tatsächlichen Namen der PLC-Software und `deviceName` für das enthaltende
Hardwaregerät. Der Eingabeselektor `plcName` akzeptiert weiterhin beide Namen. Dieselben Identitäten
werden für das Übersetzen der PLC und ausgewählter Bausteine zurückgegeben, einschließlich erwarteter Übersetzungsfehler.

Verschachtelte Compilermeldungen werden in deterministischer Reihenfolge Eltern-vor-Kind abgeflacht. Jede Zeile nutzt
die eigene Beschreibung, den eigenen Pfad und den eigenen Schweregrad dieser Meldung. Die von Siemens gemeldeten Werte `state`, `errorCount` und
`warningCount` bleiben erhalten, wann immer ein Übersetzungsergebnis verfügbar ist; sie werden nicht aus
den angezeigten Zeilen neu berechnet und bleiben bei behandelten Zugriffsfehlern auf die Sammlung der Wurzeldiagnosen verfügbar.

Ein Projektionsbudget wird über alle ausgewählten PLCs hinweg geteilt:

- Maximale Meldungstiefe: 16, wobei Wurzeln als Tiefe 1 zählen.
- Maximale Anzahl projizierter Meldungen: 200.
- Maximale Länge jeder Beschreibung und jedes Pfads: 1.024 UTF-16-Codeeinheiten.
- Maximaler kombinierter Beschreibungs-/Pfadtext: 32.000 UTF-16-Codeeinheiten.

Der serialisierte Übersetzungsbericht wird zusätzlich unter 60.000 Zeichen gehalten, einschließlich
JSON-Escaping. Nachfolgende Meldungszeilen können entfernt werden, um diese Grenze einzuhalten, ohne gemeldete Summen
oder Identitäten zu ändern. Jede betroffene PLC erhält einen bereinigten Auslassungshinweis in `diagnosticNotes`, wenn
Diagnosen gekürzt, weggelassen oder auf behebbare Weise nicht lesbar sind. Beim Zugriff auf verschachtelte Diagnosen sind
behebbare Fehler erwartete Zugriffsfehler vom exakten Typ `InvalidOperationException`, keine abgeleiteten
Ausnahmen. Infrastruktur- oder Unterbrechungsfehler, darunter Sitzungsverlust, E/A, Abbruch,
Formatfehler, abgeleitete Invalid-Operation-Ausnahmen und andere unerwartete Fehler, lassen die Operation fehlschlagen,
statt einen typisierten Teilerfolg zurückzugeben. Wenn schon die Metadaten die Antwortgrenze überschreiten, schlägt die
Operation mit begrenzten Hinweisen fehl: das Übersetzen kann gelaufen sein, daher vor der Entscheidung über eine Wiederholung den aktuellen
In-Memory-Projektzustand prüfen.

Wenn ein erwarteter Fehler beim Aufruf des Compilers kein Übersetzungsergebnis hinterlässt, behält das typisierte PLC-Ergebnis
beide Identitäten, meldet `state: "Error"` und enthält einen bereinigten Hinweis auf nicht verfügbare Details.
Seine Nullzählwerte sind Ersatzwerte, kein Nachweis für ein fehlerfreies Übersetzen.

## Querverweisabdeckung (`read_cross_references`)

`read_cross_references(target, filter?, maxResults?)` liest ein Ziel pro Aufruf.
`target` ist `{ path: [{ nodeType, name }, ...], member?: { kind, name } }`: die `path`-Segmente
aus der Ausgabe von `browse_project_tree` kopieren (der Abgleich unterscheidet wie im Baum nicht zwischen Groß- und Kleinschreibung; Geschwister, die sich nur
in der Schreibweise unterscheiden, sind `target_ambiguous`). `member` adressiert Blätter, die der Baum nicht modelliert, unter einem
`TagTable`-Pfad, mit `kind` als einem von `Tag`, `SystemConstant` oder `UserConstant` (exakte Schreibweise). `filter`
und `maxResults` (1 oder größer) werden vor jedem Worker-Aufruf validiert.

Vorläufig darf `target` stattdessen `{ hmi: { hmiName, tagName } }` für eine WinCC-Unified-HMI-Variable auf oberster
Ebene sein; das kann durch einen einheitlichen HMI-Selektor ersetzt werden, sobald Meldungen oder Bilder
Querverweise benötigen. `hmiName` ist erforderlich (Software- oder Gerätename, ohne Unterscheidung von Groß-/Kleinschreibung) und `tagName`
wird ohne Unterscheidung von Groß-/Kleinschreibung abgeglichen. Ein gepunkteter UDT-Membername (`Motor_1.Speed`) schlägt mit `target_not_found` fehl, mit
dem Hinweis, nur die Variable auf oberster Ebene zu nennen; ein mehrdeutiges HMI ist `target_ambiguous` und ein
Classic-HMI `target_kind_unsupported`. Ein Ziel trägt entweder einen nicht leeren `path` (mit optionalem `member`) oder
`hmi`: beides, keines von beiden (einschließlich eines leeren `path` ohne `hmi`) oder ein leeres `hmiName`/`tagName` ist
`validation_error`. Filter, `maxResults`, Budgets und die Ergebnisform sind unverändert, und das
Echo des Ziels wiederholt den `hmi`-Selektor.

- Ein Pfad, der an einem Blatt endet (ein OB-, FB-, FC-, GlobalDB-, InstanceDB- oder ArrayDB-Baustein, ein Systembaustein oder ein
  PLC-Typ), oder jedes `member` ist eine Abfrage auf dieses Objekt.
- Ein Pfad, der an einem Container endet (`Device`, `PlcSoftware`, `SoftwareUnit`, ein Baustein-, Systembaustein-,
  Variablentabellen- oder Typordner oder `TagTable`), durchsucht jeden Besitzer darunter. Ein Container ohne Besitzer
  ist ein leerer, vollständiger Erfolg; wenn Besitzer existieren und keine ihrer Abfragen erfolgreich ist, schlägt der Aufruf
  mit `worker_operation_failed` fehl.
- Null Treffer ist `target_not_found`; ein Blatt ohne den Querverweisdienst ist
  `target_kind_unsupported`, was sich von "keine Referenzen" unterscheidet.

Das Ergebnis ist ein typisiertes Dokument: das Echo des Ziels, `filter`, Vollständigkeitsfelder (`isComplete`,
`ownerQueryCount`, `successfulOwnerQueryCount`), Summen und die rekursive Hierarchie
`sources[] -> references[] -> locations[]`. `referencedAs` ist `{ name, typeName }` des
referenzierten Objekts, und `access` und `referenceType` sind V21-Membernamen. Überlappende Ergebnisse
verschiedener Besitzer werden nicht dedupliziert. Ein vollständiger erfolgreicher Durchlauf kann null Quellen haben.

Nicht verfügbare Dienste von Besitzern, übersprungene Traversierungen und eine Kürzung durch `maxResults` behalten die verfügbaren Daten und setzen
`isComplete:false` mit Meldungen. Ein unvollständiges `UnusedObjects`-Ergebnis ist kein maßgebliches
Audit unbenutzter Objekte und darf nicht als Beweis dafür verwendet werden, dass Objekte gefahrlos gelöscht werden können.

Budget: Quellen werden als Ganzes vom Ende her verworfen, bis der Berichtswert in 60.000 Zeichen passt und
das Dokument in 180.000. Der Bericht trägt dann `isComplete:false`, `omittedSourceCount` und Hinweise zur
Einschränkung (`filter`, `maxResults`, ein engerer `path` oder ein `member`). Wenn nicht einmal eine ganze Quelle
passt, wird die erste Quelle behalten und im Inneren gekürzt (untergeordnete Quellen, dann Referenzen, dann Stellen),
mit einer Meldung, die zählt, was weggelassen wurde; das weggelassene Ende kann nicht seitenweise abgerufen werden.

### Nicht verifizierte Besitzerarten

Nur Arten, deren Besitzer live verifiziert wurden (2026-10-05, V21), werden als Member oder Endpunkte ausgeliefert.
WinCC-Unified-`HmiTag` wird über den vorläufigen `hmi`-Selektor oben ausgeliefert (live 2026-10-12, siehe den
[Abschlussbericht](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md)).
Nicht ausgeliefert und für dieses Tool nicht verifiziert: Technologieobjekte (der Querverweisdienst funktioniert,
aber der Projektbaum hat keinen Knotentyp für sie), `DeviceItem`,
`Subnet` und `Node` (kein Dienst beobachtet), Instanz-DBs von Technologieobjekten und WinCC-Classic-HMI-Variablen
(im Spike-Projekt nicht vorhanden). `Device`, `PlcSoftware`, `SoftwareUnit` und `TagTable` sind selbst keine
Besitzer; sie zählen nur die Besitzer unter sich auf. Siehe Anhang B der
[Design-Spezifikation](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-05-plc-read-write-and-cross-references-design.md).

## PLC-Schreibzugriffe (`plc_write`)

`plc_write(operations, dryRun=false)` führt bis zu 50 PLC-Software-Schreibzugriffe in der Reihenfolge des Aufrufers gegen ein
verifiziertes, bereits geöffnetes Projekt aus. Ein weggelassenes `dryRun` oder `dryRun:false` führt aus; `dryRun:true` gibt
`phase:preview` mit Auswirkungen, `dependsOn` und Guards zurück, ohne Änderung. In keinem Modus wird eine Bestätigungsabfrage
gesendet. Jeder Eintrag hat eine eindeutige, nicht leere `operationId` von höchstens 256 Zeichen; das Tool arbeitet nur auf dem
gebundenen Projekt (ein `projectPath` im Eintrag wird als `validation_error` abgelehnt). Das Tool
speichert, lädt oder steuert nie eine PLC.

| Operation | Erforderliche Eingaben | Optionale Eingaben |
|---|---|---|
| `update_block_logic` | `blockPath`, `content`, `expectedContentHash` | `format` (standardmäßig `xml`; `source` für Global-DBs und SCL-Bausteine; `sd` für die SIMATIC-SD-Dokumente, siehe [PLC_SIMATIC_SD_SUMMARY.md](PLC_SIMATIC_SD_SUMMARY.de.md)) |
| `update_type_content` | `typePath`, `content`, `expectedContentHash` | `format` (standardmäßig `source`) |
| `create_block` | `blockPath`, `blockType` | `language`, `obEventClass` |
| `delete_block` | `blockPath` | — |
| `create_block_group` / `delete_block_group` | `blockPath` | — |
| `create_tag_table` / `delete_tag_table` | `tableName` | `plcName`, `folderPath` |
| `create_tag` | `tableName`, `name`, `dataType` | `plcName`, `folderPath`, `logicalAddress`, `comment`+`commentCulture` |
| `update_tag` | `tableName`, `name` | `plcName`, `folderPath`, `newName`, `dataType`, `logicalAddress`, `externalAccessible`, `externalVisible`, `externalWritable`, `comment`+`commentCulture` |
| `delete_tag` | `tableName`, `name` | `plcName`, `folderPath` |
| `move_tag` | `tableName`, `name`, `targetTableName` | `plcName`, `folderPath`, `targetFolderPath` |
| `create_user_constant` | `tableName`, `name`, `dataType`, `value` | `plcName`, `folderPath` |
| `update_user_constant` | `tableName`, `name` | `plcName`, `folderPath`, `dataType`, `value` |
| `delete_user_constant` | `tableName`, `name` | `plcName`, `folderPath` |
| `add_interface_member` | `memberPath`, `dataType`, `blockPath`+`section` oder `typePath` | `startValue`, `comment`+`commentCulture`, `insertBefore`, `expectedContentHash` |
| `update_interface_member` | `memberPath`, `blockPath`+`section` oder `typePath` | `newName`, `dataType`, `startValue`, `clearStartValue`, `comment`+`commentCulture`, `expectedContentHash` (mindestens eine Änderung) |
| `delete_interface_member` | `memberPath`, `blockPath`+`section` oder `typePath` | `expectedContentHash` |
| `update_db_member` | `blockPath`, `memberPath` | `startValue`, `externalAccessible`, `externalVisible`, `externalWritable`, `retain`, `setPoint` (mindestens eines) |
| `create_instance_db` | `blockPath` (der neue DB), `instanceOfName` | `number` |
| `rename_block` | `blockPath`, `newName` | — |
| `renumber_block` | `blockPath` | genau eines von `number` oder `autoNumber:true` |
| `copy_block` | `blockPath`, `newName` | `targetGroupPath`, `number` |
| `move_block` | `blockPath`, `targetGroupPath` | — |
| `move_tag_table` | `tableName`, `targetFolderPath` | `plcName`, `folderPath` |
| `import_external_source`, `generate_blocks_from_source`, `delete_external_source`, `export_source_file` | siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#external-sources) | externe Quellen (nicht live getestet) |
| `rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group` | `blockPath` / `typePath` / `folderPath` der Gruppe, `newName` | `plcName`, `unitName` (Variablentabellengruppen); nicht live getestet |
| `create_type_group`, `delete_type_group`, `create_tag_table_group`, `delete_tag_table_group` | `typePath` / `folderPath` der Gruppe | `plcName`, `unitName` (Variablentabellengruppen); siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#creating-and-deleting-plc-data-type-groups-and-tag-table-groups); nicht live getestet |
| `create_software_unit`, `delete_software_unit`, `rename_software_unit` | `unitName` (Umbenennen: `newName`) | `plcName`; siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#software-units); nicht live getestet |
| `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv`, `export_blocks` | siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#file-exports) | Dateiexporte aus vorhandenen Lesezugriffen (nicht live getestet) |
| `export_tag_table`, `import_tag_table` | `tableName` + `filePath` (Export), `filePath` (Import) | `plcName`, `folderPath`, `overwrite`, `tableName` (Import); eine Variablentabelle als SimaticML-Datei, siehe die [Referenz der BetterTIA-Operationen](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.md#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions); nicht live getestet |
| `export_opcua_xml` | `filePath` | `plcName`, `overwrite`; die OPC-UA-Daten der CPU der PLC als XML über `Project.HwUtilities.Find("OPCUAExportProvider")` und `OpcUaExportProvider.Export(DeviceItem, FileInfo)` (Siemens-V21-Referenz, `plc-blocks.md` §13), zur Laufzeit gebunden: ein fehlender Provider oder ein fehlendes Member schlägt mit `api_member_missing` fehl, und nichts wird geschrieben. Der Export geht in eine temporäre Datei, wird neben dem Ziel bereitgestellt, an seinen Platz verschoben und neu gehasht wie `export_tag_table`; eine vorhandene Datei benötigt `overwrite:true` (Info-Guard `plc_export_overwrites_file`). Verifikation `trusted` (kein Zurücklesen). TIA Portal benötigt Berichten zufolge die PLC offline (heilingbrunner/tiaportal-mcp, nicht verifiziert). Idee von heilingbrunner/tiaportal-mcp (MIT); nicht live getestet |

`move_tag` ist ein abgesichertes Neuerstellen, kein natives Verschieben (Openness V21 dokumentiert keines): eine temporäre
Variable wird in der Zieltabelle mit Datentyp, Adresse, Flags für externen Zugriff
und mehrsprachigem Kommentar der Quelle erstellt, die Quelle wird gelöscht, die Kopie übernimmt den ursprünglichen Namen und alles wird
zurückgelesen. Andere Attribute werden nicht kopiert; der Info-Guard `plc_move_tag_recreates` sagt das
vor der Ausführung. Die Schritte laufen in einer ExclusiveAccess-Transaktion, die ein Fehler zurückrollt (siehe
[Granulare Bausteinbearbeitung](#db-werte-instanz-dbs-umbenennen-umnummerieren-kopieren-verschieben)). Siehe
[HARDWARE_MODULES_SUMMARY.md](HARDWARE_MODULES_SUMMARY.md#plc_write-move_tag); nicht live getestet.

`create_block` akzeptiert `FB`, `FC`, `OB` oder `GlobalDB`. Passende Bausteintypen unterstützen `LAD`, `FBD`, `STL`, `SCL` und `GRAPH`. `GRAPH` wird für `OB` nicht unterstützt. Das Erstellen von `STL`-FB, -FC und -OB funktioniert.

Das Erstellen eines OB nimmt `obEventClass`, eine abgeschlossene Menge von 15 Namen; weggelassen bedeutet `ProgramCycle`:
`ProgramCycle`, `Startup`, `TimeOfDay`, `TimeDelayInterrupt`, `CyclicInterrupt`,
`HardwareInterrupt`, `Status`, `Update`, `Profile`, `TimeErrorInterrupt`, `DiagnosticErrorInterrupt`,
`PullOrPlugOfModules`, `RackOrStationFailure`, `ProgrammingError` und `IOAccessError`. Der Server
wählt die OB-Nummer: eine Singleton-Klasse erhält ihre Basisnummer; eine Mehrinstanzklasse erhält ihre
Basisnummer, wenn kein OB in der PLC sie belegt, sonst die niedrigste freie Nummer von 123 bis 32767 (es zählen nur
OB-Nummern, einschließlich OBs in Software Units und Systemgruppen). `dryRun` zeigt die geplante
`number` und `obEventClass` in der Auswirkung. Jedes angewendete `create_block`-Ergebnis trägt `number` (die
Nummer des erstellten Bausteins; null für andere Bausteinoperationen) und `obEventClass` (null für Nicht-OB), beide
immer geschrieben. Eine unbekannte `obEventClass` ist `validation_error` mit Auflistung der gültigen Namen; ebenso
`obEventClass` mit einem `blockType`, der kein OB ist. Eine Klasse, die die CPU nicht unterstützt, lässt den Eintrag mit der eigenen
Meldung von TIA Portal fehlschlagen (zum Beispiel "Cannot create an organization block of type
'ProgrammingError'." auf einer S7-1200). Der
[Live-Abnahmebericht](../superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md) vom 2026-10-09
zeichnet den V21-Lauf auf.

`create_block`, `create_block_group` und `delete_block_group` nehmen einen deterministischen Gruppenpfad oder den
zweiteiligen `PLC/Name` (die Wurzelgruppe der PLC); ein einteiliger Pfad ist `validation_error`.
`update_block_logic` und `delete_block` lösen den Baustein eindeutig über die PLC auf, sodass ein fehlender Baustein
`target_not_found` ist. Die PLC selbst wird eindeutig über alle Geräte aufgelöst, einschließlich gruppierter
Geräte; mehrere Treffer sind `target_ambiguous`.

### Inhaltsschreibzugriffe

`update_block_logic` und `update_type_content` nehmen das gesamte Dokument als `content`, zusammen mit
`expectedContentHash`: dem `contentHash` aus einem `plc_read` desselben Objekts im selben Format.
Wenn sich das Objekt seit diesem Lesezugriff geändert hat, schlägt der Aufruf mit `state_changed` fehl, bevor irgendetwas läuft; eine
Änderung zwischen Planung und Import lässt diesen Eintrag mit `state_changed` fehlschlagen. Ein Dokument über dem
Wertbudget (standardmäßig 60.000 Zeichen) wird in `plc_read` weggelassen, hat keinen Hash und kann nicht über
`plc_write` aktualisiert werden. Eine Quelle, die mehr als ein Objekt deklariert (ein `withDependencies`-Lesezugriff), wird abgelehnt.
Bausteinaktualisierungen importieren in den vorhandenen Baustein, übersetzen den betroffenen PLC-Bereich und verifizieren das Ergebnis
durch erneuten Export (die Verifikation `contentHash`, unten); Typaktualisierungen erfordern eine Deklaration, deren Name dem Ziel entspricht. Import/Aktualisierung
erstellt, benennt um, löscht oder legt das adressierte Objekt nie per Upsert an. Eine Vorschau kann ein begrenztes
`contentDiff` von aktuellem gegenüber angefordertem Text tragen (siehe die
[Import-/Exportoptionen](IMPORT_EXPORT_OPTIONS_SUMMARY.de.md)). Ein fehlgeschlagener `update_block_logic`-Import
meldet `blockImportOutcome` im Fehler des Eintrags; dieses Member ist im Fehlerschema jedes strukturierten Tools
bedingt.

### Guards

Guards werden für jeden Eintrag vor der ersten Änderung ausgewertet. Block-Guards beenden den Aufruf in jedem
Modus (ein Probelauf führt sie auf und gibt trotzdem `preview` zurück); Info-Guards werden zu Warnungen. Guards decken
Prüfungen von Folgen und nicht lesbaren Nachweisen ab, dazu die beibehaltene Singleton-OB-Prüfung. Namenskollisionen, vorhandene Bausteine, vorhandene Gruppen und
Tabellen sowie die Standard-Variablentabelle sind nicht abgesichert: die Planung prüft Erstellungen nicht. Abgegrenzte Live-Fälle zeigen native
Ablehnungen, mit den genauen Namespace-Richtungen und Grenzen unten (siehe [Fehler aus TIA Portal](#fehler-aus-tia-portal)).

| Guard | Schweregrad | Löst aus, wenn |
|---|---|---|
| `plc_state_unverifiable` | info | Das PLC-Inventar unvollständig ist, das Aktualisierungs- oder Löschziel oder das, was eine Löschung entfernt, nicht lesbar ist, der Wert der Anwenderkonstante, den eine Aktualisierung ersetzt, nicht lesbar ist oder der Inhalt nicht exportiert werden kann. Nicht lesbare Nachweise werden nie als leer behandelt; seit 2026-11-01 informiert es und der Eintrag läuft, TIA Portal entscheidet (Entscheidung des Eigentümers: nie strenger als TIA Portal). |
| `plc_ob_singleton_exists` | block | `create_block` einen OB einer Singleton-Ereignisklasse erstellt und die PLC die OB-Nummer dieser Klasse bereits belegt oder ein früherer Eintrag im selben Aufruf ihn erstellt. Die Meldung nennt die Klasse, die Nummer und den vorhandenen Baustein. |
| `plc_deletes_block` | info | `delete_block` den Baustein und seinen Inhalt entfernt. |
| `plc_deletes_group_contents` | info | `delete_block_group` jeden Baustein und jede Gruppe darin entfernt; die Auswirkung führt sie auf. `delete_type_group`, `delete_tag_table_group` und `delete_software_unit` lösen ihn ebenfalls aus, wenn die Gruppe oder Unit nicht leer ist (siehe die [Referenz für Quellen und Ordner](PLC_SOURCES_FOLDERS_SUMMARY.de.md)). |
| `plc_deletes_table_contents` | info | `delete_tag_table` ihre Variablen und Anwenderkonstanten entfernt. |
| `plc_address_overlap` | info | Die angeforderte logische Adresse bereits von einer anderen Variable genutzt wird; TIA Portal erlaubt die Überschneidung. |
| `plc_changes_interface` | info | Eine Bearbeitung eines Schnittstellen-Members den Baustein oder PLC-Datentyp neu importiert; Instanz-DBs, Multiinstanzen und Aufrufer bleiben inkonsistent, bis sie übersetzt werden, und das Portal kann eine strukturelle Änderung eines FB mit Instanzen ablehnen. Ein Umbenennen oder Löschen ist kein Refactoring: Referenzen auf das Member im Code des Bausteins und in anderen Bausteinen werden nicht aktualisiert. |
| `plc_block_round_trip` | info | `copy_block`/`move_block` den SimaticML-Export innerhalb einer ExclusiveAccess-Transaktion importieren, die ein Fehler zurückrollt: eine neue Objektidentität und keine Verknüpfung zum Bibliothekstyp; sollte das Zurückrollen einen verschobenen Baustein nicht wiederherstellen, wird er neu importiert, und wenn auch das fehlschlägt, wird sein Export in einem Wiederherstellungsordner aufbewahrt. |
| `plc_moves_tag_table` | info | `move_tag_table` die Tabelle exportiert, dann löscht und in den Zielordner importiert, innerhalb einer ExclusiveAccess-Transaktion, mit denselben Rückfallebenen Neuimport und Wiederherstellungsordner; die Auswirkung zählt ihre Variablen und Anwenderkonstanten. |
| `plc_move_tag_recreates` | info | `move_tag` die Variable in der Zieltabelle innerhalb einer ExclusiveAccess-Transaktion neu erstellt, die ein Fehler zurückrollt; andere Attribute als Datentyp, Adresse, Flags für externen Zugriff und Kommentar werden nicht kopiert. |
| `plc_generates_from_source` | info | `generate_blocks_from_source` jeden Baustein und PLC-Datentyp erstellt oder überschreibt, den die externe Quelle deklariert; `None` löscht bei einem Fehler, was es erzeugt hat, `KeepOnError` behält Objekte mit Fehlern ([Referenz für Quellen und Ordner](PLC_SOURCES_FOLDERS_SUMMARY.de.md)). |
| `plc_deletes_external_source` | info | `delete_external_source` nur den Quellknoten entfernt; daraus erzeugte Bausteine und PLC-Datentypen bleiben bestehen. |
| `plc_renames_group` | info | Ein Gruppen-Umbenennen (`rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group`) oder `rename_software_unit` die Pfade von allem darin ändert; Namen und Referenzen zwischen Objekten ändern sich nicht. |
| `plc_export_overwrites_files` | info | Ein Dateiexport (`export_source_file`, die CSV-Exporte, `export_blocks`) vorhandene Dateien ersetzt, weil `overwrite:true` angefordert wurde. |
| `plc_s7dcl_import_may_alter` | info | `update_block_logic` den SIMATIC-SD-Weg nimmt (ein Bundle ohne `.xml`, aber mit `.s7dcl`) und die `.s7dcl` ein Konstrukt enthält, das der V21-Import Berichten zufolge verändert (verschachtelter Index in LAD/FBD, Projekttyp mit dem Namen eines Systemtyps, AT-Überlagerung), oder die Überlagerungen nicht geprüft werden konnten. Siehe [V21-Fallstricke](V21_PITFALLS.md#pitfall-1-simatic-sd-import). |
| `plc_textual_interface_overwrite_may_terminate` | info | `update_block_logic` oder eine Bearbeitung eines Schnittstellen-Members einen Baustein mit textueller Schnittstelle überschreibt; TIA Portal V21 beendet sich Berichten zufolge bei einem solchen Überschreiben nach mehr als einem Export. Die Meldung nennt den Exportzähler des Workers. Siehe [V21-Fallstricke](V21_PITFALLS.md#pitfall-2-textual-interface-overwrite). |
| `plc_save_before_write_recommended` | info | Zusammen mit `plc_textual_interface_overwrite_may_terminate` auslöst: zuerst das Projekt speichern, ein beendetes TIA Portal verliert ungespeicherte Änderungen. |

`plc_ob_singleton_exists` blieb, weil die eigene Ablehnung eines doppelten Singleton-OB durch das Portal nie
beobachtet wurde: `create_block` wählt die OB-Nummer selbst, und ein `update_block_logic`, das eine
doppelte Nummer oder Ereignisklasse deklariert, wird vor jedem Import abgelehnt, sodass keine `plc_write`-Eingabe das Portal
mit einer solchen erreicht. Der zweite `TimeErrorInterrupt`-OB wurde vom Guard blockiert, auch innerhalb eines Aufrufs.

Die Guards `plc_name_collision`, `plc_block_exists`, `plc_default_tag_table` und
`plc_attribute_unreadable` wurden mit Schreibvertrag `2.0` entfernt. Das Löschen der Standard-Variablentabelle
ist jetzt die Ablehnung durch das Portal: `engineering_rejected` mit "The operation is not valid because of the
current status of object" (die Meldung sagt nicht "default"). Ein nicht lesbares Flag für externen Zugriff
bei `update_tag` ist kein Guard mehr; die Anforderung erreicht das Portal.

### Fehler aus TIA Portal

Wenn das Portal selbst eine Änderung ablehnt, schlägt der Eintrag mit einer typisierten Kategorie und der
Openness-Ausnahme fehl, nie mit einer vom Server geparsten Meldung:

- `engineering_rejected`: die Worker-Methode wurde als Änderung klassifiziert und Openness hat
  `EngineeringTargetInvocationException` geworfen (eine doppelte Variable, Tabelle, ein doppelter Baustein oder eine doppelte Gruppe; eine Löschung, die das Portal
  verweigert). Die Klassifizierung umfasst die gesamte Methode, einschließlich Verbindungs-, Identitäts- und Zielprüfungen vor einem nativen Schreibzugriff; sie beweist nicht, dass ein Setter oder ein Erstellen erreicht wurde. Der Fehler trägt `engineeringError`.
- `worker_operation_failed`: jede andere Siemens-`EngineeringException`. Sie trägt ebenfalls
  `engineeringError`, wenn die Ausnahme den gemeinsamen Catch des Workers erreicht hat.
- Lesezugriffe erzeugen nie `engineering_rejected`. Eine Siemens-Ausnahme während eines Planungs-Lesezugriffs behält ihre
  Lesekategorie (`worker_operation_failed`, `target_not_found` und so weiter) auf Aufrufebene.
- `portal_lost` hat Vorrang vor beiden: wenn die Portal-Anbindung während des Eintrags verworfen wurde, ist die Kategorie
  `portal_lost` ohne `engineeringError`, und Bindung und Cursor werden ungültig gemacht.

`engineeringError` ist `{ "type", "message", "inner": [ { "type", "message" } ] }`: der Name des Ausnahmetyps,
seine Meldung wörtlich und die Kette der inneren Ausnahmen, die äußerste zuerst (`inner` ist immer ein
Array, leer, wenn es keine innere Ausnahme gibt). Der Vertrag begrenzt eine Meldung auf 4.000 Zeichen
und die Kette auf 8 Einträge; längerer Text wird ohne Markierung abgeschnitten. Das Member ist bedingt: es fehlt
bei Fehlern, die nicht von Siemens kamen, und bei hostseitigen Ablehnungen. Meldungen sind in der Sprache der
Portal-Oberfläche, daher dürfen Aufrufer sie nicht parsen; der Live-Lauf nutzte eine englische Oberfläche. Aufgezeichnetes Beispiel,
`create_tag` mit einem Namen, den die Tabelle bereits enthält (Eintrag 1 von 1; der Aufruf ist `phase:"applied"`,
`success:false`, `error:null`, `isError:false`):

```json
{
  "operationId": "l1-01",
  "operation": "create_tag",
  "status": "failed",
  "failure": {
    "category": "engineering_rejected",
    "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
    "engineeringError": {
      "inner": [],
      "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
      "type": "EngineeringTargetInvocationException"
    }
  }
}
```

Andere Live-Aufzeichnungen haben dieselbe Form und `inner:[]`: eine doppelte Variablentabelle
(`PlcTagTableComposition`), eine doppelte Bausteingruppe (`PlcBlockUserGroupComposition`), ein doppelter
Baustein (`create_block` ist ein XML-Import, daher erwähnt die Meldung `Import` von
`PlcBlockComposition`, eine Zeile und eine Position) und das Löschen der Standard-Variablentabelle. Wenn Eintrag N von M
abgelehnt wird, sind die Einträge vor N `applied`, Eintrag N ist `engineering_rejected`, spätere Einträge sind
`skipped` mit `earlierOperationFailed`, und nichts wird zurückgerollt. Siehe die
[Live-Fälle](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-26-plc-write-native-rejections-live-cases.md).

[Zusätzliche Proben](../superpowers/acceptance/reports/inputs/2026-10-26-plc-write-collision-namespace-probes.md) haben auf einem Wegwerfprojekt vier weitere native Ablehnungsrichtungen gezeigt: das Erstellen einer Variable in einer zweiten Tabelle gegen einen vorhandenen Variablennamen, das Erstellen einer Anwenderkonstante gegen diesen Variablennamen, das Erstellen einer Variable gegen einen vorhandenen FC-Namen und das Erstellen einer Tabelle in einem vorhandenen Unterordner gegen einen vorhandenen Namen einer Wurzeltabelle. Die umgekehrten artübergreifenden Richtungen, gruppenübergreifende doppelte Bausteine und beliebige Ordnerlayouts bleiben unverifiziert. Die Quellvariable und die leeren Tabellen wurden nach den Proben geprüft; der FC blieb lesbar, ohne Vorher/Nachher-Vergleich des Hashs. Das Aufräumen stellte die beobachteten Inventare und den typisierten Baum wieder her; ungespeicherter Bearbeitungsverlauf blieb bestehen.

Das Portal wirft nicht bei jeder Kollision. Ein Umbenennen über `update_tag` auf einen Namen, den eine andere Variable in
der Tabelle trägt, wird stillschweigend ignoriert. Der Worker wendet das Umbenennen zuerst an, liest `Name` zurück und lässt,
wenn das Portal den alten Namen behalten hat, den Eintrag mit `postcondition_failed` fehlschlagen, bevor irgendeine andere Eigenschaft
angewendet wird ("No other property was changed"). Ein Review hat ein separates Risiko nicht unterstützter Flags festgestellt, nicht live ausgeführt: ein
`update_tag`, das ein Flag für externen Zugriff anfordert, das die Variable nicht annehmen kann, schlägt mitten in der Aktualisierung fehl, nachdem ein
Umbenennen oder eine Datentypänderung im selben Eintrag bereits angewendet wurde; es gibt kein Rollback.

### Ausführung, Verifikation und Audit

Fehler des Bindungs-Gates behalten ihre ursprüngliche Kategorie, einschließlich `worker_timeout`, `worker_crashed`,
`portal_lost` und `protocol_error`, im Fehler auf oberster Ebene und im Audit. Eine Ablehnung durch das Gate stoppt vor Planung
oder Änderung; eine fehlende, nicht passende oder geänderte Bindung ist `binding_conflict`.

Einträge laufen sequenziell. Spätere Einträge dürfen Objekte nutzen, die früher im selben Aufruf erstellt wurden; sie tragen
`dependsOn` und werden unmittelbar vor ihrer eigenen Änderung neu geplant. Erstellungen werden nicht vorab geprüft, daher schlägt ein
Erstellen in eine fehlende übergeordnete Bausteingruppe oder einen fehlenden Variablentabellenordner bei der Änderung fehl (das Portal oder der
Worker lehnt den Eintrag ab) statt in der Vorprüfung, und eine spätere Löschung eines Objekts, das nie
angelegt wurde, lässt den gesamten Aufruf als `target_not_found` fehlschlagen. Aktualisierungs- und Löschziele werden weiterhin
vor der ersten Änderung aufgelöst, sodass ein falsch geschriebener Pfad den gesamten Aufruf mit `target_not_found` fehlschlagen lässt, bevor
irgendetwas läuft. Jede Ablehnung in der Planung funktioniert so (auch eine Bearbeitung, die nichts ändert, etwa ein
`replace_tag_comments` ohne Treffer): die Planung prüft alle Einträge, bevor der erste läuft, daher
lehnt die Ablehnung eines Eintrags den gesamten Aufruf ab, und in einem Aufruf mit mehreren Einträgen sagt ihre Meldung, dass
kein Eintrag lief (Defekt K25, live V21 2026-11-02; Verhalten beibehalten, Meldung korrigiert). Der erste Fehler beendet den Aufruf, und spätere Einträge sind `skipped`. Es gibt kein
Rollback und keine automatische Wiederholung: betroffene Objekte vor einer Wiederholung mit `plc_read` erneut lesen. Phasen sind
`preview`, `applied`, `blocked` und `error`; `isError` ist nur bei einer Ablehnung wahr, bevor irgendetwas
lief, und ein Aufruf, der lief und fehlschlug, gibt `success:false` mit `error:null` zurück. Budgets sind standardmäßig 60.000
Zeichen pro Wert und 180.000 pro Dokument; ganze Werte werden weggelassen, nie abgeschnitten. Jeder
betretene Aufruf hängt einen Audit-v2-Datensatz an `writes-yyyy-MM-dd.jsonl` an, mit den tatsächlichen
Eintragsstatus und der Bestätigung `none` in read-write und `policy` in full.

Die Verifikation (Schreibvertrag `2.0`; `plc_read` bleibt `1.0`) liest nur Inhalt zurück:

- `trusted`: ein abgeschlossener Nicht-Inhaltseintrag ohne Zurücklesen. Der Openness-Aufruf kehrte ohne Ablehnung zurück, und nichts
  wurde zurückgelesen. Die Prüfungen `exists`, `absent`, `values` und `observed` aus Vertrag `1.0` gibt es nicht mehr.
- `tag`: `create_tag`, `delete_tag`, `move_tag` und ein `update_tag`, das einen Kommentar geschrieben hat, lesen die Variablentabelle erneut
  (`list_tag_tables`): die Variable existiert (bzw. ist bei einer Löschung weg; ein Verschieben vergleicht außerdem Datentyp und Adresse, die der
  Worker gemeldet hat) und, wenn ein Kommentar geschrieben wurde, zeigt `read_tag_comments` seinen Text in dieser Kultur (Defekte K22,
  K24, 2026-11-02; nicht live getestet).
- `block` für `delete_block`: der gelöschte Pfad wird mit `get_block_interface` erneut gelesen, das
  `target_not_found` antworten muss. Bausteinnamen sind pro PLC eindeutig, daher ersetzt ein späterer Eintrag, der denselben Namen in einer beliebigen Gruppe erstellt,
  die Löschung (die K19-Regel für Bausteine).
- `readBack`: `set_plc_access_level`, dessen Worker `PlcProtectionAccessLevel` zurückgelesen und die angeforderte Stufe vorgefunden hat
  (`readBack:"matched"`); `expected` ist die angeforderte und `actual` die zurückgelesene Stufe. Eine nicht lesbare Stufe bleibt
  `trusted` (nichts konnte gelesen werden).
- `unverifiable` (success true): ein abgeschlossener Schreibzugriff, dessen eigenes Zurücklesen im Worker übereinstimmte (`update_db_member`,
  `rename_block`, `renumber_block`, `copy_block`, `move_block`, `create_instance_db`), dessen erneutes Lesen durch den Host TIA Portal aber
  verweigerte, weil das Objekt bis zum Übersetzen inkonsistent ist (Defekt K01/K20); `expected`/`actual` sind die Anforderung und
  das Zurücklesen des Workers, und die Meldung trägt den Übersetzungshinweis unten. Andere auf diese Weise verweigerte Rücklesungen (ein erneuter
  Inhaltsexport, ein erneutes Lesen der Schnittstelle) bleiben fehlgeschlagene Einträge mit dem Hinweis.
- `contentHash`: ein abgeschlossener `update_block_logic`/`update_type_content`-Eintrag wird erneut exportiert und sein
  Hash gemeldet (`contentHash` und `actual`). Ein fehlgeschlagener Inhaltseintrag meldet ebenfalls den aktuellen Hash,
  da ein fehlgeschlagener Import festgeschrieben worden sein kann.
- `superseded`: ein Eintrag, dessen Objekt ein späterer Eintrag geändert hat oder geändert haben könnte; nur ein späterer Eintrag mit Zurücklesen (ein
  Inhaltshash oder ein erneutes Lesen) verifiziert den Endzustand. Ein späterer trusted-Eintrag sagt, dass der Endzustand des Objekts nicht verifiziert ist (mit
  `plc_read` prüfen); die Quell-, Ordner- und Unit-Operationen nennen stattdessen, was sie dafür bedeuten (siehe
  [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.md#planning-guards-and-verification)). Ein `update_block_logic`-Ziel mit kurzem Pfad (`PLC/Name`) wird nicht über den Baum aufgelöst, daher
  ersetzt es vorsichtshalber jeder spätere Bausteineintrag mit demselben Namen oder jedes `delete_block_group` auf dieser PLC,
  und es erhält keine Inhaltsprüfung. Wenn der spätere Eintrag ohne Verifikation fehlgeschlagen ist, ist der Endzustand ungewiss; mit `plc_read` prüfen.
  Member-Bearbeitungen werden pro Member ersetzt (seit 2026-11-02, Defekt K03: Live-Bericht vom 2026-11-01, zwei Schnittstellenbearbeitungen an verschiedenen Membern eines
  Bausteins markierten die erste als `superseded`, obwohl beide Member richtig waren): `add_interface_member`,
  `update_interface_member` (alter und neuer Name), `delete_interface_member` und `update_db_member` werden nur ersetzt durch
  einen späteren Schreibzugriff auf dasselbe oder ein umschließendes Member oder durch einen späteren Eintrag auf das ganze Objekt (Inhaltsaktualisierung, Dokument- oder
  Netzwerkbearbeitung, Umbenennen, Löschen, Gruppenlöschung); eine spätere Schnittstellenbearbeitung ersetzt weiterhin frühere Einträge auf das ganze Objekt ihres Bausteins oder
  Typs, ein späteres `update_db_member` nicht. Eine Bearbeitung eines anderen Members erhält ihr eigenes Zurücklesen im abschließenden Lesezugriff
  (`interfaceMember` oder `dbMember`, jeweils mit dem abschließenden Hash des Bausteins). Nur offline getestet; nicht live getestet.
- Ein fehlgeschlagener Nicht-Inhaltseintrag hat keinen Verifikationseintrag (nichts zu verifizieren). Das Objekt vor einer Wiederholung mit
  `plc_read` prüfen.
- `interfaceMember`, `dbMember`, `block` und `tagTable`: die granularen Bausteinbearbeitungen lesen das bearbeitete
  Member, den DB-Wert, den resultierenden Baustein oder die Variablentabellen erneut und vergleichen sie mit der Anforderung (`expected` und
  `actual` beschreiben beides; `contentHash` trägt den Hash des erneuten Lesens, wo es einen gibt). Ein fehlgeschlagener
  granularer Eintrag meldet den beobachteten Zustand. Siehe [Granulare Bausteinbearbeitung](#granulare-bausteinbearbeitung).

Inkonsistente Objekte (Defekt K20, live V21 2026-11-01/02: "Inconsistent blocks and PLC data types (UDT) cannot be
exported" nach einer Retain-Änderung, einem Umnummerieren oder einer Technologieobjekt-Änderung): der Server übersetzt nie von sich aus.
Wenn ein Export eines Bausteins (jedes Format, der SimaticML-Lesezugriff hinter `get_block_interface`, `get_db_values` und den
Round Trips von `copy_block`/`move_block`) oder eines Technologieobjekts eine Ausnahme wirft und das dokumentierte
`PlcBlock.IsConsistent` des Objekts falsch liest, hängt der Worker einen Hinweis an, der mit "TIA Portal exports only consistent objects:" beginnt
und `compile_check` mit `blockPath` (Bausteine) oder `plcName` (Technologieobjekte) nennt, an den Fehler an, oder fügt ihn als
Warnung hinzu, wenn die Anforderung dennoch erfolgreich war. Der Hinweis stammt aus diesem typisierten Nachweis, nie aus der Siemens-Meldung,
die der Sprache der Portal-Oberfläche folgt. Ein `move_block` nach einem `renumber_block` desselben Bausteins in einem Aufruf (D2/K02)
schlägt weiterhin fehl: der Export ist der erste eigene Schritt des Verschiebens, kein Planungs- oder Verifikations-Lesezugriff; sein Fehler trägt jetzt den
Hinweis. PLC-Datentypen sind nicht abgedeckt (ihr Konsistenzattribut wird nicht geprüft). Nicht live getestet.

Die Verifikation `contentHash` entspricht dem Hash, den der nächste `plc_read` dieses Objekts zurückgibt, und kann
als nächster `expectedContentHash` verwendet werden. Sie kann nicht aus dem übermittelten Inhalt berechnet werden: der Export
trägt eine generierte ID, und vier identische `update_block_logic`-Schreibzugriffe ergaben vier verschiedene Hashes.
Sie aus der Verifikation oder einem frischen Lesezugriff nehmen.

### Bekannte Grenzen

- Die Prüfung auf Adressüberschneidung ist ein exakter Zeichenkettenvergleich (`%MW0` und `%M0.0` werden nicht als
  überlappend erkannt), mit Schweregrad info.
- Zwei Inhaltsaktualisierungen desselben Bausteins in einem Aufruf können nicht beide geplant werden.
- Für Variablen, die früher im selben Aufruf erstellt wurden, wird für die Planung angenommen, dass alle Flags für externen Zugriff wahr
  sind.
- Bei unvollständigem Inventar ist ein Selektor-Fehltreffer `plc_state_unverifiable` (block), nicht
  `target_not_found`.
- Doppelte Variablentabellennamen wurden innerhalb eines Ordners getestet und, in einer zusätzlichen Probe, das Erstellen in einem Unterordner gegen einen vorhandenen Wurzelnamen. `plc_write` kann keine
  Variablentabellenordner erstellen; beliebige Paare von Unterordnern wurden nicht ausgeführt.
- Ein Review hat ohne Live-Ausführung festgestellt, dass ein `update_tag`, das ein nicht unterstütztes Flag für externen Zugriff anfordert, fehlschlagen kann, nachdem ein Umbenennen oder eine
  Datentypänderung im selben Eintrag angewendet wurde (kein Rollback).
- Die eigene Ablehnung eines doppelten Singleton-OB durch das Portal wurde nie beobachtet (siehe den Guard-Hinweis oben).
- Inhaltsimporter (`update_block_logic`, `update_type_content`) flachen Importfehler weiterhin zu
  `worker_operation_failed` ohne `engineeringError` ab; siehe das
  [Verbesserungsprotokoll](../IMPROVEMENT_LOG.md).
- Erstellte OBs nutzen die Standardwerte von TIA Portal: keine Ereignisparameter (Zykluszeit, Phasenverschiebung, Priorität,
  Uhrzeitplan, Auslöser für Prozessalarme) und kein Umnummerieren vorhandener Bausteine. Siehe das
  [Verbesserungsprotokoll](../IMPROVEMENT_LOG.md).
- `SynchronousCycle` wird nicht angeboten: es lässt sich erst übersetzen, wenn ein taktsynchrones IO-System zugewiesen ist,
  was kein Tool hier konfiguriert. Motion-Control-, Redundanz- und ProDiag-OBs werden von TIA Portal erstellt.
- Die Nummer eines Mehrinstanz-OB kann vom Plan abweichen, wenn ein anderer OB sie zwischenzeitlich belegt; die `number` im Ergebnis
  ist maßgeblich. Eine zwischenzeitlich belegte Singleton-Nummer lässt den Eintrag mit `state_changed` fehlschlagen.

## Offline-Änderungserkennung (`plc_read`)

> **Nicht live getestet** (GitHub-Issue #6). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Step7.xml`, Member
> `P:Siemens.Engineering.SW.PlcChecksumProvider.Software` ("Software checksum"), `P:...PlcChecksumProvider.TextLists`
> ("Text lists checksum"), `M:Siemens.Engineering.SW.FingerprintProvider.GetFingerprints` ("Read Fingerprint", liefert
> `IList<Siemens.Engineering.SW.Fingerprint>`), `P:Siemens.Engineering.SW.Fingerprint.Id` und `.Value`.

Zwei `plc_read`-Operationen (jeder Modus; `Observe`; eigenständige Projekte und lokale Sitzungen). Namen in
`PlcChangeDetectionMethods` (Contracts `Plc/ChangeDetection`), Parameter in `WorkerRequest.PlcChangeDetection`, Dispatch
`Program.PlcChangeDetection.cs`, Worker `Openness/Plc/ChangeDetection/PlcChangeDetectionService.cs`, Host
`TiaMcpServer/Plc/ChangeDetection/`, FakeWorker-Szenario `plc-change-detection`.

- **`get_plc_checksums`** (optional `plcName`; ohne Angabe wird jede PLC gelesen): je PLC `{ plcName, deviceName, available,
  unavailable, software, textLists }`. Die XML sagt nicht, welches Objekt den `PlcChecksumProvider` anbietet; der Worker holt
  ihn über die `GetServiceInfos()` der `PlcSoftware` mit exaktem vollem Namen und ruft ihn mit
  `IServiceProvider.GetService(Type)` ab. Nicht gelistet oder gelistet, aber nicht geliefert, ergibt `available: false` mit dem
  Grund in `unavailable` (die anderen PLCs werden trotzdem gelesen). Die XML nennt für beide Prüfsummen keinen Typ, daher wird
  jede als Text abgebildet: `{ state, value, runtimeType, error }` mit `state` `value`, `empty`, `null`, `unreadable` (eine
  behebbare Siemens-Ausnahme, in `error` benannt) oder `missing` (keine lesbare Eigenschaft dieses Namens am Live-Provider).
  Bytefolgen werden hexadezimal geschrieben, andere Werte kulturunabhängig. Eine leere oder fehlende Prüfsumme bedeutet laut
  dem Siemens-Skill change-detection für TIA Portal V21 (nicht laut XML), dass die Software nicht übersetzt oder seit dem
  letzten Übersetzen geändert wurde; das `note` des Ergebnisses sagt das.
- **`get_fingerprints`** (genau eines von `blockPath` oder `typePath`): `FingerprintProvider.GetFingerprints()` dieses
  Bausteins oder PLC-Datentyps, der Provider ebenso unter den `GetServiceInfos()` des Objekts gefunden. Nicht gelistet oder
  nicht geliefert ist `unsupported_capability`; ein Provider ohne `GetFingerprints()` ist `api_member_missing`; eine Rückgabe
  null oder keine Sammlung ist `worker_operation_failed`. Das Ergebnis `{ objectKind, path, plcName, collectionType,
  fingerprints[] }` enthält `id` und `value` jedes `Fingerprint` in derselben Textform (die XML führt die `FingerprintId`-Felder
  `Code`, `Comments`, `Interface`, `LibraryType`, `Texts`, `Alarms`, `Supervisions`, `TechnologyObject`, `Events`,
  `TextualInterface`, `Properties`, `ProgramCode` auf, nennt aber den Typ von `Fingerprint.Id` nicht, daher wird nichts
  angenommen).
- **Kein Stub-Member** wurde ergänzt: jedes Member wird zur Laufzeit vom Siemens-freien `PlcChangeDetectionRun` und
  `RuntimeServiceValues` (Contracts `Shared`) gebunden, die der FakeWorker mitbenutzt.
- **Online-Gegenstück:** der Online-`Siemens.Engineering.FingerprintData.FingerprintDataProvider`
  (`GetFingerprintData(ConfigurationAddress, OnlineConfigurationDelegate)`) ist der eigene `OnlineObserve`-Lesezugriff
  `get_station_fingerprint_data` (Audit N3, nicht live getestet; siehe die
  [PLC-Online-Referenz](PLC_ONLINE_OPENNESS_SUMMARY.md#station-fingerprint-data-get_station_fingerprint_data)).
- **Noch nötige Live-Abnahme:** welche Objekte die beiden Provider listen, die Laufzeittypen der Prüfsummen, von `Id` und
  `Value`, und ob die Prüfsummen nach einer Änderung leer und nach `compile_check` gefüllt sind.

## Ladbare Dateien (`plc_write`)

> **Nicht live getestet** (GitHub-Issue #26). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Step7.xml`,
> `M:Siemens.Engineering.SW.Loader.LoadableProvider.GenerateLoadable(System.IO.FileInfo,System.Collections.Generic.IEnumerable{Siemens.Engineering.SW.Blocks.PlcBlock},Siemens.Engineering.SW.Loader.TargetOption)`
> ("Generates a loadable file for blocks in the non unit program"), die Überladung mit `IEnumerable{Siemens.Engineering.SW.Units.PlcUnit}`
> ("Generates a loadable file for software units") und `T:Siemens.Engineering.SW.Loader.TargetOption` mit `None` ("Throws an
> exception"), `Plc` und `PlcSim`.

Die `plc_write`-Operation `generate_loadable` (read-write und full; `ProjectMutation`, sie schreibt eine Datei des Aufrufers;
keine Rückfrage) nimmt `filePath`, `target` (`Plc` oder `PlcSim`; `None` wirft laut Dokumentation und wird nicht angeboten),
genau eines von `blockPaths` (1-200 Bausteine des Programms ohne Software Units einer PLC) oder `unitNames` (1-200 Software
Units), optional `plcName` (Standard: die PLC, die die Bausteinpfade nennen, sonst die einzige PLC) und `overwrite`. Namen in
`PlcLoadableMethods` (Contracts `Plc/Loadable`), Parameter in `WorkerRequest.PlcLoadable`, Dispatch `Program.PlcLoadable.cs`,
Worker `Openness/Plc/Loadable/PlcLoadableService.cs`, Host `TiaMcpServer/Plc/Loadable/` (Test-Glob), FakeWorker
`plc-write-roundtrip` (PLC_2 bietet keinen Provider).

- **Provider und Bindung.** Die XML sagt nicht, welches Objekt den `LoadableProvider` anbietet; der Worker holt ihn über die
  `GetServiceInfos()` der `PlcSoftware` mit exaktem vollem Namen (nicht gelistet oder nicht geliefert: `unsupported_capability`).
  Zuerst werden die Bausteine (`BlockTargetResolver`) oder Software Units aufgelöst (`target_not_found`); dann bindet der
  Siemens-freie `PlcLoadableRun` (Contracts) die eine `GenerateLoadable`-Überladung mit den Parametern `FileInfo`,
  `IEnumerable<T>` mit genau dem vollen Namen des Elementtyps und der Aufzählung mit genau dem vollen Namen von `TargetOption`,
  und liest `target` über den Member-Namen; fehlt etwas, ist das `api_member_missing`, nichts geschrieben. Kein Stub-Member wurde
  ergänzt. Der Rückgabewert ist nicht dokumentiert und wird ignoriert (sein Typ steht in `result.signature`).
- **Datei.** Die Regeln für Dateien des Aufrufers wie bei den Meldungs-Exporten (`PlcAlarmFiles`): absoluter Laufwerks- oder
  UNC-Pfad, lexikalisch geprüft, eine vorhandene Datei nur mit `overwrite:true` (Info-Guard `plc_export_overwrites_file`, wenn
  gesetzt) und nie über eine Verknüpfung; der Aufruf schreibt in eine temporäre Datei neben dem Ziel, die dann an ihren Platz
  verschoben und neu gehasht wird. Ein Aufruf, der am übergebenen Pfad keine Datei hinterlässt, scheitert mit
  `postcondition_failed`. Keine Endung wird verlangt: keine ist dokumentiert.
- **Ergebnis und Verifikation.** `{ plcName, overload (blocks|units), objects[], target, signature, filePath, overwritten,
  bytes, sha256, messages }`; die Verifikationsprüfung `file` meldet den neuen Hash des Workers.
- **Noch nötige Live-Abnahme:** dass die PlcSoftware den Provider listet, was der Aufruf schreibt (eine Datei, ihre Endung), ob
  er ein übersetztes Programm braucht, und dass sich die Datei in eine PLC oder PLCSIM laden lässt.

## Know-how-Schutz und Schreibschutz von Bausteinen (`plc_read`/`plc_write`)

> **Nicht live getestet** (GitHub-Issue #5). Nachweis: die V21-IntelliSense-Dateien `Siemens.Engineering.Step7.xml`
> (`T:Siemens.Engineering.SW.Blocks.PlcBlockProtectionProvider` mit `Protect(System.Security.SecureString)` "Sets protection for
> the underlying object" und `Unprotect(System.Security.SecureString)` "Removes protection";
> `T:Siemens.Engineering.SW.Blocks.PlcBlockWriteProtectionProvider` mit `IsDefined` "Is password defined", `IsProtected` "Is
> write protection enabled", `Define(SecureString)`, `Protect(SecureString)`, `Unprotect(SecureString)`,
> `Change(SecureString, SecureString)`; `P:Siemens.Engineering.SW.Blocks.PlcBlock.IsKnowHowProtected`) und
> `Siemens.Engineering.Base.xml` (`ProtectionProviderBase.GetInvalidPasswordCharacters()`), dazu der Siemens-Skill blocks
> ("Protect Block with Password": `block.GetService<PlcBlockProtectionProvider>()`, unzulässige Zeichen vor `Protect` prüfen).

Operationen (Namen in `PlcBlockProtectionMethods`, Contracts `Plc/BlockProtection`; Parameter `WorkerRequest.PlcBlockProtection`;
Dispatch `Program.PlcBlockProtection.cs`; Worker `Openness/Plc/BlockProtection/PlcBlockProtectionService.cs`; Host
`TiaMcpServer/Plc/BlockProtection/` (Test-Glob); FakeWorker `plc-write-roundtrip`):

| Operation | Werkzeug, Fähigkeit | Eingaben | Siemens-Member | Rücklesen |
|---|---|---|---|---|
| `get_block_protection` | `plc_read`, `Observe` (jeder Modus) | `blockPath` | `PlcBlock.IsKnowHowProtected`, `IsDefined`, `IsProtected`, `GetInvalidPasswordCharacters()` | – |
| `set_block_know_how_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockProtectionProvider.Protect(SecureString)` | `IsKnowHowProtected` = true |
| `remove_block_know_how_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` (aktuelles) | `PlcBlockProtectionProvider.Unprotect(SecureString)` | `IsKnowHowProtected` = false |
| `define_block_write_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockWriteProtectionProvider.Define(SecureString)` | `IsDefined` = true |
| `set_block_write_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockWriteProtectionProvider.Protect(SecureString)` | `IsProtected` = true |
| `remove_block_write_protection` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` | `PlcBlockWriteProtectionProvider.Unprotect(SecureString)` | `IsProtected` = false |
| `change_block_write_protection_password` | `plc_write`, `ProjectMutation` | `blockPath`, `passwordVariable` (aktuelles), `newPasswordVariable` | `PlcBlockWriteProtectionProvider.Change(SecureString, SecureString)` | `IsDefined` = true (das neue Passwort selbst ist nicht lesbar) |

- **Passwörter.** Nur Passwortquellen, nie Werte: der NAME einer Umgebungsvariablen oder `{ "credential": "TiaMcp/..." }`
  (`PasswordSources`, dieselbe Schwärzung beim Binden wie bei jeder Passworteingabe). Ein fehlendes `passwordVariable` kommt
  aus der Passwortzuordnung des Servers mit dem Zweck `KnowHowProtection` (Know-how-Schreibvorgänge) oder `BlockWriteProtection`
  (Schreibschutz; beim Ändern das aktuelle Passwort) für die PLC, die der Bausteinpfad nennt; `newPasswordVariable` kommt nie
  aus der Zuordnung. Die Passwortverwaltung bietet beide Zwecke an ("Know-how-Schutz (Bausteine)", "Schreibschutz (Bausteine)").
  Die Zwecke des Bausteinschutzes werden nie als Antwort auf Lade- oder Online-Passwortabfragen angeboten. Ergebnisse und Audit
  enthalten nur die Quellen (`result.passwordSources[] = { role, purpose, kind, source }`).
- **Bindung.** Gegen keinen Schutztyp wird kompiliert, kein Stub-Member wurde ergänzt. Beide Provider werden über die
  `GetServiceInfos()` des Bausteins mit exaktem vollem Namen gefunden (die XML sagt nicht, welches Objekt den
  Schreibschutz-Provider anbietet); nicht gelistet oder nicht geliefert ist `unsupported_capability`. Jedes Member wird am
  laufenden Provider über exakten Namen und `SecureString`-Parameter gebunden (Siemens-freier `PlcBlockProtectionRun`, mit dem
  FakeWorker geteilt); fehlt es, ist das `api_member_missing`. Alles wird vor dem ersten Aufruf gebunden, eine Ablehnung ändert
  also nichts.
- **Unzulässige Zeichen.** Ein Passwort, das der Aufruf neu setzt (das Know-how-Passwort, das definierte Schreibschutzpasswort,
  das neue Passwort beim Ändern), wird zuerst gegen `GetInvalidPasswordCharacters()` des Providers geprüft, wie der Siemens-Skill
  empfiehlt; enthält es eines, scheitert der Aufruf mit `validation_error`, nichts geändert, und die Meldung nennt die Zeichen
  von TIA Portal, aber nie, welches das Passwort enthält. Ohne bindbares `GetInvalidPasswordCharacters()` entfällt die Prüfung
  mit Hinweis, TIA Portal entscheidet.
- **Planung und Guards.** Ein Planungslesezugriff (`get_block_protection`) zeigt den aktuellen Zustand im Effekt und sagt, ob
  der Baustein ein F-Baustein ist (ProgrammingLanguage `F_...`). Ein fehlender oder mehrdeutiger Baustein lehnt den Aufruf ab,
  bevor etwas läuft; ein sonst nicht lesbarer Zugriff informiert (`plc_state_unverifiable`), der Eintrag läuft; ist die PLC eine
  F-CPU oder ihre Safety-Administration nicht lesbar, löst er zusätzlich den Abnahme-Guard `plc_safety_acceptance_required` aus
  (Fail-safe, Sicherheits-Review 2026-11-03 F7; read-write bestätigt; kein Anmelde-Block für einen Baustein unbekannter Art). Für F-Bausteine
  gilt die Regel der Safety-Anmeldung von TIA Portal: der Abnahme-Guard, und der Anmelde-Block-Guard nur, wenn ein
  Safety-Passwort gesetzt und die Sitzung nicht angemeldet ist; der Worker prüft das vor der Änderung erneut (`PlcSafetyGate`).
  Info-Guards `plc_sets_block_protection` (setzen, definieren, ändern: Quelle verfügbar halten) und
  `plc_lowers_block_protection` (entfernen).
- **Verifikation.** Der Worker liest die dokumentierte Zustandseigenschaft direkt nach dem Aufruf zurück; ein lesbarer
  abweichender Wert scheitert mit `postcondition_failed`; ein passendes Rücklesen ist die Verifikation (Prüfung
  `blockProtection`); ein nicht lesbares ist `readBack: unreadable` und bleibt `trusted`. Ein späterer Eintrag auf denselben
  Baustein und denselben Schutz ersetzt einen früheren.
- **Weggelassen.** Kein V21-Member liest ein Passwort zurück oder sagt, welches gesetzt ist; eine Änderung wird daher nur über
  `IsDefined` verifiziert. Know-how-Schutz von PLC-Datentypen: die Referenz nennt `PlcType.IsKnowHowProtected`, aber keinen
  Schutz-Provider für Typen, daher nicht angeboten. Das Online-Verhalten (Laden geschützter Bausteine) ist Sache von TIA Portal.
- **Noch nötige Live-Abnahme:** welches Objekt `PlcBlockWriteProtectionProvider` listet, ob `Protect` einen übersetzten oder
  konsistenten Baustein braucht, was TIA Portal ablehnt (schon geschützt, falsches Passwort), und dass ein geschützter Baustein
  weiter übersetzt und geladen wird.

## Projektsprachen und Projekttexte (`plc_read`/`plc_write`)

> **Nicht live getestet** (GitHub-Issue #8). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`:
> `P:Siemens.Engineering.ProjectBase.LanguageSettings`; `LanguageSettings.Languages` ("The collection of all supported
> languages"), `.ActiveLanguages`, `.EditingLanguage`, `.ReferenceLanguage`; `LanguageAssociation.Add(Language)`,
> `.Remove(Language)`, `.Find(CultureInfo)`; `LanguageComposition.Find(CultureInfo)`; `Language.Culture`;
> `P:Siemens.Engineering.ProjectBase.TextCategories`, `TextCategory.Identifier`/`.Name`, `TextCategoryComposition.Find(String)`;
> `M:Siemens.Engineering.ProjectBase.ExportProjectTexts(System.IO.FileInfo,System.Globalization.CultureInfo,System.Globalization.CultureInfo)`
> und `ExportProjectTexts(FileInfo, Language, IEnumerable{Language}, IEnumerable{TextCategory}, IEnumerable{IEngineeringObject})`
> ("Export project text to an xlsx file"); `M:Siemens.Engineering.ProjectBase.ImportProjectTexts(System.IO.FileInfo,System.Boolean)`
> mit Rückgabe `ProjectTextResult` (`Path`, `State`: `ProjectTextResultState` `Error`, `Info`, `Warning`).

Projektweite Operationen (kein `plcName`); `LanguageSettings` und Export/Import der Texte sind Member von `ProjectBase`, sie laufen
also in eigenständigen Projekten und lokalen Sitzungen (freigegeben wie `plc_read`/`plc_write`). Namen in
`ProjectLanguageMethods` (Contracts `Project/ProjectLanguages.cs`), Parameter `WorkerRequest.ProjectLanguages`, Dispatch
`Program.ProjectLanguages.cs`, Worker `Openness/Project/ProjectLanguagesService.cs`, Host `TiaMcpServer/Plc/Project/` (Test-Glob),
FakeWorker `plc-write-roundtrip` (`PlcWriteRoundtripScenario.ProjectLanguages.cs`, `ProjectLanguageDoubles.cs`). Jedes Member wird
zur Laufzeit vom Siemens-freien `ProjectLanguagesRun` gebunden; fehlt eines, ist das `api_member_missing`, nichts geändert. Kein
Stub-Member wurde ergänzt.

| Operation | Werkzeug, Fähigkeit | Eingaben | Verhalten |
|---|---|---|---|
| `get_project_languages` | `plc_read`, `Observe` (jeder Modus) | keine | `state { active[], editing, reference }` (Kulturnamen), `supported[]` (`LanguageSettings.Languages`), `textCategories[] { identifier, name }`, `notes` für nicht lesbare Teile. |
| `set_project_languages` | `plc_write`, `ProjectMutation` | mindestens eines von `addLanguages`, `removeLanguages` (je 1-64 Kulturnamen), `editingLanguage`, `referenceLanguage` | Jede Kultur wird zuerst mit `Languages.Find(CultureInfo)` aufgelöst (nicht unterstützt: `target_not_found`, nichts geändert); dann `ActiveLanguages.Add` je Hinzufügung, `ReferenceLanguage` und `EditingLanguage` werden zugewiesen, dann `ActiveLanguages.Remove` je Entfernung; zurückgelesen (`postcondition_failed` bei Abweichung); Prüfung `projectLanguages`. |
| `export_project_texts` | `plc_write`, `ProjectMutation` (schreibt eine Datei des Aufrufers) | `filePath` (`.xlsx`), `sourceLanguage`, `targetLanguages` (1-64); optional `textCategories`, `deviceNames`, `overwrite` | Ein Ziel und kein Filter: `ExportProjectTexts(FileInfo, CultureInfo, CultureInfo)`; sonst die gefilterte Überladung mit den genannten (oder allen) Textkategorien und den genannten (oder allen) Geräten des Projekts (`Devices` und Gerätegruppen). Neben dem Ziel bereitgestellt, an seinen Platz verschoben, neu gehasht (Prüfung `file`). |
| `import_project_texts` | `plc_write`, `ProjectMutation` | `filePath` (`.xlsx`); optional `updateSourceLanguage` (Standard false) | `ImportProjectTexts(FileInfo, Boolean)` einer privaten Kopie; danach ein Verifikationsexport (Prüfung `importedRows`). |

- **Bearbeitungs- und Referenzsprache.** Die XML dokumentiert beide Eigenschaften, aber keinen Setter; der Worker weist sie nur
  über eine öffentliche beschreibbare Eigenschaft genau dieses Namens zu, die die `Language` annimmt (sonst `api_member_missing`,
  nichts geändert, da alles vor dem ersten Aufruf gebunden wird). Reihenfolge: Hinzufügungen, Referenz-, Bearbeitungssprache,
  Entfernungen, so kann ein Aufruf die Bearbeitungssprache von einer Sprache wegschalten, die er entfernt. TIA Portal entscheidet,
  ob eine verwendete Sprache entfernt werden kann und was mit ihren Texten geschieht. Info-Guard `plc_changes_project_languages`;
  der Effekt zeigt den aktuellen Zustand aus einem Planungslesezugriff (`get_project_languages`; nicht lesbar informiert mit
  `plc_state_unverifiable`). Texte in einer Sprache, die das Projekt nicht hat, scheitern (live E4); zuerst die Sprache mit
  `set_project_languages` hinzufügen.
- **Exportdatei.** Das dokumentierte Format ist XLSX, `filePath` muss daher auf `.xlsx` enden (Host und Worker). Es gelten die
  Regeln für Dateien des Aufrufers wie bei den Meldungs-Exporten (`PlcAlarmFiles`: kein Überschreiben ohne `overwrite:true`,
  Info-Guard `plc_export_overwrites_file`, nie über eine Verknüpfung, temporäre Datei neben dem Ziel, neuer Hash). Der Rückgabetyp
  des Exports ist nicht dokumentiert: liefert er ein Objekt, werden `State` und `Path` als Text gelesen (`result.outcome`); `Error`
  scheitert, `Warning` ist ein Hinweis. Ohne `deviceNames` oder `textCategories` erhält die gefilterte Überladung alle Geräte und
  Kategorien (im Ergebnis gelistet), weil die Referenz nicht sagt, was eine leere Sammlung bedeutet.
- **Import und Verifikation.** Der Import liest eine private Kopie der Datei (`bytes`, `sha256` sind die der Kopie); `State`
  `Error` oder ein nicht lesbarer Zustand lässt den Eintrag scheitern (das Projekt kann schon geändert sein), `Warning` ist ein
  Hinweis, `Info` ist Erfolg. Danach folgt ein Verifikationsexport (gefilterte Überladung: die Bearbeitungssprache als Quelle, jede
  andere aktive Sprache als Ziel, alle Kategorien und Geräte), dessen Zeilen mit den importierten über Kopfzeile und Wert verglichen
  werden (`XlsxCellWorkbook.CompareRows`, zusätzliche Spalten erlaubt). Jede Zeile gefunden ist `importCheck.outcome: verified`
  (Prüfung `importedRows`); alles andere ist `inconclusive` (oder `notReadBack` ohne zweite aktive Sprache) mit dem Grund in
  `result.messages`, und der Eintrag bleibt `trusted`: der Umfang des Exports ist nicht als gleich dem der importierten Datei
  dokumentiert, und mit `updateSourceLanguage:false` wird die Spalte der Quellsprache nicht importiert. Info-Guard
  `plc_imports_project_texts`.
- **TIA-Portal-Hinweise (K75).** Live am 2026-11-03 zeigte TIA Portal nach dem Export einen modalen Hinweis („Export
  erfolgreich abgeschlossen. (0032:000002)“), und der Openness-Aufruf wartete, bis ein Mensch OK klickte. Der Worker behandelt
  jetzt jede während eines MCP-Aufrufs ausgelöste `Notification` (`NotificationEventArgs.IsHandled = true`, für jede Operation,
  siehe ARCHITECTURE §4; K98: außerhalb eines Aufrufs bleiben sie TIA Portal überlassen); die drei
  Schreiboperationen der Projektsprachen listen die während des Aufrufs behandelten in `result.portalNotifications`
  (`{ caption, text, detailText, icon, handled }`), und jeder ist auch eine Warnung. Nicht live getestet.
- **Weggelassen.** Nichts von der dokumentierten Oberfläche. Nicht angeboten: einzelne mehrsprachige Texte lesen oder schreiben
  (andere Operationen schreiben Kommentare schon je Kultur) und das Auswerten des Arbeitsmappen-Layouts (es gehört TIA Portal und
  ist nicht dokumentiert).
- **Noch nötige Live-Abnahme:** ob `EditingLanguage`/`ReferenceLanguage` in V21 beschreibbar sind, das Layout der Arbeitsmappe
  (Kopfzeile), was der Export zurückgibt, was ein Import mit `updateSourceLanguage` false ändert, und dass der
  Verifikationsexport über dieselben Kopfzeilen vergleicht.

## OPC-UA-Server-Schnittstellen und Zugriffsregeln (`plc_read`, `plc_write`)

> **Nicht live getestet** (GitHub-Issue #24). Evidenz: die V21-IntelliSense-Datei `Siemens.Engineering.Step7.xml`, Namespace
> `Siemens.Engineering.SW.OpcUa` (`OpcUaProvider.CommunicationGroup`, `OpcUaCommunicationGroup.ServerInterfaceGroup`,
> `ServerInterfaceGroup.ServerInterfaces/SimaticInterfaces/ReferenceNamespaces/AccessControl`, `Create` der Compositions,
> `ServerInterface.Export/Import/Delete/Enabled`, `SimaticInterface.Export/Delete/Enabled/UseStringNodeIds`,
> `ReferenceNamespace.Export/Import/Delete/Enabled/GenerateNodes/GeneratedInterfaceName/UseStringNodeIds`,
> `AccessControl.NamespaceAccessRestrictions/RoleMappings`, `NamespaceAccessRestriction.*`, `RoleMappingComposition.Create(String, String)`,
> `.AddStandardOpcUaRole(String)`, `RoleMapping.Delete/SetRoleName/SetDefiningNamespace/SetProjectRole/NamespacePermissions`,
> `NamespacePermission.SetPermission(String, Boolean)` und seine booleschen Eigenschaften).

Das XML sagt nicht, welches Objekt `OpcUaProvider` anbietet; der Worker nimmt ihn über den exakten vollständigen Namen aus
`GetServiceInfos()` der `PlcSoftware` (nicht aufgeführt: das Lesen meldet `providerAvailable:false` mit Hinweis, ein Schreiben
schlägt mit `unsupported_capability` fehl). Jeder Member darunter wird zur Laufzeit über seinen dokumentierten Namen und seine
Parametertypen gelesen oder aufgerufen; ein fehlender schlägt mit `api_member_missing` fehl, bevor sich etwas ändert. Es wurde
kein Stub-Member ergänzt. Namen in `PlcOpcUaMethods` (Contracts `Plc/OpcUa`), Parameter in `WorkerRequest.PlcOpcUa`, Dispatch
`Program.PlcOpcUa.cs`, Worker `Openness/Plc/OpcUa/PlcOpcUaService.cs`, Host `TiaMcpServer/Plc/OpcUa/` (Test-Glob), FakeWorker
`plc-write-roundtrip` (ein Modell im Speicher; PLC_2 bietet keinen Provider).

`plc_read` `get_opcua_configuration` (jeder Modus; optional `plcName`, ohne Angabe jede PLC) liefert pro PLC die
Server-Schnittstellen (Companion Specifications und eigene Schnittstellen), SIMATIC-Schnittstellen und Referenz-Namensräume
(`name`, `enabled`, `author`, `comment`, `creationTime`, `lastModified`, `useStringNodeIds`, `generateNodes`,
`generatedInterfaceName`), die Zugriffsbeschränkungen der Namensräume (`namespaceUri`, `namespaceIndex`, `sessionRequired`,
`signingRequired`, `encryptionRequired`, `applyRestrictionsToBrowse`) und die Rollenzuordnungen (`name`, `definedInNamespace`,
`projectRole`, `roleId`, `namespacePermissions` mit `browse`, `read`, `write`, `call`, `receiveEvents`, `readRolePermissions`); ein
unlesbarer Member ist null und in `unreadable` aufgeführt, höchstens 1.000 Einträge pro Liste.

`plc_write`-Operationen (read-write und full; `ProjectMutation`, ohne Rückfrage; jedes Item nimmt optional `plcName`, Default die einzige PLC):

| Operation | Felder | Siemens-Aufruf | Zurücklesen |
| --- | --- | --- | --- |
| `create_opcua_interface` | `interfaceKind` (`server`, `simatic`, `referenceNamespace`), `interfaceName`; `filePath` (Pflicht für `referenceNamespace`; für `server` ein XML, das direkt nach dem Anlegen importiert wird; für `simatic` abgewiesen) | `ServerInterfaceComposition.Create(String)`, `SimaticInterfaceComposition.Create(String)`, `ReferenceNamespaceComposition.Create(String, FileInfo)` | danach über den Namen aufgeführt |
| `delete_opcua_interface` | `interfaceKind`, `interfaceName` | `Delete()` | nicht mehr aufgeführt |
| `export_opcua_interface` | `interfaceKind`, `interfaceName`, `filePath`, `overwrite` | `Export(FileInfo)` in eine Zwischendatei, die an ihren Platz verschoben wird | neu gehasht (`sha256`) |
| `import_opcua_interface` | `interfaceKind` (`server` oder `referenceNamespace`), `interfaceName`, `filePath` | `Import(FileInfo)` einer privaten Kopie | ein Re-Export wird Byte für Byte mit der importierten Datei verglichen: gleich ist verifiziert, abweichend (TIA Portal kann das XML normalisieren) oder fehlgeschlagen ist `notVerified` |
| `set_opcua_interface` | `interfaceKind`, `interfaceName`; `enabled`, `useStringNodeIds` (simatic, referenceNamespace), `generateNodes` (referenceNamespace), `newName` | `SetAttribute` nach Prüfung der Schreibbarkeit über `GetAttributeInfos` (das XML sagt nicht, dass diese Eigenschaften setzbar sind; TIA Portal entscheidet über den gemeldeten Zugriffsmodus) | `GetAttribute` je Wert |
| `set_opcua_namespace_restriction` | `namespaceUri`; `sessionRequired`, `signingRequired`, `encryptionRequired`, `applyRestrictionsToBrowse` | wie oben | wie oben |
| `create_opcua_role` | `roleName`, `definingNamespace` | `RoleMappingComposition.Create(String, String)` | über den Namen aufgeführt |
| `add_opcua_standard_role` | `roleName` (eine OPC-UA-Standardrolle) | `RoleMappingComposition.AddStandardOpcUaRole(String)` | über den Namen aufgeführt |
| `delete_opcua_role` | `roleName` | `RoleMapping.Delete()` | nicht mehr aufgeführt |
| `set_opcua_role` | `roleName`; `newRoleName`, `definingNamespace`, `projectRole` | `SetDefiningNamespace`, `SetProjectRole`, `SetRoleName` (in dieser Reihenfolge) | `DefinedInNamespace`, `ProjectRole`, `Name` |
| `set_opcua_namespace_permission` | `roleName`, `namespaceUri`, `permission`, `enabled` | `NamespacePermission.SetPermission(String, Boolean)` | die Eigenschaft dieses Namens, wenn er einer von `Browse`, `Read`, `Write`, `Call`, `ReceiveEvents`, `ReadRolePermissions` ist; ein anderer Name wird durchgereicht (welche Namen `SetPermission` annimmt, ist nicht dokumentiert) und ist `notVerified` |

Guards (Info): `plc_opcua_changes_server` bei jeder Änderung (sie erreicht die CPU mit dem nächsten Laden; ein Guard je ID, die
Meldungen eines Items zusammengefasst, damit `import_opcua_interface` seinen eigenen Hinweis behält, Sicherheits-Review F9),
`plc_opcua_lowers_access`, wenn ein Beschränkungs-Flag auf `false` gesetzt oder eine Berechtigung erteilt wird,
`plc_export_overwrites_file` bei einem Export mit `overwrite:true`. Ergebnis `{ operation, plcName, target, calls[], interfaceAfter,
restrictionAfter, roleAfter, filePath, bytes, sha256, reExportSha256, evidence, note }`; Verifikations-Check `opcUa` (`verified`,
`deleted` und `file` sind Erfolg, `notReadBack` ist Erfolg false). Überholen: je Schnittstelle, Beschränkung, Rolle (und
Berechtigung) und Exportdatei.

**Ausgelassen:** `OpcUaProvider` wird nur an der PlcSoftware gesucht (wo er sonst hängt, ist nicht dokumentiert); `Author`/`Comment`
der Schnittstellen werden nur gelesen (nicht als setzbar dokumentiert); eine SIMATIC-Schnittstelle hat kein dokumentiertes
`Import`. **Live-Abnahme noch nötig:** dass die PlcSoftware den Provider aufführt, welche Attribute `GetAttributeInfos` als
schreibbar meldet, welche Berechtigungsnamen `SetPermission` annimmt und ob ein Import Byte-gleich re-exportiert.

## Laden in einen Ordner und Master-Secret online (`plc_write`)

> **Nicht live getestet** (GitHub-Issue #16). Beleg: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`:
> `DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)` und `RHDownloadProvider.Download(DirectoryInfo,
> DownloadConfigurationDelegate)` („Downloads hardware and software into a folder“), `OnlineProvider.SetPlcMasterSecret(SecureString)`,
> `OnlineProvider.ResetPlcMasterSecret()`, `OnlineProvider.State`; `RHDownloadProvider.DownloadToPrimary/DownloadToBackup(IConfiguration,
> DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)` für `plc_download rhTarget`.

Namen in `PlcDeviceDataMethods` (Contracts `Plc/DeviceData`), Parameter in `WorkerRequest.PlcDeviceData`, Dispatch
`Program.PlcDeviceData.cs`, Worker `Openness/Plc/DeviceData/PlcDeviceDataService.cs`, Host `TiaMcpServer/Plc/DeviceData/` (Test-Glob),
FakeWorker `plc-write-roundtrip` (ein In-Memory-Modell: `C:\Download` existiert, `C:\DownloadFull` ist nicht leer, `C:\DownloadDialog` löst einen TIA-Portal-Dialog aus, PLC_1 ist online und hat einen R/H-Provider, PLC_2 keins von beiden).
`RHDownloadProvider` wird unter den `GetServiceInfos()` des CPU-Device-Items über den exakten vollen Namen gefunden; Member, die nicht
in den Stubs stehen, werden zur Laufzeit gebunden (`api_member_missing` vor jedem Aufruf).

| Operation | Felder | Capability, Guard | Siemens-Aufruf | Verifikation |
| --- | --- | --- | --- | --- |
| `download_to_folder` | `directoryPath` (ein vorhandenes absolutes Verzeichnis; ein nicht leeres wird ohne `overwrite:true` abgelehnt); optional `plcName`, `rhSystem` (true: der `RHDownloadProvider` des R/H-Systems), `overwrite`, `portalConfirmations` | `ProjectMutation`, Info-Guard `plc_download_to_folder_writes_files` (mit `overwrite:true` auch `plc_export_overwrites_file`); kein Gerät wird berührt; das Verzeichnis durchläuft `PlcFileTargets.InspectDirectory` (Links, Junctions, Netzwerkorte werden gemeldet) | `Download(DirectoryInfo, DownloadConfigurationDelegate)`; Download-Konfigurationen erhalten über denselben Recorder die eingebauten Antworten von `plc_download`, jede andere bricht ab (`worker_operation_failed` mit dem Abbruchgrund) | `deviceData`: die vom Aufruf geschriebenen Dateien (Schreibzeit seit dem Aufruf), neu gehasht (höchstens 1.000); keine ist `postcondition_failed` |
| `set_plc_master_secret_online` | `passwordVariable` (ein Umgebungsvariablen-NAME oder `{ "credential": "TiaMcp/..." }`, nie das Secret); optional `plcName`, `portalConfirmations` | `OnlineControl` (read-write und full), Acknowledge-Guard `plc_master_secret_online` | `OnlineProvider.SetPlcMasterSecret(SecureString)` nur, solange `OnlineProvider.State` `Online` liest (vorher mit `plc_tia_online go_online` verbinden; sonst `state_changed`, nichts geändert) | `deviceData` notVerified (success false): kein dokumentiertes Member liest das Master-Secret zurück |
| `reset_plc_master_secret_online` | optional `plcName`, `portalConfirmations` | wie oben | `OnlineProvider.ResetPlcMasterSecret()` mit derselben Online-Prüfung | wie oben |
| `load_snapshot_as_actual_values`, `load_start_values_as_actual_values` (Paket G) | `blockPath` eines DB; optional `portalConfirmations` | `OnlineControl`, Acknowledge-Guard `plc_loads_actual_values_online` | `ValueService.LoadSnapshotAsActualValues()` / `LoadStartValuesAsActualValues()` der DB-Schnittstelle mit derselben Online-Prüfung | notVerified; siehe [Referenz Vervollständigung der V21-API](V21_API_COMPLETION_SUMMARY.md#actual-values-of-a-running-plc-plc_write) |

Der Acknowledge-Guard lässt `plc_write` jeden Aufruf mit einem Online-Master-Secret-Element bestätigen (`ConfirmsEveryCall`; read-write
fragt den Menschen mit einem Prompt, der PLC und Passwortquelle nennt, full führt ihn per Richtlinie aus). Das Secret liest der Worker
aus der Passwortquelle und übergibt es als `SecureString`; es erscheint nie im Aufruf, im Ergebnis oder im Audit.

**TIA-Portal-Dialoge (Sicherheits-Review 2026-11-03, F6; nicht live getestet).** Alle drei Operationen laufen wie `plc_download`
innerhalb von `TiaPortalSession.AnswerConfirmations`: jede TIA-Portal-`Confirmation` bis zum Einsammeln der Belege beantwortet
`portalConfirmations` des Items (`decline`, Standard: Nein, aufgezeichnet, das Item scheitert; `accept`: Ja, aufgezeichnet, eine
Warnung) und das Ergebnis listet sie (`portalConfirmations`, `portalConfirmationsObserved`); ein Dialog nach dem Schließen des
Bereichs wird an der nächsten Antwort gemeldet.

**Bereits durch `plc_download` abgedeckt:** Benutzerverwaltungsdaten beim Laden sind die Download-Konfiguration `UserManagementDownload`
(Auswahlen `KeepOnlineUserManagementData`, `UpdateUserManagementDataButKeepOnlinePassword`, `DownloadAllUserManagementDataResetToProject`):
mit `decisions: { "UserManagementDownload": "..." }` beantworten; das beim Laden abgefragte Master-Secret-Passwort ist die
Passwort-Konfiguration `PlcMasterSecretPassword` (`passwords`). Ein R/H-System lädt mit `plc_download rhTarget: "primary" | "backup"`
in seine Primär- oder Backup-CPU ([PLC-Download](PLC_DOWNLOAD_SUMMARY.de.md)). Die Offline-Konfiguration des Master-Secrets bleibt bei
den Operationen `*_plc_confidential_data_*`.

**Weggelassen:** Laden mit VoT (in den V21-IntelliSense-Dateien wurde kein Member gefunden); ein R/H-System online setzen
(`RHOnlineProvider.GoOnlineToPrimary/GoOnlineToBackup`; `plc_tia_online` bleibt beim `OnlineProvider` der PLC); die Download-Überladungen
mit `ConfigurationAddress`; Aufrufer-`decisions` für `download_to_folder`; der F-Programm-Download, den TIA Portal V21 über Openness
verweigert (K33), wird nicht umgangen. **Live-Abnahme noch nötig:** welche Konfigurationen ein Ordner-Download auslöst, welche Dateien er
schreibt, ob das CPU-Device-Item `RHDownloadProvider` listet und wie sich die Online-Master-Secret-Aufrufe verhalten.

## Einstellungen des Projekts für das Bausteinübersetzen

> **Nicht live getestet** (K10, 2026-11-02). Das Getting Started von Siemens für PLCSIM Advanced verlangt "Simulation während
> Bausteinübersetzung unterstützen" vor einem Softbus-Download; `get_project_status` meldete es als `false` für
> `MCP_Safety_01` (live 2026-11-02), und kein Tool konnte es setzen (siehe den
> [vorläufigen Live-Bericht](../superpowers/acceptance/reports/2026-11-01-fork-live-acceptance-interim.md)).

Die `plc_write`-Operation `set_project_compilation_settings` (read-write und full; `ProjectMutation`; projektweit wie die
Meldeklassen: kein `plcName`, nur eigenständige Projekte, `unsupported_capability` für eine lokale Sitzung) nimmt
`simulationDuringBlockCompilation` und/oder `virtualPlcDuringBlockCompilation` (`true`/`false`; mindestens eines; weggelassen lässt eine
Einstellung unverändert). Die Worker-Methode trägt den öffentlichen Namen (`ProjectCompilationSettingsMethods` in Contracts `Project`,
Parameter in `WorkerRequest.ProjectCompilationSettings`, Dispatch `Program.ProjectSettings.cs`, Worker
`Openness/Project/ProjectCompilationSettingsService.cs`, Host `TiaMcpServer/Plc/Project/`).

- **Mechanismus.** Dieselben Provider, die `get_project_status` liest (`Project.GetService<PlcSimulationSettingsProvider>()` /
  `GetService<VirtualPlcSettingsProvider>()`, Member-Liste der Siemens-V21-Referenz:
  `IsSimulationDuringBlockCompilationEnabled`, `IsVirtualPlcDuringBlockCompilationEnabled`). Ob diese Member auf V21 schreibbar sind,
  ist nicht dokumentiert (die gleichnamigen V20-Eigenschaften von `Project` waren lesbar/schreibbar), daher wird der Schreibzugriff auf dem
  Live-Provider durch das Siemens-freie `ProjectCompilationSettingBinding` (Contracts) gebunden: (i) eine öffentliche schreibbare `Boolean`-Eigenschaft
  mit genau diesem Namen, (ii) ein Attribut dieses Namens, das `GetAttributeInfos()` als schreibbar aufführt, gesetzt mit `SetAttribute`;
  keines von beiden ist `api_member_missing`. Jede angeforderte Einstellung wird vor dem ersten Schreibzugriff gebunden, sodass ein fehlender Weg
  nichts ändert; ein Provider, den `GetService` nicht zurückgibt, ist `unsupported_capability`. Kein Stub-Member wurde hinzugefügt (der Setter wird
  per Reflection erreicht).
- **Ergebnis und Verifikation.** `result.settings[]` = `{ setting, provider, member, requested, before, after, route,
  signature }`; `after` wird unmittelbar nach dem Schreibzugriff mit dem Getter der Eigenschaft gelesen (der Mechanismus, den der Statuslesezugriff nutzt); ein
  abweichender Wert schlägt mit `postcondition_failed` fehl. Die Verifikationsprüfung `compilationSettings` vergleicht `after` mit der Anforderung.
  Die Planung liest nichts: die Auswirkung führt die angeforderten Werte auf (`current` null); die aktuellen mit
  `get_project_status` lesen.
- **Noch erforderliche Live-Abnahme:** welcher Weg auf V21 bindet (`route`), dass `after` und ein folgendes `get_project_status`
  übereinstimmen, dass das Kontrollkästchen in der TIA-Oberfläche folgt, ob das Projekt danach übersetzt werden muss und ob ein Softbus-Download
  nach PLCSIM Advanced dann gelingt.

## Granulare Bausteinbearbeitung

Diese Operationen lassen einen Agenten ein Schnittstellen-Member, einen DB-Wert oder eine Bausteineigenschaft ändern,
ohne ein ganzes Dokument neu zu schreiben. Sie laufen unter denselben `plc_read`/`plc_write`-Verträgen
(`1.0`/`2.0`), Budgets, Regeln für Reihenfolge, Bindung, Guards und Audit wie die anderen PLC-Operationen.
**Nicht live getestet**: jede Operation unten ist nur offline qualifiziert (Linux-Unit-Tests über die
V21-SimaticML-Fixtures des Repositorys und FakeWorker-Szenarien); siehe [Live-Abnahme](#noch-erforderliche-live-abnahme).

### Lesezugriffe

- `get_block_interface` gibt `{ block, format:"xml", contentHash, documentElement, isSafety,
  sections[], messages[] }` zurück. `block` ist `{ plcName, blockPath, name, blockType, number, autoNumber,
  programmingLanguage, isKnowHowProtected, instanceOfName }`; `blockPath` ist kanonisch
  (`PLC/Blocks/.../Name`), nicht lesbare Attribute sind `null`. `contentHash` entspricht dem Hash eines
  xml-Lesezugriffs `get_block_content` desselben Zustands und kann daher als `expectedContentHash` verwendet werden. Jeder
  Abschnitt ist `{ name, members[] }` und jedes Member `{ name, path, dataType, startValue, informative,
  comments[{ culture, text }], attributes[{ name, value }], otherElements[], members[] }`: nur die
  modellierten SimaticML-Elemente werden interpretiert, jedes andere XML-Attribut eines `Member` wird
  wörtlich in `attributes` aufgeführt und jedes andere Kindelement mit Namen in `otherElements`.
- `get_type_interface` gibt `{ plcName, typePath, name, format, contentHash, documentElement,
  sections[], messages[] }` für einen PLC-Datentyp zurück (`contentHash` entspricht `get_type_content` xml).
- `get_db_values` gibt `{ block, memberPath, isComplete, messages[], values[{ path, dataType, kind,
  startValue, message, attributes }] }` für einen Global- oder Instanz-DB zurück (Array-DBs und Codebausteine sind
  `target_kind_unsupported`). Die Wertpfade stammen aus den SimaticML-Dokumenten: den eigenen
  Abschnitten des DB bei einem Global-DB, den Abschnitten `Input`/`Output`/`InOut`/`Static` des FB (oder den Membern des
  PLC-Datentyps), den `InstanceOfName` nennt, bei einem Instanz-DB; `Struct`-Member rekursieren, ein in Anführungszeichen gesetzter
  Datentyp (PLC-Datentyp oder Multiinstanz-FB) wird exportiert und aufgeklappt, und Arrays mit ganzzahligen
  Literalgrenzen werden zu `name[i]`/`name[i,j]` aufgeklappt (höchstens 256 Elemente pro Array; ein `memberPath`
  in ein Array, z. B. `Big[70000].X`, zählt nur dieses Element auf, wie groß das Array auch ist).
  Jeder `value`-Eintrag wird dann nativ mit `Interface.Members.Find(path)` und
  `GetAttribute("StartValue")` gelesen; `withAttributes:true` liest zusätzlich `ExternalAccessible`,
  `ExternalVisible`, `ExternalWritable`, `Retain` und `SetPoint`. `kind` ist `value`, `unexpanded`
  (ein Systemtyp, symbolische Array-Grenzen, ein rekursiver Typ oder ein Name, der `.` enthält; seine `message`
  sagt, warum) oder `missing` (`Find` lieferte nichts). Höchstens 400 Einträge werden zurückgegeben; danach
  `isComplete:false` und eine Meldung, die bittet, mit `memberPath` einzuschränken. Ein `memberPath`, den die Dokumente
  nicht aufzählen (zum Beispiel innerhalb eines Systemtyps), wird direkt mit `Find` nachgeschlagen.

### Bearbeitungen von Schnittstellen-Membern

`add_interface_member`, `update_interface_member` und `delete_interface_member` adressieren einen Baustein
(`blockPath` und `section`: `Input`, `Output`, `InOut`, `Static`, `Temp`, `Constant`, `Return`, so wie
der Export sie aufführt) oder einen PLC-Datentyp (`typePath`, Abschnitt `None`) und einen gepunkteten `memberPath`
innerhalb des Abschnitts (`Settings.Limits.Max`; ein Name, der `.` enthält, steht in doppelten Anführungszeichen; keine
Array-Indizes). Die Planung liest `get_block_interface` (Bausteine mit Know-how-Schutz, Instanz- und Array-DBs werden
als `target_kind_unsupported` abgelehnt), exportiert das Dokument in `xml`, prüft das optionale
`expectedContentHash` und wendet die Bearbeitung mit einem namespace-bewussten XML-Modell auf das SimaticML-Dokument an,
das jeden Knoten, den es nicht berührt, unverändert lässt (einschließlich Leerraum, Deklaration und Stil der
Zeilenumbrüche). Der Probelauf meldet die Feldänderungen (`member`, `name`, `dataType`, `startValue`,
`comment[<culture>]`) und ein begrenztes `contentDiff`. Die Änderung importiert genau das geplante Dokument
über `update_block_logic` (Bausteine; die anderen Dokumente des Bundles bleiben unverändert) oder
`update_type_content` (Typen) mit dem Hash des Exports, aus dem es berechnet wurde, sodass eine zwischenzeitliche Änderung
mit `state_changed` fehlschlägt; der Bausteinweg übersetzt und meldet `importOutcome` wie
`update_block_logic`. Das Ergebnis ist `{ format:"xml", section, memberPath, importOutcome, typeImport }`.
Die Verifikation liest die Schnittstelle erneut (`check:"interfaceMember"`) und vergleicht das Member mit dem
angeforderten Zustand (Startwert und Kommentartext ohne Groß-/Kleinschreibung und umgebende Leerzeichen verglichen; der neue
Name eines Umbenennens exakt, sodass ein vom Portal ignoriertes Umbenennen, das nur die Schreibweise ändert, fehlschlägt); ihr
`contentHash` ist der nächste `expectedContentHash`. Eine spätere Bearbeitung desselben Objekts in einem Aufruf zeigt die Vorschau
gegen das früher geplante Dokument, trägt `dependsOn` und wird vor ihrer eigenen Änderung gegen den realen Zustand neu
geplant (ein `expectedContentHash` bei einem solchen späteren Eintrag wird abgelehnt).

Fehlersichere Bausteine (`ProgrammingLanguage` beginnt mit `F_`) folgen der Safety-Anmelderegel von TIA Portal (eine Anmeldung nur, wenn das
Sicherheitsprogramm ein Passwort hat; [PLC-Safety-Referenz](PLC_SAFETY_SUMMARY.md#f-block-changes)). In der Planung abgelehnt
(nichts wird importiert): andere Dokumente als `SW.Blocks.FB/FC/OB/GlobalDB` und `SW.Types.PlcStruct`, andere Schnittstellen-Namespaces
als `.../SW/Interface/v5`, informative (System-)Member, der Abschnitt `Input` eines OB, ein
Membername, der im übergeordneten Element bereits existiert (ohne Unterscheidung von Groß-/Kleinschreibung), Hinzufügen innerhalb eines Members, das nicht
`Struct` ist, ein Startwert bei einem Member vom Typ `Struct`, Array oder mit in Anführungszeichen gesetztem Typ, Umbenennen oder Löschen von `Return` und
das Einfügen eines neuen `Comment` oder `StartValue` in ein Member, das Elemente trägt, die dieses Modell nicht
ordnet (`otherElements` ist nicht leer). Eine Datentypänderung lehnt ein Member ab, das Inhalt
seines alten Typs trägt, den das Modell nicht ordnet (verschachtelte `Sections` einer Multiinstanz, `Subelement`, jedes andere
`otherElements`), oder ein `Version`-Attribut, und ein Member mit Startwert benötigt `startValue` (ein
Literal des neuen Typs) oder `clearStartValue:true` im selben Eintrag: der alte Startwert wird weder
beibehalten noch stillschweigend verworfen.

Ein Umbenennen oder Löschen ist **kein Refactoring**: Referenzen auf das Member im eigenen Code des Bausteins, in
anderen Bausteinen und in Instanz-DBs werden nicht aktualisiert; das nächste Übersetzen meldet sie (der Guard
`plc_changes_interface` sagt das bei jeder Schnittstellenbearbeitung).

### DB-Werte, Instanz-DBs, Umbenennen, Umnummerieren, Kopieren, Verschieben

- `update_db_member` setzt `startValue` (als Text an `Member.SetAttribute("StartValue", …)` übergeben)
  und/oder die booleschen Attribute `ExternalAccessible`, `ExternalVisible`, `ExternalWritable`,
  `Retain`, `SetPoint` auf `DataBlock.Interface.Members.Find(memberPath)` eines Global- oder Instanz-DB,
  in jeder Tiefe einschließlich Array-Elementen und Multiinstanzpfaden. Jedes Attribut wird vorher und
  nachher gelesen (als Text in invarianter Kultur); eines, dessen Wert das Portal behalten hat, lässt den Eintrag mit `postcondition_failed` fehlschlagen
  (vorher gesetzte Attribute bleiben angewendet; eine gleiche Zahl in anderer Schreibweise, `1.50` für `1.5`, gilt nicht
  als "behalten"). Die Verifikation liest mit `get_db_values` erneut (`check:"dbMember"`): ein Startwert besteht,
  wenn der gespeicherte Text ohne Groß-/Kleinschreibung und Leerzeichen der Anforderung entspricht oder, nur für Member von `SInt`…`ULInt` und
  `Real`/`LReal`, wenn beide als dieselbe Zahl geparst werden (invariante Kultur). Andere
  Schreibweisen schlagen fehl, insbesondere Zeit-, Datums- und typisierte Literale (`T#1s` gegenüber `T#1000ms`), da keine
  Quelle die Normalisierungsregeln des Portals für sie angibt. Ein Schreibzugriff auf ein Member ersetzt Schreibzugriffe auf andere Member
  nicht.
- `create_instance_db` ruft `PlcBlockComposition.CreateInstanceDB(name, isAutoNumber, number,
  instanceOfName)` auf der Zielgruppe auf; eine feste `number` schaltet die automatische Nummerierung aus. Name,
  `InstanceOfName` und Nummer werden zurückgelesen.
- `rename_block` setzt `Name` über `SetAttribute("Name", …)` und liest es zurück; `renumber_block`
  setzt `AutoNumber` (und `Number`) über `SetAttribute` und liest beide zurück.
- `copy_block`, `move_block`, `move_tag_table` und `move_tag` führen ihre ändernden Schritte innerhalb eines
  `TiaPortal.ExclusiveAccess(string)` und einer `ExclusiveAccess.Transaction(project, string)` aus:
  `CommitOnDispose()` wird erst angefordert, nachdem jeder Schritt und sein Zurücklesen erfolgreich waren und
  `CanCommit` wahr ist, sodass jeder Fehler davor den gesamten Round Trip zurückrollt. Ob TIA Portal
  einen Baustein- oder Variablentabellen-Import innerhalb einer Transaktion akzeptiert, ist **nicht live getestet**; wenn es ablehnt, schlägt der
  Eintrag mit unverändertem Original fehl.
- `copy_block` exportiert das SimaticML des Bausteins, setzt `Name`, entfernt `Number` (der Import vergibt dann
  eine) und importiert es in der Transaktion in die Quellgruppe oder `targetGroupPath` mit
  `ImportOptions.None`, wendet eine `number` wie `renumber_block` an und liest die Kopie zurück; eine Abweichung
  rollt die Kopie zurück. `move_block` exportiert den Baustein, löscht ihn dann, importiert den Export in
  `targetGroupPath` und liest ihn in der Transaktion zurück. Beide lehnen Bausteine mit Know-how-Schutz ab (oder mit
  nicht lesbarem Schutz: der SimaticML-Export eines geschützten Bausteins trägt nur die Schnittstelle, und Openness bietet keinen
  geschützten Import, sodass dieser Weg seinen Code nicht transportieren kann), wenden die Safety-Anmelderegel auf fehlersichere Bausteine an, bleiben innerhalb einer PLC und eines Softwarebereichs und melden eine
  neue Objektidentität: eine Verknüpfung zum Bibliothekstyp wird nicht übernommen, und die Instanz-DBs und
  Aufrufer eines verschobenen Bausteins lösen ihn beim nächsten Übersetzen wieder über den Namen auf.
- `move_tag_table` exportiert die Tabelle (`PlcTagTable.Export`), löscht sie dann und importiert sie in den
  Zielordner (`PlcTagTableComposition.Import`, vor dem Löschen über seine dokumentierte Parameterliste
  gebunden) in der Transaktion, wobei die Anzahl der Variablen und Anwenderkonstanten verglichen wird. Die Standard-Variablentabelle wird
  abgelehnt (TIA Portal löscht sie nicht). Eine Tabelle mit einer Variable, die `IsSafety=true` meldet, wird unter der
  Safety-Anmelderegel verschoben (dieselbe Regel wie bei `move_tag`; nur abgelehnt, wenn das Sicherheitsprogramm der PLC ein Passwort hat und
  die Sitzung nicht angemeldet ist).
- Nach einem fehlgeschlagenen Verschieben wird der Ort des Originals geprüft. Zurückgerollt (der übliche Fall): sonst geschieht
  nichts. Nicht wieder in der Quelle: der Export wird dort neu importiert. Schlägt auch das fehl: der Export wird
  in einen Wiederherstellungsordner `%LOCALAPPDATA%\TiaMcpServer\recovery\<UTC timestamp>-<guid>\` geschrieben
  (neben dem Audit-Ordner; der System-Temp-Ordner nur, wenn dieser nicht beschreibbar ist) als
  `document.xml` plus eine Begleitdatei `recovery.json` mit dem ursprünglichen Namen, der Art, der PLC, Quelle und Ziel
  und dem Fehler; die Dateinamen sind fest, sodass Objektnamen nie zu Pfaden werden. Wenn kein
  Wiederherstellungsordner geschrieben werden kann, wird ein bereits auf der Festplatte liegender Export von move_tag_table aufbewahrt, statt aufgeräumt
  zu werden. Das `result` des fehlgeschlagenen Eintrags ist `{ operation, objectKind, objectName, sourcePath, targetPath,
  transaction ("rolled_back"|"outcome_unknown"), originalState ("unchanged"|"restored_by_reimport"|
  "missing"|"moved_to_target"|"unknown"; copy_block: "copy_absent"|"copy_present"), recovery
  { directory, documentPath, metadataPath, location } | null, recoveryError, messages[] }`, und die
  Fehlermeldung wiederholt das Ergebnis und einen etwaigen Pfad. Die Verifikation eines fehlgeschlagenen `copy_block` liest erneut
  dort, wo die Kopie wäre, nicht die Quelle.
- Diese Ergebnisse sind `{ operation, sourceBlockPath, block, messages[] }` (Bausteinoperationen),
  `{ operation, block, memberPath, changes[{ attribute, previous, requested, observed }] }`
  (`update_db_member`) und `{ operation, plcName, tableName, sourceFolderPath, targetFolderPath,
  tagCount, userConstantCount }` (`move_tag_table`). Die Verifikation liest den resultierenden Baustein
  (`check:"block"`, mit seinem Inhaltshash) oder die Variablentabellen (`check:"tagTable"`) erneut.

### Herkunft

| Member oder Konstrukt | Verwendung | Quelle |
| --- | --- | --- |
| `DataBlock.Interface`, `PlcBlockInterface.Members`, `MemberComposition.Find(string)`, `Member.Name` | DB-Wertpfade, Startwerte | totally-integrated-claude `tia-plc-operations/references/blocks.md` (V21-API-Referenz), `tia-import-export/references/plc-blocks.md` §9; Siemens-Skill `blocks` (`InstanceDB/GlobalDB.Interface.Members`) |
| `Member.GetAttribute/SetAttribute("StartValue" / "ExternalAccessible" / "ExternalVisible" / "ExternalWritable" / "Retain" / "SetPoint")` | `get_db_values`, `update_db_member` | `plc-blocks.md` §9 (Attributnamen; die Werttypen von `Retain`/`SetPoint` sind dort nicht angegeben und werden als boolesch angenommen) |
| `PlcBlockComposition.CreateInstanceDB(string, bool, int, string)` mit Rückgabe `InstanceDB` | `create_instance_db` | `blocks.md` §12 und API-Referenz, `plc-blocks.md` §8 |
| `GetAttribute("InstanceOfName")`, `GetAttribute/SetAttribute("AutoNumber")`, `SetAttribute("Number")` | Identität, Umnummerieren | `blocks.md` §3 und §12 |
| `SetAttribute("Name", …)` auf einem Baustein | `rename_block` | Siemens-Skill `blocks` (Umbenennen eines generierten Bausteins) |
| `PlcBlock.IsKnowHowProtected` | Ablehnungen | `blocks.md` V21-API-Referenz |
| `PlcBlock.Export`, `PlcType.Export`, `PlcBlockComposition.Import(FileInfo, ImportOptions)`, `ImportOptions.None` | SimaticML-Round-Trips | `plc-blocks.md` §1–§4, `overview.md` §4 |
| `PlcTagTable.Export(FileInfo, ExportOptions)`, `PlcTagTable.IsDefault`, `PlcTagTableComposition.Import(FileInfo, ImportOptions)` | `move_tag_table` | `tags-types.md` (V21-API-Referenz) |
| "If no block number is in the XML, it is assigned automatically" | `copy_block` | `plc-blocks.md` §1 |
| `TiaPortal.ExclusiveAccess(string)`, `ExclusiveAccess.Transaction(ITransactionSupport, string)`, `Transaction.CanCommit`, `Transaction.CommitOnDispose()`; Rollback, wenn ohne Commit oder nach einer Ausnahme verworfen | Transaktionen für Kopieren/Verschieben | totally-integrated-claude `tia-csharp-common/SKILL.md` (Exclusive access, Transactions, destructive-operation safety), `tia-devices-general/SKILL.md` (execution pattern step 6); Siemens-Skill `engineering-objects`; vorhandene Verwendung in `SubnetLifecycleService` und `VciWriter` |
| `PlcTag`-Attribut `IsSafety` | Ablehnung bei `move_tag_table` | dasselbe dokumentierte Attribut, das `move_tag` liest (`TagMutationService`) |
| "Instances are exported without type info and become detached on re-import" | Round-Trip-Guard | `overview.md` §3 |
| `ProgrammingLanguage`-Präfix `F_` kennzeichnet fehlersichere Bausteine | Safety-Anmelderegel und Abnahme-Guard | `plc-blocks.md` §6 |
| `Sections` (`.../SW/Interface/v5`), `Section Name`, `Member Name/Datatype/Informative`, `Comment/MultiLanguageText Lang`, `StartValue`, verschachteltes `Member`, `AttributeList/Name`, `Number`, `ProgrammingLanguage` | XML-Modell | Repository-Fixtures `AnalogInputSettings.xml`, `get_block_content.ob-lad*.bundle.txt`, `ObFixtures/*.xml`, `BlockSourceGenerator`; Siemens-Skill `plc-data-types` (in Anführungszeichen gesetzte UDT- und `Array[..] of`-Datentypen) |
| Typnamen `Siemens.Engineering.SW.Blocks.Interface.Member/MemberComposition/PlcBlockInterface` | Stubs | `api-baselines/v21/step7.json` |

### Nicht bereitgestellt (kein belegtes Konstrukt oder Member)

- Retain-, Zugriffs- oder andere Member-Attribute von Schnittstellen-Membern von FB/FC/OB/UDT (ihre
  SimaticML-Form ist nicht belegt; DB-Member nehmen sie nativ über `update_db_member`).
- Startwerte von Array-Elementen oder von Membern innerhalb einer PLC-Datentyp-Instanz in einer Baustein- oder
  Typschnittstelle (`Subelement` ist nicht belegt); DB-Werte nehmen sie über `update_db_member`.
- Erstellen oder Löschen von Schnittstellenabschnitten, Hinzufügen von Membern innerhalb von Array- oder UDT-typisierten Membern und
  Bearbeiten der Schnittstellen von Instanz- oder Array-DBs (die Schnittstelle eines Instanz-DB ist die seines FB).
- Schnittstellenbearbeitungen, die strukturelle Änderungen akzeptieren (`SWImportOptions.IgnoreStructuralChanges`): Die
  Schnittstellenbearbeitungen nutzen den Importer-Weg ohne Importoptionen. Ein vom Aufrufer geschriebenes SimaticML-Dokument kann
  mit ihnen über `update_block_logic`/`update_type_content` `swImportOptions` importiert werden (Audit N8, siehe die
  [Import/Export-Referenz](IMPORT_EXPORT_OPTIONS_SUMMARY.md#update-contract); nicht live getestet).
- Ein natives Verschieben von Bausteinen oder Variablentabellen: keines ist dokumentiert, daher sind Verschiebungen Export/Löschen/Import mit den
  obigen Grenzen; Kopieren und Verschieben über Software Units oder PLCs hinweg.
- Netzwerklogik: LAD/FBD/SCL-Rümpfe werden von diesen Operationen nie bearbeitet.

### Noch erforderliche Live-Abnahme

1. `get_block_interface`/`get_type_interface` auf Exporten von FB, FC, OB, Global-DB, Instanz-DB und UDT;
   `contentHash` entspricht `get_block_content`/`get_type_content`.
2. Jede Schnittstellenbearbeitung an einem FB, FC (`Return`-Datentyp), OB (`Temp`), Global-DB und UDT, einschließlich
   verschachtelter `Struct`-Member, Kommentaren in zwei Kulturen und `clearStartValue`; Übersetzungsergebnis und
   erneutes Lesen; ein FB mit Instanz-DBs (Verhalten bei struktureller Änderung).
3. `get_db_values` und `update_db_member` auf Global- und Instanz-DBs: Pfade über verschachtelte Struct, UDT-Member,
   Array-Element, mehrdimensionales Array, Multiinstanz und Systemtyp (`IEC_TIMER`);
   numerische Startwerte als Text übergeben; Werttypen von `Retain`/`SetPoint`; ein Wert, den das Portal normalisiert.
4. `create_instance_db` mit automatischer und fester Nummer; für einen FC (erwartete Ablehnung).
5. `rename_block` (einschließlich eines kollidierenden Namens), `renumber_block` (Nummer und `autoNumber`, OB-Bereiche).
6. `copy_block` und `move_block` für FB/FC/DB/OB, in eine andere Gruppe, Fehlschlag des Zielimports
   (dass das Zurückrollen der Transaktion die Quelle wiederherstellt; die Rückfallebenen Neuimport und Wiederherstellungsordner),
   die Ablehnung bei Know-how-Schutz, ein fehlersicherer Baustein unter der Safety-Anmelderegel, Instanzen von Bibliothekstypen.
7. `move_tag_table` zwischen Ordnern, der Standardtabelle (Ablehnung), einer Tabelle mit einer F-Variable
   (Ablehnung) und mit einem kollidierenden Ziel (Rollback).
8. Dass Baustein- und Variablentabellen-Importe innerhalb einer ExclusiveAccess-Transaktion überhaupt akzeptiert werden und
   dass das Neuerstellen von `move_tag` als eine Einheit festgeschrieben und zurückgerollt wird.

## Abnahmegrenze der Variablensicherheit

`plc_write` hat die tokengebundenen Sicherheits-Snapshots für Variablen ersetzt. Schreibzugriffe auf Variablen, Tabellen und Konstanten planen
gegen das Variableninventar, das ihre Guards benötigen: ein nicht lesbarer Konstantenwert blockiert,
Tabellenlöschung und Adressüberschneidungen sind info. Namenskollisionen und die Standard-Variablentabelle sind nicht mehr abgesichert;
die aufgezeichneten Fälle Duplikat/Standardlöschung schlugen als `engineering_rejected` fehl; das ist ein abgegrenzter Nachweis, keine Garantie für jede Namespace-Kollision. Der
[Validierungsbericht](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md) vom 2026-10-07
zeichnet die ursprüngliche Offline- und Live-Abnahme dieses Wegs auf, und der
[Bericht zu nativen Ablehnungen](../superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md) vom 2026-10-26
zeichnet die Änderung auf Vertrag `2.0` auf.

Historisch: PR 5 band typisierte Variablen-Snapshots an Vorschau-/Anwendungstoken. Sein
[Live-Abnahmebericht](../superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
deckt nur diesen Token-Ablauf ab und qualifiziert `plc_write` nicht. Die Bindung mehrsprachiger Kommentare pro Variable
und Änderungen an der Vollständigkeit des öffentlichen `list_tag_tables` bleiben zurückgestellt.

## Abnahmegrenze der Projektbaumsicherheit

`plc_write` hat die tokengebundenen Sicherheits-Snapshots für den Projektbaum ersetzt. Schreibzugriffe auf Bausteine und Gruppen planen
nur dort gegen einen Projektbaum-Snapshot, wo ein Guard ihn benötigt (Löschungen und OB-Erstellungen): Löschungen sind
info, das aufgezeichnete doppelte Erstellen eines Bausteins am selben Ziel schlug als `engineering_rejected` fehl, und ein nicht lesbarer Knoten macht
den Snapshot unvollständig, was als `plc_state_unverifiable` blockiert, statt zu verbergen, was eine Löschung
entfernt. Der
[Validierungsbericht](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md) zeichnet
die Offline- und Live-Abnahme dieses Wegs auf.

Historisch: PR 6 band typisierte Projektbaum-Snapshots an Vorschau-/Anwendungstoken. Sein
[Live-Abnahmebericht](../superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md)
deckt nur diesen Token-Ablauf ab und macht keine Aussage zu Speichern, Persistenz, Anlage oder Hardware.

## Aktuelle Grenzen

Die aktuelle Oberfläche bietet nicht:

- Generischen Online-/Offline-Status oder Verbindungskonfiguration.
- Compare-to-Online, Programm-Upload/-Download oder `UpdateProgram`-Abläufe.
- Operationen für Lebenszyklus oder Verwaltung von Software Units; Bausteinoperationen innerhalb vorhandener Software Units werden unterstützt. Safety Units, Safety Administration, Safety-Signaturen und Safety-Validierung werden nicht bereitgestellt.
- PLC-Meldungs-, Textlisten- und ProDiag-Funktionen über die [Referenz PLC-Meldungen und Textlisten](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md) hinaus, die jede ausgelassene Operation mit ihrem Grund aufführt (zum Beispiel typisierte Textlisteneinträge, einzelne Meldeklassen und Programmmeldungen als Objekte).
- Technologieobjekt-Funktionen über die [Technologieobjekt-Referenz](TECHNOLOGY_OBJECTS_SUMMARY.de.md) hinaus (Kurvenscheibendaten, Interpreterprogramme, Leitwertkopplungen, Startdrive-Telegrammverbindungen). Beobachtungs- und Forcetabellen werden von der [Referenz PLC online über Openness](PLC_ONLINE_OPENNESS_SUMMARY.de.md) abgedeckt.
- OPC-UA-Serverkonfiguration, Kommunikationsgruppen, Zugriffssteuerung oder Rollenzuordnung.
- Systembausteine, Abläufe für Know-how-Schutz, Webserverseiten, Bausteinfingerabdrücke oder ladbare Dateien.
