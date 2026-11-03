using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Siemens-free rules for the files plc_write reads or writes at caller paths (external source import,
/// export_source_file, the CSV exports and export_blocks), shared by the host and the worker. Absolute drive paths
/// and, since 2026-11-01 (owner decision), UNC shares (<c>\\server\share</c>, <c>//server/share</c>) are accepted;
/// Win32 device or extended paths (<c>\\?\</c>, <c>\\.\</c>) are refused
/// (<see cref="TiaMcpServer.Contracts.Files.LocalFilePaths.DevicePathReason"/>). A file is written through a temporary file in the
/// target's own directory and then moved into place, never over an existing file unless the caller asked for
/// it; the written bytes are re-read and hashed. Every path segment is checked for Windows device names and
/// for names Windows cannot keep (trailing dot or blank, <c>.</c>, <c>..</c>). A mapped network drive is
/// allowed with a warning (<see cref="InspectDirectory"/>), and so is a local directory that resolves through a
/// junction or symbolic link to a network share.
/// </summary>
public static class PlcFileTargets
{
    /// <summary>External source file types the blocks reference documents for <c>CreateFromFile</c>: STL, SCL, DB, UDT.</summary>
    public static IReadOnlyList<string> SourceExtensions { get; } = Array.AsReadOnly(new[] { ".scl", ".awl", ".db", ".udt" });

    /// <summary>The formats the software-unit reference documents for a unit's external sources (no STL).</summary>
    public static IReadOnlyList<string> UnitSourceExtensions { get; } = Array.AsReadOnly(new[] { ".scl", ".db", ".udt" });

    private static readonly char[] WindowsInvalidFileNameCharacters =
        "<>:\"/\\|?*".ToCharArray().Concat(Enumerable.Range(0, 32).Select(code => (char)code)).ToArray();

    /// <summary>
    /// The Windows device names (Win32 file-naming rules): CON, PRN, AUX, NUL, COM0-COM9, LPT0-LPT9, COM and LPT
    /// with a superscript 1, 2 or 3, CONIN$ and CONOUT$. Case-insensitive.
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(
        new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }
            .Concat(Enumerable.Range(0, 10).SelectMany(digit => new[] { "COM" + digit, "LPT" + digit }))
            .Concat(new[] { '\u00B9', '\u00B2', '\u00B3' }.SelectMany(superscript => new[] { "COM" + superscript, "LPT" + superscript })),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when <paramref name="segment"/> names a Windows device: its stem (the text before the first dot, with
    /// trailing blanks removed) is one of the reserved device names. The one rule for every file-name check of
    /// these operations (<see cref="SegmentError"/>, <see cref="SafeFileSegment"/>).
    /// </summary>
    public static bool IsReservedDeviceName(string segment)
    {
        if (segment is null) return false;
        var dot = segment.IndexOf('.');
        var stem = (dot >= 0 ? segment.Substring(0, dot) : segment).TrimEnd(' ');
        return ReservedDeviceNames.Contains(stem);
    }

    /// <summary>Why Windows cannot use <paramref name="segment"/> as a file or directory name, or null.</summary>
    public static string? SegmentError(string segment)
    {
        if (segment == "." || segment == "..") return $"the segment '{segment}' is a relative path step";
        if (segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal))
            return $"the segment '{segment}' ends with a dot or a blank, which Windows removes";
        if (IsReservedDeviceName(segment)) return $"the segment '{segment}' is a Windows device name";
        return null;
    }

    /// <summary>Two leading separators of either kind: a UNC share or a Win32 device/extended path.</summary>
    public static bool IsUncOrDevicePath(string path)
        => path.Length >= 2 && (path[0] == '\\' || path[0] == '/') && (path[1] == '\\' || path[1] == '/');

    /// <summary>A Win32 device or extended path (refused; <see cref="TiaMcpServer.Contracts.Files.LocalFilePaths.DevicePathReason"/>).</summary>
    public static bool IsDevicePath(string path) => TiaMcpServer.Contracts.Files.LocalFilePaths.IsDevicePath(path);

    /// <summary>A UNC share path <c>\\server\share[\...]</c> (accepted).</summary>
    public static bool IsUncShare(string path) => TiaMcpServer.Contracts.Files.LocalFilePaths.IsUncShare(path);

    /// <summary>
    /// A drive-absolute Windows path (<c>C:\...</c> or <c>C:/...</c>), a UNC share (<c>\\server\share\...</c>), or,
    /// on a platform whose separator is <c>/</c> (the Linux test host), a rooted path. Never a device path, never a
    /// drive-relative path (<c>C:x</c>) and never an alternate data stream (a second colon).
    /// </summary>
    public static bool IsAbsoluteLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0 || IsDevicePath(path)) return false;
        if (IsUncShare(path)) return path.IndexOf(':') < 0;
        if (IsUncOrDevicePath(path)) return false;
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
            return path.IndexOf(':', 2) < 0;
        return Path.DirectorySeparatorChar == '/' && path[0] == '/';
    }

    /// <summary>The validation message for a caller path that is not a local absolute path, or null.</summary>
    public static string? PathShapeError(string member, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return $"'{member}' is required.";
        if (IsDevicePath(path!))
            return $"'{member}': {TiaMcpServer.Contracts.Files.LocalFilePaths.DevicePathReason} (received '{path}').";
        if (!IsAbsoluteLocalPath(path!)) return $"'{member}' must be an absolute path such as C:\\Exports\\file or \\\\server\\share\\file (received '{path}').";
        var root = path!.Length >= 3 && path[1] == ':' ? 3 : IsUncShare(path) ? 2 : 1;
        foreach (var segment in path.Substring(root).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            if (SegmentError(segment) is { } error) return $"'{member}' cannot be written on Windows: {error} (received '{path}').";
        return null;
    }

    /// <summary>
    /// The full, normalized form of a caller path for messages, warnings and planning keys: <c>Path.GetFullPath</c>
    /// when the path is rooted on this platform, else the path unchanged (a Windows path on the Linux test host).
    /// </summary>
    public static string Canonical(string path)
    {
        try
        {
            var rooted = Path.DirectorySeparatorChar == '\\' ? IsAbsoluteLocalPath(path) : path.StartsWith("/", StringComparison.Ordinal);
            return rooted ? Path.GetFullPath(path) : path;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return path;
        }
    }

    /// <summary>
    /// The external source name import_external_source uses when the caller names none: the file name with its
    /// extension (the blocks reference addresses sources so, e.g. <c>Find("Block_1.scl")</c>). Both Windows and
    /// forward separators end the directory part, on every platform.
    /// </summary>
    public static string DefaultSourceName(string path)
    {
        var cut = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
        return cut < 0 ? path : path.Substring(cut + 1);
    }

    /// <summary>The extension of <paramref name="path"/> in lower case, including the dot; empty when there is none.</summary>
    public static string Extension(string path) => Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;

    /// <summary>
    /// The checks before a file is written at <paramref name="path"/>: its directory exists, the path is not a
    /// directory, and an existing file is refused unless <paramref name="overwrite"/>. Null when the path is usable.
    /// </summary>
    public static string? WriteTargetError(string path, bool overwrite)
    {
        string? directory;
        try
        {
            directory = Path.GetDirectoryName(path);
        }
        catch (ArgumentException exception)
        {
            return $"'{path}' is not a valid path: {exception.Message}";
        }

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return $"The directory of '{path}' does not exist. No file was written.";
        if (Directory.Exists(path)) return $"'{path}' is a directory. No file was written.";
        if (File.Exists(path) && !overwrite) return $"'{path}' already exists; pass overwrite:true to replace it. No file was written.";
        return null;
    }

    /// <summary>
    /// Where a write into <paramref name="directory"/> really goes. A mapped network drive (<c>DriveType.Network</c>)
    /// is allowed and reported as a warning with the normalized path (owner decision). On Windows the directory's
    /// final path is resolved (<c>GetFinalPathNameByHandle</c>, which follows every junction and symbolic link): a
    /// directory whose final path is a UNC share (<c>\\?\UNC\...</c>) is written to like a UNC path (allowed since
    /// 2026-11-01) and reported; a final path that differs from the normalized one is reported as a warning. On
    /// other platforms an ancestor that is a reparse point (symbolic link) is reported as a warning.
    /// </summary>
    public static PlcFileLocation InspectDirectory(string directory)
    {
        var normalized = Canonical(directory);
        DriveType? driveType = null;
        string? finalPath = null;
        var reparseAncestor = false;
        try
        {
            if (Path.DirectorySeparatorChar == '\\' && Path.GetPathRoot(normalized) is { Length: > 0 } root)
            {
                driveType = new DriveInfo(root).DriveType;
                finalPath = FinalPath(normalized);
            }

            for (var current = new DirectoryInfo(normalized); current is not null && !reparseAncestor; current = current.Parent)
                reparseAncestor = current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            // Unreadable location evidence is reported, never treated as local.
            return new PlcFileLocation(normalized, null, $"The location of '{normalized}' could not be fully inspected ({exception.Message}); the write goes to the path as given.");
        }

        return ClassifyLocation(normalized, driveType, finalPath, reparseAncestor);
    }

    /// <summary>The Siemens-free, platform-free decision of <see cref="InspectDirectory"/> (testable on every host).</summary>
    public static PlcFileLocation ClassifyLocation(string normalized, DriveType? driveType, string? finalPath, bool reparseAncestor)
    {
        var display = finalPath is null ? null
            : finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + finalPath.Substring(@"\\?\UNC\".Length)
            : finalPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? finalPath.Substring(@"\\?\".Length)
            : finalPath;
        var finalIsShare = display is not null && IsUncOrDevicePath(display);
        if (driveType == DriveType.Network)
            return new PlcFileLocation(normalized, null,
                $"'{normalized}' is on a mapped network drive{(display is null ? string.Empty : $" ({display})")}; the file is written there as requested.");
        if (finalIsShare && !IsUncOrDevicePath(normalized))
            return new PlcFileLocation(normalized, null,
                $"'{normalized}' resolves through a junction or symbolic link to the network share '{display}'; the file is written there.");
        if (finalIsShare)
            return new PlcFileLocation(normalized, null, $"'{normalized}' is on the network share '{display}'; the file is written there as requested.");
        if (display is not null && !string.Equals(display.TrimEnd('\\'), normalized.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return new PlcFileLocation(normalized, null, $"'{normalized}' resolves through a junction or symbolic link to '{display}'; the file is written there.");
        if (display is null && reparseAncestor)
            return new PlcFileLocation(normalized, null, $"'{normalized}' passes through a junction or symbolic link whose final target could not be resolved; the file is written there.");
        return new PlcFileLocation(normalized, null, null);
    }

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);

    /// <summary>The Windows final path of an existing directory (<c>\\?\C:\...</c> or <c>\\?\UNC\...</c>), or null when it cannot be resolved.</summary>
    private static string? FinalPath(string directory)
    {
        using var handle = CreateFile(directory, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var builder = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
        if (length == 0) return null;
        if (length > builder.Capacity)
        {
            builder = new StringBuilder((int)length + 1);
            length = GetFinalPathNameByHandle(handle, builder, (uint)builder.Capacity, 0);
            if (length == 0 || length > builder.Capacity) return null;
        }

        return builder.ToString();
    }

    /// <summary>A unique temporary path in the same directory as <paramref name="path"/> (same volume, so the final move is a rename).</summary>
    public static string TemporaryPathFor(string path)
        => Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");

    /// <summary>
    /// Moves <paramref name="temporary"/> to <paramref name="destination"/> without ever losing the previous file. A new
    /// destination is placed with <c>File.Move</c>, which fails when the destination appeared meanwhile (an existing
    /// file is never replaced without the caller's consent). An existing destination with <paramref name="overwrite"/> is
    /// swapped in with <c>File.Replace</c> keeping the previous file as a <c>.bak</c> sibling (<see cref="PlcCommit.Backup"/>);
    /// where ReplaceFile fails (some FAT or SMB volumes; mapped drives are allowed) the previous file is renamed to the
    /// backup name and the temporary file moved in (on Windows with <c>MoveFileExW</c>, <c>MOVEFILE_REPLACE_EXISTING |
    /// MOVEFILE_WRITE_THROUGH</c>, never a copy), restoring the previous file when that fails. The backup stays until the
    /// written file is verified (<see cref="VerifyCommitted(string, string, PlcCommit)"/>).
    /// </summary>
    /// <exception cref="PlcFileCommitException">The swap failed; the message names where the previous file and the new content are. Nothing is deleted.</exception>
    /// <exception cref="IOException">A new destination could not be placed (the destination did not exist; the temporary file is the caller's to delete).</exception>
    public static PlcCommit Commit(string temporary, string destination, bool overwrite)
        => Commit(temporary, destination, overwrite, DefaultReplace, DefaultMoveIn);

    /// <summary><see cref="Commit(string, string, bool)"/> with the replace and move-in steps given (tests inject failures).</summary>
    /// <param name="replace"><c>File.Replace(source, destination, backup)</c>.</param>
    /// <param name="moveIn">Moves a file to a path that does not exist (the fallback's second step).</param>
    public static PlcCommit Commit(string temporary, string destination, bool overwrite, Action<string, string, string> replace, Action<string, string> moveIn)
    {
        if (!overwrite || !File.Exists(destination))
        {
            File.Move(temporary, destination);
            return new PlcCommit(false, null);
        }

        var backup = SiblingPath(destination, "bak");
        Exception replaceError;
        try
        {
            replace(temporary, destination, backup);
            return new PlcCommit(true, File.Exists(backup) ? backup : null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            replaceError = exception;
        }

        // ReplaceFile can fail after renaming the destination to the backup name (ERROR_UNABLE_TO_MOVE_REPLACEMENT_2): restore it first.
        if (!File.Exists(destination) && File.Exists(backup) && !TryMove(backup, destination, File.Move, out _))
            throw new PlcFileCommitException($"{replaceError.Message} The previous file is at '{backup}', the new content at '{temporary}'; '{destination}' is missing.");
        if (!File.Exists(temporary))
            throw new PlcFileCommitException($"{replaceError.Message} The new content is no longer at '{temporary}'; '{destination}' {(File.Exists(destination) ? "exists but its content is not verified" : "is missing")}{(File.Exists(backup) ? $" and a previous copy is kept at '{backup}'" : string.Empty)}.");
        if (!File.Exists(destination))
        {
            // The destination disappeared meanwhile (nothing to keep): a plain move.
            if (TryMove(temporary, destination, moveIn, out var placeError)) return new PlcCommit(false, null);
            throw new PlcFileCommitException($"{replaceError.Message} Moving the new file in failed too ({placeError}); the new content is kept at '{temporary}'.");
        }

        // Volumes without ReplaceFile support: rename the previous file aside, move the new one in, restore on failure.
        if (!TryMove(destination, backup, File.Move, out var asideError))
            throw new PlcFileCommitException($"{replaceError.Message} Renaming it aside failed too ({asideError}); '{destination}' was not changed and the new content is kept at '{temporary}'.");
        if (TryMove(temporary, destination, moveIn, out var moveError)) return new PlcCommit(true, backup);
        throw new PlcFileCommitException(TryMove(backup, destination, File.Move, out _)
            ? $"{replaceError.Message} Moving the new file in failed too ({moveError}); '{destination}' was restored and the new content is kept at '{temporary}'."
            : $"{replaceError.Message} Moving the new file in failed too ({moveError}) and the previous file could not be restored: it is at '{backup}', the new content at '{temporary}'.");
    }

    /// <summary>A sibling path next to <paramref name="target"/> (<c>.name.guid.extension</c>), on the same volume.</summary>
    public static string SiblingPath(string target, string extension)
        => Path.Combine(Path.GetDirectoryName(target) ?? string.Empty, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + "." + extension);

    /// <summary>
    /// File.Replace is not supported here: <c>PlatformNotSupportedException</c>, or an <c>IOException</c> with
    /// ERROR_NOT_SUPPORTED (50), ERROR_INVALID_FUNCTION (1), ERROR_UNABLE_TO_REMOVE_REPLACED (1175),
    /// ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176) or ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 (1177). Informational: the commit
    /// falls back for every replace failure and keeps the previous file either way.
    /// </summary>
    public static bool ReplaceUnsupported(Exception exception)
        => exception is PlatformNotSupportedException
            || exception is IOException io && (io.HResult & 0xFFFF) is 50 or 1 or 1175 or 1176 or 1177 && (io.HResult & 0xFFFF0000) == 0x80070000;

    private static void DefaultReplace(string source, string destination, string backup)
        => File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    private const uint MoveFileReplaceExisting = 0x1;
    private const uint MoveFileWriteThrough = 0x8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    /// <summary>
    /// The fallback's move-in: on Windows <c>MoveFileExW(MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)</c> (a rename
    /// that is flushed before it returns, never a copy: no MOVEFILE_COPY_ALLOWED), elsewhere <c>File.Move</c>.
    /// </summary>
    private static void DefaultMoveIn(string source, string destination)
    {
        if (Path.DirectorySeparatorChar != '\\')
        {
            File.Move(source, destination);
            return;
        }

        if (MoveFileEx(source, destination, MoveFileReplaceExisting | MoveFileWriteThrough)) return;
        var code = Marshal.GetLastWin32Error();
        throw new IOException($"MoveFileEx failed with Win32 error {code}.", unchecked((int)0x80070000) | (code & 0xFFFF));
    }

    private static bool TryMove(string source, string destination, Action<string, string> move, out string? error)
    {
        try
        {
            move(source, destination);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> through a temporary file in the same directory,
    /// then re-reads the file and compares its SHA-256 with the written bytes.
    /// </summary>
    /// <exception cref="IOException">Writing failed before anything was moved (the temporary file was removed).</exception>
    /// <exception cref="UnauthorizedAccessException">Writing was refused before anything was moved (the temporary file was removed).</exception>
    /// <exception cref="PlcFileCommitException">The swap over an existing file failed; the previous file and the new content are kept and named.</exception>
    /// <exception cref="PlcFileNotVerifiedException">The file was moved into place but does not read back the written bytes, or could not be re-read.</exception>
    public static PlcWrittenFile Write(string path, byte[] bytes, bool overwrite)
    {
        var temporary = TemporaryPathFor(path);
        PlcCommit commit;
        try
        {
            File.WriteAllBytes(temporary, bytes);
            commit = Commit(temporary, path, overwrite);
        }
        catch (PlcFileCommitException)
        {
            // The destination may be missing or the previous file aside: nothing is deleted, the message names both.
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw;
        }

        return new PlcWrittenFile(path, commit.Replaced, bytes.LongLength, VerifyCommitted(path, Sha256Hex(bytes), commit));
    }

    /// <summary>
    /// Re-reads a file that is already in place and returns its SHA-256 when it equals <paramref name="expected"/>; then the
    /// commit's backup of the previous file is deleted. A mismatch or an unreadable file is a
    /// <see cref="PlcFileNotVerifiedException"/> that says the file is in place and keeps (and names) the backup: the
    /// write happened, only its verification failed.
    /// </summary>
    public static string VerifyCommitted(string path, string expected, PlcCommit commit)
    {
        string actual;
        try
        {
            actual = Sha256Hex(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PlcFileNotVerifiedException(path, commit, $"it could not be re-read ({exception.Message})");
        }

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new PlcFileNotVerifiedException(path, commit, $"it reads SHA-256 {actual}, the written bytes are {expected}");
        if (commit.Backup is { } backup) TryDelete(backup);
        return actual;
    }

    /// <summary><see cref="VerifyCommitted(string, string, PlcCommit)"/> for a commit without a backup.</summary>
    public static string VerifyCommitted(string path, string expected, bool replaced)
        => VerifyCommitted(path, expected, new PlcCommit(replaced, null));

    /// <summary>Best-effort removal of a temporary file; a failure is reported on stderr, never thrown.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"The temporary file '{path}' could not be deleted: {exception.Message}");
        }
    }

    public static string Sha256Hex(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash) builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>UTF-8 with a byte order mark, the encoding TIA Portal writes SimaticML and external sources in.</summary>
    public static byte[] Utf8WithBom(string text)
    {
        var preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text ?? string.Empty);
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }

    /// <summary>
    /// A Windows-safe file or directory name for one project name segment: characters Windows forbids in a file
    /// name (<c>&lt; &gt; : " / \ | ? *</c> and control characters) become <c>_</c>, trailing dots and blanks
    /// become <c>_</c>, a reserved device name (CON, NUL, COM1, ...) gets a leading <c>_</c>. The second value is
    /// true when the segment changed. The rule is fixed (not the host platform's), so the result is the same on
    /// every machine.
    /// </summary>
    public static (string Name, bool Changed) SafeFileSegment(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (var character in segment)
            builder.Append(Array.IndexOf(WindowsInvalidFileNameCharacters, character) >= 0 ? '_' : character);
        var end = builder.Length;
        while (end > 0 && (builder[end - 1] == '.' || builder[end - 1] == ' ')) builder[--end] = '_';
        var name = builder.Length == 0 ? "_" : builder.ToString();
        if (name == "." || name == "..") name = name.Replace('.', '_');
        if (IsReservedDeviceName(name)) name = "_" + name;
        return (name, !string.Equals(name, segment, StringComparison.Ordinal));
    }
}

/// <summary>Where a write goes (<see cref="PlcFileTargets.InspectDirectory"/>): the refusal, or a warning to report, or neither.</summary>
public sealed class PlcFileLocation
{
    public PlcFileLocation(string normalizedPath, string? refusal, string? warning)
    {
        NormalizedPath = normalizedPath;
        Refusal = refusal;
        Warning = warning;
    }

    public string NormalizedPath { get; }

    public string? Refusal { get; }

    public string? Warning { get; }
}

/// <summary>The outcome of <see cref="PlcFileTargets.Commit(string, string, bool)"/>: whether a file was replaced and where its previous content is kept until verification.</summary>
public sealed class PlcCommit
{
    public PlcCommit(bool replaced, string? backup)
    {
        Replaced = replaced;
        Backup = backup;
    }

    public bool Replaced { get; }

    /// <summary>The previous file, kept next to the destination until the written file is verified; null when nothing was replaced.</summary>
    public string? Backup { get; }
}

/// <summary>A swap over an existing file that failed; nothing was deleted and the message names where the previous file and the new content are.</summary>
public sealed class PlcFileCommitException : IOException
{
    public PlcFileCommitException(string message) : base(message)
    {
    }
}

/// <summary>A file that was moved into place but whose re-read did not confirm the written bytes.</summary>
public sealed class PlcFileNotVerifiedException : Exception
{
    public PlcFileNotVerifiedException(string path, PlcCommit commit, string reason)
        : base($"'{path}' was written but not verified: {reason}. The file at '{path}' is in place"
            + (!commit.Replaced ? "."
                : commit.Backup is { } backup && File.Exists(backup) ? $"; it replaced the previous file, which is kept at '{backup}'."
                : " and replaced the previous file, which no longer exists."))
    {
        Path = path;
        Replaced = commit.Replaced;
    }

    public string Path { get; }

    public bool Replaced { get; }
}

/// <summary>One file written by <see cref="PlcFileTargets.Write"/>.</summary>
public sealed class PlcWrittenFile
{
    public PlcWrittenFile(string path, bool replaced, long bytes, string sha256)
    {
        Path = path;
        Replaced = replaced;
        Bytes = bytes;
        Sha256 = sha256;
    }

    public string Path { get; }

    public bool Replaced { get; }

    public long Bytes { get; }

    public string Sha256 { get; }
}

/// <summary>
/// A block group path (<c>PLC/Blocks/A/B</c>, <c>PLC/Units/Unit/Blocks/A</c>) or a PLC data type group path
/// (<c>PLC/Types/A</c>, <c>PLC/Units/Unit/Types/A</c>) in the plc_read path conventions. Zero folders names the
/// scope's root (system) group. Segments are trimmed; empty segments are refused.
/// </summary>
public sealed class PlcGroupPath
{
    public const string BlocksKind = "Blocks";
    public const string TypesKind = "Types";

    private PlcGroupPath(string plcName, string? unitName, string kind, IReadOnlyList<string> folders)
    {
        PlcName = plcName;
        UnitName = unitName;
        Kind = kind;
        Folders = folders;
    }

    public string PlcName { get; }

    public string? UnitName { get; }

    /// <summary><see cref="BlocksKind"/> or <see cref="TypesKind"/>.</summary>
    public string Kind { get; }

    public IReadOnlyList<string> Folders { get; }

    public bool IsRoot => Folders.Count == 0;

    public static bool TryParse(string? path, out PlcGroupPath? group, out string? error)
    {
        group = null;
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "A group path is required.";
            return false;
        }

        var segments = path!.Split('/').Select(segment => segment.Trim()).ToList();
        if (segments.Any(segment => segment.Length == 0))
        {
            error = $"Group path '{path}' contains an empty segment.";
            return false;
        }

        var index = 1;
        string? unit = null;
        if (segments.Count >= 3 && string.Equals(segments[1], "Units", StringComparison.OrdinalIgnoreCase))
        {
            unit = segments[2];
            index = 3;
        }

        if (segments.Count <= index || !(string.Equals(segments[index], BlocksKind, StringComparison.OrdinalIgnoreCase)
                || string.Equals(segments[index], TypesKind, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Group path '{path}' must be 'PLC/Blocks/...', 'PLC/Types/...', 'PLC/Units/Unit/Blocks/...' or 'PLC/Units/Unit/Types/...'.";
            return false;
        }

        var kind = string.Equals(segments[index], BlocksKind, StringComparison.OrdinalIgnoreCase) ? BlocksKind : TypesKind;
        group = new PlcGroupPath(segments[0], unit, kind, segments.Skip(index + 1).ToList().AsReadOnly());
        return true;
    }

    /// <summary>The canonical path: <c>PLC[/Units/Unit]/Blocks|Types[/folders]</c>.</summary>
    public string ToDisplayPath()
    {
        var segments = new List<string> { PlcName };
        if (UnitName is not null)
        {
            segments.Add("Units");
            segments.Add(UnitName);
        }

        segments.Add(Kind);
        segments.AddRange(Folders);
        return string.Join("/", segments);
    }

    /// <summary>This path with its last folder renamed; the root has no name to change.</summary>
    public string RenamedDisplayPath(string newName)
    {
        var display = ToDisplayPath();
        return IsRoot ? display : display.Substring(0, display.LastIndexOf('/') + 1) + newName;
    }
}
