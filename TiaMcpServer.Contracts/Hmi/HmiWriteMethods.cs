namespace TiaMcpServer.Contracts.Hmi;

public static class HmiWriteMethods
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "hmi_create_tag_table",
        "hmi_update_tag_table",
        "hmi_delete_tag_table",
        "hmi_create_tag",
        "hmi_update_tag",
        "hmi_delete_tag",
        "hmi_create_alarm",
        "hmi_update_alarm",
        "hmi_delete_alarm",
        HmiScreenMethods.CreateScreen,
        HmiScreenMethods.DeleteScreen,
        HmiScreenMethods.RenameScreens,
        HmiScreenMethods.CreateScreenItem,
        HmiScreenMethods.DeleteScreenItem,
        HmiScreenMethods.SetScreenItemProperty,
        HmiScreenMethods.SetDynamizationTag,
        HmiScreenMethods.ReplaceInScripts,
        HmiScreenMethods.RenameTags,
        HmiTextListMethods.ExportTextLists,
        HmiTextListMethods.ImportTextLists,
        HmiClassicMethods.ExportObjects,
        HmiClassicMethods.ImportObjects,
        HmiClassicMethods.DeleteObject,
        HmiClassicMethods.CreateFolder,
        HmiClassicMethods.DeleteFolder,
        HmiScreenEventMethods.SetEventScript,
        HmiScreenEventMethods.DeleteEventScript,
        HmiScreenEventMethods.SetDynamizationScript,
        HmiScreenEventMethods.SetDynamizationFlashing,
        HmiScreenEventMethods.DeleteDynamization,
        HmiUnifiedConfigurationMethods.CreateAuditClass,
        HmiUnifiedConfigurationMethods.DeleteAuditClass,
        HmiUnifiedConfigurationMethods.SetAuditClassProperty,
        HmiUnifiedConfigurationMethods.SetRuntimeSetting,
        HmiUnifiedConfigurationMethods.CreatePlantView,
        HmiUnifiedConfigurationMethods.DeletePlantView,
        HmiUnifiedConfigurationMethods.CreatePlantViewNode,
        HmiUnifiedConfigurationMethods.DeletePlantViewNode,
        HmiUnifiedConfigurationMethods.SetPlantObjectInterfaceProperty,
        HmiUnifiedConfigurationMethods.ResizeScreen,
        HmiUnifiedConfigurationMethods.SetLogDuration,
        HmiUnifiedConfigurationMethods.SetOpcUaAlarmTypeNode,
    });
}

/// <summary>
/// WinCC Unified user text lists as SimaticML files (owner request 2026-11-01); worker method names. The Siemens members
/// are not documented for Unified text lists and are bound at runtime (api_member_missing when absent).
/// </summary>
public static class HmiTextListMethods
{
    public const string ExportTextLists = "hmi_export_text_lists";
    public const string ImportTextLists = "hmi_import_text_lists";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { ExportTextLists, ImportTextLists });
}

/// <summary>
/// The WinCC Unified screen, script and batch rename writes of hmi_write (adopted from BetterTIA/writer); worker
/// method names (the public operation is the name without the hmi_ prefix).
/// </summary>
public static class HmiScreenMethods
{
    public const string CreateScreen = "hmi_create_screen";
    public const string DeleteScreen = "hmi_delete_screen";
    public const string RenameScreens = "hmi_rename_screens";
    public const string CreateScreenItem = "hmi_create_screen_item";
    public const string DeleteScreenItem = "hmi_delete_screen_item";
    public const string SetScreenItemProperty = "hmi_set_screen_item_property";
    public const string SetDynamizationTag = "hmi_set_dynamization_tag";
    public const string ReplaceInScripts = "hmi_replace_in_scripts";
    public const string RenameTags = "hmi_rename_tags";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        CreateScreen, DeleteScreen, RenameScreens, CreateScreenItem, DeleteScreenItem, SetScreenItemProperty,
        SetDynamizationTag, ReplaceInScripts, RenameTags,
    });

    /// <summary>The batch renames run in one ExclusiveAccess transaction and need the Portal handle.</summary>
    public static bool IsBatch(string method) => method is RenameScreens or RenameTags;
}
