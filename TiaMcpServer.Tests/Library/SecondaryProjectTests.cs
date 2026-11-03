using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Library;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Library;

/// <summary>Issue #20: read-only secondary projects (library_write lifecycle items, list_secondary_projects, compare_software secondary_plc).</summary>
public sealed class SecondaryProjectTests
{
    private const string Old = @"C:\Projects\Old\Old.ap21";

    [Fact]
    public void LibraryCatalog_AcceptsTheSecondaryItems_AndTheyAreLifecycle()
    {
        var items = new[]
        {
            new LibraryOperationRequest { OperationId = "o", Operation = LibraryMethods.OpenSecondaryProject, Path = Old, Upgrade = true },
            new LibraryOperationRequest { OperationId = "r", Operation = LibraryMethods.RetrieveSecondaryProject, Path = @"C:\A\Old.zap21", TargetDirectory = @"C:\Out" },
            new LibraryOperationRequest { OperationId = "c", Operation = LibraryMethods.CloseSecondaryProject, SecondaryProject = Old },
            new LibraryOperationRequest { OperationId = "m", Operation = LibraryMethods.CreateMasterCopy, Library = "project",
                Source = new LibrarySourceInfo { Kind = "block", Path = "PLC_OLD/LegacyFB", Project = Old } },
        };
        var result = LibraryCatalog.ValidateWrite(items);
        Assert.True(result.IsValid, result.Error);
        Assert.All(LibraryMethods.SecondaryProjectOperations, operation => Assert.True(LibraryMethods.IsLifecycle(operation)));
        Assert.True(LibraryCatalog.ValidateRead(new[] { new LibraryOperationRequest { OperationId = "l", Operation = LibraryMethods.ListSecondaryProjects } }).IsValid);
        // A capture from the secondary project depends on its open earlier in the call.
        Assert.Contains(LibraryCatalog.SecondaryKey(Old)!, LibraryWriteDomain.Touches(items[3]));
        Assert.Equal(LibraryCatalog.SecondaryKey(Old), LibraryCatalog.LibraryKey(items[0]));
    }

    [Fact]
    public void LibraryCatalog_RefusesARelativeSecondaryPath()
    {
        var result = LibraryCatalog.ValidateWrite(new[] { new LibraryOperationRequest { OperationId = "c", Operation = LibraryMethods.CloseSecondaryProject, SecondaryProject = "Old.ap21" } });
        Assert.False(result.IsValid);
        Assert.Contains("secondaryProject must be an absolute path", result.Error);
    }

    [Fact]
    public void CompareSoftware_SecondaryPlc_NeedsTargetProject_AndOnlyThere()
    {
        var good = new PlcOperationRequest { OperationId = "a", Operation = PlcOnlineMethods.CompareSoftware, CompareTarget = PlcOnlinePolicy.CompareTargets.SecondaryPlc, TargetName = "PLC_OLD", TargetProject = Old };
        Assert.True(PlcOperationCatalog.ValidateRead(new[] { good }).IsValid, PlcOperationCatalog.ValidateRead(new[] { good }).Error);
        var missing = new PlcOperationRequest { OperationId = "b", Operation = PlcOnlineMethods.CompareSoftware, CompareTarget = PlcOnlinePolicy.CompareTargets.SecondaryPlc, TargetName = "PLC_OLD" };
        Assert.Contains("targetProject", PlcOperationCatalog.ValidateRead(new[] { missing }).Error);
        var misplaced = new PlcOperationRequest { OperationId = "c", Operation = PlcOnlineMethods.CompareSoftware, CompareTarget = PlcOnlinePolicy.CompareTargets.Plc, TargetName = "PLC_2", TargetProject = Old };
        Assert.Contains("targetProject", PlcOperationCatalog.ValidateRead(new[] { misplaced }).Error);
    }
}
