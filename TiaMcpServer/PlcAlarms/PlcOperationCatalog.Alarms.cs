using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;

namespace TiaMcpServer.Plc;

/// <summary>
/// The PLC alarm, alarm class, text list and supervision operations of plc_read and plc_write: field sets and the
/// shape rules checked before any worker call. Non-string required fields (cellEdits) are listed as optional in the
/// field sets and required here, because the shared required-field check reads string fields only.
/// </summary>
public static partial class PlcOperationCatalog
{
    private static Dictionary<string, Spec> WithAlarmSpecs(Dictionary<string, Spec> specs)
    {
        const string Confirmations = "portalConfirmations";
        string[] paging = { "maxElements", "sheet", "startRow", "startColumn" };
        string[] assignment = { "plcName", "blockPath", "memberPath", "tableName", "name", "folderPath" };
        specs[PlcAlarmMethods.ListTextLists] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[PlcAlarmMethods.ReadTextListWorkbook] = Read(Array.Empty<string>(), new[] { "plcName", "textListNames", "cultures" }.Concat(paging).ToArray());
        specs[PlcAlarmMethods.ReadAlarmTextWorkbook] = Read(new[] { "exportOption" }, new[] { "plcName", "cultures" }.Concat(paging).ToArray());
        specs[PlcAlarmMethods.ReadSupervisionWorkbook] = Read(new[] { "blockPath" }, paging);
        specs[PlcAlarmMethods.GetProDiagFb] = Read(new[] { "blockPath" }, Array.Empty<string>());
        specs[PlcAlarmMethods.GetProDiagAssignment] = Read(Array.Empty<string>(), assignment);

        specs[PlcAlarmMethods.UpdateTextList] = Write(new[] { "textListName" }, new[] { "plcName", "newName", "listRange", "comment", "commentCulture", Confirmations });
        specs[PlcAlarmMethods.DeleteTextList] = Write(new[] { "textListName" }, new[] { "plcName", Confirmations });
        specs[PlcAlarmMethods.UpdateTextListCells] = Write(new[] { "expectedWorkbookHash" }, new[] { "plcName", "textListNames", "cultures", "cellEdits", "importOption", Confirmations });
        specs[PlcAlarmMethods.UpdateAlarmTextCells] = Write(new[] { "exportOption", "expectedWorkbookHash" }, new[] { "plcName", "cultures", "cellEdits", Confirmations });
        specs[PlcAlarmMethods.UpdateSupervisionCells] = Write(new[] { "blockPath", "expectedWorkbookHash" }, new[] { "cellEdits", "importOption", Confirmations });
        specs[PlcAlarmMethods.ExportTextLists] = Write(new[] { "filePath" }, new[] { "plcName", "overwrite", "textListNames", "cultures", Confirmations });
        specs[PlcAlarmMethods.ImportTextLists] = Write(new[] { "filePath" }, new[] { "plcName", "importOption", Confirmations });
        specs[PlcAlarmMethods.ExportAlarmTexts] = Write(new[] { "filePath", "exportOption" }, new[] { "plcName", "overwrite", "cultures", Confirmations });
        specs[PlcAlarmMethods.ImportAlarmTexts] = Write(new[] { "filePath" }, new[] { "plcName", "cultures", "exportOption", Confirmations });
        specs[PlcAlarmMethods.ExportAlarmClasses] = Write(new[] { "filePath" }, new[] { "overwrite", Confirmations });
        specs[PlcAlarmMethods.ImportAlarmClasses] = Write(new[] { "filePath" }, new[] { Confirmations });
        specs[PlcAlarmMethods.ExportSystemDiagnosticsSettings] = Write(new[] { "filePath" }, new[] { "overwrite", Confirmations });
        specs[PlcAlarmMethods.ImportSystemDiagnosticsSettings] = Write(new[] { "filePath" }, new[] { Confirmations });
        specs[PlcAlarmMethods.ExportSupervisions] = Write(new[] { "blockPath", "filePath" }, new[] { "overwrite", Confirmations });
        specs[PlcAlarmMethods.ImportSupervisions] = Write(new[] { "blockPath", "filePath" }, new[] { "importOption", Confirmations });
        specs[PlcAlarmMethods.ImportSupervisionSettings] = Write(new[] { "blockPath", "filePath" }, new[] { "importOption", Confirmations });
        specs[PlcAlarmMethods.SetProDiagAssignment] = Write(new[] { "proDiagFb" }, assignment.Append(Confirmations).ToArray());
        specs[PlcAlarmMethods.ExportProDiagInfo] = Write(new[] { "blockPath", "directoryPath" }, new[] { Confirmations });
        return specs;
    }

    private static void ValidateAlarmItem(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (!PlcAlarmMethods.IsWorkerMethod(operation.Operation)) return;
        void Check(string? error)
        {
            if (error is not null) errors.Add($"{prefix}: {error}");
        }

        if (operation.TextListName is not null) Check(PlcAlarmPolicy.CheckName(operation.TextListName, "textListName"));
        if (operation.Operation == PlcAlarmMethods.UpdateTextList)
        {
            if (operation.NewName is null && operation.ListRange is null && operation.Comment is null)
                Check("update_text_list needs newName, listRange or comment with commentCulture.");
            if (operation.NewName is not null) Check(PlcAlarmPolicy.CheckName(operation.NewName, "newName"));
            if (operation.ListRange is not null && !PlcAlarmPolicy.IsMemberName(operation.ListRange))
                Check("listRange must be an enum member name (letters, digits and '_') as list_text_lists reports it.");
            if (operation.Comment is null != operation.CommentCulture is null) Check("comment and commentCulture go together.");
            if (operation.CommentCulture is not null && !PlcAlarmPolicy.IsCultureName(operation.CommentCulture))
                Check($"commentCulture '{operation.CommentCulture}' is not a culture name such as en-US.");
            if (operation.Comment is not null && PlcAlarmPolicy.CheckCellValue(operation.Comment) is { } comment) Check("comment: " + comment);
        }

        if (operation.TextListNames is { } names)
        {
            if (names.Count is < 1 or > PlcAlarmPolicy.MaxTextListNames) Check($"textListNames must hold 1 to {PlcAlarmPolicy.MaxTextListNames} names.");
            foreach (var name in names) Check(PlcAlarmPolicy.CheckName(name, "textListNames entry"));
            if (names.Where(name => name is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count(name => name is not null))
                Check("textListNames must not repeat a name.");
        }

        if (operation.Cultures is { } cultures)
        {
            if (cultures.Count is < 1 or > PlcAlarmPolicy.MaxCultures) Check($"cultures must hold 1 to {PlcAlarmPolicy.MaxCultures} culture names.");
            foreach (var culture in cultures.Where(culture => !PlcAlarmPolicy.IsCultureName(culture)))
                Check($"cultures entry '{culture}' is not a culture name such as en-US.");
            if (cultures.Where(PlcAlarmPolicy.IsCultureName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cultures.Count(PlcAlarmPolicy.IsCultureName))
                Check("cultures must not repeat a culture.");
        }

        if (operation.Cultures is not null && operation.TextListNames is null
            && PlcAlarmMethods.WorkbookKind(operation.Operation) == PlcAlarmPolicy.WorkbookKinds.TextLists)
            Check(PlcAlarmPolicy.CulturesNeedTextListNames);
        if (operation.ExportOption is not null && !PlcAlarmPolicy.IsMemberName(operation.ExportOption))
            Check("exportOption must be a PlcAlarmTextXlsxExportOption member name (letters, digits and '_').");
        if (operation.ImportOption is not null && !PlcAlarmPolicy.ImportOptionNames.Contains(operation.ImportOption, StringComparer.Ordinal))
            Check($"importOption must be one of {string.Join(", ", PlcAlarmPolicy.ImportOptionNames)}.");
        if (operation.FilePath is not null) Check(PlcAlarmPolicy.CheckFilePath(operation.FilePath, PlcAlarmPolicy.RequiredExtension(operation.Operation)));
        if (operation.DirectoryPath is not null) Check(PlcAlarmPolicy.CheckDirectoryPath(operation.DirectoryPath));
        if (operation.Sheet is not null) Check(PlcAlarmPolicy.CheckName(operation.Sheet, "sheet"));
        if (operation.StartRow is { } startRow)
        {
            if (operation.Sheet is null) Check("startRow pages one sheet; pass sheet with it.");
            if (startRow is < 1 or > PlcAlarmPolicy.MaxRow) Check($"startRow must be between 1 and {PlcAlarmPolicy.MaxRow}.");
        }

        if (operation.StartColumn is { } startColumn)
        {
            if (operation.Sheet is null) Check("startColumn pages one sheet; pass sheet with it.");
            if (PlcAlarmPolicy.ParseColumn(startColumn) is null) Check($"startColumn must be upper-case column letters from A to {PlcAlarmPolicy.ColumnName(PlcAlarmPolicy.MaxColumn)}.");
        }

        if (operation.Operation == PlcAlarmMethods.UpdateTextListCells && operation.TextListNames is null) Check(PlcAlarmPolicy.CellUpdatesNeedTextListNames);

        if (operation.PortalConfirmations is not null && !PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(operation.PortalConfirmations))
            Check($"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.");
        if (operation.ProDiagFb is not null) Check(PlcAlarmPolicy.CheckName(operation.ProDiagFb, "proDiagFb"));
        if (operation.Operation is PlcAlarmMethods.GetProDiagAssignment or PlcAlarmMethods.SetProDiagAssignment) ValidateAssignmentTarget(operation, prefix, Check);
        if ((PlcAlarmMethods.UsesBlockPath(operation.Operation) || operation.Operation is PlcAlarmMethods.GetProDiagAssignment or PlcAlarmMethods.SetProDiagAssignment)
            && !string.IsNullOrWhiteSpace(operation.BlockPath))
        {
            try
            {
                _ = BlockAddress.Parse(operation.BlockPath!);
            }
            catch (ArgumentException exception)
            {
                Check(exception.Message);
            }
        }

        if (PlcAlarmMethods.TemporaryExportMethods.Contains(operation.Operation) && operation.MaxElements is < 1 or > PlcAlarmPolicy.MaxRows)
            Check($"maxElements must be between 1 and {PlcAlarmPolicy.MaxRows}.");
        if (PlcAlarmMethods.CellUpdates.Contains(operation.Operation))
        {
            if (operation.ExpectedWorkbookHash is not null && !PlcAlarmPolicy.IsWorkbookHash(operation.ExpectedWorkbookHash))
                Check($"expectedWorkbookHash must be '{PlcAlarmPolicy.WorkbookHashPrefix}<64 lower-case hex>' from read_*_workbook.");
            ValidateCellEdits(operation.CellEdits, Check);
        }
    }

    /// <summary>AssignedProDiagFB targets: a tag (tableName + name, optional folderPath and plcName) or a block (blockPath, optional memberPath).</summary>
    private static void ValidateAssignmentTarget(PlcOperationRequest operation, string prefix, Action<string?> check)
    {
        var tag = operation.TableName is not null || operation.Name is not null || operation.FolderPath is not null;
        var block = operation.BlockPath is not null || operation.MemberPath is not null;
        if (tag == block)
        {
            check("name the target either as a tag (tableName and name, optional folderPath and plcName) or as an instance DB or global DB member (blockPath, optional memberPath).");
            return;
        }

        if (tag)
        {
            if (operation.TableName is null || operation.Name is null) check("a tag target needs tableName and name.");
            if (operation.Name is not null) check(PlcAlarmPolicy.CheckName(operation.Name, "name"));
            return;
        }

        if (operation.BlockPath is null) check("memberPath needs blockPath (the global DB).");
        if (operation.PlcName is not null) check("plcName is not valid with blockPath; the block path names the PLC.");
        if (operation.MemberPath is not null)
        {
            var errors = new List<string>();
            PlcBlockEditValidation.MemberPath(operation.MemberPath, allowIndices: true, prefix, errors);
            foreach (var error in errors) check(error.StartsWith(prefix + ": ", StringComparison.Ordinal) ? error.Substring(prefix.Length + 2) : error);
        }
    }

    private static void ValidateCellEdits(IReadOnlyList<PlcAlarms.PlcAlarmCellEditRequest>? edits, Action<string?> check)
    {
        if (edits is null || edits.Count is < 1 or > PlcAlarmPolicy.MaxCellEdits)
        {
            check($"cellEdits is required: 1 to {PlcAlarmPolicy.MaxCellEdits} edits {{ sheet, cell, expected, value, kind? }}.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            var at = $"cellEdits[{index}]";
            if (edit is null)
            {
                check($"{at} must be an object.");
                continue;
            }

            if (PlcAlarmPolicy.CheckName(edit.Sheet, $"{at}.sheet") is { } sheet) check(sheet);
            if (PlcAlarmPolicy.ParseCell(edit.Cell) is null)
                check($"{at}.cell '{edit.Cell}' must be an A1 reference with upper-case column letters (A1 to XFD1048576).");
            if (edit.Expected is null) check($"{at}.expected is required: the cell's current value as read (\"\" for an empty cell).");
            if (PlcAlarmPolicy.CheckCellValue(edit.Value) is { } value) check($"{at}.{value}");
            if (edit.Kind is not null && !PlcAlarmPolicy.CellKinds.Writable.Contains(edit.Kind, StringComparer.Ordinal))
                check($"{at}.kind must be one of {string.Join(", ", PlcAlarmPolicy.CellKinds.Writable)}.");
            if (edit.Kind == PlcAlarmPolicy.CellKinds.Number && edit.Value is not null && !PlcAlarmPolicy.IsNumber(edit.Value))
                check($"{at}.value '{edit.Value}' is not an invariant number (kind number).");
            if (edit.Sheet is not null && edit.Cell is not null && !seen.Add(edit.Sheet + "!" + edit.Cell))
                check($"{at} edits {edit.Sheet}!{edit.Cell} a second time; edit each cell once.");
        }
    }

    /// <summary>
    /// A cell update plans and imports against the workbook its expectedWorkbookHash names, so no earlier item of the
    /// call may change the project of the same PLC (or, unknown, any PLC) before it: that change could not be in the hash.
    /// </summary>
    private static void ValidateAlarmCall(IReadOnlyList<PlcOperationRequest> operations, List<string> errors)
    {
        for (var index = 0; index < operations.Count; index++)
        {
            var item = operations[index];
            if (item is null || !PlcAlarmMethods.CellUpdates.Contains(item.Operation)) continue;
            var earlier = operations.Take(index).FirstOrDefault(other => other is not null
                && !PlcAlarmMethods.FileExports.Contains(other.Operation) && other.Operation != PlcAlarmMethods.ExportProDiagInfo
                && SamePlc(Selector(other), Selector(item)));
            if (earlier is not null)
                errors.Add($"Operation '{item.Operation}' (operationId '{item.OperationId}') follows '{earlier.Operation}' (operationId '{earlier.OperationId}'), "
                    + "which may change the same PLC's workbook; its expectedWorkbookHash cannot include that change. Run the cell update in its own plc_write "
                    + "call after reading the workbook again.");
        }
    }

    private static string? Selector(PlcOperationRequest item)
    {
        if (PlcAlarmMethods.IsProjectScoped(item.Operation)) return null;
        try
        {
            return item.BlockPath is not null ? BlockAddress.Parse(item.BlockPath).PlcName
                : item.TypePath is not null ? PlcTypeAddress.Parse(item.TypePath).PlcName
                : item.PlcName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>An unknown selector (an omitted PLC, a project-wide item) conservatively matches every PLC.</summary>
    private static bool SamePlc(string? left, string? right)
        => left is null || right is null || PlcNameRules.Comparer.Equals(left, right);
}
