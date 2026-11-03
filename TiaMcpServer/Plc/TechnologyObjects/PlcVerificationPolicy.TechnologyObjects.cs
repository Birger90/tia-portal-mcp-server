using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// Read-back of technology object writes through the public plc_read operations, after the worker already read the
/// change back itself: <c>technologyObjectParameter</c> re-reads the parameter with a fresh <c>Find</c> and compares it
/// with the value the write reported; <c>technologyObjectConnection</c> re-reads the interface: connected (ident:
/// <c>ConnectedIdentDevice</c> reads a device) and every documented target property the connect names reads back the
/// requested target (<see cref="TechnologyObjectConnectionEvidence"/>; a property that cannot be read is accepted only when
/// the worker reported the interface as not connected before), or not connected after a disconnect;
/// <c>technologyObject</c> and <c>technologyObjectGroup</c> re-list the PLC's objects and groups (a created object also by
/// the DB number the create reported). A content update is checked like every content write (<c>contentHash</c> of a
/// re-export). An export is <c>trusted</c>: the worker re-read the written file and compared its SHA-256 with the export's.
/// An item that reports nothing that can be re-read (an import that named no object) is not verifiable: success false.
/// </summary>
public sealed partial class PlcVerificationPolicy
{
    public const string TechnologyObjectCheck = "technologyObject";
    public const string TechnologyObjectGroupCheck = "technologyObjectGroup";
    public const string TechnologyObjectParameterCheck = "technologyObjectParameter";
    public const string TechnologyObjectConnectionCheck = "technologyObjectConnection";

    private static bool HasTechnologyReadBack(PlcOperationRequest item)
        => TechnologyObjectMethods.IsWrite(item.Operation) && item.Operation != TechnologyObjectMethods.Export;

    /// <summary>Hierarchical keys: a group key prefixes the located key of every object and group inside it; a member (parameter, interface) key extends its object's key.</summary>
    private static string[] TechnologyObjectKeys(PlcOperationRequest item, PlcTargetIdentity target, string scope)
    {
        string Group(string? path) => $"{scope}togroup" + string.Concat(TechnologyObjectPolicy.Segments(path).Select(segment => "\n" + segment));
        var keys = new List<string>();
        if (target.Kind == PlcWorkingState.TechnologyObjectGroupKind)
        {
            keys.Add(Group(target.FolderPath));
            if (item.Operation == TechnologyObjectMethods.RenameGroup && target.FolderPath is not null && item.NewName is not null)
                keys.Add(Group(TechnologyObjectPolicy.Combine(TechnologyObjectPolicy.ParentPath(target.FolderPath), item.NewName)));
            return keys.ToArray();
        }

        var member = item.Operation switch
        {
            TechnologyObjectMethods.SetParameter => "\nparameter\n" + item.ParameterName,
            TechnologyObjectMethods.ConnectHardware or TechnologyObjectMethods.DisconnectHardware
                => "\nconnection\n" + item.ConnectionInterface + (item.InterfaceIndex is { } index ? "\n" + index : string.Empty),
            _ => string.Empty,
        };
        var names = new List<string?> { target.Name ?? item.Name };
        if (item.Operation == TechnologyObjectMethods.Rename) names.Add(item.NewName);
        foreach (var name in names.OfType<string>())
        {
            keys.Add($"{scope}to\n{name}{member}");
            if (target.FolderPath is not null) keys.Add($"{Group(target.FolderPath)}\nobject\n{name}{member}");
        }

        return keys.ToArray();
    }

    /// <summary>
    /// The PLC-wide name keys of the objects an import reported (<c>importedObjects</c>): technology object names are unique
    /// per PLC (a TO is a DB), so an import that (re)creates a name supersedes an earlier delete or rename of that name in
    /// any folder, and a later item on an imported name supersedes the import (K19, live V21 2026-11-02: export, delete,
    /// import of Achse_Test into /Achsen in one call; the delete's read-back found /Achsen/Achse_Test and failed). The
    /// planned effect cannot name them (the file is read by TIA Portal), so they come from the import's result.
    /// </summary>
    private IEnumerable<string> ImportedObjectKeys(PlcOperationRequest item, string scope)
    {
        if (item.Operation != TechnologyObjectMethods.Import || !_results.TryGetValue(item.OperationId, out var raw)) return Array.Empty<string>();
        return CanonicalJson.Deserialize<TechnologyObjectWriteResultInfo>(raw.GetRawText()).ImportedObjects.Select(name => $"{scope}to\n{name}");
    }

    private async Task<WriteOperationVerification> TechnologyReadBackAsync(PlcOperationRequest item, StructuredOperationFailure? failure)
    {
        var target = effects[item.OperationId].Target;
        var written = _results.TryGetValue(item.OperationId, out var raw) ? CanonicalJson.Deserialize<TechnologyObjectWriteResultInfo>(raw.GetRawText()) : null;
        switch (item.Operation)
        {
            case TechnologyObjectMethods.SetParameter:
            {
                var (page, error) = await ReadTechnologyAsync<TechnologyObjectParametersInfo>(TechnologyObjectMethods.GetParameters,
                    new() { PlcName = item.PlcName, Name = written?.Name ?? target.Name ?? item.Name, ParameterName = item.ParameterName }).ConfigureAwait(false);
                var actual = page is null ? null : Shown(page.Parameters[0]);
                return Outcome(item, TechnologyObjectParameterCheck, failure, written?.Parameter is { } parameter ? Shown(parameter) : null, actual, error,
                    "get_technology_object_parameters");
            }

            case TechnologyObjectMethods.ConnectHardware:
            case TechnologyObjectMethods.DisconnectHardware:
            {
                var (connection, error) = await ReadTechnologyAsync<TechnologyObjectHardwareConnectionInfo>(TechnologyObjectMethods.GetHardwareConnection,
                    new() { PlcName = item.PlcName, Name = written?.Name ?? target.Name ?? item.Name }).ConfigureAwait(false);
                // The interface the write chose: only the providers it needs, the first that serves it (K18).
                var endpoint = connection is null ? null
                    : TechnologyObjectPolicy.PreferredEndpoints(connection.Interfaces.Select(i => (i.Interface, i.Provider, i.Index, i)), item.ConnectionInterface!)
                        .Select(e => e.Target).Where(i => item.InterfaceIndex is null || i.Index == item.InterfaceIndex).ToList() is { Count: 1 } single ? single[0] : null;
                if (connection is null || endpoint is null)
                    return Outcome(item, TechnologyObjectConnectionCheck, failure, ExpectedConnection(item), connection is null ? null : "interface not found", error,
                        "get_technology_object_hardware_connection");
                var (expected, actual, matched) = ConnectionComparison(item, written, endpoint);
                return Outcome(item, TechnologyObjectConnectionCheck, failure, expected, actual, error, "get_technology_object_hardware_connection", matched);
            }

            default:
            {
                var (inventory, error) = await ReadTechnologyAsync<TechnologyObjectInventoryInfo>(TechnologyObjectMethods.List,
                    new() { PlcName = item.PlcName ?? target.PlcName }).ConfigureAwait(false);
                var plc = inventory?.Plcs.Where(p => p.PlcName == target.PlcName).ToList() is { Count: 1 } one ? one[0] : null;
                var group = target.Kind == PlcWorkingState.TechnologyObjectGroupKind && item.Operation != TechnologyObjectMethods.Import;
                var (expected, actual) = plc is null ? (null, inventory is null ? null : $"PLC '{target.PlcName}' not listed") : Structure(item, target, written, plc);
                return Outcome(item, group ? TechnologyObjectGroupCheck : TechnologyObjectCheck, failure, expected, actual, error, "list_technology_objects");
            }
        }
    }

    /// <summary>What the structure should show after the item, and what the re-read shows, in the same words.</summary>
    private static (string? Expected, string? Actual) Structure(PlcOperationRequest item, PlcTargetIdentity target, TechnologyObjectWriteResultInfo? written,
        TechnologyObjectPlcInfo plc)
    {
        string Object(string name) => plc.TechnologyObjects.FirstOrDefault(o => TechnologyObjectPolicy.Names.Equals(o.Name, name)) is { } found
            ? $"{TechnologyObjectPolicy.Combine(found.GroupPath, found.Name)} exists" : $"{name} absent";
        string Group(string path) => plc.Groups.Any(g => TechnologyObjectPolicy.Names.Equals(g.Path, path)) ? $"{path} exists" : $"{path} absent";
        var name = written?.Name ?? target.Name ?? item.Name ?? string.Empty;
        var folder = written?.GroupPath ?? target.FolderPath ?? "/";
        switch (item.Operation)
        {
            case TechnologyObjectMethods.Create when written?.TechnologyObject?.Number is { } number:
            {
                // The DB number the create reported identifies the new object (the planner and the worker refused an existing name).
                var found = plc.TechnologyObjects.FirstOrDefault(o => TechnologyObjectPolicy.Names.Equals(o.Name, name));
                return ($"{TechnologyObjectPolicy.Combine(folder, name)} exists as number {number}",
                    found is null ? $"{name} absent" : $"{TechnologyObjectPolicy.Combine(found.GroupPath, found.Name)} exists as number {found.Number?.ToString() ?? "(unreadable)"}");
            }

            case TechnologyObjectMethods.Create:
            case TechnologyObjectMethods.UpdateContent:
                return ($"{TechnologyObjectPolicy.Combine(folder, name)} exists", Object(name));
            case TechnologyObjectMethods.Rename:
            {
                var old = target.Name ?? item.Name ?? string.Empty;
                var renamed = item.NewName ?? name;
                return TechnologyObjectPolicy.Names.Equals(old, renamed)
                    ? ($"{TechnologyObjectPolicy.Combine(folder, renamed)} exists", Object(renamed))
                    : ($"{old} absent; {TechnologyObjectPolicy.Combine(folder, renamed)} exists", $"{Object(old)}; {Object(renamed)}");
            }

            case TechnologyObjectMethods.Delete:
                return ($"{name} absent", Object(name));
            case TechnologyObjectMethods.Import:
            {
                var imported = written?.ImportedObjects ?? new List<string>();
                return (string.Join("; ", imported.Select(n => $"{TechnologyObjectPolicy.Combine(folder, n)} exists")), string.Join("; ", imported.Select(Object)));
            }

            case TechnologyObjectMethods.CreateGroup:
            case TechnologyObjectMethods.RenameGroup:
                return ($"{folder} exists", Group(folder));
            case TechnologyObjectMethods.DeleteGroup:
                return ($"{folder} absent", Group(folder));
            default:
                return (null, null);
        }
    }

    private static WriteOperationVerification Outcome(PlcOperationRequest item, string check, StructuredOperationFailure? failure,
        string? expected, string? actual, string? readError, string readOperation, bool? matched = null)
    {
        if (failure is not null)
            return new(item.OperationId, false, check, null, actual ?? $"not readable ({readError})",
                $"Operation '{item.OperationId}' failed with {failure.Category}; the current state is reported. Inspect it with plc_read {readOperation} before retrying.");
        if (actual is null)
            return new(item.OperationId, false, check, expected, null, $"Operation '{item.OperationId}': the result could not be re-read ({readError}).");
        if (string.IsNullOrEmpty(expected))
            return new(item.OperationId, false, check, expected, actual,
                $"Operation '{item.OperationId}' reported nothing that can be re-read, so it is not verifiable. Inspect it with plc_read {readOperation}.");
        var success = matched ?? string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        return new(item.OperationId, success, check, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read state differs from what the operation reported. Inspect it with plc_read {readOperation}.");
    }

    private async Task<(T? Value, string? Error)> ReadTechnologyAsync<T>(string method, TechnologyObjectRequestInfo info) where T : class
    {
        var request = new PlcOperationRequest { OperationId = "verify", Operation = method, PlcName = info.PlcName, Name = info.Name, ParameterName = info.ParameterName };
        var projected = PlcPayloadContract.Project(request, await client.TechnologyObjectAsync(method, info).ConfigureAwait(false));
        return projected.Result is { } result
            ? (CanonicalJson.Deserialize<T>(result.GetRawText()), null)
            : (null, $"{projected.Failure?.Category}: {projected.Failure?.Message}");
    }

    private static string Shown(TechnologyObjectParameterInfo parameter) => $"{parameter.ValueKind} {parameter.Value ?? "null"}";

    private static string ConnectionState(bool? connected) => connected switch { true => "connected", false => "not connected", null => "connection state unreadable" };

    private static string ExpectedConnection(PlcOperationRequest item)
        => item.Operation == TechnologyObjectMethods.DisconnectHardware ? "not connected"
            : string.Join("; ", new[] { "connected" }.Concat(TechnologyObjectConnectionEvidence.Expectations(PlcTechnologyObjectRequests.ToRequest(item)).Select(e => e.Requested)));

    /// <summary>
    /// The connection the item should leave and the re-read in the same words: connected plus every expected target
    /// property (a matching property is shown as requested), or not connected after a disconnect. An unreadable property
    /// counts as verified only when the worker reported the interface as not connected before the connect.
    /// </summary>
    private static (string Expected, string Actual, bool Matched) ConnectionComparison(PlcOperationRequest item, TechnologyObjectWriteResultInfo? written,
        TechnologyObjectConnectionInterfaceInfo endpoint)
    {
        var connected = TechnologyObjectConnectionEvidence.Connected(endpoint);
        if (item.Operation == TechnologyObjectMethods.DisconnectHardware)
            return ("not connected", ConnectionState(connected), connected == false);
        var observations = TechnologyObjectConnectionEvidence.Observe(TechnologyObjectConnectionEvidence.Expectations(PlcTechnologyObjectRequests.ToRequest(item)), endpoint);
        var wasDisconnected = written?.PreviousInterface is { } previous && TechnologyObjectConnectionEvidence.Connected(previous) == false
            && item.ConnectionInterface != TechnologyObjectPolicy.Interfaces.Ident;
        var expected = string.Join("; ", new[] { "connected" }.Concat(observations.Select(o => o.Expectation.Requested)));
        var actual = string.Join("; ", new[] { ConnectionState(connected) }.Concat(observations.Select(o => o.Match == true ? o.Expectation.Requested : o.Shown)));
        var matched = connected == true && observations.All(o => o.Match == true || (o.Match is null && wasDisconnected));
        return (expected, actual, matched);
    }
}
