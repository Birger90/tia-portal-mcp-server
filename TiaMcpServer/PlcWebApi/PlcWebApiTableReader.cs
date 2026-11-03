using System.Text.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Plc;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcWebApi;

/// <summary>One table entry with its mapping and, when mapped, the value the PLC returned.</summary>
public sealed record PlcWebApiTableLiveEntry(
    int Index,
    string? Name,
    string? Address,
    string? WebApiName,
    string? Mapping,
    string? UnmappedReason,
    bool? Success,
    JsonElement? Value,
    string? FailureCategory,
    string? FailureMessage,
    PlcWebApiErrorInfo? WebApiError);

/// <summary>
/// <c>read_watch_table_live</c>: the entries of an offline watch table or tag table and, for each
/// mappable entry, its live value. <see cref="Read"/> counts distinct names read successfully.
/// </summary>
public sealed record PlcWebApiTableLiveResult(
    string Target,
    string PlcName,
    string? DeviceName,
    string SourceKind,
    string TableName,
    string FolderPath,
    int EntryCount,
    bool EntriesComplete,
    int Mapped,
    int Unmapped,
    int Read,
    IReadOnlyList<PlcWebApiTableLiveEntry> Entries,
    IReadOnlyList<string> MappingRules,
    IReadOnlyList<string> Notes);

/// <summary>
/// Reads a table of the bound project through Openness (the existing <c>read_watch_table</c> and
/// <c>list_tag_tables</c> worker reads, binding rules unchanged) and its symbols through the existing
/// per-name <c>PlcProgram.Read</c> path of <c>read_tags</c>. The two sides are paired only by the caller.
/// </summary>
internal static class PlcWebApiTableReader
{
    public const string Operation = "read_watch_table_live";
    public const int DefaultEntries = 100;

    private static readonly IReadOnlyList<string> Notes = new[]
    {
        "target and plcName are paired by the caller: the server does not check that the CPU behind target runs this project's program.",
        "Entries are read offline from the bound project, values online one PlcProgram.Read request per distinct name, in entry order; a refused name is recorded and the next is read, a lost connection or session stops the reads.",
        "Not live-tested: the format of watch-table entry names (PlcWatchTableEntry.Name) is not documented, so only names matching a tag of the PLC or a documented Web API notation are read.",
    };

    public static async Task<StructuredOperationItem> ExecuteAsync(PlcWebApiService service, OpennessWorkerClient? workerClient,
        PlcOnlineReadRequest item, CancellationToken cancellationToken)
    {
        var target = service.Configuration.Find(item.Target);
        if (target is null)
            return PlcWebApiReader.Failed(item, WorkerFailureCategories.TargetNotFound, PlcWebApiWarnings.NotConfigured(service.Configuration, item.Target), null,
                Array.Empty<string>());
        var warnings = PlcWebApiWarnings.ForTarget(target).ToList();
        if (workerClient is null)
            return Fail(item, WorkerFailureCategories.BindingConflict, "read_watch_table_live needs the Openness worker and a bound project.", warnings);

        // Binding rules of every content read: no verified binding, no Openness read.
        var binding = await workerClient.RequireVerifiedBindingAsync().ConfigureAwait(false);
        if (!binding.Success) return Fail(item, binding.FailureCategory!, binding.Error!, warnings);

        var max = item.Count ?? DefaultEntries;
        var inventoryRead = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = "list_tag_tables", PlcName = item.PlcName },
            await workerClient.ListTagTablesAsync(item.PlcName, item.TagTable, item.TagTable is null ? null : item.FolderPath).ConfigureAwait(false));
        warnings.AddRange(inventoryRead.Warnings);
        if (inventoryRead.Result is not { } inventoryElement)
            return Fail(item, inventoryRead.Failure!.Category, $"The PLC tag inventory could not be read: {inventoryRead.Failure.Message}", warnings);
        var inventory = CanonicalJson.Deserialize<PlcTagInventoryInfo>(inventoryElement.GetRawText());
        if (inventory.Plcs.Count != 1)
            return Fail(item, inventory.Plcs.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                inventory.Plcs.Count == 0
                    ? $"{(item.PlcName is null ? "The project has no PLC" : $"No PLC matches '{item.PlcName}'")}."
                    : $"{inventory.Plcs.Count} PLCs match ({string.Join(", ", inventory.Plcs.Select(p => $"'{p.PlcName}'"))}). Name the PLC with plcName.", warnings);
        var plc = inventory.Plcs[0];
        if (!inventory.IsComplete) warnings.Add("The PLC tag inventory is incomplete; a watch-table entry naming an unread tag is not mapped by the plcTag rule.");

        var entries = new List<(int Index, string? Name, string? Address, PlcWebApiSymbolMapping.Mapping Mapping)>();
        string tableName, folderPath, kind;
        int entryCount;
        bool complete;
        if (item.Table is not null)
        {
            var tableRead = PlcPayloadContract.Project(new PlcOperationRequest { OperationId = item.OperationId, Operation = PlcOnlineMethods.ReadWatchTable },
                await workerClient.ReadWatchOrForceTableAsync(PlcOnlineMethods.ReadWatchTable, plc.PlcName, item.Table, item.FolderPath, max).ConfigureAwait(false));
            warnings.AddRange(tableRead.Warnings);
            if (tableRead.Result is not { } tableElement)
                return Fail(item, tableRead.Failure!.Category, $"The watch table could not be read: {tableRead.Failure.Message}", warnings);
            var table = CanonicalJson.Deserialize<PlcWatchTableReadInfo>(tableElement.GetRawText());
            var tags = PlcWebApiSymbolMapping.TagNames(plc);
            foreach (var entry in table.Entries)
                entries.Add((entry.Index, entry.Name, entry.Address, PlcWebApiSymbolMapping.MapWatchEntry(entry.Name, tags)));
            (kind, tableName, folderPath, entryCount, complete) = ("watchTable", table.Name, table.GroupPath, table.EntryCount, table.EntriesComplete);
        }
        else
        {
            var table = plc.Tables.Count == 1 ? plc.Tables[0] : null;
            if (table is null)
                return Fail(item, plc.Tables.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                    plc.Tables.Count == 0 ? $"Tag table '{item.TagTable}' was not found in PLC '{plc.PlcName}'." : $"Tag table '{item.TagTable}' is ambiguous; narrow it with folderPath.", warnings);
            var index = 0;
            foreach (var tag in table.Tags.Take(max))
                entries.Add((index++, tag.Name, tag.LogicalAddress, PlcWebApiSymbolMapping.MapTag(tag.Name)));
            foreach (var constant in table.UserConstants.Take(Math.Max(0, max - entries.Count)))
                entries.Add((index++, constant.Name, null, new(null, null, "A user constant is not a PLC variable; no Web API name is documented for it.")));
            var total = table.Tags.Count + table.UserConstants.Count;
            (kind, tableName, folderPath, entryCount, complete) = ("tagTable", table.Name, table.FolderPath, entries.Count, entries.Count == total);
        }

        // One request per distinct name, in entry order; the read_tags stop rules apply.
        var names = entries.Where(e => e.Mapping.WebApiName is not null).Select(e => e.Mapping.WebApiName!).Distinct(StringComparer.Ordinal).ToList();
        var (values, first) = await PlcWebApiReader.ReadNamesAsync(service, target, names, item.Representation, cancellationToken).ConfigureAwait(false);
        var byName = values.ToDictionary(value => value.Name, StringComparer.Ordinal);
        var projected = entries.Select(entry =>
        {
            if (entry.Mapping.WebApiName is null)
                return new PlcWebApiTableLiveEntry(entry.Index, entry.Name, entry.Address, null, null, entry.Mapping.UnmappedReason, null, null, null, null, null);
            byName.TryGetValue(entry.Mapping.WebApiName, out var value);
            return new PlcWebApiTableLiveEntry(entry.Index, entry.Name, entry.Address, entry.Mapping.WebApiName, entry.Mapping.Rule, null,
                value?.Success ?? false, value?.Value, value?.FailureCategory,
                value?.FailureMessage ?? (value is null ? "Not read: an earlier name lost the connection or the session." : null), value?.WebApiError);
        }).ToArray();

        var mapped = projected.Count(e => e.WebApiName is not null);
        var read = values.Count(v => v.Success);
        if (mapped < projected.Length) warnings.Add($"{projected.Length - mapped} of {projected.Length} entries could not be mapped to a Web API name and were not read; see unmappedReason.");
        if (!complete) warnings.Add($"Only the first {entries.Count} of the table's entries were read; raise count (at most {PlcWebApiCatalog.MaxEntries}).");
        var element = CanonicalJson.ToElement(new PlcWebApiTableLiveResult(target.Name, plc.PlcName, plc.DeviceName, kind, tableName, folderPath, entryCount,
            complete, mapped, projected.Length - mapped, read, projected, PlcWebApiSymbolMapping.Rules, Notes));
        if (first is null)
            return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null, warnings);
        return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Failed, element,
            new StructuredOperationFailure(first.Category, $"{names.Count - read} of {names.Count} mapped names could not be read"
                + (values.Count < names.Count ? $" (stopped after {values.Count} because of {first.Category})" : string.Empty)
                + $"; first failure: {first.Message}"), null, null, warnings);
    }

    private static StructuredOperationItem Fail(PlcOnlineReadRequest item, string category, string message, IReadOnlyList<string> warnings)
        => new(item.OperationId, item.Operation, OperationBatchStatus.Failed, null, new StructuredOperationFailure(category, message), null, null, warnings);
}
