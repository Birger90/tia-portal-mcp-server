using System.Text.Json;
using TiaMcpServer.ClaudeInbox;
using TiaMcpServer.Contracts.Shared.ClaudeInbox;
using TiaMcpServer.Tests.Contracts.ClaudeInbox;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class TiaInboxToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tia-inbox-tool-" + Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<JsonElement> Call(params TiaInboxOperationRequest[] operations)
    {
        var result = await TiaInboxTools.Run(new ClaudeInboxLocation(_root), operations, _clock, CancellationToken.None);
        return result.StructuredContent!.Value;
    }

    private static TiaInboxOperationRequest Op(string id, string operation, string? requestId = null, string? text = null, string? status = null, bool? includeAnswered = null)
        => new() { OperationId = id, Operation = operation, Id = requestId, Text = text, Status = status, IncludeAnswered = includeAnswered };

    [Fact]
    public async Task ListGetReply_RoundTrip()
    {
        var request = ClaudeInboxContractTests.SampleRequest(id: "20261103T101500Z-abcdef01");
        new ClaudeInboxStore(_root).WriteRequest(request);

        var list = await Call(Op("l", "list_requests"));
        Assert.True(list.GetProperty("success").GetBoolean());
        var listed = list.GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(1, listed.GetProperty("pendingCount").GetInt32());
        Assert.Equal(request.Id, listed.GetProperty("requests")[0].GetProperty("id").GetString());

        var got = (await Call(Op("g", "get_request", request.Id))).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal("review", got.GetProperty("action").GetString());
        Assert.Contains("Read only", got.GetProperty("instruction").GetString());
        Assert.Equal("PLC_1/Blocks/Line \"A\"/Main", got.GetProperty("selection")[0].GetProperty("address").GetProperty("blockPath").GetString());

        var replied = await Call(Op("r", "reply_request", request.Id, text: "Alles gut."));
        Assert.True(replied.GetProperty("success").GetBoolean());
        Assert.Equal("2026-11-03T11:00:00Z", replied.GetProperty("batch").GetProperty("operations")[0].GetProperty("result").GetProperty("repliedUtc").GetString());

        var again = (await Call(Op("r", "reply_request", request.Id, text: "Nochmal."))).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("failed", again.GetProperty("status").GetString());
        Assert.Equal("state_changed", again.GetProperty("failure").GetProperty("category").GetString());

        var empty = (await Call(Op("l", "list_requests"))).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.Equal(0, empty.GetProperty("requests").GetArrayLength());
        var all = (await Call(Op("l", "list_requests", includeAnswered: true))).GetProperty("batch").GetProperty("operations")[0].GetProperty("result");
        Assert.True(all.GetProperty("requests")[0].GetProperty("answered").GetBoolean());
        Assert.Equal("Alles gut.", new ClaudeInboxStore(_root).ReadReply(request.Id)!.Text);
    }

    [Fact]
    public async Task UnknownRequest_IsTargetNotFound()
    {
        var item = (await Call(Op("g", "get_request", "20261103T101500Z-00000000"))).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("target_not_found", item.GetProperty("failure").GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("get_request", null, null, null, "id is required")]
    [InlineData("get_request", "../x", null, null, "id is required")]
    [InlineData("reply_request", "20261103T101500Z-00000000", null, null, "text is required")]
    [InlineData("reply_request", "20261103T101500Z-00000000", "x", "ok", "status is done or failed")]
    [InlineData("list_requests", "20261103T101500Z-00000000", null, null, "id is not a field of list_requests")]
    [InlineData("delete_request", null, null, null, "unknown operation")]
    public void Validate_RefusesBadItems(string operation, string? id, string? text, string? status, string expected)
        => Assert.Contains(expected, TiaInboxTools.Validate(new[] { Op("a", operation, id, text, status) }));

    [Fact]
    public async Task EmptyCall_IsCanonicalValidationError()
    {
        var result = await TiaInboxTools.Run(new ClaudeInboxLocation(_root), Array.Empty<TiaInboxOperationRequest>(), _clock, CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("validation_error", result.StructuredContent!.Value.GetProperty("error").GetProperty("category").GetString());
    }
}
