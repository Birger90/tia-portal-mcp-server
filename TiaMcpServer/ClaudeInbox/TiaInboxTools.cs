using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Shared.ClaudeInbox;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.ClaudeInbox;

/// <summary>Where the inbox and outbox of "Claude in TIA" live (one per host process; tests pass a temporary root).</summary>
public sealed class ClaudeInboxLocation
{
    public ClaudeInboxLocation(string? root = null)
    {
        Root = root ?? ClaudeInboxStore.DefaultRoot(Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) ?? Path.Combine(Path.GetTempPath(), ClaudeInboxStore.DirectoryName);
    }

    public string Root { get; }

    public ClaudeInboxStore Store() => new(Root);
}

/// <summary>One tia_inbox item. Members not used by the operation must be omitted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TiaInboxOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    [Description("get_request and reply_request: the request id as list_requests reports it (yyyyMMddTHHmmssZ-xxxxxxxx).")]
    public string? Id { get; set; }

    [Description("list_requests only: true also lists requests that already have a reply. Default false (pending only).")]
    public bool? IncludeAnswered { get; set; }

    [Description("reply_request only: the answer the user reads in TIA Portal (\"Claude-Antworten anzeigen\"); plain text, 1-60000 characters, in the user's language.")]
    public string? Text { get; set; }

    [Description("reply_request only: done (default) or failed (the task could not be done; say why in text).")]
    public string? Status { get; set; }
}

public static class TiaInboxContractVersion
{
    public const string Current = "1.0";
}

public sealed record TiaInboxRequestSummaryInfo(
    string Id, string CreatedUtc, string Source, string Action, string? Text, string? ProjectPath, int SelectionCount,
    bool Answered, string? BridgeState, string? Error);

public sealed record TiaInboxListInfo(string InboxDirectory, string OutboxDirectory, int PendingCount, IReadOnlyList<TiaInboxRequestSummaryInfo> Requests);

public sealed record TiaInboxChainEntryInfo(IReadOnlyList<string> TypeNames, string? Name);

public sealed record TiaInboxAddressInfo(
    string? PlcName, string? UnitName, string? BlockPath, string? TypePath, string? TableName, string? FolderPath, string? DeviceName,
    string? DeviceItemName, string? Library, string? LibraryTypePath, string? MasterCopyName, string? MasterCopyFolderPath);

public sealed record TiaInboxSelectionInfo(string Kind, string? Name, TiaInboxAddressInfo Address, IReadOnlyList<TiaInboxChainEntryInfo> Chain);

public sealed record TiaInboxReplyInfo(string Status, string RepliedUtc, string From, string Text, string? ReadUtc);

public sealed record TiaInboxRequestInfo(
    string Id, string CreatedUtc, string Source, string Action, string Instruction, string? Text, string? ProjectPath,
    IReadOnlyList<TiaInboxSelectionInfo> Selection, string? AddInVersion, TiaInboxReplyInfo? Reply, string? BridgeState);

public sealed record TiaInboxReplyResultInfo(string Id, string Status, string RepliedUtc, string ReplyFile);

/// <summary>
/// <c>tia_inbox</c>: the MCP side of "Claude in TIA". The TIA Portal Add-In writes hand-over requests into the inbox; this
/// tool lists and reads them and writes Claude's reply into the outbox, where the Add-In shows it. It never touches TIA
/// Portal, the project or the binding, so it is offered in every access mode; its only write is the reply file.
/// </summary>
[McpServerToolType]
public sealed class TiaInboxTools
{
    internal const string ToolName = "tia_inbox";
    internal const string ListRequests = "list_requests";
    internal const string GetRequest = "get_request";
    internal const string ReplyRequest = "reply_request";

    private static readonly IReadOnlyDictionary<string, string[]> Fields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [ListRequests] = new[] { "includeAnswered" },
        [GetRequest] = new[] { "id" },
        [ReplyRequest] = new[] { "id", "text", "status" },
    };

    [McpServerTool(
        Name = ToolName,
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Requests the user handed to Claude from inside TIA Portal through the \"Claude in TIA\" Add-In (context menu \"Claude\" in the project tree, devices & networks and the project and global libraries; not live-tested). "
        + "MCP cannot push to Claude: check this tool when the user says \"schau in TIA\", \"look in TIA\" or refers to a request from TIA Portal, and at the start of a TIA session. "
        + "Operations (up to 50, independent): list_requests (optional includeAnswered; pending requests oldest first: id, createdUtc, source, action, text, projectPath, selectionCount, answered, bridgeState, error), "
        + "get_request (id: the whole request: action with its instruction, optional text, projectPath, and every selected object with kind, name, address hints (plcName, blockPath PLC_1/Blocks/Folder/Block, typePath, tableName+folderPath, deviceName, deviceItemName, library, libraryTypePath, masterCopyName+masterCopyFolderPath) and the raw parent chain of TIA Portal type names and names), "
        + "reply_request (id, text, optional status done|failed: writes the answer the user sees in TIA Portal under \"Claude-Antworten anzeigen\"; one reply per request, a second is refused already_answered). "
        + "Work on a request with the other tools: bind_project to projectPath if needed, then confirm each address with browse_project_tree/plc_read/network_read/library_read before using it (the addresses are hints built from names). "
        + "The request holds names only, never block content; the access mode still decides what may be changed. Finish every request with reply_request. Available in every mode; touches neither TIA Portal nor the project, only the reply file in %APPDATA%\\TiaMcpServer\\outbox.")]
    public static Task<CallToolResult> TiaInbox(
        ClaudeInboxLocation location,
        [Description("Ordered tia_inbox operations, each with a unique operationId and an operation.")] TiaInboxOperationRequest[] operations,
        CancellationToken cancellationToken = default)
        => Run(location, operations, TimeProvider.System, cancellationToken);

    internal static async Task<CallToolResult> Run(ClaudeInboxLocation location, IReadOnlyList<TiaInboxOperationRequest>? operations, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (Validate(operations) is { } error)
            return Error(WorkerFailureCategories.ValidationError, error);
        var store = location.Store();
        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync<TiaInboxOperationRequest>(operations!,
            item => Task.FromResult(Execute(store, item, clock)), cancellationToken).ConfigureAwait(false);
        var bounded = BatchReadTool.ApplyBudget(batch, ToolName, TiaInboxContractVersion.Current, RetryGuidance);
        return StructuredToolResult.Create(new OperationBatchReadResponse(ToolName, TiaInboxContractVersion.Current,
            bounded.IsFullySuccessful, null, Array.Empty<string>(), bounded), isError: false);
    }

    internal static string? Validate(IReadOnlyList<TiaInboxOperationRequest>? operations)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError) return countError;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError)
            {
                errors.Add(itemError);
                continue;
            }

            var prefix = $"{item.OperationId}: ";
            if (!Fields.TryGetValue(item.Operation ?? string.Empty, out var allowed))
            {
                errors.Add(prefix + $"unknown operation '{item.Operation}'; valid: {string.Join(", ", Fields.Keys)}.");
                continue;
            }

            var given = new List<string>();
            if (item.Id is not null) given.Add("id");
            if (item.IncludeAnswered is not null) given.Add("includeAnswered");
            if (item.Text is not null) given.Add("text");
            if (item.Status is not null) given.Add("status");
            foreach (var field in given.Where(f => !allowed.Contains(f, StringComparer.Ordinal)))
                errors.Add(prefix + $"{field} is not a field of {item.Operation}.");
            if (item.Operation is GetRequest or ReplyRequest && !ClaudeInboxContract.IsValidId(item.Id))
                errors.Add(prefix + "id is required and must be a request id as list_requests reports it (yyyyMMddTHHmmssZ-xxxxxxxx).");
            if (item.Operation == ReplyRequest)
            {
                if (string.IsNullOrWhiteSpace(item.Text)) errors.Add(prefix + "text is required.");
                else if (item.Text.Length > ClaudeInboxContract.MaxReplyChars) errors.Add(prefix + $"text has at most {ClaudeInboxContract.MaxReplyChars} characters.");
                if (item.Status is not null and not (ClaudeReply.StatusDone or ClaudeReply.StatusFailed)) errors.Add(prefix + "status is done or failed.");
            }
        }

        return errors.Count == 0 ? null : string.Join("\n", errors);
    }

    internal static StructuredOperationItem Execute(ClaudeInboxStore store, TiaInboxOperationRequest item, TimeProvider clock)
    {
        try
        {
            switch (item.Operation)
            {
                case ListRequests:
                {
                    var all = store.List();
                    var shown = item.IncludeAnswered == true ? all : all.Where(e => e.IsPending).ToList();
                    return Succeeded(item, new TiaInboxListInfo(store.InboxDirectory, store.OutboxDirectory, all.Count(e => e.IsPending),
                        shown.Select(Summary).ToList()));
                }
                case GetRequest:
                {
                    var entry = store.Get(item.Id!);
                    if (entry.Request is null)
                        return StructuredOperationItem.Failed(item, WorkerFailureCategories.ValidationError, $"The request file {item.Id} cannot be used: {entry.Error}", Array.Empty<string>());
                    return Succeeded(item, Details(entry.Request, entry.Reply, entry.Status));
                }
                case ReplyRequest:
                {
                    var reply = store.WriteReply(item.Id!, item.Status ?? ClaudeReply.StatusDone, item.Text!, ClaudeReply.FromClaude, clock.GetUtcNow().UtcDateTime);
                    return Succeeded(item, new TiaInboxReplyResultInfo(reply.Id, reply.Status, reply.RepliedUtc, store.ReplyPath(reply.Id)));
                }
                default:
                    return StructuredOperationItem.Failed(item, WorkerFailureCategories.ValidationError, "Unknown operation.", Array.Empty<string>());
            }
        }
        catch (ClaudeInboxException exception)
        {
            var category = exception.Category switch
            {
                "not_found" => WorkerFailureCategories.TargetNotFound,
                "already_answered" => WorkerFailureCategories.StateChanged,
                "invalid" => WorkerFailureCategories.ValidationError,
                _ => WorkerFailureCategories.WorkerOperationFailed,
            };
            return StructuredOperationItem.Failed(item, category, exception.Message, Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return StructuredOperationItem.Failed(item, WorkerFailureCategories.WorkerOperationFailed,
                $"The inbox could not be read or written ({exception.GetType().Name}).", Array.Empty<string>());
        }
    }

    private static TiaInboxRequestSummaryInfo Summary(ClaudeInboxEntry entry)
        => entry.Request is { } r
            ? new TiaInboxRequestSummaryInfo(r.Id, r.CreatedUtc, r.Source, r.Action, r.Text, r.ProjectPath, r.Selection.Count, !entry.IsPending, entry.Status?.State, null)
            : new TiaInboxRequestSummaryInfo(entry.Id, "", "", "", null, null, 0, !entry.IsPending, entry.Status?.State, entry.Error);

    internal static TiaInboxRequestInfo Details(ClaudeRequest r, ClaudeReply? reply, ClaudeRequestStatus? status)
        => new(r.Id, r.CreatedUtc, r.Source, r.Action, ClaudeActions.Instruction(r.Action), r.Text, r.ProjectPath,
            r.Selection.Select(s => new TiaInboxSelectionInfo(s.Kind, s.Name, new TiaInboxAddressInfo(
                    s.Address.PlcName, s.Address.UnitName, s.Address.BlockPath, s.Address.TypePath, s.Address.TableName, s.Address.FolderPath,
                    s.Address.DeviceName, s.Address.DeviceItemName, s.Address.Library, s.Address.LibraryTypePath, s.Address.MasterCopyName,
                    s.Address.MasterCopyFolderPath),
                s.Chain.Select(c => new TiaInboxChainEntryInfo(c.TypeNames, c.Name)).ToList())).ToList(),
            r.AddInVersion,
            reply is null ? null : new TiaInboxReplyInfo(reply.Status, reply.RepliedUtc, reply.From, reply.Text, reply.ReadUtc),
            status?.State);

    private static StructuredOperationItem Succeeded<T>(TiaInboxOperationRequest item, T value)
        => new(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, CanonicalJson.ToElement(value), null, null, null, Array.Empty<string>());

    internal static string RetryGuidance(StructuredOperationItem item)
        => "Re-run this operationId in its own tia_inbox call; for one value above the limit, raise --max-item-chars and --max-document-chars.";

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, TiaInboxContractVersion.Current, category, message);
}
