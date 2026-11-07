using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.SiemensDocs;

[Collection("Mcp protocol serial")]
public sealed class SiemensDocsMcpTests
{
    [Fact]
    public async Task ReadOnlyMode_ReadsDocumentation_WithoutTiaPortalOrABinding()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var tool = Assert.Single(await harness.Client.ListToolsAsync(), candidate => candidate.Name == "siemens_docs");
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations.OpenWorldHint);

        var result = await harness.Client.CallToolAsync("siemens_docs", new Dictionary<string, object?>
        {
            ["operations"] = new object[]
            {
                new { operationId = "ok", operation = "read_page", reference = $"iox:{SiemensDocsFixtures.IoxHsc}/{SiemensDocsFixtures.IoxHscOverview}" },
                new { operationId = "elsewhere", operation = "read_page", reference = "https://example.com/r/x" },
            },
        });

        Assert.False(result.IsError);
        var document = result.StructuredContent!.Value;
        Assert.False(document.GetProperty("success").GetBoolean());
        var operations = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal("succeeded", operations[0].GetProperty("status").GetString());
        Assert.Equal("failed", operations[1].GetProperty("status").GetString());
        Assert.Equal("validation_error", operations[1].GetProperty("failure").GetProperty("category").GetString());
    }

    [Fact]
    public async Task UnknownMember_IsACanonicalValidationError()
    {
        await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadOnly);
        var result = await harness.Client.CallToolAsync("siemens_docs", new Dictionary<string, object?>
        {
            ["operations"] = new object[] { new { operationId = "a", operation = "read_page", reference = "tia:x", url = "https://example.com" } },
        });
        ArgumentRejectionAssert.IsCanonicalValidationError(result, "siemens_docs");
    }
}
