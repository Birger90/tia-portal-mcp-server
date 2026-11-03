using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.PlcAnalysis;

internal enum PlcSweepKind
{
    Blocks,
    Types,
}

/// <summary>One cross-reference sweep of a root block or type folder and the report the reader returned.</summary>
internal sealed record PlcSweep(PlcInventoryRoot Root, PlcSweepKind Kind, CrossReferenceReport Report);

internal sealed class PlcGraphNode
{
    public PlcGraphNode(int id, string name, string kind, string? path, bool isSystemBlock, bool isType, string? softwareUnit)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Path = path;
        IsSystemBlock = isSystemBlock;
        IsType = isType;
        SoftwareUnit = softwareUnit;
    }

    public int Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string? Path { get; }

    public bool IsSystemBlock { get; }

    public bool IsType { get; }

    public string? SoftwareUnit { get; }

    public bool IsOb => Kind == ProjectTreeNodeTypes.Ob;

    public bool IsDataObject => Kind is ProjectTreeNodeTypes.GlobalDb or ProjectTreeNodeTypes.InstanceDb or ProjectTreeNodeTypes.ArrayDb || IsType;

    public PlcAnalysisObjectRef Ref => new(Name, Kind, Path, InInventory: true, IsSystemBlock);
}

/// <summary>All directed evidence from one node to another, merged over relations, sides and locations.</summary>
internal sealed class PlcGraphEdge
{
    public PlcGraphEdge(int from, int to)
    {
        From = from;
        To = to;
    }

    public int From { get; }

    public int To { get; }

    public SortedSet<string> Relations { get; } = new(StringComparer.Ordinal);

    public SortedSet<string> Accesses { get; } = new(StringComparer.Ordinal);

    public SortedSet<string> ReportedAs { get; } = new(StringComparer.Ordinal);

    public int LocationCount { get; set; }

    public List<PlcAnalysisLocation> Samples { get; } = new();

    public bool IsCallLike => Relations.Any(PlcAnalysisPolicy.IsCallLike);

    public bool IsInstanceUse => Relations.Contains(PlcAnalysisPolicy.Instance) || Relations.Contains(PlcAnalysisPolicy.ParameterInstance);

    public PlcAnalysisEdgeEvidence Evidence => new(Relations.ToArray(), Accesses.ToArray(), ReportedAs.ToArray(), LocationCount, Samples.ToArray());

    public string RelationText => string.Join("+", Relations);
}

internal sealed class PlcUndirectedPair
{
    public PlcUndirectedPair(int first, int second, string access, string referenceType)
    {
        First = first;
        Second = second;
        Access = access;
        ReferenceType = referenceType;
    }

    public int First { get; }

    public int Second { get; }

    public string Access { get; }

    public string ReferenceType { get; }

    public int LocationCount { get; set; }

    public List<PlcAnalysisLocation> Samples { get; } = new();
}

/// <summary>Users of an object outside the graph (a tag), from that object's own cross-reference query.</summary>
internal sealed record PlcLeafUses(
    IReadOnlyList<(int User, string Relation, string Access, PlcAnalysisLocation Location)> Users,
    IReadOnlyList<PlcExternalObject> ExternalUsers,
    IReadOnlyList<(int Other, string Access, string ReferenceType, PlcAnalysisLocation Location)> Undirected,
    int AmbiguousReferenceCount);

/// <summary>
/// The program graph of one PLC: inventory blocks (and types, when type folders were swept) as nodes,
/// and the directed relationships the cross-reference reports state as edges. Pure and Siemens-free;
/// see <see cref="PlcAnalysisPolicy.Rules"/> for how locations become edges.
/// </summary>
internal sealed class PlcProgramGraph
{
    private const int ExternalSamples = 20;
    private const int NameSamples = 20;

    private readonly List<PlcGraphNode> _nodes = new();
    private readonly Dictionary<string, List<int>> _byName = new(PlcNameRules.Comparer);
    private readonly Dictionary<(int, int), PlcGraphEdge> _edges = new();
    private readonly Dictionary<(int, int, string, string), PlcUndirectedPair> _undirected = new();
    private readonly Dictionary<int, List<PlcUndirectedPair>> _undirectedByNode = new();
    private readonly Dictionary<int, List<PlcGraphEdge>> _outgoing = new();
    private readonly Dictionary<int, List<PlcGraphEdge>> _incoming = new();
    private readonly Dictionary<int, List<PlcExternalObject>> _externalUsers = new();
    private readonly Dictionary<int, List<PlcExternalObject>> _externalCallees = new();
    private readonly List<PlcAnalysisQueryEvidence> _queries = new();
    private readonly List<string> _unmatchedSources = new();
    private readonly SortedSet<string> _ambiguousNames = new(PlcNameRules.Comparer);
    private int _matchedSources;
    private int _unmatchedSourceCount;
    private int _ambiguousReferences;

    private PlcProgramGraph(PlcProgramInventory inventory, bool includesTypes)
    {
        Inventory = inventory;
        IncludesTypes = includesTypes;
        foreach (var block in inventory.Blocks)
            AddNode(new PlcGraphNode(_nodes.Count, block.Name, block.Kind, block.BlockPath, block.IsSystemBlock, isType: false, block.SoftwareUnit));
        if (!includesTypes) return;
        foreach (var type in inventory.Types)
            AddNode(new PlcGraphNode(_nodes.Count, type.Name, ProjectTreeNodeTypes.Type, type.TypePath, isSystemBlock: false, isType: true, type.SoftwareUnit));
    }

    public PlcProgramInventory Inventory { get; }

    public bool IncludesTypes { get; }

    public IReadOnlyList<PlcGraphNode> Nodes => _nodes;

    public IReadOnlyCollection<PlcGraphEdge> Edges => _edges.Values;

    public IReadOnlyCollection<PlcUndirectedPair> Undirected => _undirected.Values;

    public static PlcProgramGraph Build(PlcProgramInventory inventory, IReadOnlyList<PlcSweep> sweeps)
    {
        var graph = new PlcProgramGraph(inventory, sweeps.Any(sweep => sweep.Kind == PlcSweepKind.Types));
        foreach (var sweep in sweeps)
            graph.Ingest(sweep);
        return graph;
    }

    public IReadOnlyList<int> Lookup(string name) => _byName.TryGetValue(name, out var ids) ? ids : Array.Empty<int>();

    public IReadOnlyList<PlcGraphEdge> Outgoing(int id) => _outgoing.TryGetValue(id, out var edges) ? edges : Array.Empty<PlcGraphEdge>();

    public IReadOnlyList<PlcGraphEdge> Incoming(int id) => _incoming.TryGetValue(id, out var edges) ? edges : Array.Empty<PlcGraphEdge>();

    public IReadOnlyList<PlcExternalObject> ExternalUsers(int id) => _externalUsers.TryGetValue(id, out var users) ? users : Array.Empty<PlcExternalObject>();

    public IReadOnlyList<PlcExternalObject> ExternalCallees(int id) => _externalCallees.TryGetValue(id, out var callees) ? callees : Array.Empty<PlcExternalObject>();

    public IReadOnlyList<PlcUndirectedPair> UndirectedOf(int id) => _undirectedByNode.TryGetValue(id, out var pairs) ? pairs : Array.Empty<PlcUndirectedPair>();

    public PlcUndirectedEvidence Describe(PlcUndirectedPair pair)
        => new(_nodes[pair.First].Name, _nodes[pair.Second].Name, pair.Access, pair.ReferenceType, pair.LocationCount, pair.Samples.ToArray());

    /// <summary>Records a cross-reference query that is not a sweep (a tag's own query) in the evidence.</summary>
    public void AddQuery(string target, CrossReferenceReport report) => _queries.Add(Query(target, report));

    public bool EvidenceComplete => Inventory.SkippedNodeCount == 0 && _unmatchedSourceCount == 0 && _ambiguousReferences == 0
        && _queries.All(query => query.IsComplete);

    public PlcAnalysisEvidence Evidence(IEnumerable<string>? extraLimits = null)
        => new(Inventory.PlcName, Inventory.DeviceName, Inventory.Blocks.Count, Inventory.Types.Count, Inventory.SkippedNodeCount,
            _queries.ToArray(), _matchedSources, _unmatchedSourceCount, _unmatchedSources.ToArray(), _ambiguousReferences,
            _ambiguousNames.Take(NameSamples).ToArray(), _edges.Count, _undirected.Count, EvidenceComplete,
            PlcAnalysisPolicy.Rules, PlcAnalysisPolicy.Limits.Concat(extraLimits ?? Array.Empty<string>()).ToArray());

    /// <summary>
    /// Users of an object that is not a graph node (a tag), from its own query: UsedBy locations name
    /// its users; other reference types are undirected; non-inventory users are external.
    /// </summary>
    public PlcLeafUses CollectLeafUses(CrossReferenceReport report)
    {
        var users = new List<(int, string, string, PlcAnalysisLocation)>();
        var external = new List<PlcExternalObject>();
        var undirected = new List<(int, string, string, PlcAnalysisLocation)>();
        var ambiguous = 0;
        foreach (var source in report.Sources)
            foreach (var (reference, location) in Locations(source))
            {
                var direction = PlcAnalysisPolicy.SourceUsesReference(location.ReferenceType);
                var ids = ResolveReference(reference, source.Device);
                var at = At(location);
                if (ids.Count > 1)
                {
                    ambiguous++;
                    _ambiguousNames.Add(reference.Name);
                    continue;
                }

                if (ids.Count == 0)
                {
                    if (direction == false) AddExternal(external, reference, location.Access);
                    continue;
                }

                if (direction is null) undirected.Add((ids[0], location.Access, location.ReferenceType, at));
                else if (direction == false) users.Add((ids[0], PlcAnalysisPolicy.Relation(location.Access), location.Access, at));
            }

        _ambiguousReferences += ambiguous;
        return new PlcLeafUses(users, external, undirected, ambiguous);
    }

    private void AddNode(PlcGraphNode node)
    {
        _nodes.Add(node);
        if (!_byName.TryGetValue(node.Name, out var ids)) _byName[node.Name] = ids = new List<int>();
        ids.Add(node.Id);
    }

    private void Ingest(PlcSweep sweep)
    {
        _queries.Add(Query(string.Join("/", sweep.Root.Selector.Select(segment => segment.Name)), sweep.Report));
        foreach (var source in sweep.Report.Sources)
        {
            var owners = Lookup(source.Name).Where(id => _nodes[id].IsType == (sweep.Kind == PlcSweepKind.Types)
                && PlcNameRules.Comparer.Equals(_nodes[id].SoftwareUnit ?? string.Empty, sweep.Root.SoftwareUnit ?? string.Empty)).ToList();
            if (owners.Count != 1)
            {
                _unmatchedSourceCount++;
                if (_unmatchedSources.Count < NameSamples) _unmatchedSources.Add(source.Name);
                continue;
            }

            _matchedSources++;
            var owner = owners[0];
            foreach (var (reference, location) in Locations(source))
                Add(owner, source.Device, reference, location);
        }
    }

    /// <summary>Every location of a source and of its child sources (members), attributed to the top-level source.</summary>
    private static IEnumerable<(CrossReferenceTargetInfo Reference, CrossReferenceLocationInfo Location)> Locations(CrossReferenceSourceInfo source)
    {
        foreach (var reference in source.References)
            foreach (var location in reference.Locations)
                yield return (reference, location);
        foreach (var child in source.Children)
            foreach (var pair in Locations(child))
                yield return pair;
    }

    private void Add(int owner, string ownerDevice, CrossReferenceTargetInfo reference, CrossReferenceLocationInfo location)
    {
        var relation = PlcAnalysisPolicy.Relation(location.Access);
        var direction = PlcAnalysisPolicy.SourceUsesReference(location.ReferenceType);
        var ids = ResolveReference(reference, ownerDevice);
        if (ids.Count > 1)
        {
            _ambiguousReferences++;
            _ambiguousNames.Add(reference.Name);
            return;
        }

        if (ids.Count == 0)
        {
            if (direction == false) AddExternal(Bucket(_externalUsers, owner), reference, location.Access);
            else if (direction == true && PlcAnalysisPolicy.IsCallLike(relation)) AddExternal(Bucket(_externalCallees, owner), reference, location.Access);
            return;
        }

        var other = ids[0];
        if (other == owner && !PlcAnalysisPolicy.IsCallLike(relation)) return;
        if (direction is null)
        {
            var key = (Math.Min(owner, other), Math.Max(owner, other), location.Access, location.ReferenceType);
            if (!_undirected.TryGetValue(key, out var pair))
            {
                _undirected[key] = pair = new PlcUndirectedPair(key.Item1, key.Item2, location.Access, location.ReferenceType);
                Bucket(_undirectedByNode, key.Item1).Add(pair);
                if (key.Item2 != key.Item1) Bucket(_undirectedByNode, key.Item2).Add(pair);
            }

            pair.LocationCount++;
            if (pair.Samples.Count < PlcAnalysisPolicy.LocationSamples) pair.Samples.Add(At(location));
            return;
        }

        var (from, to) = direction.Value ? (owner, other) : (other, owner);
        if (!_edges.TryGetValue((from, to), out var edge))
        {
            _edges[(from, to)] = edge = new PlcGraphEdge(from, to);
            Bucket(_outgoing, from).Add(edge);
            Bucket(_incoming, to).Add(edge);
        }

        edge.Relations.Add(relation);
        edge.Accesses.Add(location.Access);
        edge.ReportedAs.Add(direction.Value ? "uses" : "usedBy");
        edge.LocationCount++;
        if (edge.Samples.Count < PlcAnalysisPolicy.LocationSamples) edge.Samples.Add(At(location));
    }

    /// <summary>Graph nodes a reference names; a reference of another device than the queried object is outside the inventory.</summary>
    private IReadOnlyList<int> ResolveReference(CrossReferenceTargetInfo reference, string ownerDevice)
    {
        if (!string.IsNullOrEmpty(reference.Device) && !string.IsNullOrEmpty(ownerDevice)
            && !PlcNameRules.Comparer.Equals(reference.Device, ownerDevice))
            return Array.Empty<int>();
        return Lookup(reference.Name);
    }

    private static void AddExternal(List<PlcExternalObject> bucket, CrossReferenceTargetInfo reference, string access)
    {
        var external = new PlcExternalObject(reference.Name, reference.TypeName, reference.Device, access);
        if (bucket.Count < ExternalSamples && !bucket.Contains(external)) bucket.Add(external);
    }

    private static List<T> Bucket<T>(Dictionary<int, List<T>> map, int id)
    {
        if (!map.TryGetValue(id, out var list)) map[id] = list = new List<T>();
        return list;
    }

    private static PlcAnalysisLocation At(CrossReferenceLocationInfo location)
        => new(location.Access, location.ReferenceType,
            !string.IsNullOrEmpty(location.ReferenceLocation) ? location.ReferenceLocation : location.Name);

    private static PlcAnalysisQueryEvidence Query(string target, CrossReferenceReport report)
        => new(target, report.Filter, report.IsComplete, report.OwnerQueryCount, report.SuccessfulOwnerQueryCount, report.TotalSourceCount, report.Messages.ToArray());
}
