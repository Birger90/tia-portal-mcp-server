using System.IO.Compression;
using System.Text;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.FakeWorker.Plc;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// The layout-agnostic XLSX cell reader and patcher: reading shared, inline, rich and number cells, a hash over cells
/// only, bounded projection, probes, and patches that change only the edited cells and keep every other package part.
/// </summary>
public sealed class XlsxCellWorkbookTests
{
    private static byte[] Sample(bool sharedStrings = true) => XlsxFixture.Build(new[]
    {
        new XlsxFixture.Sheet("Text lists", new IReadOnlyList<object?>[]
        {
            new object?[] { "Text list", "From", "To", "en-US", "de-DE" },
            new object?[] { "USER_1", 0, 0, "Stopped", "Gestoppt" },
            new object?[] { "USER_1", 1, 1, "Running", null },
        }),
        new XlsxFixture.Sheet("Second", new IReadOnlyList<object?>[] { new object?[] { "only" } }),
    }, sharedStrings: sharedStrings);

    private static PlcAlarmCellEditInfo Edit(string sheet, string cell, string expected, string value, string? kind = null)
        => new() { Sheet = sheet, Cell = cell, Expected = expected, Value = value, Kind = kind };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_ReturnsCellsInOrderWithKinds(bool sharedStrings)
    {
        var grid = XlsxCellWorkbook.Read(Sample(sharedStrings));

        Assert.Equal(new[] { "Text lists", "Second" }, grid.Sheets.Select(s => s.Name));
        var cells = grid.Sheets[0].Cells;
        Assert.Equal(14, cells.Count);
        Assert.Equal(("A1", "string", "Text list"), (cells[0].Reference, cells[0].Kind, cells[0].Value));
        var number = cells.Single(c => c.Reference == "B3");
        Assert.Equal(("number", "1"), (number.Kind, number.Value));
        Assert.DoesNotContain(cells, c => c.Reference == "E3");
    }

    [Fact]
    public void Hash_CoversCellsOnly_AndChangesWithAnyCell()
    {
        var one = XlsxCellWorkbook.Hash(XlsxCellWorkbook.Read(Sample()));
        var inline = XlsxCellWorkbook.Hash(XlsxCellWorkbook.Read(Sample(sharedStrings: false)));
        var otherProperty = XlsxCellWorkbook.Hash(XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("Text lists", new IReadOnlyList<object?>[]
            {
                new object?[] { "Text list", "From", "To", "en-US", "de-DE" },
                new object?[] { "USER_1", 0, 0, "Stopped", "Gestoppt" },
                new object?[] { "USER_1", 1, 1, "Running", null },
            }),
            new XlsxFixture.Sheet("Second", new IReadOnlyList<object?>[] { new object?[] { "only" } }),
        }, fileContent: "something else")));

        Assert.True(PlcAlarmPolicy.IsWorkbookHash(one));
        Assert.Equal(one, inline);
        Assert.Equal(one, otherProperty);
        var (patched, _) = XlsxCellWorkbook.Patch(Sample(), new[] { Edit("Second", "A1", "only", "changed") });
        Assert.NotEqual(one, XlsxCellWorkbook.Hash(XlsxCellWorkbook.Read(patched)));
    }

    [Fact]
    public void Project_BoundsRowsAndValues()
    {
        var long1 = new string('x', PlcAlarmPolicy.MaxReturnedValueChars + 5);
        var grid = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("S", new IReadOnlyList<object?>[] { new object?[] { "a" }, new object?[] { long1 }, new object?[] { "c" } }),
        }));

        var sheets = XlsxCellWorkbook.Project(grid, new XlsxProjection { MaxRows = 2 }, out var complete);

        Assert.False(complete);
        var sheet = Assert.Single(sheets);
        Assert.Equal((3, 1, 3, true, 3), (sheet.RowCount, sheet.ColumnCount, sheet.CellCount, sheet.RowsTruncated, sheet.NextStartRow));
        Assert.Equal(2, sheet.Rows.Count);
        Assert.True(sheet.Rows[1].Cells[0].ValueTruncated);
        Assert.Equal(PlcAlarmPolicy.MaxReturnedValueChars, sheet.Rows[1].Cells[0].Value.Length);
        Assert.Equal(long1, XlsxCellWorkbook.Probe(grid, "S", "A2").Value);
    }

    /// <summary>P3: a TIA-like sheet of 10 columns with umlauts stays inside the 60,000-character value budget and pages on.</summary>
    [Fact]
    public void Project_StaysInsideTheValueBudget_AndPagesWithSheetAndStartRow()
    {
        var rows = new List<IReadOnlyList<object?>> { Enumerable.Range(1, 10).Select(c => (object?)$"Column {c}").ToArray() };
        for (var row = 1; row <= 400; row++)
            rows.Add(Enumerable.Range(1, 10).Select(c => (object?)$"Störung {row}/{c}: Ventil öffnet nicht – Überlast prüfen, Grenzwert überschritten <&>").ToArray());
        var workbook = XlsxFixture.Build(new[] { new XlsxFixture.Sheet("Alarm texts", rows), new XlsxFixture.Sheet("Other", new IReadOnlyList<object?>[] { new object?[] { "x" } }) });
        var grid = XlsxCellWorkbook.Read(workbook);

        var first = new PlcAlarmWorkbookInfo { Kind = "alarmTexts", WorkbookHash = XlsxCellWorkbook.Hash(grid) };
        first.Sheets = XlsxCellWorkbook.Project(grid, new XlsxProjection { MaxRows = PlcAlarmPolicy.MaxRows }, out var complete);
        var serialized = TiaMcpServer.Json.CanonicalJson.Serialize(first);

        Assert.False(complete);
        Assert.True(serialized.Length <= TiaMcpServer.OperationBatches.StructuredOperationBatchPayloadBudget.DefaultItemChars, $"{serialized.Length} characters");
        var sheet = first.Sheets[0];
        Assert.True(sheet.RowsTruncated);
        Assert.True(sheet.Rows.Count is > 1 and < 401, sheet.Rows.Count.ToString());
        Assert.Equal(sheet.Rows[^1].Row + 1, sheet.NextStartRow);
        Assert.Equal((true, 1), (first.Sheets[1].RowsTruncated, first.Sheets[1].NextStartRow));

        var next = XlsxCellWorkbook.Project(grid, new XlsxProjection { Sheet = "alarm TEXTS", StartRow = sheet.NextStartRow!.Value, MaxRows = 5 }, out _);
        Assert.Equal(Enumerable.Range(sheet.NextStartRow!.Value, 5), next[0].Rows.Select(r => r.Row));
        Assert.Equal((0, true, 1), (next[1].Rows.Count, next[1].RowsTruncated, next[1].NextStartRow));
        var last = XlsxCellWorkbook.Project(grid, new XlsxProjection { Sheet = "Alarm texts", StartRow = 400, MaxRows = 50 }, out _);
        Assert.Equal(new[] { 400, 401 }, last[0].Rows.Select(r => r.Row));
        Assert.Equal((false, (int?)null), (last[0].RowsTruncated, last[0].NextStartRow));
        Assert.Equal("target_not_found", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Project(grid, new XlsxProjection { Sheet = "Nope" }, out _)).Category);
    }

    /// <summary>L2: a first row wider than the budget returns the cells that fit (the first one cut) and pages on by column.</summary>
    [Fact]
    public void Project_CutsTheFirstRowToTheBudget_AndPagesItByColumn()
    {
        var huge = new string('ä', 3000);
        var grid = XlsxCellWorkbook.Read(XlsxFixture.Build(new[] { new XlsxFixture.Sheet("S", new IReadOnlyList<object?>[] { new object?[] { huge, huge, huge }, new object?[] { "b" } }) }));

        var sheets = XlsxCellWorkbook.Project(grid, new XlsxProjection { MaxChars = 6000 }, out var complete);

        Assert.False(complete);
        var row = Assert.Single(sheets[0].Rows);
        var cell = Assert.Single(row.Cells);
        Assert.Equal(("A1", true), (cell.Cell, cell.ValueTruncated));
        Assert.Equal((true, "B"), (row.CellsTruncated, row.NextStartColumn));
        Assert.Equal(2, sheets[0].NextStartRow);
        Assert.True(TiaMcpServer.Json.CanonicalJson.Serialize(sheets).Length <= 6000);

        var next = XlsxCellWorkbook.Project(grid, new XlsxProjection { Sheet = "S", StartRow = 1, StartColumn = 2, MaxChars = 6000 }, out _);
        Assert.Equal("B1", Assert.Single(Assert.Single(next[0].Rows).Cells).Cell);
        Assert.Equal("C", next[0].Rows[0].NextStartColumn);
        var last = XlsxCellWorkbook.Project(grid, new XlsxProjection { Sheet = "S", StartRow = 1, StartColumn = 3, MaxChars = 6000 }, out _);
        Assert.Equal((false, (string?)null), (last[0].Rows[0].CellsTruncated, last[0].Rows[0].NextStartColumn));
        // Row 2 has no cell from column C on, so the page ends.
        Assert.Equal((false, (int?)null), (last[0].RowsTruncated, last[0].NextStartRow));
    }

    /// <summary>L2: cells that fit whole are returned whole; the row is cut before the first cell that does not fit, never above the budget.</summary>
    [Theory]
    [InlineData(3000)]
    [InlineData(8000)]
    [InlineData(20000)]
    public void Project_NeverExceedsTheBudget_WithAWideFirstRow(int budget)
    {
        var cells = Enumerable.Range(1, 80).Select(c => (object?)$"Übersetzung {c}: {new string('x', 300)}").ToArray();
        var grid = XlsxCellWorkbook.Read(XlsxFixture.Build(new[] { new XlsxFixture.Sheet("S", new IReadOnlyList<object?>[] { cells, new object?[] { "next" } }) }));

        var info = new PlcAlarmWorkbookInfo { Kind = "alarmTexts", WorkbookHash = XlsxCellWorkbook.Hash(grid) };
        info.Sheets = XlsxCellWorkbook.Project(grid, new XlsxProjection { MaxChars = budget }, out _);

        Assert.True(TiaMcpServer.Json.CanonicalJson.Serialize(info.Sheets).Length <= budget, $"{TiaMcpServer.Json.CanonicalJson.Serialize(info.Sheets).Length} > {budget}");
        var row = Assert.Single(info.Sheets[0].Rows);
        Assert.True(row.CellsTruncated);
        Assert.All(row.Cells, cell => Assert.False(cell.ValueTruncated));
        Assert.Equal(PlcAlarmPolicy.ColumnName(row.Cells.Count + 1), row.NextStartColumn);
    }

    /// <summary>
    /// L2: the per-row estimate covers every member a row is written with (cellsTruncated, nextStartColumn included), so
    /// many short rows with the longest cell references never exceed the budget either.
    /// </summary>
    [Theory]
    [InlineData(2000)]
    [InlineData(10000)]
    [InlineData(56000)]
    public void Project_NeverExceedsTheBudget_WithManyShortRows(int budget)
    {
        var rows = Enumerable.Range(1, 3000).Select(r => (IReadOnlyList<object?>)new object?[] { r % 2 == 0 ? (object?)r : "x" }).ToArray();
        var grid = XlsxCellWorkbook.Read(XlsxFixture.Build(new[] { new XlsxFixture.Sheet("S", rows) }));

        var sheets = XlsxCellWorkbook.Project(grid, new XlsxProjection { MaxRows = PlcAlarmPolicy.MaxRows, MaxChars = budget }, out _);

        var serialized = TiaMcpServer.Json.CanonicalJson.Serialize(sheets).Length;
        Assert.True(serialized <= budget, $"{serialized} > {budget}");
        Assert.True(sheets[0].RowsTruncated);
    }

    [Fact]
    public void JsonChars_IsAnUpperBoundOfTheCanonicalWriter()
    {
        foreach (var text in new[] { "plain", "Ä Ö Ü ß", "<&>'\"+`", "tab\tnew\nline", "emoji \U0001F600", "" })
            Assert.True(XlsxCellWorkbook.JsonChars(text) >= TiaMcpServer.Json.CanonicalJson.Serialize(text).Length, text);
    }

    [Fact]
    public void Probe_ReportsMissingSheetsAndEmptyCells()
    {
        var grid = XlsxCellWorkbook.Read(Sample());

        Assert.False(XlsxCellWorkbook.Probe(grid, "Nope", "A1").SheetExists);
        var empty = XlsxCellWorkbook.Probe(grid, "text LISTS", "E3");
        Assert.True(empty.SheetExists);
        Assert.False(empty.Exists);
        Assert.Equal(string.Empty, empty.Value);
        Assert.Equal("Gestoppt", XlsxCellWorkbook.Probe(grid, "Text lists", "E2").Value);
    }

    [Fact]
    public void Patch_EditsTextNumberAndEmptyCells_AndKeepsOtherParts()
    {
        var original = Sample();
        var (patched, changes) = XlsxCellWorkbook.Patch(original, new[]
        {
            Edit("Text lists", "D2", "Stopped", "Halted"),
            Edit("Text lists", "B3", "1", "2"),
            Edit("Text lists", "E3", "", "Laeuft"),
            Edit("Text lists", "A5", "", "USER_2"),
            Edit("Text lists", "B5", "", "7", "number"),
        });

        var grid = XlsxCellWorkbook.Read(patched);
        Assert.Equal("Halted", XlsxCellWorkbook.Probe(grid, "Text lists", "D2").Value);
        Assert.Equal(("number", "2"), (XlsxCellWorkbook.Probe(grid, "Text lists", "B3").Kind, XlsxCellWorkbook.Probe(grid, "Text lists", "B3").Value));
        Assert.Equal("Laeuft", XlsxCellWorkbook.Probe(grid, "Text lists", "E3").Value);
        Assert.Equal("USER_2", XlsxCellWorkbook.Probe(grid, "Text lists", "A5").Value);
        Assert.Equal("number", XlsxCellWorkbook.Probe(grid, "Text lists", "B5").Kind);
        Assert.Equal(new[] { "string", "number", "string", "string", "number" }, changes.Select(c => c.Kind));
        Assert.Equal("Stopped", changes[0].Before);
        // Untouched parts keep their content; the unedited sheet is unchanged.
        Assert.Equal(XlsxFixture.Part(original, "docProps/custom.xml"), XlsxFixture.Part(patched, "docProps/custom.xml"));
        Assert.Equal(XlsxFixture.Part(original, "xl/worksheets/sheet2.xml"), XlsxFixture.Part(patched, "xl/worksheets/sheet2.xml"));
        Assert.Equal(XlsxFixture.Part(original, "[Content_Types].xml"), XlsxFixture.Part(patched, "[Content_Types].xml"));
        Assert.Contains("ref=\"A1:E5\"", XlsxFixture.Part(patched, "xl/worksheets/sheet1.xml"));
        var sst = XlsxFixture.Part(patched, "xl/sharedStrings.xml");
        Assert.Contains("uniqueCount=\"", sst);
        // Every untouched cell still reads the same.
        var before = XlsxCellWorkbook.Read(original).Sheets[0].Cells.Where(c => c.Reference is not ("D2" or "B3"));
        Assert.All(before, cell => Assert.Equal(cell.Value, XlsxCellWorkbook.Probe(grid, "Text lists", cell.Reference).Value));
    }

    [Fact]
    public void Patch_RecountsSharedStringReferences()
    {
        var (patched, _) = XlsxCellWorkbook.Patch(Sample(), new[] { Edit("Text lists", "B2", "0", "zero", "string"), Edit("Text lists", "A2", "USER_1", "1", "number") });

        var sst = XlsxFixture.Part(patched, "xl/sharedStrings.xml");
        var references = XlsxCellWorkbook.Read(patched).Sheets.Sum(s => s.Cells.Count(c => c.Kind == "string"));
        Assert.Contains($"count=\"{references}\"", sst);
    }

    [Fact]
    public void Patch_WithoutSharedStringTable_WritesInlineStrings()
    {
        var (patched, _) = XlsxCellWorkbook.Patch(Sample(sharedStrings: false), new[] { Edit("Text lists", "D2", "Stopped", " spaced ") });

        Assert.Contains("t=\"inlineStr\"", XlsxFixture.Part(patched, "xl/worksheets/sheet1.xml"));
        Assert.Equal(" spaced ", XlsxCellWorkbook.Probe(XlsxCellWorkbook.Read(patched), "Text lists", "D2").Value);
    }

    [Fact]
    public void Patch_KeepsLiteralEscapeSequencesAndLineBreaks()
    {
        var (patched, _) = XlsxCellWorkbook.Patch(Sample(), new[] { Edit("Text lists", "D2", "Stopped", "a_x000D_b\r\nc") });

        Assert.Equal("a_x000D_b\r\nc", XlsxCellWorkbook.Probe(XlsxCellWorkbook.Read(patched), "Text lists", "D2").Value);
    }

    [Theory]
    [InlineData("Text lists", "D2", "Wrong", "state_changed")]
    [InlineData("Missing", "A1", "", "validation_error")]
    public void Patch_RefusesWrongExpectationsAndSheets(string sheet, string cell, string expected, string category)
    {
        var exception = Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Patch(Sample(), new[] { Edit(sheet, cell, expected, "x") }));

        Assert.Equal(category, exception.Category);
    }

    [Fact]
    public void Patch_RefusesDuplicateEditsFormulasAndNonNumbers()
    {
        Assert.Equal("validation_error", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Patch(Sample(),
            new[] { Edit("Text lists", "D2", "Stopped", "a"), Edit("Text lists", "D2", "a", "b") })).Category);
        Assert.Equal("validation_error", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Patch(Sample(),
            new[] { Edit("Text lists", "B2", "0", "zero") })).Category);
        var formula = Replace(Sample(), "xl/worksheets/sheet2.xml", "<c r=\"A1\" t=\"s\"><v>", "<c r=\"A1\" t=\"str\"><f>1+1</f><v>");
        var exception = Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Patch(formula, new[] { Edit("Second", "A1", "only", "x") }));
        Assert.Contains("formula", exception.Message);
    }

    /// <summary>P1: rows out of order are read (sorted) but never patched; a duplicate cell position is malformed.</summary>
    [Fact]
    public void Patch_RefusesUnorderedRows_AndReadRefusesDuplicateCells()
    {
        var unordered = Replace(Sample(), "xl/worksheets/sheet2.xml", "<sheetData><row r=\"1\">",
            "<sheetData><row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>three</t></is></c></row><row r=\"1\">");
        Assert.Equal(new[] { "A1", "A3" }, XlsxCellWorkbook.Read(unordered).Sheets[1].Cells.Select(cell => cell.Reference));
        var exception = Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Patch(unordered, new[] { Edit("Second", "A2", "", "x") }));
        Assert.Equal(("validation_error", true), (exception.Category, exception.Message.Contains("not in ascending order", StringComparison.Ordinal)));

        var duplicate = Replace(Sample(), "xl/worksheets/sheet2.xml", "</row>", "<c r=\"A1\" t=\"inlineStr\"><is><t>again</t></is></c></row>");
        Assert.Contains("twice", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Read(duplicate)).Message);
    }

    /// <summary>P6: implicit row and cell positions are patched exactly where Read reports the cells.</summary>
    [Fact]
    public void Patch_SupportsImplicitRowAndCellPositions()
    {
        var implicitPositions = Replace(Replace(Sample(sharedStrings: false), "xl/worksheets/sheet1.xml", "<row r=\"3\">", "<row>"),
            "xl/worksheets/sheet1.xml", "<c r=\"D3\" t=\"inlineStr\">", "<c t=\"inlineStr\">");
        var before = XlsxCellWorkbook.Read(implicitPositions);
        Assert.Equal("Running", XlsxCellWorkbook.Probe(before, "Text lists", "D3").Value);

        var (patched, _) = XlsxCellWorkbook.Patch(implicitPositions, new[] { Edit("Text lists", "D3", "Running", "Laeuft"), Edit("Text lists", "E3", "", "neu") });

        var after = XlsxCellWorkbook.Read(patched);
        var differences = XlsxCellWorkbook.Diff(before, after);
        Assert.Equal(new[] { "D3", "E3" }, differences.Select(d => d.Cell));
        Assert.Equal(("Running", "Laeuft"), (differences[0].Before, differences[0].After));
    }

    /// <summary>P7: a number written over a text cell lowers the shared string count; the table is rewritten.</summary>
    [Fact]
    public void Patch_RewritesTheSharedStringTable_WhenOnlyItsCountChanges()
    {
        var original = Sample();
        var countBefore = XlsxCellWorkbook.Read(original).Sheets.Sum(s => s.Cells.Count(c => c.Kind == "string"));

        var (patched, changes) = XlsxCellWorkbook.Patch(original, new[] { Edit("Text lists", "A2", "USER_1", "5", "number") });

        Assert.Equal("number", Assert.Single(changes).Kind);
        Assert.Contains($"count=\"{countBefore - 1}\"", XlsxFixture.Part(patched, "xl/sharedStrings.xml"));
        Assert.Contains($"count=\"{countBefore}\"", XlsxFixture.Part(original, "xl/sharedStrings.xml"));
    }

    /// <summary>A part the patch only copies is charged to the decompression budget too (not only the parts it reads).</summary>
    [Fact]
    public void Patch_ChargesCopiedPartsToTheDecompressionBudget()
    {
        var workbook = AddPart(Sample(), "docProps/large.bin", new byte[2_000_000]);

        Assert.NotEmpty(XlsxCellWorkbook.Read(workbook, maxDecompressedBytes: 1_000_000).Sheets);
        var exception = Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Patch(workbook, new[] { Edit("Second", "A1", "only", "x") }, maxDecompressedBytes: 1_000_000));
        Assert.Contains("decompresses to more than 1000000 bytes", exception.Message);
        Assert.Equal(new byte[2_000_000], PartBytes(XlsxCellWorkbook.Patch(workbook, new[] { Edit("Second", "A1", "only", "x") }).Workbook, "docProps/large.bin"));
    }

    [Fact]
    public void CompareRows_FindsImportedRowsAnywhere_AndCountsMissingOnes()
    {
        var imported = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("Text lists", new IReadOnlyList<object?>[] { new object?[] { "Text list", "From" }, new object?[] { "USER_2", 1 }, new object?[] { "USER_9", 1 } }),
        }));
        var reexport = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("Text lists", new IReadOnlyList<object?>[] { new object?[] { "Text list", "From" }, new object?[] { "USER_1", 0 }, new object?[] { "USER_2", 1.0 } }),
        }));

        var comparison = XlsxCellWorkbook.CompareRows(imported, reexport);

        Assert.Equal((2, 1), (comparison.ImportedRows, comparison.RowsNotShown));
        Assert.Equal(new[] { "Text lists!row 3" }, comparison.NotShown);
        Assert.Empty(comparison.NotCompared);
        Assert.Equal(0, XlsxCellWorkbook.CompareRows(reexport, reexport).RowsNotShown);
    }

    /// <summary>M3: columns are matched by header text, extra (language) columns and trailing empty cells are ignored.</summary>
    [Fact]
    public void CompareRows_MatchesColumnsByHeader_AndIgnoresExtraColumns()
    {
        var imported = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("Alarms", new IReadOnlyList<object?>[]
            {
                new object?[] { "ID", "Text [en-US]" },
                new object?[] { 1, "Valve open" },
                new object?[] { 2, "Pump off", "" },
            }),
        }));
        var reexport = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("Alarms", new IReadOnlyList<object?>[]
            {
                new object?[] { "Text [de-DE]", "Text [en-US]", "ID" },
                new object?[] { "Pumpe aus", "Pump off", 2.0 },
                new object?[] { "Ventil offen", "Valve open", 1 },
            }),
        }));

        var comparison = XlsxCellWorkbook.CompareRows(imported, reexport);

        Assert.Equal((2, 0), (comparison.ImportedRows, comparison.RowsNotShown));
        Assert.Empty(comparison.NotCompared);
    }

    /// <summary>M3: an imported column or sheet the re-export lacks is not compared (inconclusive), never counted as a missing row.</summary>
    [Fact]
    public void CompareRows_ListsWhatItCannotMap()
    {
        var imported = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("Alarms", new IReadOnlyList<object?>[] { new object?[] { "ID", "Text [fr-FR]" }, new object?[] { 1, "Vanne ouverte" } }),
            new XlsxFixture.Sheet("Extra", new IReadOnlyList<object?>[] { new object?[] { "H" }, new object?[] { "v" } }),
            new XlsxFixture.Sheet("Loose", new IReadOnlyList<object?>[] { new object?[] { "ID" }, new object?[] { 1, "no header" } }),
        }));
        var reexport = XlsxCellWorkbook.Read(XlsxFixture.Build(new[]
        {
            new XlsxFixture.Sheet("alarms", new IReadOnlyList<object?>[] { new object?[] { "ID", "Text [en-US]" }, new object?[] { 1, "Valve open" } }),
            new XlsxFixture.Sheet("Loose", new IReadOnlyList<object?>[] { new object?[] { "ID" }, new object?[] { 1 } }),
        }));

        var comparison = XlsxCellWorkbook.CompareRows(imported, reexport);

        Assert.Equal((3, 0), (comparison.ImportedRows, comparison.RowsNotShown));
        Assert.Equal(3, comparison.NotCompared.Count);
        Assert.Contains(comparison.NotCompared, note => note.Contains("column(s) B of sheet 'Loose'", StringComparison.Ordinal));
        Assert.Contains(comparison.NotCompared, note => note.Contains("'Text [fr-FR]' (B)", StringComparison.Ordinal));
        Assert.Contains(comparison.NotCompared, note => note.Contains("sheet 'Extra'", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_FailsClosedOnBrokenPackages()
    {
        Assert.Equal("worker_operation_failed", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Read(Encoding.UTF8.GetBytes("not a zip"))).Category);
        var escaping = Replace(Sample(), "xl/_rels/workbook.xml.rels", "Target=\"worksheets/sheet1.xml\"", "Target=\"../../outside.xml\"");
        Assert.Contains("leaves the package", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Read(escaping)).Message);
        var badIndex = Replace(Sample(), "xl/worksheets/sheet2.xml", "<v>", "<v>999");
        Assert.Contains("shared string", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Read(badIndex)).Message);
        var dtd = Replace(Sample(), "xl/workbook.xml", "<workbook", "<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><workbook");
        Assert.Contains("not well-formed", Assert.Throws<PlcAlarmException>(() => XlsxCellWorkbook.Read(dtd)).Message);
    }

    private static byte[] AddPart(byte[] workbook, string name, byte[] content)
    {
        using var output = new MemoryStream();
        output.Write(workbook, 0, workbook.Length);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
            stream.Write(content, 0, content.Length);
        }

        return output.ToArray();
    }

    private static byte[] PartBytes(byte[] workbook, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(name)!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static byte[] Replace(byte[] workbook, string part, string find, string replace)
    {
        using var input = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in input.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();
                if (entry.FullName == part)
                {
                    Assert.Contains(find, text);
                    text = text.Replace(find, replace, StringComparison.Ordinal);
                }

                using var stream = zip.CreateEntry(entry.FullName).Open();
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        return output.ToArray();
    }
}
