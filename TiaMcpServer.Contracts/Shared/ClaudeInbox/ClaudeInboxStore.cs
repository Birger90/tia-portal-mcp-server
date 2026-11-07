using System.Text;

namespace TiaMcpServer.Contracts.Shared.ClaudeInbox;

/// <summary>A request in the inbox, as listed: the parsed request or the reason it was refused, plus its reply and status.</summary>
public sealed class ClaudeInboxEntry
{
    public ClaudeInboxEntry(string id, ClaudeRequest? request, string? error, ClaudeReply? reply, ClaudeRequestStatus? status)
    {
        Id = id;
        Request = request;
        Error = error;
        Reply = reply;
        Status = status;
    }

    public string Id { get; }

    public ClaudeRequest? Request { get; }

    /// <summary>Why the request file is refused (invalid JSON, wrong schema, too large), or null.</summary>
    public string? Error { get; }

    public ClaudeReply? Reply { get; }

    public ClaudeRequestStatus? Status { get; }

    public bool IsPending => Reply is null;
}

/// <summary>A store operation that is refused (unknown id, already answered, ...).</summary>
public sealed class ClaudeInboxException : Exception
{
    public ClaudeInboxException(string category, string message) : base(message)
    {
        Category = category;
    }

    /// <summary><c>not_found</c>, <c>already_answered</c>, <c>invalid</c> or <c>io_error</c>.</summary>
    public string Category { get; }
}

/// <summary>
/// The inbox and outbox folders below one root (default <c>%APPDATA%\TiaMcpServer</c>): requests are
/// <c>inbox\&lt;id&gt;.json</c>, replies <c>outbox\&lt;id&gt;.json</c>, the bridge's processing state
/// <c>outbox\&lt;id&gt;.status.json</c>. Every file is written through a temporary file in the same folder and then
/// moved into place, so a reader sees a whole file or none. A reply is written once: a second reply for the same id is
/// refused. Shared source with the Add-In (no dependency beyond the base class library).
/// </summary>
public sealed class ClaudeInboxStore
{
    public const string DirectoryName = "TiaMcpServer";
    public const string InboxFolder = "inbox";
    public const string OutboxFolder = "outbox";
    public const string RootEnvironmentVariable = "TIA_MCP_INBOX_ROOT";
    public const long MaxFileBytes = 1024 * 1024;
    private const string StatusSuffix = ".status.json";

    public ClaudeInboxStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("The inbox root is empty.", nameof(root));
        Root = Path.GetFullPath(root);
        InboxDirectory = Path.Combine(Root, InboxFolder);
        OutboxDirectory = Path.Combine(Root, OutboxFolder);
    }

    public string Root { get; }

    public string InboxDirectory { get; }

    public string OutboxDirectory { get; }

    /// <summary>The root of this user: <see cref="RootEnvironmentVariable"/> when set (tests, a non-default setup), else <c>%APPDATA%\TiaMcpServer</c>.</summary>
    public static string? DefaultRoot(Func<string, string?> readEnvironment, string? applicationData)
    {
        var configured = readEnvironment(RootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return string.IsNullOrEmpty(applicationData) ? null : Path.Combine(applicationData, DirectoryName);
    }

    public string RequestPath(string id) => Path.Combine(InboxDirectory, RequireId(id) + ".json");

    public string ReplyPath(string id) => Path.Combine(OutboxDirectory, RequireId(id) + ".json");

    public string StatusPath(string id) => Path.Combine(OutboxDirectory, RequireId(id) + StatusSuffix);

    /// <summary>Writes a new request; refuses an id that already exists.</summary>
    public string WriteRequest(ClaudeRequest request)
    {
        var json = ClaudeInboxSerializer.Serialize(request);
        var path = RequestPath(request.Id);
        WriteNew(path, json);
        return path;
    }

    /// <summary>Every request in the inbox, oldest first (ids sort by time).</summary>
    public IReadOnlyList<ClaudeInboxEntry> List()
    {
        if (!Directory.Exists(InboxDirectory)) return Array.Empty<ClaudeInboxEntry>();
        var entries = new List<ClaudeInboxEntry>();
        foreach (var file in Directory.GetFiles(InboxDirectory, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!ClaudeInboxContract.IsValidId(id)) continue;
            entries.Add(Entry(id));
        }

        return entries.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The requests without a reply, oldest first.</summary>
    public IReadOnlyList<ClaudeInboxEntry> Pending() => List().Where(e => e.IsPending).ToList();

    public ClaudeInboxEntry Get(string id)
    {
        if (!File.Exists(RequestPath(id))) throw new ClaudeInboxException("not_found", $"There is no request {id} in the inbox.");
        return Entry(id);
    }

    public ClaudeRequest ReadRequest(string id)
    {
        var entry = Get(id);
        return entry.Request ?? throw new ClaudeInboxException("invalid", $"The request {id} cannot be used: {entry.Error}");
    }

    public ClaudeReply? ReadReply(string id) => ReadOptional(ReplyPath(id), ClaudeInboxSerializer.ParseReply);

    public ClaudeRequestStatus? ReadStatus(string id) => ReadOptional(StatusPath(id), ClaudeInboxSerializer.ParseStatus);

    /// <summary>Writes the reply to a request in the inbox; a second reply for the same id is refused (<c>already_answered</c>).</summary>
    public ClaudeReply WriteReply(string id, string status, string text, string from, DateTime utcNow)
    {
        ReadRequest(id);
        var reply = new ClaudeReply(id, ClaudeInboxContract.FormatTime(utcNow), status, text, from, null);
        string json;
        try
        {
            json = ClaudeInboxSerializer.Serialize(reply);
        }
        catch (ClaudeInboxFormatException exception)
        {
            throw new ClaudeInboxException("invalid", exception.Message);
        }

        var path = ReplyPath(id);
        if (File.Exists(path)) throw new ClaudeInboxException("already_answered", $"The request {id} already has a reply; it is not overwritten.");
        try
        {
            WriteNew(path, json);
        }
        catch (IOException) when (File.Exists(path))
        {
            throw new ClaudeInboxException("already_answered", $"The request {id} already has a reply; it is not overwritten.");
        }

        return reply;
    }

    /// <summary>Every reply in the outbox, oldest first; with <paramref name="unreadOnly"/> only those the user has not seen.</summary>
    public IReadOnlyList<ClaudeReply> Replies(bool unreadOnly)
    {
        if (!Directory.Exists(OutboxDirectory)) return Array.Empty<ClaudeReply>();
        var replies = new List<ClaudeReply>();
        foreach (var file in Directory.GetFiles(OutboxDirectory, "*.json"))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(StatusSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var id = Path.GetFileNameWithoutExtension(file);
            if (!ClaudeInboxContract.IsValidId(id)) continue;
            ClaudeReply? reply;
            try
            {
                reply = ReadReply(id);
            }
            catch (ClaudeInboxFormatException)
            {
                continue;
            }

            if (reply is not null && (!unreadOnly || reply.ReadUtc is null)) replies.Add(reply);
        }

        return replies.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Marks a reply as read (the Add-In after showing it).</summary>
    public void MarkRead(string id, DateTime utcNow)
    {
        var reply = ReadReply(id) ?? throw new ClaudeInboxException("not_found", $"There is no reply {id} in the outbox.");
        if (reply.ReadUtc is not null) return;
        WriteReplace(ReplyPath(id), ClaudeInboxSerializer.Serialize(reply.MarkRead(utcNow)));
    }

    /// <summary>Writes (or replaces) the bridge's processing state of a request.</summary>
    public void WriteStatus(ClaudeRequestStatus status) => WriteReplace(StatusPath(status.Id), ClaudeInboxSerializer.Serialize(status));

    private ClaudeInboxEntry Entry(string id)
    {
        ClaudeRequest? request = null;
        string? error = null;
        try
        {
            request = ClaudeInboxSerializer.ParseRequest(ReadLimited(RequestPath(id)));
            if (!string.Equals(request.Id, id, StringComparison.Ordinal))
            {
                request = null;
                error = "The id inside the file differs from its file name.";
            }
        }
        catch (ClaudeInboxFormatException exception)
        {
            error = exception.Message;
        }
        catch (IOException exception)
        {
            error = "The file could not be read (" + exception.GetType().Name + ").";
        }

        ClaudeReply? reply = null;
        ClaudeRequestStatus? status = null;
        try
        {
            reply = ReadReply(id);
        }
        catch (ClaudeInboxFormatException)
        {
        }

        try
        {
            status = ReadStatus(id);
        }
        catch (ClaudeInboxFormatException)
        {
        }

        return new ClaudeInboxEntry(id, request, error, reply, status);
    }

    private static T? ReadOptional<T>(string path, Func<string, T> parse) where T : class
        => File.Exists(path) ? parse(ReadLimited(path)) : null;

    private static string ReadLimited(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes) throw new ClaudeInboxFormatException($"The file is larger than {MaxFileBytes} bytes.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string RequireId(string id)
        => ClaudeInboxContract.IsValidId(id) ? id : throw new ClaudeInboxException("invalid", "The id is not a valid request id (yyyyMMddTHHmmssZ-xxxxxxxx).");

    private static void WriteNew(string path, string json)
    {
        var temporary = Temporary(path, json);
        try
        {
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteReplace(string path, string json)
    {
        var temporary = Temporary(path, json);
        try
        {
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Temporary(string path, string json)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        return temporary;
    }
}
