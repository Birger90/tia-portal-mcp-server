using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;

namespace TiaMcpServer.Plc;

/// <summary>
/// The fail-safe writes in the planner's working state: they resolve only the PLC (the access state and the runtime groups
/// are planned by <see cref="PlcSafetyPlanning"/>, which reads the safety administration), and they touch nothing another
/// item depends on, so no later item is re-planned because of them.
/// </summary>
internal sealed partial class PlcWorkingState
{
    private static bool IsSafetyOperation(string operation) => PlcSafetyMethods.IsWrite(operation);

    /// <summary>The block operations whose target (or, for a group delete, any block inside it) the tree shows as an F-block.</summary>
    private static readonly HashSet<string> TreeCheckedBlockOperations = new(StringComparer.Ordinal)
    {
        "delete_block", "delete_block_group", PlcBlockEditMethods.RenameBlock, PlcBlockEditMethods.RenumberBlock,
        PlcBlockEditMethods.CopyBlock, PlcBlockEditMethods.MoveBlock,
    };

    /// <summary>
    /// True when the planned tree shows that the item changes a fail-safe block (ProgrammingLanguage F_...): its target
    /// block, or for delete_block_group any block inside the group. False without a tree or when the target is not
    /// found (resolution reports that). The planner then fires the fail-safe guards (acceptance, and the login block
    /// guard only when a safety password is set and the session is not logged on).
    /// </summary>
    internal bool ChangesFailSafeBlock(PlcOperationRequest item)
    {
        if (item.Operation == PlcBlockEditMethods.RenameObjects) return _hasTree && RenamesFailSafeBlock(item);
        // create_block_from_document with overwrite: an existing F-block of the declared name is replaced.
        if (item.Operation == Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument)
            return _hasTree && item.Overwrite == true && Contracts.Plc.SimaticSd.SimaticSdBlockDocument.TryCheck(item.Content ?? string.Empty, out _) is { } document
                && _unitRoots.Values.Prepend(_root).SelectMany(UserBlocks).Any(b => Names.Equals(b.Block.Name, document.BlockName) && PlcSafetyPolicy.IsFailSafeLanguage(b.Block.Language));
        if (!_hasTree || item.BlockPath is null || !TreeCheckedBlockOperations.Contains(item.Operation)) return false;
        try
        {
            var address = BlockAddress.Parse(item.BlockPath);
            return item.Operation == "delete_block_group"
                ? DescendantBlocks(DeletionGroup(item, address)).Any(b => PlcSafetyPolicy.IsFailSafeLanguage(b.Block.Language))
                : PlcSafetyPolicy.IsFailSafeLanguage(FindBlock(item, address).Block.Language);
        }
        catch (ResolutionException)
        {
            return false;
        }
    }

    private PlcWriteEffect ResolveSafety(PlcOperationRequest item)
        => Effect(item, PlcSafetyMethods.IsRuntimeGroupWrite(item.Operation)
                ? new(PlcSafetyPlanning.RuntimeGroupKind, _plc, _device, null, null, item.RuntimeGroupName, null, null)
                : new(PlcSafetyPlanning.AdministrationKind, _plc, _device, null, null, null, null, null),
            Array.Empty<PlcFieldChange>(), null);
}
