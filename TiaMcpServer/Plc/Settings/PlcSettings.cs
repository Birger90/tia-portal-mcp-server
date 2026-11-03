using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Plc;

/// <summary>
/// The settings family on the host (issues #15, #13, #12, #14, #21, #25; not live-tested): the worker request, the planned effect
/// and guards (no planning read: the preview shows the requested values; read the current ones with the family's plc_read
/// operation), the declared result types (decoded through the worker-payload reader; a payload that contradicts the request is
/// a protocol_error and never echoed back) and the verification from the worker's own read-back.
/// </summary>
internal static partial class PlcSettings
{
    public const string Kind = "Settings";

    /// <summary>Every field is forwarded once, unchanged; passwords only as password-source tokens.</summary>
    public static PlcSettingsRequestInfo ToRequest(PlcOperationRequest op)
    {
        var request = new PlcSettingsRequestInfo
        {
            PlcName = op.PlcName,
            Settings = (op.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>())
                .Select(e => new PlcSettingValueInfo { Name = e.Key, Value = e.Value }).ToList(),
            PasswordVariable = op.PasswordVariable,
            Name = op.Name,
            NewName = op.NewName,
            Address = op.Address,
            Port = op.Port,
            TableName = op.TableName,
            FolderPath = op.FolderPath,
            Access = op.Access,
            Display = op.Display == true,
            FilePath = op.FilePath,
            DirectoryPath = op.DirectoryPath,
            Overwrite = op.Overwrite == true,
            Culture = op.Culture,
            Position = op.Position,
            ModuleName = op.ModuleName,
        };
        MoreRequest(op, request);
        return request;
    }

    public static (PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards) Resolve(PlcOperationRequest item, PlcTagInventoryPlcInfo? plc)
    {
        var definition = PlcSettingsCatalog.Find(item.Operation)!;
        var target = new PlcTargetIdentity(Kind, plc?.PlcName ?? string.Empty, plc?.DeviceName, item.FolderPath, item.TableName, ObjectName(item), null, null);
        var changes = new List<PlcFieldChange>();
        foreach (var entry in item.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>()) changes.Add(new PlcFieldChange(entry.Key, null, entry.Value));
        foreach (var (field, value) in Requested(item)) changes.Add(new PlcFieldChange(field, null, value));
        var guards = new List<FiredGuard>();
        var where = plc is null ? "the project" : $"the CPU of PLC '{plc.PlcName}'";
        var lowered = (item.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>())
            .Where(e => definition.Lowers.Any(l => l.Setting == e.Key && string.Equals(l.Value, e.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(e => $"{e.Key}={e.Value}").ToList();
        if (lowered.Count > 0)
            guards.Add(new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                $"{item.Operation} lowers a protection of {where} in the offline project ({string.Join(", ", lowered)}); it takes effect on the CPU after a download."));
        if (definition.Raises)
            guards.Add(new FiredGuard(PlcGuardDefinitions.RaisesCpuProtection, item.OperationId,
                $"{item.Operation} protects {where} with a password from a password source; keep the source available."));
        guards.AddRange(SpecificGuards(item, where));
        return (new PlcWriteEffect(item.Operation, target, changes, item.FilePath ?? item.DirectoryPath, null, null, null), guards);
    }

    /// <summary>The object the item names (a server, a web application, a user, a role, a test ...), for the effect target.</summary>
    private static string? ObjectName(PlcOperationRequest item) => item.Name ?? item.Address ?? item.ModuleName ?? MoreObjectName(item);

    /// <summary>The non-settings fields the item requests, as effect changes (never a password: only its source is named).</summary>
    private static IEnumerable<(string Field, string? Value)> Requested(PlcOperationRequest item)
    {
        if (item.NewName is not null) yield return ("newName", item.NewName);
        if (item.Port is { } port) yield return ("port", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (item.Access is not null) yield return ("access", item.Access);
        if (item.Culture is not null) yield return ("culture", item.Culture);
        if (item.Position is { } position) yield return ("position", position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (item.PasswordVariable is not null) yield return ("password", $"from source {item.PasswordVariable}");
        foreach (var more in MoreRequested(item)) yield return more;
    }

    private static IEnumerable<FiredGuard> SpecificGuards(PlcOperationRequest item, string where)
    {
        switch (item.Operation)
        {
            case PlcCpuSettingsMethods.SetWatchTableAccess when item.Access == "Write":
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"set_watch_table_access gives {(item.Display == true ? "the PLC display" : "the web server")} of {where} write access to watch table '{item.TableName}'.");
                break;
            case PlcCpuSettingsMethods.SetForceTableAccess:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"set_force_table_access lets {(item.Display == true ? "the PLC display" : "the web server")} of {where} use force table '{item.TableName}' ({item.Access}); forcing from there overrides the program.");
                break;
            case PlcCpuSettingsMethods.GenerateWebBlocks:
                yield return new FiredGuard(PlcGuardDefinitions.GeneratesWebBlocks, item.OperationId,
                    $"generate_web_blocks generates the web DBs of the user-defined web pages of {where}" + (item.Overwrite == true ? " and overrides existing ones (WebDBGenerateOptions.Override)." : " (WebDBGenerateOptions.None: TIA Portal refuses existing ones)."));
                break;
            case PlcCpuSettingsMethods.SetSyslog:
                foreach (var guard in SyslogGuards(item, where)) yield return guard;
                break;
            case PlcCpuSettingsMethods.SetIpAccessibility:
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"set_ip_accessibility makes {where} reachable over IP through communication module '{item.ModuleName}' (PlcAccessCommunicationModule).");
                break;
        }

        foreach (var guard in MoreGuards(item, where)) yield return guard;
    }

    /// <summary>The most protective syslog transport (mutual TLS); every other one lowers protection (security review F8).</summary>
    public const string MutualTlsTransport = "TLSServerAndClientAuthentication";

    /// <summary>
    /// set_syslog: a transport other than mutual TLS lowers protection (info); SysLogAutoAcceptClient=true makes the CPU accept
    /// client certificates automatically, which the human confirms (acknowledge).
    /// </summary>
    private static IEnumerable<FiredGuard> SyslogGuards(PlcOperationRequest item, string where)
    {
        var settings = item.Settings?.Entries ?? Array.Empty<KeyValuePair<string, string>>();
        foreach (var entry in settings)
        {
            if (entry.Key == "TransportProtocol" && !string.Equals(entry.Value?.Trim(), MutualTlsTransport, StringComparison.OrdinalIgnoreCase))
                yield return new FiredGuard(PlcGuardDefinitions.LowersCpuProtection, item.OperationId,
                    $"set_syslog sets the syslog transport of {where} to '{entry.Value}', weaker than mutual TLS ({MutualTlsTransport}); it takes effect on the CPU after a download.");
            if (entry.Key == "SysLogAutoAcceptClient" && !string.Equals(entry.Value?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                yield return new FiredGuard(PlcGuardDefinitions.SyslogAutoAcceptsCertificates, item.OperationId,
                    $"set_syslog sets SysLogAutoAcceptClient of {where} to '{entry.Value}': syslog client certificates are then accepted automatically.");
        }
    }

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcSettingsReadInfo"/> or contradicts the request.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcSettingsReadInfo>(payload, read =>
        {
            if (read.Operation != operation.Operation || read.Plcs is null || read.Project is null) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(read.Plcs);
            StandalonePayloadContract.RejectNullElements(read.Project);
            foreach (var plc in read.Plcs)
            {
                if (string.IsNullOrWhiteSpace(plc.PlcName) || plc.Sections is null) throw new JsonException();
                if (operation.PlcName is { } name && !PlcNameRules.Comparer.Equals(plc.PlcName, name) && !PlcNameRules.Comparer.Equals(plc.DeviceName, name))
                    throw new JsonException("A PLC the request did not name was returned.");
                CheckSections(plc.Sections);
            }

            CheckSections(read.Project);
        }).Element;

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcSettingsResultInfo"/> or contradicts the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcSettingsResultInfo>(payload, result =>
        {
            var projectScoped = PlcSettingsCatalog.IsProjectScoped(operation.Operation);
            if (result.Operation != operation.Operation || string.IsNullOrWhiteSpace(result.Owner) || !PlcSettingsNames.ReadBacks.IsKnown(result.ReadBack)
                || result.Calls is null || result.Changes is null || result.PasswordSources is null || result.Items is null || result.Messages is null
                || projectScoped != (result.PlcName is null))
                throw new JsonException();
            if (operation.PlcName is { } name && result.PlcName is not null && !PlcNameRules.Comparer.Equals(result.PlcName, name) && !PlcNameRules.Comparer.Equals(result.DeviceName, name))
                throw new JsonException("The result names another PLC.");
            StandalonePayloadContract.RejectNullElements(result.Calls);
            StandalonePayloadContract.RejectNullElements(result.Changes);
            StandalonePayloadContract.RejectNullElements(result.PasswordSources);
            StandalonePayloadContract.RejectNullElements(result.Items);
            StandalonePayloadContract.RejectNullElements(result.Messages);
            if (result.Changes.Any(c => string.IsNullOrWhiteSpace(c.Name) || c.Matched == false)) throw new JsonException();
            // A failed comparison fails the item in the worker; a returned result never carries one. Matched needs every change compared and equal.
            if (result.ReadBack == PlcSettingsNames.ReadBacks.Matched && (result.Changes.Count == 0 || result.Changes.Any(c => c.Matched != true)))
                throw new JsonException();
            // Only tokens of the request may come back, never anything that is not a password source.
            var requested = new HashSet<string?>(RequestedSources(operation), StringComparer.Ordinal);
            if (result.PasswordSources.Any(use => !requested.Contains(use.Source) || !PasswordSources.IsSource(use.Source)))
                throw new JsonException("The result names a password source the request did not give.");
            if (result.File is { } file && (string.IsNullOrWhiteSpace(file.Path) || file.Bytes < 0 || file.Sha256 is null || file.Sha256.Length != 64
                || file.Overwritten && operation.Overwrite != true))
                throw new JsonException();
        }).Element;

    private static IEnumerable<string?> RequestedSources(PlcOperationRequest operation)
        => new[] { operation.PasswordVariable, operation.NewPasswordVariable }.Concat(MoreSources(operation));

    private static void CheckSections(List<PlcSettingsSectionInfo> sections)
    {
        StandalonePayloadContract.RejectNullElements(sections);
        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Name) || section.Properties is null || section.Collections is null || section.Members is null
                || !section.Offered && section.Owner is not null)
                throw new JsonException();
            CheckProperties(section.Properties);
            StandalonePayloadContract.RejectNullElements(section.Members);
            CheckCollections(section.Collections, 0);
        }
    }

    private static void CheckCollections(List<PlcSettingsCollectionInfo> collections, int depth)
    {
        StandalonePayloadContract.RejectNullElements(collections);
        if (depth > 4) throw new JsonException();
        foreach (var collection in collections)
        {
            if (string.IsNullOrWhiteSpace(collection.Name) || collection.Items is null || collection.Items.Count > PlcSettingsNames.MaxItems) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(collection.Items);
            foreach (var item in collection.Items)
            {
                if (item.Properties is null || item.Collections is null) throw new JsonException();
                CheckProperties(item.Properties);
                CheckCollections(item.Collections, depth + 1);
            }
        }
    }

    private static void CheckProperties(List<PlcSettingPropertyInfo> properties)
    {
        StandalonePayloadContract.RejectNullElements(properties);
        if (properties.Any(p => string.IsNullOrWhiteSpace(p.Name) || p.Value is null || !RuntimeValueStates.All.Contains(p.Value.State)))
            throw new JsonException();
    }

    /// <summary>
    /// The worker read every change back right after the call (check readBack); a write without a comparable read (a password,
    /// a generated block list) is trusted with an honest message, a written file is reported by its hash (check file). A failed
    /// item's entry is never success true.
    /// </summary>
    public static WriteOperationVerification Verify(PlcOperationRequest item, StructuredOperationFailure? failure, IReadOnlyDictionary<string, JsonElement> results)
    {
        if (failure is not null)
            return new(item.OperationId, false, PlcVerificationPolicy.ReadBackCheck, null, null,
                $"Operation '{item.OperationId}' failed with {failure.Category}; earlier changes of the item may stay. Inspect the current state with plc_read {ReadOperationFor(item.Operation)}.");
        if (!results.TryGetValue(item.OperationId, out var element))
            return new(item.OperationId, false, PlcVerificationPolicy.ReadBackCheck, null, null, $"Operation '{item.OperationId}': the worker reported no result.");
        var result = CanonicalJson.Deserialize<PlcSettingsResultInfo>(element.GetRawText());
        var expected = string.Join("; ", result.Changes.Select(c => $"{c.Name}={c.Requested}"));
        var actual = string.Join("; ", result.Changes.Select(c => $"{c.Name}={c.After?.Value ?? c.After?.State}"));
        if (result.File is { } file)
            return new(item.OperationId, true, PlcVerificationPolicy.FileCheck, file.Sha256, file.Sha256,
                $"Operation '{item.OperationId}': the worker re-read '{file.Path}' after placing it ({file.Bytes} bytes, sha256 {file.Sha256}).");
        return result.ReadBack switch
        {
            PlcSettingsNames.ReadBacks.Matched => new(item.OperationId, true, PlcVerificationPolicy.ReadBackCheck, expected, actual,
                $"Operation '{item.OperationId}': the worker read every change back right after the call ({result.Owner}{(result.Service is null ? string.Empty : ", " + result.Service)})."),
            PlcSettingsNames.ReadBacks.Unreadable => new(item.OperationId, true, WriteVerificationChecks.Trusted, expected, actual,
                $"Operation '{item.OperationId}': the call returned without an error but a value could not be read back; inspect it with plc_read {ReadOperationFor(item.Operation)}."),
            _ => new(item.OperationId, true, WriteVerificationChecks.Trusted, expected.Length == 0 ? null : expected, actual.Length == 0 ? null : actual,
                $"Operation '{item.OperationId}': {string.Join(", ", result.Calls)} returned without an error; nothing comparable can be read back"
                + (result.PasswordSources.Count > 0 ? " (a password is never readable)." : "."))
        };
    }

    /// <summary>The family's read that shows the state a write changed.</summary>
    internal static string ReadOperationFor(string operation)
        => PlcSettingsCatalog.Find(operation) is { Issue: 15 } ? PlcCpuSettingsMethods.GetCpuSettings : MoreReadFor(operation);
}
