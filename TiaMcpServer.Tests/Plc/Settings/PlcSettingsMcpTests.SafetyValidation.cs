using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>Issue #25 (Safety Validation Assistant) through plc_read/plc_write against the FakeWorker scenario plc-settings.</summary>
public sealed partial class PlcSettingsMcpTests
{
    [Fact]
    public async Task SafetyValidation_TestsArePrepared_ReportedAndChecked_UnderTheAcceptanceGuard()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pkgD-sva-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var session = await Session.StartAsync(McpAccessMode.Full);
            var (document, raw) = await session.WriteAsync(false,
                new { operationId = "g", operation = "create_activation_test_group", folderPath = "/Line1" },
                new { operationId = "t", operation = "create_activation_test", name = "AT_Door", evaluationDevice = "PLC_1", folderPath = "/Line1" },
                new { operationId = "f", operation = "create_safety_function", testName = "AT_Door", settings = new Dictionary<string, object> { ["Description"] = "Door opens: drive stops" }, traced = true },
                new { operationId = "c", operation = "create_safety_function_condition", testName = "AT_Door", functionName = "SF 1", signalDevice = "F-DI 8x24VDC", signalUsage = "InputCondition", signalName = "Door_Closed",
                    settings = new Dictionary<string, object> { ["InitialInput"] = "TRUE", ["ExecutedInput"] = "FALSE" } },
                new { operationId = "r", operation = "reset_safety_function_test_result", testName = "AT_Door", functionName = "SF 1" },
                new { operationId = "rep", operation = "generate_activation_test_report", testName = "AT_Door", filePath = Path.Combine(directory, "at.pdf") });
            Assert.False(raw.IsError == true, document.GetRawText());
            Assert.True(document.GetProperty("success").GetBoolean(), document.GetRawText());
            var guards = GuardIds(document).ToList();
            Assert.Equal(5, guards.Count(g => g == "plc_safety_acceptance_required"));
            Assert.Contains("plc_safety_printout_is_no_acceptance", guards);
            Assert.Contains(PlcSafetyPolicy.AcceptanceNotice, document.GetRawText());
            Assert.Equal("readBack", Verification(document, "r").GetProperty("check").GetString());
            Assert.Equal("file", Verification(document, "rep").GetProperty("check").GetString());

            var (read, readRaw) = await session.ReadAsync(new { operationId = "v", operation = "get_safety_validation" },
                new { operationId = "k", operation = "check_activation_test", testName = "AT_Door", functionName = "SF 1" });
            Assert.False(readRaw.IsError == true, read.GetRawText());
            var section = Section(Item(read, 0).GetProperty("result").GetProperty("project"), "safetyValidation");
            var test = section.GetProperty("collections")[0].GetProperty("items")[0];
            Assert.Equal("AT_Door", test.GetProperty("key").GetString());
            Assert.Equal("/Line1", Property(test, "Group"));
            var function = test.GetProperty("collections")[0].GetProperty("items")[0];
            Assert.Equal("NotTested", Property(function, "TestState"));
            Assert.Equal("True", Property(function, "IsTraced"));
            Assert.Equal("TRUE", Property(function.GetProperty("collections")[0].GetProperty("items")[0], "InitialInput"));
            var validity = Section(Item(read, 1).GetProperty("result").GetProperty("project"), "testValidity");
            Assert.Equal("Okay", Property(validity, "State"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SafetyValidation_ATestResultIsNeverWritten()
    {
        await using var session = await Session.StartAsync(McpAccessMode.Full);
        var (document, raw) = await session.WriteAsync(false,
            new { operationId = "f", operation = "update_safety_function", testName = "AT", functionName = "SF 1", settings = new Dictionary<string, object> { ["TestState"] = "Succeeded" } });
        Assert.True(raw.IsError == true);
        Assert.Contains("records a test result", document.GetProperty("error").GetProperty("message").GetString());
        Assert.DoesNotContain(session.Methods, PlcSettingsCatalog.IsWrite);
        Assert.DoesNotContain("import_activation_test", PlcSettingsCatalog.Write);
    }

    [Fact]
    public async Task SafetyValidation_ReadWriteCallsAskOnce()
    {
        await using var session = await Session.StartAsync(McpAccessMode.ReadWrite);
        var (preview, _) = await session.WriteAsync(true, new { operationId = "g", operation = "create_activation_test_group", folderPath = "/X" });
        Assert.Equal(new[] { "plc_safety_acceptance_required" }, GuardIds(preview));
    }
}
