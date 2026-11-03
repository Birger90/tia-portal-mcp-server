using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// plc_write set_project_compilation_settings (K10, not live-tested): the project-wide V21 block-compilation flags
/// ("Simulation während Bausteinübersetzung unterstützen" and the virtual PLC flag) that get_project_status reads as
/// <c>compilationSettings</c>. Project-scoped like the alarm classes: no PLC is resolved and plcName is empty in the effect;
/// standalone projects only (a bound local session is refused at planning, as the worker gate would refuse the mutation).
/// No planning read: the effect shows the requested values (read the current ones with get_project_status); the worker reports
/// the value before and after each write. Verification uses the worker's read-back (the provider getter, the mechanism
/// get_project_status reads with).
/// </summary>
internal static class PlcCompilationSettings
{
    public const string Kind = "ProjectCompilationSettings";

    public const string Check = "compilationSettings";

    public static (PlcWriteEffect? Effect, WriteToolError? Error) Resolve(ProjectBindingSnapshot binding, PlcOperationRequest item)
    {
        if (binding.IsVerified && binding.Context is { } context && !ProjectCapabilityCatalog.Supports(context.ContainerKind, item.Operation))
            return (null, new WriteToolError(WorkerFailureCategories.UnsupportedCapability,
                $"Operation '{item.OperationId}': {item.Operation} is unavailable for this project container; the block-compilation settings are reached "
                + "through Project.GetService<T>() of a standalone project."));
        var target = new PlcTargetIdentity(Kind, string.Empty, null, null, null, null, null, null);
        return (new PlcWriteEffect(item.Operation, target, Requested(item).Select(r => new PlcFieldChange(r.Field, null, r.Value ? "true" : "false")).ToArray(),
            null, null, null, null), null);
    }

    public static ProjectCompilationSettingsRequestInfo ToRequest(PlcOperationRequest item) => new()
    {
        SimulationDuringBlockCompilation = item.SimulationDuringBlockCompilation,
        VirtualPlcDuringBlockCompilation = item.VirtualPlcDuringBlockCompilation,
    };

    /// <exception cref="JsonException">The payload does not describe exactly the requested settings.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest item, string payload)
        => CanonicalJson.NormalizeWorkerPayload<ProjectCompilationSettingsResultInfo>(payload, result =>
        {
            var requested = Requested(item);
            if (result.Operation != ProjectCompilationSettingsMethods.Set || result.Settings.Count != requested.Count)
                throw new JsonException("The compilation settings result does not describe the request.");
            for (var index = 0; index < requested.Count; index++)
            {
                var entry = result.Settings[index];
                if (entry is null || entry.Setting != requested[index].Field || entry.Requested != requested[index].Value
                    || entry.Provider != ProjectCompilationSettingNames.ProviderFor(entry.Setting)
                    || entry.Member != ProjectCompilationSettingNames.MemberFor(entry.Setting)
                    || entry.Route is not (ProjectCompilationSettingBinding.Routes.Property or ProjectCompilationSettingBinding.Routes.SetAttribute)
                    || string.IsNullOrWhiteSpace(entry.Signature))
                    throw new JsonException("The compilation settings result does not describe the request.");
            }
        }).Element;

    /// <summary>The read-back entry: every requested setting must read back as requested.</summary>
    public static WriteOperationVerification Verify(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        var expected = string.Join(" ", Requested(item).Select(r => $"{r.Field}={(r.Value ? "true" : "false")}"));
        if (failure is not null)
            return new(item.OperationId, false, Check, expected, null,
                $"Operation '{item.OperationId}' failed with {failure.Category}; read get_project_status (compilationSettings) for the current values.");
        if (!results.TryGetValue(item.OperationId, out var element))
            return new(item.OperationId, false, Check, expected, null, $"Operation '{item.OperationId}': the worker reported no read-back.");
        var result = CanonicalJson.Deserialize<ProjectCompilationSettingsResultInfo>(element.GetRawText());
        var actual = string.Join(" ", result.Settings.Select(s => $"{s.Setting}={(s.After is { } after ? after ? "true" : "false" : "(unreadable)")}"));
        var success = result.Settings.All(s => s.After == s.Requested);
        return new(item.OperationId, success, Check, expected, actual, success
            ? "Read back by the worker through the provider getter (the member get_project_status reads)."
            : $"Operation '{item.OperationId}': a setting did not read back as requested; read get_project_status (compilationSettings).");
    }

    private static List<(string Field, bool Value)> Requested(PlcOperationRequest item)
    {
        var requested = new List<(string, bool)>();
        if (item.SimulationDuringBlockCompilation is { } simulation) requested.Add((ProjectCompilationSettingNames.SimulationField, simulation));
        if (item.VirtualPlcDuringBlockCompilation is { } virtualPlc) requested.Add((ProjectCompilationSettingNames.VirtualPlcField, virtualPlc));
        return requested;
    }
}
