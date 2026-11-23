namespace TiaMcpServer.Hmi;

/// <summary>Shape rules of the WinCC Unified event and dynamization writes (issue #29).</summary>
public static partial class HmiWriteCatalog
{
    public static readonly IReadOnlySet<string> EventOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "set_event_script", "delete_event_script", "set_dynamization_script", "set_dynamization_flashing", "delete_dynamization",
        "set_dynamization_value_converter",
    };

    public const int MaxMappingEntries = 100;

    public const int MaxMappingValueChars = 4_000;

    private static readonly IReadOnlySet<string> MappingEntryKinds = new HashSet<string>(StringComparer.Ordinal) { "range", "simple", "bitmask" };

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
        if (r.Operation == "set_dynamization_value_converter") ValidateValueConverter(r, error);
    }

    private static bool IsEnumName(string value) => value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static void ValidateValueConverter(HmiWriteOperationRequest r, Action<string> error)
    {
        if (r.Formula is null && r.FormulaSelected is null && r.ConditionType is null && r.MappingEntries is null)
            error("set_dynamization_value_converter needs formula, formulaSelected, conditionType or mappingEntries.");
        if (r.Formula is { Length: > MaxScriptCodeChars }) error($"'formula' must be at most {MaxScriptCodeChars} characters.");
        if (r.ConditionType is not null && !IsEnumName(r.ConditionType))
            error("'conditionType' must be an Openness enum member name, e.g. Range, Simple, Bitmask, Singlebit.");
        if (r.MappingEntries is not { } entries) return;
        if (entries.Count is 0 or > MaxMappingEntries) { error($"'mappingEntries' must hold 1 to {MaxMappingEntries} entries."); return; }
        if (entries.Any(e => e is null)) { error("'mappingEntries' must not hold null entries."); return; }
        if (entries.Any(e => e.Kind == "bitmask") && entries.Any(e => e.Kind != "bitmask"))
            error("'mappingEntries' cannot mix bitmask entries with range or simple entries.");
        if (entries.Where(e => e.Kind == "bitmask").Select(e => e.BitDynamizationType).Distinct(StringComparer.Ordinal).Count() > 1)
            error("Every bitmask entry needs the same bitDynamizationType.");
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var at = $"mappingEntries[{i}]";
            if (!MappingEntryKinds.Contains(e.Kind)) { error($"'{at}.kind' must be range, simple or bitmask."); continue; }
            if (e.Kind == "range" && (e.From is null || e.To is null)) error($"'{at}' (range) needs from and to.");
            if (e.Kind == "simple" && e.Condition is null) error($"'{at}' (simple) needs condition.");
            if (e.Kind == "bitmask" && e.BitDynamizationType is null) error($"'{at}' (bitmask) needs bitDynamizationType, e.g. SingleBit or MultiBit.");
            if (e.Kind != "range" && (e.From is not null || e.To is not null || e.RangeType is not null)) error($"'{at}': from, to and rangeType apply to range entries.");
            if (e.Kind == "range" && e.Condition is not null) error($"'{at}': condition applies to simple and bitmask entries.");
            if (e.Kind != "bitmask" && e.BitDynamizationType is not null) error($"'{at}': bitDynamizationType applies to bitmask entries.");
            foreach (var (name, value) in new[] { ("rangeType", e.RangeType), ("bitDynamizationType", e.BitDynamizationType), ("flashingRate", e.FlashingRate) })
                if (value is not null && !IsEnumName(value)) error($"'{at}.{name}' must be an Openness enum member name.");
            foreach (var (name, value) in new[] { ("from", e.From), ("to", e.To), ("condition", e.Condition), ("value", e.Value), ("alternateValue", e.AlternateValue) })
                if (value is { Length: > MaxMappingValueChars }) error($"'{at}.{name}' must be at most {MaxMappingValueChars} characters.");
        }
    }

    private static bool IsColor(string value)
    {
        var hex = value.StartsWith('#') ? value[1..] : value;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }
}
