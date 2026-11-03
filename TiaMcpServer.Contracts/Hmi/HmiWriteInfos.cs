namespace TiaMcpServer.Contracts.Hmi;

public sealed class HmiWriteRequestInfo
{
    public string? HmiName { get; set; }
    public string? TableName { get; set; }
    public string? GroupPath { get; set; }
    public string? TagName { get; set; }
    public string? NewName { get; set; }
    public string? Connection { get; set; }
    public string? PlcTag { get; set; }
    public string? DataType { get; set; }
    public string? AcquisitionCycle { get; set; }
    public string? AlarmName { get; set; }
    public string? AlarmKind { get; set; }
    public string? TriggerTag { get; set; }
    public string? TriggerMode { get; set; }
    public string? Condition { get; set; }
    public string? AlarmClass { get; set; }
    public List<HmiText>? Comment { get; set; }
    public List<HmiText>? DisplayName { get; set; }
    public List<HmiText>? EventText { get; set; }
    public List<HmiText>? InfoText { get; set; }
    public int? BitNumber { get; set; }
    public int? Priority { get; set; }
    public int? ConditionValueInt32 { get; set; }
    public double? ConditionValueDouble { get; set; }
    public string? ScreenName { get; set; }
    public string? ItemName { get; set; }
    public string? ItemType { get; set; }
    public string? PropertyName { get; set; }
    public string? Value { get; set; }
    public string? Culture { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Left { get; set; }
    public int? Top { get; set; }
    public List<Plc.PlcNameRenameInfo>? Renames { get; set; }
    public List<Plc.Interface.PlcTextReplacementInfo>? Replacements { get; set; }
    public string? TemporaryStamp { get; set; }

    /// <summary>export_text_lists: the existing directory that receives one &lt;name&gt;.xml per text list.</summary>
    public string? DirectoryPath { get; set; }

    /// <summary>import_text_lists: the SimaticML file to import (a private copy is imported).</summary>
    public string? FilePath { get; set; }

    /// <summary>export_text_lists: only these user text lists (omitted: every user text list).</summary>
    public List<string>? TextListNames { get; set; }

    /// <summary>export_text_lists: replace existing files; import_text_lists: <c>ImportOptions.Override</c>.</summary>
    public bool? Overwrite { get; set; }

    /// <summary>Classic WinCC writes (issue #28): the object kind (<see cref="HmiClassicKinds"/>).</summary>
    public string? ClassicKind { get; set; }

    /// <summary>delete_classic_object: the object (a slide-in screen is named by its SlideinType).</summary>
    public string? ObjectName { get; set; }

    /// <summary>export_classic_objects: only these objects (omitted: every object of the kind in groupPath/tableName).</summary>
    public List<string>? ObjectNames { get; set; }

    // WinCC Unified events and dynamizations (issue #29).
    public string? EventType { get; set; }
    public string? ScriptCode { get; set; }
    public string? GlobalDefinitionAreaScriptCode { get; set; }
    public bool? Async { get; set; }
    public string? TriggerType { get; set; }
    public List<string>? TriggerTags { get; set; }
    public string? CustomDuration { get; set; }
    public string? Color { get; set; }
    public string? AlternateColor { get; set; }
    public string? FlashingCondition { get; set; }
    public string? FlashingRate { get; set; }

    // WinCC Unified audit classes, runtime settings and plant objects (issue #29).
    public string? AuditClassName { get; set; }
    public string? SettingsSection { get; set; }
    public string? PlantViewName { get; set; }
    public string? NodeName { get; set; }
    public string? NodePath { get; set; }
    public string? PlantObjectType { get; set; }
    public string? InterfaceName { get; set; }
}

public sealed record HmiWriteOutcomeInfo
{
    public string ObjectName { get; set; } = string.Empty;
    public List<string> AppliedProperties { get; set; } = new();
    public string? FailedProperty { get; set; }
    public string? Cleanup { get; set; }
    public string? CleanupMessage { get; set; }
}

public static class HmiCleanupStates
{
    public const string NotNeeded = "notNeeded";
    public const string Deleted = "deleted";
    public const string Failed = "failed";
}
