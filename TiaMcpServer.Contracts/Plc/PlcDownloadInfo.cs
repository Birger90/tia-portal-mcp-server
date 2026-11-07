using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Host-to-worker request of <c>plc_download</c>: the exact path the host resolved from
/// <c>list_download_targets</c>. The worker re-resolves every name exactly on the provider instance
/// that downloads and never falls back to another interface or target.
/// </summary>
public class PlcDownloadRequestInfo
{
    /// <summary>Resolved PLC software name (exact).</summary>
    public string PlcName { get; set; } = string.Empty;

    /// <summary>Resolved owning device name (exact).</summary>
    public string DeviceName { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string PcInterfaceName { get; set; } = string.Empty;

    public int PcInterfaceNumber { get; set; }

    /// <summary>
    /// True when the caller supplied <c>pcInterface</c> (and so its number). When false the worker
    /// refuses a mode offering several PC interfaces with the requested name as <c>target_ambiguous</c>
    /// (<see cref="PlcDownloadPolicy.IsImplicitInterfaceNumberAmbiguous"/>).
    /// </summary>
    public bool PcInterfaceNumberExplicit { get; set; }

    public string TargetInterface { get; set; } = string.Empty;

    /// <summary>One of <see cref="PlcDownloadPolicy.Scopes"/>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Issue #16: null for a normal download; <c>primary</c> or <c>backup</c> downloads an R/H system's CPU through RHDownloadProvider.</summary>
    public string? RhTarget { get; set; }

    public bool StartAfterDownload { get; set; }

    /// <summary>
    /// The caller's answers by configuration type short name (validated by
    /// <see cref="PlcDownloadPolicy.ValidateDecisions"/>); they override the built-in defaults.
    /// </summary>
    public List<PlcDownloadDecisionInfo> Decisions { get; set; } = new List<PlcDownloadDecisionInfo>();

    /// <summary>Password configurations answered from environment variables, by type short name. Names only, never passwords.</summary>
    public List<PlcDownloadPasswordInfo> Passwords { get; set; } = new List<PlcDownloadPasswordInfo>();

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/>.</summary>
    public string PortalConfirmations { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>
    /// Normalized SHA-256 fingerprints the caller approved (<see cref="PlcTlsTrustPolicy.NormalizeFingerprint"/>): the only
    /// way the TLS verification configuration of the download's connection is answered <c>Trusted</c>, and only when
    /// every presented certificate is listed. Empty trusts nothing.
    /// </summary>
    public List<string> TrustedCertificates { get; set; } = new List<string>();

    /// <summary>
    /// Audit T1: the caller's <c>allowInsecurePasswordTransfer</c>. False (default): a password configuration whose
    /// <c>IsSecureCommunication</c> is false or unreadable is not answered (<c>insecure_communication</c>); true sends it anyway.
    /// </summary>
    public bool AllowInsecurePasswordTransfer { get; set; }
}

/// <summary>One caller decision: a selection enum member name, or a check (<c>Checked = true</c>).</summary>
public class PlcDownloadDecisionInfo
{
    /// <summary>Configuration type short name in <c>Siemens.Engineering.Download.Configurations</c> (exact, case-sensitive).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The enum member name set as <c>CurrentSelection</c> (exact, case-sensitive); null for a check.</summary>
    public string? Selection { get; set; }

    /// <summary>True for a check decision (<c>DownloadCheckConfiguration.Checked = true</c>); false for a selection.</summary>
    public bool Check { get; set; }
}

/// <summary>One password configuration answered from an environment variable of the worker process.</summary>
public class PlcDownloadPasswordInfo
{
    /// <summary>Password configuration type short name (for example <c>ModuleWriteAccessPassword</c>).</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The NAME of the environment variable holding the password; never the password itself.</summary>
    public string Variable { get; set; } = string.Empty;
}

/// <summary>
/// Offline download configuration of one or more PLCs: connection mode, PC interface and target
/// interface names read from <c>DownloadProvider.Configuration</c>. No accessible-device discovery.
/// </summary>
public class PlcDownloadTargetInventoryInfo
{
    public List<PlcDownloadTargetPlcInfo> Plcs { get; set; } = new List<PlcDownloadTargetPlcInfo>();
}

public class PlcDownloadTargetPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>False when the PLC's device item offers no <c>DownloadProvider</c>; <see cref="Modes"/> is then empty.</summary>
    public bool Downloadable { get; set; }

    public List<PlcDownloadModeInfo> Modes { get; set; } = new List<PlcDownloadModeInfo>();
}

public class PlcDownloadModeInfo
{
    public string Name { get; set; } = string.Empty;

    public List<PlcDownloadPcInterfaceInfo> PcInterfaces { get; set; } = new List<PlcDownloadPcInterfaceInfo>();
}

public class PlcDownloadPcInterfaceInfo
{
    public string Name { get; set; } = string.Empty;

    public int Number { get; set; }

    public List<PlcDownloadTargetInterfaceInfo> TargetInterfaces { get; set; } = new List<PlcDownloadTargetInterfaceInfo>();
}

public class PlcDownloadTargetInterfaceInfo
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>A PC interface identity: name and number.</summary>
public class PlcDownloadPcInterfaceRefInfo
{
    public string Name { get; set; } = string.Empty;

    public int Number { get; set; }
}

/// <summary>The resolved download path: mode, then PC interface, then target interface.</summary>
public class PlcDownloadPathInfo
{
    public string Mode { get; set; } = string.Empty;

    public PlcDownloadPcInterfaceRefInfo PcInterface { get; set; } = new PlcDownloadPcInterfaceRefInfo();

    public string TargetInterface { get; set; } = string.Empty;
}

/// <summary>
/// Projection of one <c>plc_download</c>: the resolved path, every callback configuration with the
/// decision taken and its source, an abort record when one was not answered, and the projected
/// <c>DownloadResult</c>. Writes explicit nulls. <see cref="CompletionVerified"/> is always false.
/// Never contains a password: password configurations record only the environment variable name.
/// </summary>
public class PlcDownloadResultInfo
{
    public string Operation { get; set; } = PlcDownloadMethods.Download;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public PlcDownloadPathInfo Path { get; set; } = new PlcDownloadPathInfo();

    public string Scope { get; set; } = string.Empty;

    /// <summary>The <c>DownloadOptions</c> member names passed to <c>Download</c>.</summary>
    public string DownloadOptions { get; set; } = string.Empty;

    public bool StartAfterDownload { get; set; }

    /// <summary><c>DownloadResultState</c> member name; null when <c>Download</c> returned no result.</summary>
    public string? State { get; set; }

    public int? ErrorCount { get; set; }

    public int? WarningCount { get; set; }

    public List<PlcDownloadMessageInfo> Messages { get; set; } = new List<PlcDownloadMessageInfo>();

    /// <summary>False when messages were omitted (depth, count, an unreadable message, or the response budget).</summary>
    public bool MessagesComplete { get; set; } = true;

    /// <summary>Why messages are incomplete; null when complete.</summary>
    public string? MessageOmission { get; set; }

    public List<PlcDownloadConfigurationInfo> ConfigurationsSeen { get; set; } = new List<PlcDownloadConfigurationInfo>();

    /// <summary>The configuration that stopped the download; null when every one was answered.</summary>
    public PlcDownloadAbortInfo? Abort { get; set; }

    /// <summary>One of <see cref="PlcDownloadPolicy.PortalConfirmationPolicies"/>: how TIA Portal confirmations were answered.</summary>
    public string PortalConfirmationPolicy { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>
    /// TIA Portal <c>Confirmation</c> dialogs raised while the download ran, each answered as
    /// <see cref="PortalConfirmationPolicy"/> says (<c>No</c> for decline, <c>Yes</c> for accept); at
    /// most <see cref="PlcDownloadPolicy.MaxPortalConfirmations"/> are recorded. Empty when none was raised.
    /// </summary>
    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new List<PlcDownloadPortalConfirmationInfo>();

    /// <summary>Every TIA Portal confirmation answered during the download, including those beyond the recorded list.</summary>
    public int PortalConfirmationsObserved { get; set; }

    /// <summary>False when more confirmations were answered than <see cref="PortalConfirmations"/> lists.</summary>
    public bool PortalConfirmationsComplete { get; set; } = true;

    public bool CompletionVerified { get; set; }

    public string CompletionNote { get; set; } = string.Empty;
}

/// <summary>One projected <c>DownloadResultMessage</c> with its nested messages.</summary>
public class PlcDownloadMessageInfo
{
    public string? Message { get; set; }

    public string? State { get; set; }

    public List<PlcDownloadMessageInfo> Messages { get; set; } = new List<PlcDownloadMessageInfo>();
}

/// <summary>One pre- or post-download callback configuration and the decision this tool took.</summary>
public class PlcDownloadConfigurationInfo
{
    /// <summary>One of <see cref="PlcDownloadPolicy.Phases"/>.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>Runtime full type name.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The public type of <c>Siemens.Engineering.Download.Configurations</c> (pre- and post-download) or
    /// <c>Siemens.Engineering.Online.Configurations</c> (online legitimation) the configuration exactly is
    /// (<see cref="PlcDownloadPolicy.CanonicalConfigurationType(string, Type)"/>); null when its type chain has none.
    /// </summary>
    public string? CanonicalType { get; set; }

    /// <summary>The <c>decisions</c>/<c>passwords</c> key for it: the short name of <see cref="CanonicalType"/>; null with it.</summary>
    public string? DecisionKey { get; set; }

    /// <summary>One of <see cref="PlcDownloadPolicy.Kinds"/>: what the live object exposes.</summary>
    public string Kind { get; set; } = PlcDownloadPolicy.Kinds.Other;

    /// <summary><c>DownloadConfiguration.Message</c>; null when unreadable.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// For a selection configuration, the member names of its live <c>CurrentSelection</c> enum (at
    /// most <see cref="PlcDownloadPolicy.MaxAvailableSelections"/>); empty for every other kind.
    /// </summary>
    public List<string> AvailableSelections { get; set; } = new List<string>();

    /// <summary>One of <see cref="PlcDownloadPolicy.Decisions"/>.</summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>The selection member name set; null unless <see cref="Decision"/> is <c>selected</c>.</summary>
    public string? Selection { get; set; }

    /// <summary>One of <see cref="PlcDownloadPolicy.Sources"/> for an applied answer; null when aborted.</summary>
    public string? Source { get; set; }

    /// <summary>The environment variable NAME the password was read from; null unless <c>password_set</c>. Never the password.</summary>
    public string? PasswordVariable { get; set; }

    /// <summary>
    /// Audit T1: <c>IsSecureCommunication</c> of a password configuration the recorder had a password for, read before
    /// <c>SetPassword</c>; omitted when it is no such entry or the flag was unreadable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SecureCommunication { get; set; }

    /// <summary>
    /// Present only for the online legitimation TLS verification configuration (<see cref="PlcTlsTrustPolicy.CanonicalType"/>)
    /// when it was read: the certificates with their SHA-256 fingerprints, PlcName and VerificationInfo as text, and the selection set.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlcTlsVerificationInfo? Tls { get; set; }
}

/// <summary>One TIA Portal <c>Confirmation</c> raised while <c>plc_download</c> ran, and how it was answered.</summary>
public class PlcDownloadPortalConfirmationInfo
{
    /// <summary>Runtime full type name of the event arguments.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The dialog text when the event arguments expose a readable public <c>Text</c> string (unverified member); otherwise null.</summary>
    public string? Text { get; set; }

    /// <summary><c>No</c> (policy decline) or <c>Yes</c> (policy accept).</summary>
    public string Answer { get; set; } = PlcDownloadPolicy.PortalConfirmationAnswers.No;
}

/// <summary>
/// The configuration that was not answered and stopped the download, with what a retry needs: its
/// decision key, kind and, for a selection configuration, the live enum member names.
/// </summary>
public class PlcDownloadAbortInfo
{
    public string Phase { get; set; } = string.Empty;

    /// <summary>Runtime full type name.</summary>
    public string Type { get; set; } = string.Empty;

    public string? CanonicalType { get; set; }

    /// <summary>The <c>decisions</c>/<c>passwords</c> key a retry uses; null when the type has none.</summary>
    public string? DecisionKey { get; set; }

    /// <summary>One of <see cref="PlcDownloadPolicy.Kinds"/>.</summary>
    public string Kind { get; set; } = PlcDownloadPolicy.Kinds.Other;

    public string? Message { get; set; }

    /// <summary>The live <c>CurrentSelection</c> enum member names for a selection configuration; empty otherwise.</summary>
    public List<string> AvailableSelections { get; set; } = new List<string>();

    /// <summary>One of <see cref="PlcDownloadPolicy.AbortReasons"/>.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Why the answer did not apply (a missing member, an unset environment variable named by NAME); null otherwise. Never a password.</summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Present only when the refused configuration is the TLS verification of the connection and its evidence was read:
    /// what a human needs to decide (every certificate's SHA-256 fingerprint, subject, issuer and validity, PlcName, VerificationInfo).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlcTlsVerificationInfo? Tls { get; set; }
}
