using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Contracts.Library;

/// <summary>One library as the tools address it.</summary>
public class LibraryRefInfo
{
    /// <summary>The selector: <c>project</c>, or the global library's <see cref="Path"/>.</summary>
    public string Library { get; set; } = string.Empty;

    /// <summary><c>project</c>, <c>user</c>, <c>system</c>, <c>corporate</c> or <c>global</c> (another specialization).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Global library file path (<c>GlobalLibrary.Path</c>); null for the project library.</summary>
    public string? Path { get; set; }

    /// <summary>The library name when readable (runtime lookup); null otherwise.</summary>
    public string? Name { get; set; }

    /// <summary>Runtime type name of the library object.</summary>
    public string? RuntimeType { get; set; }
}

/// <summary>Result of <c>list_libraries</c>: the project library and every open global library.</summary>
public class LibraryListInfo
{
    public List<LibraryRefInfo> Libraries { get; set; } = new List<LibraryRefInfo>();

    /// <summary>Members that could not be read (for example a global library without a readable path).</summary>
    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>One <c>GlobalLibraryInfo</c> from <c>GlobalLibraryComposition.GetGlobalLibraryInfos()</c>.</summary>
public class GlobalLibraryDescriptorInfo
{
    /// <summary><c>GlobalLibraryInfo.LibraryType</c> by name (<c>System</c>, <c>Corporate</c>, <c>User</c>).</summary>
    public string? LibraryType { get; set; }

    public string? Name { get; set; }

    public string? Path { get; set; }

    /// <summary>True when <see cref="Path"/> equals the path of an open global library.</summary>
    public bool IsOpen { get; set; }
}

/// <summary>Result of <c>list_global_library_infos</c>.</summary>
public class GlobalLibraryDescriptorListInfo
{
    public List<GlobalLibraryDescriptorInfo> Infos { get; set; } = new List<GlobalLibraryDescriptorInfo>();

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>A version as listed in trees and type details.</summary>
public class LibraryVersionSummaryInfo
{
    /// <summary><c>LibraryTypeVersion.VersionNumber</c> as text (runtime lookup).</summary>
    public string? VersionNumber { get; set; }

    public string? Guid { get; set; }

    /// <summary><c>LibraryTypeVersionState</c> by name (<c>InWork</c>, <c>Committed</c>).</summary>
    public string? State { get; set; }

    /// <summary><c>LibraryTypeVersion.IsDefault</c> (runtime lookup); null when unreadable.</summary>
    public bool? IsDefault { get; set; }
}

/// <summary>One entry of a library tree page.</summary>
public class LibraryTreeEntryInfo
{
    /// <summary><c>typeFolder</c>, <c>type</c> or <c>masterCopy</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Slash path below the type folder root (<c>/A/B/Type</c>), or <c>/Name</c> for a root master copy.</summary>
    public string Path { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>Depth below the listed folder (its direct children are 1).</summary>
    public int Depth { get; set; }

    /// <summary>Type GUID; null for folders and master copies.</summary>
    public string? Guid { get; set; }

    /// <summary>Types: <c>codeBlock</c>, <c>plcType</c> or <c>other</c>; null otherwise.</summary>
    public string? TypeKind { get; set; }

    public string? RuntimeType { get; set; }

    /// <summary>Types with versions included: every version; null otherwise.</summary>
    public List<LibraryVersionSummaryInfo>? Versions { get; set; }

    /// <summary>Master copies: <c>ContentDescriptions[].ContentType</c> as text; null otherwise.</summary>
    public List<string>? ContentTypes { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>Result of <c>get_library_tree</c>: one offset/limit page of the flattened tree.</summary>
public class LibraryTreeInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    public string FolderPath { get; set; } = "/";

    public string Content { get; set; } = LibraryPolicy.TreeContents.All;

    public int MaxDepth { get; set; }

    public int Offset { get; set; }

    public int Limit { get; set; }

    /// <summary>Entries collected by the walk (before paging).</summary>
    public int Total { get; set; }

    public int Returned { get; set; }

    /// <summary>False when the depth cap or the traversal cap stopped the walk.</summary>
    public bool Complete { get; set; }

    /// <summary>Why the walk is incomplete; null when complete.</summary>
    public string? Omission { get; set; }

    /// <summary>
    /// <c>all</c>: master copies of the root master copy folder and of its user folders (<c>MasterCopyFolder.Folders</c>, V21) are
    /// listed, each folder as a <c>masterCopyFolder</c> entry; <c>masterCopyFolderPath</c> starts the master copy walk below a folder.
    /// </summary>
    public string MasterCopyScope { get; set; } = "all";

    public List<LibraryTreeEntryInfo> Entries { get; set; } = new List<LibraryTreeEntryInfo>();
}

/// <summary>A reference from one version to another (dependencies, dependents).</summary>
public class LibraryVersionRefInfo
{
    public string? TypeName { get; set; }

    public string? VersionNumber { get; set; }

    public string? Guid { get; set; }
}

public class LibraryVersionDetailInfo : LibraryVersionSummaryInfo
{
    public List<LibraryVersionRefInfo> Dependencies { get; set; } = new List<LibraryVersionRefInfo>();

    public List<LibraryVersionRefInfo> Dependents { get; set; } = new List<LibraryVersionRefInfo>();

    public List<string> MasterCopiesContainingInstances { get; set; } = new List<string>();

    /// <summary><c>OriginalLibrary</c> as text when readable; null otherwise.</summary>
    public string? OriginalLibrary { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>Result of <c>get_type</c>.</summary>
public class LibraryTypeDetailInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    /// <summary>The type's path below the type folder root, when it was addressed or found by path; null for a GUID lookup.</summary>
    public string? Path { get; set; }

    public string? Name { get; set; }

    public string? Guid { get; set; }

    public string? TypeKind { get; set; }

    public string? RuntimeType { get; set; }

    public List<LibraryVersionDetailInfo> Versions { get; set; } = new List<LibraryVersionDetailInfo>();

    /// <summary><c>LibraryType.DoNotUse</c> (V21: "Gets or sets"); null when unreadable.</summary>
    public bool? DoNotUse { get; set; }

    /// <summary><c>LibraryType.SetForUpdate</c>; null when unreadable.</summary>
    public bool? SetForUpdate { get; set; }

    /// <summary><c>LibraryType.Status</c> (consistency state) as text; null when unreadable.</summary>
    public string? Status { get; set; }

    /// <summary><c>LibraryType.Namespace</c>; null when unreadable.</summary>
    public string? Namespace { get; set; }

    /// <summary><c>LibraryType.Author</c>; null when unreadable.</summary>
    public string? Author { get; set; }

    /// <summary><c>LibraryType.Comment</c> as text; null when unreadable.</summary>
    public string? Comment { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>get_supported_export_formats: <c>LibraryType.GetSupportedExportFormats()</c>.</summary>
public class LibraryExportFormatsInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    public string? Path { get; set; }

    public string? Name { get; set; }

    public string? Guid { get; set; }

    public List<string> Formats { get; set; } = new List<string>();
}

/// <summary>One <c>DetailedCompareResultElement</c>.</summary>
public class LibraryDetailedCompareEntryInfo
{
    public string? Description { get; set; }

    /// <summary><c>DetailCompareStatus</c> as text (<c>ObjectsIdentical</c>, <c>ObjectsDifferent</c>, <c>NotCompared</c>).</summary>
    public string? Status { get; set; }

    public string? LeftValue { get; set; }

    public string? RightValue { get; set; }
}

/// <summary>compare_types, compare_type_versions, compare_master_copies: <c>CompareTo(...)</c> → <c>DetailedCompareResult.Properties</c>.</summary>
public class LibraryDetailedCompareInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    public string Left { get; set; } = string.Empty;

    public LibraryRefInfo CompareTo { get; set; } = new LibraryRefInfo();

    public string Right { get; set; } = string.Empty;

    public List<LibraryDetailedCompareEntryInfo> Properties { get; set; } = new List<LibraryDetailedCompareEntryInfo>();

    /// <summary>True when every property was listed (at most <see cref="LibraryPolicy.MaxCompareElements"/>).</summary>
    public bool Complete { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>One TIA Portal setting (<c>TiaPortalSetting.Name</c>, <c>.Value</c>).</summary>
public class PortalSettingInfo
{
    /// <summary>The folder path (<c>A/B</c>) of the setting.</summary>
    public string FolderPath { get; set; } = string.Empty;

    public string? Name { get; set; }

    /// <summary>The value as invariant text (an enum by member name); null when the value is null or unreadable.</summary>
    public string? Value { get; set; }

    /// <summary>The runtime type of the value (the XML documents none); null when null or unreadable.</summary>
    public string? ValueType { get; set; }

    /// <summary>The members of an enum value, so a caller can choose one.</summary>
    public List<string>? EnumMembers { get; set; }

    /// <summary>Whether the live <c>Value</c> property has a public setter.</summary>
    public bool Writable { get; set; }

    /// <summary>True when the folder or name mentions security, certificates, communication, encryption, passwords or trust: changing it needs acknowledgement.</summary>
    public bool SecurityRelevant { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>list_portal_settings: the settings tree below <c>TiaPortal.SettingsFolders</c> (depth-first, folder by folder).</summary>
public class PortalSettingsInfo
{
    public string FolderPath { get; set; } = string.Empty;

    public List<string> Folders { get; set; } = new List<string>();

    public List<PortalSettingInfo> Settings { get; set; } = new List<PortalSettingInfo>();

    public int MaxDepth { get; set; }

    public bool Complete { get; set; } = true;

    public string? Omission { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>One project the TIA Portal has open as a secondary (read-only reference) project.</summary>
public class SecondaryProjectInfo
{
    public string? Path { get; set; }

    public string? Name { get; set; }

    /// <summary><c>ProjectBase.IsPrimary</c> as read (false for a secondary project; null when unreadable).</summary>
    public bool? IsPrimary { get; set; }
}

/// <summary>list_secondary_projects: the projects of <c>TiaPortal.Projects</c> whose <c>ProjectBase.IsPrimary</c> reads false.</summary>
public class SecondaryProjectListInfo
{
    public List<SecondaryProjectInfo> Projects { get; set; } = new List<SecondaryProjectInfo>();

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>A file a library export wrote, re-hashed by the worker.</summary>
public class LibraryFileInfo
{
    public string Path { get; set; } = string.Empty;

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
/// What a document transfer reported: <c>TypeCreateTransferResults</c>, <c>VersionCreateTransferResults</c> or
/// <c>ExportTransferResult</c> (state and messages), and for exports the files found afterwards.
/// </summary>
public class LibraryTransferInfo
{
    /// <summary><c>TransferResultState</c> as text (<c>Success</c>, <c>Warning</c>); null when unreadable.</summary>
    public string? State { get; set; }

    public List<string> Messages { get; set; } = new List<string>();

    /// <summary>ExportTransferResult.ExportedDocuments as reported (text).</summary>
    public List<string> Documents { get; set; } = new List<string>();

    /// <summary>Files written, re-hashed after the call.</summary>
    public List<LibraryFileInfo> Files { get; set; } = new List<LibraryFileInfo>();

    public List<string> Unreadable { get; set; } = new List<string>();
}

public class LibraryInstanceInfo
{
    public string? Name { get; set; }

    public string? RuntimeType { get; set; }
}

public class LibraryVersionInstancesInfo
{
    public string? VersionNumber { get; set; }

    public string? Guid { get; set; }

    public List<LibraryInstanceInfo> Instances { get; set; } = new List<LibraryInstanceInfo>();

    /// <summary>False when the instance cap stopped the listing.</summary>
    public bool Complete { get; set; }
}

/// <summary>Result of <c>find_type_instances</c> (<c>LibraryTypeVersion.FindInstances(IInstanceSearchScope)</c> on one PLC).</summary>
public class LibraryTypeInstancesInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    public string? TypeName { get; set; }

    public string? TypeGuid { get; set; }

    public string PlcName { get; set; } = string.Empty;

    public string? DeviceName { get; set; }

    public List<LibraryVersionInstancesInfo> Versions { get; set; } = new List<LibraryVersionInstancesInfo>();
}

/// <summary>Result of <c>get_instance_info</c> (<c>GetService&lt;LibraryTypeInstanceInfo&gt;()</c>).</summary>
public class LibraryInstanceLinkInfo
{
    /// <summary>The PLC the path names; null for a bare block or type name (resolved in the only PLC).</summary>
    public string? PlcName { get; set; }

    /// <summary><c>block</c> or <c>plcType</c>.</summary>
    public string ObjectKind { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    /// <summary>False when the object exposes no <c>LibraryTypeInstanceInfo</c> (not a type instance, e.g. placed from a master copy).</summary>
    public bool Linked { get; set; }

    public string? TypeName { get; set; }

    public string? TypeGuid { get; set; }

    public string? VersionNumber { get; set; }

    public string? VersionGuid { get; set; }

    public string? VersionState { get; set; }

    public List<string> Unreadable { get; set; } = new List<string>();
}

/// <summary>One recursive <c>UpdateCheckResultMessage</c>.</summary>
public class LibraryMessageInfo
{
    public string? Description { get; set; }

    /// <summary><c>MessageParts</c> projected to text (dictionary entries as <c>key: value</c>).</summary>
    public List<string> MessageParts { get; set; } = new List<string>();

    public List<LibraryMessageInfo> Messages { get; set; } = new List<LibraryMessageInfo>();
}

/// <summary>Result of <c>update_check</c>.</summary>
public class LibraryUpdateCheckInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    public string Mode { get; set; } = string.Empty;

    public List<LibraryMessageInfo> Messages { get; set; } = new List<LibraryMessageInfo>();

    public int MessageCount { get; set; }

    /// <summary>False when the depth or message cap stopped the projection.</summary>
    public bool Complete { get; set; }
}

public class LibraryAttributeInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }
}

public class LibraryCompareDetailInfo
{
    public List<LibraryAttributeInfo> Attributes { get; set; } = new List<LibraryAttributeInfo>();
}

/// <summary>One <c>LibraryCompareResultElement</c>, projected schema-free.</summary>
public class LibraryCompareElementInfo
{
    /// <summary><c>ComparisonResult</c> (<c>LibraryCompareResultState</c>) by name.</summary>
    public string? ComparisonResult { get; set; }

    /// <summary>Readable scalar attributes of the element (<c>GetAttributeInfos</c>/<c>GetAttribute</c>).</summary>
    public List<LibraryAttributeInfo> Attributes { get; set; } = new List<LibraryAttributeInfo>();

    /// <summary><c>DetailedInformation.Properties</c>, each projected schema-free.</summary>
    public List<LibraryCompareDetailInfo> Details { get; set; } = new List<LibraryCompareDetailInfo>();

    public List<LibraryCompareElementInfo> Elements { get; set; } = new List<LibraryCompareElementInfo>();

    public bool ElementsTruncated { get; set; }
}

/// <summary>Result of <c>compare_libraries</c> (<c>CompareToLibrary(ISoftwareCompareTarget)</c>).</summary>
public class LibraryCompareInfo
{
    public LibraryRefInfo Library { get; set; } = new LibraryRefInfo();

    public LibraryRefInfo CompareTo { get; set; } = new LibraryRefInfo();

    public int MaxDepth { get; set; }

    public int MaxElements { get; set; }

    public LibraryCompareElementInfo? Root { get; set; }

    public int ElementsListed { get; set; }

    public bool Complete { get; set; }

    public string? Omission { get; set; }
}

/// <summary>Planning evidence of one <c>library_write</c> item (<c>library_plan</c>, no mutation).</summary>
public class LibraryPlanInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary>The resolved library; null when the item opens, retrieves or creates one.</summary>
    public LibraryRefInfo? Library { get; set; }

    /// <summary>What the item acts on, resolved (e.g. <c>type /Motors/Pump version 1.0.1</c>).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>open_global_library: the file is already open as a global library (no Open call is made).</summary>
    public bool AlreadyOpen { get; set; }

    /// <summary>Readable consequences: dependents, instances, contents, files written.</summary>
    public List<string> Consequences { get; set; } = new List<string>();

    /// <summary>
    /// Present only for set_portal_setting: the setting as the worker resolved it, with the folder and setting names TIA Portal
    /// reports (not the caller's text); the host classifies the item from these names (security review F2).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PortalSettingInfo? Setting { get; set; }
}

public class LibraryCreatedObjectInfo
{
    public string? Name { get; set; }

    public string? RuntimeType { get; set; }
}

/// <summary>Result of every <c>library_write</c> mutation.</summary>
public class LibraryWriteResultInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary>The library acted on (for open/retrieve/create the library afterwards).</summary>
    public LibraryRefInfo? Library { get; set; }

    public string Target { get; set; } = string.Empty;

    /// <summary>Objects the call returned (master copies, instances, devices, an in-work version).</summary>
    public List<LibraryCreatedObjectInfo> Created { get; set; } = new List<LibraryCreatedObjectInfo>();

    /// <summary>rename_*: the name read back after the write.</summary>
    public string? NameAfter { get; set; }

    /// <summary>Version operations: the addressed (or, for edit, the returned) version read back; null otherwise or after delete.</summary>
    public LibraryVersionSummaryInfo? VersionAfter { get; set; }

    /// <summary>open_global_library: the file was already open; nothing was called.</summary>
    public bool AlreadyOpen { get; set; }

    /// <summary>Runtime type of a value the Siemens call returned whose type is undocumented; null when nothing was returned.</summary>
    public string? ReturnedType { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Present only when the worker could not read back the postcondition the item's Siemens call should establish (an
    /// undocumented or unreadable member, an object that could not be read again, a <c>CreateFrom</c> bound without a return
    /// value): why. The item still succeeds (the call returned without rejection), but its verification entry is
    /// <c>notVerified</c> (success false), never <c>trusted</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Unverified { get; set; }

    /// <summary>
    /// Present only for the document and file operations (create_type_from_documents, create_version_from_documents,
    /// export_type_version, export_type_version_as_documents, archive_global_library, save_global_library_as): the transfer state
    /// and messages TIA Portal reported, and the files the worker found and hashed afterwards.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LibraryTransferInfo? Transfer { get; set; }

    /// <summary>Present only for set_portal_setting: the setting before and after (read back).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PortalSettingChangeInfo? Setting { get; set; }

    /// <summary>Present only for the secondary-project items: the project opened or closed, read back from <c>TiaPortal.Projects</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecondaryProjectInfo? SecondaryProject { get; set; }

    /// <summary>
    /// The <c>portalConfirmations</c> policy the item ran under (<c>decline</c> or <c>accept</c>): every TIA Portal
    /// <c>Confirmation</c> raised while the mutation ran was answered by it (plc_download's rule).
    /// </summary>
    public string PortalConfirmationPolicy { get; set; } = PlcDownloadPolicy.PortalConfirmationPolicies.Decline;

    /// <summary>The confirmations TIA Portal raised while the item ran, with the answer given; at most <see cref="PlcDownloadPolicy.MaxPortalConfirmations"/> listed.</summary>
    public List<PlcDownloadPortalConfirmationInfo> PortalConfirmations { get; set; } = new List<PlcDownloadPortalConfirmationInfo>();

    /// <summary>Every confirmation observed while the item ran, listed or not.</summary>
    public int PortalConfirmationsObserved { get; set; }

    /// <summary>True when every observed confirmation is listed.</summary>
    public bool PortalConfirmationsComplete { get; set; } = true;
}

/// <summary>set_portal_setting: the setting's value before and after the write.</summary>
public class PortalSettingChangeInfo
{
    public string FolderPath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Before { get; set; }

    public string? After { get; set; }

    public string? ValueType { get; set; }

    public bool SecurityRelevant { get; set; }
}
