using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using TiaMcpServer.Contracts.Files;

namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// Siemens-free rules of the PLC alarm, text list and supervision operations, shared by the host catalog,
/// the host planner and the worker: limits, caller path rules, culture and enum-member name syntax, cell
/// references and the interpretation of TIA Portal result states. Nothing here invents a Siemens member:
/// enum members are only checked for identifier syntax, the installed TIA Portal decides whether they exist.
/// </summary>
public static class PlcAlarmPolicy
{
    public static class WorkbookKinds
    {
        public const string TextLists = "textLists";
        public const string AlarmTexts = "alarmTexts";
        public const string Supervisions = "supervisions";
        public const string SupervisionSettings = "supervisionSettings";
        public const string AlarmClasses = "alarmClasses";

        /// <summary>The project's system diagnostics settings (issue #19); a file of undocumented format, like the alarm classes.</summary>
        public const string SystemDiagnosticsSettings = "systemDiagnosticsSettings";

        /// <summary>
        /// Kinds whose file is no XLSX workbook and whose format is undocumented: the import is verified by a byte compare of a
        /// re-export, and <c>Warning</c> counts as success with a warning (both result enums document Success / Warning / Error,
        /// Warning as a successful process with warnings).
        /// </summary>
        public static bool IsOpaqueFile(string? kind) => kind is AlarmClasses or SystemDiagnosticsSettings;
    }

    /// <summary>Cell kinds a workbook read reports.</summary>
    public static class CellKinds
    {
        public const string String = "string";
        public const string Number = "number";
        public const string Boolean = "boolean";
        public const string Error = "error";
        public const string Formula = "formula";

        /// <summary>The kinds a cell edit may write.</summary>
        public static IReadOnlyList<string> Writable { get; } = new[] { String, Number };
    }

    /// <summary><c>Siemens.Engineering.ImportOptions</c> member names the sources show (<c>None</c>, <c>Override</c>).</summary>
    public static IReadOnlyList<string> ImportOptionNames { get; } = new[] { "None", "Override" };

    /// <summary>Every import sample in the sources passes <c>ImportOptions.Override</c>.</summary>
    public const string DefaultImportOption = "Override";

    /// <summary>Rows a workbook read returns per sheet by default; a read also stops at its character budget.</summary>
    public const int DefaultMaxRows = 50;
    public const int MaxRows = 5000;

    /// <summary>
    /// The character budget of one workbook read result when the request names none: the default value budget
    /// (60,000) minus room for the response envelope. The host passes the effective budget (raised with --max-item-chars).
    /// </summary>
    public const int DefaultMaxResultChars = 56_000;

    /// <summary>What the host keeps free of the effective value budget for the rest of the response item.</summary>
    public const int ResultHeadroomChars = 4_000;

    /// <summary>The smallest result budget a read accepts (one row of short cells).</summary>
    public const int MinResultChars = 2_000;

    /// <summary>Temporary files and directories this server left behind (worker crash or timeout) are removed after this age.</summary>
    public static readonly TimeSpan StaleTemporaryAge = TimeSpan.FromHours(2);

    /// <summary>The <c>ProgrammingLanguage</c> member name of a ProDiag FB (blocks.md: <c>ProgrammingLanguage.ProDiag</c>).</summary>
    public const string ProDiagLanguage = "ProDiag";

    /// <summary>The attribute blocks.md reads and writes on instance DBs, UDT-instance DB members and tags.</summary>
    public const string AssignedProDiagFbAttribute = "AssignedProDiagFB";

    /// <summary>The ProDiag-FB attributes blocks.md lists for GetAttribute/GetAttributes.</summary>
    public static IReadOnlyList<string> ProDiagFbAttributes { get; } = new[] { "ProDiagVersion", "InitialValueAcquisition", "UseCentralTimeStamp" };

    /// <summary>The objects that carry <see cref="AssignedProDiagFbAttribute"/>.</summary>
    public static class AssignmentTargets
    {
        public const string InstanceDb = "instanceDb";
        public const string DbMember = "dbMember";
        public const string Tag = "tag";
    }
    public const int MaxCellEdits = 500;
    public const int MaxTextListNames = 200;
    public const int MaxCultures = 50;
    public const int MaxNameChars = 256;
    public const int MaxPathChars = 1024;

    /// <summary>Excel's documented limit of one cell's text.</summary>
    public const int MaxCellValueChars = 32767;

    /// <summary>A read returns at most this many characters of one cell (the rest is reported as truncated).</summary>
    public const int MaxReturnedValueChars = 4000;

    /// <summary>
    /// The filtered <c>ExportToXlsx(FileInfo, IEnumerable&lt;string&gt;, IEnumerable&lt;Language&gt;)</c> takes text list names and
    /// languages together; the server does not invent the name list, so cultures narrow a text list export only with textListNames.
    /// </summary>
    public const string CulturesNeedTextListNames =
        "cultures narrows the text list workbook only together with textListNames (the filtered ExportToXlsx overload takes both); list_text_lists returns the names.";

    public const string CellUpdatesNeedTextListNames =
        "update_text_list_cells needs textListNames (user text lists only): it exports and imports again only the named lists, never a system "
        + "text list. Read the same lists with read_text_list_workbook and the same textListNames and cultures; list_text_lists returns the names.";

    /// <summary>The workbook hash names its own format so it is never mistaken for a SimaticML content hash.</summary>
    public const string WorkbookHashPrefix = "cells:sha256:";

    /// <summary>Excel's sheet column limit (XFD) and row limit.</summary>
    public const int MaxColumn = 16384;
    public const int MaxRow = 1048576;

    private static readonly Regex CulturePattern = new("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,3}$", RegexOptions.CultureInvariant);
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex CellPattern = new("^([A-Z]{1,3})([1-9][0-9]{0,6})$", RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new("^cells:sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    public static bool IsCultureName(string? culture) => culture is not null && CulturePattern.IsMatch(culture);

    /// <summary>Identifier syntax of an enum member name the caller passes; the installed enum decides whether it exists.</summary>
    public static bool IsMemberName(string? name) => name is not null && IdentifierPattern.IsMatch(name);

    public static bool IsWorkbookHash(string? hash) => hash is not null && HashPattern.IsMatch(hash);

    /// <summary>A nonblank object name without control, format or separator characters.</summary>
    public static string? CheckName(string? name, string field)
    {
        if (string.IsNullOrWhiteSpace(name)) return $"{field} must be nonblank.";
        if (name!.Length > MaxNameChars) return $"{field} must be at most {MaxNameChars} characters.";
        return HasUnsafeCharacters(name) ? $"{field} must not contain control or format characters." : null;
    }

    /// <summary>A cell value to write: at most Excel's cell limit and text XML 1.0 can carry (<see cref="CheckText"/>).</summary>
    public static string? CheckCellValue(string? value)
        => value is null ? "value is required (an empty string clears a text cell)." : CheckText(value, "value");

    /// <summary>
    /// A text written into a workbook cell or a multilingual text item: at most Excel's cell limit, only characters XML 1.0
    /// allows (<see cref="XmlConvert.IsXmlChar"/>, surrogate pairs through <see cref="XmlConvert.IsXmlSurrogatePair"/>;
    /// U+FFFE, U+FFFF and unpaired surrogates are refused) and no control character except tab, CR and LF.
    /// </summary>
    public static string? CheckText(string value, string field)
    {
        if (value.Length > MaxCellValueChars) return $"{field} must be at most {MaxCellValueChars} characters (Excel's cell limit).";
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && XmlConvert.IsXmlSurrogatePair(value[index + 1], character))
                {
                    index++;
                    continue;
                }

                return $"{field} must not contain unpaired surrogates.";
            }

            if (char.IsLowSurrogate(character)) return $"{field} must not contain unpaired surrogates.";
            if (char.IsControl(character) && character is not ('\t' or '\r' or '\n'))
                return $"{field} must not contain control characters other than tab, CR and LF.";
            if (!XmlConvert.IsXmlChar(character))
                return $"{field} must not contain U+{(int)character:X4}, which XML 1.0 cannot carry.";
        }

        return null;
    }

    /// <summary>Parses an A1 cell reference (upper-case column letters, row from 1); null when malformed or beyond Excel's limits.</summary>
    public static (int Column, int Row)? ParseCell(string? cell)
    {
        if (cell is null) return null;
        var match = CellPattern.Match(cell);
        if (!match.Success) return null;
        var column = 0;
        foreach (var letter in match.Groups[1].Value) column = column * 26 + (letter - 'A' + 1);
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var row)) return null;
        return column is < 1 or > MaxColumn || row is < 1 or > MaxRow ? null : (column, row);
    }

    /// <summary>Upper-case column letters A to XFD; null when malformed.</summary>
    public static int? ParseColumn(string? letters)
        => letters is { Length: >= 1 and <= 3 } && letters.All(c => c is >= 'A' and <= 'Z') && ParseCell(letters + "1") is { } cell ? cell.Column : null;

    /// <summary>The import check outcomes (<see cref="PlcAlarmImportCheckInfo.Outcome"/>).</summary>
    public static class ImportOutcomes
    {
        public const string Verified = "verified";
        public const string Contradicted = "contradicted";
        public const string Inconclusive = "inconclusive";
        public const string NotReadBack = "notReadBack";

        public static IReadOnlyList<string> All { get; } = new[] { Verified, Contradicted, Inconclusive, NotReadBack };
    }

    public static string CellName(int column, int row) => ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

    public static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            var remainder = (column - 1) % 26;
            name = (char)('A' + remainder) + name;
            column = (column - 1) / 26;
        }

        return name;
    }

    /// <summary>A number cell value: invariant decimal or exponent notation of a finite double.</summary>
    public static bool IsNumber(string? value)
        => value is not null && value.Length > 0 && value.Trim() == value
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && !double.IsNaN(number) && !double.IsInfinity(number);

    /// <summary>
    /// A caller-supplied file path, checked lexically by the shared <see cref="LocalFilePaths.CheckFile"/>: absolute on a
    /// drive or a UNC share (elsewhere than on Windows, where only the tests run, rooted at <c>/</c>), naming a file, without
    /// device prefixes, <c>..</c> segments, alternate data streams, reserved device names, segments ending in a space or a
    /// dot, and control or format characters. Mapped network drives pass (the worker reports them). Null when acceptable.
    /// </summary>
    public static string? CheckFilePath(string? path, string? requiredExtension) => LocalFilePaths.CheckFile(path, "filePath", requiredExtension);

    /// <summary>A caller-supplied directory (export_prodiag_info), checked like <see cref="CheckFilePath"/>.</summary>
    public static string? CheckDirectoryPath(string? path) => LocalFilePaths.CheckDirectory(path, "directoryPath");

    public static bool IsUncOrDevicePath(string path) => LocalFilePaths.IsUncOrDevicePath(path);

    /// <summary>
    /// The file extension a file operation requires: XLSX workbooks, and <c>.DAT</c> for the common alarm classes (the
    /// Siemens example names <c>AlarmClasses.DAT</c>; live on V21 2026-11-02 an <c>.xml</c> name made TIA Portal show the
    /// modal "ungültige Dateiendung" dialog (0406:000014) and the call waited on it, K46). Matched case-insensitively.
    /// </summary>
    /// <remarks>The system diagnostics settings require none: neither the V21 XML nor a sample states an extension, so any
    /// extension passes (TIA Portal may still refuse one, as it did for the alarm classes, K46).</remarks>
    public static string? RequiredExtension(string? method)
        => method switch
        {
            PlcAlarmMethods.ExportAlarmClasses or PlcAlarmMethods.ImportAlarmClasses => ".DAT",
            PlcAlarmMethods.ExportSystemDiagnosticsSettings or PlcAlarmMethods.ImportSystemDiagnosticsSettings => null,
            _ => ".xlsx",
        };

    /// <summary>
    /// The interpretation of a TIA Portal result state name. Only a member named exactly <c>Success</c> is
    /// success (the sources name <c>Success</c> for the alarm-class and supervision results); <c>Warning</c>
    /// is accepted with a warning where the sources name it (alarm classes: Success / Warning / Error; system diagnostics
    /// settings: <c>SystemdiagnosticsSettingsExportImportResultState</c> Success "Succesful process", Warning "Succesful process
    /// with warnings", Error "Failed process" in Siemens.Engineering.Base.xml).
    /// Every other name, and an unreadable state, fails closed.
    /// </summary>
    public static StateVerdict InterpretState(string? state, bool warningIsSuccess)
        => state switch
        {
            "Success" => StateVerdict.Success,
            "Warning" when warningIsSuccess => StateVerdict.Warning,
            null => StateVerdict.Unknown,
            _ => StateVerdict.Failure,
        };

    public enum StateVerdict
    {
        Success,
        Warning,
        Failure,
        Unknown,
    }

    /// <summary>Text bounded for a result field: at most <paramref name="max"/> characters, with an ellipsis marker.</summary>
    public static string? Bound(string? text, int max = 4000)
        => text is null || text.Length <= max ? text : text.Substring(0, max) + " [TRUNCATED]";

    public static bool HasUnsafeCharacters(string text) => LocalFilePaths.HasUnsafeCharacters(text);
}
