# Technologieobjekte (`plc_read` / `plc_write`)

**Sprache:** [English](TECHNOLOGY_OBJECTS_SUMMARY.md) · Deutsch

> **Nicht live getestet.** Jede der folgenden Operationen ist anhand der unter
> [Herkunft](#herkunft-jedes-verwendeten-siemens-members) genannten V21-Quellen implementiert und offline gegen die
> skriptgesteuerten FakeWorker-Szenarien `technology-objects` und `technology-objects-silent` (deren Create,
> CreateGroup, Connect und Import nichts ändern, sodass die Rücklese-Prüfungen fehlschlagen müssen) sowie mit
> Linux-Unit-Tests durchlaufen. Jedes technologieobjektspezifische Siemens-Member wird zur Laufzeit gebunden und
> schlägt fail-closed mit `unsupported_capability` fehl, wenn das installierte V21 es nicht exakt wie dokumentiert
> bereitstellt. Führe die [Live-Abnahmeprüfungen](#live-abnahmeprüfungen) aus, bevor du dich auf diese Operationen
> verlässt.

Technologieobjekte (TOs) liegen in `PlcSoftware.TechnologicalObjectGroup`, immer an der PLC-Wurzel: Der
Siemens-Skill zu Technologieobjekten stellt fest, dass Software Units keine haben. Ein TO ist ein
`TechnologicalInstanceDB`; seine Parameter sind `TechnologicalParameter`s; seine Hardwareverbindung läuft über die
dokumentierten Connection-Provider.

## Warum plc_read und plc_write

TOs sind PLC-Software-Inhalt und werden wie Variablentabellen und Beobachtungstabellen adressiert: ein PLC-Selektor
(`plcName`), ein Objektname und ein Gruppenpfad (`folderPath`). Die vorhandenen PLC-Tools bringen bereits jeden
Mechanismus mit, den die Domäne braucht (die abgesicherte Single-Call-Pipeline mit Dry Run, Info-Guards,
Rücklese-Verifikation und Audit; den PLC-Arbeitszustand, mit dem ein späteres Item ein Objekt verwenden kann, das
ein früheres Item angelegt hat; den Content-Hash-Roundtrip der Beobachtungs- und Forcetabellen), daher wurden die
Operationen dort ergänzt statt in einem neuen Tool-Paar. Die Tool-Oberfläche bleibt bei **11 Tools in read-only,
30 in read-write und 30 in full** (seit `retrieve_project`) (`library_read`/`library_write` wurden ein eigenes Paar, weil ein
Bibliotheksselektor keine PLC ist). Die Feldmenge von `PlcOperationRequest` wächst; jedes Feld ist je Operation
durch den Katalog eingeschränkt (`PlcOperationCatalog.TechnologyObjects.cs`), sodass ein nicht verwendetes Feld ein
`validation_error` ist.

## Zugriff

| Operationen | Capability | read-only | read-write | full |
| --- | --- | --- | --- | --- |
| `list_technology_objects`, `get_technology_object_parameters`, `get_technology_object_hardware_connection` | `Observe` | ja | ja | ja |
| `get_technology_object_content` (temporärer SimaticML-Export) | `TemporaryExport` | ja | ja | ja |
| die zwölf `plc_write`-Operationen, einschließlich `export_technology_object` (es schreibt eine Datei des Aufrufers außerhalb von TIA Portal, wie `export_io_map`) | `ProjectMutation` | nein | ja, ohne Rückfrage | ja |

Die Operationen wirken auf das gebundene Projekt (Standalone oder lokale Session, wie jede
`plc_read`/`plc_write`-Operation); die Worker-Methoden tragen die öffentlichen Operationsnamen
(`TechnologyObjectMethods` in `TiaMcpServer.Contracts/Plc/TechnologyObjects`), Parameter werden in
`WorkerRequest.TechnologyObject` übertragen, und `ProjectCapabilityCatalog.OperationFor` ordnet sie
`plc_read`/`plc_write` zu. Nichts ist hart blockiert außer ungültiger oder mehrdeutiger Eingabe: Die Guards für
Technologieobjekte sind informativ, und der gemeinsame Block-Guard `plc_state_unverifiable` greift nur, wenn die
Planung den Zustand nicht sehen kann (ein unvollständiges Inventar oder ein fehlgeschlagener Planungs-Lesevorgang,
siehe [Guards](#guards-und-verifikation)).

## plc_read-Operationen

| Operation | Felder | Ergebnis |
| --- | --- | --- |
| `list_technology_objects` | optional `plcName` (weggelassen: jede PLC), optional `folderPath` (eine Gruppe einer PLC: nur deren Teilbaum) | `{ plcs[{ plcName, deviceName, groups[{ path, name }], technologyObjects[{ name, groupPath, number, systemLibElement, systemLibVersion, runtimeType, unreadable[] }], complete }] }`; `systemLibElement`/`systemLibVersion` sind `OfSystemLibElement`/`OfSystemLibVersion` als Text, `number` ist die DB-Nummer; höchstens 5.000 Objekte plus Gruppen je PLC (darüber `complete:false`) |
| `get_technology_object_parameters` | `name`; optional `plcName`; `prefix` (ordinal), `offset` (Standard 0), `limit` (1-2000, Standard 200) **oder** `parameterName` (ein exaktes `Find`) | `{ name, groupPath, parameterName, prefix, totalCount, matchedCount, offset, limit, nextOffset, parameters[{ name, valueKind, valueType, value, enumMembers[], enumMembersTruncated, enumFlags }] }` |
| `get_technology_object_hardware_connection` | `name`; optional `plcName` | `{ services[], interfaces[{ interface, provider, index, runtimeType, isConnected, properties[{ name, kind, value, runtimeType, modulePath }], unreadable[] }], unavailableServices[] }`; `unavailableServices` nennt jeden Provider, den `GetServiceInfos()` auflistet, `GetService` aber nicht zurückgibt (seine Schnittstellen fehlen, der Lesevorgang gelingt trotzdem; live auf V21 am 2026-11-02 bei anderen Services beobachtet, D13/D14); Connect und Disconnect schlagen für einen solchen Provider mit `unsupported_capability` fehl, nichts geändert |
| `get_technology_object_content` | `name`; optional `plcName` | `{ format:"xml", content, contentHash, warnings }`: der SimaticML-Export, aus dem wie bei Baustein-Exporten `DocumentInfo` entfernt ist; der Hash speist `update_technology_object_content` |

### Parameterwerte

Die V21-Referenz dokumentiert `TechnologicalParameter.Value` ohne Typ, und der Siemens-Skill sagt, dass der
akzeptierte Typ vom Parameter abhängt und dass eine inkompatible Zuweisung eine Exception wirft. Der einzige
Typnachweis, den die API bietet, ist der Laufzeittyp des aktuellen Werts, daher werden Werte nach ihm typisiert:

| `valueKind` | Laufzeittypen | `value`-Text | Aus `value` schreibbar |
| --- | --- | --- | --- |
| `boolean` | `System.Boolean` | `true`/`false` | ja (`true`/`false`, ohne Beachtung der Groß-/Kleinschreibung) |
| `integer` | `SByte` … `UInt64` | invarianter Integer | ja, bereichsgeprüft für genau diesen Typ |
| `real` | `Single`, `Double`, `Decimal` | Float/Double: der kürzeste Text, der wieder zu denselben Bits geparst wird (unter gleich kurzen der dem exakten Wert nächste), im `R`-Layout von .NET 10 (einfache Schreibweise, außer die führende Zehnerpotenz liegt unter -4 oder bei mindestens max(Stellen, 17 / 9), dann `d.dddE+XX`); `-0` behält sein Vorzeichen. Decimal: Signifikand und Skala (`1.50`) | siehe [Real-Werte](#real-werte) |
| `string` | `System.String` | der Text (bei 4.000 Zeichen mit einer Markierung abgeschnitten) | ja, jeder Text mit höchstens 4.000 Zeichen (ein längeres Rücklesen könnte nicht ungekürzt verglichen werden) |
| `enum` | jede Enum | Member-Name; ein kombinierter `[Flags]`-Wert als die Namen der Member, aus denen er besteht (`A, B`, vom größten Wert abwärts genommen, aufsteigend aufgelistet wie bei `Enum.ToString`), jeder andere undefinierte Wert als sein invarianter Integer, nie der kulturabhängige Enum-Text der Laufzeit; wie jeder Text bei 4.000 Zeichen mit der Markierung abgeschnitten; `enumMembers` listet die ersten 200 Member, `enumMembersTruncated` gibt an, dass es mehr gibt, `enumFlags` kennzeichnet eine `[Flags]`-Enum | ja, exakter Member-Name; bei einer `[Flags]`-Enum unterschiedliche exakte Member-Namen, durch Kommas getrennt, bitweise kombiniert (ein wiederholter Name oder das Null-Member innerhalb einer Kombination wird als mehrdeutig abgelehnt). Der Host lehnt einen Namen nur gegen eine vollständige Member-Liste ab; bei einer gekürzten Liste prüft der Worker ihn gegen die Live-Enum. Eine Ablehnung nennt die unbekannten Namen und listet die ersten 200 Member, jede Liste bei 4.000 Zeichen mit der Markierung abgeschnitten |
| `plcTag` | `PlcTag` | Variablenname | nur mit `tableName` + `tagName` |
| `null` | kein aktueller Wert | null | nur eine `PlcTag` (`tableName` + `tagName`), weil der Typ nicht bestimmt werden kann |
| `object` | alles andere | null (Typ in `valueType`) | nein |
| `unreadable` | das Lesen warf einen behebbaren Openness-Fehler | null | nein |

### Real-Werte

Der Worker (.NET Framework) und der Host (.NET 10) parsen und formatieren Real-Werte unterschiedlich (der
Framework-Parser rundet nicht korrekt, parst `Single` über `Double` und verliert das Vorzeichen von `-0`; seine
`R`/G16-Ausgabe weicht ab), daher erfolgt jede Real-Konvertierung und -Darstellung durch `TechnologyObjectReals`
(Contracts) ausschließlich mit exakter Ganzzahlarithmetik (`BigInteger`) und dem IEEE-754-Bitlayout, nie mit einem
Parser oder Formatierer der Laufzeit. Tests lesen die kompilierte IL (System.Reflection.Metadata, sodass der
net48-Worker wie jede Datei gelesen wird): `TechnologyObjectReals` und `TechnologyObjectValues` machen keinen
Aufruf, der einen Real-Wert parst, formatiert oder konvertiert, und auch keinen, der einen geboxten Real-Wert
formatieren könnte (`String.Format`, `Concat`/`Join` über Objekte, `StringBuilder`-Appends von Objekten,
Interpolation-Handler, `IFormattable.ToString`, `Object.ToString`, `System.Convert`); und kein
Technologieobjekt-Code von Contracts, dem Worker, dem FakeWorker oder dem Host (Methoden ausgewählt nach ihrer
Quelldatei im PDB, einschließlich Lambdas, lokaler Funktionen, Iteratoren und async-Zustandsautomaten; jede
Technologieobjekt-Quelldatei muss IL beitragen, und ein Worker- oder Host-Build, der älter als seine Quellen ist,
wird abgelehnt) ruft einen Real-Parser oder -Formatierer der Laufzeit oder irgendetwas anderes auf, das einen
Real-Wert entgegennimmt oder zurückgibt, außer `BitConverter` und dem obigen Kern; außerhalb der skriptgesteuerten
Werte des FakeWorkers boxt keiner einen Real-Wert. Selbsttests an Beispielmethoden zeigen, dass jede Regel greift
(auch in compilergenerierten Rümpfen und über die Auswahl nach Quelldatei) und sauberer Code besteht. Die
Ergebnisse werden gegen den IEEE-korrekten Parser und die `R`-Ausgabe von .NET 10 an zufälligen und extremen
Werten sowie an jeder Zweierpotenz geprüft; der kürzeste Text entspricht dem `R` von .NET 10, außer bei den zwei
Doubles, deren `R`-Text keinen Roundtrip besteht, 2^-25 und 2^-958 (`R` liefert den kürzesten Text des Doubles
darunter); diese sind auf die kürzesten Texte einer unabhängigen Implementierung festgelegt
(`2.9802322387695312E-08`, `4.1045368012983762E-289`), ebenso der Float 2^-25 (`2.9802322E-08`). Somit erzeugt der
.NET-Framework-Worker, der denselben rein ganzzahligen Code ausführt, dieselben Bits und Texte. Der Bereich eines
Exponenten wird entschieden, bevor irgendeine Zehnerpotenz gebildet wird (mit Vergleichen, die für jeden
64-Bit-Exponenten nicht überlaufen können), sodass `1e-999999999999` oder `1e999999999999` in begrenzter Zeit
abgelehnt wird und nie eine Exception wirft. Ein `value`, der länger als 4.000 Zeichen ist, wird unter Nennung der
Grenze (`'value' has 4001 characters; at most 4000 are accepted`) vom Host-Katalog vor jedem Worker-Aufruf
abgelehnt, und vom Worker mit demselben Text. Der Worker ist die einzige Instanz für Bereich und Exaktheit gegen
den Live-Typ; der Host-Planer lehnt nur Text ab, der keine invariante endliche Zahl ist. Ein abgelehnter Wert
lautet `Parameter '<parameter>' of '<object>': <reason>. Nothing was changed.`, gleich auf dem Host, im Worker und
im FakeWorker (`TechnologyObjectValues.ParameterRefusal`).

| Ziel | Akzeptiert | Abgelehnt (`validation_error`) |
| --- | --- | --- |
| `Single`, `Double` | invarianter Zahlentext, dessen korrekt gerundeter Wert (zum nächsten, bei Gleichstand zur geraden Zahl; Subnormale; Überlauf erst bei oder jenseits des größten endlichen Werts plus einer halben ulp) endlich ist, wenn der Text (als Zahl) der kürzeste Roundtrip-Text dieses Werts oder seine exakte Dezimalentwicklung ist: für den Float 0.1 sowohl `0.1` als auch `0.100000001490116119384765625`; `3.4028235E+38` (`float.MaxValue`); `-0` bleibt `-0` | jeder andere Text, unter Nennung des kürzesten Texts des nächsten Werts (`16777217` → `16777216`, Float `1e-50` → `0`, `0.10000000149` → `0.1`): `'16777217' is neither the shortest round-trip text nor the exact decimal value of the nearest System.Single; the nearest representable value is 16777216 (its shortest round-trip text). Write that text instead`; oder außerhalb des Bereichs, unter Nennung des größten Betrags |
| `Decimal` | eine Zahl, die der Typ exakt aufnimmt (96-Bit-Signifikand, Skala bis 28) | Text, den er nicht exakt aufnehmen kann, unter Nennung des tatsächlich nächsten darstellbaren Decimals über alle Skalen im selben Wortlaut (`... cannot be held exactly by System.Decimal (...); the nearest representable value is 0. Write that text instead`; `1e-40` → `0`, 29 Dezimalstellen, `7922816251426433759354395033.56` → `7922816251426433759354395033.5`), ein Betrag von 2^96 oder mehr (außerhalb des Bereichs, unter Nennung des größten Betrags) und `-0` (das Vorzeichen einer Decimal-Null wird nicht verglichen, daher ist es mehrdeutig) |

Ein Float- oder Double-Rücklesewert muss dieselben Bits wie der geschriebene Wert haben (`-0` ist nicht `0`).

## plc_write-Operationen

Alle laufen durch die abgesicherte Pipeline: `dryRun:true` zeigt Effekte und Guards ohne Mutation an, 1-50
geordnete Items, Stopp beim ersten Fehler, kein Rollback, ein Audit-v2-Datensatz je Aufruf (Bestätigung `none` in
read-write, `policy` in full). Spätere Items dürfen Objekte verwenden, die früher im Aufruf angelegt wurden
(`dependsOn`; unmittelbar vor ihrer Mutation gegen den realen Zustand neu geplant). Nach einem
`import_technology_object` sind die importierten Namen nicht bekannt, bevor er läuft, daher hängt jedes spätere
TO-Item des Aufrufs von ihm ab.

| Operation | Felder | Siemens-Aufruf | Rücklesen im Worker |
| --- | --- | --- | --- |
| `create_technology_object` | `name`, `technologyType` (z. B. `TO_PositioningAxis`), `version` (z. B. `6.0`); optional `plcName`, `folderPath` | `TechnologicalInstanceDBComposition.Create(String, String, Version)` auf den `TechnologicalObjects` der Gruppe, nach der Prüfung, dass kein TO der PLC diesen Namen hat (sonst `validation_error`) | die Gruppe listet genau ein Objekt dieses Namens, das nur das angelegte sein kann |
| `delete_technology_object` | `name` | `Delete()` | die Gruppe listet es nicht mehr |
| `rename_technology_object` | `name`, `newName` | generisches `SetAttribute("Name")`, nachdem `GetAttributeInfos()` `Name` als schreibbar listet | `GetAttribute("Name")` und die Auflistung der Gruppe |
| `create_technology_object_group` | `folderPath` (die neue Gruppe) | `TechnologicalInstanceDBUserGroupComposition.Create(String)` auf den `Groups` der übergeordneten Gruppe, nach der Prüfung, dass die übergeordnete Gruppe keine Gruppe dieses Namens hat | die übergeordnete Gruppe listet genau eine Gruppe dieses Namens und eine Gruppe mehr als zuvor |
| `delete_technology_object_group` | `folderPath` (eine Benutzergruppe) | `TechnologicalInstanceDBUserGroup.Delete()`; die übergeordnete Gruppe wird vor dem Löschen über den Pfad aufgelöst | die übergeordnete Gruppe listet sie nicht mehr (immer geprüft) |
| `rename_technology_object_group` | `folderPath`, `newName` | generisches `SetAttribute("Name")` mit der Schreibbarkeitsprüfung | `GetAttribute("Name")` |
| `set_technology_object_parameter` | `name`, `parameterName`; `value` **oder** `tableName` + `tagName` | `Parameters.Find(name)`, dann der `Value`-Setter mit dem Wert, konvertiert in exakt den Laufzeittyp des aktuellen Werts, oder die aufgelöste `PlcTag` | ein frisches `Find`: Der Wert muss exakt gleich sein (gleicher Typ), eine `PlcTag` per `Equals`; sonst `postcondition_failed` mit beiden Werten |
| `connect_technology_object_hardware` | `name`, `connectionInterface`, `connectionKind` und die Felder dieser Überladung (unten) | die Schnittstelle wird gelesen (`previousInterface`), dann die dokumentierte `Connect`-Überladung | die Schnittstelle liest sich als verbunden, und die dokumentierte Zieleigenschaft der Überladung liest das angeforderte Ziel zurück ([unten](#hardwareverbindungen)); sonst `postcondition_failed` |
| `disconnect_technology_object_hardware` | `name`, `connectionInterface`; optional `interfaceIndex` | `Disconnect()` (für ident nicht dokumentiert) | `IsConnected` muss `false` lesen |
| `export_technology_object` | `name`, `filePath`; optional `overwrite` | `TechnologicalInstanceDB.Export(FileInfo, ExportOptions.None)` in eine private temporäre Datei, dann an `filePath` abgelegt ([Dateien](#dateien)) | die Datei wird erneut gelesen: Ihr SHA-256 muss dem des Exports entsprechen; Länge und SHA-256 werden gemeldet |
| `import_technology_object` | `filePath`; optional `plcName`, `folderPath`, `overwrite` (`ImportOptions.Override`; Standard `None`) | `TechnologicalInstanceDBComposition.Import(FileInfo, ImportOptions)` auf den `TechnologicalObjects` der Gruppe | jedes gemeldete Objekt ist in der Gruppe gelistet; wenn `Import` keine Objektliste zurückgibt, werden die in der Gruppe neuen Objekte gemeldet (mit einer Warnung), und keines neu ohne `overwrite` ist `postcondition_failed` |
| `update_technology_object_content` | `name`, `content`, `expectedContentHash` (xml, aus `get_technology_object_content`) | der Export wird neu gehasht (`state_changed`, wenn er abweicht), das Dokument muss wie der Export genau das adressierte Objekt beschreiben (gleiches Wurzelelement und gleiche Kinder der Wurzel nach vollem XML-Namen, `DocumentInfo` ignoriert; genau ein Objektelement mit genau einer `AttributeList`, die genau einen `Name` enthält, gleich dem Namen des Objekts ohne Trimmen), dann `Import(file, ImportOptions.Override)` in die Gruppe des Objekts | der Import lieferte genau dieses Objekt zurück (wenn er eine Liste zurückgibt), und die Gruppe listet es |

Objekte werden über den Namen im gesamten TO-Baum der PLC adressiert (ohne Beachtung der Groß-/Kleinschreibung,
genau ein Treffer; mehrere sind `target_ambiguous`), Gruppen über den Pfad (`/` ist `TechnologicalObjectGroup`;
jedes Segment ohne Beachtung der Groß-/Kleinschreibung und eindeutig). Ein Create, dessen Name bereits von einem TO
der PLC verwendet wird (Namen sind je PLC eindeutig: ein TO ist ein DB), oder dessen Gruppenpfad bereits existiert,
ist ungültige Eingabe: Der Planer lehnt es anhand des Inventars ab (ein früheres Löschen im selben Aufruf gibt den
Namen frei), und der Worker prüft unmittelbar vor `Create` erneut. Es wird erwartet, dass TIA Portal eine solche
Kollision selbst ablehnt, aber das ist nicht verifiziert, und die Vorprüfung ist es, die das danach gefundene
Objekt zum angelegten macht. Diese Vorprüfungen und die Nachbedingungen für Create, Delete, Gruppen und Import sind
Siemens-freie Regeln (`TechnologyObjectWriteChecks`, Contracts), die sowohl der Worker als auch der FakeWorker
aufrufen. Ein Name, der von einem Baustein verwendet wird, der kein TO ist, steht nicht im TO-Inventar; `Create`
schlägt dann so fehl, wie TIA Portal es meldet. Akzeptierte Typ/Versions-Paare hängen von der CPU-Firmware und der
Installation ab. Der Siemens-Skill berichtet, dass auf seinem V21-Setup `TO_PositioningAxis` `10.0` benötigte
(`1.0`–`9.0`, `11.0` und `12.0` schlugen fehl); live auf V21 am 2026-11-02 (Projekt MCP_Livetest_01, CPU 1516-3
PN/DP FW V2.9, Defekt K23) wurde `10.0` abgelehnt ("The type or version of the library element
'TO_PositioningAxis.10.0' does not exist", `engineering_rejected`) und `6.0` angelegt, wie am 2026-11-01 mit
derselben CPU-Firmware ([Zwischenbericht](../superpowers/acceptance/reports/2026-11-01-fork-live-acceptance-interim.md),
Zeile 10). Der Server rät oder probiert nie eine Version.

### Hardwareverbindungen

Der Siemens-Skill bezieht `AxisHardwareConnectionProvider` als Service des TO und warnt, dass das Schreiben von
`Actor.*`-Parametern gelingt, ohne den Antrieb zu verbinden. Der Worker listet daher die Services des Objekts mit
`IEngineeringServiceProvider.GetServiceInfos()` auf (die dokumentierte Discovery, vor `GetService` aufgerufen),
nimmt jeden dokumentierten Provider, dessen Typname exakt übereinstimmt, und ruft ihn mit
`System.IServiceProvider.GetService(Type)` ab:

| `connectionInterface` | Provider (V21-Typ) | Schnittstellenobjekt |
| --- | --- | --- |
| `actor`, `torque` | `Motion.AxisHardwareConnectionProvider` | `ActorInterface`, `TorqueInterface` |
| `sensor` | `Motion.AxisHardwareConnectionProvider` (Achse) oder `Motion.EncoderHardwareConnectionProvider` (Geber) | `SensorInterface`; eine Komposition wird über `interfaceIndex` adressiert (nullbasierte `Item(Int32)`-Position; erforderlich, wenn mehrere existieren) |
| `measuringInput` | `Motion.MeasuringInputHardwareConnectionProvider` | der Provider selbst |
| `outputCam` | `Motion.OutputCamHardwareConnectionProvider` | der Provider selbst |
| `ident` | `Ident.IdentTechnologicalObjectProvider` | der Provider selbst (nur Connect) |

Ein Connect oder Disconnect löst nur die Provider auf, die seine `connectionInterface` braucht, in der Reihenfolge
dieser Tabelle (`TechnologyObjectPolicy.ProvidersFor`, gemeinsam genutzt von Host-Planer, Worker und FakeWorker):
`actor` und `torque` nur den Achs-Provider; `sensor` zuerst das `SensorInterface` des Achs-Providers und den
Geber-Provider nur als die dokumentierte Alternative, wenn der Achs-Provider keinen Sensor bietet
(`PreferredEndpoints`). Ein Provider, den `GetServiceInfos()` auflistet, `GetService` aber nicht zurückgibt, lässt
das Item (`unsupported_capability`, nichts geändert) nur dann fehlschlagen, wenn kein benötigter Provider die
Schnittstelle bedient. Live V21 2026-11-02 (Defekt K18, MCP_Livetest_01, `Achse_1` TO_PositioningAxis 6.0): Das
Objekt listete `EncoderHardwareConnectionProvider`, ohne dass `GetService` ihn zurückgab, während
`AxisHardwareConnectionProvider` die Aktor-Schnittstelle bediente; vor dieser Regel löste ein Aktor-Connect jeden
Provider auf und schlug am Geber-Provider fehl. Der Lesevorgang (`get_technology_object_hardware_connection`)
listet weiterhin jeden Provider und meldet nicht verfügbare in `unavailableServices`. Nach der Korrektur nicht live
getestet.

`connectionKind` wählt die dokumentierte Überladung; der Katalog lehnt jede Kombination ab, die die Referenz nicht
dokumentiert:

| `connectionKind` | Felder | actor / sensor | torque | measuringInput | outputCam | ident |
| --- | --- | --- | --- | --- | --- | --- |
| `address` | `inputAddress`, `outputAddress` (rohe Bitadressen, Byteadresse × 8, wie das Siemens-Beispiel sie herleitet), optional `connectOption` (Standard `Default`) | `Connect(Int32, Int32, ConnectOption)` | gleich | `Connect(Int32)` nur mit `inputAddress` | `Connect(Int32)` nur mit `outputAddress` | – |
| `module` | `modulePath` (exakte Device-Item-Namen vom Gerät abwärts) | `Connect(DeviceItem)` | gleich | `Connect(DeviceItem, Int32)` mit `channelIndex` | – | `Connect(DeviceItem)` |
| `modules` | `modulePath` (Eingang), `outputModulePath`; optional `connectOption` (Überladung mit drei Argumenten) | `Connect(DeviceItem, DeviceItem[, ConnectOption])` | gleich | – | – | – |
| `channel` | `modulePath`, `channelNumber`; optional `channelIoType`, `channelType` | `Connect(Channel)` | – | `Connect(Channel)` | `Connect(Channel)` | – |
| `tag` | `tableName`, `tagName` | `Connect(PlcTag)` (analog) | – | – | `Connect(PlcTag)` | – |
| `dbMember` | `dbMemberPath` | `Connect(String)` | gleich | – | – | – |

`ConnectOption`-Member werden über den exakten Namen auf der installierten Enum geparst (nur `Default` erscheint in
den Quellen). Der Lesevorgang meldet jede dokumentierte Eigenschaft jeder Schnittstelle nach ihrem Laufzeittyp
(`deviceItem` mit `modulePath`, `channel` mit dem `modulePath` seines Device Items, `plcTag`, `integer`, `boolean`,
`string`, `enum`); eine dokumentierte Eigenschaft, die auf dem installierten Typ fehlt, wird in `unreadable`
gelistet. Ident dokumentiert kein `IsConnected`: Sein Zustand (das `current` des Effekts, der Ersetzungs-Guard, das
Rücklesen) ist, ob `ConnectedIdentDevice` ein Gerät liest.

Der Siemens-Skill: "a successful call is not proof of a real connection"; Bestätigung über
`IsConnected`/`InputAddress`/`OutputAddress`. Ein Connect liest die Schnittstelle daher vor (`previousInterface` im
Ergebnis) und nach dem Aufruf und verlangt neben dem verbundenen Zustand, dass die dokumentierte Eigenschaft der
Überladung das angeforderte Ziel zurückliest (`TechnologyObjectConnectionEvidence` in Contracts, gemeinsam genutzt
von Worker, FakeWorker und dem Rücklesen im Host):

| `connectionKind` | Eigenschaft, die das Ziel zurücklesen muss | Verglichen (Worker; der Host vergleicht die dargestellten Werte) |
| --- | --- | --- |
| `address` | `InputAddress` und `OutputAddress` (Messeingang: `InputAddress`; Nockenausgang: `OutputAddress`) | Integer-Gleichheit mit der Anfrage |
| `module` | `InputOutputModule` (Messeingang: `InputModule` und `ChannelIndex`; ident: `ConnectedIdentDevice`) | `Equals` mit dem aufgelösten `DeviceItem` (Host: `modulePath`) |
| `modules` | `InputModule` und `OutputModule` | `Equals` mit den aufgelösten Device Items |
| `channel` | `Channel` | `Equals` mit dem aufgelösten `Channel` (Host: Modulpfad, Nummer und die angegebenen IoType/Type) |
| `tag` | `OutputTag` | `Equals` mit der aufgelösten `PlcTag` (Host: Variablenname) |
| `dbMember` | `PathToDBMember` | ordinale String-Gleichheit |
| `address` und `modules` mit `connectOption` (actor, sensor, torque) | zusätzlich `ConnectOption` ("ConnectOption that has been set when the connection was made"; `Default`, wenn bei `address` weggelassen) | `Equals` mit dem übergebenen Enum-Wert (Host: Member-Name) |

Eine Eigenschaft, die einen anderen Wert liest, schlägt mit `postcondition_failed` fehl und nennt beide; das
`result` des fehlgeschlagenen Items trägt dann das Schreibergebnis mit `previousInterface` und `interface` als
strukturierte Daten (auch bei einem fehlgeschlagenen Disconnect und einem Parameterschreibvorgang, der seinen Wert
nicht behielt), vom Host mit demselben Vertrag decodiert wie ein Erfolg. Eine Eigenschaft, die nicht gelesen werden
kann, wird mit einer Warnung nur akzeptiert, wenn die Schnittstelle vor dem Aufruf `IsConnected=false` las (dann
ist der Wechsel auf `true` der Nachweis für diesen Connect); wenn sie vorher verbunden war, ihr Zustand nicht lesbar
war, oder bei ident, ist der Connect nicht verifizierbar und schlägt mit `postcondition_failed` fehl. Ob die rohen
Bitadressen exakt wie übergeben zurückgelesen werden, ist eine Live-Abnahmeprüfung: Wenn nicht, schlägt jeder
`address`-Connect mit `postcondition_failed` und den zurückgelesenen Werten fehl.

### Dateien

`filePath` wird lexikalisch (anhand seines Texts, `TechnologyObjectPolicy.ValidateLocalFilePath`) vom Katalog und
erneut vom Worker geprüft: ein absoluter lokaler Pfad auf einem Laufwerk (`C:\...` oder `C:/...`), der eine Datei
benennt, ohne Steuer-, Format-, Platzhalter- oder reservierte Zeichen, ohne einen zweiten Doppelpunkt (alternative
Datenströme), ohne leere, `.`- oder `..`-Segmente, ohne ein Segment, das auf einen Punkt oder ein Leerzeichen endet
(Windows entfernt sie stillschweigend), und ohne einen reservierten Gerätenamen (`CON`, `PRN`, `AUX`, `NUL`,
`COM0`-`COM9`, `LPT0`-`LPT9` und die hochgestellten `COM¹²³`/`LPT¹²³`, mit oder ohne Erweiterung) in irgendeinem
Segment. UNC-Freigaben (`\\server\share\...`, `//server/share/...`) werden seit 2026-11-01 akzeptiert
(Entscheidung des Eigentümers); Gerätepfade (`\\?\`, `\\.\`, `\??\`) werden abgelehnt, weil sie die
Win32-Pfadnormalisierung umgehen. `CONIN$`, `CONOUT$` und `CLOCK$` zählen ebenfalls als reservierte Namen
(`TechnologyObjectPolicy.IsReservedDeviceName`, die eine Regel, die alle Prüfungen von Aufruferdateien verwenden).
Der Effekt zeigt den Pfad so, wie der Worker ihn verwendet (Backslash als Trennzeichen). Der Worker verlangt dann,
dass `Path.GetFullPath` exakt diesen Pfad zurückgibt (ohne Beachtung der Groß-/Kleinschreibung; ein kurzer
`8.3`-Name wird zum Beispiel mit dem aufgelösten Pfad abgelehnt), und meldet, was nur das Dateisystem sagen kann,
als Warnungen, nie als Ablehnungen: ein Ziel auf einem verbundenen Netzlaufwerk (`DriveInfo.DriveType == Network`;
Entscheidung des Eigentümers: Netzlaufwerke werden wie angefordert verwendet), eine Datei, die selbst ein
Reparse-Point ist (symbolischer Link), und ein vorhandenes übergeordnetes Verzeichnis, das ein Reparse-Point ist
(Junction, symbolischer Link oder Einhängepunkt).

Ein Export in eine vorhandene Datei braucht `overwrite:true` (dann der Guard `plc_overwrites_file`): Der Planer
prüft die Datei auf dem Host (Host und Worker teilen sich die Maschine), und der Worker prüft erneut. Der Export
geht zuerst in eine private temporäre Datei; seine Bytes werden als `.<name>.<guid>.tmp` neben `filePath`
bereitgestellt (`TechnologyObjectFiles`, Contracts) und an ihren Platz verschoben, sodass `filePath` nie teilweise
geschrieben wird. Eine neue Datei wird mit `File.Move` abgelegt. Eine vorhandene Datei mit `overwrite:true` wird mit
`File.Replace` getauscht, wobei die vorherige Datei als `.<name>.<guid>.bak` daneben behalten wird (.NET Framework
hat kein überschreibendes `File.Move`). Wenn `ReplaceFile` fehlschlägt, wird eine vorherige Datei, die es bereits
auf den Backup-Namen umbenannt hat, zuerst zurückgestellt; dann wird für Volumes ohne `ReplaceFile`-Unterstützung
(manche FAT- oder SMB-Volumes; verbundene Netzlaufwerke sind erlaubt) die vorherige Datei auf den Backup-Namen
umbenannt und die bereitgestellte Datei hineinverschoben, wobei die vorherige Datei wiederhergestellt wird, falls
das fehlschlägt; ein Ziel, das zwischenzeitlich außerhalb des Aufrufs entfernt wurde, erhält wie angefordert den
neuen Inhalt. Ein Fehler lässt `filePath` wie es war, oder stellt es aus dem Backup wieder her, wann immer das
Zurückverschieben gelingt, und jede Fehlermeldung endet damit, was aus den drei Dateien geworden ist, sodass beide
Inhalte wiederhergestellt werden können: `filePath` (nicht geändert, auf die vorherige Datei wiederhergestellt,
fehlend oder mit nicht verifiziertem Inhalt), die bereitgestellte Datei (`the new content is kept at '<path>'`,
entfernt oder verschwunden) und das Backup (`the previous file is kept at '<path>'`, keines angelegt oder keines
übrig); ein Test durchläuft jeden Pfad (jedes fehlschlagende Verschieben, eine fehlgeschlagene Wiederherstellung,
eine außerhalb des Aufrufs entfernte bereitgestellte Datei oder Zieldatei, ein fehlgeschlagenes Rücklesen, eine
geänderte Datei) und prüft die Dateien gegen die Meldung. Ein Backup, das nach Erfolg nicht gelöscht werden kann,
wird als Warnung gemeldet, die es nennt. Die geschriebene Datei wird erneut gelesen, und ihr SHA-256 muss dem des
Exports entsprechen (sonst `postcondition_failed`, das Backup bleibt erhalten und wird genannt); das Backup wird
erst danach gelöscht. Der führende Punkt sortiert die bereitgestellte und die Backup-Datei nur neben das Ziel; sie
sind nicht versteckt. Das Exportverzeichnis muss existieren.

## Guards und Verifikation

| Guard | Schweregrad | Ausgelöst durch |
| --- | --- | --- |
| `plc_deletes_technology_object` | info | `delete_technology_object` |
| `plc_deletes_technology_object_group_contents` | info | `delete_technology_object_group` einer nicht leeren Gruppe (`removes.blocks` des Effekts listet die Objekte, `removes.groups` die Gruppen) |
| `plc_technology_object_import_replaces` | info | `import_technology_object` mit `overwrite:true`, jedes `update_technology_object_content` (von `ImportOptions.Override` wird erwartet, dass es ein gleichnamiges Objekt ersetzt; nicht live verifiziert) |
| `plc_overwrites_file` | info | `export_technology_object` mit `overwrite:true` über eine vorhandene Datei |
| `plc_technology_parameter_not_hardware_connection` | info | `set_technology_object_parameter` eines `Actor.*`-Parameters |
| `plc_replaces_hardware_connection` | info | `connect_technology_object_hardware` auf eine Schnittstelle, die sich als verbunden liest (ident: `ConnectedIdentDevice` liest ein Gerät); dass `Connect` die Verbindung ersetzt, ist eine Erwartung (nicht dokumentiert), das Rücklesen prüft das angeforderte Ziel |
| `plc_state_unverifiable` (vorhanden) | **block** | ein unvollständiges TO-Inventar oder ein fehlgeschlagener Planungs-Lesevorgang des Parameters oder der Verbindung (außer nicht gefunden oder mehrdeutig): der einzige TO-Fall, der blockiert, weil die Planung nicht sehen kann, was das Item ändern würde |

Die Planung liest nur öffentliche `plc_read`-Operationen: `list_technology_objects` einmal je PLC und Aufruf, den
aktuellen Parameter (sein Wert wird zum `current` des Effekts; ein Wert, der sich nicht in den aktuellen Typ
konvertieren lässt, wird abgelehnt, bevor irgendetwas läuft, ebenso ein nicht lesbarer aktueller Wert) und die
aktuelle Schnittstelle (fehlende oder mehrdeutige Schnittstellen werden abgelehnt, bevor irgendetwas läuft). Nach
dem Aufruf liest der gemeinsame Verifizierer über `plc_read` erneut:

| Prüfung | Operationen | Verglichen |
| --- | --- | --- |
| `technologyObjectParameter` | Parameter setzen | `valueKind value`, vom Schreibvorgang gemeldet, gegen ein frisches `get_technology_object_parameters` |
| `technologyObjectConnection` | Connect, Disconnect | `connected` plus jede erwartete Zieleigenschaft (`InputAddress=2048`, `InputOutputModule=Drive_1/Module_1`, ...; eine nicht lesbare zählt nur, wenn `previousInterface` nicht verbunden war), oder `not connected` |
| `technologyObject` | Create, Rename, Delete, Import | Vorhandensein und Gruppe des Objekts in `list_technology_objects` (Create: auch die DB-Nummer, die das Create gemeldet hat); ein Import, der kein Objekt nannte, ist nicht verifizierbar (`success:false`) |
| `technologyObjectGroup` | Gruppe anlegen, umbenennen, löschen | das Vorhandensein der Gruppe |
| `contentHash` | Inhalt aktualisieren | der Hash eines erneuten Exports (als nächster `expectedContentHash` verwendbar) |
| `trusted` | Export | der Worker hat die Datei erneut gelesen und ihren SHA-256 mit dem des Exports verglichen |

Spätere Items auf demselben Objekt, Member oder derselben Gruppe lösen frühere ab (`superseded`). Namen von
Technologieobjekten sind je PLC eindeutig (ein TO ist ein DB), daher löst ein Import ein früheres Löschen oder
Umbenennen jedes Namens ab, den er in `importedObjects` meldet, in jedem Ordner, und ein späteres Item auf einem
importierten Namen löst den Import ab (Defekt K19, live V21 2026-11-02, MCP_Livetest_01: Export, Löschen und Import
von `Achse_Test` nach `/Achsen` in einem Aufruf; das Rücklesen des Löschens erwartete `Achse_Test absent`, fand
`/Achsen/Achse_Test exists` und ließ den Aufruf fehlschlagen, obwohl jeder Schritt funktioniert hatte; mit demselben
Ordner war es bereits `superseded`). Nach der Korrektur nicht live getestet.

Ein Export eines Technologieobjekts (`export_technology_object`, `get_technology_object_content`, der
Prüf-Export des Inhalts-Updates), den TIA Portal verweigert, während das Objekt `IsConsistent=false` liest (live
2026-11-02: nach `set_technology_object_parameter`, einem Umbenennen oder einem Import), trägt den Hinweis,
`compile_check` mit dem `plcName` der PLC auszuführen und es erneut zu versuchen (Defekt K20); es wird nichts
automatisch übersetzt. Nicht live getestet.

Der Host decodiert jede Worker-Payload über den Worker-Payload-Reader und lehnt (`protocol_error`, nie
zurückgegeben) eine Payload ab, die der Anfrage oder ihren Grenzen widerspricht: mehr als 100 Services oder 200
Enum-Member, Enum-Felder an einem Nicht-Enum-Wert, eine Kürzungsmarkierung an einer kurzen Liste, eine
Verbindungseigenschaft, die für ihre Schnittstelle nicht dokumentiert ist, eine Parameterseite, deren
`offset`/`limit`/`parameterName`/`prefix`/`nextOffset` nicht der Anfrage entsprechen, ein Exportergebnis, dessen
`filePath` nicht der angeforderte Pfad ist oder dessen `fileSha256` nicht aus 64 hexadezimalen Kleinbuchstaben
besteht, ein Connect-Ergebnis für eine andere Schnittstelle oder Position oder dessen `previousInterface` ein
anderer Endpunkt ist, mehr als 64 Schnittstellen oder eine wiederholte Schnittstelle/Position, eine Schnittstelle,
die nicht jede dokumentierte Eigenschaft genau einmal meldet (gelesen oder in `unreadable` gelistet), ein Modulpfad
tiefer als 32 Namen, ein Textwert länger als 4.000 Zeichen plus die Kürzungsmarkierung, ein Inventar mit mehr als
5.000 Objekten plus Gruppen oder einer wiederholten Gruppe, mehr als 5.000 oder wiederholte `importedObjects`, ein
Update, das nicht genau ein Objekt nennt, oder eine Parameterseite mit wiederholten Namen.

## Herkunft jedes verwendeten Siemens-Members

Quellen (schreibgeschützte Referenzdaten, nie ausgeführt oder kopiert): **(T)** `totally-integrated-claude`
`skills/tia-plc-operations/references/technological-objects.md` (gegen V21-XML geprüft: Namen und Parametertypen,
keine Rückgabe- oder Eigenschaftstypen); **(S)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/technology-objects/SKILL.md`; **(E)** `.../engineering-objects/SKILL.md`; **(W)**
`.../object-tree-walking/SKILL.md`; **(B)** die V21-Typnamen-Baselines `api-baselines/v21/step7.json` und
`startdrive.json`. Der Siemens-Skill `parameters` behandelt SINAMICS-Antriebsparameter (`DriveObject.Parameters`,
Startdrive), keine TO-Parameter, und wurde nicht verwendet.

Status: **existing** (ein unveränderter vorhandener Stub, bereits von anderen Funktionen genutzt); **runtime** (auf
dem Live-Objekt über den dokumentierten Namen und die Argumenttypen durch `LibraryRuntimeBinding` gebunden: genau
ein anwendbares öffentliches Member, sonst `unsupported_capability` vor jedem Aufruf); **runtime lookup** (eine
Eigenschaft, die über ihren dokumentierten Namen gelesen wird; informative Member fallen auf `unreadable` zurück);
**generic** (die dokumentierte `IEngineeringObject`-Attribut-API). Es wurde kein Stub-Member ergänzt: Die Referenz
dokumentiert für kein TO-Member einen Rückgabe- oder Eigenschaftstyp, und die Typnamen des Skills
(`TechnologicalObject`, `TechnologicalObjectComposition`) sind keine V21-Typen (B), daher ist keine TO-spezifische
Signatur gut genug bekannt, um dagegen zu kompilieren. `ref/*.dll` ist unverändert.

| Siemens-Member | Assembly (B) | Quelle | Status |
| --- | --- | --- | --- |
| `PlcSoftware.TechnologicalObjectGroup` → `TechnologicalInstanceDBGroup` | Step7 | S, T | existing |
| `TechnologicalInstanceDBGroup.Name`, `.Groups`, `.TechnologicalObjects`; Enumeration von `TechnologicalInstanceDBComposition` und `TechnologicalInstanceDBUserGroupComposition` | Step7 | T, S | existing |
| `TechnologicalInstanceDB`, `TechnologicalInstanceDBUserGroup` (Typprüfungen) und `TechnologicalInstanceDB.Name` (der vorhandene Stub deklariert `TechnologicalInstanceDB : InstanceDB`, daher bindet `Name` an `PlcBlock.Name`) | Step7 | T ("Name of the Block"), B | existing |
| `IEngineeringInstance.Parent` (das übergeordnete Element einer Benutzergruppe; die Device-Item-Kette von `modulePath`) | Base | T (`Parent`), E (`DeviceItem.Parent`) | existing |
| `TechnologicalInstanceDB.OfSystemLibElement`, `.OfSystemLibVersion` | Step7 | T | runtime lookup (informativ) |
| `Number` am TO (geerbtes `PlcBlock.Number` über die vorhandene Stub-Vererbung) | Step7 | vorhandener Stub | runtime lookup (informativ) |
| `TechnologicalInstanceDB.Parameters`; Enumeration; `TechnologicalParameterComposition.Find(String)` | Step7 | T, S | runtime |
| `TechnologicalParameter.Name`, `.Value` (Get und der öffentliche Setter) | Step7 | T, S | runtime |
| `TechnologicalInstanceDBComposition.Create(String, String, Version)` | Step7 | T, S | runtime |
| `TechnologicalInstanceDBComposition.Import(FileInfo, ImportOptions)` (Rückgabe als Liste von `TechnologicalInstanceDB` gelesen, wenn sie eine ist) | Step7 | T | runtime |
| `TechnologicalInstanceDB.Export(FileInfo, ExportOptions)` | Step7 | T | runtime |
| `Delete()` am TO (in T nicht unter `TechnologicalInstanceDB` gelistet; geerbtes `PlcBlock.Delete` gemäß der vorhandenen Stub-Vererbung) | Step7 | vorhandener Stub | runtime, parameterlos |
| `TechnologicalInstanceDBUserGroupComposition.Create(String)`; `TechnologicalInstanceDBUserGroup.Delete()` | Step7 | T, S (`Groups.Create`) | runtime |
| `IEngineeringObject.GetAttributeInfos()`, `GetAttribute("Name")`, `SetAttribute("Name", value)` (Umbenennungen, über das vorhandene `LibraryReflection.Rename`) | Base | E | generic |
| `IEngineeringServiceProvider.GetServiceInfos()` → `IList<EngineeringServiceInfo>`, `EngineeringServiceInfo.Type` (`System.Type`) | Base (B listet beide Typen) | E (Signatur, explizite Interface-Implementierung, Aufruf vor `GetService`), W | Live-Prüfung zur Laufzeit: vom Interface-Typ selbst genommen (Reflection auf den Proxy-Typ liefert null, E), und der deklarierende Typ, die parameterlose Signatur, die Rückgabe `IList<Siemens.Engineering.EngineeringServiceInfo>`, ein `System.Type`-typisiertes `Type`, eine Liste ungleich null und der Typ jedes Elements müssen wie dokumentiert sein, sonst `unsupported_capability`. Nicht in den Stubs deklariert: Die Quellen sagen nicht, ob `EngineeringServiceInfo` eine Klasse oder ein Struct ist, was ein kompilierter Aufruf in der IL festlegen würde, und das Ergänzen des Members im Stub-Interface würde auch jede Stub-Klasse ändern, die es implementiert |
| `System.IServiceProvider.GetService(Type)` | BCL | E, W | kompiliert (BCL) |
| `Motion.AxisHardwareConnectionProvider` (ein Service, S) mit `ActorInterface`, `SensorInterface`, `TorqueInterface` | Step7 | S, T | runtime (exakter Typname unter den gelisteten Services) |
| `Motion.EncoderHardwareConnectionProvider.SensorInterface` | Step7 | T | runtime |
| `Motion.AxisEncoderHardwareConnectionInterface`: `Channel`, `ConnectOption`, `InputAddress`, `InputModule`, `InputOutputModule`, `IsConnected`, `OutputAddress`, `OutputModule`, `OutputTag`, `PathToDBMember`, `SensorIndexInActorTelegram`; `Connect(Channel)`, `(DeviceItem)`, `(PlcTag)`, `(String)`, `(DeviceItem, DeviceItem)`, `(DeviceItem, DeviceItem, ConnectOption)`, `(Int32, Int32, ConnectOption)`; `Disconnect()` | Step7 | T, S (`Connect(Int32, Int32, ConnectOption.Default)`, `IsConnected`) | runtime |
| `Motion.AxisEncoderHardwareConnectionInterfaceComposition`-Enumeration | Step7 | T | runtime (nicht generisches `IEnumerable`) |
| `Motion.TorqueHardwareConnectionInterface`: seine acht Eigenschaften, `Connect(DeviceItem)`, `(String)`, `(DeviceItem, DeviceItem)`, `(DeviceItem, DeviceItem, ConnectOption)`, `(Int32, Int32, ConnectOption)`; `Disconnect()` | Step7 | T | runtime |
| `Motion.MeasuringInputHardwareConnectionProvider`: `Channel`, `ChannelIndex`, `InputAddress`, `InputModule`, `IsConnected`; `Connect(Channel)`, `(Int32)`, `(DeviceItem, Int32)`; `Disconnect()` | Step7 | T | runtime |
| `Motion.OutputCamHardwareConnectionProvider`: `Channel`, `IsConnected`, `OutputAddress`, `OutputTag`; `Connect(Channel)`, `(PlcTag)`, `(Int32)`; `Disconnect()` | Step7 | T | runtime |
| `Ident.IdentTechnologicalObjectProvider.ConnectedIdentDevice`, `.Connect(DeviceItem)` | Step7 | T | runtime |
| `Motion.ConnectOption` (Member `Default`; andere Member über den exakten Namen auf der installierten Enum) | Step7 | T (Typ), S (`Default`) | runtime enum |
| `ExportOptions.None`, `ImportOptions.None`/`.Override`; `Device`/`DeviceItem.Name`, `DeviceItems`, `DeviceItem.Channels`, `Channel.Number`/`IoType`/`Type`; `PlcTag`, `PlcTagTable`, Variablentabellengruppen, `PlcTagComposition.Find` | Base, Step7 | vorhandene Stubs | existing |

## Ausgelassen, und warum

| Operation | Grund |
| --- | --- |
| Lesen einer Familien/Versions-Matrix | Keine Quelle liefert eine: Der Siemens-Skill stellt fest, dass keine bekannte Openness-API gültige Typ/Versions-Kombinationen aufzählt und dass Versionen installationsspezifisch sind; sein einziger V21-Datenpunkt (`TO_PositioningAxis` → `10.0`) wurde am 2026-11-02 live für eine CPU 1516-3 PN/DP FW V2.9 widerlegt (`10.0` abgelehnt, `6.0` akzeptiert), daher sagt die Tool-Beschreibung, dass die Version von der CPU-Firmware abhängt. Die Kaskaden-Probe des Skills (Versionen durchprobieren, bis `Create` gelingt) ist nicht implementiert: Sie legt im Projekt des Benutzers durch Ausprobieren Objekte an oder lässt sie fehlschlagen und rät Versionen. |
| Ein TO übersetzen | Kein Übersetzungs-Member auf TO-Ebene steht in den Quellen; übersetze die PLC-Software mit `compile_check`. |
| Ein TO oder eine TO-Benutzergruppe aus einer Kopiervorlage platzieren (`CreateFrom(MasterCopy[, MasterCopyMode])` auf beiden Kompositionen) | Die Wurzelkomposition ist bereits durch `library_write instantiate_master_copy` (Ziel `technologyObjects`) abgedeckt; das Platzieren in eine TO-Untergruppe und Kopiervorlagen von TO-Gruppen gehören zur Bibliotheksdomäne und sind dort ein Folgepunkt. |
| `Export(FileInfo, ExportOptions, DocumentInfoOptions)` | Die `DocumentInfoOptions`-Member stehen nicht in den Quellen; es wird der Export mit zwei Argumenten verwendet. |
| Startdrive-Verbindungsschnittstellen (`AxisHardwareConnectionSDRProvider`, `EncoderHardwareConnectionSDRProvider`, `*SDRInterface.Connect(Telegram[, ConnectOption])`) | Sie liegen in `Siemens.Engineering.Startdrive.dll`, die der Worker nicht referenziert, und nehmen `Telegram`-Objekte von Startdrive-Antriebsobjekten (Antriebe sind eine eigene, noch nicht entworfene Domäne). Adress-, Modul- und DB-Member-Verbindungen decken den dokumentierten Step7-Pfad ab. |
| Disconnect der Ident-Schnittstelle; Entfernen einer `PlcTag` aus einem Parameter | Nicht dokumentiert. |
| Verschieben eines TO zwischen Gruppen | Kein dokumentiertes Member; Export, Löschen und Import in die andere Gruppe sind getrennte Items. |
| Kurvenscheibendaten (`CamDataSupport.LoadCamData`/`SaveCamData`/binär/Punktliste) und Interpreterprogramme (`InterpreterProgramSupport.LoadSource`/`SaveSource`) | Die Methoden sind dokumentiert, aber wie diese Objekte von einem TO bezogen werden, ist nicht angegeben, und die Member von `CamDataFormat`/`CamDataFormatSeparator` sind nicht gelistet. Sie brauchen dieselbe Service-Discovery plus Live-Nachweis; zurückgestellt. |
| Leitwertkopplungen (`SynchronousAxisMasterValues`, `ConveyorTrackingLeadingValues`, `SuperimposingAxes` mit `SetPointCoupling`/`ActualValueCoupling`/`DelayedCoupling`, `TechnologicalInstanceDBAssociation.Add/Remove`), Interpreter-Mappings (`InterpreterMappings`, `TOMapping`, `DBMemberMapping`) und `OutputCamMeasuringInputContainer` | Wie diese Objekte erreicht werden und die Eigenschaftstypen der Kopplungen sind in den Quellen nicht angegeben; zurückgestellt, bis ein Live-Discovery-Nachweis existiert. |
| `ShowInEditor` | Eine UI-Aktion; in den Quellen für TOs nicht dokumentiert. |
| TOs in Software Units | Von TIA Portal nicht unterstützt (Siemens-Skill). |
| Parameter-Metadaten (Schreibbarkeit, deklarierter Typ, Grenzen, Einheiten) | Kein dokumentiertes Member meldet sie; der Laufzeittyp des aktuellen Werts ist der einzige Typnachweis. |
| SINAMICS-Antriebsparameter (Siemens-Skill `parameters`) | Startdrive-Antriebsobjekte, keine Technologieobjekte. |

## Live-Abnahmeprüfungen

1. `list_technology_objects` auf einem Projekt mit Achsen, Gebern, Messeingängen, Nocken und verschachtelten
   Gruppen: Namen, Gruppen, `number`, und was `systemLibElement`/`systemLibVersion` im Vergleich zu dem Typ und der
   Version melden, mit denen das Objekt angelegt wurde.
2. `create_technology_object` `TO_PositioningAxis` mit der Version, die die CPU-Firmware akzeptiert (live
   2026-11-02, FW V2.9: `6.0`), an der Wurzel und in einer Gruppe; eine nicht verfügbare Version schlägt mit
   `engineering_rejected` und dem Siemens-Text fehl; der Rückgabewert von `Create` (nur über seinen Effekt
   protokolliert).
3. `get_technology_object_parameters`: Dauer des ersten und späterer Lesevorgänge einer großen Achse, die
   Laufzeittypen gängiger Parameter, nicht lesbare Parameter, `Find` mit Namen aus der Auflistung.
4. `set_technology_object_parameter` für einen Bool-, einen Real-, einen Integer- und einen Enum-Parameter,
   `Actor.*`-Schreibvorgänge und einen `PlcTag`-Parameter (`_PositionLimits_HW.MinSwitchAddress` aus dem
   Siemens-Beispiel); eine Abweichung beim Rücklesen.
5. `get_technology_object_hardware_connection`: dass `GetServiceInfos()` `AxisHardwareConnectionProvider` (und die
   anderen Provider) für die passenden TO-Typen listet; die Form von `SensorInterface` (Komposition oder einzeln)
   für Achsen und Geber; Eigenschaftstypen.
6. `connect_technology_object_hardware` actor `address` mit Telegramm `StartAddress × 8` (Siemens-Beispiel) und
   `ConnectOption Default`. Jede Rücklese-Regel der Nachweistabelle ist live unbewiesen und muss geprüft werden: ob
   `InputAddress`/`OutputAddress` die übergebenen Werte zurücklesen, ob `ConnectOption` die übergebene Option
   zurückliest (einschließlich `Default`), ob `InputOutputModule`/`InputModule`/`OutputModule`/`ChannelIndex`/
   `Channel`/`OutputTag` Objekte zurücklesen, die den übergebenen gleich (`Equals`) sind, für `module`, `modules`,
   `channel` und `tag`, ob `PathToDBMember` den übergebenen Text für `dbMember` zurückliest; Messeingang
   `Connect(DeviceItem, Int32)`; Nockenausgang; ident `ConnectedIdentDevice`; `Disconnect()`.
7. Roundtrip `export_technology_object` / `import_technology_object`, `overwrite`, der Laufzeittyp des
   Rückgabewerts von `Import`; `update_technology_object_content` (ob der Export das einzelne
   `AttributeList/Name`-Objektelement hat, das die Dokumentprüfung als Vorlage verwendet).
8. `rename_technology_object` und `rename_technology_object_group`: ob `GetAttributeInfos()` `Name` als schreibbar
   listet; `delete_technology_object` bindet `Delete()`; Löschen einer nicht leeren Gruppe.
9. Dieselben Lese- und Schreibvorgänge auf einer lokalen Multiuser-Session.

## Offene Risiken

- Der kompilierte TO-Zugriff stützt sich auf `TechnologicalInstanceDB : InstanceDB` des vorhandenen Stubs (auch
  von VCI verwendet); wenn die installierte Hierarchie abwiche, würde der erste TO-Zugriff zur Laufzeit
  fehlschlagen.
- Die Provider-Discovery nimmt an, dass die Provider von `GetServiceInfos()` gelistet werden; wenn nicht, melden
  die Hardware-Operationen keine Schnittstelle (`target_kind_unsupported`), statt einen anderen Zugriffspfad zu
  raten. Wenn das installierte `GetServiceInfos()` von der dokumentierten Signatur abweicht, schlägt jeder
  Hardware-Lese- und -Schreibvorgang mit `unsupported_capability` fehl.
- Die Connect-Verifikation verlangt, dass die dokumentierte Zieleigenschaft (und die übergebene `ConnectOption`)
  die Anfrage exakt zurückliest; wenn V21 zum Beispiel eine andere rohe Bitadresse meldet, einen anderen Proxy, der
  nicht `Equals` zum übergebenen Device Item ist, oder eine andere Option, schlagen diese Connects mit
  `postcondition_failed` fehl (nachdem die Änderung vorgenommen wurde), bis die Regel anhand von Live-Nachweisen
  angepasst ist.
- Parametern, deren aktueller Wert null ist, kann nur eine `PlcTag` zugewiesen werden; andere Null-Parameter können
  nicht geschrieben werden, weil ihr Typ unbekannt ist.
- Die Deutung von `OfSystemLibElement`/`OfSystemLibVersion` als TO-Typ und -Version ist nicht verifiziert.
- Ein Import, dessen Rückgabewert keine Objektliste ist, identifiziert nur neue Objekte; ein ersetztes Objekt
  (`overwrite:true`) wird dann nicht genannt (Warnung).
