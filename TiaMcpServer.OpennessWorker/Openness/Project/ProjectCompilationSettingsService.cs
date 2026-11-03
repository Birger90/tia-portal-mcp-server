using Siemens.Engineering;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

using Project = Siemens.Engineering.Project;

/// <summary>
/// plc_write set_project_compilation_settings (K10, not live-tested): sets "Simulation während Bausteinübersetzung
/// unterstützen" (<c>PlcSimulationSettingsProvider.IsSimulationDuringBlockCompilationEnabled</c>) and the virtual PLC flag
/// (<c>VirtualPlcSettingsProvider.IsVirtualPlcDuringBlockCompilationEnabled</c>) through the same providers
/// <see cref="ProjectMetadataReader"/> reads (<c>Project.GetService&lt;T&gt;()</c>). Every requested write is bound first
/// (<see cref="ProjectCompilationSettingBinding"/>: a writable property, else a writable attribute); a missing route or provider
/// fails <c>api_member_missing</c> / <c>unsupported_capability</c> before anything is written. Then each value is written and read
/// back with the property getter; a different read-back fails <c>postcondition_failed</c> with the evidence.
/// </summary>
internal static class ProjectCompilationSettingsService
{
    public static ProjectCompilationSettingsResultInfo Set(Project project, ProjectCompilationSettingsRequestInfo request)
    {
        var requested = new List<(string Field, bool Value)>();
        if (request.SimulationDuringBlockCompilation is { } simulation) requested.Add((ProjectCompilationSettingNames.SimulationField, simulation));
        if (request.VirtualPlcDuringBlockCompilation is { } virtualPlc) requested.Add((ProjectCompilationSettingNames.VirtualPlcField, virtualPlc));
        if (requested.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "set_project_compilation_settings needs simulationDuringBlockCompilation or virtualPlcDuringBlockCompilation.");

        // Bind everything before the first write: nothing changes when one route is missing.
        var bound = requested.Select(entry => (entry.Field, entry.Value, Assignment: Bind(Provider(project, entry.Field), entry.Field))).ToList();
        var result = new ProjectCompilationSettingsResultInfo();
        foreach (var (field, value, assignment) in bound)
        {
            var info = new ProjectCompilationSettingResultInfo
            {
                Setting = field,
                Provider = ProjectCompilationSettingNames.ProviderFor(field),
                Member = assignment.Member,
                Requested = value,
                Before = assignment.Read(),
                Route = assignment.Route,
                Signature = assignment.Signature,
            };
            result.Settings.Add(info);
            assignment.Apply(value);
            info.After = assignment.Read();
            if (info.After != value)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"{info.Provider}.{info.Member} was set to {value} through {info.Route}, but reads back "
                    + $"{(info.After is { } after ? after.ToString() : "nothing (unreadable)")}. Read get_project_status (compilationSettings).");
        }

        return result;
    }

    private static object Provider(Project project, string field)
    {
        object? provider = field == ProjectCompilationSettingNames.SimulationField
            ? project.GetService<PlcSimulationSettingsProvider>()
            : project.GetService<VirtualPlcSettingsProvider>();
        return provider ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            $"Project.GetService<{ProjectCompilationSettingNames.ProviderFor(field)}>() returned null for this project; "
            + $"{ProjectCompilationSettingNames.MemberFor(field)} cannot be set. Nothing was changed.");
    }

    private static ProjectCompilationSettingBinding.Assignment Bind(object provider, string field)
    {
        try
        {
            return ProjectCompilationSettingBinding.Bind(provider, ProjectCompilationSettingNames.MemberFor(field), provider is IEngineeringObject engineering
                ? new ProjectCompilationSettingBinding.AttributeSurface(
                    () => engineering.GetAttributeInfos().Select(i => (i.Name, i.AccessMode is EngineeringAttributeAccessMode.Write or EngineeringAttributeAccessMode.ReadWrite)),
                    (attribute, value) => engineering.SetAttribute(attribute, value))
                : null);
        }
        catch (ProjectCompilationSettingMissingException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, exception.Message);
        }
    }
}
