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

    public static IReadOnlyList<string> Reads { get; } = Array.AsReadOnly(new[] { ListEvents, ListDynamizations });

    public static IReadOnlyList<string> Writes { get; } = Array.AsReadOnly(new[]
    {
        SetEventScript, DeleteEventScript, SetDynamizationScript, SetDynamizationFlashing, DeleteDynamization,
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
