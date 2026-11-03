using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Hmi.HmiGuardDefinitions;

namespace TiaMcpServer.Hmi;

/// <summary>
/// The WinCC Unified audit class, runtime setting and plant view writes (issue #29) in the HMI working state: the worker
/// resolves the targets, so the effect lists the request; a plant view node item depends on the latest earlier item on
/// its plant view.
/// </summary>
internal sealed partial class HmiWorkingState
{
    private static readonly HashSet<string> GmpRuntimeSettings = new(StringComparer.Ordinal) { "GMPEnabled", "GeneralESIGCommentsStrategy" };

    private HmiWriteEffect ResolveConfiguration(HmiWriteOperationRequest r, List<FiredGuard> guards)
    {
        var changes = new List<HmiFieldChange>();
        void Change(string field, string? current, string? requested) => changes.Add(new(field, current, requested, false));
        var property = r.Culture is null ? r.PropertyName : $"{r.PropertyName}[{r.Culture}]";
        HmiTargetIdentity target;
        var dependencies = new List<HmiTargetIdentity>();
        switch (r.Operation)
        {
            case "create_audit_class":
            case "delete_audit_class":
            case "set_audit_class_property":
                target = Identity("AuditClass", r.AuditClassName!);
                if (r.Operation == "create_audit_class") Change("name", null, r.AuditClassName);
                else if (r.Operation == "delete_audit_class")
                {
                    Change("delete", r.AuditClassName, null);
                    guards.Add(Fire(DeletesConfiguration, r, $"Deleting alarm audit class '{r.AuditClassName}' removes it; alarms that name it are not changed."));
                }
                else
                {
                    Change(property!, null, r.Value);
                    guards.Add(Fire(AuditPropertyUnverified, r, $"set_audit_class_property reads '{r.PropertyName}' of the audit class before it writes it; audit properties are not verified live."));
                }

                break;
            case "set_runtime_setting":
                target = Identity("RuntimeSettings", r.SettingsSection ?? "(runtime settings)");
                Change(property!, null, r.Value);
                if (GmpRuntimeSettings.Contains(r.PropertyName!))
                    guards.Add(Fire(AuditPropertyUnverified, r, $"'{r.PropertyName}' is a GMP setting; some devices do not support it (hmi_read get_runtime_settings reports it null there)."));
                break;
            case "create_plant_view":
            case "delete_plant_view":
                target = Identity("PlantView", r.PlantViewName!);
                if (r.Operation == "create_plant_view") Change("name", null, r.PlantViewName);
                else
                {
                    Change("delete", r.PlantViewName, null);
                    guards.Add(Fire(DeletesConfiguration, r, $"Deleting plant view '{r.PlantViewName}' removes it with its nodes."));
                }

                break;
            default:
                var path = r.Operation == "create_plant_view_node" ? (r.NodePath is null ? r.NodeName! : r.NodePath + "/" + r.NodeName) : r.NodePath!;
                target = Identity("PlantViewNode", $"{r.PlantViewName}/{path}");
                dependencies.Add(Identity("PlantView", r.PlantViewName!));
                if (r.Operation == "create_plant_view_node")
                {
                    Change("node", null, path);
                    if (r.PlantObjectType is not null) Change("plantObjectType", null, r.PlantObjectType);
                }
                else if (r.Operation == "delete_plant_view_node")
                {
                    Change("delete", path, null);
                    guards.Add(Fire(DeletesConfiguration, r, $"Deleting plant view node '{path}' removes it with its child nodes."));
                }
                else Change($"interface {r.InterfaceName} {property}", null, r.Value);

                break;
        }

        dependencies.Add(target);
        return new(r.Operation, target, changes, null, Dependency(dependencies));
    }
}
