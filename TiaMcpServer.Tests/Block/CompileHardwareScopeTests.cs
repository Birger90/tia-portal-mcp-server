using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

/// <summary>
/// D9: compile_check scope hardware compiles the CPU device item (its ICompilable service), not the PLC software, so
/// hardware errors such as a protection level without a password become visible before a download.
/// </summary>
public sealed class CompileHardwareScopeTests
{
    private sealed class Compiler(CompilerResult result) : ICompilable
    {
        public int Calls { get; private set; }

        public CompilerResult Compile()
        {
            Calls++;
            return result;
        }
    }

    private static (Siemens.Engineering.Project Project, Compiler Software, Compiler Hardware) Project(CompilerResult software, CompilerResult hardware)
    {
        var softwareCompiler = new Compiler(software);
        var hardwareCompiler = new Compiler(hardware);
        var project = new Siemens.Engineering.Project();
        var device = new Device { Name = "Station_1" };
        device.DeviceItems.Items.Add(new DeviceItem
        {
            Name = "CPU_1",
            Container = new SoftwareContainer { Software = new PlcSoftware { Name = "PLC_1", CompilerService = softwareCompiler } },
            HardwareCompiler = hardwareCompiler,
        });
        project.Devices.Items.Add(device);
        return (project, softwareCompiler, hardwareCompiler);
    }

    private static CompilerResult ProtectionErrors() => new()
    {
        State = CompilerResultState.Error,
        ErrorCount = 3,
        Messages =
        {
            new CompilerResultMessage { Description = "Access level requires a password.", Path = "PLC_1", State = CompilerResultState.Error, ErrorCount = 1 },
        },
    };

    [Fact]
    public void HardwareScope_CompilesTheCpuDeviceItem_NotTheSoftware()
    {
        var (project, software, hardware) = Project(new CompilerResult { State = CompilerResultState.Success }, ProtectionErrors());

        var report = CompileChecker.Compile(project, null, null, CompileScopes.Hardware);

        Assert.Equal((0, 1), (software.Calls, hardware.Calls));
        Assert.Equal("hardware", report.Scope);
        Assert.Null(report.BlockPath);
        Assert.Equal("Error", report.OverallState);
        Assert.Equal(3, report.TotalErrorCount);
        var plc = Assert.Single(report.Plcs);
        Assert.Equal(("PLC_1", "Station_1"), (plc.PlcName, plc.DeviceName));
        Assert.Equal("Access level requires a password.", Assert.Single(plc.Messages).Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CompileScopes.Software)]
    public void SoftwareScope_IsTheUnchangedProgramCompile(string? scope)
    {
        // D9 as observed: the program compiles clean while the hardware would report three errors.
        var (project, software, hardware) = Project(new CompilerResult { State = CompilerResultState.Success }, ProtectionErrors());

        var report = CompileChecker.Compile(project, null, null, scope);

        Assert.Equal((1, 0), (software.Calls, hardware.Calls));
        Assert.Equal("plc", report.Scope);
        Assert.Equal(0, report.TotalErrorCount);
    }

    [Fact]
    public void HardwareScope_UnknownPlcIsTargetNotFound_AndBlockPathIsRefused()
    {
        var (project, _, hardware) = Project(new CompilerResult(), new CompilerResult());

        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => CompileChecker.Compile(project, "PLC_9", null, CompileScopes.Hardware)).FailureCategory);
        Assert.Equal(WorkerFailureCategories.ValidationError,
            Assert.Throws<WorkerOperationException>(() => CompileChecker.Compile(project, null, "PLC_1/Main", CompileScopes.Hardware)).FailureCategory);
        Assert.Equal(0, hardware.Calls);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("software", "Main", null)]
    [InlineData("hardware", null, null)]
    [InlineData("hardware", "Main", "cannot be combined")]
    [InlineData("Hardware", null, "not supported")]
    [InlineData("hardware_and_software", null, "not supported")]
    public void ScopeValidation(string? scope, string? blockPath, string? error)
    {
        var message = CompileScopes.Error(scope, blockPath);
        if (error is null) Assert.Null(message);
        else Assert.Contains(error, message);
    }

    [Fact]
    public void HostDecode_AcceptsTheHardwareScope_AndRejectsItWithABlockPath()
    {
        var report = new CompileCheckReport
        {
            Scope = "hardware", OverallState = "Error", TotalErrorCount = 3,
            Plcs = { new PlcCompileInfo { PlcName = "PLC_1", DeviceName = "Station_1", State = "Error", ErrorCount = 3 } },
        };
        var outcome = StandalonePayloadContract.DecodeCompile(WorkerCallResult.Ok(WorkerJson.SerializePayload(report)));
        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Failure);
        Assert.Equal("hardware", outcome.Value!.Scope);

        report.BlockPath = "Main";
        var rejected = StandalonePayloadContract.DecodeCompile(WorkerCallResult.Ok(WorkerJson.SerializePayload(report)));
        Assert.Equal("protocol_error", rejected.Failure!.Category);
    }
}
