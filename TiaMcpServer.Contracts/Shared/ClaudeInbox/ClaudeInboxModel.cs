using System.Globalization;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Shared.ClaudeInbox;

/// <summary>
/// The "Claude in TIA" hand-over contract (shared source with the TIA Portal Add-In and the Claude bridge). The Add-In
/// writes one request file per click into the inbox; Claude reads it through the MCP tool <c>tia_inbox</c> and answers
/// with a reply file in the outbox; the Add-In shows the replies. The files carry names and paths only: no project data
/// (block content, tag values, passwords) is copied into them.
/// </summary>
public static class ClaudeInboxContract
{
    public const string RequestSchema = "tia-mcp-claude-request/1";
    public const string ReplySchema = "tia-mcp-claude-reply/1";
    public const string StatusSchema = "tia-mcp-claude-status/1";

    public const int MaxTextChars = 4000;
    public const int MaxReplyChars = 60000;
    public const int MaxSelectionItems = 50;
    public const int MaxChainEntries = 32;
    public const int MaxNameChars = 512;
    public const int MaxTypeNamesPerEntry = 8;

    /// <summary>Request ids: UTC timestamp, a dash and eight lower-case hex digits, e.g. <c>20261103T101500Z-1a2b3c4d</c>; safe as a file name.</summary>
    public static readonly Regex IdPattern = new("^[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}$", RegexOptions.CultureInvariant);

    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    public static string NewId(DateTime utcNow, Random random)
    {
        var bytes = new byte[4];
        random.NextBytes(bytes);
        return utcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-"
               + string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    public static string FormatTime(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>The fixed actions of the Add-In's context menu (TIA Portal V21 add-ins have no documented text-input dialog).</summary>
public static class ClaudeActions
{
    public const string Handover = "handover";
    public const string Explain = "explain";
    public const string Review = "review";
    public const string Revise = "revise";
    public const string Document = "document";

    public static readonly IReadOnlyList<string> All = new[] { Handover, Explain, Review, Revise, Document };

    /// <summary>The German menu text of each action.</summary>
    public static string MenuText(string action) => action switch
    {
        Handover => "An Claude übergeben…",
        Explain => "Erklären",
        Review => "Prüfen",
        Revise => "Überarbeiten",
        Document => "Dokumentieren",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };

    /// <summary>What Claude is asked to do (English, for the prompt and the tool result).</summary>
    public static string Instruction(string action) => action switch
    {
        Handover => "The user handed these objects over without a fixed task: look at them, then do what the request text says, or ask in the reply what should be done.",
        Explain => "Explain what the selected objects do and how they fit into the project. Read only; change nothing.",
        Review => "Review the selected objects for errors, risks and style problems and list the findings. Read only; change nothing.",
        Revise => "Revise the selected objects as the request text asks (or improve them where the review finds problems), within the server's access mode; report every change.",
        Document => "Write documentation for the selected objects (purpose, interface, behaviour). Change comments only when the request text asks for it.",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown action."),
    };
}

/// <summary>Where in TIA Portal the context menu was opened (one Add-In provider each).</summary>
public static class ClaudeSources
{
    public const string ProjectTree = "projectTree";
    public const string DevicesAndNetworks = "devicesAndNetworks";
    public const string ProjectLibrary = "projectLibrary";
    public const string GlobalLibrary = "globalLibrary";

    public static readonly IReadOnlyList<string> All = new[] { ProjectTree, DevicesAndNetworks, ProjectLibrary, GlobalLibrary };
}

/// <summary>One object on the way from the selected object up to the project (index 0 is the selected object).</summary>
public sealed class ClaudeChainEntry
{
    public ClaudeChainEntry(IReadOnlyList<string> typeNames, string? name)
    {
        TypeNames = typeNames ?? throw new ArgumentNullException(nameof(typeNames));
        Name = name;
    }

    /// <summary>The runtime type's full name and its base types in the <c>Siemens.Engineering</c> namespaces, most derived first.</summary>
    public IReadOnlyList<string> TypeNames { get; }

    /// <summary>The object's <c>Name</c> attribute, or null when it has none or it could not be read.</summary>
    public string? Name { get; }

    public bool Is(string fullTypeName) => TypeNames.Contains(fullTypeName, StringComparer.Ordinal);
}

/// <summary>How the MCP tools address the selected object (every member null when not applicable). A hint built from names:
/// Claude confirms it with <c>browse_project_tree</c>, <c>plc_read</c>, <c>network_read</c> or <c>library_read</c>.</summary>
public sealed class ClaudeAddress
{
    public static readonly ClaudeAddress None = new(null, null, null, null, null, null, null, null, null, null, null, null);

    public ClaudeAddress(string? plcName, string? unitName, string? blockPath, string? typePath, string? tableName, string? folderPath,
        string? deviceName, string? deviceItemName, string? library, string? libraryTypePath, string? masterCopyName, string? masterCopyFolderPath)
    {
        PlcName = plcName;
        UnitName = unitName;
        BlockPath = blockPath;
        TypePath = typePath;
        TableName = tableName;
        FolderPath = folderPath;
        DeviceName = deviceName;
        DeviceItemName = deviceItemName;
        Library = library;
        LibraryTypePath = libraryTypePath;
        MasterCopyName = masterCopyName;
        MasterCopyFolderPath = masterCopyFolderPath;
    }

    /// <summary>The PLC software name (<c>plcName</c> of <c>plc_read</c>).</summary>
    public string? PlcName { get; }

    public string? UnitName { get; }

    /// <summary><c>plc_read</c> <c>blockPath</c>: <c>PLC_1/Blocks/Folder/Block</c> (or <c>PLC_1/Units/U/Blocks/...</c>); for a block group the group path.</summary>
    public string? BlockPath { get; }

    /// <summary><c>plc_read</c> <c>typePath</c>: <c>PLC_1/Types/Folder/Type</c>.</summary>
    public string? TypePath { get; }

    /// <summary>Tag table name (<c>tableName</c>), with <see cref="FolderPath"/>.</summary>
    public string? TableName { get; }

    /// <summary>Tag table folder as the inventory emits it (<c>/</c> or <c>/Line/Cell</c>).</summary>
    public string? FolderPath { get; }

    public string? DeviceName { get; }

    public string? DeviceItemName { get; }

    /// <summary><c>library_read</c> <c>library</c>: <c>project</c>, or the global library's path when it could be read.</summary>
    public string? Library { get; }

    /// <summary><c>library_read</c> <c>typePath</c>: <c>/Folder/Type</c>.</summary>
    public string? LibraryTypePath { get; }

    public string? MasterCopyName { get; }

    /// <summary>Master copy folder below the root (<c>/A/B</c>), with <see cref="MasterCopyName"/>.</summary>
    public string? MasterCopyFolderPath { get; }

    public bool IsEmpty => PlcName is null && UnitName is null && BlockPath is null && TypePath is null && TableName is null && FolderPath is null
                           && DeviceName is null && DeviceItemName is null && Library is null && LibraryTypePath is null && MasterCopyName is null
                           && MasterCopyFolderPath is null;
}

/// <summary>One selected object of a request.</summary>
public sealed class ClaudeSelectionItem
{
    public ClaudeSelectionItem(string kind, string? name, IReadOnlyList<ClaudeChainEntry> chain, ClaudeAddress address)
    {
        Kind = kind;
        Name = name;
        Chain = chain;
        Address = address;
    }

    /// <summary>One of <see cref="ClaudeSelectionKinds"/>.</summary>
    public string Kind { get; }

    public string? Name { get; }

    /// <summary>The object and its parents (index 0 is the object), as the Add-In read them.</summary>
    public IReadOnlyList<ClaudeChainEntry> Chain { get; }

    public ClaudeAddress Address { get; }
}

/// <summary>A request the Add-In wrote into the inbox.</summary>
public sealed class ClaudeRequest
{
    public ClaudeRequest(string id, string createdUtc, string source, string action, string? text, string? projectPath, IReadOnlyList<ClaudeSelectionItem> selection, string? addInVersion)
    {
        Id = id;
        CreatedUtc = createdUtc;
        Source = source;
        Action = action;
        Text = text;
        ProjectPath = projectPath;
        Selection = selection;
        AddInVersion = addInVersion;
    }

    public string Id { get; }

    public string CreatedUtc { get; }

    public string Source { get; }

    public string Action { get; }

    /// <summary>Free text for Claude, or null (the V21 Add-In menu offers fixed actions only).</summary>
    public string? Text { get; }

    /// <summary>The primary project's file path as TIA Portal reported it, or null.</summary>
    public string? ProjectPath { get; }

    public IReadOnlyList<ClaudeSelectionItem> Selection { get; }

    public string? AddInVersion { get; }
}

/// <summary>Claude's (or the bridge's) answer to one request.</summary>
public sealed class ClaudeReply
{
    public const string StatusDone = "done";
    public const string StatusFailed = "failed";
    public const string FromClaude = "claude";
    public const string FromBridge = "bridge";

    public ClaudeReply(string id, string repliedUtc, string status, string text, string from, string? readUtc)
    {
        Id = id;
        RepliedUtc = repliedUtc;
        Status = status;
        Text = text;
        From = from;
        ReadUtc = readUtc;
    }

    public string Id { get; }

    public string RepliedUtc { get; }

    /// <summary><see cref="StatusDone"/> or <see cref="StatusFailed"/>.</summary>
    public string Status { get; }

    public string Text { get; }

    /// <summary><see cref="FromClaude"/> (written through <c>tia_inbox</c>) or <see cref="FromBridge"/> (the bridge's fallback).</summary>
    public string From { get; }

    /// <summary>When the user saw it in TIA Portal ("Claude-Antworten anzeigen"), or null.</summary>
    public string? ReadUtc { get; }

    public ClaudeReply MarkRead(DateTime utcNow) => new(Id, RepliedUtc, Status, Text, From, ClaudeInboxContract.FormatTime(utcNow));
}

/// <summary>The bridge's processing state of a request (<c>outbox/&lt;id&gt;.status.json</c>).</summary>
public sealed class ClaudeRequestStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Failed = "failed";
    public const string TimedOut = "timedOut";
    public const string Skipped = "skipped";

    public ClaudeRequestStatus(string id, string state, string updatedUtc, string? message)
    {
        Id = id;
        State = state;
        UpdatedUtc = updatedUtc;
        Message = message;
    }

    public string Id { get; }

    public string State { get; }

    public string UpdatedUtc { get; }

    public string? Message { get; }
}

/// <summary>The selection kinds the Add-In recognises (anything else is <see cref="Other"/>).</summary>
public static class ClaudeSelectionKinds
{
    public const string PlcBlock = "plcBlock";
    public const string BlockGroup = "blockGroup";
    public const string PlcType = "plcType";
    public const string TypeGroup = "typeGroup";
    public const string TagTable = "tagTable";
    public const string TagTableGroup = "tagTableGroup";
    public const string Tag = "tag";
    public const string PlcSoftware = "plcSoftware";
    public const string SoftwareUnit = "softwareUnit";
    public const string Device = "device";
    public const string DeviceItem = "deviceItem";
    public const string Subnet = "subnet";
    public const string Library = "library";
    public const string LibraryType = "libraryType";
    public const string LibraryTypeVersion = "libraryTypeVersion";
    public const string LibraryTypeFolder = "libraryTypeFolder";
    public const string MasterCopy = "masterCopy";
    public const string MasterCopyFolder = "masterCopyFolder";
    public const string Project = "project";
    public const string Other = "other";
}
