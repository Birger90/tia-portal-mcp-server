using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Fail-closed rules for the callback configurations TIA Portal presents during a station upload
/// (<c>UploadConfigurationDelegate</c>) or an online legitimation (<c>OnlineConfigurationDelegate</c>).
/// Every configuration is answered only by an explicit caller entry keyed by its canonical type short
/// name: a selection by an enum member name (<c>decisions</c>), a password by the NAME of an environment
/// variable of the worker process (<c>passwords</c>). The TLS verification configuration is the one
/// exception: it is answered only by <c>trustedCertificates</c> (<see cref="PlcTlsTrustPolicy"/>), never by a
/// decision. Anything else aborts and is reported with the live enum member names a retry can use.
/// </summary>
public static class PlcCallbackPolicy
{
    public const string UploadConfigurationsNamespace = "Siemens.Engineering.Upload.Configurations";

    public const string OnlineConfigurationsNamespace = "Siemens.Engineering.Online.Configurations";

    public const int MaxConfigurations = 100;

    public const int MaxAvailableSelections = 64;

    public const int MaxEntries = 64;

    public const int MaxVariableNameLength = 128;

    public static class Kinds
    {
        public const string Selection = "selection";
        public const string Password = "password";
        public const string Other = "other";
    }

    public static class Decisions
    {
        public const string Selected = "selected";
        public const string PasswordSet = "password_set";
        public const string Aborted = "aborted";
    }

    public static class AbortReasons
    {
        /// <summary>The configuration has a decision key, but the caller supplied no entry for it.</summary>
        public const string DecisionMissing = "decision_missing";

        /// <summary>Not a public type of the expected namespace, or neither a selection nor a password configuration.</summary>
        public const string UnsupportedConfiguration = "unsupported_configuration";

        /// <summary>The selection is not an exact member name of the live enum.</summary>
        public const string SelectionUnavailable = "selection_unavailable";

        /// <summary>The named environment variable is unset or empty.</summary>
        public const string PasswordUnavailable = "password_unavailable";

        /// <summary>A selection was given for a password configuration, or a password for a selection one.</summary>
        public const string DecisionNotApplicable = "decision_not_applicable";

        /// <summary>Setting the selection or the password threw.</summary>
        public const string AnswerRejected = "answer_rejected";

        public const string ConfigurationLimitExceeded = "configuration_limit_exceeded";

        /// <summary>A TLS verification configuration whose presented certificates are not all in <c>trustedCertificates</c>.</summary>
        public const string CertificateNotTrusted = PlcTlsTrustPolicy.CertificateNotTrustedReason;

        public static IReadOnlyList<string> All { get; } = new[]
        {
            DecisionMissing, UnsupportedConfiguration, SelectionUnavailable, PasswordUnavailable,
            DecisionNotApplicable, AnswerRejected, ConfigurationLimitExceeded, CertificateNotTrusted,
        };
    }

    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public static bool IsIdentifier(string? value) => value is not null && value.Length <= PlcOnlinePolicy.MaxNameLength && Identifier.IsMatch(value);

    /// <summary>
    /// The first public type of the runtime type chain whose namespace is exactly
    /// <paramref name="configurationsNamespace"/>; non-public or other-namespace implementation types are
    /// walked past. Null when the chain has none.
    /// </summary>
    public static string? CanonicalType(Type? runtimeType, string configurationsNamespace)
        => CanonicalType(runtimeType, new[] { configurationsNamespace });

    /// <summary>
    /// <see cref="CanonicalType(Type, string)"/> for a callback that may present types of several namespaces
    /// (station upload: its own configurations and the online legitimation ones): the first public type of the
    /// runtime type chain whose namespace is exactly one of <paramref name="configurationsNamespaces"/>.
    /// </summary>
    public static string? CanonicalType(Type? runtimeType, IReadOnlyCollection<string> configurationsNamespaces)
    {
        for (var type = runtimeType; type is not null; type = type.BaseType)
            if ((type.IsPublic || type.IsNestedPublic) && type.Namespace is { } ns && configurationsNamespaces.Contains(ns, StringComparer.Ordinal))
                return type.FullName;
        return null;
    }

    /// <summary>The decision key of a canonical type: its short name.</summary>
    public static string? DecisionKey(string? canonicalType)
        => canonicalType is null ? null : canonicalType.Substring(canonicalType.LastIndexOf('.') + 1);

    /// <summary>Validates caller entries; null when valid. Keys and selections are identifiers, keys are unique across both lists.</summary>
    public static string? ValidateEntries(IReadOnlyList<PlcCallbackDecisionInfo>? decisions, IReadOnlyList<PlcCallbackPasswordInfo>? passwords)
    {
        decisions ??= Array.Empty<PlcCallbackDecisionInfo>();
        passwords ??= Array.Empty<PlcCallbackPasswordInfo>();
        if (decisions.Count + passwords.Count > MaxEntries) return $"At most {MaxEntries} decisions and passwords are accepted.";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (decision is null || !IsIdentifier(decision.Type)) return "Every decision key must be a configuration type short name (an identifier, for example UploadMissingProducts).";
            if (PlcTlsTrustPolicy.IsTlsVerificationKey(decision.Type)) return PlcTlsTrustPolicy.DecisionKeyRefusal("decisions");
            if (!IsIdentifier(decision.Selection)) return $"The decision for '{decision.Type}' must be an enum member name (an identifier).";
            if (!keys.Add(decision.Type)) return $"'{decision.Type}' is answered more than once.";
        }

        foreach (var password in passwords)
        {
            if (password is null || !IsIdentifier(password.Type)) return "Every password key must be a configuration type short name (an identifier, for example ModuleReadAccessPassword).";
            if (PlcTlsTrustPolicy.IsTlsVerificationKey(password.Type)) return PlcTlsTrustPolicy.DecisionKeyRefusal("passwords");
            var environment = password.Variable is not null && password.Variable.Length <= MaxVariableNameLength && Identifier.IsMatch(password.Variable);
            if (!environment && Shared.PasswordSources.KindOf(password?.Variable) != Shared.PasswordSourceKind.Credential)
                return $"The password for '{password!.Type}' must name an environment variable (letters, digits and underscores, at most {MaxVariableNameLength} characters) "
                    + $"or a Windows Credential Manager generic credential {{ \"credential\": \"{Shared.PasswordSources.CredentialTargetPrefix}...\" }}; never pass the password itself.";
            if (!keys.Add(password.Type)) return $"'{password.Type}' is answered more than once.";
        }

        return null;
    }
}

/// <summary>Thrown from a callback to abort the Siemens operation; the recorder already holds the evidence.</summary>
public sealed class PlcCallbackAbortedException : Exception
{
    public PlcCallbackAbortedException(string message) : base(message)
    {
    }
}

/// <summary>A configuration's kind and, for a selection, its live enum member names.</summary>
public sealed class PlcCallbackShape
{
    public PlcCallbackShape(string kind, IReadOnlyList<string>? availableSelections = null)
    {
        Kind = kind;
        AvailableSelections = availableSelections ?? Array.Empty<string>();
    }

    public static PlcCallbackShape Other { get; } = new(PlcCallbackPolicy.Kinds.Other);

    public string Kind { get; }

    public IReadOnlyList<string> AvailableSelections { get; }
}

/// <summary>How the recorder reads and answers a live configuration without referencing Siemens types.</summary>
public interface IPlcCallbackAccess
{
    string TypeName(object configuration);

    string? CanonicalType(object configuration);

    string? ReadMessage(object configuration);

    PlcCallbackShape Describe(object configuration);

    /// <summary>Sets <c>CurrentSelection</c>; returns false when <paramref name="selection"/> is not an exact member of the live enum. The setter's exception propagates.</summary>
    bool ApplySelection(object configuration, string selection);

    /// <summary>Calls <c>SetPassword(SecureString)</c> with the value of <paramref name="variable"/>; returns false when it is unset or empty. The call's exception propagates.</summary>
    bool ApplyPassword(object configuration, string variable);
}

/// <summary>
/// The production <see cref="IPlcCallbackAccess"/> for the worker and the FakeWorker. It answers by
/// reflection on the runtime object, never by a compiled cast, through the documented member names:
/// <c>CurrentSelection</c> (exactly one public readable and writable enum property; the value is parsed by
/// exact member name of that property's own enum type) and <c>SetPassword(SecureString)</c> (exactly one
/// public instance method). A password is copied from the process environment into a read-only
/// <see cref="SecureString"/> that is disposed after the call and is never recorded.
/// </summary>
public sealed class PlcCallbackReflectionAccess : IPlcCallbackAccess
{
    public const string SelectionPropertyName = "CurrentSelection";
    public const string PasswordMethodName = "SetPassword";

    private readonly IReadOnlyCollection<string> _namespaces;
    private readonly Func<object, string?> _readMessage;
    private readonly Func<string, string?> _readEnvironment;
    private readonly Shared.ICredentialReader? _credentials;

    public PlcCallbackReflectionAccess(string configurationsNamespace, Func<object, string?> readMessage, Func<string, string?> readEnvironment,
        Shared.ICredentialReader? credentials = null)
        : this(new[] { configurationsNamespace ?? throw new ArgumentNullException(nameof(configurationsNamespace)) }, readMessage, readEnvironment, credentials)
    {
    }

    /// <summary>For a recorder that answers configurations of several namespaces (station upload and its online legitimation).</summary>
    public PlcCallbackReflectionAccess(IReadOnlyCollection<string> configurationsNamespaces, Func<object, string?> readMessage, Func<string, string?> readEnvironment,
        Shared.ICredentialReader? credentials = null)
    {
        _credentials = credentials;
        if (configurationsNamespaces is null || configurationsNamespaces.Count == 0 || configurationsNamespaces.Any(string.IsNullOrEmpty))
            throw new ArgumentException("At least one configurations namespace is required.", nameof(configurationsNamespaces));
        _namespaces = configurationsNamespaces.ToArray();
        _readMessage = readMessage ?? throw new ArgumentNullException(nameof(readMessage));
        _readEnvironment = readEnvironment ?? throw new ArgumentNullException(nameof(readEnvironment));
    }

    public string TypeName(object configuration) => configuration.GetType().FullName ?? configuration.GetType().Name;

    public string? CanonicalType(object configuration) => PlcCallbackPolicy.CanonicalType(configuration.GetType(), _namespaces);

    public string? ReadMessage(object configuration) => _readMessage(configuration);

    public PlcCallbackShape Describe(object configuration)
    {
        var type = configuration.GetType();
        if (PasswordSetter(type) is not null) return new PlcCallbackShape(PlcCallbackPolicy.Kinds.Password);
        if (SelectionProperty(type) is { } selection)
            return new PlcCallbackShape(PlcCallbackPolicy.Kinds.Selection,
                Enum.GetNames(selection.PropertyType).Take(PlcCallbackPolicy.MaxAvailableSelections).ToArray());
        return PlcCallbackShape.Other;
    }

    public bool ApplySelection(object configuration, string selection)
    {
        var property = SelectionProperty(configuration.GetType()) ?? throw new InvalidOperationException("The configuration has no selection property.");
        if (!PlcCallbackPolicy.IsIdentifier(selection) || !Enum.IsDefined(property.PropertyType, selection)) return false;
        var value = Enum.Parse(property.PropertyType, selection, ignoreCase: false);
        Invoke(() => property.SetValue(configuration, value, null));
        return true;
    }

    public bool ApplyPassword(object configuration, string variable)
    {
        var setter = PasswordSetter(configuration.GetType()) ?? throw new InvalidOperationException("The configuration has no password setter.");
        using var secure = Shared.PasswordSources.Resolve(variable, _readEnvironment, _credentials, out _);
        if (secure is null) return false;
        Invoke(() => setter.Invoke(configuration, new object[] { secure }));
        return true;
    }

    private static PropertyInfo? SelectionProperty(Type type)
    {
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name == SelectionPropertyName && p.PropertyType.IsEnum && p.CanRead && p.CanWrite
                && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static MethodInfo? PasswordSetter(Type type)
    {
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == PasswordMethodName && m.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(SecureString))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static void Invoke(Action call)
    {
        try
        {
            call();
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}

/// <summary>
/// The fail-closed decision loop. <see cref="Handle"/> records every presented configuration in
/// <see cref="Seen"/>, answers it from the caller's entries, or records the first <see cref="Abort"/>
/// and throws <see cref="PlcCallbackAbortedException"/>; after an abort every later configuration is
/// recorded as aborted and throws again (whether such a throw can terminate TIA Portal is not known). The TLS
/// verification is the exception (K27): a refused TLS question is answered <c>NonTrusted</c> and recorded as the abort
/// WITHOUT a throw whenever NonTrusted can be set, also after an earlier abort. An exception from an answer is recorded as
/// <c>answer_rejected</c>; a fatal one (<paramref name="isFatal"/>, for example a lost Portal) is rethrown
/// unchanged after recording so the shared classification still applies. A TLS verification configuration
/// (<see cref="PlcTlsTrustPolicy.CanonicalType"/>) is answered only from the caller's trusted fingerprints:
/// <c>Trusted</c> when every presented certificate is approved, otherwise <c>NonTrusted</c> (where it can be set)
/// and an abort carrying the certificate evidence (<see cref="PlcCallbackConfigurationInfo.Tls"/>). Thread-safe like
/// <c>PlcDownloadCallbackRecorder</c>: Siemens may raise callbacks on another thread, so
/// <see cref="Handle"/> runs under one lock and every reader returns a snapshot taken under it.
/// </summary>
public sealed class PlcCallbackRecorder
{
    private readonly object _sync = new();
    private readonly List<PlcCallbackConfigurationInfo> _seen = new();
    private PlcCallbackAbortInfo? _abort;
    private Exception? _answerFailure;
    private readonly Dictionary<string, string> _selections;
    private readonly Dictionary<string, string> _passwords;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly IPlcCallbackAccess _access;
    private readonly Func<Exception, bool> _isFatal;
    private readonly IReadOnlyList<string> _trusted;
    private readonly bool _plcSimInterface;
    private readonly Func<object, IReadOnlyCollection<string>, PlcTlsVerificationInfo> _readTls;

    /// <param name="trustedCertificates">
    /// Normalized SHA-256 fingerprints (<see cref="PlcTlsTrustPolicy.NormalizeFingerprint"/>) the caller approved; the only
    /// way a TLS verification configuration can be answered <c>Trusted</c>. Null or empty trusts nothing.
    /// </param>
    /// <param name="readTls">Reads the TLS evidence of a live configuration; defaults to <see cref="PlcTlsTrustPolicy.Read"/>.</param>
    public PlcCallbackRecorder(IReadOnlyList<PlcCallbackDecisionInfo>? decisions, IReadOnlyList<PlcCallbackPasswordInfo>? passwords,
        IPlcCallbackAccess access, Func<Exception, bool>? isFatal = null, IReadOnlyList<string>? trustedCertificates = null,
        Func<object, IReadOnlyCollection<string>, PlcTlsVerificationInfo>? readTls = null, bool plcSimInterface = false)
    {
        _plcSimInterface = plcSimInterface;
        _selections = (decisions ?? Array.Empty<PlcCallbackDecisionInfo>()).ToDictionary(d => d.Type, d => d.Selection, StringComparer.Ordinal);
        _passwords = (passwords ?? Array.Empty<PlcCallbackPasswordInfo>()).ToDictionary(p => p.Type, p => p.Variable, StringComparer.Ordinal);
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _isFatal = isFatal ?? (_ => false);
        // Only normalized fingerprints can ever match (the evidence is normalized); anything else is ignored.
        _trusted = (trustedCertificates ?? Array.Empty<string>()).Where(PlcTlsTrustPolicy.IsNormalizedFingerprint).Distinct(StringComparer.Ordinal).ToArray();
        _readTls = readTls ?? ((configuration, trusted) => PlcTlsTrustPolicy.Read(configuration, trusted, _isFatal));
    }

    /// <summary>A snapshot of every presented configuration, in order.</summary>
    public List<PlcCallbackConfigurationInfo> Seen
    {
        get
        {
            lock (_sync) return _seen.ToList();
        }
    }

    public PlcCallbackAbortInfo? Abort
    {
        get
        {
            lock (_sync) return _abort;
        }
    }

    /// <summary>The exception an answer threw, when the abort reason is <c>answer_rejected</c>.</summary>
    public Exception? AnswerFailure
    {
        get
        {
            lock (_sync) return _answerFailure;
        }
    }

    /// <summary>Keys the caller supplied that no presented configuration used.</summary>
    public IReadOnlyList<string> UnusedKeys()
    {
        lock (_sync)
            return _selections.Keys.Concat(_passwords.Keys).Where(key => !_used.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToArray();
    }

    public void Handle(object configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        lock (_sync) HandleLocked(configuration);
    }

    private void HandleLocked(object configuration)
    {
        if (_abort is not null)
        {
            var late = Entry(configuration, PlcCallbackPolicy.Decisions.Aborted);
            if (_seen.Count <= PlcCallbackPolicy.MaxConfigurations) _seen.Add(late);
            // A later TLS question is never trusted; it is refused with NonTrusted without a throw where possible (K27).
            if (PlcTlsTrustPolicy.IsTlsVerification(late.CanonicalType) && RefuseTlsAfterAbort(configuration, late)) return;
            throw new PlcCallbackAbortedException(AbortMessage(_abort));
        }

        if (_seen.Count >= PlcCallbackPolicy.MaxConfigurations)
        {
            var extra = Entry(configuration, PlcCallbackPolicy.Decisions.Aborted);
            _seen.Add(extra);
            Fail(extra, PlcCallbackPolicy.AbortReasons.ConfigurationLimitExceeded, $"more than {PlcCallbackPolicy.MaxConfigurations} configurations were presented");
        }

        var entry = Entry(configuration, PlcCallbackPolicy.Decisions.Aborted);
        _seen.Add(entry);
        if (entry.DecisionKey is not { } key)
            Fail(entry, PlcCallbackPolicy.AbortReasons.UnsupportedConfiguration, "its type is not a public type of the expected configurations namespace, so it has no decision key");
        else if (PlcTlsTrustPolicy.IsTlsVerification(entry.CanonicalType))
            AnswerTls(configuration, entry);
        else if (entry.Kind == PlcCallbackPolicy.Kinds.Selection)
            AnswerSelection(configuration, entry, key);
        else if (entry.Kind == PlcCallbackPolicy.Kinds.Password)
            AnswerPassword(configuration, entry, key);
        else
            Fail(entry, PlcCallbackPolicy.AbortReasons.UnsupportedConfiguration, "it is neither a selection nor a password configuration, so it cannot be answered");
    }

    private void AnswerSelection(object configuration, PlcCallbackConfigurationInfo entry, string key)
    {
        if (_passwords.ContainsKey(key))
            Fail(entry, PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, "a password was supplied, but it is a selection configuration");
        if (!_selections.TryGetValue(key, out var selection))
            Fail(entry, PlcCallbackPolicy.AbortReasons.DecisionMissing, $"no decision was supplied; pass decisions {{ \"{key}\": \"<one of the available selections>\" }}");
        _used.Add(key);
        bool applied;
        try
        {
            applied = _access.ApplySelection(configuration, selection!);
        }
        catch (Exception ex)
        {
            Rejected(entry, ex);
            throw;
        }

        if (!applied) Fail(entry, PlcCallbackPolicy.AbortReasons.SelectionUnavailable, $"'{selection}' is not a member of its selection enum");
        entry.Decision = PlcCallbackPolicy.Decisions.Selected;
        entry.Selection = selection;
    }

    private void AnswerPassword(object configuration, PlcCallbackConfigurationInfo entry, string key)
    {
        if (_selections.ContainsKey(key))
            Fail(entry, PlcCallbackPolicy.AbortReasons.DecisionNotApplicable, "a selection was supplied, but it is a password configuration");
        if (!_passwords.TryGetValue(key, out var variable))
            Fail(entry, PlcCallbackPolicy.AbortReasons.DecisionMissing, $"no password was supplied; pass passwords {{ \"{key}\": \"<NAME of an environment variable of the server process>\" }}");
        _used.Add(key);
        bool applied;
        try
        {
            applied = _access.ApplyPassword(configuration, variable!);
        }
        catch (Exception ex)
        {
            Rejected(entry, ex);
            throw;
        }

        // Never repeat the given name: an unset "variable" may be a password pasted by mistake.
        if (!applied) Fail(entry, PlcCallbackPolicy.AbortReasons.PasswordUnavailable, "the environment variable named for this configuration is unset or empty");
        entry.Decision = PlcCallbackPolicy.Decisions.PasswordSet;
        entry.PasswordVariable = variable;
    }

    /// <summary>
    /// The TLS trust question: never answered by a decision. The evidence is read first (for the human); then
    /// <c>Trusted</c> is set only when <see cref="PlcTlsTrustPolicy.Trusts"/> allows it. Otherwise <c>NonTrusted</c> is set
    /// where possible and, when it was set, the abort is recorded WITHOUT a throw (entry answered <c>NonTrusted</c>): TIA Portal
    /// then fails the connection itself (K27: throwing from this callback was followed by TIA Portal terminating, reproduced live
    /// 4x on 2026-11-02, while "Verbindung abbrechen" in the TIA UI left it running). Only when NonTrusted cannot be set does it
    /// still throw, a known risk but the only remaining refusal.
    /// </summary>
    private void AnswerTls(object configuration, PlcCallbackConfigurationInfo entry)
    {
        PlcTlsVerificationInfo tls;
        try
        {
            tls = _readTls(configuration, _trusted);
        }
        catch (Exception ex)
        {
            // Only a fatal exception (a lost Portal) escapes the reader: record it and keep its classification.
            Rejected(entry, ex);
            throw;
        }

        entry.Tls = tls;
        if (entry.Kind != PlcCallbackPolicy.Kinds.Selection)
        {
            if (!tls.UnreadableMembers.Contains(PlcTlsTrustPolicy.Members.CurrentSelection)) tls.UnreadableMembers.Add(PlcTlsTrustPolicy.Members.CurrentSelection);
            Fail(entry, PlcCallbackPolicy.AbortReasons.DecisionNotApplicable,
                "it has no single public readable and writable enum CurrentSelection property, so neither Trusted nor NonTrusted can be set");
        }

        if (!PlcTlsTrustPolicy.Trusts(tls) && entry.Kind == PlcCallbackPolicy.Kinds.Selection
            && PlcTlsTrustPolicy.AllowsNonVerifiedOnPlcSim(tls, _plcSimInterface) && TryApply(configuration, PlcTlsTrustPolicy.NonVerified))
        {
            // K52: PLCSIM without any certificate; the owner's rule answers "continue without a check".
            tls.SelectionSet = PlcTlsTrustPolicy.NonVerified;
            tls.Source = PlcTlsTrustPolicy.PlcSimNoCertificateSource;
            Answered(entry, PlcTlsTrustPolicy.NonVerified);
            return;
        }

        if (!PlcTlsTrustPolicy.Trusts(tls))
        {
            // Nothing is written to a CurrentSelection that is missing or not of the documented type.
            if (PlcTlsTrustPolicy.CanSetSelection(tls)) RefuseTls(configuration, entry, tls);
            Refuse(entry, tls, tls.UnreadableMembers.Count > 0 ? PlcCallbackPolicy.AbortReasons.DecisionNotApplicable : PlcCallbackPolicy.AbortReasons.CertificateNotTrusted,
                PlcTlsTrustPolicy.RefusalDetail(tls, _trusted.Count));
            return;
        }

        bool applied;
        try
        {
            applied = _access.ApplySelection(configuration, PlcTlsTrustPolicy.Trusted);
        }
        catch (Exception ex)
        {
            if (!_isFatal(ex)) RefuseTls(configuration, entry, tls);
            Rejected(entry, ex);
            if (!_isFatal(ex) && tls.SelectionSet == PlcTlsTrustPolicy.NonTrusted)
            {
                Answered(entry, PlcTlsTrustPolicy.NonTrusted);
                return;
            }

            throw;
        }

        if (!applied)
        {
            RefuseTls(configuration, entry, tls);
            Refuse(entry, tls, PlcCallbackPolicy.AbortReasons.SelectionUnavailable, $"'{PlcTlsTrustPolicy.Trusted}' is not a member of its selection enum");
            return;
        }

        tls.SelectionSet = PlcTlsTrustPolicy.Trusted;
        tls.Source = PlcTlsTrustPolicy.CallerSource;
        Answered(entry, PlcTlsTrustPolicy.Trusted);
    }

    /// <summary>Sets a selection; false (nothing claimed) when it is not a member or a recoverable exception is thrown.</summary>
    private bool TryApply(object configuration, string selection)
    {
        try
        {
            return _access.ApplySelection(configuration, selection);
        }
        catch (Exception ex) when (!_isFatal(ex))
        {
            return false;
        }
    }

    /// <summary>Records the abort; returns normally when NonTrusted was set (the entry is answered NonTrusted), otherwise throws.</summary>
    private void Refuse(PlcCallbackConfigurationInfo entry, PlcTlsVerificationInfo tls, string reason, string detail)
    {
        if (tls.SelectionSet != PlcTlsTrustPolicy.NonTrusted) Fail(entry, reason, detail);
        _abort = AbortOf(entry, reason, detail);
        Answered(entry, PlcTlsTrustPolicy.NonTrusted);
    }

    private static void Answered(PlcCallbackConfigurationInfo entry, string selection)
    {
        entry.Decision = PlcCallbackPolicy.Decisions.Selected;
        entry.Selection = selection;
    }

    /// <summary>A TLS question after an abort: answered NonTrusted without a throw when it can be set; false otherwise (the caller throws).</summary>
    private bool RefuseTlsAfterAbort(object configuration, PlcCallbackConfigurationInfo entry)
    {
        PlcTlsVerificationInfo tls;
        try
        {
            tls = _readTls(configuration, _trusted);
        }
        catch (Exception ex) when (!_isFatal(ex))
        {
            return false;
        }

        entry.Tls = tls;
        if (entry.Kind != PlcCallbackPolicy.Kinds.Selection || !PlcTlsTrustPolicy.CanSetSelection(tls)) return false;
        try
        {
            if (_access.ApplySelection(configuration, PlcTlsTrustPolicy.NonTrusted)) tls.SelectionSet = PlcTlsTrustPolicy.NonTrusted;
        }
        catch (Exception ex) when (!_isFatal(ex))
        {
            return false;
        }

        if (tls.SelectionSet != PlcTlsTrustPolicy.NonTrusted) return false;
        Answered(entry, PlcTlsTrustPolicy.NonTrusted);
        return true;
    }

    /// <summary>Sets <c>NonTrusted</c> where possible; a fatal exception of that attempt is recorded and rethrown unchanged.</summary>
    private void RefuseTls(object configuration, PlcCallbackConfigurationInfo entry, PlcTlsVerificationInfo tls)
    {
        try
        {
            if (_access.ApplySelection(configuration, PlcTlsTrustPolicy.NonTrusted)) tls.SelectionSet = PlcTlsTrustPolicy.NonTrusted;
        }
        catch (Exception ex) when (_isFatal(ex))
        {
            Rejected(entry, ex);
            throw;
        }
        catch (Exception)
        {
            // Not set: the abort that follows refuses the connection by throwing.
        }
    }

    /// <summary>
    /// Records the answer's exception as the abort; the caller rethrows it unchanged, so a fatal one
    /// (a lost Portal) keeps the shared classification with this evidence attached.
    /// </summary>
    private void Rejected(PlcCallbackConfigurationInfo entry, Exception exception)
    {
        _answerFailure = exception;
        _abort = AbortOf(entry, PlcCallbackPolicy.AbortReasons.AnswerRejected,
            $"setting the answer threw {exception.GetType().Name}: {PlcOnlinePolicy.BoundText(exception.Message)}");
    }

    private void Fail(PlcCallbackConfigurationInfo entry, string reason, string detail)
    {
        _abort = AbortOf(entry, reason, detail);
        throw new PlcCallbackAbortedException(AbortMessage(_abort));
    }

    private PlcCallbackConfigurationInfo Entry(object configuration, string decision)
    {
        var canonical = Safe(() => _access.CanonicalType(configuration));
        var shape = canonical is null ? PlcCallbackShape.Other : Safe(() => _access.Describe(configuration)) ?? PlcCallbackShape.Other;
        return new PlcCallbackConfigurationInfo
        {
            Type = Safe(() => _access.TypeName(configuration)) ?? configuration.GetType().Name,
            CanonicalType = canonical,
            DecisionKey = PlcCallbackPolicy.DecisionKey(canonical),
            Kind = shape.Kind,
            Message = PlcOnlinePolicy.BoundText(Safe(() => _access.ReadMessage(configuration))),
            AvailableSelections = shape.AvailableSelections.ToList(),
            Decision = decision,
        };
    }

    private T? Safe<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (!_isFatal(ex))
        {
            return null;
        }
    }

    private static PlcCallbackAbortInfo AbortOf(PlcCallbackConfigurationInfo entry, string reason, string detail) => new()
    {
        Type = entry.Type,
        DecisionKey = entry.DecisionKey,
        Kind = entry.Kind,
        Message = entry.Message,
        AvailableSelections = entry.AvailableSelections.ToList(),
        Reason = reason,
        Detail = detail,
        Tls = entry.Tls,
    };

    public static string AbortMessage(PlcCallbackAbortInfo abort)
        => abort.Tls is { } tls
            ? $"Aborted: TIA Portal asked whether to trust the device's TLS certificate ('{abort.Type}') and it was not trusted ({abort.Reason}: {abort.Detail}). "
                + PlcTlsTrustPolicy.Explain(tls)
            : $"Aborted: TIA Portal presented '{abort.Type}' ({abort.Kind}) and it was not answered ({abort.Reason}: {abort.Detail})."
                + (abort.AvailableSelections.Count > 0 ? $" Available selections: {string.Join(", ", abort.AvailableSelections)}." : string.Empty);
}
