using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// plc_write set_project_compilation_settings against stand-in providers that the production
/// <see cref="ProjectCompilationSettingBinding"/> binds exactly like the worker does (property first, then a writable attribute).
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    /// <summary>Stand-in for <c>PlcSimulationSettingsProvider</c> (only the documented member).</summary>
    public sealed class SimulationProvider
    {
        public bool IsSimulationDuringBlockCompilationEnabled { get; set; }
    }

    /// <summary>Stand-in for <c>VirtualPlcSettingsProvider</c> (only the documented member).</summary>
    public sealed class VirtualPlcProvider
    {
        public bool IsVirtualPlcDuringBlockCompilationEnabled { get; set; }
    }

    /// <summary>A provider whose flag is read-only and that offers no attribute surface.</summary>
    public sealed class ReadOnlySimulationProvider
    {
        public bool IsSimulationDuringBlockCompilationEnabled => false;
    }

    private readonly SimulationProvider _simulation = new();
    private readonly VirtualPlcProvider _virtualPlc = new();

    private string CompilationSettings(WorkerRequest request)
    {
        var info = request.ProjectCompilationSettings ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "missing request");
        var requested = new List<(string Field, bool Value, object Provider)>();
        if (info.SimulationDuringBlockCompilation is { } simulation)
            requested.Add((ProjectCompilationSettingNames.SimulationField, simulation, readOnlyCompilationSettings ? new ReadOnlySimulationProvider() : _simulation));
        if (info.VirtualPlcDuringBlockCompilation is { } virtualPlc)
            requested.Add((ProjectCompilationSettingNames.VirtualPlcField, virtualPlc, _virtualPlc));
        List<(string Field, bool Value, ProjectCompilationSettingBinding.Assignment Assignment)> bound;
        try
        {
            bound = requested.Select(entry => (entry.Field, entry.Value,
                ProjectCompilationSettingBinding.Bind(entry.Provider, ProjectCompilationSettingNames.MemberFor(entry.Field), null))).ToList();
        }
        catch (ProjectCompilationSettingMissingException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, exception.Message);
        }

        var result = new ProjectCompilationSettingsResultInfo();
        foreach (var (field, value, assignment) in bound)
        {
            var entry = new ProjectCompilationSettingResultInfo
            {
                Setting = field, Provider = ProjectCompilationSettingNames.ProviderFor(field), Member = assignment.Member, Requested = value,
                Before = assignment.Read(), Route = assignment.Route, Signature = assignment.Signature,
            };
            assignment.Apply(value);
            entry.After = assignment.Read();
            result.Settings.Add(entry);
        }

        return Ok(result);
    }
}
