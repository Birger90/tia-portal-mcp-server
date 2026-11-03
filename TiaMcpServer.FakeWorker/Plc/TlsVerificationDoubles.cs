using System.Collections;
using System.Security.Cryptography.X509Certificates;

// Executable FakeWorker stand-ins only: the shape of the online legitimation TLS verification configuration as
// the sources describe it (online-status.md: PlcName, VerificationInfo, Certificates of VerificationCertificate
// whose Certificate is an X509Certificate2, CurrentSelection of TlsVerificationConfigurationSelection
// { NonVerified, Trusted, NonTrusted }), so the real reflection reader and recorders see these members. The types
// of PlcName and VerificationInfo are undocumented; strings are used here. Nothing else is claimed about V21.
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
        public TlsVerificationConfiguration(string plcName, string verificationInfo, params X509Certificate2[] certificates)
        {
            PlcName = plcName;
            VerificationInfo = verificationInfo;
            Certificates = new Siemens.Engineering.Online.Security.VerificationCertificateComposition(certificates);
        }

        public string PlcName { get; }

        public string VerificationInfo { get; }

        public Siemens.Engineering.Online.Security.VerificationCertificateComposition Certificates { get; }

        public TlsVerificationConfigurationSelection CurrentSelection { get; set; }
    }
}

namespace Siemens.Engineering.Online.Security
{
    public class VerificationCertificate
    {
        public VerificationCertificate(X509Certificate2 certificate) => Certificate = certificate;

        public X509Certificate2 Certificate { get; }
    }

    public class VerificationCertificateComposition : IEnumerable<VerificationCertificate>
    {
        private readonly List<VerificationCertificate> _items;

        public VerificationCertificateComposition(IEnumerable<X509Certificate2> certificates)
            => _items = certificates.Select(certificate => new VerificationCertificate(certificate)).ToList();

        public IEnumerator<VerificationCertificate> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace TiaMcpServer.FakeWorker.Plc
{
    /// <summary>
    /// The device certificates the TLS scenarios present: a leaf "CN=PLC_1" issued by a stand-in CA, both fixed DER
    /// test certificates generated with openssl (EC P-256, valid 2026-11-01 to 2126-10-08). Their SHA-256 fingerprints
    /// over the DER bytes are <see cref="LeafSha256"/> and <see cref="CaSha256"/> (cross-checked with openssl).
    /// </summary>
    static class FakeTlsCertificates
    {
        public const string LeafSha256 = "DE87F8569826C2EC86DC10F9964E0699BD2D833A7CE5085929B05DDA48F5C061";

        public const string CaSha256 = "E7D989934C4BB66B7EFABDADDFED6E9A4977752E05979E8A0BF9F8561A0634AE";

        public const string VerificationInfo = "The IP address of the device does not match the addresses of its certificate.";

        private const string LeafDer =
            "MIIBcjCCARgCFHBzO09L8E0xSQVzfZhgztDg1gVHMAoGCCqGSM49BAMCMEMxHzAdBgNVBAMMFkZha2VXb3JrZXIgU3RhbmQtaW4gQ0ExIDAeBgNVBAoMF1RpYU1jcFNlcnZlciBGYWtlV29ya2VyMCAXDTI2MTEwMTEwMjkzOFoYDzIxMjYxMDA4MTAyOTM4WjAyMQ4wDAYDVQQDDAVQTENfMTEgMB4GA1UECgwXVGlhTWNwU2VydmVyIEZha2VXb3JrZXIwWTATBgcqhkjOPQIBBggqhkjOPQMBBwNCAATWs0kzFqtAsYHnX3XiYH3Q8fiDd38gnMVjJJjgjR7Xumu0JCxzQhOWAFqot7Tnm/oCjIX9ioetwvurDObJu//TMAoGCCqGSM49BAMCA0gAMEUCIQCAoiS+Kuz5AITcMtoKi2ca+TMN7swT7s+q1lr2shWTDQIgTZ53eVLu7/Uij3a5vZl5Y7qZg2n4+mM6fQbOh/DlS+M=";

        private const string CaDer =
            "MIIB3jCCAYOgAwIBAgIUWCoTVhU7axahUU7GCQwN7t4tLj8wCgYIKoZIzj0EAwIwQzEfMB0GA1UEAwwWRmFrZVdvcmtlciBTdGFuZC1pbiBDQTEgMB4GA1UECgwXVGlhTWNwU2VydmVyIEZha2VXb3JrZXIwIBcNMjYxMTAxMTAyOTM4WhgPMjEyNjEwMDgxMDI5MzhaMEMxHzAdBgNVBAMMFkZha2VXb3JrZXIgU3RhbmQtaW4gQ0ExIDAeBgNVBAoMF1RpYU1jcFNlcnZlciBGYWtlV29ya2VyMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE6ji34ZohssGOjxe5SdRzU7MlU7JguF7twAwYvP82Xsvn135FUM26YXyVfDrkAQ/4EhfOhT5XRwW8i72l1QcWmqNTMFEwHQYDVR0OBBYEFPlDVzF0PcWo4BKaFzU/mbrXFTTDMB8GA1UdIwQYMBaAFPlDVzF0PcWo4BKaFzU/mbrXFTTDMA8GA1UdEwEB/wQFMAMBAf8wCgYIKoZIzj0EAwIDSQAwRgIhALyOkRGVfErDyL5VqOsvjqcWToMQ2HR+p/2NciZD6CQ+AiEA72Q0aNEo0ZyhXLOwo6ANPNqCX1uXsNryYQfo3GdPv58=";

        /// <summary>A fresh trust question for <paramref name="plcName"/> presenting the leaf and the CA.</summary>
        public static Siemens.Engineering.Online.Configurations.TlsVerificationConfiguration Configuration(string plcName)
            => new(plcName, VerificationInfo,
                X509CertificateLoader.LoadCertificate(Convert.FromBase64String(LeafDer)),
                X509CertificateLoader.LoadCertificate(Convert.FromBase64String(CaDer)));
    }
}
