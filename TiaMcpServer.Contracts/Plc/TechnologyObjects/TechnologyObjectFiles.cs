using System.Globalization;
using System.Security.Cryptography;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// Siemens-free placement of an exported file at a caller path (the pattern of <c>HardwareIoMapExport</c>): the bytes are
/// staged as <c>.&lt;name&gt;.&lt;guid&gt;.tmp</c> next to the target and moved into place, so the target is never
/// partially written, then re-read and compared by SHA-256. A new target is placed with <c>File.Move</c> (which fails
/// when the file appeared meanwhile). An existing target with overwrite is swapped in with <c>File.Replace</c> keeping
/// the previous file as <c>.&lt;name&gt;.&lt;guid&gt;.bak</c> next to it; .NET Framework has no overwriting
/// <c>File.Move</c>, and where <c>ReplaceFile</c> fails (for example on some FAT or SMB volumes; mapped network drives are
/// allowed) the previous file is renamed to the backup name and the staged file moved in. A failure leaves the target as
/// it was, or restores it from the backup whenever moving it back works, and its message states what became of the
/// target, the staged file (the new content) and the backup (the previous file), naming the path of each that still
/// exists; the backup is deleted only after the written file's hash matched. The staged and backup names start with a
/// dot only to sort them next to the target; they are not hidden files.
/// </summary>
public static class TechnologyObjectFiles
{
    /// <summary>A failure of <see cref="Place(byte[], string, bool, string, List{string})"/>: a worker failure category and its message.</summary>
    public sealed class Failure
    {
        public Failure(string category, string message)
        {
            Category = category;
            Message = message;
        }

        public string Category { get; }

        public string Message { get; }
    }

    /// <summary>A staging (<c>.tmp</c>) or backup (<c>.bak</c>) path next to <paramref name="target"/>.</summary>
    public static string SiblingPath(string target, string extension)
        => Path.Combine(Path.GetDirectoryName(target) ?? string.Empty, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.{extension}");

    /// <summary>
    /// The file system calls <see cref="Place(byte[], string, bool, string, List{string}, Operations)"/> makes, replaceable
    /// for tests (the defaults are <c>File.Replace(source, destination, backup, ignoreMetadataErrors: true)</c>,
    /// <c>File.Move</c> and <c>File.Delete</c>).
    /// </summary>
    public sealed class Operations
    {
        /// <summary>The real file system calls (a new instance each time, so no caller can change another's).</summary>
        public static Operations Default => new();

        public Action<string, string, string> Replace { get; set; } = (source, destination, backup) => File.Replace(source, destination, backup, ignoreMetadataErrors: true);

        public Action<string, string> Move { get; set; } = (source, destination) => File.Move(source, destination);

        public Action<string> Delete { get; set; } = path => File.Delete(path);
    }

    /// <inheritdoc cref="Place(byte[], string, bool, string, List{string}, Operations)"/>
    public static Failure? Place(byte[] bytes, string target, bool overwrite, string what, List<string> warnings)
        => Place(bytes, target, overwrite, what, warnings, Operations.Default);

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="target"/> through a staged file and checks the result; null on
    /// success. A target that appeared meanwhile without <paramref name="overwrite"/> is <c>validation_error</c>, another
    /// I/O failure <c>worker_operation_failed</c>, a re-read that differs <c>postcondition_failed</c>. Every failure message
    /// ends with what became of the three files involved: the target (unchanged, restored, missing, or holding unverified
    /// content), the staged file (its path when it is kept, or that it was removed or is gone) and the backup (its path
    /// when it holds the previous file, or that none was made or none is left), so the previous and the new content can
    /// always be found. A backup that cannot be deleted after success is reported in <paramref name="warnings"/>.
    /// </summary>
    public static Failure? Place(byte[] bytes, string target, bool overwrite, string what, List<string> warnings, Operations operations)
    {
        var staging = SiblingPath(target, "tmp");
        try
        {
            using var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var staged = TryDelete(staging, operations, out var leftOver) ? "no staged file is left" : $"a partly written staged file is left at '{staging}' ({leftOver})";
            return new(WorkerFailureCategories.WorkerOperationFailed,
                $"{what} could not be staged next to '{target}': {exception.Message} '{target}' was not changed; {staged}; no backup was made.");
        }

        string? backup = null;
        if (overwrite && File.Exists(target))
        {
            backup = SiblingPath(target, "bak");
            if (ReplaceExisting(staging, target, backup, operations) is { } reason)
                return new(WorkerFailureCategories.WorkerOperationFailed, $"{what} could not replace '{target}': {reason}");
        }
        else
        {
            try
            {
                operations.Move(staging, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var staged = TryDelete(staging, operations, out var leftOver) ? "the staged file was removed" : $"the staged file is left at '{staging}' ({leftOver})";
                return !overwrite && File.Exists(target)
                    ? new(WorkerFailureCategories.ValidationError,
                        $"'{target}' already exists; set overwrite:true to replace it. Nothing was written: '{target}' was not changed; {staged}; no backup was made.")
                    : new(WorkerFailureCategories.WorkerOperationFailed,
                        $"{what} could not be written to '{target}': {exception.Message} Nothing was written: '{target}' was not changed; {staged}; no backup was made.");
            }
        }

        // The staged file has become the target, so none is left; the backup (if any) is deleted only after the hash matched.
        var kept = $"no staged file is left; {(backup is null ? "no backup was made (there was no previous file)" : Backup(backup))}";
        byte[] written;
        try
        {
            written = File.ReadAllBytes(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(WorkerFailureCategories.PostconditionFailed,
                $"{what} was moved to '{target}', but the file cannot be read back: {exception.Message} '{target}' {(File.Exists(target) ? "holds unverified content" : "is missing")}; {kept}.");
        }

        var (expected, actual) = (Sha256(bytes), Sha256(written));
        if (expected != actual)
            return new(WorkerFailureCategories.PostconditionFailed,
                $"'{target}' reads back {written.LongLength} bytes (SHA-256 {actual}), not the {bytes.LongLength} exported bytes (SHA-256 {expected}); "
                + $"another process may have changed it. '{target}' holds that other content; {kept}.");
        if (backup is not null && File.Exists(backup) && !TryDelete(backup, operations, out var deleteError))
            warnings.Add($"The previous file was kept at '{backup}' because it could not be deleted ({deleteError}); delete it when it is no longer needed.");
        return null;
    }

    /// <summary>
    /// Swaps the staged file in over an existing target; null on success (also when the target was removed outside this
    /// call and the staged file could be moved in), otherwise the replace error, what was tried after it and the state of
    /// the target, the staged file and the backup.
    /// </summary>
    private static string? ReplaceExisting(string staging, string target, string backup, Operations operations)
    {
        Exception? replaceError;
        try
        {
            operations.Replace(staging, target, backup);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            replaceError = exception;
        }

        var failed = replaceError.Message;
        // ReplaceFile can fail after renaming the target to the backup name (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2): restore it first.
        if (!File.Exists(target) && File.Exists(backup) && !TryMove(backup, target, operations, out var restoreError))
            return $"{failed} It had renamed '{target}' to the backup name and moving it back failed ({restoreError}): '{target}' is missing; {Staged(staging)}; {Backup(backup)}.";
        if (!File.Exists(staging))
            return $"{failed} The staged file is gone (removed outside this call): '{target}' {(File.Exists(target) ? "was not changed by this call" : "is missing (removed outside this call)")}; "
                + $"{Staged(staging)}; {Backup(backup)}.";
        if (!File.Exists(target))
        {
            // Removed outside this call (no backup holds it): place the new content as requested, or report where it is.
            if (TryMove(staging, target, operations, out var lateError)) return null;
            return $"{failed} '{target}' was removed outside this call and moving the new content in failed too ({lateError}): '{target}' is missing; {Staged(staging)}; {Backup(backup)}.";
        }

        // Volumes without ReplaceFile support: rename the previous file aside, move the new one in, restore on failure.
        if (!TryMove(target, backup, operations, out var asideError))
            return $"{failed} Renaming it aside failed too ({asideError}): '{target}' "
                + (File.Exists(target) ? "was not changed" : "is missing (removed outside this call)") + $"; {Staged(staging)}; {Backup(backup)}.";
        if (TryMove(staging, target, operations, out var moveError)) return null;
        return TryMove(backup, target, operations, out var backError)
            ? $"{failed} After renaming it aside, moving the new file in failed too ({moveError}): '{target}' was restored to the previous file; {Staged(staging)}; {Backup(backup)}."
            : $"{failed} After renaming it aside, moving the new file in failed too ({moveError}) and moving the previous file back failed ({backError}): "
                + $"'{target}' is missing; {Staged(staging)}; {Backup(backup)}.";
    }

    /// <summary>What became of the staged file: its path while it exists (it holds the new content).</summary>
    private static string Staged(string staging) => File.Exists(staging) ? $"the new content is kept at '{staging}'" : "the staged file is gone";

    /// <summary>What became of the backup: its path while it exists (it holds the previous file).</summary>
    private static string Backup(string backup) => File.Exists(backup) ? $"the previous file is kept at '{backup}'" : "no backup is left";

    /// <summary>Lower-case hex SHA-256.</summary>
    public static string Sha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static bool TryMove(string source, string destination, Operations operations, out string? error)
    {
        try
        {
            operations.Move(source, destination);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryDelete(string path, Operations operations, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(path)) operations.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A left-over staging or backup file carries the target's name and a .tmp/.bak suffix.
            error = exception.Message;
            return false;
        }
    }
}
