using System.Text.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

public sealed class NetworkVerificationPolicy(IReadOnlyDictionary<string, NetworkWriteEffect> effects,
    IReadOnlyDictionary<string, JsonElement>? results = null)
    : IWriteVerificationPolicy<NetworkOperationRequest>
{
    public const string CustomIdentityCheck = "customIdentity";
    public const string FileCheck = "file";

    private readonly IReadOnlyDictionary<string, JsonElement> _results = results ?? new Dictionary<string, JsonElement>();

    public string ReadTool => "network_read";

    /// <summary>
    /// K78 (live 2026-11-03: "trusted ... No separate verification read followed"): set/remove_custom_identity read the key back
    /// with <c>Get</c> after the write and export_cax re-hashes the placed file; that worker read-back in the item's result is the
    /// verification (checks <see cref="CustomIdentityCheck"/>, <see cref="FileCheck"/>), like K57's create_block. Only items with a
    /// result; everything else stays trusted/superseded.
    /// </summary>
    public bool HasReadBack(NetworkOperationRequest item)
        => item.Operation is HardwareModuleMethods.SetCustomIdentity or HardwareModuleMethods.RemoveCustomIdentity or HardwareModuleMethods.ExportCax
            && _results.ContainsKey(item.OperationId);

    public Task<WriteOperationVerification> ReadBackAsync(NetworkOperationRequest item, StructuredOperationFailure? failure)
        => Task.FromResult(item.Operation == HardwareModuleMethods.ExportCax ? ExportedFile(item) : CustomIdentity(item));

    private WriteOperationVerification CustomIdentity(NetworkOperationRequest item)
    {
        var result = CanonicalJson.Deserialize<HardwareCustomIdentityWriteResultInfo>(_results[item.OperationId].GetRawText());
        var set = item.Operation == HardwareModuleMethods.SetCustomIdentity;
        var expected = set ? $"{item.Key}={item.Value}" : $"{item.Key} absent";
        var after = result.After;
        var actual = after.State == CustomIdentityNames.States.Found ? $"{result.Key}={after.Value}"
            : after.State == CustomIdentityNames.States.NotFound ? $"{result.Key} absent" : $"{result.Key} {after.State}";
        var success = set ? after.State == CustomIdentityNames.States.Found && after.Value == item.Value
            : after.State == CustomIdentityNames.States.NotFound;
        return new(item.OperationId, success, CustomIdentityCheck, expected, actual, success
            ? $"Operation '{item.OperationId}': the worker read the key back with CustomIdentityProvider.Get right after the write."
            : $"Operation '{item.OperationId}': the read-back differs from the request. Inspect it with network_read get_custom_identity.");
    }

    private WriteOperationVerification ExportedFile(NetworkOperationRequest item)
    {
        var result = CanonicalJson.Deserialize<HardwareCaxExportResultInfo>(_results[item.OperationId].GetRawText());
        var file = result.File;
        var success = file.Exists && !string.IsNullOrEmpty(file.Sha256);
        var actual = file.Exists ? $"{file.Path} exists ({file.Length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} bytes, sha256={file.Sha256 ?? "(unreadable)"})" : $"{file.Path} absent";
        return new(item.OperationId, success, FileCheck, $"{item.FilePath} written", actual, success
            ? $"Operation '{item.OperationId}': the worker re-read and hashed the placed file (result.file.sha256)."
            : $"Operation '{item.OperationId}': the placed file was not confirmed. Inspect the file.");
    }

    public IReadOnlyList<string> ObjectKeys(NetworkOperationRequest item, bool asLater)
    {
        var target = effects[item.OperationId].Target;
        return item.Operation switch
        {
            "add_network_device" => [$"device\n{target.DeviceName!.ToUpperInvariant()}"],
            "configure_network_device" => [$"device\n{target.DeviceName!.ToUpperInvariant()}\ninterface\n{Owner(target)}\nnode\n{target.NodeId}"],
            "update_subnet" or "delete_subnet" => [$"subnet\n{target.SubnetId}"],
            "create_subnet" => [],
            // A later configure of the same node is a different setting; only another create on this controller supersedes.
            NetworkIoSystemMethods.CreateIoSystem => [$"device\n{target.DeviceName!.ToUpperInvariant()}\ninterface\n{Owner(target)}\nnode\n{target.NodeId}\nioController"],
            var name when HardwareModuleWritePlanner.Handles(name) => HardwareModuleWritePlanner.ObjectKeys(item, effects[item.OperationId]),
            _ => throw new NotSupportedException(),
        };
    }

    /// <inheritdoc />
    public string? WorkerCheckEvidence(NetworkOperationRequest item) => item.Operation switch
    {
        NetworkIoSystemMethods.CreateIoSystem => "the worker read the new IO system back from its subnet before reporting it (result.subnetId, result.number).",
        "export_io_map" => "the written file was re-read and hashed (result.sha256).",
        var name when HardwareModuleWritePlanner.Handles(name) => "the worker read the item's own result back (identity, attribute values, addresses, the custom identity value, the port link, the transfer area, the exported file's hash or the project's device names before and after an import in result) and fails postcondition_failed on a mismatch.",
        _ => null,
    };

    // Optional type evidence constrains selection but does not change the node's identity.
    private static string Owner(NetworkWriteTargetEvidence target)
        => NetworkInterfacePathEncoding.Encode((target.InterfacePath
            ?? throw new InvalidOperationException("A prepared Network node must have a qualified owner path."))
            .Select(segment => new NetworkInterfacePathSegmentInfo
            { Name = segment.Name, PositionNumber = segment.PositionNumber }).ToArray());
}
