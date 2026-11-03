using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// Plans replace_tag_comments from the public read read_tag_comments: the effect lists every comment text the one-pass
/// replacement would change (tags of the PLC's own tag tables in scope); an item that would change nothing is refused,
/// and an unreadable or incomplete read is reported (plc_state_unverifiable, info) while the item still runs. The worker
/// applies the replacement to the live texts again.
/// </summary>
internal static class PlcTagCommentPlanning
{
    public static async Task<(PlcWriteEffect Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error)> ResolveAsync(
        OpennessWorkerClient client, PlcOperationRequest item, PlcWriteEffect effect, bool dependsOnEarlier)
    {
        var guards = new List<FiredGuard>();
        if (dependsOnEarlier) return (effect, guards, null);
        var (changes, problem) = await PreviewAsync(client, item, effect.Target.PlcName).ConfigureAwait(false);
        if (changes is null)
        {
            guards.Add(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                $"Operation '{item.OperationId}': the current tag comments could not be read ({problem}), so the changes cannot be previewed; the worker applies the replacement to the live texts."));
            return (effect, guards, null);
        }

        if (problem is not null)
            guards.Add(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId, $"Operation '{item.OperationId}': {problem}"));
        if (changes.Count == 0)
            return (effect, guards, new WriteToolError(WorkerFailureCategories.ValidationError,
                $"Operation '{item.OperationId}': replace_tag_comments changes nothing (no comment text in scope contains a find); nothing is written."));
        return (effect with { Changes = changes.Select(c => new PlcFieldChange($"comment {c.Object} [{c.Culture}]", c.Old, c.New)).ToArray() }, guards, null);
    }

    /// <summary>The texts the replacement changes in the current comments, or null with the reason when they could not be read.</summary>
    internal static async Task<(List<PlcTextChangeInfo>? Changes, string? Problem)> PreviewAsync(OpennessWorkerClient client, PlcOperationRequest item, string plcName)
    {
        var (inventory, problem) = await ReadAsync(client, item, plcName).ConfigureAwait(false);
        if (inventory is null) return (null, problem);
        var changes = new List<PlcTextChangeInfo>();
        foreach (var (table, member, text) in Texts(inventory, item, plcName))
        {
            var replaced = SimaticMlNetworkTexts.ReplaceAll(text.Text, item.Replacements!);
            if (replaced != text.Text)
                changes.Add(new PlcTextChangeInfo { Object = ObjectPath(table, member.Name), Where = "comment", Key = ObjectPath(table, member.Name), Culture = text.Culture, Old = text.Text, New = replaced });
        }

        return (changes, inventory.Complete ? null : "the tag comment read is incomplete: " + string.Join(" ", inventory.Messages));
    }

    internal static async Task<(PlcTagCommentInventoryInfo? Inventory, string? Problem)> ReadAsync(OpennessWorkerClient client, PlcOperationRequest item, string plcName)
    {
        var read = new PlcOperationRequest { OperationId = item.OperationId, Operation = PlcSourceFolderMethods.ReadTagComments, PlcName = plcName, TableName = item.TableName };
        var projected = PlcPayloadContract.Project(read, await PlcSourceFolderOperations.InvokeReadAsync(client, read).ConfigureAwait(false));
        return projected.Result is { } result
            ? (CanonicalJson.Deserialize<PlcTagCommentInventoryInfo>(result.GetRawText()), null)
            : (null, projected.Failure?.Message);
    }

    /// <summary>Every comment text of the tags in scope: the PLC's own tag tables (no software unit), one table when named.</summary>
    internal static IEnumerable<(PlcTagTableCommentsInfo Table, PlcMemberCommentInfo Member, PlcCommentTextInfo Text)> Texts(
        PlcTagCommentInventoryInfo inventory, PlcOperationRequest item, string plcName)
    {
        var folder = Folder(item.FolderPath);
        foreach (var plc in inventory.Plcs.Where(p => PlcNameRules.Comparer.Equals(p.PlcName, plcName)))
        foreach (var table in plc.Tables.Where(t => t.UnitName is null && (item.TableName is null
                     || PlcNameRules.Comparer.Equals(t.TableName, item.TableName) && string.Equals(Folder(t.FolderPath), folder, StringComparison.OrdinalIgnoreCase))))
        foreach (var member in table.Members.Where(m => m.Kind == "Tag"))
        foreach (var text in member.Comment ?? new List<PlcCommentTextInfo>())
        {
            if (!string.IsNullOrEmpty(item.Culture) && !string.Equals(text.Culture, item.Culture, StringComparison.OrdinalIgnoreCase)) continue;
            yield return (table, member, text);
        }
    }

    internal static string ObjectPath(PlcTagTableCommentsInfo table, string tag) => $"{Folder(table.FolderPath).TrimEnd('/')}/{table.TableName}/{tag}";

    private static string Folder(string? path)
    {
        var segments = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? "/" : "/" + string.Join("/", segments);
    }
}
