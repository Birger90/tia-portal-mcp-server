namespace TiaMcpServer.Contracts.Hmi;

/// <summary>
/// WinCC Unified audit classes, runtime settings and plant objects (issue #29); worker method names. Evidence: V21
/// <c>Siemens.Engineering.WinCCUnified.xml</c>: <c>HmiAudit.HmiAlarmAuditClassComposition.Create()/Find(String)</c>,
/// <c>HmiAlarmAuditClass.Delete()</c>, <c>HmiAuditClass.Name/DisplayName/CommentRequired/ConfirmationMode/RequiredFunctionRights</c>;
/// <c>RuntimeSettings.HmiRuntimeSetting</c> and its section properties; <c>Cpm.PlantViewsProvider.PlantViews</c>,
/// <c>PlantViewComposition.Create/Find(String)</c>, <c>PlantView.Name/AssignedHmiDevice/PlantViewNodes/Delete()</c>,
/// <c>PlantViewNodeComposition.Create(String)/Create(String, String)/Find(String)</c>, <c>PlantViewNode.Name/ViewPath/
/// PlantObject/PlantViewNodes/Delete()</c>, <c>PlantObject.Type/PlantObjectInterfaces</c>, <c>PlantObjectInterface</c> properties.
/// </summary>
public static class HmiUnifiedConfigurationMethods
{
    public const string ListPlantViews = "hmi_list_plant_views";
    public const string CreateAuditClass = "hmi_create_audit_class";
    public const string DeleteAuditClass = "hmi_delete_audit_class";
    public const string SetAuditClassProperty = "hmi_set_audit_class_property";
    public const string SetRuntimeSetting = "hmi_set_runtime_setting";
    public const string CreatePlantView = "hmi_create_plant_view";
    public const string DeletePlantView = "hmi_delete_plant_view";
    public const string CreatePlantViewNode = "hmi_create_plant_view_node";
    public const string DeletePlantViewNode = "hmi_delete_plant_view_node";
    public const string SetPlantObjectInterfaceProperty = "hmi_set_plant_object_interface_property";

    public static IReadOnlyList<string> Reads { get; } = Array.AsReadOnly(new[] { ListPlantViews });

    public static IReadOnlyList<string> Writes { get; } = Array.AsReadOnly(new[]
    {
        CreateAuditClass, DeleteAuditClass, SetAuditClassProperty, SetRuntimeSetting,
        CreatePlantView, DeletePlantView, CreatePlantViewNode, DeletePlantViewNode, SetPlantObjectInterfaceProperty,
    });

    /// <summary>The documented writable-looking properties of <c>HmiAuditClass</c> (V21); <c>IsGmpEnabled</c> is a check, not a setting.</summary>
    public static IReadOnlyList<string> AuditClassProperties { get; } = Array.AsReadOnly(new[]
    {
        "Name", "DisplayName", "CommentRequired", "ConfirmationMode", "RequiredFunctionRights",
    });

    /// <summary>The documented section properties of <c>HmiRuntimeSetting</c> (V21); omitted section: the runtime settings themselves.</summary>
    public static IReadOnlyList<string> RuntimeSettingSections { get; } = Array.AsReadOnly(new[]
    {
        "HmiExclusiveOperationSettings", "HmiReportingSettings", "HmiUnifiedTagSettings", "HmiUpssRuntimeSettings",
        "MaxLoginRuntimeSettings", "OpcUaServerRuntimeSettings", "ProcessDiagnosticsRuntimeSettings", "RuntimeResourceSettings",
        "ScreenResolution", "TelemetryRuntimeSettings",
    });

    /// <summary>The documented properties of <c>PlantObjectInterface</c> (V21) other than its compositions.</summary>
    public static IReadOnlyList<string> PlantObjectInterfaceProperties { get; } = Array.AsReadOnly(new[]
    {
        "AccessMode", "AcquisitionCycle", "AcquisitionMode", "Comment", "Connection", "DataType", "HmiDataType", "MaxLength",
        "Name", "Persistent", "PlcName", "PlcTag",
    });

    public const string PlantViewsProviderType = "Siemens.Engineering.HmiUnified.Cpm.PlantViewsProvider";
}

/// <summary>Result of <c>list_plant_views</c>.</summary>
public class HmiPlantViewsInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    /// <summary>Where the PlantViewsProvider was found: <c>hmiSoftware</c> or <c>project</c>.</summary>
    public string? ProviderOwner { get; set; }

    public List<HmiPlantViewInfo> PlantViews { get; set; } = new List<HmiPlantViewInfo>();
}

public class HmiPlantViewInfo
{
    public string Name { get; set; } = string.Empty;

    public string? AssignedHmiDevice { get; set; }

    /// <summary>Every node, depth first, with its slash-separated path below the view.</summary>
    public List<HmiPlantViewNodeInfo> Nodes { get; set; } = new List<HmiPlantViewNodeInfo>();

    public bool NodesTruncated { get; set; }
}

public class HmiPlantViewNodeInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Slash-separated node names below the view (the path the write operations take).</summary>
    public string NodePath { get; set; } = string.Empty;

    /// <summary><c>PlantViewNode.ViewPath</c> as TIA Portal reports it.</summary>
    public string? ViewPath { get; set; }

    public string? PlantObjectType { get; set; }

    public List<string> Interfaces { get; set; } = new List<string>();
}
