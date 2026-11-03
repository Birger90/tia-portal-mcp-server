using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.PlcOnline;

/// <summary>Going online and offline with a PLC, registered in read-write and full (OnlineControl) only.</summary>
[McpServerToolType]
public class PlcOnlineConnectionTools
{
    internal const string K52PlcSimRule = "Exception (owner decision 2026-11-02, K52): over the PC interface exactly PLCSIM, a TLS check that presents no certificate at all is answered NonVerified (continue without a check; tls.source plcsimNoCertificate); plc_download and compare_to_online keep refusing it. ";

    [McpServerTool(Name = PlcOnlineMethods.OnlineTool, ReadOnly = false, Destructive = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcOnlineConnectionResponse))]
    [Description("Connect TIA Portal to a PLC of the bound standalone project (action go_online) or close that connection (action go_offline). "
        + "go_online resolves the exact path offline from plc_read get_online_state: connection mode (default PN/IE), PC interface { name, number } "
        + "(required; nothing is chosen by default) and target interface (optional only when the PC interface offers exactly one), then applies it "
        + "(ConnectionConfiguration.ApplyConfiguration), calls OnlineProvider.GoOnline and polls OnlineProvider.State until Online, a terminal state "
        + "(Incompatible, NotReachable, Protected) or timeoutSeconds (default 30, at most 120), because GoOnline returns before the connection is "
        + "established. A PLC that is already online is left as is (ApplyConfiguration throws while connected). Online password prompts are answered "
        + "only from the environment variables named in passwords (type short name -> variable NAME, never the password); a TLS certificate check "
        + "(TlsVerificationConfiguration) is answered Trusted only when every certificate the device presents has a SHA-256 fingerprint listed in "
        + "trustedCertificates, otherwise NonTrusted and the call aborts with result.value.abort.tls (per certificate sha256, subject, issuer, notBefore, "
        + "notAfter; plcName, verificationInfo): have the user check the fingerprints (for example in TIA Portal's trust prompt, 'Zertifikate anzeigen') "
        + "and only then retry with trustedCertificates. " + K52PlcSimRule + "Any other prompt aborts with the presented configuration in result.value.configurationsSeen. "
        + "Hardware interfaces are allowed (info guard "
        + "online_hardware_target). Going online or offline downloads nothing to the PLC (the effect on the device is not live-verified). A TIA Portal confirmation dialog raised while the action runs "
        + "(until its evidence is collected) is answered No, reported in result.value.portalConfirmations and fails the call, unless portalConfirmations "
        + "is accept (answered Yes and reported), like plc_download. Set dryRun=true to see the resolved path and guards without "
        + "connecting. Read-write asks the user to confirm each actual call; full runs it under policy.")]
    public static Task<CallToolResult> PlcOnlineConnection(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("go_online or go_offline.")] string action,
        [Description("PLC software or device name. Optional (omitted or null) when the project has exactly one PLC; several matches fail with target_ambiguous.")] string? plcName = null,
        [Description("go_online only, required: the PC interface { name, number } exactly as get_online_state lists it.")] [RejectExplicitNull] PlcOnlinePcInterfaceArgument? pcInterface = null,
        [Description("go_online only: target interface name under the PC interface, exact. Optional when the PC interface offers exactly one.")] string? targetInterface = null,
        [Description("go_online only: connection mode name, exact. Defaults to PN/IE.")] string? mode = null,
        [Description("Seconds to wait for Online (go_online) or Offline (go_offline); 1 to 120, default 30.")] int timeoutSeconds = PlcOnlinePolicy.DefaultTimeoutSeconds,
        [Description("go_online only: online password configurations to answer, as { \"<configuration type short name, e.g. OnlinePasswordConfiguration>\": \"<NAME of an environment variable of the server process>\" }. Never pass a password; a value that is not a variable name is redacted and refused. Instead of a variable name a value may be { \"env\": \"NAME\" } or { \"credential\": \"TiaMcp/...\" }, the target of a Windows Credential Manager generic credential the user stored once with cmdkey /generic (read by the worker at the call; only the target name is recorded). The server password map file (%APPDATA%\\TiaMcpServer\\passwords.json) can supply entries the call omits.")] PlcDownloadPasswordMap? passwords = null,
        [Description("TIA Portal confirmation dialogs raised during the action: decline (default; answered No, recorded, the call fails) or accept "
            + "(answered Yes and recorded).")] string portalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Decline,
        [Description("go_online only. " + PlcTrustedCertificatesDescription.Text)] PlcTrustedCertificateList? trustedCertificates = null,
        [Description("Resolve the path and evaluate guards without connecting or asking for confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
    {
        var item = new PlcOnlineConnectionItem
        {
            Action = action ?? string.Empty,
            Operation = PlcOnlineConnectionActions.WorkerMethod(action) ?? string.Empty,
            PlcName = plcName,
            PcInterface = pcInterface,
            TargetInterface = targetInterface,
            Mode = mode,
            TimeoutSeconds = timeoutSeconds,
            // Only go_online answers passwords; the server's password map adds names for it alone.
            Passwords = action == "go_online" ? Passwords(passwords, null, workerClient.BindingSnapshot.ProjectPath, plcName) : Passwords(passwords),
            PortalConfirmations = portalConfirmations ?? string.Empty,
            // An explicit JSON null means omitted.
            TrustedCertificates = trustedCertificates?.Entries,
        };
        return execution.RunAsync(new PlcOnlineConnectionWriteDomain(workerClient), new WriteCall<PlcOnlineConnectionItem>(new[] { item }, dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            bindingStrategy: null, cancellationToken);
    }

    /// <summary>
    /// Bound through plc_download's <see cref="PlcDownloadPasswordMap"/>: a duplicate key or a non-string value is an
    /// argument-shape error, and a value that is not a variable name (perhaps a pasted password) is already
    /// redacted, so it never reaches the pipeline, the response or the audit.
    /// </summary>
    internal static IReadOnlyList<PlcCallbackPasswordInfo> Passwords(PlcDownloadPasswordMap? passwords, IEnumerable<string>? decisionKeys = null,
        string? projectPath = null, string? plc = null)
        => PasswordMapStore.Merge(passwords?.Entries, decisionKeys ?? Array.Empty<string>(), projectPath, plc)
            .Select(entry => new PlcCallbackPasswordInfo { Type = entry.Type, Variable = entry.Variable }).ToArray();
}

/// <summary>Station upload into the offline project, registered in read-write and full (ProjectMutation) only.</summary>
[McpServerToolType]
public class PlcStationUploadTools
{
    [McpServerTool(Name = PlcOnlineMethods.StationUpload, ReadOnly = false, Destructive = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcStationUploadWriteResponse))]
    [Description("Upload the station (hardware, software and files) of the device at address and add it as a new device to the bound standalone "
        + "project (StationUploadProvider.StationUpload). The path is resolved offline from plc_read list_station_upload_interfaces: connection mode "
        + "(default PN/IE) and PC interface { name, number }, exact; the address is created on that PC interface. The tool sends nothing to the device's program or configuration (not live-verified); "
        + "the project is changed and not saved (save_project). Upload decisions are answered only by the caller: decisions { \"<configuration type "
        + "short name, e.g. UploadMissingProducts>\": \"<enum member name, e.g. NoAction>\" } and passwords { \"<type short name, e.g. "
        + "ModuleReadAccessPassword>\": \"<NAME of an environment variable of the server process>\" }; any configuration without an entry aborts and "
        + "result.value.abort lists its availableSelections for a retry. The upload's connection may also ask through its online legitimation: online "
        + "passwords from passwords, and a TLS certificate check (TlsVerificationConfiguration) is answered Trusted only when every certificate the device "
        + "presents has a SHA-256 fingerprint listed in trustedCertificates, otherwise NonTrusted and the upload aborts with result.value.abort.tls; have the "
        + "user check the fingerprints and only then retry with trustedCertificates. " + PlcOnlineConnectionTools.K52PlcSimRule + "Check decisions (true) are refused: no upload check configuration is "
        + "documented. TIA Portal confirmation dialogs raised during the upload are answered by portalConfirmations like plc_download. Set dryRun=true to see the resolved path and guards without connecting. "
        + "Fail-safe CPU (live V21 2026-11-02, CPU 1516F-3 PN/DP on PLCSIM): TIA Portal uploads an F-CPU (UI and Openness) only when the Safety Administration option "
        + "\"Konsistentes Laden von der F-CPU ermöglichen\" is enabled and downloaded; otherwise it refuses (\"Das konsistente fehlersichere Laden ... ist nicht aktiviert\"). "
        + "With it the upload succeeded with F-signatures identical to the source. get_safety_administration does not report this option (not visible through the V21 API as read). "
        + "Read-write asks the user to confirm each actual call; full runs it under policy. There is no rollback: on failure inspect the project tree.")]
    public static Task<CallToolResult> PlcStationUpload(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("Device address to create on the PC interface, e.g. 192.168.0.1.")] string address,
        [Description("PC interface { name, number } exactly as list_station_upload_interfaces lists it.")] PlcOnlinePcInterfaceArgument pcInterface,
        [Description("Connection mode name, exact. Defaults to PN/IE.")] string mode = PlcOnlinePolicy.DefaultMode,
        [Description("Selection configurations to answer: { \"<configuration type short name>\": \"<enum member name>\" }. TlsVerificationConfiguration is not accepted (use trustedCertificates).")] PlcDownloadDecisionMap? decisions = null,
        [Description("Password configurations to answer: { \"<configuration type short name>\": \"<NAME of an environment variable of the server process>\" }. Never pass a password; a value that is not a variable name is redacted and refused. Instead of a variable name a value may be { \"env\": \"NAME\" } or { \"credential\": \"TiaMcp/...\" }, the target of a Windows Credential Manager generic credential the user stored once with cmdkey /generic (read by the worker at the call; only the target name is recorded). The server password map file (%APPDATA%\\TiaMcpServer\\passwords.json) can supply entries the call omits.")] PlcDownloadPasswordMap? passwords = null,
        [Description("TIA Portal confirmation dialogs raised during the upload: decline (default; answered No, recorded, the call fails) or accept "
            + "(answered Yes and recorded).")] string portalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Decline,
        [Description(PlcTrustedCertificatesDescription.Text)] PlcTrustedCertificateList? trustedCertificates = null,
        [Description("Resolve the path and evaluate guards without connecting or asking for confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
    {
        var item = new PlcStationUploadWriteItem
        {
            Address = address ?? string.Empty,
            Mode = mode,
            PcInterface = pcInterface ?? new PlcOnlinePcInterfaceArgument(),
            Decisions = (decisions?.Entries ?? Array.Empty<PlcDownloadDecisionInfo>()).Where(entry => !entry.Check)
                .Select(entry => new PlcCallbackDecisionInfo { Type = entry.Type, Selection = entry.Selection ?? string.Empty }).ToArray(),
            CheckDecisions = (decisions?.Entries ?? Array.Empty<PlcDownloadDecisionInfo>()).Where(entry => entry.Check).Select(entry => entry.Type).ToArray(),
            Passwords = PlcOnlineConnectionTools.Passwords(passwords, (decisions?.Entries ?? Array.Empty<PlcDownloadDecisionInfo>()).Select(entry => entry.Type),
                workerClient.BindingSnapshot.ProjectPath),
            PortalConfirmations = portalConfirmations ?? string.Empty,
            // An explicit JSON null means omitted.
            TrustedCertificates = trustedCertificates?.Entries ?? Array.Empty<string>(),
        };
        return execution.RunAsync(new PlcStationUploadWriteDomain(workerClient), new WriteCall<PlcStationUploadWriteItem>(new[] { item }, dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            bindingStrategy: null, cancellationToken);
    }
}

internal static class PlcOnlineConnectionToolRegistration
{
    /// <summary>
    /// <c>pcInterface</c> may be omitted (go_offline) but never null (<see cref="RejectExplicitNullAttribute"/>), so its
    /// root input schema advertises a plain object. <c>passwords</c> reuses plc_download's map and schema;
    /// <c>decisions</c> (plc_station_upload) advertises enum member names only, because check decisions are refused.
    /// </summary>
    internal static JsonNode TransformSchema(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.PropertyInfo is not null || schema is not JsonObject node) return schema;
        if (context.TypeInfo.Type == typeof(PlcOnlinePcInterfaceArgument))
        {
            node["type"] = "object";
            node.Remove("default");
            return schema;
        }

        if (context.TypeInfo.Type == typeof(PlcDownloadDecisionMap))
        {
            node["type"] = new JsonArray("object", "null");
            node["additionalProperties"] = new JsonObject { ["type"] = "string", ["minLength"] = 1 };
            return schema;
        }

        return PlcDownloadToolRegistration.TransformSchema(context, schema);
    }
}
