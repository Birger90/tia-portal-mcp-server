namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// Worker parameters of every <see cref="PlcAlarmMethods"/> method (<c>WorkerRequest.PlcAlarm</c>). Only the
/// fields of the called method are read; the host catalog already restricted them per operation.
/// </summary>
public sealed class PlcAlarmRequestInfo
{
    /// <summary>PLC software or device name (case-insensitive); null selects the only PLC. Unused by the alarm-class methods.</summary>
    public string? PlcName { get; set; }

    /// <summary>The ProDiag FB of the supervision methods, as a block path.</summary>
    public string? BlockPath { get; set; }

    /// <summary>update_text_list / delete_text_list: the user text list (case-insensitive, unique).</summary>
    public string? TextListName { get; set; }

    /// <summary>update_text_list: the new name.</summary>
    public string? NewName { get; set; }

    /// <summary>update_text_list: the ListRange enum member name as the installed TIA Portal spells it.</summary>
    public string? ListRange { get; set; }

    /// <summary>update_text_list: the comment text for <see cref="CommentCulture"/>.</summary>
    public string? Comment { get; set; }

    public string? CommentCulture { get; set; }

    /// <summary>Text list workbook reads, cell updates and exports: narrow to these text lists (the filtered ExportToXlsx overload).</summary>
    public List<string>? TextListNames { get; set; }

    /// <summary>Cultures (project languages) of the workbook; null exports every active project language.</summary>
    public List<string>? Cultures { get; set; }

    /// <summary>PlcAlarmTextXlsxExportOption member name of the alarm-text exports.</summary>
    public string? ExportOption { get; set; }

    /// <summary>ImportOptions member name of the text-list and supervision imports (None or Override).</summary>
    public string? ImportOption { get; set; }

    /// <summary>The caller's absolute local file of an export or import.</summary>
    public string? FilePath { get; set; }

    /// <summary>Exports: replace an existing file.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Cell updates: the edits, applied in order to the exported workbook.</summary>
    public List<PlcAlarmCellEditInfo>? CellEdits { get; set; }

    /// <summary>Cell updates: the workbook hash of the read the edits were based on.</summary>
    public string? ExpectedWorkbookHash { get; set; }

    /// <summary>Workbook reads: rows returned per sheet.</summary>
    public int MaxRows { get; set; } = PlcAlarmPolicy.DefaultMaxRows;

    /// <summary>Workbook reads: return rows of this sheet only (exact name, else the one case-insensitive match).</summary>
    public string? Sheet { get; set; }

    /// <summary>Workbook reads with <see cref="Sheet"/>: the first row number returned.</summary>
    public int StartRow { get; set; } = 1;

    /// <summary>Workbook reads with <see cref="Sheet"/>: the first column (letters) of each returned row.</summary>
    public string StartColumn { get; set; } = "A";

    /// <summary>Workbook reads: the characters the result may take; the host passes its effective value budget minus headroom.</summary>
    public int MaxResultChars { get; set; } = PlcAlarmPolicy.DefaultMaxResultChars;

    /// <summary>Workbook reads (host planning and verification only): cells whose full value is returned in <c>probes</c>.</summary>
    public List<PlcAlarmCellRefInfo>? Probes { get; set; }

    /// <summary>Writes: how TIA Portal confirmation dialogs are answered (decline, the default, or accept).</summary>
    public string? PortalConfirmations { get; set; }

    /// <summary><c>plc_alarm_plan</c>: the write method the planning evidence is for.</summary>
    public string? PlannedOperation { get; set; }

    /// <summary>ProDiag assignment of a DB member: the member path (blocks.md: a UDT instance in a global DB).</summary>
    public string? MemberPath { get; set; }

    /// <summary>ProDiag assignment of a tag: the tag table, its folder and the tag name.</summary>
    public string? TableName { get; set; }

    public string? FolderPath { get; set; }

    public string? Name { get; set; }

    /// <summary>set_prodiag_assignment: the name of the ProDiag FB to assign.</summary>
    public string? ProDiagFb { get; set; }

    /// <summary>export_prodiag_info: the absolute local directory to create.</summary>
    public string? DirectoryPath { get; set; }
}

/// <summary>One cell edit: the cell must currently hold <see cref="Expected"/> ("" for an empty cell).</summary>
public sealed class PlcAlarmCellEditInfo
{
    public string Sheet { get; set; } = string.Empty;

    /// <summary>A1 reference, e.g. C5.</summary>
    public string Cell { get; set; } = string.Empty;

    public string? Expected { get; set; }

    public string? Value { get; set; }

    /// <summary>string or number; null keeps the current cell's kind (string for an empty cell).</summary>
    public string? Kind { get; set; }
}

public sealed class PlcAlarmCellRefInfo
{
    public string Sheet { get; set; } = string.Empty;

    public string Cell { get; set; } = string.Empty;
}
