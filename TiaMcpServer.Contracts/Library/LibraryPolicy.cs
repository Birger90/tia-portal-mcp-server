namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// Siemens-free rules shared by the host catalogs, the worker and the FakeWorker: selectors, paths, the exact
/// Siemens enum member names the sources document, target kinds and caps.
/// </summary>
public static class LibraryPolicy
{
    /// <summary>
    /// The words that make a TIA Portal setting security-relevant (issue #27, extended by the security review 2026-11-03, F2):
    /// its folder path or name contains one of them (case-insensitive). The documented API names no setting, so the
    /// classification is by the names the worker resolved in planning (never the caller's text), and such a setting fires the
    /// acknowledge guard <c>library_portal_security_setting</c> on top of the confirmation every set_portal_setting asks in
    /// read-write, whatever the direction of the change. The stems cover their word families (<c>certif</c>: certificate,
    /// <c>encrypt</c>: encryption, <c>communicat</c>: communication, <c>verif</c>: verification).
    /// </summary>
    public static IReadOnlyList<string> SecuritySettingWords { get; } = new[]
    {
        "secur", "certif", "communicat", "encrypt", "password", "trust", "tls", "ssl", "legacy", "authent", "protect", "umac", "signing", "signature",
        "accept", "allow", "verif", "cipher", "access", "credential", "openness", "firewall",
    };

    public static bool IsSecurityRelevantSetting(string? folderPath, string? name)
        => SecuritySettingWords.Any(word => (folderPath ?? string.Empty).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
            || (name ?? string.Empty).IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>
    /// The classification of a setting from the folder and setting names TIA Portal resolved (<c>Name</c> of each folder found
    /// and of the setting); a name that could not be read makes it security-relevant (fail safe).
    /// </summary>
    public static bool IsSecurityRelevantResolved(string? resolvedFolderPath, string? resolvedName, bool anyNameUnreadable)
        => anyNameUnreadable || resolvedName is null || IsSecurityRelevantSetting(resolvedFolderPath, resolvedName);

    /// <summary>
    /// The refusal of a set_portal_setting mutation whose setting, resolved again by the worker, is classified differently from
    /// the plan the human confirmed (null planned: no plan was sent, which counts as a difference); null when they agree.
    /// </summary>
    public static string? PortalSettingClassificationMismatch(bool? planned, bool resolved, string target)
        => planned == resolved ? null
            : $"set_portal_setting: {target} is {(resolved ? "security-relevant" : "not security-relevant")} as resolved now, but was planned as "
              + $"{(planned is null ? "unclassified" : planned.Value ? "security-relevant" : "not security-relevant")}; the setting changed between planning and the change. Nothing was changed; plan the call again.";

    /// <summary>
    /// Whether an open project may become a binding candidate by its <c>ProjectBase.IsPrimary</c> read (issue #20, security review
    /// U3): only a project that reads <c>IsPrimary == true</c>. A secondary project (false) and a project whose IsPrimary is
    /// unreadable or absent (null) are never bound, so a secondary project cannot become the target of content writes.
    /// </summary>
    public static bool IsBindingCandidate(bool? isPrimary) => isPrimary == true;

    /// <summary>The selector of the bound project's library.</summary>
    public const string ProjectLibrary = "project";

    public const int MaxNameLength = 256;
    public const int MaxPathLength = 1024;
    public const int MaxTextLength = 1000;

    public const int DefaultTreeDepth = 16;
    public const int MaxTreeDepth = 32;
    public const int DefaultPageLimit = 200;
    public const int MaxPageLimit = 500;

    /// <summary>The walk stops after this many collected entries (folders, types, master copies).</summary>
    public const int MaxTreeEntries = 5000;

    public const int MaxInstances = 1000;
    public const int MaxMessageDepth = 8;
    public const int MaxMessages = 500;
    public const int MaxReferences = 200;
    public const int DefaultCompareDepth = 16;
    public const int MaxCompareDepth = 64;
    public const int DefaultCompareElements = 300;
    public const int MaxCompareElements = 5000;
    public const int MaxAttributes = 20;

    public static class TreeContents
    {
        public const string All = "all";
        public const string Types = "types";
        public const string MasterCopies = "masterCopies";
        public static IReadOnlyList<string> Values { get; } = new[] { All, Types, MasterCopies };
    }

    public static class EntryKinds
    {
        public const string TypeFolder = "typeFolder";
        public const string MasterCopyFolder = "masterCopyFolder";
        public const string Type = "type";
        public const string MasterCopy = "masterCopy";
    }

    public static class LibraryKinds
    {
        public const string Project = "project";
        public const string User = "user";
        public const string System = "system";
        public const string Corporate = "corporate";
        public const string Global = "global";
    }

    public static class TypeKinds
    {
        public const string CodeBlock = "codeBlock";
        public const string PlcType = "plcType";
        public const string Other = "other";
    }

    /// <summary>create_master_copy sources.</summary>
    public static class SourceKinds
    {
        public const string Block = "block";
        public const string PlcType = "plcType";
        public const string TagTable = "tagTable";
        public const string Device = "device";

        /// <summary>A PLC user text list (official skill: <c>MasterCopies.Create(alarmTextList)</c>); path is its name.</summary>
        public const string TextList = "textList";
        public static IReadOnlyList<string> Values { get; } = new[] { Block, PlcType, TagTable, Device, TextList };

        /// <summary>Kinds whose path does not carry the PLC, so source.plcName selects it.</summary>
        public static bool TakesPlcName(string? kind) => kind is TagTable or TextList;
    }

    /// <summary>instantiate_master_copy / instantiate_type_version targets (the composition whose CreateFrom is called).</summary>
    public static class TargetKinds
    {
        public const string BlockGroup = "blockGroup";
        public const string TypeGroup = "typeGroup";
        public const string TagTableGroup = "tagTableGroup";
        public const string TechnologyObjects = "technologyObjects";
        public const string SoftwareUnits = "softwareUnits";

        /// <summary>The PLC's external sources: <c>PlcExternalSourceComposition.CreateFrom(MasterCopy, MasterCopyMode)</c> (external-sources.md).</summary>
        public const string ExternalSources = "externalSources";
        public const string Devices = "devices";
        public const string DeviceItem = "deviceItem";

        /// <summary>The PLC's user text lists (<c>PlcAlarmUserTextlistComposition.CreateFrom(MasterCopy, MasterCopyMode)</c>).</summary>
        public const string TextLists = "textLists";

        public static IReadOnlyList<string> MasterCopyTargets { get; } = new[] { BlockGroup, TypeGroup, TagTableGroup, TechnologyObjects, SoftwareUnits, ExternalSources, Devices, DeviceItem, TextLists };

        public static IReadOnlyList<string> TypeVersionTargets { get; } = new[] { BlockGroup, TypeGroup };

        /// <summary>Targets whose documented CreateFrom takes a MasterCopyMode (G: blocks, types, tag tables, TOs, SW units; external-sources.md: external sources; text lists); devices/device items take none.</summary>
        public static bool TakesMasterCopyMode(string? kind) => kind is BlockGroup or TypeGroup or TagTableGroup or TechnologyObjects or SoftwareUnits or ExternalSources or TextLists;

        public static bool NeedsPlc(string? kind) => kind is BlockGroup or TypeGroup or TagTableGroup or TechnologyObjects or SoftwareUnits or ExternalSources or TextLists;

        public static bool HasFolder(string? kind) => kind is BlockGroup or TypeGroup or TagTableGroup;
    }

    /// <summary><c>Siemens.Engineering.OpenMode</c> member names the sources name.</summary>
    public static IReadOnlyList<string> OpenModes { get; } = new[] { "ReadOnly", "ReadWrite" };

    public const string DefaultOpenMode = "ReadOnly";

    /// <summary><c>MasterCopyMode</c> member names (L: Rename, Replace, ThrowIfExists).</summary>
    public static IReadOnlyList<string> MasterCopyModes { get; } = new[] { "ThrowIfExists", "Rename", "Replace" };

    public const string DefaultMasterCopyMode = "ThrowIfExists";

    /// <summary><c>UpdatePathsMode</c> member names (L).</summary>
    public static IReadOnlyList<string> UpdatePathsModes { get; } = new[] { "ThrowIfPathsConflict", "KeepExistingPathsInTarget", "UpdatePathsInTarget" };

    public const string DefaultUpdatePathsMode = "ThrowIfPathsConflict";

    /// <summary><c>CreateOrReleaseDependenciesMode</c> member names (V21 API description).</summary>
    public static IReadOnlyList<string> DependenciesModes { get; } = new[] { "DoNotAutomaticallyCreateOrReleaseDependencies", "AutomaticallyCreateOrReleaseDependenciesIfRequired" };

    public const string DefaultDependenciesMode = "DoNotAutomaticallyCreateOrReleaseDependencies";

    /// <summary><c>UpdateCheckMode</c> member names (V21 API description).</summary>
    public static IReadOnlyList<string> UpdateCheckModes { get; } = new[] { "ReportOutOfDateOnly", "ReportOutOfDateAndUpToDate" };

    public const string DefaultUpdateCheckMode = "ReportOutOfDateOnly";

    // Enum member names from the V21 API description (Siemens IntelliSense XML, Siemens.Engineering.Base.xml, issue #17).

    /// <summary><c>Siemens.Engineering.Library.LibraryArchivationMode</c>.</summary>
    public static IReadOnlyList<string> ArchivationModes { get; } = new[] { "None", "Compressed", "DiscardRestorableData", "DiscardRestorableDataAndCompressed" };

    public const string DefaultArchivationMode = "Compressed";

    /// <summary><c>Siemens.Engineering.Library.Types.CleanUpMode</c> (project library only).</summary>
    public static IReadOnlyList<string> CleanUpModes { get; } = new[] { "DeleteUnusedTypes", "PreserveDefaultVersionOfUnusedTypes" };

    public const string DefaultCleanUpMode = "PreserveDefaultVersionOfUnusedTypes";

    /// <summary><c>Siemens.Engineering.Library.Types.HarmonizeProjectOptions</c> without <c>None</c> ("Not supposed to be used. Throws exception if used.").</summary>
    public static IReadOnlyList<string> HarmonizeOptions { get; } = new[] { "HarmonizePaths", "HarmonizeNames" };

    /// <summary><c>Siemens.Engineering.Library.Types.ForceUpdateMode</c>.</summary>
    public static IReadOnlyList<string> ForceUpdateModes { get; } = new[] { "ForceSetAnyUpdatedVersionAsDefault", "NoDefaultVersionChange", "SetOnlyHigherUpdatedVersionAsDefault" };

    /// <summary><c>Siemens.Engineering.Library.Types.DeleteUnusedVersionsMode</c>.</summary>
    public static IReadOnlyList<string> DeleteUnusedVersionsModes { get; } = new[] { "AutomaticallyDelete", "DoNotDelete" };

    /// <summary><c>Siemens.Engineering.Library.Types.StructureConflictResolutionMode</c>.</summary>
    public static IReadOnlyList<string> StructureConflictResolutionModes { get; } = new[] { "CancelIfStructureConflicts", "RetainStructure", "UpdateStructure" };

    /// <summary><c>Siemens.Engineering.Library.LibraryImportOptions</c>.</summary>
    public static IReadOnlyList<string> LibraryImportOptions { get; } = new[] { "None", "ActivateInactiveCultures", "SkipInactiveCultures" };

    public const string DefaultLibraryImportOptions = "None";

    /// <summary><c>Siemens.Engineering.Library.LibraryExportOptions</c>.</summary>
    public static IReadOnlyList<string> LibraryExportOptions { get; } = new[] { "None", "OnlyLibraryVersionInfoFile", "WithLibraryVersionInfoFile" };

    public const string DefaultLibraryExportOptions = "None";

    /// <summary><c>Siemens.Engineering.Library.Types.CreateOptions</c>.</summary>
    public static IReadOnlyList<string> CreateOptions { get; } = new[] { "None", "Override" };

    public const string DefaultCreateOption = "None";

    /// <summary><c>Siemens.Engineering.ExportOptions</c>.</summary>
    public static IReadOnlyList<string> ExportOptions { get; } = new[] { "None", "WithDefaults", "WithReadOnly" };

    public const string DefaultExportOptions = "None";

    /// <summary><c>Siemens.Engineering.DocumentInfoOptions</c>.</summary>
    public static IReadOnlyList<string> DocumentInfoOptions { get; } = new[] { "None", "All", "CreatedTimeStamp", "ExportSetting", "InstalledProducts" };

    /// <summary>Full names of the enum types bound by name at runtime (none is declared on the stubs).</summary>
    public static class EnumTypes
    {
        public const string ArchivationMode = "Siemens.Engineering.Library.LibraryArchivationMode";
        public const string CleanUpMode = "Siemens.Engineering.Library.Types.CleanUpMode";
        public const string HarmonizeProjectOptions = "Siemens.Engineering.Library.Types.HarmonizeProjectOptions";
        public const string ForceUpdateMode = "Siemens.Engineering.Library.Types.ForceUpdateMode";
        public const string DeleteUnusedVersionsMode = "Siemens.Engineering.Library.Types.DeleteUnusedVersionsMode";
        public const string StructureConflictResolutionMode = "Siemens.Engineering.Library.Types.StructureConflictResolutionMode";
        public const string LibraryImportOptions = "Siemens.Engineering.Library.LibraryImportOptions";
        public const string LibraryExportOptions = "Siemens.Engineering.Library.LibraryExportOptions";
        public const string CreateOptions = "Siemens.Engineering.Library.Types.CreateOptions";
        public const string ExportOptions = "Siemens.Engineering.ExportOptions";
        public const string DocumentInfoOptions = "Siemens.Engineering.DocumentInfoOptions";
        public const string OpenMode = "Siemens.Engineering.OpenMode";
        public const string LibraryTypeOrFolderSelection = "Siemens.Engineering.Library.Types.ILibraryTypeOrFolderSelection";
        public const string UpdateProjectScope = "Siemens.Engineering.Library.Types.IUpdateProjectScope";
    }

    public static bool IsProject(string? library) => string.Equals(library, ProjectLibrary, StringComparison.Ordinal);

    /// <summary>
    /// Path equality for global library selectors: ordinal case-insensitive after unifying separators and trimming
    /// trailing separators (Windows file-system semantics; no file-system access).
    /// </summary>
    public static bool SamePath(string? left, string? right)
        => left is not null && right is not null
            && string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

    public static string NormalizePath(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');

    /// <summary>
    /// Folder path segments: <c>null</c>, empty or <c>/</c> is the root; otherwise <c>/A/B</c> or <c>A/B</c>.
    /// Returns null (and an error) for empty segments, control characters or overlong names.
    /// </summary>
    public static IReadOnlyList<string>? ParseFolderPath(string? path, out string? error)
    {
        error = null;
        if (path is null) return Array.Empty<string>();
        var trimmed = path.Trim();
        if (trimmed.Length == 0 || trimmed == "/") return Array.Empty<string>();
        if (trimmed.Length > MaxPathLength) { error = $"The path exceeds {MaxPathLength} characters."; return null; }
        var body = trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed.Substring(1) : trimmed;
        if (body.EndsWith("/", StringComparison.Ordinal)) body = body.Substring(0, body.Length - 1);
        var segments = body.Split('/');
        foreach (var segment in segments)
        {
            if (NameError(segment) is { } nameError) { error = $"Path segment '{segment}': {nameError}"; return null; }
        }

        return segments;
    }

    /// <summary>A type path: folder segments plus the type name (the last segment).</summary>
    public static (IReadOnlyList<string> Folder, string Name)? ParseTypePath(string? path, out string? error)
    {
        var segments = ParseFolderPath(path, out error);
        if (segments is null) return null;
        if (segments.Count == 0) { error = "A type path names a type, for example /Motors/PumpControl."; return null; }
        return (segments.Take(segments.Count - 1).ToArray(), segments[segments.Count - 1]);
    }

    public static string FormatPath(IEnumerable<string> segments)
    {
        var joined = string.Join("/", segments);
        return "/" + joined;
    }

    /// <summary>Null when <paramref name="name"/> is a usable object name.</summary>
    public static string? NameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "must be nonblank.";
        if (name!.Length > MaxNameLength) return $"must be at most {MaxNameLength} characters.";
        if (name.Trim().Length != name.Length) return "must not start or end with whitespace.";
        if (name.Any(char.IsControl)) return "must not contain control characters.";
        if (name.IndexOf('/') >= 0) return "must not contain '/'.";
        return null;
    }

    /// <summary>
    /// The characters Windows refuses in a file name (<c>Path.GetInvalidFileNameChars()</c> on Windows: <c>"</c>, <c>&lt;</c>,
    /// <c>&gt;</c>, <c>|</c>, <c>:</c>, <c>*</c>, <c>?</c>, <c>\</c>, <c>/</c> and U+0000-U+001F). A fixed set, because the host
    /// also runs on Linux, where <c>Path.GetInvalidFileNameChars()</c> is only U+0000 and <c>/</c>.
    /// </summary>
    public static IReadOnlyList<char> WindowsInvalidFileNameChars => InvalidFileNameChars;

    private static readonly char[] InvalidFileNameChars =
        new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' }.Concat(Enumerable.Range(0, 32).Select(code => (char)code)).ToArray();

    private static readonly string[] WindowsReservedFileNames =
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Null when <paramref name="name"/> is usable as the name of a new global library, which TIA Portal turns into a directory
    /// and file name below <c>targetDirectory</c> (<c>Create&lt;UserGlobalLibrary&gt;(DirectoryInfo, string)</c>): the
    /// <see cref="NameError"/> rules plus Windows file-name rules, so the name can neither leave the target directory
    /// (<c>..\..\Temp\Evil</c>) nor name a device or a stream: no <see cref="WindowsInvalidFileNameChars"/> (<c>\</c>, <c>/</c>,
    /// <c>:</c> included), no <c>..</c>, no trailing <c>.</c> (Windows drops it) and no reserved device name (<c>CON</c>,
    /// <c>NUL</c>, <c>COM1</c>, ..., also with an extension).
    /// </summary>
    public static string? LibraryFileNameError(string? name)
    {
        if (NameError(name) is { } error) return error;
        // NameError has already refused control characters.
        if (name!.IndexOfAny(InvalidFileNameChars) is var at && at >= 0)
            return $"must be a valid Windows file name: '{name[at]}' is not allowed (no \\ / : * ? \" < > | or control characters).";
        if (name.IndexOf("..", StringComparison.Ordinal) >= 0) return "must not contain '..'.";
        if (name.EndsWith(".", StringComparison.Ordinal)) return "must not end with '.' (Windows drops it from file names).";
        var stem = name.Split('.')[0].TrimEnd();
        if (WindowsReservedFileNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) return $"must not be the reserved Windows device name '{stem}'.";
        return null;
    }

    /// <summary>
    /// True for a Win32 device or extended-length path prefix: two separators of either kind, then <c>?</c> or <c>.</c>, then a
    /// separator or the end (<c>\\?\</c>, <c>\\.\</c>, and their forward-slash forms such as <c>\\./COM1</c> or <c>\\?/UNC/...</c>).
    /// </summary>
    public static bool IsDevicePath(string? path)
    {
        static bool Separator(char character) => character is '\\' or '/';
        return path is not null && path.Length >= 3 && Separator(path[0]) && Separator(path[1]) && path[2] is '?' or '.'
            && (path.Length == 3 || Separator(path[3]));
    }

    /// <summary>
    /// Null when <paramref name="path"/> is a syntactically absolute Windows path (drive letter or UNC share)
    /// without traversal segments or control characters, and not a device or extended-length path (<see cref="IsDevicePath"/>).
    /// Existence is checked by the worker.
    /// </summary>
    public static string? FilePathError(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "must be nonblank.";
        if (path!.Length > MaxPathLength) return $"must be at most {MaxPathLength} characters.";
        if (path.Trim().Length != path.Length) return "must not start or end with whitespace.";
        if (path.Any(char.IsControl)) return "must not contain control characters.";
        if (IsDevicePath(path)) return "must not be a Win32 device or extended-length path (\\\\?\\, \\\\.\\, with either separator).";
        var drive = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
        var unc = path.StartsWith("\\\\", StringComparison.Ordinal);
        if (!drive && !unc) return "must be an absolute path (drive letter or UNC share).";
        if (path.IndexOf(':', 2) >= 0) return "must not contain ':' after the drive (alternate data streams).";
        if (path.Split('\\', '/').Any(segment => segment == "..")) return "must not contain '..' segments.";
        // Reserved device names (NUL, CON, PRN, AUX, COM1-9, LPT1-9, with any extension) as the other path checks refuse them (U2).
        var root = drive ? 3 : 2;
        if (path.Substring(root).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(Plc.PlcFileTargets.IsReservedDeviceName) is { } reserved)
            return $"must not contain the reserved Windows device name '{reserved}'.";
        return null;
    }

    /// <summary>True for an archived library file (<c>.zalNN</c>), which is opened with retrieve_global_library (G).</summary>
    public static bool IsArchive(string? path)
    {
        if (path is null) return false;
        var name = path.Split('\\', '/').LastOrDefault() ?? string.Empty;
        var dot = name.LastIndexOf('.');
        return dot >= 0 && name.Substring(dot).StartsWith(".zal", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGuid(string? text) => text is not null && System.Guid.TryParse(text, out _);

    /// <summary>A <c>System.Version</c> text with two to four numeric components, as <c>Release</c> takes it.</summary>
    public static bool IsVersionNumber(string? text)
        => text is not null && text.Trim() == text && text.Split('.').Length is >= 2 and <= 4 && System.Version.TryParse(text, out _);

    /// <summary>
    /// Whether a version number read back equals the requested one: both as <c>System.Version</c> with missing components read
    /// as 0 (<c>2.0</c> equals <c>2.0.0</c>); a text that is no version compares ordinally.
    /// </summary>
    public static bool SameVersionNumber(string? actual, string? requested)
    {
        if (actual is null || requested is null) return false;
        if (!System.Version.TryParse(actual.Trim(), out var left) || !System.Version.TryParse(requested.Trim(), out var right))
            return string.Equals(actual, requested, StringComparison.Ordinal);
        static int Part(int value) => value < 0 ? 0 : value;
        return left.Major == right.Major && left.Minor == right.Minor && Part(left.Build) == Part(right.Build) && Part(left.Revision) == Part(right.Revision);
    }

    /// <summary>The short type name a master copy content type reports (G strips the namespace before routing).</summary>
    public static string ShortTypeName(string? fullName)
        => string.IsNullOrEmpty(fullName) ? string.Empty
            : fullName!.LastIndexOf('.') is var dot && dot >= 0 ? fullName.Substring(dot + 1) : fullName;

    public static string? Bound(string? text)
        => text is null || text.Length <= MaxTextLength ? text : text.Substring(0, MaxTextLength);

    /// <summary>The page of <paramref name="items"/> starting at <paramref name="offset"/>.</summary>
    public static List<T> Page<T>(IReadOnlyList<T> items, int offset, int limit)
        => items.Skip(Math.Max(0, offset)).Take(Math.Max(0, limit)).ToList();
}
