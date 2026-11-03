using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Builds small Office Open XML workbooks for the FakeWorker and the tests: a package with content types, package
/// relationships, a custom document property (TIA Portal's import checks <c>FileContent</c> on text list workbooks),
/// the workbook, its relationships, one worksheet per sheet and a shared string table. The sheet layouts the fixtures
/// use are invented for tests; they are not TIA Portal's export layout.
/// </summary>
public static class XlsxFixture
{
    public sealed record Sheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows);

    /// <param name="sharedStrings">False writes every text as an inline string and no shared string table.</param>
    public static byte[] Build(IReadOnlyList<Sheet> sheets, string fileContent = "Alarm text lists", bool sharedStrings = true)
    {
        var strings = new List<string>();
        var references = 0;
        int Index(string value)
        {
            references++;
            var index = strings.IndexOf(value);
            if (index >= 0) return index;
            strings.Add(value);
            return strings.Count - 1;
        }

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var overrides = new StringBuilder();
            overrides.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            overrides.Append("<Override PartName=\"/docProps/custom.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.custom-properties+xml\"/>");
            var workbookSheets = new StringBuilder();
            var workbookRels = new StringBuilder();
            for (var index = 0; index < sheets.Count; index++)
            {
                var number = (index + 1).ToString(CultureInfo.InvariantCulture);
                overrides.Append($"<Override PartName=\"/xl/worksheets/sheet{number}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                workbookSheets.Append($"<sheet name=\"{Escape(sheets[index].Name)}\" sheetId=\"{number}\" r:id=\"rId{number}\"/>");
                workbookRels.Append($"<Relationship Id=\"rId{number}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{number}.xml\"/>");
                var data = new StringBuilder();
                var maxColumn = 0;
                for (var row = 0; row < sheets[index].Rows.Count; row++)
                {
                    var rowNumber = (row + 1).ToString(CultureInfo.InvariantCulture);
                    data.Append($"<row r=\"{rowNumber}\">");
                    var cells = sheets[index].Rows[row];
                    for (var column = 0; column < cells.Count; column++)
                    {
                        var reference = Column(column + 1) + rowNumber;
                        switch (cells[column])
                        {
                            case null:
                                continue;
                            case string text when sharedStrings:
                                data.Append($"<c r=\"{reference}\" t=\"s\"><v>{Index(text)}</v></c>");
                                break;
                            case string text:
                                data.Append($"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>");
                                break;
                            case IFormattable numeric:
                                data.Append($"<c r=\"{reference}\" s=\"1\"><v>{numeric.ToString(null, CultureInfo.InvariantCulture)}</v></c>");
                                break;
                        }

                        maxColumn = Math.Max(maxColumn, column + 1);
                    }

                    data.Append("</row>");
                }

                var dimension = sheets[index].Rows.Count == 0 ? "A1" : $"A1:{Column(Math.Max(1, maxColumn))}{sheets[index].Rows.Count}";
                Write(zip, $"xl/worksheets/sheet{number}.xml",
                    $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><dimension ref=\"{dimension}\"/><sheetData>{data}</sheetData></worksheet>");
            }

            if (sharedStrings)
            {
                overrides.Append("<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>");
                workbookRels.Append("<Relationship Id=\"rIdStrings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>");
                Write(zip, "xl/sharedStrings.xml",
                    $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{references}\" uniqueCount=\"{strings.Count}\">"
                    + string.Concat(strings.Select(text => $"<si><t xml:space=\"preserve\">{Escape(text)}</t></si>")) + "</sst>");
            }

            Write(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + overrides + "</Types>");
            Write(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties\" Target=\"docProps/custom.xml\"/></Relationships>");
            Write(zip, "docProps/custom.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" "
                + "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\"><property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" name=\"FileContent\">"
                + $"<vt:lpwstr>{Escape(fileContent)}</vt:lpwstr></property></Properties>");
            Write(zip, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" "
                + $"xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>{workbookSheets}</sheets></workbook>");
            Write(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + workbookRels + "</Relationships>");
        }

        return output.ToArray();
    }

    /// <summary>The raw text of one package part (for tests that check what a patch kept unchanged).</summary>
    public static string Part(byte[] workbook, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        var bytes = new UTF8Encoding(false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;

    private static string Column(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            name = (char)('A' + (column - 1) % 26) + name;
            column = (column - 1) / 26;
        }

        return name;
    }
}
