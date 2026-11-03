using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Issue #9: CAx data (network_write export_cax / import_cax, AutomationML). The Siemens-free TransferResult projection, the catalog
/// and policy, and the hardware-modules FakeWorker end to end. Not live-tested: which object offers CaxProvider and how TIA Portal
/// reports a failed transfer are decided only live.
/// </summary>
[Collection("Mcp protocol serial")]
public sealed class CaxTests
{
    private enum State { Success, Information, Warning, Error }

    private sealed class Entry(string text, State state)
    {
        public string Message => text;
        public State State => state;
        public List<Entry> Messages { get; } = new();
    }

    private sealed class Result(State state, params Entry[] messages)
    {
        public State State => state;
        public int ErrorCount => messages.Count(message => message.State == State.Error);
        public int WarningCount => 0;
        public List<Entry> Messages { get; } = messages.ToList();
    }

    [Fact]
    public void Run_ProjectsTheTransferResult_AndBoundsTheMessages()
    {
        var nested = new Entry("outer", State.Warning);
        nested.Messages.Add(new Entry("inner", State.Error));
        var projected = CaxRun.Read(new Result(State.Error, nested), _ => false);
        Assert.True(CaxRun.Failed(projected));
        Assert.Equal("0", projected.ErrorCount.Value); // the stand-in counts top-level errors only; the value is reported as read
        Assert.Equal("inner", Assert.Single(projected.Messages[0].Messages).Message.Value);
        Assert.Equal(RuntimeValueStates.Missing, projected.Messages[0].DateTime.State);
        Assert.Contains("inner", CaxRun.Summary(projected));

        var many = new Result(State.Success, Enumerable.Range(0, CaxNames.MaxMessages + 5).Select(index => new Entry($"m{index}", State.Information)).ToArray());
        var bounded = CaxRun.Read(many, _ => false);
        Assert.False(CaxRun.Failed(bounded));
        Assert.Equal(CaxNames.MaxMessages, bounded.Messages.Count);
        Assert.Equal(5, bounded.MessagesOmitted);
        Assert.Equal(new[] { "B" }, CaxRun.Added(new[] { "a" }, new[] { "A", "B" }));
    }

    private static bool Valid(NetworkOperationRequest operation) => NetworkOperationCatalog.ValidateWrite(new[] { operation }).IsValid;

    [Fact]
    public void Catalog_RequiresAnAbsoluteAmlPath_AndADocumentedImportOption()
    {
        Assert.True(Valid(new() { OperationId = "e", Operation = HardwareModuleMethods.ExportCax, FilePath = @"C:\export\plant.aml" }));
        Assert.True(Valid(new() { OperationId = "e", Operation = HardwareModuleMethods.ExportCax, FilePath = @"C:\export\plant.aml", DeviceName = "PLC_1", LogFilePath = @"C:\export\plant.log", Overwrite = true }));
        Assert.False(Valid(new() { OperationId = "e", Operation = HardwareModuleMethods.ExportCax, FilePath = @"C:\export\plant.xml" }));
        Assert.False(Valid(new() { OperationId = "e", Operation = HardwareModuleMethods.ExportCax, FilePath = "plant.aml" }));
        Assert.False(Valid(new() { OperationId = "e", Operation = HardwareModuleMethods.ExportCax, FilePath = @"C:\export\plant.aml", LogFilePath = @"C:\export\plant.aml" }));
        Assert.False(Valid(new() { OperationId = "e", Operation = HardwareModuleMethods.ExportCax, FilePath = @"C:\export\plant.aml", ImportOption = "RetainTiaDevice" }));
        Assert.True(Valid(new() { OperationId = "i", Operation = HardwareModuleMethods.ImportCax, FilePath = @"C:\export\plant.aml", ImportOption = "RetainTiaDevice" }));
        Assert.False(Valid(new() { OperationId = "i", Operation = HardwareModuleMethods.ImportCax, FilePath = @"C:\export\plant.aml" }));
        Assert.False(Valid(new() { OperationId = "i", Operation = HardwareModuleMethods.ImportCax, FilePath = @"C:\export\plant.aml", ImportOption = "Merge" }));
        Assert.False(Valid(new() { OperationId = "i", Operation = HardwareModuleMethods.ImportCax, FilePath = @"C:\export\plant.aml", ImportOption = "RetainTiaDevice", DeviceName = "PLC_1" }));
    }

    [Fact]
    public void Policy_BothMutate_AndGateAsNetworkWrite()
    {
        foreach (var method in new[] { HardwareModuleMethods.ExportCax, HardwareModuleMethods.ImportCax })
        {
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
            Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, method));
            Assert.Equal("network_write", ProjectCapabilityCatalog.OperationFor(method));
        }
    }

    private static async Task<JsonElement> CallAsync(McpProtocolTestHarness harness, object[] operations, bool? dryRun = null)
    {
        var arguments = new Dictionary<string, object?> { ["operations"] = operations };
        if (dryRun is not null) arguments["dryRun"] = dryRun;
        var reply = await harness.Client.CallToolAsync("network_write", arguments);
        var root = Assert.IsType<JsonElement>(reply.StructuredContent);
        Assert.Equal(CanonicalJson.Serialize(root), Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);
        return root;
    }

    private static JsonElement Item(JsonElement root) => root.GetProperty("batch").GetProperty("operations")[0];

    [Fact]
    public async Task Mcp_ExportThenImport_AndARejectedImport()
    {
        using var audit = new TempAuditDirectory();
        var directory = Path.Combine(Path.GetTempPath(), "pkgC-cax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path, "hardware-modules");
            var file = Path.Combine(directory, "Plant.aml");

            var exported = await CallAsync(harness, new object[] { new { operationId = "e", operation = "export_cax", filePath = file, deviceName = "PLC_1" } });
            Assert.True(exported.GetProperty("success").GetBoolean(), exported.GetRawText());
            var result = Item(exported).GetProperty("result");
            Assert.Equal("device", result.GetProperty("scope").GetString());
            Assert.Equal(64, result.GetProperty("file").GetProperty("sha256").GetString()!.Length);
            Assert.Equal("Success", result.GetProperty("transfer").GetProperty("state").GetProperty("value").GetString());
            // K78: the worker's re-hash of the placed file is the verification.
            var entry = exported.GetProperty("verification").GetProperty("operations")[0];
            Assert.Equal(("file", true), (entry.GetProperty("check").GetString(), entry.GetProperty("success").GetBoolean()));
            Assert.Contains(result.GetProperty("file").GetProperty("sha256").GetString()!, entry.GetProperty("actual").GetString());

            var again = await CallAsync(harness, new object[] { new { operationId = "e", operation = "export_cax", filePath = file } });
            Assert.Equal("validation_error", again.GetProperty("error").GetProperty("category").GetString());
            var overwritten = await CallAsync(harness, new object[] { new { operationId = "e", operation = "export_cax", filePath = file, overwrite = true, logFilePath = Path.Combine(directory, "Plant.log") } });
            Assert.True(overwritten.GetProperty("success").GetBoolean(), overwritten.GetRawText());
            Assert.Contains(overwritten.GetProperty("guards").EnumerateArray(), guard => guard.GetProperty("id").GetString() == "network_cax_export_overwrites_file");
            Assert.True(Item(overwritten).GetProperty("result").GetProperty("succeeded").GetBoolean());

            var import = new { operationId = "i", operation = "import_cax", filePath = file, importOption = "RetainTiaDevice" };
            var preview = await CallAsync(harness, new object[] { import }, dryRun: true);
            Assert.Contains(preview.GetProperty("guards").EnumerateArray(), guard => guard.GetProperty("id").GetString() == "network_cax_import_changes_hardware");
            var imported = await CallAsync(harness, new object[] { import });
            Assert.True(imported.GetProperty("success").GetBoolean(), imported.GetRawText());
            Assert.Equal("Plant", Assert.Single(Item(imported).GetProperty("result").GetProperty("addedDevices").EnumerateArray()).GetString());

            var broken = Path.Combine(directory, "Broken.aml");
            File.WriteAllText(broken, "<CAEXFile>Fail</CAEXFile>");
            var rejected = await CallAsync(harness, new object[] { new { operationId = "i", operation = "import_cax", filePath = broken, importOption = "MoveToParkingLot" } });
            Assert.False(rejected.GetProperty("success").GetBoolean());
            Assert.Equal("engineering_rejected", Item(rejected).GetProperty("failure").GetProperty("category").GetString());

            var missing = await CallAsync(harness, new object[] { new { operationId = "i", operation = "import_cax", filePath = Path.Combine(directory, "None.aml"), importOption = "RetainTiaDevice" } });
            Assert.Equal("target_not_found", missing.GetProperty("error").GetProperty("category").GetString());

            // Security review F4: an existing log is refused without overwrite:true, for the export and the import.
            var log = Path.Combine(directory, "Plant.log");
            Assert.True(File.Exists(log));
            var logRefused = await CallAsync(harness, new object[] { new { operationId = "e", operation = "export_cax", filePath = Path.Combine(directory, "Other.aml"), logFilePath = log } });
            Assert.Equal("validation_error", logRefused.GetProperty("error").GetProperty("category").GetString());
            Assert.Contains("log file", logRefused.GetProperty("error").GetProperty("message").GetString());
            Assert.False(File.Exists(Path.Combine(directory, "Other.aml")));
            var importLogRefused = await CallAsync(harness, new object[] { new { operationId = "i", operation = "import_cax", filePath = file, importOption = "RetainTiaDevice", logFilePath = log } });
            Assert.Equal("validation_error", importLogRefused.GetProperty("error").GetProperty("category").GetString());
            var importLogReplaced = await CallAsync(harness, new object[] { new { operationId = "i", operation = "import_cax", filePath = file, importOption = "RetainTiaDevice", logFilePath = log, overwrite = true } });
            Assert.True(importLogReplaced.GetProperty("success").GetBoolean(), importLogReplaced.GetRawText());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"\\?\C:\Logs\Plant.log")]
    [InlineData(@"\\.\NUL")]
    [InlineData(@"C:\Logs\NUL.log")]
    public void LogFilePath_DevicePathsAndReservedNamesAreRefused(string log)
    {
        var validation = NetworkOperationCatalog.ValidateWrite(new[] { new NetworkOperationRequest { OperationId = "e", Operation = "export_cax", FilePath = @"C:\Exports\Plant.aml", LogFilePath = log } });
        Assert.False(validation.IsValid);
        Assert.Contains("logFilePath", validation.Error, StringComparison.Ordinal);
    }
}
