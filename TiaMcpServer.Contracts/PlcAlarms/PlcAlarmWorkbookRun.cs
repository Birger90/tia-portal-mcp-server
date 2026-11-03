namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// The Siemens-free orchestration of every alarm export and import, shared by the Openness worker and the FakeWorker: the
/// worker passes delegates that call the documented provider members and read their results, everything else (temporary
/// files and directories, result states, hashes, the cell patch, verification re-exports, caller files) runs here and is
/// unit-tested against scripted delegates.
/// <list type="bullet">
/// <item>A state counts as success only by <see cref="PlcAlarmPolicy.InterpretState"/>; anything else fails
/// worker_operation_failed, saying whether the project may already be changed.</item>
/// <item>A cell update runs export, hash check, patch, import and verification export inside one exclusive scope (the
/// worker's <c>TiaPortal.ExclusiveAccess</c>, which blocks input in that TIA Portal's UI; it is not documented to block
/// other Openness clients). It fails postcondition_failed when the re-export misses an edit or shows any cell no edit
/// requested.</item>
/// <item>An import first copies the caller's file to a private temporary file (hashed and reported), imports that copy and,
/// in the same exclusive scope, re-exports the same kind where an export is documented: <c>verified</c> when every imported
/// row shows (<see cref="XlsxCellWorkbook.CompareRows"/>, columns by header) or the alarm-class file re-exports byte for
/// byte, <c>contradicted</c> when a compared row is absent, <c>inconclusive</c> when the comparison cannot decide (a layout
/// difference, a differing alarm-class file, a failed or unreadable verification export), <c>notReadBack</c> without a
/// documented export. A verification never fails the import, which has already changed the project.</item>
/// <item>Temporary directories are always deleted; leftovers of a crashed worker are swept once per run.</item>
/// </list>
/// Failures are <see cref="PlcAlarmException"/>s; delegate exceptions (Siemens ones included) pass through unchanged.
/// </summary>
public sealed class PlcAlarmWorkbookRun
{
    private const string ValidationError = "validation_error";
    private const string StateChanged = "state_changed";
    private const string WorkerOperationFailed = "worker_operation_failed";
    private const string PostconditionFailed = "postcondition_failed";

    /// <summary>At most this many unrequested changes are listed.</summary>
    public const int MaxListedChanges = 50;

    /// <summary>Exports or imports the workbook at <paramref name="path"/> and returns the read result.</summary>
    public delegate PlcAlarmResultReader.Outcome Transfer(string path);

    private readonly string _temporaryRoot;
    private readonly Func<IDisposable?>? _exclusive;
    private readonly Func<DateTime> _utcNow;
    private bool _swept;

    /// <param name="temporaryRoot">Where workbook directories are created; the system temporary directory by default.</param>
    /// <param name="exclusive">Opens the exclusive scope of a cell update or import (the worker: <c>TiaPortal.ExclusiveAccess</c>).</param>
    public PlcAlarmWorkbookRun(string? temporaryRoot = null, Func<IDisposable?>? exclusive = null, Func<DateTime>? utcNow = null)
    {
        _temporaryRoot = temporaryRoot ?? Path.GetTempPath();
        _exclusive = exclusive;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Warnings for the response (state Warning accepted, network drive or link, not-verified import, swept leftovers).</summary>
    public List<string> Warnings { get; } = new List<string>();

    // ================================================================ reads

    /// <summary>Exports to a temporary file, reads the cells within the projection's budget, then deletes the file.</summary>
    public PlcAlarmWorkbookInfo Read(Transfer export, PlcAlarmWorkbookInfo info, XlsxProjection projection, IReadOnlyList<PlcAlarmCellRefInfo>? probes)
    {
        if (export is null) throw new ArgumentNullException(nameof(export));
        if (projection.MaxRows is < 1 or > PlcAlarmPolicy.MaxRows) throw new PlcAlarmException(ValidationError, $"maxElements must be between 1 and {PlcAlarmPolicy.MaxRows}.");
        if (projection.StartRow is < 1 or > PlcAlarmPolicy.MaxRow) throw new PlcAlarmException(ValidationError, $"startRow must be between 1 and {PlcAlarmPolicy.MaxRow}.");
        if (projection.StartColumn is < 1 or > PlcAlarmPolicy.MaxColumn) throw new PlcAlarmException(ValidationError, $"startColumn must be a column from A to {PlcAlarmPolicy.ColumnName(PlcAlarmPolicy.MaxColumn)}.");
        if (projection.Sheet is null && (projection.StartRow != 1 || projection.StartColumn != 1))
            throw new PlcAlarmException(ValidationError, "startRow and startColumn page one sheet; pass sheet with them.");
        WithTemporaryDirectory("read", directory =>
        {
            var path = Path.Combine(directory, "workbook.xlsx");
            var outcome = export(path);
            info.ExportState = outcome.State;
            RequireSuccess(outcome, "export", warningIsSuccess: false, changedProject: false);
            var grid = ReadGrid(path, "exported");
            info.WorkbookHash = XlsxCellWorkbook.Hash(grid);
            info.Messages.AddRange(grid.Messages);
            info.Probes = (probes ?? Array.Empty<PlcAlarmCellRefInfo>()).Select(probe => XlsxCellWorkbook.Probe(grid, probe.Sheet, probe.Cell)).ToList();
            info.Sheet = projection.Sheet is null ? null : XlsxCellWorkbook.ResolveSheet(grid, projection.Sheet) ?? projection.Sheet;
            info.StartRow = projection.Sheet is null ? 1 : projection.StartRow;
            info.StartColumn = PlcAlarmPolicy.ColumnName(projection.Sheet is null ? 1 : projection.StartColumn);
            var budget = new XlsxProjection
            {
                Sheet = projection.Sheet,
                StartRow = projection.StartRow,
                StartColumn = projection.StartColumn,
                MaxRows = projection.MaxRows,
                MaxChars = Math.Max(PlcAlarmPolicy.MinResultChars, projection.MaxChars - Overhead(info)),
            };
            info.Sheets = XlsxCellWorkbook.Project(grid, budget, out var complete);
            info.Complete = complete;
        });
        return info;
    }

    // ================================================================ cell updates

    /// <summary>
    /// Exports the workbook, checks <paramref name="expectedHash"/>, patches the cells, imports the patched workbook and
    /// re-exports it, all inside the exclusive scope; then checks every edit and every other cell. A refusal before the
    /// import changes nothing.
    /// </summary>
    public void UpdateCells(Transfer export, Transfer import, IReadOnlyList<PlcAlarmCellEditInfo>? edits, string? expectedHash, string readMethod,
        PlcAlarmWriteResultInfo result)
    {
        if (edits is null || edits.Count is < 1 or > PlcAlarmPolicy.MaxCellEdits)
            throw new PlcAlarmException(ValidationError, $"cellEdits must hold 1 to {PlcAlarmPolicy.MaxCellEdits} edits.");
        if (!PlcAlarmPolicy.IsWorkbookHash(expectedHash))
            throw new PlcAlarmException(ValidationError, "expectedWorkbookHash must be the workbookHash of a read of the same workbook.");
        WithTemporaryDirectory("cells", directory =>
        {
            using (_exclusive?.Invoke())
            {
                var beforePath = Path.Combine(directory, "before.xlsx");
                RequireSuccess(export(beforePath), "export", warningIsSuccess: false, changedProject: false);
                var original = File.ReadAllBytes(beforePath);
                var before = Grid(original, "exported");
                var hashBefore = XlsxCellWorkbook.Hash(before);
                if (!string.Equals(hashBefore, expectedHash, StringComparison.Ordinal))
                    throw new PlcAlarmException(StateChanged, $"The workbook changed since it was read (now {hashBefore}). Read it again with {readMethod}. Nothing was imported.");

                byte[] patched;
                List<PlcAlarmCellChangeInfo> changes;
                try
                {
                    (patched, changes) = XlsxCellWorkbook.Patch(original, edits);
                }
                catch (PlcAlarmException exception)
                {
                    throw new PlcAlarmException(exception.Category, exception.Message + " Nothing was imported.");
                }

                var patchedPath = Path.Combine(directory, "patched.xlsx");
                File.WriteAllBytes(patchedPath, patched);
                result.WorkbookHashBefore = hashBefore;
                var imported = import(patchedPath);
                Copy(imported, result);
                RequireSuccess(imported, "import", warningIsSuccess: false, changedProject: true);

                var afterPath = Path.Combine(directory, "after.xlsx");
                RequireSuccess(export(afterPath), "verification export", warningIsSuccess: false, changedProject: true);
                var after = ReadGrid(afterPath, "re-exported");
                result.WorkbookHashAfter = XlsxCellWorkbook.Hash(after);
                var mismatches = new List<string>();
                foreach (var change in changes)
                {
                    var observed = XlsxCellWorkbook.Probe(after, change.Sheet, change.Cell);
                    if (!SameCell(change, observed)) mismatches.Add($"{change.Sheet}!{change.Cell} shows {Quote(observed.Value)}");
                    change.After = observed.Value;
                }

                result.Edits = changes;
                var edited = new HashSet<string>(changes.Select(change => change.Sheet + "!" + change.Cell), StringComparer.Ordinal);
                var unrequested = XlsxCellWorkbook.Diff(before, after).Where(difference => !edited.Contains(difference.Sheet + "!" + difference.Cell)).ToList();
                result.UnrequestedChanges = unrequested.Take(MaxListedChanges)
                    .Select(difference => $"{difference.Sheet}!{difference.Cell}: {Quote(difference.Before)} -> {Quote(difference.After)}").ToList();
                if (mismatches.Count > 0)
                    throw new PlcAlarmException(PostconditionFailed,
                        $"The import reported {imported.State}, but the re-exported workbook does not show {mismatches.Count} of {changes.Count} edits "
                        + $"({string.Join("; ", mismatches.Take(10))}{(mismatches.Count > 10 ? "; ..." : string.Empty)}). The project may be partly changed; inspect it with {readMethod}.");
                if (unrequested.Count > 0)
                    throw new PlcAlarmException(PostconditionFailed,
                        $"The import reported {imported.State} and every edit shows, but the re-exported workbook also changed {unrequested.Count} cell(s) no edit requested "
                        + $"({string.Join("; ", result.UnrequestedChanges.Take(10))}{(unrequested.Count > 10 ? "; ..." : string.Empty)}; see result.unrequestedChanges). "
                        + $"The project is changed; inspect it with {readMethod}.");
            }
        });
    }

    // ================================================================ caller files

    /// <summary>Exports to a temporary file next to <paramref name="path"/>, then moves it into place and re-hashes it.</summary>
    public void ExportFile(Transfer export, string? path, bool overwrite, string? requiredExtension, bool warningIsSuccess, PlcAlarmWriteResultInfo result)
    {
        PlcAlarmFiles.PrecheckExport(path, overwrite, requiredExtension);
        Note(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path!)));
        var swept = PlcAlarmFiles.SweepStaleTemporaries(path!, _utcNow());
        if (swept > 0) Warnings.Add($"Removed {swept} temporary export file(s) next to '{path}' that an interrupted earlier export left behind.");
        var temporary = PlcAlarmFiles.TemporaryPath(path!);
        try
        {
            var outcome = export(temporary);
            Copy(outcome, result);
            RequireSuccess(outcome, "export", warningIsSuccess, changedProject: false);
            var (overwritten, bytes, sha256) = PlcAlarmFiles.Commit(temporary, path!, overwrite);
            result.FilePath = path;
            result.Overwritten = overwritten;
            result.Bytes = bytes;
            result.Sha256 = sha256;
            // A link that appeared during the export is reported too.
            Note(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path!)));
        }
        finally
        {
            PlcAlarmFiles.Discard(temporary);
        }
    }

    /// <summary>
    /// Imports a private copy of <paramref name="path"/> and, inside the same exclusive scope, re-exports the same kind with
    /// <paramref name="reexport"/> to check what the import shows (<see cref="PlcAlarmImportCheckInfo"/>). Without a documented
    /// re-export (<paramref name="reexport"/> null) the import is not read back, for <paramref name="notReadBackReason"/>.
    /// </summary>
    public void ImportFile(Transfer import, Transfer? reexport, string? path, string? requiredExtension, bool warningIsSuccess, bool compareBytes,
        string? notReadBackReason, PlcAlarmWriteResultInfo result)
    {
        PlcAlarmFiles.PrecheckImport(path, requiredExtension);
        Note(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path!)));
        result.FilePath = path;
        var check = new PlcAlarmImportCheckInfo { Method = reexport is null ? "none" : compareBytes ? "fileHash" : "rows" };
        WithTemporaryDirectory("import", directory =>
        {
            // The import and its comparison use one private copy, so the caller's file may change meanwhile without effect.
            var copy = Path.Combine(directory, Path.GetFileName(path!));
            File.Copy(path!, copy);
            (result.Bytes, result.Sha256) = PlcAlarmFiles.Hash(copy);
            using (_exclusive?.Invoke())
            {
                var outcome = import(copy);
                Copy(outcome, result);
                RequireSuccess(outcome, "import", warningIsSuccess, changedProject: true);
                if (reexport is null)
                    Decide(check, PlcAlarmPolicy.ImportOutcomes.NotReadBack, $"not read back: {notReadBackReason ?? "no documented export for this data."}");
                else
                    Verify(reexport, copy, Path.Combine(directory, "verify"), compareBytes, warningIsSuccess, check);
            }
        });

        result.ImportCheck = check;
        switch (check.Outcome)
        {
            case PlcAlarmPolicy.ImportOutcomes.NotReadBack:
                Warnings.Add($"The import reported state {result.State} and changed the project; it is {check.Reason}");
                break;
            case PlcAlarmPolicy.ImportOutcomes.Inconclusive:
                Warnings.Add($"The import reported state {result.State} and changed the project, but its verification is inconclusive: {check.Reason}");
                break;
            case PlcAlarmPolicy.ImportOutcomes.Contradicted:
                Warnings.Add($"The import reported state {result.State} and changed the project, but the re-export contradicts it: {check.Reason} Inspect the project.");
                break;
        }
    }

    /// <summary>
    /// The verification export after an import, inside its exclusive scope. It never fails the import: a failed or unreadable
    /// export, an unreadable workbook or a file error is an inconclusive outcome. A delegate's own exception other than
    /// <see cref="PlcAlarmException"/> or a file error passes through (the worker turns recoverable Siemens failures of the
    /// export into an unreadable outcome first; a lost Portal still escapes).
    /// </summary>
    private void Verify(Transfer reexport, string imported, string directory, bool compareBytes, bool warningIsSuccess, PlcAlarmImportCheckInfo check)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var again = Path.Combine(directory, "reexport" + (compareBytes ? Path.GetExtension(imported) : ".xlsx"));
            var outcome = reexport(again);
            var verdict = PlcAlarmPolicy.InterpretState(outcome.State, warningIsSuccess);
            if (verdict is not (PlcAlarmPolicy.StateVerdict.Success or PlcAlarmPolicy.StateVerdict.Warning) || !File.Exists(again))
            {
                Decide(check, PlcAlarmPolicy.ImportOutcomes.Inconclusive,
                    $"the verification export reported {(outcome.State is null ? "an unreadable state" : $"state {outcome.State}")}"
                    + (outcome.Unreadable.Count > 0 ? $" ({string.Join("; ", outcome.Unreadable.Take(3))})" : string.Empty)
                    + (verdict is PlcAlarmPolicy.StateVerdict.Success or PlcAlarmPolicy.StateVerdict.Warning ? " but wrote no file" : string.Empty) + ".");
                return;
            }

            if (compareBytes)
            {
                check.ImportedSha256 = PlcAlarmFiles.Hash(imported).Sha256;
                check.ReexportSha256 = PlcAlarmFiles.Hash(again).Sha256;
                if (check.ImportedSha256 == check.ReexportSha256) Decide(check, PlcAlarmPolicy.ImportOutcomes.Verified, null);
                else Decide(check, PlcAlarmPolicy.ImportOutcomes.Inconclusive,
                    "the re-exported file differs from the imported file byte for byte; its format is not documented and TIA Portal may write an equivalent file differently.");
                return;
            }

            var reexported = XlsxCellWorkbook.Read(File.ReadAllBytes(again));
            check.WorkbookHashAfter = XlsxCellWorkbook.Hash(reexported);
            var comparison = XlsxCellWorkbook.CompareRows(XlsxCellWorkbook.Read(File.ReadAllBytes(imported)), reexported);
            check.ImportedRows = comparison.ImportedRows;
            check.RowsNotShown = comparison.RowsNotShown;
            check.NotShown = comparison.NotShown.ToList();
            var notCompared = comparison.NotCompared.Count == 0 ? string.Empty
                : $" Not compared: {string.Join("; ", comparison.NotCompared.Take(5))}{(comparison.NotCompared.Count > 5 ? "; ..." : string.Empty)}.";
            if (comparison.RowsNotShown > 0)
                Decide(check, PlcAlarmPolicy.ImportOutcomes.Contradicted,
                    $"the re-export does not show {comparison.RowsNotShown} of the imported rows ({string.Join(", ", comparison.NotShown.Take(5))}"
                    + $"{(comparison.RowsNotShown > 5 ? ", ..." : string.Empty)}), compared by header and value: TIA Portal did not keep them as imported (with ImportOptions.None it keeps existing entries).{notCompared}");
            else if (comparison.NotCompared.Count > 0)
                Decide(check, PlcAlarmPolicy.ImportOutcomes.Inconclusive, $"every compared row shows, but the layouts differ.{notCompared}");
            else
                Decide(check, PlcAlarmPolicy.ImportOutcomes.Verified, null);
        }
        catch (Exception exception) when (exception is PlcAlarmException or IOException or UnauthorizedAccessException)
        {
            Decide(check, PlcAlarmPolicy.ImportOutcomes.Inconclusive, $"the verification export could not be compared ({exception.Message}).");
        }
    }

    private static void Decide(PlcAlarmImportCheckInfo check, string outcome, string? reason)
    {
        check.Outcome = outcome;
        check.Verified = outcome == PlcAlarmPolicy.ImportOutcomes.Verified;
        check.Reason = reason;
    }

    /// <summary>
    /// Creates an empty temporary directory next to <paramref name="path"/>, lets <paramref name="export"/> write into it,
    /// then moves it into place and lists every file with its hash (export_prodiag_info).
    /// </summary>
    public void ExportDirectory(Action<string> export, string? path, PlcAlarmWriteResultInfo result)
    {
        PlcAlarmFiles.PrecheckDirectoryExport(path);
        Note(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path!)));
        var swept = PlcAlarmFiles.SweepStaleTemporaryDirectories(path!, _utcNow());
        if (swept > 0) Warnings.Add($"Removed {swept} temporary export director(ies) next to '{path}' that an interrupted earlier export left behind.");
        var temporary = PlcAlarmFiles.TemporaryDirectoryPath(path!);
        Directory.CreateDirectory(temporary);
        try
        {
            export(temporary);
            // An export that wrote nothing creates no directory: the temporary one is removed in finally.
            if (Directory.Exists(temporary) && !Directory.EnumerateFiles(temporary, "*", SearchOption.AllDirectories).Any())
                throw new PlcAlarmException(PostconditionFailed, $"The export returned without error but wrote no file; '{path}' was not created.");
            result.Files = PlcAlarmFiles.CommitDirectory(temporary, path!);
            result.DirectoryPath = path;
        }
        finally
        {
            PlcAlarmFiles.DiscardDirectory(temporary);
        }
    }

    // ================================================================ helpers

    /// <summary>
    /// Fails the step unless the state is success (or warning where the sources name it). An import that reports failure
    /// may already have changed the project; the message says so.
    /// </summary>
    public void RequireSuccess(PlcAlarmResultReader.Outcome outcome, string what, bool warningIsSuccess, bool changedProject)
    {
        var verdict = PlcAlarmPolicy.InterpretState(outcome.State, warningIsSuccess);
        if (verdict == PlcAlarmPolicy.StateVerdict.Warning)
            Warnings.Add($"TIA Portal reported state Warning for the {what} ({outcome.WarningCount ?? 0} warnings).");
        if (verdict is PlcAlarmPolicy.StateVerdict.Success or PlcAlarmPolicy.StateVerdict.Warning) return;
        var state = outcome.State is null ? "an unreadable state" : $"state {outcome.State}";
        throw new PlcAlarmException(WorkerOperationFailed,
            $"TIA Portal reported {state} for the {what} ({outcome.RuntimeType ?? "no result"}"
            + (outcome.ErrorCount is { } errors ? $", {errors} errors" : string.Empty)
            + (outcome.LogFilePath is { } log ? $", log file '{log}'" : string.Empty)
            + (outcome.Messages is { Count: > 0 } messages ? $", first message: {messages[0]}" : string.Empty)
            + (outcome.State is null && outcome.Unreadable.Count > 0 ? $", unreadable: {string.Join("; ", outcome.Unreadable.Take(3))}" : string.Empty) + "). "
            + (changedProject ? "The project may be partly changed; inspect it before retrying." : "Nothing was written to the target."));
    }

    public static void Copy(PlcAlarmResultReader.Outcome outcome, PlcAlarmWriteResultInfo result)
    {
        result.State = outcome.State;
        result.LogFilePath = outcome.LogFilePath;
        result.ErrorCount = outcome.ErrorCount;
        result.WarningCount = outcome.WarningCount;
        result.Messages = outcome.Messages;
    }

    /// <summary>A text cell must read back exactly; a number cell by numeric value (TIA Portal may write 1 for 1.0).</summary>
    public static bool SameCell(PlcAlarmCellChangeInfo change, PlcAlarmCellProbeInfo observed)
    {
        if (change.Kind == PlcAlarmPolicy.CellKinds.Number)
            return observed.Exists && PlcAlarmPolicy.IsNumber(observed.Value) && PlcAlarmPolicy.IsNumber(change.After)
                && double.Parse(observed.Value, System.Globalization.CultureInfo.InvariantCulture) == double.Parse(change.After, System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(observed.Value, change.After, StringComparison.Ordinal);
    }

    /// <summary>Runs <paramref name="body"/> in a fresh temporary directory that is always deleted (stale ones are swept first, once per run).</summary>
    public void WithTemporaryDirectory(string purpose, Action<string> body)
    {
        if (!_swept)
        {
            _swept = true;
            PlcAlarmFiles.SweepStaleDirectories(_temporaryRoot, _utcNow());
        }

        var directory = Path.Combine(_temporaryRoot, $"{PlcAlarmFiles.TemporaryDirectoryPrefix}{purpose}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            PlcAlarmFiles.DiscardDirectory(directory);
        }
    }

    private void Note(IEnumerable<string> notes)
    {
        foreach (var note in notes)
            if (!Warnings.Contains(note)) Warnings.Add(note);
    }

    private static XlsxWorkbookGrid ReadGrid(string path, string what)
    {
        if (!File.Exists(path))
            throw new PlcAlarmException(WorkerOperationFailed, $"TIA Portal reported no error but produced no {what} workbook file.");
        return Grid(File.ReadAllBytes(path), what);
    }

    private static XlsxWorkbookGrid Grid(byte[] bytes, string what)
    {
        try
        {
            return XlsxCellWorkbook.Read(bytes);
        }
        catch (PlcAlarmException exception)
        {
            throw new PlcAlarmException(exception.Category, $"The {what} workbook could not be read: {exception.Message}");
        }
    }

    /// <summary>The characters a read result takes besides its sheets' rows (an upper bound; rows fill the rest of the budget).</summary>
    private static int Overhead(PlcAlarmWorkbookInfo info)
        => 900 + new[] { info.Kind, info.PlcName, info.DeviceName, info.BlockPath, info.ExportOption, info.ExportState, info.ProgrammingLanguage, info.Sheet, info.StartColumn, info.WorkbookHash }
                .Sum(XlsxCellWorkbook.JsonChars)
            + info.Cultures.Sum(culture => XlsxCellWorkbook.JsonChars(culture) + 1)
            + (info.TextListNames ?? new List<string>()).Sum(name => XlsxCellWorkbook.JsonChars(name) + 1)
            + info.Messages.Sum(message => XlsxCellWorkbook.JsonChars(message) + 1)
            + info.Probes.Sum(probe => 100 + XlsxCellWorkbook.JsonChars(probe.Sheet) + XlsxCellWorkbook.JsonChars(probe.Cell) + XlsxCellWorkbook.JsonChars(probe.Value));

    private static string Quote(string? value) => value is null ? "(empty)" : value.Length == 0 ? "nothing (empty)" : "'" + PlcAlarmPolicy.Bound(value, 200) + "'";
}
