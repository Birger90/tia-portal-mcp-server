using TiaMcpServer.Contracts.Shared.ClaudeInbox;
using Xunit;
using static TiaMcpServer.Contracts.Shared.ClaudeInbox.ClaudeSelectionAddressing;
using static TiaMcpServer.Tests.Contracts.ClaudeInbox.ClaudeInboxContractTests;

namespace TiaMcpServer.Tests.Contracts.ClaudeInbox;

public sealed class ClaudeSelectionAddressingTests
{
    private static readonly ClaudeChainEntry Software = E("PLC_1", PlcSoftwareType);
    private static readonly ClaudeChainEntry Device = E("Station_1", DeviceType);

    [Fact]
    public void BlockInRoot_IsPlcBlocksName()
    {
        var item = Describe(new[] { E("Main", "Siemens.Engineering.SW.Blocks.OB", PlcBlockType), E("Program blocks", BlockSystemGroupType), Software, Device });
        Assert.Equal(ClaudeSelectionKinds.PlcBlock, item.Kind);
        Assert.Equal("PLC_1/Blocks/Main", item.Address.BlockPath);
        Assert.Equal("PLC_1", item.Address.PlcName);
        Assert.Equal("Station_1", item.Address.DeviceName);
    }

    [Fact]
    public void BlockInNestedFoldersOfUnit_UsesUnitsPath()
    {
        var item = Describe(new[]
        {
            E("FB_Motor", PlcBlockType), E("Inner", BlockUserGroupType), E("Outer", BlockUserGroupType), E("Blocks", BlockSystemGroupType),
            E("Unit_A", PlcUnitType), Software,
        });
        Assert.Equal("PLC_1/Units/Unit_A/Blocks/Outer/Inner/FB_Motor", item.Address.BlockPath);
        Assert.Equal("Unit_A", item.Address.UnitName);
    }

    [Fact]
    public void BlockGroup_IsGroupPath_AndRootGroupIsBlocks()
    {
        Assert.Equal("PLC_1/Blocks/Outer", Describe(new[] { E("Outer", BlockUserGroupType), E("Program blocks", BlockSystemGroupType), Software }).Address.BlockPath);
        Assert.Equal("PLC_1/Blocks", Describe(new[] { E("Program blocks", BlockSystemGroupType), Software }).Address.BlockPath);
    }

    [Fact]
    public void FolderWithoutName_GivesNoPath()
    {
        var item = Describe(new[] { E("Main", PlcBlockType), new ClaudeChainEntry(new[] { BlockUserGroupType }, null), Software });
        Assert.Null(item.Address.BlockPath);
        Assert.Equal("PLC_1", item.Address.PlcName);
    }

    [Fact]
    public void PlcTypeAndTagTableAndTag()
    {
        Assert.Equal("PLC_1/Types/Motor/UDT_Motor", Describe(new[] { E("UDT_Motor", "Siemens.Engineering.SW.Types.PlcStruct", PlcTypeType), E("Motor", TypeUserGroupType), E("PLC data types", TypeSystemGroupType), Software }).Address.TypePath);
        var table = Describe(new[] { E("Ventile", TagTableType), E("Cell", TagTableUserGroupType), E("Line", TagTableUserGroupType), E("PLC tags", TagTableSystemGroupType), Software });
        Assert.Equal(("Ventile", "/Line/Cell"), (table.Address.TableName, table.Address.FolderPath));
        var tag = Describe(new[] { E("V1", TagType), E("Ventile", TagTableType), E("PLC tags", TagTableSystemGroupType), Software });
        Assert.Equal(ClaudeSelectionKinds.Tag, tag.Kind);
        Assert.Equal(("Ventile", "/"), (tag.Address.TableName, tag.Address.FolderPath));
    }

    [Fact]
    public void DeviceAndDeviceItem()
    {
        Assert.Equal("Station_1", Describe(new[] { Device, E("Project1", ProjectType) }).Address.DeviceName);
        var item = Describe(new[] { E("PROFINET interface_1", DeviceItemType), E("PLC_1", DeviceItemType), Device });
        Assert.Equal(ClaudeSelectionKinds.DeviceItem, item.Kind);
        Assert.Equal(("Station_1", "PROFINET interface_1"), (item.Address.DeviceName, item.Address.DeviceItemName));
    }

    [Fact]
    public void LibraryItems()
    {
        var type = Describe(new[] { E("Valve", LibraryTypeType), E("Actors", LibraryTypeUserFolderType), E("Types", LibraryTypeSystemFolderType), E("Project library", ProjectLibraryType) });
        Assert.Equal(("project", "/Actors/Valve"), (type.Address.Library, type.Address.LibraryTypePath));
        var version = Describe(new[] { E("1.0.0", LibraryTypeVersionType), E("Valve", LibraryTypeType), E("Types", LibraryTypeSystemFolderType), E("Lib", GlobalLibraryType) }, @"D:\Libs\Lib.al21");
        Assert.Equal((@"D:\Libs\Lib.al21", "/Valve"), (version.Address.Library, version.Address.LibraryTypePath));
        var copy = Describe(new[] { E("Station", MasterCopyType), E("Sub", MasterCopyUserFolderType), E("Master copies", MasterCopySystemFolderType), E("Lib", GlobalLibraryType) });
        Assert.Equal(((string?)null, "Station", "/Sub"), (copy.Address.Library, copy.Address.MasterCopyName, copy.Address.MasterCopyFolderPath));
        Assert.Equal("/", Describe(new[] { E("Master copies", MasterCopySystemFolderType), E("Project library", ProjectLibraryType) }).Address.MasterCopyFolderPath);
    }

    [Fact]
    public void UnknownType_IsOtherWithoutAddress()
    {
        var item = Describe(new[] { E("Screen_1", "Siemens.Engineering.HmiUnified.UI.Screens.HmiScreen") });
        Assert.Equal(ClaudeSelectionKinds.Other, item.Kind);
        Assert.True(item.Address.IsEmpty);
    }
}
