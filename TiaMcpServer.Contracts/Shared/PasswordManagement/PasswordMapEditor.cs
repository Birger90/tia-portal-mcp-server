using System.Text;
using TiaMcpServer.Contracts.Files;

namespace TiaMcpServer.Contracts.Shared.PasswordManagement;

/// <summary>The outcome of <see cref="PasswordMapEditor.Save"/>.</summary>
public sealed class PasswordMapSaveResult
{
    private PasswordMapSaveResult(string? error, PasswordMapEntry? entry, string? obsoleteTarget, int sharedWith)
    {
        Error = error;
        Entry = entry;
        ObsoleteTarget = obsoleteTarget;
        SharedWith = sharedWith;
    }

    internal static PasswordMapSaveResult Refused(string error) => new(error, null, null, 0);

    internal static PasswordMapSaveResult Done(PasswordMapEntry entry, string? obsoleteTarget, int sharedWith) => new(null, entry, obsoleteTarget, sharedWith);

    /// <summary>The refusal (German); nothing was written. Null on success.</summary>
    public string? Error { get; }

    public bool Saved => Error is null;

    /// <summary>The new map entry.</summary>
    public PasswordMapEntry? Entry { get; }

    /// <summary>The replaced entry's credential target that no entry references any more (the caller may delete it), or null.</summary>
    public string? ObsoleteTarget { get; }

    /// <summary>How many other entries use the same credential target (they get the new password too).</summary>
    public int SharedWith { get; }
}

/// <summary>
/// The non-UI logic of the TiaMcp password manager: it edits the names-only password map (<see cref="PasswordMapFile"/>)
/// and stores each secret as a Windows generic credential through an <see cref="ICredentialStore"/>. The map never holds a
/// secret: an entry is a project scope, a PLC scope, a purpose and the credential target (or a hand-written environment
/// variable NAME). A save writes the credential first and changes the map only when that succeeded.
/// </summary>
public sealed class PasswordMapEditor
{
    /// <summary>The target segment for every project (a project of that name would get the same target; such a collision is refused).</summary>
    public const string AllProjectsSegment = "Alle Projekte";

    /// <summary>The target segment for every PLC.</summary>
    public const string AllPlcsSegment = "Alle PLCs";

    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE (5 * 512 bytes) of wincred.h in UTF-16 characters.</summary>
    public const int MaxSecretLength = 1280;

    /// <summary>The comment stored with each credential (no secret, no scope that is not in the target name anyway).</summary>
    public const string CredentialComment = "TiaMcp Passwortverwaltung";

    private readonly List<PasswordMapEntry> _entries;

    /// <param name="entries">The map's entries (for example <see cref="PasswordMapFile.Parse"/> of the existing file).</param>
    /// <param name="credentialPrefix">The prefix the server accepts; <see cref="PasswordSources.AnyCredentialPrefix"/> or null means new targets use the default prefix.</param>
    public PasswordMapEditor(IEnumerable<PasswordMapEntry>? entries, string? credentialPrefix)
    {
        _entries = (entries ?? Array.Empty<PasswordMapEntry>()).ToList();
        AcceptedPrefix = string.IsNullOrEmpty(credentialPrefix) ? PasswordSources.CredentialTargetPrefix : credentialPrefix!;
        TargetPrefix = AcceptedPrefix == PasswordSources.AnyCredentialPrefix ? PasswordSources.CredentialTargetPrefix : AcceptedPrefix;
    }

    /// <summary>The editor of the map text (null or blank: an empty map).</summary>
    /// <exception cref="PasswordMapException">The text is not a valid, names-only map; the editor refuses to overwrite it.</exception>
    public static PasswordMapEditor FromJson(string? json, string? credentialPrefix)
        => new(string.IsNullOrWhiteSpace(json) ? null : PasswordMapFile.Parse(json!), credentialPrefix);

    public IReadOnlyList<PasswordMapEntry> Entries => _entries.AsReadOnly();

    /// <summary>The prefix the server accepts (<see cref="PasswordSources.AnyCredentialPrefix"/>: any target).</summary>
    public string AcceptedPrefix { get; }

    /// <summary>The prefix of the targets this editor creates.</summary>
    public string TargetPrefix { get; }

    /// <summary>The credential target for a scope and purpose: <c>prefix + project + "/" + plc + "/" + purpose</c>.</summary>
    public string TargetFor(string? project, string? plc, string purpose)
        => TargetPrefix + Segment(project, AllProjectsSegment) + "/" + Segment(plc, AllPlcsSegment) + "/" + (purpose == PasswordMapFile.SafetyPurpose ? "Safety" : purpose);

    /// <summary>Why a project or PLC scope name is not accepted (German); null when it is. Null as <paramref name="name"/> means "every".</summary>
    /// <param name="what">The field as the message names it, for example <c>Projekt</c> or <c>PLC</c>.</param>
    public static string? ScopeError(string? name, string what)
    {
        if (name is null) return null;
        if (name.Trim().Length == 0) return $"{what}: bitte einen Namen eingeben oder \"alle\" wählen.";
        if (name != name.Trim()) return $"{what}: der Name darf nicht mit Leerzeichen beginnen oder enden.";
        if (name.Length > PasswordMapFile.MaxNameLength) return $"{what}: der Name hat höchstens {PasswordMapFile.MaxNameLength} Zeichen.";
        if (name.Any(char.IsControl)) return $"{what}: der Name darf keine Steuerzeichen enthalten.";
        if (name.Contains('*')) return $"{what}: der Name darf kein '*' enthalten; für alle die Auswahl \"alle\" nutzen.";
        return null;
    }

    /// <summary>Why the typed secret and its repetition are not accepted (German); null when they are. Never repeats the secret.</summary>
    public static string? SecretError(char[]? secret, char[]? repetition)
    {
        if (secret is null || secret.Length == 0) return "Bitte ein Passwort eingeben.";
        if (secret.Length > MaxSecretLength) return $"Das Passwort hat höchstens {MaxSecretLength} Zeichen (Grenze der Windows-Anmeldeinformationsverwaltung).";
        if (repetition is null || repetition.Length != secret.Length) return "Die beiden Eingaben stimmen nicht überein.";
        var difference = 0;
        for (var i = 0; i < secret.Length; i++) difference |= secret[i] ^ repetition[i];
        return difference == 0 ? null : "Die beiden Eingaben stimmen nicht überein.";
    }

    /// <summary>The entry with this project, PLC and purpose (wildcards as null), or null.</summary>
    public PasswordMapEntry? EntryFor(string? project, string? plc, string purpose)
        => _entries.FirstOrDefault(e => SameKey(e, project, plc, purpose));

    /// <summary>The entry the server would use for a PLC of a project (<see cref="PasswordMapFile.Find"/>), or null.</summary>
    public PasswordMapEntry? Effective(string? project, string? plc, string purpose) => PasswordMapFile.Find(_entries, project, plc, purpose);

    /// <summary>
    /// Stores <paramref name="secret"/> as the generic credential of a new entry or of <paramref name="replacing"/> and updates
    /// the map. The credential is written first; on any refusal neither the map nor (before the write) the credential store
    /// changes. An unchanged scope and purpose of a credential entry keeps its target; the caller clears the secret afterwards.
    /// </summary>
    public PasswordMapSaveResult Save(string? project, string? plc, string purpose, char[] secret, ICredentialStore store, PasswordMapEntry? replacing = null)
    {
        if (replacing is not null && !_entries.Contains(replacing)) return PasswordMapSaveResult.Refused("Der Eintrag ist nicht mehr in der Liste; bitte neu laden.");
        if (ScopeError(project, "Projekt") is { } projectError) return PasswordMapSaveResult.Refused(projectError);
        if (ScopeError(plc, "PLC") is { } plcError) return PasswordMapSaveResult.Refused(plcError);
        if (PasswordPurposes.Error(purpose) is { } purposeError) return PasswordMapSaveResult.Refused(purposeError);
        if (secret is null || secret.Length == 0) return PasswordMapSaveResult.Refused("Bitte ein Passwort eingeben.");
        if (secret.Length > MaxSecretLength) return PasswordMapSaveResult.Refused($"Das Passwort hat höchstens {MaxSecretLength} Zeichen.");

        var existing = EntryFor(project, plc, purpose);
        if (existing is not null && !ReferenceEquals(existing, replacing))
            return PasswordMapSaveResult.Refused("Für diesen Zweck und Geltungsbereich gibt es schon einen Eintrag; bitte diesen ändern.");

        var keepTarget = replacing is not null && SameKey(replacing, project, plc, purpose) ? PasswordSources.CredentialTargetOf(replacing.Source) : null;
        var target = keepTarget ?? TargetFor(project, plc, purpose);
        if (PasswordSources.CredentialTargetError(target, AcceptedPrefix) is { } targetError)
            return PasswordMapSaveResult.Refused("Der Name der Anmeldeinformation ist nicht zulässig: " + targetError + ".");
        var token = PasswordSources.CredentialToken(target);
        var sharing = _entries.Where(e => !ReferenceEquals(e, replacing) && e.Source == token).ToList();
        if (keepTarget is null && sharing.Count > 0)
            return PasswordMapSaveResult.Refused("Der Name der Anmeldeinformation wird schon von einem anderen Eintrag verwendet.");

        if (store.Write(target, secret, CredentialComment) is { } writeError) return PasswordMapSaveResult.Refused(writeError);

        var entry = new PasswordMapEntry(project, plc, purpose, token);
        var index = replacing is null ? _entries.Count : _entries.IndexOf(replacing);
        if (replacing is not null) _entries.RemoveAt(index);
        _entries.Insert(index, entry);
        var obsolete = replacing is not null && PasswordSources.CredentialTargetOf(replacing.Source) is { } old && old != target && _entries.All(e => e.Source != replacing.Source)
            ? old
            : null;
        return PasswordMapSaveResult.Done(entry, obsolete, sharing.Count);
    }

    /// <summary>Removes the entry; returns its credential target when no other entry uses it (the caller may delete it), or null.</summary>
    public string? Remove(PasswordMapEntry entry)
    {
        if (!_entries.Remove(entry)) return null;
        return PasswordSources.CredentialTargetOf(entry.Source) is { } target && _entries.All(e => e.Source != entry.Source) ? target : null;
    }

    /// <summary>Checks that the entry's source exists (never its value): a credential through the store, an environment variable in this program's environment.</summary>
    public CredentialCheckResult Test(PasswordMapEntry entry, ICredentialStore store, Func<string, string?> readEnvironment)
    {
        if (PasswordSources.CredentialTargetOf(entry.Source) is { } target)
        {
            if (PasswordSources.CredentialTargetError(target, AcceptedPrefix) is not null)
                return new CredentialCheckResult(CredentialState.PrefixMismatch, AcceptedPrefix == PasswordSources.AnyCredentialPrefix
                    ? "Der Name der Anmeldeinformation ist nicht zulässig (zu lang oder mit unzulässigen Zeichen)."
                    : $"Der Name der Anmeldeinformation beginnt nicht mit dem Präfix '{AcceptedPrefix}', den der Server annimmt.");
            return store.Check(target);
        }

        return string.IsNullOrEmpty(readEnvironment(entry.Source))
            ? new CredentialCheckResult(CredentialState.EnvironmentUnset, $"Die Umgebungsvariable {entry.Source} ist in der Umgebung dieses Programms nicht gesetzt. Der Server sieht die Umgebung, die sein MCP-Client ihm gibt.")
            : new CredentialCheckResult(CredentialState.EnvironmentSet, $"Die Umgebungsvariable {entry.Source} ist in der Umgebung dieses Programms gesetzt. Der Server sieht die Umgebung, die sein MCP-Client ihm gibt.");
    }

    /// <summary>The map text (names only; checked by parsing it again).</summary>
    public string ToJson() => PasswordMapFile.Serialize(_entries);

    /// <summary>
    /// Writes the map to <paramref name="path"/> through a temporary file in the same folder, so a reader sees the old or the
    /// new file, never half of one. Device paths are refused like everywhere else (they bypass path normalization); UNC
    /// shares are allowed.
    /// </summary>
    public void WriteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new IOException("Kein Pfad für die Zuordnungsdatei.");
        if (LocalFilePaths.IsDevicePath(path)) throw new IOException("Gerätepfade (\\\\?\\, \\\\.\\, \\??\\) werden für die Zuordnungsdatei nicht angenommen.");
        var json = ToJson();
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = full + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(full)) File.Replace(temporary, full, destinationBackupFileName: null);
        else File.Move(temporary, full);
    }

    /// <summary>The German description of an entry's scope.</summary>
    public static string DescribeProject(PasswordMapEntry entry) => entry.Project ?? "Für alle Projekte";

    /// <summary>The German description of an entry's PLC scope.</summary>
    public static string DescribePlc(PasswordMapEntry entry) => entry.Plc ?? "alle PLCs";

    /// <summary>The German description of an entry's source (target or variable name, never a value).</summary>
    public static string DescribeSource(PasswordMapEntry entry)
        => PasswordSources.CredentialTargetOf(entry.Source) is { } target ? "Anmeldeinformation " + target : "Umgebungsvariable " + entry.Source;

    private static bool SameKey(PasswordMapEntry entry, string? project, string? plc, string purpose)
        => string.Equals(entry.Project, project, StringComparison.OrdinalIgnoreCase)
           && string.Equals(entry.Plc, plc, StringComparison.OrdinalIgnoreCase)
           && string.Equals(entry.Purpose, purpose, StringComparison.Ordinal);

    private static string Segment(string? name, string all) => name is null ? all : name.Replace('/', '_').Replace('\\', '_');
}
