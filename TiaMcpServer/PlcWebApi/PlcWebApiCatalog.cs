using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.PlcWebApi;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.PlcWebApi;

/// <summary>
/// Public operation catalog of plc_online_read and plc_online_write: names, the members each
/// operation accepts, strict validation before any network activity, and the access check.
/// </summary>
public static partial class PlcWebApiCatalog
{
    public const int MaxTags = 100;
    public const int MaxEntries = 1000;
    public const int DefaultEntries = 50;
    public const int MaxSymbolChars = 1024;
    public const int MaxValueChars = 4096;
    public const int MaxPathChars = 1024;
    public const string DefaultLanguage = "en-US";

    /// <summary>The earliest and latest timestamps the client's own parameter check accepts for Plc.SetSystemTime.</summary>
    public static readonly DateTimeOffset MinSystemTime = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset MaxSystemTime = new(2554, 7, 21, 23, 34, 33, 709, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, string[]> ReadFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ping"] = new[] { "target" },
        ["version"] = new[] { "target" },
        ["permissions"] = new[] { "target" },
        ["read_operating_mode"] = new[] { "target" },
        ["read_mode_selector"] = new[] { "target" },
        ["read_cpu_type"] = new[] { "target" },
        ["read_runtime_information"] = new[] { "target" },
        ["read_system_time"] = new[] { "target" },
        ["read_project_information"] = new[] { "target" },
        ["browse_tags"] = new[] { "target", "var", "browseMode", "type" },
        ["read_tags"] = new[] { "target", "names", "representation" },
        ["read_diagnostic_buffer"] = new[] { "target", "count", "language" },
        ["browse_alarms"] = new[] { "target", "count", "alarmId", "language" },
        ["browse_modules"] = new[] { "target", "hwid", "browseMode" },
        ["read_module_status"] = new[] { "target", "hwid", "language" },
        ["read_module_leds"] = new[] { "target", "hwid" },
        ["browse_syslog"] = new[] { "target", "count", "first" },
        ["read_watch_table_live"] = new[] { "target", "plcName", "table", "tagTable", "folderPath", "representation", "count" },
    };

    private static readonly IReadOnlyDictionary<string, string[]> WriteFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["change_operating_mode"] = new[] { "target", "operatingMode" },
        ["write_tags"] = new[] { "target", "tags", "representation" },
        ["acknowledge_alarm"] = new[] { "target", "alarmId" },
        ["flash_leds"] = new[] { "target", "hwid" },
        ["set_system_time"] = new[] { "target", "timestamp" },
        ["create_backup"] = new[] { "target", "path", "overwrite" },
        ["restore_backup"] = new[] { "target", "path" },
    };

    public static IReadOnlyCollection<string> ReadOperationNames => ReadFields.Keys.ToArray();

    public static IReadOnlyCollection<string> WriteOperationNames => WriteFields.Keys.ToArray();

    public static string Method(string operation) => PlcWebApiMethods.ForOperation(operation);

    public static OperationValidation ValidateRead(IReadOnlyList<PlcOnlineReadRequest?>? operations)
        => Validate(operations, ReadFields, WriteFields, "plc_online_read", "plc_online_write", (item, errors, prefix) =>
        {
            var read = (PlcOnlineReadRequest)item;
            return new (string, bool)[]
            {
                ("target", read.Target is not null), ("var", read.Var is not null), ("browseMode", read.BrowseMode is not null),
                ("type", read.Type is not null), ("names", read.Names is not null), ("representation", read.Representation is not null),
                ("count", read.Count is not null), ("first", read.First is not null), ("alarmId", read.AlarmId is not null),
                ("language", read.Language is not null), ("hwid", read.Hwid is not null),
                ("plcName", read.PlcName is not null), ("table", read.Table is not null), ("tagTable", read.TagTable is not null),
                ("folderPath", read.FolderPath is not null),
            };
        }, ValidateReadItem);

    public static OperationValidation ValidateWrite(IReadOnlyList<PlcOnlineWriteRequest?>? operations)
    {
        var validation = Validate(operations, WriteFields, ReadFields, "plc_online_write", "plc_online_read", (item, errors, prefix) =>
        {
            var write = (PlcOnlineWriteRequest)item;
            return new (string, bool)[]
            {
                ("target", write.Target is not null), ("operatingMode", write.OperatingMode is not null), ("tags", write.Tags is not null),
                ("representation", write.Representation is not null), ("alarmId", write.AlarmId is not null), ("hwid", write.Hwid is not null),
                ("timestamp", write.Timestamp is not null), ("path", write.Path is not null), ("overwrite", write.Overwrite is not null),
            };
        }, ValidateWriteItem);
        if (!validation.IsValid) return validation;

        var duplicates = operations!.Where(item => item!.Operation == "create_backup")
            .GroupBy(item => System.IO.Path.GetFullPath(item!.Path!), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        return duplicates.Length == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError,
                $"Several create_backup items write the same file: {string.Join(", ", duplicates)}.");
    }

    public static IReadOnlyList<string> ValidateAccessMode(IEnumerable<IOperationBatchItem> operations, McpAccessMode mode)
        => operations
            .Where(item => !OperationPolicyCatalog.IsAllowed(mode, Method(item.Operation)))
            .Select(item => $"Operation '{item.Operation}' is not permitted in {McpAccessModeNames.ToName(mode)} mode.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>Parses an RFC 3339 timestamp that carries Z or an explicit offset; returns it in UTC.</summary>
    public static bool TryParseTimestamp(string? text, out DateTimeOffset utc)
    {
        utc = default;
        if (text is null || text.Length > 64 || !ExplicitOffset().IsMatch(text)) return false;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        utc = parsed.ToUniversalTime();
        return true;
    }

    public static string FormatTimestamp(DateTimeOffset utc)
        => utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static OperationValidation Validate<T>(
        IReadOnlyList<T?>? operations,
        IReadOnlyDictionary<string, string[]> fields,
        IReadOnlyDictionary<string, string[]> other,
        string tool,
        string otherTool,
        Func<T, List<string>, string, (string Name, bool Present)[]> members,
        Action<T, List<string>, string> validateItem)
        where T : class, IOperationBatchItem
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError)
            {
                errors.Add(itemError);
                continue;
            }

            var prefix = $"{item!.OperationId}: ";
            if (string.IsNullOrWhiteSpace(item.Operation))
            {
                errors.Add(prefix + "operation is required.");
                continue;
            }

            if (!fields.TryGetValue(item.Operation, out var allowed))
            {
                errors.Add(prefix + (other.ContainsKey(item.Operation)
                    ? $"'{item.Operation}' belongs to {otherTool}, not {tool}."
                    : $"unknown {tool} operation '{item.Operation}'. Valid operations: {string.Join(", ", fields.Keys)}."));
                continue;
            }

            foreach (var (name, present) in members(item, errors, prefix))
                if (present && !allowed.Contains(name, StringComparer.Ordinal))
                    errors.Add(prefix + $"'{name}' is not valid for {item.Operation}.");

            validateItem(item, errors, prefix);
        }

        return errors.Count == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    private static void ValidateTarget(string? target, List<string> errors, string prefix)
    {
        if (!PlcWebApiConfigLoader.IsValidName(target))
            errors.Add(prefix + $"target is required: the exact name of a configured PLC Web API target (1 to {PlcWebApiConfigLoader.MaxNameLength} characters).");
    }

    private static void ValidateReadItem(PlcOnlineReadRequest item, List<string> errors, string prefix)
    {
        ValidateTarget(item.Target, errors, prefix);
        switch (item.Operation)
        {
            case "browse_tags":
                if (item.Var is not null && !IsSymbol(item.Var))
                    errors.Add(prefix + $"var must be nonblank and at most {MaxSymbolChars} characters.");
                if (item.BrowseMode is not null and not ("children" or "var"))
                    errors.Add(prefix + "browseMode must be children or var for browse_tags.");
                if (item.BrowseMode == "var" && item.Var is null)
                    errors.Add(prefix + "browseMode var needs var: the variable whose metadata to read.");
                if (item.Type is not null and not "code_blocks")
                    errors.Add(prefix + "type must be code_blocks (the only PlcProgram.Browse type filter the client library offers).");
                if (item.Type is not null && (item.Var is not null || item.BrowseMode == "var"))
                    errors.Add(prefix + "type code_blocks lists the program's code blocks; it takes neither var nor browseMode var.");
                break;
            case "read_tags":
                if (item.Names is not { Count: > 0 and <= MaxTags })
                    errors.Add(prefix + $"read_tags needs names: 1 to {MaxTags} symbolic names.");
                else if (item.Names.Any(name => !IsSymbol(name)))
                    errors.Add(prefix + $"every name must be nonblank and at most {MaxSymbolChars} characters.");
                ValidateRepresentation(item.Representation, errors, prefix);
                break;
            case "read_diagnostic_buffer":
            case "browse_syslog":
                ValidateCount(item.Count, errors, prefix);
                if (item.First is < 0) errors.Add(prefix + "first must be 0 or more.");
                ValidateLanguage(item.Language, errors, prefix);
                break;
            case "browse_alarms":
                ValidateCount(item.Count, errors, prefix);
                if (item.AlarmId is not null && !IsIdentifier(item.AlarmId))
                    errors.Add(prefix + "alarmId must be nonblank and at most 256 characters.");
                if (item.AlarmId is not null && item.Count is not null)
                    errors.Add(prefix + "browse_alarms takes either alarmId or count, not both (the Web API refuses the combination).");
                ValidateLanguage(item.Language, errors, prefix);
                break;
            case "browse_modules":
                ValidateHwid(item.Hwid, required: false, errors, prefix);
                if (item.BrowseMode is not null and not ("children" or "node"))
                    errors.Add(prefix + "browseMode must be children or node for browse_modules.");
                break;
            case "read_module_status":
                ValidateHwid(item.Hwid, required: true, errors, prefix);
                ValidateLanguage(item.Language, errors, prefix);
                break;
            case "read_module_leds":
                ValidateHwid(item.Hwid, required: true, errors, prefix);
                break;
            case "read_watch_table_live":
                if ((item.Table is null) == (item.TagTable is null))
                    errors.Add(prefix + "read_watch_table_live needs exactly one of table (a watch table) and tagTable (a PLC tag table).");
                foreach (var (name, value) in new[] { ("table", item.Table), ("tagTable", item.TagTable), ("plcName", item.PlcName), ("folderPath", item.FolderPath) })
                    if (value is not null && !IsIdentifier(value))
                        errors.Add(prefix + $"{name} must be nonblank and at most 256 characters.");
                ValidateCount(item.Count, errors, prefix);
                ValidateRepresentation(item.Representation, errors, prefix);
                break;
        }
    }

    private static void ValidateWriteItem(PlcOnlineWriteRequest item, List<string> errors, string prefix)
    {
        ValidateTarget(item.Target, errors, prefix);
        switch (item.Operation)
        {
            case "change_operating_mode":
                if (item.OperatingMode is not ("run" or "stop"))
                    errors.Add(prefix + "operatingMode must be run or stop.");
                break;
            case "write_tags":
                if (item.Tags is not { Count: > 0 and <= MaxTags })
                {
                    errors.Add(prefix + $"write_tags needs tags: 1 to {MaxTags} entries {{ name, value }}.");
                    break;
                }

                for (var index = 0; index < item.Tags.Count; index++)
                {
                    var tag = item.Tags[index];
                    if (tag is null || !IsSymbol(tag.Name))
                        errors.Add(prefix + $"tags[{index}].name must be nonblank and at most {MaxSymbolChars} characters.");
                    if (tag?.Value is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                        errors.Add(prefix + $"tags[{index}].value is required and must not be null.");
                    else if (value.GetRawText().Length > MaxValueChars)
                        errors.Add(prefix + $"tags[{index}].value exceeds {MaxValueChars} characters of JSON.");
                }

                ValidateRepresentation(item.Representation, errors, prefix);
                break;
            case "acknowledge_alarm":
                if (!IsIdentifier(item.AlarmId))
                    errors.Add(prefix + "acknowledge_alarm needs alarmId (nonblank, at most 256 characters) from plc_online_read browse_alarms.");
                break;
            case "flash_leds":
                ValidateHwid(item.Hwid, required: true, errors, prefix);
                break;
            case "set_system_time":
                if (!TryParseTimestamp(item.Timestamp, out var timestamp))
                    errors.Add(prefix + "timestamp must be an RFC 3339 date-time with Z or an explicit offset, for example 2026-10-31T12:00:00Z.");
                else if (timestamp < MinSystemTime || timestamp > MaxSystemTime)
                    errors.Add(prefix + $"timestamp must be between {FormatTimestamp(MinSystemTime)} and {FormatTimestamp(MaxSystemTime)} (the client library's range).");
                break;
            case "create_backup":
            case "restore_backup":
                if (item.Path is null || item.Path.Length > MaxPathChars || !IsLocalFilePath(item.Path, OperatingSystem.IsWindows()))
                    errors.Add(prefix + $"path must be an absolute local file path (at most {MaxPathChars} characters) that names a file: "
                        + "a drive path or a UNC share (\\\\server\\share\\...) on Windows, no device (\\\\?\\, \\\\.\\) path, and no control or Unicode format characters.");
                break;
        }
    }

    /// <summary>
    /// An absolute path on a drive or (since 2026-11-01, owner decision) a UNC share that names a file. Refused before
    /// anything touches the file system: device paths (<c>\\?\</c>, <c>\\.\</c>, <c>\??\</c>; they bypass Win32 path
    /// normalization), on Windows anything but <c>X:\</c> or <c>\\server\share\</c>, a full path that normalizes to
    /// one of those, and control or Unicode format characters (the path is shown in the confirmation prompt).
    /// </summary>
    internal static bool IsLocalFilePath(string path, bool windows)
    {
        static bool Separator(char character) => character is '\\' or '/';
        static bool UncOrDevice(string candidate) => Contracts.Files.LocalFilePaths.IsDevicePath(candidate);
        static bool DriveRooted(string candidate)
            => candidate.Length >= 3 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':' && Separator(candidate[2])
                || Contracts.Files.LocalFilePaths.IsUncShare(candidate);

        if (path.Length == 0 || UncOrDevice(path) || Safety.Pipeline.ConfirmationText.ContainsUnsafeCharacters(path)
            || path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            return false;
        if ((windows ? !DriveRooted(path) : path[0] != '/' || Contracts.Files.LocalFilePaths.IsUncOrDevicePath(path)) || Separator(path[^1])) return false;
        // The normalization below needs the platform's own path rules; the shape check above is all
        // that can be said about the other platform's paths.
        if (windows != OperatingSystem.IsWindows()) return true;
        if (!System.IO.Path.IsPathFullyQualified(path) || string.IsNullOrEmpty(System.IO.Path.GetFileName(path))) return false;
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return false;
        }

        return !UncOrDevice(full) && (!windows || DriveRooted(full));
    }

    private static void ValidateRepresentation(string? representation, List<string> errors, string prefix)
    {
        if (representation is not null and not ("simple" or "raw"))
            errors.Add(prefix + "representation must be simple or raw.");
    }

    private static void ValidateCount(int? count, List<string> errors, string prefix)
    {
        if (count is { } value && (value < 1 || value > MaxEntries))
            errors.Add(prefix + $"count must be 1 to {MaxEntries}.");
    }

    private static void ValidateHwid(long? hwid, bool required, List<string> errors, string prefix)
    {
        if (hwid is null)
        {
            if (required) errors.Add(prefix + "hwid is required: the module's hardware identifier from plc_online_read browse_modules.");
            return;
        }

        if (hwid < 0 || hwid > uint.MaxValue) errors.Add(prefix + $"hwid must be 0 to {uint.MaxValue}.");
    }

    private static void ValidateLanguage(string? language, List<string> errors, string prefix)
    {
        if (language is not null && !LanguageName().IsMatch(language))
            errors.Add(prefix + "language must be a culture name such as en-US or de-DE.");
    }

    private static bool IsSymbol(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaxSymbolChars;

    private static bool IsIdentifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageName();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitOffset();
}
