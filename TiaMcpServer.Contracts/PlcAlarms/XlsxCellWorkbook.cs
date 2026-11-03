using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TiaMcpServer.Contracts.PlcAlarms;

public sealed class XlsxCell
{
    public XlsxCell(int column, int row, string kind, string value)
    {
        Column = column;
        Row = row;
        Kind = kind;
        Value = value;
    }

    public int Column { get; }

    public int Row { get; }

    public string Reference => PlcAlarmPolicy.CellName(Column, Row);

    public string Kind { get; }

    public string Value { get; }
}

public sealed class XlsxSheet
{
    public XlsxSheet(string name, IReadOnlyList<XlsxCell> cells)
    {
        Name = name;
        Cells = cells;
    }

    public string Name { get; }

    /// <summary>Cells that hold a value, ordered by row, then column.</summary>
    public IReadOnlyList<XlsxCell> Cells { get; }
}

/// <summary>What a workbook read returns: optionally one sheet from a start row, at most a row count and a character budget.</summary>
public sealed class XlsxProjection
{
    /// <summary>Null projects every sheet in workbook order (each from its first row).</summary>
    public string? Sheet { get; set; }

    /// <summary>The first row number of <see cref="Sheet"/> that is returned (1-based).</summary>
    public int StartRow { get; set; } = 1;

    /// <summary>The first column of <see cref="Sheet"/>'s rows that is returned (1-based; A = 1).</summary>
    public int StartColumn { get; set; } = 1;

    public int MaxRows { get; set; } = PlcAlarmPolicy.DefaultMaxRows;

    /// <summary>The characters the projected sheets may take in the canonical JSON response.</summary>
    public int MaxChars { get; set; } = PlcAlarmPolicy.DefaultMaxResultChars;
}

/// <summary>One cell that differs between two reads (null kind and value: the cell is empty on that side).</summary>
public sealed record XlsxCellDifference(string Sheet, string Cell, string? BeforeKind, string? Before, string? AfterKind, string? After);

/// <summary>
/// The rows of an imported workbook a re-export shows (<see cref="XlsxCellWorkbook.CompareRows"/>): of the
/// <see cref="ImportedRows"/> data rows (every row below each sheet's header row), <see cref="RowsNotShown"/> compared rows are
/// absent; <see cref="NotCompared"/> names the sheets and columns the comparison could not map.
/// </summary>
public sealed record XlsxRowComparison(int ImportedRows, int RowsNotShown, IReadOnlyList<string> NotShown, IReadOnlyList<string> NotCompared);

public sealed class XlsxWorkbookGrid
{
    public XlsxWorkbookGrid(IReadOnlyList<XlsxSheet> sheets, IReadOnlyList<string> messages)
    {
        Sheets = sheets;
        Messages = messages;
    }

    /// <summary>The worksheets in workbook order (chart and dialog sheets are skipped with a message).</summary>
    public IReadOnlyList<XlsxSheet> Sheets { get; }

    public IReadOnlyList<string> Messages { get; }
}

/// <summary>
/// Reads and patches the cells of an Office Open XML workbook (ISO/IEC 29500, the XLSX format TIA Portal's
/// text list, alarm text and supervision exports write) without interpreting its column layout. Only the
/// package parts the format defines are used: the package relationships, the workbook, its relationships,
/// the shared string table and the worksheets. A patch rewrites only the worksheets it edits and the shared
/// string table when its items or counts change; every other part (document properties, styles, the custom
/// properties TIA Portal checks on import) keeps its content unchanged. The package itself is written anew
/// (recompressed), so the container bytes differ even where no part content does. Every part read or copied is
/// charged by its declared and its actually decompressed size to one decompression budget. Anything the reader
/// does not understand fails closed.
/// </summary>
public static class XlsxCellWorkbook
{
    private const string WorkerOperationFailed = "worker_operation_failed";
    private const string ValidationError = "validation_error";
    private const string StateChanged = "state_changed";
    private const string TargetNotFound = "target_not_found";

    /// <summary>Upper bound of the decompressed XML the reader accepts (zip-bomb protection).</summary>
    public const long MaxDecompressedBytes = 256L * 1024 * 1024;

    /// <summary>Upper bound of cells held in memory.</summary>
    public const int MaxCells = 2_000_000;

    private static readonly Regex EscapePattern = new("_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant);
    private static readonly Regex EscapeLike = new("_x[0-9A-Fa-f]{4}_", RegexOptions.CultureInvariant);

    /// <param name="maxDecompressedBytes">The decompression budget (tests lower it).</param>
    public static XlsxWorkbookGrid Read(byte[] workbook, long maxDecompressedBytes = MaxDecompressedBytes)
    {
        using var package = Open(workbook);
        var parts = Locate(package, maxDecompressedBytes);
        var messages = new List<string>(parts.Messages);
        var strings = parts.SharedStrings is null ? new List<string>() : ReadSharedStrings(Load(package, parts.SharedStrings, parts.Budget));
        var sheets = new List<XlsxSheet>();
        var total = 0;
        foreach (var sheet in parts.Sheets)
        {
            var document = Load(package, sheet.Part, parts.Budget);
            var cells = ReadCells(document, strings, sheet.Name);
            total += cells.Count;
            if (total > MaxCells)
                throw new PlcAlarmException(WorkerOperationFailed, $"The workbook holds more than {MaxCells} cells; it is not read.");
            sheets.Add(new XlsxSheet(sheet.Name, cells));
        }

        return new XlsxWorkbookGrid(sheets, messages);
    }

    /// <summary>
    /// SHA-256 over every cell of every worksheet in workbook order (sheet name, then reference, kind and value of
    /// each cell). Document properties, styles and timestamps are not part of it, so two exports of unchanged
    /// content hash equally.
    /// </summary>
    public static string Hash(XlsxWorkbookGrid grid)
    {
        var builder = new StringBuilder();
        foreach (var sheet in grid.Sheets)
        {
            builder.Append("S\t").Append(sheet.Name.Length.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(sheet.Name).Append('\n');
            foreach (var cell in sheet.Cells)
                builder.Append("C\t").Append(cell.Reference).Append('\t').Append(cell.Kind).Append('\t')
                    .Append(cell.Value.Length.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(cell.Value).Append('\n');
        }

        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return PlcAlarmPolicy.WorkbookHashPrefix + string.Concat(digest.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The sheets of a read: every sheet's name and counts, and rows until <see cref="XlsxProjection.MaxRows"/> per sheet
    /// or the character budget <see cref="XlsxProjection.MaxChars"/> (estimated as the canonical JSON writer escapes) is
    /// reached. With <see cref="XlsxProjection.Sheet"/> only that sheet returns rows, from <see cref="XlsxProjection.StartRow"/>
    /// and, in each row, from <see cref="XlsxProjection.StartColumn"/> (rows without a cell from there on are skipped); every
    /// sheet that has rows left reports <c>rowsTruncated</c> and the <c>nextStartRow</c> to page with. The first row of a
    /// page always makes progress within the budget: when it alone does not fit, the cells that fit whole are returned (the
    /// first one cut when even it does not fit) with <c>cellsTruncated</c> and the <c>nextStartColumn</c> to read the row on
    /// with. Values are cut to <see cref="PlcAlarmPolicy.MaxReturnedValueChars"/> (<c>valueTruncated</c>; a probe returns
    /// the full value).
    /// </summary>
    public static List<PlcAlarmSheetInfo> Project(XlsxWorkbookGrid grid, XlsxProjection projection, out bool complete)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));
        int? selected = null;
        if (projection.Sheet is not null)
            selected = FindSheet(grid.Sheets.Select(s => s.Name).ToList(), projection.Sheet)
                ?? throw new PlcAlarmException(TargetNotFound,
                    $"The workbook has no worksheet named '{projection.Sheet}' (worksheets: {string.Join(", ", grid.Sheets.Select(s => $"'{s.Name}'"))}).");
        complete = true;
        var result = new List<PlcAlarmSheetInfo>();
        var used = grid.Sheets.Sum(sheet => SheetOverheadChars + JsonChars(sheet.Name));
        var exhausted = false;
        var returned = 0;
        for (var index = 0; index < grid.Sheets.Count; index++)
        {
            var sheet = grid.Sheets[index];
            var rows = sheet.Cells.GroupBy(cell => cell.Row).ToList();
            var info = new PlcAlarmSheetInfo
            {
                Name = sheet.Name,
                RowCount = rows.Count,
                ColumnCount = sheet.Cells.Count == 0 ? 0 : sheet.Cells.Max(cell => cell.Column),
                CellCount = sheet.Cells.Count,
            };
            result.Add(info);
            var start = selected == index ? projection.StartRow : 1;
            var startColumn = selected == index ? projection.StartColumn : 1;
            var candidates = selected is null || selected == index ? rows.Where(row => row.Key >= start).ToList() : rows;
            if (selected == index && projection.StartColumn > 1) candidates = candidates.Where(row => row.Any(cell => cell.Column >= startColumn)).ToList();
            if (selected is not null && selected != index || exhausted)
            {
                Stop(info, candidates.FirstOrDefault()?.Key);
                complete &= !info.RowsTruncated;
                continue;
            }

            for (var position = 0; position < candidates.Count; position++)
            {
                var row = candidates[position];
                if (info.Rows.Count >= projection.MaxRows)
                {
                    Stop(info, row.Key);
                    break;
                }

                var cells = row.Where(cell => cell.Column >= startColumn).ToList();
                if (cells.Count == 0) continue;
                var projected = ProjectRow(row.Key, cells, PlcAlarmPolicy.MaxReturnedValueChars);
                var size = RowChars(projected);
                if (used + size > projection.MaxChars)
                {
                    if (returned > 0)
                    {
                        exhausted = true;
                        Stop(info, row.Key);
                        break;
                    }

                    // The first row of the page always makes progress: the cells that fit (the first one cut to the
                    // budget when even it does not fit); the rest of the row is read on with startColumn.
                    projected = FitRow(row.Key, cells, projection.MaxChars - used);
                    size = RowChars(projected);
                    info.Rows.Add(projected);
                    used += size;
                    returned++;
                    complete &= !projected.CellsTruncated && projected.Cells.All(cell => !cell.ValueTruncated);
                    exhausted = true;
                    Stop(info, position + 1 < candidates.Count ? candidates[position + 1].Key : null);
                    break;
                }

                info.Rows.Add(projected);
                used += size;
                returned++;
                complete &= projected.Cells.All(cell => !cell.ValueTruncated);
            }

            complete &= !info.RowsTruncated;
        }

        return result;
    }

    private static void Stop(PlcAlarmSheetInfo info, int? nextRow)
    {
        info.RowsTruncated = nextRow is not null;
        info.NextStartRow = nextRow;
    }

    /// <summary>The cells of one row that fit <paramref name="budget"/>, at least one (its value cut to fit), with the next column to page from.</summary>
    private static PlcAlarmRowInfo FitRow(int number, IReadOnlyList<XlsxCell> cells, int budget)
    {
        var row = new PlcAlarmRowInfo { Row = number };
        var available = budget - RowOverheadChars;
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = ProjectRow(number, new[] { cells[index] }, PlcAlarmPolicy.MaxReturnedValueChars).Cells[0];
            var chars = CellOverheadChars + JsonChars(cell.Value);
            if (chars > available)
            {
                if (row.Cells.Count > 0)
                {
                    row.CellsTruncated = true;
                    row.NextStartColumn = PlcAlarmPolicy.ColumnName(cells[index].Column);
                    return row;
                }

                // At most six characters per value character, plus the two quotes.
                cell = ProjectRow(number, new[] { cells[index] }, Math.Max(0, (available - CellOverheadChars - 2) / 6)).Cells[0];
                cell.ValueTruncated = true;
                row.Cells.Add(cell);
                if (index + 1 < cells.Count)
                {
                    row.CellsTruncated = true;
                    row.NextStartColumn = PlcAlarmPolicy.ColumnName(cells[index + 1].Column);
                }

                return row;
            }

            row.Cells.Add(cell);
            available -= chars;
        }

        return row;
    }

    private static PlcAlarmRowInfo ProjectRow(int number, IReadOnlyList<XlsxCell> cells, int maxValueChars)
    {
        var row = new PlcAlarmRowInfo { Row = number };
        foreach (var cell in cells)
        {
            var truncated = cell.Value.Length > maxValueChars;
            var value = truncated ? cell.Value.Substring(0, maxValueChars) : cell.Value;
            // Never cut between the two halves of a surrogate pair.
            if (truncated && value.Length > 0 && char.IsHighSurrogate(value[value.Length - 1])) value = value.Substring(0, value.Length - 1);
            row.Cells.Add(new PlcAlarmCellInfo { Cell = cell.Reference, Kind = cell.Kind, Value = value, ValueTruncated = truncated });
        }

        return row;
    }

    // Upper bounds of the JSON a sheet, a row and a cell take besides their names and values, at the largest numbers and
    // references: a sheet's counts, flags and nextStartRow; a row's number, cellsTruncated and nextStartColumn ("XFD"),
    // about 75; a cell's reference ("XFD1048576"), kind ("formula") and valueTruncated, about 71; each with its comma.
    private const int SheetOverheadChars = 160;
    private const int RowOverheadChars = 80;
    private const int CellOverheadChars = 80;

    private static int RowChars(PlcAlarmRowInfo row) => RowOverheadChars + row.Cells.Sum(cell => CellOverheadChars + JsonChars(cell.Value));

    /// <summary>
    /// An upper bound of the characters <paramref name="text"/> takes as a JSON string value of the host's canonical writer
    /// (its default encoder escapes every character outside printable ASCII, and HTML-sensitive ones, as <c>\uXXXX</c>).
    /// </summary>
    public static int JsonChars(string? text)
    {
        if (text is null) return 4;
        var chars = 2;
        foreach (var character in text)
            chars += character is >= ' ' and <= '~' && character is not ('"' or '\\' or '<' or '>' or '&' or '\'' or '+' or '`') ? 1 : 6;
        return chars;
    }

    /// <summary>The workbook's name of a sheet (exact, else the one case-insensitive match); null when there is none.</summary>
    public static string? ResolveSheet(XlsxWorkbookGrid grid, string name)
        => FindSheet(grid.Sheets.Select(s => s.Name).ToList(), name) is { } index ? grid.Sheets[index].Name : null;

    /// <summary>The full value of one cell; a sheet is matched case-insensitively like Excel does.</summary>
    public static PlcAlarmCellProbeInfo Probe(XlsxWorkbookGrid grid, string sheetName, string cellReference)
    {
        var probe = new PlcAlarmCellProbeInfo { Sheet = sheetName, Cell = cellReference };
        var sheet = FindSheet(grid.Sheets.Select(s => s.Name).ToList(), sheetName) is { } index ? grid.Sheets[index] : null;
        if (sheet is null) return probe;
        probe.SheetExists = true;
        var position = PlcAlarmPolicy.ParseCell(cellReference);
        var cell = position is null ? null : sheet.Cells.FirstOrDefault(c => c.Column == position.Value.Column && c.Row == position.Value.Row);
        if (cell is null) return probe;
        probe.Exists = true;
        probe.Kind = cell.Kind;
        probe.Value = cell.Value;
        return probe;
    }

    /// <summary>
    /// Applies <paramref name="edits"/> in order and returns the patched package. Each edited cell must hold its
    /// <c>Expected</c> value (an empty cell: ""), must not be a formula, error, boolean or date cell, and is written
    /// as a shared string (an inline string when the workbook has no shared string table) or a number. Missing rows
    /// and cells are created in order. The same cell may not be edited twice.
    /// </summary>
    public static (byte[] Workbook, List<PlcAlarmCellChangeInfo> Changes) Patch(byte[] workbook, IReadOnlyList<PlcAlarmCellEditInfo> edits,
        long maxDecompressedBytes = MaxDecompressedBytes)
    {
        using var package = Open(workbook);
        var parts = Locate(package, maxDecompressedBytes);
        XDocument? sst = parts.SharedStrings is null ? null : Load(package, parts.SharedStrings, parts.Budget);
        var strings = sst is null ? new List<string>() : ReadSharedStrings(sst);
        var sstBefore = sst is null ? null : SharedStringState(sst);
        var sheetDocuments = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<PlcAlarmCellChangeInfo>();
        var names = parts.Sheets.Select(s => s.Name).ToList();
        foreach (var edit in edits)
        {
            var where = $"cell {edit.Sheet}!{edit.Cell}";
            var index = FindSheet(names, edit.Sheet)
                ?? throw new PlcAlarmException(ValidationError, $"The workbook has no worksheet named '{edit.Sheet}' (worksheets: {string.Join(", ", names.Select(n => $"'{n}'"))}).");
            var sheet = parts.Sheets[index];
            var position = PlcAlarmPolicy.ParseCell(edit.Cell) ?? throw new PlcAlarmException(ValidationError, $"'{edit.Cell}' is not an A1 cell reference.");
            if (!seen.Add(sheet.Name + "!" + edit.Cell))
                throw new PlcAlarmException(ValidationError, $"{where} is edited twice; edit each cell once.");
            var main = XNamespace.None;
            if (!sheetDocuments.TryGetValue(sheet.Part, out var document))
            {
                sheetDocuments[sheet.Part] = document = Load(package, sheet.Part, parts.Budget);
                main = document.Root!.Name.Namespace;
                // Implicit row and cell positions are written out exactly as Read derives them, so the patch addresses the cells Read reported.
                Materialize(document.Root.Element(main + "sheetData") ?? throw Malformed(sheet.Name, "has no sheetData"), main, sheet.Name);
            }

            main = document.Root!.Name.Namespace;
            var data = document.Root.Element(main + "sheetData") ?? throw Malformed(sheet.Name, "has no sheetData");
            if (PlcAlarmPolicy.CheckCellValue(edit.Value) is { } invalid)
                throw new PlcAlarmException(ValidationError, $"{where}: {invalid}");
            var row = FindOrCreateRow(data, main, position.Row, sheet.Name);
            var cell = FindOrCreateCell(row, main, position.Column, position.Row, sheet.Name, out var created);
            var current = created ? null : CellValue(cell, strings, sheet.Name);
            string? currentKind = current?.Kind;
            var currentValue = current?.Value ?? string.Empty;
            if (cell.Element(main + "f") is not null || currentKind is PlcAlarmPolicy.CellKinds.Error or PlcAlarmPolicy.CellKinds.Boolean or "date")
                throw new PlcAlarmException(ValidationError, $"{where} is a {(cell.Element(main + "f") is not null ? "formula" : currentKind)} cell; only text and number cells are edited.");
            if (!string.Equals(currentValue, edit.Expected ?? string.Empty, StringComparison.Ordinal))
                throw new PlcAlarmException(StateChanged,
                    $"{where} holds {Describe(currentValue)}, not the expected {Describe(edit.Expected ?? string.Empty)}. Read the workbook again.");
            var kind = edit.Kind ?? (currentKind == PlcAlarmPolicy.CellKinds.Number ? PlcAlarmPolicy.CellKinds.Number : PlcAlarmPolicy.CellKinds.String);
            var value = edit.Value ?? string.Empty;
            if (kind == PlcAlarmPolicy.CellKinds.Number && !PlcAlarmPolicy.IsNumber(value))
                throw new PlcAlarmException(ValidationError, $"{where}: '{value}' is not an invariant number (kind number).");
            cell.Elements().Where(e => e.Name == main + "f" || e.Name == main + "v" || e.Name == main + "is").Remove();
            cell.Attribute("t")?.Remove();
            if (kind == PlcAlarmPolicy.CellKinds.Number)
            {
                cell.Add(new XElement(main + "v", value));
            }
            else if (sst is not null)
            {
                cell.SetAttributeValue("t", "s");
                cell.Add(new XElement(main + "v", SharedStringIndex(sst, strings, value).ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                cell.SetAttributeValue("t", "inlineStr");
                cell.Add(new XElement(main + "is", Text(main, value)));
            }

            changed.Add(sheet.Part);
            changes.Add(new PlcAlarmCellChangeInfo { Sheet = sheet.Name, Cell = edit.Cell, Kind = kind, Before = currentValue, After = value });
        }

        foreach (var part in changed) UpdateDimension(sheetDocuments[part]);
        if (sst is not null) UpdateCounts(package, parts, sst, sheetDocuments);
        var replaced = changed.ToDictionary(part => part, part => Serialize(sheetDocuments[part]), StringComparer.OrdinalIgnoreCase);
        // The table is rewritten whenever an item was added or a count changed (a number written over a text cell lowers count).
        if (sst is not null && SharedStringState(sst) != sstBefore) replaced[parts.SharedStrings!] = Serialize(sst);
        return (Rewrite(package, replaced, parts.Budget), changes);
    }

    private static string SharedStringState(XDocument sst)
        => $"{sst.Root!.Elements(sst.Root.Name.Namespace + "si").Count()}|{sst.Root.Attribute("count")?.Value}|{sst.Root.Attribute("uniqueCount")?.Value}";

    /// <summary>
    /// Every cell of <paramref name="before"/> and <paramref name="after"/> whose kind or value differs, or that only one
    /// of them holds (sheets matched by exact name), in sheet, row and column order.
    /// </summary>
    public static List<XlsxCellDifference> Diff(XlsxWorkbookGrid before, XlsxWorkbookGrid after)
    {
        var differences = new List<XlsxCellDifference>();
        var names = before.Sheets.Select(s => s.Name).Concat(after.Sheets.Select(s => s.Name)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var name in names)
        {
            var left = before.Sheets.FirstOrDefault(s => s.Name == name)?.Cells.ToDictionary(c => (c.Row, c.Column)) ?? new Dictionary<(int, int), XlsxCell>();
            var right = after.Sheets.FirstOrDefault(s => s.Name == name)?.Cells.ToDictionary(c => (c.Row, c.Column)) ?? new Dictionary<(int, int), XlsxCell>();
            foreach (var key in left.Keys.Concat(right.Keys).Distinct().OrderBy(k => k.Item1).ThenBy(k => k.Item2))
            {
                left.TryGetValue(key, out var was);
                right.TryGetValue(key, out var now);
                if (was is not null && now is not null && was.Kind == now.Kind && was.Value == now.Value) continue;
                differences.Add(new XlsxCellDifference(name, PlcAlarmPolicy.CellName(key.Item2, key.Item1), was?.Kind, was?.Value, now?.Kind, now?.Value));
            }
        }

        return differences;
    }

    /// <summary>
    /// How many rows of <paramref name="imported"/> the re-export <paramref name="reexport"/> shows. Sheets are matched by name
    /// (exact, else the one case-insensitive match). Columns are matched by the text of the sheet's first row (the header;
    /// the n-th equal header text maps to the n-th one), not by position, so a re-export with columns in another order or
    /// with extra columns (for example more languages) still compares. Imported columns whose header the re-export lacks,
    /// sheets it lacks and imported values in columns without a header are listed in <see cref="XlsxRowComparison.NotCompared"/>.
    /// A data row is shown when the re-export sheet has a row with the same values in the mapped columns (numbers by value,
    /// empty and absent cells alike), wherever that row is; each re-exported row matches one imported row.
    /// </summary>
    public static XlsxRowComparison CompareRows(XlsxWorkbookGrid imported, XlsxWorkbookGrid reexport, int maxListed = 20)
    {
        var importedRows = 0;
        var notShown = 0;
        var listed = new List<string>();
        var notCompared = new List<string>();
        foreach (var sheet in imported.Sheets)
        {
            var rows = sheet.Cells.GroupBy(c => c.Row).OrderBy(r => r.Key).ToList();
            if (rows.Count == 0) continue;
            importedRows += rows.Count - 1;
            var match = FindSheet(reexport.Sheets.Select(s => s.Name).ToList(), sheet.Name) is { } index ? reexport.Sheets[index] : null;
            if (match is null)
            {
                notCompared.Add($"sheet '{sheet.Name}' ({rows.Count - 1} data rows) is not in the re-export");
                continue;
            }

            var exportRows = match.Cells.GroupBy(c => c.Row).OrderBy(r => r.Key).ToList();
            var mapping = MapColumns(rows[0].ToList(), exportRows.FirstOrDefault()?.ToList() ?? new List<XlsxCell>(), sheet.Name, notCompared);
            var headed = new HashSet<int>(rows[0].Select(c => c.Column));
            var loose = rows.Skip(1).SelectMany(row => row).Where(c => !headed.Contains(c.Column) && Normalize(c.Value).Length > 0)
                .Select(c => c.Column).Distinct().OrderBy(c => c).ToList();
            if (loose.Count > 0)
                notCompared.Add($"values in column(s) {string.Join(", ", loose.Take(10).Select(PlcAlarmPolicy.ColumnName))}{(loose.Count > 10 ? ", ..." : string.Empty)} "
                    + $"of sheet '{sheet.Name}' have no header and are not compared");
            var available = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in exportRows.Skip(1))
            {
                var signature = Signature(row, mapping, exported: true);
                available[signature] = available.TryGetValue(signature, out var count) ? count + 1 : 1;
            }

            foreach (var row in rows.Skip(1))
            {
                // A row with values only in unmapped columns is not compared; those columns are already listed.
                var signature = Signature(row, mapping, exported: false);
                if (signature.Length == 0) continue;
                if (available.TryGetValue(signature, out var count) && count > 0)
                {
                    available[signature] = count - 1;
                    continue;
                }

                notShown++;
                if (listed.Count < maxListed) listed.Add($"{sheet.Name}!row {row.Key}");
            }
        }

        return new XlsxRowComparison(importedRows, notShown, listed, notCompared);
    }

    /// <summary>Imported column → re-exported column, by header text; columns without a matching header are not compared.</summary>
    private static List<(int Imported, int Exported)> MapColumns(List<XlsxCell> importedHeader, List<XlsxCell> exportHeader, string sheet, List<string> notCompared)
    {
        var mapping = new List<(int, int)>();
        var used = new HashSet<int>();
        foreach (var header in importedHeader.OrderBy(c => c.Column))
        {
            var text = Normalize(header.Value);
            var occurrence = importedHeader.Count(c => c.Column < header.Column && Normalize(c.Value) == text);
            var candidates = exportHeader.Where(c => Normalize(c.Value) == text).OrderBy(c => c.Column).ToList();
            if (candidates.Count <= occurrence)
                candidates = exportHeader.Where(c => string.Equals(Normalize(c.Value), text, StringComparison.OrdinalIgnoreCase)).OrderBy(c => c.Column).ToList();
            if (candidates.Count > occurrence && used.Add(candidates[occurrence].Column))
                mapping.Add((header.Column, candidates[occurrence].Column));
            else
                notCompared.Add($"column '{PlcAlarmPolicy.Bound(header.Value, 80)}' ({PlcAlarmPolicy.ColumnName(header.Column)}) of sheet '{sheet}' has no header in the re-export");
        }

        return mapping;
    }

    private static string Signature(IEnumerable<XlsxCell> row, List<(int Imported, int Exported)> mapping, bool exported)
    {
        var cells = row.ToDictionary(c => c.Column);
        var parts = new List<string>();
        for (var index = 0; index < mapping.Count; index++)
        {
            var column = exported ? mapping[index].Exported : mapping[index].Imported;
            if (cells.TryGetValue(column, out var cell) && Normalize(cell.Value) is { Length: > 0 } value)
                parts.Add(index.ToString(CultureInfo.InvariantCulture) + "\u0002" + value);
        }

        return string.Join("\u0001", parts);
    }

    /// <summary>A value as compared: numbers by value, everything else exactly.</summary>
    private static string Normalize(string value)
        => PlcAlarmPolicy.IsNumber(value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("R", CultureInfo.InvariantCulture) : value;

    // ------------------------------------------------------------------ package

    private sealed class Budget
    {
        public Budget(long limit)
        {
            Limit = Remaining = limit;
        }

        public long Limit { get; }

        public long Remaining { get; set; }

        /// <summary>Parts already charged (read once by Load), so copying them again is not charged twice.</summary>
        public HashSet<string> Charged { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Charge(string part, long bytes)
        {
            if (bytes > Remaining)
                throw new PlcAlarmException(WorkerOperationFailed, $"The workbook decompresses to more than {Limit} bytes; it is not read.");
            Remaining -= bytes;
            Charged.Add(part);
        }
    }

    private sealed record SheetPart(string Name, string Part);

    private sealed record Parts(string Workbook, string? SharedStrings, List<SheetPart> Sheets, List<string> Messages, Budget Budget);

    private sealed record Relationship(string Id, string Type, string Target);

    private static ZipArchive Open(byte[] workbook)
    {
        try
        {
            return new ZipArchive(new MemoryStream(workbook, writable: false), ZipArchiveMode.Read, leaveOpen: false);
        }
        catch (InvalidDataException exception)
        {
            throw new PlcAlarmException(WorkerOperationFailed, $"The file is not an XLSX (zip) package: {exception.Message}");
        }
    }

    private static Parts Locate(ZipArchive package, long maxDecompressedBytes)
    {
        var budget = new Budget(maxDecompressedBytes);
        var messages = new List<string>();
        var root = Relationships(package, "_rels/.rels", string.Empty, budget);
        var workbook = root.FirstOrDefault(r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal))?.Target
            ?? throw new PlcAlarmException(WorkerOperationFailed, "The package names no office document (_rels/.rels); it is not a workbook.");
        var document = Load(package, workbook, budget);
        var main = document.Root!.Name.Namespace;
        if (document.Root.Name.LocalName != "workbook")
            throw new PlcAlarmException(WorkerOperationFailed, $"The office document '{workbook}' is not a spreadsheet workbook.");
        var directory = Directory(workbook);
        var related = Relationships(package, directory + "_rels/" + FileName(workbook) + ".rels", directory, budget);
        var sharedStrings = related.Where(r => r.Type.EndsWith("/sharedStrings", StringComparison.Ordinal)).Select(r => r.Target).FirstOrDefault();
        var sheets = new List<SheetPart>();
        foreach (var sheet in document.Root.Element(main + "sheets")?.Elements(main + "sheet") ?? Enumerable.Empty<XElement>())
        {
            var name = sheet.Attribute("name")?.Value ?? throw new PlcAlarmException(WorkerOperationFailed, "A workbook sheet has no name.");
            var id = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id" && a.Name.NamespaceName.EndsWith("relationships", StringComparison.Ordinal))?.Value;
            var relationship = related.FirstOrDefault(r => r.Id == id)
                ?? throw new PlcAlarmException(WorkerOperationFailed, $"Sheet '{name}' names no part of the package.");
            if (!relationship.Type.EndsWith("/worksheet", StringComparison.Ordinal))
            {
                messages.Add($"Sheet '{name}' is not a worksheet ({relationship.Type.Substring(relationship.Type.LastIndexOf('/') + 1)}) and is skipped.");
                continue;
            }

            sheets.Add(new SheetPart(name, relationship.Target));
        }

        return new Parts(workbook, sharedStrings, sheets, messages, budget);
    }

    private static List<Relationship> Relationships(ZipArchive package, string part, string baseDirectory, Budget budget)
    {
        if (Entry(package, part) is null) return new List<Relationship>();
        var document = Load(package, part, budget);
        return document.Root!.Elements().Where(e => e.Name.LocalName == "Relationship")
            .Where(e => !string.Equals(e.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            .Select(e => new Relationship(e.Attribute("Id")?.Value ?? string.Empty, e.Attribute("Type")?.Value ?? string.Empty,
                Resolve(baseDirectory, e.Attribute("Target")?.Value ?? string.Empty)))
            .ToList();
    }

    private static string Resolve(string baseDirectory, string target)
    {
        var path = target.Replace('\\', '/');
        var combined = path.StartsWith("/", StringComparison.Ordinal) ? path.Substring(1) : baseDirectory + path;
        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new PlcAlarmException(WorkerOperationFailed, $"The part reference '{target}' leaves the package.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    private static string Directory(string part) => part.Contains('/') ? part.Substring(0, part.LastIndexOf('/') + 1) : string.Empty;

    private static string FileName(string part) => part.Substring(part.LastIndexOf('/') + 1);

    private static ZipArchiveEntry? Entry(ZipArchive package, string part)
        => package.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), part, StringComparison.OrdinalIgnoreCase));

    private static XDocument Load(ZipArchive package, string part, Budget budget)
    {
        var entry = Entry(package, part) ?? throw new PlcAlarmException(WorkerOperationFailed, $"The package part '{part}' is missing.");
        if (!budget.Charged.Contains(part)) budget.Charge(part, entry.Length);
        try
        {
            using var limited = new MemoryStream();
            CopyLimited(entry, limited, part);
            limited.Position = 0;
            using var reader = XmlReader.Create(limited, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new PlcAlarmException(WorkerOperationFailed, $"The package part '{part}' is not well-formed XML: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            throw new PlcAlarmException(WorkerOperationFailed, $"The package part '{part}' cannot be decompressed: {exception.Message}");
        }
    }

    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
            OmitXmlDeclaration = document.Declaration is null,
        };
        using (var writer = XmlWriter.Create(stream, settings)) document.Save(writer);
        return stream.ToArray();
    }

    /// <summary>
    /// A new package with every entry in its original order; replaced parts get their new bytes, all others their original
    /// content. Every copied part not read before is charged to <paramref name="budget"/> by its declared size first, and
    /// every copy stops when it yields more than that.
    /// </summary>
    private static byte[] Rewrite(ZipArchive source, IReadOnlyDictionary<string, byte[]> replaced, Budget budget)
    {
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var name = entry.FullName.Replace('\\', '/').TrimStart('/');
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;
                using var destination = copy.Open();
                if (replaced.TryGetValue(name, out var bytes))
                {
                    destination.Write(bytes, 0, bytes.Length);
                }
                else
                {
                    if (!budget.Charged.Contains(name)) budget.Charge(name, entry.Length);
                    CopyLimited(entry, destination, name);
                }
            }
        }

        return output.ToArray();
    }

    /// <summary>Copies a part, failing when it decompresses to more than its declared size.</summary>
    private static void CopyLimited(ZipArchiveEntry entry, Stream destination, string part)
    {
        try
        {
            using var stream = entry.Open();
            var buffer = new byte[81920];
            int read;
            long total = 0;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > entry.Length) throw new PlcAlarmException(WorkerOperationFailed, $"The package part '{part}' is larger than its declared size.");
                destination.Write(buffer, 0, read);
            }
        }
        catch (InvalidDataException exception)
        {
            throw new PlcAlarmException(WorkerOperationFailed, $"The package part '{part}' cannot be decompressed: {exception.Message}");
        }
    }

    // ------------------------------------------------------------------ cells

    private static List<string> ReadSharedStrings(XDocument document)
    {
        var main = document.Root!.Name.Namespace;
        return document.Root.Elements(main + "si").Select(si => StringItem(si, main)).ToList();
    }

    /// <summary>The text of a string item: its <c>t</c>, or the <c>t</c> of every rich-text run (phonetic runs excluded).</summary>
    private static string StringItem(XElement item, XNamespace main)
    {
        if (item.Element(main + "t") is { } plain) return Decode(plain.Value);
        return string.Concat(item.Elements(main + "r").Select(run => Decode(run.Element(main + "t")?.Value ?? string.Empty)));
    }

    private static List<XlsxCell> ReadCells(XDocument document, IReadOnlyList<string> strings, string sheet)
    {
        var main = document.Root!.Name.Namespace;
        var data = document.Root.Element(main + "sheetData");
        var cells = new List<XlsxCell>();
        if (data is null) return cells;
        var rowNumber = 0;
        foreach (var row in data.Elements(main + "row"))
        {
            rowNumber = row.Attribute("r") is { } r ? ParseNumber(r.Value, sheet, "row") : rowNumber + 1;
            var column = 0;
            foreach (var cell in row.Elements(main + "c"))
            {
                if (cell.Attribute("r") is { } reference)
                {
                    var position = PlcAlarmPolicy.ParseCell(reference.Value) ?? throw Malformed(sheet, $"has the cell reference '{reference.Value}'");
                    if (position.Row != rowNumber) throw Malformed(sheet, $"lists cell {reference.Value} in row {rowNumber}");
                    column = position.Column;
                }
                else
                {
                    column++;
                }

                if (CellValue(cell, strings, sheet) is { } value)
                    cells.Add(new XlsxCell(column, rowNumber, value.Kind, value.Value));
            }
        }

        var ordered = cells.OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].Row == ordered[index - 1].Row && ordered[index].Column == ordered[index - 1].Column)
                throw Malformed(sheet, $"lists cell {ordered[index].Reference} twice");
        }

        return ordered;
    }

    /// <summary>The kind and value of a cell, or null when it holds no value (an empty, possibly styled cell).</summary>
    private static (string Kind, string Value)? CellValue(XElement cell, IReadOnlyList<string> strings, string sheet)
    {
        var main = cell.Name.Namespace;
        var type = cell.Attribute("t")?.Value ?? "n";
        var formula = cell.Element(main + "f") is not null;
        var v = cell.Element(main + "v")?.Value;
        if (type == "inlineStr")
        {
            var inline = cell.Element(main + "is");
            return inline is null ? null : (formula ? PlcAlarmPolicy.CellKinds.Formula : PlcAlarmPolicy.CellKinds.String, StringItem(inline, main));
        }

        if (v is null) return formula ? (PlcAlarmPolicy.CellKinds.Formula, string.Empty) : null;
        var kind = formula ? PlcAlarmPolicy.CellKinds.Formula : type switch
        {
            "s" or "str" => PlcAlarmPolicy.CellKinds.String,
            "b" => PlcAlarmPolicy.CellKinds.Boolean,
            "e" => PlcAlarmPolicy.CellKinds.Error,
            "d" => "date",
            "n" => PlcAlarmPolicy.CellKinds.Number,
            _ => throw Malformed(sheet, $"has a cell of unknown type '{type}'"),
        };
        return type switch
        {
            "s" => (kind, SharedString(strings, v, sheet)),
            "b" => (kind, v.Trim() == "1" ? "true" : "false"),
            "str" => (kind, Decode(v)),
            _ => (kind, v),
        };
    }

    private static string SharedString(IReadOnlyList<string> strings, string index, string sheet)
    {
        var number = ParseNumber(index, sheet, "shared string index");
        return number < strings.Count ? strings[number] : throw Malformed(sheet, $"refers to shared string {number}, which does not exist");
    }

    private static int ParseNumber(string text, string sheet, string what)
        => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 0
            ? number : throw Malformed(sheet, $"has the {what} '{text}'");

    private static PlcAlarmException Malformed(string sheet, string problem)
        => new(WorkerOperationFailed, $"Worksheet '{sheet}' {problem}; the workbook is not read.");

    private static int? FindSheet(IReadOnlyList<string> names, string name)
    {
        var exact = names.Select((n, i) => (n, i)).Where(p => string.Equals(p.n, name, StringComparison.Ordinal)).Select(p => (int?)p.i).FirstOrDefault();
        if (exact is not null) return exact;
        var folded = names.Select((n, i) => (n, i)).Where(p => string.Equals(p.n, name, StringComparison.OrdinalIgnoreCase)).ToList();
        return folded.Count == 1 ? folded[0].i : null;
    }

    /// <summary>
    /// Writes out implicit row and cell positions (a row without <c>r</c> follows the previous row, a cell without <c>r</c>
    /// the previous cell, as <see cref="Read"/> derives them) and refuses rows or cells that are not in strictly ascending
    /// order (ISO/IEC 29500 requires it; a patch could not tell where a new cell belongs).
    /// </summary>
    private static void Materialize(XElement data, XNamespace main, string sheet)
    {
        var previousRow = 0;
        foreach (var row in data.Elements(main + "row"))
        {
            int number;
            if (row.Attribute("r") is { } r)
            {
                number = ParseNumber(r.Value, sheet, "row");
                if (number < 1 || number > PlcAlarmPolicy.MaxRow) throw Malformed(sheet, $"has the row number '{r.Value}'");
                if (number <= previousRow)
                    throw new PlcAlarmException(ValidationError, $"Worksheet '{sheet}' lists row {number} after row {previousRow}; rows that are not in ascending order are not patched.");
            }
            else
            {
                number = previousRow + 1;
                if (number > PlcAlarmPolicy.MaxRow) throw Malformed(sheet, "has more rows than Excel allows");
                row.SetAttributeValue("r", number.ToString(CultureInfo.InvariantCulture));
            }

            previousRow = number;
            var previousColumn = 0;
            foreach (var cell in row.Elements(main + "c"))
            {
                int column;
                if (cell.Attribute("r") is { } reference)
                {
                    var position = PlcAlarmPolicy.ParseCell(reference.Value) ?? throw Malformed(sheet, $"has the cell reference '{reference.Value}'");
                    if (position.Row != number) throw Malformed(sheet, $"lists cell {reference.Value} in row {number}");
                    column = position.Column;
                    if (column <= previousColumn)
                        throw new PlcAlarmException(ValidationError,
                            $"Worksheet '{sheet}' lists cell {reference.Value} after column {PlcAlarmPolicy.ColumnName(previousColumn)} in row {number}; cells that are not in ascending order are not patched.");
                }
                else
                {
                    column = previousColumn + 1;
                    if (column > PlcAlarmPolicy.MaxColumn) throw Malformed(sheet, $"has more columns than Excel allows in row {number}");
                    cell.SetAttributeValue("r", PlcAlarmPolicy.CellName(column, number));
                }

                previousColumn = column;
            }
        }
    }

    /// <summary>The row numbered <paramref name="number"/>, searched among all rows first, else a new row inserted in order.</summary>
    private static XElement FindOrCreateRow(XElement data, XNamespace main, int number, string sheet)
    {
        var rows = data.Elements(main + "row").Select(row => (Row: row, Number: ParseNumber(row.Attribute("r")!.Value, sheet, "row"))).ToList();
        if (rows.FirstOrDefault(row => row.Number == number).Row is { } existing) return existing;
        var created = new XElement(main + "row", new XAttribute("r", number.ToString(CultureInfo.InvariantCulture)));
        if (rows.FirstOrDefault(row => row.Number > number).Row is { } before) before.AddBeforeSelf(created);
        else data.Add(created);
        return created;
    }

    /// <summary>The cell in <paramref name="column"/>, searched among all cells of the row first, else a new cell inserted in order.</summary>
    private static XElement FindOrCreateCell(XElement row, XNamespace main, int column, int rowNumber, string sheet, out bool created)
    {
        var cells = row.Elements(main + "c").Select(cell => (Cell: cell, Column: (PlcAlarmPolicy.ParseCell(cell.Attribute("r")!.Value)
            ?? throw Malformed(sheet, $"has the cell reference '{cell.Attribute("r")!.Value}'")).Column)).ToList();
        if (cells.FirstOrDefault(cell => cell.Column == column).Cell is { } existing)
        {
            created = false;
            return existing;
        }

        // A new cell has no style; the row's spans attribute is an optional hint and is dropped when it no longer covers the cells.
        var element = new XElement(main + "c", new XAttribute("r", PlcAlarmPolicy.CellName(column, rowNumber)));
        if (cells.FirstOrDefault(cell => cell.Column > column).Cell is { } before) before.AddBeforeSelf(element);
        else row.Add(element);
        row.Attribute("spans")?.Remove();
        created = true;
        return element;
    }

    private static int SharedStringIndex(XDocument sst, List<string> strings, string value)
    {
        var main = sst.Root!.Name.Namespace;
        var items = sst.Root.Elements(main + "si").ToList();
        for (var index = 0; index < items.Count; index++)
        {
            // Reuse only a plain item with exactly this text, never a rich-text one.
            if (items[index].Element(main + "t") is { } plain && items[index].Elements().Count() == 1 && strings[index] == value) return index;
        }

        sst.Root.Add(new XElement(main + "si", Text(main, value)));
        strings.Add(value);
        return strings.Count - 1;
    }

    private static XElement Text(XNamespace main, string value)
    {
        var element = new XElement(main + "t", Encode(value));
        if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[value.Length - 1]) || value.IndexOfAny(new[] { '\n', '\r', '\t' }) >= 0))
            element.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        return element;
    }

    /// <summary>The ST_Xstring escape of ISO/IEC 29500: <c>_xHHHH_</c> stands for the character U+HHHH.</summary>
    private static string Decode(string text)
        => text.IndexOf("_x", StringComparison.Ordinal) < 0 ? text
            : EscapePattern.Replace(text, match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    /// <summary>A literal <c>_xHHHH_</c> in a value is protected by escaping its underscore (<c>_x005F_</c>).</summary>
    private static string Encode(string text)
        => text.IndexOf("_x", StringComparison.Ordinal) < 0 ? text : EscapeLike.Replace(text, match => "_x005F_" + match.Value.Substring(1));

    private static void UpdateDimension(XDocument document)
    {
        var main = document.Root!.Name.Namespace;
        var dimension = document.Root.Element(main + "dimension");
        if (dimension is null) return;
        var positions = document.Root.Element(main + "sheetData")?.Elements(main + "row").SelectMany(r => r.Elements(main + "c"))
            .Select(c => PlcAlarmPolicy.ParseCell(c.Attribute("r")?.Value)).Where(p => p is not null).Select(p => p!.Value).ToList()
            ?? new List<(int Column, int Row)>();
        if (positions.Count == 0) return;
        var first = PlcAlarmPolicy.CellName(positions.Min(p => p.Column), positions.Min(p => p.Row));
        var last = PlcAlarmPolicy.CellName(positions.Max(p => p.Column), positions.Max(p => p.Row));
        dimension.SetAttributeValue("ref", first == last ? first : first + ":" + last);
    }

    /// <summary>Recounts the shared string references over every worksheet (count) and the items (uniqueCount), where present.</summary>
    private static void UpdateCounts(ZipArchive package, Parts parts, XDocument sst, Dictionary<string, XDocument> loaded)
    {
        var main = sst.Root!.Name.Namespace;
        if (sst.Root.Attribute("uniqueCount") is not null)
            sst.Root.SetAttributeValue("uniqueCount", sst.Root.Elements(main + "si").Count().ToString(CultureInfo.InvariantCulture));
        if (sst.Root.Attribute("count") is null) return;
        var references = 0;
        foreach (var sheet in parts.Sheets)
        {
            var document = loaded.TryGetValue(sheet.Part, out var known) ? known : Load(package, sheet.Part, parts.Budget);
            var sheetMain = document.Root!.Name.Namespace;
            references += document.Root.Element(sheetMain + "sheetData")?.Elements(sheetMain + "row")
                .SelectMany(r => r.Elements(sheetMain + "c")).Count(c => c.Attribute("t")?.Value == "s") ?? 0;
        }

        sst.Root.SetAttributeValue("count", references.ToString(CultureInfo.InvariantCulture));
    }

    private static string Describe(string value) => value.Length == 0 ? "nothing (empty)" : "'" + PlcAlarmPolicy.Bound(value, 200) + "'";
}
