namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// Worker method and public operation names of the PLC alarm, alarm class, text list and ProDiag
/// supervision operations. Each <c>plc_read</c>/<c>plc_write</c> operation uses its public name as its
/// worker method (like the watch-table operations). They feed <c>OperationPolicyCatalog</c>,
/// <c>ProjectCapabilityCatalog</c> and the worker dispatch switch.
/// </summary>
public static class PlcAlarmMethods
{
    // plc_read: offline observation (Observe).
    public const string ListTextLists = "list_text_lists";
    public const string GetProDiagFb = "get_prodiag_fb";
    public const string GetProDiagAssignment = "get_prodiag_assignment";

    // plc_read: XLSX export through a temporary file that is deleted again (TemporaryExport).
    public const string ReadTextListWorkbook = "read_text_list_workbook";
    public const string ReadAlarmTextWorkbook = "read_alarm_text_workbook";
    public const string ReadSupervisionWorkbook = "read_supervision_workbook";

    // plc_write: project mutations and file exports to caller paths (ProjectMutation).
    public const string UpdateTextList = "update_text_list";
    public const string DeleteTextList = "delete_text_list";
    public const string UpdateTextListCells = "update_text_list_cells";
    public const string UpdateAlarmTextCells = "update_alarm_text_cells";
    public const string UpdateSupervisionCells = "update_supervision_cells";
    public const string ExportTextLists = "export_text_lists";
    public const string ImportTextLists = "import_text_lists";
    public const string ExportAlarmTexts = "export_alarm_texts";
    public const string ImportAlarmTexts = "import_alarm_texts";
    public const string ExportAlarmClasses = "export_alarm_classes";
    public const string ImportAlarmClasses = "import_alarm_classes";
    public const string ExportSupervisions = "export_supervisions";
    public const string ImportSupervisions = "import_supervisions";
    public const string ImportSupervisionSettings = "import_supervision_settings";
    public const string SetProDiagAssignment = "set_prodiag_assignment";
    public const string ExportProDiagInfo = "export_prodiag_info";

    /// <summary>
    /// The project's system diagnostics settings (GitHub issue #19): <c>SystemdiagnosticsSettingsDataProvider.Export(FileInfo)</c> /
    /// <c>.Import(FileInfo)</c> (Siemens.Engineering.Base.xml, V21), the provider found among the Project's <c>GetServiceInfos()</c>.
    /// Project-wide and standalone-only like the alarm classes.
    /// </summary>
    public const string ExportSystemDiagnosticsSettings = "export_system_diagnostics_settings";
    public const string ImportSystemDiagnosticsSettings = "import_system_diagnostics_settings";

    /// <summary>
    /// Worker-only, non-mutating (Observe): the planning evidence of one write item (<see cref="PlcAlarmPlanInfo"/>). It is
    /// not a public operation; <c>ProjectCapabilityCatalog</c> gates it as plc_write.
    /// </summary>
    public const string Plan = "plc_alarm_plan";

    public static IReadOnlyList<string> ObserveMethods { get; } = new[] { ListTextLists, GetProDiagFb, GetProDiagAssignment };

    public static IReadOnlyList<string> TemporaryExportMethods { get; } = new[]
    {
        ReadTextListWorkbook, ReadAlarmTextWorkbook, ReadSupervisionWorkbook,
    };

    public static IReadOnlyList<string> Read { get; } = ObserveMethods.Concat(TemporaryExportMethods).ToArray();

    public static IReadOnlyList<string> Write { get; } = new[]
    {
        UpdateTextList, DeleteTextList, UpdateTextListCells, UpdateAlarmTextCells, UpdateSupervisionCells,
        ExportTextLists, ImportTextLists, ExportAlarmTexts, ImportAlarmTexts, ExportAlarmClasses, ImportAlarmClasses,
        ExportSupervisions, ImportSupervisions, ImportSupervisionSettings, SetProDiagAssignment, ExportProDiagInfo,
        ExportSystemDiagnosticsSettings, ImportSystemDiagnosticsSettings,
    };

    /// <summary>The public plc_read and plc_write operations (<see cref="Plan"/> is not one).</summary>
    public static IReadOnlyList<string> All { get; } = Read.Concat(Write).ToArray();

    /// <summary>
    /// The common alarm classes are reached through <c>Project.GetService&lt;AlarmClassDataProvider&gt;()</c>,
    /// so they are project-wide (no PLC) and delivered for standalone projects only.
    /// </summary>
    public static IReadOnlyList<string> StandaloneOnlyOperations { get; } = new[]
    {
        ExportAlarmClasses, ImportAlarmClasses, ExportSystemDiagnosticsSettings, ImportSystemDiagnosticsSettings,
    };

    /// <summary>Writes that create or replace a file at a caller-supplied path.</summary>
    public static IReadOnlyList<string> FileExports { get; } = new[]
    {
        ExportTextLists, ExportAlarmTexts, ExportAlarmClasses, ExportSupervisions, ExportSystemDiagnosticsSettings,
    };

    /// <summary>Writes that read a caller-supplied file into the project.</summary>
    public static IReadOnlyList<string> FileImports { get; } = new[]
    {
        ImportTextLists, ImportAlarmTexts, ImportAlarmClasses, ImportSupervisions, ImportSupervisionSettings, ImportSystemDiagnosticsSettings,
    };

    /// <summary>The three workbook cell updates (export, patch, import, re-export).</summary>
    public static IReadOnlyList<string> CellUpdates { get; } = new[] { UpdateTextListCells, UpdateAlarmTextCells, UpdateSupervisionCells };

    /// <summary>Operations addressed by a ProDiag FB (<c>blockPath</c>) instead of a PLC.</summary>
    public static IReadOnlyList<string> SupervisionOperations { get; } = new[]
    {
        ReadSupervisionWorkbook, UpdateSupervisionCells, ExportSupervisions, ImportSupervisions, ImportSupervisionSettings,
    };

    /// <summary>
    /// The ProDiag operations of blocks.md: the FB's documented evidence, its alarm information export, and the
    /// AssignedProDiagFB attribute of instance DBs, UDT-instance DB members and tags.
    /// </summary>
    public static IReadOnlyList<string> ProDiagOperations { get; } = new[] { GetProDiagFb, GetProDiagAssignment, SetProDiagAssignment, ExportProDiagInfo };

    /// <summary>Writes that call a TIA Portal export or import (and so may raise confirmation dialogs, answered by portalConfirmations).</summary>
    public static IReadOnlyList<string> Transfers { get; } = CellUpdates.Concat(FileExports).Concat(FileImports).Append(ExportProDiagInfo).ToArray();

    public static bool IsWorkerMethod(string? method) => method is not null && (method == Plan || All.Contains(method, StringComparer.Ordinal));

    /// <summary>The operations whose target is a block path (supervisions, ProDiag FBs, and the instance DB or DB member of an assignment).</summary>
    public static bool UsesBlockPath(string? method)
        => IsSupervision(method) || method is GetProDiagFb or ExportProDiagInfo;

    public static bool IsReadMethod(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWriteMethod(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsProjectScoped(string? method)
        => method is ExportAlarmClasses or ImportAlarmClasses or ExportSystemDiagnosticsSettings or ImportSystemDiagnosticsSettings;

    public static bool IsSystemDiagnostics(string? method) => method is ExportSystemDiagnosticsSettings or ImportSystemDiagnosticsSettings;

    public static bool IsSupervision(string? method) => method is not null && SupervisionOperations.Contains(method, StringComparer.Ordinal);

    /// <summary>The read that exports the same workbook a cell update edits (its planning and verification read).</summary>
    public static string? WorkbookReadFor(string? method) => method switch
    {
        UpdateTextListCells => ReadTextListWorkbook,
        UpdateAlarmTextCells => ReadAlarmTextWorkbook,
        UpdateSupervisionCells => ReadSupervisionWorkbook,
        _ => null,
    };

    /// <summary>The workbook kind a read or cell update addresses.</summary>
    public static string? WorkbookKind(string? method) => method switch
    {
        ReadTextListWorkbook or UpdateTextListCells or ExportTextLists or ImportTextLists => PlcAlarmPolicy.WorkbookKinds.TextLists,
        ReadAlarmTextWorkbook or UpdateAlarmTextCells or ExportAlarmTexts or ImportAlarmTexts => PlcAlarmPolicy.WorkbookKinds.AlarmTexts,
        ReadSupervisionWorkbook or UpdateSupervisionCells or ExportSupervisions or ImportSupervisions => PlcAlarmPolicy.WorkbookKinds.Supervisions,
        ImportSupervisionSettings => PlcAlarmPolicy.WorkbookKinds.SupervisionSettings,
        ExportAlarmClasses or ImportAlarmClasses => PlcAlarmPolicy.WorkbookKinds.AlarmClasses,
        ExportSystemDiagnosticsSettings or ImportSystemDiagnosticsSettings => PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings,
        _ => null,
    };

    /// <summary>The read whose workbook a file import is verified against after the import (null when none is documented).</summary>
    public static string? ReexportFor(string? method) => method switch
    {
        ImportTextLists => ReadTextListWorkbook,
        ImportAlarmTexts => ReadAlarmTextWorkbook,
        ImportSupervisions => ReadSupervisionWorkbook,
        _ => null,
    };
}
