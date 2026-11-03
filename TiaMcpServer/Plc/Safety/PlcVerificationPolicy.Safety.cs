using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The F-runtime group check of block imports (live 2026-11-02, TIA Portal V21, E7/L1): an import that replaced the main
/// safety block of an F-runtime group left the group without its MainSafetyBlockName. SD writes onto a main safety block now
/// take the temporary F-block route (<see cref="PlcMainSafetyBlockSdRoute"/>); this check stays on every route as the safety
/// net (generic: any import that replaces a block a group names). The planner recorded which groups
/// named the block (<see cref="PlcMainSafetyLinkPlan"/>); after the item the groups are read again with
/// get_safety_administration, and a reference that no longer names the block fails the item's verification with check
/// <see cref="PlcMainSafetyLinks.Check"/> and the repair. The worker already tried to restore it (its warning says how).
/// </summary>
public sealed partial class PlcVerificationPolicy
{
    private IReadOnlyDictionary<string, PlcMainSafetyLinkPlan> _safetyLinks = new Dictionary<string, PlcMainSafetyLinkPlan>();

    /// <summary>Null when every recorded reference still names the block; otherwise the failed entry replacing <paramref name="entry"/>.</summary>
    private async Task<WriteOperationVerification?> MainSafetyLinksReadBackAsync(PlcOperationRequest item, PlcMainSafetyLinkPlan plan, WriteOperationVerification entry)
    {
        var expected = string.Join("; ", plan.Links.SelectMany(link => PlcMainSafetyLinks.References(link, plan.BlockName)
            .Select(r => $"'{link.RuntimeGroup}' {PlcSafetyRuntimeGroupBinding.ReadBackAttribute(r.Member)} = '{r.Expected}'")));
        var read = await client.PlcSafetyAsync(PlcSafetyMethods.GetAdministration, new PlcSafetyRequestInfo { PlcName = plan.PlcName }).ConfigureAwait(false);
        PlcSafetyCpuInfo? cpu = null;
        string? problem = null;
        if (!read.Success) problem = $"get_safety_administration failed: {read.Error}";
        else
        {
            try
            {
                var matches = PlcSafetyContract.Administration(read.Payload).Plcs
                    .Where(p => PlcNameRules.Comparer.Equals(p.PlcName, plan.PlcName) && (plan.DeviceName is null || PlcNameRules.Comparer.Equals(p.DeviceName, plan.DeviceName))).ToList();
                if (matches.Count == 1) cpu = matches[0];
                else problem = $"the safety administration read does not describe PLC '{plan.PlcName}' exactly once";
            }
            catch (JsonException)
            {
                problem = "the worker's safety administration payload did not match its declared contract";
            }
        }

        var lost = new List<string>();
        if (cpu is not null)
        {
            foreach (var link in plan.Links)
            {
                var group = cpu.RuntimeGroups.FirstOrDefault(g => string.Equals(g.Name, link.RuntimeGroup, StringComparison.OrdinalIgnoreCase));
                var now = group is null ? null : PlcSafetyPlanning.Link(group);
                foreach (var (member, name) in PlcMainSafetyLinks.References(link, plan.BlockName))
                {
                    var value = PlcMainSafetyLinks.Value(now, member);
                    if (!PlcMainSafetyLinks.Same(value, name))
                        lost.Add($"'{link.RuntimeGroup}' {PlcSafetyRuntimeGroupBinding.ReadBackAttribute(member)} = '{(group is null ? "group not found" : value ?? "null")}'");
                }
            }
        }

        // K34 (live 2026-11-02): an intact reference used to leave no trace in the verification, so the announced re-read was
        // invisible. The content check stays the entry's check; its message now says the groups were re-read and still match.
        if (problem is null && lost.Count == 0)
            return entry with { Message = $"{entry.Message} F-runtime group reference(s) re-read after the import and intact: {expected} (check {PlcMainSafetyLinks.Check})." };
        if (entry.Check == "contentHash" && entry.Actual is { } hash) _contentHashes[item.OperationId] = hash;
        var repair = "Repair in TIA Portal Safety Administration (F-runtime groups: select the main safety block again), then compile_check; "
            + "plc_write set_safety_runtime_group (runtimeGroupName, mainSafetyBlockPath and, for an F-FB, mainSafetyInstanceDbPath) tries it through Openness, "
            + "but on TIA Portal V21 (live 2026-11-02) the RuntimeGroup offered no member for it (api_member_missing; get_safety_administration attributeInfos lists what is writable).";
        return new WriteOperationVerification(item.OperationId, false, PlcMainSafetyLinks.Check, expected, problem ?? string.Join("; ", lost),
            problem is not null
                ? $"Operation '{item.OperationId}' replaced '{plan.BlockName}', the main safety block (or IDB) of F-runtime group(s) {string.Join(", ", plan.Links.Select(l => $"'{l.RuntimeGroup}'"))}, "
                  + $"and the groups could not be read again ({problem}); whether they still reference it is unknown. {repair}"
                : $"Operation '{item.OperationId}' replaced '{plan.BlockName}', and afterwards {string.Join("; ", lost)} (TIA Portal dropped the reference, as a SIMATIC SD import "
                  + $"did live on V21 on 2026-11-02, and the worker could not restore it; see the item's warnings). The PLC has no valid main safety block until this is repaired. {repair}"
                  + (entry.Success ? string.Empty : $" The content check also failed: {entry.Message}"));
    }
}
