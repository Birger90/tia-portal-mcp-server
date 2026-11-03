# Classic WinCC and WinCC Unified extensions (hmi_read / hmi_write)

**Language:** English · [Deutsch](HMI_CLASSIC_UNIFIED_EXTENSIONS_SUMMARY.de.md)

This reference covers issue #28 (Classic WinCC: Comfort/Advanced panels) and issue #29 (WinCC Unified: events,
dynamizations, shapes and widgets, audit, plant objects). Everything here is **not live-tested**. The evidence for every
Siemens member is the V21 IntelliSense description (`Siemens.Engineering.WinCC.xml`, `Siemens.Engineering.WinCCUnified.xml`,
`PublicAPI\V21\net48`, 2025-10-29); no member outside it is used. The tools, access modes and counts (11/30/30) are
unchanged: these are more `hmi_read` and `hmi_write` operations, with the conventions of the
[HMI reference](HMI_OPERATIONS_SUMMARY.md) (50 items, caller order, stop at the first failure, no rollback, `dryRun:true`
previews, write contract `2.0`, read contract `1.1`).

## Classic WinCC (issue #28)

The worker still has no compile reference to `Siemens.Engineering.WinCC.dll`. A Classic HMI is recognised by its type name
`Siemens.Engineering.Hmi.HmiTarget`, and every classic member is bound at runtime by exact name and argument types on the
live object (worker `Openness/Hmi/HmiClassicObjects.cs`, kinds in Contracts `Hmi/HmiClassicInfos.cs`). A member the
installation does not offer fails the item `api_member_missing` before anything is written or changed. The Unified
operations keep refusing a Classic HMI with `target_kind_unsupported`; their message now names the classic operations.
The classic operations refuse a Unified HMI the same way. With `hmiName` omitted they pick the project's only Classic HMI.

### Kinds (`classicKind`)

| classicKind | Where (V21) | Folders | Delete | Export | Import into |
| --- | --- | --- | --- | --- | --- |
| `tagTables` | `HmiTarget.TagFolder` → `TagTables` | yes | yes | yes | folder (`groupPath`) |
| `tags` | `TagTable.Tags` | – (tables) | yes | yes | tag table (`tableName`, required) |
| `screens` | `HmiTarget.ScreenFolder` → `Screens` | yes | yes | yes | folder |
| `screenTemplates` | `ScreenTemplateFolder` → `ScreenTemplates` | yes | yes | yes | folder |
| `screenPopups` | `ScreenPopupFolder` → `ScreenPopups` | yes | yes | yes | folder |
| `screenSlideins` | `ScreenSlideinFolder` → `ScreenSlideins` (named by `SlideinType`) | – | – (none documented) | yes | system folder |
| `vbScripts` | `VBScriptFolder` → `VBScripts` | yes | yes | yes | folder |
| `textLists` | `HmiTarget.TextLists` | – | yes | yes | target |
| `graphicLists` | `HmiTarget.GraphicLists` | – | yes | **never** (V21 pitfall 3) | target |
| `connections` | `HmiTarget.Connections` | – | yes | yes | target |
| `cycles` | `HmiTarget.Cycles` | – | yes | yes | target |
| `screenGlobalElements` | `HmiTarget.ScreenGlobalElements` (one object) | – | – | yes | `HmiTarget.ImportScreenGlobalElements` |
| `screenOverview` | `HmiTarget.ScreenOverview` (one object) | – | – | yes | `HmiTarget.ImportScreenOverview` |

`groupPath` is the `/`-separated user folder path below the kind's system folder (`Folders` composition, case-insensitive
names); for `tags` it narrows the tag tables to that folder.

### Operations

| Tool | Operation | Required | Optional | Siemens members |
| --- | --- | --- | --- | --- |
| `hmi_read` | `list_classic_objects` | `classicKind` | `hmiName`, `groupPath`, `tableName` (tags) | `Name` (or `SlideinType`), `IsSystemObject`, `Folders` |
| `hmi_write` | `export_classic_objects` | `classicKind`, `directoryPath` | `groupPath`, `tableName`, `objectNames` (1–200), `overwrite` | `Export(FileInfo, ExportOptions.None)` |
| `hmi_write` | `import_classic_objects` | `classicKind`, `filePath` | `groupPath`, `tableName` (required for tags), `overwrite` | composition `Import(FileInfo, ImportOptions)`, `ImportScreenGlobalElements`/`ImportScreenOverview` |
| `hmi_write` | `delete_classic_object` | `classicKind`, `objectName` | `groupPath`, `tableName` | `Delete()` |
| `hmi_write` | `create_classic_folder` | `classicKind`, `groupPath` (path of the new folder) | – | `…UserFolderComposition.Create(String)` |
| `hmi_write` | `delete_classic_folder` | `classicKind`, `groupPath` | – | `…UserFolder.Delete()` |

- **list_classic_objects** lists every object of the kind in the scope with `name`, `folderPath`, `tableName`,
  `isSystemObject` (tag tables, cycles; null elsewhere) and `typeName`, plus the user folder paths. It is not paged: on a
  large panel narrow `tags` with `tableName` (a response over the budget is omitted with that guidance).
- **export_classic_objects** writes one `<name>.xml` per object (singletons: `<classicKind>.xml`) into an existing directory:
  every `Export` member is bound before the first export, each export goes to a private temporary file, is staged next to
  the target, moved into place and hashed (`appliedProperties` lists path, bytes and SHA-256). Existing files need
  `overwrite:true`; two objects that would write the same file are refused before anything is written.
- **import_classic_objects** imports a private copy of the file with `ImportOptions.None` (`Override` with `overwrite:true`,
  info guard `hmi_classic_import_overrides`). Without overwrite a new object must appear in the composition, otherwise the
  item fails `postcondition_failed`; the new names are listed. A singleton import is not read back (Openness offers no read
  of its content) and says so.
- **delete_classic_object** and the folder writes are read back: the object or folder must be gone (or present after a
  create), otherwise `postcondition_failed`. Deletes fire the info guard `hmi_classic_deletes`.
- File paths follow the shared rules (absolute drive or UNC share paths; device paths, reserved names, `.`/`..` and trailing
  dot or blank segments refused).
- Verification: completed items are `trusted` or `superseded` (keys per kind and scope, `ClassicObjects`/`ClassicObject`/
  `ClassicFolder`); an item depends on the latest earlier item of the same classic kind.

### V21 pitfalls applied

- **Pitfall 3** (reported, not verified on V21): exporting a classic graphic list is reported to crash TIA Portal V21.
  `export_classic_objects` with `graphicLists` is refused by the catalog and, in the worker, before `Export` is bound:
  "skipped: reported to crash TIA Portal V21 …; nothing was written." `HmiPitfallSourceTests` pins that order. Listing,
  importing and deleting graphic lists are allowed.
- **Pitfall 4** (reported, not verified on V21): a classic screen import with a duplicate screen `<Number>` is reported to
  terminate TIA Portal. Before `Import`, the worker reads every `Hmi.Screen.Screen` (`AttributeList/Name`, `Number`) of
  the file and the number of every existing screen (generic `GetAttribute("Number")`; when TIA Portal refuses it, from a
  private export of that screen) and refuses the item (`validation_error`, nothing imported) when a number repeats in the
  file or another existing screen uses it (a same-named screen is the one an overriding import replaces). A screen whose
  number cannot be read is named in a warning; the import then runs. This is the one classic refusal stricter than TIA
  Portal, for the same reason as the VCI `SimaticSD` export refusal: the reported failure is a terminated TIA Portal.

### Left out (issue #28)

| Item | Why |
| --- | --- |
| `CreateFrom(MasterCopy)` / `CreateFrom(…LibraryTypeVersion)` of tag tables, tags, screens, templates, pop-ups, VB scripts | Library instantiation belongs to `library_write`; its targets do not include classic HMI compositions yet. Not built here. |
| Multilingual graphics (`GraphicsProvider.Graphics`, `MultiLingualGraphicComposition`) | V21 documents the provider but not which object offers it as a service; nothing is guessed. |
| `Hmi.Alarm`, `Hmi.Recipe`, `Hmi.Logging`, `Hmi.Report`, `Hmi.Scheduler`, `Hmi.Faceplate` | V21 documents only enums (and library type names) in these namespaces, no composition, export or import member. |
| Classic object content (properties of a tag, screen items, connection parameters) | V21 documents no typed members; content goes through the SimaticML export/import above. |
| Slide-in screen delete, global elements/overview delete | No `Delete()` is documented. |
| Classic compile | `compile_check` stays PLC-only. |

Live checks still open: every row above on a project with a Comfort panel (issue #28 asks for one); whether
`GetAttribute("Number")` reads a classic screen number; the `ScreenTemplateFolder` shape (V21 calls it a "composition of
screen template folders").

## WinCC Unified: events and dynamizations (issue #29)

Worker `Openness/Hmi/HmiScreenEvents.cs`, method names `HmiScreenEventMethods` (Contracts `Hmi/HmiScreenEventInfos.cs`),
host partials `TiaMcpServer/Hmi/Extended/*.Events.cs`. The members are documented in V21 but are not in the compile stubs,
so they are bound at runtime on the live object; a missing one fails `api_member_missing` before anything changes. A
target is a screen (`screenName`, optional `groupPath` for writes) or one of its items (`itemName`).

| Tool | Operation | Required | Optional | Siemens members |
| --- | --- | --- | --- | --- |
| `hmi_read` | `list_screen_item_events` | `screenName` | `hmiName`, `itemName` | `EventHandlers`, `PropertyEventHandlers`, handler `EventType`/`PropertyName`/`Script`, `IHmiScript.ScriptCode`/`GlobalDefinitionAreaScriptCode`/`Async` |
| `hmi_read` | `list_screen_item_dynamizations` | `screenName` | `hmiName`, `itemName` | `Dynamizations`, `DynamizationBase.PropertyName`/`DynamizationType`, the documented properties of each type, `ScriptDynamization.Trigger` (`Type`, `Tags`, `CustomDuration`) |
| `hmi_write` | `set_event_script` | `screenName`, `eventType`, `scriptCode` | `groupPath`, `itemName`, `propertyName`, `globalDefinitionAreaScriptCode`, `async` | `…EventHandlerComposition.Find/Create(<type>EventType)`; with `propertyName`: `PropertyEventHandlerComposition.Find/Create(String, PropertyEventType)`; `Script` properties |
| `hmi_write` | `delete_event_script` | `screenName`, `eventType` | `groupPath`, `itemName`, `propertyName` | `Find`, handler `Delete()` |
| `hmi_write` | `set_dynamization_script` | `screenName`, `propertyName`, `scriptCode` | `groupPath`, `itemName`, `globalDefinitionAreaScriptCode`, `async`, `triggerType`, `triggerTags` (1–50), `customDuration` | `DynamizationBaseComposition.Create<ScriptDynamization>(String)`, `ScriptCode`, `Trigger.Type/Tags/CustomDuration` |
| `hmi_write` | `set_dynamization_flashing` | `screenName`, `propertyName` | `groupPath`, `itemName`, `color`, `alternateColor`, `flashingCondition`, `flashingRate` | `Create<FlashingDynamization>(String)`, `Color`, `AlternateColor`, `FlashingCondition`, `FlashingRate` |
| `hmi_write` | `delete_dynamization` | `screenName`, `propertyName` | `groupPath`, `itemName` | `DynamizationBase.Delete()` |

- **Event types.** Each screen item type has its own event enum (`HmiButtonEventType`, `HmiIOFieldEventType`, … and
  `HmiScreenEventType` for the screen); the worker binds the single `Find`/`Create` of the target's `EventHandlers` that
  takes one enum and parses `eventType` as an exact member name. Another name fails `validation_error` and lists the
  members. A property event (`propertyName` set) takes `Change` or `QualityCodeChange` (`PropertyEventType`).
- **Scripts.** `set_event_script` finds the handler or creates it, then writes `ScriptCode` (and
  `GlobalDefinitionAreaScriptCode`, `Async` when given) through the documented `IHmiScript` properties, each read back.
  `scriptCode` is sent as given (at most 60,000 characters); syntax is not checked (`IHmiScript.SyntaxCheck` is documented
  but not called; compile the HMI in TIA Portal).
- **Dynamizations.** A property has one dynamization. `set_dynamization_script`/`set_dynamization_flashing` update the
  existing one of the same kind or create one; another kind on the property is refused with `target_kind_unsupported`
  (delete it first), as `set_dynamization_tag` already refuses a script dynamization. Trigger and flashing values are
  converted to exactly the runtime type of the current value (enum member names such as `T1s`, `CustomCycle`, `Tags`,
  `Always`, `Fast`; colours `#RRGGBB`/`#AARRGGBB`); `triggerTags` replaces the trigger's tag list; every value is read back.
  `customDuration` belongs to `triggerType:"CustomCycle"`.
- **Reads** report only the documented properties a dynamization has (`Tag`, `PlcTag`, `Address`, `DataType`, `ReadOnly`,
  `UseIndirectAddressing`, `ScriptCode`, `GlobalDefinitionAreaScriptCode`, `Async`, `Color`, `AlternateColor`,
  `FlashingCondition`, `FlashingRate`, `ResourceList`, `DynamicTagName`, `Trigger.*`) as text: enums by name, colours as
  `#AARRGGBB`, lists joined by ", ". There is no generic attribute enumeration (V21 pitfall 3).
- **Deletes** are read back (`Find` finds nothing afterwards) and fire the info guard `hmi_deletes_event_or_dynamization`.
- **Shapes and widgets** (`UI.Shapes.*`, `UI.Widgets.*`) are created with the existing `create_screen_item` by their exact
  Openness type name (`HmiLine`, `HmiPolyline`, `HmiEllipse`, `HmiGauge`, `HmiSlider`, `HmiClock`, …) and changed with
  `set_screen_item_property`; their events and dynamizations use the operations above.
- Verification: `trusted`/`superseded` on the screen item key; an item depends on the latest earlier item on the same screen.

## WinCC Unified: audit classes, runtime settings, plant views (issue #29)

Worker `Openness/HmiExtensions/HmiUnifiedConfiguration.cs` (outside `Openness/Hmi` because it uses the service helpers of
`TechnologyObjectRuntime`; dispatched from `Program.cs`, scanned by `HmiPitfallSourceTests`), method names
`HmiUnifiedConfigurationMethods` (Contracts `Hmi/HmiUnifiedConfigurationInfos.cs`), host partials
`TiaMcpServer/Hmi/Extended/*.Configuration.cs`.

| Tool | Operation | Required | Optional | Siemens members |
| --- | --- | --- | --- | --- |
| `hmi_read` | `list_plant_views` | – | `hmiName`, `plantViewName` | `PlantViewsProvider.PlantViews`, `PlantView.Name/AssignedHmiDevice/PlantViewNodes`, `PlantViewNode.Name/ViewPath/PlantObject`, `PlantObject.Type/PlantObjectInterfaces` |
| `hmi_write` | `create_audit_class` | `auditClassName` | – | `HmiAlarmAuditClassComposition.Create()`, then `Name`; `Find(String)` read-back |
| `hmi_write` | `delete_audit_class` | `auditClassName` | – | `Find(String)`, `HmiAlarmAuditClass.Delete()` |
| `hmi_write` | `set_audit_class_property` | `auditClassName`, `propertyName`, `value` | `culture` | `HmiAuditClass.Name`, `DisplayName`, `CommentRequired`, `ConfirmationMode`, `RequiredFunctionRights` |
| `hmi_write` | `set_runtime_setting` | `propertyName`, `value` | `settingsSection`, `culture` | `HmiSoftware.RuntimeSettings` and its documented sections |
| `hmi_write` | `create_plant_view` / `delete_plant_view` | `plantViewName` | – | `PlantViewComposition.Create(String)`/`Find(String)`, `PlantView.Delete()` |
| `hmi_write` | `create_plant_view_node` | `plantViewName`, `nodeName` | `nodePath` (parent), `plantObjectType` | `PlantViewNodeComposition.Create(String)` or `Create(String, String)` |
| `hmi_write` | `delete_plant_view_node` | `plantViewName`, `nodePath` | – | `PlantViewNode.Delete()` |
| `hmi_write` | `set_plant_object_interface_property` | `plantViewName`, `nodePath`, `interfaceName`, `propertyName`, `value` | `culture` | `PlantObjectInterface` properties (`AccessMode`, `AcquisitionCycle`, `AcquisitionMode`, `Comment`, `Connection`, `DataType`, `HmiDataType`, `MaxLength`, `Name`, `Persistent`, `PlcName`, `PlcTag`) |

- **Values** go through the same conversion as `set_screen_item_property`: the current value is read with the generic
  attribute API to learn its runtime type (string, bool, number, enum member name, colour, multilingual text with
  `culture`), written and read back. `propertyName` is restricted to the documented properties for audit classes and
  plant object interfaces; for runtime settings any property name of the settings or of the documented section
  (`HmiExclusiveOperationSettings`, `HmiReportingSettings`, `HmiUnifiedTagSettings`, `HmiUpssRuntimeSettings`,
  `MaxLoginRuntimeSettings`, `OpcUaServerRuntimeSettings`, `ProcessDiagnosticsRuntimeSettings`, `RuntimeResourceSettings`,
  `ScreenResolution`, `TelemetryRuntimeSettings`) is passed to TIA Portal, which decides (an unknown name fails
  `target_not_found` with TIA Portal's message). A section whose value is not a settings object is refused with a hint to
  set it as a property of the runtime settings.
- **Audit risk.** The hmi_read spike saw audit/GMP properties fail on Comfort panels and one tag audit property
  (`ConfirmationType`) crash TIA Portal when read; `set_audit_class_property` and the GMP runtime settings
  (`GMPEnabled`, `GeneralESIGCommentsStrategy`) fire the info guard `hmi_audit_property_unverified` (the write reads the
  current value first). TIA Portal decides.
- **Plant views.** V21 documents `Cpm.PlantViewsProvider` but not which object offers it; it is looked up by its full type
  name among the services the HMI software and then the project list (`GetServiceInfos()`), and `list_plant_views`
  reports `providerOwner`. When neither lists it the item fails `api_member_missing` (plant views need the plant model
  option). `nodePath` is the `/`-separated node names below the view (`list_plant_views` returns it per node; TIA
  Portal's own `ViewPath` is reported beside it). Reads walk at most 5,000 nodes and depth 16 (`nodesTruncated`).
- **Deletes** (audit class, plant view, plant view node) are read back and fire `hmi_deletes_audit_class_or_plant_view`;
  creates are read back with `Find` or by name.

### Left out (issue #29)

| Item | Why |
| --- | --- |
| Unified library (`HmiUnified.Library.ScriptModuleType`) | V21 documents the type without own members; library types are handled by `library_read`/`library_write`. |
| Audit trails (`HmiLogging.HmiAuditTrail`) writes | Only `Name` is documented and the composition has no `Create`; `list_logs` already reads them. |
| Reading audit class properties | The spike's audit/GMP crash finding (see above); `list_alarm_classes` keeps listing names only. |
| Creating expression, resource list and tag parameter dynamizations; value converters and mapping tables of tag dynamizations (`UI.Dynamization.Tag.*`) | Not built in this package; `list_screen_item_dynamizations` reports these dynamizations with their documented values. |
| `IHmiScript.SyntaxCheck()` | Documented but not called; compile the HMI in TIA Portal. |
| Plant object interface members and plant object logging tags (`PlantObjectInterfaceMember`, `PlantObjectLoggingTag`) writes | Not built in this package; reads report interface names only. |
| Shape parts (polyline/polygon `Points` and other `UI.Parts` compositions) | Not built in this package; geometry of box items goes through `set_screen_item_property`. |
| SiVArc, HMI compile | Out of scope as before (`compile_check` stays PLC-only). |

Live checks still open for issue #29: every operation above on a WinCC Unified project (none was available); the event
enum binding for screen item types other than the documented button shape; whether `Trigger.Tags` is a mutable list;
which object lists the `PlantViewsProvider`.
