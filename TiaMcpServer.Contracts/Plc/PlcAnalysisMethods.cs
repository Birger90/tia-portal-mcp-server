namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Program-analysis operations of <c>plc_read</c>. They are computed by the host from existing reads
/// (the project-tree block inventory, the <c>read_cross_references</c> reader and, for instance DBs,
/// the <c>get_block_content</c> SimaticML export), so they are <c>Observe</c> and have no worker method of
/// their own. They need no Siemens member the repository does not already call.
/// </summary>
public static class PlcAnalysisMethods
{
    public const string GetCallTree = "get_call_tree";

    public const string FindCallPaths = "find_call_paths";

    public const string FindUnusedBlocks = "find_unused_blocks";

    public const string FindOrphanedInstanceDbs = "find_orphaned_instance_dbs";

    public const string AnalyzeChangeImpact = "analyze_change_impact";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        GetCallTree, FindCallPaths, FindUnusedBlocks, FindOrphanedInstanceDbs, AnalyzeChangeImpact,
    });
}
