using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

/// <summary>The one refused VCI export: SimaticSD of a fail-safe or system-folder PLC block (recorded TIA Portal V21 crash).</summary>
public sealed class VciSafetyExportTests
{
    private static readonly string[] Normal = { ProjectTreeNodeTypes.Device, ProjectTreeNodeTypes.PlcSoftware, ProjectTreeNodeTypes.BlockFolder, ProjectTreeNodeTypes.Ob };
    private static readonly string[] InSystemFolder = { ProjectTreeNodeTypes.Device, ProjectTreeNodeTypes.PlcSoftware, ProjectTreeNodeTypes.BlockFolder, ProjectTreeNodeTypes.SystemBlockFolder, ProjectTreeNodeTypes.Ob };

    [Theory]
    [InlineData("SimaticSD", true, "F_FBD", false)]
    [InlineData("simaticsd", true, "F_LAD", false)]
    [InlineData("SimaticSD", true, "SCL", true)]
    [InlineData("SimaticSD", true, null, true)]
    public void RefusesSimaticSdOfFailSafeOrSystemFolderBlocks(string format, bool isPlc, string? language, bool systemFolder)
    {
        var refusal = VciSafetyExport.Refusal(format, isPlc, systemFolder ? InSystemFolder : Normal, language, "Main_Safety");
        Assert.NotNull(refusal);
        Assert.Contains("Main_Safety", refusal);
        Assert.Contains("SimaticML", refusal);
    }

    [Theory]
    [InlineData("SimaticML", true, "F_FBD", true)]
    [InlineData("SCL", true, "F_SCL", true)]
    [InlineData("SimaticSD", true, "LAD", false)]
    [InlineData("SimaticSD", true, null, false)]
    [InlineData("SimaticSD", false, "F_FBD", true)]
    [InlineData(null, true, "F_FBD", true)]
    public void LeavesEverythingElseToTiaPortal(string? format, bool isPlc, string? language, bool systemFolder)
        => Assert.Null(VciSafetyExport.Refusal(format, isPlc, systemFolder ? InSystemFolder : Normal, language, "Block"));
}
