using TiaMcpServer.Contracts.PlcAlarms;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>Caller files: the export is staged next to the target and moved into place; nothing is overwritten unless asked.</summary>
public sealed class PlcAlarmFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("tia-mcp-alarm-files-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Export_MovesTheTemporaryFileIntoPlace_AndHashesIt()
    {
        var target = PathOf("TextLists.xlsx");
        PlcAlarmFiles.PrecheckExport(target, overwrite: false, ".xlsx");
        var temporary = PlcAlarmFiles.TemporaryPath(target);
        Assert.Equal(_directory, Path.GetDirectoryName(temporary));
        Assert.EndsWith(".xlsx", temporary, StringComparison.Ordinal);
        File.WriteAllBytes(temporary, new byte[] { 1, 2, 3 });

        var (overwritten, bytes, sha256) = PlcAlarmFiles.Commit(temporary, target, overwrite: false);

        Assert.False(overwritten);
        Assert.Equal(3, bytes);
        Assert.Equal(PlcAlarmFiles.Hash(target).Sha256, sha256);
        Assert.False(File.Exists(temporary));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Export_RefusesAnExistingFile_UnlessOverwrite()
    {
        var target = PathOf("TextLists.xlsx");
        File.WriteAllText(target, "keep");

        var refused = Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckExport(target, overwrite: false, ".xlsx"));
        Assert.Equal("validation_error", refused.Category);
        var temporary = PlcAlarmFiles.TemporaryPath(target);
        File.WriteAllText(temporary, "new");
        Assert.Equal("validation_error", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.Commit(temporary, target, overwrite: false)).Category);
        Assert.False(File.Exists(temporary));
        Assert.Equal("keep", File.ReadAllText(target));

        PlcAlarmFiles.PrecheckExport(target, overwrite: true, ".xlsx");
        File.WriteAllText(temporary, "new");
        Assert.True(PlcAlarmFiles.Commit(temporary, target, overwrite: true).Overwritten);
        Assert.Equal("new", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Export_RefusesMissingDirectoriesDirectoriesAndDevicePaths()
    {
        Assert.Contains("does not exist", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckExport(PathOf("missing/TextLists.xlsx"), false, ".xlsx")).Message);
        Directory.CreateDirectory(PathOf("dir.xlsx"));
        Assert.Contains("is a directory", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckExport(PathOf("dir.xlsx"), true, ".xlsx")).Message);
        Assert.Contains("normalization", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckExport(@"\\?\C:\x.xlsx", true, ".xlsx")).Message);
    }

    [Fact]
    public void Commit_NeverReplacesThroughALink_AndRechecksAtCommit()
    {
        var real = PathOf("real.xlsx");
        File.WriteAllText(real, "keep");
        var link = PathOf("link.xlsx");
        File.CreateSymbolicLink(link, real);

        Assert.Contains("link", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckExport(link, overwrite: true, ".xlsx")).Message);
        var temporary = PlcAlarmFiles.TemporaryPath(link);
        File.WriteAllText(temporary, "new");
        Assert.Equal("validation_error", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.Commit(temporary, link, overwrite: true)).Category);
        Assert.False(File.Exists(temporary));
        Assert.Equal("keep", File.ReadAllText(real));
    }

    [Fact]
    public void Inspect_ReportsLinkedAncestorsAndTheFullPath_ButNeverRefusesThem()
    {
        var target = Directory.CreateDirectory(PathOf("target")).FullName;
        var linked = PathOf("linked");
        Directory.CreateSymbolicLink(linked, target);
        var path = Path.Combine(linked, "TextLists.xlsx");

        var info = PlcAlarmFiles.Inspect(path);

        Assert.Equal(Path.GetFullPath(path), info.FullPath);
        Assert.True(info.ParentExists);
        Assert.False(info.Exists);
        Assert.Contains(linked, info.ReparseAncestors);
        Assert.False(info.NetworkDrive);
        Assert.Contains(PlcAlarmFiles.Notes(info), note => note.Contains("is a link (reparse point)", StringComparison.Ordinal));
        PlcAlarmFiles.PrecheckExport(path, overwrite: false, ".xlsx");
        Assert.Empty(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(PathOf("plain.xlsx"))));
    }

    [Fact]
    public void SweepStaleTemporaries_DeletesOnlyOldLeftoversOfThatExport()
    {
        var target = PathOf("TextLists.xlsx");
        var old = PathOf($".TextLists.{Guid.NewGuid():N}.tmp.xlsx");
        var fresh = PathOf($".TextLists.{Guid.NewGuid():N}.tmp.xlsx");
        var otherExport = PathOf($".Other.{Guid.NewGuid():N}.tmp.xlsx");
        var notOurs = PathOf(".TextLists.not-a-guid.tmp.xlsx");
        // L7: this server writes the GUID in lower-case "N" format only; an upper-case one is someone else's file.
        var upperCase = PathOf($".TextLists.{Guid.NewGuid().ToString("N").ToUpperInvariant()}.tmp.xlsx");
        // The name and extension compare case-insensitively, like Windows file names.
        var otherCase = PathOf($".textlists.{Guid.NewGuid():N}.tmp.XLSX");
        foreach (var file in new[] { old, fresh, otherExport, notOurs, upperCase, otherCase }) File.WriteAllText(file, "x");
        foreach (var file in new[] { old, otherExport, notOurs, upperCase, otherCase }) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-3));

        Assert.Equal(2, PlcAlarmFiles.SweepStaleTemporaries(target, DateTime.UtcNow));

        Assert.False(File.Exists(old) || File.Exists(otherCase));
        Assert.True(File.Exists(fresh) && File.Exists(otherExport) && File.Exists(notOurs) && File.Exists(upperCase));
    }

    /// <summary>L3: leftovers of an interrupted directory export (<c>.name.GUID.tmp</c>), the same pattern and age rule.</summary>
    [Fact]
    public void SweepStaleTemporaryDirectories_DeletesOnlyOldLeftoversOfThatDirectoryExport()
    {
        var target = PathOf("ProDiag");
        var old = Directory.CreateDirectory(PathOf($".ProDiag.{Guid.NewGuid():N}.tmp")).FullName;
        File.WriteAllText(Path.Combine(old, "alarms.csv"), "x");
        var fresh = Directory.CreateDirectory(PathOf($".ProDiag.{Guid.NewGuid():N}.tmp")).FullName;
        var upperCase = Directory.CreateDirectory(PathOf($".ProDiag.{Guid.NewGuid().ToString("N").ToUpperInvariant()}.tmp")).FullName;
        var otherExport = Directory.CreateDirectory(PathOf($".ProDiag2.{Guid.NewGuid():N}.tmp")).FullName;
        var file = PathOf($".ProDiag.{Guid.NewGuid():N}.tmp.xlsx");
        File.WriteAllText(file, "x");
        foreach (var directory in new[] { old, upperCase, otherExport }) Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-3));

        Assert.Equal(1, PlcAlarmFiles.SweepStaleTemporaryDirectories(target, DateTime.UtcNow));

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh) && Directory.Exists(upperCase) && Directory.Exists(otherExport) && File.Exists(file));
    }

    [Fact]
    public void SweepStaleDirectories_DeletesOnlyOldWorkbookDirectoriesOfThisServer()
    {
        var old = Directory.CreateDirectory(PathOf($"tia-mcp-alarm-read-{Guid.NewGuid():N}")).FullName;
        File.WriteAllText(Path.Combine(old, "workbook.xlsx"), "x");
        var fresh = Directory.CreateDirectory(PathOf($"tia-mcp-alarm-cells-{Guid.NewGuid():N}")).FullName;
        var foreign = Directory.CreateDirectory(PathOf("tia-mcp-alarm-something-else")).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-3));
        Directory.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddHours(-3));

        Assert.Equal(1, PlcAlarmFiles.SweepStaleDirectories(_directory, DateTime.UtcNow));

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh) && Directory.Exists(foreign));
    }

    [Fact]
    public void DirectoryExports_CreateANewDirectory_AndListEveryFile()
    {
        var target = PathOf("ProDiag");
        PlcAlarmFiles.PrecheckDirectoryExport(target);
        var temporary = PlcAlarmFiles.TemporaryDirectoryPath(target);
        Directory.CreateDirectory(Path.Combine(temporary, "sub"));
        File.WriteAllText(Path.Combine(temporary, "b.csv"), "b");
        File.WriteAllText(Path.Combine(temporary, "sub", "a.csv"), "a");

        var files = PlcAlarmFiles.CommitDirectory(temporary, target);

        Assert.Equal(new[] { "b.csv", "sub/a.csv" }, files.Select(f => f.Name));
        Assert.Equal(PlcAlarmFiles.Hash(Path.Combine(target, "b.csv")).Sha256, files[0].Sha256);
        Assert.False(Directory.Exists(temporary));
        Assert.Contains("already exists", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckDirectoryExport(target)).Message);
        Assert.Contains("parent directory", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckDirectoryExport(PathOf("missing/ProDiag"))).Message);
    }

    [Fact]
    public void Commit_FailsWhenTiaPortalWroteNothing()
        => Assert.Equal("worker_operation_failed",
            Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.Commit(PathOf(".never.tmp.xlsx"), PathOf("x.xlsx"), false)).Category);

    [Fact]
    public void Import_RequiresAnExistingFile()
    {
        Assert.Equal("target_not_found", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckImport(PathOf("absent.xlsx"), ".xlsx")).Category);
        File.WriteAllText(PathOf("present.xlsx"), "x");
        PlcAlarmFiles.PrecheckImport(PathOf("present.xlsx"), ".xlsx");
        Assert.Equal("validation_error", Assert.Throws<PlcAlarmException>(() => PlcAlarmFiles.PrecheckImport(PathOf("present.txt"), ".xlsx")).Category);
    }
}
