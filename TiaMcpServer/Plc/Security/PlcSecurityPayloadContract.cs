using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Json;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Plc;

/// <summary>
/// The worker parameters and the declared result types of the CPU protection operations: one CLR type per operation,
/// decoded through the worker-payload reader (every member required, explicit nulls). A payload that does not decode
/// or contradicts the request is a <c>protocol_error</c> and is never echoed back.
/// </summary>
public static class PlcSecurityPayloadContract
{
    /// <summary>Every field is forwarded once, unchanged; passwords only as password-source tokens.</summary>
    public static PlcSecurityRequestInfo ToRequest(PlcOperationRequest op) => new()
    {
        PlcName = op.PlcName,
        AccessLevel = op.AccessLevel,
        PasswordVariable = op.PasswordVariable,
        NewPasswordVariable = op.NewPasswordVariable,
        AccessLevelPasswords = (op.AccessLevelPasswords?.Entries ?? Array.Empty<PlcDownloadPasswordInfo>())
            .Select(entry => new PlcSecurityPasswordInfo { Level = entry.Type, Source = entry.Variable }).ToList(),
        ResetAccessLevelPasswords = (op.ResetAccessLevelPasswords ?? Array.Empty<string>()).ToList(),
        AllConfiguration = op.AllConfiguration == true,
    };

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcSecurityReadInfo"/> or contradicts the request.</exception>
    public static JsonElement DecodeRead(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcSecurityReadInfo>(payload, read =>
        {
            if (read.Plcs is null || read.Plcs.Count == 0) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(read.Plcs);
            foreach (var plc in read.Plcs)
            {
                if (string.IsNullOrWhiteSpace(plc.PlcName) || plc.Services is null || plc.Services.Count > PlcSecurityPolicy.MaxServices)
                    throw new JsonException();
                if (operation.PlcName is { } name && !PlcNameRules.Comparer.Equals(plc.PlcName, name) && !PlcNameRules.Comparer.Equals(plc.DeviceName, name))
                    throw new JsonException("A PLC the request did not name was returned.");
                StandalonePayloadContract.RejectNullElements(plc.Services);
                CheckSetting(plc.AccessLevel, PlcSecurityPolicy.AccessLevelProviderType, PlcSecurityPolicy.AccessLevelProperty);
                CheckSetting(plc.AccessControl, PlcSecurityPolicy.AccessControlProviderType, PlcSecurityPolicy.AccessControlProperty);
                CheckConfidential(plc.ConfidentialDataProtection);
            }
        }).Element;

    /// <exception cref="JsonException">The payload does not decode as <see cref="PlcSecurityWriteResultInfo"/> or contradicts the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest operation, string payload)
        => CanonicalJson.NormalizeWorkerPayload<PlcSecurityWriteResultInfo>(payload, result =>
        {
            if (result.Operation != operation.Operation || result.Setting != PlcSecurityPolicy.Settings.For(operation.Operation)
                || string.IsNullOrWhiteSpace(result.PlcName) || string.IsNullOrWhiteSpace(result.ServiceType) || string.IsNullOrWhiteSpace(result.Member)
                || !PlcSecurityPolicy.ReadBacks.IsKnown(result.ReadBack)
                || result.ObservedBefore is null || result.ObservedAfter is null)
                throw new JsonException();
            if (operation.PlcName is { } name && !PlcNameRules.Comparer.Equals(result.PlcName, name) && !PlcNameRules.Comparer.Equals(result.DeviceName, name))
                throw new JsonException("The result names another PLC.");
            CheckMembers(result.ObservedBefore);
            CheckMembers(result.ObservedAfter);
            if (result.Calls is null || result.Calls.Count == 0 || result.PasswordSources is null) throw new JsonException();
            StandalonePayloadContract.RejectNullElements(result.Calls);
            StandalonePayloadContract.RejectNullElements(result.PasswordSources);
            // Only tokens of the request may come back, never anything that is not a password source.
            var requested = new HashSet<string?>(new[] { operation.PasswordVariable, operation.NewPasswordVariable }
                .Concat((operation.AccessLevelPasswords?.Entries ?? Array.Empty<PlcDownloadPasswordInfo>()).Select(e => e.Variable)), StringComparer.Ordinal);
            if (result.PasswordSources.Any(use => !requested.Contains(use.Source) || !Contracts.Shared.PasswordSources.IsSource(use.Source)))
                throw new JsonException("The result names a password source the request did not give.");
            if (operation.Operation == PlcSecurityMethods.SetAccessLevel)
            {
                // The requested level is echoed; a readable read-back must equal it (the worker fails postcondition_failed otherwise).
                if (result.Requested != operation.AccessLevel || result.ReadBack == PlcSecurityPolicy.ReadBacks.NotAvailable
                    || !result.BeforeReadable && result.Before is not null
                    || result.AfterReadable != (result.ReadBack == PlcSecurityPolicy.ReadBacks.Matched)
                    || result.AfterReadable && result.After != operation.AccessLevel
                    || !result.AfterReadable && result.After is not null)
                    throw new JsonException();
            }
            else if (result.Requested is not null || result.Before is not null || result.After is not null
                     || result.BeforeReadable || result.AfterReadable || result.ReadBack != PlcSecurityPolicy.ReadBacks.NotAvailable)
            {
                throw new JsonException();
            }
        }).Element;

    private static void CheckSetting(PlcSecuritySettingInfo? setting, string service, string property)
    {
        if (setting is null || setting.Service != service || setting.Property != property || setting.AvailableValues is null
            || setting.ServiceOffered != (setting.ServiceType is not null)
            || !setting.Readable && setting.Value is not null
            || !setting.ServiceOffered && (setting.Readable || setting.Writable))
            throw new JsonException();
        StandalonePayloadContract.RejectNullElements(setting.AvailableValues);
    }

    private static void CheckConfidential(PlcConfidentialDataInfo? info)
    {
        if (info is null || info.Service != PlcSecurityPolicy.MasterSecretConfiguratorName || info.UnprotectCandidates is null || info.ObservedMembers is null
            || info.ServiceOffered != (info.ServiceType is not null)
            || !info.ServiceOffered && (info.ProtectBindable || info.UnprotectBindable || info.UnprotectCandidates.Count > 0 || info.ObservedMembers.Count > 0))
            throw new JsonException();
        StandalonePayloadContract.RejectNullElements(info.UnprotectCandidates);
        CheckMembers(info.ObservedMembers);
    }

    private static void CheckMembers(List<PlcSecurityMemberInfo> members)
    {
        StandalonePayloadContract.RejectNullElements(members);
        if (members.Count > PlcSecurityPolicy.MaxMembers || members.Any(m => string.IsNullOrWhiteSpace(m.Name) || !m.Readable && m.Value is not null))
            throw new JsonException();
    }
}
