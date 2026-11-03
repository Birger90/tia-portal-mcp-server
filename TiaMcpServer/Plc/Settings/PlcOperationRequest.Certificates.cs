using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The certificate fields of the settings family (issue #14); filePath, overwrite, settings, name and passwordVariable are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("create_cpu_certificate (required): the CertificateUsage of the template (LocalCertificateStore.GetCertificateTemplate): Tls, WebServer, OpcUaServer, OpcUaClient or OpcUaClientServer (None is not supported).")]
    public string? Usage { get; set; }

    [Description("export_cpu_certificate, delete_cpu_certificate, assign_cpu_certificate (required): the certificate's Id as get_cpu_certificates lists it. add_certificate_supported_service, update_certificate_supported_service, remove_certificate_supported_service (required): the UInt32 certificate id of the supported service (CertificateSupportedServiceComposition.Create/Find).")]
    public string? CertificateId { get; set; }

    [Description("assign_cpu_certificate, unassign_cpu_certificate (required): webserver (the PLC's dynamic attribute WebserverCertificate) or opcUaServer (OpcUaServerCertificate of the OPC UA submodule).")]
    public string? CertificateRole { get; set; }

    [Description("create_cpu_certificate: subject alternative names of the template, each \"Type:Value\" with Type a SubjectAlternativeNameType member (Dns, Email, IP, Uri), e.g. \"IP:192.168.0.1\"; at most 20.")]
    public IReadOnlyList<string>? SubjectAlternativeNames { get; set; }

    [Description("add_certificate_supported_service (required): the CertificateSupportedServiceName of the service: Webserver, OpcUaServer or OpcUaClient.")]
    public string? ServiceType { get; set; }
}
