using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Parameters of the compare, online-state, plc_tia_online and plc_station_upload worker methods
/// (<c>WorkerRequest.PlcOnline</c>). Only the members a method names are read.
/// </summary>
public class PlcOnlineRequestInfo
{
    /// <summary>Every method except list_station_upload_interfaces and plc_station_upload: PLC software or device name; null means "the only PLC" (or every PLC for get_online_state).</summary>
    public string? PlcName { get; set; }

    /// <summary>plc_go_online, plc_go_offline: the device name the host resolved; the worker refuses a PLC that no longer resolves to it.</summary>
    public string? DeviceName { get; set; }

    /// <summary>compare_software: one of <see cref="PlcOnlinePolicy.CompareTargets"/>.</summary>
    public string? CompareTarget { get; set; }

    /// <summary>compare_software: the other PLC (target <c>plc</c>) or the master copy name (target <c>master_copy</c>).</summary>
    public string? TargetName { get; set; }

    /// <summary>compare_software target <c>secondary_plc</c>: the path of the open secondary project the target PLC is in (issue #20).</summary>
    public string? TargetProject { get; set; }

    /// <summary>compare_*: depth cap of the projected tree (root is depth 1).</summary>
    public int MaxDepth { get; set; } = PlcOnlinePolicy.DefaultCompareDepth;

    /// <summary>compare_*: element cap of the projected tree.</summary>
    public int MaxElements { get; set; } = PlcOnlinePolicy.DefaultCompareElements;

    /// <summary>compare_*: skip elements whose state is identical (they are counted, not listed).</summary>
    public bool DifferencesOnly { get; set; } = true;

    /// <summary>compare_hardware / compare_hardware_to_online: one of <see cref="PlcOnlinePolicy.HardwareScopes"/> (null: device).</summary>
    public string? HardwareScope { get; set; }

    /// <summary>plc_go_online, plc_station_upload: exact connection mode name.</summary>
    public string? Mode { get; set; }

    /// <summary>plc_go_online, plc_station_upload: exact PC interface name.</summary>
    public string? PcInterfaceName { get; set; }

    /// <summary>plc_go_online, plc_station_upload: PC interface number.</summary>
    public int PcInterfaceNumber { get; set; }

    /// <summary>plc_go_online: exact target interface name the host resolved.</summary>
    public string? TargetInterface { get; set; }

    /// <summary>plc_go_online: seconds to poll <c>OnlineProvider.State</c> for <c>Online</c>.</summary>
    public int TimeoutSeconds { get; set; } = PlcOnlinePolicy.DefaultTimeoutSeconds;

    /// <summary>plc_station_upload: the device address created on the PC interface (<c>Addresses.Create</c>).</summary>
    public string? Address { get; set; }

    /// <summary>plc_station_upload: caller decisions for selection callback configurations.</summary>
    public List<PlcCallbackDecisionInfo> Decisions { get; set; } = new List<PlcCallbackDecisionInfo>();

    /// <summary>plc_go_online, plc_station_upload: password configurations answered from environment variables (names only).</summary>
    public List<PlcCallbackPasswordInfo> Passwords { get; set; } = new List<PlcCallbackPasswordInfo>();

    /// <summary>
    /// compare_to_online, plc_go_online, plc_station_upload: normalized SHA-256 fingerprints the caller approved
    /// (<see cref="PlcTlsTrustPolicy.NormalizeFingerprint"/>). A TLS verification configuration is answered
    /// <c>Trusted</c> only when every presented certificate's fingerprint is listed; empty trusts nothing.
    /// </summary>
    public List<string> TrustedCertificates { get; set; } = new List<string>();

    /// <summary>
    /// plc_go_online, plc_go_offline, plc_station_upload: one of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/>,
    /// how TIA Portal <c>Confirmation</c> dialogs raised while the action runs are answered (like plc_download).
    /// compare_to_online always declines and ignores this member.
    /// </summary>
    public string PortalConfirmations { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;
}

/// <summary>One mode → PC interfaces → target interfaces listing, read offline from a provider's <c>Configuration</c>.</summary>
public class PlcConnectionModeInfo
{
    public string Name { get; set; } = string.Empty;

    public List<PlcConnectionPcInterfaceInfo> PcInterfaces { get; set; } = new List<PlcConnectionPcInterfaceInfo>();
}

public class PlcConnectionPcInterfaceInfo
{
    public string Name { get; set; } = string.Empty;

    public int Number { get; set; }

    /// <summary>Empty for station upload, which addresses a device through <c>Addresses.Create</c> instead.</summary>
    public List<PlcConnectionTargetInterfaceInfo> TargetInterfaces { get; set; } = new List<PlcConnectionTargetInterfaceInfo>();
}

public class PlcConnectionTargetInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>The exact path an online action used; <see cref="TargetInterface"/> is null for station upload and go offline.</summary>
public class PlcConnectionPathInfo
{
    public string Mode { get; set; } = string.Empty;

    public PlcConnectionPcInterfaceRefInfo PcInterface { get; set; } = new PlcConnectionPcInterfaceRefInfo();

    public string? TargetInterface { get; set; }
}

public class PlcConnectionPcInterfaceRefInfo
{
    public string Name { get; set; } = string.Empty;

    public int Number { get; set; }
}

/// <summary><c>get_online_state</c>: every matching PLC's online state and its offline online-path listing.</summary>
public class PlcOnlineStateInventoryInfo
{
    public List<PlcOnlineStatePlcInfo> Plcs { get; set; } = new List<PlcOnlineStatePlcInfo>();
}

public class PlcOnlineStatePlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>False when the PLC's CPU device item offers no <c>OnlineProvider</c>; then every other member is null or empty.</summary>
    public bool OnlineProviderAvailable { get; set; }

    /// <summary><c>OnlineProvider.State</c> by name (<see cref="PlcOnlinePolicy.States"/>); null without a provider.</summary>
    public string? State { get; set; }

    /// <summary><c>ConnectionConfiguration.IsConfigured</c>; null without a provider.</summary>
    public bool? IsConfigured { get; set; }

    /// <summary><c>OnlineProvider.Configuration.Modes</c>; read offline, never <c>GetAccessibleDevices()</c>.</summary>
    public List<PlcConnectionModeInfo> Modes { get; set; } = new List<PlcConnectionModeInfo>();
}

/// <summary><c>list_station_upload_interfaces</c>: the project's <c>StationUploadProvider.Configuration</c> modes and PC interfaces.</summary>
public class PlcStationUploadInterfacesInfo
{
    /// <summary>False when the project offers no <c>StationUploadProvider</c>.</summary>
    public bool Available { get; set; }

    public List<PlcConnectionModeInfo> Modes { get; set; } = new List<PlcConnectionModeInfo>();
}

/// <summary>Result of <c>compare_to_online</c> and <c>compare_software</c>: a bounded projection of the <c>CompareResult</c> tree.</summary>
public class PlcCompareResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary><c>online</c> or one of <see cref="PlcOnlinePolicy.CompareTargets"/>.</summary>
    public string CompareTarget { get; set; } = string.Empty;

    /// <summary>The other PLC or master copy compared against; null for <c>online</c> and <c>project_library</c>.</summary>
    public string? TargetName { get; set; }

    /// <summary>compare_to_online only: <c>OnlineProvider.State</c> before the compare; null for an offline compare or without a provider.</summary>
    public string? OnlineStateBefore { get; set; }

    /// <summary>compare_to_online only: the state after the compare (and after the restoring GoOffline when one ran).</summary>
    public string? OnlineStateAfter { get; set; }

    /// <summary>
    /// compare_to_online only: true only when the restoring GoOffline ran and <c>Offline</c> was observed within
    /// 10 s; false otherwise (no restore was needed, or it failed or timed out: see <see cref="RestoreAttempted"/>).
    /// Null without an online provider.
    /// </summary>
    public bool? DisconnectedAfterCompare { get; set; }

    /// <summary>
    /// compare_to_online only: true when the PLC was not online before and was <c>Online</c> or <c>Connecting</c>
    /// after the compare (returned or threw), so GoOffline was called. Null without an online provider.
    /// </summary>
    public bool? RestoreAttempted { get; set; }

    /// <summary><c>RootElement.ComparisonResult</c> by name; null when the result has no root element.</summary>
    public string? RootState { get; set; }

    /// <summary>True when the root state is <c>ObjectsIdentical</c> or <c>FolderContentsIdentical</c>.</summary>
    public bool Identical { get; set; }

    public PlcCompareElementInfo? Root { get; set; }

    public int MaxDepth { get; set; }

    public int MaxElements { get; set; }

    public bool DifferencesOnly { get; set; }

    /// <summary>Elements listed in <see cref="Root"/>.</summary>
    public int ElementsListed { get; set; }

    /// <summary>Elements whose state is identical and that were skipped because <see cref="DifferencesOnly"/> was set.</summary>
    public int IdenticalElementsSkipped { get; set; }

    /// <summary>False when the element or depth cap stopped the walk; <see cref="Omission"/> then says where.</summary>
    public bool Complete { get; set; } = true;

    public string? Omission { get; set; }

    /// <summary>
    /// compare_to_online only: the online legitimation configurations presented while the compare ran (for example a
    /// TLS verification answered <c>Trusted</c> from <c>trustedCertificates</c>); empty when none was presented and
    /// always empty for compare_software.
    /// </summary>
    public List<PlcCallbackConfigurationInfo> ConfigurationsSeen { get; set; } = new List<PlcCallbackConfigurationInfo>();

    /// <summary>
    /// compare_to_online only: the legitimation configuration that was refused. A compare that refused one is
    /// discarded, so a non-null abort appears only in failure evidence (the failed item's result, without a tree).
    /// </summary>
    public PlcCallbackAbortInfo? Abort { get; set; }
}

/// <summary>One <c>CompareResultElement</c>.</summary>
public class PlcCompareElementInfo
{
    public string? LeftName { get; set; }

    public string? RightName { get; set; }

    /// <summary><c>ComparisonResult</c> (<c>CompareResultState</c>) by name.</summary>
    public string? ComparisonResult { get; set; }

    public string? DetailedInformation { get; set; }

    public List<PlcCompareElementInfo> Elements { get; set; } = new List<PlcCompareElementInfo>();

    /// <summary>True when this element has children that the depth or element cap left out.</summary>
    public bool ElementsTruncated { get; set; }
}

/// <summary>Result of <c>plc_go_online</c> or <c>plc_go_offline</c>.</summary>
public class PlcOnlineActionResultInfo
{
    /// <summary><c>go_online</c> or <c>go_offline</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// The path that was applied (go_online with outcome <c>connected</c>, <c>not_reached</c> or
    /// <c>configuration_not_applied</c>); null for go_offline and for <c>already_online</c>, where nothing was applied.
    /// </summary>
    public PlcConnectionPathInfo? Path { get; set; }

    /// <summary>One of <see cref="PlcOnlinePolicy.ActionOutcomes"/>; null in failure evidence (the action threw).</summary>
    public string? Outcome { get; set; }

    /// <summary>
    /// True only for <c>connected</c>: ApplyConfiguration accepted the requested path and Online was reached after
    /// GoOnline. An existing connection (<c>already_online</c>) is never verified against the requested path.
    /// </summary>
    public bool PathVerified { get; set; }

    public string? StateBefore { get; set; }

    public string? StateAfter { get; set; }

    /// <summary>The <c>OnlineState</c> that <c>GoOnline()</c> returned, by name; null when it was not called.</summary>
    public string? GoOnlineReturned { get; set; }

    /// <summary><c>ApplyConfiguration</c>'s return value; null when it was not called (already online, or go_offline).</summary>
    public bool? ConfigurationApplied { get; set; }

    /// <summary>True when the action's goal state was reached: <c>Online</c> for go_online, <c>Offline</c> for go_offline.</summary>
    public bool GoalReached { get; set; }

    /// <summary>True when polling stopped at the timeout before the goal or a terminal state.</summary>
    public bool TimedOut { get; set; }

    public int TimeoutSeconds { get; set; }

    public int ElapsedMilliseconds { get; set; }

    /// <summary>Every distinct consecutive state observed while polling, in order (at most <see cref="PlcOnlinePolicy.MaxObservedStates"/>).</summary>
    public List<string> StatesObserved { get; set; } = new List<string>();

    /// <summary>Online legitimation callback configurations presented during GoOnline.</summary>
    public List<PlcCallbackConfigurationInfo> ConfigurationsSeen { get; set; } = new List<PlcCallbackConfigurationInfo>();

    public PlcCallbackAbortInfo? Abort { get; set; }

    /// <summary>Why nothing was called, when nothing was (for example already online); null otherwise.</summary>
    public string? Note { get; set; }

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/>: how TIA Portal confirmations were answered.</summary>
    public string PortalConfirmationPolicy { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>
    /// The TIA Portal <c>Confirmation</c> dialogs raised while the action ran, each answered as
    /// <see cref="PortalConfirmationPolicy"/> says; at most <see cref="PlcDownloadPolicy.MaxPortalConfirmations"/> are listed.
    /// </summary>
    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new List<PlcDownloadPortalConfirmationInfo>();

    /// <summary>Every confirmation answered in the action's scope, listed or not.</summary>
    public int PortalConfirmationsObserved { get; set; }

    /// <summary>False when more confirmations were answered than <see cref="PortalConfirmations"/> lists.</summary>
    public bool PortalConfirmationsComplete { get; set; } = true;
}

/// <summary>Result of <c>plc_station_upload</c>.</summary>
public class PlcStationUploadResultInfo
{
    public string Address { get; set; } = string.Empty;

    public PlcConnectionPathInfo Path { get; set; } = new PlcConnectionPathInfo();

    /// <summary><c>UploadResult.State</c> by name; null when StationUpload returned no result.</summary>
    public string? State { get; set; }

    public int? ErrorCount { get; set; }

    /// <summary><c>UploadResult.UploadedStation.Name</c>; null when no station was returned.</summary>
    public string? UploadedStationName { get; set; }

    /// <summary>Schema-free projection of <c>UploadResult.Messages</c> (attributes read through <c>GetAttributeInfos</c>/<c>GetAttribute</c>).</summary>
    public List<PlcEngineeringMessageInfo> Messages { get; set; } = new List<PlcEngineeringMessageInfo>();

    public bool MessagesComplete { get; set; } = true;

    public string? MessageOmission { get; set; }

    public List<PlcCallbackConfigurationInfo> ConfigurationsSeen { get; set; } = new List<PlcCallbackConfigurationInfo>();

    public PlcCallbackAbortInfo? Abort { get; set; }

    /// <summary>Decision and password keys the caller supplied that no presented configuration used.</summary>
    public List<string> UnusedDecisions { get; set; } = new List<string>();

    /// <summary>Always true: the project was changed and is not saved; save it with save_project.</summary>
    public bool ProjectModified { get; set; } = true;

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/>: how TIA Portal confirmations were answered.</summary>
    public string PortalConfirmationPolicy { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>
    /// The TIA Portal <c>Confirmation</c> dialogs raised while the action ran, each answered as
    /// <see cref="PortalConfirmationPolicy"/> says; at most <see cref="PlcDownloadPolicy.MaxPortalConfirmations"/> are listed.
    /// </summary>
    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new List<PlcDownloadPortalConfirmationInfo>();

    /// <summary>Every confirmation answered in the action's scope, listed or not.</summary>
    public int PortalConfirmationsObserved { get; set; }

    /// <summary>False when more confirmations were answered than <see cref="PortalConfirmations"/> lists.</summary>
    public bool PortalConfirmationsComplete { get; set; } = true;
}

/// <summary>One result message, projected without a compiled schema.</summary>
public class PlcEngineeringMessageInfo
{
    /// <summary>Runtime type name.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Readable scalar attributes by name (<c>GetAttributeInfos</c> order), values as invariant text.</summary>
    public List<PlcEngineeringAttributeInfo> Attributes { get; set; } = new List<PlcEngineeringAttributeInfo>();

    /// <summary>Nested messages from enumerable engineering-object properties.</summary>
    public List<PlcEngineeringMessageInfo> Messages { get; set; } = new List<PlcEngineeringMessageInfo>();
}

public class PlcEngineeringAttributeInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }
}

/// <summary>One caller decision: the configuration type short name and the enum member name set as its <c>CurrentSelection</c>.</summary>
public class PlcCallbackDecisionInfo
{
    public string Type { get; set; } = string.Empty;

    public string Selection { get; set; } = string.Empty;
}

/// <summary>One password configuration answered from an environment variable of the worker process. The NAME only, never the password.</summary>
public class PlcCallbackPasswordInfo
{
    public string Type { get; set; } = string.Empty;

    public string Variable { get; set; } = string.Empty;
}

/// <summary>One callback configuration TIA Portal presented, and what was done with it.</summary>
public class PlcCallbackConfigurationInfo
{
    /// <summary>Runtime full type name.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The first public type of the expected namespace in the runtime type chain; null when there is none.</summary>
    public string? CanonicalType { get; set; }

    /// <summary>The <c>decisions</c>/<c>passwords</c> key: the short name of <see cref="CanonicalType"/>; null with it.</summary>
    public string? DecisionKey { get; set; }

    /// <summary>One of <see cref="PlcCallbackPolicy.Kinds"/>.</summary>
    public string Kind { get; set; } = PlcCallbackPolicy.Kinds.Other;

    /// <summary>The configuration's <c>Message</c>; null when the type has none or it is unreadable.</summary>
    public string? Message { get; set; }

    /// <summary>For a selection configuration, its live enum member names; empty otherwise.</summary>
    public List<string> AvailableSelections { get; set; } = new List<string>();

    /// <summary>One of <see cref="PlcCallbackPolicy.Decisions"/>.</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>The selection set; null unless <see cref="Decision"/> is <c>selected</c>.</summary>
    public string? Selection { get; set; }

    /// <summary>The environment variable NAME the password came from; null unless <c>password_set</c>. Never the password.</summary>
    public string? PasswordVariable { get; set; }

    /// <summary>
    /// Present only for a TLS verification configuration (<see cref="PlcTlsTrustPolicy.CanonicalType"/>) that was read:
    /// the certificates with their SHA-256 fingerprints, PlcName and VerificationInfo as text, and the selection set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlcTlsVerificationInfo? Tls { get; set; }
}

/// <summary>The configuration that was not answered and stopped the action, with what a retry needs.</summary>
public class PlcCallbackAbortInfo
{
    public string Type { get; set; } = string.Empty;

    public string? DecisionKey { get; set; }

    public string Kind { get; set; } = PlcCallbackPolicy.Kinds.Other;

    public string? Message { get; set; }

    public List<string> AvailableSelections { get; set; } = new List<string>();

    /// <summary>One of <see cref="PlcCallbackPolicy.AbortReasons"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Why the answer did not apply; never a password.</summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Present only when the refused configuration is a TLS verification whose evidence was read: what a human needs to
    /// decide (every certificate's SHA-256 fingerprint, subject, issuer and validity, PlcName, VerificationInfo).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlcTlsVerificationInfo? Tls { get; set; }
}
