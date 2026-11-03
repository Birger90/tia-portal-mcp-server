# Klassisches WinCC und WinCC-Unified-Erweiterungen (hmi_read / hmi_write)

**Sprache:** [English](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.md) · Deutsch

Diese Referenz deckt Issue #28 (klassisches WinCC: Comfort/Advanced Panels) und Issue #29 (WinCC Unified: Ereignisse,
Dynamisierungen, Formen und Widgets, Audit, Anlagenobjekte) ab. Alles hier ist **nicht live getestet**. Beleg für jedes
Siemens-Member ist die V21-IntelliSense-Beschreibung (`Siemens.Engineering.WinCC.xml`, `Siemens.Engineering.WinCCUnified.xml`,
`PublicAPI\V21\net48`, 29.10.2025); kein Member außerhalb davon wird verwendet. Tools, Zugriffsmodi und Zählungen (11/30/30)
bleiben gleich: Es sind weitere `hmi_read`- und `hmi_write`-Operationen mit den Regeln der
[HMI-Referenz](HMI_OPERATIONS_SUMMARY.de.md) (50 Einträge, Reihenfolge des Aufrufers, Stopp beim ersten Fehler, kein
Rollback, `dryRun:true` als Vorschau, Schreibvertrag `2.0`, Lesevertrag `1.1`).

## Klassisches WinCC (Issue #28)

Der Worker hat weiterhin keine Compile-Referenz auf `Siemens.Engineering.WinCC.dll`. Ein klassisches HMI wird am Typnamen
`Siemens.Engineering.Hmi.HmiTarget` erkannt, und jedes klassische Member wird zur Laufzeit über exakten Namen und
Argumenttypen am Live-Objekt gebunden (Worker `Openness/Hmi/HmiClassicObjects.cs`, Arten in Contracts
`Hmi/HmiClassicInfos.cs`). Bietet die Installation ein Member nicht an, scheitert der Eintrag mit `api_member_missing`, bevor
etwas geschrieben oder geändert wird. Die Unified-Operationen lehnen ein klassisches HMI weiter mit `target_kind_unsupported`
ab; ihre Meldung nennt jetzt die klassischen Operationen. Die klassischen Operationen lehnen ein Unified-HMI ebenso ab. Ohne
`hmiName` nehmen sie das einzige klassische HMI des Projekts.

### Arten (`classicKind`)

| classicKind | Ort (V21) | Ordner | Löschen | Export | Import nach |
| --- | --- | --- | --- | --- | --- |
| `tagTables` | `HmiTarget.TagFolder` → `TagTables` | ja | ja | ja | Ordner (`groupPath`) |
| `tags` | `TagTable.Tags` | – (Tabellen) | ja | ja | Variablentabelle (`tableName`, Pflicht) |
| `screens` | `HmiTarget.ScreenFolder` → `Screens` | ja | ja | ja | Ordner |
| `screenTemplates` | `ScreenTemplateFolder` → `ScreenTemplates` | ja | ja | ja | Ordner |
| `screenPopups` | `ScreenPopupFolder` → `ScreenPopups` | ja | ja | ja | Ordner |
| `screenSlideins` | `ScreenSlideinFolder` → `ScreenSlideins` (benannt nach `SlideinType`) | – | – (keins dokumentiert) | ja | Systemordner |
| `vbScripts` | `VBScriptFolder` → `VBScripts` | ja | ja | ja | Ordner |
| `textLists` | `HmiTarget.TextLists` | – | ja | ja | Ziel |
| `graphicLists` | `HmiTarget.GraphicLists` | – | ja | **nie** (V21-Fallstrick 3) | Ziel |
| `connections` | `HmiTarget.Connections` | – | ja | ja | Ziel |
| `cycles` | `HmiTarget.Cycles` | – | ja | ja | Ziel |
| `screenGlobalElements` | `HmiTarget.ScreenGlobalElements` (ein Objekt) | – | – | ja | `HmiTarget.ImportScreenGlobalElements` |
| `screenOverview` | `HmiTarget.ScreenOverview` (ein Objekt) | – | – | ja | `HmiTarget.ImportScreenOverview` |

`groupPath` ist der mit `/` getrennte Pfad der Benutzerordner unter dem Systemordner der Art (Komposition `Folders`, Namen
ohne Groß-/Kleinschreibung); bei `tags` grenzt er die Variablentabellen auf diesen Ordner ein.

### Operationen

| Tool | Operation | Pflicht | Optional | Siemens-Member |
| --- | --- | --- | --- | --- |
| `hmi_read` | `list_classic_objects` | `classicKind` | `hmiName`, `groupPath`, `tableName` (tags) | `Name` (oder `SlideinType`), `IsSystemObject`, `Folders` |
| `hmi_write` | `export_classic_objects` | `classicKind`, `directoryPath` | `groupPath`, `tableName`, `objectNames` (1–200), `overwrite` | `Export(FileInfo, ExportOptions.None)` |
| `hmi_write` | `import_classic_objects` | `classicKind`, `filePath` | `groupPath`, `tableName` (Pflicht bei tags), `overwrite` | Komposition `Import(FileInfo, ImportOptions)`, `ImportScreenGlobalElements`/`ImportScreenOverview` |
| `hmi_write` | `delete_classic_object` | `classicKind`, `objectName` | `groupPath`, `tableName` | `Delete()` |
| `hmi_write` | `create_classic_folder` | `classicKind`, `groupPath` (Pfad des neuen Ordners) | – | `…UserFolderComposition.Create(String)` |
| `hmi_write` | `delete_classic_folder` | `classicKind`, `groupPath` | – | `…UserFolder.Delete()` |

- **list_classic_objects** listet jedes Objekt der Art im Bereich mit `name`, `folderPath`, `tableName`, `isSystemObject`
  (Variablentabellen, Zyklen; sonst null) und `typeName`, dazu die Pfade der Benutzerordner. Es blättert nicht: Auf einem
  großen Panel `tags` mit `tableName` eingrenzen (eine Antwort über dem Budget wird mit diesem Hinweis ausgelassen).
- **export_classic_objects** schreibt je Objekt eine `<name>.xml` (Einzelobjekte: `<classicKind>.xml`) in ein vorhandenes
  Verzeichnis: Jedes `Export`-Member wird vor dem ersten Export gebunden, jeder Export geht in eine private temporäre Datei,
  wird neben dem Ziel bereitgestellt, an seinen Platz verschoben und gehasht (`appliedProperties` nennt Pfad, Bytes und
  SHA-256). Vorhandene Dateien brauchen `overwrite:true`; zwei Objekte, die dieselbe Datei schreiben würden, werden vor dem
  ersten Schreiben abgelehnt.
- **import_classic_objects** importiert eine private Kopie der Datei mit `ImportOptions.None` (`Override` mit
  `overwrite:true`, Info-Guard `hmi_classic_import_overrides`). Ohne overwrite muss ein neues Objekt in der Komposition
  erscheinen, sonst scheitert der Eintrag mit `postcondition_failed`; die neuen Namen werden aufgeführt. Ein Import eines
  Einzelobjekts wird nicht zurückgelesen (Openness bietet kein Lesen seines Inhalts) und sagt das.
- **delete_classic_object** und die Ordner-Schreibvorgänge werden zurückgelesen: Objekt oder Ordner muss weg sein (bzw.
  nach dem Anlegen vorhanden), sonst `postcondition_failed`. Löschvorgänge lösen den Info-Guard `hmi_classic_deletes` aus.
- Dateipfade folgen den gemeinsamen Regeln (absolute Laufwerks- oder UNC-Freigabepfade; Gerätepfade, reservierte Namen,
  `.`/`..` sowie Segmente mit Punkt oder Leerzeichen am Ende werden abgelehnt).
- Verifikation: Erledigte Einträge sind `trusted` oder `superseded` (Schlüssel je Art und Bereich,
  `ClassicObjects`/`ClassicObject`/`ClassicFolder`); ein Eintrag hängt vom letzten früheren Eintrag derselben klassischen Art ab.

### Angewandte V21-Fallstricke

- **Fallstrick 3** (berichtet, auf V21 nicht verifiziert): Das Exportieren einer klassischen Grafikliste soll TIA Portal V21
  abstürzen lassen. `export_classic_objects` mit `graphicLists` lehnt der Katalog ab und im Worker, bevor `Export` gebunden
  wird: "skipped: reported to crash TIA Portal V21 …; nothing was written." `HmiPitfallSourceTests` sichert diese
  Reihenfolge. Auflisten, Importieren und Löschen von Grafiklisten sind erlaubt.
- **Fallstrick 4** (berichtet, auf V21 nicht verifiziert): Ein klassischer Bildimport mit doppelter Bildnummer `<Number>`
  soll TIA Portal beenden. Vor `Import` liest der Worker jedes `Hmi.Screen.Screen` (`AttributeList/Name`, `Number`) der
  Datei und die Nummer jedes vorhandenen Bildes (generisches `GetAttribute("Number")`; lehnt TIA Portal das ab, aus einem
  privaten Export dieses Bildes) und lehnt den Eintrag ab (`validation_error`, nichts importiert), wenn sich eine Nummer in
  der Datei wiederholt oder ein anderes vorhandenes Bild sie nutzt (ein gleichnamiges Bild ist das, das ein überschreibender
  Import ersetzt). Ein Bild, dessen Nummer nicht lesbar ist, wird in einer Warnung genannt; der Import läuft dann. Das ist
  die einzige klassische Ablehnung, die strenger als TIA Portal ist, aus demselben Grund wie die Ablehnung des VCI-Exports
  `SimaticSD`: Der berichtete Fehler ist ein beendetes TIA Portal.

### Weggelassen (Issue #28)

| Punkt | Warum |
| --- | --- |
| `CreateFrom(MasterCopy)` / `CreateFrom(…LibraryTypeVersion)` von Variablentabellen, Variablen, Bildern, Vorlagen, Pop-ups, VB-Skripten | Instanziieren aus der Bibliothek gehört zu `library_write`; dessen Ziele umfassen klassische HMI-Kompositionen noch nicht. Hier nicht gebaut. |
| Mehrsprachige Grafiken (`GraphicsProvider.Graphics`, `MultiLingualGraphicComposition`) | V21 dokumentiert den Provider, aber nicht, welches Objekt ihn als Dienst anbietet; nichts wird geraten. |
| `Hmi.Alarm`, `Hmi.Recipe`, `Hmi.Logging`, `Hmi.Report`, `Hmi.Scheduler`, `Hmi.Faceplate` | V21 dokumentiert in diesen Namensräumen nur Enums (und Namen von Bibliothekstypen), keine Komposition, keinen Export oder Import. |
| Inhalt klassischer Objekte (Eigenschaften einer Variable, Bildobjekte, Verbindungsparameter) | V21 dokumentiert keine typisierten Member; der Inhalt läuft über den SimaticML-Export/-Import oben. |
| Löschen von Slide-in-Bildern, globalen Elementen, Bildübersicht | Kein `Delete()` dokumentiert. |
| Übersetzen klassischer HMIs | `compile_check` bleibt nur für PLCs. |

Noch offene Live-Prüfungen: jede Zeile oben an einem Projekt mit Comfort Panel (Issue #28 verlangt eines); ob
`GetAttribute("Number")` die Nummer eines klassischen Bildes liest; die Form von `ScreenTemplateFolder` (V21 nennt es eine
"composition of screen template folders").

## WinCC Unified: Ereignisse und Dynamisierungen (Issue #29)

Worker `Openness/Hmi/HmiScreenEvents.cs`, Methodennamen `HmiScreenEventMethods` (Contracts `Hmi/HmiScreenEventInfos.cs`),
Host-Partials `TiaMcpServer/Hmi/Extended/*.Events.cs`. Die Member sind in V21 dokumentiert, aber nicht in den
Compile-Stubs, daher werden sie zur Laufzeit am Live-Objekt gebunden; fehlt eines, scheitert der Eintrag mit
`api_member_missing`, bevor sich etwas ändert. Ein Ziel ist ein Bild (`screenName`, bei Schreibvorgängen optional
`groupPath`) oder eines seiner Objekte (`itemName`).

| Tool | Operation | Pflicht | Optional | Siemens-Member |
| --- | --- | --- | --- | --- |
| `hmi_read` | `list_screen_item_events` | `screenName` | `hmiName`, `itemName` | `EventHandlers`, `PropertyEventHandlers`, Handler `EventType`/`PropertyName`/`Script`, `IHmiScript.ScriptCode`/`GlobalDefinitionAreaScriptCode`/`Async` |
| `hmi_read` | `list_screen_item_dynamizations` | `screenName` | `hmiName`, `itemName` | `Dynamizations`, `DynamizationBase.PropertyName`/`DynamizationType`, die dokumentierten Eigenschaften je Typ, `ScriptDynamization.Trigger` (`Type`, `Tags`, `CustomDuration`) |
| `hmi_write` | `set_event_script` | `screenName`, `eventType`, `scriptCode` | `groupPath`, `itemName`, `propertyName`, `globalDefinitionAreaScriptCode`, `async` | `…EventHandlerComposition.Find/Create(<Typ>EventType)`; mit `propertyName`: `PropertyEventHandlerComposition.Find/Create(String, PropertyEventType)`; Eigenschaften von `Script` |
| `hmi_write` | `delete_event_script` | `screenName`, `eventType` | `groupPath`, `itemName`, `propertyName` | `Find`, Handler `Delete()` |
| `hmi_write` | `set_dynamization_script` | `screenName`, `propertyName`, `scriptCode` | `groupPath`, `itemName`, `globalDefinitionAreaScriptCode`, `async`, `triggerType`, `triggerTags` (1–50), `customDuration` | `DynamizationBaseComposition.Create<ScriptDynamization>(String)`, `ScriptCode`, `Trigger.Type/Tags/CustomDuration` |
| `hmi_write` | `set_dynamization_flashing` | `screenName`, `propertyName` | `groupPath`, `itemName`, `color`, `alternateColor`, `flashingCondition`, `flashingRate` | `Create<FlashingDynamization>(String)`, `Color`, `AlternateColor`, `FlashingCondition`, `FlashingRate` |
| `hmi_write` | `delete_dynamization` | `screenName`, `propertyName` | `groupPath`, `itemName` | `DynamizationBase.Delete()` |

- **Ereignistypen.** Jeder Bildobjekttyp hat eine eigene Ereignis-Enum (`HmiButtonEventType`, `HmiIOFieldEventType`, …
  und `HmiScreenEventType` für das Bild); der Worker bindet das einzige `Find`/`Create` der `EventHandlers` des Ziels, das
  eine Enum nimmt, und liest `eventType` als exakten Membernamen. Ein anderer Name scheitert mit `validation_error` und
  nennt die Member. Ein Eigenschaftsereignis (`propertyName` gesetzt) nimmt `Change` oder `QualityCodeChange`
  (`PropertyEventType`).
- **Skripte.** `set_event_script` findet den Handler oder legt ihn an und schreibt dann `ScriptCode` (sowie
  `GlobalDefinitionAreaScriptCode`, `Async`, wenn angegeben) über die dokumentierten `IHmiScript`-Eigenschaften, jede
  zurückgelesen. `scriptCode` wird unverändert gesendet (höchstens 60.000 Zeichen); die Syntax wird nicht geprüft
  (`IHmiScript.SyntaxCheck` ist dokumentiert, wird aber nicht aufgerufen; das HMI in TIA Portal übersetzen).
- **Dynamisierungen.** Eine Eigenschaft hat eine Dynamisierung. `set_dynamization_script`/`set_dynamization_flashing`
  ändern die vorhandene derselben Art oder legen eine an; eine andere Art an der Eigenschaft wird mit
  `target_kind_unsupported` abgelehnt (zuerst löschen), so wie `set_dynamization_tag` schon eine Skript-Dynamisierung
  ablehnt. Trigger- und Blinkwerte werden genau in den Laufzeittyp des aktuellen Werts umgewandelt (Enum-Membernamen wie
  `T1s`, `CustomCycle`, `Tags`, `Always`, `Fast`; Farben `#RRGGBB`/`#AARRGGBB`); `triggerTags` ersetzt die Variablenliste
  des Triggers; jeder Wert wird zurückgelesen. `customDuration` gehört zu `triggerType:"CustomCycle"`.
- **Lesevorgänge** melden nur die dokumentierten Eigenschaften, die eine Dynamisierung hat (`Tag`, `PlcTag`, `Address`,
  `DataType`, `ReadOnly`, `UseIndirectAddressing`, `ScriptCode`, `GlobalDefinitionAreaScriptCode`, `Async`, `Color`,
  `AlternateColor`, `FlashingCondition`, `FlashingRate`, `ResourceList`, `DynamicTagName`, `Trigger.*`), als Text: Enums
  per Name, Farben als `#AARRGGBB`, Listen mit ", " verbunden. Es gibt keine generische Aufzählung von Attributen
  (V21-Fallstrick 3).
- **Löschvorgänge** werden zurückgelesen (`Find` findet danach nichts) und lösen den Info-Guard
  `hmi_deletes_event_or_dynamization` aus.
- **Formen und Widgets** (`UI.Shapes.*`, `UI.Widgets.*`) legt das vorhandene `create_screen_item` über ihren exakten
  Openness-Typnamen an (`HmiLine`, `HmiPolyline`, `HmiEllipse`, `HmiGauge`, `HmiSlider`, `HmiClock`, …) und
  `set_screen_item_property` ändert sie; ihre Ereignisse und Dynamisierungen nutzen die Operationen oben.
- Verifikation: `trusted`/`superseded` auf dem Schlüssel des Bildobjekts; ein Eintrag hängt vom letzten früheren Eintrag am
  selben Bild ab.

## WinCC Unified: Audit-Klassen, Runtime-Einstellungen, Anlagensichten (Issue #29)

Worker `Openness/HmiExtensions/HmiUnifiedConfiguration.cs` (außerhalb von `Openness/Hmi`, weil es die Dienst-Helfer von
`TechnologyObjectRuntime` nutzt; aufgerufen aus `Program.cs`, von `HmiPitfallSourceTests` mitgeprüft), Methodennamen
`HmiUnifiedConfigurationMethods` (Contracts `Hmi/HmiUnifiedConfigurationInfos.cs`), Host-Partials
`TiaMcpServer/Hmi/Extended/*.Configuration.cs`.

| Tool | Operation | Pflicht | Optional | Siemens-Member |
| --- | --- | --- | --- | --- |
| `hmi_read` | `list_plant_views` | – | `hmiName`, `plantViewName` | `PlantViewsProvider.PlantViews`, `PlantView.Name/AssignedHmiDevice/PlantViewNodes`, `PlantViewNode.Name/ViewPath/PlantObject`, `PlantObject.Type/PlantObjectInterfaces` |
| `hmi_write` | `create_audit_class` | `auditClassName` | – | `HmiAlarmAuditClassComposition.Create()`, dann `Name`; Rücklesen mit `Find(String)` |
| `hmi_write` | `delete_audit_class` | `auditClassName` | – | `Find(String)`, `HmiAlarmAuditClass.Delete()` |
| `hmi_write` | `set_audit_class_property` | `auditClassName`, `propertyName`, `value` | `culture` | `HmiAuditClass.Name`, `DisplayName`, `CommentRequired`, `ConfirmationMode`, `RequiredFunctionRights` |
| `hmi_write` | `set_runtime_setting` | `propertyName`, `value` | `settingsSection`, `culture` | `HmiSoftware.RuntimeSettings` und seine dokumentierten Abschnitte |
| `hmi_write` | `create_plant_view` / `delete_plant_view` | `plantViewName` | – | `PlantViewComposition.Create(String)`/`Find(String)`, `PlantView.Delete()` |
| `hmi_write` | `create_plant_view_node` | `plantViewName`, `nodeName` | `nodePath` (Elternknoten), `plantObjectType` | `PlantViewNodeComposition.Create(String)` oder `Create(String, String)` |
| `hmi_write` | `delete_plant_view_node` | `plantViewName`, `nodePath` | – | `PlantViewNode.Delete()` |
| `hmi_write` | `set_plant_object_interface_property` | `plantViewName`, `nodePath`, `interfaceName`, `propertyName`, `value` | `culture` | Eigenschaften von `PlantObjectInterface` (`AccessMode`, `AcquisitionCycle`, `AcquisitionMode`, `Comment`, `Connection`, `DataType`, `HmiDataType`, `MaxLength`, `Name`, `Persistent`, `PlcName`, `PlcTag`) |

- **Werte** laufen durch dieselbe Umwandlung wie bei `set_screen_item_property`: Der aktuelle Wert wird über die
  generische Attribut-API gelesen, um seinen Laufzeittyp zu erfahren (String, Bool, Zahl, Enum-Membername, Farbe,
  mehrsprachiger Text mit `culture`), geschrieben und zurückgelesen. `propertyName` ist bei Audit-Klassen und
  Anlagenobjekt-Schnittstellen auf die dokumentierten Eigenschaften beschränkt; bei Runtime-Einstellungen wird jeder
  Eigenschaftsname der Einstellungen oder des dokumentierten Abschnitts (`HmiExclusiveOperationSettings`,
  `HmiReportingSettings`, `HmiUnifiedTagSettings`, `HmiUpssRuntimeSettings`, `MaxLoginRuntimeSettings`,
  `OpcUaServerRuntimeSettings`, `ProcessDiagnosticsRuntimeSettings`, `RuntimeResourceSettings`, `ScreenResolution`,
  `TelemetryRuntimeSettings`) an TIA Portal weitergegeben, das entscheidet (ein unbekannter Name scheitert mit
  `target_not_found` und der Meldung von TIA Portal). Ein Abschnitt, dessen Wert kein Einstellungsobjekt ist, wird mit dem
  Hinweis abgelehnt, ihn als Eigenschaft der Runtime-Einstellungen zu setzen.
- **Audit-Risiko.** Der hmi_read-Spike sah Audit-/GMP-Eigenschaften auf Comfort Panels scheitern und eine
  Audit-Eigenschaft einer Variable (`ConfirmationType`) beim Lesen TIA Portal abstürzen; `set_audit_class_property` und
  die GMP-Runtime-Einstellungen (`GMPEnabled`, `GeneralESIGCommentsStrategy`) lösen den Info-Guard
  `hmi_audit_property_unverified` aus (das Schreiben liest zuerst den aktuellen Wert). TIA Portal entscheidet.
- **Anlagensichten.** V21 dokumentiert `Cpm.PlantViewsProvider`, aber nicht, welches Objekt ihn anbietet; er wird über
  seinen vollständigen Typnamen unter den Diensten gesucht, die die HMI-Software und dann das Projekt auflisten
  (`GetServiceInfos()`), und `list_plant_views` meldet `providerOwner`. Listet ihn keines, scheitert der Eintrag mit
  `api_member_missing` (Anlagensichten brauchen die Option Anlagenmodell). `nodePath` sind die mit `/` getrennten
  Knotennamen unter der Sicht (`list_plant_views` liefert ihn je Knoten; der eigene `ViewPath` von TIA Portal steht
  daneben). Lesevorgänge durchlaufen höchstens 5.000 Knoten und Tiefe 16 (`nodesTruncated`).
- **Löschvorgänge** (Audit-Klasse, Anlagensicht, Knoten) werden zurückgelesen und lösen
  `hmi_deletes_audit_class_or_plant_view` aus; Anlegevorgänge werden mit `Find` oder per Name zurückgelesen.

### Weggelassen (Issue #29)

| Punkt | Warum |
| --- | --- |
| Unified-Bibliothek (`HmiUnified.Library.ScriptModuleType`) | V21 dokumentiert den Typ ohne eigene Member; Bibliothekstypen behandeln `library_read`/`library_write`. |
| Schreiben von Audit-Trails (`HmiLogging.HmiAuditTrail`) | Nur `Name` ist dokumentiert, und die Komposition hat kein `Create`; `list_logs` liest sie bereits. |
| Lesen von Eigenschaften der Audit-Klassen | Der Audit-/GMP-Absturzbefund des Spikes (siehe oben); `list_alarm_classes` listet weiter nur Namen. |
| Anlegen von Ausdrucks-, Ressourcenlisten- und Variablenparameter-Dynamisierungen; Wertkonverter und Zuordnungstabellen von Variablen-Dynamisierungen (`UI.Dynamization.Tag.*`) | In diesem Paket nicht gebaut; `list_screen_item_dynamizations` meldet diese Dynamisierungen mit ihren dokumentierten Werten. |
| `IHmiScript.SyntaxCheck()` | Dokumentiert, aber nicht aufgerufen; das HMI in TIA Portal übersetzen. |
| Schreiben von Schnittstellen-Membern und Archivvariablen von Anlagenobjekten (`PlantObjectInterfaceMember`, `PlantObjectLoggingTag`) | In diesem Paket nicht gebaut; Lesevorgänge melden nur die Namen der Schnittstellen. |
| Teile von Formen (`Points` von Polylinien/Polygonen und andere `UI.Parts`-Kompositionen) | In diesem Paket nicht gebaut; die Geometrie von Box-Objekten läuft über `set_screen_item_property`. |
| SiVArc, HMI-Übersetzen | Wie bisher außerhalb des Umfangs (`compile_check` bleibt nur für PLCs). |

Noch offene Live-Prüfungen für Issue #29: jede Operation oben an einem WinCC-Unified-Projekt (keines war verfügbar); die
Bindung der Ereignis-Enum bei anderen Bildobjekttypen als der dokumentierten Schaltfläche; ob `Trigger.Tags` eine
veränderbare Liste ist; welches Objekt den `PlantViewsProvider` auflistet.
