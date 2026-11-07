namespace TiaMcpServer.Contracts.Shared.ClaudeInbox;

/// <summary>A request, reply or status file that does not follow the contract; the message never repeats file content.</summary>
public sealed class ClaudeInboxFormatException : Exception
{
    public ClaudeInboxFormatException(string message) : base(message)
    {
    }
}

/// <summary>Writes and strictly reads the inbox, outbox and status files (closed schemas, shared source with the Add-In).</summary>
public static class ClaudeInboxSerializer
{
    private static readonly string[] RequestMembers = { "schema", "id", "createdUtc", "source", "action", "text", "projectPath", "selection", "addInVersion" };
    private static readonly string[] SelectionMembers = { "kind", "name", "chain", "address" };
    private static readonly string[] ChainMembers = { "typeNames", "name" };
    private static readonly string[] AddressMembers = { "plcName", "unitName", "blockPath", "typePath", "tableName", "folderPath", "deviceName", "deviceItemName", "library", "libraryTypePath", "masterCopyName", "masterCopyFolderPath" };
    private static readonly string[] ReplyMembers = { "schema", "id", "repliedUtc", "status", "text", "from", "readUtc" };
    private static readonly string[] StatusMembers = { "schema", "id", "state", "updatedUtc", "message" };

    public static string Serialize(ClaudeRequest request)
    {
        Validate(request);
        return new MiniJsonObjectWriter()
            .String("schema", ClaudeInboxContract.RequestSchema)
            .String("id", request.Id)
            .String("createdUtc", request.CreatedUtc)
            .String("source", request.Source)
            .String("action", request.Action)
            .String("text", request.Text)
            .String("projectPath", request.ProjectPath)
            .Raw("selection", MiniJsonObjectWriter.Array(request.Selection.Select(item => Serialize(item, 2)), 1))
            .String("addInVersion", request.AddInVersion)
            .ToString();
    }

    private static string Serialize(ClaudeSelectionItem item, int level)
    {
        var a = item.Address;
        var address = new MiniJsonObjectWriter(level + 1)
            .String("plcName", a.PlcName).String("unitName", a.UnitName).String("blockPath", a.BlockPath).String("typePath", a.TypePath)
            .String("tableName", a.TableName).String("folderPath", a.FolderPath).String("deviceName", a.DeviceName)
            .String("deviceItemName", a.DeviceItemName).String("library", a.Library).String("libraryTypePath", a.LibraryTypePath)
            .String("masterCopyName", a.MasterCopyName).String("masterCopyFolderPath", a.MasterCopyFolderPath).ToString();
        var chain = MiniJsonObjectWriter.Array(item.Chain.Select(entry => new MiniJsonObjectWriter(level + 2)
            .Raw("typeNames", "[" + string.Join(", ", entry.TypeNames.Select(MiniJson.Quote)) + "]")
            .String("name", entry.Name).ToString()), level + 1);
        return new MiniJsonObjectWriter(level).String("kind", item.Kind).String("name", item.Name).Raw("chain", chain).Raw("address", address).ToString();
    }

    public static string Serialize(ClaudeReply reply)
    {
        Validate(reply);
        return new MiniJsonObjectWriter()
            .String("schema", ClaudeInboxContract.ReplySchema)
            .String("id", reply.Id)
            .String("repliedUtc", reply.RepliedUtc)
            .String("status", reply.Status)
            .String("text", reply.Text)
            .String("from", reply.From)
            .String("readUtc", reply.ReadUtc)
            .ToString();
    }

    public static string Serialize(ClaudeRequestStatus status)
    {
        if (!ClaudeInboxContract.IsValidId(status.Id)) throw new ClaudeInboxFormatException("The status id is not a valid request id.");
        return new MiniJsonObjectWriter()
            .String("schema", ClaudeInboxContract.StatusSchema)
            .String("id", status.Id)
            .String("state", status.State)
            .String("updatedUtc", status.UpdatedUtc)
            .String("message", Truncate(status.Message, 2000))
            .ToString();
    }

    public static ClaudeRequest ParseRequest(string json)
    {
        var root = Root(json, RequestMembers, ClaudeInboxContract.RequestSchema, "request");
        var selection = new List<ClaudeSelectionItem>();
        foreach (var value in RequiredArray(root, "selection"))
        {
            var item = Object(value, SelectionMembers, "selection item");
            var chain = new List<ClaudeChainEntry>();
            foreach (var entryValue in RequiredArray(item, "chain"))
            {
                var entry = Object(entryValue, ChainMembers, "chain entry");
                var types = RequiredArray(entry, "typeNames").Select(t => t as string ?? throw new ClaudeInboxFormatException("A chain type name is not a string.")).ToList();
                chain.Add(new ClaudeChainEntry(types, OptionalString(entry, "name")));
            }

            var address = Object(Required(item, "address"), AddressMembers, "address");
            selection.Add(new ClaudeSelectionItem(RequiredString(item, "kind"), OptionalString(item, "name"), chain, new ClaudeAddress(
                OptionalString(address, "plcName"), OptionalString(address, "unitName"), OptionalString(address, "blockPath"), OptionalString(address, "typePath"),
                OptionalString(address, "tableName"), OptionalString(address, "folderPath"), OptionalString(address, "deviceName"),
                OptionalString(address, "deviceItemName"), OptionalString(address, "library"), OptionalString(address, "libraryTypePath"),
                OptionalString(address, "masterCopyName"), OptionalString(address, "masterCopyFolderPath"))));
        }

        var request = new ClaudeRequest(RequiredString(root, "id"), RequiredString(root, "createdUtc"), RequiredString(root, "source"),
            RequiredString(root, "action"), OptionalString(root, "text"), OptionalString(root, "projectPath"), selection, OptionalString(root, "addInVersion"));
        Validate(request);
        return request;
    }

    public static ClaudeReply ParseReply(string json)
    {
        var root = Root(json, ReplyMembers, ClaudeInboxContract.ReplySchema, "reply");
        var reply = new ClaudeReply(RequiredString(root, "id"), RequiredString(root, "repliedUtc"), RequiredString(root, "status"),
            RequiredString(root, "text"), RequiredString(root, "from"), OptionalString(root, "readUtc"));
        Validate(reply);
        return reply;
    }

    public static ClaudeRequestStatus ParseStatus(string json)
    {
        var root = Root(json, StatusMembers, ClaudeInboxContract.StatusSchema, "status");
        var status = new ClaudeRequestStatus(RequiredString(root, "id"), RequiredString(root, "state"), RequiredString(root, "updatedUtc"), OptionalString(root, "message"));
        if (!ClaudeInboxContract.IsValidId(status.Id)) throw new ClaudeInboxFormatException("The status id is not a valid request id.");
        return status;
    }

    public static void Validate(ClaudeRequest request)
    {
        if (!ClaudeInboxContract.IsValidId(request.Id)) throw new ClaudeInboxFormatException("The request id is not valid.");
        if (!ClaudeSources.All.Contains(request.Source, StringComparer.Ordinal)) throw new ClaudeInboxFormatException("The request source is unknown.");
        if (!ClaudeActions.All.Contains(request.Action, StringComparer.Ordinal)) throw new ClaudeInboxFormatException("The request action is unknown.");
        if (request.Text is { Length: > ClaudeInboxContract.MaxTextChars }) throw new ClaudeInboxFormatException($"The request text is longer than {ClaudeInboxContract.MaxTextChars} characters.");
        if (request.Selection.Count > ClaudeInboxContract.MaxSelectionItems) throw new ClaudeInboxFormatException($"A request holds at most {ClaudeInboxContract.MaxSelectionItems} selected objects.");
        foreach (var item in request.Selection)
        {
            if (item.Chain.Count is 0 or > ClaudeInboxContract.MaxChainEntries) throw new ClaudeInboxFormatException($"A selection chain holds 1 to {ClaudeInboxContract.MaxChainEntries} entries.");
            foreach (var entry in item.Chain)
            {
                if (entry.TypeNames.Count is 0 or > ClaudeInboxContract.MaxTypeNamesPerEntry) throw new ClaudeInboxFormatException("A chain entry has no or too many type names.");
                if (entry.Name is { Length: > ClaudeInboxContract.MaxNameChars } || entry.TypeNames.Any(t => t.Length > ClaudeInboxContract.MaxNameChars))
                    throw new ClaudeInboxFormatException("A chain entry name is too long.");
            }
        }
    }

    public static void Validate(ClaudeReply reply)
    {
        if (!ClaudeInboxContract.IsValidId(reply.Id)) throw new ClaudeInboxFormatException("The reply id is not a valid request id.");
        if (reply.Status is not (ClaudeReply.StatusDone or ClaudeReply.StatusFailed)) throw new ClaudeInboxFormatException("The reply status must be done or failed.");
        if (reply.From is not (ClaudeReply.FromClaude or ClaudeReply.FromBridge)) throw new ClaudeInboxFormatException("The reply origin must be claude or bridge.");
        if (string.IsNullOrWhiteSpace(reply.Text)) throw new ClaudeInboxFormatException("The reply text is empty.");
        if (reply.Text.Length > ClaudeInboxContract.MaxReplyChars) throw new ClaudeInboxFormatException($"The reply text is longer than {ClaudeInboxContract.MaxReplyChars} characters.");
    }

    public static string? Truncate(string? text, int max) => text is null || text.Length <= max ? text : text.Substring(0, max) + "…";

    private static Dictionary<string, object?> Root(string json, string[] members, string schema, string what)
    {
        object? parsed;
        try
        {
            parsed = MiniJson.Parse(json);
        }
        catch (FormatException exception)
        {
            throw new ClaudeInboxFormatException($"The {what} file is not valid JSON ({exception.Message})");
        }

        var root = Object(parsed, members, what);
        if (!string.Equals(RequiredString(root, "schema"), schema, StringComparison.Ordinal))
            throw new ClaudeInboxFormatException($"The {what} file is not of schema {schema}.");
        return root;
    }

    private static Dictionary<string, object?> Object(object? value, string[] members, string what)
    {
        if (value is not Dictionary<string, object?> map) throw new ClaudeInboxFormatException($"The {what} is not a JSON object.");
        foreach (var key in map.Keys)
            if (!members.Contains(key, StringComparer.Ordinal))
                throw new ClaudeInboxFormatException($"The {what} has an unknown member (allowed: {string.Join(", ", members)}).");
        foreach (var member in members)
            if (!map.ContainsKey(member))
                throw new ClaudeInboxFormatException($"The {what} misses the member \"{member}\" (every member is written, null when not set).");
        return map;
    }

    private static object? Required(Dictionary<string, object?> map, string name) => map[name];

    private static string RequiredString(Dictionary<string, object?> map, string name)
        => map[name] as string ?? throw new ClaudeInboxFormatException($"The member \"{name}\" must be a string.");

    private static string? OptionalString(Dictionary<string, object?> map, string name)
        => map[name] switch
        {
            null => null,
            string s => s,
            _ => throw new ClaudeInboxFormatException($"The member \"{name}\" must be a string or null."),
        };

    private static List<object?> RequiredArray(Dictionary<string, object?> map, string name)
        => map[name] as List<object?> ?? throw new ClaudeInboxFormatException($"The member \"{name}\" must be an array.");
}
