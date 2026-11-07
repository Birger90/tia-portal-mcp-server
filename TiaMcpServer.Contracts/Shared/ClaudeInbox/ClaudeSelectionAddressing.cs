namespace TiaMcpServer.Contracts.Shared.ClaudeInbox;

/// <summary>
/// Turns the chain of a selected object (its runtime type names and names, from the object up to the project) into the
/// kind and the MCP addressing hints of <see cref="ClaudeAddress"/>. Siemens-free: the type names are compared as text
/// against the V21 type names (<c>Siemens.Engineering.SW.Blocks.PlcBlock</c>, ...), all documented in the V21 API XML.
/// A chain whose shape is not recognised gives <see cref="ClaudeSelectionKinds.Other"/> and no address; the raw chain is
/// always kept in the request, so Claude can still find the object with <c>browse_project_tree</c>.
/// </summary>
public static class ClaudeSelectionAddressing
{
    public const string PlcBlockType = "Siemens.Engineering.SW.Blocks.PlcBlock";
    public const string BlockUserGroupType = "Siemens.Engineering.SW.Blocks.PlcBlockUserGroup";
    public const string BlockSystemGroupType = "Siemens.Engineering.SW.Blocks.PlcBlockSystemGroup";
    public const string PlcTypeType = "Siemens.Engineering.SW.Types.PlcType";
    public const string TypeUserGroupType = "Siemens.Engineering.SW.Types.PlcTypeUserGroup";
    public const string TypeSystemGroupType = "Siemens.Engineering.SW.Types.PlcTypeSystemGroup";
    public const string TagTableType = "Siemens.Engineering.SW.Tags.PlcTagTable";
    public const string TagTableUserGroupType = "Siemens.Engineering.SW.Tags.PlcTagTableUserGroup";
    public const string TagTableSystemGroupType = "Siemens.Engineering.SW.Tags.PlcTagTableSystemGroup";
    public const string TagType = "Siemens.Engineering.SW.Tags.PlcTag";
    public const string PlcSoftwareType = "Siemens.Engineering.SW.PlcSoftware";
    public const string PlcUnitType = "Siemens.Engineering.SW.Units.PlcUnit";
    public const string DeviceType = "Siemens.Engineering.HW.Device";
    public const string DeviceItemType = "Siemens.Engineering.HW.DeviceItem";
    public const string SubnetType = "Siemens.Engineering.HW.Subnet";
    public const string ProjectLibraryType = "Siemens.Engineering.Library.ProjectLibrary";
    public const string GlobalLibraryType = "Siemens.Engineering.Library.GlobalLibrary";
    public const string LibraryTypeType = "Siemens.Engineering.Library.Types.LibraryType";
    public const string LibraryTypeVersionType = "Siemens.Engineering.Library.Types.LibraryTypeVersion";
    public const string LibraryTypeUserFolderType = "Siemens.Engineering.Library.Types.LibraryTypeUserFolder";
    public const string LibraryTypeSystemFolderType = "Siemens.Engineering.Library.Types.LibraryTypeSystemFolder";
    public const string MasterCopyType = "Siemens.Engineering.Library.MasterCopies.MasterCopy";
    public const string MasterCopyUserFolderType = "Siemens.Engineering.Library.MasterCopies.MasterCopyUserFolder";
    public const string MasterCopySystemFolderType = "Siemens.Engineering.Library.MasterCopies.MasterCopySystemFolder";
    public const string ProjectType = "Siemens.Engineering.Project";

    /// <summary>Builds the selection item for one chain; <paramref name="globalLibraryPath"/> is the global library's path when the Add-In could read it.</summary>
    public static ClaudeSelectionItem Describe(IReadOnlyList<ClaudeChainEntry> chain, string? globalLibraryPath = null)
    {
        if (chain is null || chain.Count == 0) throw new ArgumentException("A selection needs at least the selected object.", nameof(chain));
        var self = chain[0];
        var kind = KindOf(self);
        return new ClaudeSelectionItem(kind, self.Name, chain, AddressOf(kind, chain, globalLibraryPath));
    }

    public static string KindOf(ClaudeChainEntry entry)
    {
        if (entry.Is(PlcBlockType)) return ClaudeSelectionKinds.PlcBlock;
        if (entry.Is(BlockUserGroupType) || entry.Is(BlockSystemGroupType)) return ClaudeSelectionKinds.BlockGroup;
        if (entry.Is(PlcTypeType)) return ClaudeSelectionKinds.PlcType;
        if (entry.Is(TypeUserGroupType) || entry.Is(TypeSystemGroupType)) return ClaudeSelectionKinds.TypeGroup;
        if (entry.Is(TagTableType)) return ClaudeSelectionKinds.TagTable;
        if (entry.Is(TagTableUserGroupType) || entry.Is(TagTableSystemGroupType)) return ClaudeSelectionKinds.TagTableGroup;
        if (entry.Is(TagType)) return ClaudeSelectionKinds.Tag;
        if (entry.Is(PlcSoftwareType)) return ClaudeSelectionKinds.PlcSoftware;
        if (entry.Is(PlcUnitType)) return ClaudeSelectionKinds.SoftwareUnit;
        if (entry.Is(DeviceType)) return ClaudeSelectionKinds.Device;
        if (entry.Is(DeviceItemType)) return ClaudeSelectionKinds.DeviceItem;
        if (entry.Is(SubnetType)) return ClaudeSelectionKinds.Subnet;
        if (entry.Is(ProjectLibraryType) || entry.Is(GlobalLibraryType)) return ClaudeSelectionKinds.Library;
        if (entry.Is(LibraryTypeType)) return ClaudeSelectionKinds.LibraryType;
        if (entry.Is(LibraryTypeVersionType)) return ClaudeSelectionKinds.LibraryTypeVersion;
        if (entry.Is(LibraryTypeUserFolderType) || entry.Is(LibraryTypeSystemFolderType)) return ClaudeSelectionKinds.LibraryTypeFolder;
        if (entry.Is(MasterCopyType)) return ClaudeSelectionKinds.MasterCopy;
        if (entry.Is(MasterCopyUserFolderType) || entry.Is(MasterCopySystemFolderType)) return ClaudeSelectionKinds.MasterCopyFolder;
        if (entry.Is(ProjectType)) return ClaudeSelectionKinds.Project;
        return ClaudeSelectionKinds.Other;
    }

    private static ClaudeAddress AddressOf(string kind, IReadOnlyList<ClaudeChainEntry> chain, string? globalLibraryPath)
    {
        var software = IndexOf(chain, PlcSoftwareType);
        var plcName = software >= 0 ? chain[software].Name : null;
        var unit = software >= 0 ? IndexOf(chain, PlcUnitType, software) : -1;
        var unitName = unit >= 0 ? chain[unit].Name : null;
        var device = IndexOf(chain, DeviceType);
        var deviceName = device >= 0 ? chain[device].Name : null;
        var self = chain[0];

        switch (kind)
        {
            case ClaudeSelectionKinds.PlcBlock:
            case ClaudeSelectionKinds.BlockGroup:
            {
                if (plcName is null || (unit >= 0 && unitName is null)) return Device(deviceName, null, plcName);
                var root = unitName is null ? plcName + "/Blocks" : plcName + "/Units/" + unitName + "/Blocks";
                var folders = Folders(chain, BlockUserGroupType, kind == ClaudeSelectionKinds.PlcBlock ? 1 : 0, unit >= 0 ? unit : software);
                if (folders is null) return Device(deviceName, unitName, plcName);
                string path;
                if (kind == ClaudeSelectionKinds.PlcBlock)
                {
                    if (self.Name is null) return Device(deviceName, unitName, plcName);
                    path = Join(root, folders, self.Name);
                }
                else
                {
                    path = self.Is(BlockSystemGroupType) ? root : Join(root, folders, null);
                }

                return new ClaudeAddress(plcName, unitName, path, null, null, null, deviceName, null, null, null, null, null);
            }
            case ClaudeSelectionKinds.PlcType:
            case ClaudeSelectionKinds.TypeGroup:
            {
                if (plcName is null) return Device(deviceName, null, null);
                var root = plcName + "/Types";
                var folders = Folders(chain, TypeUserGroupType, kind == ClaudeSelectionKinds.PlcType ? 1 : 0, software);
                if (folders is null || (kind == ClaudeSelectionKinds.PlcType && self.Name is null)) return Device(deviceName, unitName, plcName);
                var path = kind == ClaudeSelectionKinds.PlcType ? Join(root, folders, self.Name) : self.Is(TypeSystemGroupType) ? root : Join(root, folders, null);
                return new ClaudeAddress(plcName, unitName, null, path, null, null, deviceName, null, null, null, null, null);
            }
            case ClaudeSelectionKinds.TagTable:
            case ClaudeSelectionKinds.Tag:
            {
                if (plcName is null) return Device(deviceName, null, null);
                var table = kind == ClaudeSelectionKinds.TagTable ? 0 : IndexOf(chain, TagTableType);
                if (table < 0 || chain[table].Name is null) return Device(deviceName, unitName, plcName);
                var folders = Folders(chain, TagTableUserGroupType, table + 1, software);
                if (folders is null) return Device(deviceName, unitName, plcName);
                return new ClaudeAddress(plcName, unitName, null, null, chain[table].Name, "/" + string.Join("/", folders), deviceName, null, null, null, null, null);
            }
            case ClaudeSelectionKinds.TagTableGroup:
            case ClaudeSelectionKinds.PlcSoftware:
            case ClaudeSelectionKinds.SoftwareUnit:
                return Device(deviceName, unitName ?? (kind == ClaudeSelectionKinds.SoftwareUnit ? self.Name : null), plcName ?? (kind == ClaudeSelectionKinds.PlcSoftware ? self.Name : null));
            case ClaudeSelectionKinds.Device:
                return Device(self.Name, null, null);
            case ClaudeSelectionKinds.DeviceItem:
                return new ClaudeAddress(plcName, null, null, null, null, null, deviceName, self.Name, null, null, null, null);
            case ClaudeSelectionKinds.Library:
            case ClaudeSelectionKinds.LibraryType:
            case ClaudeSelectionKinds.LibraryTypeVersion:
            case ClaudeSelectionKinds.LibraryTypeFolder:
            case ClaudeSelectionKinds.MasterCopy:
            case ClaudeSelectionKinds.MasterCopyFolder:
                return LibraryAddress(kind, chain, globalLibraryPath);
            default:
                return ClaudeAddress.None;
        }
    }

    private static ClaudeAddress LibraryAddress(string kind, IReadOnlyList<ClaudeChainEntry> chain, string? globalLibraryPath)
    {
        var library = IndexOf(chain, ProjectLibraryType) is var p && p >= 0 ? "project"
            : IndexOf(chain, GlobalLibraryType) >= 0 ? globalLibraryPath : null;
        var self = chain[0];
        string? typePath = null, masterCopyName = null, masterCopyFolder = null;
        switch (kind)
        {
            case ClaudeSelectionKinds.LibraryType:
            case ClaudeSelectionKinds.LibraryTypeVersion:
            {
                var type = kind == ClaudeSelectionKinds.LibraryType ? 0 : IndexOf(chain, LibraryTypeType);
                if (type >= 0 && chain[type].Name is { } typeName)
                {
                    var folders = FoldersBelowRoot(chain, LibraryTypeUserFolderType, type + 1);
                    if (folders is not null) typePath = "/" + string.Join("/", folders.Concat(new[] { typeName }));
                }

                break;
            }
            case ClaudeSelectionKinds.LibraryTypeFolder:
            {
                var folders = self.Is(LibraryTypeSystemFolderType) ? new List<string>() : FoldersBelowRoot(chain, LibraryTypeUserFolderType, 0);
                if (folders is not null) typePath = "/" + string.Join("/", folders);
                break;
            }
            case ClaudeSelectionKinds.MasterCopy:
            {
                var folders = FoldersBelowRoot(chain, MasterCopyUserFolderType, 1);
                masterCopyName = self.Name;
                if (folders is not null) masterCopyFolder = "/" + string.Join("/", folders);
                break;
            }
            case ClaudeSelectionKinds.MasterCopyFolder:
            {
                var folders = self.Is(MasterCopySystemFolderType) ? new List<string>() : FoldersBelowRoot(chain, MasterCopyUserFolderType, 0);
                if (folders is not null) masterCopyFolder = "/" + string.Join("/", folders);
                break;
            }
        }

        return new ClaudeAddress(null, null, null, null, null, null, null, null, library, typePath, masterCopyName, masterCopyFolder);
    }

    private static ClaudeAddress Device(string? deviceName, string? unitName, string? plcName)
        => new(plcName, unitName, null, null, null, null, deviceName, null, null, null, null, null);

    private static int IndexOf(IReadOnlyList<ClaudeChainEntry> chain, string type, int before = int.MaxValue)
    {
        for (var i = 0; i < chain.Count && i < before; i++)
            if (chain[i].Is(type)) return i;
        return -1;
    }

    /// <summary>The names of the user groups of <paramref name="groupType"/> from <paramref name="start"/> up to (not including)
    /// <paramref name="end"/>, outermost first; null when one of them has no name.</summary>
    private static List<string>? Folders(IReadOnlyList<ClaudeChainEntry> chain, string groupType, int start, int end)
    {
        var names = new List<string>();
        for (var i = start; i < chain.Count && i < end; i++)
        {
            if (!chain[i].Is(groupType)) continue;
            if (chain[i].Name is not { } name) return null;
            names.Add(name);
        }

        names.Reverse();
        return names;
    }

    private static List<string>? FoldersBelowRoot(IReadOnlyList<ClaudeChainEntry> chain, string userFolderType, int start)
        => Folders(chain, userFolderType, start, chain.Count);

    private static string Join(string root, List<string> folders, string? leaf)
    {
        var parts = new List<string> { root };
        parts.AddRange(folders);
        if (leaf is not null) parts.Add(leaf);
        return string.Join("/", parts);
    }
}
