using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.OpennessWorker.Openness.Safety;
using Xunit;
using SiemensProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// The worker's fail-safe gate on the Siemens doubles. The doubles offer no SafetyAdministration (the test project
/// links the gate without its administration lookup), so an F-block change there is left to TIA Portal and passes;
/// the one refusal (password set, not logged on) is pinned on the shared rule in PlcSafetyPolicyTests.
/// </summary>
public sealed class PlcSafetyGateTests
{
    private static SiemensProject Project()
    {
        var project = new SiemensProject { Path = new FileInfo(@"C:\p\p.ap21") };
        var device = new Device { Name = "Device" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = new PlcSoftware { Name = "PLC_1" } } });
        project.Devices.Items.Add(device);
        return project;
    }

    [Theory]
    [InlineData("delete_block")]
    [InlineData("rename_block")]
    [InlineData("renumber_block")]
    [InlineData("update_db_member")]
    [InlineData("copy_block")]
    [InlineData("move_block")]
    public void FBlockChanges_AreNotRefusedWhereTiaPortalDecides(string operation)
    {
        var project = Project();
        PlcSafetyGate.RequireChangePermitted(project, "PLC_1", new PlcBlock { Name = "F_Main", ProgrammingLanguage = "F_LAD" }, operation);
        PlcSafetyGate.RequireChangePermitted(project, "PLC_1", new PlcBlock { Name = "Main", ProgrammingLanguage = "LAD" }, operation);
        PlcSafetyGate.RequireChangePermitted(project, "PLC_1", null, operation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FBlockUpdates_InXmlAndSource_AreNotRefusedWhereTiaPortalDecides(bool sourceFormat)
        => PlcSafetyGate.RequireImportPermitted(Project(), "PLC_1", new PlcBlock { Name = "F_Main", ProgrammingLanguage = "F_LAD" },
            sourceFormat ? null : "<ProgrammingLanguage>F_LAD</ProgrammingLanguage>", sourceFormat);

    [Fact]
    public void StandardBlocks_NeverLookUpThePlc()
        // No project at all: a standard block returns before any lookup.
        => PlcSafetyGate.RequireChangePermitted(null!, "PLC_1", new PlcBlock { Name = "Main", ProgrammingLanguage = "SCL" }, "delete_block");
}
