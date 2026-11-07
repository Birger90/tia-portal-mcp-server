using TiaMcpServer.Contracts.Shared.ClaudeInbox;
using Xunit;

namespace TiaMcpServer.Tests.Contracts.ClaudeInbox;

public sealed class ClaudeInboxContractTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 3, 10, 15, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claude-inbox-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    internal static ClaudeChainEntry E(string name, params string[] types) => new(types, name);

    internal static ClaudeRequest SampleRequest(string? id = null, string action = ClaudeActions.Review, string? text = null)
    {
        var chain = new[]
        {
            E("Main", "Siemens.Engineering.SW.Blocks.OB", ClaudeSelectionAddressing.PlcBlockType),
            new ClaudeChainEntry(new[] { "Siemens.Engineering.SW.Blocks.PlcBlockComposition" }, null),
            E("Line \"A\"", ClaudeSelectionAddressing.BlockUserGroupType),
            E("Program blocks", ClaudeSelectionAddressing.BlockSystemGroupType),
            E("PLC_1", ClaudeSelectionAddressing.PlcSoftwareType),
            E("PLC_1", ClaudeSelectionAddressing.DeviceType),
        };
        return new ClaudeRequest(id ?? ClaudeInboxContract.NewId(Now, new Random(7)), ClaudeInboxContract.FormatTime(Now), ClaudeSources.ProjectTree,
            action, text, @"C:\Projekte\Anlage\Anlage.ap21", new[] { ClaudeSelectionAddressing.Describe(chain) }, "0.1.0");
    }

    [Fact]
    public void NewId_IsTimestampAndHex_AndValid()
    {
        var id = ClaudeInboxContract.NewId(Now, new Random(1));
        Assert.Matches("^20261103T101500Z-[0-9a-f]{8}$", id);
        Assert.True(ClaudeInboxContract.IsValidId(id));
        Assert.False(ClaudeInboxContract.IsValidId("../20261103T101500Z-1a2b3c4d"));
        Assert.False(ClaudeInboxContract.IsValidId("20261103T101500Z-1A2B3C4D"));
        Assert.False(ClaudeInboxContract.IsValidId(null));
    }

    [Fact]
    public void Request_RoundTrips_WithEscapesAndNulls()
    {
        var request = SampleRequest(text: "Bitte prüfen: \"Ventil\"\n\tTab ä € \u0001");
        var json = ClaudeInboxSerializer.Serialize(request);
        var back = ClaudeInboxSerializer.ParseRequest(json);
        Assert.Equal(request.Id, back.Id);
        Assert.Equal(request.Text, back.Text);
        Assert.Equal(request.ProjectPath, back.ProjectPath);
        var item = Assert.Single(back.Selection);
        Assert.Equal(ClaudeSelectionKinds.PlcBlock, item.Kind);
        Assert.Equal("PLC_1/Blocks/Line \"A\"/Main", item.Address.BlockPath);
        Assert.Null(item.Chain[1].Name);
        Assert.Equal(6, item.Chain.Count);
        Assert.Equal(json, ClaudeInboxSerializer.Serialize(back));
    }

    [Fact]
    public void Request_CarriesNoContentMembers()
    {
        var json = ClaudeInboxSerializer.Serialize(SampleRequest());
        Assert.DoesNotContain("content", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"schema\":\"tia-mcp-claude-request/1\"}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"schema\":\"other/1\",\"id\":\"x\"}")]
    public void ParseRequest_RefusesInvalidDocuments(string json)
        => Assert.Throws<ClaudeInboxFormatException>(() => ClaudeInboxSerializer.ParseRequest(json));

    [Fact]
    public void ParseRequest_RefusesUnknownMembersAndUnknownAction()
    {
        var json = ClaudeInboxSerializer.Serialize(SampleRequest());
        Assert.Throws<ClaudeInboxFormatException>(() => ClaudeInboxSerializer.ParseRequest(json.Replace("\"addInVersion\"", "\"blockContent\": \"x\",\n  \"addInVersion\"")));
        Assert.Throws<ClaudeInboxFormatException>(() => ClaudeInboxSerializer.ParseRequest(json.Replace("\"review\"", "\"delete_everything\"")));
        Assert.Throws<ClaudeInboxFormatException>(() => ClaudeInboxSerializer.ParseRequest(json.Replace("\"id\"", "\"id\": \"a\",\n  \"x\"")));
    }

    [Fact]
    public void Validate_RefusesTooLongText()
        => Assert.Throws<ClaudeInboxFormatException>(() => ClaudeInboxSerializer.Serialize(SampleRequest(text: new string('x', ClaudeInboxContract.MaxTextChars + 1))));

    [Fact]
    public void MiniJson_RefusesRepeatedKeysAndDeepNesting()
    {
        Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1,\"a\":2}"));
        Assert.Throws<FormatException>(() => MiniJson.Parse(new string('[', 40) + new string(']', 40)));
        Assert.Equal(1.5, MiniJson.Parse(" 1.5 "));
        Assert.Equal("\u00e4", MiniJson.Parse("\"\\u00e4\""));
    }

    [Fact]
    public void Store_WritesListsRepliesAndMarksRead()
    {
        var store = new ClaudeInboxStore(_root);
        var request = SampleRequest();
        store.WriteRequest(request);
        Assert.Throws<IOException>(() => store.WriteRequest(request));

        var pending = Assert.Single(store.Pending());
        Assert.Equal(request.Id, pending.Id);
        Assert.Null(pending.Error);

        var reply = store.WriteReply(request.Id, ClaudeReply.StatusDone, "Main ist in Ordnung.", ClaudeReply.FromClaude, Now);
        Assert.Equal("2026-11-03T10:15:00Z", reply.RepliedUtc);
        Assert.Empty(store.Pending());
        var again = Assert.Throws<ClaudeInboxException>(() => store.WriteReply(request.Id, ClaudeReply.StatusDone, "zweite Antwort", ClaudeReply.FromClaude, Now));
        Assert.Equal("already_answered", again.Category);

        Assert.Single(store.Replies(unreadOnly: true));
        store.MarkRead(request.Id, Now.AddMinutes(1));
        Assert.Empty(store.Replies(unreadOnly: true));
        Assert.Equal("2026-11-03T10:16:00Z", Assert.Single(store.Replies(unreadOnly: false)).ReadUtc);
        Assert.Empty(Directory.GetFiles(store.OutboxDirectory, "*.tmp"));
    }

    [Fact]
    public void Store_ReplyToUnknownOrInvalidId_IsRefused()
    {
        var store = new ClaudeInboxStore(_root);
        Assert.Equal("not_found", Assert.Throws<ClaudeInboxException>(() => store.WriteReply("20261103T101500Z-00000000", ClaudeReply.StatusDone, "x", ClaudeReply.FromClaude, Now)).Category);
        Assert.Equal("invalid", Assert.Throws<ClaudeInboxException>(() => store.WriteReply("..\\..\\x", ClaudeReply.StatusDone, "x", ClaudeReply.FromClaude, Now)).Category);
    }

    [Fact]
    public void Store_ListsBrokenRequestWithError_AndEmptyReplyIsRefused()
    {
        var store = new ClaudeInboxStore(_root);
        Directory.CreateDirectory(store.InboxDirectory);
        File.WriteAllText(Path.Combine(store.InboxDirectory, "20261103T101500Z-0000000a.json"), "{ broken");
        File.WriteAllText(Path.Combine(store.InboxDirectory, "notes.json"), "{}");
        var entry = Assert.Single(store.List());
        Assert.Null(entry.Request);
        Assert.NotNull(entry.Error);
        Assert.Equal("invalid", Assert.Throws<ClaudeInboxException>(() => store.ReadRequest(entry.Id)).Category);

        var request = SampleRequest(id: "20261103T101500Z-0000000b");
        store.WriteRequest(request);
        Assert.Equal("invalid", Assert.Throws<ClaudeInboxException>(() => store.WriteReply(request.Id, ClaudeReply.StatusDone, "  ", ClaudeReply.FromClaude, Now)).Category);
    }

    [Fact]
    public void Store_StatusIsReplacedAndIgnoredAsReply()
    {
        var store = new ClaudeInboxStore(_root);
        var request = SampleRequest();
        store.WriteRequest(request);
        store.WriteStatus(new ClaudeRequestStatus(request.Id, ClaudeRequestStatus.Running, "2026-11-03T10:15:01Z", null));
        store.WriteStatus(new ClaudeRequestStatus(request.Id, ClaudeRequestStatus.Finished, "2026-11-03T10:16:01Z", "ok"));
        Assert.Equal(ClaudeRequestStatus.Finished, store.ReadStatus(request.Id)!.State);
        Assert.Empty(store.Replies(unreadOnly: false));
        Assert.Equal(ClaudeRequestStatus.Finished, Assert.Single(store.List()).Status!.State);
    }

    [Fact]
    public void DefaultRoot_PrefersEnvironmentVariable()
    {
        Assert.Equal("X:\\r", ClaudeInboxStore.DefaultRoot(n => n == ClaudeInboxStore.RootEnvironmentVariable ? "X:\\r" : null, "C:\\AppData"));
        Assert.Equal(Path.Combine("C:\\AppData", "TiaMcpServer"), ClaudeInboxStore.DefaultRoot(_ => null, "C:\\AppData"));
        Assert.Null(ClaudeInboxStore.DefaultRoot(_ => null, null));
    }
}
