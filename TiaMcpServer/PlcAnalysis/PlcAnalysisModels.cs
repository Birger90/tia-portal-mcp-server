namespace TiaMcpServer.PlcAnalysis;

/// <summary>
/// A program object in an analysis result. <see cref="Kind"/> is the project-tree node type
/// (OB, FB, FC, GlobalDB, InstanceDB, ArrayDB, Block) or Type; null with <see cref="InInventory"/>
/// false for an object the cross-references name but the PLC's block and type inventory does not
/// contain (an instruction, a system type, a technology object, an object of another device).
/// <see cref="Path"/> is the deterministic blockPath/typePath for plc_read, null for system blocks.
/// </summary>
public sealed record PlcAnalysisObjectRef(string Name, string? Kind, string? Path, bool InInventory, bool IsSystemBlock);

/// <summary>One cross-reference location kept as evidence: its access, reference type and where it is.</summary>
public sealed record PlcAnalysisLocation(string Access, string ReferenceType, string At);

/// <summary>
/// The evidence behind one directed relationship. <see cref="ReportedAs"/> says from which side the
/// cross-reference result stated it: <c>uses</c> (the user's own result, referenceType Uses) and/or
/// <c>usedBy</c> (the used object's result, referenceType UsedBy). <see cref="LocationCount"/> counts
/// location records; a location reported from both sides is counted twice.
/// </summary>
public sealed record PlcAnalysisEdgeEvidence(
    IReadOnlyList<string> Relations,
    IReadOnlyList<string> Accesses,
    IReadOnlyList<string> ReportedAs,
    int LocationCount,
    IReadOnlyList<PlcAnalysisLocation> Locations);

/// <summary>A pair whose locations carry a referenceType other than Uses/UsedBy: the direction is not stated, so it is never traversed.</summary>
public sealed record PlcUndirectedEvidence(
    string First,
    string Second,
    string Access,
    string ReferenceType,
    int LocationCount,
    IReadOnlyList<PlcAnalysisLocation> Locations);

/// <summary>An object outside the block/type inventory (HMI object, other device, tag, instruction) related to a block.</summary>
public sealed record PlcExternalObject(string Name, string TypeName, string Device, string Access);

/// <summary>One cross-reference query the analysis ran, with the reader's completeness accounting.</summary>
public sealed record PlcAnalysisQueryEvidence(
    string Target,
    string Filter,
    bool IsComplete,
    int OwnerQueryCount,
    int SuccessfulOwnerQueryCount,
    int SourceCount,
    IReadOnlyList<string> Messages);

/// <summary>
/// What the analysis was computed from and how far it can be trusted. <see cref="Complete"/> is false
/// when the inventory skipped nodes, a query was incomplete, a top-level source matched no inventory
/// object or several, or a reference name matched several objects.
/// </summary>
public sealed record PlcAnalysisEvidence(
    string PlcName,
    string DeviceName,
    int InventoryBlockCount,
    int InventoryTypeCount,
    int InventorySkippedNodeCount,
    IReadOnlyList<PlcAnalysisQueryEvidence> CrossReferenceQueries,
    int MatchedSourceCount,
    int UnmatchedSourceCount,
    IReadOnlyList<string> UnmatchedSources,
    int AmbiguousReferenceCount,
    IReadOnlyList<string> AmbiguousNames,
    int DirectedEdgeCount,
    int UndirectedEvidenceCount,
    bool Complete,
    IReadOnlyList<string> Rules,
    IReadOnlyList<string> Limits);

public sealed record PlcCallTreeNode(
    int Id,
    int? ParentId,
    int Depth,
    PlcAnalysisObjectRef Block,
    PlcAnalysisEdgeEvidence? Evidence,
    IReadOnlyList<PlcInstanceUse> Instances,
    bool Cycle,
    bool ExpandedElsewhere,
    bool ChildrenOmitted);

/// <summary>An instance-type use of the node: access InstanceDB/InstanceAndSymbol (instance) or Parameterinstance(AndSymbol).</summary>
public sealed record PlcInstanceUse(PlcAnalysisObjectRef Block, PlcAnalysisEdgeEvidence Evidence);

public sealed record PlcCallTreeResult(
    PlcAnalysisObjectRef Root,
    int MaxDepth,
    int MaxElements,
    bool Complete,
    IReadOnlyList<string> Truncation,
    int NodeCount,
    IReadOnlyList<PlcCallTreeNode> Nodes,
    IReadOnlyList<PlcUndirectedEvidence> UndirectedCallEvidence,
    PlcAnalysisEvidence Evidence);

/// <summary>One path from an OB to the target: <see cref="Relations"/>[i] joins the relations of the hop Blocks[i] to Blocks[i+1] with '+'.</summary>
public sealed record PlcCallPath(IReadOnlyList<string> Blocks, IReadOnlyList<string> Relations);

public sealed record PlcCallPathsResult(
    PlcAnalysisObjectRef Target,
    int MaxDepth,
    int MaxElements,
    bool Complete,
    IReadOnlyList<string> Truncation,
    int PathCount,
    IReadOnlyList<PlcCallPath> Paths,
    IReadOnlyList<string> Roots,
    IReadOnlyList<PlcAnalysisObjectRef> DirectCallers,
    IReadOnlyList<PlcUndirectedEvidence> UndirectedEvidence,
    PlcAnalysisEvidence Evidence);

/// <summary>
/// A block not reachable from any OB. Category <c>unreferenced</c>: no directed reference into it at
/// all; <c>referencedOnlyByUnreachableBlocks</c>: only unreachable blocks reference it (listed in
/// <see cref="ReferencedBy"/>); <c>usedOnlyOutsideProgram</c>: no reachable block references it but
/// objects outside the inventory do (<see cref="ExternalUsers"/>); <c>undetermined</c>: it shares
/// undirected evidence with a reachable block, or an undetermined block leads to it (over a directed
/// relation or further undirected evidence), so reachability cannot be decided. Any undetermined block
/// makes the result incomplete.
/// </summary>
public sealed record PlcUnreachableBlock(
    PlcAnalysisObjectRef Block,
    string Category,
    IReadOnlyList<string> ReferencedBy,
    IReadOnlyList<PlcExternalObject> ExternalUsers);

public sealed record PlcUnusedBlocksResult(
    int InventoryBlockCount,
    IReadOnlyList<string> Roots,
    int ReachableCount,
    int UnusedCount,
    IReadOnlyList<PlcUnreachableBlock> Unused,
    int UsedOnlyOutsideProgramCount,
    IReadOnlyList<PlcUnreachableBlock> UsedOnlyOutsideProgram,
    int UndeterminedCount,
    IReadOnlyList<PlcUnreachableBlock> Undetermined,
    int MaxElements,
    bool Complete,
    IReadOnlyList<string> Truncation,
    PlcAnalysisEvidence Evidence);

/// <summary>
/// One instance DB. <see cref="InstanceOfName"/> is the <c>InstanceOfName</c> element of its SimaticML
/// export. Orphan category: <c>instanceOfUnreachable</c> (the FB exists but no OB reaches it).
/// Unresolved category (<c>unresolvedInstanceOf</c>, not an orphan): <c>instanceOfNotInInventory</c> (the
/// name is neither a block nor a PLC data type of this PLC: a system FB/SFB or system data type such as an
/// IEC timer type, which the inventory does not list, or a deleted FB). Not-checked categories:
/// <c>notAddressable</c>, <c>exportFailed</c>, <c>noInstanceOfName</c>, <c>notAnInstanceDbDocument</c>,
/// <c>instanceOfAmbiguous</c> (the name matches several inventory objects; it carries the name),
/// <c>maxElements</c>, <c>timeoutSeconds</c>.
/// </summary>
public sealed record PlcInstanceDbFinding(
    PlcAnalysisObjectRef Block,
    string? InstanceOfName,
    string? InstanceOfKind,
    string Category,
    bool? DbReachable,
    string? Reason);

/// <summary>
/// <see cref="CheckedCount"/> counts the DBs classified as orphan, unresolved or resolved; <see cref="UnresolvedInstanceOf"/>
/// lists at most 200 of <see cref="UnresolvedInstanceOfCount"/>. Unresolved DBs do not make the result incomplete:
/// they were checked, but whether their father instance is a system type or a deleted FB is not decided.
/// </summary>
public sealed record PlcOrphanedInstanceDbsResult(
    int InstanceDbCount,
    int CheckedCount,
    int OrphanCount,
    IReadOnlyList<PlcInstanceDbFinding> Orphans,
    int UnresolvedInstanceOfCount,
    IReadOnlyList<PlcInstanceDbFinding> UnresolvedInstanceOf,
    IReadOnlyList<PlcInstanceDbFinding> NotChecked,
    int MaxElements,
    bool Complete,
    IReadOnlyList<string> Truncation,
    IReadOnlyList<string> InstanceOfEvidence,
    PlcAnalysisEvidence Evidence);

/// <summary>The analysed object: kind block, type or tag.</summary>
public sealed record PlcImpactTarget(string Kind, string Name, string? Path);

/// <summary>
/// One object affected by a change of the target. Depth 1 is a direct user; deeper users depend on
/// <see cref="Via"/> (the user one level closer to the target) through <see cref="Relations"/>.
/// </summary>
public sealed record PlcImpactUser(
    PlcAnalysisObjectRef Object,
    int Depth,
    string Via,
    IReadOnlyList<string> Relations,
    IReadOnlyList<string> Accesses);

public sealed record PlcChangeImpactResult(
    PlcImpactTarget Target,
    int MaxDepth,
    int MaxElements,
    bool Complete,
    IReadOnlyList<string> Truncation,
    int DirectUserCount,
    int TransitiveUserCount,
    IReadOnlyList<PlcImpactUser> Users,
    IReadOnlyList<string> AffectedRoots,
    IReadOnlyList<PlcExternalObject> ExternalUsers,
    IReadOnlyList<PlcUndirectedEvidence> UndirectedEvidence,
    PlcAnalysisEvidence Evidence);
