using System.Globalization;
using System.Numerics;
using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Hmi;

/// <summary>Pure request validation and normalization before any worker activity.</summary>
public static partial class HmiWriteCatalog
{
    private sealed record Spec(string[] Required, string[] Optional);
    private static Spec Op(string required, string optional = "") =>
        new(required.Split(' ', StringSplitOptions.RemoveEmptyEntries), optional.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static readonly IReadOnlyDictionary<string, Spec> Specs = new Dictionary<string, Spec>(StringComparer.Ordinal)
    {
        ["create_tag_table"] = Op("tableName", "groupPath"),
        ["update_tag_table"] = Op("tableName newName"),
        ["delete_tag_table"] = Op("tableName"),
        ["create_tag"] = Op("tableName tagName", "connection plcTag dataType comment displayName acquisitionCycle"),
        ["update_tag"] = Op("tagName", "newName plcTag connection dataType comment displayName acquisitionCycle"),
        ["delete_tag"] = Op("tagName"),
        ["create_alarm"] = Op("alarmKind alarmName triggerTag", "condition conditionValue alarmClass priority eventText infoText bitNumber triggerMode"),
        ["update_alarm"] = Op("alarmKind alarmName", "newName triggerTag condition conditionValue alarmClass priority eventText infoText bitNumber triggerMode"),
        ["delete_alarm"] = Op("alarmKind alarmName"),
        ["create_screen"] = Op("screenName", "groupPath width height"),
        ["delete_screen"] = Op("screenName", "groupPath"),
        ["rename_screens"] = Op("renames"),
        ["create_screen_item"] = Op("screenName itemName itemType", "groupPath left top width height"),
        ["delete_screen_item"] = Op("screenName itemName", "groupPath"),
        ["set_screen_item_property"] = Op("screenName propertyName value", "groupPath itemName culture"),
        ["set_dynamization_tag"] = Op("screenName propertyName tagName", "groupPath itemName"),
        ["replace_in_scripts"] = Op("replacements", "screenName groupPath"),
        ["rename_tags"] = Op("renames"),
        ["export_text_lists"] = Op("directoryPath", "textListNames overwrite"),
        ["import_text_lists"] = Op("filePath", "overwrite"),
        // Classic WinCC objects (issue #28).
        ["export_classic_objects"] = Op("classicKind directoryPath", "groupPath tableName objectNames overwrite"),
        ["import_classic_objects"] = Op("classicKind filePath", "groupPath tableName overwrite"),
        ["delete_classic_object"] = Op("classicKind objectName", "groupPath tableName"),
        ["create_classic_folder"] = Op("classicKind groupPath"),
        ["delete_classic_folder"] = Op("classicKind groupPath"),
        // WinCC Unified events and dynamizations (issue #29).
        ["set_event_script"] = Op("screenName eventType scriptCode", "groupPath itemName propertyName globalDefinitionAreaScriptCode async"),
        ["delete_event_script"] = Op("screenName eventType", "groupPath itemName propertyName"),
        ["set_dynamization_script"] = Op("screenName propertyName scriptCode", "groupPath itemName globalDefinitionAreaScriptCode async triggerType triggerTags customDuration"),
        ["set_dynamization_flashing"] = Op("screenName propertyName", "groupPath itemName color alternateColor flashingCondition flashingRate"),
        ["delete_dynamization"] = Op("screenName propertyName", "groupPath itemName"),
        // WinCC Unified audit classes, runtime settings and plant objects (issue #29).
        ["create_audit_class"] = Op("auditClassName"),
        ["delete_audit_class"] = Op("auditClassName"),
        ["set_audit_class_property"] = Op("auditClassName propertyName value", "culture"),
        ["set_runtime_setting"] = Op("propertyName value", "settingsSection culture"),
        ["create_plant_view"] = Op("plantViewName"),
        ["delete_plant_view"] = Op("plantViewName"),
        ["create_plant_view_node"] = Op("plantViewName nodeName", "nodePath plantObjectType"),
        ["delete_plant_view_node"] = Op("plantViewName nodePath"),
        ["set_plant_object_interface_property"] = Op("plantViewName nodePath interfaceName propertyName value", "culture"),
        // Package G (not live-tested).
        ["resize_screen"] = Op("screenName", "groupPath"),
        ["set_log_duration"] = Op("objectName propertyName value"),
        ["set_opcua_alarm_type_node"] = Op("objectName value connection"),
    };

    private static readonly (string Name, Func<HmiWriteOperationRequest, object?> Get)[] Fields =
    {
        ("tableName", r => r.TableName),
        ("groupPath", r => r.GroupPath),
        ("tagName", r => r.TagName),
        ("newName", r => r.NewName),
        ("connection", r => r.Connection),
        ("plcTag", r => r.PlcTag),
        ("dataType", r => r.DataType),
        ("acquisitionCycle", r => r.AcquisitionCycle),
        ("alarmName", r => r.AlarmName),
        ("alarmKind", r => r.AlarmKind),
        ("triggerTag", r => r.TriggerTag),
        ("triggerMode", r => r.TriggerMode),
        ("condition", r => r.Condition),
        ("alarmClass", r => r.AlarmClass),
        ("comment", r => r.Comment),
        ("displayName", r => r.DisplayName),
        ("eventText", r => r.EventText),
        ("infoText", r => r.InfoText),
        ("bitNumber", r => r.BitNumber),
        ("priority", r => r.Priority),
        ("conditionValue", r => r.ConditionValue),
        ("screenName", r => r.ScreenName),
        ("itemName", r => r.ItemName),
        ("itemType", r => r.ItemType),
        ("propertyName", r => r.PropertyName),
        ("value", r => r.Value),
        ("culture", r => r.Culture),
        ("width", r => r.Width),
        ("height", r => r.Height),
        ("left", r => r.Left),
        ("top", r => r.Top),
        ("renames", r => r.Renames),
        ("replacements", r => r.Replacements),
        ("directoryPath", r => r.DirectoryPath),
        ("filePath", r => r.FilePath),
        ("textListNames", r => r.TextListNames),
        ("overwrite", r => r.Overwrite),
        ("classicKind", r => r.ClassicKind),
        ("objectName", r => r.ObjectName),
        ("objectNames", r => r.ObjectNames),
        ("eventType", r => r.EventType),
        ("scriptCode", r => r.ScriptCode),
        ("globalDefinitionAreaScriptCode", r => r.GlobalDefinitionAreaScriptCode),
        ("async", r => r.Async),
        ("triggerType", r => r.TriggerType),
        ("triggerTags", r => r.TriggerTags),
        ("customDuration", r => r.CustomDuration),
        ("color", r => r.Color),
        ("alternateColor", r => r.AlternateColor),
        ("flashingCondition", r => r.FlashingCondition),
        ("flashingRate", r => r.FlashingRate),
        ("auditClassName", r => r.AuditClassName),
        ("settingsSection", r => r.SettingsSection),
        ("plantViewName", r => r.PlantViewName),
        ("nodeName", r => r.NodeName),
        ("nodePath", r => r.NodePath),
        ("plantObjectType", r => r.PlantObjectType),
        ("interfaceName", r => r.InterfaceName),
    };

    public static IReadOnlyList<string> OperationNames { get; } = Specs.Keys.ToArray();

    public static OperationValidation ValidateWrite(IReadOnlyList<HmiWriteOperationRequest>? operations)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in operations!)
        {
            if (OperationBatchRules.CheckItem(r, ids) is { } itemError) { errors.Add(itemError); continue; }
            void Error(string message) => errors.Add($"Operation '{r.Operation}' (operationId '{r.OperationId}'): {message}");
            if (r.Operation is null || !Specs.TryGetValue(r.Operation, out var spec))
            { Error("Unknown operation."); continue; }
            foreach (var (name, get) in Fields)
            {
                var value = get(r);
                if (value is not null && !spec.Required.Contains(name) && !spec.Optional.Contains(name))
                    Error($"'{name}' is not valid for this operation.");
                if (spec.Required.Contains(name) && (value is null || value is string s && string.IsNullOrWhiteSpace(s)))
                    Error($"'{name}' is required.");
                if (value is string str && string.IsNullOrWhiteSpace(str))
                    Error($"'{name}' must be nonblank when supplied.");
            }
            if (r.HmiName is not null && string.IsNullOrWhiteSpace(r.HmiName)) Error("'hmiName' must be nonblank.");
            if (r.GroupPath is not null && r.GroupPath.Split('/').Any(string.IsNullOrWhiteSpace))
                Error("'groupPath' must have no empty segment.");
            if (r.NewName is not null && !ValidName(r.NewName)) Error("'newName' is invalid.");
            var createdName = r.Operation switch
            {
                "create_tag_table" => r.TableName, "create_tag" => r.TagName, "create_alarm" => r.AlarmName, _ => null,
            };
            if (createdName is not null && !ValidName(createdName)) Error("New object name is invalid.");
            if (r.Operation is "create_tag" or "update_tag")
            {
                if (r.Connection is not null && r.PlcTag is null) Error("'connection' requires 'plcTag'.");
                if (r.Operation == "create_tag" && r.PlcTag is null && r.DataType is null) Error("Create requires plcTag or dataType.");
            }
            if (r.AlarmKind is not null && r.AlarmKind is not ("discrete" or "analog")) Error("'alarmKind' must be discrete or analog.");
            if (r.AlarmKind == "analog")
            {
                if (r.BitNumber is not null || r.TriggerMode is not null) Error("Analog alarms cannot specify bitNumber or triggerMode.");
                if (r.Operation == "create_alarm" && (r.Condition is null || r.ConditionValue is null))
                    Error("Analog create requires condition and conditionValue.");
            }
            if (r.AlarmKind == "discrete" && (r.Condition is not null || r.ConditionValue is not null))
                Error("Discrete alarms cannot specify condition or conditionValue.");
            if (r.AcquisitionCycle is not null && r.AcquisitionCycle is not ("T100ms" or "T250ms" or "T500ms" or "T1s" or "T2s" or "T5s" or "T10s"))
                Error("'acquisitionCycle' is invalid.");
            if (r.TriggerMode is not null && r.TriggerMode is not ("OnRisingEdge" or "OnFallingEdge")) Error("'triggerMode' is invalid.");
            if (r.Condition is not null && r.Condition is not ("LowerLimit" or "UpperLimit" or "Equal" or "NotEqual" or "LowerLimitOrEqual" or "UpperLimitOrEqual"))
                Error("'condition' is invalid.");
            if (r.Priority is < 0 or > 255) Error("'priority' must be 0 to 255.");
            if (r.BitNumber is < 0) Error("'bitNumber' must be nonnegative.");
            if (r.ConditionValue is { } number && !TryNumber(number, out _)) Error("'conditionValue' must be a finite JSON number.");
            ValidateScreenItem(r, Error);
            ValidateClassic(r, Error);
            ValidateEvents(r, Error);
            ValidateConfiguration(r, Error);
            foreach (var (name, texts) in new[] { ("comment", r.Comment), ("displayName", r.DisplayName), ("eventText", r.EventText), ("infoText", r.InfoText) })
            {
                if (texts is null) continue;
                if (texts.Count == 0) Error($"'{name}' must not be empty.");
                var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var text in texts)
                {
                    if (text is null || text.Text is null || !ValidCulture(text.Culture))
                    { Error($"'{name}' requires culture and text."); continue; }
                    if (!cultures.Add(CultureInfo.GetCultureInfo(text.Culture).Name)) Error($"'{name}' contains duplicate culture.");
                    if (text.Text.StartsWith("<body", StringComparison.OrdinalIgnoreCase)) Error("Send plain text; the server adds markup.");
                    if (name == "eventText" && text.Text.IndexOfAny(['\r', '\n']) >= 0) Error("'eventText' cannot contain line breaks.");
                }
            }
        }
        return errors.Count == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    private static bool ValidName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name == name.Trim() && !name.Any(c => char.IsControl(c) || c is '.' or '"');

    private static bool ValidCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return false;
        try { return CultureInfo.GetCultureInfo(culture).Name.Length != 0; }
        catch (CultureNotFoundException) { return false; }
    }

    private static bool TryNumber(JsonElement number, out double value)
    {
        value = 0;
        return number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out value) && double.IsFinite(value);
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<HmiWriteOperationRequest> operations, McpAccessMode mode) =>
        operations.Where(r => r is not null && r.Operation is not null && Specs.ContainsKey(r.Operation)
            && !OperationPolicyCatalog.IsAllowed(mode, HmiOperationCatalog.WorkerMethod(r.Operation)))
            .Select(r => $"Operation '{r.Operation}' (operationId '{r.OperationId}') is not permitted in {McpAccessModeNames.ToName(mode)} mode.").ToArray();

    public static string WrapEventText(string plain) => "<body><p>" + plain.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;") + "</p></body>";

    internal static string? NormalizePlcTag(string? value)
    {
        if (value is null || !value.StartsWith('"')) return value;
        var end = value.IndexOf('"', 1);
        return end > 1 && end + 1 < value.Length && value[end + 1] == '.'
            ? value[1..end] + value[(end + 1)..] : value;
    }

    public static HmiWriteRequestInfo ToWorkerRequest(HmiWriteOperationRequest r, string resolvedHmiName)
    {
        var result = new HmiWriteRequestInfo
        {
            HmiName = resolvedHmiName,
            TableName = r.TableName,
            GroupPath = r.GroupPath,
            TagName = r.TagName,
            NewName = r.NewName,
            Connection = r.Connection,
            PlcTag = r.PlcTag,
            DataType = r.DataType,
            AcquisitionCycle = r.AcquisitionCycle,
            AlarmName = r.AlarmName,
            AlarmKind = r.AlarmKind,
            TriggerTag = r.TriggerTag,
            TriggerMode = r.TriggerMode,
            Condition = r.Condition,
            AlarmClass = r.AlarmClass,
            Comment = r.Comment,
            DisplayName = r.DisplayName,
            InfoText = r.InfoText,
            EventText = r.EventText?.Select(t => new HmiText(t.Culture, WrapEventText(t.Text))).ToList(),
            BitNumber = r.BitNumber,
            Priority = r.Priority,
            ScreenName = r.ScreenName,
            ItemName = r.ItemName,
            ItemType = r.ItemType,
            PropertyName = r.PropertyName,
            Value = r.Value,
            Culture = r.Culture,
            Width = r.Width,
            Height = r.Height,
            Left = r.Left,
            Top = r.Top,
            Renames = r.Renames,
            Replacements = r.Replacements,
            DirectoryPath = r.DirectoryPath,
            FilePath = r.FilePath,
            TextListNames = r.TextListNames,
            Overwrite = r.Overwrite,
            ClassicKind = r.ClassicKind,
            ObjectName = r.ObjectName,
            ObjectNames = r.ObjectNames,
            EventType = r.EventType,
            ScriptCode = r.ScriptCode,
            GlobalDefinitionAreaScriptCode = r.GlobalDefinitionAreaScriptCode,
            Async = r.Async,
            TriggerType = r.TriggerType,
            TriggerTags = r.TriggerTags,
            CustomDuration = r.CustomDuration,
            Color = r.Color,
            AlternateColor = r.AlternateColor,
            FlashingCondition = r.FlashingCondition,
            FlashingRate = r.FlashingRate,
            AuditClassName = r.AuditClassName,
            SettingsSection = r.SettingsSection,
            PlantViewName = r.PlantViewName,
            NodeName = r.NodeName,
            NodePath = r.NodePath,
            PlantObjectType = r.PlantObjectType,
            InterfaceName = r.InterfaceName,
            // A fresh stamp per attempt keeps the temporary names of a batch rename unique.
            TemporaryStamp = r.Operation is "rename_screens" or "rename_tags" ? Guid.NewGuid().ToString("N").Substring(0, 8) : null,
        };
        if (r.ConditionValue is { } number)
        {
            if (!TryNumber(number, out var value)) throw new ArgumentException("conditionValue must be a finite JSON number.");
            // Classify the exact token before Double rounding can hide a fractional component.
            if (BigInteger.TryParse(number.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var integer)
                && integer >= int.MinValue && integer <= int.MaxValue)
                result.ConditionValueInt32 = (int)integer;
            else result.ConditionValueDouble = value;
        }
        return result;
    }
}
