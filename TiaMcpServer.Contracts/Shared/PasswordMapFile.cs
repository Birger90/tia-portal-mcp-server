using System.Text.Json;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>One password map entry: where the password for a purpose on a PLC (of a project) is. Names only.</summary>
public sealed class PasswordMapEntry
{
    public PasswordMapEntry(string? project, string? plc, string purpose, string source)
    {
        Project = project;
        Plc = plc;
        Purpose = purpose;
        Source = source;
    }

    /// <summary>The project name (file name without extension, case-insensitive); null (in the file: omitted or <c>"*"</c>) matches every project.</summary>
    public string? Project { get; }

    /// <summary>The PLC software or device name (case-insensitive); null (in the file: omitted or <c>"*"</c>) matches every PLC.</summary>
    public string? Plc { get; }

    /// <summary><c>safety</c> or a password configuration type short name such as <c>ModuleWriteAccessPassword</c>.</summary>
    public string Purpose { get; }

    /// <summary>The source token (<see cref="PasswordSources"/>).</summary>
    public string Source { get; }
}

/// <summary>A password map file that is not accepted; the message never repeats a value from the file.</summary>
public sealed class PasswordMapException(string message) : Exception(message);

/// <summary>
/// The optional server-wide password map (default <c>%APPDATA%\TiaMcpServer\passwords.json</c>, or the path in
/// <see cref="EnvironmentVariable"/>): it maps a PLC (and optionally a project) and a purpose to the NAME of an environment
/// variable or a Windows credential target, so a caller need not pass a source each time. It never holds a secret: the
/// schema is closed, and a member whose name looks like a value field (password, secret, value, ...) refuses the whole
/// file, as does every other unknown member.
/// <code>
/// { "version": 1, "entries": [
///   { "project": "MCP_Safety_01", "plc": "PLC_F1", "purpose": "safety", "credential": "TiaMcp/MCP_Safety_01/PLC_F1/Safety" },
///   { "plc": "PLC_1", "purpose": "ModuleWriteAccessPassword", "env": "PLC1_WRITE_PW" },
///   { "project": "*", "plc": "*", "purpose": "FullAccess", "credential": "TiaMcp/Alle Projekte/Alle PLCs/FullAccess" } ] }
/// </code>
/// <c>project</c> and <c>plc</c> are optional; an omitted one and <see cref="AnyScope"/> both mean "every".
/// </summary>
public static class PasswordMapFile
{
    public const string EnvironmentVariable = "TIA_MCP_PASSWORD_MAP";
    public const string DirectoryName = "TiaMcpServer";
    public const string FileName = "passwords.json";
    public const string SafetyPurpose = "safety";
    public const int MaxEntries = 500;
    public const int MaxNameLength = 256;

    /// <summary>The scope value that means every project or every PLC (the same as omitting the member).</summary>
    public const string AnyScope = "*";

    private static readonly string[] RootMembers = { "version", "entries" };
    private static readonly string[] EntryMembers = { "project", "plc", "purpose", "env", "credential" };

    /// <summary>Member names that would hold a secret: their presence refuses the file.</summary>
    private static readonly string[] ValueLikeFragments = { "password", "passwd", "pwd", "pass", "secret", "value", "token", "pin", "blob", "key" };

    public static bool LooksLikeValueField(string name)
    {
        var lower = name.ToLowerInvariant();
        return ValueLikeFragments.Any(fragment => lower.Contains(fragment));
    }

    /// <exception cref="PasswordMapException">The file is not a valid, names-only password map.</exception>
    public static IReadOnlyList<PasswordMapEntry> Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            throw new PasswordMapException("The password map is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new PasswordMapException("The password map must be a JSON object with version and entries.");
            CheckMembers(root, RootMembers, "the root");
            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
                throw new PasswordMapException("The password map needs \"version\": 1.");
            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                throw new PasswordMapException("The password map needs an \"entries\" array.");
            if (entries.GetArrayLength() > MaxEntries) throw new PasswordMapException($"The password map has more than {MaxEntries} entries.");
            var result = new List<PasswordMapEntry>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var entry in entries.EnumerateArray())
            {
                var where = $"entry {index++}";
                if (entry.ValueKind != JsonValueKind.Object) throw new PasswordMapException($"Password map {where} must be an object.");
                CheckMembers(entry, EntryMembers, where);
                var project = Scope(Text(entry, "project", where, required: false));
                var plc = Scope(Text(entry, "plc", where, required: false));
                var purpose = Text(entry, "purpose", where, required: true)!;
                if (!IsPurpose(purpose))
                    throw new PasswordMapException($"Password map {where}: purpose must be \"{SafetyPurpose}\" or a password configuration type short name (not TlsVerificationConfiguration).");
                var env = Text(entry, "env", where, required: false);
                var credential = Text(entry, "credential", where, required: false);
                if ((env is null) == (credential is null)) throw new PasswordMapException($"Password map {where} needs exactly one of env or credential.");
                string source;
                if (env is not null)
                {
                    if (PasswordSources.KindOf(env) != PasswordSourceKind.Environment)
                        throw new PasswordMapException($"Password map {where}: env must be an environment variable NAME (letters, digits, underscores).");
                    source = env;
                }
                else
                {
                    if (PasswordSources.CredentialTargetError(credential) is { } error) throw new PasswordMapException($"Password map {where}: {error}.");
                    source = PasswordSources.CredentialToken(credential!);
                }

                if (!keys.Add($"{project}\n{plc}\n{purpose}")) throw new PasswordMapException($"Password map {where} repeats the project, plc and purpose of an earlier entry.");
                result.Add(new PasswordMapEntry(project, plc, purpose, source));
            }

            return result;
        }
    }

    /// <summary>
    /// The source for <paramref name="purpose"/> on <paramref name="plc"/> in <paramref name="project"/>: the most specific
    /// entry wins, by <see cref="Specificity"/> (project and PLC, then project, then PLC in every project, then the
    /// default for every project and PLC); null when none matches.
    /// </summary>
    public static string? Lookup(IReadOnlyList<PasswordMapEntry> entries, string? project, string? plc, string purpose)
        => Find(entries, project, plc, purpose)?.Source;

    /// <summary>The entry <see cref="Lookup"/> takes the source from, or null.</summary>
    public static PasswordMapEntry? Find(IReadOnlyList<PasswordMapEntry> entries, string? project, string? plc, string purpose)
    {
        static bool Matches(string? pattern, string? value) => pattern is null || value is not null && string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
        return entries
            .Where(e => string.Equals(e.Purpose, purpose, StringComparison.Ordinal) && Matches(e.Project, project) && Matches(e.Plc, plc))
            .OrderByDescending(Specificity)
            .FirstOrDefault();
    }

    /// <summary>
    /// The rank of an entry: 3 project and PLC, 2 project only (every PLC of it), 1 PLC only (that PLC name in every
    /// project), 0 neither (the default for every project and PLC). A named project outranks a named PLC because PLC names
    /// such as PLC_1 repeat across projects.
    /// </summary>
    public static int Specificity(PasswordMapEntry entry) => (entry.Project is null ? 0 : 2) + (entry.Plc is null ? 0 : 1);

    /// <summary>
    /// The map as JSON (version 1, wildcards written as <see cref="AnyScope"/>): names only, checked by parsing it again, so
    /// a writer can never produce a file the server refuses.
    /// </summary>
    /// <exception cref="PasswordMapException">An entry is not valid (the message never repeats a value).</exception>
    public static string Serialize(IEnumerable<PasswordMapEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("entries");
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("project", entry.Project ?? AnyScope);
                writer.WriteString("plc", entry.Plc ?? AnyScope);
                writer.WriteString("purpose", entry.Purpose);
                if (PasswordSources.CredentialTargetOf(entry.Source) is { } target) writer.WriteString("credential", target);
                else writer.WriteString("env", entry.Source);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        Parse(json);
        return json;
    }

    /// <summary>The map file a writer uses: the path in <see cref="EnvironmentVariable"/>, else the %APPDATA% default (existing or not).</summary>
    public static string? TargetPath(Func<string, string?> readEnvironment, string? applicationData)
    {
        var configured = readEnvironment(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return string.IsNullOrEmpty(applicationData) ? null : Path.Combine(applicationData, DirectoryName, FileName);
    }

    private static string? Scope(string? value) => value == AnyScope ? null : value;

    private static void CheckMembers(JsonElement element, string[] allowed, string where)
    {
        foreach (var member in element.EnumerateObject())
        {
            if (allowed.Contains(member.Name, StringComparer.Ordinal)) continue;
            throw new PasswordMapException(LooksLikeValueField(member.Name)
                ? $"The password map is refused: {where} has a member that looks like a secret value field. The map holds only names of environment variables or credential targets; store the password itself with cmdkey /generic or in an environment variable."
                : $"The password map is refused: {where} has an unknown member (allowed: {string.Join(", ", allowed)}).");
        }
    }

    private static string? Text(JsonElement entry, string name, string where, bool required)
    {
        if (!entry.TryGetProperty(name, out var value))
            return required ? throw new PasswordMapException($"Password map {where} needs {name}.") : null;
        if (value.ValueKind != JsonValueKind.String) throw new PasswordMapException($"Password map {where}: {name} must be a string.");
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text) || text!.Length > MaxNameLength || text.Any(char.IsControl))
            throw new PasswordMapException($"Password map {where}: {name} must be a nonblank name of at most {MaxNameLength} characters.");
        return text;
    }

    /// <summary>True for <c>safety</c>, an access level name or a password configuration type short name (an identifier, not TlsVerificationConfiguration).</summary>
    public static bool IsPurpose(string? value) => !string.IsNullOrEmpty(value) && PlcIdentifier(value!) && value != "TlsVerificationConfiguration";

    private static bool PlcIdentifier(string value) => value.Length <= MaxNameLength
        && (char.IsLetter(value[0]) || value[0] == '_') && value.All(c => char.IsLetterOrDigit(c) || c == '_');
}
