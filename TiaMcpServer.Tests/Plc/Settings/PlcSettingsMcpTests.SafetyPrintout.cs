using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Issue #21 (safety printout) through plc_write against the FakeWorker scenario plc-settings.</summary>
public sealed partial class PlcSettingsMcpTests
{
    [Fact]
    public async Task SafetyPrintout_IsWrittenAsAFile_AndSaysItIsNoAcceptance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pkgD-printout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
            var path = Path.Combine(directory, "safety.pdf");
            var (document, raw) = await session.WriteAsync(false,
                new { operationId = "p", operation = "generate_safety_printout", filePath = path, documentLayout = "DocuInfo_ISO_A4_Portrait", printOption = "Compact" });
            Assert.False(raw.IsError == true, document.GetRawText());
            Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
            Assert.Equal(new[] { "plc_safety_printout_is_no_acceptance" }, GuardIds(document));
            Assert.Contains(PlcSafetyPolicy.AcceptanceNotice, document.GetRawText());
            Assert.Equal("file", Verification(document, "p").GetProperty("check").GetString());
            Assert.StartsWith("%PDF-1.4 fake safety printout MicrosoftPrintToPdf DocuInfo_ISO_A4_Portrait Compact", File.ReadAllText(path));

            var (again, _) = await session.WriteAsync(false,
                new { operationId = "p2", operation = "generate_safety_printout", filePath = path, documentLayout = "DocuInfo_ISO_A4_Portrait" });
            Assert.Equal("failed", Item(again, 0).GetProperty("status").GetString());

            var (failed, _) = await session.WriteAsync(false,
                new { operationId = "p3", operation = "generate_safety_printout", filePath = Path.Combine(directory, "false.pdf"), documentLayout = "fake:false" });
            Assert.Equal("postcondition_failed", Item(failed, 0).GetProperty("failure").GetProperty("category").GetString());
            Assert.False(File.Exists(Path.Combine(directory, "false.pdf")));
            Assert.Contains(TiaMcpServer.Contracts.Plc.Settings.PlcSafetyPrintoutNames.LayoutHint,
                Item(failed, 0).GetProperty("failure").GetProperty("message").GetString());

            // K79: TIA Portal's refusal of an unknown layout name keeps its text and says where the names come from.
            var (unknown, _) = await session.WriteAsync(false,
                new { operationId = "p4", operation = "generate_safety_printout", filePath = Path.Combine(directory, "unknown.pdf"), documentLayout = "fake:unknown" });
            var message = Item(unknown, 0).GetProperty("failure").GetProperty("message").GetString()!;
            Assert.Contains("No Document description object", message);
            Assert.Contains("Dokumentationseinstellungen", message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
