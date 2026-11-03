using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// <c>list_text_lists</c>: the project's languages and, per matching PLC, its user and system text lists
/// (<c>PlcSoftware.PlcAlarmTextlistGroup</c>). Entries are not part of the documented object model; they are
/// read with <c>read_text_list_workbook</c>.
/// </summary>
public class PlcTextListInventoryInfo
{
    public PlcProjectLanguagesInfo ProjectLanguages { get; set; } = new PlcProjectLanguagesInfo();

    public List<PlcTextListPlcInfo> Plcs { get; set; } = new List<PlcTextListPlcInfo>();
}

/// <summary><c>Project.LanguageSettings</c>: the active cultures, the editing and the reference language (null when unreadable or unset).</summary>
public class PlcProjectLanguagesInfo
{
    public List<string> Active { get; set; } = new List<string>();

    public string? Editing { get; set; }

    public string? Reference { get; set; }

    /// <summary>False when a language member could not be read (named in <see cref="Messages"/>).</summary>
    public bool Complete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();
}

public class PlcTextListPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public List<PlcTextListInfo> UserTextLists { get; set; } = new List<PlcTextListInfo>();

    public List<PlcTextListInfo> SystemTextLists { get; set; } = new List<PlcTextListInfo>();

    /// <summary>False when the text list cap stopped the walk or a list could not be enumerated.</summary>
    public bool Complete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();
}

/// <summary>One text list. Name, ID, ListRange and Comment are the documented <c>PlcAlarmTextlist</c> members.</summary>
public class PlcTextListInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>True for a system text list (read-only through this server).</summary>
    public bool IsSystem { get; set; }

    /// <summary><c>ID</c> as text (its type is undocumented); null when unreadable.</summary>
    public string? Id { get; set; }

    /// <summary><c>ListRange</c> enum member name as the installed TIA Portal reports it; null when unreadable.</summary>
    public string? ListRange { get; set; }

    /// <summary>The <c>Comment</c> per culture (<c>MultilingualText.Items</c>); null when unreadable or not multilingual.</summary>
    public List<PlcAlarmTextItemInfo>? Comment { get; set; }

    /// <summary>Members that could not be read.</summary>
    public List<string> Unreadable { get; set; } = new List<string>();
}

public class PlcAlarmTextItemInfo
{
    public string Culture { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// <c>read_*_workbook</c>: the cells of the XLSX workbook TIA Portal exports, read as-is (the server does
/// not interpret the column layout). <see cref="WorkbookHash"/> covers every cell of every sheet, also the
/// rows beyond the returned ones; cell updates need it as <c>expectedWorkbookHash</c>.
/// </summary>
public class PlcAlarmWorkbookInfo
{
    /// <summary>textLists, alarmTexts or supervisions.</summary>
    public string Kind { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    public string? DeviceName { get; set; }

    /// <summary>Supervisions: the ProDiag FB as it was resolved.</summary>
    public string? BlockPath { get; set; }

    /// <summary>Supervisions: the FB's <c>ProgrammingLanguage</c> member name (ProDiag for a ProDiag FB); null otherwise or when unreadable.</summary>
    public string? ProgrammingLanguage { get; set; }

    /// <summary>Supervisions: <c>FB.Supervisions.Count</c> (presence evidence; a supervision has no documented member); null otherwise or when unreadable.</summary>
    public int? SupervisionCount { get; set; }

    /// <summary>
    /// The cultures passed to the export (resolved in <c>LanguageSettings.Languages</c>). Empty for the unfiltered text list
    /// export, the <c>ExportToXlsx(FileInfo)</c> overload, which exports TIA Portal's activated project languages itself.
    /// </summary>
    public List<string> Cultures { get; set; } = new List<string>();

    /// <summary>Text lists: the exported text list names; null when every text list was exported.</summary>
    public List<string>? TextListNames { get; set; }

    /// <summary>Alarm texts: the PlcAlarmTextXlsxExportOption member used.</summary>
    public string? ExportOption { get; set; }

    /// <summary>The result state TIA Portal reported for the export, by name.</summary>
    public string? ExportState { get; set; }

    /// <summary>SHA-256 over every cell of every sheet; always the whole workbook, whatever rows were returned.</summary>
    public string WorkbookHash { get; set; } = string.Empty;

    /// <summary>The sheet the read selected (only its rows are returned), as the workbook names it; null for every sheet.</summary>
    public string? Sheet { get; set; }

    /// <summary>The first row returned of the selected sheet (1 without a selection).</summary>
    public int StartRow { get; set; } = 1;

    /// <summary>The first column (letters) returned of the selected sheet's rows; A without a selection.</summary>
    public string StartColumn { get; set; } = "A";

    public List<PlcAlarmSheetInfo> Sheets { get; set; } = new List<PlcAlarmSheetInfo>();

    /// <summary>The full value of each requested probe cell (host planning and verification); empty for a public read.</summary>
    public List<PlcAlarmCellProbeInfo> Probes { get; set; } = new List<PlcAlarmCellProbeInfo>();

    /// <summary>False when a sheet's rows or a cell's value were cut to the bounds.</summary>
    public bool Complete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();
}

public class PlcAlarmSheetInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Rows that hold at least one cell.</summary>
    public int RowCount { get; set; }

    /// <summary>The highest column number that holds a cell.</summary>
    public int ColumnCount { get; set; }

    public int CellCount { get; set; }

    public List<PlcAlarmRowInfo> Rows { get; set; } = new List<PlcAlarmRowInfo>();

    /// <summary>True when rows of this sheet were not returned (row cap, character budget, or another sheet was selected).</summary>
    public bool RowsTruncated { get; set; }

    /// <summary>The first row number not returned: pass it as startRow with this sheet to read on; null when nothing is left.</summary>
    public int? NextStartRow { get; set; }
}

public class PlcAlarmRowInfo
{
    /// <summary>1-based row number.</summary>
    public int Row { get; set; }

    public List<PlcAlarmCellInfo> Cells { get; set; } = new List<PlcAlarmCellInfo>();

    /// <summary>True when this row alone was wider than the budget and not all its cells were returned.</summary>
    public bool CellsTruncated { get; set; }

    /// <summary>The first column not returned (letters): read this row on with sheet, startRow = row and startColumn; null otherwise.</summary>
    public string? NextStartColumn { get; set; }
}

public class PlcAlarmCellInfo
{
    /// <summary>A1 reference.</summary>
    public string Cell { get; set; } = string.Empty;

    /// <summary>string, number, boolean, error or formula (a formula cell reports its cached value).</summary>
    public string Kind { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool ValueTruncated { get; set; }
}

public class PlcAlarmCellProbeInfo
{
    public string Sheet { get; set; } = string.Empty;

    public string Cell { get; set; } = string.Empty;

    /// <summary>False for an empty cell or a missing sheet (see <see cref="SheetExists"/>).</summary>
    public bool Exists { get; set; }

    public bool SheetExists { get; set; }

    public string? Kind { get; set; }

    /// <summary>The full value ("" for an empty cell).</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>The result of every <c>plc_write</c> alarm, text list and supervision operation; inapplicable members are null.</summary>
public class PlcAlarmWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    public string? DeviceName { get; set; }

    public string? BlockPath { get; set; }

    /// <summary>update_text_list / delete_text_list: the text list as it was resolved.</summary>
    public string? TextListName { get; set; }

    /// <summary>update_text_list: the members read back after the change.</summary>
    public PlcTextListInfo? TextListAfter { get; set; }

    /// <summary>update_text_list: the members that were set, in order (Name, ListRange, Comment[culture]).</summary>
    public List<string>? AppliedMembers { get; set; }

    /// <summary>Exports and imports: the caller's file.</summary>
    public string? FilePath { get; set; }

    /// <summary>Exports: true when an existing file was replaced (only with overwrite:true).</summary>
    public bool? Overwritten { get; set; }

    /// <summary>
    /// Exports: size and SHA-256 of the file as read back after it was moved into place. Imports: of the private copy of the
    /// caller's file that was imported and compared (the caller's file itself is not imported directly).
    /// </summary>
    public long? Bytes { get; set; }

    public string? Sha256 { get; set; }

    /// <summary>The result state TIA Portal reported, by name (export or import; for cell updates the import).</summary>
    public string? State { get; set; }

    /// <summary>The log file TIA Portal named in its result; it is not read.</summary>
    public string? LogFilePath { get; set; }

    /// <summary>Alarm classes: error and warning counts and the result messages (bounded).</summary>
    public int? ErrorCount { get; set; }

    public int? WarningCount { get; set; }

    public List<string>? Messages { get; set; }

    public List<string>? Cultures { get; set; }

    public List<string>? TextListNames { get; set; }

    public string? ExportOption { get; set; }

    public string? ImportOption { get; set; }

    /// <summary>Cell updates: the workbook hash before the import and after the re-export.</summary>
    public string? WorkbookHashBefore { get; set; }

    public string? WorkbookHashAfter { get; set; }

    /// <summary>Cell updates: every edit with the value before and the value the re-export shows.</summary>
    public List<PlcAlarmCellChangeInfo>? Edits { get; set; }

    /// <summary>
    /// Cell updates: cells the re-export shows changed that no edit requested (at most 50, "Sheet!Cell: before -> after");
    /// any entry fails the item with postcondition_failed. Empty on success, null for other operations.
    /// </summary>
    public List<string>? UnrequestedChanges { get; set; }

    /// <summary>Imports: what the verification re-export after the import shows (null for other operations).</summary>
    public PlcAlarmImportCheckInfo? ImportCheck { get; set; }

    /// <summary>The portalConfirmations policy the call ran with (decline or accept).</summary>
    public string PortalConfirmationPolicy { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>The TIA Portal confirmation dialogs answered while the item ran (at most 100; each answered by the policy).</summary>
    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new List<PlcDownloadPortalConfirmationInfo>();

    public int PortalConfirmationsObserved { get; set; }

    public bool PortalConfirmationsComplete { get; set; } = true;

    /// <summary>set_prodiag_assignment: the assigned object (instanceDb, dbMember or tag) and its path.</summary>
    public string? TargetKind { get; set; }

    public string? Target { get; set; }

    /// <summary>set_prodiag_assignment: <c>AssignedProDiagFB</c> before the change and as read back after it.</summary>
    public string? AssignedProDiagFbBefore { get; set; }

    public string? AssignedProDiagFbAfter { get; set; }

    /// <summary>export_prodiag_info: the created directory and every file in it with size and SHA-256 as read back.</summary>
    public string? DirectoryPath { get; set; }

    public List<PlcAlarmExportedFileInfo>? Files { get; set; }

    /// <summary>export_prodiag_info: <c>PlcBlock.IsConsistent</c> of the FB before the export (the documented sample exports only consistent FBs); null when unreadable.</summary>
    public bool? IsConsistent { get; set; }
}

/// <summary>
/// The verification re-export after an import. <c>rows</c>: the imported workbook's rows compared with a re-export of the
/// same kind (<see cref="XlsxCellWorkbook.CompareRows"/>, columns matched by header text); <c>fileHash</c>: the alarm-class
/// file compared byte for byte with a re-export; <c>none</c>: no documented export reads the imported data back
/// (supervision settings) or the request gave no export option to re-export with (alarm texts).
/// <see cref="Outcome"/>: <c>verified</c> (everything imported shows), <c>contradicted</c> (an imported row is absent from
/// the re-export: the import did not apply it as given), <c>inconclusive</c> (the comparison cannot decide: a layout
/// difference, a differing alarm-class file, a failed verification export) or <c>notReadBack</c> (no documented export).
/// </summary>
public class PlcAlarmImportCheckInfo
{
    /// <summary>rows, fileHash or none.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>verified, contradicted, inconclusive or notReadBack.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>True exactly when <see cref="Outcome"/> is verified.</summary>
    public bool Verified { get; set; }

    /// <summary>Why the import is not verified; null when verified.</summary>
    public string? Reason { get; set; }

    /// <summary>rows: the workbook hash of the re-export.</summary>
    public string? WorkbookHashAfter { get; set; }

    public int? ImportedRows { get; set; }

    public int? RowsNotShown { get; set; }

    /// <summary>rows: the first imported rows the re-export does not show ("Sheet!row N", at most 20).</summary>
    public List<string>? NotShown { get; set; }

    /// <summary>fileHash: the SHA-256 of the imported file and of the re-export.</summary>
    public string? ImportedSha256 { get; set; }

    public string? ReexportSha256 { get; set; }
}

public class PlcAlarmExportedFileInfo
{
    /// <summary>The path relative to the created directory.</summary>
    public string Name { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// <c>plc_alarm_plan</c>: the worker's non-mutating planning evidence of one alarm write item, read before any write runs
/// (dryRun included). It resolves the item's PLC or FB and binds every Siemens member the item will call, by documented
/// name and argument types, without calling it; it reports the caller file's location as the file system sees it.
/// </summary>
public class PlcAlarmPlanInfo
{
    public string Operation { get; set; } = string.Empty;

    public string? PlcName { get; set; }

    public string? DeviceName { get; set; }

    public string? BlockPath { get; set; }

    /// <summary>The members bound for the call, as "Type.Member(arguments) -> Result.State".</summary>
    public List<string> Bound { get; set; } = new List<string>();

    /// <summary>The caller file or directory as the file system sees it; null for operations without one.</summary>
    public PlcAlarmPathInfo? Path { get; set; }

    /// <summary>Text lists named by textListNames as TIA Portal spells them (user and system lists); null without textListNames.</summary>
    public List<string>? TextListNames { get; set; }

    /// <summary>Supervision items: the FB's ProgrammingLanguage and FB.Supervisions.Count.</summary>
    public string? ProgrammingLanguage { get; set; }

    public int? SupervisionCount { get; set; }

    /// <summary>set_prodiag_assignment: the current AssignedProDiagFB of the target.</summary>
    public string? AssignedProDiagFb { get; set; }

    public List<string> Messages { get; set; } = new List<string>();
}

/// <summary>A caller path as the worker's file system sees it. The host's checks are lexical; this is what the worker observed.</summary>
public class PlcAlarmPathInfo
{
    /// <summary>The normalized full path (<c>Path.GetFullPath</c>).</summary>
    public string FullPath { get; set; } = string.Empty;

    public bool Exists { get; set; }

    public bool IsDirectory { get; set; }

    /// <summary>True when the parent directory exists.</summary>
    public bool ParentExists { get; set; }

    /// <summary>The <c>DriveInfo.DriveType</c> of the path's root by name; null when unreadable.</summary>
    public string? DriveType { get; set; }

    /// <summary>True on a mapped network drive (DriveType Network). Allowed; reported as an info guard and a warning.</summary>
    public bool NetworkDrive { get; set; }

    /// <summary>True when the path itself exists and is a link (reparse point).</summary>
    public bool ReparsePoint { get; set; }

    /// <summary>Existing ancestor directories that are links (reparse points): the file lands where they point.</summary>
    public List<string> ReparseAncestors { get; set; } = new List<string>();
}

/// <summary><c>get_prodiag_fb</c>: what blocks.md documents about a ProDiag FB.</summary>
public class PlcProDiagFbInfo
{
    public string BlockPath { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The runtime block kind (FB, FC, ...); only an FB has supervisions.</summary>
    public string BlockType { get; set; } = string.Empty;

    /// <summary>PlcBlock.ProgrammingLanguage by member name; null when unreadable.</summary>
    public string? ProgrammingLanguage { get; set; }

    /// <summary>True when ProgrammingLanguage is ProDiag.</summary>
    public bool IsProDiag { get; set; }

    /// <summary>FB.Supervisions.Count; null when not an FB or unreadable.</summary>
    public int? SupervisionCount { get; set; }

    /// <summary>PlcBlock.IsConsistent; null when unreadable.</summary>
    public bool? IsConsistent { get; set; }

    /// <summary>True when FB.GetService&lt;SupervisionProvider&gt;() returned a provider.</summary>
    public bool HasSupervisionProvider { get; set; }

    /// <summary>ProDiagVersion, InitialValueAcquisition and UseCentralTimeStamp through GetAttribute (an unreadable one is listed in unreadable).</summary>
    public List<PlcProDiagAttributeInfo> Attributes { get; set; } = new List<PlcProDiagAttributeInfo>();

    public List<string> Unreadable { get; set; } = new List<string>();
}

public class PlcProDiagAttributeInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }
}

/// <summary><c>get_prodiag_assignment</c>: the AssignedProDiagFB attribute of an instance DB, a UDT-instance DB member or a tag.</summary>
public class PlcProDiagAssignmentInfo
{
    /// <summary>instanceDb, dbMember or tag.</summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>The block path, block path plus member path, or folder, table and tag name.</summary>
    public string Target { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>GetAttribute("AssignedProDiagFB") as text; null when empty or unreadable (see readable).</summary>
    public string? AssignedProDiagFb { get; set; }

    /// <summary>False when the attribute could not be read (blocks.md: only UDT instances in global DBs and tag tables carry it).</summary>
    public bool Readable { get; set; }

    public string? Message { get; set; }
}

public class PlcAlarmCellChangeInfo
{
    public string Sheet { get; set; } = string.Empty;

    public string Cell { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Before { get; set; } = string.Empty;

    public string After { get; set; } = string.Empty;
}
