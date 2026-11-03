namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Parameters of every <see cref="PlcSourceFolderMethods"/> worker method; only the method's own fields are
/// read. Group paths use the plc_read path conventions (<c>PLC/Blocks/...</c>, <c>PLC/Types/...</c>, with
/// <c>PLC/Units/Unit/...</c> for a software unit); tag-table and watch-table groups use <c>folderPath</c>.
/// </summary>
public class PlcSourceFolderRequestInfo
{
    public string? PlcName { get; set; }

    /// <summary>
    /// Software unit owning the external sources or the tag-table group; null for the PLC itself. The software
    /// unit operations: the unit to create, delete or rename.
    /// </summary>
    public string? UnitName { get; set; }

    /// <summary>read_tag_comments: only this tag table (null: every table).</summary>
    public string? TableName { get; set; }

    /// <summary>External source name (import, generate, delete).</summary>
    public string? SourceName { get; set; }

    /// <summary>Absolute local file path: the source file to import, or the file export_source_file writes.</summary>
    public string? FilePath { get; set; }

    /// <summary>generate_blocks_from_source: <c>None</c> or <c>KeepOnError</c> (exact GenerateBlockOption member names).</summary>
    public string? GenerateOption { get; set; }

    /// <summary>generate_blocks_from_source: block group path or PLC data type group path; null generates into the scope root.</summary>
    public string? TargetGroupPath { get; set; }

    /// <summary>export_source_file: the blocks to generate the source from.</summary>
    public List<string>? BlockPaths { get; set; }

    /// <summary>export_source_file: the PLC data types to generate the source from.</summary>
    public List<string>? TypePaths { get; set; }

    /// <summary>export_source_file: GenerateOptions.WithDependencies instead of GenerateOptions.None.</summary>
    public bool WithDependencies { get; set; }

    /// <summary>export_source_file: replace an existing file.</summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// rename_block_group (a block group path), rename_type_group, create_type_group and delete_type_group (a PLC
    /// data type group path).
    /// </summary>
    public string? GroupPath { get; set; }

    /// <summary>
    /// The tag-table and watch-table group operations: the group as <c>/A/B</c>; read_tag_comments: only the
    /// tables in this folder (null: every folder).
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>The group renames and rename_software_unit: the new name.</summary>
    public string? NewName { get; set; }

    /// <summary>scan_accessible_devices: the exact connection mode name.</summary>
    public string? Mode { get; set; }

    /// <summary>scan_accessible_devices: the exact PC interface name.</summary>
    public string? PcInterfaceName { get; set; }

    /// <summary>scan_accessible_devices: the PC interface number.</summary>
    public int? PcInterfaceNumber { get; set; }

    /// <summary>scan_accessible_devices: device cap.</summary>
    public int? MaxDevices { get; set; }
}

/// <summary>Result of <c>list_external_sources</c>: the external sources of each PLC and of its software units.</summary>
public class PlcExternalSourceInventoryInfo
{
    public List<PlcExternalSourcePlcInfo> Plcs { get; set; } = new List<PlcExternalSourcePlcInfo>();

    /// <summary>False when a PLC, a software unit or a source could not be read (each is named in <see cref="Messages"/>).</summary>
    public bool Complete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();
}

public class PlcExternalSourcePlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public List<PlcExternalSourceInfo> Sources { get; set; } = new List<PlcExternalSourceInfo>();
}

/// <summary>One <c>PlcExternalSource</c> of the root "External source files" folder of the PLC or a software unit.</summary>
public class PlcExternalSourceInfo
{
    /// <summary><c>PlcExternalSource.Name</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The owning software unit; null for the PLC's own external sources.</summary>
    public string? UnitName { get; set; }
}

/// <summary>Result of <c>import_external_source</c> and <c>delete_external_source</c>.</summary>
public class PlcExternalSourceMutationInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    public string SourceName { get; set; } = string.Empty;

    /// <summary>import_external_source: the imported file; null for delete_external_source.</summary>
    public string? FilePath { get; set; }

    /// <summary>The read-back after the mutation: <c>Find(sourceName)</c> found the source (import) or no longer finds it (delete).</summary>
    public bool ReadBackConfirmed { get; set; }
}

/// <summary>Result of <c>generate_blocks_from_source</c>.</summary>
public class PlcSourceGenerationInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    public string SourceName { get; set; } = string.Empty;

    /// <summary>The GenerateBlockOption member that was passed (<c>None</c> or <c>KeepOnError</c>).</summary>
    public string GenerateOption { get; set; } = string.Empty;

    /// <summary>The user group passed to the call; null when the overload without a group generated into the scope root.</summary>
    public string? TargetGroupPath { get; set; }

    /// <summary>The objects GenerateBlocksFromSource returned, in its order.</summary>
    public List<PlcGeneratedObjectInfo> Generated { get; set; } = new List<PlcGeneratedObjectInfo>();
}

public class PlcGeneratedObjectInfo
{
    /// <summary>The object's Name; null when it could not be read.</summary>
    public string? Name { get; set; }

    /// <summary><c>OB</c>, <c>FB</c>, <c>FC</c>, <c>GlobalDB</c>, <c>InstanceDB</c>, <c>ArrayDB</c>, <c>Block</c>, <c>Type</c>, or the runtime type name of anything else.</summary>
    public string Kind { get; set; } = string.Empty;
}

/// <summary>Result of <c>export_source_file</c>.</summary>
public class PlcSourceFileExportInfo
{
    public string FilePath { get; set; } = string.Empty;

    /// <summary>True when an existing file was replaced (only with overwrite:true).</summary>
    public bool Overwritten { get; set; }

    public long Bytes { get; set; }

    /// <summary>SHA-256 (lower-case hex) of the file as read back from disk after the rename.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public bool WithDependencies { get; set; }

    public string PlcName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    /// <summary>The resolved blocks and PLC data types passed to GenerateSource, as their deterministic paths.</summary>
    public List<string> Objects { get; set; } = new List<string>();
}

/// <summary>Result of the four group renames.</summary>
public class PlcGroupRenameInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary><c>BlockGroup</c>, <c>TypeGroup</c>, <c>TagTableGroup</c> or <c>WatchTableGroup</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string? UnitName { get; set; }

    /// <summary>The group's path before the rename, in the request's convention.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The group's path after the rename.</summary>
    public string NewPath { get; set; } = string.Empty;

    public string OldName { get; set; } = string.Empty;

    /// <summary>The name read back with GetAttribute("Name") after the rename.</summary>
    public string NewName { get; set; } = string.Empty;
}

/// <summary>Result of create/delete of a PLC data type group, a tag table group or a software unit.</summary>
public class PlcContainerMutationInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary><c>TypeGroup</c>, <c>TagTableGroup</c> or <c>SoftwareUnit</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>The owning software unit of a group; for a software unit operation the unit itself.</summary>
    public string? UnitName { get; set; }

    /// <summary>The created or deleted object's path in the request's convention (<c>PLC/Types/A</c>, <c>/A</c>, <c>PLC/Units/U</c>).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The name of the created or deleted object (a create reports the name the created object reads back).</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Result of <c>read_tag_comments</c>: the tag tables of every PLC <c>plcName</c> selects (all when null) and of
/// their software units, with the filters of <c>list_tag_tables</c>.
/// </summary>
public class PlcTagCommentInventoryInfo
{
    /// <summary>False when a PLC, table, tag or comment could not be read; <see cref="Messages"/> says which.</summary>
    public bool Complete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<PlcTagCommentPlcInfo> Plcs { get; set; } = new List<PlcTagCommentPlcInfo>();
}

public class PlcTagCommentPlcInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public List<PlcTagTableCommentsInfo> Tables { get; set; } = new List<PlcTagTableCommentsInfo>();
}

public class PlcTagTableCommentsInfo
{
    /// <summary>The software unit owning the table; null for the PLC's own tag tables.</summary>
    public string? UnitName { get; set; }

    /// <summary>The table's folder as <c>/</c> or <c>/A/B</c>.</summary>
    public string FolderPath { get; set; } = "/";

    public string TableName { get; set; } = string.Empty;

    public List<PlcMemberCommentInfo> Members { get; set; } = new List<PlcMemberCommentInfo>();
}

/// <summary>One tag or user constant and its multilingual comment (<c>MultilingualText.Items</c>).</summary>
public class PlcMemberCommentInfo
{
    /// <summary><c>Tag</c> or <c>UserConstant</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>One entry per language item with a non-empty text, in the items' order; null when the comment was unreadable.</summary>
    public List<PlcCommentTextInfo>? Comment { get; set; }
}

public class PlcCommentTextInfo
{
    /// <summary>The item's <c>Language.Culture</c> name, e.g. <c>en-US</c>.</summary>
    public string Culture { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>Result of <c>scan_accessible_devices</c>.</summary>
public class PlcAccessibleDeviceScanInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public string PcInterfaceName { get; set; } = string.Empty;

    public int PcInterfaceNumber { get; set; }

    /// <summary>Devices GetAccessibleDevices() returned (all of them, also when <see cref="Truncated"/>).</summary>
    public int DeviceCount { get; set; }

    /// <summary>True when more devices were returned than the cap; the first ones in the returned order are listed.</summary>
    public bool Truncated { get; set; }

    public int MaxDevices { get; set; }

    public List<PlcAccessibleDeviceInfo> Devices { get; set; } = new List<PlcAccessibleDeviceInfo>();
}

/// <summary>One <c>ConfigurationAccessibleDevice</c>: the four documented members, as text (null when null).</summary>
public class PlcAccessibleDeviceInfo
{
    public string? Name { get; set; }

    public string? DeviceSeries { get; set; }

    public string? Address { get; set; }

    /// <summary><c>ConfigurationAccessibleDevice.MACAddress</c>.</summary>
    public string? MacAddress { get; set; }
}
