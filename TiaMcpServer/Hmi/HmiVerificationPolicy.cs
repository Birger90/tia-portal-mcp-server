using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Hmi;

/// <summary>
/// Flat per-object keys for hmi_write supersession: a rename owns its old and new names, and a table
/// delete also offers the tags its effect removes. Nothing else crosses kinds (D4).
/// </summary>
public sealed class HmiVerificationPolicy(IReadOnlyDictionary<string, HmiWriteEffect> effects)
    : IWriteVerificationPolicy<HmiWriteOperationRequest>
{
    public string ReadTool => "hmi_read";
    public bool HasReadBack(HmiWriteOperationRequest item) => false;
    public Task<WriteOperationVerification> ReadBackAsync(HmiWriteOperationRequest item, StructuredOperationFailure? failure)
        => throw new NotSupportedException();

    public IReadOnlyList<string> ObjectKeys(HmiWriteOperationRequest item, bool asLater)
    {
        // K59: an export writes files outside the project and changes no HMI object; it owns only its directory, so it never
        // supersedes an earlier import or change of the text lists, and a later change never supersedes the export.
        if (item.Operation == "export_text_lists")
            return new[] { "hmi\nexportdirectory\n" + (item.DirectoryPath ?? string.Empty).Replace('/', '\\').TrimEnd('\\').ToUpperInvariant() };
        var effect = effects[item.OperationId];
        var target = effect.Target;
        var keys = new[] { target.Name, item.NewName }.OfType<string>().Select(name => Key(target, target.Kind, name));
        if (asLater && item.Operation == "delete_tag_table" && effect.Removes is { } removal)
            keys = keys.Concat(removal.Tags.Select(tag => Key(target, "Tag", tag)));
        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Key(HmiTargetIdentity target, string kind, string name)
    {
        var prefix = $"hmi\n{target.HmiName.ToUpperInvariant()}\n";
        var upper = name.ToUpperInvariant();
        return kind switch
        {
            "TagTable" => $"{prefix}table\n{upper}",
            "Tag" => $"{prefix}tag\n{upper}",
            "Alarm" => $"{prefix}alarm\n{target.AlarmKind}\n{upper}",
            // Screens, items and scripts: one key per target; batches and script replacements own a key of their own.
            "Screen" => $"{prefix}screen\n{upper}",
            "ScreenItem" => $"{prefix}screenitem\n{upper}",
            "Scripts" or "ScreenBatch" or "TagBatch" or "TextLists" => $"{prefix}{kind.ToLowerInvariant()}\n{upper}",
            // Classic WinCC (issue #28): the name carries the classic kind and scope.
            "ClassicObjects" or "ClassicObject" or "ClassicFolder" => $"{prefix}{kind.ToLowerInvariant()}\n{upper}",
            // Unified audit classes, runtime settings and plant views (issue #29).
            "AuditClass" or "RuntimeSettings" or "PlantView" or "PlantViewNode" => $"{prefix}{kind.ToLowerInvariant()}\n{upper}",
            _ => throw new NotSupportedException(),
        };
    }
}
