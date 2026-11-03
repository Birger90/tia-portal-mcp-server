using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// The fail-closed rules for the TLS trust question TIA Portal asks through
/// <c>ConnectionConfiguration.OnlineLegitimation</c> (a <c>TlsVerificationConfiguration</c>), shared by
/// host, worker and FakeWorker and unit-tested without Siemens types. There is no default trust: the
/// device's certificates are trusted only when at least one is presented and the SHA-256 fingerprint of
/// EVERY presented certificate (over its <c>RawData</c>) is in the caller's <c>trustedCertificates</c>;
/// then <c>CurrentSelection</c> is set to <c>Trusted</c> (source <c>caller</c>). Anything else sets
/// <c>NonTrusted</c> where possible and aborts with the evidence a human needs to decide. A plain
/// <c>decisions</c> entry can never answer it, and <c>EnableLegacyCommunication</c> is never touched.
/// Members are read by reflection on the runtime object (documented types: <c>Certificate</c> is an
/// <c>X509Certificate2</c>, <c>CurrentSelection</c> a <c>TlsVerificationConfigurationSelection</c>); a missing or
/// unreadable member, or a <c>CurrentSelection</c> of another type, fails closed and nothing is written to it.
/// </summary>
public static class PlcTlsTrustPolicy
{
    /// <summary>The decision key (type short name) of the TLS verification configuration.</summary>
    public const string ConfigurationKey = "TlsVerificationConfiguration";

    /// <summary>The canonical public V21 type (source: online-status.md, V21 type-name baseline).</summary>
    public const string CanonicalType = PlcCallbackPolicy.OnlineConfigurationsNamespace + "." + ConfigurationKey;

    /// <summary>
    /// The documented enum type of <c>CurrentSelection</c> (source: online-status.md, which sets
    /// <c>tls.CurrentSelection</c> to <c>TlsVerificationConfigurationSelection</c> members; type name in the V21 baseline).
    /// </summary>
    public const string SelectionType = PlcCallbackPolicy.OnlineConfigurationsNamespace + ".TlsVerificationConfigurationSelection";

    /// <summary><c>TlsVerificationConfigurationSelection</c> member names (source: online-status.md), set by name.</summary>
    public const string Trusted = "Trusted";

    public const string NonTrusted = "NonTrusted";

    /// <summary>The source recorded for a <c>Trusted</c> answer: the caller's <c>trustedCertificates</c>.</summary>
    public const string CallerSource = "caller";

    /// <summary>
    /// K52 (owner decision 2026-11-02): the selection that continues without a certificate check. Answered only by the
    /// PLCSIM rule (<see cref="AllowsNonVerifiedOnPlcSim"/>), never for a presented certificate or a real controller.
    /// </summary>
    public const string NonVerified = "NonVerified";

    /// <summary>The source recorded for a <c>NonVerified</c> answer of the PLCSIM rule.</summary>
    public const string PlcSimNoCertificateSource = "plcsimNoCertificate";

    /// <summary>The exact PC interface name of the PLCSIM simulation (as list_download_targets and get_online_state list it).</summary>
    public const string PlcSimInterfaceName = "PLCSIM";

    /// <summary>
    /// K52 (live V21 2026-11-02, owner decision): a station upload from PLCSIM asked the TLS question with "certificate not
    /// matching" and no certificate at all, so no fingerprint can be checked. Over the PC interface exactly PLCSIM, with every
    /// member readable and zero certificates presented, the question is answered NonVerified (continue without a check);
    /// a presented certificate or any other PC interface keeps the fingerprint rule.
    /// </summary>
    public static bool AllowsNonVerifiedOnPlcSim(PlcTlsVerificationInfo? tls, bool plcSimInterface)
        => plcSimInterface && tls is not null && tls.UnreadableMembers.Count == 0 && tls.CertificatesComplete
            && tls.CertificatesPresented == 0 && tls.Certificates.Count == 0;

    /// <summary>True exactly for the PC interface name <see cref="PlcSimInterfaceName"/> (ordinal).</summary>
    public static bool IsPlcSimInterface(string? pcInterfaceName) => string.Equals(pcInterfaceName, PlcSimInterfaceName, StringComparison.Ordinal);

    /// <summary>The abort reason when the presented certificates are not all approved by the caller.</summary>
    public const string CertificateNotTrustedReason = "certificate_not_trusted";

    public const string ArgumentName = "trustedCertificates";

    /// <summary>At most this many fingerprints in <c>trustedCertificates</c>.</summary>
    public const int MaxTrustedCertificates = 16;

    /// <summary>At most this many presented certificates are read and listed; more are never trusted.</summary>
    public const int MaxCertificates = 16;

    /// <summary>At most this many unreadable member names are recorded.</summary>
    public const int MaxUnreadableMembers = 40;

    /// <summary>Length of a SHA-256 fingerprint in hexadecimal characters.</summary>
    public const int FingerprintLength = 64;

    /// <summary>The live members read by reflection (source: online-status.md "TLS legitimation and online authentication").</summary>
    public static class Members
    {
        public const string PlcName = "PlcName";
        public const string VerificationInfo = "VerificationInfo";
        public const string Certificates = "Certificates";
        public const string Certificate = "Certificate";
        public const string CurrentSelection = "CurrentSelection";
    }

    public static bool IsTlsVerification(string? canonicalType) => string.Equals(canonicalType, CanonicalType, StringComparison.Ordinal);

    public static bool IsTlsVerificationKey(string? decisionKey) => string.Equals(decisionKey, ConfigurationKey, StringComparison.Ordinal);

    /// <summary>
    /// The normalized form of a caller fingerprint: 64 hexadecimal characters, upper case, with every
    /// colon and space removed; null when <paramref name="value"/> is anything else.
    /// </summary>
    public static string? NormalizeFingerprint(string? value)
    {
        if (value is null || value.Length > 4 * FingerprintLength) return null;
        var normalized = new StringBuilder(FingerprintLength);
        foreach (var character in value)
        {
            if (character is ':' or ' ') continue;
            if (!IsHex(character) || normalized.Length == FingerprintLength) return null;
            normalized.Append(char.ToUpperInvariant(character));
        }

        return normalized.Length == FingerprintLength ? normalized.ToString() : null;
    }

    /// <summary>True when <paramref name="value"/> already is a normalized fingerprint.</summary>
    public static bool IsNormalizedFingerprint(string? value)
        => value is { Length: FingerprintLength } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    /// <summary>
    /// Host validation of the caller's <c>trustedCertificates</c>: at most <see cref="MaxTrustedCertificates"/>
    /// entries, each a SHA-256 fingerprint (see <see cref="NormalizeFingerprint"/>), none repeated after
    /// normalization. Returns the first violation (null when valid) and the normalized list in caller order.
    /// </summary>
    public static string? NormalizeList(IReadOnlyList<string?>? values, out List<string> normalized)
    {
        normalized = new List<string>();
        if (values is null) return null;
        if (values.Count > MaxTrustedCertificates)
            return $"{ArgumentName} accepts at most {MaxTrustedCertificates} SHA-256 fingerprints.";
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            if (NormalizeFingerprint(values[index]) is not { } fingerprint)
                return $"{ArgumentName}[{index}] is not a SHA-256 fingerprint: give 64 hexadecimal characters (case-insensitive; colons and "
                    + "spaces are ignored), exactly as tls.certificates[].sha256 of an aborted call reports them.";
            if (seen.TryGetValue(fingerprint, out var first))
                return $"{ArgumentName}[{index}] repeats {ArgumentName}[{first}] (the same SHA-256 fingerprint).";
            seen.Add(fingerprint, index);
            normalized.Add(fingerprint);
        }

        return null;
    }

    /// <summary>Worker defense in depth: the host sends only normalized, unique fingerprints, at most <see cref="MaxTrustedCertificates"/>.</summary>
    public static string? ValidateNormalized(IReadOnlyList<string>? values)
    {
        if (values is null) return $"{ArgumentName} must be a list when supplied.";
        if (values.Count > MaxTrustedCertificates) return $"{ArgumentName} accepts at most {MaxTrustedCertificates} SHA-256 fingerprints.";
        if (values.Any(value => !IsNormalizedFingerprint(value)))
            return $"Every {ArgumentName} entry must be a normalized SHA-256 fingerprint (64 upper-case hexadecimal characters).";
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) return $"{ArgumentName} lists a fingerprint more than once.";
        return null;
    }

    /// <summary>The refusal of a <c>decisions</c> or <c>passwords</c> entry keyed by the TLS verification configuration.</summary>
    public static string DecisionKeyRefusal(string map)
        => $"{map}.{ConfigurationKey} is not accepted: a device TLS certificate is trusted only through {ArgumentName} (the SHA-256 "
            + "fingerprints of every certificate the device presents, after the user checked them), never through decisions or passwords.";

    /// <summary>The SHA-256 fingerprint of a certificate's DER bytes, as 64 upper-case hexadecimal characters.</summary>
    public static string Sha256Fingerprint(byte[] rawData)
    {
        if (rawData is null) throw new ArgumentNullException(nameof(rawData));
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(rawData));
    }

    /// <summary>
    /// True when the evidence allows <c>Trusted</c>: every member could be read, at least one certificate was
    /// presented, every presented certificate is listed, and every one is approved.
    /// </summary>
    public static bool Trusts(PlcTlsVerificationInfo? tls)
        => tls is not null && tls.UnreadableMembers.Count == 0 && tls.CertificatesComplete
            && tls.Certificates.Count > 0 && tls.Certificates.Count == tls.CertificatesPresented
            && tls.Certificates.All(certificate => certificate.Approved);

    /// <summary>
    /// Reads the human-decision evidence of a live TLS verification configuration by reflection on the runtime
    /// object, never by a compiled cast: <c>PlcName</c> and <c>VerificationInfo</c> as text, and for each item of
    /// <c>Certificates</c> its <c>Certificate</c> (<see cref="X509Certificate2"/>): SHA-256 over <c>RawData</c>,
    /// SHA-1 thumbprint, subject, issuer, validity, and whether its fingerprint is in <paramref name="trusted"/>.
    /// A member that is missing, ambiguous or throws is listed in <see cref="PlcTlsVerificationInfo.UnreadableMembers"/>
    /// (fail closed); so is <c>CurrentSelection</c> unless it is exactly one public readable and writable property of the
    /// documented enum type <see cref="SelectionType"/> (it is not read, only its type is checked; the recorders then write
    /// nothing to it). An exception <paramref name="isFatal"/> accepts (a lost Portal) is rethrown unchanged.
    /// </summary>
    public static PlcTlsVerificationInfo Read(object configuration, IReadOnlyCollection<string> trusted, Func<Exception, bool>? isFatal = null)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        var approved = new HashSet<string>(trusted ?? Array.Empty<string>(), StringComparer.Ordinal);
        var fatal = isFatal ?? (_ => false);
        var tls = new PlcTlsVerificationInfo();
        var type = configuration.GetType();
        tls.PlcName = ReadText(configuration, type, Members.PlcName, tls, fatal);
        tls.VerificationInfo = ReadText(configuration, type, Members.VerificationInfo, tls, fatal);
        ReadCertificates(configuration, type, approved, tls, fatal);
        if (!HasDocumentedSelection(type)) Unreadable(tls, Members.CurrentSelection);
        return tls;
    }

    /// <summary>
    /// True when <c>CurrentSelection</c> may be written: exactly one public instance property of that name, without index
    /// parameters, with a public getter and setter, whose type is the enum <see cref="SelectionType"/>.
    /// </summary>
    public static bool HasDocumentedSelection(Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => string.Equals(property.Name, Members.CurrentSelection, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 && matches[0].PropertyType.IsEnum
            && string.Equals(matches[0].PropertyType.FullName, SelectionType, StringComparison.Ordinal)
            && matches[0].GetIndexParameters().Length == 0
            && matches[0].GetGetMethod(nonPublic: false) is not null && matches[0].GetSetMethod(nonPublic: false) is not null;
    }

    /// <summary>
    /// True when the recorders may write <c>NonTrusted</c> to refuse: <c>CurrentSelection</c> was found with its documented
    /// type (it is not listed in <see cref="PlcTlsVerificationInfo.UnreadableMembers"/>). Otherwise nothing is written and
    /// the callback refuses by throwing.
    /// </summary>
    public static bool CanSetSelection(PlcTlsVerificationInfo tls)
        => tls is not null && !tls.UnreadableMembers.Contains(Members.CurrentSelection);

    /// <summary>Why the evidence does not allow <c>Trusted</c> (the abort detail); never a secret.</summary>
    public static string RefusalDetail(PlcTlsVerificationInfo tls, int trustedCount)
    {
        if (tls.UnreadableMembers.Count > 0)
            return $"the TLS verification member(s) {string.Join(", ", tls.UnreadableMembers.Take(8))} could not be read or do not have their documented "
                + "type, and trust is never given without them";
        if (tls.CertificatesPresented == 0) return "the device presented no certificate";
        if (!tls.CertificatesComplete || tls.Certificates.Count != tls.CertificatesPresented)
            return $"more than {MaxCertificates} certificates were presented, so not every one could be checked";
        if (trustedCount == 0) return $"no {ArgumentName} were given, so the device's certificate is not trusted";
        var unapproved = tls.Certificates.Count(certificate => !certificate.Approved);
        return $"{unapproved} of {tls.Certificates.Count} presented certificate(s) have a SHA-256 fingerprint that is not in {ArgumentName}";
    }

    /// <summary>
    /// The human-decision text of an abort: what the device presented and what the caller must do. It asks the
    /// caller to have the USER check the fingerprints; nothing is trusted by default and there is no way around it.
    /// </summary>
    public static string Explain(PlcTlsVerificationInfo tls)
    {
        var text = new StringBuilder();
        text.Append($"The device presented a TLS certificate check for PLC name {Quote(tls.PlcName)} (verification information {Quote(tls.VerificationInfo)}): ");
        if (tls.Certificates.Count == 0) text.Append("no certificate could be read. ");
        for (var index = 0; index < tls.Certificates.Count && index < 8; index++)
        {
            var certificate = tls.Certificates[index];
            text.Append($"certificate {index + 1}: SHA-256 {certificate.Sha256}{(certificate.Approved ? " (in trustedCertificates)" : " (not in trustedCertificates)")}, ")
                .Append($"subject {Quote(certificate.Subject)}, issuer {Quote(certificate.Issuer)}, valid from {certificate.NotBefore ?? "(unreadable)"} ")
                .Append($"to {certificate.NotAfter ?? "(unreadable)"}{(certificate.Sha1Thumbprint is null ? string.Empty : $", SHA-1 thumbprint {certificate.Sha1Thumbprint}")}. ");
        }

        if (tls.Certificates.Count > 8) text.Append($"{tls.Certificates.Count - 8} more certificate(s) are listed in tls.certificates. ");
        text.Append(tls.SelectionSet == NonTrusted
            ? "NonTrusted was set (the same answer as 'Verbindung abbrechen' in TIA Portal's trust prompt) without an exception in TIA Portal's callback, so TIA Portal refuses the connection itself. "
            : !CanSetSelection(tls)
                ? $"CurrentSelection is missing or not of the documented type {SelectionType}, so nothing was set; the callback threw to refuse the connection. "
                : "NonTrusted could not be set; the callback threw to refuse the connection. ");
        text.Append("Do not decide this yourself: show the user these fingerprints and ask them to compare them with the device's own certificate "
            + "(for example in TIA Portal's trust prompt with 'Zertifikate anzeigen' / 'Show certificates'; where a viewer shows only a SHA-1 thumbprint, "
            + "compare it with the SHA-1 thumbprint above). Only if the user confirms that every certificate belongs to this device, retry with "
            + $"{ArgumentName}: [{string.Join(", ", tls.Certificates.Take(MaxTrustedCertificates).Select(certificate => $"\"{certificate.Sha256}\""))}]; "
            + "every presented certificate must be listed. Nothing is ever trusted by default.");
        return text.ToString();
    }

    /// <summary>
    /// Host check of recorded TLS evidence against the planned <paramref name="trusted"/> fingerprints: shape and caps,
    /// every <c>approved</c> flag exactly matches membership, <c>source</c> is <c>caller</c> exactly when <c>Trusted</c>
    /// was set, and an applied answer (<paramref name="trustedApplied"/>) is <c>Trusted</c> and allowed by
    /// <see cref="Trusts"/>, while an unapplied one never claims <c>Trusted</c>.
    /// </summary>
    public static bool IsConsistent(PlcTlsVerificationInfo? tls, bool trustedApplied, IReadOnlyCollection<string> trusted, bool plcSimInterface = false)
    {
        if (tls is null) return !trustedApplied;
        if (tls.Certificates is null || tls.UnreadableMembers is null
            || tls.Certificates.Count > MaxCertificates || tls.CertificatesPresented < tls.Certificates.Count
            || tls.CertificatesComplete && tls.CertificatesPresented != tls.Certificates.Count
            || tls.UnreadableMembers.Count > MaxUnreadableMembers
            || tls.UnreadableMembers.Any(member => string.IsNullOrWhiteSpace(member) || member.Length > PlcOnlinePolicy.MaxNameLength)
            || tls.PlcName is { Length: > PlcOnlinePolicy.MaxTextChars } || tls.VerificationInfo is { Length: > PlcOnlinePolicy.MaxTextChars })
            return false;
        var approved = new HashSet<string>(trusted ?? Array.Empty<string>(), StringComparer.Ordinal);
        foreach (var certificate in tls.Certificates)
        {
            if (certificate is null || !IsNormalizedFingerprint(certificate.Sha256) || certificate.Approved != approved.Contains(certificate.Sha256)
                || certificate.Sha1Thumbprint is { } sha1 && (sha1.Length != 40 || !sha1.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F'))
                || certificate.Subject is { Length: > PlcOnlinePolicy.MaxTextChars } || certificate.Issuer is { Length: > PlcOnlinePolicy.MaxTextChars }
                || certificate.NotBefore is { Length: > 64 } || certificate.NotAfter is { Length: > 64 })
                return false;
        }

        if (tls.SelectionSet is not (null or Trusted or NonTrusted or NonVerified) || tls.Source is not (null or CallerSource or PlcSimNoCertificateSource)
            || (tls.Source == CallerSource) != (tls.SelectionSet == Trusted)
            || (tls.Source == PlcSimNoCertificateSource) != (tls.SelectionSet == NonVerified)
            || tls.SelectionSet == NonVerified && !AllowsNonVerifiedOnPlcSim(tls, plcSimInterface))
            return false;
        return trustedApplied ? tls.SelectionSet == Trusted && Trusts(tls) : tls.SelectionSet != Trusted;
    }

    private static string? ReadText(object target, Type type, string member, PlcTlsVerificationInfo tls, Func<Exception, bool> fatal)
    {
        if (!TryRead(target, type, member, fatal, out var value))
        {
            Unreadable(tls, member);
            return null;
        }

        try
        {
            return PlcOnlinePolicy.BoundText(value as string ?? value?.ToString());
        }
        catch (Exception ex) when (!fatal(ex))
        {
            Unreadable(tls, member);
            return null;
        }
    }

    private static void ReadCertificates(object configuration, Type type, HashSet<string> approved, PlcTlsVerificationInfo tls, Func<Exception, bool> fatal)
    {
        if (!TryRead(configuration, type, Members.Certificates, fatal, out var value) || value is not IEnumerable certificates || value is string)
        {
            Unreadable(tls, Members.Certificates);
            tls.CertificatesComplete = false;
            return;
        }

        var complete = true;
        var index = 0;
        try
        {
            foreach (var item in certificates)
            {
                if (index >= MaxCertificates)
                {
                    // At least one more than can be checked: never trusted, listed as incomplete.
                    tls.CertificatesPresented = index + 1;
                    tls.CertificatesComplete = false;
                    return;
                }

                var path = $"{Members.Certificates}[{index}].{Members.Certificate}";
                index++;
                if (item is null || !TryRead(item, item.GetType(), Members.Certificate, fatal, out var raw) || raw is not X509Certificate2 certificate)
                {
                    Unreadable(tls, path);
                    complete = false;
                    continue;
                }

                if (Describe(certificate, approved, fatal) is { } described) tls.Certificates.Add(described);
                else
                {
                    Unreadable(tls, path + ".RawData");
                    complete = false;
                }
            }
        }
        catch (Exception ex) when (!fatal(ex))
        {
            Unreadable(tls, Members.Certificates);
            complete = false;
        }

        tls.CertificatesPresented = index;
        tls.CertificatesComplete = complete;
    }

    /// <summary>The evidence of one certificate; null when its DER bytes cannot be read (it is then never trusted).</summary>
    private static PlcTlsCertificateInfo? Describe(X509Certificate2 certificate, HashSet<string> approved, Func<Exception, bool> fatal)
    {
        string sha256;
        try
        {
            var raw = certificate.RawData;
            if (raw is null || raw.Length == 0) return null;
            sha256 = Sha256Fingerprint(raw);
        }
        catch (Exception ex) when (!fatal(ex))
        {
            return null;
        }

        return new PlcTlsCertificateInfo
        {
            Sha256 = sha256,
            Sha1Thumbprint = Safe(() => certificate.Thumbprint is { Length: 40 } thumbprint ? thumbprint.ToUpperInvariant() : null, fatal),
            Subject = Safe(() => PlcOnlinePolicy.BoundText(certificate.Subject), fatal),
            Issuer = Safe(() => PlcOnlinePolicy.BoundText(certificate.Issuer), fatal),
            NotBefore = Safe(() => Utc(certificate.NotBefore), fatal),
            NotAfter = Safe(() => Utc(certificate.NotAfter), fatal),
            Approved = approved.Contains(sha256),
        };
    }

    private static string Utc(DateTime value)
        => (value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Local) : value)
            .ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? Safe(Func<string?> read, Func<Exception, bool> fatal)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (!fatal(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads exactly one public readable instance property without index parameters, by name. False when it is
    /// missing or ambiguous or its getter throws a non-fatal exception; a fatal one is rethrown unchanged.
    /// </summary>
    private static bool TryRead(object target, Type type, string name, Func<Exception, bool> fatal, out object? value)
    {
        value = null;
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => string.Equals(property.Name, name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1 || matches[0].GetIndexParameters().Length != 0 || matches[0].GetGetMethod(nonPublic: false) is null) return false;
        try
        {
            value = matches[0].GetValue(target, null);
            return true;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            if (fatal(ex.InnerException)) ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            return false;
        }
        catch (Exception ex) when (!fatal(ex))
        {
            return false;
        }
    }

    private static void Unreadable(PlcTlsVerificationInfo tls, string member)
    {
        if (tls.UnreadableMembers.Count < MaxUnreadableMembers && !tls.UnreadableMembers.Contains(member)) tls.UnreadableMembers.Add(member);
    }

    private static string Quote(string? value) => value is null ? "(none)" : $"'{value}'";

    private static bool IsHex(char character) => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static string Hex(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes) text.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        return text.ToString();
    }
}

/// <summary>
/// What a <c>TlsVerificationConfiguration</c> presented and what was set, so a human can decide whether to trust
/// the device: its <c>PlcName</c> and <c>VerificationInfo</c> as text (types undocumented, read by reflection), every
/// readable certificate, the members that could not be read, and the <c>CurrentSelection</c> member that was set.
/// Fingerprints are not secrets.
/// </summary>
public class PlcTlsVerificationInfo
{
    /// <summary><c>PlcName</c> as text; null when its value is null or the member could not be read (then listed in <see cref="UnreadableMembers"/>).</summary>
    public string? PlcName { get; set; }

    /// <summary><c>VerificationInfo</c> as text (its type is undocumented); null when null or unreadable.</summary>
    public string? VerificationInfo { get; set; }

    /// <summary>Every readable presented certificate, in presentation order (at most <see cref="PlcTlsTrustPolicy.MaxCertificates"/>).</summary>
    public List<PlcTlsCertificateInfo> Certificates { get; set; } = new List<PlcTlsCertificateInfo>();

    /// <summary>How many certificates the device presented (one more than the cap when it presented more).</summary>
    public int CertificatesPresented { get; set; }

    /// <summary>True when every presented certificate was read and is listed.</summary>
    public bool CertificatesComplete { get; set; }

    /// <summary>Members that were missing, ambiguous or threw (for example <c>Certificates[1].Certificate</c>); trust is never given then.</summary>
    public List<string> UnreadableMembers { get; set; } = new List<string>();

    /// <summary>The <c>TlsVerificationConfigurationSelection</c> member set: <c>Trusted</c>, <c>NonTrusted</c>, or null when none could be set.</summary>
    public string? SelectionSet { get; set; }

    /// <summary><c>caller</c> when <c>Trusted</c> was set from <c>trustedCertificates</c>; null otherwise.</summary>
    public string? Source { get; set; }
}

/// <summary>One presented certificate (<c>VerificationCertificate.Certificate</c>, an <c>X509Certificate2</c>).</summary>
public class PlcTlsCertificateInfo
{
    /// <summary>SHA-256 over the certificate's DER bytes (<c>RawData</c>), 64 upper-case hexadecimal characters: the value <c>trustedCertificates</c> takes.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>The SHA-1 thumbprint (<c>X509Certificate2.Thumbprint</c>), only for comparison with viewers that show nothing else; never accepted in <c>trustedCertificates</c>.</summary>
    public string? Sha1Thumbprint { get; set; }

    public string? Subject { get; set; }

    public string? Issuer { get; set; }

    /// <summary><c>NotBefore</c> in UTC (ISO 8601).</summary>
    public string? NotBefore { get; set; }

    /// <summary><c>NotAfter</c> in UTC (ISO 8601); an expired certificate is reported, not refused: the user decides.</summary>
    public string? NotAfter { get; set; }

    /// <summary>True when <see cref="Sha256"/> is in the caller's <c>trustedCertificates</c>.</summary>
    public bool Approved { get; set; }
}
