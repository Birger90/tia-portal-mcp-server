using TiaMcpServer.Contracts.PlcAlarms;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>The Siemens-free rules shared by host and worker: caller paths, names, cells, numbers and result states.</summary>
public sealed class PlcAlarmPolicyTests
{
    [Theory]
    [InlineData(@"\\?\C:\Exports\TextLists.xlsx", "UNC")]
    [InlineData(@"\\.\PhysicalDrive0", "UNC")]
    [InlineData(@"\??\C:\x.xlsx", "UNC")]
    [InlineData("TextLists.xlsx", "absolute")]
    [InlineData(@"Exports\TextLists.xlsx", "absolute")]
    [InlineData(@"C:\Exports\", "directory")]
    [InlineData(@"C:\Exports\..\Windows\x.xlsx", "'..'")]
    [InlineData(@"C:\Exports\x.xlsx:stream", "':'")]
    [InlineData(@"C:\Exports\x*.xlsx", "wildcard")]
    [InlineData(@"C:\Exports\x.csv", ".xlsx")]
    [InlineData(@"C:\Exports\.xlsx", "before .xlsx")]
    [InlineData("C:\\Exports\\x\u202e.xlsx", "control")]
    public void FilePath_RefusesUnsafeOrMalformedPaths(string path, string reason)
        => Assert.Contains(reason, PlcAlarmPolicy.CheckFilePath(path, ".xlsx"));

    /// <summary>P9: reserved device names and trailing dots or spaces are refused in every segment, with or without an extension.</summary>
    [Theory]
    [InlineData(@"C:\CON\TextLists.xlsx", "reserved device name 'CON'")]
    [InlineData(@"C:\Exports\nul.xlsx", "reserved device name 'nul.xlsx'")]
    [InlineData(@"C:\Exports\COM1 .xlsx", "reserved device name")]
    [InlineData("C:\\Exports\\com\u00b9.xlsx", "reserved device name")]
    [InlineData(@"C:\LPT9\x.xlsx", "reserved device name 'LPT9'")]
    [InlineData(@"C:\Exports\CONIN$\x.xlsx", "reserved device name 'CONIN$'")]
    [InlineData(@"C:\Exports\conout$.xlsx", "reserved device name")]
    [InlineData(@"C:\Exports\COM0\x.xlsx", "reserved device name 'COM0'")]
    [InlineData(@"C:\Exports.\x.xlsx", "ends in a space or a dot ('Exports.')")]
    [InlineData(@"C:\Exports \x.xlsx", "ends in a space or a dot ('Exports ')")]
    [InlineData(@"C:\Exports\x.xlsx.", "ends in a space or a dot")]
    [InlineData(@"/??/C:/x.xlsx", "UNC")]
    [InlineData(@"\??\UNC\server\x.xlsx", "UNC")]
    [InlineData("C:\\Exports\\x\u200f.xlsx", "control")]
    public void FilePath_RefusesReservedNamesAndTrailingDotsInEverySegment(string path, string reason)
        => Assert.Contains(reason, PlcAlarmPolicy.CheckFilePath(path, ".xlsx"));

    [Theory]
    [InlineData(@"Z:\Shared\Exports\TextLists.xlsx")]
    [InlineData(@"C:\Exports\CONSOLE\x.xlsx")]
    [InlineData(@"C:\Exports\COM10.xlsx")]
    [InlineData(@"C:\Exports\.hidden\x.xlsx")]
    // UNC shares are accepted since 2026-11-01 (owner decision).
    [InlineData(@"\\server\share\TextLists.xlsx")]
    [InlineData("//server/share/TextLists.xlsx")]
    public void FilePath_AcceptsMappedDrivesAndNamesThatOnlyResembleDevices(string path)
        => Assert.Null(PlcAlarmPolicy.CheckFilePath(path, ".xlsx"));

    [Fact]
    public void DirectoryPath_FollowsTheSameRules()
    {
        Assert.Null(PlcAlarmPolicy.CheckDirectoryPath(@"C:\Exports\ProDiag"));
        Assert.Null(PlcAlarmPolicy.CheckDirectoryPath(@"C:\Exports\ProDiag\"));
        Assert.Contains("below a drive root", PlcAlarmPolicy.CheckDirectoryPath(@"C:\"));
        Assert.Contains("reserved device name", PlcAlarmPolicy.CheckDirectoryPath(@"C:\Exports\AUX"));
        Assert.Null(PlcAlarmPolicy.CheckDirectoryPath(@"\\server\share\x"));
        Assert.Contains("normalization", PlcAlarmPolicy.CheckDirectoryPath(@"\\?\C:\x"));
    }

    /// <summary>The network export_io_map uses the shared validator: \??\ and alternate data streams are refused too.</summary>
    [Theory]
    [InlineData(@"\??\C:\io.csv", "UNC")]
    [InlineData(@"C:\Exports\io.csv:stream", "alternate data streams")]
    [InlineData(@"C:\NUL\io.csv", "reserved device name")]
    [InlineData(@"C:\Exports.\io.csv", "ends in a space or a dot")]
    public void ExportIoMap_UsesTheSharedPathRules(string path, string reason)
    {
        var item = new TiaMcpServer.Network.NetworkOperationRequest { OperationId = "io", Operation = "export_io_map", FilePath = path };
        var validation = TiaMcpServer.Network.NetworkOperationCatalog.ValidateWrite(new[] { item });
        Assert.False(validation.IsValid);
        Assert.Contains(reason, validation.Error);
        Assert.NotNull(TiaMcpServer.Network.HardwareIoMapExport.Precheck(path, overwrite: true));
    }

    [Theory]
    [InlineData(@"C:\Exports\TextLists.xlsx")]
    [InlineData("C:/Exports/Alarm texts.XLSX")]
    [InlineData("/tmp/exports/TextLists.xlsx")]
    public void FilePath_AcceptsLocalAbsoluteFiles(string path)
    {
        if (OperatingSystem.IsWindows() && path.StartsWith('/')) return;
        Assert.Null(PlcAlarmPolicy.CheckFilePath(path, ".xlsx"));
    }

    [Fact]
    public void AlarmClassFiles_NeedTheDatExtension()
    {
        Assert.Equal(".DAT", PlcAlarmPolicy.RequiredExtension(PlcAlarmMethods.ExportAlarmClasses));
        Assert.Equal(".DAT", PlcAlarmPolicy.RequiredExtension(PlcAlarmMethods.ImportAlarmClasses));
        Assert.Null(PlcAlarmPolicy.CheckFilePath(@"C:\Exports\AlarmClasses.DAT", ".DAT"));
        Assert.Null(PlcAlarmPolicy.CheckFilePath(@"C:\Exports\alarmclasses.dat", ".DAT"));
        Assert.Equal("filePath must end in .DAT.", PlcAlarmPolicy.CheckFilePath(@"C:\Exports\AlarmClasses.xml", ".DAT"));
        Assert.Equal(".xlsx", PlcAlarmPolicy.RequiredExtension(PlcAlarmMethods.ImportSupervisionSettings));
    }

    [Theory]
    [InlineData("A1", 1, 1)]
    [InlineData("Z9", 26, 9)]
    [InlineData("AA10", 27, 10)]
    [InlineData("XFD1048576", 16384, 1048576)]
    public void Cells_ParseAndFormat(string cell, int column, int row)
    {
        Assert.Equal((column, row), PlcAlarmPolicy.ParseCell(cell));
        Assert.Equal(cell, PlcAlarmPolicy.CellName(column, row));
    }

    [Theory]
    [InlineData("a1")]
    [InlineData("A0")]
    [InlineData("XFE1")]
    [InlineData("A1048577")]
    [InlineData("A01")]
    [InlineData("$A$1")]
    [InlineData("")]
    public void Cells_RefuseMalformedReferences(string cell) => Assert.Null(PlcAlarmPolicy.ParseCell(cell));

    [Theory]
    [InlineData("en-US", true)]
    [InlineData("de-DE", true)]
    [InlineData("sr-Latn-RS", true)]
    [InlineData("en_US", false)]
    [InlineData("e", false)]
    [InlineData("en-US;drop", false)]
    public void Cultures_AreCheckedForSyntaxOnly(string culture, bool valid) => Assert.Equal(valid, PlcAlarmPolicy.IsCultureName(culture));

    [Theory]
    [InlineData("Decimal", true)]
    [InlineData("_Option2", true)]
    [InlineData("2Option", false)]
    [InlineData("Option A", false)]
    [InlineData("Option.A", false)]
    public void MemberNames_AreIdentifiers(string name, bool valid) => Assert.Equal(valid, PlcAlarmPolicy.IsMemberName(name));

    [Theory]
    [InlineData("3", true)]
    [InlineData("-2.5", true)]
    [InlineData("1E-3", true)]
    [InlineData("1,5", false)]
    [InlineData(" 1", false)]
    [InlineData("NaN", false)]
    [InlineData("", false)]
    public void Numbers_AreInvariant(string value, bool valid) => Assert.Equal(valid, PlcAlarmPolicy.IsNumber(value));

    [Fact]
    public void CellValues_AllowLineBreaksButNoOtherControls()
    {
        Assert.Null(PlcAlarmPolicy.CheckCellValue("a\tb\r\nc"));
        Assert.NotNull(PlcAlarmPolicy.CheckCellValue("a\u0001b"));
        Assert.NotNull(PlcAlarmPolicy.CheckCellValue("\ud800"));
        Assert.NotNull(PlcAlarmPolicy.CheckCellValue(new string('x', PlcAlarmPolicy.MaxCellValueChars + 1)));
        Assert.NotNull(PlcAlarmPolicy.CheckCellValue(null));
    }

    /// <summary>P2: only characters XML 1.0 carries are written; surrogate pairs pass, U+FFFE/U+FFFF and lone surrogates do not.</summary>
    [Theory]
    [InlineData("emoji \U0001F600 ok", null)]
    [InlineData("a\uFFFEb", "XML 1.0")]
    [InlineData("a\uFFFFb", "XML 1.0")]
    [InlineData("a\u0085b", "control characters")]
    [InlineData("a\u000bb", "control characters")]
    public void CellValues_AreValidXml(string value, string? reason)
    {
        var refusal = PlcAlarmPolicy.CheckCellValue(value);
        if (reason is null) Assert.Null(refusal);
        else Assert.Contains(reason, refusal);
        Assert.Equal(refusal?.Replace("value", "comment", StringComparison.Ordinal), PlcAlarmPolicy.CheckText(value, "comment"));
    }

    /// <summary>Lone surrogates are built in code: xUnit would replace them in attribute data.</summary>
    [Fact]
    public void CellValues_RefuseUnpairedSurrogates()
    {
        foreach (var value in new[] { "a" + (char)0xDC00 + "b", "a" + (char)0xD83D, (char)0xD83D + "x" })
            Assert.Contains("unpaired surrogates", PlcAlarmPolicy.CheckCellValue(value));
        Assert.Null(PlcAlarmPolicy.CheckCellValue("ok " + char.ConvertFromUtf32(0x1F600)));
    }

    [Theory]
    [InlineData("Success", false, PlcAlarmPolicy.StateVerdict.Success)]
    [InlineData("Warning", true, PlcAlarmPolicy.StateVerdict.Warning)]
    [InlineData("Warning", false, PlcAlarmPolicy.StateVerdict.Failure)]
    [InlineData("Error", true, PlcAlarmPolicy.StateVerdict.Failure)]
    [InlineData("success", false, PlcAlarmPolicy.StateVerdict.Failure)]
    [InlineData(null, false, PlcAlarmPolicy.StateVerdict.Unknown)]
    public void States_OnlyNamedSuccessSucceeds(string? state, bool warningIsSuccess, PlcAlarmPolicy.StateVerdict verdict)
        => Assert.Equal(verdict, PlcAlarmPolicy.InterpretState(state, warningIsSuccess));

    [Fact]
    public void Names_RefuseBlankOverlongAndControlCharacters()
    {
        Assert.Null(PlcAlarmPolicy.CheckName("USER_1", "textListName"));
        Assert.NotNull(PlcAlarmPolicy.CheckName(" ", "textListName"));
        Assert.NotNull(PlcAlarmPolicy.CheckName(new string('a', PlcAlarmPolicy.MaxNameChars + 1), "textListName"));
        Assert.NotNull(PlcAlarmPolicy.CheckName("a\u200bb", "textListName"));
    }

    [Fact]
    public void WorkbookHash_NamesItsOwnFormat()
    {
        Assert.True(PlcAlarmPolicy.IsWorkbookHash("cells:sha256:" + new string('a', 64)));
        Assert.False(PlcAlarmPolicy.IsWorkbookHash("xml:sha256:" + new string('a', 64)));
        Assert.False(PlcAlarmPolicy.IsWorkbookHash("cells:sha256:" + new string('A', 64)));
    }
}
