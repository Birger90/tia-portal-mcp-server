using TiaMcpServer.Contracts.Hmi;

namespace TiaMcpServer.Hmi;

/// <summary>Shape rules of the WinCC Unified audit class, runtime setting and plant view writes (issue #29).</summary>
public static partial class HmiWriteCatalog
{
    public static readonly IReadOnlySet<string> ConfigurationOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "create_audit_class", "delete_audit_class", "set_audit_class_property", "set_runtime_setting",
        "create_plant_view", "delete_plant_view", "create_plant_view_node", "delete_plant_view_node", "set_plant_object_interface_property",
        "resize_screen", "set_log_duration", "set_opcua_alarm_type_node",
    };

    private static void ValidateConfiguration(HmiWriteOperationRequest r, Action<string> error)
    {
        if (!ConfigurationOperations.Contains(r.Operation)) return;
        if (r.Operation == "create_audit_class" && r.AuditClassName is { } audit && !ValidName(audit)) error("'auditClassName' is invalid.");
        if (r.Operation is "create_plant_view" && r.PlantViewName is { } view && (!ValidName(view) || view.Contains('/'))) error("'plantViewName' is invalid.");
        if (r.NodeName is { } node && (!ValidName(node) || node.Contains('/'))) error("'nodeName' is invalid (no '/', '.', '\"' or control characters).");
        if (r.NodePath is { } path && path.Split('/').Any(string.IsNullOrWhiteSpace)) error("'nodePath' must be '/'-separated node names with no empty segment.");
        if (r.Operation == "set_audit_class_property" && r.PropertyName is { } auditProperty && !HmiUnifiedConfigurationMethods.AuditClassProperties.Contains(auditProperty))
            error($"'propertyName' must be a documented HmiAuditClass property: {string.Join(", ", HmiUnifiedConfigurationMethods.AuditClassProperties)}.");
        if (r.Operation == "set_plant_object_interface_property" && r.PropertyName is { } interfaceProperty && !HmiUnifiedConfigurationMethods.PlantObjectInterfaceProperties.Contains(interfaceProperty))
            error($"'propertyName' must be a documented PlantObjectInterface property: {string.Join(", ", HmiUnifiedConfigurationMethods.PlantObjectInterfaceProperties)}.");
        if (r.SettingsSection is { } section && !HmiUnifiedConfigurationMethods.RuntimeSettingSections.Contains(section))
            error($"'settingsSection' must be one of: {string.Join(", ", HmiUnifiedConfigurationMethods.RuntimeSettingSections)}.");
        if (r.Operation == "set_runtime_setting" && r.PropertyName is { } setting && !setting.All(c => char.IsLetterOrDigit(c) || c == '_'))
            error("'propertyName' must be the Openness property name of a runtime setting, e.g. StartScreen.");
        if (r.Operation == "set_log_duration")
        {
            if (r.PropertyName is { } durationProperty && !HmiUnifiedConfigurationMethods.LogDurationProperties.Contains(durationProperty))
                error($"'propertyName' must be one of {string.Join(", ", HmiUnifiedConfigurationMethods.LogDurationProperties)}.");
            if (r.Value is { } duration && (duration.Split(':') is var parts && (parts.Length is < 4 or > 5
                || parts.Any(p => !uint.TryParse(p, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)))))
                error("'value' must be days:hours:minutes:seconds[:ticks] as non-negative integers (ticks: hundred nanoseconds), e.g. 7:0:0:0.");
        }

        if (r.Culture is not null && !ValidCulture(r.Culture)) error("'culture' must be a culture name, e.g. en-US.");
        if (r.PlantObjectType is not null && string.IsNullOrWhiteSpace(r.PlantObjectType)) error("'plantObjectType' must be nonblank.");
    }
}
