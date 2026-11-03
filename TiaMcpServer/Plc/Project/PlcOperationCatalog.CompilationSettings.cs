using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Plc;

/// <summary>plc_write set_project_compilation_settings: its field set and shape rule (K10).</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithCompilationSettingsSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[ProjectCompilationSettingsMethods.Set] = Write(Array.Empty<string>(),
            new[] { ProjectCompilationSettingNames.SimulationField, ProjectCompilationSettingNames.VirtualPlcField });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> CompilationSettingsOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        (ProjectCompilationSettingNames.SimulationField, op => op.SimulationDuringBlockCompilation is not null),
        (ProjectCompilationSettingNames.VirtualPlcField, op => op.VirtualPlcDuringBlockCompilation is not null),
    };

    private static void ValidateCompilationSettingsItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.Operation == ProjectCompilationSettingsMethods.Set && op.SimulationDuringBlockCompilation is null && op.VirtualPlcDuringBlockCompilation is null)
            errors.Add($"{prefix}: set_project_compilation_settings needs {ProjectCompilationSettingNames.SimulationField} or "
                + $"{ProjectCompilationSettingNames.VirtualPlcField} (true or false).");
    }
}
