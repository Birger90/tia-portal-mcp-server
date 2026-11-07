using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// The Siemens-free rules of <c>plc_download</c>, shared by host, worker and FakeWorker: the
/// scope-to-<c>DownloadOptions</c> map, the worker request validation, the caller-decision shape
/// rules, the built-in default answers and the exact canonical-type walk. The worker maps a live
/// <c>DownloadConfiguration</c> to its exact canonical public type name
/// (<see cref="CanonicalConfigurationType(Type)"/>) and asks <see cref="PlcDownloadDecisionSet"/>
/// through <see cref="PlcDownloadCallbackRecorder"/>; a configuration with neither a caller decision
/// nor a built-in default aborts the download.
/// </summary>
public static class PlcDownloadPolicy
{
    /// <summary>
    /// The name of TIA Portal's simulation PC interface (ordinal). Any other PC interface is treated as
    /// real hardware: it fires the acknowledge guard <c>download_to_hardware</c> and makes the
    /// built-in <c>TargetForSoftware</c> answer <c>CPU</c>. A name check on TIA Portal's offline PC
    /// interface entry only: it does not prove what is reachable behind that interface.
    /// </summary>
    public const string SimulationPcInterfaceName = "PLCSIM";

    /// <summary>Namespace of the public V21 download configuration types; the only one canonical types come from, apart from the one exact F-program type <c>Siemens.Engineering.Safety.Download.Configurations.SafetyProgram</c> (decision key <c>SafetyProgram</c>, no default answer).</summary>
    public const string ConfigurationsNamespace = "Siemens.Engineering.Download.Configurations";

    /// <summary>TIA Portal confirmations raised while a download runs are recorded up to this count (all are counted).</summary>
    public const int MaxPortalConfirmations = 100;

    public const int DefaultPcInterfaceNumber = 1;

    public const string DefaultMode = "PN/IE";

    /// <summary>Longest accepted PLC, mode, PC-interface, target-interface, decision key, selection or variable name.</summary>
    public const int MaxNameLength = 256;

    /// <summary>A download presenting more configurations than this is aborted rather than answered.</summary>
    public const int MaxConfigurations = 100;

    /// <summary>At most this many caller <c>decisions</c> and, separately, <c>passwords</c> entries.</summary>
    public const int MaxDecisions = 64;

    /// <summary>Enum member names listed per configuration in <c>availableSelections</c>; more are cut.</summary>
    public const int MaxAvailableSelections = 64;

    /// <summary>Nesting depth of projected <c>DownloadResult.Messages</c>; deeper children are omitted.</summary>
    public const int MaxMessageDepth = 8;

    /// <summary>Total projected <c>DownloadResult</c> messages, at every depth.</summary>
    public const int MaxMessages = 500;

    /// <summary>Longest projected Siemens message text; longer text is cut.</summary>
    public const int MaxTextChars = 1000;

    /// <summary>The caller decision key that is never accepted: the post-download start decision follows <c>startAfterDownload</c>.</summary>
    public const string StartModulesKey = "StartModules";

    public const string CompletionNote =
        "Not verified: TIA Portal V21 documents that DownloadProvider.Download may return before the device has "
        + "finished processing the download. This result does not show that the PLC or simulator finished loading "
        + "or is running; check its state before relying on it.";

    public static bool IsSimulationInterface(string? pcInterfaceName)
        => string.Equals(pcInterfaceName, SimulationPcInterfaceName, StringComparison.Ordinal);

    /// <summary>Cuts projected Siemens text at <see cref="MaxTextChars"/>.</summary>
    public static string? BoundText(string? text)
        => text is null || text.Length <= MaxTextChars ? text : text.Substring(0, MaxTextChars);

    /// <summary>
    /// True when the caller did not choose the PC interface number and the selected mode offers more
    /// than one PC interface named exactly <paramref name="requestedName"/> (whatever their numbers):
    /// a default number must not silently pick one of several equally named interfaces. Applies to
    /// every interface name, not only <see cref="SimulationPcInterfaceName"/>.
    /// </summary>
    public static bool IsImplicitInterfaceNumberAmbiguous(IEnumerable<string?> pcInterfaceNamesInMode, string? requestedName, bool numberExplicit)
        => !numberExplicit && pcInterfaceNamesInMode.Count(name => string.Equals(name, requestedName, StringComparison.Ordinal)) > 1;

    /// <summary>
    /// The worker's own re-validation of a host request (defense in depth behind host validation),
    /// Siemens-free so it is unit-tested and shared with the FakeWorker. Returns the
    /// <c>DownloadOptions</c> names on success. Hardware PC interfaces are allowed: the access mode
    /// the server was started with controls downloads, not this check.
    /// </summary>
    public static PlcDownloadRequestValidation ValidateWorkerRequest(PlcDownloadRequestInfo? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlcName) || string.IsNullOrWhiteSpace(request.DeviceName)
            || string.IsNullOrWhiteSpace(request.Mode) || string.IsNullOrWhiteSpace(request.PcInterfaceName)
            || string.IsNullOrWhiteSpace(request.TargetInterface))
            return PlcDownloadRequestValidation.Fail(WorkerFailureCategories.ValidationError,
                "plc_download requires the exact resolved PLC, device, mode, PC interface and target interface.");
        if (!TryGetDownloadOptionNames(request.Scope, out var options))
            return PlcDownloadRequestValidation.Fail(WorkerFailureCategories.ValidationError, $"Unknown download scope '{request.Scope}'.");
        if (!PortalConfirmationPolicies.IsKnown(request.PortalConfirmations))
            return PlcDownloadRequestValidation.Fail(WorkerFailureCategories.ValidationError,
                "plc_download requires portalConfirmations 'decline' or 'accept'.");
        var decisions = ValidateDecisions(request.Decisions, request.Passwords);
        if (decisions is not null)
            return PlcDownloadRequestValidation.Fail(WorkerFailureCategories.ValidationError, decisions);
        if (PlcTlsTrustPolicy.ValidateNormalized(request.TrustedCertificates) is { } trusted)
            return PlcDownloadRequestValidation.Fail(WorkerFailureCategories.ValidationError, trusted);
        return PlcDownloadRequestValidation.Ok(options);
    }

    /// <summary>
    /// The shape rules of caller <c>decisions</c> and <c>passwords</c>, shared by host and worker:
    /// keys are configuration type short names (identifiers, unique, never in both maps, never
    /// <see cref="StartModulesKey"/>); a decision is exactly one non-blank enum member name or a check;
    /// a password entry names an environment variable (ASCII letters, digits and underscores, not
    /// starting with a digit). Unknown keys are allowed: configuration types may appear only at
    /// runtime. Returns the first violation, or null. Messages never echo an invalid value.
    /// </summary>
    public static string? ValidateDecisions(IReadOnlyList<PlcDownloadDecisionInfo>? decisions, IReadOnlyList<PlcDownloadPasswordInfo>? passwords)
    {
        if (decisions is null || passwords is null) return "decisions and passwords must be objects when supplied.";
        if (decisions.Count > MaxDecisions) return $"decisions accepts at most {MaxDecisions} entries.";
        if (passwords.Count > MaxDecisions) return $"passwords accepts at most {MaxDecisions} entries.";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (decision is null || !IsTypeKey(decision.Type))
                return KeyMessage("decisions", decision?.Type);
            if (!keys.Add(decision.Type)) return $"decisions lists '{decision.Type}' more than once.";
            if (PlcTlsTrustPolicy.IsTlsVerificationKey(decision.Type)) return PlcTlsTrustPolicy.DecisionKeyRefusal("decisions");
            if (string.Equals(decision.Type, StartModulesKey, StringComparison.Ordinal))
                return "decisions.StartModules is not accepted: the post-download start decision follows startAfterDownload "
                    + "(true answers StartModule, false answers NoAction).";
            if (decision.Check)
            {
                if (decision.Selection is not null)
                    return $"decisions.{decision.Type} must be either a selection name or true, not both.";
            }
            else if (decision.Selection is null || !IsIdentifier(decision.Selection))
            {
                return $"decisions.{decision.Type} must be a non-empty enum member name (exact, case-sensitive; letters, digits and "
                    + $"underscores, at most {MaxNameLength} characters) or true.";
            }
        }

        var passwordKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var password in passwords)
        {
            if (password is null || !IsTypeKey(password.Type))
                return KeyMessage("passwords", password?.Type);
            if (!passwordKeys.Add(password.Type)) return $"passwords lists '{password.Type}' more than once.";
            if (PlcTlsTrustPolicy.IsTlsVerificationKey(password.Type)) return PlcTlsTrustPolicy.DecisionKeyRefusal("passwords");
            if (keys.Contains(password.Type))
                return $"'{password.Type}' is listed in both decisions and passwords; give a password configuration only a passwords entry.";
            if (!Shared.PasswordSources.IsSource(password.Variable))
                return $"passwords.{password.Type} must be the NAME of an environment variable (ASCII letters, digits and underscores, "
                    + $"not starting with a digit, at most {MaxNameLength} characters) or {{ \"credential\": \"{Shared.PasswordSources.CredentialTargetPrefix}...\" }} "
                    + "(a Windows Credential Manager generic credential) that holds the password; never pass the password itself.";
        }

        return null;
    }

    private static string KeyMessage(string map, string? key)
        => key is not null && key.StartsWith(ConfigurationsNamespace + ".", StringComparison.Ordinal) && IsTypeKey(key.Substring(ConfigurationsNamespace.Length + 1))
            ? $"{map} keys are configuration type short names: use '{key.Substring(ConfigurationsNamespace.Length + 1)}', not the full type name."
            : $"{map} contains a key that is not a configuration type short name (letters, digits and underscores, at most {MaxNameLength} characters).";

    /// <summary>A configuration type short name as a decision key: an identifier of at most <see cref="MaxNameLength"/> characters.</summary>
    public static bool IsTypeKey(string? value) => IsIdentifier(value);

    /// <summary>Letters, digits and underscores, not starting with a digit, 1–<see cref="MaxNameLength"/> characters.</summary>
    public static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.Length > MaxNameLength) return false;
        if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
        return value.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>ASCII letters, digits and underscores, not starting with a digit, 1–<see cref="MaxNameLength"/> characters.</summary>
    public static bool IsEnvironmentVariableName(string? value)
    {
        if (string.IsNullOrEmpty(value) || value!.Length > MaxNameLength) return false;
        static bool Letter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
        return Letter(value[0]) && value.All(c => Letter(c) || c is >= '0' and <= '9');
    }

    /// <summary>
    /// The exact canonical type of a live configuration: the first public type, walking the runtime
    /// type and then its base types, whose namespace is <see cref="ConfigurationsNamespace"/>; its full
    /// name, or null when the chain has none. A public subclass of a type in that namespace is
    /// therefore its own type (unlike an <c>is</c> check), and nested types never match.
    /// </summary>
    public static string? CanonicalConfigurationType(Type? runtimeType)
        => CanonicalConfigurationType(TypeChain(runtimeType));

    /// <summary>
    /// <see cref="CanonicalConfigurationType(Type)"/> over names: <paramref name="typeChain"/> is the
    /// runtime type followed by its base types, each as namespace, full name and whether it is public.
    /// </summary>
    public static string? CanonicalConfigurationType(IEnumerable<PlcDownloadTypeLevel> typeChain)
    {
        foreach (var level in typeChain)
        {
            // The F-program download configuration lives in the Safety namespace; it is the one type admitted from there.
            if (level.IsPublic && string.Equals(level.FullName, Safety.PlcSafetyApiCatalog.SafetyProgramConfigurationType, StringComparison.Ordinal))
                return level.FullName;
            if (!level.IsPublic || !string.Equals(level.Namespace, ConfigurationsNamespace, StringComparison.Ordinal))
                continue;
            return DecisionKey(level.FullName) is null ? null : level.FullName;
        }

        return null;
    }

    /// <summary>
    /// The caller decision key of a canonical type: its short name (the full name without
    /// <see cref="ConfigurationsNamespace"/>), or null when <paramref name="canonicalType"/> is not a
    /// well-formed type of that namespace.
    /// </summary>
    public static string? DecisionKey(string? canonicalType)
    {
        if (string.Equals(canonicalType, Safety.PlcSafetyApiCatalog.SafetyProgramConfigurationType, StringComparison.Ordinal))
            return Safety.PlcSafetyApiCatalog.SafetyProgramDecisionKey;
        const string prefix = ConfigurationsNamespace + ".";
        if (canonicalType is null || !canonicalType.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var key = canonicalType.Substring(prefix.Length);
        return IsTypeKey(key) ? key : null;
    }

    /// <summary>The namespace the canonical types of <paramref name="phase"/> come from: the online configurations for the online legitimation, otherwise the download configurations.</summary>
    public static string ConfigurationsNamespaceFor(string? phase)
        => phase == Phases.OnlineLegitimation ? PlcCallbackPolicy.OnlineConfigurationsNamespace : ConfigurationsNamespace;

    /// <summary>
    /// <see cref="CanonicalConfigurationType(Type)"/> for a configuration presented in <paramref name="phase"/>: the online
    /// legitimation walks to the first public type of <c>Siemens.Engineering.Online.Configurations</c>.
    /// </summary>
    public static string? CanonicalConfigurationType(string? phase, Type? runtimeType)
    {
        var ns = ConfigurationsNamespaceFor(phase);
        foreach (var level in TypeChain(runtimeType))
        {
            if (phase != Phases.OnlineLegitimation && level.IsPublic
                && string.Equals(level.FullName, Safety.PlcSafetyApiCatalog.SafetyProgramConfigurationType, StringComparison.Ordinal))
                return level.FullName;
            if (!level.IsPublic || !string.Equals(level.Namespace, ns, StringComparison.Ordinal)) continue;
            return DecisionKey(phase, level.FullName) is null ? null : level.FullName;
        }

        return null;
    }

    /// <summary>
    /// <see cref="DecisionKey(string)"/> for <paramref name="phase"/>: the short name of a well-formed type of
    /// <see cref="ConfigurationsNamespaceFor"/>, or null.
    /// </summary>
    public static string? DecisionKey(string? phase, string? canonicalType)
    {
        if (phase != Phases.OnlineLegitimation && string.Equals(canonicalType, Safety.PlcSafetyApiCatalog.SafetyProgramConfigurationType, StringComparison.Ordinal))
            return Safety.PlcSafetyApiCatalog.SafetyProgramDecisionKey;
        var prefix = ConfigurationsNamespaceFor(phase) + ".";
        if (canonicalType is null || !canonicalType.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var key = canonicalType.Substring(prefix.Length);
        return IsTypeKey(key) ? key : null;
    }

    private static IEnumerable<PlcDownloadTypeLevel> TypeChain(Type? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return new PlcDownloadTypeLevel(current.Namespace, current.FullName, current.IsPublic);
    }

    public static class Scopes
    {
        public const string HardwareAndSoftware = "hardware_and_software";
        public const string Software = "software";
        public const string SoftwareOnlyChanges = "software_only_changes";

        public static IReadOnlyList<string> All { get; } = new[] { HardwareAndSoftware, Software, SoftwareOnlyChanges };
    }

    /// <summary>
    /// The <c>DownloadOptions</c> member names for <paramref name="scope"/>, as one
    /// <c>Enum.Parse</c> string. <c>Software</c> is never combined with <c>SoftwareOnlyChanges</c>
    /// and <c>None</c> is never used.
    /// </summary>
    public static bool TryGetDownloadOptionNames(string? scope, out string optionNames)
    {
        switch (scope)
        {
            case Scopes.HardwareAndSoftware:
                optionNames = "Hardware, Software";
                return true;
            case Scopes.Software:
                optionNames = "Software";
                return true;
            case Scopes.SoftwareOnlyChanges:
                optionNames = "SoftwareOnlyChanges";
                return true;
            default:
                optionNames = string.Empty;
                return false;
        }
    }

    /// <summary>How TIA Portal <c>Confirmation</c> dialogs raised while the download runs are answered.</summary>
    public static class PortalConfirmationPolicies
    {
        /// <summary>Default: every dialog is answered No, recorded, and fails the call.</summary>
        public const string Decline = "decline";

        /// <summary>Every dialog is answered Yes and recorded; the call does not fail because of it.</summary>
        public const string Accept = "accept";

        public static IReadOnlyList<string> All { get; } = new[] { Decline, Accept };

        public static bool IsKnown(string? policy) => policy is Decline or Accept;

        /// <summary>The answer recorded for every confirmation under <paramref name="policy"/>.</summary>
        public static string AnswerFor(string? policy) => policy == Accept ? PortalConfirmationAnswers.Yes : PortalConfirmationAnswers.No;
    }

    public static class PortalConfirmationAnswers
    {
        public const string Yes = "Yes";
        public const string No = "No";
    }

    public static class Phases
    {
        public const string PreDownload = "pre_download";
        public const string PostDownload = "post_download";

        /// <summary>
        /// A configuration TIA Portal presented through <c>ConnectionConfiguration.OnlineLegitimation</c> of the
        /// download provider's configuration while connecting (TLS verification, online passwords).
        /// </summary>
        public const string OnlineLegitimation = "online_legitimation";

        public static bool IsKnown(string? phase) => phase is PreDownload or PostDownload or OnlineLegitimation;
    }

    public static class Decisions
    {
        /// <summary>A selection configuration was answered: <c>CurrentSelection</c> set to <see cref="PlcDownloadConfigurationInfo.Selection"/>.</summary>
        public const string Selected = "selected";

        /// <summary>A check configuration was answered: <c>Checked</c> set to true.</summary>
        public const string Checked = "checked";

        /// <summary>A password configuration was answered with <c>SetPassword</c> from <see cref="PlcDownloadConfigurationInfo.PasswordVariable"/>.</summary>
        public const string PasswordSet = "password_set";

        /// <summary>The configuration was not answered; the callback threw to abort the download.</summary>
        public const string Aborted = "aborted";

        public static bool IsKnown(string? decision) => decision is Selected or Checked or PasswordSet or Aborted;
    }

    /// <summary>Where an applied answer came from.</summary>
    public static class Sources
    {
        /// <summary>The built-in default (<see cref="DefaultSelection"/>).</summary>
        public const string Default = "default";

        /// <summary>The caller's <c>decisions</c> or <c>passwords</c> argument.</summary>
        public const string Caller = "caller";

        /// <summary>
        /// The server's fixed TLS policy: a TLS verification configuration whose certificates are not all trusted is answered
        /// <c>NonTrusted</c> (and the download is reported as aborted with the certificate evidence) instead of throwing from
        /// the callback (K27: throwing from the online legitimation callback was followed by TIA Portal terminating, live 2026-11-02).
        /// Audit T2: also an unanswered selection configuration answered <c>NoAction</c> instead of a throw (it carries the abort).
        /// </summary>
        public const string Policy = "policy";

        public static bool IsKnown(string? source) => source is Default or Caller or Policy;
    }

    /// <summary>What the live configuration object exposes, read by reflection (evidence only).</summary>
    public static class Kinds
    {
        /// <summary>A public <c>SetPassword(SecureString)</c> method.</summary>
        public const string Password = "password";

        /// <summary>A public readable and writable <c>bool Checked</c> property.</summary>
        public const string Check = "check";

        /// <summary>A public readable and writable enum <c>CurrentSelection</c> property.</summary>
        public const string Selection = "selection";

        /// <summary>None of the above (or it could not be read).</summary>
        public const string Other = "other";

        public static bool IsKnown(string? kind) => kind is Password or Check or Selection or Other;
    }

    public static class AbortReasons
    {
        /// <summary>The configuration's type chain has no public type in <see cref="ConfigurationsNamespace"/>, so it has no decision key.</summary>
        public const string UnsupportedConfiguration = "unsupported_configuration";

        /// <summary>There is no built-in default and no caller decision or password entry for its decision key.</summary>
        public const string DecisionRequired = "decision_required";

        /// <summary>The live object lacks the member the answer needs (<c>CurrentSelection</c>, <c>Checked</c> or <c>SetPassword</c>).</summary>
        public const string DecisionNotApplicable = "decision_not_applicable";

        /// <summary>The selection name is not a member of the live <c>CurrentSelection</c> enum.</summary>
        public const string SelectionUnavailable = "selection_unavailable";

        /// <summary>The environment variable named by the password entry is not set or empty in the worker.</summary>
        public const string PasswordUnavailable = "password_unavailable";

        /// <summary>Applying the answer (setter or <c>SetPassword</c>) threw; the configuration was not answered.</summary>
        public const string DecisionRejected = "decision_rejected";

        /// <summary>More than <see cref="MaxConfigurations"/> configurations were presented.</summary>
        public const string ConfigurationLimitExceeded = "configuration_limit_exceeded";

        /// <summary>The TLS verification of the connection presented certificates that are not all in <c>trustedCertificates</c>.</summary>
        public const string CertificateNotTrusted = PlcTlsTrustPolicy.CertificateNotTrustedReason;

        /// <summary>
        /// Audit T1: a password configuration whose <c>IsSecureCommunication</c> is false or unreadable, without the call's
        /// <c>allowInsecurePasswordTransfer</c>; the password was not sent (<see cref="PlcOnline.PlcPasswordChannel"/>).
        /// </summary>
        public const string InsecureCommunication = PlcOnline.PlcPasswordChannel.InsecureCommunicationReason;

        public static IReadOnlyList<string> All { get; } = new[]
        {
            UnsupportedConfiguration, DecisionRequired, DecisionNotApplicable, SelectionUnavailable,
            PasswordUnavailable, DecisionRejected, ConfigurationLimitExceeded, CertificateNotTrusted, InsecureCommunication,
        };

        public static bool IsKnown(string? reason) => reason is not null && All.Contains(reason);
    }

    /// <summary>Canonical public type names of the configurations that have a built-in default answer.</summary>
    public static class ConfigurationTypes
    {
        private const string Namespace = ConfigurationsNamespace + ".";

        public const string StopModules = Namespace + "StopModules";
        public const string ConsistentBlocksDownload = Namespace + "ConsistentBlocksDownload";
        public const string AlarmTextLibrariesDownload = Namespace + "AlarmTextLibrariesDownload";
        public const string OverwriteSystemData = Namespace + "OverwriteSystemData";
        public const string TargetForSoftware = Namespace + "TargetForSoftware";
        public const string StartModules = Namespace + "StartModules";
    }

    /// <summary>The built-in default <c>TargetForSoftware</c> answer: PLCSIM Advanced on the PLCSIM interface, otherwise the CPU.</summary>
    public static string TargetForSoftwareDefault(string? pcInterfaceName)
        => IsSimulationInterface(pcInterfaceName) ? "PlcSimulationAdvanced" : "CPU";

    /// <summary>The six canonical types with a built-in default answer (five pre-download, <c>StartModules</c> post-download).</summary>
    public static IReadOnlyList<string> DefaultConfigurationTypes { get; } = new[]
    {
        ConfigurationTypes.StopModules, ConfigurationTypes.ConsistentBlocksDownload, ConfigurationTypes.AlarmTextLibrariesDownload,
        ConfigurationTypes.OverwriteSystemData, ConfigurationTypes.TargetForSoftware, ConfigurationTypes.StartModules,
    };

    /// <summary>
    /// The built-in default answer (an enum member name) for <paramref name="canonicalType"/> in
    /// <paramref name="phase"/>, or null when there is none. Caller decisions override these
    /// (except <c>StartModules</c>, which follows <paramref name="startAfterDownload"/>).
    /// </summary>
    public static string? DefaultSelection(string? phase, string? canonicalType, bool startAfterDownload, string? pcInterfaceName)
    {
        if (phase == Phases.PreDownload)
        {
            return canonicalType switch
            {
                ConfigurationTypes.StopModules => "StopAll",
                ConfigurationTypes.ConsistentBlocksDownload => "ConsistentDownload",
                ConfigurationTypes.AlarmTextLibrariesDownload => "ConsistentDownload",
                ConfigurationTypes.OverwriteSystemData => "Overwrite",
                ConfigurationTypes.TargetForSoftware => TargetForSoftwareDefault(pcInterfaceName),
                _ => null,
            };
        }

        if (phase == Phases.PostDownload && string.Equals(canonicalType, ConfigurationTypes.StartModules, StringComparison.Ordinal))
            return startAfterDownload ? "StartModule" : "NoAction";
        return null;
    }

    /// <summary>
    /// The one explanation of a fail-closed abort, used by the worker, the host projection and the
    /// FakeWorker: which configuration in which phase, why, what the caller can pass on a retry, and
    /// what may already have happened. Never contains a password (only variable names are known here).
    /// </summary>
    public static string AbortMessage(PlcDownloadAbortInfo abort)
    {
        if (abort is null) throw new ArgumentNullException(nameof(abort));
        if (abort.Tls is { } tls)
            return $"plc_download aborted in the {abort.Phase} phase: TIA Portal asked whether to trust the device's TLS certificate "
                + $"('{abort.Type}') and it was not trusted ({abort.Reason}: {abort.Detail}). {PlcTlsTrustPolicy.Explain(tls)} "
                + "TIA Portal asks this while connecting; configurationsSeen shows whether any download configuration was answered before it. "
                + "Inspect the PLC or simulator before retrying.";
        var key = abort.DecisionKey;
        var available = abort.AvailableSelections is { Count: > 0 } names ? string.Join(", ", names) : null;
        var guidance = abort.Reason switch
        {
            AbortReasons.UnsupportedConfiguration =>
                $"Its type is not a public type of {ConfigurationsNamespace}, so no decision can be given for it.",
            AbortReasons.DecisionRequired => abort.Kind switch
            {
                Kinds.Password => $"It asks for a password. Retry with passwords: {{ \"{key}\": \"<NAME_OF_ENVIRONMENT_VARIABLE>\" }} "
                    + "naming an environment variable set in the MCP client configuration; never pass the password itself.",
                Kinds.Check => $"There is no default and no caller decision for it. Retry with decisions: {{ \"{key}\": true }} to confirm it.",
                Kinds.Selection => $"There is no default and no caller decision for it. Retry with decisions: {{ \"{key}\": \"<selection>\" }} "
                    + $"using one of: {available ?? "(the installed enum lists no members)"}.",
                _ => $"There is no default and no caller decision for it. Retry with decisions: {{ \"{key}\": \"<selection>\" }} or true "
                    + "if it is a selection or check configuration.",
            },
            AbortReasons.DecisionNotApplicable => $"The answer for '{key}' does not fit the live configuration: {abort.Detail ?? "the needed member is missing"}.",
            AbortReasons.SelectionUnavailable => $"The selection given for '{key}' is not a member of its installed enum. Available: {available ?? "(none listed)"}.",
            AbortReasons.PasswordUnavailable => (abort.Detail ?? $"The password for '{key}' could not be read from its environment variable.")
                + " Set the variable in the MCP client configuration (env) and restart the server.",
            AbortReasons.DecisionRejected => $"TIA Portal rejected the answer for '{key}'.",
            AbortReasons.ConfigurationLimitExceeded => $"More than {MaxConfigurations} configurations were presented.",
            AbortReasons.CertificateNotTrusted => $"The device's TLS certificate is trusted only through {PlcTlsTrustPolicy.ArgumentName}.",
            AbortReasons.InsecureCommunication => (abort.Detail ?? "The password would not travel over a secure connection; it was not sent.")
                + ". Ask the user before retrying with " + PlcOnline.PlcPasswordChannel.ArgumentName + ": true.",
            _ => "It was not answered.",
        };
        return $"plc_download aborted in the {abort.Phase} phase: TIA Portal presented '{abort.Type}' ({abort.Reason}). {guidance} "
            + (abort.Phase == Phases.PostDownload
                ? "The download was already transferred; the PLC or simulator may be stopped. Inspect it before retrying."
                : abort.Phase == Phases.OnlineLegitimation
                    ? "TIA Portal asks this while connecting; configurationsSeen shows whether any download configuration was answered before it. Inspect the PLC or simulator before retrying."
                    : "The PLC or simulator may already have been stopped. Inspect it before retrying.");
    }
}

/// <summary>
/// One answer for one configuration: a selection, a check, a password from an environment
/// variable, or an abort reason. <see cref="Source"/> records where an answer came from.
/// </summary>
public sealed class PlcDownloadCallbackDecision
{
    private PlcDownloadCallbackDecision(string? decision, string? selection, string? passwordVariable, string? source, string? abortReason)
    {
        Decision = decision;
        Selection = selection;
        PasswordVariable = passwordVariable;
        Source = source;
        AbortReason = abortReason;
    }

    public bool Handled => AbortReason is null;

    /// <summary>One of <see cref="PlcDownloadPolicy.Decisions"/> except <c>aborted</c>; null when aborting.</summary>
    public string? Decision { get; }

    /// <summary>The enum member name to set as <c>CurrentSelection</c>; null otherwise.</summary>
    public string? Selection { get; }

    /// <summary>The environment variable NAME the password is read from; null otherwise. Never a password.</summary>
    public string? PasswordVariable { get; }

    /// <summary>One of <see cref="PlcDownloadPolicy.Sources"/>; null when aborting.</summary>
    public string? Source { get; }

    /// <summary>One of <see cref="PlcDownloadPolicy.AbortReasons"/>; null when handled.</summary>
    public string? AbortReason { get; }

    public static PlcDownloadCallbackDecision Select(string selection, string source)
        => new(PlcDownloadPolicy.Decisions.Selected, selection ?? throw new ArgumentNullException(nameof(selection)), null, source, null);

    public static PlcDownloadCallbackDecision Check(string source) => new(PlcDownloadPolicy.Decisions.Checked, null, null, source, null);

    public static PlcDownloadCallbackDecision Password(string variable, string source)
        => new(PlcDownloadPolicy.Decisions.PasswordSet, null, variable ?? throw new ArgumentNullException(nameof(variable)), source, null);

    public static PlcDownloadCallbackDecision Abort(string reason) => new(null, null, null, null, reason);
}

/// <summary>
/// The answers one download may give: the caller's <c>passwords</c> and <c>decisions</c> (by
/// configuration type short name, applied in every phase) over the built-in defaults
/// (<see cref="PlcDownloadPolicy.DefaultSelection"/>, phase-specific), plus the caller's trusted TLS
/// fingerprints, which only <see cref="PlcTlsTrustPolicy"/> evaluates. Shared by the worker and
/// FakeWorker recorders and by the host's evidence check, so all three agree on every answer.
/// </summary>
public sealed class PlcDownloadDecisionSet
{
    private readonly Dictionary<string, PlcDownloadDecisionInfo> _decisions;
    private readonly Dictionary<string, string> _passwords;

    public PlcDownloadDecisionSet(IEnumerable<PlcDownloadDecisionInfo>? decisions, IEnumerable<PlcDownloadPasswordInfo>? passwords,
        bool startAfterDownload, string? pcInterfaceName, IEnumerable<string>? trustedCertificates = null, bool allowInsecurePasswordTransfer = false)
    {
        AllowInsecurePasswordTransfer = allowInsecurePasswordTransfer;
        _decisions = (decisions ?? Array.Empty<PlcDownloadDecisionInfo>()).ToDictionary(decision => decision.Type, StringComparer.Ordinal);
        _passwords = (passwords ?? Array.Empty<PlcDownloadPasswordInfo>()).ToDictionary(password => password.Type, password => password.Variable, StringComparer.Ordinal);
        StartAfterDownload = startAfterDownload;
        PcInterfaceName = pcInterfaceName;
        // Only normalized fingerprints can match the (normalized) evidence; anything else is ignored.
        TrustedCertificates = (trustedCertificates ?? Array.Empty<string>()).Where(PlcTlsTrustPolicy.IsNormalizedFingerprint)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public static PlcDownloadDecisionSet For(PlcDownloadRequestInfo request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return new PlcDownloadDecisionSet(request.Decisions, request.Passwords, request.StartAfterDownload, request.PcInterfaceName,
            request.TrustedCertificates, request.AllowInsecurePasswordTransfer);
    }

    /// <summary>Audit T1: the caller agreed that a password may be sent over a connection that is not (known to be) secure.</summary>
    public bool AllowInsecurePasswordTransfer { get; }

    public bool StartAfterDownload { get; }

    public string? PcInterfaceName { get; }

    /// <summary>The caller-approved SHA-256 fingerprints; the only source of a <c>Trusted</c> TLS answer.</summary>
    public IReadOnlyList<string> TrustedCertificates { get; }

    /// <summary>
    /// The answer for a configuration whose exact canonical type is <paramref name="canonicalType"/>:
    /// a password entry, else a caller decision, else the built-in default for the phase, else an
    /// abort (<c>unsupported_configuration</c> without a decision key, <c>decision_required</c> with one).
    /// </summary>
    /// <remarks>
    /// The TLS verification of the online legitimation is never resolved here: it depends on the presented
    /// certificates (<see cref="PlcTlsTrustPolicy"/>), so this returns <c>certificate_not_trusted</c> for it.
    /// </remarks>
    public PlcDownloadCallbackDecision Resolve(string? phase, string? canonicalType)
    {
        var key = PlcDownloadPolicy.DecisionKey(phase, canonicalType);
        if (key is null || !PlcDownloadPolicy.Phases.IsKnown(phase))
            return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.UnsupportedConfiguration);
        if (phase == PlcDownloadPolicy.Phases.OnlineLegitimation && PlcTlsTrustPolicy.IsTlsVerification(canonicalType))
            return PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.CertificateNotTrusted);
        if (_passwords.TryGetValue(key, out var variable))
            return PlcDownloadCallbackDecision.Password(variable, PlcDownloadPolicy.Sources.Caller);
        if (_decisions.TryGetValue(key, out var decision))
            return decision.Check
                ? PlcDownloadCallbackDecision.Check(PlcDownloadPolicy.Sources.Caller)
                : PlcDownloadCallbackDecision.Select(decision.Selection!, PlcDownloadPolicy.Sources.Caller);
        var selection = PlcDownloadPolicy.DefaultSelection(phase, canonicalType, StartAfterDownload, PcInterfaceName);
        return selection is null
            ? PlcDownloadCallbackDecision.Abort(PlcDownloadPolicy.AbortReasons.DecisionRequired)
            : PlcDownloadCallbackDecision.Select(selection, PlcDownloadPolicy.Sources.Default);
    }
}

/// <summary>One level of a configuration's type chain, by name: what <see cref="PlcDownloadPolicy.CanonicalConfigurationType(IEnumerable{PlcDownloadTypeLevel})"/> reads.</summary>
public readonly struct PlcDownloadTypeLevel
{
    public PlcDownloadTypeLevel(string? @namespace, string? fullName, bool isPublic)
    {
        Namespace = @namespace;
        FullName = fullName;
        IsPublic = isPublic;
    }

    public string? Namespace { get; }

    public string? FullName { get; }

    public bool IsPublic { get; }
}

/// <summary>The worker's verdict on a host download request: the <c>DownloadOptions</c> names, or a failure.</summary>
public sealed class PlcDownloadRequestValidation
{
    private PlcDownloadRequestValidation(string? downloadOptions, string? category, string? message)
    {
        DownloadOptions = downloadOptions;
        FailureCategory = category;
        Message = message;
    }

    public bool IsValid => FailureCategory is null;

    /// <summary>The <c>DownloadOptions</c> member names for the request scope; null when invalid.</summary>
    public string? DownloadOptions { get; }

    public string? FailureCategory { get; }

    public string? Message { get; }

    public static PlcDownloadRequestValidation Ok(string downloadOptions) => new(downloadOptions, null, null);

    public static PlcDownloadRequestValidation Fail(string category, string message) => new(null, category, message);
}
