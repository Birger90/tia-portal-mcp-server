using System.Globalization;
using System.Runtime.InteropServices;

namespace TiaMcpServer.Contracts.Files;

/// <summary>
/// The lexical rules of a caller-supplied local file or directory path, shared by every domain that writes or reads a
/// caller file (the PLC alarm exports and imports, <c>export_io_map</c>). The check is lexical only: it never touches
/// the file system, so it does not see mapped network drives, links or reparse points (the file operations report
/// those). Accepted roots: a drive (<c>C:\</c>) and, since 2026-11-01 (owner decision), a UNC share
/// (<c>\\server\share\...</c> or <c>//server/share/...</c>). Refused: Win32 device and extended paths with either
/// separator (<c>\\?\</c>, <c>\\.\</c>, <c>\??\</c>; see <see cref="DevicePathReason"/>), relative paths, <c>.</c> and <c>..</c> segments, empty
/// segments, alternate data streams (a <c>:</c> after the drive), reserved device names in any segment (CON, PRN,
/// AUX, NUL, COM0-9, LPT0-9 with their superscript-digit forms, CONIN$, CONOUT$; with or without an extension), a
/// segment that ends in a space or a dot, wildcards, invalid path characters and control, format (bidi) or
/// separator characters. Mapped network drives are deliberately not refused.
/// </summary>
public static class LocalFilePaths
{
    public const int MaxPathChars = 1024;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00b9", "COM\u00b2", "COM\u00b3",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00b9", "LPT\u00b2", "LPT\u00b3",
    };

    private static readonly char[] Wildcards = { '<', '>', '"', '|', '?', '*' };

    /// <summary>
    /// Why device paths stay refused: they are the one path form whose checks cannot hold. Win32 passes
    /// <c>\\?\</c> paths to the file system without normalization (no <c>.</c>/<c>..</c> resolution, no removal of
    /// trailing dots and blanks, no reserved-name handling), and <c>\\.\</c> and <c>\??\</c> name devices and NT
    /// objects rather than files.
    /// </summary>
    public const string DevicePathReason =
        "Win32 device and extended paths (\\\\?\\, \\\\.\\, \\??\\) are refused: they bypass Win32 path normalization (no '.'/'..' resolution, no trailing dot or blank removal, no reserved-name handling) or name devices instead of files, so the path checks could not hold. Use the drive or UNC form (\\\\server\\share\\...).";

    /// <summary>
    /// Two leading separators of either kind (a UNC share, <c>\\?\</c>, <c>\\.\</c>) or a separator followed by
    /// <c>?</c> (the NT object path <c>\??\</c>): every path that does not start at a drive root.
    /// </summary>
    public static bool IsUncOrDevicePath(string path)
        => path.Length >= 2 && path[0] is '\\' or '/' && path[1] is '\\' or '/' or '?';

    /// <summary>
    /// A Win32 device or extended path with either separator: <c>\\?\</c>, <c>\\.\</c> (also <c>\\?</c> or
    /// <c>\\.</c> alone) and the NT object path <c>\??\</c>.
    /// </summary>
    public static bool IsDevicePath(string path)
        => path.Length >= 2 && path[0] is '\\' or '/'
            && (path[1] == '?'
                || path[1] is '\\' or '/' && path.Length >= 3 && path[2] is '?' or '.' && (path.Length == 3 || path[3] is '\\' or '/'));

    /// <summary>A UNC share path: two leading separators, then a non-empty server and share name (<c>\\server\share[\...]</c>).</summary>
    public static bool IsUncShare(string path)
    {
        if (path.Length < 5 || !(path[0] is '\\' or '/') || !(path[1] is '\\' or '/') || IsDevicePath(path)) return false;
        var parts = path.Substring(2).Split('\\', '/');
        return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }

    /// <summary><c>X:\</c> or <c>X:/</c>.</summary>
    public static bool IsDriveRooted(string path)
        => path.Length >= 3 && (path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') && path[1] == ':' && path[2] is '\\' or '/';

    /// <summary>
    /// A drive-rooted path or a UNC share (allowed since 2026-11-01); elsewhere than on Windows (where only the tests
    /// run) also a path rooted at <c>/</c>.
    /// </summary>
    public static bool IsAbsoluteLocal(string path)
        => IsDriveRooted(path) || IsUncShare(path)
            || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && path.Length > 0 && path[0] == '/' && !IsUncOrDevicePath(path);

    /// <summary>A reserved Win32 device name, with or without an extension (<c>NUL</c>, <c>nul.txt</c>, <c>COM1 .xlsx</c>).</summary>
    public static bool IsReservedDeviceName(string segment)
    {
        var dot = segment.IndexOf('.');
        var stem = (dot >= 0 ? segment.Substring(0, dot) : segment).TrimEnd(' ');
        return ReservedNames.Contains(stem);
    }

    /// <summary>
    /// A file path: null when acceptable, otherwise the reason (<paramref name="field"/> names the argument). With
    /// <paramref name="requiredExtension"/> the file name must end in it and name something before it.
    /// </summary>
    public static string? CheckFile(string? path, string field, string? requiredExtension)
    {
        if (CheckRoot(path, field) is { } root) return root;
        if (path![path.Length - 1] is '\\' or '/') return $"{field} must name a file, not a directory.";
        if ((CheckCharacters(path, field) ?? CheckSegments(path, field)) is { } invalid) return invalid;
        var name = Segments(path).Last();
        if (requiredExtension is not null && !name.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            return $"{field} must end in {requiredExtension}.";
        if (requiredExtension is not null && name.Length <= requiredExtension.Length)
            return $"{field} must name a file before {requiredExtension}.";
        return null;
    }

    /// <summary>A directory path (a trailing separator is accepted): null when acceptable, otherwise the reason.</summary>
    public static string? CheckDirectory(string? path, string field)
    {
        if (CheckRoot(path, field) is { } root) return root;
        var trimmed = path!.TrimEnd('\\', '/');
        if (trimmed.Length == 0 || IsDriveRooted(path) && trimmed.Length <= 2)
            return $"{field} must name a directory below a drive root or a UNC share, for example C:\\Exports\\ProDiag or \\\\server\\share\\ProDiag.";
        return CheckCharacters(trimmed, field) ?? CheckSegments(trimmed, field);
    }

    /// <summary>Control, format (bidi), separator, invalid path and wildcard characters; null when none is present.</summary>
    public static string? CheckCharacters(string path, string field)
    {
        if (HasUnsafeCharacters(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(Wildcards) >= 0)
            return $"{field} must not contain control, format or wildcard characters.";
        return null;
    }

    /// <summary>
    /// The segment rules of an absolute path: no <c>:</c> after the drive (alternate data streams), no <c>.</c>,
    /// <c>..</c> or empty segment, no reserved device name and no segment that ends in a space or a dot.
    /// </summary>
    public static string? CheckSegments(string path, string field)
    {
        if (path.Length > 2 && path.IndexOf(':', IsUncShare(path) ? 0 : 2) >= 0) return $"{field} must not contain ':' after the drive (alternate data streams are refused).";
        var segments = Segments(path).ToList();
        if (segments.Any(segment => segment is "." or "..")) return $"{field} must not contain '.' or '..' segments.";
        if (segments.Any(segment => segment.Length == 0)) return $"{field} must not contain empty segments.";
        if (segments.FirstOrDefault(IsReservedDeviceName) is { } reserved)
            return $"{field} must not use the reserved device name '{reserved}' (CON, PRN, AUX, NUL, COM0-9, LPT0-9, CONIN$, CONOUT$) as a segment.";
        if (segments.FirstOrDefault(segment => segment.TrimEnd(' ', '.') != segment) is { } trailing)
            return $"{field} must not contain a segment that ends in a space or a dot ('{trailing}').";
        return null;
    }

    /// <summary>True for control, format (bidi and zero-width), line and paragraph separator characters and unpaired surrogates.</summary>
    public static bool HasUnsafeCharacters(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                if (IsUnsafe(CharUnicodeInfo.GetUnicodeCategory(text, index))) return true;
                index++;
                continue;
            }

            if (char.IsSurrogate(character) || IsUnsafe(CharUnicodeInfo.GetUnicodeCategory(character))) return true;
        }

        return false;
    }

    private static string? CheckRoot(string? path, string field)
    {
        if (string.IsNullOrWhiteSpace(path)) return $"{field} must be an absolute local path.";
        if (path!.Length > MaxPathChars) return $"{field} must be at most {MaxPathChars} characters.";
        if (IsDevicePath(path)) return $"{field}: {DevicePathReason}";
        if (!IsAbsoluteLocal(path)) return $"{field} must be an absolute path on a drive or a UNC share, for example C:\\Exports\\TextLists.xlsx or \\\\server\\share\\TextLists.xlsx.";
        return null;
    }

    /// <summary>
    /// The segments after the root: the drive (<c>C:</c>) or the leading <c>/</c> is not a segment; for a UNC share the
    /// server and share names are (they follow the same naming rules).
    /// </summary>
    private static IEnumerable<string> Segments(string path)
    {
        if (IsUncShare(path)) return path.Substring(2).Split('\\', '/');
        var segments = path.Split('\\', '/');
        return IsDriveRooted(path) || path.StartsWith("/", StringComparison.Ordinal) ? segments.Skip(1) : segments;
    }

    private static bool IsUnsafe(UnicodeCategory category) => category is UnicodeCategory.Control or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
