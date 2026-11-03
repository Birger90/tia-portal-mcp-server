using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// create_block_from_document in the planner's working state: the block the document declares, in its target group;
/// the tree tells whether a block of that name already exists anywhere in the PLC (refused without overwrite, as the
/// worker refuses it again). The network edits resolve like set_network_text (PlcWorkingState.cs).
/// </summary>
internal sealed partial class PlcWorkingState
{
    private const string PlcSimaticSdOperation = SimaticSdMethods.CreateBlockFromDocument;

    private static bool IsSimaticSdOperation(string operation) => operation == SimaticSdMethods.CreateBlockFromDocument;

    /// <summary>The block a create_block_from_document document declares, or null when the document is not usable.</summary>
    private static string? SimaticSdBlockDocumentName(PlcOperationRequest item)
        => SimaticSdBlockDocument.TryCheck(item.Content ?? string.Empty, out _)?.BlockName;

    private PlcWriteEffect ResolveSimaticSd(PlcOperationRequest item, List<FiredGuard> guards)
    {
        var document = SimaticSdBlockDocument.TryCheck(item.Content!, out var error)
            ?? throw new ResolutionException(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {error}");
        var into = TargetGroupPath(item.TargetGroupPath!);
        var path = $"{into}/{document.BlockName}";
        var existing = _unitRoots.Values.Prepend(_root).SelectMany(UserBlocks).Where(b => Names.Equals(b.Block.Name, document.BlockName)).ToList();
        if (existing.Count > 0 && item.Overwrite != true)
            throw new ResolutionException(WorkerFailureCategories.ValidationError,
                $"Operation '{item.OperationId}': a block named '{document.BlockName}' already exists at '{existing[0].Group.Path}/{existing[0].Block.Name}'. "
                + "Set overwrite:true to import with ImportDocumentOptions.Override, or rename the block in the .s7dcl document.");
        if (existing.Count > 0)
            guards.Add(Fire(SdDocumentReplacesBlock, item,
                $"create_block_from_document imports '{document.BaseName}' with ImportDocumentOptions.Override although '{string.Join("', '", existing.Select(e => $"{e.Group.Path}/{e.Block.Name}"))}' "
                + $"already exists (overwrite:true); TIA Portal decides what that replaces when the existing block is not in '{into}'."));
        if (_skipped.Any(IsBlockTreeNode)) guards.Add(SkippedGuard(item));
        var changes = Change(existing.Count > 0 ? "replace" : "create", existing.Count > 0 ? $"{existing[0].Group.Path}/{existing[0].Block.Name}" : null, path)
            .Concat(Change("blockKind", null, document.Parsed.BlockKind))
            .Concat(Change("networks", null, document.Parsed.Networks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        return Effect(item, new(Contracts.Plc.PlcNameRules.Block, _plc, _device, null, null, document.BlockName, path, null), changes, into);
    }

    private void ApplySimaticSd(PlcOperationRequest item)
    {
        if (item.Operation != SimaticSdMethods.CreateBlockFromDocument || SimaticSdBlockDocument.TryCheck(item.Content!, out _) is not { } document) return;
        Touch(CpuKey(document.BlockName), item);
        if (!_hasTree) return;
        foreach (var (group, block) in _unitRoots.Values.Prepend(_root).SelectMany(UserBlocks).Where(b => Names.Equals(b.Block.Name, document.BlockName)).ToList())
            group.Blocks.Remove(block);
        var type = document.Parsed.BlockKind switch
        {
            "FUNCTION_BLOCK" => "FB", "FUNCTION" => "FC", "ORGANIZATION_BLOCK" => "OB", "DATA_BLOCK" => "GlobalDB", _ => "Block",
        };
        FindGroupByPath(TargetGroupPath(item.TargetGroupPath!))?.Blocks.Add(new Block(document.BlockName, BlockNodeType(type),
            type == "GlobalDB" ? "DB" : document.Parsed.PreferredLanguage, false, null));
    }
}
