using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.PlcAnalysis;

/// <summary>
/// The analyses over a <see cref="PlcProgramGraph"/>: call tree, call paths, reachability from OBs,
/// unused blocks, orphaned instance DBs and change impact. Bounded by depth, element and time caps;
/// a hit cap is named in <c>truncation</c> and makes the result incomplete. Pure and Siemens-free.
/// </summary>
internal static class PlcProgramAnalyzer
{
    public const string TruncatedByDepth = "maxDepth";
    public const string TruncatedByElements = "maxElements";
    public const string TruncatedByTime = "timeoutSeconds";
    public const string TruncatedBySteps = "pathSteps";

    private const int UndirectedSamples = 50;
    private const int ReferrerSamples = 10;

    public static PlcCallTreeResult CallTree(PlcProgramGraph graph, int rootId, int maxDepth, int maxElements, Func<bool> expired)
    {
        var nodes = new List<PlcCallTreeNode>();
        var truncation = new SortedSet<string>(StringComparer.Ordinal);
        var expanded = new HashSet<int>();
        var path = new HashSet<int>();
        var inTree = new HashSet<int>();

        void Visit(int nodeId, int? parentId, int depth, PlcGraphEdge? edge)
        {
            if (nodes.Count >= maxElements) { truncation.Add(TruncatedByElements); return; }
            if (expired()) { truncation.Add(TruncatedByTime); return; }
            // A self call (recursion) stays in the list and is shown as a cycle.
            var calls = graph.Outgoing(nodeId).Where(e => e.IsCallLike)
                .OrderBy(e => graph.Nodes[e.To].Name, PlcNameRules.Comparer).ToList();
            var externals = graph.ExternalCallees(nodeId);
            var cycle = path.Contains(nodeId);
            var repeated = !cycle && expanded.Contains(nodeId);
            var hasChildren = calls.Count > 0 || externals.Count > 0;
            var depthCut = !cycle && !repeated && depth >= maxDepth && hasChildren;
            if (depthCut) truncation.Add(TruncatedByDepth);
            var id = nodes.Count;
            inTree.Add(nodeId);
            var instances = graph.Outgoing(nodeId).Where(e => e.IsInstanceUse)
                .OrderBy(e => graph.Nodes[e.To].Name, PlcNameRules.Comparer)
                .Select(e => new PlcInstanceUse(graph.Nodes[e.To].Ref, e.Evidence)).ToArray();
            nodes.Add(new PlcCallTreeNode(id, parentId, depth, graph.Nodes[nodeId].Ref, edge?.Evidence, instances, cycle, repeated,
                ChildrenOmitted: hasChildren && (cycle || repeated || depthCut)));
            if (cycle || repeated || depthCut) return;

            expanded.Add(nodeId);
            path.Add(nodeId);
            foreach (var call in calls)
                Visit(call.To, id, depth + 1, call);
            foreach (var external in externals)
            {
                if (nodes.Count >= maxElements) { truncation.Add(TruncatedByElements); break; }
                nodes.Add(new PlcCallTreeNode(nodes.Count, id, depth + 1, new PlcAnalysisObjectRef(external.Name, null, null, false, false),
                    new PlcAnalysisEdgeEvidence(new[] { PlcAnalysisPolicy.Relation(external.Access) }, new[] { external.Access }, new[] { "uses" }, 1,
                        Array.Empty<PlcAnalysisLocation>()), Array.Empty<PlcInstanceUse>(), false, false, false));
            }

            path.Remove(nodeId);
        }

        Visit(rootId, null, 0, null);
        var undirected = graph.Undirected.Where(pair => PlcAnalysisPolicy.IsCallLike(PlcAnalysisPolicy.Relation(pair.Access))
                && (inTree.Contains(pair.First) || inTree.Contains(pair.Second)))
            .Take(UndirectedSamples).Select(graph.Describe).ToArray();
        return new PlcCallTreeResult(graph.Nodes[rootId].Ref, maxDepth, maxElements, graph.EvidenceComplete && truncation.Count == 0,
            truncation.ToArray(), nodes.Count, nodes, undirected, graph.Evidence());
    }

    public static PlcCallPathsResult CallPaths(PlcProgramGraph graph, int targetId, int maxDepth, int maxPaths, Func<bool> expired)
    {
        var target = graph.Nodes[targetId];
        var truncation = new SortedSet<string>(StringComparer.Ordinal);
        var paths = new List<PlcCallPath>();
        var stack = new List<(int Node, PlcGraphEdge? Edge)> { (targetId, null) };
        var onPath = new HashSet<int> { targetId };
        var steps = 0;

        bool Allowed(PlcGraphEdge edge) => edge.IsCallLike || edge.To == targetId && target.IsDataObject;

        void Record()
        {
            var ordered = Enumerable.Reverse(stack).ToList();
            paths.Add(new PlcCallPath(ordered.Select(entry => graph.Nodes[entry.Node].Name).ToArray(),
                ordered.Take(ordered.Count - 1).Select((entry, index) => ordered[index].Edge!.RelationText).ToArray()));
        }

        bool Back(int node, int depth)
        {
            if (paths.Count >= maxPaths) { truncation.Add(TruncatedByElements); return false; }
            if (++steps > PlcAnalysisPolicy.MaxPathSteps) { truncation.Add(TruncatedBySteps); return false; }
            if (expired()) { truncation.Add(TruncatedByTime); return false; }
            if (graph.Nodes[node].IsOb)
            {
                Record();
                return true;
            }

            var callers = graph.Incoming(node).Where(edge => node != targetId ? edge.IsCallLike : Allowed(edge))
                .Where(edge => edge.From != node).OrderBy(edge => graph.Nodes[edge.From].Name, PlcNameRules.Comparer).ToList();
            if (callers.Count > 0 && depth >= maxDepth)
            {
                truncation.Add(TruncatedByDepth);
                return true;
            }

            foreach (var edge in callers)
            {
                if (!onPath.Add(edge.From)) continue;
                stack.Add((edge.From, edge));
                var keepGoing = Back(edge.From, depth + 1);
                stack.RemoveAt(stack.Count - 1);
                onPath.Remove(edge.From);
                if (!keepGoing) return false;
            }

            return true;
        }

        Back(targetId, 0);
        var direct = graph.Incoming(targetId).Where(Allowed).Where(edge => edge.From != targetId)
            .Select(edge => graph.Nodes[edge.From]).OrderBy(node => node.Name, PlcNameRules.Comparer).Select(node => node.Ref).ToArray();
        var undirected = graph.UndirectedOf(targetId).Take(UndirectedSamples).Select(graph.Describe).ToArray();
        return new PlcCallPathsResult(target.Ref, maxDepth, maxPaths, graph.EvidenceComplete && truncation.Count == 0, truncation.ToArray(),
            paths.Count, paths, paths.Select(p => p.Blocks[0]).Distinct(PlcNameRules.Comparer).ToArray(), direct, undirected, graph.Evidence());
    }

    /// <summary>Nodes reachable from any OB over every directed relation (the OBs included).</summary>
    public static bool[] Reachable(PlcProgramGraph graph)
    {
        var reached = new bool[graph.Nodes.Count];
        var queue = new Queue<int>();
        foreach (var node in graph.Nodes.Where(node => node.IsOb))
        {
            reached[node.Id] = true;
            queue.Enqueue(node.Id);
        }

        while (queue.Count > 0)
            foreach (var edge in graph.Outgoing(queue.Dequeue()))
                if (!reached[edge.To])
                {
                    reached[edge.To] = true;
                    queue.Enqueue(edge.To);
                }

        return reached;
    }

    /// <summary>
    /// Unreached nodes whose reachability cannot be decided: those that share undirected evidence with a reached node,
    /// and every unreached node an undetermined node leads to over a directed relation or further undirected evidence
    /// (if the undetermined node is in fact reached, so is what it uses). Never true for a reached node.
    /// </summary>
    public static bool[] Undetermined(PlcProgramGraph graph, bool[] reached)
    {
        var undetermined = new bool[graph.Nodes.Count];
        var queue = new Queue<int>();
        void Mark(int id)
        {
            if (reached[id] || undetermined[id]) return;
            undetermined[id] = true;
            queue.Enqueue(id);
        }

        foreach (var pair in graph.Undirected)
        {
            if (reached[pair.First]) Mark(pair.Second);
            if (reached[pair.Second]) Mark(pair.First);
        }

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var edge in graph.Outgoing(node)) Mark(edge.To);
            foreach (var pair in graph.UndirectedOf(node)) Mark(pair.First == node ? pair.Second : pair.First);
        }

        return undetermined;
    }

    /// <summary>
    /// The unreached blocks by category. A non-empty <c>undetermined</c> list makes the result incomplete
    /// (<c>complete:false</c> with an <c>evidence.limits</c> line): the listing does not decide those blocks, so it is no
    /// complete answer to which blocks are unused.
    /// </summary>
    public static PlcUnusedBlocksResult Unused(PlcProgramGraph graph, int maxElements)
    {
        var reached = Reachable(graph);
        var maybe = Undetermined(graph, reached);
        var unused = new List<PlcUnreachableBlock>();
        var outside = new List<PlcUnreachableBlock>();
        var undetermined = new List<PlcUnreachableBlock>();
        foreach (var node in graph.Nodes.Where(node => !node.IsType && !reached[node.Id]).OrderBy(node => node.Name, PlcNameRules.Comparer))
        {
            var referrers = graph.Incoming(node.Id).Where(edge => edge.From != node.Id).Select(edge => graph.Nodes[edge.From].Name)
                .Distinct(PlcNameRules.Comparer).OrderBy(name => name, PlcNameRules.Comparer).Take(ReferrerSamples).ToArray();
            var external = graph.ExternalUsers(node.Id);
            if (maybe[node.Id])
                undetermined.Add(new PlcUnreachableBlock(node.Ref, "undetermined", referrers, external));
            else if (external.Count > 0)
                outside.Add(new PlcUnreachableBlock(node.Ref, "usedOnlyOutsideProgram", referrers, external));
            else
                unused.Add(new PlcUnreachableBlock(node.Ref, referrers.Length == 0 ? "unreferenced" : "referencedOnlyByUnreachableBlocks", referrers, external));
        }

        var truncation = unused.Count + outside.Count + undetermined.Count > maxElements ? new[] { TruncatedByElements } : Array.Empty<string>();
        var roots = graph.Nodes.Where(node => node.IsOb).Select(node => node.Name).OrderBy(name => name, PlcNameRules.Comparer).ToArray();
        var limits = new List<string>();
        if (roots.Length == 0) limits.Add("The PLC has no OB, so nothing is reachable and every block is reported.");
        if (undetermined.Count > 0)
            limits.Add($"{undetermined.Count} block(s) are undetermined: they share undirected evidence with a reachable block, or are reached "
                + "only through such a block, so whether an OB reaches them is not decided; the result is incomplete.");
        var remaining = maxElements;
        IReadOnlyList<PlcUnreachableBlock> Take(List<PlcUnreachableBlock> list)
        {
            var taken = list.Take(Math.Max(0, remaining)).ToArray();
            remaining -= taken.Length;
            return taken;
        }

        return new PlcUnusedBlocksResult(graph.Inventory.Blocks.Count, roots, reached.Take(graph.Inventory.Blocks.Count).Count(r => r),
            unused.Count, Take(unused), outside.Count, Take(outside), undetermined.Count, Take(undetermined), maxElements,
            graph.EvidenceComplete && truncation.Length == 0 && undetermined.Count == 0, truncation, graph.Evidence(limits));
    }

    /// <summary>Where one checked instance DB goes in the find_orphaned_instance_dbs result.</summary>
    public enum InstanceDbBucket
    {
        /// <summary>Its InstanceOfName names exactly one inventory object, and that does not make it an orphan.</summary>
        Resolved,

        /// <summary><c>orphans[]</c>: <c>instanceOfUnreachable</c>.</summary>
        Orphan,

        /// <summary><c>unresolvedInstanceOf[]</c>: no inventory object has the name (a deleted FB, or a system FB/SFB or system type).</summary>
        Unresolved,

        /// <summary><c>notChecked[]</c> with <c>instanceOfAmbiguous</c>: several inventory objects have the name; it is not resolved.</summary>
        Ambiguous,
    }

    public const string InstanceOfNotInInventory = "instanceOfNotInInventory";
    public const string InstanceOfUnreachable = "instanceOfUnreachable";
    public const string InstanceOfAmbiguous = "instanceOfAmbiguous";

    /// <summary>
    /// Classifies one instance DB whose export named <paramref name="instanceOfName"/>. The finding is null for
    /// <see cref="InstanceDbBucket.Resolved"/>. Duplicate names are never resolved (<see cref="InstanceDbBucket.Ambiguous"/>).
    /// </summary>
    public static (InstanceDbBucket Bucket, PlcInstanceDbFinding? Finding) ClassifyInstanceDb(PlcProgramGraph graph, bool[] reached, PlcGraphNode db, string instanceOfName)
    {
        var dbReachable = reached[db.Id];
        var blocks = graph.Lookup(instanceOfName).Where(id => !graph.Nodes[id].IsType).ToList();
        var types = graph.Inventory.Types.Where(type => PlcNameRules.Comparer.Equals(type.Name, instanceOfName)).ToList();
        if (blocks.Count == 0 && types.Count == 0)
            return (InstanceDbBucket.Unresolved, new PlcInstanceDbFinding(db.Ref, instanceOfName, null, InstanceOfNotInInventory, dbReachable,
                "Neither a block nor a PLC data type of this PLC has this name: it is a system FB/SFB or system data type (for example an IEC timer type), "
                + "which the inventory does not list, or the FB was deleted. Not reported as an orphan: the analysis cannot tell which."));
        if (blocks.Count + types.Count > 1)
            return (InstanceDbBucket.Ambiguous, new PlcInstanceDbFinding(db.Ref, instanceOfName, null, InstanceOfAmbiguous, dbReachable,
                $"{blocks.Count + types.Count} blocks or PLC data types of this PLC have this name (duplicate names across software units or namespaces); "
                + "the father instance is not resolved, so the DB is not checked."));
        if (blocks.Count == 1)
        {
            var fb = graph.Nodes[blocks[0]];
            if (fb.Kind == ProjectTreeNodeTypes.Fb && !reached[fb.Id])
                return (InstanceDbBucket.Orphan, new PlcInstanceDbFinding(db.Ref, instanceOfName, fb.Kind, InstanceOfUnreachable, dbReachable,
                    "The FB exists but no OB reaches it through the recorded cross-references."));
        }

        return (InstanceDbBucket.Resolved, null);
    }

    public static (List<PlcImpactUser> Users, int DirectCount, SortedSet<string> Truncation) Impact(
        PlcProgramGraph graph,
        IReadOnlyList<(int User, string Via, IReadOnlyList<string> Relations, IReadOnlyList<string> Accesses)> direct,
        int? targetId,
        int maxDepth,
        int maxElements,
        Func<bool> expired)
    {
        var users = new List<PlcImpactUser>();
        var truncation = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        if (targetId is { } target) seen.Add(target);
        var queue = new Queue<(int Node, int Depth)>();
        foreach (var (user, via, relations, accesses) in direct.OrderBy(entry => graph.Nodes[entry.User].Name, PlcNameRules.Comparer))
        {
            if (!seen.Add(user)) continue;
            if (users.Count >= maxElements) { truncation.Add(TruncatedByElements); break; }
            users.Add(new PlcImpactUser(graph.Nodes[user].Ref, 1, via, relations, accesses));
            queue.Enqueue((user, 1));
        }

        var directCount = users.Count;
        while (queue.Count > 0 && !truncation.Contains(TruncatedByElements))
        {
            if (expired()) { truncation.Add(TruncatedByTime); break; }
            var (node, depth) = queue.Dequeue();
            foreach (var edge in graph.Incoming(node).OrderBy(edge => graph.Nodes[edge.From].Name, PlcNameRules.Comparer))
            {
                if (seen.Contains(edge.From)) continue;
                if (depth >= maxDepth) { truncation.Add(TruncatedByDepth); break; }
                if (users.Count >= maxElements) { truncation.Add(TruncatedByElements); break; }
                seen.Add(edge.From);
                users.Add(new PlcImpactUser(graph.Nodes[edge.From].Ref, depth + 1, graph.Nodes[node].Name, edge.Relations.ToArray(), edge.Accesses.ToArray()));
                queue.Enqueue((edge.From, depth + 1));
            }
        }

        return (users, directCount, truncation);
    }
}
