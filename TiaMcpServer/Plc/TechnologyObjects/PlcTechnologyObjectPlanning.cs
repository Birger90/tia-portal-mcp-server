using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// The planning reads of technology object writes, all through the public plc_read operations: the PLC's technology
/// object inventory (once per PLC and call), the current value of a parameter to set and the current state of an
/// interface to connect or disconnect. A dependent item (its object is created earlier in the same call) skips the
/// object reads; it is re-planned against the real state just before its mutation. File paths are checked lexically
/// (<see cref="TechnologyObjectPolicy.ValidateLocalFilePath"/>); only the export's existing-file check looks at the file
/// system, and the worker checks both again and reports network drives and reparse points as warnings.
/// </summary>
internal static class PlcTechnologyObjectPlanning
{
    public static bool Handles(PlcOperationRequest item) => TechnologyObjectMethods.IsWrite(item.Operation) && item.Operation != TechnologyObjectMethods.UpdateContent;

    public static async Task<(TechnologyObjectPlcInfo? Inventory, WriteToolError? Error)> ReadInventoryAsync(OpennessWorkerClient client, PlcTagInventoryPlcInfo plc)
    {
        var request = new PlcOperationRequest { OperationId = "plan", Operation = TechnologyObjectMethods.List, PlcName = plc.PlcName };
        var projected = PlcPayloadContract.Project(request, await client.TechnologyObjectAsync(TechnologyObjectMethods.List,
            new TechnologyObjectRequestInfo { PlcName = plc.PlcName }).ConfigureAwait(false));
        if (projected.Result is not { } result)
            return (null, new(projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The technology objects of PLC '{plc.PlcName}' could not be read: {projected.Failure?.Message}"));
        var inventory = CanonicalJson.Deserialize<TechnologyObjectInventoryInfo>(result.GetRawText());
        var match = inventory.Plcs.Where(p => p.PlcName == plc.PlcName && p.DeviceName == plc.DeviceName).ToList();
        return match.Count == 1 ? (match[0], null)
            : (null, new(WorkerFailureCategories.StateChanged, $"The technology object inventory does not describe PLC '{plc.PlcName}' on device '{plc.DeviceName}' exactly once."));
    }

    public static async Task<(PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error)> ResolveAsync(
        OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, bool dependent)
    {
        var guards = new List<FiredGuard>();
        FiredGuard Fire(string id, string message) => new(id, item.OperationId, $"Operation '{item.OperationId}': {message}");
        WriteToolError Error(string category, string message) => new(category, $"Operation '{item.OperationId}': {message}");
        var name = effect.Target.Name ?? item.Name;
        switch (item.Operation)
        {
            case TechnologyObjectMethods.SetParameter:
            {
                if (item.ParameterName!.StartsWith(TechnologyObjectPolicy.ActorParameterPrefix, StringComparison.Ordinal))
                    guards.Add(Fire(TechnologyParameterNotHardwareConnection,
                        $"'{item.ParameterName}' is an Actor.* parameter; the Siemens skill states that such writes succeed without connecting the drive. Use connect_technology_object_hardware for the connection."));
                if (dependent) return (effect, guards, null);
                var read = await ReadAsync<TechnologyObjectParametersInfo>(client, TechnologyObjectMethods.GetParameters,
                    new TechnologyObjectRequestInfo { PlcName = item.PlcName, Name = name, ParameterName = item.ParameterName }).ConfigureAwait(false);
                if (read.Error is { } readError)
                {
                    if (readError.Category is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
                        return (effect, guards, Error(readError.Category, readError.Message));
                    guards.Add(Fire(StateUnverifiable, $"The current value of parameter '{item.ParameterName}' could not be read ({readError.Message})."));
                    return (effect, guards, null);
                }

                var current = read.Value!.Parameters[0];
                if (Refusal(item, current, name) is { } refusal) return (effect, guards, Error(WorkerFailureCategories.ValidationError, refusal));
                var change = effect.Changes.Select(c => c with { Current = $"{current.ValueKind} {current.Value ?? "null"}" }).ToArray();
                return (effect with { Changes = change }, guards, null);
            }

            case TechnologyObjectMethods.ConnectHardware:
            case TechnologyObjectMethods.DisconnectHardware:
            {
                if (dependent) return (effect, guards, null);
                var read = await ReadAsync<TechnologyObjectHardwareConnectionInfo>(client, TechnologyObjectMethods.GetHardwareConnection,
                    new TechnologyObjectRequestInfo { PlcName = item.PlcName, Name = name }).ConfigureAwait(false);
                if (read.Error is { } readError)
                {
                    if (readError.Category is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
                        return (effect, guards, Error(readError.Category, readError.Message));
                    guards.Add(Fire(StateUnverifiable, $"The hardware connection of '{name}' could not be read ({readError.Message})."));
                    return (effect, guards, null);
                }

                // Only the providers the interface needs, the first that serves it (K18): the worker chooses the same way.
                var preferred = TechnologyObjectPolicy.PreferredEndpoints(
                    read.Value!.Interfaces.Select(i => (i.Interface, i.Provider, i.Index, i)), item.ConnectionInterface!);
                var candidates = preferred.Select(e => e.Target).Where(i => item.InterfaceIndex is null || i.Index == item.InterfaceIndex).ToList();
                if (preferred.Count == 0
                    && TechnologyObjectPolicy.UnavailableProviderMessage(item.ConnectionInterface!, read.Value.UnavailableServices) is { } unavailable)
                    return (effect, guards, Error(WorkerFailureCategories.UnsupportedCapability, $"technology object '{name}': {unavailable}"));
                if (candidates.Count == 0)
                    return (effect, guards, Error(WorkerFailureCategories.TargetKindUnsupported,
                        $"technology object '{name}' offers no {item.ConnectionInterface}{(item.InterfaceIndex is null ? string.Empty : $" interface at position {item.InterfaceIndex}")} "
                        + $"(offered: {(read.Value.Interfaces.Count == 0 ? "none" : string.Join(", ", read.Value.Interfaces.Select(Describe)))})."));
                if (candidates.Count > 1)
                    return (effect, guards, Error(WorkerFailureCategories.TargetAmbiguous,
                        $"technology object '{name}' has {candidates.Count} {item.ConnectionInterface} interfaces ({string.Join(", ", candidates.Select(Describe))}); set interfaceIndex."));
                var endpoint = candidates[0];
                // Ident documents no IsConnected: its state is whether ConnectedIdentDevice reads a device.
                var connected = TechnologyObjectConnectionEvidence.Connected(endpoint);
                var state = connected switch { true => "connected", false => "not connected", null => "connection state unreadable" };
                if (item.Operation == TechnologyObjectMethods.ConnectHardware && connected == true)
                    guards.Add(Fire(ReplacesHardwareConnection, $"the {Describe(endpoint)} interface of '{name}' reads as connected ({Summary(endpoint)}); "
                        + "Connect is expected to replace that connection (not documented; the read-back checks the requested target)."));
                var changes = effect.Changes.Select(c => c with { Current = state }).ToArray();
                return (effect with { Changes = changes }, guards, null);
            }

            case TechnologyObjectMethods.Export:
            {
                var (refusal, guard) = ExportFileCheck(item, name, File.Exists);
                if (refusal is not null) return (effect, guards, Error(WorkerFailureCategories.ValidationError, refusal));
                if (guard is not null) guards.Add(Fire(OverwritesFile, guard));
                return (effect, guards, null);
            }

            default:
                return (effect, guards, null);
        }
    }

    /// <summary>
    /// The export's existing-file check (the host and the worker share the machine): an existing file is refused unless
    /// replaced on purpose (then the <c>plc_overwrites_file</c> message). <paramref name="exists"/> is the file system probe.
    /// </summary>
    internal static (string? Refusal, string? Guard) ExportFileCheck(PlcOperationRequest item, string? name, Func<string, bool> exists)
    {
        if (item.FilePath is not { } path || TechnologyObjectPolicy.ValidateLocalFilePath(path) is not null) return (null, null);
        var normalized = TechnologyObjectPolicy.NormalizeLocalFilePath(path);
        if (!exists(normalized)) return (null, null);
        return item.Overwrite == true
            ? (null, $"the export of '{name}' replaces the existing file '{normalized}'.")
            : ($"'{normalized}' already exists; set overwrite:true to replace it.", null);
    }

    /// <summary>
    /// The request cannot be converted to the parameter's current type: refused before anything runs, like the worker would.
    /// A refused value text is worded by <see cref="TechnologyObjectValues.ParameterRefusal"/>, exactly as the worker words it.
    /// </summary>
    internal static string? Refusal(PlcOperationRequest item, TechnologyObjectParameterInfo current, string? objectName = null)
    {
        string Refused(string reason) => TechnologyObjectValues.ParameterRefusal(item.ParameterName, objectName ?? item.Name, reason);
        var tag = item.TagName is not null;
        switch (current.ValueKind)
        {
            case TechnologyObjectValueKinds.Unreadable:
                return $"the current value of parameter '{item.ParameterName}' is unreadable, so its type cannot be determined.";
            case TechnologyObjectValueKinds.PlcTag:
                return tag ? null : $"parameter '{item.ParameterName}' holds a PlcTag; assign another tag with tableName and tagName.";
            case TechnologyObjectValueKinds.Null:
                return tag ? null : $"parameter '{item.ParameterName}' has no current value (null), so its type cannot be determined; only a PlcTag (tableName and tagName) can be assigned to it.";
        }

        if (tag) return $"parameter '{item.ParameterName}' holds a {current.ValueType} ({current.ValueKind}), not a PlcTag; write it with value.";
        if (current.ValueKind == TechnologyObjectValueKinds.Enum)
            // A truncated member list cannot refuse an unlisted name: the worker checks it against the live enum.
            return TechnologyObjectValues.EnumRefusal(item.Value, current.EnumMembers, current.EnumFlags, current.EnumMembersTruncated, current.ValueType) is { } enumRefusal
                ? Refused(enumRefusal)
                : null;
        if (!TechnologyObjectValueKinds.TextWritable.Contains(current.ValueKind, StringComparer.Ordinal))
            return $"parameter '{item.ParameterName}' holds a {current.ValueType}, which cannot be written from text.";
        // Reals: the worker alone decides range and exactness against the live type (one authority); the host checks the syntax.
        if (current.ValueKind == TechnologyObjectValueKinds.Real)
            return TechnologyObjectValues.RealSyntaxRefusal(item.Value, current.ValueType) is { } syntax ? Refused(syntax) : null;
        // Booleans, integers and strings resolve on the host; the worker converts against the live type again.
        return current.ValueType is { } typeName && Type.GetType(typeName, throwOnError: false) is { } type
            && !TechnologyObjectValues.TryConvert(item.Value, type, out _, out var conversion)
            ? Refused(conversion)
            : null;
    }

    private static string Describe(TechnologyObjectConnectionInterfaceInfo endpoint)
        => endpoint.Index is { } index ? $"{endpoint.Interface}[{index}]" : endpoint.Interface;

    private static string Summary(TechnologyObjectConnectionInterfaceInfo endpoint)
        => string.Join(", ", endpoint.Properties.Where(p => p.Kind != TechnologyObjectValueKinds.Null && p.Name != "IsConnected")
            .Select(p => $"{p.Name}={p.Value}").Take(6));

    private static async Task<(T? Value, WriteToolError? Error)> ReadAsync<T>(OpennessWorkerClient client, string method, TechnologyObjectRequestInfo info)
        where T : class
    {
        var request = new PlcOperationRequest
        {
            OperationId = "plan", Operation = method, Name = info.Name, PlcName = info.PlcName, ParameterName = info.ParameterName,
        };
        var projected = PlcPayloadContract.Project(request, await client.TechnologyObjectAsync(method, info).ConfigureAwait(false));
        if (projected.Result is not { } result)
            return (null, new(projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed, projected.Failure?.Message ?? "the read failed"));
        try
        {
            return (CanonicalJson.Deserialize<T>(result.GetRawText()), null);
        }
        catch (JsonException)
        {
            return (null, new(WorkerFailureCategories.ProtocolError, "the planning read did not match its declared contract"));
        }
    }
}
