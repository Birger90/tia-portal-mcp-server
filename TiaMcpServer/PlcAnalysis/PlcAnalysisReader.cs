using System.Diagnostics;
using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;

namespace TiaMcpServer.PlcAnalysis;

/// <summary>
/// Runs one program-analysis plc_read item: reads the evidence (tree, one cross-reference sweep per
/// root block folder, plus type folders for a type target, the tag's own query for a tag target, and
/// SimaticML exports of instance DBs), builds the graph and runs the analysis. Read-only; every
/// worker call is an existing Observe method.
/// </summary>
internal static class PlcAnalysisReader
{
    /// <summary>Failure categories after which further worker reads of the same item are pointless.</summary>
    private static readonly HashSet<string> Fatal = new(StringComparer.Ordinal)
    {
        WorkerFailureCategories.BindingConflict, WorkerFailureCategories.PortalLost, WorkerFailureCategories.WorkerCrashed,
        WorkerFailureCategories.WorkerTimeout, WorkerFailureCategories.AccessDenied,
    };

    private const int NotCheckedSamples = 200;
    private const int UndirectedSamples = 50;

    public static async Task<StructuredOperationItem> ExecuteAsync(PlcAnalysisSession session, PlcOperationRequest operation, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(PlcAnalysisPolicy.TimeoutSeconds(operation));
        bool Expired() => clock.Elapsed > timeout || cancellationToken.IsCancellationRequested;
        var warnings = new List<string>();

        var tree = await session.TreeAsync().ConfigureAwait(false);
        warnings.AddRange(tree.Warnings);
        if (tree.Value is null) return Failed(operation, tree.Failure!, warnings);

        BlockAddress? blockAddress = operation.BlockPath is null ? null : BlockAddress.Parse(operation.BlockPath);
        PlcTypeAddress? typeAddress = operation.TypePath is null ? null : PlcTypeAddress.Parse(operation.TypePath);
        var plcName = blockAddress?.PlcName ?? typeAddress?.PlcName ?? operation.PlcName;
        var (inventory, selectFailure) = PlcProgramInventory.Select(tree.Value.Roots, tree.Value.Skipped, plcName);
        if (inventory is null) return Failed(operation, selectFailure!, warnings);

        // Resolve the target before any cross-reference read, so a wrong path costs no sweep.
        PlcInventoryBlock? block = null;
        PlcInventoryType? type = null;
        PlcInventoryTagTable? table = null;
        if (blockAddress is not null)
        {
            var (resolved, failure) = inventory.ResolveBlock(blockAddress);
            if (resolved is null) return Failed(operation, failure!, warnings);
            block = resolved;
        }
        else if (typeAddress is not null)
        {
            var (resolved, failure) = inventory.ResolveType(typeAddress);
            if (resolved is null) return Failed(operation, failure!, warnings);
            type = resolved;
        }
        else if (operation.Operation == PlcAnalysisMethods.AnalyzeChangeImpact)
        {
            var (resolved, failure) = inventory.ResolveTagTable(operation.TableName!, operation.FolderPath);
            if (resolved is null) return Failed(operation, failure!, warnings);
            table = resolved;
        }

        if (operation.Operation == PlcAnalysisMethods.GetCallTree
            && block!.Kind is not (ProjectTreeNodeTypes.Ob or ProjectTreeNodeTypes.Fb or ProjectTreeNodeTypes.Fc or ProjectTreeNodeTypes.Block))
            return Failed(operation, new(WorkerFailureCategories.TargetKindUnsupported,
                $"A call tree starts at a code block (OB, FB or FC); '{block.Name}' is a {block.Kind}. Use analyze_change_impact or find_call_paths for a data block."), warnings);

        var sweeps = new List<PlcSweep>();
        foreach (var root in inventory.BlockRoots)
        {
            var failure = await SweepAsync(session, root, PlcSweepKind.Blocks, sweeps, warnings).ConfigureAwait(false);
            if (failure is not null) return Failed(operation, failure, warnings);
        }

        if (type is not null)
            foreach (var root in inventory.TypeRoots)
            {
                var failure = await SweepAsync(session, root, PlcSweepKind.Types, sweeps, warnings).ConfigureAwait(false);
                if (failure is not null) return Failed(operation, failure, warnings);
            }

        var graph = PlcProgramGraph.Build(inventory, sweeps);
        object result;
        switch (operation.Operation)
        {
            case PlcAnalysisMethods.GetCallTree:
                result = PlcProgramAnalyzer.CallTree(graph, NodeOf(graph, block!), PlcAnalysisPolicy.MaxDepth(operation),
                    PlcAnalysisPolicy.MaxElements(operation), Expired);
                break;
            case PlcAnalysisMethods.FindCallPaths:
                result = PlcProgramAnalyzer.CallPaths(graph, NodeOf(graph, block!), PlcAnalysisPolicy.MaxDepth(operation),
                    PlcAnalysisPolicy.MaxElements(operation), Expired);
                break;
            case PlcAnalysisMethods.FindUnusedBlocks:
                result = PlcProgramAnalyzer.Unused(graph, PlcAnalysisPolicy.MaxElements(operation));
                if (result is PlcUnusedBlocksResult { Complete: false })
                    warnings.Add("The evidence or the listing is incomplete, or some blocks are undetermined; the result is not proof that blocks can be deleted.");
                break;
            case PlcAnalysisMethods.FindOrphanedInstanceDbs:
                var orphans = await OrphansAsync(session, graph, PlcAnalysisPolicy.MaxElements(operation), Expired, warnings).ConfigureAwait(false);
                if (orphans.Failure is not null) return Failed(operation, orphans.Failure, warnings);
                result = orphans.Result!;
                break;
            case PlcAnalysisMethods.AnalyzeChangeImpact:
                var impact = await ImpactAsync(session, graph, operation, block, type, table, Expired, warnings).ConfigureAwait(false);
                if (impact.Failure is not null) return Failed(operation, impact.Failure, warnings);
                result = impact.Result!;
                break;
            default:
                throw new InvalidOperationException($"'{operation.Operation}' is not a program-analysis operation.");
        }

        return new StructuredOperationItem(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded,
            Element(result), Failure: null, Omission: null, SkipReason: null, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static async Task<StructuredOperationFailure?> SweepAsync(PlcAnalysisSession session, PlcInventoryRoot root, PlcSweepKind kind,
        List<PlcSweep> sweeps, List<string> warnings)
    {
        var evidence = await session.CrossReferencesAsync(new CrossReferenceSelectorInfo { Path = root.Selector.ToList() }).ConfigureAwait(false);
        warnings.AddRange(evidence.Warnings);
        if (evidence.Value is null) return evidence.Failure;
        sweeps.Add(new PlcSweep(root, kind, evidence.Value));
        return null;
    }

    private static int NodeOf(PlcProgramGraph graph, PlcInventoryBlock block) => graph.Inventory.Blocks.IndexOf(block);

    private static async Task<(PlcOrphanedInstanceDbsResult? Result, StructuredOperationFailure? Failure)> OrphansAsync(
        PlcAnalysisSession session, PlcProgramGraph graph, int maxElements, Func<bool> expired, List<string> warnings)
    {
        var reached = PlcProgramAnalyzer.Reachable(graph);
        var dbs = graph.Nodes.Where(node => !node.IsType && node.Kind == ProjectTreeNodeTypes.InstanceDb)
            .OrderBy(node => node.Name, PlcNameRules.Comparer).ToList();
        var orphans = new List<PlcInstanceDbFinding>();
        var unresolved = new List<PlcInstanceDbFinding>();
        var notChecked = new List<PlcInstanceDbFinding>();
        var truncation = new SortedSet<string>(StringComparer.Ordinal);
        var notCheckedCount = 0;
        var unresolvedCount = 0;
        var checkedCount = 0;
        var attempted = 0;

        void Skip(PlcGraphNode db, string category, string reason, string? instanceOfName = null)
        {
            notCheckedCount++;
            if (notChecked.Count < NotCheckedSamples)
                notChecked.Add(new PlcInstanceDbFinding(db.Ref, instanceOfName, null, category, reached[db.Id], reason));
        }

        foreach (var db in dbs)
        {
            if (db.Path is null)
            {
                Skip(db, "notAddressable", "A DB in a system block folder has no blockPath, so its export cannot be read.");
                continue;
            }

            if (attempted >= maxElements)
            {
                truncation.Add(PlcProgramAnalyzer.TruncatedByElements);
                Skip(db, PlcProgramAnalyzer.TruncatedByElements, $"Not exported: maxElements ({maxElements}) exports were already read.");
                continue;
            }

            if (expired())
            {
                truncation.Add(PlcProgramAnalyzer.TruncatedByTime);
                Skip(db, PlcProgramAnalyzer.TruncatedByTime, "Not exported: timeoutSeconds elapsed.");
                continue;
            }

            attempted++;
            var export = await session.Client.GetBlockContentAsync(db.Path, SourceFormatNames.Xml).ConfigureAwait(false);
            warnings.AddRange(export.Warnings);
            if (!export.Success)
            {
                var category = export.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed;
                if (Fatal.Contains(category))
                    return (null, new(category, $"The export of instance DB '{db.Path}' failed: {export.Error}"));
                Skip(db, "exportFailed", $"{category}: {export.Error}");
                continue;
            }

            var (instanceOf, problemCategory, problem) = InstanceDbXml.Read(export.Payload);
            if (instanceOf is null)
            {
                Skip(db, problemCategory!, problem!);
                continue;
            }

            var (bucket, finding) = PlcProgramAnalyzer.ClassifyInstanceDb(graph, reached, db, instanceOf);
            switch (bucket)
            {
                case PlcProgramAnalyzer.InstanceDbBucket.Ambiguous:
                    // A duplicate name is never resolved: the DB is not checked and the result is incomplete.
                    Skip(db, finding!.Category, finding.Reason!, instanceOf);
                    continue;
                case PlcProgramAnalyzer.InstanceDbBucket.Orphan:
                    orphans.Add(finding!);
                    break;
                case PlcProgramAnalyzer.InstanceDbBucket.Unresolved:
                    unresolvedCount++;
                    if (unresolved.Count < NotCheckedSamples) unresolved.Add(finding!);
                    break;
            }

            checkedCount++;
        }

        var complete = graph.EvidenceComplete && truncation.Count == 0 && notCheckedCount == 0;
        return (new PlcOrphanedInstanceDbsResult(dbs.Count, checkedCount, orphans.Count, orphans, unresolvedCount, unresolved, notChecked, maxElements,
            complete, truncation.ToArray(), InstanceDbXml.Evidence, graph.Evidence()), null);
    }

    private static async Task<(PlcChangeImpactResult? Result, StructuredOperationFailure? Failure)> ImpactAsync(
        PlcAnalysisSession session, PlcProgramGraph graph, PlcOperationRequest operation, PlcInventoryBlock? block, PlcInventoryType? type,
        PlcInventoryTagTable? table, Func<bool> expired, List<string> warnings)
    {
        var direct = new List<(int User, string Via, IReadOnlyList<string> Relations, IReadOnlyList<string> Accesses)>();
        IReadOnlyList<PlcExternalObject> external;
        IReadOnlyList<PlcUndirectedEvidence> undirected;
        PlcImpactTarget target;
        int? targetId = null;
        if (table is not null)
        {
            var selector = new CrossReferenceSelectorInfo
            {
                Path = table.Selector.ToList(),
                Member = new CrossReferenceMemberSelectorInfo { Kind = CrossReferenceMemberKinds.Tag, Name = operation.Name! },
            };
            var evidence = await session.CrossReferencesAsync(selector).ConfigureAwait(false);
            warnings.AddRange(evidence.Warnings);
            if (evidence.Value is null) return (null, evidence.Failure);
            graph.AddQuery(PlcAnalysisSession.Display(selector), evidence.Value);
            var name = evidence.Value.Target.Member?.Name ?? operation.Name!;
            target = new PlcImpactTarget("tag", name, $"{table.FolderPath.TrimEnd('/')}/{table.Name}");
            var uses = graph.CollectLeafUses(evidence.Value);
            foreach (var group in uses.Users.GroupBy(use => use.User))
                direct.Add((group.Key, name, group.Select(u => u.Relation).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToArray(),
                    group.Select(u => u.Access).Distinct().OrderBy(a => a, StringComparer.Ordinal).ToArray()));
            external = uses.ExternalUsers;
            undirected = uses.Undirected.GroupBy(u => (u.Other, u.Access, u.ReferenceType)).Take(UndirectedSamples)
                .Select(g => new PlcUndirectedEvidence(name, graph.Nodes[g.Key.Other].Name, g.Key.Access, g.Key.ReferenceType, g.Count(),
                    g.Take(PlcAnalysisPolicy.LocationSamples).Select(u => u.Location).ToArray())).ToArray();
        }
        else
        {
            var id = block is not null
                ? NodeOf(graph, block)
                : graph.Lookup(type!.Name).Single(node => graph.Nodes[node].IsType && graph.Nodes[node].Path == type.TypePath);
            targetId = id;
            var node = graph.Nodes[id];
            target = new PlcImpactTarget(block is not null ? "block" : "type", node.Name, node.Path);
            foreach (var edge in graph.Incoming(id).Where(edge => edge.From != id))
                direct.Add((edge.From, node.Name, edge.Relations.ToArray(), edge.Accesses.ToArray()));
            external = graph.ExternalUsers(id);
            undirected = graph.UndirectedOf(id).Take(UndirectedSamples).Select(graph.Describe).ToArray();
        }

        var (users, directCount, truncation) = PlcProgramAnalyzer.Impact(graph, direct, targetId, PlcAnalysisPolicy.MaxDepth(operation),
            PlcAnalysisPolicy.MaxElements(operation), expired);
        var roots = users.Where(user => user.Object.Kind == ProjectTreeNodeTypes.Ob).Select(user => user.Object.Name)
            .Concat(block is { Kind: ProjectTreeNodeTypes.Ob } ? new[] { block.Name } : Array.Empty<string>())
            .Distinct(PlcNameRules.Comparer).OrderBy(name => name, PlcNameRules.Comparer).ToArray();
        return (new PlcChangeImpactResult(target, PlcAnalysisPolicy.MaxDepth(operation), PlcAnalysisPolicy.MaxElements(operation),
            graph.EvidenceComplete && truncation.Count == 0, truncation.ToArray(), directCount, users.Count - directCount, users, roots, external,
            undirected, graph.Evidence()), null);
    }

    private static JsonElement Element(object result) => result switch
    {
        PlcCallTreeResult tree => CanonicalJson.ToElement(tree),
        PlcCallPathsResult paths => CanonicalJson.ToElement(paths),
        PlcUnusedBlocksResult unused => CanonicalJson.ToElement(unused),
        PlcOrphanedInstanceDbsResult orphans => CanonicalJson.ToElement(orphans),
        PlcChangeImpactResult impact => CanonicalJson.ToElement(impact),
        _ => throw new InvalidOperationException("Unknown analysis result."),
    };

    private static StructuredOperationItem Failed(PlcOperationRequest operation, StructuredOperationFailure failure, List<string> warnings)
        => StructuredOperationItem.Failed(operation, failure.Category, failure.Message, warnings.Distinct(StringComparer.Ordinal).ToArray());
}
