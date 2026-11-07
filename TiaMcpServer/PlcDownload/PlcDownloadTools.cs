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

namespace TiaMcpServer.PlcDownload;

/// <summary>
/// The guarded PLC download (PLCSIM or real hardware), registered in read-write and full
/// (OnlineControl) only. The access mode the server was started with is the human's control.
/// </summary>
[McpServerToolType]
public class PlcDownloadTools
{
    [McpServerTool(Name = PlcDownloadMethods.Download, ReadOnly = false, Destructive = true, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(PlcDownloadWriteResponse))]
    [Description("Download the bound standalone project's PLC to a PLC simulation (PC interface PLCSIM) or to a REAL controller (any other PC interface; "
        + "the machine it controls may stop or move). The access mode controls it: read-write asks the user to confirm every actual download, full runs it "
        + "under policy, read-only does not offer this tool. The path is resolved offline from plc_read list_download_targets: connection mode, then PC "
        + "interface { name, number }, then target interface; nothing falls back to another interface. Set dryRun=true to see the resolved path, effects "
        + "and guards (download_to_hardware for a non-PLCSIM interface, download_stops_plc) without touching the target. "
        + "TIA Portal asks download configuration questions in callbacks; each is matched by its exact public type in "
        + "Siemens.Engineering.Download.Configurations. Built-in answers: StopModules=StopAll, ConsistentBlocksDownload=ConsistentDownload, "
        + "AlarmTextLibrariesDownload=ConsistentDownload, OverwriteSystemData=Overwrite, TargetForSoftware=PlcSimulationAdvanced on PLCSIM and CPU "
        + "otherwise (all before the download), StartModules after it from startAfterDownload. 'decisions' answers any other configuration and overrides "
        + "the built-in answers; 'passwords' answers password configurations from environment variables. A configuration without an answer aborts the "
        + "download; result.value.abort then names its decisionKey, kind and (for a selection) availableSelections, so retry with a decision. Every "
        + "configuration and the answer's source (default or caller) is reported in result.value.configurationsSeen. A TIA Portal confirmation dialog "
        + "raised until Download returns and the evidence is collected is answered No, reported in result.value.portalConfirmations and fails the "
        + "call, unless portalConfirmations is accept (answered Yes and reported); a dialog raised after that (for example after an early return of "
        + "Download) keeps the call's answer until the next worker request starts and is reported then as a late-confirmation warning (and in the server log), not in this call's evidence. "
        + "While the download connects, TIA Portal may ask through the connection's online legitimation (phase online_legitimation in configurationsSeen): "
        + "online passwords are answered from 'passwords'; a TLS certificate check (TlsVerificationConfiguration) is answered Trusted only when every certificate "
        + "the device presents has a SHA-256 fingerprint listed in trustedCertificates, otherwise NonTrusted and the download aborts with result.value.abort.tls "
        + "(per certificate sha256, subject, issuer, notBefore, notAfter; plcName, verificationInfo): have the user check the fingerprints (for example in TIA "
        + "Portal's trust prompt, 'Zertifikate anzeigen') and only then retry with trustedCertificates. There is no default trust and no way to skip the check. "
        + "Completion is never verified (completionVerified is false): TIA Portal V21 may "
        + "return before the device finished processing the download.")]
    public static Task<CallToolResult> PlcDownload(
        OpennessWorkerClient workerClient, WriteExecution execution,
        [Description("PLC software or device name. Optional (omitted or null) when the project has exactly one PLC; several matches fail with target_ambiguous.")] string? plcName = null,
        [Description("PC interface { name, number } from list_download_targets. PLCSIM is the simulation interface; any other interface is real hardware. Omitted defaults to { name: \"PLCSIM\", number: 1 } but fails target_ambiguous when the mode offers several PLCSIM interfaces; an explicit null is rejected.")] [RejectExplicitNull] PlcDownloadPcInterfaceArgument? pcInterface = null,
        [Description("Target interface name under the PC interface, exact. Optional (omitted or null) when the PC interface offers exactly one; otherwise target_ambiguous lists the candidates.")] string? targetInterface = null,
        [Description("Connection mode name, exact. Defaults to PN/IE.")] string mode = PlcDownloadPolicy.DefaultMode,
        [Description("hardware_and_software (default, DownloadOptions Hardware|Software), software (Software) or software_only_changes (SoftwareOnlyChanges).")] string scope = PlcDownloadPolicy.Scopes.HardwareAndSoftware,
        [Description("Answer the post-download StartModules decision with StartModule (true, default) or NoAction (false, the CPU stays in STOP).")] bool startAfterDownload = true,
        [Description("Caller decisions for download configurations, overriding the built-in answers. Key: the configuration type short name in "
            + "Siemens.Engineering.Download.Configurations (decisionKey in configurationsSeen/abort), e.g. OverwriteSystemData, DifferentTargetConfiguration, "
            + "DataBlockReinitialization, ResetModule, UpgradeTargetDevice, CheckBeforeDownload. Value: the selection enum member name, exact and "
            + "case-sensitive (availableSelections), for a selection configuration, or true to confirm a check configuration. StartModules is not accepted "
            + "(use startAfterDownload). TlsVerificationConfiguration is not accepted (use trustedCertificates). SafetyProgram answers the F-program "
            + "configuration Siemens.Engineering.Safety.Download.Configurations.SafetyProgram (no default, e.g. ConsistentDownload as availableSelections lists it): "
            + "on PLCSIM and on a real controller alike, with the acknowledge guard download_includes_safety_program (a real controller also fires "
            + "download_to_hardware); read-write confirms the call, full runs under policy. Live on V21 (2026-11-02, CPU 1516F-3 PN/DP V2.9, PLCSIM) TIA Portal "
            + "refused the F-download itself ('Laden oder Ueberladen fehlersicherer Daten mit Openness ist nicht erlaubt.'): download the F-program in the "
            + "TIA Portal UI. Unknown keys are allowed; a value that does not "
            + "fit the live configuration aborts the download.")] PlcDownloadDecisionMap? decisions = null,
        [Description("Password configurations (e.g. ModuleWriteAccessPassword, ModuleReadAccessPassword, BlockBindingPassword, PlcMasterSecretPassword): key = "
            + "type short name, value = the NAME of an environment variable that holds the password (set it in the MCP client configuration 'env' and "
            + "restart the server). Never pass the password itself; passwords never appear in results or the audit. Instead of a variable name a value may be { \"env\": \"NAME\" } or { \"credential\": \"TiaMcp/...\" }, the target of a Windows Credential Manager generic credential the user stored once with cmdkey /generic (read by the worker at the call; only the target name is recorded). The server password map file (%APPDATA%\\TiaMcpServer\\passwords.json) can supply entries the call omits.")] PlcDownloadPasswordMap? passwords = null,
        [Description("TIA Portal confirmation dialogs raised during the download: decline (default; answered No, recorded, the call fails) or accept "
            + "(answered Yes and recorded).")] string portalConfirmations = PlcDownloadPolicy.PortalConfirmationPolicies.Decline,
        [Description(PlcTrustedCertificatesDescription.Text)] PlcTrustedCertificateList? trustedCertificates = null,
        [Description("R/H system only: primary or backup downloads to that CPU of the redundant system through RHDownloadProvider.DownloadToPrimary/DownloadToBackup "
            + "(same path, decisions, passwords, guards and confirmation as a normal download). Omitted or null: the normal DownloadProvider.Download. A PLC without "
            + "RHDownloadProvider fails unsupported_capability before anything is downloaded.")] string? rhTarget = null,
        [Description(PlcPasswordChannelDescription.Text)] bool allowInsecurePasswordTransfer = false,
        [Description("Resolve the path and evaluate guards without downloading or asking for confirmation.")] bool dryRun = false,
        McpServer? server = null, CancellationToken cancellationToken = default)
    {
        var item = new PlcDownloadWriteItem
        {
            // plcName and targetInterface: an explicit JSON null means omitted, like the other tools' optional selectors.
            PlcName = plcName,
            // pcInterface: an explicit JSON null is an argument-shape error ([RejectExplicitNull], before this method runs),
            // so null here always means omitted and only an omitted selector defaults. A supplied selector is used as
            // given; an explicit null name inside it fails validation.
            PcInterfaceName = pcInterface is null ? PlcDownloadPolicy.SimulationPcInterfaceName : pcInterface.Name,
            PcInterfaceNumber = pcInterface is null ? PlcDownloadPolicy.DefaultPcInterfaceNumber : pcInterface.Number,
            PcInterfaceNumberExplicit = pcInterface is not null,
            TargetInterface = targetInterface,
            Mode = mode,
            Scope = scope,
            StartAfterDownload = startAfterDownload,
            RhTarget = rhTarget,
            // decisions and passwords: an explicit JSON null means omitted. Entries are kept in ordinal key order.
            Decisions = (decisions?.Entries ?? Array.Empty<PlcDownloadDecisionInfo>()).OrderBy(entry => entry.Type, StringComparer.Ordinal).ToArray(),
            // The server's password map adds sources (names only) for configurations the caller answered neither way.
            Passwords = PasswordMapStore.Merge(passwords?.Entries, (decisions?.Entries ?? Array.Empty<PlcDownloadDecisionInfo>()).Select(d => d.Type),
                workerClient.BindingSnapshot.ProjectPath, plcName),
            PortalConfirmations = portalConfirmations,
            // trustedCertificates: an explicit JSON null means omitted.
            TrustedCertificates = trustedCertificates?.Entries ?? Array.Empty<string>(),
            AllowInsecurePasswordTransfer = allowInsecurePasswordTransfer,
        };
        return execution.RunAsync(new PlcDownloadWriteDomain(workerClient), new WriteCall<PlcDownloadWriteItem>(new[] { item }, dryRun),
            new WriteConfirmationContext(server is null ? null : UserConfirmation.For(server, TimeSpan.FromMinutes(2))),
            bindingStrategy: null, cancellationToken);
    }
}

/// <summary>The one description of the <c>allowInsecurePasswordTransfer</c> argument (audit T1; plc_download, plc_tia_online, plc_station_upload, plc_read compare items).</summary>
internal static class PlcPasswordChannelDescription
{
    internal const string Text = "Default false: a password from 'passwords' is handed to TIA Portal only when the password configuration reports "
        + "IsSecureCommunication true; when it is false or unreadable the password is not sent and the call aborts with reason insecure_communication. "
        + "true sends it anyway over the non-secure (legacy) connection, as TIA Portal allows: set it only after the user agreed. Not live-tested.";
}

/// <summary>The one description of the <c>trustedCertificates</c> argument (plc_download, plc_tia_online, plc_station_upload, plc_read compare_to_online).</summary>
internal static class PlcTrustedCertificatesDescription
{
    public const string Text = "SHA-256 fingerprints of the device's TLS certificates that the USER checked and approved (64 hexadecimal characters each, "
        + "case-insensitive, colons and spaces ignored; at most 16). A TLS certificate check (TlsVerificationConfiguration) is answered Trusted only when the "
        + "device presents at least one certificate and EVERY presented certificate's fingerprint is listed; otherwise NonTrusted and the call aborts with the "
        + "fingerprints, subjects, issuers and validity in abort.tls. Take the values only from that evidence after the user compared them with the "
        + "device's certificate; never trust a certificate on your own. Omitted or null trusts nothing.";
}

internal static class PlcDownloadToolRegistration
{
    /// <summary>
    /// The schema of a <c>trustedCertificates</c> list (or null, meaning omitted): strings of 64 hexadecimal digits with
    /// optional colons and spaces, at most <see cref="PlcTlsTrustPolicy.MaxTrustedCertificates"/>.
    /// </summary>
    internal static void TrustedCertificatesSchema(JsonObject node)
    {
        node["type"] = new JsonArray("array", "null");
        node["items"] = new JsonObject
        {
            ["type"] = "string",
            ["pattern"] = "^[ :]*([0-9A-Fa-f][ :]*){64}$",
        };
        node["maxItems"] = PlcTlsTrustPolicy.MaxTrustedCertificates;
    }

    /// <summary>
    /// <c>pcInterface</c> may be omitted but never null (<see cref="RejectExplicitNullAttribute"/>), so
    /// its root input schema advertises a plain object without a null default. <c>decisions</c> and
    /// <c>passwords</c> are bound by custom converters (duplicate keys and other value types are
    /// argument-shape errors), so their schemas are written here: objects (or null, meaning omitted)
    /// whose values are an enum member name or <c>true</c>, respectively an environment variable name. <c>trustedCertificates</c> is bound by its own converter
    /// (<see cref="TrustedCertificatesSchema"/>).
    /// </summary>
    internal static JsonNode TransformSchema(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.PropertyInfo is not null || schema is not JsonObject node) return schema;
        if (context.TypeInfo.Type == typeof(PlcDownloadPcInterfaceArgument))
        {
            node["type"] = "object";
            node.Remove("default");
        }
        else if (context.TypeInfo.Type == typeof(PlcDownloadDecisionMap))
        {
            node["type"] = new JsonArray("object", "null");
            node["additionalProperties"] = new JsonObject
            {
                ["anyOf"] = new JsonArray(
                    new JsonObject { ["type"] = "string", ["minLength"] = 1 },
                    new JsonObject { ["const"] = true }),
            };
        }
        else if (context.TypeInfo.Type == typeof(PlcDownloadPasswordMap))
        {
            node["type"] = new JsonArray("object", "null");
            node["additionalProperties"] = PasswordSourceArgument.Schema(nullable: false);
        }
        else if (context.TypeInfo.Type == typeof(PlcTrustedCertificateList))
        {
            TrustedCertificatesSchema(node);
        }

        return schema;
    }
}
