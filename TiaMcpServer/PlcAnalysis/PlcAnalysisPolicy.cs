using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Plc;

namespace TiaMcpServer.PlcAnalysis;

/// <summary>
/// The relation rules applied to cross-reference locations, the caps of the analysis operations and
/// their validation. Pure and Siemens-free.
/// </summary>
public static class PlcAnalysisPolicy
{
    public const string Call = "call";
    public const string MultiInstance = "multiInstance";
    public const string Instance = "instance";
    public const string ParameterInstance = "parameterInstance";
    public const string Reference = "reference";

    public const int MinTimeoutSeconds = 1;
    public const int MaxTimeoutSeconds = 600;
    public const int DefaultTimeoutSeconds = 60;
    public const int MaxDepthLimit = 50;

    /// <summary>Locations kept per edge or undirected pair; the rest are counted only.</summary>
    public const int LocationSamples = 2;

    /// <summary>Upper bound of backward steps of find_call_paths, independent of maxElements.</summary>
    public const int MaxPathSteps = 200_000;

    /// <summary>
    /// Maps a V21 <c>Access</c> name (closed set, see <c>CrossReferenceAccessNames</c>) to a relation.
    /// Call, UC and CC are the call accesses (the reader's live run recorded <c>Call</c> on a system FB);
    /// Multiinstance(AndSymbol) is a multi-instance declaration (recorded live as <c>Multiinstance</c>);
    /// InstanceDB/InstanceAndSymbol and Parameterinstance(AndSymbol) are instance uses; every other
    /// access (Read, Write, RW, Open, Definition, Undefined, Unknown, ...) is a plain reference.
    /// </summary>
    public static string Relation(string access) => access switch
    {
        "Call" or "UC" or "CC" => Call,
        "Multiinstance" or "MultiinstanceAndSymbol" => MultiInstance,
        "InstanceDB" or "InstanceAndSymbol" => Instance,
        "Parameterinstance" or "ParameterinstanceAndSymbol" => ParameterInstance,
        _ => Reference,
    };

    /// <summary>Relations a call tree and a call path follow.</summary>
    public static bool IsCallLike(string relation) => relation is Call or MultiInstance;

    /// <summary>
    /// Direction from a location's <c>ReferenceType</c>: <c>UsedBy</c> means the reference object uses
    /// the queried (source) object; <c>Uses</c> means the source uses the reference object. Every other
    /// value leaves the direction open (null).
    /// </summary>
    public static bool? SourceUsesReference(string referenceType) => referenceType switch
    {
        "Uses" => true,
        "UsedBy" => false,
        _ => null,
    };

    public static IReadOnlyList<string> Rules { get; } = new[]
    {
        "A cross-reference location with referenceType UsedBy means the reference object uses the queried object; Uses means the queried object uses the reference object; any other referenceType (Undefined, TypeInstance, ...) states no direction, so the pair is listed as undirected evidence and never traversed.",
        "Access Call, UC or CC is a call; Multiinstance or MultiinstanceAndSymbol is a multi-instance declaration (shown as relation multiInstance; whether the declared instance is actually called is not distinguishable from this evidence); InstanceDB or InstanceAndSymbol is an instance-DB use, Parameterinstance(AndSymbol) a parameter instance; every other access is a plain reference.",
        "Call trees and call paths follow call and multiInstance relations; reachability (find_unused_blocks, find_orphaned_instance_dbs) and change impact follow every directed relation.",
        "Objects are matched by exact name (case-insensitive) against this PLC's block and type inventory; a top-level source is matched only among the blocks or types of the folder that was swept; a name that matches several objects is not resolved and is counted in ambiguousReferenceCount; a reference whose device differs from the queried object's device is treated as an object outside the inventory.",
    };

    public static IReadOnlyList<string> Limits { get; } = new[]
    {
        "Only relationships TIA Portal records as cross-references are visible: calls through block parameters (BLOCK_FB, BLOCK_FC), ANY/VARIANT/DB_ANY pointers, DB numbers computed at runtime, indirect OPN or instructions that address blocks by number are not seen, so an unreachable block is a candidate, not proof that it can be deleted.",
        "Every OB is a root: event OBs are not distinguished from cyclic OBs, and the analysis cannot tell whether an OB's event ever occurs.",
        "Not live-tested against TIA Portal: the direction rule for blocks is inferred from the V21 ReferenceType names and recorded UsedBy evidence for HMI tags and tag users; no recorded block-call probe exists. Results from software units or namespaces with duplicate block names are partial.",
        "The analysis reads the offline project as Openness reports it; whether edits not yet compiled are reflected in the cross-references is not verified.",
    };

    public static int TimeoutSeconds(PlcOperationRequest operation) => operation.TimeoutSeconds ?? DefaultTimeoutSeconds;

    public static int MaxDepth(PlcOperationRequest operation) => operation.MaxDepth ?? operation.Operation switch
    {
        PlcAnalysisMethods.FindCallPaths => 20,
        _ => 10,
    };

    public static int MaxElements(PlcOperationRequest operation) => operation.MaxElements ?? operation.Operation switch
    {
        PlcAnalysisMethods.GetCallTree => 100,
        PlcAnalysisMethods.FindCallPaths => 50,
        PlcAnalysisMethods.FindOrphanedInstanceDbs => 200,
        _ => 500,
    };

    private static int MaxElementsLimit(string operation) => operation switch
    {
        PlcAnalysisMethods.GetCallTree => 2000,
        PlcAnalysisMethods.FindCallPaths => 1000,
        PlcAnalysisMethods.FindOrphanedInstanceDbs => 1000,
        _ => 5000,
    };

    public static bool Handles(string? operation) => operation is not null && PlcAnalysisMethods.All.Contains(operation);

    /// <summary>Value checks of the analysis operations (fields per operation are the catalog's).</summary>
    public static void Validate(PlcOperationRequest operation, string prefix, List<string> errors)
    {
        if (!Handles(operation.Operation)) return;
        if (operation.TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds)
            errors.Add($"{prefix}: timeoutSeconds must be between {MinTimeoutSeconds} and {MaxTimeoutSeconds}.");
        if (operation.MaxDepth is < 1 or > MaxDepthLimit)
            errors.Add($"{prefix}: maxDepth must be between 1 and {MaxDepthLimit}.");
        var limit = MaxElementsLimit(operation.Operation);
        if (operation.MaxElements is { } elements && (elements < 1 || elements > limit))
            errors.Add($"{prefix}: maxElements must be between 1 and {limit}.");

        foreach (var (name, value) in new[] { ("blockPath", operation.BlockPath), ("typePath", operation.TypePath), ("name", operation.Name) })
            if (value is not null && string.IsNullOrWhiteSpace(value))
                errors.Add($"{prefix}: '{name}' must be nonblank when supplied.");

        if (operation.BlockPath is { } blockPath && !string.IsNullOrWhiteSpace(blockPath))
        {
            try { BlockAddress.Parse(blockPath); }
            catch (ArgumentException exception) { errors.Add($"{prefix}: {exception.Message}"); }
        }

        if (operation.TypePath is { } typePath && !string.IsNullOrWhiteSpace(typePath))
        {
            try { PlcTypeAddress.Parse(typePath); }
            catch (ArgumentException exception) { errors.Add($"{prefix}: {exception.Message}"); }
        }

        if (operation.Operation != PlcAnalysisMethods.AnalyzeChangeImpact) return;
        var targets = new[] { operation.BlockPath is not null, operation.TypePath is not null, operation.TableName is not null || operation.Name is not null }
            .Count(set => set);
        if (targets != 1)
            errors.Add($"{prefix}: analyze_change_impact takes exactly one target: blockPath, typePath, or tableName with name (a PLC tag).");
        else if ((operation.TableName is null) != (operation.Name is null))
            errors.Add($"{prefix}: a tag target needs both tableName and name.");
        if (operation.FolderPath is not null && operation.TableName is null)
            errors.Add($"{prefix}: folderPath narrows the tag table of a tag target; it needs tableName and name.");
        if (operation.PlcName is not null && (operation.BlockPath is not null || operation.TypePath is not null))
            errors.Add($"{prefix}: plcName applies to a tag target only; a blockPath or typePath names its PLC itself.");
    }
}
