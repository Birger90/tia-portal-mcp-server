# Hardware-Baugruppen, Hardware-Parameter, E/A-Adressen und Variablen-Extras

**Sprache:** [English](HARDWARE_MODULES_SUMMARY.md) · Deutsch

Status: **offline implementiert, nicht live getestet.** Jedes Siemens-Member unten wurde den
V21-Dokumentationsquellen entnommen, die unter [Herkunft](#herkunft) aufgeführt sind; keines wurde gegen ein
installiertes TIA Portal V21 verifiziert. Die FakeWorker-Fixture modelliert nur den Objektzugriff und ist kein Nachweis für das
Verhalten von Siemens. Vor dem Verlass auf diese Operationen die [Live-Abnahmeliste](#live-abnahmeliste) an einem Wegwerfprojekt
abarbeiten.

Die Operationen erweitern bestehende Tools; sie haben kein Tool hinzugefügt (die Zählungen waren 10/27/27, 11/30/30 seit den Bibliotheks-Tools, 12/31/31 seit `tia_inbox`, 13/32/32 seit `siemens_docs`):

| Tool | Hinzugefügte Operationen |
|---|---|
| `network_read` | `list_plug_locations`, `find_module_types`, `find_container_types`, `list_io_addresses` |
| `network_write` | `plug_device_item`, `move_device_item`, `copy_device_item`, `delete_device_item`, `change_device_item_type`, `set_hardware_attributes`, `set_io_address`, `export_io_map` |
| `plc_read` | `read_address_occupancy`, `find_free_address` |
| `plc_write` | `move_tag` |

Das Lesen von Hardware-Parametern gab es bereits: `inspect_network_object` auf einem `deviceItem`-Ziel
liefert jedes modellierte und dynamische Attribut mit `access`, `supportedTypes` und einem typisierten `value`.
Das Setzen der Flags für externen Zugriff einer Variable gab es bereits als `update_tag` (`externalAccessible`,
`externalVisible`, `externalWritable`); es wurde keine zweite Operation hinzugefügt.

## Zugriffsmodi

Lesevorgänge sind `Observe` (jeder Modus). Die Hardware-Schreibvorgänge, `move_tag` und `export_io_map` sind
`ProjectMutation`: nur read-write und full, keine Bestätigungsabfrage, geschützte Einzelaufruf-Pipeline
(Validierung, verifizierte Bindung, Planungslesevorgänge, Guards, `dryRun`, Mutation, Verifikation, ein
Audit-Datensatz), genau wie die anderen `network_write`/`plc_write`-Elemente. `export_io_map` schreibt eine Datei
außerhalb von TIA Portal; es ist als `ProjectMutation` eingestuft, damit read-only nie etwas schreibt. Es gibt
keinen Block-Guard: Nur ungültige oder mehrdeutige Eingaben, ein nicht lesbares Inventar und die eigenen
Machbarkeitsantworten von TIA Portal stoppen einen Aufruf.

## Ziele

Hardware-Baugruppen-Elemente benennen ihr Objekt mit `target`:

- `{ "kind": "deviceItem", "deviceName", "itemPath": [{ index, name, positionNumber, typeIdentifier }, ...] }`
  — der Selektor, den `read_hardware_config` und `list_network_objects` liefern; aufgelöst über den exakten
  Indexpfad und gegen `name` und `positionNumber` verifiziert, gegen `typeIdentifier`, wenn er angegeben ist
  (kein Rückfall auf den Namen allein). `typeIdentifier` ist optional: eingebaute Elemente (Schnittstellen,
  Ports, Kartenleser, Display, Kanal-Unterelemente einer CPU) haben keinen, werden mit `typeIdentifier: null`
  gemeldet und sind auswählbar; eine Anfrage darf `null` oder `""` angeben oder ihn weglassen (K95, nicht live getestet);
- `{ "kind": "device", "deviceName" }` — nur für `list_plug_locations`, `plug_device_item` (den
  Container), `set_hardware_attributes` und die Custom-Identity-Operationen, und für `destination`.

Ein Hardware-Snapshot (`read_hardware_config`) plant den Aufruf, wie bei jedem `network_write`-Element:
Ein Element kann kein Gerät und keine Baugruppe zum Ziel haben, die ein früheres Element desselben Aufrufs angelegt hat. Nach erneutem Lesen
einen zweiten Aufruf ausführen.

## network_read-Operationen

| Operation | Eingabe | Ergebnis |
|---|---|---|
| `list_plug_locations` | `target` (device oder deviceItem) | `{ target, plugLocations[{ positionNumber, label }], messages }` aus `HardwareObject.GetPlugLocations()` ("information about free slots") |
| `find_module_types` | `typeIdentifier` (partiell: `OrderNumber:` ≥1 Teil, `GSD:` ≥2 Teile, `System:` ≥1 Teil) | `{ partialTypeIdentifier, moduleTypes[] }` aus `ModuleInformationProvider.FindModuleTypes` |
| `find_container_types` | `typeIdentifier` | `{ typeIdentifier, containerTypes[] }` aus `ModuleInformationProvider.FindContainerTypes` (laut Dokumentation nur Baugruppen der Netzsicht) |
| `list_io_addresses` | `target` (deviceItem) | `{ target, addresses[{ index, subItemPath[{ index, name, positionNumber, typeIdentifier }], ioType, startAddress, length, context, processImage, isochronousMode, interruptObNumber }], messages }`; ein nicht lesbares oder nicht anwendbares Member ist null mit einer Meldung |
| `list_port_connections` | `target` (device oder deviceItem) | `{ target, ports[{ port, interfaceOwner, connectedPortsState, connectedPorts[{ port, runtimeType }], messages }], messages }` aus `NetworkPort.ConnectedPorts` jedes Port-Geräteelements; siehe [Port-Topologie](#port-topologie) |
| `list_transfer_areas` | `target` (deviceItem mit dem `NetworkInterface`) | `{ target, transferAreas[], multicastableTransferAreas[], messages }`; siehe [IO-Systeme, DP-Mastersysteme und Transferbereiche](#io-systeme-dp-mastersysteme-und-transferbereiche) |
| `get_custom_identity` | `target` (device oder deviceItem), `key` | `{ target, key, observed { state, value }, providerType }` aus `CustomIdentityProvider.Get(key)`; `state` `found` (mit `value`), `notFound` (TIA Portal warf `CustomIdentityNotFoundException`; ein erfolgreicher Lesezugriff, kein Fehler) oder `null` (Get lieferte null). Siehe [Custom Identities](#custom-identities). |

Wenn das ausgewählte Element keine eigenen `Addresses` hat, sammelt `list_io_addresses` die Adressen
seiner Unterelemente in Tiefensuche (die eigenen Adressen jedes Unterelements vor denen seiner Kinder, höchstens 16 Ebenen) und
meldet jede mit dem relativen `subItemPath`, aus dem sie stammt; `subItemPath` ist leer für die eigenen Adressen des Elements,
und `index` läuft über die gesamte Auflistung. Live 2026-11-02 (TIA Portal V21, K06): Die
S7-1500-Baugruppe `DI_32` (`OrderNumber:6ES7 521-1BL00-0AB0/V2.2`) hat keine eigenen Adressen; sie liegen
auf ihrem Unterelement `DI_32/DI_32`, dessen `TypeIdentifier` null ist, sodass dieses Unterelement nicht als
`deviceItem`-Ziel ausgewählt werden kann. Das Sammeln der Unterelemente ist noch nicht live getestet.

`ModuleInformationProvider` wird durch Aufzählen von `project.HwUtilities` gefunden; ein Container ohne ihn
schlägt mit `unsupported_capability` fehl.

## network_write-Operationen

| Operation | Erforderlich | Optional | Siemens-Aufrufe |
|---|---|---|---|
| `plug_device_item` | `target` (Container), `typeIdentifier`, `positionNumber` | `deviceItemName` (weggelassen: `""`, TIA Portal benennt das Element) | `CanPlugNew`, `PlugNew` |
| `move_device_item` | `target`, `positionNumber` | `destination` (Standard: das aktuelle übergeordnete Element) | `CanPlugMove`, `PlugMove` |
| `copy_device_item` | `target`, `positionNumber` | `destination` (Standard: das aktuelle übergeordnete Element) | `CanPlugCopy`, `PlugCopy` |
| `delete_device_item` | `target` | — | `DeviceItem.Delete()` |
| `change_device_item_type` | `target`, `typeIdentifier` | — | `DeviceItem.ChangeType(String)` |
| `set_hardware_attributes` | `target`, `attributes` (1–100, eindeutige Namen) | `attributeErrorPolicy` (`abort` Standard, `ignore`), `attributeWriteMode` (`bulk` Standard, `sequential`) | `SetAttributes(IEnumerable<KeyValuePair<string, object>>, AttributeDelegate)` oder `SetAttribute` pro Attribut |
| `set_io_address` | `target`, `address { ioType, startAddress, length? }`, `addressChanges { startAddress?, processImage?, isochronousMode?, interruptObNumber? }` | — | Setter von `Address.StartAddress`; `SetAttribute("ProcessImage" / "IsochronousMode" / "InterruptObNumber")` |
| `set_custom_identity` | `target` (device oder deviceItem), `key`, `value` (beliebiger Text, auch leer) | — | `CustomIdentityProvider.Set(String, String)`, mit `Get` zurückgelesen |
| `remove_custom_identity` | `target`, `key` | — | `CustomIdentityProvider.Remove(String)`, mit `Get` zurückgelesen |
| `connect_ports` | `target` (Port-deviceItem), `partner` (Port-deviceItem) | — | `NetworkPort.ConnectToPort(NetworkPort)`, aus `ConnectedPorts` zurückgelesen |
| `disconnect_ports` | `target`, `partner` | — | `NetworkPort.DisconnectFromPort(NetworkPort)`, aus `ConnectedPorts` zurückgelesen |
| `create_transfer_area`, `update_transfer_area`, `delete_transfer_area`, `delete_io_system`, `disconnect_from_io_system` | siehe [IO-Systeme, DP-Mastersysteme und Transferbereiche](#io-systeme-dp-mastersysteme-und-transferbereiche) | | |
| `export_cax`, `import_cax` | siehe [CAx-Daten (AutomationML)](#cax-daten-automationml) | | |
| `export_io_map` | `filePath` (lokal absolut, `.csv`) | `overwrite`, `csvDelimiter` (`,` `;` Tab), `rawCells` (Standard false), `deviceName`, `plcName`, `includeTagMatches` (Standard true) | keine (Dateischreiben des Hosts über `read_hardware_config` mit `includeIoDetails`) |

Attributwerte sind `{ name, kind, value }` mit `kind` `string`, `boolean`, `integer`, `number`
oder `enum` (exakter Membername). Der Worker wandelt jeden Wert in einen Typ um, den die
`EngineeringAttributeInfo.SupportedTypes` des Attributs deklariert: `integer` in den ersten deklarierten ganzzahligen Typ,
der ihn aufnehmen kann (oder einen Gleitkommatyp, wenn keiner ganzzahlig ist), `number` nur in einen Gleitkommatyp, `enum`
in den einen deklarierten Enum-Typ, der das Member definiert. Das Schreiben von `null` wird nicht unterstützt.

### Planung und Guards

Die Planung fügt dem Hardware-Snapshot des Aufrufs Worker-Lesevorgänge hinzu:

- plug, move, copy: `probe_hardware_plug` wertet `CanPlugNew`/`CanPlugMove`/`CanPlugCopy` aus; der
  Effekt trägt `canPlug`. `false` lässt den Aufruf vor jeder Mutation als `validation_error` fehlschlagen (die
  Mutation prüft es unmittelbar vor `PlugNew`/`PlugMove`/`PlugCopy` erneut). Für `plug_device_item` auf einem
  `device`-Ziel ergänzt die Meldung, dass Baugruppen üblicherweise in das Baugruppenträger-Element gesteckt werden, und gibt, wenn das Gerät
  genau ein Element der obersten Ebene hat, dessen Typbezeichner mit `System:Rack` beginnt, den
  `deviceItem`-Selektor dieses Elements an (mehrere Baugruppenträger-Elemente werden mit Namen aufgelistet). Das ändert nur die Meldung; was
  TIA Portal erlaubt, ist unverändert. Live 2026-11-02 (K29): `list_plug_locations` auf dem Gerät `ET200SP_1`
  listete die Positionen 1-10 als frei, `CanPlugNew` auf dem Gerät war für ein F-DI an Position 1 false, und
  das Stecken in das Baugruppenträger-Element `Baugruppenträger_0` (`System:Rack.ET200SP`) funktionierte.
- `set_hardware_attributes`: `read_hardware_attributes` liefert die aktuellen typisierten Werte und ein
  Umwandlungsurteil pro Wert; ein unbekanntes, schreibgeschütztes oder nicht umwandelbares Attribut lässt den Aufruf
  als `validation_error` fehlschlagen. Der Effekt listet aktuelle und angeforderte Werte.
- `set_io_address`: `list_io_addresses` wählt die Adresse über `ioType`, `startAddress` und optional
  `length` unter denselben Adressen aus, die es auflistet (die eigenen des Elements oder die seiner Unterelemente, wenn es keine hat), sodass eine
  Adresse eines Unterelements ein gültiges Ziel ist. Kein Treffer ist `target_not_found`; mehrere Treffer (zum Beispiel
  zwei Unterelemente mit demselben Anfang) sind `validation_error`, der jeden Kandidaten mit seinem Unterelement und seiner
  Länge nennt; `address.length` hinzufügen. Eine Adresse eines Unterelements setzt `subItemPath` in die aktuellen Einstellungen
  des Effekts. Eine Änderung der Anfangsadresse liest außerdem die E/A-Zuordnung des Geräts mit Variablentreffern (des besitzenden
  Unterelements).
- `export_io_map`: Der Pfad muss ein lokaler absoluter Pfad sein, lexikalisch geprüft bei der Validierung und erneut
  vor dem Schreiben durch das gemeinsame `LocalFilePaths` (Contracts), das auch die PLC-Meldungsexporte verwenden:
  Die Geräteformen `\\?\` / `\\.\` und der NT-Objektpfad `\??\` mit beiden Trennzeichen (UNC-Freigaben
  `\\server\share\...` sind seit 2026-11-01 erlaubt), alternative Datenströme (ein `:` nach dem Laufwerk), `.`/`..`-Segmente,
  reservierte Gerätenamen in jedem Segment (CON, PRN, AUX, NUL, COM0-9, LPT0-9, CONIN$, CONOUT$, mit oder
  ohne Erweiterung), Segmente, die auf ein Leerzeichen oder einen Punkt enden, sowie Steuer-, Bidi- oder Platzhalterzeichen werden
  abgelehnt; zugeordnete Netzlaufwerke sind erlaubt. Bei der Validierung folgt jedes Pfadsegment außerdem den
  Dateinamensregeln der PLC-Dateiexporte (kein Windows-Gerätename wie `CON` oder `COM1`, kein Segment,
  das auf einen Punkt oder ein Leerzeichen endet, kein `.`/`..`-Segment; `PlcFileTargets.PathShapeError`); ein Pfad, den beide Regelsätze
  ablehnen, meldet beide Gründe. Das Verzeichnis muss existieren, der Pfad darf kein Verzeichnis sein, und eine
  bestehende Datei wird abgelehnt, außer bei `overwrite:true`.

| Guard | Schweregrad | Wird ausgelöst, wenn |
|---|---|---|
| `network_deletes_device_item` | info | `delete_device_item`: nennt die Anzahl der Unterelemente und die verbundenen Knoten, die ihre Subnetz-Verbindung verlieren; Variablen und Programmreferenzen werden nicht geändert |
| `network_changes_device_item_type` | info | `change_device_item_type`: ChangeType meldet keine Details dazu, was es geändert hat |
| `network_io_address_tags_not_rewired` | info | `set_io_address` mit `startAddress`: listet die getroffenen Variablen im aktuellen Bereich; TIA Portal verdrahtet Variablen nicht neu und kann den gekoppelten Bereich ändern |
| `network_attribute_errors_ignored` | info | `set_hardware_attributes` mit `attributeErrorPolicy:"ignore"` (Teilaktualisierung) |
| `network_export_overwrites_file` | info | `export_io_map` ersetzt eine bestehende Datei |

### Ergebnisse und Verifikation

Jede Mutation liest ihr eigenes Ergebnis im Worker zurück und schlägt mit `postcondition_failed` fehl, wenn das
Ergebnis nicht bestätigt werden kann; ein nicht lesbarer Wert wird nie als Übereinstimmung behandelt:

- plug/move/copy liefern die weiterreichbare Identität des resultierenden Elements (über `Parent` nach oben ermittelt) und
  `placedUnder`, wo es tatsächlich gelandet ist. Es muss ein direktes Kind des Containers (oder des
  Ziels) an der angeforderten Position sein; `PlugNew` muss außerdem den angeforderten Namen tragen, wenn einer
  angegeben wurde. Wenn der Container ein Element der obersten Ebene seines Geräts ist (ein Baugruppenträger oder eine Profilschiene), wird ein Element, das TIA
  Portal stattdessen direkt unter dem Gerät platziert hat, akzeptiert, wenn seine Position, sein Name (falls angegeben) und sein
  Typbezeichner (der angeforderte bei plug, der der Quelle bei move/copy; ohne Beachtung der Groß-/Kleinschreibung verglichen)
  übereinstimmen: `placedUnder` ist dann das Gerät (leerer `itemPath`), `result.itemPath` ist der eigene
  Pfad des Elements auf Geräteebene, und eine Meldung sagt das. Live 2026-11-02 (TIA Portal V21, K05): Das Stecken in die
  S7-1500-Profilschiene `Profilschiene_0` (`OrderNumber:6ES7 590-1***0-0AA0`, Steckplatz 4) und in den ET-200SP-Baugruppenträger
  `Baugruppenträger_0` (`System:Rack.ET200SP`, Steckplatz 1, F-DI `6ES7 136-6BA01-0CA0/V2.0`) platzierte die Baugruppe
  an `PLC_1/DI_32_Test` und `ET200SP_1/F-DI_1`; die vorherige Prüfung meldete für eine
  gesteckte Baugruppe `postcondition_failed`. Wenn der Rückgabewert von `PlugNew` nicht gelesen werden kann, wird das Element gesucht: Genau ein Element,
  das vorher nicht existierte, unter dem Container oder (Ebene des Baugruppenträgers) unter dem Gerät, mit dem angeforderten
  Typbezeichner, Namen und der angeforderten Position wird akzeptiert. Eine Null-Rückgabe ohne ein solches Element, eine nicht lesbare
  Position oder Kette übergeordneter Elemente, eine andere Position oder ein anderes übergeordnetes Element schlägt fehl. Ein verschobenes Element darf außerdem
  nicht mehr unter seinem ursprünglichen exakten Pfad aufgelöst werden. Die Akzeptanz auf Geräteebene ist noch nicht live getestet.
- delete löst den exakten Pfad erneut auf und darf das Element nicht finden.
- change type löst das Element erneut auf (aus dem Rückgabewert, sonst über die Position) und muss einen
  lesbaren Typbezeichner melden, der nicht der alte ist.
- Attribut-Schreibvorgänge lesen jedes Attribut zurück (`applied`, `ignored` unter Policy ignore,
  `notApplied` schlägt fehl). Wenn der AttributeDelegate TIA Portal die Antwort des Aufrufers nicht geben kann
  (`CurrentSelection` fehlt, ist schreibgeschützt oder lehnt den Wert ab), zeichnet er das Problem auf, statt
  eine Ausnahme in TIA Portal zu werfen; SetAttributes fährt dann mit dem eigenen Standard von Portal für dieses
  Attribut fort, und das Element schlägt nach der Rückkehr von SetAttributes mit `unsupported_capability` fehl, mit der
  Rücklesung jedes angeforderten Attributs in den Warnungen.
- Adress-Schreibvorgänge lesen vorher und nachher dieselbe Adresssammlung wie `list_io_addresses` und melden
  `changedIndexes` (gekoppelte Bereiche eingeschlossen). Die geschriebene Adresse wird über ihr besitzendes
  Unterelement (`subItemPath`), ihren `ioType`, die angeforderte (oder unveränderte) Anfangsadresse und ihre ursprüngliche
  Länge identifiziert, nie über ihren Index in `Addresses`; kein Kandidat oder mehrere Kandidaten schlagen fehl.

Siemens-Fehler behalten die gemeinsame Klassifizierung (`engineering_rejected` mit `engineeringError`
während einer Mutationsmethode) und tragen die Rücklesung der Attribute oder Adressen in den Warnungen des
Elements; frühere Attribute oder Adress-Member können angewendet bleiben (kein Rollback). Die Pipeline-Verifikation ist `trusted`
oder `superseded`; die überschreibenden Schlüssel sind pro Geräteelement hierarchisch, sodass ein späteres Löschen eines
Containers frühere Elemente auf seinen Unterelementen überschreibt.

`export_io_map` schreibt über den gemeinsamen Dateischreiber der PLC-Dateiexporte (`PlcFileTargets.Write`): eine
temporäre Datei im Zielverzeichnis, die an ihren Platz verschoben wird; eine bestehende Datei wird mit `File.Replace` ausgetauscht,
wobei die vorherige Datei als `.bak`-Geschwister behalten wird, bis der Hash beim erneuten Lesen übereinstimmt (wo ReplaceFile fehlschlägt, wird die
vorherige Datei beiseite umbenannt und die neue hineinverschoben, nie kopiert; ein fehlgeschlagener Austausch behält und benennt beide
Kopien), siehe [PLC_SOURCES_FOLDERS_SUMMARY.md](PLC_SOURCES_FOLDERS_SUMMARY.de.md). Die geschriebene Datei wird
erneut gelesen, und das Ergebnis meldet `sha256`, Byte- und Zeilenzahlen, `rawCells`, `neutralizedCells` und
`discoveryComplete`; ein erneutes Lesen, das fehlschlägt oder abweicht, ist `postcondition_failed` (die Datei ist an ihrem Platz, die
behaltene Sicherung wird genannt). Diagnosen, die länger als die Obergrenze von 512 Zeichen sind, werden durch eine Zusammenfassung ersetzt und
in der Wurzel-`omission` gemeldet (`diagnosticDetailsOmitted`); seit 2026-11-02 macht das allein den Aufruf nicht mehr
zu `success:false` (live 2026-11-02, K07: Die CSV mit 4.850 Bytes wurde geschrieben und ihr
`sha256` zurückgegeben, aber 2.362 Zeichen Diagnose ließen den Aufruf einen Fehlschlag melden). CSV: UTF-8 mit
BOM, CRLF, Quoting nach RFC 4180; Spalten `RowType, Device, ItemPath, Item, TypeIdentifier,
PositionNumber, AddressIoType, StartAddress, Length, AddressContext, Controllers, ChannelNumber,
ChannelIoType, ChannelType, LogicalAddress, TagName, TagDataType, TagLogicalAddress, TagTable,
TagFolder`; eine `address`-Zeile pro Address und eine `channel`-Zeile pro Kanal und getroffener Variable.
Die Werte sind die von `read_hardware_config`, mit einer Änderung: Einer Zelle, die mit `=`, `+`, `-`,
`@`, einem Tabulator oder einem Wagenrücklauf beginnt, wird ein einfaches Anführungszeichen vorangestellt, sodass Tabellenkalkulationsprogramme sie als
Text anzeigen, statt sie als Formel auszuwerten (CSV-/Formel-Injection). Das Ergebnis zählt diese Zellen in
`neutralizedCells`; `rawCells:true` schreibt jeden Wert genau so, wie er gelesen wurde.

## Custom Identities

> **Nicht live getestet** (GitHub-Issue #18). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.CustomIdentity.CustomIdentityProvider.Set(System.String,System.String)` ("The user defined key and
> value to set"), `.Get(System.String)` (liefert `System.String`), `.Remove(System.String)` ("Throws
> `CustomIdentityNotFoundException` if CustomIdentity for a given key is not found") und
> `T:Siemens.Engineering.CustomIdentity.CustomIdentityNotFoundException`.

Benutzerdefinierte Schlüssel/Wert-Paare an einem Gerät oder einem Geräteelement: `get_custom_identity` (`network_read`, jeder
Modus, `Observe`) und `set_custom_identity` / `remove_custom_identity` (`network_write`, read-write und full,
`ProjectMutation`; keine Rückfrage, kein Guard). Worker-Teil `Openness/Network/HardwareModuleService.CustomIdentity.cs`;
Anfragefelder `key`/`value` (`HardwareModuleRequestInfo.CustomIdentityKey`/`CustomIdentityValue`); FakeWorker
`hardware-modules` (die DQ-Baugruppe bietet keinen Provider).

- **Provider.** Die XML sagt nicht, welche Objekte den `CustomIdentityProvider` anbieten; der Worker holt ihn über die
  `GetServiceInfos()` des Ziels mit exaktem vollem Namen und ruft ihn mit `IServiceProvider.GetService(Type)` ab. Nicht
  gelistet oder gelistet, aber nicht geliefert, ist `unsupported_capability`, nichts geändert. Ein Schreibzugriff scheitert
  daran schon in der Planung, die den Schlüssel zuerst über `get_custom_identity` liest.
- **Aufrufe.** `Get`, `Set` und `Remove` werden zur Laufzeit über den exakten Namen und `String`-Parameter vom Siemens-freien
  `CustomIdentityRun` gebunden (Contracts `Network/HardwareModules/CustomIdentity.cs`, vom FakeWorker mitbenutzt); ein fehlendes
  Member ist `api_member_missing`. `CustomIdentityNotFoundException` wird an seinem exakten vollen Namen erkannt (auch an einem
  Basistyp). Wie `Get` auf einen fehlenden Schlüssel antwortet, ist nicht dokumentiert: das Ergebnis meldet `notFound` für diese
  Ausnahme und `null` für eine Rückgabe null.
- **Schreibzugriffe.** Der Planungseffekt zeigt `currentSettings.state`/`value` und `requestedSettings.key`/`value` (oder
  `removed`). `set_custom_identity` liest den Schlüssel mit `Get` zurück und scheitert mit `postcondition_failed`, wenn nicht
  der angeforderte Wert gelesen wird; `remove_custom_identity` scheitert mit `target_not_found`, wenn TIA Portal
  `CustomIdentityNotFoundException` wirft (der Schlüssel existiert nicht; gemeldet, kein Absturz), und mit
  `postcondition_failed`, wenn `Get` den Schlüssel noch findet. Das Ergebnis enthält `before` und `after`; die Verifikation ist
  das Rücklesen des Workers (K78: Prüfung `customIdentity`, erwartet `key=value` bzw. `key absent`, tatsächlich aus `after`), je
  Gerät/Element und Schlüssel überholbar: ein Setzen mit anschließendem Entfernen desselben Schlüssels wird vom verifizierten
  Entfernen überholt, dessen eigener Eintrag prüft, dass der Schlüssel fehlt.
- **Noch nötige Live-Abnahme:** welche Geräte und Geräteelemente den Provider listen, was `Get` bei einem fehlenden Schlüssel
  tut, ob ein Wert Speichern/Schließen/Öffnen übersteht und ob leere Werte angenommen werden.

## Port-Topologie

> **Nicht live getestet** (GitHub-Issue #10). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`,
> `T:Siemens.Engineering.HW.Features.NetworkPort` ("Represents a port on a device item"), `M:...NetworkPort.ConnectToPort(NetworkPort)`,
> `M:...NetworkPort.DisconnectFromPort(NetworkPort)`, `P:...NetworkPort.ConnectedPorts` (Beschreibung "Internal use only") und
> `P:...NetworkPort.Interface`; der Siemens-Skill `devices-and-hardware` (Abschnitt "Port-to-Port Topology Connections") holt den
> Dienst mit `deviceItem.GetService<NetworkPort>()` und prüft vor `ConnectToPort` mit `ConnectedPorts.Contains`.

PROFINET-Verkabelung zwischen Ports: `list_port_connections` (`network_read`, jeder Modus, `Observe`) und `connect_ports` /
`disconnect_ports` (`network_write`, read-write und full, `ProjectMutation`; keine Abfrage). Worker-Teil
`Openness/Network/HardwareModuleService.Ports.cs`; Siemens-freie Aufrufe `PortTopologyRun` (Contracts
`Network/HardwareModules/PortTopology.cs`, gemeinsam mit dem FakeWorker); neues Anfragefeld `partner`
(`HardwareModuleRequestInfo.Partner`); FakeWorker `hardware-modules` (CPU-Schnittstelle `PROFINET interface_1` mit `Port_1` und
`Port_2`).

- **Dienst.** `NetworkPort` steht nicht in den Compile-Stubs. Ein Port-Geräteelement ist ein Geräteelement, das
  `Siemens.Engineering.HW.Features.NetworkPort` unter seinen `GetServiceInfos()` führt; der Dienst wird mit
  `IServiceProvider.GetService(Type)` geholt. `ConnectToPort`, `DisconnectFromPort`, `ConnectedPorts` und `Interface` werden zur
  Laufzeit über den exakten Namen gebunden (eine fehlende Methode ist `api_member_missing`).
- **Lesen.** `list_port_connections` nimmt ein `device`- oder `deviceItem`-Ziel und listet jedes Port-Geräteelement des Geräts bzw.
  des Elements und seiner Unterelemente: `{ target, ports[{ port, interfaceOwner, connectedPortsState, connectedPorts[{ port,
  runtimeType }], messages }], messages }`. `interfaceOwner` ist `NetworkPort.Interface.OwnedBy`. Die XML dokumentiert kein
  `Parent` des Port-Dienstes; ein Partner aus `ConnectedPorts` wird deshalb über das Geräteelement des Projekts benannt, dessen
  Port-Dienst ihm gleicht (`object.Equals`); `port` ist null, wenn keines passt (z. B. ein Port eines anderen Projekts).
  `connectedPortsState` ist `unreadable`, wenn `ConnectedPorts` fehlt oder einen behebbaren Fehler wirft. K77 (live am 2026-11-03,
  CPU 1516-3 PN/DP): der Aufruf scheiterte mit `protocol_error`, weil integrierte Schnittstellen und Ports `TypeIdentifier` null
  lesen und der Worker dann eine Identität ohne Gerätenamen schickte; ein `TypeIdentifier` null ist jetzt das Pfadsegment
  `typeIdentifier: null`, das auch `read_hardware_config` meldet (K95: der Selektor akzeptiert null, `""` oder kein Feld, sodass
  `port` und `interfaceOwner` als Ziel zurückgegeben werden können), und ein Element, dessen Selektor
  nicht lesbar ist, ist null (`interfaceOwner`, Partner-`port`) bzw. beim Port selbst Gerätename und Elementname ohne Pfad, jeweils
  mit Hinweis. Nicht live getestet.
- **Geräteelemente ohne Port (K83, nicht live getestet).** TIA Portal V21 listet `NetworkPort` unter den Diensten jedes
  Geräteelements (CPU, Kartenleser, Display, OPC UA, DP- und PN-Schnittstellen), liefert ihn aber nur an Ports. Der Lesevorgang
  fügt nicht mehr je Element eine Meldung hinzu: Eine Meldung oben zählt sie und nennt höchstens 10. Ein ausdrücklich genanntes
  connect/disconnect-Element bekommt weiter seine eigene Meldung.
- **Schreiben.** `connect_ports` und `disconnect_ports` nehmen `target` (das Port-Geräteelement) und `partner` (das andere
  Port-Geräteelement, kind `deviceItem`). Die Planung liest `list_port_connections` des Ziels: Ein Ziel ohne Port-Dienst scheitert
  vor jeder Änderung mit `unsupported_capability`; der Effekt trägt `currentSettings.connected` und `linkedPorts`. Eine bestehende
  Verbindung (connect) löst den Info-Guard `network_port_link_exists` aus (laut Siemens-Skill wirft `ConnectToPort` dann), eine
  fehlende oder nicht lesbare (disconnect) `network_port_link_missing`; das Element läuft trotzdem und TIA Portal entscheidet (eine
  Ablehnung ist `engineering_rejected`). Der Worker liest `ConnectedPorts` zurück: Eine Verbindung, die nicht wie angefordert
  zurückgelesen wird, ist `postcondition_failed`; ein nicht lesbares `ConnectedPorts` lässt `connectedAfter` null mit einer
  Meldung (vertraut, nicht verifiziert). Port und Partner sind eine Verbindung: Ein späteres Element auf demselben Paar ersetzt
  ein früheres.
- **Leitungsattribute.** Die Handbuch-Abschnitte "Attributes of port inter-connection" und "Configurable attributes of a
  port-to-port connection" (Leitungslänge, Signallaufzeit) sind Attribute des Port-Geräteelements, nicht von `NetworkPort`:
  `inspect_network_object` auf dem Port-Element listet sie mit Zugriff und unterstützten Typen, `set_hardware_attributes` schreibt
  sie. Der Server nimmt keinen Attributnamen an.
- **Noch live abzunehmen:** ob `ConnectedPorts` eine in der Oberfläche und eine mit `ConnectToPort` gesetzte Verbindung listet,
  was TIA Portal bei einer bestehenden Verbindung wirft und ob ein Partner in einer anderen Station auf sein Geräteelement aufgelöst wird.

## IO-Systeme, DP-Mastersysteme und Transferbereiche

> **Nicht live getestet** (GitHub-Issue #11). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Base.xml`:
> `M:Siemens.Engineering.HW.IoSystem.Delete`, `M:...IoConnector.DisconnectFromIoSystem`, `P:...IoConnector.ConnectedToIoSystem`,
> `P:...Features.NetworkInterface.TransferAreas` / `.MulticastableTransferAreas`, `M:...TransferAreaComposition.Create(String,
> TransferAreaType)` / `Create(String, TransferAreaType, Int32)`, `M:...MulticastableTransferAreaComposition.Create(NetworkInterface,
> TransferAreaType)` / `(NetworkInterface, TransferAreaType, String)` / `(NetworkInterface, TransferAreaType, String, Int32)` /
> `(MulticastableTransferArea, TransferAreaType)`, die Eigenschaften von `TransferArea` (`Name`, `Type`, `Direction`, `PositionNumber`,
> `ExtendedPositionNumber`, `LocalToPartnerLength`, `PartnerToLocalLength`, `LocalAddresses`, `PartnerAddresses`,
> `TransferAreaMappingRules`) und `MulticastableTransferArea` (`Name`, `Type`, `Direction`, `DataLength`, `Comment`, `Addresses`,
> `PartnerTransferAreas`), `Delete()` beider und die 30 Member von `TransferAreaType`. Der Siemens-Skill `devices-and-hardware`
> ("Create Multicast Transfer Area") zeigt die beiden multicastfähigen `Create`-Formen.

Worker-Teil `Openness/Network/HardwareModuleService.NetworkSystems.cs`; Siemens-freie Transferbereichslogik `TransferAreaRun`
(Contracts `Network/HardwareModules/NetworkSystems.cs`, gemeinsam mit dem FakeWorker); Anfragefelder `transferArea` und `partner`;
FakeWorker `hardware-modules` (CPU-Schnittstellen `PROFINET interface_1` (ein I-Device im IO-System 100 des Subnetzes `subnet-pn-1`)
und `PROFINET interface_2`). Keines dieser Member steht in den Compile-Stubs; jedes wird zur Laufzeit über exakten Namen und
Argumenttypen gebunden (ein fehlendes ist `api_member_missing`); die Schnittstelle ist `GetService<NetworkInterface>()` des
Geräteelements.

| Operation | Werkzeug | Eingabe | Siemens-Aufrufe |
|---|---|---|---|
| `list_transfer_areas` | `network_read` | `target` (deviceItem mit dem `NetworkInterface`) | `NetworkInterface.TransferAreas`, `.MulticastableTransferAreas`, die dokumentierten Eigenschaften, `GetAttributeInfos()` |
| `create_transfer_area` | `network_write` | `target`, `transferArea { collection, type, name?, positionNumber?, dataLength?, templateIndex? }`, `partner` (multicastable) | die `Create`-Überladung, die die Felder wählen (unten) |
| `update_transfer_area` | `network_write` | `target`, `transferArea { collection, index, name }`, `attributes` | `SetAttribute` je Attribut, mit `GetAttribute` zurückgelesen |
| `delete_transfer_area` | `network_write` | `target`, `transferArea { collection, index, name }` | `TransferArea.Delete()` / `MulticastableTransferArea.Delete()` |
| `delete_io_system` | `network_write` | `target { kind: "ioSystem", subnetId, number, ioSystemIndex?, ioSystemName? }` | `IoSystem.Delete()` |
| `disconnect_from_io_system` | `network_write` | `target` (deviceItem mit dem `NetworkInterface` des IO-Devices) | `IoConnector.DisconnectFromIoSystem()` |

- **Transferbereiche lesen.** `{ target, transferAreas[], multicastableTransferAreas[], messages }`; jeder Bereich ist `{ collection,
  index, name, properties[{ name, value { state, value, runtimeType, error } }], attributes[{ name, accessMode }], runtimeType }`.
  Die XML nennt keine Eigenschaftstypen; jede dokumentierte Eigenschaft wird daher als Text ausgegeben, sammlungswertige als
  `<Name>Count`. `attributes` ist, was `GetAttributeInfos()` listet (was `update_transfer_area` setzen darf). Eine Komposition, die
  die Installation nicht anbietet, ist null mit einer Meldung.
- **Anlegen.** Sammlung `transferAreas` (Adresszuordnung I-Slave / I-Device): `name` und `type` Pflicht, optional `positionNumber`
  → `TransferAreaComposition.Create(name, type[, positionNumber])`. Sammlung `multicastable`: `type` und `partner` (das Geräteelement
  mit dem Partner-`NetworkInterface`) Pflicht → `Create(partnerInterface, type)`, mit `name` → `Create(partnerInterface, type, name)`,
  mit `name` und `dataLength` → `Create(partnerInterface, type, name, length)`; oder `templateIndex` (ein Bereich der
  `MulticastableTransferAreas` des Partners) → `Create(area, type)`. `type` muss ein dokumentiertes Member von `TransferAreaType`
  sein; die installierte Aufzählung entscheidet zur Laufzeit. Der angelegte Bereich muss in der neu gelesenen Komposition stehen
  (sonst `postcondition_failed`); ein von TIA Portal geänderter Name ist eine Meldung.
- **Ändern und löschen.** Der Bereich wird über `index` mit dem aktuellen `name` als Nachweis gewählt (Planung und Worker:
  `target_evidence_mismatch`, wenn der Name abweicht). `update_transfer_area` prüft jedes Attribut gegen `GetAttributeInfos()`
  (Zugriffsmodus, Umwandlung nach `SupportedTypes` wie bei `set_hardware_attributes`), schreibt sie einzeln und liest jedes zurück
  (`postcondition_failed`, sofern nicht jedes den geschriebenen Wert zurückliest). `delete_transfer_area` verlangt danach einen
  Bereich weniger in der Komposition und löst den Info-Guard `network_deletes_transfer_area` aus. Indizes verschieben sich nach
  einem Löschen: Ein späteres Element auf derselben Komposition ersetzt.
- **IO-Systeme.** `delete_io_system` nimmt den IO-System-Selektor, den `read_hardware_config` meldet
  (subnets[].ioSystems[].selector). Die Planung löst ihn im Snapshot auf und löst den Info-Guard `network_deletes_io_system` mit den
  getrennten IO-Devices aus; der Worker löst ihn exakt auf, liest Name, Nummer und verbundene IO-Devices, ruft `IoSystem.Delete()`
  und löst über Subnetz, Nummer und Name neu auf (`postcondition_failed`, wenn es noch gelistet ist). Es löscht PROFINET-IO-Systeme
  und DP-Mastersysteme gleich, soweit TIA Portal sie in `IoSystems` des Subnetzes führt. `disconnect_from_io_system` nimmt das
  Schnittstellen-Element des IO-Devices, wählt den einen verbundenen IO-Connector (mehrere verbunden, oder mehrere und keiner
  verbunden: `target_ambiguous`; ein einzelner unverbundener Connector wird trotzdem an TIA Portal übergeben), ruft
  `DisconnectFromIoSystem()` und liest `ConnectedToIoSystem` zurück (muss null sein); Info-Guard `network_disconnects_io_connector`.
- **DP-Mastersysteme.** V21 dokumentiert kein eigenes Member, das ein DP-Mastersystem anlegt (die gefundenen Namen sind
  Aufzählungswerte wie `DpOperatingMode.DpMaster`). `create_io_system` ruft das allgemeine `IoController.CreateIoSystem(System.String)`
  am ersten IO-Controller der Schnittstelle des gewählten Knotens auf; an einem PROFIBUS-DP-Master-Knoten entscheidet TIA Portal, ob
  daraus ein DP-Mastersystem wird. Der IO-Connector eines DP-Slaves tritt mit `configure_network_device` `changes.ioSystem`
  (`IoConnector.ConnectToIoSystem`) bei, wie ein PROFINET-IO-Device. An einer DP-Schnittstelle nicht live getestet (PLC_F1 des
  Testprojekts hat X3 DP).
- **Weggelassen.** "Modifying CCDX transfer areas" über das hinaus, was `GetAttributeInfos()` als schreibbar anbietet: Kein anderes
  ändernde Member ist dokumentiert. Bearbeiten von `TransferAreaMappingRule` (`Begin`, `End`) und der Adressobjekte der
  Transferbereiche: nur als Anzahl gelesen; ihre Schreibsemantik ist nicht dokumentiert. Umbenennen oder Umnummerieren von
  IO-Systemen: `IoSystem.Name`/`Number` sind ohne Setter dokumentiert.
- **Noch live abzunehmen:** welche Schnittstellen welche Komposition anbieten, welche `TransferAreaType`-Member jede annimmt, was
  `GetAttributeInfos()` für einen Bereich listet und ob `IoSystem.Delete()` ein DP-Mastersystem entfernt.

## CAx-Daten (AutomationML)

> **Nicht live getestet** (GitHub-Issue #9). Nachweis: die V21-IntelliSense-Datei `Siemens.Engineering.Step7.xml`,
> `T:Siemens.Engineering.Cax.CaxProvider` ("Service Provider for CAx Export/Import operations") mit `Export(Device, FileInfo)`,
> `Export(ProjectBase, FileInfo)` (→ `TransferResult`), `Export(Device, FileInfo, FileInfo)`, `Export(ProjectBase, FileInfo, FileInfo)`,
> `Export(Project, FileInfo, FileInfo)` (dritter Parameter die Protokolldatei, → `Boolean`), `Import(FileInfo, CaxImportOptions)`
> (→ `TransferResult`), `Import(FileInfo, FileInfo, CaxImportOptions)` (→ `Boolean`); `CaxImportOptions` (`MoveToParkingLot`,
> `OverwriteTiaDevice`, `RetainTiaDevice`); `TransferResult` (`State`, `ErrorCount`, `WarningCount`, `Messages`),
> `TransferResultMessage` (`Message`, `State`, `DateTime`, `ErrorCount`, `WarningCount`, `Messages`), `TransferResultState`
> (`Success`, `Information`, `Warning`, `Error`). TIA-Portal-Hilfe V20 "CAx-Daten exportieren und importieren": Format AutomationML,
> Endung `.aml`.

`export_cax` und `import_cax` (`network_write`, read-write und full, `ProjectMutation`; keine Abfrage). Worker-Teil
`Openness/Network/HardwareModuleService.Cax.cs`; Siemens-freie Projektion `CaxRun` (Contracts `Network/HardwareModules/Cax.cs`);
Anfragefelder `filePath`, `deviceName`, `overwrite` (vorhanden; seit F4 auch für das Protokoll des Imports) und `logFilePath`, `importOption` (neu); FakeWorker
`hardware-modules` (ein Import einer Datei mit `Fail` wird abgelehnt).

| Operation | Pflicht | Optional | Siemens-Aufrufe |
|---|---|---|---|
| `export_cax` | `filePath` (absolut, `.aml`) | `deviceName` (ein Gerät; ohne: das Projekt), `logFilePath`, `overwrite` | `CaxProvider.Export(Device \| ProjectBase, FileInfo[, FileInfo])` |
| `import_cax` | `filePath` (absolut, `.aml`, muss existieren), `importOption` | `logFilePath`, `overwrite` (für das Protokoll) | `CaxProvider.Import(FileInfo[, FileInfo], CaxImportOptions)` |

- **Provider.** Die XML sagt nicht, welches Objekt `CaxProvider` anbietet; der Worker holt ihn über den exakten vollen Namen aus den
  `GetServiceInfos()` des Projekts (nicht gelistet oder nicht geliefert: `unsupported_capability`). Er arbeitet auf dem gebundenen
  Container (`ProjectBase`), eine lokale Multiuser-Sitzung wird also als solche übergeben (das dokumentierte `Export(ProjectBase, ...)`
  "Single/Multi user Project").
- **Überladungen.** Ohne `logFilePath` wird die Überladung mit `TransferResult` aufgerufen; `State`, `ErrorCount`, `WarningCount` und
  der Meldungsbaum (`message`, `state`, `dateTime`, verschachtelte `messages`; höchstens 200 Meldungen und 8 Ebenen, `messagesOmitted`
  zählt den Rest) kommen als Text zurück (die XML nennt keine Eigenschaftstypen). Mit `logFilePath` wird die Überladung mit
  Protokolldatei aufgerufen; sie liefert nur true/false (`succeeded`), das Protokoll wird neu gelesen (`log { path, exists, length,
  sha256 }`). Die Überladung wird über exakten Namen und die dokumentierten Parametertypen gebunden (`Device` oder `ProjectBase`,
  `FileInfo`, die Laufzeit-Aufzählung `CaxImportOptions`); eine fehlende ist `api_member_missing`.
- **Fehler.** Ein `TransferResult` mit Zustand `Error` oder `false` aus einer Protokoll-Überladung ist die Ablehnung durch TIA Portal:
  `engineering_rejected` mit bis zu 50 Meldungen als Warnungen; ein Import nennt zusätzlich, welche Geräte schon hinzugekommen oder
  weggefallen sein können.
- **Export-Verifikation.** Eine vorhandene Datei wird ohne `overwrite:true` abgelehnt (mit löst es den Info-Guard
  `network_cax_export_overwrites_file` aus). Seit dem Sicherheits-Review 2026-11-03 (F4; nicht live getestet) schreibt TIA Portal den
  Export in einen Staging-Ordner neben dem Ziel; die Datei wird mit `PlcFileTargets.Commit` an ihren Platz verschoben (eine vorhandene
  mit Sicherungskopie ersetzt, die nach passendem Hash gelöscht wird) und neu gelesen und gehasht (`file { path, exists, length, sha256 }`);
  ohne geschriebene Datei ist es `postcondition_failed` und `filePath` bleibt unverändert. Dieser neue Hash ist die Verifikation (K78:
  Prüfung `file`, tatsächlich mit Länge und `sha256`). Weitere Dateien, die TIA Portal neben den Export schreibt, bleiben im
  Staging-Ordner und werden in einer Warnung genannt.
- **Protokolldatei (F4).** Eine vorhandene `logFilePath` wird bei `export_cax` und `import_cax` ohne `overwrite:true` abgelehnt
  (Planer und Worker); ihr Verzeichnis muss existieren, Gerätepfade und reservierte Namen lehnt der Katalog ab, und der Worker meldet
  einen Link, eine Junction oder einen Netzwerkort auf dem Weg (`PlcFileTargets.InspectDirectory`), wie bei der Datei selbst.
- **Import.** Die Planung verlangt, dass die Datei existiert (`target_not_found`), und löst immer den Info-Guard
  `network_cax_import_changes_hardware` aus (Hardware ändert sich, kein Rollback; Testprojekt verwenden). Das Ergebnis listet die
  Gerätenamen des Projekts vorher und nachher mit `addedDevices` und `removedDevices`; die importierte Hardware selbst wird nicht
  zurückgelesen (Verifikation `trusted` mit diesem Nachweis). Ein Import kann jedes Gerät betreffen und ersetzt deshalb jedes frühere
  Hardware-Element des Aufrufs.
- **Weggelassen.** `Export(Project, FileInfo, FileInfo)` wird nicht eigens aufgerufen: Es ist die Einzelprojekt-Form des dokumentierten
  `Export(ProjectBase, FileInfo, FileInfo)`, das beide abdeckt. Was eine AML-Datei enthält (EPLAN-Zuordnung, Inhalt des
  Parkplatz-Ordners), hat kein Openness-Member.
- **Noch live abzunehmen:** ob das Projekt `CaxProvider` listet, ein Export des Projekts und eines Geräts, ein Import mit jedem
  `CaxImportOptions`-Member und was ein fehlgeschlagener Import meldet.

## plc_read-Operationen

| Operation | Eingabe | Ergebnis |
|---|---|---|
| `read_address_occupancy` | optional `plcName`, `areas` (Teilmenge von I, Q, M), `includeHardware` (Standard true) | `{ plcName, deviceName, isComplete, hardwareIncluded, areas[{ area, entries[], usedBytes }], overlaps[], tagsOutsideHardware[], unparsedTags[], unsizedTags[], widthMismatches[], messages }` |
| `find_free_address` | `area` (I, Q, M), `dataType` | optional `plcName`, `startByte` (0), `endByte` (65535; beide 0–65535), `alignBytes` (1, 2, 4, 8), `requireHardware` (nur I/Q) → `{ found, address, startByte, startBit, bitLength, ... isComplete, unsizedTags[], messages }` |

Beide werden im Host aus `list_tag_tables` berechnet und, wenn Hardware benötigt wird, aus
`read_hardware_config` mit `includeIoDetails`. Genau eine PLC muss passen (`target_not_found` /
`target_ambiguous`). Variablenadressen werden aus `%<area>[B|W|D]<byte>` oder `%<area><byte>.<bit>` geparst
(international I/Q/M; die deutschen Buchstaben E/A werden auf I/Q abgebildet); symbolische, DB-, `:P`- und andere Schreibweisen
werden in `unparsedTags` aufgeführt. Hardware-Adressen gehören zur PLC, wenn die Steuerungszuordnung der Adresse
das Gerät der PLC nennt.

Die Breite einer Variable ist die **dokumentierte Länge ihres Datentyps**, abgeglichen mit der Schreibweise
der Adresse: Der größere der beiden Werte wird verwendet, und eine Abweichung wird in `widthMismatches` aufgeführt (ein LReal auf
`%MB100` belegt 64 Bit, nicht 8). Die Längen stammen aus dem Informationssystem von TIA Portal V21
("Data types"): Bool 1; Byte, Char, SInt, USInt 8; Word, WChar, Int, UInt 16; DWord, DInt, UDInt, Real,
TOD/Time_Of_Day 32; LWord, LInt, ULInt, LReal, LTOD/LTime_Of_Day, DT/Date_And_Time, LDT/Date_And_LTime
64; DTL 96 (12 Byte). Jeder andere Typ (PLC-Datentypen, Arrays, Strings, Strukturen sowie TIME, LTIME,
S5TIME und DATE, deren Länge aus diesen Seiten nicht bestätigt werden konnte) ist **ohne Größe**: Die Variable wird
in `unsizedTags` mit ihrem Anfang aufgeführt, sie belegt mindestens ihre geschriebene Breite, Einträge,
`usedBytes` und Überlappungen lassen sie aus, und `isComplete` ist in beiden Operationen false, weil eine Adresse
nach ihrem Anfang in ihr liegen kann.

`find_free_address` platziert die Typen mit einer dokumentierten Länge von 1/8/16/32 Bit und einer Schreibweise: Bool,
Byte, Char, SInt, USInt, Word, WChar, Int, UInt, DWord, DInt, UDInt, Real, TOD/Time_Of_Day. „Frei“
bedeutet, dass keine deklarierte Variable das Intervall überlappt; mit `requireHardware` muss das Intervall innerhalb eines
Hardware-Adressbereichs der PLC liegen. `startByte` und `endByte` sind 0–65535. Die Suche durchläuft die
sortierten, zusammengeführten belegten Intervalle (jeder Schritt springt über ein Intervall hinweg, sodass der Aufwand von der
Anzahl der Variablen abhängt, nicht vom Bytebereich) und stoppt, wenn der Aufruf abgebrochen wird. Ein unvollständiges Inventar
oder eine Variable ohne Größe im Bereich setzt `isComplete:false` mit einer Meldung.

## plc_write `move_tag`

`move_tag { tableName, name, targetTableName, plcName?, folderPath?, targetFolderPath? }`. Openness
V21 dokumentiert kein Member, das eine `PlcTag` zwischen Tabellen verschiebt, und Variablennamen sind pro PLC eindeutig, daher
ist dies eine **geschützte Neuanlage**, angegeben durch den Info-Guard `plc_move_tag_recreates`:

1. eine temporäre Variable (`__mcp_move_<12 hex>`) in der Zieltabelle mit dem `DataTypeName` und der
   `LogicalAddress` der Quelle anlegen, die lesbaren `ExternalAccessible`, `ExternalVisible`,
   `ExternalWritable` kopieren und den mehrsprachigen **Kommentar** kopieren: Für jedes nicht leere
   `MultilingualTextItem { Language, Text }` der `PlcTag.Comment.Items` der Quelle erhält das Element der neuen Variable
   in derselben Sprache (`Comment.Items.Find(Language)`) den Text, der sofort
   zurückgelesen wird;
2. die Quellvariable löschen;
3. die temporäre Variable in den ursprünglichen Namen umbenennen und Name, Typ, Adresse, Flags und
   Kommentar in der Zieltabelle sowie das Fehlen in der Quelltabelle zurücklesen.

Die drei Schritte laufen innerhalb einer ExclusiveAccess-Transaktion (siehe die
[Referenz der PLC-Operationen](PLC_OPERATIONS_SUMMARY.md#db-values-instance-dbs-rename-renumber-copy-move)):
Ein Fehler in irgendeinem Schritt, einschließlich eines Kommentars, der sich nicht zurücklesen lässt, rollt alle zurück, sodass die
Quelle unverändert bleibt und keine temporäre Variable übrig bleibt; die Fehlermeldung nennt den fehlgeschlagenen Schritt und
den nach dem Rollback zurückgelesenen Zustand der Variablentabellen (nicht live getestet). Eine Variable mit `IsSafety=true` wird unter der
Safety-Anmelderegel verschoben (nur abgelehnt, wenn das Sicherheitsprogramm ein Passwort hat und die Sitzung nicht angemeldet ist). Eine Sprache, für die die neue Variable kein Textelement hat, ein nicht lesbarer Kommentar und jedes Attribut
außerhalb von Datentyp, Adresse, den drei Flags und dem Kommentar werden nicht kopiert; das Ergebnis listet sie in
`notCopied` auf, und `steps` nennt die kopierten Sprachen. Danach `compile_check` ausführen, damit Bausteine
das Symbol neu binden.

## Herkunft

| Member | Quelle | Status |
|---|---|---|
| `HardwareObject.CanPlugNew(String, String, Int32)` → `bool`; `PlugNew(String, String, Int32)` → `DeviceItem` | device-item-operations.md §4 (Beispieltypen); hardware-parameters.md API-Referenz von `HardwareObject` | als Stub angelegt, typisierter Aufruf, nicht live getestet |
| `HardwareObject.CanPlugMove(DeviceItem, Int32)`, `PlugMove(DeviceItem, Int32)` | device-item-operations.md §5; hardware-parameters.md | als Stub angelegt, typisierter Aufruf, nicht live getestet |
| `HardwareObject.CanPlugCopy(DeviceItem, Int32)`, `PlugCopy(DeviceItem, Int32)` | device-item-operations.md §6; hardware-parameters.md | als Stub angelegt, typisierter Aufruf, nicht live getestet |
| `HardwareObject.GetPlugLocations()` → `IList<PlugLocation>` | device-item-operations.md §9 (Beispieltyp); hardware-parameters.md | als Stub angelegt, typisierter Aufruf, nicht live getestet |
| `Siemens.Engineering.HW.Extensions.PlugLocation` (Typ), `.PositionNumber`, `.Label` | device-item-operations.md §9 und API-Referenz; Typliste in base.json | Typ als Stub angelegt; Eigenschaften per Reflection gelesen (Typen undokumentiert) |
| `DeviceItem.Delete()` | device-item-operations.md §7; device-creation.md API-Referenz | als Stub angelegt (`void`), nicht live getestet |
| `DeviceItem.ChangeType(String)` | device-item-operations.md §8; device-creation.md API-Referenz | **per Reflection über die exakte Signatur aufgerufen** (Rückgabetyp undokumentiert); kein Stub |
| `ModuleInformationProvider.FindContainerTypes(String)` → `string[]`, `FindModuleTypes(String)` → `string[]` | device-item-operations.md §9; Typliste in base.json | als Stub angelegt, typisierter Aufruf |
| `Project.HwUtilities` (`HardwareUtilityComposition`) | device-item-operations.md §9 | **per Reflection gelesen** (deklarierender Typ undokumentiert) und aufgezählt |
| `IEngineeringObject.SetAttributes(IEnumerable<KeyValuePair<string, object>>, AttributeDelegate)`; `AttributeDelegate(AttributeConfiguration)`; `AttributeConfiguration.Name`, `.Message`, `.CurrentSelection`; `AttributeChoiceSelection.Abort`/`.Ignore` | device-item-operations.md §10; device-attributes.md §4; Typliste in base.json | **per Reflection gebunden** (exakte Parametertypen, Delegate aus dem Laufzeittyp erzeugt, Member per Name gesetzt); kein Stub |
| `IEngineeringObject.GetAttributeInfos()`, `GetAttribute`, `SetAttribute`; `EngineeringAttributeInfo.Name`, `.AccessMode`, `.SupportedTypes` | bestehende Stubs; device-attributes.md §5 | bestehend |
| `Address.StartAddress` (Int32, lesen/schreiben) | addresses-and-channels.md §2 | Setter zum Stub hinzugefügt |
| Dynamische Attribute von `Address` `Context`, `ProcessImage`, `IsochronousMode`, `InterruptObNumber` | addresses-and-channels.md §2–3 | bestehendes `GetAttribute`/`SetAttribute` |
| `PlcTagComposition.Create(String, String, String)`, `Find`, `PlcTag.Delete()`, `.Name`, `.DataTypeName`, `.LogicalAddress`, `.ExternalAccessible/Visible/Writable` | tags-types.md §4; Siemens tags-and-tagtables SKILL.md | bestehende Stubs |
| `PlcTag`-Attribut `IsSafety` | tags-types.md §4 (Attributtabelle) | gelesen mit `GetAttribute` (nur für die Ablehnung) |
| `CustomIdentityProvider.Get(String)` → `String`, `Set(String, String)`, `Remove(String)`; `CustomIdentityNotFoundException` | `Siemens.Engineering.Base.xml` (V21-IntelliSense) | **per Reflection gebunden** über exakten Namen und Parameter; Provider unter `GetServiceInfos()` gefunden; kein Stub, nicht live getestet |
| `NetworkPort` (Dienst), `.ConnectToPort(NetworkPort)`, `.DisconnectFromPort(NetworkPort)`, `.ConnectedPorts` ("Internal use only"), `.Interface`; `NetworkInterface.OwnedBy` | `Siemens.Engineering.Base.xml` (V21-IntelliSense); Siemens-Skill devices-and-hardware | Dienst unter `GetServiceInfos()` gefunden, Member **per Reflection gebunden**; `OwnedBy` vorhandener Stub; nicht live getestet |
| `IoSystem.Delete()`, `IoConnector.DisconnectFromIoSystem()`, `NetworkInterface.TransferAreas`/`.MulticastableTransferAreas`, die `Create`-Überladungen, Eigenschaften und `Delete()` von `TransferArea`/`MulticastableTransferArea`, `TransferAreaType` | `Siemens.Engineering.Base.xml` (V21-IntelliSense); Siemens-Skill devices-and-hardware | **per Reflection gebunden** über exakten Namen und Argumenttypen; `GetService<NetworkInterface>`, `IoConnectors`, `ConnectedToIoSystem` vorhandene Stubs; nicht live getestet |
| `CaxProvider.Export`/`Import` (alle dokumentierten Überladungen), `CaxImportOptions`, `TransferResult`, `TransferResultMessage`, `TransferResultState` | `Siemens.Engineering.Step7.xml` (V21-IntelliSense); TIA-Portal-Hilfe V20 "CAx-Daten exportieren und importieren" | Provider unter `GetServiceInfos()` des Projekts, **per Reflection gebunden**; nicht als Stub angelegt, nicht live getestet |
| `PlcTag.Comment` (`MultilingualText`) | tags-types.md (`PlcTag`: "The multilingual comment of the PlcTag"); language-settings.md §5 (Modell MultilingualText) | als Stub angelegt (Getter), typisierter Aufruf, nicht live getestet |
| `MultilingualText.Items`, `MultilingualTextItem.Language` / `.Text` (get/set), `MultilingualTextItemComposition.Find(Language)` | language-settings.md §5 (Lesen und Schreiben eines mehrsprachigen Texts) | `Items`, `Language`, `Text` bestehende Stubs; `Find(Language)` als Stub angelegt |
| Datentyplängen (Bool … DTL, siehe `plc_read`) | Informationssystem von TIA Portal V21, Seiten "Data types" (docs.tia.siemens.cloud, die Seiten BOOL … DTL) | kein API-Member; Werte in `PlcAddressSpace`; TIME, LTIME, S5TIME, DATE nicht bestätigt und ohne Größe belassen |

Die Compile-Zeit-Member werden durch
`reference-stubs/TiaMcpServer.OpennessReferenceProbe/HardwareModulesReferenceSurface.cs` ausgeübt; das Bauen
der Probe gegen die installierte V21-PublicAPI prüft die deklarierten Rückgabe- und Parametertypen.

## Weggelassen, und warum

- `DriveItemHardwareModule.ChangeType` (Startdrive), Motor-/Geberprojektion, 
  `CreateWithItem`-Varianten und Kopiervorlagen: SINAMICS-spezifisch oder außerhalb dieses Umfangs.
- `ModuleInformationProvider.GetTypeIdentifierNormalized`: Rückgabetyp undokumentiert und nicht benötigt.
- `HardwareCatalog.Find(String, String)` (Filter kompatibel/steckbar) und `HwIdentifiers`: für den
  angeforderten Umfang nicht benötigt; `search_equipment_catalog` ist unverändert.
- `Address.AssignProcessImageToOrganizationBlock(OB)`: braucht eine Bausteinauflösung über die Tools hinweg; nicht
  implementiert.
- „Gerät tauschen“ einer ganzen Station: kein dokumentiertes `Device.ChangeType`; das Kopf-/CPU-Element
  mit `change_device_item_type` ändern.
- PSC-Export, Frontpanel-Logo, GSD-`GetPrmData`/`SetPrmData`: nicht im angeforderten Umfang. `ImConnection` eines
  Erweiterungsbaugruppenträgers, Fernwirk-Datenpunktdateien, Subnetze eines Knotens, der IO-Controller eines Connectors,
  die PC-Schnittstellenzuordnung, Aktualisierungen der Baugruppenbeschreibung und Kommunikationsverbindungen kamen mit
  Paket G hinzu (siehe [Referenz Vervollständigung der V21-API](V21_API_COMPLETION_SUMMARY.de.md)).
- Eine Variable nativ verschieben: kein dokumentiertes Member (siehe `move_tag`); `move_tag` legt sie neu an.
- Platzieren von Typen mit 64 Bit und mehr mit `find_free_address` (LReal, LWord, LInt, ULInt, LTOD, DT,
  LDT, DTL): Für diese Breiten ist in den Quellen keine I/Q/M-Schreibweise dokumentiert. Bestehende Variablen dieser
  Typen werden in beiden Operationen mit ihrer dokumentierten Länge bemessen.
- Längen von TIME, LTIME, S5TIME und DATE: Die für diese Änderung abgerufenen Seiten des Informationssystems wurden
  vor ihren Längentabellen abgeschnitten, daher bleiben diese Typen ohne Größe, statt geraten zu werden.
- Gerätegruppen-Operationen und das Verschieben von Geräten zwischen Gruppen: unveränderte Lücke.

## Live-Abnahmeliste

An einem Wegwerfprojekt (Einzelplatz) mit einer S7-1500-Station, im Modus `full`:

1. `list_plug_locations` auf dem Baugruppenträger; mit `plug_device_item` eine DI-Baugruppe an einer freien Position stecken (Probelauf,
   dann tatsächlich); mit `read_hardware_config` bestätigen. Auf einer S7-1500-Profilschiene und einem ET-200SP-Baugruppenträger wiederholen
   (K05): Das Element muss gelingen, mit `placedUnder` gleich dem Gerät und `result.itemPath` gleich dem Pfad auf
   Geräteebene. Auf dem ET-200SP-Gerät selbst muss ein abgelehntes `CanPlugNew` das Baugruppenträger-Element nennen (K29).
2. `plug_device_item` an einer belegten Position → `validation_error` (CanPlugNew false).
3. `move_device_item` auf einen anderen freien Steckplatz; `copy_device_item`; `delete_device_item` der Kopie.
4. `change_device_item_type` auf eine andere Firmwareversion derselben Baugruppe; den gemeldeten
   Typbezeichner prüfen.
5. `inspect_network_object` auf der DI-Baugruppe, dann `set_hardware_attributes` mit einem dokumentierten
   Parameter im Modus `bulk`, erneut im Modus `sequential` und einmal mit einem ungültigen Wert unter
   `abort` und `ignore`; das Verhalten des AttributeDelegate aufzeichnen.
6. `list_io_addresses`; `set_io_address` auf eine neue Eingangs-Anfangsadresse; den gekoppelten
   Ausgangsbereich prüfen und dass Variablen nicht neu verdrahtet wurden. Mit der S7-1500-Baugruppe DI 32 wiederholen
   (`6ES7 521-1BL00-0AB0/V2.2`, K06): Ihre Adressen müssen mit `subItemPath` `DI_32` aufgelistet werden und
   über den Selektor der Baugruppe beschreibbar sein.
7. `find_module_types` / `find_container_types` für die Bestellnummer der Baugruppe.
8. `export_io_map` in eine neue Datei, erneut ohne `overwrite` (abgelehnt), dann mit `overwrite:true`;
   einem Gerät einen kommentarartigen Namen geben, der mit `=` beginnt, und die CSV in einer Tabellenkalkulation öffnen; einen
   UNC-Freigabepfad (akzeptiert) und einen `\\?\`-Pfad (abgelehnt) ausprobieren.
9. `read_address_occupancy` und `find_free_address` (M Int, I Bool mit `requireHardware`), mit einer
   LReal-Variable auf `%MB100` und einer Variable eines PLC-Datentyps im Bereich; prüfen, wie TIA Portal die
   LReal-Adresse schreibt, und es aufzeichnen.
10. `move_tag` zwischen zwei Tabellen mit einem Kommentar in zwei Projektsprachen; Name, Typ,
   Adresse, Flags und beide Kommentartexte verifizieren; `compile_check`.
11. `set_hardware_attributes` im Modus `bulk` mit einem Wert, den TIA Portal ablehnt; die
   Member von `AttributeConfiguration` aufzeichnen und ob `CurrentSelection` `Abort`/`Ignore` annimmt.
12. 1 und 10 in einer lokalen Multiuser-Sitzung wiederholen.
13. `list_port_connections` an zwei PROFINET-Stationen; `connect_ports` Port 1 der CPU mit Port 1 der ET 200SP (Probelauf, dann
   echt), mit der Topologiesicht vergleichen, `connect_ports` erneut (Ablehnung festhalten), `disconnect_ports`; die Leitungslänge
   eines Ports mit `inspect_network_object` / `set_hardware_attributes` setzen.
14. `list_transfer_areas` an einer I-Device-Schnittstelle und an einer DDX-fähigen Schnittstelle; `create_transfer_area`
   (transferAreas `CD`, multicastable `DDX` mit Partner), `update_transfer_area` eines gelisteten Attributs, `delete_transfer_area`;
   `disconnect_from_io_system` einer ET 200SP, `delete_io_system`; `create_io_system` am X3-DP-Knoten von PLC_F1 und
   `delete_io_system` des Ergebnisses.
15. `export_cax` des Projekts und eines Geräts (mit und ohne `logFilePath`), die .aml öffnen; `import_cax` in ein frisches
   Testprojekt mit jeder `importOption`; eine defekte Datei importieren und die Meldungen festhalten.
