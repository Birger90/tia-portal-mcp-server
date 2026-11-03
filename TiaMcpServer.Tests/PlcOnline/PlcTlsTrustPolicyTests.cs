using Siemens.Engineering.Online.Configurations;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Tests.TestUtilities;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// The Siemens-free TLS trust rules: fingerprint normalization, the reflection reader of the presented
/// evidence, the "every presented certificate approved" decision and the host's evidence consistency check.
/// </summary>
public sealed class PlcTlsTrustPolicyTests
{
    private const string Abc = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    [Theory]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("BA:78:16:BF:8F:01:CF:EA:41:41:40:DE:5D:AE:22:23:B0:03:61:A3:96:17:7A:9C:B4:10:FF:61:F2:00:15:AD")]
    [InlineData("ba 78 16 bf 8f 01 cf ea 41 41 40 de 5d ae 22 23 b0 03 61 a3 96 17 7a 9c b4 10 ff 61 f2 00 15 ad")]
    [InlineData(" BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD ")]
    [InlineData("Ba7816bf8F01:cfea414140de5dae2223b00361a396177a9cb410ff61f20015AD")]
    public void Fingerprint_IsNormalizedToUpperCaseHexWithoutSeparators(string input)
        => Assert.Equal(Abc, PlcTlsTrustPolicy.NormalizeFingerprint(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015A")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD00")]
    [InlineData("BA-78-16-BF-8F-01-CF-EA-41-41-40-DE-5D-AE-22-23-B0-03-61-A3-96-17-7A-9C-B4-10-FF-61-F2-00-15-AD")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AG")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F200\t15AD")]
    [InlineData("A9993E364706816ABA3E25717850C26C9CD0D89D")]
    public void Fingerprint_RefusesAnythingElse(string? input)
        => Assert.Null(PlcTlsTrustPolicy.NormalizeFingerprint(input));

    [Fact]
    public void List_IsNormalizedInCallerOrder_AndRefusesRepeatsTooManyAndMalformedEntries()
    {
        var other = new string('0', 64);
        Assert.Null(PlcTlsTrustPolicy.NormalizeList(new[] { other, Abc.ToLowerInvariant() }, out var normalized));
        Assert.Equal(new[] { other, Abc }, normalized);
        Assert.Null(PlcTlsTrustPolicy.NormalizeList(null, out var none));
        Assert.Empty(none);

        Assert.Contains("trustedCertificates[1] repeats trustedCertificates[0]",
            PlcTlsTrustPolicy.NormalizeList(new[] { Abc, string.Join(":", Enumerable.Range(0, 32).Select(i => Abc.Substring(i * 2, 2))) }, out _));
        Assert.Contains("trustedCertificates[0] is not a SHA-256 fingerprint", PlcTlsTrustPolicy.NormalizeList(new string?[] { null }, out _));
        Assert.Contains("at most 16", PlcTlsTrustPolicy.NormalizeList(Enumerable.Range(0, 17).Select(i => i.ToString("X64")).ToArray(), out _));
    }

    [Fact]
    public void Worker_AcceptsOnlyNormalizedUniqueFingerprints()
    {
        Assert.Null(PlcTlsTrustPolicy.ValidateNormalized(new[] { Abc }));
        Assert.Null(PlcTlsTrustPolicy.ValidateNormalized(Array.Empty<string>()));
        Assert.NotNull(PlcTlsTrustPolicy.ValidateNormalized(new[] { Abc.ToLowerInvariant() }));
        Assert.NotNull(PlcTlsTrustPolicy.ValidateNormalized(new[] { Abc, Abc }));
        Assert.NotNull(PlcTlsTrustPolicy.ValidateNormalized(null));
    }

    [Fact]
    public void Sha256_IsComputedOverTheDerBytes()
    {
        Assert.Equal(Abc, PlcTlsTrustPolicy.Sha256Fingerprint("abc"u8.ToArray()));
        var certificate = TestCertificates.Create("PLC_1");
        Assert.Equal(TestCertificates.Sha256(certificate), PlcTlsTrustPolicy.Sha256Fingerprint(certificate.RawData));
    }

    [Fact]
    public void Read_ReportsEveryCertificateAsText_AndApprovesOnlyListedFingerprints()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var ca = TestCertificates.Create("Plant CA");
        var configuration = new TlsVerificationConfiguration("PLC_1", new Uri("https://192.168.0.1/"), leaf, ca);

        var tls = PlcTlsTrustPolicy.Read(configuration, new[] { TestCertificates.Sha256(leaf) });

        Assert.Equal("PLC_1", tls.PlcName);
        Assert.Equal("https://192.168.0.1/", tls.VerificationInfo);
        Assert.Equal(2, tls.CertificatesPresented);
        Assert.True(tls.CertificatesComplete);
        Assert.Empty(tls.UnreadableMembers);
        Assert.Collection(tls.Certificates,
            first =>
            {
                Assert.Equal(TestCertificates.Sha256(leaf), first.Sha256);
                Assert.Equal(leaf.Thumbprint, first.Sha1Thumbprint);
                Assert.Equal("CN=PLC_1", first.Subject);
                Assert.Equal("CN=PLC_1", first.Issuer);
                Assert.Equal("2026-01-01T00:00:00Z", first.NotBefore);
                Assert.Equal("2036-01-01T00:00:00Z", first.NotAfter);
                Assert.True(first.Approved);
            },
            second =>
            {
                Assert.Equal(TestCertificates.Sha256(ca), second.Sha256);
                Assert.False(second.Approved);
            });
        Assert.False(PlcTlsTrustPolicy.Trusts(tls));
        Assert.True(PlcTlsTrustPolicy.Trusts(PlcTlsTrustPolicy.Read(configuration, new[] { TestCertificates.Sha256(ca), TestCertificates.Sha256(leaf) })));
        Assert.Null(tls.SelectionSet);
        Assert.Null(tls.Source);
    }

    [Fact]
    public void Read_FailsClosedOnMissingThrowingOrForeignMembers()
    {
        var certificate = TestCertificates.Create("PLC_1");
        var shape = new PartialTlsShape { Certificates = new object?[] { new CertificateWithoutMember(), null } };

        var tls = PlcTlsTrustPolicy.Read(shape, new[] { TestCertificates.Sha256(certificate) });

        Assert.Equal(new[] { "PlcName", "VerificationInfo", "Certificates[0].Certificate", "Certificates[1].Certificate", "CurrentSelection" },
            tls.UnreadableMembers);
        Assert.Equal(2, tls.CertificatesPresented);
        Assert.False(tls.CertificatesComplete);
        Assert.Empty(tls.Certificates);
        Assert.False(PlcTlsTrustPolicy.Trusts(tls));
        Assert.Contains("could not be read", PlcTlsTrustPolicy.RefusalDetail(tls, 1));

        var noCertificates = PlcTlsTrustPolicy.Read(new object(), Array.Empty<string>());
        Assert.Contains("Certificates", noCertificates.UnreadableMembers);
        Assert.False(PlcTlsTrustPolicy.Trusts(noCertificates));
    }

    [Fact]
    public void Read_RequiresTheDocumentedSelectionType()
    {
        var leaf = TestCertificates.Create("PLC_1");

        var foreign = PlcTlsTrustPolicy.Read(new ForeignSelectionTlsShape(leaf), new[] { TestCertificates.Sha256(leaf) });

        Assert.Equal(new[] { "CurrentSelection" }, foreign.UnreadableMembers);
        Assert.Single(foreign.Certificates);
        Assert.False(PlcTlsTrustPolicy.Trusts(foreign));
        Assert.False(PlcTlsTrustPolicy.CanSetSelection(foreign));
        Assert.Contains("do not have their documented type", PlcTlsTrustPolicy.RefusalDetail(foreign, 1));
        Assert.Contains("not of the documented type", PlcTlsTrustPolicy.Explain(foreign));
        Assert.True(PlcTlsTrustPolicy.HasDocumentedSelection(typeof(TlsVerificationConfiguration)));
        Assert.False(PlcTlsTrustPolicy.HasDocumentedSelection(typeof(ForeignSelectionTlsShape)));
        Assert.Equal("Siemens.Engineering.Online.Configurations.TlsVerificationConfigurationSelection", PlcTlsTrustPolicy.SelectionType);
    }

    [Fact]
    public void Read_RethrowsAFatalException()
        => Assert.Throws<FatalTestException>(() => PlcTlsTrustPolicy.Read(new FatalTlsShape(), Array.Empty<string>(), ex => ex is FatalTestException));

    [Fact]
    public void ZeroOrTooManyCertificates_AreNeverTrusted()
    {
        var none = PlcTlsTrustPolicy.Read(new TlsVerificationConfiguration("PLC_1", "info"), Array.Empty<string>());
        Assert.True(none.CertificatesComplete);
        Assert.Equal(0, none.CertificatesPresented);
        Assert.False(PlcTlsTrustPolicy.Trusts(none));
        Assert.Equal("the device presented no certificate", PlcTlsTrustPolicy.RefusalDetail(none, 0));

        var certificates = Enumerable.Range(0, PlcTlsTrustPolicy.MaxCertificates + 1).Select(i => TestCertificates.Create("C" + i)).ToArray();
        var many = PlcTlsTrustPolicy.Read(new TlsVerificationConfiguration("PLC_1", "info", certificates), certificates.Select(TestCertificates.Sha256).ToArray());
        Assert.Equal(PlcTlsTrustPolicy.MaxCertificates, many.Certificates.Count);
        Assert.Equal(PlcTlsTrustPolicy.MaxCertificates + 1, many.CertificatesPresented);
        Assert.False(many.CertificatesComplete);
        Assert.False(PlcTlsTrustPolicy.Trusts(many));
    }

    [Fact]
    public void Explain_GivesTheHumanEverythingToDecide_AndTheRetry()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var tls = PlcTlsTrustPolicy.Read(new TlsVerificationConfiguration("PLC_1", "IP 192.168.0.1 not in certificate", leaf), Array.Empty<string>());
        tls.SelectionSet = PlcTlsTrustPolicy.NonTrusted;

        var text = PlcTlsTrustPolicy.Explain(tls);

        Assert.Contains(TestCertificates.Sha256(leaf), text);
        Assert.Contains("subject 'CN=PLC_1'", text);
        Assert.Contains("valid from 2026-01-01T00:00:00Z to 2036-01-01T00:00:00Z", text);
        Assert.Contains("'IP 192.168.0.1 not in certificate'", text);
        Assert.Contains("NonTrusted was set", text);
        Assert.Contains("show the user these fingerprints", text);
        Assert.Contains("Zertifikate anzeigen", text);
        Assert.Contains($"trustedCertificates: [\"{TestCertificates.Sha256(leaf)}\"]", text);
        Assert.Equal($"no trustedCertificates were given, so the device's certificate is not trusted", PlcTlsTrustPolicy.RefusalDetail(tls, 0));
    }

    [Fact]
    public void Consistency_AcceptsOnlyEvidenceThatMatchesTheTrustedList()
    {
        var leaf = TestCertificates.Create("PLC_1");
        var fingerprint = TestCertificates.Sha256(leaf);
        PlcTlsVerificationInfo Read(params string[] trusted) => PlcTlsTrustPolicy.Read(new TlsVerificationConfiguration("PLC_1", "info", leaf), trusted);

        var trusted = Read(fingerprint);
        trusted.SelectionSet = PlcTlsTrustPolicy.Trusted;
        trusted.Source = PlcTlsTrustPolicy.CallerSource;
        Assert.True(PlcTlsTrustPolicy.IsConsistent(trusted, trustedApplied: true, new[] { fingerprint }));
        // The host plans another list than the worker evaluated: the approved flags no longer match.
        Assert.False(PlcTlsTrustPolicy.IsConsistent(trusted, trustedApplied: true, Array.Empty<string>()));
        // An unapplied answer never claims Trusted.
        Assert.False(PlcTlsTrustPolicy.IsConsistent(trusted, trustedApplied: false, new[] { fingerprint }));

        var refused = Read();
        refused.SelectionSet = PlcTlsTrustPolicy.NonTrusted;
        Assert.True(PlcTlsTrustPolicy.IsConsistent(refused, trustedApplied: false, Array.Empty<string>()));
        Assert.False(PlcTlsTrustPolicy.IsConsistent(refused, trustedApplied: true, Array.Empty<string>()));
        Assert.True(PlcTlsTrustPolicy.IsConsistent(null, trustedApplied: false, Array.Empty<string>()));
        Assert.False(PlcTlsTrustPolicy.IsConsistent(null, trustedApplied: true, Array.Empty<string>()));

        var forged = Read();
        forged.Certificates[0].Approved = true;
        forged.SelectionSet = PlcTlsTrustPolicy.Trusted;
        forged.Source = PlcTlsTrustPolicy.CallerSource;
        Assert.False(PlcTlsTrustPolicy.IsConsistent(forged, trustedApplied: true, Array.Empty<string>()));

        var noSource = Read(fingerprint);
        noSource.SelectionSet = PlcTlsTrustPolicy.Trusted;
        Assert.False(PlcTlsTrustPolicy.IsConsistent(noSource, trustedApplied: true, new[] { fingerprint }));

        var malformed = Read();
        malformed.Certificates[0].Sha256 = fingerprint.ToLowerInvariant();
        Assert.False(PlcTlsTrustPolicy.IsConsistent(malformed, trustedApplied: false, Array.Empty<string>()));
    }

    [Fact]
    public void TlsKey_IsRefusedAsADecisionOrPassword()
    {
        Assert.Contains("only through trustedCertificates", PlcCallbackPolicy.ValidateEntries(
            new[] { new PlcCallbackDecisionInfo { Type = PlcTlsTrustPolicy.ConfigurationKey, Selection = "Trusted" } }, null));
        Assert.Contains("only through trustedCertificates", PlcCallbackPolicy.ValidateEntries(
            null, new[] { new PlcCallbackPasswordInfo { Type = PlcTlsTrustPolicy.ConfigurationKey, Variable = "X" } }));
    }

    [Fact]
    public void CanonicalType_WalksSeveralNamespaces()
    {
        Assert.Equal(PlcTlsTrustPolicy.CanonicalType, PlcCallbackPolicy.CanonicalType(typeof(TlsVerificationConfiguration),
            new[] { PlcCallbackPolicy.UploadConfigurationsNamespace, PlcCallbackPolicy.OnlineConfigurationsNamespace }));
        Assert.Null(PlcCallbackPolicy.CanonicalType(typeof(TlsVerificationConfiguration), PlcCallbackPolicy.UploadConfigurationsNamespace));
        Assert.Equal("Siemens.Engineering.Upload.Configurations.UploadMissingProducts", PlcCallbackPolicy.CanonicalType(
            typeof(Siemens.Engineering.Upload.Configurations.UploadMissingProducts),
            new[] { PlcCallbackPolicy.UploadConfigurationsNamespace, PlcCallbackPolicy.OnlineConfigurationsNamespace }));
    }
}
