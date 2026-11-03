using TiaMcpServer.Contracts.Plc.Interface;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Parameters of one <see cref="PlcBlockEditMethods"/> worker call; only the fields of the method
/// are read. Travels in <c>WorkerRequest.PlcBlockEdit</c>.
/// </summary>
public class PlcBlockEditRequestInfo
{
    /// <summary>Block path (get_block_interface, get_db_values, update_db_member, rename/renumber/copy/move_block; the new block's path for create_instance_db).</summary>
    public string? BlockPath { get; set; }

    /// <summary>PLC data type path (get_type_interface).</summary>
    public string? TypePath { get; set; }

    /// <summary>Dotted member path (get_db_values narrowing, update_db_member).</summary>
    public string? MemberPath { get; set; }

    /// <summary>New block name (rename_block, copy_block).</summary>
    public string? NewName { get; set; }

    /// <summary>Requested block number (renumber_block, copy_block, create_instance_db); null means automatic.</summary>
    public int? Number { get; set; }

    /// <summary>renumber_block: true switches the block to automatic numbering.</summary>
    public bool? AutoNumber { get; set; }

    /// <summary>create_instance_db: name of the FB the instance DB belongs to.</summary>
    public string? InstanceOfName { get; set; }

    /// <summary>copy_block (optional) and move_block: deterministic block group path or 'PLC' root form.</summary>
    public string? TargetGroupPath { get; set; }

    /// <summary>move_tag_table: optional PLC software name.</summary>
    public string? PlcName { get; set; }

    /// <summary>move_tag_table: tag table name.</summary>
    public string? TableName { get; set; }

    /// <summary>move_tag_table: source folder ("/" for the root).</summary>
    public string? FolderPath { get; set; }

    /// <summary>move_tag_table: target folder ("/" for the root).</summary>
    public string? TargetFolderPath { get; set; }

    /// <summary>update_db_member: start value literal, passed to Member.SetAttribute("StartValue") as text.</summary>
    public string? StartValue { get; set; }

    public bool? ExternalAccessible { get; set; }

    public bool? ExternalVisible { get; set; }

    public bool? ExternalWritable { get; set; }

    public bool? Retain { get; set; }

    public bool? SetPoint { get; set; }

    /// <summary>get_db_values: also read the documented member attributes of every value.</summary>
    public bool? WithAttributes { get; set; }

    /// <summary>get_db_snapshot, create_db_snapshot: the most values returned (default and cap in <see cref="Interface.PlcDbSnapshotDocument"/>).</summary>
    public int? MaxElements { get; set; }

    /// <summary>rename_tags, rename_objects: the renames by name.</summary>
    public List<PlcNameRenameInfo>? Renames { get; set; }

    /// <summary>rename_tags, rename_objects: the stamp of the temporary names (fresh per attempt, host-generated).</summary>
    public string? TemporaryStamp { get; set; }

    /// <summary>replace_tag_comments: the find/replace pairs.</summary>
    public List<Interface.PlcTextReplacementInfo>? Replacements { get; set; }

    /// <summary>replace_tag_comments: only this language; null replaces in every language.</summary>
    public string? Culture { get; set; }

    /// <summary>export_tag_table / import_tag_table: the absolute drive or UNC share path of the SimaticML file.</summary>
    public string? FilePath { get; set; }

    /// <summary>export_tag_table: replace an existing file; import_tag_table: <c>ImportOptions.Override</c>; create_block_from_document: replace a block of the same name.</summary>
    public bool? Overwrite { get; set; }

    /// <summary>create_block_from_document: the sd bundle (.s7dcl and .s7res documents).</summary>
    public string? Content { get; set; }
}

/// <summary>Identity of one PLC block as the worker resolved it.</summary>
public class PlcBlockIdentityInfo
{
    public string PlcName { get; set; } = string.Empty;

    /// <summary>Canonical deterministic path, e.g. PLC_1/Blocks/Folder/Name or PLC_1/Units/U/Blocks/Name.</summary>
    public string BlockPath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>OB, FB, FC, GlobalDB, InstanceDB, ArrayDB, or the Openness type name of another block kind.</summary>
    public string BlockType { get; set; } = string.Empty;

    public int? Number { get; set; }

    /// <summary>GetAttribute("AutoNumber"); null when unreadable.</summary>
    public bool? AutoNumber { get; set; }

    public string? ProgrammingLanguage { get; set; }

    /// <summary>PlcBlock.IsKnowHowProtected; null when unreadable.</summary>
    public bool? IsKnowHowProtected { get; set; }

    /// <summary>GetAttribute("InstanceOfName") of an instance DB; null for other blocks or when unreadable.</summary>
    public string? InstanceOfName { get; set; }
}

/// <summary>get_block_interface: identity, the hash of the matching get_block_content (xml) read and the parsed interface.</summary>
public class PlcBlockInterfaceInfo
{
    public PlcBlockIdentityInfo Block { get; set; } = new PlcBlockIdentityInfo();

    public string Format { get; set; } = "xml";

    /// <summary>Equal to the contentHash a get_block_content xml read of the same state returns.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>SimaticML object element, e.g. SW.Blocks.FB; null when the bundle had no SimaticML document.</summary>
    public string? DocumentElement { get; set; }

    /// <summary>True when the SimaticML ProgrammingLanguage starts with F_ (a fail-safe block).</summary>
    public bool IsSafety { get; set; }

    public List<PlcInterfaceSectionInfo> Sections { get; set; } = new List<PlcInterfaceSectionInfo>();

    public List<string> Messages { get; set; } = new List<string>();
}

/// <summary>get_type_interface: the PLC data type's identity, xml content hash and members (section None).</summary>
public class PlcTypeInterfaceInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string TypePath { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Format { get; set; } = "xml";

    /// <summary>Equal to the contentHash a get_type_content xml read of the same state returns.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public string? DocumentElement { get; set; }

    public List<PlcInterfaceSectionInfo> Sections { get; set; } = new List<PlcInterfaceSectionInfo>();

    public List<string> Messages { get; set; } = new List<string>();
}

/// <summary>get_db_values: start values of a global or instance DB, one entry per value path.</summary>
public class PlcDbValuesInfo
{
    public PlcBlockIdentityInfo Block { get; set; } = new PlcBlockIdentityInfo();

    /// <summary>The requested narrowing path, or null for the whole DB.</summary>
    public string? MemberPath { get; set; }

    /// <summary>False when the entry cap was reached, a type could not be expanded, or a value was unreadable.</summary>
    public bool IsComplete { get; set; }

    public List<string> Messages { get; set; } = new List<string>();

    public List<PlcDbValueInfo> Values { get; set; } = new List<PlcDbValueInfo>();
}

public class PlcDbValueInfo
{
    /// <summary>Path as passed to Interface.Members.Find, e.g. Motor.Speed or Arr[3].</summary>
    public string Path { get; set; } = string.Empty;

    public string? DataType { get; set; }

    /// <summary>value (a start value was read), unexpanded (structured member whose children could not be enumerated), or missing (Members.Find returned null).</summary>
    public string Kind { get; set; } = PlcDbValueKinds.Value;

    public string? StartValue { get; set; }

    /// <summary>Why the value or its children could not be read; null when read.</summary>
    public string? Message { get; set; }

    /// <summary>The documented member attributes when withAttributes was requested; otherwise null.</summary>
    public PlcDbMemberAttributesInfo? Attributes { get; set; }
}

public static class PlcDbValueKinds
{
    public const string Value = "value";
    public const string Unexpanded = "unexpanded";
    public const string Missing = "missing";
}

/// <summary>Member attributes documented as read/write on DB members (V21 import/export reference §9); null when unreadable.</summary>
public class PlcDbMemberAttributesInfo
{
    public bool? ExternalAccessible { get; set; }

    public bool? ExternalVisible { get; set; }

    public bool? ExternalWritable { get; set; }

    public bool? Retain { get; set; }

    public bool? SetPoint { get; set; }
}

/// <summary>update_db_member: every requested attribute with its value before, requested and observed after.</summary>
public class PlcDbMemberUpdateResultInfo
{
    public string Operation { get; set; } = PlcBlockEditMethods.UpdateDbMember;

    public PlcBlockIdentityInfo Block { get; set; } = new PlcBlockIdentityInfo();

    public string MemberPath { get; set; } = string.Empty;

    public List<PlcDbAttributeChangeInfo> Changes { get; set; } = new List<PlcDbAttributeChangeInfo>();
}

public class PlcDbAttributeChangeInfo
{
    /// <summary>StartValue, ExternalAccessible, ExternalVisible, ExternalWritable, Retain or SetPoint.</summary>
    public string Attribute { get; set; } = string.Empty;

    public string? Previous { get; set; }

    public string Requested { get; set; } = string.Empty;

    public string? Observed { get; set; }
}

/// <summary>Result of create_instance_db, rename_block, renumber_block, copy_block and move_block.</summary>
public class PlcBlockEditResultInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary>The source block before the operation; null for create_instance_db.</summary>
    public string? SourceBlockPath { get; set; }

    /// <summary>The resulting block as read back after the operation.</summary>
    public PlcBlockIdentityInfo Block { get; set; } = new PlcBlockIdentityInfo();

    public List<string> Messages { get; set; } = new List<string>();

    /// <summary>
    /// create_block_from_document only: present when the document replaced the main safety block of an F-runtime group and
    /// therefore went through a temporary F-block and a SimaticML import (route sdViaXmlForMainSafetyBlock, live 2026-11-02).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Safety.PlcMainSafetyBlockRouteInfo? MainSafetyBlockRoute { get; set; }
}

/// <summary>Result of move_tag_table: the table as read back in its target folder.</summary>
public class PlcTagTableMoveResultInfo
{
    public string Operation { get; set; } = PlcBlockEditMethods.MoveTagTable;

    public string PlcName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string SourceFolderPath { get; set; } = string.Empty;

    public string TargetFolderPath { get; set; } = string.Empty;

    public int TagCount { get; set; }

    public int UserConstantCount { get; set; }
}

/// <summary>
/// Evidence of a failed move_block, move_tag_table or copy_block: where the original is after the
/// failure and, when it is no longer in the project, where its export was kept. The worker sends it
/// with the failure; plc_write returns it as the failed item's result.
/// </summary>
public class PlcBlockEditFailureInfo
{
    public string Operation { get; set; } = string.Empty;

    /// <summary><c>block</c> or <c>tagTable</c>.</summary>
    public string ObjectKind { get; set; } = string.Empty;

    public string ObjectName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string? TargetPath { get; set; }

    /// <summary>One of <see cref="PlcBlockEditTransactionStates"/>.</summary>
    public string Transaction { get; set; } = PlcBlockEditTransactionStates.RolledBack;

    /// <summary>One of <see cref="PlcBlockEditOriginalStates"/>.</summary>
    public string OriginalState { get; set; } = PlcBlockEditOriginalStates.Unchanged;

    /// <summary>The kept export when the original is not in the project any more; null otherwise.</summary>
    public PlcRecoveryFileInfo? Recovery { get; set; }

    /// <summary>Why no recovery file could be written; null when none was needed or it was written.</summary>
    public string? RecoveryError { get; set; }

    public List<string> Messages { get; set; } = new();
}

public static class PlcBlockEditTransactionStates
{
    /// <summary>The ExclusiveAccess transaction was disposed without a commit, so TIA Portal rolled it back.</summary>
    public const string RolledBack = "rolled_back";

    /// <summary>Disposing the transaction itself failed; whether TIA Portal committed is unknown.</summary>
    public const string OutcomeUnknown = "outcome_unknown";
}

public static class PlcBlockEditOriginalStates
{
    /// <summary>The original is in its source location (the rollback restored it, or nothing had changed).</summary>
    public const string Unchanged = "unchanged";

    /// <summary>The rollback did not bring it back; it was re-imported from its export into the source location.</summary>
    public const string RestoredByReimport = "restored_by_reimport";

    /// <summary>The original is no longer in the project; <see cref="PlcBlockEditFailureInfo.Recovery"/> names its export.</summary>
    public const string Missing = "missing";

    /// <summary>The object is in the target and not in the source although the call failed (the transaction outcome was unknown).</summary>
    public const string MovedToTarget = "moved_to_target";

    /// <summary>Where the object is could not be read; its export was kept and it was not re-imported.</summary>
    public const string Unknown = "unknown";

    /// <summary>copy_block: the copy is not in the project; the source was never changed.</summary>
    public const string CopyAbsent = "copy_absent";

    /// <summary>copy_block: a copy is in the target despite the failure (inspect it).</summary>
    public const string CopyPresent = "copy_present";
}

/// <summary>A recovery folder: the object's SimaticML export and a JSON sidecar describing it.</summary>
public class PlcRecoveryFileInfo
{
    public string Directory { get; set; } = string.Empty;

    /// <summary>The SimaticML export; import it with Portal's import function or plc_write.</summary>
    public string DocumentPath { get; set; } = string.Empty;

    /// <summary>The sidecar JSON with the original name, kind, locations, time and failure; null when only the document could be written.</summary>
    public string? MetadataPath { get; set; }

    /// <summary><c>localAppData</c> (the default, beside the audit folder) or <c>temp</c> (fallback when that folder is not writable).</summary>
    public string Location { get; set; } = string.Empty;
}
