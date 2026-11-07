using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcOnline;

/// <summary>One plc_tia_online call; the operation is the worker method of the action, supplied by the host.</summary>
public sealed record PlcOnlineConnectionItem : IOperationBatchItem
{
    public string OperationId => PlcOnlineMethods.OnlineTool;

    /// <summary><see cref="PlcOnlineMethods.GoOnline"/> or <see cref="PlcOnlineMethods.GoOffline"/>.</summary>
    public string Operation { get; init; } = PlcOnlineMethods.GoOnline;

    /// <summary>The public action name: go_online or go_offline.</summary>
    public string Action { get; init; } = PlcOnlineConnectionActions.GoOnline;

    public string? PlcName { get; init; }

    /// <summary>go_online only; null means <see cref="PlcOnlinePolicy.DefaultMode"/>.</summary>
    public string? Mode { get; init; }

    /// <summary>Required for go_online, refused for go_offline.</summary>
    public PlcOnlinePcInterfaceArgument? PcInterface { get; init; }

    public string? TargetInterface { get; init; }

    public int TimeoutSeconds { get; init; } = PlcOnlinePolicy.DefaultTimeoutSeconds;

    public IReadOnlyList<PlcCallbackPasswordInfo> Passwords { get; init; } = Array.Empty<PlcCallbackPasswordInfo>();

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/> (decline by default), like plc_download.</summary>
    public string PortalConfirmations { get; init; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>go_online only: the caller's <c>trustedCertificates</c> as given; null when omitted (go_offline refuses a supplied list).</summary>
    public IReadOnlyList<string>? TrustedCertificates { get; init; }

    /// <summary>Audit T1, go_online only: <c>allowInsecurePasswordTransfer</c> (default false; go_offline refuses true).</summary>
    public bool AllowInsecurePasswordTransfer { get; init; }
}

public static class PlcOnlineConnectionActions
{
    public const string GoOnline = "go_online";
    public const string GoOffline = "go_offline";

    public static IReadOnlyList<string> All { get; } = new[] { GoOnline, GoOffline };

    public static string? WorkerMethod(string? action) => action switch
    {
        GoOnline => PlcOnlineMethods.GoOnline,
        GoOffline => PlcOnlineMethods.GoOffline,
        _ => null,
    };
}

/// <summary>One plc_station_upload call; the operation identity is supplied by the host, never the caller.</summary>
public sealed record PlcStationUploadWriteItem : IOperationBatchItem
{
    public string OperationId => PlcOnlineMethods.StationUpload;

    public string Operation => PlcOnlineMethods.StationUpload;

    public string Address { get; init; } = string.Empty;

    public string Mode { get; init; } = PlcOnlinePolicy.DefaultMode;

    public PlcOnlinePcInterfaceArgument PcInterface { get; init; } = new();

    public IReadOnlyList<PlcCallbackDecisionInfo> Decisions { get; init; } = Array.Empty<PlcCallbackDecisionInfo>();

    /// <summary>Keys the caller answered with <c>true</c> (a check decision); always refused, no upload check configuration is documented.</summary>
    public IReadOnlyList<string> CheckDecisions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<PlcCallbackPasswordInfo> Passwords { get; init; } = Array.Empty<PlcCallbackPasswordInfo>();

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/> (decline by default), like plc_download.</summary>
    public string PortalConfirmations { get; init; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>The caller's <c>trustedCertificates</c> as given (validated and normalized by <see cref="PlcTlsTrustPolicy.NormalizeList"/>).</summary>
    public IReadOnlyList<string> TrustedCertificates { get; init; } = Array.Empty<string>();

    /// <summary>Audit T1: <c>allowInsecurePasswordTransfer</c> (default false).</summary>
    public bool AllowInsecurePasswordTransfer { get; init; }
}

/// <summary>The caller's PC interface selector; both members are required when it is supplied.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcOnlinePcInterfaceArgument
{
    [JsonRequired]
    [Description("PC interface name, exact and case-sensitive, as get_online_state (plc_tia_online) or list_station_upload_interfaces (plc_station_upload) lists it.")]
    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    [JsonNumberHandling(JsonNumberHandling.Strict)]
    [Description("PC interface number, as listed.")]
    public int Number { get; set; }
}

/// <summary>
/// The resolved PLC, path and requested consequences of one plc_tia_online call. <c>TrustedCertificates</c> are the
/// normalized SHA-256 fingerprints a TLS certificate check may trust (always empty for go_offline).
/// </summary>
public sealed record PlcOnlineConnectionEffects(
    string Action,
    string PlcName,
    string DeviceName,
    PlcConnectionPathInfo? Path,
    bool TargetInterfaceSelectedAutomatically,
    string? StateBefore,
    bool? SimulationInterface,
    int TimeoutSeconds,
    IReadOnlyList<string> PasswordKeys,
    string PortalConfirmations,
    IReadOnlyList<string> TrustedCertificates)
{
    /// <summary>Audit T1: true when the caller allowed passwords over a non-secure connection; omitted otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowInsecurePasswordTransfer { get; init; }
}

/// <summary>
/// The resolved path and requested consequences of one plc_station_upload call. <c>TrustedCertificates</c> are the
/// normalized SHA-256 fingerprints a TLS certificate check of the upload's connection may trust.
/// </summary>
public sealed record PlcStationUploadEffects(
    string Address,
    PlcConnectionPathInfo Path,
    IReadOnlyList<string> DecisionKeys,
    IReadOnlyList<string> PasswordKeys,
    bool AddsDeviceToProject,
    string PortalConfirmations,
    IReadOnlyList<string> TrustedCertificates)
{
    /// <summary>Audit T1: true when the caller allowed passwords over a non-secure connection; omitted otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowInsecurePasswordTransfer { get; init; }
}

/// <summary>The plc_tia_online document: the shared write envelope plus typed effects and one typed result outcome.</summary>
public sealed record PlcOnlineConnectionResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    string Phase,
    IReadOnlyList<WriteGuardReport> Guards,
    PlcOnlineConnectionEffects? Effects,
    StandaloneToolOutcome<PlcOnlineActionResultInfo>? Result);

/// <summary>The plc_station_upload document: the shared write envelope plus typed effects and one typed result outcome.</summary>
public sealed record PlcStationUploadWriteResponse(
    string Tool,
    string ContractVersion,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    string Phase,
    IReadOnlyList<WriteGuardReport> Guards,
    PlcStationUploadEffects? Effects,
    StandaloneToolOutcome<PlcStationUploadResultInfo>? Result);

/// <summary>
/// The plc_tia_online and plc_station_upload guards. Both domains confirm every read-write call (lifecycle
/// model), so their guards are informational: the owner decision allows every target, and the prompt
/// names the consequence. No guard blocks.
/// </summary>
public static class PlcOnlineGuardDefinitions
{
    public const string HardwareTarget = "online_hardware_target";

    public const string Disconnects = "online_disconnects";

    public const string AddsDevice = "station_upload_adds_device";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new WriteGuardDefinition(HardwareTarget, WriteGuardSeverities.Info,
            "go_online connects TIA Portal to the device behind a PC interface that is not the PLCSIM simulation interface; nothing is downloaded to the device."),
        new WriteGuardDefinition(Disconnects, WriteGuardSeverities.Info,
            "go_offline closes TIA Portal's online connection to the PLC, including one opened in the TIA Portal UI."),
        new WriteGuardDefinition(AddsDevice, WriteGuardSeverities.Info,
            "Station upload reads the station at the address and adds it as a new device to the offline project (not saved); the tool sends nothing to the device (not live-verified)."),
    });
}

/// <summary>The plan-time verdict: resolved effects, or a call-level failure naming the candidates.</summary>
public sealed record PlcOnlineResolution<TEffects>(TEffects? Effects, WriteToolError? Error) where TEffects : class
{
    public bool Success => Effects is not null;
}

/// <summary>
/// Pure and Siemens-free: resolves plc_tia_online and plc_station_upload requests against the offline
/// listings. Every level matches exactly (ordinal); there is no fallback to another mode, interface or
/// target. An omitted target interface is chosen only when the PC interface offers exactly one.
/// </summary>
public static class PlcOnlinePathResolver
{
    private const int MaxCandidates = 20;

    public static PlcOnlineResolution<PlcOnlineConnectionEffects> ResolveOnline(PlcOnlineStateInventoryInfo inventory, PlcOnlineConnectionItem item)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(item);
        var named = item.PlcName is null ? string.Empty : $" named '{item.PlcName}'";
        if (inventory.Plcs.Count == 0)
            return Fail<PlcOnlineConnectionEffects>(WorkerFailureCategories.TargetNotFound, $"No PLC software{named} was found in the project.");
        if (inventory.Plcs.Count > 1)
            return Fail<PlcOnlineConnectionEffects>(WorkerFailureCategories.TargetAmbiguous,
                $"Several PLC software instances match{named}. Specify an unambiguous plcName. Candidates: "
                + Candidates(inventory.Plcs.Select(plc => $"'{plc.PlcName}' (device '{plc.DeviceName}')")));
        var plc = inventory.Plcs[0];
        if (!plc.OnlineProviderAvailable)
            return Fail<PlcOnlineConnectionEffects>(WorkerFailureCategories.TargetKindUnsupported,
                $"PLC '{plc.PlcName}' (device '{plc.DeviceName}') offers no online provider.");
        var passwordKeys = item.Passwords.Select(p => p.Type).ToArray();
        if (item.Action == PlcOnlineConnectionActions.GoOffline)
            return new(new PlcOnlineConnectionEffects(item.Action, plc.PlcName, plc.DeviceName, null, false, plc.State, null, item.TimeoutSeconds, passwordKeys,
                item.PortalConfirmations, Array.Empty<string>()), null);

        var pc = item.PcInterface!;
        var modeName = item.Mode ?? PlcOnlinePolicy.DefaultMode;
        var (pcInterface, error) = ResolvePcInterface(plc.Modes, modeName, pc.Name, pc.Number, $"PLC '{plc.PlcName}'");
        if (error is not null) return new(null, error);
        var where = $"under PC interface '{pcInterface!.Name}' #{pcInterface.Number} in mode '{modeName}' for PLC '{plc.PlcName}'";
        var candidates = item.TargetInterface is null
            ? pcInterface.TargetInterfaces
            : pcInterface.TargetInterfaces.Where(target => string.Equals(target.Name, item.TargetInterface, StringComparison.Ordinal)).ToList();
        if (candidates.Count != 1)
        {
            var listed = Candidates(pcInterface.TargetInterfaces.Select(target => $"'{target.Name}'"));
            var category = candidates.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous;
            return Fail<PlcOnlineConnectionEffects>(category, item.TargetInterface is null
                ? candidates.Count == 0 ? $"No target interface is offered {where}." : $"Several target interfaces are offered {where}. Specify targetInterface. Candidates: {listed}"
                : $"Target interface '{item.TargetInterface}' {(candidates.Count == 0 ? "is not offered" : "is offered more than once")} {where}. Target interfaces: {listed}");
        }

        var path = new PlcConnectionPathInfo
        {
            Mode = modeName,
            PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = pcInterface.Name, Number = pcInterface.Number },
            TargetInterface = candidates[0].Name,
        };
        return new(new PlcOnlineConnectionEffects(item.Action, plc.PlcName, plc.DeviceName, path, item.TargetInterface is null, plc.State,
            string.Equals(pcInterface.Name, "PLCSIM", StringComparison.Ordinal), item.TimeoutSeconds, passwordKeys, item.PortalConfirmations,
            Trusted(item.TrustedCertificates)) { AllowInsecurePasswordTransfer = item.AllowInsecurePasswordTransfer ? true : null }, null);
    }

    public static PlcOnlineResolution<PlcStationUploadEffects> ResolveUpload(PlcStationUploadInterfacesInfo interfaces, PlcStationUploadWriteItem item)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(item);
        if (!interfaces.Available)
            return Fail<PlcStationUploadEffects>(WorkerFailureCategories.TargetKindUnsupported, "The bound project offers no station upload provider.");
        var (pcInterface, error) = ResolvePcInterface(interfaces.Modes, item.Mode, item.PcInterface.Name, item.PcInterface.Number, "the station upload");
        if (error is not null) return new(null, error);
        var path = new PlcConnectionPathInfo
        {
            Mode = item.Mode,
            PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = pcInterface!.Name, Number = pcInterface.Number },
            TargetInterface = null,
        };
        return new(new PlcStationUploadEffects(item.Address, path, item.Decisions.Select(d => d.Type).ToArray(),
            item.Passwords.Select(p => p.Type).ToArray(), AddsDeviceToProject: true, item.PortalConfirmations, Trusted(item.TrustedCertificates))
            { AllowInsecurePasswordTransfer = item.AllowInsecurePasswordTransfer ? true : null }, null);
    }

    /// <summary>The normalized fingerprints (validated before planning; an invalid list trusts nothing).</summary>
    private static IReadOnlyList<string> Trusted(IReadOnlyList<string>? raw)
        => PlcTlsTrustPolicy.NormalizeList(raw, out var normalized) is null ? normalized : Array.Empty<string>();

    private static (PlcConnectionPcInterfaceInfo? PcInterface, WriteToolError? Error) ResolvePcInterface(
        IReadOnlyList<PlcConnectionModeInfo> modes, string modeName, string pcName, int pcNumber, string owner)
    {
        var matches = modes.Where(mode => string.Equals(mode.Name, modeName, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1)
            return (null, new WriteToolError(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"Connection mode '{modeName}' {(matches.Count == 0 ? "is not configured" : "is configured more than once")} for {owner}. Modes: "
                + Candidates(modes.Select(mode => $"'{mode.Name}'"))));
        var mode = matches[0];
        var pcInterfaces = mode.PcInterfaces.Where(pc => string.Equals(pc.Name, pcName, StringComparison.Ordinal) && pc.Number == pcNumber).ToList();
        if (pcInterfaces.Count != 1)
            return (null, new WriteToolError(pcInterfaces.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"PC interface '{pcName}' #{pcNumber} {(pcInterfaces.Count == 0 ? "is not offered" : "is offered more than once")} in mode '{mode.Name}' for {owner}. PC interfaces: "
                + Candidates(mode.PcInterfaces.Select(pc => $"'{pc.Name}' #{pc.Number}"))));
        return (pcInterfaces[0], null);
    }

    private static string Candidates(IEnumerable<string> names)
    {
        var all = names.ToList();
        if (all.Count == 0) return "(none).";
        var shown = string.Join(", ", all.Take(MaxCandidates));
        return all.Count > MaxCandidates ? $"{shown} and {all.Count - MaxCandidates} more." : shown + ".";
    }

    private static PlcOnlineResolution<T> Fail<T>(string category, string message) where T : class => new(null, new WriteToolError(category, message));
}
