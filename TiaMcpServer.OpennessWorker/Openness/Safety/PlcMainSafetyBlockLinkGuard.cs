using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;

namespace TiaMcpServer.OpennessWorker.Openness.Safety;

/// <summary>
/// Keeps the F-runtime group references of a block across an import that replaces it. Live evidence (TIA Portal V21,
/// 2026-11-02, E7): replace_network (SIMATIC SD <c>ImportFromDocuments</c> Override) on the main safety block of an F-runtime
/// group succeeded and compiled, but afterwards the group's <c>MainSafetyBlockName</c> was null and the PLC compile failed
/// ("does not have a valid main safety block"); the SimaticML route (set_network_text) kept it (E7b). Every import route
/// runs this check anyway (cheap): before the import the groups whose MainSafetyBlockName or MainSafetyBlockIDbName name
/// the block are recorded; after it they are re-read and a lost reference is re-assigned with set_safety_runtime_group's
/// mechanism (<see cref="PlcSafetyRuntimeGroupBinding"/>). Every lost reference becomes a warning (restored, or the exact
/// repair); the import's own result is never changed, because the import happened. The host re-reads the groups for the
/// item's verification (check <c>safetyRuntimeGroup</c>). Nothing here throws except a lost TIA Portal. Not live-tested.
/// </summary>
internal sealed class PlcMainSafetyBlockLinkGuard
{
    private readonly object _administration;
    private readonly string _plcName;
    private readonly string _blockName;
    private readonly string _blockPath;
    private readonly IReadOnlyList<PlcMainSafetyLink> _before;
    private readonly Func<PlcBlock?> _resolveAfter;

    private PlcMainSafetyBlockLinkGuard(object administration, string plcName, string blockName, string blockPath,
        IReadOnlyList<PlcMainSafetyLink> before, Func<PlcBlock?> resolveAfter)
    {
        _administration = administration;
        _plcName = plcName;
        _blockName = blockName;
        _blockPath = blockPath;
        _before = before;
        _resolveAfter = resolveAfter;
    }

    /// <summary>Only a lost TIA Portal escapes; every other failure of the check becomes a warning.</summary>
    private static bool IsFatal(Exception exception)
        => exception is NonRecoverableException or EngineeringObjectDisposedException or OutOfMemoryException;

    /// <summary>The guard for update_block_logic of <paramref name="blockPath"/>; null when the block is no main safety block of any group.</summary>
    public static PlcMainSafetyBlockLinkGuard? Before(ProjectBase project, string blockPath)
    {
        try
        {
            var address = BlockAddress.Parse(blockPath);
            var block = BlockTargetResolver.ResolveForImport(project, address).Block;
            return block is null ? null : Before(project, address.PlcName, block, blockPath,
                () => BlockTargetResolver.ResolveForImport(project, address).Block);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            // The import's own preflight reports an unresolvable target.
            return null;
        }
    }

    /// <summary>The guard for an import that replaces <paramref name="block"/>; <paramref name="resolveAfter"/> finds the block after the import.</summary>
    public static PlcMainSafetyBlockLinkGuard? Before(ProjectBase project, string? plcName, PlcBlock block, string blockPath, Func<PlcBlock?> resolveAfter)
    {
        try
        {
            string? language = null;
            try
            {
                language = block.ProgrammingLanguage.ToString();
            }
            catch (EngineeringException exception) when (!IsFatal(exception))
            {
                // Unknown language: checked like an F-block.
            }

            // A main safety block (and its instance DB) is a fail-safe block (F_...); standard blocks are not looked up.
            if (language is not null && !PlcSafetyPolicy.IsFailSafeLanguage(language)) return null;
            var plc = PlcSoftwareLocator.FindUnique(project, plcName);
            if (PlcSafetyService.Administration(plc.HostDeviceItem).Administration is not { } administration) return null;
            var name = block.Name;
            var affected = PlcMainSafetyLinks.Affected(PlcSafetyService.ReadLinks(administration), name);
            return affected.Count == 0 ? null : new PlcMainSafetyBlockLinkGuard(administration, plc.Software.Name, name, blockPath, affected, resolveAfter);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            Console.Error.WriteLine($"The F-runtime groups of PLC '{plcName}' could not be read before the import of '{blockPath}' ({exception.Message}); "
                + "whether the import keeps a main safety block reference is not checked. Inspect plc_read get_safety_administration afterwards.");
            return null;
        }
    }

    /// <summary>Re-reads the recorded groups, restores lost references and writes one warning per lost reference to the request's warnings.</summary>
    public void After()
    {
        try
        {
            var outcomes = PlcMainSafetyLinks.Reconcile(_before, _blockName,
                group => PlcSafetyService.FindRuntimeGroup(_administration, group) is { } found ? PlcSafetyService.Link(found) : null,
                Restore, IsFatal);
            foreach (var outcome in outcomes.Where(o => o.Lost))
                Console.Error.WriteLine(PlcMainSafetyLinks.Warning(outcome, _plcName, _blockPath));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            Console.Error.WriteLine($"The F-runtime group(s) {string.Join(", ", _before.Select(l => $"'{l.RuntimeGroup}'"))} of PLC '{_plcName}' referenced '{_blockName}' before the import of "
                + $"'{_blockPath}', and they could not be checked afterwards ({exception.Message}). Inspect plc_read get_safety_administration; repair a lost reference with "
                + "plc_write set_safety_runtime_group or TIA Portal Safety Administration, then compile_check.");
        }
    }

    private (string Route, string? ReadBack) Restore(string groupName, string member, string expected)
    {
        var group = PlcSafetyService.FindRuntimeGroup(_administration, groupName)
            ?? throw new InvalidOperationException($"the F-runtime group '{groupName}' no longer exists");
        var block = _resolveAfter() ?? throw new InvalidOperationException($"no block '{expected}' was found at '{_blockPath}' after the import");
        var assignment = PlcSafetyService.BindAssignment(group, member);
        assignment.Apply(block, block.Name);
        return (assignment.Signature, PlcSafetyService.TryReadAttribute(group, PlcSafetyRuntimeGroupBinding.ReadBackAttribute(member)));
    }
}
