namespace TiaMcpServer.Hmi;

/// <summary>Shape rules of the WinCC Unified event and dynamization writes (issue #29).</summary>
public static partial class HmiWriteCatalog
{
    public static readonly IReadOnlySet<string> EventOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "set_event_script", "delete_event_script", "set_dynamization_script", "set_dynamization_flashing", "delete_dynamization",
    };

    public const int MaxTriggerTags = 50;

    /// <summary>Script code is sent as given; the cap keeps one item inside the default 60,000-character value budget.</summary>
    public const int MaxScriptCodeChars = 60_000;

    private static void ValidateEvents(HmiWriteOperationRequest r, Action<string> error)
    {
        if (!EventOperations.Contains(r.Operation)) return;
        foreach (var (name, value) in new[] { ("screenName", r.ScreenName), ("itemName", r.ItemName), ("propertyName", r.PropertyName) })
            if (value is not null && !ValidName(value)) error($"'{name}' is invalid.");
        foreach (var (name, value) in new[] { ("eventType", r.EventType), ("triggerType", r.TriggerType), ("flashingCondition", r.FlashingCondition), ("flashingRate", r.FlashingRate) })
            if (value is not null && !value.All(c => char.IsLetterOrDigit(c) || c == '_'))
                error($"'{name}' must be an Openness enum member name, e.g. Tapped, Change, T1s, Always, Fast.");
        foreach (var (name, value) in new[] { ("scriptCode", r.ScriptCode), ("globalDefinitionAreaScriptCode", r.GlobalDefinitionAreaScriptCode) })
            if (value is { Length: > MaxScriptCodeChars }) error($"'{name}' must be at most {MaxScriptCodeChars} characters.");
        if (r.TriggerTags is { } tags && (tags.Count is 0 or > MaxTriggerTags || tags.Any(string.IsNullOrWhiteSpace)))
            error($"'triggerTags' must hold 1 to {MaxTriggerTags} nonblank tag names.");
        if (r.CustomDuration is not null && r.TriggerType is not null && r.TriggerType != "CustomCycle")
            error("'customDuration' applies to triggerType CustomCycle.");
        foreach (var (name, value) in new[] { ("color", r.Color), ("alternateColor", r.AlternateColor) })
            if (value is not null && !IsColor(value)) error($"'{name}' must be a colour #RRGGBB or #AARRGGBB.");
    }

    private static bool IsColor(string value)
    {
        var hex = value.StartsWith('#') ? value[1..] : value;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }
}
