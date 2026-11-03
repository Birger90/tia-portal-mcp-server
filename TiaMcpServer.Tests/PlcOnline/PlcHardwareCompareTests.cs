using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.PlcOnline;

/// <summary>
/// Issue #7: plc_read compare_hardware / compare_hardware_to_online. Catalog shape rules, the shared policy and the standalone
/// restriction; the end-to-end run is in <see cref="PlcOnlineMcpTests"/>. Not live-tested: which types implement
/// IHardwareCompareTarget is decided only at runtime.
/// </summary>
public sealed class PlcHardwareCompareTests
{
    private static bool Valid(PlcOperationRequest operation) => PlcOperationCatalog.ValidateRead(new[] { operation }).IsValid;

    private static PlcOperationRequest Op(string operation, string? target = null, string? name = null, string? scope = null)
        => new() { OperationId = "h", Operation = operation, CompareTarget = target, TargetName = name, HardwareScope = scope };

    [Fact]
    public void Catalog_OfflineNeedsATargetWithName_OnlineTakesNone()
    {
        Assert.True(Valid(Op(PlcOnlineMethods.CompareHardware, "plc", "PLC_2")));
        Assert.True(Valid(Op(PlcOnlineMethods.CompareHardware, "master_copy", "StationTemplate", "cpu")));
        Assert.False(Valid(Op(PlcOnlineMethods.CompareHardware, "plc")));
        Assert.False(Valid(Op(PlcOnlineMethods.CompareHardware, "project_library", "x")));
        Assert.False(Valid(Op(PlcOnlineMethods.CompareHardware, "plc", "PLC_2", "rack")));
        Assert.True(Valid(Op(PlcOnlineMethods.CompareHardwareToOnline)));
        Assert.True(Valid(Op(PlcOnlineMethods.CompareHardwareToOnline, scope: "device")));
        Assert.False(Valid(Op(PlcOnlineMethods.CompareHardwareToOnline, "plc", "PLC_2")));
        Assert.False(Valid(new PlcOperationRequest { OperationId = "h", Operation = PlcOnlineMethods.CompareHardwareToOnline, TrustedCertificates = new[] { "nothex" } }));
        // hardwareScope belongs to the hardware compares only.
        Assert.False(Valid(new PlcOperationRequest { OperationId = "s", Operation = PlcOnlineMethods.CompareSoftware, CompareTarget = "project_library", HardwareScope = "cpu" }));
    }

    [Fact]
    public void Policy_SharesTheCompareRules()
    {
        Assert.True(PlcOnlineMethods.IsCompare(PlcOnlineMethods.CompareHardware));
        Assert.True(PlcOnlineMethods.IsOnlineCompare(PlcOnlineMethods.CompareHardwareToOnline));
        Assert.False(PlcOnlineMethods.IsOnlineCompare(PlcOnlineMethods.CompareHardware));
        Assert.Null(PlcOnlinePolicy.ValidateHardwareCompare(null, null, null, PlcOnlinePolicy.DefaultCompareDepth, PlcOnlinePolicy.DefaultCompareElements, online: true));
        Assert.NotNull(PlcOnlinePolicy.ValidateHardwareCompare("plc", "PLC_2", null, 0, PlcOnlinePolicy.DefaultCompareElements, online: false));
        foreach (var method in new[] { PlcOnlineMethods.CompareHardware, PlcOnlineMethods.CompareHardwareToOnline })
        {
            Assert.Equal(ProjectCapabilityCatalog.OperationFor(PlcOnlineMethods.CompareSoftware) == PlcOnlineMethods.CompareSoftware ? method : "plc_read",
                ProjectCapabilityCatalog.OperationFor(method));
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method));
            Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
        }
    }
}
