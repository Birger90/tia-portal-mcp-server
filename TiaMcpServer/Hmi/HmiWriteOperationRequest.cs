using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Hmi;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class HmiWriteOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
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
    public JsonElement? ConditionValue { get; set; }

    // WinCC Unified screens, scripts and batch renames (adopted from BetterTIA/writer).
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
    public List<TiaMcpServer.Contracts.Plc.PlcNameRenameInfo>? Renames { get; set; }
    public List<TiaMcpServer.Contracts.Plc.Interface.PlcTextReplacementInfo>? Replacements { get; set; }

    // WinCC Unified user text lists as SimaticML files (owner request 2026-11-01).
    public string? DirectoryPath { get; set; }
    public string? FilePath { get; set; }
    public List<string>? TextListNames { get; set; }
    public bool? Overwrite { get; set; }

    // Classic WinCC objects (issue #28).
    public string? ClassicKind { get; set; }
    public string? ObjectName { get; set; }
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
