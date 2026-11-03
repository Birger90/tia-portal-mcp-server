// Stand-ins for the CPU certificates and dynamic certificate settings (issue #14) with the member names of Siemens.Engineering.Base.xml (V21).
// Export writes a real self-signed DER certificate so the fingerprint path runs; member shapes only, no evidence of Siemens behavior.
using System.Collections;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Siemens.Engineering.Security
{
    public enum CertificateUsage { None, Tls, WebServer, OpcUaServer, OpcUaClient, OpcUaClientServer }

    public enum SignatureAlgorithm { None, Sha1RSA, Sha256RSA }

    public enum SubjectAlternativeNameType { None, Dns, Email, IP, Uri }

    public sealed class SubjectAlternativeName(SubjectAlternativeNameType type, string value)
    {
        public SubjectAlternativeNameType Type { get; } = type;

        public string Value { get; } = value;
    }

    public sealed class SubjectAlternativeNameComposition : IEnumerable<SubjectAlternativeName>
    {
        public List<SubjectAlternativeName> Items { get; } = new();

        public SubjectAlternativeName Create(SubjectAlternativeNameType type, string value)
        {
            if (type == SubjectAlternativeNameType.None) throw new InvalidOperationException("None is not supported.");
            var san = new SubjectAlternativeName(type, value);
            Items.Add(san);
            return san;
        }

        public IEnumerator<SubjectAlternativeName> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class CertificateTemplate(CertificateUsage usage)
    {
        public SignatureAlgorithm Signature { get; set; } = SignatureAlgorithm.Sha256RSA;

        public SubjectAlternativeNameComposition SubjectAlternativeNames { get; } = new();

        public string SubjectCommonName { get; set; } = "PLC_1";

        public CertificateUsage Usage { get; set; } = usage;

        public DateTime ValidFrom { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public DateTime ValidUntil { get; set; } = new(2046, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    public sealed class Certificate
    {
        private readonly CertificateComposition _owner;
        private readonly byte[] _der;

        public Certificate(CertificateComposition owner, uint id, string commonName, DateTime validUntil, bool hasPrivateKey)
        {
            _owner = owner;
            Id = id;
            SubjectCommonName = commonName;
            ValidUntil = validUntil;
            HasPrivateKey = hasPrivateKey;
            using var key = RSA.Create(2048);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(validUntil));
            _der = certificate.Export(X509ContentType.Cert);
        }

        public uint Id { get; }

        public string SubjectCommonName { get; }

        public DateTime ValidUntil { get; }

        public bool HasPrivateKey { get; }

        public void Export(FileInfo path) => File.WriteAllBytes(path.FullName, _der);

        public void Delete() => _owner.Items.Remove(this);
    }

    public sealed class CertificateComposition : IEnumerable<Certificate>
    {
        private uint _next = 1;

        public List<Certificate> Items { get; } = new();

        public Certificate Create(CertificateTemplate template)
        {
            var certificate = new Certificate(this, _next++, template.SubjectCommonName, template.ValidUntil, hasPrivateKey: true);
            Items.Add(certificate);
            return certificate;
        }

        public Certificate Import(FileInfo file) => Import(file, null);

        public Certificate Import(FileInfo file, SecureString? password)
        {
            if (!file.Exists) throw new InvalidOperationException("The certificate file does not exist.");
            var certificate = new Certificate(this, _next++, Path.GetFileNameWithoutExtension(file.Name), new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc), password is not null);
            Items.Add(certificate);
            return certificate;
        }

        public IEnumerator<Certificate> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class LocalCertificateStore
    {
        public CertificateComposition Certificates { get; } = new();

        public CertificateTemplate GetCertificateTemplate(CertificateUsage usage)
            => usage == CertificateUsage.None ? throw new InvalidOperationException("None is not supported.") : new CertificateTemplate(usage);
    }

    public sealed class LocalCertificateManager
    {
        public bool EnableGlobalCertificatesStore { get; set; } = true;

        public LocalCertificateStore LocalCertificateStore { get; } = new();
    }
}

namespace Siemens.Engineering.HW
{
    public enum CertificateConfigurationUsage { TIAPortal, Runtime }

    public enum CertificateSupportedServiceName { None, OpcUaClient, OpcUaServer, Webserver }

    public sealed class CertificateSupportedService(CertificateSupportedServiceComposition owner, uint id, string group, CertificateSupportedServiceName type)
    {
        public uint Id { get; } = id;

        public CertificateSupportedServiceName ServiceType { get; } = type;

        public string ServiceGroupName { get; set; } = group;

        public string ApplicationUri { get; } = "urn:fake";

        public Guid Guid { get; } = Guid.Empty;

        public void Delete() => owner.Items.Remove(this);
    }

    public sealed class CertificateSupportedServiceComposition : IEnumerable<CertificateSupportedService>
    {
        public List<CertificateSupportedService> Items { get; } = new();

        public CertificateSupportedService Create(uint id, string group, CertificateSupportedServiceName type)
        {
            var service = new CertificateSupportedService(this, id, group, type);
            Items.Add(service);
            return service;
        }

        public CertificateSupportedService? Find(uint id) => Items.FirstOrDefault(s => s.Id == id);

        public CertificateSupportedService? Find(Guid guid) => Items.FirstOrDefault(s => s.Guid == guid);

        public IEnumerator<CertificateSupportedService> GetEnumerator() => Items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace Siemens.Engineering.HW.Features
{
    public sealed class CertificateManagementConfiguration
    {
        public Siemens.Engineering.HW.CertificateConfigurationUsage Usage { get; set; } = Siemens.Engineering.HW.CertificateConfigurationUsage.TIAPortal;

        public bool CertificateExpirationEventActivated { get; set; }

        public ushort RemainingCertificateLifetime { get; set; } = 20;

        public Siemens.Engineering.HW.CertificateSupportedServiceComposition CertificateSupportedServices { get; } = new();
    }
}
