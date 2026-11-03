using System.Text.Json;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.PlcAlarms;

/// <summary>
/// Issue #19: plc_write export_system_diagnostics_settings and import_system_diagnostics_settings, project-wide members of the
/// alarm family (caller-file rules of the alarm classes). Policy, catalog, the state-only result binding, and the
/// plc-alarms-roundtrip FakeWorker end to end (its import answers Warning). Not live-tested: where the provider hangs and which
/// file extension TIA Portal wants are decided only live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class SystemDiagnosticsSettingsTests
{
    private sealed class StateOnlyResult
    {
        public string State { get; set; } = "Warning";
    }

    [Fact]
    public void Methods_AreProjectScoped_StandaloneOnly_AndNeedNoExtension()
    {
        foreach (var method in new[] { PlcAlarmMethods.ExportSystemDiagnosticsSettings, PlcAlarmMethods.ImportSystemDiagnosticsSettings })
        {
            Assert.True(PlcAlarmMethods.IsProjectScoped(method));
            Assert.Equal(PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings, PlcAlarmMethods.WorkbookKind(method));
            Assert.Null(PlcAlarmPolicy.RequiredExtension(method));
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method));
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
        }

        Assert.True(PlcAlarmPolicy.WorkbookKinds.IsOpaqueFile(PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings));
        Assert.Null(PlcAlarmPolicy.CheckFilePath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "settings.anything"), null));
        Assert.Equal(PlcAlarmPolicy.StateVerdict.Warning, PlcAlarmPolicy.InterpretState("Warning", warningIsSuccess: true));
        Assert.Equal(PlcAlarmPolicy.StateVerdict.Failure, PlcAlarmPolicy.InterpretState("Error", warningIsSuccess: true));
    }

    [Fact]
    public void Catalog_TakesFilePathOverwriteAndConfirmations_ButNoPlc()
    {
        PlcOperationRequest Op(string operation) => new() { OperationId = "d", Operation = operation, FilePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "diag.bin") };
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { Op(PlcAlarmMethods.ExportSystemDiagnosticsSettings) }).IsValid);
        var withOverwrite = Op(PlcAlarmMethods.ExportSystemDiagnosticsSettings);
        withOverwrite.Overwrite = true;
        withOverwrite.PortalConfirmations = "accept";
        Assert.True(PlcOperationCatalog.ValidateWrite(new[] { withOverwrite }).IsValid);
        var withPlc = Op(PlcAlarmMethods.ImportSystemDiagnosticsSettings);
        withPlc.PlcName = "PLC_1";
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { withPlc }).IsValid);
        var importOverwrite = Op(PlcAlarmMethods.ImportSystemDiagnosticsSettings);
        importOverwrite.Overwrite = true;
        Assert.False(PlcOperationCatalog.ValidateWrite(new[] { importOverwrite }).IsValid);
    }

    [Fact]
    public void StateOnlyBinding_ReadsState_AndReportsNoMissingMember()
    {
        var binding = PlcAlarmResultReader.BindStateOnly(typeof(StateOnlyResult));
        Assert.True(binding.IsUsable);
        Assert.Empty(binding.Missing);
        var outcome = PlcAlarmResultReader.Read(new StateOnlyResult(), binding);
        Assert.Equal("Warning", outcome.State);
        Assert.Empty(outcome.Unreadable);
        Assert.False(PlcAlarmResultReader.BindStateOnly(typeof(object)).IsUsable);
    }

    [Fact]
    public async Task Mcp_ExportsThenImportsAPrivateCopy_AndAWarningSucceedsWithAWarning()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var log = new FakeWorkerRequestLog(audit.Path);
        using var ui = new FakeWorkerUiOpenProject("plc-alarms-roundtrip");
        var files = Directory.CreateTempSubdirectory("tia-mcp-sysdiag-").FullName;
        try
        {
            await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "plc-alarms-roundtrip");
            var path = Path.Combine(files, "SystemDiagnostics.cfg");

            async Task<JsonElement> Write(params object[] operations)
            {
                var raw = await harness.Client.CallToolAsync("plc_write", new Dictionary<string, object?> { ["operations"] = operations, ["dryRun"] = false });
                Assert.Empty(StructuredContractInspector.FindViolations(raw));
                return raw.StructuredContent!.Value;
            }

            var exported = await Write(new { operationId = "e", operation = "export_system_diagnostics_settings", filePath = path });
            Assert.True(exported.GetProperty("success").GetBoolean(), exported.GetRawText());
            Assert.Equal("SystemDiagnosticsSettings", exported.GetProperty("effects")[0].GetProperty("effect").GetProperty("target").GetProperty("kind").GetString());
            var result = exported.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
            Assert.Equal(PlcAlarmFiles.Hash(path).Sha256, result.GetProperty("sha256").GetString());
            Assert.Equal("file", exported.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());

            var again = await Write(new { operationId = "e", operation = "export_system_diagnostics_settings", filePath = path });
            Assert.Contains("pass overwrite:true", again.GetProperty("error").GetProperty("message").GetString());

            var imported = await Write(new { operationId = "i", operation = "import_system_diagnostics_settings", filePath = path });
            Assert.True(imported.GetProperty("success").GetBoolean(), imported.GetRawText());
            Assert.Contains("plc_imports_alarm_data", imported.GetProperty("guards").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
            var importResult = imported.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
            Assert.Equal("Warning", importResult.GetProperty("state").GetString());
            Assert.Equal("fileHash", importResult.GetProperty("importCheck").GetProperty("method").GetString());
            Assert.Contains("TIA Portal reported state Warning for the import", imported.GetRawText());
            Assert.Contains(PlcAlarmMethods.ImportSystemDiagnosticsSettings, log.Methods());
        }
        finally
        {
            Directory.Delete(files, recursive: true);
        }
    }
}
