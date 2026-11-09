# Externe Quellen, Projektbaum-Ordner, Software Units, Dateiexporte und der Scan erreichbarer Geräte

**Sprache:** [English](PLC_SOURCES_FOLDERS_SUMMARY.md) · Deutsch

Operationen, die `plc_read` und `plc_write` für die Zeilen 7–10 der Adoption-Roadmap hinzugefügt wurden
([competitor-feature-adoption.md](../roadmap/competitor-feature-adoption.md)). **Nicht live getestet**:
alles Folgende ist offline verifiziert (Linux-Unit-Tests und das FakeWorker-Fixture `source-folders`,
das nur Payload-Formen modelliert) und benötigt die [Live-Abnahmeliste](#live-abnahmeliste).

| Tool | Operationen |
|---|---|
| `plc_read` | `list_external_sources`, `read_tag_comments`, `find_objects`, `scan_accessible_devices` |
| `plc_write` | `import_external_source`, `generate_blocks_from_source`, `delete_external_source`, `export_source_file`, `rename_block_group`, `rename_type_group`, `rename_tag_table_group`, `rename_watch_table_group`, `create_type_group`, `delete_type_group`, `create_tag_table_group`, `delete_tag_table_group`, `create_software_unit`, `delete_software_unit`, `rename_software_unit`, `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv`, `export_blocks` |
| `library_write` | `instantiate_master_copy` Zielart `externalSources` (eine Kopiervorlage in die externen Quellen der PLC) |

Die Tool-Anzahlen bleiben 13/33/33. Die Methodennamen sind `PlcSourceFolderMethods` in
`TiaMcpServer.Contracts/Plc`; die Worker-Parameter werden in `WorkerRequest.PlcSourceFolder` übertragen; der
Worker-Dispatch besteht aus einer Zeile plus `Program.SourcesFolders.cs`.

## Zugriffsmodi und Capabilities

| Operationen | Capability | Modi |
|---|---|---|
| `list_external_sources`, `read_tag_comments` | `Observe` (Worker-Lesezugriffe) | jeder Modus |
| `find_objects` | `Observe` (Host-Berechnung über bestehende Lesezugriffe, keine Worker-Methode) | jeder Modus |
| `scan_accessible_devices` | `OnlineObserve` (Worker-Lesezugriff) | jeder Modus, nur Standalone-Projekte |
| die fünfzehn Worker-Schreibzugriffe | `ProjectMutation` | read-write, full |
| die vier Dateiexporte | `ProjectMutation` (Host-Berechnung, keine Worker-Methode) | read-write, full |

**Entscheidung für den Scan (Owner-Entscheidung: der Zugriffsmodus ist die einzige Kontrolle).**
`ConfigurationPcInterface.GetAccessibleDevices()` führt über eine PC-Schnittstelle eine Live-Netzwerkerkennung
durch und ändert weder ein Gerät noch das Projekt. AGENTS.md definiert `OnlineObserve` genau so
(„erreicht das Gerät, ändert aber weder Gerät noch Projekt“) und erlaubt es in jedem Modus, read-only
eingeschlossen; `compare_to_online` ist genauso eingestuft. Es gibt keine Allowlist von Schnittstellen oder
Netzwerken: der aufrufende Agent nennt den Modus und die PC-Schnittstelle `{ name, number }` (kein Default, kein
Fallback, exakter ordinaler Abgleich auf der eigenen `OnlineProvider.Configuration` der PLC), und der Mensch steuert
den Scan nur über den Zugriffsmodus. Die Referenz verlangt, dass Schnittstelle und Erkennungsumfang autorisiert
werden; diese Autorisierung ist die Anfrage des Agenten unter dem gewählten Modus. Wie die anderen
Online-Pfad-Operationen ist er nur für Standalone-Projekte verfügbar (Standalone-Liste von `ProjectCapabilityCatalog`). Er läuft in einem
TIA-Portal-Bestätigungs-Scope mit festem Nein wie `compare_to_online`: jeder während des Aufrufs ausgelöste Dialog wird
mit Nein beantwortet (nie mit dem sitzungsweiten Ja von read-write/full) und protokolliert, auch während das Ergebnis gelesen wird;
ein abgelehnter Dialog lässt das Item fehlschlagen (`worker_operation_failed`, zusammen mit dem eigenen Fehler des Aufrufs gemeldet, wenn
`GetAccessibleDevices()` oder das Lesen der zurückgegebenen Geräte ebenfalls fehlschlug; ein verlorenes Portal bleibt `portal_lost`); ein
Dialog nach dem Aufruf behält das Nein, bis die nächste Anfrage beginnt.

**Dateiexporte sind Schreibzugriffe.** Sie ändern nichts in TIA Portal, schreiben aber Dateien an Pfade des Aufrufers,
daher sind sie `ProjectMutation` und laufen durch die abgesicherte `plc_write`-Pipeline (Vorschau mit
`dryRun:true`, Audit), genau wie `network_write export_io_map`.

## Externe Quellen

Jeder Scope – die PLC und jede Software Unit – besitzt seine eigene `ExternalSourceGroup`
(`PlcExternalSourceSystemGroup`). Nur seine Quellen auf Wurzelebene werden adressiert: die Bausteinreferenz gibt an, dass
der Zugriff auf Gruppen innerhalb des Ordners "External source files" nicht unterstützt wird. `unitName` wählt die Quellen einer
Software Unit; ohne Angabe die eigenen der PLC.

| Operation | Eingabe | Siemens-Aufruf | Ergebnis |
|---|---|---|---|
| `list_external_sources` | optional `plcName` (ohne Angabe: jede PLC) | Enumeration von `ExternalSources`, `PlcExternalSource.Name`, Units von `PlcUnitProvider` | `{ plcs[{ plcName, deviceName, sources[{ name, unitName }] }], complete, messages }` |
| `import_external_source` | `filePath` (absolute lokale `.scl`, `.awl`, `.db`, `.udt`; eine Unit nimmt nur `.scl`, `.db`, `.udt`), optional `sourceName` (Default: der Dateiname mit Erweiterung), `plcName`, `unitName` | `ExternalSources.CreateFromFile(name, path)`, dann `Find(name)` | `{ operation, plcName, deviceName, unitName, sourceName, filePath, readBackConfirmed }` |
| `generate_blocks_from_source` | `sourceName`, optional `generateOption` (`None` Default, `KeepOnError`), `targetGroupPath` (eine Bausteingruppe oder PLC-Datentyp-Gruppe des eigenen Scopes der Quelle; ohne Angabe oder die Wurzel: die Wurzel des Scopes), `plcName`, `unitName` | `Find(name)`, dann `GenerateBlocksFromSource(option)`, `(PlcBlockUserGroup, option)` oder `(PlcTypeUserGroup, option)` | `{ plcName, deviceName, unitName, sourceName, generateOption, targetGroupPath, generated[{ name, kind }] }` in der zurückgegebenen Reihenfolge |
| `delete_external_source` | `sourceName`, optional `plcName`, `unitName` | `Find(name)`, `PlcExternalSource.Delete()`, erneut `Find(name)` | wie beim Import (`filePath` null) |
| `export_source_file` | `filePath` (`.scl`, `.awl`, `.db` oder `.udt`), `blockPaths` und/oder `typePaths` (1–500, eine PLC, ein Scope), optional `withDependencies`, `overwrite` | `ExternalSourceGroup.GenerateSource(objects, FileInfo, GenerateOptions.None/WithDependencies)` des eigenen Scopes der Objekte | `{ filePath, overwritten, bytes, sha256, withDependencies, plcName, unitName, objects[] }` |

Die Generierungssemantik ist die von Siemens: vorhandene Bausteine und PLC-Datentypen mit den Namen, die die Quelle
deklariert, werden überschrieben; bei `None` lässt ein Fehler TIA Portal eine Exception werfen und das Generierte löschen, bei
`KeepOnError` wird keine Exception geworfen, und Objekte mit Fehlern bleiben erhalten (zum Übersetzen
`compile_check` verwenden). Der Server übersetzt nach der Generierung nicht. `generated[].kind` ist `OB`, `FB`,
`FC`, `GlobalDB`, `InstanceDB`, `ArrayDB`, `Block`, `Type` oder der Laufzeittypname von allem anderen.
`GenerateBlockOption.None` ist der zur Compile-Zeit gebundene Member, den die bestehenden Quell-Updates bereits übergeben;
`KeepOnError` hat keinen dokumentierten numerischen Wert und wird über seinen exakten Membernamen aus der installierten Assembly
entnommen (bricht mit `unsupported_capability` ab (fail closed), wenn er fehlt).

`export_source_file` lässt TIA Portal die Datei in ein frisches Staging-Verzeichnis neben dem Ziel schreiben
(`GenerateSource` verweigert eine vorhandene Datei), verschiebt sie dann mit dem gemeinsamen Writer, der unter Dateiexporte beschrieben ist, an ihren Platz (`File.Move`, oder
`File.Replace` mit einer Sicherung bei `overwrite:true`), liest sie erneut und vergleicht den SHA-256. Siemens dokumentiert AWL- und SCL-Bausteine sowie
`.udt` für PLC-Datentypen; das bestehende Lesen von Global-DB-Quellen verwendet `.db`. Was TIA Portal sonst
verweigert, lässt das Item fehlschlagen (`engineering_rejected` oder `worker_operation_failed`).

## Projektbaum-Ordner

### `find_objects`

`pattern` (erforderlich, `*` beliebige Zeichenfolge, `?` ein Zeichen, ganzer Name, ohne Beachtung der Groß-/Kleinschreibung), optional `plcName`
(ohne Angabe: jede PLC), `kinds` (Teilmenge von `block`, `block_group`, `type`, `type_group`, `tag_table`,
`tag_table_group`, `watch_table`, `force_table`, `watch_table_group`, `external_source`; ohne Angabe: alle),
`offset` (Default 0), `limit` (1–1000, Default 100). Zu den Arten gehören außerdem `software_unit` (aus dem
Baum) sowie `tag` und `user_constant` (aus `list_tag_tables`: die eigenen Variablentabellen der PLC; das Variableninventar
liest keine Variablentabelle einer Software Unit, daher werden Unit-Variablen nicht gefunden); Variablen und Anwenderkonstanten tragen `tableName`
und `folderPath` ihrer Tabelle und ihren Datentyp in `objectType`. Es wird im Host aus bestehenden Lesezugriffen berechnet,
jeder höchstens einmal pro Item und nur für die angefragten Arten: der Projektbaum-Snapshot (Bausteine, Bausteingruppen,
PLC-Datentypen und Gruppen, Variablentabellen und Ordner, der PLC und ihrer Software Units),
`list_watch_and_force_tables` und `list_external_sources`. Ein fehlgeschlagener Lesezugriff lässt das Item fehlschlagen; er wird nie
zu einer leeren Liste.

Ergebnis: `{ pattern, kinds, offset, limit, totalMatches, nextOffset, complete, messages, matches[{ kind,
name, plcName, deviceName, unitName, path, folderPath, objectType, isSystem }] }`, sortiert nach PLC,
Art (die obige Reihenfolge), Unit, Pfad und Name; `nextOffset` ist auf der letzten Seite null. `path` ist der
plc_read-Pfad für Bausteine, Bausteingruppen, PLC-Datentypen und Typgruppen (null für Systembausteine und
-gruppen, die keinen haben); Tabellen und Gruppen tragen `folderPath` (bei einer Tabelle ihr Ordner, bei einer Gruppe die
Gruppe selbst, `"/"` die Wurzel). `complete` ist false, wenn Baumknoten, das Beobachtungstabellen-Inventar oder das
Inventar der externen Quellen nicht vollständig gelesen werden konnten (`messages` sagt, welche). Das Paging liest pro
Aufruf neu; es ist kein Snapshot.

### Umbenennen von Gruppen

| Operation | Ziel | Art |
|---|---|---|
| `rename_block_group` | `blockPath` einer Bausteinbenutzergruppe, z. B. `PLC_1/Blocks/Line` oder `PLC_1/Units/U1/Blocks/Line` | `BlockGroup` |
| `rename_type_group` | `typePath` einer PLC-Datentyp-Benutzergruppe, z. B. `PLC_1/Types/Motion` | `TypeGroup` |
| `rename_tag_table_group` | `folderPath` eines Variablentabellen-Ordners, optional `unitName`, `plcName` | `TagTableGroup` |
| `rename_watch_table_group` | `folderPath` einer Gruppe von Beobachtungs- und Forcetabellen, optional `plcName` | `WatchTableGroup` |

Alle nehmen `newName` (kein `/` oder `\`). Die Gruppe wird exakt aufgelöst (ohne Beachtung der Groß-/Kleinschreibung, ein Treffer pro
Ebene, nie die Systemgruppe) und über die dokumentierte generische Attribut-API umbenannt:
`GetAttributeInfos()` muss ein schreibbares `Name` auflisten, dann `SetAttribute("Name", newName)`, und
`GetAttribute("Name")` muss den neuen Namen zurücklesen (sonst `postcondition_failed`; Portal hat früher eine kollidierende
Variablenumbenennung stillschweigend ignoriert). Die Bausteinreferenz dokumentiert genau das für Bausteingruppen;
für die anderen Gruppen ist es der generische `IEngineeringObject`-Vertrag, und eine Installation, die
`Name` nicht als schreibbar auflistet, bricht mit `unsupported_capability` ab (fail closed), bevor sich etwas ändert. Ergebnis:
`{ operation, kind, plcName, deviceName, unitName, path, newPath, oldName, newName }`.

### Anlegen und Löschen von PLC-Datentyp-Gruppen und Variablentabellen-Gruppen

| Operation | Ziel | Siemens-Aufruf |
|---|---|---|
| `create_type_group` | `typePath` der neuen Gruppe, z. B. `PLC_1/Types/Motion/Drives` oder `PLC_1/Units/U1/Types/Motion` | `PlcTypeGroup.Groups.Create(name)` der übergeordneten Gruppe (zur Laufzeit gebunden, siehe Herkunft), dann `Find(name)` |
| `delete_type_group` | `typePath` einer PLC-Datentyp-Benutzergruppe | `PlcTypeUserGroup.Delete()`, danach dürfen die Gruppen der übergeordneten Gruppe sie nicht mehr auflisten |
| `create_tag_table_group` | `folderPath` des neuen Ordners, z. B. `/Line/Spare`, optional `unitName`, `plcName` | `PlcTagTableUserGroupComposition.Create(name)` der übergeordneten Gruppe, dann `Find(name)` |
| `delete_tag_table_group` | `folderPath` eines Variablentabellen-Benutzerordners, optional `unitName`, `plcName` | `PlcTagTableUserGroup.Delete()`, danach dürfen die Gruppen der übergeordneten Gruppe sie nicht mehr auflisten |

Die übergeordnete Gruppe eines Anlegens wird exakt aufgelöst (ohne Beachtung der Groß-/Kleinschreibung, ein Treffer pro Ebene) und muss existieren; ein
Anlegen wird bei der Planung nicht auf Duplikate geprüft (Portal lehnt es ab, wie jedes andere Anlegen), und
die Systemgruppe kann weder angelegt noch gelöscht werden. Ein Anlegen, das `Find(name)` danach nicht sieht,
und ein Löschen, dessen Gruppe noch aufgelistet ist, schlagen mit `postcondition_failed` fehl. Ergebnis: `{ operation, kind,
plcName, deviceName, unitName, path, name }` (`kind` `TypeGroup` oder `TagTableGroup`). Das Löschen einer Gruppe,
die nicht leer ist, löst den Info-Guard `plc_deletes_group_contents` aus (wie `delete_block_group`) mit
den PLC-Datentypen bzw. Variablentabellen (und, für die eigenen Variablentabellen der PLC, den Anzahlen der Variablen und Anwenderkonstanten)
und Untergruppen, die es entfernt; die Untergruppen sind außerdem `removes.groups` des Effekts. Ein Teil des Baums innerhalb
der Gruppe, der nicht gelesen werden konnte, löst den Block-Guard `plc_state_unverifiable` aus.

## Software Units

| Operation | Eingabe | Siemens-Aufruf (software-units.md §2) |
|---|---|---|
| `create_software_unit` | `unitName`, optional `plcName` | `plcSoftware.GetService<PlcUnitProvider>().UnitGroup.Units.Create(name)`, dann `Find(name)` |
| `delete_software_unit` | `unitName`, optional `plcName` | `PlcUnit.Delete()`, danach dürfen die Units sie nicht mehr auflisten |
| `rename_software_unit` | `unitName`, `newName`, optional `plcName` | `SetAttribute("Name", newName)` nach einer Schreibbarkeitsprüfung mit `GetAttributeInfos`, zurückgelesen mit `GetAttribute("Name")` |

Namen dürfen kein `/` oder `\` enthalten. Eine PLC ohne `PlcUnitProvider` schlägt mit `target_kind_unsupported` fehl.
Anlegen und Löschen liefern `{ operation, kind:"SoftwareUnit", plcName, deviceName, unitName, path, name }`
(`path` `PLC/Units/Name`); das Umbenennen liefert die Form der Gruppenumbenennung mit `kind:"SoftwareUnit"`. Das Löschen einer
Unit löst `plc_deletes_group_contents` aus, wenn sie Bausteine, Bausteingruppen, PLC-Datentypen, Typgruppen,
Variablentabellen, Variablentabellen-Ordner oder externe Quellen enthält (ihre Bausteine und Bausteingruppen sind `removes`
des Effekts); ein Umbenennen löst `plc_renames_group` aus, weil sich jeder Pfad `PLC/Units/Name/...` darin ändert.
Beziehungen anderer Units zu der Unit (`PlcUnitRelation.RelatedObject`) werden weder geprüft noch
aktualisiert. Spätere Items des Aufrufs, die die Unit adressieren (über `blockPath`, `typePath` oder `unitName`), tragen
`dependsOn` auf das Anlegen, Umbenennen oder Löschen.

## Variablenkommentare (`read_tag_comments`)

Optional `plcName` (ohne Angabe: jede PLC), `tableName`, `folderPath` – die Filter von `list_tag_tables` –
über die eigenen Variablentabellen der PLC und die ihrer Software Units (`unit.TagTableGroup`, software-units.md;
`unitName` ist null für die eigenen der PLC). Ergebnis: `{ complete, messages, plcs[{ plcName, deviceName,
tables[{ unitName, folderPath, tableName, members[{ kind, name, comment }] }] }] }` mit `kind` `Tag` oder
`UserConstant` und `comment` je ein `{ culture, text }` pro Eintrag des mehrsprachigen Textes mit nicht leerem
Text (`MultilingualText.Items`, `Language.Culture`), oder null, wenn er nicht gelesen werden konnte (eine Nachricht sagt,
warum, und `complete` ist false). Eine Culture, die nicht gelesen werden kann, erhält den Schlüssel `(unreadable language #n)` (n die
Position des Eintrags) mit einer Nachricht, sodass zwei unlesbare Sprachen nie zu einem Schlüssel zusammenfallen. `PlcTag.Comment` ist ein typisiertes `MultilingualText`; `PlcUserConstant.Comment` ist
ohne Typ aufgeführt, daher wird es zur Laufzeit über seinen exakten Namen gelesen und nur als
`MultilingualText` akzeptiert. Ein Filter, der keine Tabelle trifft, schlägt mit `target_not_found` fehl.

## Dateiexporte

Gemeinsame Regeln: absolute Laufwerkspfade oder, seit 2026-11-01 (Owner-Entscheidung), UNC-Freigaben (`\\server\share\...`);
Gerätepfade `\\?\` und `\\.\` werden bei der Validierung abgewiesen, weil sie die Win32-Pfadnormalisierung umgehen; jedes Pfadsegment muss ein Name sein, den Windows behalten kann: kein Gerätename (der Stamm vor dem
ersten Punkt, ohne nachgestellte Leerzeichen, ist nicht CON, PRN, AUX, NUL, COM0–COM9, LPT0–LPT9, COM¹²³, LPT¹²³,
CONIN$ oder CONOUT$, ohne Beachtung der Groß-/Kleinschreibung), kein Segment, das auf einen Punkt oder ein Leerzeichen endet, kein Segment `.` oder `..`
(`PlcFileTargets.SegmentError`, die eine Regel für diese Operationen; `SafeFileSegment` wendet dieselbe
Gerätenamenregel an, wenn es Projektnamen dateisicher macht). **Speicherorte (Owner-Entscheidung):** ein verbundenes
Netzlaufwerk (`DriveType.Network`) ist erlaubt, und die Antwort trägt eine Warnung mit dem normalisierten
Pfad; unter Windows wird der endgültige Pfad des Zielverzeichnisses aufgelöst (`GetFinalPathNameByHandle`), und in ein lokales
Verzeichnis, das über eine Junction oder einen symbolischen Link zu einer UNC-Freigabe (`\\?\UNC\...`) aufgelöst wird, wird geschrieben, und es wird
gemeldet wie ein endgültiger Pfad anderswo (eine Warnung) (dieselbe Prüfung gilt für
`network_write export_io_map`). Das Verzeichnis muss existieren; eine vorhandene Datei wird abgewiesen, außer bei `overwrite:true` (geprüft
bei der Planung, sodass `dryRun` es meldet, und erneut beim Verschieben, das nie eine Datei ersetzt, die
zwischenzeitlich entstanden ist); jede Datei wird in eine temporäre Datei in ihrem eigenen Verzeichnis geschrieben und an ihren
Platz verschoben. Eine neue Datei wird mit `File.Move` platziert. Eine vorhandene Datei (nur bei `overwrite:true`) wird
mit `File.Replace` ausgetauscht, das die vorherige Datei als Geschwisterdatei `.<name>.<guid>.bak` behält; wo ReplaceFile
fehlschlägt (manche FAT- oder SMB-Volumes, Fehler wie 1, 50, 1175–1177 oder `PlatformNotSupported`), wird eine vorherige Datei,
die ReplaceFile bereits beiseite umbenannt hat, zuerst wiederhergestellt, dann wird die vorherige Datei in den Sicherungsnamen
umbenannt und die neue Datei hineinverschoben (unter Windows `MoveFileExW` mit `MOVEFILE_REPLACE_EXISTING |
MOVEFILE_WRITE_THROUGH`, ein geflushtes Umbenennen, nie eine Kopie), wobei die vorherige Datei wiederhergestellt wird, wenn das Verschieben fehlschlägt.
Nie wird etwas über ein Ziel kopiert, und bei jedem Fehler des Austauschs werden die vorherige Datei und der neue
Inhalt behalten und beide Pfade genannt (ein Fehler von `export_source_file` behält außerdem sein Staging-Verzeichnis). Die geschriebenen Bytes werden erneut gelesen und ihr SHA-256 gemeldet; die Sicherung wird erst gelöscht, nachdem der
Hash übereinstimmte. Ein erneutes Lesen, das fehlschlägt oder abweicht, sagt das genau: die Datei wurde geschrieben, aber nicht verifiziert,
sie liegt an ihrem Platz, und die vorherige Datei ist unter der genannten Sicherung behalten (`postcondition_failed`). Derselbe
Writer und dieselben Dateinamenregeln dienen `network_write export_io_map`. Dateien sind UTF-8 mit BOM (die
Kodierung, in der TIA Portal SimaticML und Quellen schreibt, und die, mit der der Bausteinimport bereitstellt).
Jeder Export wirkt auf eine PLC (ein ausgelassenes `plcName` erfordert genau eine PLC im Projekt).

### CSV-Exporte

`export_tag_tables_csv` (aus `list_tag_tables`), `export_watch_tables_csv` und
`export_force_tables_csv` (aus `list_watch_and_force_tables`, dann `read_watch_table`/`read_force_table`
pro Tabelle mit der Obergrenze von 5.000 Einträgen): `filePath` mit Endung `.csv`, optional `plcName`, `tableName`
(eine Tabelle; bei Beobachtungs-/Forcetabellen in `folderPath`, Default `/`), `folderPath` (Tabellen direkt in diesem
Ordner bzw. dieser Gruppe), `csvDelimiter` (`,` Default, `;`, Tab), `rawCells`, `overwrite`. Quoting nach RFC 4180,
CRLF-Zeilenenden, eine Kopfzeile. Sofern nicht `rawCells:true` gesetzt ist, erhält eine Datenzelle, die mit `=`, `+`, `-`, `@`,
einem Tab oder einem Wagenrücklauf beginnt, oder mit deren vollbreiten Formen U+FF1D `＝`, U+FF0B `＋`, U+FF0D `－`, U+FF20 `＠`
– auch nach führenden Leerzeichen, Tabs, CR, LF oder geschützten Leerzeichen – ein führendes `'`, damit Tabellenkalkulationen sie nicht
auswerten (die gemeinsame `HardwareIoMapCsv`-Regel von `export_io_map`); zu beachten ist, dass dies auch negativen
Zahlen wie `-5` das Präfix voranstellt. Ein `folderPath`, der keine Gruppe von Beobachtungs- und Forcetabellen der PLC benennt, schlägt mit
`target_not_found` fehl (wie die Variablen-CSV), außer ein unvollständiges Inventar könnte sie verbergen. Der Variablenexport umfasst
nur die eigenen Variablentabellen der PLC (das Variableninventar `list_tag_tables` liest keine Tabelle einer Software Unit).

| Export | Spalten |
|---|---|
| Variablentabellen | `RowType` (`tag`/`constant`), `Plc`, `Device`, `Folder`, `Table`, `TableIsDefault`, `Name`, `DataType`, `LogicalAddress`, `Value`, `ExternalAccessible`, `ExternalVisible`, `ExternalWritable`, dann eine Spalte `Comment[<culture>]` pro Kommentarsprache, die `read_tag_comments` liefert (ordinale Reihenfolge) |
| Beobachtungstabellen | `Plc`, `Device`, `Group`, `Table`, `TableIsConsistent`, `Index`, `Name`, `Address`, `DisplayFormat`, `MonitorTrigger`, `ModifyIntention`, `ModifyTrigger`, `ModifyValue`, `UnreadableAttributes` (`\|`-getrennt) |
| Forcetabellen | wie Beobachtungstabellen, mit `ForceIntention`, `ForceValue` statt der drei Modify-Spalten |

Ergebnis: `{ filePath, overwritten, delimiter, rawCells, neutralizedCells, plcCount, tableCount,
emptyTableCount, rowCount, bytes, sha256, complete, messages }`. `complete` ist false, wenn ein Lesezugriff
unvollständige Evidenz gemeldet hat (Inventar unvollständig, eine Tabelle über der Eintragsobergrenze); die Datei enthält, was
gelesen wurde. Der Variablenexport liest die Kommentare mit `read_tag_comments` (gleiche Filter) nach
`list_tag_tables`; ein fehlgeschlagenes Kommentarlesen lässt das Item fehlschlagen, ein unlesbarer Kommentar lässt seine Zellen leer
und macht `complete` false, und eine Variable, die das Kommentarlesen nicht auflistet (die Tabellen haben sich zwischen den beiden
Lesezugriffen geändert), wird in einer Nachricht gezählt.

### `export_blocks`

`directoryPath`, optional `plcName`, `blockPath` (eine Bausteingruppe, z. B. `PLC_1/Blocks` oder
`PLC_1/Blocks/Line`) oder `typePath` (eine PLC-Datentyp-Gruppe), `format` (`xml` Default, `source`),
`maxElements` (1–2000, Default 500), `overwrite`. Ohne Gruppe wird die ganze PLC exportiert: Bausteine und
PLC-Datentypen der PLC und ihrer Software Units. Ein Projektbaum-Lesezugriff listet die Objekte; jedes wird
mit dem bestehenden `get_block_content`/`get_type_content` im angeforderten Format gelesen, und jedes
Dokument des Lesezugriffs wird geschrieben: die Dokumente des xml-Lesezugriffs (das SimaticML-`.xml` und, falls vorhanden,
die SIMATIC SD-Begleitdateien `.s7dcl`/`.s7res`, `DocumentInfo` entfernt wie bei jedem Lesezugriff), an ihren
Markern `--- FILE: name ---` nach den Regeln des Bausteinimports aufgeteilt (kein nicht deklarierter Text vor dem ersten Marker, keine
fehlerhafte Markerzeile, kein leeres oder wiederholtes Dokument: sonst `protocol_error`), oder das eine Quelldokument, als Ganzes geschrieben (eine Quellzeile, die wie ein Marker aussieht, ist Inhalt). Das Verzeichnis spiegelt den Baum: `Blocks/<folders>/<name>.xml`, `Types/...`,
`Units/<unit>/Blocks/...`; jedes Segment wird Windows-sicher gemacht (`< > : " / \ | ? *` und Steuerzeichen werden zu `_`, nachgestellte Punkte und Leerzeichen ebenso, reservierte Gerätenamen erhalten ein führendes `_`).

Abgewiesen, bevor etwas geschrieben wird: mehr Objekte als `maxElements`, zwei Objekte, deren Dateien
nach der Bereinigung kollidieren würden (ohne Beachtung der Groß-/Kleinschreibung), eine fehlende Gruppe, vorhandene Primärdateien ohne
`overwrite:true`. Systembausteine werden nicht exportiert (sie sind kein Anwenderinhalt und haben keinen plc_read-Pfad; eine Nachricht zählt sie). Mit `format:"source"` werden nur Global-DBs (`.db`), SCL-FB/FC/OB (`.scl`)
und PLC-Datentypen (`.udt`) exportiert, jedes andere Objekt wird mit der Regel in `skipped` aufgeführt. Ein
Objekt, dessen Lesezugriff fehlschlägt, wird mit der Fehlerkategorie in `skipped` aufgeführt, und der Export läuft weiter,
außer bei `portal_lost`, `binding_conflict`, `access_denied`, `protocol_error`, `state_changed`,
`worker_timeout` und `worker_crashed`, die das Item stoppen. Ein Schreibfehler stoppt das Item, und seine
Nachricht listet die bereits geschriebenen Dateien auf (sie bleiben; es gibt kein Rollback). Ein Export mit
`overwrite:true` ersetzt die Dateien, die er schreibt, und lässt jede andere Datei im Verzeichnis unberührt: Dateien
von Objekten, die seit einem früheren Export gelöscht oder umbenannt wurden, bleiben (der Server löscht nie Dateien des
Aufrufers); das Manifest sagt, welche Dateien zu diesem Export gehören.

Zuletzt wird das Manifest `tia-mcp-export-manifest.json` in das Verzeichnis geschrieben (UTF-8 mit BOM,
deterministisches eingerücktes JSON: `format`, `plcName`, `deviceName`, `scope`, `complete`, `files[{ relativePath,
objectPath, kind, bytes, sha256 }]`, `skipped[{ objectPath, kind, reason, failureCategory }]` in Exportreihenfolge); ein vorhandenes Manifest wird wie jede Datei abgewiesen, außer bei `overwrite:true`. Ergebnis: `{ directoryPath,
format, overwrite, plcName, deviceName, scope, objectCount, fileCount, manifestPath, manifestBytes,
manifestSha256, files[{ objectPath, kind, relativePath, bytes, sha256, overwritten }], filesTruncated,
skippedCount, skipped[{ objectPath, kind, reason, failureCategory }], skippedTruncated, complete, messages }`.
`files` und `skipped` listen höchstens die ersten 50 Einträge und höchstens 20.000 (files) bzw. 10.000 (skipped)
serialisierte Zeichen, ein aufgeführter `reason` wird bei 200 Zeichen abgeschnitten, und höchstens 20 Nachrichten werden aufgeführt
(`filesTruncated`/`skippedTruncated`); das Manifest listet alles vollständig, sodass das Ergebnis bei der Obergrenze von
2.000 Objekten unabhängig von den Längen der Pfade und Begründungen unter dem Item-Budget von 60.000 Zeichen bleibt. `complete` ist false, wenn ein Objekt übersprungen wurde oder Baumknoten im Scope unlesbar waren.
Ein Dokument, auf das im xml-Bündel ein weiteres folgt, kann mit einem Zeilenumbruch enden, den das Bündelformat hinzugefügt hat.

## Erreichbare Geräte (`scan_accessible_devices`)

`mode` und `pcInterface { name, number }` (genau so, wie `get_online_state` sie auflistet; beide erforderlich),
optional `plcName`, `maxElements` (Geräteobergrenze 1–1000, Default 256). Der Worker löst Modus und PC-Schnittstelle auf der `OnlineProvider.Configuration` der PLC auf und ruft `GetAccessibleDevices()` innerhalb des
Bestätigungs-Scopes mit festem Nein auf; der Server wendet nichts auf die Verbindungskonfiguration an und ruft weder
`ApplyConfiguration` noch `GoOnline` auf (ob TIA Portal für die Erkennung intern eine Verbindung öffnet,
ist nicht dokumentiert). Der Aufruf läuft innerhalb des Worker-Request-Timeouts (`--worker-timeout-minutes`, 5–60,
Default 5); ein Scan, der es überschreitet, schlägt mit `worker_timeout` fehl, und der Worker wird neu gestartet, wie bei jeder Anfrage
mit Zeitüberschreitung. Ergebnis: `{ plcName, deviceName, mode,
pcInterfaceName, pcInterfaceNumber, deviceCount, truncated, maxDevices, devices[{ name, deviceSeries,
address, macAddress }] }`.

Der Member und die Gerätemember sind ohne Typen dokumentiert, daher werden sie zur Laufzeit gebunden
(`PlcAccessibleDeviceProjection` in Contracts, Siemens-frei): genau ein öffentliches parameterloses
`GetAccessibleDevices`, ein aufzählbares Ergebnis, jedes Element vom exakten Typ
`Siemens.Engineering.Connection.ConfigurationAccessibleDevice` (oder abgeleitet), die vier Member auf
diesem Typ über ihre dokumentierten Namen gelesen (`Name`, `DeviceSeries`, `Address`, `MACAddress`). Ein Wert wird
nur gemeldet, wenn er Text oder ein Enum-Wert ist, den ein Member benennt (ein definierter Wert oder eine `[Flags]`-Kombination, deren jedes Bit benannt ist; eine Null, die kein Member benennt, ist undefiniert); ein undefinierter Enum-Wert und jeder andere Laufzeittyp brechen
mit `unsupported_capability` ab (fail closed), wobei der Typ bzw. Wert benannt wird, sodass nichts durch eine geratene Konvertierung dargestellt wird.

## Planung, Guards und Verifikation

`plc_write` plant diese Items nur aus öffentlichen Lesezugriffen: den externen Quellen (nur für
`generate_blocks_from_source`, `delete_external_source` sowie das Löschen und Umbenennen von Software Units), dem Projektbaum der PLC (die Umbenennungen von Baustein-, Typ- und
Variablentabellen-Gruppen, das Löschen von Typ- und Variablentabellen-Gruppen, das Löschen und Umbenennen von Software Units sowie
`export_blocks`) und dem Beobachtungstabellen-Inventar (`rename_watch_table_group`). Update- und Löschziele
(eine Quelle, aus der generiert oder die gelöscht werden soll, eine Gruppe oder Unit, die umbenannt oder gelöscht werden soll) müssen existieren – fehlend ist
`target_not_found`, mehrdeutig `target_ambiguous`, unlesbare Evidenz informiert mit
`plc_state_unverifiable` (TIA Portal entscheidet) –, und Anlegevorgänge (`import_external_source`, das Anlegen von Gruppen und Units) werden nicht
auf Kollisionen geprüft (Portal lehnt sie ab). Die Quelldatei eines Imports muss bei der Planung existieren. Ein
Lesen der externen Quellen, das mit `worker_operation_failed` fehlschlägt, lässt die Planung nicht fehlschlagen: das Inventar ist
leer und unvollständig, sodass eine Quelle, aus der generiert oder die gelöscht werden soll, und die Quellen, die ein Unit-Löschen entfernt,
nicht verifizierbar sind (`plc_state_unverifiable` blockiert), und der Guard des Unit-Umbenennens sagt, dass das Inventar
unvollständig ist; ein verlorenes Portal, ein Binding- oder Protokollfehler stoppt die Planung weiterhin. Frühere Items eines Aufrufs werden
angewendet, sodass ein späteres Item eine früher importierte Quelle oder eine früher umbenannte Gruppe verwenden kann und `dependsOn` trägt
(es wird vor seiner Mutation gegen den realen Zustand neu geplant).

Info-Guards: `plc_generates_from_source`, `plc_deletes_external_source`, `plc_renames_group`,
`plc_export_overwrites_files` und das gemeinsame `plc_deletes_group_contents` für das Löschen nicht leerer Gruppen und
Units. Die Verifikation ist `trusted` (der Worker liest Importe, Anlegevorgänge, Löschvorgänge und Umbenennungen zurück; der Host
liest jede geschriebene Datei erneut) oder `superseded`. Die Verifikationsschlüssel (`PlcObjectKeys`, auch von den
bestehenden PLC-Schreibzugriffen verwendet) teilen jeden Pfad und Ordner in Segmente und markieren das letzte Segment eines Blatts
(Baustein, PLC-Datentyp, Variablentabelle, Beobachtungs- oder Forcetabelle) mit einem Steuerzeichen: der Schlüssel einer Gruppe oder eines Ordners
ist Präfix von allem darin in beliebiger Tiefe, und ein Blatt ist nie Präfix eines Containers oder Objekts
gleichen Namens (Baustein `Line` neben Gruppe `Line`). Ein Umbenennen, Anlegen oder Löschen besitzt den Pfad der Gruppe (ein Umbenennen
seinen alten und neuen Pfad); die Schlüssel einer Software Unit sind Präfix der Schlüssel ihrer Bausteine, PLC-Datentypen, externen
Quellen und Variablentabellen-Ordner; die Variablentabellen-Ordner einer Unit haben ihren eigenen Schlüsselnamensraum. Als späteres Item
bietet `generate_blocks_from_source` die Schlüssel jedes Bausteins und PLC-Datentyps seiner PLC an: es kann
Objekte überschreiben, die der Planer nicht benennen kann, daher werden frühere Baustein- und Typ-Items der PLC als
`superseded` gemeldet statt mit einem Inhalts-Hash, den es veraltet gemacht haben könnte (nur Inhalts-Items von Bausteinen und PLC-Datentypen,
unabhängig von der verwendeten Pfadform; Container-Operationen, Quellimporte und andere Generierungen
werden davon nicht abgelöst). Operationen auf Bausteingruppen, PLC-Datentyp-Gruppen und Software Units besitzen ihren Schlüssel in
einem separaten Container-Namensraum, sodass eine spätere Operation auf demselben oder einem umschließenden Container sie ablöst,
Inhalts-Items dagegen nicht. Wenn dem späteren Item ohne Zurücklesen vertraut wird, sagt die Nachricht, dass der
Endzustand nicht verifiziert ist, statt "verified there". Ein abgelöstes Item benennt, was das
spätere Item für es bedeutet: ein Umbenennen sagt, dass das Objekt nicht erneut verifiziert wird und wo es erneut zu lesen ist
(`re-read it at '<new path>'`), ein Löschen, dass es weg ist, eine Generierung, dass es überschrieben worden sein könnte.

Ein späteres Item hängt (`dependsOn`) vom letzten früheren Item ab, das seinen eigenen Schlüssel, einen seiner
Container oder – neu mit diesen Operationen – die Software Unit, die sein Pfad nennt, einen Variablentabellen-Ordner über
seiner Tabelle, eine PLC-Datentyp-Gruppe über seinem Typ oder die Zielgruppe von `generate_blocks_from_source` berührt hat.
Wie bei `delete_block_group` hängt ein Gruppenlöschen von Items ab, die die Gruppe oder eine Gruppe darüber berührt haben,
nicht von Items darin; seine Planung listet dennoch auf, was diese Items angelegt haben.

## Herkunft

| Member | Quelle | Status |
|---|---|---|
| `PlcSoftware.ExternalSourceGroup`, `PlcUnit.ExternalSourceGroup` (`PlcExternalSourceSystemGroup`) | blocks.md §10; software-units.md §7 und Memberliste; software-hierarchy SKILL.md | bestehende Stubs |
| `PlcExternalSourceComposition.CreateFromFile(String, String)` → `PlcExternalSource` | API-Referenz external-sources.md; blocks SKILL.md (`CreateFromFile(sourceFile.Name, sourceFile.FullName)`) | bestehender Stub, typisierter Aufruf, nicht live getestet |
| `PlcExternalSourceComposition.Find(String)` → `PlcExternalSource` | API-Referenz external-sources.md; blocks.md §13 (typisiertes Beispiel) | **Stub hinzugefügt**, typisierter Aufruf |
| Enumeration von `PlcExternalSourceComposition` | external-sources.md (`GetEnumerator`); software-units.md §7 | bestehender Stub |
| `PlcExternalSource.Name` | external-sources.md ("The name of the Plc external source"); Typ `string` wie bei jedem `Name`-Member dieser Referenzen (Präzedenzfall `PlcWatchAndForceTableGroup.Name`) | **Stub hinzugefügt** (Getter) |
| `PlcExternalSource.GenerateBlocksFromSource(GenerateBlockOption)`, `(PlcBlockUserGroup, GenerateBlockOption)`, `(PlcTypeUserGroup, GenerateBlockOption)` → `IList<IEngineeringObject>` | external-sources.md; blocks.md §10, §18 | bestehende Stubs, typisierte Aufrufe |
| `GenerateBlockOption.None`, `.KeepOnError` | blocks.md §10 (Semantik) | `None` bestehend (Präzedenzfall: BlockImporter); `KeepOnError` **über den exakten Namen zur Laufzeit** (kein dokumentierter Wert) |
| `PlcExternalSource.Delete()` | external-sources.md; blocks.md §13 | bestehender Stub |
| `PlcExternalSourceSystemGroup.GenerateSource(IEnumerable<IGenerateSource>, FileInfo, GenerateOptions)`; `GenerateOptions.None`/`WithDependencies` | external-sources.md; blocks.md §10; software-units.md §7 | bestehende Stubs (Präzedenzfall: die Quell-Lesezugriffe) |
| Gruppendurchläufe von `PlcBlockUserGroup`, `PlcTypeUserGroup`, `PlcTagTableUserGroup`, `PlcWatchAndForceTableUserGroup` (`Groups`, `Name`) | blocks.md §1, §4; software-units.md §3; watch-force-tables.md | bestehende Stubs |
| Gruppenumbenennung: `IEngineeringObject.GetAttributeInfos()`, `EngineeringAttributeInfo.Name`/`.AccessMode`, `SetAttribute("Name", …)`, `GetAttribute("Name")` | blocks.md §4 (Bausteingruppen: `SetAttribute("Name", …)`); engineering-objects SKILL.md (Attribut lesen/schreiben) | bestehende Stubs; gemeinsames `LibraryReflection.Rename` |
| `OnlineProvider.Configuration`, `ConnectionConfiguration.Modes`, `ConfigurationMode.Name`/`.PcInterfaces`, `ConfigurationPcInterface.Name`/`.Number` | online-connection-configuration.md §1–2; online-and-download SKILL.md | bestehende Stubs |
| `ConfigurationPcInterface.GetAccessibleDevices()` | online-connection-configuration.md §2, §5 (Rückgabetyp nicht dokumentiert) | **per Laufzeit-Lookup aufgerufen** (genau eine öffentliche parameterlose Methode); kein Stub |
| `ConfigurationAccessibleDevice` (Typ), `.Name`, `.DeviceSeries`, `.Address`, `.MACAddress` | online-connection-configuration.md §5 (Membertypen nicht dokumentiert); Typliste base.json | **zur Laufzeit über den exakten Namen gelesen**, nur Text- oder Enum-Werte; kein Stub |
| `PlcTagTableUserGroupComposition.Create(String)` → `PlcTagTableUserGroup` | tags-types.md §2 (typisiertes Beispiel `PlcTagTableUserGroup myGroup = groupComposition.Create(...)`) und V21-API-Liste | **Stub hinzugefügt**, typisierter Aufruf |
| `PlcTagTableUserGroupComposition.Find(String)` | tags-types.md §1, §2 | bestehender Stub |
| `PlcTagTableUserGroup.Delete()` | tags-types.md §2 (`group.Delete()`), V21-API-Liste ("Deletes this instance") | **Stub hinzugefügt** |
| `PlcTypeUserGroupComposition.Create(String)` | tags-types.md V21-API-Liste ("Create the user folder for the PLC data type collection"; kein Rückgabetyp, kein Beispiel) | **zur Laufzeit gebunden** über den exakten Namen und `(String)` (`LibraryReflection.Invoke`, `unsupported_capability`, wenn er fehlt); kein Stub; der Rückgabewert wird ignoriert, und die Gruppe wird mit `Find` zurückgelesen |
| `PlcTypeUserGroupComposition.Find(String)` → `PlcTypeUserGroup` | blocks.md §18 (typisiertes Beispiel `PlcTypeUserGroup typeFolder = plc.TypeGroup.Groups.Find("Group_1")`), tags-types.md V21-API-Liste | **Stub hinzugefügt**, typisierter Aufruf |
| `PlcTypeUserGroup.Delete()` | tags-types.md V21-API-Liste ("Deletes this instance") | **Stub hinzugefügt** |
| `PlcSoftware.GetService<PlcUnitProvider>()`, `PlcUnitProvider.UnitGroup.Units` | software-units.md Zugriffseinstiegspunkt | bestehende Stubs |
| `PlcUnitComposition.Create(String)` → `PlcUnit`, `PlcUnitComposition.Find(String)` → `PlcUnit` | software-units.md §1–§2 (typisierte Beispiele `PlcUnit unit = ...Units.Find("Unit_1")`, `PlcUnit unit1 = unitComposition.Create("Unit_1")`), V21-API-Liste | **Stubs hinzugefügt**, typisierte Aufrufe |
| `PlcUnit.Delete()` | software-units.md §2 und V21-API-Liste | **Stub hinzugefügt** |
| Umbenennen einer Software Unit: `SetAttribute("Name", …)` | software-units.md §2 (einer der drei dokumentierten Wege; `Name` Read/Write) | bestehende generische Stubs; gemeinsames `LibraryReflection.Rename` |
| `PlcTag.Comment` (`MultilingualText`), `MultilingualText.Items`, `MultilingualTextItem.Language`/`.Text`, `Language.Culture` | tags-types.md V21-API-Liste; Präzedenzfall `move_tag` (TagMutationService) | bestehende Stubs |
| `PlcUserConstant.Comment` | tags-types.md V21-API-Liste ("The comment of the user constant"; kein Typ) | **zur Laufzeit über den exakten Namen gelesen**, nur als `MultilingualText` akzeptiert; kein Stub |
| `PlcExternalSourceComposition.CreateFrom(MasterCopy, MasterCopyMode)` (`library_write`-Ziel `externalSources`) | external-sources.md V21-API-Liste | **zur Laufzeit gebunden** wie die anderen `instantiate_master_copy`-Ziele (`LibraryReflection.Invoke` über die exakten Parametertypen); kein Stub |
| `PlcUnitBase.TagTableGroup` (Unit-Variablentabellen in `read_tag_comments`) | software-units.md (Unterobjekte von `PlcUnit`, API-Liste) | bestehender Stub |
| TIA-Portal-Bestätigungs-Scope des Scans (`ConfirmationEventArgs`, mit Nein beantwortet) | bestehendes `TiaPortalSession.AnswerConfirmations` (Präzedenzfall compare_to_online) | bestehender Code, kein neuer Member |
| Nicht Siemens: `CreateFileW` + `GetFinalPathNameByHandleW` (kernel32), `DriveInfo.DriveType` | Win32-/.NET-Dokumentation | Prüfung des endgültigen Pfads und verbundener Laufwerke bei Dateizielen (nur Windows) |
| CSV, `export_blocks` | bestehende Lesezugriffe `list_tag_tables`, `list_watch_and_force_tables`, `read_watch_table`, `read_force_table`, `browse_project_tree`, `get_block_content`, `get_type_content`; die Variablen-CSV außerdem `read_tag_comments` | kein neuer Siemens-Member |

Die Compile-Zeit-Member werden durch
`reference-stubs/TiaMcpServer.OpennessReferenceProbe/SourceFolderReferenceSurface.cs` abgedeckt; das Bauen der
Probe gegen die installierte V21 PublicAPI prüft die deklarierten Rückgabe- und Parametertypen. Der Umfang
folgt den Zeilen 7–10 von [competitor-feature-adoption.md](../roadmap/competitor-feature-adoption.md); die beiden
Tool-Listen der Wettbewerber hinter diesen Zeilen wurden nur für Ideen herangezogen (Operationsnamen und Umfang, kein
Code, kein Text, kein Siemens-Member daraus übernommen).

## Ausgelassen, und warum

| Operation | Grund |
|---|---|
| Benutzergruppen externer Quellen (anlegen, löschen, auflisten oder Quellen darin generieren) | blocks.md §10 und §13 geben zweimal an, dass der Zugriff auf Gruppen innerhalb des Ordners "External source files" nicht unterstützt wird; die V21-API-Liste nennt zwar `PlcExternalSourceGroup.Groups`, `PlcExternalSourceUserGroupComposition.Create(String)` (Zusammenfassung: "Creates a MasterCopy"), `Find` und `PlcExternalSourceUserGroup.Delete`, aber die Aussage des Handbuchs und die widersprüchliche Zusammenfassung lassen keine dokumentierte Verwendung übrig |
| Verschieben einer beliebigen Gruppe (Baustein, Typ, Variablentabelle, Beobachtungstabelle, Technologieobjekt, Software Unit) | der ganze Baum `skills/*/references/` wurde durchsucht: der einzige dokumentierte Verschiebe-Member ist `HardwareObject.PlugMove(DeviceItem, Int32)` (Geräteelemente, tia-devices-general); keine Gruppen-Composition und keine Benutzergruppe dokumentiert ein Verschieben, und keine Referenz beschreibt das Verschieben einer Gruppe als Folge dokumentierter Schritte |
| Technologieobjekte und Technologieobjekt-Gruppen in `find_objects` | Anlegen, Löschen und Umbenennen von Gruppen und das Auflisten der Objekte sind die Technologieobjekt-Operationen (`create_technology_object_group`, `delete_technology_object_group`, `rename_technology_object_group`, `list_technology_objects`; [TECHNOLOGY_OBJECTS_SUMMARY.md](TECHNOLOGY_OBJECTS_SUMMARY.de.md)), zusammengeführt mit diesem Feature; `find_objects` listet sie noch nicht auf (es liest den Projektbaum, das Beobachtungstabellen-Inventar und die externen Quellen; das Inventar von `list_technology_objects` als weiteren Evidenz-Lesezugriff hinzuzufügen, ist ein offenes Follow-up). Technologieobjekte findet man bis dahin mit `list_technology_objects` |
| Gruppen-Kopiervorlagen in Gruppen-Compositions (`PlcBlockUserGroupComposition`, `PlcTypeUserGroupComposition`, `PlcTagTableUserGroupComposition`, `PlcWatchAndForceTableUserGroupComposition`, `TechnologicalInstanceDBUserGroupComposition` `.CreateFrom(MasterCopy[, MasterCopyMode])`) | in den V21-API-Listen dokumentiert, aber das Instanziieren von Kopiervorlagen ist die Domäne von `library_write`, deren Ziele Objekt-Compositions sind; eine Gruppen-Zielart ist eine Bibliotheksänderung (deren eigene Ausgelassen-Tabelle listet die benachbarten Lücken) |
| Eigenschaften und Beziehungen von Software Units: `Author`, `NamespacePreset`, `Comment`, Beziehungen anlegen/löschen/ändern, `Access` (Veröffentlichen), Generieren ladbarer Dateien, Unit-Kopiervorlagen | in software-units.md dokumentiert (die Eigenschaftentabelle von PlcUnit, §4–§6, §8, §10–§11); sie sind Unit-Inhalt, keine Container, und außerhalb der Zeilen 7–10. Unit-Kopiervorlagen sind das bestehende Ziel `softwareUnits` von `library_write` |
| Übersetzen nach `generate_blocks_from_source` | nicht ausgelassen: der Server übersetzt nie implizit; `compile_check` erledigt es |
| Meldetexte / Textlisten als CSV | die Meldungs- und Textlisten-Operationen sind zusammengeführt ([PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md)): sie lesen und exportieren die XLSX-Arbeitsmappe von TIA Portal (`read_*_workbook` als nicht interpretierte Zellen, `export_text_lists`, `export_alarm_texts`); eine CSV-Projektion dieser Zellen ist nicht implementiert und bleibt ein offenes Follow-up, das den CSV-Writer wiederverwenden kann |
| Variablentabellen von Software Units in der Variablen-CSV und Unit-Variablen/Anwenderkonstanten in `find_objects` | die CSV und die Arten `tag`/`user_constant` verwenden das PLC-Variableninventar (`list_tag_tables`, ein gemeinsamer Lesezugriff mit eigenem Vertrag), das nur die eigenen Variablentabellen der PLC liest; dort Unit-Variablentabellen zu lesen (`unit.TagTableGroup`, software-units.md), ändert diesen gemeinsamen Vertrag, eine Owner-Entscheidung. Kommentare von Unit-Variablentabellen werden gelesen (`read_tag_comments`, `unitName` pro Tabelle); Unit-Variablentabellen und -Ordner werden als `tag_table`/`tag_table_group` gefunden |
| `PlcUnit.Export()` | software-units.md listet `Export()` mit Rückgabe void ohne Parameter; jeder dokumentierte SimaticML-Export der Referenz nimmt `(FileInfo, ExportOptions)`, daher ist die Aufrufform nicht dokumentiert |
| Named-Value-Typdokumente (`unit.TypeGroup.Documents`, `PlcDocument`, software-units.md §9) | §9 dokumentiert nur `Documents.Find(name)` und ein schreibgeschütztes `Name`; kein Enumerations-, Export- oder Inhalts-Member, und der Projektbaum listet sie nicht auf, daher kann `find_objects` sie nicht durchsuchen |
| Meldetext-Textlisten von Software Units im Guard des Unit-Löschens | `PlcUnitBase.PlcAlarmTextlistGroup` ist aufgeführt (API-Liste in software-units.md), aber die zusammengeführten Textlisten-Lesezugriffe (`list_text_lists`, [PLC_ALARMS_TEXTLISTS_SUMMARY.md](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md)) decken nur das eigene `PlcSoftware.PlcAlarmTextlistGroup` der PLC ab, nicht das einer Unit; die Guard-Nachricht sagt, dass sie nicht aufgelistet sind |
| XLSX-Exporte | kein Siemens-Member ist beteiligt; es braucht einen OOXML-Writer (eine neue Abhängigkeit oder ein handgeschriebenes Paket), eine Owner-Entscheidung. CSV mit UTF-8-BOM öffnet sich in jedem Tabellenkalkulationsprogramm |
| Anwenden eines Online-Pfads oder Online-Gehen aus dem Scan | nicht ausgelassen: das ist `plc_tia_online`; der Scan listet nur auf |

## Bekannte Grenzen und offene Risiken

- Nichts hier ist live getestet; das FakeWorker-Fixture zeigt Payload-Formen, nicht das Verhalten von Siemens.
- Die Regeln zur Groß-/Kleinschreibung von `PlcExternalSourceComposition.Find` sind nicht dokumentiert; die Planung gleicht `sourceName`
  exakt (ordinal) ab, und der Worker verwendet `Find`.
- Ob `CreateFromFile` eine Referenz auf die Datei behält oder sie kopiert und wie TIA Portal eine
  Quelldatei behandelt, die UTF-8 ohne BOM ist, ist nicht verifiziert (blocks.md: UTF-8 mit BOM ist erforderlich, wenn
  die Quelle Sonderzeichen enthält).
- `GenerateSource` mit gemischten Bausteinen und PLC-Datentypen oder mit einer Erweiterung, die nicht zu den
  Objekten passt, ist die Entscheidung von Siemens; das Ergebnis ist nicht verifiziert.
- `GetAccessibleDevices()` kann in einem großen Netzwerk lange dauern; es läuft innerhalb des Worker-Request-Timeouts, und ein Scan, der es überschreitet, schlägt mit `worker_timeout` fehl und startet den Worker neu (das Binding
  braucht dann erneut `bind_project`, wie nach jedem Worker-Neustart). Ob es Berechtigungen braucht oder TIA-Portal-Dialoge auslöst, ist unbekannt; ein ausgelöster Dialog wird mit Nein beantwortet und lässt das Item fehlschlagen.
- Die Gruppenumbenennungen setzen voraus, dass `Name` für Typ-, Variablentabellen- und Beobachtungstabellen-Gruppen als schreibbar aufgeführt ist;
  tags-types.md gibt an, dass Namen von Variablentabellen-Gruppen ab V20 schreibbar sind (für Typ- und Beobachtungstabellen-Gruppen sagt es
  keine Referenz), daher entscheidet die Laufzeitprüfung. Eine Tool-Liste eines Wettbewerbers berichtet, dass ihr Umbenennen von Ordnern
  nur für Technologieobjekt-Gruppen funktioniert; wenn V21 `Name` für Baustein-, Typ-, Variablentabellen- oder
  Beobachtungstabellen-Gruppen als schreibgeschützt meldet, brechen die Umbenennungen mit `unsupported_capability` ab (fail closed), und nichts ändert sich. Punkt 6
  der Live-Abnahme klärt das.
- Die Speicherortprüfung löst den endgültigen Pfad des Zielverzeichnisses einmal bei der Planung und erneut unmittelbar
  vor dem Schreiben auf; eine dazwischen geänderte Junction wird nicht erkannt. Ein lokaler symbolischer Verzeichnislink (oder eine
  Junction), der auf einen Buchstaben eines verbundenen Laufwerks zeigt, wird zum UNC-Pfad der Freigabe aufgelöst, und es wird mit einer Warnung hineingeschrieben
  (UNC-Freigaben sind seit 2026-11-01 erlaubt). Verbundene Laufwerke und Freigaben warnen, es wird aber in sie geschrieben:
  das Schreiben über temporäre Datei und Umbenennen ist nur so weit atomar, wie der Dateiserver es macht.
- `find_objects` und `export_blocks` lesen den ganzen Projektbaum; ein sehr großes Projekt kann an die
  Snapshot-Grenze stoßen (`snapshot_too_large`).
- `PlcTypeUserGroupComposition.Create(String)` hat keinen dokumentierten Rückgabetyp; es wird zur Laufzeit gebunden, und
  das Ergebnis wird nur durch `Find(name)` bestätigt. Ob das Anlegen oder Löschen einer Typgruppe abgelehnt wird, während
  Typen darin verwendet werden, und was das Löschen einer nicht leeren Gruppe mit Bausteinen macht, die ihre Typen verwenden, ist
  Verhalten von Siemens und nicht verifiziert.
- Das Löschen einer Software Unit entfernt alles darin; ob TIA Portal es ablehnt, während andere Units
  Beziehungen zu ihr haben, und ob ein Unit-Umbenennen die Beziehungen anderer Units aktualisiert, ist nicht verifiziert. Keines von beiden wird
  vom Planer geprüft.
- Die Kommentarspalten der Variablen-CSV hängen von den gefundenen Sprachen ab, daher können zwei Exporte verschiedener Projekte
  unterschiedliche Kopfzeilen haben.

## Live-Abnahmeliste

Auf einem Wegwerf-Standalone-Projekt mit einer S7-1500-PLC und einer Software Unit, im Modus `full`:

1. `list_external_sources`; `import_external_source` einer SCL-FB-Datei (mit und ohne `sourceName`),
   einer `.udt`, einer `.db` und eines AWL-Bausteins `.awl` in die PLC und einer `.scl` in die Unit; denselben
   Namen erneut importieren (die Ablehnung durch Portal protokollieren); ein UNC-Freigabepfad (akzeptiert) und ein `\\?\`-Pfad (abgewiesen).
2. `generate_blocks_from_source` mit `None` in die Wurzel, in eine Bausteinbenutzergruppe und (für die
   `.udt`) in eine Typbenutzergruppe; eine Quelle mit einem Fehler einmal mit `None` (nichts behalten) und einmal mit
   `KeepOnError` (Objekte behalten); `generated` prüfen und übersetzen.
3. `delete_external_source`; bestätigen, dass die generierten Bausteine bleiben.
4. `export_source_file` für zwei SCL-Bausteine mit und ohne `withDependencies`, einen UDT nach `.udt`, einen
   Unit-Baustein (Unit-Scope), in eine vorhandene Datei ohne und mit `overwrite`; die Datei mit einem
   manuellen "Generate source from blocks" vergleichen.
5. `find_objects` mit `*`, `?`, Arten und Paging auf einem Projekt mit verschachtelten Gruppen, Systembausteinen,
   Beobachtungstabellen und externen Quellen.
6. `rename_block_group`, `rename_type_group`, `rename_tag_table_group` (PLC und Unit) und
   `rename_watch_table_group`; Umbenennen in den Namen eines vorhandenen Geschwisterelements (das Ergebnis des Zurücklesens protokollieren);
   protokollieren, ob `Name` für jede Gruppenart als schreibbar aufgeführt ist.
7. `export_tag_tables_csv`, `export_watch_tables_csv`, `export_force_tables_csv` (alle Tabellen, eine
   Tabelle, ein Ordner); in einer Tabellenkalkulation öffnen; neutralisierte Zellen und `rawCells` prüfen.
8. `export_blocks` für die ganze PLC in `xml` und `source`, für eine Gruppe, bei vorhandenen know-how-geschützten und
   fehlersicheren Bausteinen; eine exportierte `.xml` mit `update_block_logic` erneut importieren.
9. `scan_accessible_devices` auf der PLCSIM-Schnittstelle und auf einem realen Ethernet-Adapter mit Geräten an
   der Leitung, im Modus read-only; die Laufzeittypen von `DeviceSeries`, `Address`, `MACAddress`
   und die Dauer protokollieren.
10. `create_type_group` (PLC-Wurzel, verschachtelt, Unit) und `delete_type_group` (leer; mit einem Typ, der von einem
    Baustein verwendet wird; das Verhalten von Portal protokollieren); den Laufzeit-Rückgabetyp von `PlcTypeUserGroupComposition.Create` protokollieren.
11. `create_tag_table_group` und `delete_tag_table_group` (PLC und Unit, leer und mit Tabellen); ein
    doppeltes Anlegen (die Ablehnung durch Portal protokollieren).
12. `create_software_unit`, `rename_software_unit` (protokollieren, ob `Name` als schreibbar aufgeführt ist und ob
    Beziehungen einer anderen Unit dem Umbenennen folgen) und `delete_software_unit` einer Unit mit Bausteinen, Typen,
    Variablentabellen, externen Quellen und einer Beziehung von einer anderen Unit.
13. `read_tag_comments` auf Variablen und Anwenderkonstanten mit Kommentaren in zwei Projektsprachen, einem leeren
    Kommentar und einer Anwenderkonstante (den Laufzeittyp von `PlcUserConstant.Comment` protokollieren); die Variablen-CSV mit
    diesen Kommentaren.
14. `library_write instantiate_master_copy` einer Kopiervorlage einer externen Quelle mit `target.kind:
    "externalSources"`, jeder `masterCopyMode`.
15. Dateispeicherorte: jeder Export in ein verbundenes Netzlaufwerk (Warnung, Datei geschrieben), in ein lokales
    Verzeichnis, das eine Junction oder ein symbolischer Verzeichnislink auf eine Freigabe ist (abgewiesen), in eine Junction auf
    ein anderes lokales Volume (Warnung); ein `filePath` mit `CON`, `COM¹`, `nul .txt`, einem nachgestellten Punkt (bei der Validierung
    abgewiesen); `overwrite:true` auf eine Freigabe, die `ReplaceFile` nicht unterstützt (Fallback mit Beiseite-Umbenennen und
    `MoveFileExW`; die vorherige Datei wird als `.bak` behalten, bis der Hash übereinstimmte).
16. `scan_accessible_devices`, während ein TIA-Portal-Dialog erscheint (zum Beispiel beim ersten Zugriff auf eine
    Schnittstelle): das Item schlägt fehl, wobei der abgelehnte Dialog aufgeführt ist; im Modus read-write wird das sitzungsweite Ja
    nicht verwendet.
17. `export_blocks` von mehr als 50 Objekten: `filesTruncated:true`, das Manifest listet jede Datei mit
    den Hashes, die die Dateien beim Zurücklesen ergeben; ein zweiter Export mit `overwrite:true` nach dem Löschen eines Bausteins lässt
    die alte Datei des gelöschten Bausteins an ihrem Platz und listet im Manifest nur die aktuellen Dateien.
