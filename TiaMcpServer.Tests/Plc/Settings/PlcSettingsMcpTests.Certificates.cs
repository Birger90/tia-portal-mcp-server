using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Issue #14 (CPU certificates, dynamic certificate settings) through plc_read/plc_write against the FakeWorker scenario plc-settings.</summary>
public sealed partial class PlcSettingsMcpTests
{
    static partial void InitializeCertificates()
    {
        InitializeSafetyDocuments();
    }

    static partial void InitializeSafetyDocuments();

    [Fact]
    public async Task Certificates_AreCreatedReadWithFingerprintsAssignedExportedAndImported_WithoutTrust()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pkgD-cert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
            var exportPath = Path.Combine(directory, "tls.cer");
            var (document, raw) = await session.WriteAsync(false,
                new { operationId = "create", operation = "create_cpu_certificate", usage = "Tls", settings = new Dictionary<string, object> { ["SubjectCommonName"] = "PLC_1 TLS" }, subjectAlternativeNames = new[] { "IP:192.168.0.10" } },
                new { operationId = "assign", operation = "assign_cpu_certificate", certificateId = "1", certificateRole = "webserver" },
                new { operationId = "export", operation = "export_cpu_certificate", certificateId = "1", filePath = exportPath },
                new { operationId = "mgmt", operation = "set_certificate_management", settings = new Dictionary<string, object> { ["Usage"] = "Runtime" } },
                new { operationId = "svc", operation = "add_certificate_supported_service", certificateId = "7", serviceType = "Webserver", name = "Web" });
            Assert.False(raw.IsError == true, document.GetRawText());
            Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
            var created = Item(document, 0).GetProperty("result").GetProperty("items").EnumerateArray().Select(i => i.GetString()).ToList();
            Assert.Contains("Id 1", created);
            var sha = created.Single(i => i!.StartsWith("sha256 ", StringComparison.Ordinal))!.Substring(7);
            Assert.Equal(64, sha.Length);
            Assert.Equal("file", Verification(document, "export").GetProperty("check").GetString());
            Assert.True(File.Exists(exportPath));
            Assert.Equal("readBack", Verification(document, "assign").GetProperty("check").GetString());

            var (read, readRaw) = await session.ReadAsync(new { operationId = "c", operation = "get_cpu_certificates" });
            Assert.False(readRaw.IsError == true, read.GetRawText());
            var sections = Item(read, 0).GetProperty("result").GetProperty("plcs")[0].GetProperty("sections");
            var certificate = Section(sections, "localCertificateManager").GetProperty("collections")[0].GetProperty("items")[0];
            Assert.Equal("1", certificate.GetProperty("key").GetString());
            Assert.Equal(sha, Property(certificate, "Sha256"));
            Assert.Equal("CN=PLC_1 TLS", Property(certificate, "Subject"));
            Assert.Contains(sha, Property(Section(sections, "certificateAssignments"), "WebserverCertificate"));
            Assert.Equal("Runtime", Property(Section(sections, "certificateManagement"), "Usage"));

            var (preview, _) = await session.WriteAsync(true, new { operationId = "imp", operation = "import_cpu_certificate", filePath = exportPath });
            Assert.Equal(new[] { "plc_imports_certificate" }, GuardIds(preview));
            var (imported, _) = await session.WriteAsync(false,
                new { operationId = "imp", operation = "import_cpu_certificate", filePath = exportPath },
                new { operationId = "unassign", operation = "unassign_cpu_certificate", certificateRole = "webserver" },
                new { operationId = "del", operation = "delete_cpu_certificate", certificateId = "1" });
            Assert.True(imported.GetProperty("success").GetBoolean(), imported.GetRawText());
            Assert.Contains("Nothing was trusted.", imported.GetRawText());
            Assert.Contains("sha256 ", Item(imported, 0).GetProperty("result").GetRawText());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Certificates_AssignWithAnUnknownId_FailsTargetNotFound()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (document, _) = await session.WriteAsync(false, new { operationId = "a", operation = "assign_cpu_certificate", certificateId = "99", certificateRole = "opcUaServer" });
        Assert.Equal("target_not_found", Item(document, 0).GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public void Certificates_TheReadIsATemporaryExport_AllowedReadOnly()
    {
        Assert.Equal(OperationCapability.TemporaryExport, OperationPolicyCatalog.GetCapability("get_cpu_certificates"));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "get_cpu_certificates"));
    }
}
