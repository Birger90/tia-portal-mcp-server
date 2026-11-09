# Vervollständigung der V21-API (Paket G)

**Sprache:** [English](V21_API_COMPLETION_SUMMARY.md) · Deutsch

Stand: **nicht live getestet**. Ein Namensvergleich der API-Beschreibung von TIA Portal V21 Openness (die V21-IntelliSense-XML-Dateien)
mit diesem Server ergab dokumentierte Methoden, die der Server noch nicht aufrief. Diese Referenz beschreibt die, die in die
bestehenden Tools aufgenommen wurden, und nennt jede ausgelassene mit Grund. Offline-Tests decken Prüfung, Guards, Dekodierung,
Verifikation und den FakeWorker-Weg ab; kein Punkt lief gegen ein echtes TIA Portal.

Gemeinsame Regeln (wie überall in diesem Server): Member, die nicht in den Compile-Stubs stehen, werden zur Laufzeit über exakten
Namen und dokumentierte Parametertypen gebunden (`api_member_missing` vor jedem Aufruf, nichts geändert); Passwörter nur aus
Passwortquellen; jeder Schreibvorgang wird zurückgelesen, wo ein dokumentiertes Lesen existiert, sonst sagt der Eintrag das ehrlich
(`trusted` oder `notVerified`); TIA Portal entscheidet, eine Ablehnung wird als `engineering_rejected` gemeldet.

## Istwerte einer laufenden PLC (`plc_write`)

Beleg: V21 `Siemens.Engineering.Step7.xml`, `ValueService.LoadSnapshotAsActualValues()` („Loads the snapshot as actual values“) und
`ValueService.LoadStartValuesAsActualValues()` („Loads the start values as actual values“). `ValueService` ist ein Dienst des
`Interface` des DB, wie bei `create_db_snapshot` (BetterTIA/watch). Beide Operationen gehören zur Gerätedaten-Familie
(`PlcDeviceDataMethods`, Contracts `Plc/DeviceData`), neben dem Master-Secret online.

| Operation | Felder | Fähigkeit, Guard | Siemens-Aufruf | Verifikation |
| --- | --- | --- | --- | --- |
| `load_snapshot_as_actual_values` | `blockPath` eines Global- oder Instanz-DB (das erste Segment nennt die PLC); optional `portalConfirmations` | `OnlineControl` (read-write und full; read-only lehnt ab), Acknowledge-Guard `plc_loads_actual_values_online` | zuerst wird der `ValueService` des DB gebunden, dann muss `OnlineProvider.State` `Online` lesen (vorher mit `plc_tia_online go_online` verbinden; sonst `state_changed`, nichts geändert), dann `LoadSnapshotAsActualValues()` | `deviceData` notVerified (success false): Openness dokumentiert kein Lesen der Istwerte einer CPU |
| `load_start_values_as_actual_values` | wie oben | wie oben | `LoadStartValuesAsActualValues()` mit denselben Prüfungen | wie oben |

Der Acknowledge-Guard lässt `plc_write` jeden Aufruf mit einem solchen Punkt bestätigen (read-write fragt den Menschen mit einer
Rückfrage, die jeden DB und jede PLC nennt; full läuft unter Policy), wie `plc_download` und das Master-Secret online. PLCSIM ist
erlaubt: Ziel ist die Steuerung, mit der die PLC des Projekts online ist. Der Prozess kann sofort auf die neuen Werte reagieren; der
Server kann das Laden nicht rückgängig machen. Die Werte in TIA Portal prüfen (DB online beobachten) oder mit `plc_online_read`
über die Web API.

## Projektschutz und UMC (`plc_read` / `plc_write`, Einstellungsfamilie)

Beleg: V21 `Siemens.Engineering.Base.xml`: `ProjectBase.ProtectProject(String administratorUsername, SecureString
administratorUserPassword)` („Protects the project“); Namespace `Siemens.Engineering.Umac`: `UmcUserComposition.CreateOfflineUmcUser(String)`,
`UmcUserGroupComposition.CreateOfflineUmcUserGroup()`, `UmcUserComposition.Create(UmcUserInfo)`, `UmcUserGroupComposition.Create(UmcUserGroupInfo)`,
`UmcServerConfigurator.UmcServer` und `.CheckConsistency()` (Boolean), `UmcServer.GetUserByName(String)` (`UmcUserInfo.Name`),
`.GetUserGroupByName(String)` (`UmcUserGroupInfo.UserGroupName`, `.Description`), Ereignis `UmcServer.Authentication`
(`UmcAuthenticationEventArgs.UmcCredentials`: `UmacCredentials.Name`, `.Type` (`UmacUserType.Global`), `.SetPassword(SecureString)`).
Sie erweitern die UMAC-Operationen der [Einstellungsfamilie](PLC_SETTINGS_SUMMARY.de.md) (Definitionen von Issue #12, `PlcSettingsRun.UmacMore.cs`).
Wie der `UmcServerConfigurator` erreicht wird, ist nicht dokumentiert: er wird unter den `GetServiceInfos()` des Projekts, dann des
`UmacConfigurator` gesucht; nicht gefunden ist `unsupported_capability`, nichts geändert.

| Operation | Felder | Fähigkeit, Guards | Siemens-Aufrufe | Rücklesen |
| --- | --- | --- | --- | --- |
| `plc_write protect_project` | `userName` (der Administrator), `passwordVariable` (eine Passwortquelle) | `ProjectMutation`; Acknowledge-Guard `plc_protects_project` (read-write fragt nach: der Schutz kann den Benutzer aussperren, und Openness dokumentiert keinen Weg zurück), Info `plc_changes_project_access` | `ProjectBase.ProtectProject(String, SecureString)` | `EngineeringFunctionRights` nicht leer (Siemens-Security-Skill: das Projekt ist geschützt) und der Administrator unter `ProjectUsers`; ohne UmacConfigurator danach: nicht zurückgelesen, mit Meldung |
| `plc_write create_offline_umc_user` | `userName` | `ProjectMutation`, Info `plc_changes_project_access` | `UmacConfigurator.UmcUsers.CreateOfflineUmcUser(String)` | `UmcUsers.Find`; live getestet am 2026-11-07 (Build 986928f, CPU 1516-3 PN/DP V2.9): der Benutzer wurde angelegt und zurückgelesen |
| `plc_write create_offline_umc_user_group` | optional `name` | wie oben | `UmcUserGroups.CreateOfflineUmcUserGroup()` (TIA nennt die Gruppe `Group_N`), dann `UmcUserGroup.SetName(String)` an der zurückgegebenen Gruppe, wenn `name` angegeben ist | `Find` des tatsächlichen Gruppennamens in neu gelesenen `UmcUserGroups`, und der zurückgelesene Name gleicht `name`; das Ergebnis nennt die Gruppe (`Group_N` ohne `name`). K96: live am 2026-11-07 (Build 986928f) scheiterte der frühere Zählvergleich mit `postcondition_failed` vor `SetName`, `Group_1` blieb; die Anzahl wird nicht mehr verwendet (seitdem nicht live getestet) |
| `plc_write import_umc_user` | `name`; optional `userKind` (`umcUser` Standard, `umcUserGroup`), UMC-Server-Anmeldung `userName` + `passwordVariable` (beide oder keines) | wie oben, dazu Info `plc_lowers_cpu_protection` (der Benutzer erhält die Rechte seiner Rollen) | `UmcServer.GetUserByName`/`GetUserGroupByName`, dann `Create(UmcUserInfo)`/`Create(UmcUserGroupInfo)` | `Find(name)`; ein dem Server unbekannter Benutzer ist `target_not_found` |
| `plc_read get_umc_server_user` | `name`; optional `userKind`, Anmeldung | `OnlineObserve` (jeder Modus: kontaktiert den UMC-Server, ändert nichts) | `GetUserByName`/`GetUserGroupByName` | Abschnitt `umcServerUser`: `Found`, `Name` oder `UserGroupName` und `Description` |
| `plc_read check_umc_consistency` | optional Anmeldung | `OnlineObserve` | `UmcServerConfigurator.CheckConsistency()` | Abschnitt `umcConsistency`: `Consistent` |

UMC-Server-Anmeldung: mit `userName` und `passwordVariable` abonniert der Worker für den Aufruf `UmcServer.Authentication` und
beantwortet jede Anfrage mit `UmacCredentials.Name`, `Type` = `Global` (wenn schreibbar) und `SetPassword(SecureString)`; das
Passwort wird direkt vor dem Aufruf aufgelöst und danach verworfen, der Handler wirft nie in TIA Portal hinein, und das Ergebnis
nennt, wie viele Anfragen beantwortet wurden. Ohne Anmeldung authentifiziert TIA Portal selbst (z. B. Single Sign-on) oder lehnt ab.
Ausgelassen: `UmcServerConfigurator.Synchronize()` (ersetzt die UMC-Kopie des Projekts; nicht in der Lückenliste, kann folgen) und das
Aufheben des Projektschutzes (kein dokumentierter Member).

## Engineering-Objekte über Pfade: Markierungen, Kennungen, Erzeugungsinfos, verknüpfte Variablen, Kurvenscheiben (`plc_read` / `plc_write`)

Beleg: V21 `Siemens.Engineering.Base.xml`: `Multiuser.LocalSession.MarkingService`; `MarkingService.GetMarkings()`
(`Markings.AllMarkings`, `.ConflictedMarkings`; `Marking.MarkedObject`, `.MarkState`), `.GetMarkStateInfo(IEngineeringObject)`
(`MarkStateInfo.IsMarkable`, `.MarkState`), `.IsMarkable(IEngineeringObject)`, `.MarkObjects(IEnumerable<IEngineeringObject>)`,
`.UnmarkObjects(...)`, Enum `MarkState` (`None`, `IsMarkedByMe`, `IsMarkedByOthers`, `IsUptoDate`); `ObjectIdentifierProvider.GetIdentifier(IEngineeringObject)`
und `.Find(String)`; `IEngineeringObject.GetCompositionInfos()` (`EngineeringCompositionInfo.Name`, `.Type`), `.GetCreationInfos(String)`,
`.GetComposition(String)`, `IEngineeringComposition.GetCreationInfos()` (`EngineeringCreationInfo.Type`, `.ParameterInfos`;
`EngineeringCreationParameterInfo.Name`, `.IsMandatory`, `.SupportedTypes`); `Siemens.Engineering.Step7.xml`: `SW.PlcTagProvider.GetLinkedTags()`
(„Service provider for obtaining Plc Tag from Channel“), `Motion.CamDataSupport.LoadCamData(FileInfo, CamDataFormatSeparator)`,
`.LoadCamDataBinary(FileInfo)`, `.SaveCamData(FileInfo, CamDataFormat, CamDataFormatSeparator)`, `.SaveCamDataBinary(FileInfo)`,
`.SaveCamDataPointList(FileInfo, CamDataFormatSeparator, Int32)`, Enums `CamDataFormat` (`MCD`, `PointList`, `Scout`) und
`CamDataFormatSeparator` (`Comma`, `Tab`). Die Operationen gehören zur Einstellungsfamilie (`PlcObjectMethods`, Contracts
`Plc/Settings/PlcSettingsCatalog.Objects.cs`, Engine `PlcSettingsRun.Objects.cs`); der Worker löst Objektpfade mit seinen Locators auf
(`EngineeringObjectResolver`) und gibt der Familie jetzt die gebundene lokale Sitzung mit (`Program.PlcSettings` nutzt `WithActiveContext`).
Die Dienste werden unter den `GetServiceInfos()` des Objekts über den exakten vollen Namen gesucht (`ObjectIdentifierProvider`: die des
gebundenen Engineering-Roots); wie `CamDataSupport` und `PlcTagProvider` erreicht werden, steht nicht in den Quellen. Schnittstellenmethoden
werden über die dokumentierte `Siemens.Engineering`-Schnittstelle aufgerufen.

Objektpfade (exakte Namen): `project`, `plc:PLC`, `block:BAUSTEINPFAD`, `type:TYPPFAD`, `tagTable:PLC/TABELLE` (eindeutig über den Namen in
der PLC), `device:GERÄT`, `deviceItem:GERÄT/BAUGRUPPE` (eindeutig unterhalb des Geräts), `technologyObject:PLC/NAME`,
`channel:GERÄT/BAUGRUPPE/TYP/IOTYP/NUMMER` (wie `Channel.Type`, `IoType`, `Number` gelesen werden). Nicht gefunden ist `target_not_found`,
mehrere Treffer `target_ambiguous`. Ein nicht gelisteter Dienst lässt den Abschnitt der Operation `offered: false` mit `unavailable` und ohne
`owner` (K92: ein Besitzer an einem solchen Abschnitt ließ den Host die ganze Antwort als `protocol_error` verwerfen).

| Operation | Felder | Fähigkeit, Guards | Siemens-Aufrufe | Ergebnis / Rücklesen |
| --- | --- | --- | --- | --- |
| `plc_read get_markings` | keine | `Observe`; eine gebundene lokale Multiuser-Sitzung (ein Einzelprojekt lässt den Eintrag mit `unsupported_capability` scheitern, K92) | `LocalSession.MarkingService.GetMarkings()` | `AllMarkings`, `ConflictedMarkings`: Objekt, `MarkState` |
| `plc_read get_mark_state` | `objectPath` | wie oben | `GetMarkStateInfo`, `IsMarkable` | `MarkState`, `IsMarkable`, `IsMarkableCheck` |
| `plc_write mark_objects`, `unmark_objects` | `objectPaths` (1-100, ohne Wiederholung) | `ProjectMutation`, Info-Guard `plc_changes_markings`; ein Einzelprojekt ist `unsupported_capability` | `MarkObjects`/`UnmarkObjects` mit einer Liste des dokumentierten Elementtyps | `MarkState` jedes Objekts zurückgelesen: `IsMarkedByMe` nach dem Markieren, etwas anderes nach dem Entmarkieren (sonst `postcondition_failed`). Nichts wird eingecheckt |
| `plc_read get_object_identifier` | `objectPath` | `Observe` | `ObjectIdentifierProvider.GetIdentifier` | `Identifier` |
| `plc_read find_object_by_identifier` | `name` (die Kennung) | `Observe` | `ObjectIdentifierProvider.Find(String)` | `Found`, das Objekt |
| `plc_read get_creation_infos` | `objectPath`, optional `name` (eine Komposition) | `Observe` | `GetCompositionInfos()`, `GetCreationInfos(name)` je Komposition (höchstens 100); mit `name` auch `GetComposition(name)` und deren `GetCreationInfos()` | je Komposition ihr `Type` (`EngineeringCompositionInfo.Type`) und jeder erzeugbare Typ mit seinen Parametern (Pflicht, unterstützte Typen): das allgemeine „was kann hier erzeugt werden“. Die Liste kann leer sein: unter V21 nannte `plc:PLC` die sieben Member `BlockGroup`, `ExternalSourceGroup`, `PlcAlarmTextlistGroup`, `TagTableGroup`, `TechnologicalObjectGroup`, `TypeGroup`, `WatchAndForceTableGroup`, jeden ohne Erzeugungsinfos (live 2026-11-07, K94; es sind einzelne Systemgruppen von `PlcSoftware`, keine Kompositionen vieler Objekte, was der Grund sein kann; Ursache nicht bestätigt). Eine leere Liste wird so weitergegeben, wie TIA Portal sie liefert |
| `plc_read get_linked_tags` | `objectPath` `channel:...` | `Observe` | `PlcTagProvider.GetLinkedTags()` des Kanals | `Name`, `LogicalAddress`, `DataTypeName`, `TagTable` jeder PLC-Variablen |
| `plc_write save_cam_data` | `objectPath` `technologyObject:...`, `filePath`; optional `camFormat` (`MCD` Standard, `Scout`, `PointList` mit `samplePoints` 1-1.000.000, `Binary`), `camSeparator` (`Comma` Standard, `Tab`), `overwrite` | `ProjectMutation` (eine Aufruferdatei); mit `overwrite:true` Info-Guard `plc_export_overwrites_file` | `SaveCamData` / `SaveCamDataPointList` / `SaveCamDataBinary` in eine bereitgestellte Datei neben dem Ziel | `file`: an den Zielort verschoben und neu gehasht |
| `plc_write load_cam_data` | `objectPath`, `filePath` (eine vorhandene Datei); optional `camFormat` (`MCD`, `Scout`, `Binary`), `camSeparator` | `ProjectMutation`, Info-Guard `plc_loads_cam_data` | `LoadCamData(FileInfo, CamDataFormatSeparator)` / `LoadCamDataBinary(FileInfo)` | trusted: kein dokumentierter Member vergleicht die Kurvenscheibe mit der Datei (zur Prüfung erneut exportieren) |

Hier ausgelassen: `IEngineeringObject.Create`/`IEngineeringComposition.Create(Type, ...)` (das allgemeine Erzeugen; die typisierten Erzeugungen
bleiben bei den Fachoperationen), das allgemeine `Invoke` und die Interpreterprogramme (`InterpreterProgramSupport`), deren Dienst aus den
Quellen nicht erreichbar ist.

## Hardware-Funktionen (`network_read` / `network_write`)

Beleg: V21 `Siemens.Engineering.Base.xml`: `HW.Node.CreateAndConnectToSubnet(String)` (liefert das `Subnet`), `.DisconnectFromSubnet()`,
`.ConnectedSubnet`; `HW.Features.NetworkInterface.Nodes`, `.IoConnectors`; `HW.IoConnector.GetIoController()`, `HW.IoController.IoSystem`;
`HW.Features.ImConnection.GetPartnerPort()`, `.Connect(DeviceItem)`, `.Disconnect()`; `HW.Features.PcInterfaceAssignment.AssignInterface(PcInterfaceAssignmentMode)`,
`.AssignInterface(PcInterfaceAssignmentMode, DeviceItem)`, `.GetAvailableIPCExpansions()`, `.PcInterfaceAssignmentMode`, `.HardwareResource`,
`.IpcExpansion`, `.SoftwarePlc` (Enum `PcInterfaceAssignmentMode`: `None`, `PcStation`, `SoftwarePlc`); `HW.Features.ModuleDescriptionUpdater.CanUpdate`,
`.UpdateModuleDescription()` (Boolean); `HW.Features.TelecontrolManagement.ExportDataPoints(FileInfo)`, `.ImportDataPoints(FileInfo)` („Path to the
csv file“), `.TelecontrolDataPoints`; `HW.Features.CommunicationManagement.Connections`, `CommunicationConnections.ConnectionComposition.Create<T>(Node
localInterface, DeviceItem partnerTarget, Node partnerInterface)` mit T einem der dokumentierten Typen `S7Connection`, `HmiConnection`, `TcpConnection`,
`UdpConnection`, `IsoOnTcpConnection`, `IsoConnection`, `FdlConnection`, `PtpConnection`; `Connection.ConnectionType`, `.IsValid`, `.LocalSubnetName`,
`.PartnerSubnetName`, `.LocalTarget`, `.PartnerTarget`, `.Delete()`. Familie der Hardware-Baugruppen (`HardwareFeatureMethods`, Contracts
`Network/HardwareModules/HardwareFeatures.cs`, Siemens-freie Engine `HardwareFeatureRun`, gemeinsam mit dem FakeWorker `hardware-modules`,
Worker-Partial `HardwareModuleService.Features.cs`). Jede Funktion wird unter den `GetServiceInfos()` der Ziel-Baugruppe über den exakten vollen
Namen gesucht (nicht gelistet: `unsupported_capability`, nichts geändert); das generische `Create<T>` wird per Reflection mit dem über den exakten
vollen Namen gefundenen dokumentierten Typ gebunden; kein Stub-Member wurde ergänzt. Jeder Schreibvorgang löst den Info-Guard
`network_hardware_feature_changes` aus.

| Operation | Felder | Siemens-Aufrufe | Ergebnis / Rücklesen |
| --- | --- | --- | --- |
| `network_read get_interface_nodes` | `target` (die Schnittstelle) | `NetworkInterface.Nodes`, `Node.ConnectedSubnet` | jeder Knoten mit seinem Subnetz |
| `network_write create_subnet_and_connect_node` | `target`, `newSubnetName`, optional `nodeName` (Pflicht bei mehreren Knoten) | `Node.CreateAndConnectToSubnet(String)` | `ConnectedSubnet` muss das neue Subnetz sein (sonst `postcondition_failed`) |
| `network_write disconnect_node_from_subnets` | `target`, optional `nodeName` | `Node.DisconnectFromSubnet()` | danach kein `ConnectedSubnet` |
| `network_read get_io_controller` | `target` | `IoConnector.GetIoController()` jedes IO-Connectors | der IO-Controller und sein IO-System |
| `network_read get_im_partner_port` | `target` (die Baugruppe der IM mit `ImConnection`) | `ImConnection.GetPartnerPort()` | der Partner-Port |
| `network_write connect_im_port` / `disconnect_im_port` | `target`; `partner` (Verbinden) | `Connect(DeviceItem)` / `Disconnect()` | `GetPartnerPort()` zurückgelesen |
| `network_read get_pc_interface_assignment` | `target` | Eigenschaften, `GetAvailableIPCExpansions()` | Modus, Hardware-Ressource, IPC-Erweiterung, Software-PLC, verfügbare Erweiterungen |
| `network_write assign_pc_interface` | `target`, `assignmentMode`; optional `partner` (die CPU einer Software-PLC) | `AssignInterface(mode)` / `AssignInterface(mode, DeviceItem)` | `PcInterfaceAssignmentMode` zurückgelesen |
| `network_read get_module_description_update` | `target` | `ModuleDescriptionUpdater.CanUpdate` | `CanUpdate` |
| `network_write update_module_description` | `target` | `UpdateModuleDescription()` | `false` ist `postcondition_failed`; `CanUpdate` danach gemeldet |
| `network_write export_telecontrol_data_points` | `target`, `filePath`; optional `overwrite` | `TelecontrolManagement.ExportDataPoints(FileInfo)` in eine bereitgestellte Datei | an den Zielort verschoben und neu gehasht (eine vorhandene Datei braucht `overwrite:true`) |
| `network_write import_telecontrol_data_points` | `target`, `filePath` (vorhanden) | `ImportDataPoints(FileInfo)` einer privaten Kopie (gehasht) | Anzahl der Datenpunkte vorher und nachher; TIA Portal entscheidet, was sich ändert |
| `network_read list_communication_connections` | `target` (die Baugruppe mit `CommunicationManagement`) | `Connections` | jede Verbindung (0-basierter Index): Name, Typ, Gültigkeit, lokales und Partnerziel und Subnetz |
| `network_write create_communication_connection` | `target` (lokale Schnittstelle), `partner` (Partnerschnittstelle), `connectionType`; optional `nodeName`, `partnerNodeName` | `Connections.Create<T>(Node, DeviceItem, Node)` | eine Verbindung mehr, beschrieben |
| `network_write delete_communication_connection` | `target`, `connectionIndex`; optional `connectionName` (zuerst geprüft: `state_changed`) | `Connection.Delete()` | danach nicht mehr vorhanden |

Hier ausgelassen: `TelecontrolDataPointComposition.Create<T>(IEngineeringObject tag)` (der Datenpunkt braucht ein PLC-Variablen- oder DB-Member-Objekt,
das die Netzwerk-Tools nicht auflösen; stattdessen eine Datenpunktdatei importieren), weitere Attribute einer Verbindung (`inspect_network_object`).
Nicht live getestet: welche Baugruppe einer echten Station welche Funktion anbietet.

## WinCC-Unified-Bilder, Archivdauern, OPC-UA-Alarmtypen (`hmi_write`)

Beleg: V21 `Siemens.Engineering.WinCCUnified.xml`: `UI.Screens.HmiScreen.ResizeScreen()` („Resize screen as per device display size“);
`HmiLogging.HmiLoggingCommon.LogDuration.SetLogDuration(UInt32 days, UInt32 hours, UInt32 minutes, UInt32 seconds, UInt32 ticks)`,
`.GetStringLogDuration()`, `.Days`/`.Hours`/`.Minutes`/`.Seconds`/`.Ticks`, ebenso `SegmentDuration.SetSegmentDuration`/`GetStringSegmentDuration`
(`Settings.LogTimePeriod` und `Segment.SegmentTimePeriod` eines Archivs; Archive aus `HmiSoftware.DataLogs`/`AlarmLogs`); `HmiSoftware.OpcUaAlarmTypes`,
`HmiOpcUaAlarm.HmiOpcUaAlarmType.SetNodeIdAndConnection(String nodeId, String connectionName)`. In die WinCC-Unified-Konfigurationsfamilie aufgenommen
(`HmiUnifiedConfigurationMethods`, Worker `Openness/HmiExtensions/HmiUnifiedConfiguration.cs`, Host `HmiWorkingState.Configuration.cs`).

| Operation | Felder | Siemens-Aufrufe | Rücklesen |
| --- | --- | --- | --- |
| `resize_screen` | `screenName`, optional `groupPath` | `HmiScreen.ResizeScreen()` | Breite und Höhe vorher und nachher, in `appliedProperties` |
| `set_log_duration` | `objectName` (das Archiv), `propertyName` (`DataLog.LogTimePeriod`, `DataLog.SegmentTimePeriod`, `AlarmLog.LogTimePeriod`, `AlarmLog.SegmentTimePeriod`), `value` (`Tage:Stunden:Minuten:Sekunden[:Ticks]`, Ticks in hundert Nanosekunden) | `SetLogDuration` / `SetSegmentDuration` | jeder Teil zurückgelesen (bei Abweichung `postcondition_failed`), die Textform vorher und nachher |
| `set_opcua_alarm_type_node` | `objectName` (der Alarmtyp), `value` (Node-ID), `connection` | `HmiOpcUaAlarmType.SetNodeIdAndConnection` | `NodeId`, wenn der Alarmtyp sie anbietet, sonst als nicht verfügbar gemeldet |

Hier ausgelassen: `PlantViewComposition.FindPlantViewNode(String hierarchyPath)` (das Format des Hierarchiepfads ist nicht dokumentiert; die
Anlagensicht-Schreibvorgänge lösen Knoten weiter über die dokumentierten `PlantViewNodes` auf), `OpcUaAlarm.GetNodeId(String)` (kein dokumentierter
Member liefert ein `OpcUaAlarm`), `HmiCustomWebControlContainerEventHandlerComposition.GetValidEnumValues()` / `HmiCustomWidgetContainerEventHandlerComposition
.GetValidEnumValues()` und `HmiCustomControlInterfaceComposition.CanCreate()` (Custom-Web-Control- und Widget-Container bilden die Bildobjekt-Lesezugriffe
noch nicht ab), das generische `Create<T>` von `HmiScreenItemBaseComposition`, `DynamizationBaseComposition` und `MappingTableEntryBaseComposition`
(die typnamenbasierten Erzeugungen des Bild-Schreibers decken Bildobjekte und Dynamisierungen ab).

## Ausgelassen, und warum

| Member | Grund |
| --- | --- |
| `AttachingEventArgs.GrantAccess` | Würde den Server den Openness-Zugriff auf TIA Portal selbst erlauben lassen und den Zustimmungsdialog des Benutzers umgehen. Eine Opt-in-Lösung für den Eigentümer wurde am 2026-11-03 angefragt; der Bau wurde vom Berechtigungssystem als Sicherheitsabschwächung gestoppt, daher nicht umgesetzt. |
| `TiaPortal.Finalize`, `TiaPortal.GetProcess` | Prozess-Infrastruktur, keine Engineering-Funktion. |
| `ProjectBase.ShowHwEditor` | Öffnet nur einen Editor in der Oberfläche. |
| `Siemens.Engineering.AddIn.*` | Für TIA-Portal-Add-Ins, die in TIA Portal laufen, nicht für einen Openness-Client. |
| `ProjectServer.AddProjectToServer`, `.CreateLocalSession`, `.DeleteLocalSessionFromServer`, `.DeleteConnection`, `.SetHostName`, `.SetPort`, `.SetProtocol`, `ProjectServerGroup.AddProjectToServer` | Nach Paket G als neues Portal-Tool `project_server_write` gebaut (Entscheidung des Eigentümers vom 2026-11-09; nicht live getestet), zusammen mit `ProjectServerComposition.Create`; siehe die [Project-Server-Referenz](PROJECT_SERVER_WRITE_SUMMARY.de.md). `bind_project` bleibt lesend. |
| `Upload.ParameterUploadProvider.ParameterUpload(IConfiguration, ConfigurationAddress, UploadConfigurationDelegate)` | Welches Objekt den Provider anbietet, ist nicht dokumentiert, und sein `IConfiguration`-Argument braucht die Pfadauflösung des Ladens gegen eine Zielschnittstelle einer vorhandenen Station; ohne Live-Belege lässt sich der Aufruf nicht sicher binden. Der Stations-Upload (`plc_station_upload`) bleibt der Weg zum Hochladen. |
| `TeamcenterGateway.*` (`TeamcenterConnectionProvider.Connect`/`ConnectSSO`/`Disconnect`, `TcGatewaySearchAndDownloadProvider.Search`/`Download`, `TcGatewayLockProvider.CheckoutDataset`/`CheckinDataset`/`CancelCheckoutDataset`, `TcGatewayWorkflowProvider.Save*`, `GetTeamcenterCustomAttributes`) | Eine eigene PLM-Anbindung (Server-Anmeldung, Ein-/Auschecken auf dem Teamcenter-Server, Herunterladen in den lokalen Cache): sie braucht eine eigene geschützte Oberfläche und eine Entscheidung des Eigentümers; siehe [Teamcenter-Referenz](TEAMCENTER_OPERATIONS_SUMMARY.de.md). |
| `Siemens.Engineering.WinCC.Extension` | Nur Operatoren von `NullableDateTime`; nichts, was ein MCP-Client aufruft. |
| `UmcServerConfigurator.Synchronize()` | Ersetzt die UMC-Kopie des Projekts durch die des Servers; nicht in der Lückenliste, kann über denselben Anmeldeweg folgen. |
| `TelecontrolDataPointComposition.Create<T>`, `FindPlantViewNode`, `OpcUaAlarm.GetNodeId`, `GetValidEnumValues`/`CanCreate` der Custom-Control-Container, das generische HMI-`Create<T>` | Siehe die Gründe in den Abschnitten oben. |
| `IEngineeringServiceProvider.GetService<T>()` | Nicht verwendet: jeder Dienst von Paket G wird unter `GetServiceInfos()` über den exakten vollen Namen gesucht (das bestehende Muster, das auch den nicht generischen Weg abdeckt, der für manche Dienste null lieferte, K76); die K76-Korrektur selbst gehört zu einer anderen Änderung. |
