using TiaMcpServer.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// Read-backs that replace the former <c>trusted</c> entries of operations that change one object (K24, live V21
/// 2026-11-02: set_plc_access_level reported readBack matched while its verification said "nothing was read back"):
/// <c>tag</c> re-reads the tag table after create_tag, delete_tag and move_tag (one list_tag_tables call; move_tag also
/// compares the data type and address the worker reported) and the comment after a create_tag or update_tag that wrote one
/// (read_tag_comments, K22); <c>block</c> re-reads a deleted block's path (get_block_interface must answer
/// target_not_found); <c>readBack</c> reports the worker's own read-back of set_plc_access_level when it matched (an
/// unreadable level stays <c>trusted</c>: nothing could be read).
/// </summary>
public sealed partial class PlcVerificationPolicy
{
    public const string TagCheck = "tag";
    public const string ReadBackCheck = "readBack";

    private const string CreateTag = "create_tag";
    private const string UpdateTag = "update_tag";
    private const string DeleteTag = "delete_tag";
    private const string DeleteBlock = "delete_block";
    private const string CreateBlock = "create_block";

    private static bool IsTagReadBack(PlcOperationRequest item)
        => item.Operation is CreateTag or DeleteTag or PlcTagMoveMethods.MoveTag || item.Operation == UpdateTag && item.Comment is not null;

    private bool HasAccessLevelReadBack(PlcOperationRequest item)
        => AccessLevelResult(item) is { ReadBack: PlcSecurityPolicy.ReadBacks.Matched };

    private PlcSecurityWriteResultInfo? AccessLevelResult(PlcOperationRequest item)
        => item.Operation == PlcSecurityMethods.SetAccessLevel && _results.TryGetValue(item.OperationId, out var raw)
            ? CanonicalJson.Deserialize<PlcSecurityWriteResultInfo>(raw.GetRawText())
            : null;

    /// <summary>
    /// Only completed items (a result exists): a failed one keeps the shared rule "no verification entry, inspect it with
    /// plc_read" (a failed create_tag collision would otherwise report the colliding tag that already existed).
    /// </summary>
    private bool HandlesTagOrBlockReadBack(PlcOperationRequest item)
        => (IsTagReadBack(item) || item.Operation is DeleteBlock or CreateBlock) && _results.ContainsKey(item.OperationId);

    private Task<WriteOperationVerification> TagOrBlockReadBackAsync(PlcOperationRequest item)
        => item.Operation switch
        {
            DeleteBlock => DeletedBlockReadBackAsync(item),
            CreateBlock => Task.FromResult(CreatedBlockWorkerReadBack(item)),
            _ => TagReadBackAsync(item),
        };

    /// <summary>
    /// K57 (live V21 2026-11-02: create_block said "trusted, nothing was read back"): the worker re-resolves the created block
    /// by its path right after the import and fails the item when it is not found (BlockMutationService
    /// VerifyCreatedBlockPostconditions), and its result carries the number read from the created block. That read is the
    /// item's verification, check block; no host read follows (a new block is not compiled, and TIA Portal exports no SimaticML
    /// of a block not compiled since it was created).
    /// </summary>
    private WriteOperationVerification CreatedBlockWorkerReadBack(PlcOperationRequest item)
    {
        var result = CanonicalJson.Deserialize<Contracts.Block.BlockMutationResultInfo>(_results[item.OperationId].GetRawText());
        var path = string.IsNullOrEmpty(result.BlockPath) ? item.BlockPath : result.BlockPath;
        var expected = $"{path} exists";
        var success = result.Success && !string.IsNullOrEmpty(path);
        var actual = success
            ? $"{path} exists ({result.BlockType ?? item.BlockType}, number={result.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(unreadable)"})"
            : $"{path} not confirmed";
        return new(item.OperationId, success, BlockCheck, expected, actual, success
            ? $"Operation '{item.OperationId}': the worker re-resolved the created block by its path right after the import (not compiled; run compile_check to compile it)."
            : $"Operation '{item.OperationId}': the result does not confirm the created block. Inspect it with plc_read get_block_interface.");
    }

    /// <summary>The worker read PlcProtectionAccessLevel back right after the write and found the requested level.</summary>
    private WriteOperationVerification AccessLevelReadBack(PlcOperationRequest item)
    {
        var result = AccessLevelResult(item)!;
        var success = string.Equals(result.After, result.Requested, StringComparison.Ordinal);
        return new(item.OperationId, success, ReadBackCheck, result.Requested, result.After, success
            ? $"Operation '{item.OperationId}': the worker read PlcProtectionAccessLevel back right after setting it (readBack matched)."
            : $"Operation '{item.OperationId}': the read-back level differs from the requested one. Inspect it with plc_read get_plc_security.");
    }

    private async Task<WriteOperationVerification> TagReadBackAsync(PlcOperationRequest item)
    {
        var plcName = effects[item.OperationId].Target.PlcName;
        var move = item.Operation == PlcTagMoveMethods.MoveTag;
        var read = await client.ListTagTablesAsync(item.PlcName ?? plcName, tableName: move ? null : item.TableName).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = "list_tag_tables" }, read);
        if (projected.Result is not { } inventory)
            return Fail(item, TagCheck, $"Operation '{item.OperationId}': the tag table could not be re-read ({projected.Failure?.Message}).");
        var tables = CanonicalJson.Deserialize<PlcTagInventoryInfo>(inventory.GetRawText()).Plcs
            .Where(p => PlcNameRules.Comparer.Equals(p.PlcName, plcName)).SelectMany(p => p.Tables).ToList();
        var written = _results.TryGetValue(item.OperationId, out var raw) ? raw : (System.Text.Json.JsonElement?)null;
        var (expected, actual) = item.Operation switch
        {
            PlcTagMoveMethods.MoveTag => Moved(item, tables, written),
            DeleteTag => ($"{TagPath(item.FolderPath, item.TableName, item.Name)} absent", Shown(tables, item.FolderPath, item.TableName, item.Name)),
            _ => ($"{TagPath(item.FolderPath, item.TableName, FinalName(item))} exists", Shown(tables, item.FolderPath, item.TableName, FinalName(item))),
        };
        if (item.Comment is not null)
        {
            var culture = written is { } result ? CanonicalJson.Deserialize<TagMutationResultInfo>(result.GetRawText()).CommentCulture ?? item.CommentCulture! : item.CommentCulture!;
            expected += $"; comment[{culture}]={Text(item.Comment)}";
            actual += $"; comment[{culture}]={await CommentAsync(item, plcName, culture).ConfigureAwait(false)}";
        }

        var success = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) && CommentMatches(expected, actual);
        return new(item.OperationId, success, TagCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the re-read tag table does not show the requested state. Inspect it with plc_read list_tag_tables and read_tag_comments.");
    }

    /// <summary>move_tag: gone from the source table, in the target table with the data type and address the worker reported.</summary>
    private static (string Expected, string Actual) Moved(PlcOperationRequest item, List<TagTableInfo> tables, System.Text.Json.JsonElement? written)
    {
        var source = $"{TagPath(item.FolderPath, item.TableName, item.Name)} absent";
        var actualSource = Shown(tables, item.FolderPath, item.TableName, item.Name);
        var tag = Find(tables, item.TargetFolderPath, item.TargetTableName, item.Name);
        var target = TagPath(item.TargetFolderPath, item.TargetTableName, item.Name);
        var actualTarget = tag is null ? Shown(tables, item.TargetFolderPath, item.TargetTableName, item.Name) : $"{target} exists dataType={tag.DataType} address={tag.LogicalAddress}";
        if (written is not { } raw) return ($"{source}; {target} exists", $"{actualSource}; {actualTarget}");
        var moved = CanonicalJson.Deserialize<PlcTagMoveResultInfo>(raw.GetRawText());
        return ($"{source}; {target} exists dataType={moved.DataType} address={moved.LogicalAddress}", $"{actualSource}; {actualTarget}");
    }

    /// <summary>The comment text of the culture as read_tag_comments reports it (which lists only non-empty texts).</summary>
    private async Task<string> CommentAsync(PlcOperationRequest item, string plcName, string culture)
    {
        var (inventory, problem) = await PlcTagCommentPlanning.ReadAsync(client, item, plcName).ConfigureAwait(false);
        if (inventory is null) return $"(unreadable: {problem})";
        var member = inventory.Plcs.Where(p => PlcNameRules.Comparer.Equals(p.PlcName, plcName)).SelectMany(p => p.Tables)
            .Where(t => t.UnitName is null && SameFolder(t.FolderPath, item.FolderPath) && string.Equals(t.TableName, item.TableName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => t.Members).FirstOrDefault(m => m.Kind == "Tag" && string.Equals(m.Name, FinalName(item), StringComparison.OrdinalIgnoreCase));
        if (member is null) return "(tag not listed)";
        if (member.Comment is null) return "(unreadable)";
        return Text(member.Comment.FirstOrDefault(c => string.Equals(c.Culture, culture, StringComparison.OrdinalIgnoreCase))?.Text);
    }

    /// <summary>Comment texts are compared exactly (case and blanks count); the rest of the description case-insensitively.</summary>
    private static bool CommentMatches(string expected, string actual)
    {
        var e = expected.IndexOf("; comment[", StringComparison.Ordinal);
        var a = actual.IndexOf("; comment[", StringComparison.Ordinal);
        return e < 0 && a < 0 || e >= 0 && a >= 0 && string.Equals(expected[e..], actual[a..], StringComparison.Ordinal);
    }

    /// <summary>delete_block: get_block_interface of the deleted path must answer target_not_found.</summary>
    private async Task<WriteOperationVerification> DeletedBlockReadBackAsync(PlcOperationRequest item)
    {
        var read = await client.PlcBlockEditAsync(PlcBlockEditMethods.GetBlockInterface, new PlcBlockEditRequestInfo { BlockPath = item.BlockPath }).ConfigureAwait(false);
        var projected = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = PlcBlockEditMethods.GetBlockInterface }, read);
        var expected = $"{item.BlockPath} absent";
        var actual = projected.Result is not null ? $"{item.BlockPath} exists"
            : projected.Failure?.Category == WorkerFailureCategories.TargetNotFound ? expected
            : null;
        if (actual is null)
            return Fail(item, BlockCheck, $"Operation '{item.OperationId}': '{item.BlockPath}' could not be re-read ({projected.Failure?.Category}: {projected.Failure?.Message}).");
        var success = actual == expected;
        return new(item.OperationId, success, BlockCheck, expected, actual, success ? null
            : $"Operation '{item.OperationId}': the block still exists after Delete(). Inspect it with plc_read get_block_interface.");
    }

    private static string FinalName(PlcOperationRequest item) => item.Operation == UpdateTag ? item.NewName ?? item.Name! : item.Name!;

    private static string Text(string? text) => string.IsNullOrEmpty(text) ? "(empty)" : text;

    private static TagInfo? Find(List<TagTableInfo> tables, string? folder, string? table, string? name)
        => tables.FirstOrDefault(t => SameFolder(t.FolderPath, folder) && string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))
            ?.Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Shown(List<TagTableInfo> tables, string? folder, string? table, string? name)
    {
        if (!tables.Any(t => SameFolder(t.FolderPath, folder) && string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase)))
            return $"{TagPath(folder, table, null)} absent";
        return Find(tables, folder, table, name) is { } tag ? $"{TagPath(folder, table, tag.Name)} exists" : $"{TagPath(folder, table, name)} absent";
    }

    private static string TagPath(string? folder, string? table, string? name)
    {
        var normalized = NormalizeFolder(folder);
        var path = (normalized == "/" ? "/" : normalized + "/") + table;
        return name is null ? path : $"{path}/{name}";
    }

    private static bool SameFolder(string? left, string? right) => string.Equals(NormalizeFolder(left), NormalizeFolder(right), StringComparison.OrdinalIgnoreCase);
}
