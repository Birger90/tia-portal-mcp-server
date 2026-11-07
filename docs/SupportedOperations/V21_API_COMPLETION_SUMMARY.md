# V21 API completion (package G)

**Language:** English · [Deutsch](V21_API_COMPLETION_SUMMARY.de.md)

Status: **not live-tested**. A name-level comparison of the TIA Portal V21 Openness API description (the V21 IntelliSense XML
files) with this server listed documented methods the server did not call yet. This reference describes the ones that were added
to the existing tools, and lists every one that was left out with its reason. Offline tests cover validation, guards, decoding,
verification and the FakeWorker path; no item ran against a real TIA Portal.

Common rules (as everywhere in this server): members not on the compile stubs are bound at runtime by exact name and documented
parameter types (`api_member_missing` before any call, nothing changed); passwords only from password sources; every write is read
back where a documented read exists, otherwise the entry says so honestly (`trusted` or `notVerified`); TIA Portal decides, a
native rejection is reported as `engineering_rejected`.

## Actual values of a running PLC (`plc_write`)

Evidence: V21 `Siemens.Engineering.Step7.xml`, `ValueService.LoadSnapshotAsActualValues()` ("Loads the snapshot as actual values")
and `ValueService.LoadStartValuesAsActualValues()` ("Loads the start values as actual values"). `ValueService` is a service of the
DB's `Interface`, as for `create_db_snapshot` (BetterTIA/watch). Both operations belong to the device data family
(`PlcDeviceDataMethods`, Contracts `Plc/DeviceData`), next to the online master secret.

| Operation | Fields | Capability, guard | Siemens call | Verification |
| --- | --- | --- | --- | --- |
| `load_snapshot_as_actual_values` | `blockPath` of a global or instance DB (the first segment names the PLC); optional `portalConfirmations` | `OnlineControl` (read-write and full; read-only refuses), acknowledge guard `plc_loads_actual_values_online` | the DB's `ValueService` is bound first, then `OnlineProvider.State` must read `Online` (connect first with `plc_tia_online go_online`; otherwise `state_changed`, nothing changed), then `LoadSnapshotAsActualValues()` | `deviceData` notVerified (success false): Openness documents no read of a CPU's actual values |
| `load_start_values_as_actual_values` | as above | as above | `LoadStartValuesAsActualValues()` under the same checks | as above |

The acknowledge guard makes `plc_write` confirm every call with such an item (read-write asks the human with a prompt that names
each DB and PLC; full runs it under policy), like `plc_download` and the online master secret. PLCSIM is allowed: the target is
whatever controller the project's PLC is online with. The process may react to the new values at once; the server cannot undo the
load. Check the values in TIA Portal (monitor the DB online) or with `plc_online_read` over the Web API.

## Project protection and UMC (`plc_read` / `plc_write`, settings family)

Evidence: V21 `Siemens.Engineering.Base.xml`: `ProjectBase.ProtectProject(String administratorUsername, SecureString
administratorUserPassword)` ("Protects the project"); namespace `Siemens.Engineering.Umac`: `UmcUserComposition.CreateOfflineUmcUser(String)`,
`UmcUserGroupComposition.CreateOfflineUmcUserGroup()`, `UmcUserComposition.Create(UmcUserInfo)`, `UmcUserGroupComposition.Create(UmcUserGroupInfo)`,
`UmcServerConfigurator.UmcServer` and `.CheckConsistency()` (Boolean), `UmcServer.GetUserByName(String)` (`UmcUserInfo.Name`),
`.GetUserGroupByName(String)` (`UmcUserGroupInfo.UserGroupName`, `.Description`), event `UmcServer.Authentication`
(`UmcAuthenticationEventArgs.UmcCredentials`: `UmacCredentials.Name`, `.Type` (`UmacUserType.Global`), `.SetPassword(SecureString)`).
They extend the UMAC operations of the [settings family](PLC_SETTINGS_SUMMARY.md) (issue #12 definitions, `PlcSettingsRun.UmacMore.cs`).
How the `UmcServerConfigurator` is obtained is not documented: it is searched among the project's, then the `UmacConfigurator`'s
`GetServiceInfos()`; not found is `unsupported_capability`, nothing changed.

| Operation | Fields | Capability, guards | Siemens calls | Read-back |
| --- | --- | --- | --- | --- |
| `plc_write protect_project` | `userName` (the administrator), `passwordVariable` (a password source) | `ProjectMutation`; acknowledge guard `plc_protects_project` (read-write confirms the call: the protection can lock the user out, and Openness documents no way back), info `plc_changes_project_access` | `ProjectBase.ProtectProject(String, SecureString)` | `EngineeringFunctionRights` not empty (the Siemens security skill: the project is protected) and the administrator found among `ProjectUsers`; no UmacConfigurator afterwards: not read back, with a message |
| `plc_write create_offline_umc_user` | `userName` | `ProjectMutation`, info `plc_changes_project_access` | `UmacConfigurator.UmcUsers.CreateOfflineUmcUser(String)` | `UmcUsers.Find`; live-tested 2026-11-07 (build 986928f, CPU 1516-3 PN/DP V2.9): the user was created and read back |
| `plc_write create_offline_umc_user_group` | optional `name` | as above | `UmcUserGroups.CreateOfflineUmcUserGroup()` (TIA names the group `Group_N`), then `UmcUserGroup.SetName(String)` on the returned group when `name` is given | `Find` of the group's actual name in a freshly read `UmcUserGroups`, and the name read back equals `name`; the result names the group (`Group_N` without `name`). K96: live 2026-11-07 (build 986928f) the former count comparison failed `postcondition_failed` before `SetName`, leaving `Group_1`; the count is no longer used (not live-tested since) |
| `plc_write import_umc_user` | `name`; optional `userKind` (`umcUser` default, `umcUserGroup`), UMC server credentials `userName` + `passwordVariable` (together or neither) | as above, plus info `plc_lowers_cpu_protection` (the user gets the rights of its roles) | `UmcServer.GetUserByName`/`GetUserGroupByName`, then `Create(UmcUserInfo)`/`Create(UmcUserGroupInfo)` | `Find(name)`; a user the server does not know is `target_not_found` |
| `plc_read get_umc_server_user` | `name`; optional `userKind`, credentials | `OnlineObserve` (every mode: contacts the UMC server, changes nothing) | `GetUserByName`/`GetUserGroupByName` | section `umcServerUser`: `Found`, `Name` or `UserGroupName` and `Description` |
| `plc_read check_umc_consistency` | optional credentials | `OnlineObserve` | `UmcServerConfigurator.CheckConsistency()` | section `umcConsistency`: `Consistent` |

UMC server credentials: with `userName` and `passwordVariable`, the worker subscribes to `UmcServer.Authentication` for the call and
answers every request by setting `UmacCredentials.Name`, `Type` = `Global` (when writable) and `SetPassword(SecureString)`; the
password is resolved right before the call and disposed after it, the handler never throws into TIA Portal, and the result says
how many requests were answered. Without credentials TIA Portal authenticates on its own (for example single sign-on) or refuses.
Left out: `UmcServerConfigurator.Synchronize()` (it replaces the project's UMC copy; not in the gap list, may follow), and making a
protected project unprotected (no documented member).

## Engineering objects by path: markings, identifiers, creation infos, linked tags, cam data (`plc_read` / `plc_write`)

Evidence: V21 `Siemens.Engineering.Base.xml`: `Multiuser.LocalSession.MarkingService`; `MarkingService.GetMarkings()`
(`Markings.AllMarkings`, `.ConflictedMarkings`; `Marking.MarkedObject`, `.MarkState`), `.GetMarkStateInfo(IEngineeringObject)`
(`MarkStateInfo.IsMarkable`, `.MarkState`), `.IsMarkable(IEngineeringObject)`, `.MarkObjects(IEnumerable<IEngineeringObject>)`,
`.UnmarkObjects(...)`, enum `MarkState` (`None`, `IsMarkedByMe`, `IsMarkedByOthers`, `IsUptoDate`); `ObjectIdentifierProvider.GetIdentifier(IEngineeringObject)`
and `.Find(String)`; `IEngineeringObject.GetCompositionInfos()` (`EngineeringCompositionInfo.Name`, `.Type`), `.GetCreationInfos(String)`,
`.GetComposition(String)`, `IEngineeringComposition.GetCreationInfos()` (`EngineeringCreationInfo.Type`, `.ParameterInfos`;
`EngineeringCreationParameterInfo.Name`, `.IsMandatory`, `.SupportedTypes`); `Siemens.Engineering.Step7.xml`: `SW.PlcTagProvider.GetLinkedTags()`
("Service provider for obtaining Plc Tag from Channel"), `Motion.CamDataSupport.LoadCamData(FileInfo, CamDataFormatSeparator)`,
`.LoadCamDataBinary(FileInfo)`, `.SaveCamData(FileInfo, CamDataFormat, CamDataFormatSeparator)`, `.SaveCamDataBinary(FileInfo)`,
`.SaveCamDataPointList(FileInfo, CamDataFormatSeparator, Int32)`, enums `CamDataFormat` (`MCD`, `PointList`, `Scout`) and
`CamDataFormatSeparator` (`Comma`, `Tab`). The operations belong to the settings family (`PlcObjectMethods`, Contracts
`Plc/Settings/PlcSettingsCatalog.Objects.cs`, engine `PlcSettingsRun.Objects.cs`); the worker resolves object paths with its locators
(`EngineeringObjectResolver`) and now passes the bound local session to the family (`Program.PlcSettings` uses `WithActiveContext`).
The services are found among the object's `GetServiceInfos()` by exact full name (`ObjectIdentifierProvider`: the bound engineering
root's); how `CamDataSupport` and `PlcTagProvider` are reached is not stated in the sources. Interface methods are called through the
documented `Siemens.Engineering` interface.

Object paths (exact names): `project`, `plc:PLC`, `block:BLOCKPATH`, `type:TYPEPATH`, `tagTable:PLC/TABLE` (unique by name in the
PLC), `device:DEVICE`, `deviceItem:DEVICE/ITEM` (unique below the device), `technologyObject:PLC/NAME`,
`channel:DEVICE/ITEM/TYPE/IOTYPE/NUMBER` (as `Channel.Type`, `IoType`, `Number` read). Not found is `target_not_found`, several matches
`target_ambiguous`. A service that is not listed leaves the operation's section `offered: false` with `unavailable` and no `owner`
(K92: an owner on such a section made the host reject the whole payload as `protocol_error`).

| Operation | Fields | Capability, guards | Siemens calls | Result / read-back |
| --- | --- | --- | --- | --- |
| `plc_read get_markings` | none | `Observe`; a bound Multiuser local session (a standalone project fails the item with `unsupported_capability`, K92) | `LocalSession.MarkingService.GetMarkings()` | `AllMarkings`, `ConflictedMarkings`: object, `MarkState` |
| `plc_read get_mark_state` | `objectPath` | as above | `GetMarkStateInfo`, `IsMarkable` | `MarkState`, `IsMarkable`, `IsMarkableCheck` |
| `plc_write mark_objects`, `unmark_objects` | `objectPaths` (1-100, no repeats) | `ProjectMutation`, info guard `plc_changes_markings`; a standalone project is `unsupported_capability` | `MarkObjects`/`UnmarkObjects` with a list of the documented element type | every object's `MarkState` read back: `IsMarkedByMe` after a mark, anything else after an unmark (`postcondition_failed` otherwise). Nothing is checked in |
| `plc_read get_object_identifier` | `objectPath` | `Observe` | `ObjectIdentifierProvider.GetIdentifier` | `Identifier` |
| `plc_read find_object_by_identifier` | `name` (the identifier) | `Observe` | `ObjectIdentifierProvider.Find(String)` | `Found`, the object |
| `plc_read get_creation_infos` | `objectPath`, optional `name` (one composition) | `Observe` | `GetCompositionInfos()`, `GetCreationInfos(name)` per composition (at most 100); with `name` also `GetComposition(name)` and its `GetCreationInfos()` | per composition its `Type` (`EngineeringCompositionInfo.Type`) and each creatable type with its parameters (mandatory, supported types): the generic "what can be created here". The list may be empty: on V21, `plc:PLC` listed the seven members `BlockGroup`, `ExternalSourceGroup`, `PlcAlarmTextlistGroup`, `TagTableGroup`, `TechnologicalObjectGroup`, `TypeGroup`, `WatchAndForceTableGroup`, each with no creation infos (live 2026-11-07, K94; these are single system groups of `PlcSoftware`, not compositions of many objects, which may be why; cause not confirmed). An empty list is passed on as TIA Portal returned it |
| `plc_read get_linked_tags` | `objectPath` `channel:...` | `Observe` | the channel's `PlcTagProvider.GetLinkedTags()` | `Name`, `LogicalAddress`, `DataTypeName`, `TagTable` of each PLC tag |
| `plc_write save_cam_data` | `objectPath` `technologyObject:...`, `filePath`; optional `camFormat` (`MCD` default, `Scout`, `PointList` with `samplePoints` 1-1,000,000, `Binary`), `camSeparator` (`Comma` default, `Tab`), `overwrite` | `ProjectMutation` (a caller file); with `overwrite:true` info guard `plc_export_overwrites_file` | `SaveCamData` / `SaveCamDataPointList` / `SaveCamDataBinary` into a staged file next to the target | `file`: moved into place and re-hashed |
| `plc_write load_cam_data` | `objectPath`, `filePath` (an existing file); optional `camFormat` (`MCD`, `Scout`, `Binary`), `camSeparator` | `ProjectMutation`, info guard `plc_loads_cam_data` | `LoadCamData(FileInfo, CamDataFormatSeparator)` / `LoadCamDataBinary(FileInfo)` | trusted: no documented member compares the cam data with the file (export again to check) |

Left out here: `IEngineeringObject.Create`/`IEngineeringComposition.Create(Type, ...)` (the generic create; the typed creates stay with the
domain operations), the generic `Invoke`, and the interpreter program support (`InterpreterProgramSupport`), whose service is not
reachable from the sources.

## Hardware features (`network_read` / `network_write`)

Evidence: V21 `Siemens.Engineering.Base.xml`: `HW.Node.CreateAndConnectToSubnet(String)` (returns the `Subnet`), `.DisconnectFromSubnet()`,
`.ConnectedSubnet`; `HW.Features.NetworkInterface.Nodes`, `.IoConnectors`; `HW.IoConnector.GetIoController()`, `HW.IoController.IoSystem`;
`HW.Features.ImConnection.GetPartnerPort()`, `.Connect(DeviceItem)`, `.Disconnect()`; `HW.Features.PcInterfaceAssignment.AssignInterface(PcInterfaceAssignmentMode)`,
`.AssignInterface(PcInterfaceAssignmentMode, DeviceItem)`, `.GetAvailableIPCExpansions()`, `.PcInterfaceAssignmentMode`, `.HardwareResource`,
`.IpcExpansion`, `.SoftwarePlc` (enum `PcInterfaceAssignmentMode`: `None`, `PcStation`, `SoftwarePlc`); `HW.Features.ModuleDescriptionUpdater.CanUpdate`,
`.UpdateModuleDescription()` (Boolean); `HW.Features.TelecontrolManagement.ExportDataPoints(FileInfo)`, `.ImportDataPoints(FileInfo)` ("Path to the
csv file"), `.TelecontrolDataPoints`; `HW.Features.CommunicationManagement.Connections`, `CommunicationConnections.ConnectionComposition.Create<T>(Node
localInterface, DeviceItem partnerTarget, Node partnerInterface)` with T one of the documented `S7Connection`, `HmiConnection`, `TcpConnection`,
`UdpConnection`, `IsoOnTcpConnection`, `IsoConnection`, `FdlConnection`, `PtpConnection`; `Connection.ConnectionType`, `.IsValid`, `.LocalSubnetName`,
`.PartnerSubnetName`, `.LocalTarget`, `.PartnerTarget`, `.Delete()`. Hardware-module family (`HardwareFeatureMethods`, Contracts
`Network/HardwareModules/HardwareFeatures.cs`, Siemens-free engine `HardwareFeatureRun` shared with the FakeWorker `hardware-modules`,
worker partial `HardwareModuleService.Features.cs`). Every feature is found among the target device item's `GetServiceInfos()` by exact full
name (not listed: `unsupported_capability`, nothing changed); generic `Create<T>` is bound by reflection with the documented type found by
exact full name; no stub member was added. Every write fires the info guard `network_hardware_feature_changes`.

| Operation | Fields | Siemens calls | Result / read-back |
| --- | --- | --- | --- |
| `network_read get_interface_nodes` | `target` (the interface) | `NetworkInterface.Nodes`, `Node.ConnectedSubnet` | each node with its subnet |
| `network_write create_subnet_and_connect_node` | `target`, `newSubnetName`, optional `nodeName` (required when the interface has several nodes) | `Node.CreateAndConnectToSubnet(String)` | `ConnectedSubnet` must be the new subnet (`postcondition_failed` otherwise) |
| `network_write disconnect_node_from_subnets` | `target`, optional `nodeName` | `Node.DisconnectFromSubnet()` | no `ConnectedSubnet` afterwards |
| `network_read get_io_controller` | `target` | `IoConnector.GetIoController()` of each IO connector | the IO controller and its IO system |
| `network_read get_im_partner_port` | `target` (the IM module's item with `ImConnection`) | `ImConnection.GetPartnerPort()` | the partner port |
| `network_write connect_im_port` / `disconnect_im_port` | `target`; `partner` (connect) | `Connect(DeviceItem)` / `Disconnect()` | `GetPartnerPort()` read back |
| `network_read get_pc_interface_assignment` | `target` | properties, `GetAvailableIPCExpansions()` | mode, hardware resource, IPC expansion, software PLC, available expansions |
| `network_write assign_pc_interface` | `target`, `assignmentMode`; optional `partner` (the CPU device item for a software PLC) | `AssignInterface(mode)` / `AssignInterface(mode, DeviceItem)` | `PcInterfaceAssignmentMode` read back |
| `network_read get_module_description_update` | `target` | `ModuleDescriptionUpdater.CanUpdate` | `CanUpdate` |
| `network_write update_module_description` | `target` | `UpdateModuleDescription()` | `false` is `postcondition_failed`; `CanUpdate` afterwards reported |
| `network_write export_telecontrol_data_points` | `target`, `filePath`; optional `overwrite` | `TelecontrolManagement.ExportDataPoints(FileInfo)` into a staged file | moved into place and re-hashed (an existing file needs `overwrite:true`) |
| `network_write import_telecontrol_data_points` | `target`, `filePath` (existing) | `ImportDataPoints(FileInfo)` of a private copy (hashed) | data point counts before and after; TIA Portal decides what changes |
| `network_read list_communication_connections` | `target` (the item with `CommunicationManagement`) | `Connections` | each connection (0-based index order): name, type, validity, local and partner target and subnet |
| `network_write create_communication_connection` | `target` (local interface), `partner` (partner interface), `connectionType`; optional `nodeName`, `partnerNodeName` | `Connections.Create<T>(Node, DeviceItem, Node)` | one more connection, described |
| `network_write delete_communication_connection` | `target`, `connectionIndex`; optional `connectionName` (checked first: `state_changed`) | `Connection.Delete()` | gone afterwards |

Left out here: `TelecontrolDataPointComposition.Create<T>(IEngineeringObject tag)` (the data point needs a PLC tag or DB member object the
network tools do not resolve; import a data point file instead), the connection's further attributes (use `inspect_network_object`).
Not live-tested: which device item of a real station offers each feature.

## WinCC Unified screens, log durations, OPC UA alarm types (`hmi_write`)

Evidence: V21 `Siemens.Engineering.WinCCUnified.xml`: `UI.Screens.HmiScreen.ResizeScreen()` ("Resize screen as per device display size");
`HmiLogging.HmiLoggingCommon.LogDuration.SetLogDuration(UInt32 days, UInt32 hours, UInt32 minutes, UInt32 seconds, UInt32 ticks)`,
`.GetStringLogDuration()`, `.Days`/`.Hours`/`.Minutes`/`.Seconds`/`.Ticks`, the same for `SegmentDuration.SetSegmentDuration`/`GetStringSegmentDuration`
(a log's `Settings.LogTimePeriod` and `Segment.SegmentTimePeriod`; logs from `HmiSoftware.DataLogs`/`AlarmLogs`); `HmiSoftware.OpcUaAlarmTypes`,
`HmiOpcUaAlarm.HmiOpcUaAlarmType.SetNodeIdAndConnection(String nodeId, String connectionName)`. Added to the WinCC Unified configuration family
(`HmiUnifiedConfigurationMethods`, worker `Openness/HmiExtensions/HmiUnifiedConfiguration.cs`, host `HmiWorkingState.Configuration.cs`).

| Operation | Fields | Siemens calls | Read-back |
| --- | --- | --- | --- |
| `resize_screen` | `screenName`, optional `groupPath` | `HmiScreen.ResizeScreen()` | width and height before and after, in `appliedProperties` |
| `set_log_duration` | `objectName` (the log), `propertyName` (`DataLog.LogTimePeriod`, `DataLog.SegmentTimePeriod`, `AlarmLog.LogTimePeriod`, `AlarmLog.SegmentTimePeriod`), `value` (`days:hours:minutes:seconds[:ticks]`, ticks in hundred nanoseconds) | `SetLogDuration` / `SetSegmentDuration` | each part read back (`postcondition_failed` on a difference), the string form before and after |
| `set_opcua_alarm_type_node` | `objectName` (the alarm type), `value` (node id), `connection` | `HmiOpcUaAlarmType.SetNodeIdAndConnection` | `NodeId` when the alarm type exposes it, otherwise reported unavailable |

Left out here: `PlantViewComposition.FindPlantViewNode(String hierarchyPath)` (the hierarchy path format is not documented; the plant
view writes keep resolving nodes by walking the documented `PlantViewNodes`), `OpcUaAlarm.GetNodeId(String)` (no documented member returns
an `OpcUaAlarm`), `HmiCustomWebControlContainerEventHandlerComposition.GetValidEnumValues()` / `HmiCustomWidgetContainerEventHandlerComposition
.GetValidEnumValues()` and `HmiCustomControlInterfaceComposition.CanCreate()` (custom web control and widget containers are not modelled by the
HMI screen-item reads yet), the generic `Create<T>` of `HmiScreenItemBaseComposition`, `DynamizationBaseComposition` and
`MappingTableEntryBaseComposition` (the type-name based creates of the screen writer cover screen items and dynamizations).

## Left out, and why

| Member | Reason |
| --- | --- |
| `AttachingEventArgs.GrantAccess` | Would let the server approve Openness access to TIA Portal itself and bypass the user's consent dialog. An owner opt-in was requested on 2026-11-03; building it was stopped by the permission system as a security weakening, so it is not implemented. |
| `TiaPortal.Finalize`, `TiaPortal.GetProcess` | Process plumbing, no engineering function. |
| `ProjectBase.ShowHwEditor` | Opens a UI editor only. |
| `Siemens.Engineering.AddIn.*` | For TIA Portal add-ins running inside TIA Portal, not for an Openness client. |
| `ProjectServer.AddProjectToServer`, `.CreateLocalSession`, `.DeleteLocalSessionFromServer`, `.DeleteConnection`, `.SetHostName`, `.SetPort`, `.SetProtocol`, `ProjectServerGroup.AddProjectToServer` | Project Server administration acts on the Portal and the server, not on a bound project: every existing write tool requires a verified binding (`library_write` even a standalone one), and `bind_project` is permitted in every mode, so it must stay read-only. Doing these right needs a Portal-level guarded lifecycle write surface (confirmation per call, no binding, read back through `list_server_projects`/`list_local_sessions`/`list_server_connections`), which is a new tool or a new lifecycle action and so an owner decision on the tool surface. Not built in package G. |
| `Upload.ParameterUploadProvider.ParameterUpload(IConfiguration, ConfigurationAddress, UploadConfigurationDelegate)` | Which object offers the provider is not documented, and its `IConfiguration` argument needs the download path resolution against a target interface of an existing station; without live evidence the call cannot be bound safely. The station upload (`plc_station_upload`) stays the upload path. |
| `TeamcenterGateway.*` (`TeamcenterConnectionProvider.Connect`/`ConnectSSO`/`Disconnect`, `TcGatewaySearchAndDownloadProvider.Search`/`Download`, `TcGatewayLockProvider.CheckoutDataset`/`CheckinDataset`/`CancelCheckoutDataset`, `TcGatewayWorkflowProvider.Save*`, `GetTeamcenterCustomAttributes`) | A separate PLM integration (server credentials, check-in/check-out on the Teamcenter server, downloads into the local cache): it needs its own guarded surface and an owner decision; see the [Teamcenter reference](TEAMCENTER_OPERATIONS_SUMMARY.md). |
| `Siemens.Engineering.WinCC.Extension` | Only `NullableDateTime` operators; nothing an MCP client calls. |
| `UmcServerConfigurator.Synchronize()` | Replaces the project's UMC copy with the server's; not in the gap list, can follow on the same credential path. |
| `TelecontrolDataPointComposition.Create<T>`, `FindPlantViewNode`, `OpcUaAlarm.GetNodeId`, the custom control container `GetValidEnumValues`/`CanCreate`, the HMI generic `Create<T>` | See the reasons in the sections above. |
| `IEngineeringServiceProvider.GetService<T>()` | Not used: every service of package G is found among `GetServiceInfos()` by exact full name (the existing pattern, which also covers the non-generic path that returned null for some services, K76); the K76 fix itself belongs to another change. |
