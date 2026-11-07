using System.Collections;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Executable boundary doubles only: the shape of the online legitimation TLS verification configuration as the
// sources describe it (online-status.md: PlcName, VerificationInfo, Certificates of VerificationCertificate whose
// Certificate is an X509Certificate2, CurrentSelection of TlsVerificationConfigurationSelection { NonVerified,
// Trusted, NonTrusted }). The types of PlcName and VerificationInfo are undocumented; the doubles use a string and
// an object so the text projection is exercised. Nothing else about the V21 types is claimed.
namespace Siemens.Engineering.Online.Configurations
{
    public enum TlsVerificationConfigurationSelection
    {
        NonVerified,
        Trusted,
        NonTrusted,
    }

    public class TlsVerificationConfiguration
    {
        private readonly Func<string?> _plcName;
        private TlsVerificationConfigurationSelection _selection;

        public TlsVerificationConfiguration(string? plcName, object? verificationInfo, params X509Certificate2?[] certificates)
            : this(() => plcName, verificationInfo, new Siemens.Engineering.Online.Security.VerificationCertificateComposition(certificates))
        {
        }

        public TlsVerificationConfiguration(Func<string?> plcName, object? verificationInfo, Siemens.Engineering.Online.Security.VerificationCertificateComposition certificates)
        {
            _plcName = plcName;
            VerificationInfo = verificationInfo;
            Certificates = certificates;
        }

        public string? PlcName => _plcName();

        public object? VerificationInfo { get; }

        public Siemens.Engineering.Online.Security.VerificationCertificateComposition Certificates { get; }

        /// <summary>When set, assigning <c>Trusted</c> throws like a TIA Portal rejection.</summary>
        public bool RejectTrusted { get; set; }

        /// <summary>When set, assigning <c>NonTrusted</c> throws (NonTrusted cannot be set: the refusal must still throw).</summary>
        public bool RejectNonTrusted { get; set; }

        /// <summary>Every value assigned, in order.</summary>
        public List<TlsVerificationConfigurationSelection> Assigned { get; } = new();

        public TlsVerificationConfigurationSelection CurrentSelection
        {
            get => _selection;
            set
            {
                if (RejectTrusted && value == TlsVerificationConfigurationSelection.Trusted)
                    throw new InvalidOperationException("TIA Portal rejected the trust selection.");
                if (RejectNonTrusted && value == TlsVerificationConfigurationSelection.NonTrusted)
                    throw new InvalidOperationException("TIA Portal rejected the NonTrusted selection.");
                Assigned.Add(value);
                _selection = value;
            }
        }
    }

    /// <summary>An online password callback (documented SetPassword(SecureString)), for the download's legitimation phase.</summary>
    public class OnlineReadAccessPassword
    {
        public bool PasswordSupplied { get; private set; }

        /// <summary>Audit T1: documented IsSecureCommunication; secure unless a test says otherwise.</summary>
        public bool IsSecureCommunication { get; set; } = true;

        public void SetPassword(System.Security.SecureString password) => PasswordSupplied = password.Length > 0;
    }

    /// <summary>An online configuration that is neither a selection nor a password (for example authentication): it cannot be answered.</summary>
    public class OnlineAuthenticationConfiguration
    {
    }
}

namespace Siemens.Engineering.Online.Security
{
    public class VerificationCertificate
    {
        public VerificationCertificate(X509Certificate2? certificate) => Certificate = certificate;

        public X509Certificate2? Certificate { get; }
    }

    public class VerificationCertificateComposition : IEnumerable<VerificationCertificate>
    {
        private readonly List<VerificationCertificate> _items;

        public VerificationCertificateComposition(IEnumerable<X509Certificate2?> certificates)
            => _items = certificates.Select(certificate => new VerificationCertificate(certificate)).ToList();

        public IEnumerator<VerificationCertificate> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace TiaMcpServer.Tests.TestUtilities
{
    /// <summary>Self-signed test certificates with fixed validity, created in memory.</summary>
    internal static class TestCertificates
    {
        public static X509Certificate2 Create(string commonName)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
            return request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }

        public static string Sha256(X509Certificate2 certificate) => Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    /// <summary>A shape whose members can be missing or throw, for the reflection reader.</summary>
    public sealed class PartialTlsShape
    {
        public string? PlcName => throw new InvalidOperationException("unreadable");

        public IEnumerable<object?> Certificates { get; init; } = Array.Empty<object?>();
    }

    /// <summary>An item of a certificate list that is not a VerificationCertificate.</summary>
    public sealed class CertificateWithoutMember
    {
        public string Name => "no Certificate member";
    }

    public sealed class FatalTestException : Exception
    {
        public FatalTestException() : base("The TIA Portal connection was lost.")
        {
        }
    }

    public sealed class FatalTlsShape
    {
        public string? PlcName => throw new FatalTestException();
    }

    /// <summary>An enum with the documented member names that is not <c>TlsVerificationConfigurationSelection</c>.</summary>
    public enum ForeignTrustSelection
    {
        NonVerified,
        Trusted,
        NonTrusted,
    }

    /// <summary>Every documented TLS member, but <c>CurrentSelection</c> is of another enum type: nothing may be written to it.</summary>
    public sealed class ForeignSelectionTlsShape
    {
        public ForeignSelectionTlsShape(X509Certificate2 certificate)
            => Certificates = new Siemens.Engineering.Online.Security.VerificationCertificateComposition(new[] { certificate });

        public string PlcName => "PLC_1";

        public string VerificationInfo => "info";

        public Siemens.Engineering.Online.Security.VerificationCertificateComposition Certificates { get; }

        /// <summary>Every value assigned to <see cref="CurrentSelection"/>, in order.</summary>
        public List<ForeignTrustSelection> Assigned { get; } = new();

        public ForeignTrustSelection CurrentSelection
        {
            get => Assigned.LastOrDefault();
            set => Assigned.Add(value);
        }
    }
}
