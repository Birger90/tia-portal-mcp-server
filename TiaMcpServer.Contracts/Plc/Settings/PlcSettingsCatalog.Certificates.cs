namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #14, CPU certificates and dynamic certificate settings. Evidence: Siemens.Engineering.Base.xml (V21):
/// <c>Security.LocalCertificateManager</c> (<c>EnableGlobalCertificatesStore</c>, <c>LocalCertificateStore</c>), <c>LocalCertificateStore</c>
/// (<c>Certificates</c>, <c>GetCertificateTemplate(CertificateUsage)</c>), <c>CertificateComposition.Create(CertificateTemplate)</c>,
/// <c>.Import(FileInfo)</c>, <c>.Import(FileInfo, SecureString)</c>, <c>Certificate</c> (<c>Id</c>, <c>SubjectCommonName</c>, <c>ValidUntil</c>,
/// <c>HasPrivateKey</c>, <c>Export(FileInfo)</c>, <c>Delete()</c>), <c>CertificateTemplate</c> (<c>Signature</c>, <c>SubjectAlternativeNames</c>,
/// <c>SubjectCommonName</c>, <c>Usage</c>, <c>ValidFrom</c>, <c>ValidUntil</c>), <c>SubjectAlternativeNameComposition.Create(SubjectAlternativeNameType, String)</c>,
/// <c>HW.Features.CertificateManagementConfiguration</c> (<c>Usage</c>, <c>CertificateExpirationEventActivated</c>,
/// <c>RemainingCertificateLifetime</c>, <c>CertificateSupportedServices</c>), <c>HW.CertificateSupportedServiceComposition.Create(UInt32, String,
/// CertificateSupportedServiceName)</c>, <c>.Find(UInt32)</c>, <c>HW.CertificateSupportedService</c> (<c>Id</c>, <c>ServiceType</c>,
/// <c>ServiceGroupName</c>, <c>ApplicationUri</c>, <c>Guid</c>, <c>Delete()</c>); the V21 manual sections "Managing certificate" (the
/// LocalCertificateManager service at the PLC device item; the dynamic attributes <c>WebserverCertificate</c> of the PLC and
/// <c>OpcUaServerCertificate</c> of the OPC UA submodule assign and, set to null, unassign a certificate) and "Managing dynamic certificate
/// settings". Nothing here trusts a certificate: the fingerprints are reported for the human to compare.
/// </summary>
public static class PlcCertificateMethods
{
    public const string GetCpuCertificates = "get_cpu_certificates";
    public const string SetCertificateManager = "set_certificate_manager";
    public const string CreateCpuCertificate = "create_cpu_certificate";
    public const string ImportCpuCertificate = "import_cpu_certificate";
    public const string ExportCpuCertificate = "export_cpu_certificate";
    public const string DeleteCpuCertificate = "delete_cpu_certificate";
    public const string AssignCpuCertificate = "assign_cpu_certificate";
    public const string UnassignCpuCertificate = "unassign_cpu_certificate";
    public const string SetCertificateManagement = "set_certificate_management";
    public const string AddCertificateSupportedService = "add_certificate_supported_service";
    public const string UpdateCertificateSupportedService = "update_certificate_supported_service";
    public const string RemoveCertificateSupportedService = "remove_certificate_supported_service";
}

public static class PlcCertificateNames
{
    public const string Manager = "Siemens.Engineering.Security.LocalCertificateManager";
    public const string Management = "Siemens.Engineering.HW.Features.CertificateManagementConfiguration";

    public static readonly string[] ManagerProperties = { "EnableGlobalCertificatesStore" };

    public static readonly string[] TemplateProperties = { "SubjectCommonName", "Signature", "ValidFrom", "ValidUntil" };

    public static readonly string[] ManagementProperties = { "Usage", "CertificateExpirationEventActivated", "RemainingCertificateLifetime" };

    public static readonly string[] SupportedServiceProperties = { "ServiceGroupName" };

    /// <summary>The certificate roles (dynamic attributes) a certificate is assigned to.</summary>
    public const string WebserverRole = "webserver";
    public const string OpcUaServerRole = "opcUaServer";

    public static readonly string[] Roles = { WebserverRole, OpcUaServerRole };

    public const string WebserverAttribute = "WebserverCertificate";
    public const string OpcUaServerAttribute = "OpcUaServerCertificate";

    public static string AttributeOf(string role) => role == OpcUaServerRole ? OpcUaServerAttribute : WebserverAttribute;

    /// <summary>The most subject alternative names one create takes.</summary>
    public const int MaxSubjectAlternativeNames = 20;

    /// <summary>The extension of the temporary export the fingerprints are computed from.</summary>
    public const string FingerprintExportExtension = ".cer";
}

/// <summary>A certificate as read: the documented properties and, from a temporary export, its X.509 fingerprints.</summary>
public sealed class PlcCertificateFingerprintInfo
{
    /// <summary>SHA-256 over the exported certificate's DER bytes (<c>RawData</c>), upper-case hex; null when not computable.</summary>
    public string? Sha256 { get; set; }

    public string? Sha1Thumbprint { get; set; }

    public string? Subject { get; set; }

    public string? Issuer { get; set; }

    public string? NotBefore { get; set; }

    public string? NotAfter { get; set; }

    /// <summary>Why no fingerprint could be computed; null when it was.</summary>
    public string? Error { get; set; }
}

public static partial class PlcSettingsCatalog
{
    private static void AddCertificates()
    {
        const int Issue = 14;
        // The read exports each certificate to a temporary file to compute its fingerprints: TemporaryExport.
        Add(new(PlcCertificateMethods.GetCpuCertificates, false, Issue, false, PlcCertificateNames.Manager) { Exports = true });
        Add(new(PlcCertificateMethods.SetCertificateManager, true, Issue, false, PlcCertificateNames.Manager, PlcCertificateNames.ManagerProperties, requiresSettings: true));
        Add(new(PlcCertificateMethods.CreateCpuCertificate, true, Issue, false, PlcCertificateNames.Manager, PlcCertificateNames.TemplateProperties,
            new[] { ("Signature", "Sha1RSA") }));
        Add(new(PlcCertificateMethods.ImportCpuCertificate, true, Issue, false, PlcCertificateNames.Manager));
        Add(new(PlcCertificateMethods.ExportCpuCertificate, true, Issue, false, PlcCertificateNames.Manager));
        Add(new(PlcCertificateMethods.DeleteCpuCertificate, true, Issue, false, PlcCertificateNames.Manager));
        Add(new(PlcCertificateMethods.AssignCpuCertificate, true, Issue, false, null));
        Add(new(PlcCertificateMethods.UnassignCpuCertificate, true, Issue, false, null));
        Add(new(PlcCertificateMethods.SetCertificateManagement, true, Issue, false, PlcCertificateNames.Management, PlcCertificateNames.ManagementProperties, requiresSettings: true));
        Add(new(PlcCertificateMethods.AddCertificateSupportedService, true, Issue, false, PlcCertificateNames.Management));
        Add(new(PlcCertificateMethods.UpdateCertificateSupportedService, true, Issue, false, PlcCertificateNames.Management, PlcCertificateNames.SupportedServiceProperties, requiresSettings: true));
        Add(new(PlcCertificateMethods.RemoveCertificateSupportedService, true, Issue, false, PlcCertificateNames.Management));
    }
}
