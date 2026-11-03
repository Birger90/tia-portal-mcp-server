using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.OpcUa;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Plc;

/// <summary>
/// plc_read get_opcua_configuration and the plc_write OPC UA operations (issue #24, not live-tested). Planned host-side by matching
/// the PLC in the call's inventory (no planning read); the provider, the interface, namespace or role and every member are resolved
/// and bound by the worker before anything changes (target_not_found, unsupported_capability, api_member_missing). The worker reads
/// the state back after each change (interfaces, restrictions, roles) or re-hashes the files (export, import with a re-export); the
/// verification reports that evidence.
/// </summary>
internal static class PlcOpcUa
{
    public const string Kind = "OpcUa";

    public const string Check = "opcUa";

    public static (PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards) Resolve(PlcOperationRequest item, PlcTagInventoryPlcInfo plc)
    {
        var name = item.InterfaceName ?? item.RoleName ?? item.NamespaceUri;
        var target = new PlcTargetIdentity(Kind, plc.PlcName, plc.DeviceName, item.InterfaceKind, null, name, null, null);
        var changes = new List<PlcFieldChange>();
        void Add(string field, object? value)
        {
            if (value is not null) changes.Add(new PlcFieldChange(field, null, value is bool b ? (b ? "true" : "false") : value.ToString()));
        }

        Add("enabled", item.Enabled);
        Add("useStringNodeIds", item.UseStringNodeIds);
        Add("generateNodes", item.GenerateNodes);
        Add("newName", item.NewName);
        Add("sessionRequired", item.SessionRequired);
        Add("signingRequired", item.SigningRequired);
        Add("encryptionRequired", item.EncryptionRequired);
        Add("applyRestrictionsToBrowse", item.ApplyRestrictionsToBrowse);
        Add("newRoleName", item.NewRoleName);
        Add("definingNamespace", item.DefiningNamespace);
        Add("projectRole", item.ProjectRole);
        Add("permission", item.Permission);
        var guards = new List<FiredGuard>();
        void Fire(string id, string message) => guards.Add(new FiredGuard(id, item.OperationId, message));
        if (item.Operation != PlcOpcUaMethods.ExportInterface)
            Fire(PlcGuardDefinitions.OpcUaChangesServer, $"{item.Operation} changes the OPC UA server configuration of PLC '{plc.PlcName}' in the offline project; it reaches the CPU with the next download.");
        if (item.Operation == PlcOpcUaMethods.ExportInterface && item.Overwrite == true)
            Fire(PlcGuardDefinitions.ExportOverwritesFile, $"{item.Operation} replaces '{item.FilePath}' if it exists (overwrite:true).");
        if (item.Operation == PlcOpcUaMethods.ImportInterface)
            Fire(PlcGuardDefinitions.OpcUaChangesServer, $"Import replaces the content of the {item.InterfaceKind} interface '{item.InterfaceName}' with '{item.FilePath}'; the result is verified by a re-export.");
        var relaxed = new[] { ("sessionRequired", item.SessionRequired), ("signingRequired", item.SigningRequired), ("encryptionRequired", item.EncryptionRequired),
            ("applyRestrictionsToBrowse", item.ApplyRestrictionsToBrowse) }.Where(pair => pair.Item2 == false).Select(pair => pair.Item1).ToArray();
        if (relaxed.Length > 0)
            Fire(PlcGuardDefinitions.OpcUaLowersAccess, $"Namespace '{item.NamespaceUri}': {string.Join(", ", relaxed)} set to false relaxes its access restriction.");
        if (item.Operation == PlcOpcUaMethods.SetNamespacePermission && item.Enabled == true)
            Fire(PlcGuardDefinitions.OpcUaLowersAccess, $"Role '{item.RoleName}' is granted '{item.Permission}' on '{item.NamespaceUri}'.");
        return (new PlcWriteEffect(item.Operation, target, changes, item.FilePath, null, null, null), Merge(guards));
    }

    /// <summary>
    /// One guard per id, its messages joined in firing order (security review F9: import_opcua_interface's specific message
    /// was lost when only the first guard of an id was kept).
    /// </summary>
    internal static FiredGuard[] Merge(IEnumerable<FiredGuard> guards)
        => guards.GroupBy(g => g.Id, StringComparer.Ordinal)
            .Select(g => g.Skip(1).Any() ? new FiredGuard(g.Key, g.First().OperationId, string.Join(" ", g.Select(x => x.Message).Distinct(StringComparer.Ordinal))) : g.First())
            .ToArray();

    public static PlcOpcUaRequestInfo ToRequest(PlcOperationRequest op) => new()
    {
        Operation = op.Operation, PlcName = op.PlcName, InterfaceKind = op.InterfaceKind, InterfaceName = op.InterfaceName, FilePath = op.FilePath,
        Overwrite = op.Overwrite == true, NewName = op.NewName, Enabled = op.Enabled, UseStringNodeIds = op.UseStringNodeIds, GenerateNodes = op.GenerateNodes,
        NamespaceUri = op.NamespaceUri, SessionRequired = op.SessionRequired, SigningRequired = op.SigningRequired, EncryptionRequired = op.EncryptionRequired,
        ApplyRestrictionsToBrowse = op.ApplyRestrictionsToBrowse, RoleName = op.RoleName, NewRoleName = op.NewRoleName, DefiningNamespace = op.DefiningNamespace,
        ProjectRole = op.ProjectRole, Permission = op.Permission,
    };

    /// <exception cref="JsonException">The payload does not decode or names a PLC the request did not.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcOpcUaConfigurationInfo>(payload, read =>
        {
            if (read.Plcs is null || read.Plcs.Count == 0) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(read.Plcs);
            foreach (var plc in read.Plcs)
            {
                if (string.IsNullOrWhiteSpace(plc.PlcName)) throw new JsonException();
                if (operation.PlcName is { } name && !PlcNameRules.Comparer.Equals(plc.PlcName, name) && !PlcNameRules.Comparer.Equals(plc.DeviceName, name)) throw new JsonException("A PLC the request did not name was returned.");
                if (!plc.ProviderAvailable && (plc.Note is null || plc.ServerInterfaces.Count + plc.SimaticInterfaces.Count + plc.ReferenceNamespaces.Count
                        + plc.NamespaceAccessRestrictions.Count + plc.RoleMappings.Count > 0))
                    throw new JsonException("An unavailable provider contradicts the content.");
                StandalonePayloadContract.RejectNullElements(plc.ServerInterfaces);
                StandalonePayloadContract.RejectNullElements(plc.SimaticInterfaces);
                StandalonePayloadContract.RejectNullElements(plc.ReferenceNamespaces);
                StandalonePayloadContract.RejectNullElements(plc.NamespaceAccessRestrictions);
                StandalonePayloadContract.RejectNullElements(plc.RoleMappings);
            }
        }).Element;

    /// <exception cref="JsonException">The payload does not describe the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest item, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcOpcUaWriteResultInfo>(payload, result =>
        {
            var evidence = new[] { PlcOpcUaEvidence.Verified, PlcOpcUaEvidence.NotReadBack, PlcOpcUaEvidence.Deleted, PlcOpcUaEvidence.File };
            if (result.Operation != item.Operation || string.IsNullOrWhiteSpace(result.PlcName) || string.IsNullOrWhiteSpace(result.Target)
                || result.Calls is null || result.Calls.Count == 0 || result.Calls.Any(string.IsNullOrWhiteSpace) || !evidence.Contains(result.Evidence, StringComparer.Ordinal))
                throw new JsonException("The OPC UA result does not describe the request.");
            if (item.Operation == PlcOpcUaMethods.ExportInterface && (result.Evidence != PlcOpcUaEvidence.File || result.Sha256 is not { Length: 64 } || result.FilePath is null))
                throw new JsonException("An export result without its file.");
            if (item.Operation is PlcOpcUaMethods.DeleteInterface or PlcOpcUaMethods.DeleteRole && result.Evidence != PlcOpcUaEvidence.Deleted)
                throw new JsonException("A delete result without its absence.");
        }).Element;

    /// <summary>The worker's read-back is the verification: verified/deleted/file are success, notReadBack is notVerified (success false).</summary>
    public static WriteOperationVerification Verify(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        if (failure is not null)
            return new(item.OperationId, false, Check, null, null,
                $"Operation '{item.OperationId}' failed with {failure.Category}; inspect the OPC UA configuration with plc_read get_opcua_configuration before retrying.");
        if (!results.TryGetValue(item.OperationId, out var element))
            return new(item.OperationId, false, Check, null, null, $"Operation '{item.OperationId}': the worker reported no OPC UA evidence.");
        var result = CanonicalJson.Deserialize<PlcOpcUaWriteResultInfo>(element.GetRawText());
        return result.Evidence switch
        {
            PlcOpcUaEvidence.NotReadBack => new(item.OperationId, false, Check, result.Sha256, result.ReExportSha256,
                $"Operation '{item.OperationId}': the call completed, but its result could not be read back: {result.Note} Inspect it with plc_read get_opcua_configuration."),
            PlcOpcUaEvidence.File => new(item.OperationId, true, Check, result.Sha256, result.Sha256,
                $"Operation '{item.OperationId}': the worker re-read '{result.FilePath}' after placing it ({result.Bytes} bytes, sha256 {result.Sha256})."),
            PlcOpcUaEvidence.Deleted => new(item.OperationId, true, Check, null, null, $"Operation '{item.OperationId}': {result.Target} is no longer listed."),
            _ => new(item.OperationId, true, Check, null, null, $"Operation '{item.OperationId}': {result.Target} was read back after the change and matches the request."),
        };
    }

    /// <summary>Each interface, restriction and role is its own object; a later item on the same object supersedes an earlier one.</summary>
    public static IReadOnlyList<string> ObjectKeys(PlcOperationRequest item, PlcTargetIdentity target, string scope)
        => item.Operation == PlcOpcUaMethods.ExportInterface
            ? new[] { PlcAlarms.PlcAlarmVerification.FileKey(item.FilePath) }
            : item.InterfaceName is not null
                ? new[] { $"{scope}opcua\ninterface\n{item.InterfaceKind}\n{item.InterfaceName}" }.Concat(item.NewName is null ? Array.Empty<string>()
                    : new[] { $"{scope}opcua\ninterface\n{item.InterfaceKind}\n{item.NewName}" }).ToArray()
                : item.RoleName is not null
                    ? new[] { $"{scope}opcua\nrole\n{item.RoleName}" + (item.Operation == PlcOpcUaMethods.SetNamespacePermission ? $"\n{item.NamespaceUri}\n{item.Permission}" : string.Empty) }
                    : new[] { $"{scope}opcua\nrestriction\n{item.NamespaceUri}" };
}
