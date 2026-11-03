using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// Caller files of the alarm exports and imports, Siemens-free and shared by the worker and its fakes. An export
/// is written by TIA Portal to a temporary file in the target directory and then moved into place; an existing
/// file is replaced only with <c>overwrite:true</c> and never through a link, and the moved file is read back and
/// hashed. A failure deletes the temporary file; temporary files a crashed or timed-out worker left behind are swept
/// on the next export to the same file once they are <see cref="PlcAlarmPolicy.StaleTemporaryAge"/> old. Nothing here
/// reads a file's content beyond its bytes for the hash. The host's path check is lexical
/// (<see cref="PlcAlarmPolicy.CheckFilePath"/>); <see cref="Inspect"/> is what the file system shows.
/// </summary>
public static class PlcAlarmFiles
{
    private const string ValidationError = "validation_error";
    private const string TargetNotFound = "target_not_found";
    private const string WorkerOperationFailed = "worker_operation_failed";
    private const string PostconditionFailed = "postcondition_failed";

    /// <summary>The prefix of the worker's temporary workbook directories under the system temporary directory.</summary>
    public const string TemporaryDirectoryPrefix = "tia-mcp-alarm-";

    /// <summary>At most this many files of a directory export are listed and hashed.</summary>
    public const int MaxExportedFiles = 1000;

    private static readonly Regex TemporaryDirectoryName = new("^" + Regex.Escape(TemporaryDirectoryPrefix) + "[a-z]+-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    /// <summary>Refuses (validation_error, nothing written) a path the export must not write.</summary>
    public static void PrecheckExport(string? path, bool overwrite, string? requiredExtension)
    {
        Check(path, requiredExtension);
        var directory = DirectoryOf(path!);
        if (!Directory.Exists(directory))
            throw new PlcAlarmException(ValidationError, $"The directory of '{path}' does not exist. Nothing was written.");
        if (Directory.Exists(path))
            throw new PlcAlarmException(ValidationError, $"'{path}' is a directory. Nothing was written.");
        if (!File.Exists(path)) return;
        if (!overwrite)
            throw new PlcAlarmException(ValidationError, $"'{path}' already exists; pass overwrite:true to replace it. Nothing was written.");
        RefuseLink(path!);
    }

    /// <summary>Refuses a path the import cannot read (target_not_found when there is no such file).</summary>
    public static void PrecheckImport(string? path, string? requiredExtension)
    {
        Check(path, requiredExtension);
        if (Directory.Exists(path))
            throw new PlcAlarmException(ValidationError, $"'{path}' is a directory. Nothing was imported.");
        if (!File.Exists(path))
            throw new PlcAlarmException(TargetNotFound, $"The file '{path}' does not exist. Nothing was imported.");
    }

    /// <summary>A fresh temporary file name next to <paramref name="path"/> that keeps its extension (<c>.name.GUID.tmp.ext</c>).</summary>
    public static string TemporaryPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(DirectoryOf(path), $".{name}.{Guid.NewGuid():N}.tmp{extension}");
    }

    /// <summary>
    /// Moves the export into place: refuses an existing file without <paramref name="overwrite"/> (also one created
    /// meanwhile) and an existing file that is a link, replaces it otherwise, then reads the result back. Returns whether
    /// a file was replaced, its size and SHA-256.
    /// </summary>
    public static (bool Overwritten, long Bytes, string Sha256) Commit(string temporary, string path, bool overwrite)
    {
        if (!File.Exists(temporary))
            throw new PlcAlarmException(WorkerOperationFailed, $"TIA Portal reported no error but wrote no export file for '{path}'. Nothing was written.");
        var expected = Hash(temporary);
        var existed = File.Exists(path);
        try
        {
            if (existed)
            {
                if (!overwrite)
                    throw new PlcAlarmException(ValidationError, $"'{path}' already exists; pass overwrite:true to replace it. Nothing was written.");
                // Re-checked here: a link created after the precheck is never replaced either.
                RefuseLink(path);
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Discard(temporary);
            var exists = File.Exists(path);
            throw new PlcAlarmException(!overwrite && exists ? ValidationError : WorkerOperationFailed,
                $"The export could not be moved to '{path}': {exception.Message}"
                + (!overwrite && exists ? " It exists; pass overwrite:true to replace it." : string.Empty) + " The temporary file was removed.");
        }
        catch
        {
            Discard(temporary);
            throw;
        }

        var actual = Hash(path);
        if (actual.Sha256 != expected.Sha256 || actual.Bytes != expected.Bytes)
            throw new PlcAlarmException(PostconditionFailed,
                $"'{path}' does not read back the exported bytes (expected SHA-256 {expected.Sha256}, read {actual.Sha256}).");
        return (existed, actual.Bytes, actual.Sha256);
    }

    /// <summary>Deletes a temporary file; a failure is reported on standard error and never masks the call's own outcome.</summary>
    public static void Discard(string? temporary)
    {
        if (temporary is null) return;
        try
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"The temporary file '{temporary}' could not be deleted: {exception.Message}");
        }
    }

    public static (long Bytes, string Sha256) Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(stream);
        return (stream.Length, string.Concat(digest.Select(b => b.ToString("x2", CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// What the file system shows of a caller path: its normalized full path, whether it (and its parent) exists, the drive
    /// type of its root (a mapped network drive is allowed and reported) and every existing ancestor directory that is a
    /// link (reparse point). Unreadable details stay at their defaults.
    /// </summary>
    public static PlcAlarmPathInfo Inspect(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new PlcAlarmPathInfo { FullPath = full, IsDirectory = Directory.Exists(full) };
        info.Exists = info.IsDirectory || File.Exists(full);
        var parent = Path.GetDirectoryName(full.TrimEnd('\\', '/'));
        info.ParentExists = !string.IsNullOrEmpty(parent) && Directory.Exists(parent);
        try
        {
            if (Path.GetPathRoot(full) is { Length: > 0 } root)
            {
                var drive = new DriveInfo(root);
                info.DriveType = drive.DriveType.ToString();
                info.NetworkDrive = drive.DriveType == DriveType.Network;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        if (info.Exists) info.ReparsePoint = IsLink(full);
        for (var directory = parent; !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(directory) && IsLink(directory!)) info.ReparseAncestors.Add(directory!);
        }

        return info;
    }

    /// <summary>The warnings a caller path deserves (network drive, links); empty for a plain local path.</summary>
    public static List<string> Notes(PlcAlarmPathInfo info)
    {
        var notes = new List<string>();
        if (TiaMcpServer.Contracts.Files.LocalFilePaths.IsUncShare(info.FullPath))
            notes.Add($"'{info.FullPath}' is on a network share; the file is read or written there as requested.");
        else if (info.NetworkDrive)
            notes.Add($"'{info.FullPath}' is on a mapped network drive; the file is read or written over the network share the drive maps.");
        foreach (var link in info.ReparseAncestors)
            notes.Add($"'{link}' is a link (reparse point); '{info.FullPath}' is read or written where it points.");
        return notes;
    }

    /// <summary>
    /// Deletes <c>.name.GUID.tmp.ext</c> files of the export to <paramref name="path"/> (exactly that pattern, this server's
    /// GUID format) that are at least <see cref="PlcAlarmPolicy.StaleTemporaryAge"/> old: leftovers of a worker that crashed
    /// or timed out during an export. Links are never followed. Returns how many were deleted; failures go to standard error.
    /// </summary>
    public static int SweepStaleTemporaries(string path, DateTime utcNow)
    {
        var directory = DirectoryOf(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        // The name and extension compare case-insensitively (Windows file names); the GUID only in this server's own "N" format.
        var pattern = new Regex("^\\.(?i:" + Regex.Escape(name) + ")\\.[0-9a-f]{32}\\.tmp(?i:" + Regex.Escape(extension) + ")$", RegexOptions.CultureInvariant);
        var deleted = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, ".*.tmp*"))
            {
                if (!pattern.IsMatch(Path.GetFileName(file)) || IsLink(file) || utcNow - File.GetLastWriteTimeUtc(file) < PlcAlarmPolicy.StaleTemporaryAge) continue;
                File.Delete(file);
                deleted++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Stale temporary exports in '{directory}' could not be removed: {exception.Message}");
        }

        return deleted;
    }

    /// <summary>
    /// Deletes this server's temporary workbook directories (<c>tia-mcp-alarm-purpose-GUID</c>, exactly that pattern) under
    /// <paramref name="root"/> that are at least <see cref="PlcAlarmPolicy.StaleTemporaryAge"/> old. Returns how many were deleted.
    /// </summary>
    public static int SweepStaleDirectories(string root, DateTime utcNow)
    {
        var deleted = 0;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, TemporaryDirectoryPrefix + "*"))
            {
                if (!TemporaryDirectoryName.IsMatch(Path.GetFileName(directory)) || IsLink(directory)
                    || utcNow - Directory.GetLastWriteTimeUtc(directory) < PlcAlarmPolicy.StaleTemporaryAge) continue;
                if (DiscardDirectory(directory)) deleted++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Stale temporary workbook directories in '{root}' could not be removed: {exception.Message}");
        }

        return deleted;
    }

    // ------------------------------------------------------------------ directory exports (export_prodiag_info)

    /// <summary>
    /// Deletes <c>.name.GUID.tmp</c> directories of the directory export to <paramref name="path"/> (exactly that pattern, the
    /// GUID in this server's lower-case "N" format) that are at least <see cref="PlcAlarmPolicy.StaleTemporaryAge"/> old.
    /// </summary>
    public static int SweepStaleTemporaryDirectories(string path, DateTime utcNow)
    {
        var full = path.TrimEnd('\\', '/');
        var parent = DirectoryOf(full);
        var pattern = new Regex("^\\.(?i:" + Regex.Escape(Path.GetFileName(full)) + ")\\.[0-9a-f]{32}\\.tmp$", RegexOptions.CultureInvariant);
        var deleted = 0;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(parent, ".*.tmp"))
            {
                if (!pattern.IsMatch(Path.GetFileName(directory)) || IsLink(directory) || utcNow - Directory.GetLastWriteTimeUtc(directory) < PlcAlarmPolicy.StaleTemporaryAge) continue;
                if (DiscardDirectory(directory)) deleted++;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Stale temporary export directories in '{parent}' could not be removed: {exception.Message}");
        }

        return deleted;
    }

    /// <summary>
    /// Refuses (validation_error, nothing written) a directory export target: it must be a lexically valid local path
    /// whose parent exists and that does not exist yet. A directory export never merges into or replaces a directory,
    /// because the exported file names are not documented.
    /// </summary>
    public static void PrecheckDirectoryExport(string? path)
    {
        if (PlcAlarmPolicy.CheckDirectoryPath(path) is { } refusal) throw new PlcAlarmException(ValidationError, refusal);
        var full = path!.TrimEnd('\\', '/');
        if (!Directory.Exists(DirectoryOf(full)))
            throw new PlcAlarmException(ValidationError, $"The parent directory of '{path}' does not exist. Nothing was written.");
        if (Directory.Exists(full) || File.Exists(full))
            throw new PlcAlarmException(ValidationError,
                $"'{path}' already exists; a directory export creates a new directory and never merges into or replaces one (the exported file names are not documented). Choose a new directory. Nothing was written.");
    }

    /// <summary>A fresh temporary directory name next to <paramref name="path"/> (<c>.name.GUID.tmp</c>).</summary>
    public static string TemporaryDirectoryPath(string path)
    {
        var full = path.TrimEnd('\\', '/');
        return Path.Combine(DirectoryOf(full), $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
    }

    /// <summary>
    /// Moves the exported directory into place (refused when the target appeared meanwhile) and returns every file in it,
    /// relative names with '/' and ordinal order, with size and SHA-256 as read back after the move.
    /// </summary>
    public static List<PlcAlarmExportedFileInfo> CommitDirectory(string temporary, string path)
    {
        var target = path.TrimEnd('\\', '/');
        if (!Directory.Exists(temporary))
            throw new PlcAlarmException(WorkerOperationFailed, $"TIA Portal reported no error but created no export directory for '{path}'. Nothing was written.");
        var expected = HashTree(temporary);
        if (Directory.Exists(target) || File.Exists(target))
        {
            DiscardDirectory(temporary);
            throw new PlcAlarmException(ValidationError, $"'{path}' was created by someone else during the export; it is not replaced. The temporary directory was removed.");
        }

        try
        {
            Directory.Move(temporary, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiscardDirectory(temporary);
            throw new PlcAlarmException(WorkerOperationFailed, $"The export could not be moved to '{path}': {exception.Message} The temporary directory was removed.");
        }

        var actual = HashTree(target);
        if (actual.Count != expected.Count || actual.Zip(expected, (a, e) => a.Name == e.Name && a.Sha256 == e.Sha256 && a.Bytes == e.Bytes).Any(same => !same))
            throw new PlcAlarmException(PostconditionFailed, $"'{path}' does not read back the exported files.");
        return actual;
    }

    /// <summary>Every file below <paramref name="directory"/> (at most <see cref="MaxExportedFiles"/>), relative and hashed.</summary>
    public static List<PlcAlarmExportedFileInfo> HashTree(string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd('\\', '/');
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => (Full: file, Name: file.Substring(root.Length + 1).Replace('\\', '/')))
            .OrderBy(file => file.Name, StringComparer.Ordinal).Take(MaxExportedFiles + 1).ToList();
        if (files.Count > MaxExportedFiles)
            throw new PlcAlarmException(WorkerOperationFailed, $"The export wrote more than {MaxExportedFiles} files into '{directory}'; they are not listed.");
        return files.Select(file =>
        {
            var (bytes, sha256) = Hash(file.Full);
            return new PlcAlarmExportedFileInfo { Name = file.Name, Bytes = bytes, Sha256 = sha256 };
        }).ToList();
    }

    /// <summary>Deletes a temporary directory; false (reported on standard error) when it could not be deleted.</summary>
    public static bool DiscardDirectory(string? temporary)
    {
        if (temporary is null || !Directory.Exists(temporary)) return true;
        try
        {
            Directory.Delete(temporary, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"The temporary directory '{temporary}' could not be deleted: {exception.Message}");
            return false;
        }
    }

    private static void RefuseLink(string path)
    {
        if (IsLink(path))
            throw new PlcAlarmException(ValidationError, $"'{path}' is a link (reparse point); it is not replaced. Nothing was written.");
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void Check(string? path, string? requiredExtension)
    {
        if (PlcAlarmPolicy.CheckFilePath(path, requiredExtension) is { } refusal)
            throw new PlcAlarmException(ValidationError, refusal);
    }

    private static string DirectoryOf(string path)
        => Path.GetDirectoryName(path) is { Length: > 0 } directory ? directory
            : throw new PlcAlarmException(ValidationError, $"'{path}' names no directory.");
}
