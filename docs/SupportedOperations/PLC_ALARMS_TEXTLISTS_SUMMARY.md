# PLC alarms, alarm classes, text lists, supervisions and ProDiag (`plc_read`, `plc_write`)

**Language:** English · [Deutsch](PLC_ALARMS_TEXTLISTS_SUMMARY.de.md)

**Not live-tested.** Everything on this page is built from the V21 sources listed under
[Provenance](#provenance-of-every-siemens-member-used) and verified offline only (unit tests, the
`plc-alarms-roundtrip` FakeWorker scenario, the compile-only reference probe). The
[live acceptance checks](#live-acceptance-checks) are open.

The operations cover what the documented V21 Openness surface offers for PLC alarm data:

- **Text lists** (`PlcSoftware.PlcAlarmTextlistGroup`): list user and system text lists with name, ID,
  list range and multilingual comment; rename a user list, change its range and its comment per
  culture; delete a user list; create one from a master copy (`library_write`); read and change
  entries through TIA Portal's XLSX export and import; export and import whole text list workbooks.
- **Program alarm texts** (`PlcAlarmTextProvider`): read and change alarm instance texts per culture
  through the XLSX export and import; export and import alarm text workbooks.
- **Common alarm classes** (`AlarmClassDataProvider`): export and import (project-wide).
- **ProDiag supervisions** (`SupervisionProvider` of a ProDiag FB): read and change the supervision
  workbook cells; export and import supervisions; import supervision settings.
- **ProDiag** (blocks.md section 12): the documented evidence of a ProDiag FB, the
  `AssignedProDiagFB` attribute of instance DBs, UDT-instance members of global DBs and tags (read and
  set) and the FB's ProDiag alarm information export (`ExportProDIAGInfo`, CSV).

System text lists are never changed through this server. Access follows the access mode only: reads in
every mode, writes in read-write and full; no write prompts.

## Why `plc_read`/`plc_write` and not a new tool pair

AGENTS.md routes PLC software content through `plc_read`/`plc_write` (tags, blocks, watch/force
tables, granular block edits) and adds a domain to an existing tool when it fits its selectors
(hardware modules became `network_read`/`network_write` operations). Text lists, alarm texts,
supervisions and ProDiag objects belong to a PLC (`plcName`, a block path or a tag) and use the
existing PLC binding, planner, guards, verification and audit. The tool surface therefore stays
**13/33/33**. The common alarm classes are the one project-wide part: they are reached through
`Project.GetService<AlarmClassDataProvider>()`, take no `plcName`, show an empty `plcName` in the
effect target and are delivered for standalone projects only (their own `standaloneOnly` capability
rows). A separate pair (the library precedent) was not needed: libraries span projects and global
library files, these operations do not.

## Operations

### `plc_read`

| Operation | Fields | Capability | Result |
| --- | --- | --- | --- |
| `list_text_lists` | optional `plcName` (omitted: every PLC) | Observe | `{ projectLanguages { active[], editing, reference, complete, messages[] }, plcs[{ plcName, deviceName, userTextLists[], systemTextLists[], complete, messages[] }] }`; each list `{ name, isSystem, id, listRange, comment[{ culture, text }] \| null, unreadable[] }` |
| `read_text_list_workbook` | optional `plcName`, `textListNames`, `cultures` (only with `textListNames`), `maxElements`, `sheet`, `startRow`, `startColumn` | TemporaryExport | workbook cells (below) |
| `read_alarm_text_workbook` | `exportOption`; optional `plcName`, `cultures`, `maxElements`, `sheet`, `startRow`, `startColumn` | TemporaryExport | workbook cells |
| `read_supervision_workbook` | `blockPath` (ProDiag FB); optional `maxElements`, `sheet`, `startRow`, `startColumn` | TemporaryExport | workbook cells plus `programmingLanguage`, `supervisionCount` |
| `get_prodiag_fb` | `blockPath` | Observe | `{ blockPath, plcName, blockType, programmingLanguage, isProDiag, supervisionCount, isConsistent, hasSupervisionProvider, attributes[{ name, value }], unreadable[] }` |
| `get_prodiag_assignment` | a tag (`tableName`, `name`, optional `folderPath`, `plcName`) or a block (`blockPath`, optional `memberPath`) | Observe | `{ targetKind, target, plcName, assignedProDiagFb, readable, message }` |

**Workbook reads** export to a fresh temporary directory that is always deleted again, read the cells
and return `{ kind, plcName, deviceName, blockPath, programmingLanguage, supervisionCount, cultures[],
textListNames, exportOption, exportState, workbookHash, sheet, startRow, startColumn, sheets[{ name,
rowCount, columnCount, cellCount, rows[{ row, cells[{ cell, kind, value, valueTruncated }],
cellsTruncated, nextStartColumn }], rowsTruncated, nextStartRow }], probes[], complete, messages[] }`.

- `maxElements` caps the rows per worksheet (1-5000, **default 50**). The read also stops at the
  character budget, estimated with every character outside printable ASCII and every HTML-sensitive
  one counted as the six-character `\uXXXX` escape the canonical writer emits. The budget is the
  effective value budget (60,000 by default, raised with `--max-item-chars`) minus 4,000 for the rest
  of the item; when one `plc_read` call holds several workbook reads, each gets at most an equal share
  of the document budget (180,000 by default, `--max-document-chars`) minus 20,000 for the envelope
  and the other items, minus the same 4,000 (with the defaults: 56,000 for one or two reads, 49,333
  for three, 12,000 for ten), never less than 2,000 (from 27 reads with the defaults). A read result
  therefore stays inside the value budget; the document budget can still omit whole results (with
  retry guidance, never cut) when the call's other items are large or the 2,000 floor applies.
- The first row of a page always makes progress without exceeding the budget: when it alone does not
  fit, the cells that fit whole are returned (only the first cell is cut, `valueTruncated`, when even
  it does not fit) and the row reports `cellsTruncated:true` with `nextStartColumn`, the first column
  not returned. Read the rest of the row with `sheet`, `startRow` = that row and `startColumn` =
  `nextStartColumn`.
- `sheet` returns rows of that worksheet only (exact name, else the one case-insensitive match;
  `target_not_found` otherwise), from `startRow` (default 1) and `startColumn` (upper-case letters A
  to XFD, default A; rows with no cell from that column on are skipped); both need `sheet`. Every
  sheet with rows left reports `rowsTruncated:true` and `nextStartRow`, the row number to page on
  with; unselected sheets report their counts and `nextStartRow` = their first row.
- A cell value is cut at 4,000 characters (`valueTruncated`); `complete` is false when anything was
  cut. `workbookHash` (`cells:sha256:<hex>`) always covers every cell of every worksheet, whatever was
  returned. `probes` is used only by the server's own planning and verification reads.
- Every TIA Portal confirmation dialog raised during a read's export is answered **No** (a read never
  accepts) and fails the read, because the export may then be incomplete.

### `plc_write`

Every write takes optional `portalConfirmations` ([below](#tia-portal-confirmation-dialogs)).

| Operation | Fields | What it does |
| --- | --- | --- |
| `update_text_list` | `textListName`; optional `plcName`, `newName`, `listRange`, `comment` + `commentCulture` | Renames first and reads the name back (TIA Portal may ignore a colliding rename silently, as observed for tags), then sets `ListRange`, then the comment item of the culture. A new name already used by another user or a system list (case-insensitive, as TIA Portal compares) is refused at planning; writability per `GetAttributeInfos`, the enum member and the culture item are all checked before the first change; every member is read back and a failure names the members already applied. |
| `delete_text_list` | `textListName`; optional `plcName` | `PlcAlarmUserTextlist.Delete()`, then checks the list is gone. Info guard `plc_deletes_text_list`. |
| `update_text_list_cells` | `cellEdits`, `expectedWorkbookHash`, `textListNames` (user lists only); optional `plcName`, `cultures`, `importOption` | Workbook round trip (below) with the filtered `ExportToXlsx(file, textListNames, languages)` and `ImportFromXlsx(file, importOption)`. |
| `update_alarm_text_cells` | `exportOption`, `cellEdits`, `expectedWorkbookHash`; optional `plcName`, `cultures` | Round trip with `ImportInstanceTextsFromXlsx(file, languages)`. |
| `update_supervision_cells` | `blockPath`, `cellEdits`, `expectedWorkbookHash`; optional `importOption` | Round trip with `ImportSupervisionsFromXlsx(file, importOption)`. |
| `export_text_lists` | `filePath` (`.xlsx`); optional `plcName`, `overwrite`, `textListNames`, `cultures` | `ExportToXlsx` to the caller's file. |
| `export_alarm_texts` | `filePath` (`.xlsx`), `exportOption`; optional `plcName`, `overwrite`, `cultures` | `ExportInstanceTextsToXlsx` to the caller's file. |
| `export_alarm_classes` | `filePath` (extension `.DAT`); optional `overwrite` | `AlarmClassDataProvider.Export`. Standalone only. |
| `export_supervisions` | `blockPath`, `filePath` (`.xlsx`); optional `overwrite` | `ExportSupervisionsToXlsx`. |
| `import_text_lists` | `filePath`; optional `plcName`, `importOption` | `ImportFromXlsx` of a private copy, then a verification re-export. Info guard `plc_imports_alarm_data`. |
| `import_alarm_texts` | `filePath`; optional `plcName`, `cultures`, `exportOption` (for the verification re-export) | `ImportInstanceTextsFromXlsx`; without `exportOption` the import is not read back (`trusted` with a warning). |
| `import_alarm_classes` | `filePath` | `AlarmClassDataProvider.Import`, then a re-export compared byte for byte. Standalone only. |
| `export_system_diagnostics_settings` | `filePath` (any extension); optional `overwrite` | `SystemdiagnosticsSettingsDataProvider.Export(FileInfo)`. Standalone only; see [System diagnostics settings](#system-diagnostics-settings). |
| `import_system_diagnostics_settings` | `filePath` | `SystemdiagnosticsSettingsDataProvider.Import(FileInfo)`, then a re-export compared byte for byte. Standalone only. |
| `import_supervisions` | `blockPath`, `filePath`; optional `importOption` | `ImportSupervisionsFromXlsx`, then a verification re-export. |
| `import_supervision_settings` | `blockPath`, `filePath`; optional `importOption` | `ImportSupervisionSettingsFromXlsx`; not read back (no documented settings export; `trusted` with a warning). |
| `set_prodiag_assignment` | `proDiagFb`; the target of `get_prodiag_assignment` | `SetAttribute("AssignedProDiagFB", <FB name>)`, read back by FB name ([ProDiag](#prodiag)). The FB must be an FB of the same PLC; one whose language is not ProDiag is passed on with a warning. |
| `export_prodiag_info` | `blockPath` (ProDiag FB), `directoryPath` (must not exist) | `CodeBlock.ExportProDIAGInfo(DirectoryInfo)` into a temporary sibling directory that is then moved into place; every file is listed with size and SHA-256. An export that writes no file fails `postcondition_failed` and creates no directory. A non-ProDiag or inconsistent FB is passed on with a warning (the documented sample exports consistent ProDiag FBs). |

`library_write` gains two documented kinds: `create_master_copy` source kind `textList` (`path` = the
user text list name, optional `plcName`; `MasterCopies.Create(alarmTextList)` from the official
skill) and `instantiate_master_copy` target kind `textLists` (`plcName`;
`PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)`). That is the documented way
to **create** a user text list; see the [library reference](LIBRARY_OPERATIONS_SUMMARY.md).

`importOption` is `None` or `Override` (default `Override`, the option every import sample in the
sources passes; the sources state that `ActivateInactiveCultures`/`SkipInactiveCultures` are not
supported for the text list import). `exportOption` is a `PlcAlarmTextXlsxExportOption` member name:
the sources name the type but not its members, so the installed TIA Portal decides; an unknown name
fails with the installed member list. `listRange` is likewise a member name of the runtime type of
the list's current `ListRange` value, as `list_text_lists` reports it.

## System diagnostics settings

> **Not live-tested** (GitHub issue #19). Evidence: the V21 IntelliSense file `Siemens.Engineering.Base.xml`,
> `M:Siemens.Engineering.HW.Systemdiagnostics.Settings.SystemdiagnosticsSettingsDataProvider.Export(System.IO.FileInfo)` and
> `.Import(System.IO.FileInfo)` ("export/import system diagnostics project relevant settings", both returning
> `SystemdiagnosticsSettingsExportImportResult`), `P:...SystemdiagnosticsSettingsExportImportResult.State` and the enum
> `SystemdiagnosticsSettingsExportImportResultState` with `Success` ("Succesful process"), `Warning` ("Succesful process with
> warnings") and `Error` ("Failed process").

`export_system_diagnostics_settings` and `import_system_diagnostics_settings` are project-wide `plc_write` items of this family
(`ProjectMutation`, read-write and full; no `plcName`; own standalone-only capability rows, a bound local session is refused at
planning), handled exactly like the alarm classes: `plc_alarm_plan` binds everything first, the export is staged next to the
target, moved into place, re-hashed and never replaces a file without `overwrite:true`; the import runs on a private copy and
is verified by re-exporting and comparing the files byte for byte (`importCheck.method` `fileHash`; a difference is
`inconclusive`, because the format is undocumented); `portalConfirmations` answers TIA Portal dialogs.

- **Provider.** The XML does not say which object offers the provider; the worker takes
  `Siemens.Engineering.HW.Systemdiagnostics.Settings.SystemdiagnosticsSettingsDataProvider` from the Project's
  `GetServiceInfos()` by exact full name and retrieves it with `IServiceProvider.GetService(Type)`; not listed or not returned
  is `unsupported_capability` before anything runs.
- **Binding.** Neither the provider nor the result type is in the compile stubs: `Export(FileInfo)` and `Import(FileInfo)` are
  bound on the live provider by exact name and parameter, `State` on the bound method's return type
  (`PlcAlarmResultReader.BindStateOnly`; the XML documents no other result member); a missing one is `unsupported_capability`.
- **States.** `Success` succeeds; `Warning` succeeds with a warning (the XML calls it a successful process with warnings, as
  for the alarm classes); `Error`, any other name and an unreadable state fail `worker_operation_failed`.
- **File extension (K46).** TIA Portal V21 showed a modal dialog for an alarm-class file with the wrong extension. For the
  system diagnostics settings neither the XML nor a Siemens sample names an extension, so none is required or refused; which
  one TIA Portal expects is unknown until a live test.
- **Live acceptance still required:** that the Project lists the provider, the extension TIA Portal writes and accepts, the
  result state of a normal export and import, and whether a re-export after an import is byte-identical.

## Planning (`plc_alarm_plan`)

Every alarm write item first runs the worker's non-mutating `plc_alarm_plan` (Observe, worker-only,
gated as `plc_write`), also in a dryRun: it resolves the PLC, FB, list or tag and binds every Siemens
member the item will call (provider members by documented name and argument types, result members on
the call's declared return type) **without calling any of them**, so a member or a result `State` that
does not bind fails `unsupported_capability` before anything runs. It reports the caller path as the
worker's file system sees it (below). When an earlier item of the same call writes to the same PLC, a
plan refusal such an item can cause (a list it creates, renames or deletes) does not fail planning;
the worker repeats every check at mutation. A cell update then reads the same workbook once (the
probe below).

## System text lists

System text lists are read like user lists (`list_text_lists`, `read_text_list_workbook`,
`export_text_lists` with their names). They are never written through a list operation or a cell
update: `update_text_list` and `delete_text_list` refuse them, and `update_text_list_cells`
**requires** `textListNames` (`validation_error` at validation) and refuses a system list in it
(`validation_error` at planning against the inventory and again in the worker). A cell update therefore always uses the
filtered `ExportToXlsx(file, textListNames, languages)` overload with user list names only, so the
workbook it imports again holds no system list rows. Its `expectedWorkbookHash` comes from a
`read_text_list_workbook` with the same `textListNames` (and `cultures`); an unfiltered read's hash
never matches (`state_changed`). The server does not fill in every user list name by itself: an
unfiltered read would then never match a cell update's hash. **What the server cannot see:** a file
import (`import_text_lists`) may contain system text list rows; the server does not interpret the
layout, so it cannot tell which rows belong to a system list, and TIA Portal's import decides what it
accepts.

## Workbook cells

The server does not know or interpret TIA Portal's XLSX column layout. It reads the Office Open XML
package (ISO/IEC 29500) through its documented parts only: package relationships, the workbook and
its relationships, the shared string table and the worksheets. Cell kinds are `string` (shared,
inline or formula-free `str` text), `number`, `boolean`, `error`, `formula` (cached value) and
`date`. The `_xHHHH_` escape is decoded. Chart and dialog sheets are skipped with a message. Missing
parts, unknown cell types, a cell position listed twice, parts that leave the package, DTDs, more than
256 MB decompressed (every part read **or copied** is charged by its declared size, and no part may
decompress to more than it declares) or more than 2,000,000 cells fail closed
(`worker_operation_failed`).

A cell edit `{ sheet, cell, expected, value, kind? }` addresses a cell exactly as the read returned
it (sheet names compare case-insensitively when unique; A1 references in upper case). `expected` is
the current value ("" for an empty cell); `kind` is `string` or `number` (default: the current kind,
`string` for an empty cell). A value may hold only characters XML 1.0 carries
(`XmlConvert.IsXmlChar`, surrogate pairs through `IsXmlSurrogatePair`; U+FFFE, U+FFFF, unpaired
surrogates and control characters other than tab, CR and LF are refused); the same rule applies to a
text list comment. Empty cells may be written, which adds values and rows (for example a translation
in a new culture column or a new text list entry, following the header the read returned). Formula,
error, boolean and date cells are refused. The same cell cannot be edited twice in one item.

A patch writes out implicit row and cell positions exactly as the reader derives them (a row without
`r` follows the previous row, a cell without `r` the previous cell) and refuses rows or cells that are
not in strictly ascending order (`validation_error`; ISO/IEC 29500 requires the order and a new cell's
place would be ambiguous). An existing row or cell is searched among all of them before a new one is
inserted in order. A patch rewrites only the edited worksheets and the shared string table whenever an
item was appended or `count`/`uniqueCount` changed (a number written over a text cell lowers `count`),
reusing an identical plain item; inline strings are written when the workbook has no shared string
table; the worksheet `dimension` is updated. Every other part, including the custom document property
TIA Portal checks on import (`FileContent`), keeps its **content** unchanged; the package itself is
written anew and recompressed, so the container bytes differ.

### Cell update round trip

1. Planning (also with `dryRun:true`): `plc_alarm_plan`, then one probe read of the same workbook
   (same `plcName`, `blockPath`, `textListNames`, `cultures`, `exportOption`); a different
   `workbookHash` fails `state_changed`; a missing sheet, a refused cell kind or a cell whose current
   value differs from `expected` fails `validation_error`. The effect lists every edit as `changes[{
   field: "Sheet!Cell", current, requested }]` and fires the info guard `plc_workbook_round_trip`.
2. Mutation (worker), all inside one `TiaPortal.ExclusiveAccess`: export again, check the hash again
   (`state_changed`, nothing imported), patch (refusals: nothing imported), import the patched
   workbook, re-export. Every edited cell must show (numbers by value), and **no other cell may have
   changed**: the whole before and after workbooks are compared, and any unrequested change fails
   `postcondition_failed` with the cells listed in `result.unrequestedChanges` (at most 50,
   `Sheet!Cell: before -> after`). The result carries `workbookHashBefore`, `workbookHashAfter` and
   every edit with the value the re-export shows.
3. Verification: the item re-reads its cells (`check: "cells"`) and reports the new workbook hash as
   `contentHash`, the next `expectedWorkbookHash`.

`ExclusiveAccess` signals a controlled activity to TIA Portal and blocks input in that TIA Portal's
UI while it lasts (a second `ExclusiveAccess`, for example of another tool, fails the item before
anything changed). No transaction is used: whether the XLSX imports may run inside one is not
documented (compile and ProjectText imports may not). **Remaining window:** between the planning probe
and the mutation the hash is checked again inside the exclusive scope, so a change in that window is
refused; `ExclusiveAccess` is not documented to stop other Openness clients attached to the same TIA
Portal, or a Multiuser server, from changing the project during the round trip; such a change would
show as an unrequested change and fail the item, with the project changed.

A cell update may not follow another item of the same call that can change the same PLC's project
(any item except an export, of the same or an unknown PLC; project-wide alarm-class imports count as
unknown): its `expectedWorkbookHash` cannot include that change. Run it in its own call after reading
again.

## Files

**Host check (lexical).** `filePath` (and `directoryPath`) is checked by the shared
`LocalFilePaths` (Contracts), which `export_io_map` uses too, without touching the file system: an
absolute path on a drive (`X:\...` or `X:/...`) or a UNC share (`\\server\share\...`, allowed since 2026-11-01),
naming a file (`.xlsx`; alarm classes `.DAT`: live on V21 2026-11-02 an `.xml` name made TIA Portal show the modal error 0406:000014 "invalid file extension" and the call waited on it), at most 1,024 characters; refused are device or NT
object paths with either separator (`\\?\`, `\\.\`, `\??\`, `/??/`), `.`/`..` and empty segments, a
`:` after the drive (alternate data streams), a reserved device name in any segment (CON, PRN, AUX,
NUL, COM0-9, LPT0-9 and their superscript-digit forms, CONIN$, CONOUT$; with or without an
extension), a segment ending in a space or a dot, wildcards and control, format (bidi) or separator
characters. **Mapped network drives are allowed** (owner decision).

**Worker evidence.** `plc_alarm_plan` reports the normalized full path, whether it and its parent
exist, the root's `DriveInfo.DriveType` and every existing ancestor directory that is a link (reparse
point). Planning refuses a missing parent directory, a directory at a file path, an existing export
target without `overwrite:true`, an existing target that is itself a link (it is never replaced), a
missing import file (`target_not_found`) and an existing `export_prodiag_info` directory. A path on a
mapped network drive or below a link is **allowed** and fires the info guard
`plc_file_on_network_or_link` with the normalized path; the worker repeats the check at mutation and
adds a warning to the item.

An export is written by TIA Portal to a temporary file `.<name>.<guid>.tmp<extension>` in the target
directory; only after TIA Portal reports success is it moved into place (`File.Move`, or
`File.Replace` with `overwrite:true`, the link check repeated right before), read back and hashed
(`bytes`, `sha256`). An existing file, also one created meanwhile, is replaced only with
`overwrite:true` (info guard `plc_export_overwrites_file`). Any failure deletes the temporary file.
Verification re-hashes the file (`check: "file"`). File content is never returned; result log paths
are named, not read. `export_prodiag_info` works the same way with a temporary sibling directory
`.<name>.<guid>.tmp` (it never merges into or replaces an existing directory: the exported file names
are not documented) and is verified by re-hashing every listed file (`check: "directory"`). An
export that returns without writing a file fails `postcondition_failed`; its empty temporary
directory is removed and `directoryPath` is not created.

**Imports** never hand the caller's file to TIA Portal: it is copied into this server's private
temporary workbook directory (same file name), hashed (`result.bytes`, `result.sha256` are the
copy's), imported and compared from that copy, so a change of the caller's file during the import
has no effect. The copy is deleted with the directory.

**Leftovers.** A worker that crashed or timed out can leave a temporary export file, export directory
or workbook directory behind. The next export to the same file deletes that file's
`.<name>.<32 hex>.tmp<ext>` leftovers, and the next `export_prodiag_info` to the same directory its
`.<name>.<32 hex>.tmp` directories (exactly those patterns, the GUID in lower-case hex as this server
writes it, name and extension case-insensitive), once they are at least two hours old, with a
warning; every alarm run deletes this server's `%TEMP%\tia-mcp-alarm-<purpose>-<32 hex>` directories
older than two hours. Links are never followed.

## Imports and their verification

An import reads an existing local file (through a private copy, [above](#files)). What it replaces,
adds or keeps is TIA Portal's decision (info guard `plc_imports_alarm_data`). Right after it, inside
the same `ExclusiveAccess`, the worker re-exports the same kind where a documented export exists and
reports `result.importCheck { method, outcome, verified, reason, workbookHashAfter, importedRows,
rowsNotShown, notShown[], importedSha256, reexportSha256 }`:

- `rows` (text lists, alarm texts with `exportOption`, supervisions): every data row of the imported
  workbook (below each sheet's first row, the header) must appear in the same-named sheet of the
  re-export, wherever the row is. Columns are matched by the **header text** (the n-th equal header to
  the n-th), not by position, so a re-export with columns in another order or with extra columns (for
  example more languages) still compares; values compare exactly, numbers by value, empty and absent
  cells alike (trailing empty cells do not matter). Imported columns whose header the re-export lacks,
  sheets it lacks and values in columns without a header are listed as not compared.
- `fileHash` (alarm classes): the re-export must be byte-identical to the imported file.
- `none` (supervision settings: no documented settings export; alarm texts without `exportOption`).

`outcome` and the verification entry:

| `outcome` | When | Verification entry |
| --- | --- | --- |
| `verified` | every compared row shows and nothing was left uncompared, or the alarm-class file is identical | `importedRows` / `fileHash`, success true |
| `contradicted` | a compared imported row is absent from the re-export (TIA Portal did not keep it as imported, for example `ImportOptions.None` kept an existing entry, or it normalised a value) | `notVerified`, success **false** (root `success` false) |
| `inconclusive` | every compared row shows but something could not be compared (a layout difference), the alarm-class file differs (an equivalent file may be written differently), or the verification export failed, reported a non-success or unreadable state, wrote no file or an unreadable workbook | `trusted`, success true, with the reason |
| `notReadBack` | no documented export (`none`) | `trusted`, success true, message `not read back: ...` |

In every case the item itself succeeded and **the import has changed the project**; every outcome
other than `verified` adds a warning saying so and naming why. A Siemens failure of the verification
export (a recoverable `EngineeringException`) is caught in the worker and becomes an `inconclusive`
outcome, never a failed import; a lost TIA Portal (a `NonRecoverableException`, or a Portal disposal
observed during the export) still fails as `portal_lost`.

## ProDiag

`get_prodiag_fb` reads what blocks.md documents about a ProDiag FB: `ProgrammingLanguage` (compared
with the member name `ProDiag` from the documented sample, `isProDiag`), `FB.Supervisions.Count`
(`SupervisionComposition`; a `Supervision` has no documented member, so the count is presence
evidence only), `PlcBlock.IsConsistent`, whether `GetService<SupervisionProvider>()` returns a
provider, and the attributes `ProDiagVersion`, `InitialValueAcquisition`, `UseCentralTimeStamp`
through `GetAttribute` (unreadable ones are listed). The supervision operations report the same
language and count, and an FB without `SupervisionProvider` fails `unsupported_capability` naming both.

`AssignedProDiagFB` is read and set on the three objects blocks.md names: an instance DB
(`blockPath`), a UDT-instance member of a global DB (`blockPath` + `memberPath`, found with
`Interface.Members.Find`) and a tag (`tableName` + `name`, optional `folderPath` and `plcName`).
blocks.md restricts the attribute to UDT instances in global DBs and tag tables; on another object
TIA Portal decides (`readable:false` with its message, or a rejected write). The value is read **by FB
name**: `GetAttribute` returning a string is the name, returning a block uses its `Name`, null or ""
is unset; any other value type, or a recoverable Siemens failure, is `readable:false` with the reason.
`set_prodiag_assignment` writes the FB's name as TIA Portal spells it and reads it back: a value
unreadable before the write counts as unset (with a warning); a readable different name fails
`postcondition_failed`; an unreadable read-back leaves `assignedProDiagFbAfter` null with a warning
and the verification (`proDiagAssignment`, a fresh `get_prodiag_assignment`) reports `notVerified`
with success false. A DB member path may index arrays (`memberPath` `UdtArray[0].Valve`, passed to
`Interface.Members.Find` as blocks.md shows for `ArrayUdtMember.ArrayUdtMember[0]`). Clearing an
assignment is not documented and not offered.

## Languages and multilingual texts

Cultures are resolved in `LanguageSettings.Languages`, the catalogue, matched by
`Language.Culture.Name` (case-insensitive, exactly one), because the documented filtered
`ExportToXlsx` sample finds its languages there (`project.LanguageSettings.Languages.Find(...)`); a
culture that is not a TIA Portal language fails `target_not_found` with the active list, and one that
is not active in the project is passed on with a warning (what TIA Portal does with it is its
decision). `list_text_lists` reports the active, editing and reference languages. Omitted `cultures`
means every active language: for alarm texts every active `Language` is passed, for a text list read
or export without `textListNames` the unfiltered `ExportToXlsx(FileInfo)` overload ("all activated
project languages") is used (the workbook's `cultures` is then empty), and with `textListNames` (always
for `update_text_list_cells`) the filtered overload gets every active `Language`. The filtered text list export takes names and languages
together, so `cultures` narrows a text list workbook only together with `textListNames`; the server
does not invent a name list, and it passes the names as TIA Portal spells them. A text list comment is
`MultilingualText.Items`, one `{ culture, text }` per item; `update_text_list` writes the item of
`commentCulture` and refuses a culture without an item (activate the language first).

## TIA Portal confirmation dialogs

Every write runs inside a confirmation scope like `plc_download`, `plc_station_upload` and
`library_write`: until the item's evidence is collected, every TIA Portal `Confirmation` dialog is
answered by the item's `portalConfirmations` instead of the session-wide handler, and recorded in
`result.portalConfirmations[{ type, text, answer }]` (at most 100) with `portalConfirmationsObserved`,
`portalConfirmationsComplete` and `portalConfirmationPolicy`. `decline` (default) answers No; a
declined dialog fails the item (`worker_operation_failed`, the result kept), because declining may
have changed what the call did. `accept` answers Yes (never in read-only, where the session cannot
accept) and adds a warning. A dialog raised after the evidence was collected keeps the call's answer
and is reported as a warning on the next response. Workbook reads always answer No and fail when they
had to.

## Result states and member binding

The result objects' members (`State`, `LogFilePath`, `ErrorCount`, `WarningCount`, `Messages`) are
bound on the call's declared return type before the call (`PlcAlarmResultReader.Bind`; for the
runtime-bound alarm text methods on `MethodInfo.ReturnType`), the most derived declaration of a name
winning, so a hiding redeclaration never raises `AmbiguousMatchException`. Only a state member named
exactly `Success` succeeds (the sources name `Success` for the alarm-class and supervision results);
`Warning` is accepted, with a warning, for the alarm classes, whose source names Success / Warning /
Error. Any other name, and an unreadable state, fails the item `worker_operation_failed` with the
state, the error count, the log file path and the first message; an export then writes nothing to the
target, an import says the project may be partly changed.

## Guards, verification, superseding

| Guard | Severity | When |
| --- | --- | --- |
| `plc_state_unverifiable` | info (TIA Portal decides) | incomplete text list inventory for an update/delete, or a workbook that cannot be exported for a cell update |
| `plc_deletes_text_list` | info | `delete_text_list` |
| `plc_workbook_round_trip` | info | every `update_*_cells` |
| `plc_imports_alarm_data` | info | every import |
| `plc_export_overwrites_file` | info | an export with `overwrite:true` onto an existing file |
| `plc_file_on_network_or_link` | info | the caller file or directory is on a mapped network drive or below a link |

Verification checks: `textList` (update/delete re-list), `cells`, `file`, `directory`,
`proDiagAssignment` (`notVerified` when unreadable), and for imports `importedRows`, `fileHash`,
`notVerified` (contradicted) or `trusted` (inconclusive, not read back). Superseding keys: a
text list item owns `<PLC>/alarm/textlists/<name>`; a whole-workbook text list item (import, cells)
owns `<PLC>/alarm/textlists/#workbook` and offers `<PLC>/alarm/textlists` (it supersedes earlier list
items and workbook items); a list item offers its workbook key as a later item; alarm texts own
`<PLC>/alarm/texts`, supervisions `<PLC>/alarm/supervision/<blockPath>`, an assignment
`<PLC>/alarm/prodiag/<target>`, alarm classes `project/alarmclasses`, an export only `file/<path>`
(scope-free and canonical: the full path, `/` as `\`, no trailing separator, compared
case-insensitively, so the same file is one object whichever PLC or spelling names it). A
later block or type change on the PLC (`delete_block`, `delete_block_group`, `rename_block_group`,
`update_block_logic`, `update_type_content`, `rename_block`, `renumber_block`, `move_block`,
`copy_block`, `create_block`, `create_instance_db`, `generate_blocks_from_source`, `delete_software_unit`,
`rename_software_unit`, `delete_type_group`, `add_interface_member`, `update_interface_member`,
`delete_interface_member`, `update_db_member`) offers `<PLC>/alarm/texts`, `<PLC>/alarm/supervision` and `<PLC>/alarm/prodiag`:
a block may own alarm instance texts, supervisions or an assignment, so it conservatively supersedes
earlier alarm-text, supervision and assignment items of that PLC (text list items stay). A later
`delete_tag`, `update_tag`, `move_tag`, `delete_tag_table`, `move_tag_table`, `delete_tag_table_group` or
`rename_tag_table_group` offers only
`<PLC>/alarm/supervision` and `<PLC>/alarm/prodiag`: a tag can carry AssignedProDiagFB (blocks.md), so
its FB supervises it; alarm instance texts are not documented to reference tags (alarms.md names
none), so tag changes do not supersede alarm-text items. Update and delete targets are
resolved at planning (`target_not_found`, `target_ambiguous`, system list: `validation_error`); after an
earlier import or cell update of the same call a missing list becomes a dependent item that is
re-planned before its mutation.

## Failure categories

Validation and selector errors are canonical root errors before anything runs. Worker categories:
`target_not_found`, `target_ambiguous`, `target_kind_unsupported` (a block that is not an FB or DB),
`unsupported_capability` (a provider or member the installed TIA Portal does not offer, a result
whose `State` does not bind, a non-writable attribute; nothing changed; also the alarm classes in a
local session, refused at planning like the worker gate refuses them), `state_changed`,
`validation_error`, `postcondition_failed`, `worker_operation_failed` (also a refused
`ExclusiveAccess` and declined confirmation dialogs); a Siemens `EngineeringTargetInvocationException`
during a write is `engineering_rejected` with `engineeringError`, as for every PLC write. A failed write
item carries the result collected until the failure, with its confirmation record.

## Provenance of every Siemens member used

Sources (read-only reference data, never executed): **(A)** `totally-integrated-claude`
`skills/tia-plc-operations/references/alarms.md` (V21 API reference); **(SV)** `.../supervision.md`;
**(BL)** `.../blocks.md` (section 12, ProDiag-FB, and the API reference); **(X)**
`skills/tia-import-export/references/plc-alarms-and-tags.md` (V21 code samples); **(L)**
`skills/tia-project-general/references/language-settings.md`; **(C)**
`skills/tia-csharp-common/SKILL.md` (exclusive access, transactions); **(SK)** Siemens
`tia-portal-ai-extensions/openness_development/skills/libraries-and-alarms/SKILL.md`; **(B)** the V21
type-name baseline `api-baselines/v21/step7.json` (type names and assemblies only).

Status: **compiled** (declared in the Step7 stubs `Alarms.cs` or `Blocks.cs`, pinned by
`PlcAlarmReferenceStubTests` and the probe `AlarmsReferenceSurface.cs`); **runtime** (the source names
the member and its argument types but not its return type: bound by exact name and argument types
through `LibraryRuntimeBinding` before the call, exactly one applicable public member or
`unsupported_capability`); **runtime property** (named without a type: the most derived public
property of that exact name, else `IEngineeringObject.GetAttribute`; unreadable members are
reported); **generic** (documented `IEngineeringObject`/`IEngineeringServiceProvider` API);
**existing** (an unchanged existing stub).

| Siemens member | Source | Status |
| --- | --- | --- |
| `PlcSoftware.PlcAlarmTextlistGroup` → `PlcAlarmTextlistGroup` | SK (`plcSoftware.PlcAlarmTextlistGroup.PlcAlarmUserTextlists`), A, B | compiled (one line in `Software.cs`) |
| `PlcAlarmTextlistGroup.PlcAlarmUserTextlists` / `.PlcAlarmSystemTextlists` | A ("Navigator of ... composition"), SK, B | compiled |
| `PlcAlarmUserTextlistComposition`, `PlcAlarmSystemTextlistComposition` : `IEnumerable<T>` (`GetEnumerator`) | A, B | compiled |
| `PlcAlarmTextlist` (base of user and system lists) | A ("Base class for user and system text lists"), B | compiled (type only) |
| `PlcAlarmUserTextlist.Name` (string) | SK (`x.Name == "USER_1"`), A | compiled |
| `PlcAlarmUserTextlist.Delete()` | A | compiled |
| `PlcAlarmTextlist.ID`, `.ListRange`, `.Comment`; system list `Name` | A (types not given) | runtime property |
| `ListRange` enum members | A names `PlcAlarmTextlistListRange` without members | runtime type of the current value; parsed by exact name |
| `IEngineeringObject.GetAttributeInfos/SetAttribute("Name"/"ListRange")` | generic; L (attribute API) | generic, existing (rename through `LibraryReflection.Rename`) |
| `MultilingualText.Items`, `MultilingualTextItem.Language/Text` | L | existing |
| `ProjectBase.LanguageSettings.Languages/ActiveLanguages/EditingLanguage/ReferenceLanguage`, `Language.Culture` | L, X (`LanguageSettings.Languages.Find`; matched by enumeration) | existing |
| `PlcSoftware.GetService<PlcAlarmTextListProvider>()` | X | compiled (provider declared as `IEngineeringService`) |
| `PlcAlarmTextListProvider.ExportToXlsx(FileInfo)` → `TextListXlsxResult` | X, A | compiled |
| `PlcAlarmTextListProvider.ExportToXlsx(FileInfo, IEnumerable<string>, IEnumerable<Language>)` → `TextListXlsxResult` | X, A | compiled |
| `PlcAlarmTextListProvider.ImportFromXlsx(FileInfo, ImportOptions)` → `TextListXlsxResult` | X, A | compiled |
| `TextListXlsxResult.State`, `.LogFilePath` | A (types not given) | runtime property, bound on the declared type before the call |
| `PlcSoftware.GetService<PlcAlarmTextProvider>()` | SK | compiled |
| `PlcAlarmTextProvider.ExportInstanceTextsToXlsx(FileInfo, IEnumerable<Language>, PlcAlarmTextXlsxExportOption)` | A (return type not given) | runtime (bound before the call; result members on `ReturnType`) |
| `PlcAlarmTextProvider.ImportInstanceTextsFromXlsx(FileInfo, IEnumerable<Language>)` | A (return type not given) | runtime (as above) |
| `PlcAlarmTextXlsxExportOption` | A, B (no members) | compiled enum without values; parsed by name at runtime |
| result `State`, `LogFilePath` of the alarm text methods | A (`PlcAlarmTextXlsxResult` type) | runtime property |
| `Project.GetService<AlarmClassDataProvider>()` | X | compiled (called on the `ProjectBase` root; refused unless it is a `Project`) |
| `AlarmClassDataProvider.Export(FileInfo)`, `.Import(FileInfo)` → `AlarmClassExportImportResult` | X, A | compiled |
| `AlarmClassExportImportResult.State/ErrorCount/WarningCount/Messages`, message `.Message/.State` | X, A (types not given) | runtime property |
| `FB` via `IEngineeringServiceProvider.GetService<SupervisionProvider>()` | X (`proDiagBlock.GetService<SupervisionProvider>()`) | generic, compiled provider |
| `SupervisionProvider.ExportSupervisionsToXlsx(FileInfo)` → `SupervisionXlsxResult` | X, SV | compiled |
| `SupervisionProvider.ImportSupervisionsFromXlsx(FileInfo, ImportOptions)`, `.ImportSupervisionSettingsFromXlsx(FileInfo, ImportOptions)` → `SupervisionXlsxResult` | X, SV | compiled |
| `SupervisionXlsxResult.State`, `.LogFilePath` | SV (types not given) | runtime property |
| `ImportOptions.None/Override` | X | existing; parsed by name at runtime |
| `FB.Supervisions` → `SupervisionComposition` (`Count`, `IEnumerable<Supervision>`, `Parent`) | BL (`fb.Supervisions.Count > 0`; API reference), B | compiled (`Blocks.cs`) |
| `Supervision` | BL, B (no members) | compiled (type only, never read) |
| `PlcBlock.ProgrammingLanguage` compared with the member name `ProDiag` | BL (`fb.ProgrammingLanguage == ProgrammingLanguage.ProDiag`, `CreateFB(..., ProgrammingLanguage.ProDiag)`) | existing property; member compared by name |
| `PlcBlock.IsConsistent` (bool) | BL | runtime property (evidence only) |
| ProDiag-FB attributes `ProDiagVersion`, `InitialValueAcquisition`, `UseCentralTimeStamp` | BL (GetAttribute/GetAttributes table) | generic `GetAttribute` |
| `AssignedProDiagFB` on instance DB, DB member, tag (`GetAttribute`/`SetAttribute` with the FB name) | BL (sections "Assign ProDiag-FB to IDB", "AssignedProDiagFB on DB member and Tag") | generic |
| `DataBlock.Interface.Members.Find(string)` | BL (sample) | existing |
| `PlcTagTableGroup.Groups/TagTables.Find`, `PlcTagComposition.Find` | existing (tag operations) | existing |
| `CodeBlock.ExportProDIAGInfo(DirectoryInfo)` | BL (return type not given; the sample ignores it) | runtime (bound before the call, result ignored) |
| `TiaPortal.ExclusiveAccess(string)` | C | existing |
| `PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)` | A | runtime (library binding; not declared) |
| `MasterCopyComposition.Create(IMasterCopySource)` with a user text list | SK (`MasterCopies.Create(alarmTextList)`) | existing; the list is checked as `IMasterCopySource` at runtime |

Every declared type is in the Step7 baseline (B) and only in the Step7 reference. The stub enums
declare no member. After any merge that touches `reference-stubs/`, regenerate `ref/*.dll` with
`pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -Update`.

### Left out, and why

| Operation | Reason |
| --- | --- |
| Create a user text list by name | No documented create member: `PlcAlarmUserTextlistComposition` documents only `CreateFrom(MasterCopy[, MasterCopyMode])`, offered through `library_write`. An XLSX import may create lists; that is TIA Portal's import behaviour, not a documented contract. |
| Typed text list entries (value ranges and texts as objects): read, add, update, delete | No entry type or entry composition is documented. Entries are read and changed as cells of TIA Portal's own XLSX export/import, without the server interpreting the layout. |
| Delete text list entries | Removing rows and importing is not documented to delete entries (the import semantics are TIA Portal's) and row removal would rewrite every following reference; not offered. Texts can be cleared with a cell edit; a whole list can be deleted. |
| Change system text lists | Read-only by owner rule; refused before anything runs (see [System text lists](#system-text-lists) for what a file import can still contain). |
| List, read, create, update or delete individual alarm classes | V21 Step7 documents no alarm class object model, only `AlarmClassDataProvider.Export/Import` of the common alarm classes; the exported format is not documented, so the server neither reads nor patches it. |
| Program alarms (Program_Alarm instances) as objects | No program-alarm type is in the V21 baseline: `Siemens.Engineering.SW.Alarm` holds only providers, results, exceptions and text lists. Alarm instance texts are reachable only through the XLSX export (`read_alarm_text_workbook`). |
| Supervisions as objects (`FB.Supervisions` items) | `Siemens.Engineering.SW.Blocks.Supervision` and `SupervisionComposition` are in the baseline and blocks.md, but `Supervision` has no documented member; only `Count` is read (`get_prodiag_fb`). Supervisions are read and changed through the supervision workbook. |
| Write the ProDiag-FB attributes (`ProDiagVersion`, `InitialValueAcquisition`, `UseCentralTimeStamp`) | blocks.md lists them for GetAttribute/GetAttributes only and notes that `SetAttributes()` cannot set more than one attribute of a ProDiag FB; no write is documented. They are read. |
| Clear an `AssignedProDiagFB` assignment | Only `SetAttribute(..., prodiag.Name)` with an FB name is documented; what an empty value does is not. |
| Create a ProDiag FB | Documented (`PlcBlockComposition.CreateFB(name, isAutoNumber, number, ProgrammingLanguage.ProDiag)`), but block creation is the block domain's: `create_block` creates blocks by generated source import (LAD, FBD, STL, SCL, GRAPH) and `CreateFB` is not declared there; adding it belongs to that domain and its review, not to this branch. The instance DB of a ProDiag FB is created with the existing `create_instance_db` (`CreateInstanceDB`, as the documented sample does). |
| `export_prodiag_info` into an existing directory | The exported file names are not documented, so merging into or replacing a directory could overwrite or mix files; a new directory is required. |
| Supervision settings DAT export/import (`SupervisionSettingsProvider.Export/Import`) | The sources document the members but not where the provider is obtained (no `GetService` owner); not guessed. The XLSX settings import is offered (not read back). |
| Software-unit text lists and alarm texts | The sources say the providers' parent "can be Plc or Unit" but document no unit entry point; the PLC selectors of `plc_read`/`plc_write` address PLCs. |
| `TextListNotFoundException` handling | Its base type is not documented; it surfaces through the shared exception classification. |
| Change project languages (activate, editing/reference language) | Project settings, not PLC alarm data; reported read-only in `list_text_lists`. |
| A transaction around imports and cell updates | Not documented as compatible with the XLSX imports (compile and ProjectText imports are documented as not allowed in one); only `ExclusiveAccess` is used. |
| Reporting whether two consecutive exports hash equally (volatile cells) | It would cost a second export on every read; left to the live acceptance checks. A volatile cell would make every cell update fail `state_changed` or `postcondition_failed`, never apply silently. |
| Online alarm acknowledgement | Exists over the S7-1500 Web API (`plc_online_write acknowledge_alarm`). |
| HMI alarms and HMI text lists | WinCC Unified alarms are `hmi_read`/`hmi_write`; HMI text lists are a separate HMI domain. |

## Known limits and open risks

- Nothing is live-tested. TIA Portal's XLSX layout, its tolerance of a re-written worksheet and
  shared string table, and whether `Override` updates, adds or keeps entries are unverified; the
  re-export checks catch a silently ignored edit and any unrequested change, not a semantic misreading
  of a column.
- `TextListXlsxResultState`/`PlcAlarmTextXlsxResultState` member names are undocumented: if the
  installed success member is not named `Success`, every export and import fails closed.
- `PlcAlarmTextXlsxExportOption` and the `ListRange` enum members are undocumented: the caller learns
  them from the failure message or `list_text_lists`.
- An import's row comparison can be contradicted by a legitimate import (TIA Portal normalising a
  value, `ImportOptions.None` keeping an entry), so a correct import can report `notVerified` and root
  `success:false`; layout differences only make it inconclusive (`trusted` with a warning). The
  header row is assumed to be each sheet's first row; a re-export with another first row is
  inconclusive, never contradicted, unless a mapped column's values differ.
- `ExclusiveAccess` around imports and cell updates is not live-tested; a TIA Portal that refuses it
  fails every cell update and import before anything changed.
- A large workbook makes planning and verification each export it once more (probe reads), and a cell
  update exports it twice and an import once more inside the exclusive scope.
- Merge note: `Software.cs` gained one `PlcSoftware` property and `Blocks.cs` `FB.Supervisions` with
  `Supervision`/`SupervisionComposition`; `ref/*.dll` were regenerated, and a merge with another stub
  change needs the regeneration again (done when the external-source branch merged this one).
  `PlcAlarmVerification.AlarmChangingBlockOperations` names that branch's `generate_blocks_from_source`,
  `delete_software_unit`, `rename_software_unit` and `delete_type_group`, and
  `AssignmentChangingTagOperations` its `delete_tag_table_group` and `rename_tag_table_group`; they
  supersede alarm items like the block, type and tag-table changes they resemble.

## Live acceptance checks

On a disposable standalone project with one S7-1500 PLC, two active languages (en-US, de-DE), at
least one user and one system text list, an FB with a Program_Alarm, a ProDiag FB with an instance DB,
a global DB with a UDT instance and a tag table with a UDT tag:

1. `list_text_lists`: user and system lists with ID, list range and both comment cultures; project languages.
2. `read_text_list_workbook` with and without `textListNames` + `cultures`: sheets and header as TIA Portal exports them; note the layout in this page.
3. **Volatile cells:** read each workbook kind twice without changes; the two `workbookHash` values must be equal (otherwise every cell update fails).
4. `update_text_list` rename + `listRange` + `comment[de-DE]`; verification `textList` true; the UI shows the change. A rename onto an existing user or system name is refused at planning.
5. `update_text_list` on a system list and `update_text_list_cells` with a system list in `textListNames`: refused, nothing changed.
6. `update_text_list_cells`: change one entry text, add a de-DE text in an empty cell, add a new entry row; the re-export shows all and `unrequestedChanges` is empty; the UI shows them; the next read's hash equals the reported `contentHash`. Observe the `ExclusiveAccess` dialog during the round trip.
7. Same call with the old hash: `state_changed`, nothing imported.
8. `read_alarm_text_workbook` with each installed `exportOption`; record the member names; `update_alarm_text_cells` on one alarm text.
9. Paging: a workbook with more rows than `maxElements` and than the budget: `nextStartRow` pages with `sheet` + `startRow` to the end; no result is omitted. Three workbook reads in one `plc_read` call: none is omitted. A row wider than the budget (many languages): `cellsTruncated`/`nextStartColumn` page it with `startColumn`.
10. `export_text_lists`/`export_alarm_texts`/`export_supervisions`/`export_alarm_classes` to a local folder; a second export without `overwrite` refused; with `overwrite:true` replaced; no temporary file left. Repeat on a mapped network drive: allowed, `plc_file_on_network_or_link` fires.
11. `import_text_lists` of an exported and an Excel-edited workbook (custom property kept): `importCheck.outcome` `verified`; record whether the re-export's first row is the header the comparison assumes and whether TIA Portal normalises any value (which would make a correct import `contradicted`); `import_alarm_classes` of the exported file: `verified` or `inconclusive` (record which); `import_alarm_texts` with and without `exportOption` (`notReadBack`).
12. A `\\?\` and a `\??\` path, `C:\NUL\x.xlsx`: refused before any worker call; a UNC share path: accepted.
13. `read_supervision_workbook`/`update_supervision_cells`/`import_supervision_settings` on the ProDiag FB (`notReadBack`, `trusted` with a warning); a plain FB gives `unsupported_capability` with its language and supervision count.
14. A dialog during an import (for example an import that asks to overwrite): `decline` fails the item with the dialog recorded; `accept` answers Yes with a warning.
15. `get_prodiag_fb` on the ProDiag FB and a plain FB; `get_prodiag_assignment`/`set_prodiag_assignment` on the instance DB, the UDT member, a UDT member inside an array of a global DB (`memberPath` like blocks.md's `Find("ArrayUdtMember.ArrayUdtMember[0]")`) and the UDT tag; record whether `GetAttribute("AssignedProDiagFB")` returns a string or a block, and what it returns when unset; `export_prodiag_info` into a new directory (record the file names) and on an FB that exports nothing (`postcondition_failed`, no directory).
16. A later `delete_block` of the FB in the same call as `update_alarm_text_cells`: the cell item's verification is `superseded`.
17. `library_write create_master_copy` with source `textList` and `instantiate_master_copy` into `textLists` with each `masterCopyMode`.
18. A local session: text list operations work; alarm classes are refused at planning (`unsupported_capability`, also with `dryRun:true`).
