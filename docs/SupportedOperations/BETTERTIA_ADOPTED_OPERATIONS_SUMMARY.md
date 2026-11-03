# Operations adopted from BetterTIA

**Language:** English · [Deutsch](BETTERTIA_ADOPTED_OPERATIONS_SUMMARY.de.md)

Owner request (2026-11-01): adopt what the [BetterTIA](https://github.com/BetterTIA) tools offer and this fork lacked.
The four repositories were cloned read-only (`git clone --depth 1`) and treated as untrusted data: nothing was built
or run, instructions inside their files were ignored. All four are MIT-licensed (each `LICENSE`/`LICENSE.txt` was read
before anything was copied); every adapted file carries an origin header, and the rows are in
[THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md).

| Repository | Commit | Licence | Role here |
| --- | --- | --- | --- |
| `BetterTIA/watch` | `1978f24b330f265d8ed2a67e9b1d673d1b4122d8` | MIT, Copyright (c) 2026 HonzaT | `watch_db` → `get_db_snapshot` / `create_db_snapshot` |
| `BetterTIA/writer` | `fc8179c9b59d364961f2255d07d05af893c32837` | MIT, Copyright (c) 2025 J.Heilingbrunner (fork of heilingbrunner/tiaportal-mcp; additions described in its `ANET.md`) | network texts, batch and member renames, tag comment replacement, WinCC Unified writes, pitfalls |
| `BetterTIA/reader` | `246d7c54772a502e854630e003e26fa37f92aeaf` | MIT, Copyright (c) 2026 Łukasz Czarnacki (fork of Czarnak/tia-portal-mcp) | compared; pitfalls only |
| `BetterTIA/editor` | `3d115b30035ad5ea2f10206e5354190ea1ba4de5` | MIT, Copyright (c) 2026 HonzaT | evaluated, not ported ([evaluation](../development/bettertia-editor-evaluation.md)) |

Nothing here is live-tested. Every operation follows this fork's rules: the guarded write pipeline (`plc_write`
executes by default, `dryRun:true` previews), the owner principle "allow everything TIA Portal allows" (no
BetterTIA-style `allowFailsafe` switch or default dry run; fail-safe objects follow TIA Portal's login rule), and
Siemens members that are neither in the compile stubs nor documented in the Siemens skills are bound at runtime by
exact name and argument types, failing `api_member_missing` with nothing changed.

## DB snapshots of actual values (`plc_read get_db_snapshot`, `plc_write create_db_snapshot`)

Adopted from `watch_db` (`src/BtiaWatch/OpennessPortal.cs` `ExportDbSnapshot`, `src/BtiaWatch/WatchTools.cs`
`FormatSnapshot`).

| Operation | Tool | Capability | Fields | What it does |
| --- | --- | --- | --- | --- |
| `get_db_snapshot` | `plc_read` | `TemporaryExport` (every mode) | `blockPath` (global or instance DB), optional `memberPath`, `maxElements` (1-20000, default 2000) | Exports the snapshot stored in the offline project (`InterfaceSnapshot.Export(FileInfo, ExportOptions.WithReadOnly)` into a temporary file that is deleted) and returns `{ hasSnapshot, snapshotDate, totalValues, matchedValues, truncated, values [{ path, value, dataType }], topLevelMembers }`. |
| `create_db_snapshot` | `plc_write` | `ProjectMutation` | the same | Requires the PLC online (`OnlineProvider.State` `Online`, e.g. after `plc_tia_online go_online`); calls `ValueService.CreateSnapshot()`, which takes the actual values of the online PLC and stores them as the DB's snapshot in the offline project, then returns the export as above. Info guard `plc_stores_db_snapshot`. Verification `dbSnapshot`: `get_db_snapshot` must show the snapshot time and value count the write returned. |

Why a write: TIA Portal stores the snapshot in the offline project (the project shows as changed), so it is not a
read in the sense of read-only mode. Nothing is written to the PLC, and no start value changes: the value-loading
members of `ValueService` are never bound (TIA Portal offers applying snapshot values as start values or loading them
into the PLC; see left out below).

`memberPath` keeps the members equal to the path or below it (`A.B` matches `A.B`, `A.B.C` and `A.B[1]`, never
`A.BC`), case-insensitively, as `watch_db` does; a filter that matches nothing lists the top-level members.

### Siemens members

| Member | Binding | Evidence |
| --- | --- | --- |
| `ValueService` on `PlcBlock.Interface` | Service found among `GetServiceInfos()` by exact short name in a `Siemens.Engineering` namespace, retrieved with `IServiceProvider.GetService(Type)` | BetterTIA/watch `OpennessPortal.cs` (`db.Interface.GetService<ValueService>()`), reported live-checked against V21 with a real PLC in its README; not in the Siemens skills or the compile stubs |
| `ValueService.CreateSnapshot()` | `LibraryRuntimeBinding.Find(type, "CreateSnapshot", [])` | same; the README reports that offline it returns without error and leaves the snapshot empty, so `create_db_snapshot` refuses an offline PLC (`validation_error`, nothing changed) |
| `InterfaceSnapshot` on the block | as `ValueService`, on the block itself | same (`db.GetService<InterfaceSnapshot>()`; "on the interface it is null (spike, V21)") |
| `InterfaceSnapshot.Export(FileInfo, ExportOptions)` | `LibraryRuntimeBinding.Find(type, "Export", [FileInfo, ExportOptions])` | same |
| `ExportOptions.WithReadOnly` | parsed by name on the live enum (the stub enum declares only `None`) | same ("ExportOptions.None leaves it out (live test, V21)") |
| `OnlineProvider.State` on the CPU device item | compiled (stub) | already used by `get_online_state` |

The export layout (`AttributeList/Snapshot/.../Value[@Path,@Type]` and `AttributeList/SnapshotDate`) is the one
BetterTIA/watch parses; `PlcDbSnapshotDocument` (Contracts, Siemens-free) reads only those elements. An export
without a `Snapshot` attribute is reported `hasSnapshot:false` with a message, never as an empty value list.

### Left out

- **Technology object instance DBs** (`PID_Compact`, axes): BetterTIA/watch reports that V21 refuses `Interface` on
  `TechnologicalInstanceDB`, so no actual values come through Openness; `blockPath` only resolves program blocks.
- **Loading snapshot values as start values or into the PLC**: TIA Portal offers both, but no source names the
  `ValueService` members; BetterTIA deliberately never binds them. Not guessed.
- **`watch_online`**: `plc_tia_online` already goes online/offline on the exact download path with per-call
  confirmation; its `BTIA_WATCH_ALLOWED_TARGETS` allowlist is replaced by the access mode (owner decision).
- **`watch_screen`** (TIA editor screenshot with monitoring): see [left out](#left-out-overall).

## Network titles and comments (`plc_write set_network_text`, `replace_network_text`)

Adopted from the writer's `SetNetworkText` / `ReplaceNetworkText` (`src/TiaMcpServer/Siemens/SimaticMl.cs`, region
"network titles and comments", and `Portal.AnetFork.cs` `EditNetworkTexts`).

| Operation | Fields | What it does |
| --- | --- | --- |
| `set_network_text` | `blockPath` (OB, FB or FC), `networkNumber` (1-based), `title` and/or `comment`, optional `culture`, `expectedContentHash` (xml) | Sets the title and/or comment of one network. Without `culture` the text must exist in exactly one language; with it, a language the network has no item for is added (a `MultilingualTextItem` with the next free hexadecimal ID), as TIA Portal lets a user type a text in any project language. |
| `replace_network_text` | `blockPath`, `replacements` (1-100 `{ find, replace }`), optional `culture` (omitted: every language), `textScope` `networks` (default) or `all`, `expectedContentHash` | Replaces every `find` in one pass (literal, case-sensitive, longest first, so `Pump 1`→`Pump 10` with `Pump 10`→`Pump 1` swaps and `Pump 10` is never eaten by `Pump 1`) in every network title and comment; `all` also covers the block title and comment and the GRAPH sequence, step and transition texts. Step and transition names are interface members and stay. |

Openness has no setter for network texts, so both are host-planned document edits like the interface member edits
(`PlcDocumentEditPlanner`): the block identity is read (`get_block_interface`; know-how-protected blocks are refused,
their export carries only the interface), the xml bundle is exported (or the document an earlier item of the call
planned is used, with `dependsOn` and a re-plan before the mutation), the SimaticML document is edited with the
Siemens-free `SimaticMlNetworkTexts` and imported through `update_block_logic` against the export's hash, so a change
in between fails `state_changed`. An item that changes nothing (texts already as requested, no find matched) is refused
before anything runs, because re-importing an unchanged block can still make its instance DB and callers inconsistent.
F-blocks follow TIA Portal's rule (`plc_safety_acceptance_required`, a login only when a safety password is set),
like every other F-block change. Info guard `plc_text_edit_reimports_block`; the V21 pitfall guards of a SimaticML
overwrite apply. Verification `networkText`: the block is re-exported, every changed text must read back at the same
place and language, and the new `contentHash` is reported (usable as the next `expectedContentHash`).

The SimaticML model (a network is a `SW.Blocks.CompileUnit` in the block's `ObjectList`, numbered from 1 in document
order; title and comment are `MultilingualText` objects `Title`/`Comment` with one `MultilingualTextItem` per culture;
GRAPH texts are `MultiLanguageText` elements with `Lang`) is the writer's and matches this fork's V21 SimaticML
fixtures. No Siemens member is called beyond the existing export and import.

Left out: the writer's `Dependents` / `InconsistentAfter` lists (from the cross references) — use
`plc_read analyze_change_impact` before and `compile_check` after; its default `dryRun=true` and `allowFailsafe`
(this fork executes by default and follows TIA Portal's F-rule).

## Member renames (`plc_write rename_members`)

Adopted from the writer's `RenameMembers` (`SimaticMl.cs` region "member rename", `Portal.AnetFork.cs`).

Fields: `blockPath` (a code block or global DB) or `typePath` (a PLC data type), `memberRenames` (1-100
`{ memberPath, newName, section? }`), optional `expectedContentHash` (xml). `section` may be omitted for a block when
exactly one section holds the path.

What TIA Portal does, and therefore what this operation does: renaming a member in the block editor renames it in the
interface and updates the block's own code. Openness has no member setter, so the operation is a host-planned document
edit like `set_network_text` (`PlcDocumentEditPlanner` with the Siemens-free `SimaticMlMemberRenames`):

- every rename is applied to the interface with the existing edit model, first under temporary names and then under the
  final ones, so swaps, rotations and chains work in one item and a name held by another member (in any section of a
  block) is refused before anything runs; a member and one of its own members cannot be renamed in one item;
- the object's own accesses are updated: every `Access Scope="LocalVariable"` symbol and every
  `Instance Scope="LocalVariable"` whose leading components name a renamed member (case-insensitively, matched against
  the names before the edit, so a swap does not rename twice), in LAD, FBD, STL, SCL and GRAPH networks, and the local
  operands (`#name`) GRAPH actions keep as text (the writer's `Segment`/`Operand` patterns); comments and titles are
  texts and stay (a mention is reported as a warning, `plc_state_unverifiable` info);
- accesses in **other** blocks are not updated: TIA Portal does not rewrite them either (the callers and readers of an
  FB's or DB's members become inconsistent until they are edited and compiled). The writer's cross-block rewrite (every
  block and type of the PLC exported, a declaration resolver over DBs, instance DBs, multi-instances and PLC data types,
  formal parameters at calls) was not adopted: it is not what TIA Portal does, it was not verified against a running TIA
  Portal by its authors, and it would re-import every affected block. Read `plc_read analyze_change_impact` before and
  run `compile_check` after;
- instance and array DBs are refused (rename the FB's members); know-how-protected blocks are refused.

Info guard `plc_changes_interface` with the message above; F-blocks follow TIA Portal's rule; verification
`renamedMembers` re-reads the interface (`get_block_interface` / `get_type_interface`): every member must carry its new
name exactly and its old path must be gone (unless another rename of the item takes it over), and the new `contentHash`
is reported.

Left out from the writer: its `HmiDependents` list (HMI tags whose PLC path passes a renamed member) — the HMI tag
binding is changed with `hmi_write update_tag plcTag`; `summaryOnly` report files; `allowFailsafe`.

## Batch renames (`plc_write rename_tags`, `rename_objects`)

Adopted from the writer's `RenameTags` / `RenameObjects` (`Portal.AnetFork.cs` region "batch rename",
`Siemens/BatchRename.cs`).

| Operation | Fields | Objects |
| --- | --- | --- |
| `rename_tags` | optional `plcName`, `renames` (1-200 `{ name, newName }`) | PLC tags by name across every tag table of the PLC (tag names are unique per PLC; the planner resolves them in the tag inventory, so a missing tag is `target_not_found` before anything runs, and later items of the call see the new names) |
| `rename_objects` | optional `plcName`, `renames` | blocks, PLC data types and technology objects of the PLC by name; a name found in more than one kind is `target_ambiguous` (resolved by the worker) |

Every object is resolved before anything changes. `PlcBatchRenamePlan` (Contracts, the writer's `BatchRename.Plan`)
orders the setter calls: an object whose new name another object of the batch still holds is parked under a temporary
name (`zzTmp_<stamp>_<i>`, a fresh stamp per attempt) and gets its final name last, so swaps, rotations and chains
work; a case-only rename is no conflict; a name given twice is refused. Each step is
`SetAttribute("Name")` after a `GetAttributeInfos()` writability check with `GetAttribute("Name")` read back (the
shared `LibraryReflection.Rename`, also used by the group renames), and all steps run in one `ExclusiveAccess`
transaction (`PlcRoundTrip.Run`): a failed step (TIA Portal refusing a name, a read-back that differs) rolls every
rename back, temporary names included, and the failure names each object's name afterwards.

Differences to the writer, by the owner principle: no collision pre-check against names outside the batch (TIA
Portal decides; a silently ignored rename fails the read-back and rolls back), no `dryRun` default and no
`allowFailsafe`; fail-safe tags (`IsSafety`) and F-blocks follow TIA Portal's safety login rule (worker gate before the
transaction; an F-block named in `rename_objects` that the project tree shows fires the acceptance guard like
`rename_block`); know-how-protected blocks are not refused (TIA Portal renames them). Info guard `plc_batch_rename`.
Verification `batchRename`: `rename_tags` re-reads the tag tables (every new name present, every old name that no
rename takes over gone); `rename_objects` reports the names the transaction read back.

Siemens members: `PlcSoftware.TagTableGroup`/`BlockGroup`/`TypeGroup`/`TechnologicalObjectGroup` with their `Groups`,
`TagTables`/`Tags`, `Blocks`, `Types`, `TechnologicalObjects` (compiled, in the stubs); `IEngineeringObject`
`GetAttributeInfos`/`SetAttribute`/`GetAttribute("Name")` (generic contract, already used by the group renames);
`GetAttribute("IsSafety")` (already used by `move_tag`); `TiaPortal.ExclusiveAccess` / `Transaction` (already used by
`move_tag`, `copy_block`, `move_block`).

Left out from the writer: the cross-reference scan for fail-safe users (`UsedByFailsafe`; TIA Portal's own rule
applies instead), `summaryOnly` report files, tags of software units (only the PLC's own tag tables are searched,
like the writer), user constants.

## Tag comment replacement (`plc_write replace_tag_comments`)

Adopted from the writer's `ReplaceTagComments` / `EditTagComments` (`Portal.AnetFork.cs` region "tag comments").

Fields: optional `plcName`, optional `tableName` (with `folderPath`) for one table, `replacements` (1-100
`{ find, replace }`, the one-pass longest-first replacement of `replace_network_text`), optional `culture` (omitted:
every language). Scope: the tags of the PLC's own tag tables (software units and user constants are not covered,
like the writer).

The planner previews the change from the public read `read_tag_comments`: the effect lists every comment text that
changes (`comment /<folder>/<table>/<tag> [<culture>]`, old and new); an item that changes nothing is refused, an
unreadable or incomplete read fires `plc_state_unverifiable` (info) and the item runs. The worker applies the same
replacement to the live texts: every `MultilingualTextItem` of `PlcTag.Comment.Items` whose text changes is set
through `MultilingualTextItem.Text` and read back, all in one `ExclusiveAccess` transaction (a failure rolls every text
back). Comments are texts, not safety program content: no safety gate applies, TIA Portal decides. Verification
`tagComments` re-reads `read_tag_comments`: every text the worker reported changed must read back as written.

Siemens members: `PlcTag.Comment` (`MultilingualText`), `MultilingualText.Items`, `MultilingualTextItem.Language.Culture`
and `.Text` (get/set) — all in the compile stubs and already used by `read_tag_comments` and `move_tag`.

`replace_tag_comments` changes existing texts only: an item without a match is refused (and, because planning refuses
it, the whole call does not run; the message says so). A tag's comment is set directly with `create_tag`/`update_tag`
`comment`+`commentCulture` (defect K22, 2026-11-02; the writer's `SetTagComment`): the text item of that culture is
found as the Siemens skill (tia-project-general, language settings §5) writes a multilingual text
(`LanguageSettings.Languages` → `MultilingualText.Items.Find(Language)` → `MultilingualTextItem.Text`) and read back;
an inactive project language is passed on with a warning (TIA Portal decides), a culture TIA Portal has no text item
for fails (no member that creates one is documented; `create_tag` then deletes the tag it created), and a culture
that is not a TIA Portal language is refused before anything changes. Verification `tag` re-reads the tag table and
`read_tag_comments`. Not live-tested.

## WinCC Unified screens, scripts and batch renames (`hmi_write`)

Adopted from the writer's Unified tools (`Portal.AnetFork.Hmi.cs`, `Portal.AnetFork.HmiScreens.cs`, `ANET.md`
sections "WinCC Unified" and "WinCC Unified screens and screen items"). Nine new `hmi_write` operations (worker methods
`hmi_<name>`, `HmiScreenMethods` in Contracts; `ProjectMutation` like every HMI write); the tool then had 18, and has 20 with the [text list files](#tag-tables-and-wincc-unified-text-lists-as-files-owner-additions).

| Operation | Fields | What it does |
| --- | --- | --- |
| `create_screen` | `screenName`, optional `groupPath`, `width`, `height` | `HmiScreenComposition.Create(name)` at the root or in a screen group, then `Width`/`Height`; a failed setter deletes the new screen again (the shared create-cleanup of `hmi_write`). Openness offers no screen copy or template (writer, by reflection over V21), so a new screen is empty. |
| `delete_screen` | `screenName`, optional `groupPath` | `Delete()` (info guard `hmi_deletes_screen`: screen windows, navigation and scripts that name it are not changed) |
| `rename_screens`, `rename_tags` | `renames` (1-200 `{ name, newName }`) | screens or HMI tags by name, swaps and rotations through temporary names (`PlcBatchRenamePlan`), every step `SetAttribute("Name")` with read-back, all in one `ExclusiveAccess` transaction that any failure rolls back. Info guard `hmi_rename_references_unverified`: whether TIA Portal follows a renamed HMI tag in dynamizations, scripts and alarm triggers is open (the writer lists it as not verified), and screen names in scripts and screen windows are not changed. |
| `create_screen_item` | `screenName`, `itemName`, `itemType`, optional `left`, `top`, `width`, `height` | `ScreenItems.Create<T>(name)` with `T` the public non-abstract screen item type whose short name is exactly `itemType` (e.g. `HmiButton`, `HmiIOField`, `HmiText`, `HmiRectangle`, `HmiCircle`; the type is never guessed), then the geometry; a failed setter deletes the item again. A circle has a centre and radius instead of a box: set them with `set_screen_item_property`. |
| `delete_screen_item` | `screenName`, `itemName` | `Delete()` |
| `set_screen_item_property` | `screenName`, optional `itemName` (omitted: the screen), `propertyName`, `value`, optional `culture` | `SetAttribute(propertyName, value)` with the value converted to exactly the runtime type of the current value (string, bool, number, enum member name, `System.Drawing.Color` from `#RRGGBB`/`#AARRGGBB`); a `MultilingualText` takes the text of `culture`; read back. `ScriptDiagnosisOverviewText` is refused ("skipped: reported to crash TIA Portal V21", V21 pitfall 3). |
| `set_dynamization_tag` | `screenName`, optional `itemName`, `propertyName`, `tagName` | sets `Tag` of the property's `TagDynamization`, or creates one with `Dynamizations.Create<TagDynamization>(propertyName)` when the property has none (the writer only set existing ones); a script dynamization on the property is refused; read back |
| `replace_in_scripts` | `replacements` (1-100), optional `screenName` (+ `groupPath`) | the one-pass replacement of `replace_network_text` over `ScriptCode` and `GlobalDefinitionAreaScriptCode` of every event handler, property event handler and script dynamization of the screens and their items (four item levels deep, as the writer walks them); each written script is read back; the result's `appliedProperties` names every changed script |

Siemens members. Compiled (in the stubs): `HmiSoftware.Screens`/`ScreenGroups`/`Tags.Find`,
`HmiScreenComposition.Find`, `HmiScreenGroup.Screens`/`Groups`, `HmiScreen.ScreenItems`, `HmiScreenItemBase.Name`,
`IEngineeringObject.GetAttribute`/`SetAttribute`, `MultilingualText.Items`. Bound at runtime by exact name and argument
types (`LibraryRuntimeBinding`), `api_member_missing` before anything changes: `HmiScreenComposition.Create(String)`,
`HmiScreenItemBaseComposition.Create<T>(String)`, `Delete()` of screens and items, the `Dynamizations`,
`EventHandlers` and `PropertyEventHandlers` properties, `Dynamizations.Create<TagDynamization>(String)`, the handler's
`Script` and the script's `ScriptCode`/`GlobalDefinitionAreaScriptCode`, `Color.FromArgb(Int32)`. Evidence for all of
them is BetterTIA/writer (checked there by reflection over the V21 assemblies, not run against TIA Portal); the Siemens
skills document none of the Unified screen API. In keeping with V21 pitfall 3 the HMI worker never enumerates attributes
generically, so attribute writability is not pre-checked: TIA Portal decides and every value is read back.

Covered already, so not added: the writer's `SetHmiTagPlcTags` (HMI tag → PLC tag) is `hmi_write update_tag` with
`plcTag`/`connection`; its single `RenameHmiTags`/`RenameHmiScreen` is the batch with one entry (or `update_tag
newName`); `SetHmiItemAttribute` is `set_screen_item_property`; `CompileHmi` is TIA Portal's compile (not offered by
Openness on the HMI software, as the writer found).

Left out: `CreateHmiScreenItems` batches from JSON with `events` (an event handler created with its script):
`EventHandlers.Create(enum)` per item type and the script assignment are unverified twice over (the writer lists
"whether an event script written through `IHmiScript.ScriptCode` compiles" as open) — edit existing scripts with
`replace_in_scripts`; dynamizations other than by tag; faceplates, graphic views and other item types beyond what the
caller names exactly (any public type works if it exists); the writer's usage lists (script lines that name a
renamed screen) — use `hmi_read` and `replace_in_scripts`; screen copies and templates (not in Openness V21 per the
writer).

## Tag tables and WinCC Unified text lists as files (owner additions)

A follow-up owner request (2026-11-01) asked for WinCC Unified text list export and import and for PLC tag table
export to a file. A per-version TIA MCP by Gangdolf Automation (an MIT fork of heilingbrunner/tiaportal-mcp) is said to
offer `ExportHmiTextLists`/`ImportHmiTextLists` and `ExportPlcTags`; its source is not published, and nothing was
downloaded from that site. Both features below are implemented only from the Siemens Openness skills and the compile
stubs; nothing is copied from any third party.

| Operation | Fields | What it does |
| --- | --- | --- |
| `plc_write export_tag_table` | `tableName`, optional `folderPath`, `plcName`; `filePath`, optional `overwrite` | `PlcTagTable.Export(FileInfo, ExportOptions.None)` into a private temporary file, then staged next to the target, moved into place and re-hashed (`TechnologyObjectFiles.Place`, the technology object export writer); an existing file is refused unless `overwrite:true` (info guard `plc_export_overwrites_file`). The result carries the file's size and SHA-256 and the table's tag and user constant counts. The CSV export `export_tag_tables_csv` stays; this is the SimaticML file TIA Portal can import again. |
| `plc_write import_tag_table` | `filePath`, optional `folderPath`, `plcName`, `tableName`, `overwrite` | `PlcTagTableComposition.Import(FileInfo, ImportOptions)` of a private copy of the file into the folder, inside one `ExclusiveAccess` transaction (`PlcRoundTrip`) so a failed postcondition rolls it back; `overwrite:true` uses `ImportOptions.Override` (info guard `plc_import_replaces_tag_table`). `tableName` names the table the file holds: then it must not exist yet without `overwrite` (planner and worker), must be in the folder afterwards and is the verified table; without it the imported names come from the import's return value when that is a table or tables, else from the folder's new names. Overwriting a table that holds an `IsSafety=true` tag follows the safety login rule (like `move_tag_table`). Verified by re-reading the tag tables (check `tagTable`: every imported table with the reported tag and user constant counts). Later items of the call may use an imported `tableName`. |
| `hmi_write export_text_lists` | `directoryPath` (existing), optional `textListNames` (1-200), `overwrite` | one `<name>.xml` per WinCC Unified user text list of `HmiSoftware.HmiTextLists` (all, or the named ones): `Export(FileInfo, ExportOptions.None)` on each `HmiTextList` into a temporary file, placed like the tag table export; a list name that is not a valid file name is refused; every target is checked before the first export. `appliedProperties` lists every file with its size and SHA-256. |
| `hmi_write import_text_lists` | `filePath`, optional `overwrite` | `Import(FileInfo, ImportOptions)` on `HmiSoftware.HmiTextLists` of a private copy (`ImportOptions.Override` with `overwrite:true`); without `overwrite` a new list must appear (`postcondition_failed` otherwise); `appliedProperties` names the new lists. |

Siemens members. Tag tables: `PlcTagTable.Export(FileInfo, ExportOptions)` is compiled (stub) and documented with
`TagTableCollection.Import(FileInfo, ImportOptions)` in the Siemens skill `openness_development/skills/tags-and-tagtables`
("Export TagTable to XML and Re-Import"); `move_tag_table` already uses both. The import is bound at runtime by its
parameter list (the reference states no return type), before anything changes. `ExportOptions.None` and
`ImportOptions.None`/`Override` are stub members.

WinCC Unified text lists: no Siemens document describes a file export or import for Unified text lists (the skills in
`tia-portal-ai-extensions` mention only PLC alarm text lists, and the stub `HmiTextList` has only `Name`). The two
members are a hypothesis following the SimaticML pattern of the documented compositions and are bound at runtime by
exact name and argument types on the live types, before the first file is written or anything is imported; an
installation without them fails `api_member_missing` with nothing written or changed (info guard
`hmi_text_list_file_unverified` on every such item). System text lists and graphic lists are never touched (V21 pitfall
3, see [V21 pitfalls](V21_PITFALLS.md#pitfall-3-graphic-lists-and-scriptdiagnosisoverviewtext)); `HmiPitfallSourceTests`
pins that `HmiTextListFiles` is the only HMI worker file that binds an export. Like every `hmi_write` item the
verification is `trusted`/`superseded`; check the lists with `hmi_read list_text_and_graphic_lists`.

Checked and already covered: `ExportPlcTags` matches `plc_write export_tag_tables_csv` (CSV) and now
`export_tag_table` (SimaticML). Left out: an XLSX text list export for Unified (no documented provider); the PLC text
list XLSX export/import (`plc_write export_text_lists`/`import_text_lists`) exists already for PLC text lists.

## Left out overall

- **`watch_screen`** (`ShowInEditor()`, Ctrl+T "Monitor all", `PrintWindow` capture as MCP image content): not
  adopted. It does not fit the worker model safely: the worker is a headless stdio process that may serve a TIA Portal
  started without user interface (no main window), the shortcut needs TIA Portal as the foreground window (focus
  stealing, keystrokes to whatever window is in front if activation fails, a toggle that BetterTIA can only read through
  MSAA), Ctrl+T in an offline editor makes TIA Portal open its own "Go online" dialog and connect over the last used
  interface, and every structured tool returns one canonical JSON document, with no image content in the contract. A
  capture of the window alone (no keys) would still bring TIA Portal to the front and was not judged worth a new
  content type. `ShowInEditor` stays left out as in the [technology object reference](TECHNOLOGY_OBJECTS_SUMMARY.md).
