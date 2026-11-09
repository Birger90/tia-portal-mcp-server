# Project and global library operations (`library_read`, `library_write`)

**Language:** English · [Deutsch](LIBRARY_OPERATIONS_SUMMARY.de.md)

This reference covers the two library tools this fork adds:

- `library_read` (every access mode): the project library and the open global libraries, global library
  descriptors, the type/master-copy tree with paging, type details, type instances, the library type a PLC
  block or data type is an instance of, the update check and the library compare;
- `library_write` (read-write and full): global library lifecycle (open, retrieve, create, save, save as, archive, close),
  master copies (capture, copy, delete, rename, instantiate into a PLC or the hardware configuration), type
  versions (instantiate, update the project, edit, release, discard, set default, delete) and renames of
  types and type folders; since issue #17 also clean-up, harmonize, library update, type deletion and DoNotUse, type and
  master copy folders, types and versions from documents, type version exports and detailed compares of types, versions and
  master copies ([below](#operations-added-from-the-v21-api-description-issue-17)).

> **Status: not live-tested.** These features are offline-qualified only: built against the compile-only
> reference stubs, unit-tested on the Siemens-free logic (`LibraryPolicy`, `LibraryRuntimeBinding`,
> catalogs, planning and guards) and exercised end to end through the scripted FakeWorker
> (`library-roundtrip`). Nothing here has run against TIA Portal. The mechanical signature check is
> `scripts/verify-reference-stubs.ps1` (which compiles the probe
> `reference-stubs/TiaMcpServer.OpennessReferenceProbe/LibraryReferenceSurface.cs`) plus a Windows build of
> the worker and the probe against the installed `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`.
> Most library members are bound at runtime (see [provenance](#provenance-of-every-siemens-member-used)); each
> fails closed with `unsupported_capability` when the installed V21 does not expose it, and every one must be
> exercised by the [live acceptance checks](#live-acceptance-checks) before the tools are relied on.

Tool counts: 13 tools in read-only, 33 in read-write and 33 in full since `project_server_write` (13/32/32 since `siemens_docs`, 12/31/31 since `tia_inbox`, 11/30/30 before; `library_read` is the eleventh
read-only tool, `library_write` the twenty-ninth writable one).

## Why two tools

`library_read`/`library_write` follow the `vci_read`/`vci_write` split: one read tool with independent items
(a failed item does not stop the others) and one guarded write tool with ordered items that stops at the first
failure without rollback. Global library lifecycle items stay in `library_write` instead of a separate
lifecycle tool, so a call can open a library and work in it; the domain confirms such a call once (below).

## Access and confirmation

The owner decision for this fork: the AI may do everything the PLC engineer can do in TIA Portal, controlled
by the access mode. Read-only is observation; read-write edits like the other domain writes (no prompt) and
confirms lifecycle actions through elicitation; full runs everything without prompts. Nothing is
hard-blocked except invalid or ambiguous input: every `library_write` guard is informative, including
`library_close_unsaved_changes`, which names the unsaved changes this server made that a close discards
([below](#closing-a-global-library); a block until 2026-11-01, relaxed because TIA Portal lets a user close without saving).

| Operation / tool | Worker method | Capability | read-only | read-write | full |
| --- | --- | --- | --- | --- | --- |
| `library_read` (all fourteen operations) | `library_<operation>` | `Observe` | yes | yes | yes |
| `library_write` planning | `library_plan` | `Observe` | n/a | yes | yes |
| `library_write` content items (29 operations; every call with a `set_portal_setting` asks, below) | `library_<operation>` | `ProjectMutation` | no (not listed) | yes, no prompt | yes |
| `library_write` lifecycle items: `open_global_library`, `retrieve_global_library`, `create_global_library`, `save_global_library`, `close_global_library`, `archive_global_library`, `save_global_library_as`, `open_secondary_project`, `retrieve_secondary_project`, `close_secondary_project` | `library_<operation>` | `ProjectMutation` | no (not listed) | yes, **one prompt per actual call that contains one** | yes, policy |

Lifecycle items create, extract, open (read-write or with upgrade) or overwrite library files outside the
project; the Siemens source requires explicit authorization for them, so a call containing one is confirmed
once in read-write (`ConfirmsEveryCall` is true for exactly those calls), like `open_project`/`save_project`.
The prompt states the effect first (the call opens, creates, retrieves, saves or closes global library files
outside the project), then one line per lifecycle item: the operation, its resolved target, the open mode it
requests (`OpenWithUpgrade: may upgrade the library file in place on disk`, `OpenMode ReadWrite: save_global_library
overwrites the library file` or `OpenMode ReadOnly`, for open and retrieve), its `portalConfirmations` policy and its
`operationId`, every caller value quoted and escaped with the shared `ConfirmationText` (targets embed caller paths,
so no caller text can add a line or reorder the prompt); it then states how many content items run in the same call.
A lifecycle item that depends on an earlier item of the call (for example `close_global_library` X, then
`open_global_library` X with `upgrade:true`) has no plan until after the confirmation, so its line is built from the
item itself (path, archive and target directory, new name and directory, or library) and says that it is resolved
just before its mutation; the requested mode, an upgrade included, is shown either way. The pipeline shows only
acknowledge guards in a prompt (escaped lines before the description); `library_write` defines none (its guards are
informative and one block), so its prompt is the description alone, and the informative guards (such as
`library_upgrades_in_place`) appear in the response. A call that contains a lifecycle item refuses `operationId`s
with control, separator or format characters (`validation_error`, nothing runs, like every confirming tool): the
pipeline applies that rule right after `Validate` (when `ConfirmsEveryCall` is known) and again immediately before
any prompt; content-only calls never prompt and keep them. A declined, cancelled or unsupported prompt
refuses the call with `access_denied` before anything runs. They are classified `ProjectMutation` rather than `ProjectLifecycle`: they do not open, close
or switch the bound project, and a Portal rejection is `engineering_rejected` like every content write.

### TIA Portal confirmation dialogs (`portalConfirmations`)

Every `library_write` item takes `portalConfirmations`: `decline` (default) or `accept`, with the same rules as
`plc_download`. While the item's mutation runs, every TIA Portal `Confirmation` dialog is answered by that policy
instead of the session-wide answer (Yes in read-write and full) and recorded: the worker dispatch
(`Program.Library`) opens `TiaPortalSession.AnswerConfirmations` through `LibraryConfirmationScope.Open` (owner: the
worker method) with a `PlcDownloadPortalConfirmationLog`, and the service closes the scope before it copies the
record into the result (`portalConfirmationPolicy`,
`portalConfirmations[{ type, text, answer }]` at most 100 listed, `portalConfirmationsObserved`,
`portalConfirmationsComplete`). With `decline` a dialog is answered No and the item **fails**
(`worker_operation_failed`, the result kept, the call stops): declining may have changed what the Siemens call did
(a declined save prompt during a close can discard changes), so inspect with `library_read` and retry with
`accept` when Yes is wanted. With `accept` it is answered Yes (in read-only nothing runs) and the item succeeds with
a warning. A failed item carries the evidence collected until the failure. The host checks the evidence (policy as
requested, list and count consistent, every answer the policy's) and rejects anything else as `protocol_error`.
Planning lists the policy as the item's last consequence, and the read-write prompt names it per lifecycle item.
Which items raise dialogs at all is undocumented; planning (`library_plan`) and `library_read` run without a scope
of their own. After the item returned, its scope lingers with the same answer until the next worker request
starts (`PlcOnlineConfirmationScope.BeginRequest`, as for plc_download): a dialog TIA Portal raises late is never
answered by the session-wide Yes under `decline`, is not in the item's evidence, and is reported as a warning on
that next response (an item's `warnings`, or the root `warnings` when it is a planning response of `library_write`).
`library_write` takes no passwords, so the password redaction of plc_download does not apply.

Both tools require a verified binding to a **standalone** project (`Project.ProjectLibrary` is the documented
entry point; local Multiuser sessions are not delivered, `unsupported_capability`). Global libraries belong to
the TIA Portal session of that binding and stay open across calls until `close_global_library` (or TIA Portal)
closes them. Project-library changes persist through `save_project`; global-library changes only through
`save_global_library`.

## Selectors and paths

| Field | Meaning |
| --- | --- |
| `library` | `project` (exact, lowercase) or the path of an **open** global library exactly as `list_libraries` reports it (compared case-insensitively, `/` and `\` equal, trailing separators ignored) |
| `targetLibrary` | the second library of `copy_master_copy` and `compare_libraries` (same form; must differ from `library`) |
| `folderPath` | type folder below the type folder root: `/` (root) or `/A/B`; segments are matched case-insensitively and must be unique |
| `typePath` / `typeGuid` | a type as `/Folder/Type` (or `Type` in the root type folder), or its GUID (`ILibrary.FindType`) |
| `version` / `versionGuid` | the exact version number text as listed (e.g. `1.0.2`), or the version GUID (`ILibrary.FindVersion`); optional when the type has exactly one version |
| `masterCopyName` | a master copy of the master copy folder `masterCopyFolderPath` (omitted: the root master copy folder; `MasterCopyComposition.Find`) |
| `masterCopyFolderPath` / `targetMasterCopyFolderPath` | a master copy folder below the root: `/` or `/A/B` through `MasterCopyFolder.Folders` (V21), segments case-insensitive and unique |
| `path`, `targetDirectory` | absolute Windows paths (drive letter or UNC share), no `..`, no alternate data streams, no Win32 device or extended-length paths (`\\?\`, `\\.\`, also with `/` such as `\\./COM1` or `\\?/UNC/...`), no segment that is a reserved device name (`NUL`, `CON`, `PRN`, `AUX`, `COM1`-`COM9`, `LPT1`-`LPT9`, with any extension; security review U2); existence is checked by the worker (`target_not_found`) |
| `name` (`create_global_library`) | becomes a directory and file name below `targetDirectory`, so besides the name rules it must be a valid Windows file name: none of `\ / : * ? " < > \|` or control characters (a fixed Windows set, also on a Linux host), no `..`, no trailing `.`, no reserved device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1`-`COM9`, `LPT1`-`LPT9`, also with an extension); `..\..\Temp\Evil` is refused. Object names (`newName`, `masterCopyName`, type and folder names) are not file names: they are nonblank, at most 256 characters, unpadded, without control characters or `/` |

Enum options carry the exact Siemens member names: `openMode` `ReadOnly` (default) or `ReadWrite`;
`masterCopyMode` `ThrowIfExists` (default), `Rename`, `Replace`; `updatePathsMode` `ThrowIfPathsConflict`
(default, as the source recommends for unattended use), `KeepExistingPathsInTarget`, `UpdatePathsInTarget`;
`dependenciesMode` `DoNotAutomaticallyCreateOrReleaseDependencies` (the only member the sources name);
`updateCheckMode` `ReportOutOfDateOnly` (the only member the sources name). The stub enums declare no values:
the worker parses the name against the live enum and fails closed (`unsupported_capability`, listing the live
members) when it is absent.

## `library_read` operations

1-50 independent items, each `{ operationId, operation, ... }`; only the listed fields are accepted.

| Operation | Fields | Result |
| --- | --- | --- |
| `list_libraries` | none | `{ libraries[{ library, kind, path, name, runtimeType }], unreadable }`: the project library (`kind: project`) and every open global library (`user`, `system`, `corporate`, or `global` for another specialization) |
| `list_global_library_infos` | none | `{ infos[{ libraryType, name, path, isOpen }], unreadable }` from `GetGlobalLibraryInfos()`; `isOpen` is path equality with an open library |
| `get_library_tree` | `library`; optional `folderPath`, `content` (`all`/`types`/`masterCopies`), `includeVersions` (default true), `maxDepth` (1-32, default 16), `offset` (default 0), `limit` (1-500, default 200) | one offset/limit page of the flattened tree: `entries[{ kind (typeFolder/type/masterCopy), path, name, depth, guid, typeKind (codeBlock/plcType/other), runtimeType, versions[{ versionNumber, guid, state, isDefault }], contentTypes, unreadable }]`, `total`, `returned`, `complete`, `omission`, `masterCopyScope: "all"` (master copy folders as `kind: masterCopyFolder`; optional `masterCopyFolderPath` starts that walk) |
| `get_type` | `library`, `typePath` or `typeGuid` | `{ library, path, name, guid, typeKind, runtimeType, versions[{ versionNumber, guid, state, isDefault, dependencies[], dependents[], masterCopiesContainingInstances[], originalLibrary, unreadable }] }` |
| `find_type_instances` | `library`, `typePath`/`typeGuid` (all versions) and/or `version`/`versionGuid`; optional `plcName` | `{ library, typeName, typeGuid, plcName, deviceName, versions[{ versionNumber, guid, instances[{ name, runtimeType }], complete }] }` from `FindInstances(PlcSoftware)` |
| `get_instance_info` | `blockPath` or `plcTypePath` (the `plc_read` path grammar) | `{ plcName, objectKind, objectName, linked, typeName, typeGuid, versionNumber, versionGuid, versionState }`; `linked:false` when the object exposes no `LibraryTypeInstanceInfo` (not a type instance, e.g. placed from a master copy) |
| `update_check` | `library`; optional `updateCheckMode` | `{ library, mode, messages[{ description, messageParts[], messages[] }], messageCount, complete }` from `UpdateCheck(project, mode)` against the bound project |
| `compare_libraries` | `library`, `targetLibrary`; optional `maxDepth` (1-64, default 16), `maxElements` (1-5000, default 300) | `{ library, compareTo, root, elementsListed, complete, omission }`; each element `{ comparisonResult, attributes[{ name, value }], details[{ attributes }], elements[], elementsTruncated }` |

The tree is walked depth-first: a folder's types, then each child folder followed by its content. Master copies
follow the types when `folderPath` is the root (or `masterCopyFolderPath` is given): a master copy folder's master copies, then
each child master copy folder followed by its content. The walk stops at 5,000
collected entries or at `maxDepth` (then `complete:false` with `omission`); paging is plain offset/limit over
that walk without a snapshot, so changes between pages can shift entries.

`compare_libraries` projects elements **schema-free**: the sources name `RootElement`, `Elements`,
`ComparisonResult` and `DetailedInformation.Properties` but neither the element identity members nor the
`LibraryCompareResultState` member names, so each element and each detail property lists its readable scalar
attributes (`GetAttributeInfos`/`GetAttribute`, at most 20) and no identical-state filtering is offered.
`update_check` messages are projected the same way from their named members (`Description`, `MessageParts`
as text, nested `Messages`; 8 levels, 500 messages).

**Libraries without a type folder (K82, not live-tested).** Live on V21 `get_library_tree` (content `all`) on the system
library "Documentation templates" failed with an unhandled `TargetException`; most likely its `ILibrary.TypeFolder` is
null. The tree now lists no types for such a library, reports `complete: false` with the reason in `omission` and still
walks the master copies; a `folderPath` below the root fails `target_not_found` ("no type folder"). Every required
library member read on a null owner now fails `worker_operation_failed` with the member name instead of a `TargetException`.

## `library_write` operations

1-50 ordered items; `dryRun:true` plans and evaluates guards without mutation or prompt; omitted or false
executes. The call stops at the first failed item; earlier items stay applied (no rollback, no replay). Every item
also accepts `portalConfirmations` (`decline` default, `accept`; see above).

| Operation | Fields | Siemens call (see provenance) | Notes |
| --- | --- | --- | --- |
| `open_global_library` | `path`; `openMode` or `upgrade:true` | `GlobalLibraries.Open(FileInfo, OpenMode)` / `OpenWithUpgrade(FileInfo)` | an archive (`.zalNN`) is an argument error pointing to `retrieve_global_library`; an already open file (compared by full path) is reported (`alreadyOpen`) and nothing is called; a library the call returns that was open before is reported `alreadyOpen` too; K80 (live 2026-11-03: TIA Portal refused the path of `PrintingSysLib.as21`, "System and Corporate global libraries cannot be opened with a path."): when `GetGlobalLibraryInfos()` lists `path` as a `System` or `Corporate` library, the worker opens it with `GlobalLibraryComposition.Open(GlobalLibraryInfo)` instead (shared decision `GlobalLibraryInfoOpen`, Contracts); that call takes no OpenMode, so `openMode: ReadWrite` and `upgrade:true` are `validation_error` and TIA Portal opens the library read-only; the result's `note` names the call; read back by `list_libraries` like every open (not live-tested). The selector stays the `path` `list_global_library_infos` returns (its name is not a separate selector: paths are what `library` selectors and `list_libraries` use) |
| `retrieve_global_library` | `path` (archive), `targetDirectory`; `openMode` | `GlobalLibraries.Retrieve(FileInfo, DirectoryInfo, OpenMode)` | extracts and opens; the result names the opened library's path |
| `create_global_library` | `targetDirectory`, `name` | `GlobalLibraries.Create<UserGlobalLibrary>(DirectoryInfo, string)` | the directory must exist; `name` must be a valid Windows file name ([selectors](#selectors-and-paths)), checked by the host and again by the worker |
| `save_global_library` | `library` (user global library) | `UserGlobalLibrary.Save()` | system/corporate libraries are read-only (`target_kind_unsupported`) |
| `close_global_library` | `library` (user global library) | `UserGlobalLibrary.Close()` | user global libraries only: the sources document `Close()` for `UserGlobalLibrary` only, so a system or corporate library is refused at planning (`target_kind_unsupported`, nothing runs; close it in TIA Portal); refused while this server's changes are unsaved (below); read back: the library must no longer be listed (`postcondition_failed`) |
| `create_master_copy` | `library`, `source { kind block/plcType/tagTable/device/textList, path, plcName (tagTable and textList only) }` | `MasterCopyFolder.MasterCopies.Create(IMasterCopySource)` | the source must be an `IMasterCopySource` (`target_kind_unsupported` otherwise); root master copy folder; a `textList` path is the user text list name (A: `MasterCopies.Create(alarmTextList)`; system lists are refused) |
| `copy_master_copy` | `library`, `masterCopyName`, `targetLibrary` | `target.MasterCopyFolder.MasterCopies.CreateFrom(MasterCopy)` | e.g. project library → open user global library |
| `delete_master_copy` | `library`, `masterCopyName` | `MasterCopy.Delete()` | read back: `Find(name)` must return nothing |
| `rename_master_copy` | `library`, `masterCopyName`, `newName` | `SetAttribute("Name", newName)` | the master copy's content keeps its names |
| `instantiate_master_copy` | `library`, `masterCopyName`, `target`; `masterCopyMode` (PLC kinds), `newName` | `CreateFrom(MasterCopy, MasterCopyMode)` on the PLC root's block/type/tag-table group (`folderPath`), `TechnologicalObjectGroup.TechnologicalObjects`, `PlcUnitProvider.UnitGroup.Units`, `ExternalSourceGroup.ExternalSources` or `PlcAlarmTextlistGroup.PlcAlarmUserTextlists` (the documented way to create a user text list; see the [PLC alarms and text lists reference](PLC_ALARMS_TEXTLISTS_SUMMARY.md)); `CreateFrom(MasterCopy)` on `Project.Devices` or a parent device item's `DeviceItems` | target kinds `blockGroup`, `typeGroup`, `tagTableGroup`, `technologyObjects`, `softwareUnits`, `externalSources`, `textLists` (PLC), `devices`, `deviceItem` (`parent` = the `network_read` hardware selector); the caller chooses the kind from the master copy's `contentTypes` (no automatic routing); `newName` renames the single created object afterwards (G's rename-after-placement); evidence: `CreateFrom` returning null is `postcondition_failed`, a member bound without a return value or an empty returned collection makes the item `notVerified` |
| `instantiate_type_version` | `library`, type, version, `target { kind blockGroup/typeGroup, plcName, folderPath }`; `updatePathsMode` | `Blocks.CreateFrom(CodeBlockLibraryTypeVersion, UpdatePathsMode)` / `Types.CreateFrom(PlcTypeLibraryTypeVersion, UpdatePathsMode)` | a code-block version goes into a block group, a PLC data type version into a type group (`target_kind_unsupported` otherwise); evidence as for `instantiate_master_copy` (G shows this call without using its value, so a `void` binding is `notVerified`, never trusted) |
| `update_project` | `library`; one of `typePath`/`typeGuid`/`folderPath`; optional `plcName` | `UpdateProject(<type or folder>, <PlcSoftware>)` | TIA Portal's default update behavior (the mode overloads are undocumented); run `update_check` first |
| `rename_type` | `library`, type, `newName` | `SetAttribute("Name", newName)` | read back through `LibraryType.Name` |
| `rename_type_folder` | `library`, `folderPath` (a user folder), `newName` | `SetAttribute("Name", newName)` | the root type folder cannot be renamed |
| `edit_type_version` | `library`, type, version | `LibraryTypeVersion.Edit()` | returns the in-work version (`versionAfter`); edit its instance with `plc_write`, compile, then release; read back: `Edit()` returning nothing, or a returned version whose `State` is not `InWork`, is `postcondition_failed` |
| `release_type_version` | `library`, type, version, `versionNumber`; `author`, `comment`, `dependenciesMode` | `Release(CreateOrReleaseDependenciesMode, Version, string, string)` | `author`/`comment` are the third and fourth arguments (the source's example passes an author and a comment text; their meaning is not documented further); read back on the same version (or found again by GUID with `ILibrary.FindVersion`): `State` must be `Committed` and `VersionNumber` must equal `versionNumber` (compared as versions, missing components 0), else `postcondition_failed` |
| `discard_type_version` | `library`, type, version | `Discard()` | read back: `ILibrary.FindVersion(guid)` must find nothing, or the found version's `State` must differ from before, else `postcondition_failed` |
| `set_default_type_version` | `library`, type, version | `SetAsDefault()` | read back: `IsDefault` must not read false (`postcondition_failed`); an unreadable `IsDefault` makes the item `notVerified` |
| `delete_type_version` | `library`, type, version | `LibraryTypeVersion.Delete()` | read back: `FindVersion(guid)` must return nothing; the guard lists the dependents read at planning |

Renames use the documented generic attribute API: `Name` must be listed as writable by `GetAttributeInfos()`
(`unsupported_capability` otherwise), then `SetAttribute("Name", value)` and a `GetAttribute("Name")` read-back;
a silently ignored rename fails `postcondition_failed`.

### Operations added from the V21 API description (issue #17)

Source **(V21)**: the Siemens IntelliSense files of TIA Portal V21 (`PublicAPI\V21\net48\Siemens.Engineering.Base.xml`, dated
2025-10-29), which name every member, its parameter list and its enum members. Every member below is bound **at runtime** by its
documented name and parameter list through the Siemens-free `LibrarySignatureBinding` (Contracts): enum parameters are matched by
their documented full type name and the value by its exact member name, collection parameters by their documented element type,
other parameters by assignability; exactly one applicable member must exist, otherwise the item fails closed with
`unsupported_capability` before anything is called. No stub member was added. Not live-tested.

| Operation | Fields | Siemens call | Notes |
| --- | --- | --- | --- |
| `retrieve_global_library` with `upgrade:true` | as above | `GlobalLibraryComposition.RetrieveWithUpgrade(FileInfo, DirectoryInfo, OpenMode)` | lifecycle; the prompt says the retrieved library is upgraded |
| `archive_global_library` | `library` (user global), `targetDirectory`, `name` (archive file name, file-name rules), `archivationMode` (`None`, `Compressed` default, `DiscardRestorableData`, `DiscardRestorableDataAndCompressed`) | `UserGlobalLibrary.Archive(DirectoryInfo, String, LibraryArchivationMode)` | lifecycle (confirms in read-write); evidence: the files named `name` or `name.*` the call wrote in `targetDirectory`, hashed into `result.transfer.files`; none found: `notVerified` |
| `save_global_library_as` | `library` (user global), `targetDirectory` | `UserGlobalLibrary.SaveAs(DirectoryInfo)` | lifecycle; the open library is found again by identity and its path afterwards is reported (`result.library`, note); the files written are hashed; none found: `notVerified`. The unsaved-change ledger is not cleared (the original file is unchanged) |
| `clean_up_library` | `library`, `typePath`/`typeGuid` or `folderPath`; `cleanUpMode` (project library only: `PreserveDefaultVersionOfUnusedTypes` default, `DeleteUnusedTypes`) | `ProjectLibrary.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, CleanUpMode)` / `UserGlobalLibrary.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>)` | guard `library_deletes` with the type and version count of the selection; the note reports the counts before and after (no further read-back is documented) |
| `harmonize_project` | `library`, selection, `harmonizeOptions` (`HarmonizePaths` and/or `HarmonizeNames`; both only when the live enum is `[Flags]`, `None` is documented to throw and is refused), optional `plcName` | `HarmonizeProject(IEnumerable<ILibraryTypeOrFolderSelection>, IEnumerable<IUpdateProjectScope>, HarmonizeProjectOptions)` with the PLC software as scope (as `update_project`) | guard `library_changes_project`; `trusted` (no documented read-back); compile afterwards |
| `update_library` | `library`, selection, `targetLibrary`; optional `forceUpdateMode`, `deleteUnusedVersionsMode`, `structureConflictResolutionMode` (all three or none) | `UpdateLibrary(IEnumerable<ILibraryTypeOrFolderSelection>, ILibrary)` or the five-argument overload | changes `targetLibrary` (ledger, `library_global_unsaved` when it is global); `library_update_defaults` without modes, `library_deletes` with `AutomaticallyDelete` |
| `update_project` with modes | as above plus `deleteUnusedVersionsMode` (project library) or all three modes (global library) | `ProjectLibrary.UpdateProject(…, DeleteUnusedVersionsMode)` / `GlobalLibrary.UpdateProject(…, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode)` | without modes the documented two-argument call as before |
| `delete_type` | `library`, type | `LibraryType.Delete()` | read back: `ILibrary.FindType(guid)` must find nothing; guard `library_deletes` lists the versions |
| `set_type_do_not_use` | `library`, type, `doNotUse` | `LibraryType.DoNotUse` setter (V21: "Gets or sets") | read back through the same property; a different value is `postcondition_failed`; `get_type` reports `doNotUse` |
| `create_type_folder` | `library`, `folderPath` (parent, default root), `newName` | `LibraryTypeUserFolderComposition.Create(String)` on the parent's `LibraryTypeFolder.Folders` | read back: the folder must be listed below the parent |
| `delete_type_folder` | `library`, `folderPath` (a user folder) | `LibraryTypeUserFolder.Delete()` | everything inside goes too (guard `library_deletes` with the counts); read back: no longer listed |
| `create_master_copy_folder` | `library`, `masterCopyFolderPath` (parent, default root), `newName` | `MasterCopyUserFolderComposition.Create(String)` on `MasterCopyFolder.Folders` | read back: listed below the parent |
| `delete_master_copy_folder` | `library`, `masterCopyFolderPath` (a user folder) | `MasterCopyUserFolder.Delete()` | guard `library_deletes`; read back: no longer listed |
| `rename_master_copy_folder` | `library`, `masterCopyFolderPath`, `newName` | generic `SetAttribute("Name")` after a `GetAttributeInfos` writability check | read back with `GetAttribute("Name")` |
| `create_type_from_documents` | `library`, `documentDirectory`, `documentName` (file name without extension), optional `folderPath` (destination type folder), `plcName` (the PLC software passed as `targetEnvironment`), `libraryImportOptions` (`None` default, `ActivateInactiveCultures`, `SkipInactiveCultures`) | `LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], LibraryImportOptions)` on the folder's `Types` | `targetEnvironment` is documented as "Target test environment for instance creation of Step7 library types"; which object TIA Portal expects there is not documented, so the PLC software is passed and TIA Portal decides (a rejection is `engineering_rejected`). Result: `TypeCreateTransferResults` state and messages in `result.transfer`, `CreatedType` in `created` and found again with `FindType`; unreadable: `notVerified` |
| `create_version_from_documents` | `library`, type, `documentDirectory`, `documentName`, optional `createOption` (`None` default: a new version; `Override`: overwrite the in-work/in-test version, guard `library_replaces_objects`), `plcName`, `libraryImportOptions` | `LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], CreateOptions, LibraryImportOptions)` on `LibraryType.Versions` | `CreatedVersion` read back into `versionAfter` and found again with `FindVersion` |
| `export_type_version` | `library`, version, `filePath`; optional `exportOptions` (`None` default, `WithDefaults`, `WithReadOnly`), `documentInfoOptions` (`None`, `All`, `CreatedTimeStamp`, `ExportSetting`, `InstalledProducts`; selects the three-argument overload), `overwrite` | `LibraryTypeVersion.Export(FileInfo, ExportOptions[, DocumentInfoOptions])` | SimaticML into a staged file next to the target, moved into place (an existing file needs `overwrite:true`), re-hashed (`result.transfer.files`); guard `library_export_overwrites_files` |
| `export_type_version_as_documents` | `library`, version, `documentDirectory`, `documentName`, `exportFormat` (from `get_supported_export_formats`), optional `libraryExportOptions` (`None` default, `WithLibraryVersionInfoFile`, `OnlyLibraryVersionInfoFile`), `overwrite` | `LibraryTypeVersion.ExportAsDocuments(DirectoryInfo, String, String, LibraryExportOptions)` | existing `documentName.*` files need `overwrite:true` (then TIA Portal decides); `ExportTransferResult` state, messages and `ExportedDocuments` in `result.transfer`, the files written hashed; none found: `notVerified` |
| `edit_type_version` with an instance | as above plus `blockPath` or `plcTypePath` | `LibraryTypeVersion.Edit(LibraryTypeInstanceInfo)` (V21: for types "that require instance to edit the type") | the instance's `LibraryTypeInstanceInfo` (`GetService`) is passed; an object that is no type instance is `target_kind_unsupported` |

Master copy items (`create_master_copy`, `copy_master_copy`, `delete_master_copy`, `rename_master_copy`, `instantiate_master_copy`,
`compare_master_copies`) take `masterCopyFolderPath` (`/` or `/A/B`, case-insensitive unique segments through `MasterCopyFolder.Folders`;
omitted: the root master copy folder); `copy_master_copy` also takes `targetMasterCopyFolderPath` (destination folder in `targetLibrary`).

New `library_read` operations:

| Operation | Fields | Result |
| --- | --- | --- |
| `get_supported_export_formats` | `library`, type | `{ library, path, name, guid, formats[] }` from `LibraryType.GetSupportedExportFormats()` |
| `compare_types` | `library`, type, `targetTypePath`/`targetTypeGuid`, optional `targetLibrary` (omitted: `library`), `maxElements` | `LibraryType.CompareTo(LibraryType)` |
| `compare_type_versions` | `library`, version, `targetTypePath`/`targetTypeGuid` with `targetVersion`, or `targetVersionGuid`; optional `targetLibrary`, `maxElements` | `LibraryTypeVersion.CompareTo(LibraryTypeVersion)` |
| `compare_master_copies` | `library`, `masterCopyName`, `masterCopyFolderPath`, `targetMasterCopyName`, `targetMasterCopyFolderPath`, optional `targetLibrary`, `maxElements` | `MasterCopy.CompareTo(MasterCopy)` |

Each compare returns `{ library, left, compareTo, right, properties[{ description, status, leftValue, rightValue }], complete, unreadable }`
from `DetailedCompareResult.Properties` (`DetailedCompareResultElement.Description`, `.DetailCompareStatus` — `ObjectsIdentical`,
`ObjectsDifferent`, `NotCompared` —, `.LeftValue`, `.RightValue`; at most `maxElements`, default 300). Comparing an object with itself is
an argument error. `get_type` also reports `doNotUse`, `setForUpdate`, `status`, `namespace`, `author` and `comment` (runtime lookups,
null and listed in `unreadable` when absent). `get_library_tree` lists master copy user folders (`kind: masterCopyFolder`) and the
master copies in them (`masterCopyScope: "all"`); `masterCopyFolderPath` starts that walk below a folder. `update_check` accepts
`updateCheckMode` `ReportOutOfDateAndUpToDate`, and `release_type_version` `dependenciesMode` `AutomaticallyCreateOrReleaseDependenciesIfRequired`
(both enums are now documented in full).

### Secondary (read-only reference) projects (issue #20)

> **Not live-tested.** Evidence (V21 `Siemens.Engineering.Base.xml`): `ProjectComposition.Open(FileInfo, UmacDelegate, ProjectOpenMode)`,
> `OpenWithUpgrade(FileInfo, UmacDelegate, ProjectOpenMode)`, `Retrieve(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)`,
> `RetrieveWithUpgrade(FileInfo, DirectoryInfo, UmacDelegate, ProjectOpenMode)`, `ProjectOpenMode.Secondary` ("Opens file as a secondary
> project"), `ProjectBase.IsPrimary`, `Project.Close()`; the Siemens session-and-project skill describes the secondary project as read-only
> and hidden, for copying and comparing.

TIA Portal keeps one project open for editing (K30/K51); a secondary project opens next to it read-only. The tools keep their
one-project binding: a secondary project is never a binding candidate (the worker's open-context list offers only projects whose
`IsPrimary` reads true; one that reads false or cannot be read is skipped and an unreadable one is reported, security review U3), content tools act only on the bound project, and the binding does not change when a secondary project
opens or closes.

| Operation | Fields | Siemens call | Notes |
| --- | --- | --- | --- |
| `open_secondary_project` (lifecycle) | `path` (project file), `upgrade` | `Open` / `OpenWithUpgrade` with `ProjectOpenMode.Secondary` and a null UMAC delegate | the bound project's own path is refused; evidence: the returned project reads `IsPrimary == false` (true: `postcondition_failed`; unreadable: `notVerified`); `result.secondaryProject { path, name, isPrimary }` |
| `retrieve_secondary_project` (lifecycle) | `path` (project archive), `targetDirectory`, `upgrade` | `Retrieve` / `RetrieveWithUpgrade` with `ProjectOpenMode.Secondary` | extracts into `targetDirectory` (guard `library_writes_files`) |
| `close_secondary_project` (lifecycle) | `secondaryProject` (path as listed) | `Project.Close()` on that secondary project | read back: no longer listed |
| `library_read` `list_secondary_projects` | none | `TiaPortal.Projects` filtered by `IsPrimary == false` | `{ projects[{ path, name, isPrimary }], unreadable }` |
| `create_master_copy` with `source.project` | as `create_master_copy`, plus the secondary project's path | the source is resolved in the secondary project, then `MasterCopies.Create(IMasterCopySource)` in the bound project's (or an open global) library | copy objects: capture from the secondary project, then `instantiate_master_copy` into the bound project; a capture depends on an `open_secondary_project` of that path earlier in the call |
| `plc_read` `compare_software` `compareTarget: secondary_plc` | `targetName` (PLC of the secondary project), `targetProject` (its path) | `PlcSoftware.CompareTo(<secondary PlcSoftware>)` | the TIA Portal is the bound project's parent (`IEngineeringInstance.Parent`) |

The three lifecycle items are confirmed once per actual read-write call like the global-library lifecycle items (guard
`library_secondary_project`, info; `library_writes_files` for a retrieve or an upgrade). The UMAC delegate is passed as null:
a UMAC-protected project is refused by TIA Portal (`engineering_rejected`), credentials are not in scope. **Left out:** reading a
secondary project's content directly with `plc_read`, `browse_project_tree`, `network_read` or `hmi_read` (the content tools act
on the bound project by the binding rule; capture into the library or compare instead); opening a secondary project of a
Multiuser local session. **Live acceptance still required:** that `IsPrimary` reads false for the opened project, that TIA Portal
accepts a null UMAC delegate for an unprotected project, and that a capture from a secondary project works.

### TIA Portal settings (issue #27)

> **Not live-tested.** Evidence (V21 `Siemens.Engineering.Base.xml`): `TiaPortal.SettingsFolders` (`TiaPortalSettingsFolderComposition`
> with `Find(String)`), `TiaPortalSettingsFolder.Name`, `.Folders`, `.Settings` (`TiaPortalSettingComposition.Find(String)`),
> `TiaPortalSetting.Name`, `.Value`. No setting is named and no value type is documented.

The settings belong to the TIA Portal instance, not to the project; they live in the library tools because those are the
tools that already act on TIA Portal-wide objects (global libraries), so no tool is added. Like every library operation they
need a bound standalone project (capability row of the library tools).

- `library_read` `list_portal_settings` (optional `settingsFolderPath` `A/B`, `maxDepth`): the folders and settings
  depth-first: `{ folderPath, folders[], settings[{ folderPath, name, value, valueType, enumMembers, writable, securityRelevant,
  unreadable }], complete, omission }`; `value` is invariant text, `valueType` the runtime type, `writable` whether the live
  `Value` property has a public setter, at most 5,000 entries.
- `library_write` `set_portal_setting` (`settingsFolderPath`, `settingName`, `settingValue` as text): the folder is found with the
  documented `Find(String)` per segment, the setting with `TiaPortalSettingComposition.Find(String)`; the text is converted to
  the runtime type of the current value (`true`/`false`, a number, an enum member by exact name, or text; another type is
  `unsupported_capability`); the value is set only through the live `Value` property's public setter (none:
  `unsupported_capability`, nothing changed) and read back (different: `postcondition_failed`). Result
  `setting { folderPath, name, before, after, valueType, securityRelevant }`.
- Guards and confirmation (security review 2026-11-03, F2; not live-tested): every call with a `set_portal_setting` confirms
  in read-write (`ConfirmsEveryCall`, so unsafe operationIds are refused like every confirming call); `library_portal_setting`
  (info) fires on every change. A setting is security-relevant when the folder path or setting name **TIA Portal resolved in
  planning** (the `Name` of every folder `Find(String)` returned and of the setting; the plan carries it as the conditional
  `setting` of `library_plan`, never the caller's text) contains `secur`, `certif`, `communicat`, `encrypt`, `password`,
  `trust`, `tls`, `ssl`, `legacy`, `authent`, `protect`, `umac`, `signing`, `signature`, `accept`, `allow`, `verif`,
  `cipher`, `access`, `credential`, `openness` or `firewall` (case-insensitive; the API names no setting, so the
  classification is by name; an unreadable name or a plan without the resolved setting counts as security-relevant).
  Such a setting also fires the **acknowledge** guard `library_portal_security_setting`; full satisfies it by policy. The
  host sends the planned classification with the mutation (`plannedSecurityRelevant`); the worker resolves the setting
  again and fails the item `state_changed` (nothing changed) when it classifies differently. There is no hard block.
- **Left out:** setting values whose type is not text, boolean, number or enum; settings not reachable through
  `SettingsFolders` (none are documented). **Live acceptance still required:** which settings exist, which have a setter, and
  their value types.

### Planning, ordering, guards, verification

Planning sends every item to the worker's non-mutating `library_plan`, which resolves exactly what the mutation
will use (library, folder, type, version, master copy, source, PLC group, device item; files and directories)
and reports the consequences; a refusal there fails the call before anything runs (`target_not_found`,
`target_ambiguous`, `target_kind_unsupported`, ...) and names the item. An item on a library that an earlier
item of the same call touches (`library`, `targetLibrary`, the `path` of an open item; instantiation and
`update_project` also touch the project library) depends on that item: it is planned again just before its own
mutation (`dependsOn`), so a call can open a library and copy a master copy into it. Libraries created or
retrieved in a call are addressed in a later call (their path is in the result and in `list_libraries`).

Guards (all `info` except one `block`; none needs acknowledgement):

| Guard | Fired by |
| --- | --- |
| `library_close_unsaved_changes` | `close_global_library` of a library with a change this server made that no `save_global_library` covers ([below](#closing-a-global-library)) |
| `library_writes_files` | `open_global_library` with `ReadWrite` or `upgrade`, `retrieve_global_library`, `create_global_library`, `save_global_library`, `archive_global_library`, `save_global_library_as` |
| `library_upgrades_in_place` | `open_global_library` with `upgrade:true` |
| `library_already_open` | `open_global_library` of an open file |
| `library_close_unsaved` | `close_global_library` |
| `library_deletes` | `delete_master_copy`, `delete_type_version`, `discard_type_version`, `delete_type`, `delete_type_folder`, `delete_master_copy_folder`, `clean_up_library`, `update_project`/`update_library` with `AutomaticallyDelete` (with the planned consequences) |
| `library_changes_project` | `harmonize_project` |
| `library_export_overwrites_files` | `export_type_version`, `export_type_version_as_documents` |
| `library_replaces_objects` | `instantiate_master_copy` with `masterCopyMode: Replace`, `create_version_from_documents` with `createOption: Override` |
| `library_syncs_project_library` | `instantiate_type_version` from a global library (the version is synchronized into the project library first) |
| `library_update_defaults` | `update_project`, `update_library` without modes |
| `library_global_unsaved` | a content change in a global library (`library` or the `copy_master_copy` target) |

Verification is `trusted` (or `superseded` by a later item of the call on the same object, or by a later
`close_global_library` of the same library that no `save_global_library` of that library precedes; K47: the object of a
`copy_master_copy` is the copy in the target library, never the source, and a save is never superseded, a close does not
undo it) for every completed item: the worker mutation itself performs the
read-backs listed above and fails `postcondition_failed` when they disagree, so there is no separate verification
read. A read-back that cannot be made fails closed to `notVerified`, never `trusted`: when a member it needs is a
runtime lookup that cannot be read (`VersionNumber`, `IsDefault`), the version cannot be read again (neither the object
nor `ILibrary.FindVersion`), the live `LibraryTypeVersionState` has no member of the expected name (looked up by exact
name through `LibraryRuntimeBinding.EnumValue`; the stub enum declares none), or a `CreateFrom` is bound without a return
value, the worker sets the result's conditional `unverified` (why), the item still succeeds (the call returned without
rejection), and its verification entry is `{ check: "notVerified", success: false }`, so the call reports
`success:false` with the "could not be verified" warning. K61: the library lifecycle and master copy items are also
read back after the call with the reads `library_read` uses: `open_global_library`, `retrieve_global_library` and
`create_global_library` must be listed by `list_libraries` (the path the result names), `close_global_library` must no
longer be listed (check `openLibraries`); `create_master_copy` and `copy_master_copy` must be in the root master copy
folder of their library (`copy_master_copy`: the target library), `delete_master_copy` must be gone from it
(`get_library_tree` content `masterCopies`, check `masterCopy`). A later open, retrieve or create of a library supersedes
an earlier close of it; a later close that supersedes a content item says that it may have discarded the change.
`save_global_library` stays `trusted` and says why: no documented member reports whether a global library has unsaved
changes. Warnings on those read responses (for example a TIA Portal dialog raised after a close returned) reach the root
`warnings` of the call. Failed items without a read-back have no verification entry; a failed item that is read back
is never `success: true` (K58). A Siemens `EngineeringTargetInvocationException` in a mutation
is `engineering_rejected` with `engineeringError`; other Siemens failures are `worker_operation_failed`;
`portal_lost` wins over both. Every `library_write` call appends one audit v2 record (`tool: library_write`).

Result value of every write item `LibraryWriteResultInfo`: `operation`, `library`, `target`, `created[{ name,
runtimeType }]`, `nameAfter`, `versionAfter { versionNumber, guid, state, isDefault }`, `alreadyOpen`,
`returnedType` (runtime type of a value whose type the sources do not document, e.g. `UpdateProject`'s), `note`,
`unverified` (conditional: present only when a read-back could not be made, see above), and the confirmation evidence `portalConfirmationPolicy`, `portalConfirmations`, `portalConfirmationsObserved`,
`portalConfirmationsComplete`.

### Closing a global library

The sources document only the parameterless `Close()`, and only on user global libraries (A: `globalLibrary.Close()`
on a `UserGlobalLibrary`, key types `UserGlobalLibrary.Close()`; L: "A `UserGlobalLibrary` ... closed deterministically
with `Close()`"), so `close_global_library` refuses system and corporate global libraries at planning
(`target_kind_unsupported`, nothing runs). There is no `saveChanges` argument, and no documented member reports whether a global library
has unsaved changes (`IsModified` is documented for projects only). So `close_global_library` never chooses
between saving and discarding:

- The host keeps a ledger (`LibraryUnsavedChanges`, one per worker client, so it survives worker restarts) of the
  global libraries this server changed and did not save: a completed content item on a global library (master
  copy create/delete/rename, the `copy_master_copy` target, type and folder renames, edit/release/discard/default/
  delete of versions; instantiations and `update_project` change the project, not the library), or a failed one
  whose failure may follow a change (anything except argument, binding, target and capability refusals;
  `engineering_rejected` counts as a possible change, because the mutation flag spans the whole worker method and
  so does not prove that no setter ran). A successful `save_global_library` or `close_global_library`, or a fresh
  open of the path, forgets the library.
- Entries are keyed by the TIA Portal process the change was made in (`PortalProcessId` of the pinned binding) and
  the normalized path: global libraries belong to a Portal session, so a change made in Portal A never blocks a
  close of the same file in Portal B, and a save, close or open in B never forgets A's change. A verified binding
  always carries a Portal PID; were it unknown, the change would match every Portal and a save, close or open
  would forget nothing (the close stays refused rather than being wrongly allowed). A reused PID of a later Portal
  process inherits stale entries; `save_global_library` or a fresh open resolves the refused close.
- An open is fresh only when the file was not already open. The worker compares `new FileInfo(path).FullName` with
  each open library's `Path.FullName` (G: check for an already-open library "by comparing
  `GlobalLibrary.Path.FullName`", ordinal case-insensitive; `.` segments and duplicate or forward separators are
  resolved), and when `Open`, `OpenWithUpgrade`, `Retrieve` or `Create` returns a library that was already open
  before the call (the same live object by `Equals`, or the same full path) the result says `alreadyOpen:true` with
  a note, and the ledger keeps its entry.
- A close fires the info guard `library_close_unsaved_changes` when the ledger, or an earlier item of the same call,
  holds such a change that no `save_global_library` between them covers. The guard lists the changes the close
  discards; the close runs (read-write confirms it, as every lifecycle item). Save first to keep them. A dry run
  reports the guard.
- Changes the ledger cannot know (made in the TIA Portal UI, by another Openness client, or before the server
  started) are not detected. If `Close()` then raises a TIA Portal confirmation, it is answered by
  `portalConfirmations` and recorded; with the default `decline` the item fails, and the library may already be
  closed with its changes discarded. Inspect `list_libraries` before retrying.

## Caps and budgets

| Cap | Value |
| --- | --- |
| items per call | 1-50 |
| tree depth / page | 1-32 (default 16) / 1-500 (default 200); walk stops at 5,000 entries |
| compare depth / elements | 1-64 (default 16) / 1-5000 (default 300) |
| instances per version | 1,000 |
| update-check messages | 8 levels, 500 messages |
| dependencies, dependents, master copies containing instances | 200 each |
| attributes per compare element or detail | 20 |
| names / paths | 256 / 1,024 characters; texts cut at 1,000 |

The worker request timeout (`--worker-timeout-minutes`, default 5) bounds every call. Documents use the effective
value/document budgets (`--max-item-chars`, `--max-document-chars`); an oversized value is omitted whole with
guidance, never cut.

## Provenance of every Siemens member used

Sources (read-only reference data, never executed): **(L)** `totally-integrated-claude`
`skills/tia-project-general/references/library-operations.md` (states that it was checked against the installed
V21 `Siemens.Engineering.Base.dll`/`.xml`); **(G)** Siemens `tia-portal-ai-extensions`
`openness_development/skills/global-library/SKILL.md`; **(A)** `.../libraries-and-alarms/SKILL.md`; **(E)**
`.../engineering-objects/SKILL.md`; **(S)** `.../session-and-project/SKILL.md`; **(B)** the V21 type-name baseline
extracted from real DLL metadata (`api-baselines/v21/base.json`, `step7.json`; type names and assemblies only).

Status: **compiled** (declaring type, return type and parameters are all shown by the source; declared in the
stubs and pinned by the probe); **runtime** (the source names the member and its arguments but not its return,
declaring or collection parameter type: bound on the live object by exact name and the documented argument
types through `LibraryRuntimeBinding`, exactly one applicable public member or fail closed); **runtime,
parameterless** (the source names a self-action on the object without a parameter list; bound as a
parameterless method, fail closed); **runtime lookup** (a named property read by name; informative members
degrade to `unreadable`); **generic** (the documented `IEngineeringObject` attribute API); **existing** (an
unchanged existing stub). Every declared type is in the baseline assembly the stubs declare it in (B), pinned by
`LibraryReferenceStubTests`.

| Siemens member | Assembly | Source | Status |
| --- | --- | --- | --- |
| `Project.ProjectLibrary` → `ProjectLibrary` | Base | L | existing |
| `TiaPortal.GlobalLibraries` → `GlobalLibraryComposition` | Base | L | compiled |
| `ProjectLibrary`, `GlobalLibrary` implement `ILibrary`; `ILibrary.TypeFolder` → `LibraryTypeSystemFolder`, `.MasterCopyFolder` → `MasterCopySystemFolder` | Base | L | compiled |
| `ILibrary.FindType(Guid)` → `LibraryType`, `ILibrary.FindVersion(Guid)` → `LibraryTypeVersion` | Base | L | compiled |
| `LibraryType.Guid` (`Guid`), `LibraryType.Name` (`string`) | Base | L (Guid passed to `FindType`), G (`parentType.Name` compared as string) | compiled |
| `LibraryTypeVersion.Guid` (`Guid`), `.State` (`LibraryTypeVersionState`), `.Edit()` → `LibraryTypeVersion` | Base | L (`selected.State == LibraryTypeVersionState.Committed`; the catalogue: "`InWork` or `Committed`") | compiled; state members (`InWork`, `Committed`) compared by name (listing), and for the release/edit evidence against the live enum member of that exact name (`LibraryRuntimeBinding.EnumValue`; absent → `notVerified`) |
| `MasterCopy.Name` (`string`) | Base | L (`target.Find(created.Name)`) | compiled |
| `MasterCopyComposition.Create(IMasterCopySource)` → `MasterCopy` | Base | L ("installed signature"); A shows the call | compiled |
| `MasterCopyComposition.CreateFrom(MasterCopy)` → `MasterCopy` | Base | L | compiled |
| `MasterCopyFolder.MasterCopies`, `MasterCopyComposition.Find(string)` and enumeration | Base | existing (compare feature) | existing |
| `GetService<LibraryTypeInstanceInfo>()` on `PlcBlock`/`PlcType` (through `IEngineeringServiceProvider`) | Base / Step7 | G | compiled (existing `GetService<T>`) |
| Types used in `is`/`as`/`typeof`: `GlobalLibrary`, `UserGlobalLibrary`, `SystemGlobalLibrary`, `CorporateGlobalLibrary`, `LibraryTypeFolder`, `LibraryTypeSystemFolder`, `LibraryTypeUserFolder`, `LibraryTypeInstanceInfo`, `IMasterCopySource`, `IInstanceSearchScope`, `IUpdateProjectScope`, `ILibraryTypeOrFolderSelection` | Base | L, G, B | compiled (types; specializations from L's catalogue, folder hierarchy from G, interfaces from L/G) |
| `CodeBlockLibraryType`, `CodeBlockLibraryTypeVersion` (`SW.Blocks`), `PlcTypeLibraryType`, `PlcTypeLibraryTypeVersion` (`SW.Types`) | Step7 | G §4-§5, B | compiled (types) |
| Enums `OpenMode`, `MasterCopyMode`, `UpdatePathsMode`, `CreateOrReleaseDependenciesMode`, `UpdateCheckMode` | Base | L, G, S, B | compiled without members; values parsed by exact name at runtime |
| Issue #17 members (V21): `UserGlobalLibrary.Archive(DirectoryInfo, String, LibraryArchivationMode)`, `.SaveAs(DirectoryInfo)`, `.CleanUpLibrary(IEnumerable<ILibraryTypeOrFolderSelection>)`; `ProjectLibrary.CleanUpLibrary(…, CleanUpMode)`, `.UpdateProject(…, DeleteUnusedVersionsMode)`; `ProjectLibrary`/`GlobalLibrary` `.HarmonizeProject(…, HarmonizeProjectOptions)`, `.UpdateLibrary(…, ILibrary[, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode])`; `GlobalLibrary.UpdateProject(…, ForceUpdateMode, DeleteUnusedVersionsMode, StructureConflictResolutionMode)`; `GlobalLibraryComposition.RetrieveWithUpgrade(FileInfo, DirectoryInfo, OpenMode)`; `LibraryType.Delete()`, `.DoNotUse` (get/set), `.GetSupportedExportFormats()`, `.CompareTo(LibraryType)`, `.Author`, `.Comment`, `.Namespace`, `.SetForUpdate`, `.Status`; `LibraryTypeVersion.Export(FileInfo, ExportOptions[, DocumentInfoOptions])`, `.ExportAsDocuments(DirectoryInfo, String, String, LibraryExportOptions)`, `.CompareTo(LibraryTypeVersion)`, `.Edit(LibraryTypeInstanceInfo)`; `LibraryTypeComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], LibraryImportOptions)`; `LibraryTypeVersionComposition.CreateFromDocuments(DirectoryInfo, String[, IEngineeringObject], CreateOptions, LibraryImportOptions)`; `LibraryTypeUserFolderComposition.Create(String)`, `LibraryTypeUserFolder.Delete()`; `MasterCopyFolder.Folders`, `MasterCopyUserFolderComposition.Create(String)`, `MasterCopyUserFolder.Delete()`, `.Name`; `MasterCopy.CompareTo(MasterCopy)`; `DetailedCompareResult.Properties`, `DetailedCompareResultElement.Description/.DetailCompareStatus/.LeftValue/.RightValue`; `TypeCreateTransferResults.CreatedType/.Messages/.TransferResultState`, `VersionCreateTransferResults.CreatedVersion/…`, `ExportTransferResult.ExportedDocuments/…`, `TransferResultMessage.Message`; enums `LibraryArchivationMode`, `CleanUpMode`, `HarmonizeProjectOptions`, `ForceUpdateMode`, `DeleteUnusedVersionsMode`, `StructureConflictResolutionMode`, `LibraryImportOptions`, `LibraryExportOptions`, `CreateOptions`, `ExportOptions`, `DocumentInfoOptions` (all members), `CreateOrReleaseDependenciesMode` and `UpdateCheckMode` (both members) | Base | V21 | runtime (`LibrarySignatureBinding`: enums by full type name, collections by element type; properties and results runtime lookups); no stub |
| Enumeration of `GlobalLibraryComposition` | Base | G (`OfType<GlobalLibrary>()`) | runtime (non-generic `IEnumerable`) |
| `GlobalLibrary.Path` | Base | G (`Path.FullName`) | runtime lookup; a library without a readable path is not addressable |
| `GlobalLibrary.Name` | Base | none (informative) | runtime lookup (property, then attribute); null when absent |
| `GlobalLibraryComposition.GetGlobalLibraryInfos()`; `GlobalLibraryInfo.LibraryType`, `.Name`, `.Path` | Base | L | runtime; runtime lookup |
| `GlobalLibraryComposition.Open(FileInfo, OpenMode)` | Base | L | runtime (return type undocumented; L casts it) |
| `GlobalLibraryComposition.Open(GlobalLibraryInfo)` ("The global library info associated with a global library to be opened") | Base | L | runtime (K80: system and corporate libraries, found by `GlobalLibraryInfo.Path`/`.LibraryType`) |
| `GlobalLibraryComposition.OpenWithUpgrade(FileInfo)` | Base | G, S | runtime |
| `GlobalLibraryComposition.Retrieve(FileInfo, DirectoryInfo, OpenMode)` | Base | G | runtime |
| `GlobalLibraryComposition.Create<UserGlobalLibrary>(DirectoryInfo, string)` | Base | A | runtime (generic, closed over `UserGlobalLibrary`) |
| `UserGlobalLibrary.Save()` | Base | L (names `Save`) | runtime, parameterless |
| `UserGlobalLibrary.Close()` | Base | A (`globalLibrary.Close()` on a created `UserGlobalLibrary`; key types: `UserGlobalLibrary.Close()`), L ("A `UserGlobalLibrary` opened by the application must be saved when intended and closed deterministically with `Close()`"; catalogue: `UserGlobalLibrary` "save, archive, close") | runtime, parameterless, on user global libraries only (no source shows it on `SystemGlobalLibrary` or `CorporateGlobalLibrary`) |
| `LibraryTypeFolder.Folders` (child `LibraryTypeUserFolder`s), `LibraryTypeFolder.Types` (direct child types) | Base | G (read on the base class, never the derived shadow) | runtime (on the declaring base type) |
| Type folder name | Base | G (folders form `GlobalLib://` paths; no member named) | runtime lookup (`Name` property, then attribute); unreadable folders cannot be addressed |
| `LibraryType.Versions` | Base | L (`foreach (LibraryTypeVersion version in type.Versions)`) | runtime |
| `LibraryTypeVersion.VersionNumber`, `.IsDefault`, `.Dependencies`, `.Dependents`, `.MasterCopiesContainingInstances`, `.OriginalLibrary` | Base | L (names), G (`VersionNumber` ordering) | runtime lookup (informative; `unreadable` when absent) |
| `LibraryTypeVersion.FindInstances(IInstanceSearchScope)` with `PlcSoftware` as scope | Base | L | runtime |
| `LibraryTypeInstanceInfo.LibraryTypeVersion` | Base | G | runtime lookup |
| `MasterCopy.ContentDescriptions[].ContentType` | Base | G | runtime lookup |
| `UpdateCheck(Project, UpdateCheckMode)`; `UpdateCheckResult.Messages`, `UpdateCheckResultMessage.Description`, `.MessageParts`, `.Messages` | Base | L | runtime (declaring type undocumented: bound on the live library); runtime lookup |
| `CompareToLibrary(ISoftwareCompareTarget)`; `LibraryCompareResult.RootElement`, `.Elements`, `.ComparisonResult`, `.DetailedInformation.Properties` | Base | L | runtime; runtime lookup; element and detail attributes generic (E) |
| `UpdateProject(<collection of ILibraryTypeOrFolderSelection>, <collection of IUpdateProjectScope>)` | Base | G (`lib.UpdateProject(typeSelections, new List<IUpdateProjectScope> { plcSoftware })`), L (catalogue roles) | runtime: the single two-parameter overload that accepts a `List<T>` (or `T[]`) of each documented element type |
| `LibraryTypeVersion.Release(CreateOrReleaseDependenciesMode, Version, string, string)` | Base | L ("the exact installed method") | runtime (return undocumented) |
| `LibraryTypeVersion.Discard()`, `.SetAsDefault()`, `.Delete()` | Base | L (names `Discard`, `SetAsDefault`, `Delete`) | runtime, parameterless |
| `MasterCopy.Delete()` | Base | L (`Delete()`) | runtime |
| `CreateFrom(MasterCopy, MasterCopyMode)` on `PlcBlockGroup.Blocks`, `PlcTypeGroup.Types`, `PlcTagTableGroup.TagTables`, `TechnologicalObjectGroup.TechnologicalObjects`, `PlcUnitProvider.UnitGroup.Units` | Step7 | G §7 (A for `GetService<PlcUnitProvider>()`) | runtime (return undocumented; groups and compositions existing) |
| `PlcExternalSourceComposition.CreateFrom(MasterCopy, MasterCopyMode)` on `PlcSoftware.ExternalSourceGroup.ExternalSources` (target kind `externalSources`, added by `feature/external-sources-folders`) | Step7 | `totally-integrated-claude` `tia-plc-operations/references/external-sources.md` V21 API list | runtime (return undocumented; group and composition existing) |
| `CreateFrom(MasterCopy)` on `Project.Devices` and on a device item's `DeviceItems` | Base | G §9 | runtime (declaring type undocumented) |
| `PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)` on `PlcSoftware.PlcAlarmTextlistGroup.PlcAlarmUserTextlists` | Step7 | `tia-plc-operations/references/alarms.md` (both `CreateFrom` overloads), A (`PlcAlarmTextlistGroup.PlcAlarmUserTextlists`) | runtime (return undocumented; group and composition compiled, see the [alarm provenance](PLC_ALARMS_TEXTLISTS_SUMMARY.md#provenance-of-every-siemens-member-used)) |
| A user text list as `MasterCopies.Create` source | Step7 | A (`MasterCopies.Create(alarmTextList)`) | runtime check `as IMasterCopySource` (not declared on the stub) |
| `Blocks.CreateFrom(CodeBlockLibraryTypeVersion, UpdatePathsMode)`, `Types.CreateFrom(PlcTypeLibraryTypeVersion, UpdatePathsMode)` | Step7 | G §6 | runtime (return undocumented) |
| Renames: `IEngineeringObject.GetAttributeInfos()`, `EngineeringAttributeInfo.Name/.AccessMode`, `SetAttribute("Name", value)`, `GetAttribute("Name")` | Base | E (generic API, check `GetAttributeInfos` first), G (rename via `SetAttribute("Name", …)`) | generic (existing) |
| `TiaPortal.Confirmation` / `ConfirmationEventArgs.Result` (`ConfirmationResult.Yes`/`No`) and the dialog text read | Base | existing (`TiaPortalSession`, plc_download's scope and text reader) | existing; no new member |
| PLC groups, `PlcSoftware.BlockGroup/TypeGroup/TagTableGroup/TechnologicalObjectGroup`, `Groups`, `PlcTagTableComposition.Find`, `ProjectBase.Devices`, `HardwareObject.DeviceItems`, device-item selector resolution, `PlcBlock`/`PlcType` resolvers | Base / Step7 | existing | existing |

Stub conventions that are not Siemens facts: the new enums declare no members, the `IEngineeringObject`/
`IEngineeringInstance` interface lists follow the existing stub style, parameter names are placeholders, and
`GlobalLibraryComposition` declares no enumeration interface (the worker enumerates it as `IEnumerable`).

### Left out, and why

| Requested / considered operation | Reason |
| --- | --- |
| The per-type overloads `LibraryType.UpdateLibrary(...)` and `LibraryType.UpdateProject(...)` | the library-level overloads with a type selection (`update_library`, `update_project`) call the same function; the per-type ones take their modes in another order and add nothing |
| Combined `ExportOptions` (e.g. `WithDefaults` and `WithReadOnly`) | V21 does not say that `ExportOptions` is a flags enum; one member per call |
| Setting a type's or version's `Author`/`Comment`, a type's `SetForUpdate` | V21 documents these properties without "Gets or sets"; they are read (`get_type`), not written |
| Global library properties (`Author`, `Comment`, `IsModified`, `IsReadOnly`, `HistoryEntries`, ...) | not requested by issue #17; `IsModified` could replace the unsaved-change ledger of `close_global_library` and is an open follow-up |
| Compare filters (`differencesOnly`) and element names in `compare_libraries` | V21 names `LibraryCompareResultState` (`ObjectsIdentical`, `ContainerContentsIdentical`, ...) and `LeftName`/`RightName`; the tree stays schema-free as before (follow-up) |
| Placing into a software unit's own block/type/tag-table groups; device user groups as targets | not implemented in this iteration (PLC-root groups, the PLC's unit composition, project devices and device items only) |
| Automatic routing of a master copy by content type (G §7) | deliberate: the caller chooses `target.kind` from `contentTypes`, nothing is inferred |
| Reload a global library, glob search, add a type as multi-instance (tiacommander ideas) | no reload member documented; search is client-side over `get_library_tree`; multi-instance members are block-interface editing, not a library API |
| Transactions / `ExclusiveAccess` around library writes | L: "where the participating API supports it" without saying which do; none is used |
| Multiuser local sessions | the project library is documented on `Project` only; standalone projects only |
| Closing a system or corporate global library | `Close()` is sourced for `UserGlobalLibrary` only (A, L); `close_global_library` refuses other kinds (`target_kind_unsupported`) |
| `close_global_library` with a save-or-discard choice (`saveChanges`); detecting unsaved changes made outside this server | only the parameterless `Close()` is documented; the ledger names this server's own unsaved changes ([Closing a global library](#closing-a-global-library)) |

## Known limits and open risks

- Not live-tested. Most members are runtime-bound: any name, argument type or return shape that differs in the
  installed V21 fails closed with `unsupported_capability` (nothing called) — but a member that exists with a
  different meaning than assumed (notably `Save()`, `Discard()`, `SetAsDefault()`, `Delete()` bound as
  parameterless, and `Release`'s third/fourth string arguments) would only show in the live checks.
- `UpdateProject` is bound by its documented call shape: if several two-parameter overloads accept the
  collections, nothing is chosen (`unsupported_capability` listing the count).
- Which items raise TIA Portal `Confirmation` dialogs is undocumented. Each one is answered by the item's
  `portalConfirmations` and recorded; with the default `decline` such an item fails even when the Siemens call
  completed. A dialog raised after the item returned keeps the item's answer until the next worker request and
  is only a warning on that next response (which may belong to another tool when no library request follows).
- The unsaved-change ledger knows only this server's own changes (see [Closing a global
  library](#closing-a-global-library)); it may also be stale (a library saved or closed in the UI): then the refused
  close is resolved by `save_global_library` or a fresh open.
- `create_global_library` and `retrieve_global_library` return the library TIA Portal opened; its path is read
  back from the returned object and must be listed among the open libraries (`postcondition_failed` otherwise,
  after the files were written).
- `instantiate_*` with `newName` renames after creation: a rename refusal fails the item after the object was
  created under its library name (no cleanup).
- Global-library type instantiation synchronizes the version into the project library first (L); this extra
  mutation is reported as a guard but not verified separately.
- Global library paths and folder/type names are matched case-insensitively and must be unique; TIA Portal's
  own uniqueness rules are not verified. Libraries without a readable `Path` are listed in `unreadable` and
  cannot be addressed.
- The open-library list and the tree are read by enumeration; a Siemens failure while enumerating ends the
  enumeration (reported as a warning on stderr), so a missing library or entry is possible on a faulty
  installation.

## Live acceptance checks

Run on a disposable standalone project and copies of libraries first:

1. `library_read list_libraries`, `list_global_library_infos`: paths and kinds match the TIA Portal "Global
   libraries" pane; the project library is listed; no `unreadable` entries.
2. `get_library_tree` on the project library and on an open global library: folders, types, versions (`state`,
   `isDefault`, `versionNumber` texts) and root master copies (`contentTypes`) match the Libraries task card;
   page with `offset`/`limit`; check `maxDepth`.
3. `get_type`, `find_type_instances` (`FindInstances` with the PLC), `get_instance_info` on an instance and on a
   master-copy-placed block (`linked:false`).
4. `update_check` against an out-of-date project (messages and nesting) and `compare_libraries` project vs.
   global (record which attributes the elements expose).
5. `open_global_library` read-only, read-write and with `upgrade` (on a copy of an older library); confirm the
   prompt in read-write and none in full; `retrieve_global_library` of a `.zal21`; `create_global_library`;
   `save_global_library`; `close_global_library` with and without unsaved changes: the refusal after a change made
   through `library_write`, then (after a UI change) the recorded dialog with `decline` and with `accept`, and
   whether the changes were saved or discarded; record every dialog `type`/`text` the items raise. Open the same
   file again under a different spelling (`.` segment, forward slashes): `alreadyOpen:true`, nothing called;
   `close_global_library` of an open system library: `target_kind_unsupported`.
6. `create_master_copy` from a block, a UDT, a tag table and a device; `copy_master_copy` into a global
   library; `rename_master_copy`; `delete_master_copy`.
7. `instantiate_master_copy` into each target kind (`ThrowIfExists` on an existing name → `engineering_rejected`;
   `Rename`; `Replace`), with `newName`; compile afterwards; record the runtime return type of each `CreateFrom`
   (`returnedType`) and that none is `notVerified`.
8. `instantiate_type_version` of a code block and of a UDT version from the project library and from a global
   library (check the project library synchronization); every `updatePathsMode`.
9. `update_project` for a type and for a folder; compare with the update check.
10. Type workflow: `edit_type_version` → edit the test instance with `plc_write` → `compile_check` →
    `release_type_version` (record what the third and fourth arguments show in TIA Portal, and that the released
    version keeps its GUID, reads `Committed` and the requested number, i.e. is `trusted`, not `notVerified`) →
    `set_default_type_version` → `discard_type_version` on another in-work version (record whether it disappears
    from `FindVersion` or changes state) → `delete_type_version`.
11. `rename_type`, `rename_type_folder`; confirm `GetAttributeInfos` lists `Name` as writable on types, folders
    and master copies.
12. A runtime-binding failure path: an installation member mismatch must report `unsupported_capability` without
    a change (inspect with the reads).
