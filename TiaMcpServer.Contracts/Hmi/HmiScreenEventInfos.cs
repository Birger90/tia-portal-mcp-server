namespace TiaMcpServer.Contracts.Hmi;

/// <summary>
/// WinCC Unified events and dynamizations of screens and screen items (issue #29); worker method names. Evidence: V21
/// <c>Siemens.Engineering.WinCCUnified.xml</c> (<c>UI.Events.*EventHandlerComposition.Create/Find(…EventType)</c>,
/// <c>PropertyEventHandlerComposition.Create/Find(String, PropertyEventType)</c>, <c>*EventHandler.Script/Delete</c>,
/// <c>IHmiScript.ScriptCode/GlobalDefinitionAreaScriptCode/Async</c>, <c>DynamizationBaseComposition.Create&lt;T&gt;(String)/Find</c>,
/// <c>ScriptDynamization.Trigger</c>, <c>Trigger.Type/Tags/CustomDuration</c>, <c>FlashingDynamization.Color/AlternateColor/
/// FlashingCondition/FlashingRate</c>, <c>DynamizationBase.Delete</c>). Bound at runtime: the compile stubs do not carry them.
/// </summary>
public static class HmiScreenEventMethods
{
    public const string ListEvents = "hmi_list_screen_item_events";
    public const string ListDynamizations = "hmi_list_screen_item_dynamizations";
    public const string SetEventScript = "hmi_set_event_script";
    public const string DeleteEventScript = "hmi_delete_event_script";
    public const string SetDynamizationScript = "hmi_set_dynamization_script";
    public const string SetDynamizationFlashing = "hmi_set_dynamization_flashing";
    public const string DeleteDynamization = "hmi_delete_dynamization";

    /// <summary>
    /// The value converter of a tag or expression dynamization: <c>ValueConverter.Formula</c>/<c>IsFormulaSelected</c> and the
    /// <c>MappingTable</c> (<c>ConditionType</c>, <c>Entries</c> of <c>MappingTableEntryRange</c>/<c>Simple</c>/<c>Bitmask</c>).
    /// Evidence: the V21 type list in <c>totally-integrated-claude</c> <c>skills/tia-hmi-operations/references/unified-dynamization.md</c>
    /// (generated from <c>Siemens.Engineering.WinCCUnified.xml</c>); idea from jessenic/TIA-MCP-Server (MIT). Bound at runtime.
    /// </summary>
    public const string SetDynamizationValueConverter = "hmi_set_dynamization_value_converter";

    public static IReadOnlyList<string> Reads { get; } = Array.AsReadOnly(new[] { ListEvents, ListDynamizations });

    public static IReadOnlyList<string> Writes { get; } = Array.AsReadOnly(new[]
    {
        SetEventScript, DeleteEventScript, SetDynamizationScript, SetDynamizationFlashing, DeleteDynamization, SetDynamizationValueConverter,
    });
}

/// <summary>Result of <c>list_screen_item_events</c>: the event and property event handlers of one screen or screen item.</summary>
public class HmiScreenItemEventsInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public string ScreenName { get; set; } = string.Empty;

    /// <summary>The screen item; null when the screen itself was read.</summary>
    public string? ItemName { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public List<HmiEventScriptInfo> Events { get; set; } = new List<HmiEventScriptInfo>();

    public List<HmiEventScriptInfo> PropertyEvents { get; set; } = new List<HmiEventScriptInfo>();
}

public class HmiEventScriptInfo
{
    public string EventType { get; set; } = string.Empty;

    /// <summary>The property of a property event; null for an event.</summary>
    public string? PropertyName { get; set; }

    public string? ScriptCode { get; set; }

    public string? GlobalDefinitionAreaScriptCode { get; set; }

    public bool? Async { get; set; }
}

/// <summary>Result of <c>list_screen_item_dynamizations</c>: the dynamizations of one screen or screen item.</summary>
public class HmiScreenItemDynamizationsInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public string ScreenName { get; set; } = string.Empty;

    public string? ItemName { get; set; }

    public string TypeName { get; set; } = string.Empty;

    public List<HmiDynamizationInfo> Dynamizations { get; set; } = new List<HmiDynamizationInfo>();
}

public class HmiDynamizationInfo
{
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>The runtime type, e.g. <c>TagDynamization</c>, <c>ScriptDynamization</c>, <c>FlashingDynamization</c>.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary><c>DynamizationBase.DynamizationType</c> (Tag, Script, Flashing, Expression, ResourceList, TagParameter, None).</summary>
    public string? DynamizationType { get; set; }

    /// <summary>The documented properties of the type that the dynamization has, as text (Trigger members as <c>Trigger.Type</c> ...).</summary>
    public List<HmiNamedValueInfo> Values { get; set; } = new List<HmiNamedValueInfo>();
}

public class HmiNamedValueInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }
}

/// <summary>
/// One mapping table entry of <c>set_dynamization_value_converter</c>. <c>Kind</c> picks the entry type:
/// <c>range</c> (<c>MappingTableEntryRange.From/To/RangeType</c>), <c>simple</c> (<c>MappingTableEntrySimple.Condition</c>) or
/// <c>bitmask</c> (<c>MappingTableEntryBitmask</c>, created with <c>Entries.Create(BitDynamizationType)</c>; <c>Condition</c>
/// as an unsigned number). Every entry takes <c>Value</c>, <c>AlternateValue</c>, <c>Flashing</c> and <c>FlashingRate</c>
/// (<c>MappingTableEntryBase</c>). Values are text, converted on the worker to the runtime type of the live member.
/// The text members are declared non-nullable so the tool schema stays within the client depth limit; an omitted one
/// is null and is not written. <c>Flashing</c> is written only when true (every entry is new).
/// </summary>
public class HmiMappingEntryInfo
{
    public string Kind { get; set; } = string.Empty;

    public string From { get; set; } = null!;

    public string To { get; set; } = null!;

    public string RangeType { get; set; } = null!;

    public string Condition { get; set; } = null!;

    public string BitDynamizationType { get; set; } = null!;

    public string Value { get; set; } = null!;

    public string AlternateValue { get; set; } = null!;

    public bool Flashing { get; set; }

    public string FlashingRate { get; set; } = null!;
}
