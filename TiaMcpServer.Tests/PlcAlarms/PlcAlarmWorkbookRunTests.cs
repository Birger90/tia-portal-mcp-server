using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.FakeWorker.Plc;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// The Siemens-free orchestration the worker and the FakeWorker share, against scripted export/import delegates: result
/// states, the hash check, the patch, the verification re-export (missed edits and unrequested changes), caller files
/// (commit race, overwrite, cleanup), import verification and temporary directory cleanup on every exit.
/// </summary>
public sealed class PlcAlarmWorkbookRunTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("tia-mcp-alarm-run-").FullName;
    private readonly string _temporary;
    private readonly string _files;
    private byte[] _stored = Workbook("Stopped");
    private int _imports;
    private int _exclusive;
    private int _exclusiveOpen;

    public PlcAlarmWorkbookRunTests()
    {
        _temporary = Directory.CreateDirectory(Path.Combine(_root, "temp")).FullName;
        _files = Directory.CreateDirectory(Path.Combine(_root, "files")).FullName;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static byte[] Workbook(string d2, string a1 = "Text list") => XlsxFixture.Build(new[]
    {
        new XlsxFixture.Sheet("Text lists", new IReadOnlyList<object?>[]
        {
            new object?[] { a1, "From", "To", "en-US" },
            new object?[] { "USER_1", 0, 0, d2 },
        }),
    });

    private static PlcAlarmResultReader.Outcome State(string? state) => new() { RuntimeType = "Scripted.Result", State = state };

    private PlcAlarmWorkbookRun Run() => new(_temporary, () =>
    {
        _exclusive++;
        _exclusiveOpen++;
        return new Scope(() => _exclusiveOpen--);
    });

    private PlcAlarmWorkbookRun.Transfer Export(string? state = "Success") => path =>
    {
        File.WriteAllBytes(path, _stored);
        return State(state);
    };

    private PlcAlarmWorkbookRun.Transfer Import(string? state = "Success", Func<byte[], byte[]>? transform = null) => path =>
    {
        _imports++;
        _stored = (transform ?? (bytes => bytes))(File.ReadAllBytes(path));
        return State(state);
    };

    private string Hash() => XlsxCellWorkbook.Hash(XlsxCellWorkbook.Read(_stored));

    private static PlcAlarmCellEditInfo Edit(string expected, string value) => new() { Sheet = "Text lists", Cell = "D2", Expected = expected, Value = value };

    private void AssertNoTemporaryDirectories() => Assert.Empty(Directory.GetDirectories(_temporary));

    [Fact]
    public void Read_FailsOnAFailureOrUnreadableState_AndAlwaysRemovesItsDirectory()
    {
        var info = new PlcAlarmWorkbookInfo { Kind = "textLists" };
        Run().Read(Export(), info, new XlsxProjection(), null);
        Assert.Equal(Hash(), info.WorkbookHash);
        Assert.Equal("Success", info.ExportState);
        AssertNoTemporaryDirectories();

        var failure = Assert.Throws<PlcAlarmException>(() => Run().Read(Export("Failure"), new PlcAlarmWorkbookInfo(), new XlsxProjection(), null));
        Assert.Equal("worker_operation_failed", failure.Category);
        Assert.Contains("state Failure", failure.Message);
        Assert.Contains("Nothing was written", failure.Message);
        Assert.Contains("an unreadable state", Assert.Throws<PlcAlarmException>(() => Run().Read(Export(null), new PlcAlarmWorkbookInfo(), new XlsxProjection(), null)).Message);
        AssertNoTemporaryDirectories();
        Assert.Contains("pass sheet", Assert.Throws<PlcAlarmException>(() => Run().Read(Export(), new PlcAlarmWorkbookInfo(), new XlsxProjection { StartRow = 2 }, null)).Message);
    }

    [Fact]
    public void UpdateCells_ChecksTheHashInsideTheExclusiveScope_AndImportsOnlyWhenItMatches()
    {
        var stale = Assert.Throws<PlcAlarmException>(() => Run().UpdateCells(Export(), Import(), new[] { Edit("Stopped", "Halted") }, "cells:sha256:" + new string('0', 64),
            PlcAlarmMethods.ReadTextListWorkbook, new PlcAlarmWriteResultInfo()));
        Assert.Equal("state_changed", stale.Category);
        Assert.Equal(0, _imports);

        var result = new PlcAlarmWriteResultInfo();
        var before = Hash();
        Run().UpdateCells(Export(), Import(), new[] { Edit("Stopped", "Halted") }, before, PlcAlarmMethods.ReadTextListWorkbook, result);

        Assert.Equal((1, 2, 0), (_imports, _exclusive, _exclusiveOpen));
        Assert.Equal((before, Hash()), (result.WorkbookHashBefore, result.WorkbookHashAfter));
        Assert.Equal("Halted", Assert.Single(result.Edits!).After);
        Assert.Empty(result.UnrequestedChanges!);
        Assert.Equal("Success", result.State);
        AssertNoTemporaryDirectories();
    }

    [Fact]
    public void UpdateCells_FailsWhenTheReexportMissesAnEdit()
    {
        var result = new PlcAlarmWriteResultInfo();
        var exception = Assert.Throws<PlcAlarmException>(() => Run().UpdateCells(Export(), Import(transform: _ => Workbook("Stopped")),
            new[] { Edit("Stopped", "Halted") }, Hash(), PlcAlarmMethods.ReadTextListWorkbook, result));

        Assert.Equal("postcondition_failed", exception.Category);
        Assert.Contains("does not show 1 of 1 edits", exception.Message);
        Assert.Contains("partly changed", exception.Message);
        Assert.Equal("Stopped", result.Edits!.Single().After);
        AssertNoTemporaryDirectories();
    }

    [Fact]
    public void UpdateCells_FailsWhenTheImportChangedACellNoEditRequested()
    {
        var result = new PlcAlarmWriteResultInfo();
        var exception = Assert.Throws<PlcAlarmException>(() => Run().UpdateCells(Export(), Import(transform: _ => Workbook("Halted", a1: "Renamed")),
            new[] { Edit("Stopped", "Halted") }, Hash(), PlcAlarmMethods.ReadTextListWorkbook, result));

        Assert.Equal("postcondition_failed", exception.Category);
        Assert.Contains("changed 1 cell(s) no edit requested", exception.Message);
        Assert.Equal("Text lists!A1: 'Text list' -> 'Renamed'", Assert.Single(result.UnrequestedChanges!));
    }

    [Fact]
    public void UpdateCells_ReportsAFailedImportAsPossiblyChanged()
    {
        var exception = Assert.Throws<PlcAlarmException>(() => Run().UpdateCells(Export(), Import("Failure"), new[] { Edit("Stopped", "Halted") }, Hash(),
            PlcAlarmMethods.ReadTextListWorkbook, new PlcAlarmWriteResultInfo()));

        Assert.Contains("state Failure for the import", exception.Message);
        Assert.Contains("may be partly changed", exception.Message);
        Assert.Equal(0, _exclusiveOpen);
        AssertNoTemporaryDirectories();
    }

    [Fact]
    public void ExportFile_RemovesTheTemporaryFile_WhenTheExportFails()
    {
        var path = Path.Combine(_files, "TextLists.xlsx");
        Assert.Throws<PlcAlarmException>(() => Run().ExportFile(Export("Failure"), path, overwrite: false, ".xlsx", warningIsSuccess: false, new PlcAlarmWriteResultInfo()));

        Assert.Empty(Directory.GetFiles(_files));
    }

    [Fact]
    public void ExportFile_RefusesATargetCreatedDuringTheExport_AndKeepsIt()
    {
        var path = Path.Combine(_files, "TextLists.xlsx");
        var exception = Assert.Throws<PlcAlarmException>(() => Run().ExportFile(temporary =>
        {
            File.WriteAllBytes(temporary, _stored);
            File.WriteAllText(path, "someone else");
            return State("Success");
        }, path, overwrite: false, ".xlsx", warningIsSuccess: false, new PlcAlarmWriteResultInfo()));

        Assert.Equal("validation_error", exception.Category);
        Assert.Equal("someone else", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_files));
    }

    [Fact]
    public void ExportFile_SweepsStaleLeftoversAndWarnsAboutThem()
    {
        var path = Path.Combine(_files, "TextLists.xlsx");
        var stale = Path.Combine(_files, $".TextLists.{Guid.NewGuid():N}.tmp.xlsx");
        File.WriteAllText(stale, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-3));
        var run = Run();
        var result = new PlcAlarmWriteResultInfo();

        run.ExportFile(Export(), path, overwrite: false, ".xlsx", warningIsSuccess: false, result);

        Assert.False(File.Exists(stale));
        Assert.Contains(run.Warnings, w => w.Contains("interrupted earlier export", StringComparison.Ordinal));
        Assert.Equal(PlcAlarmFiles.Hash(path).Sha256, result.Sha256);
    }

    /// <summary>M3: every imported row shows (verified), an absent row contradicts the import, a layout difference is inconclusive.</summary>
    [Fact]
    public void ImportFile_VerifiesEveryImportedRow_ContradictsAMissingOne_AndIsInconclusiveOnLayouts()
    {
        var file = Path.Combine(_files, "Import.xlsx");
        File.WriteAllBytes(file, Workbook("Imported"));

        var shown = new PlcAlarmWorkbookRun(_temporary);
        var result = new PlcAlarmWriteResultInfo();
        shown.ImportFile(Import(), Export(), file, ".xlsx", warningIsSuccess: false, compareBytes: false, null, result);
        Assert.Equal(("rows", "verified", true, 1, 0), (result.ImportCheck!.Method, result.ImportCheck.Outcome, result.ImportCheck.Verified, result.ImportCheck.ImportedRows, result.ImportCheck.RowsNotShown));
        Assert.Null(result.ImportCheck.Reason);
        Assert.Equal(Hash(), result.ImportCheck.WorkbookHashAfter);
        Assert.Empty(shown.Warnings);

        var ignored = new PlcAlarmWorkbookRun(_temporary);
        var kept = new PlcAlarmWriteResultInfo();
        ignored.ImportFile(Import(transform: _ => Workbook("Stopped")), Export(), file, ".xlsx", warningIsSuccess: false, compareBytes: false, null, kept);
        Assert.Equal(("contradicted", false, 1), (kept.ImportCheck!.Outcome, kept.ImportCheck.Verified, kept.ImportCheck.RowsNotShown));
        Assert.Equal(new[] { "Text lists!row 2" }, kept.ImportCheck.NotShown);
        Assert.Contains(ignored.Warnings, w => w.Contains("changed the project, but the re-export contradicts it", StringComparison.Ordinal));

        // The re-export renames a header: that column cannot be compared, the others show.
        var layout = new PlcAlarmWorkbookRun(_temporary);
        var renamed = new PlcAlarmWriteResultInfo();
        layout.ImportFile(Import(transform: _ => Workbook("Imported", a1: "Name")), Export(), file, ".xlsx", warningIsSuccess: false, compareBytes: false, null, renamed);
        Assert.Equal(("inconclusive", false, 0), (renamed.ImportCheck!.Outcome, renamed.ImportCheck.Verified, renamed.ImportCheck.RowsNotShown));
        Assert.Contains("'Text list' (A)", renamed.ImportCheck.Reason);
        Assert.Contains(layout.Warnings, w => w.Contains("changed the project, but its verification is inconclusive", StringComparison.Ordinal));
        AssertNoTemporaryDirectories();
    }

    /// <summary>M1: a verification export that fails, writes nothing or writes an unreadable workbook never fails the import.</summary>
    [Fact]
    public void ImportFile_AFailedOrUnreadableVerificationExport_IsInconclusive_NotAFailedImport()
    {
        var file = Path.Combine(_files, "Import.xlsx");
        File.WriteAllBytes(file, Workbook("Imported"));
        PlcAlarmWorkbookRun.Transfer garbage = path =>
        {
            File.WriteAllText(path, "not a workbook");
            return State("Success");
        };
        PlcAlarmWorkbookRun.Transfer nothing = _ => State("Success");
        PlcAlarmWorkbookRun.Transfer threw = _ =>
        {
            var outcome = new PlcAlarmResultReader.Outcome();
            outcome.Unreadable.Add("the verification export threw EngineeringTargetInvocationException: busy");
            return outcome;
        };

        foreach (var (reexport, reason) in new[] { (garbage, "could not be compared"), (nothing, "wrote no file"), (threw, "EngineeringTargetInvocationException: busy") })
        {
            var run = new PlcAlarmWorkbookRun(_temporary);
            var result = new PlcAlarmWriteResultInfo();
            run.ImportFile(Import(), reexport, file, ".xlsx", warningIsSuccess: false, compareBytes: false, null, result);
            Assert.Equal(("Success", "inconclusive", false), (result.State, result.ImportCheck!.Outcome, result.ImportCheck.Verified));
            Assert.Contains(reason, result.ImportCheck.Reason);
            Assert.Contains(run.Warnings, w => w.Contains("changed the project", StringComparison.Ordinal));
        }

        AssertNoTemporaryDirectories();
    }

    /// <summary>L8: the import and its comparison use a private copy, hashed and reported; a later change of the caller's file has no effect.</summary>
    [Fact]
    public void ImportFile_ImportsAndComparesAPrivateCopy()
    {
        var file = Path.Combine(_files, "Import.xlsx");
        File.WriteAllBytes(file, Workbook("Imported"));
        var (bytes, sha256) = PlcAlarmFiles.Hash(file);
        string? imported = null;
        PlcAlarmWorkbookRun.Transfer import = path =>
        {
            imported = path;
            File.WriteAllBytes(file, Workbook("Changed meanwhile"));
            return Import()(path);
        };

        var result = new PlcAlarmWriteResultInfo();
        Run().ImportFile(import, Export(), file, ".xlsx", warningIsSuccess: false, compareBytes: false, null, result);

        Assert.NotEqual(Path.GetFullPath(file), Path.GetFullPath(imported!));
        Assert.Equal("Import.xlsx", Path.GetFileName(imported));
        Assert.Equal((bytes, sha256), (result.Bytes!.Value, result.Sha256));
        Assert.Equal((file, "verified"), (result.FilePath, result.ImportCheck!.Outcome));
        AssertNoTemporaryDirectories();
    }

    [Fact]
    public void ImportFile_ComparesAlarmClassesByteForByte_AndNeverFailsOnTheVerificationAlone()
    {
        var file = Path.Combine(_files, "AlarmClasses.dat");
        File.WriteAllText(file, "CLASSES");
        _stored = File.ReadAllBytes(file);
        var same = new PlcAlarmWriteResultInfo();
        Run().ImportFile(Import("Warning"), Export("Warning"), file, null, warningIsSuccess: true, compareBytes: true, null, same);
        Assert.Equal(("fileHash", "verified", true), (same.ImportCheck!.Method, same.ImportCheck.Outcome, same.ImportCheck.Verified));

        var different = new PlcAlarmWriteResultInfo();
        Run().ImportFile(Import(transform: bytes => bytes.Append((byte)'!').ToArray()), Export(), file, null, warningIsSuccess: true, compareBytes: true, null, different);
        Assert.Equal(("inconclusive", false), (different.ImportCheck!.Outcome, different.ImportCheck.Verified));
        Assert.Contains("byte for byte", different.ImportCheck.Reason);

        var failedReexport = new PlcAlarmWriteResultInfo();
        Run().ImportFile(Import(), Export("Error"), file, null, warningIsSuccess: true, compareBytes: true, null, failedReexport);
        Assert.Equal(("inconclusive", "Success"), (failedReexport.ImportCheck!.Outcome, failedReexport.State));
        Assert.Contains("verification export reported state Error", failedReexport.ImportCheck.Reason);

        var none = new PlcAlarmWorkbookRun(_temporary);
        var notReadBack = new PlcAlarmWriteResultInfo();
        none.ImportFile(Import(), null, file, null, warningIsSuccess: false, compareBytes: false, "no documented export.", notReadBack);
        Assert.Equal(("none", "notReadBack", false, "not read back: no documented export."),
            (notReadBack.ImportCheck!.Method, notReadBack.ImportCheck.Outcome, notReadBack.ImportCheck.Verified, notReadBack.ImportCheck.Reason));
        Assert.Contains(none.Warnings, w => w.Contains("changed the project; it is not read back", StringComparison.Ordinal));

        Assert.Contains("partly changed", Assert.Throws<PlcAlarmException>(() =>
            Run().ImportFile(Import("Error"), Export(), file, null, warningIsSuccess: false, compareBytes: true, null, new PlcAlarmWriteResultInfo())).Message);
        Assert.Equal(0, _exclusiveOpen);
    }

    [Fact]
    public void ExportDirectory_MovesTheExportIntoANewDirectory_AndCleansUpOnFailure()
    {
        var target = Path.Combine(_files, "ProDiag");
        var result = new PlcAlarmWriteResultInfo();
        Run().ExportDirectory(directory => File.WriteAllText(Path.Combine(directory, "alarms.csv"), "1;2"), target, result);
        Assert.Equal(("alarms.csv", target), (Assert.Single(result.Files!).Name, result.DirectoryPath));

        Assert.Throws<InvalidOperationException>(() => Run().ExportDirectory(_ => throw new InvalidOperationException("export failed"), Path.Combine(_files, "Other"), new PlcAlarmWriteResultInfo()));
        Assert.Equal(new[] { "ProDiag" }, Directory.GetDirectories(_files).Select(Path.GetFileName));
    }

    /// <summary>L4: an export that writes no file fails postcondition_failed and leaves no directory behind.</summary>
    [Fact]
    public void ExportDirectory_WithoutFiles_FailsAndCreatesNoDirectory()
    {
        var target = Path.Combine(_files, "Empty");
        var result = new PlcAlarmWriteResultInfo();

        var exception = Assert.Throws<PlcAlarmException>(() => Run().ExportDirectory(directory => Directory.CreateDirectory(Path.Combine(directory, "sub")), target, result));

        Assert.Equal("postcondition_failed", exception.Category);
        Assert.Contains("wrote no file", exception.Message);
        Assert.Empty(Directory.GetDirectories(_files));
        Assert.Null(result.Files);
    }

    /// <summary>L3: stale temporary export directories of the same target are swept (exact pattern, two hours old).</summary>
    [Fact]
    public void ExportDirectory_SweepsStaleTemporaryDirectoriesOfTheSameTarget()
    {
        var stale = Directory.CreateDirectory(Path.Combine(_files, $".ProDiag.{Guid.NewGuid():N}.tmp")).FullName;
        var fresh = Directory.CreateDirectory(Path.Combine(_files, $".ProDiag.{Guid.NewGuid():N}.tmp")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_files, $".Other.{Guid.NewGuid():N}.tmp")).FullName;
        foreach (var directory in new[] { stale, other }) Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddHours(-3));

        var run = Run();
        run.ExportDirectory(directory => File.WriteAllText(Path.Combine(directory, "alarms.csv"), "1;2"), Path.Combine(_files, "ProDiag"), new PlcAlarmWriteResultInfo());

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(other));
        Assert.Contains(run.Warnings, w => w.Contains("temporary export director(ies)", StringComparison.Ordinal));
    }

    [Fact]
    public void Runs_SweepStaleWorkbookDirectoriesOnce()
    {
        var stale = Directory.CreateDirectory(Path.Combine(_temporary, $"tia-mcp-alarm-read-{Guid.NewGuid():N}")).FullName;
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-3));

        Run().Read(Export(), new PlcAlarmWorkbookInfo(), new XlsxProjection(), null);

        Assert.False(Directory.Exists(stale));
        AssertNoTemporaryDirectories();
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
