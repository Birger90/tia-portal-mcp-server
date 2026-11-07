using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

/// <summary>
/// K91: the plc_read/plc_write tool descriptions are short (Claude Desktop refused the full ones), and the full per-operation
/// reference moved to plc_read describe_operations without losing an operation, a field rule or a safety statement.
/// </summary>
public class PlcOperationReferenceTests
{
    private static IEnumerable<string> AllOperations
        => PlcOperationCatalog.ReadOperationNames.Concat(PlcOperationCatalog.WriteOperationNames);

    private static string MethodDescription(Type type, string method)
        => type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.GetCustomAttribute<DescriptionAttribute>()!.Description;

    [Fact]
    public void DescribeOperations_IsAnObserveRead()
    {
        Assert.Contains(PlcOperationHelpMethods.DescribeOperations, PlcOperationCatalog.ReadOperationNames);
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(PlcOperationHelpMethods.DescribeOperations));
        Assert.All(new[] { McpAccessMode.ReadOnly, McpAccessMode.ReadWrite, McpAccessMode.Full },
            mode => Assert.True(OperationPolicyCatalog.IsAllowed(mode, PlcOperationHelpMethods.DescribeOperations)));
    }

    [Fact]
    public void ShortDescriptions_NameEveryOperationAndStayBelowTheClientLimit()
    {
        var read = MethodDescription(typeof(PlcReadTools), nameof(PlcReadTools.PlcRead));
        var write = MethodDescription(typeof(PlcWriteTools), nameof(PlcWriteTools.PlcWrite));
        Assert.True(read.Length <= 8000, $"plc_read description has {read.Length} characters");
        Assert.True(write.Length <= 8000, $"plc_write description has {write.Length} characters");
        Assert.All(PlcOperationCatalog.ReadOperationNames, name => Assert.True(PlcOperationReference.NamesOperation(read, name), name));
        Assert.All(PlcOperationCatalog.WriteOperationNames, name => Assert.True(PlcOperationReference.NamesOperation(write, name), name));
        Assert.Contains("describe_operations", read);
        Assert.Contains("describe_operations", write);
    }

    [Fact]
    public void WriteDescription_KeepsEveryAcknowledgeAndBlockGuardAndTheSafetyAcceptance()
    {
        var write = MethodDescription(typeof(PlcWriteTools), nameof(PlcWriteTools.PlcWrite));
        var interactive = PlcGuardDefinitions.Definitions
            .Where(guard => guard.Severity is WriteGuardSeverities.Acknowledge or WriteGuardSeverities.Block)
            .Select(guard => guard.Id).ToArray();
        Assert.NotEmpty(interactive);
        Assert.All(interactive, id => Assert.Contains(id, write));
        Assert.Contains("never replaces the safety acceptance", write);
        Assert.Contains("read-write asks the human", write);
        Assert.Contains("full satisfies it by policy", write);
        Assert.Contains("dryRun:true", write);
        Assert.Contains("no rollback", write);
        Assert.Contains("never values", write);
    }

    [Fact]
    public void ReadDescription_KeepsTheOnlineAndTrustRules()
    {
        var read = MethodDescription(typeof(PlcReadTools), nameof(PlcReadTools.PlcRead));
        Assert.Contains("change nothing", read);
        Assert.Contains("trustedCertificates", read);
        Assert.Contains("no safety acceptance evidence", read);
        Assert.Contains("read-only refuses it", read);
    }

    [Fact]
    public void Reference_DocumentsEveryOperationInItsOwnToolText()
    {
        foreach (var name in AllOperations.Where(name => name != PlcOperationHelpMethods.DescribeOperations))
        {
            var description = PlcOperationReference.DescribeOne(name);
            Assert.True(description.Reference.Count > 0, $"{name} has no reference segment");
            Assert.All(description.Reference, segment => Assert.True(PlcOperationReference.NamesOperation(segment, name), name));
            Assert.Equal(PlcOperationCatalog.WriteOperationNames.Contains(name) ? "plc_write" : "plc_read", description.Tool);
        }
    }

    [Fact]
    public void Reference_DocumentsEveryDeclaredField()
    {
        foreach (var name in AllOperations)
        {
            var description = PlcOperationReference.DescribeOne(name);
            Assert.True(PlcOperationCatalog.TryGetFields(name, out var required, out var optional, out _));
            Assert.Equal(required.Concat(optional), description.Fields.Select(field => field.Name));
            Assert.All(description.Fields, field => Assert.False(string.IsNullOrWhiteSpace(field.Description), $"{name}.{field.Name}"));
        }
    }

    [Fact]
    public void Reference_KeepsTheMovedTextsAndTheSafetyRules()
    {
        // The texts are the former tool descriptions: every segment is kept, so joining them gives the text again.
        foreach (var text in new[] { PlcOperationReference.ReadTool, PlcOperationReference.WriteTool })
            Assert.Equal(text.Replace(" ", ""), string.Concat(PlcOperationReference.Segments(text)).Replace(" ", ""));
        var login = PlcOperationReference.DescribeOne("safety_login");
        Assert.Contains(login.Reference.Concat(login.SectionNotes), segment => segment.Contains("plc_safety_acceptance_required"));
        Assert.Contains(login.Reference.Concat(login.SectionNotes), segment => segment.Contains("never replaces the safety acceptance"));
        var secret = PlcOperationReference.DescribeOne("set_plc_master_secret_online");
        Assert.Contains(secret.Reference.Concat(secret.SectionNotes), segment => segment.Contains("acknowledge guard plc_master_secret_online"));
        var sd = PlcOperationReference.DescribeOne("insert_network");
        Assert.Contains(sd.Reference.Concat(sd.SectionNotes), segment => segment.Contains("networkCode") && segment.Contains("codeMatchesExport"));
        Assert.Contains(sd.Reference.Concat(sd.SectionNotes), segment => segment.Contains("plc_safety_acceptance_required"));
    }

    [Fact]
    public void Index_ListsEveryOperationWithItsFieldsAndTheToolWideRules()
    {
        var index = PlcOperationReference.Describe(null);
        Assert.Null(index.Operations);
        Assert.Equal(AllOperations, index.Index!.Select(entry => entry.Operation));
        Assert.Equal("blockPath; optional: format, withDependencies", index.Index!.Single(entry => entry.Operation == "get_block_content").Fields);
        Assert.Contains(index.WriteToolRules, rule => rule.Contains("no rollback"));
        Assert.Contains("Birger90/tia-portal-mcp-server", index.ReferenceDocument);
    }

    [Theory]
    [InlineData(new[] { "a", "b", "c", "d" }, "1 to 3")]
    [InlineData(new[] { "nope" }, "is no PLC operation")]
    [InlineData(new[] { "create_tag", "create_tag" }, "more than once")]
    [InlineData(new[] { " " }, "nonblank")]
    public void Validation_RefusesBadNames(string[] names, string message)
    {
        var validation = PlcOperationCatalog.ValidateRead(new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = PlcOperationHelpMethods.DescribeOperations, OperationNames = names },
        });
        Assert.False(validation.IsValid);
        Assert.Contains(message, validation.Error);
    }

    [Fact]
    public void Validation_AcceptsNamesAndRefusesTheFieldElsewhere()
    {
        Assert.True(PlcOperationCatalog.ValidateRead(new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = PlcOperationHelpMethods.DescribeOperations, OperationNames = new[] { "create_tag", "get_block_content" } },
            new PlcOperationRequest { OperationId = "i", Operation = PlcOperationHelpMethods.DescribeOperations },
        }).IsValid);
        var elsewhere = PlcOperationCatalog.ValidateRead(new[]
        {
            new PlcOperationRequest { OperationId = "x", Operation = "list_tag_tables", OperationNames = new[] { "create_tag" } },
        });
        Assert.False(elsewhere.IsValid);
        Assert.Contains("'operationNames' is not valid for list_tag_tables", elsewhere.Error);
        var asWrite = PlcOperationCatalog.ValidateWrite(new[]
        {
            new PlcOperationRequest { OperationId = "w", Operation = PlcOperationHelpMethods.DescribeOperations },
        });
        Assert.False(asWrite.IsValid);
        Assert.Contains("use plc_read", asWrite.Error);
    }

    [Fact]
    public void ShortFieldDescription_IsTheFirstSentenceOrNone()
    {
        Assert.Equal("Optional PLC software name.", PlcOperationReference.ShortDescription("Optional PLC software name. More text here.", 160));
        Assert.Equal("A path (e.g. C:\\x. or y), kept.", PlcOperationReference.ShortDescription("A path (e.g. C:\\x. or y), kept. Rest.", 160));
        Assert.Null(PlcOperationReference.ShortDescription(new string('x', 200) + ".", 160));
    }
}

/// <summary>describe_operations through plc_read: no bound project needed, a mixed call still needs one.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public class PlcOperationReferenceReadTests
{
    private static OpennessWorkerClient Unbound()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

    [Fact]
    public async Task DescribeOperations_RunsWithoutABoundProject()
    {
        using var client = Unbound();
        var result = await PlcReadTools.PlcRead(client, new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = PlcOperationHelpMethods.DescribeOperations, OperationNames = new[] { "create_tag" } },
        });
        Assert.False(result.IsError);
        var item = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        var described = item.GetProperty("result").GetProperty("operations")[0];
        Assert.Equal("create_tag", described.GetProperty("operation").GetString());
        Assert.Equal("plc_write", described.GetProperty("tool").GetString());
        Assert.Contains("tableName", described.GetProperty("requiredFields").EnumerateArray().Select(field => field.GetString()));
    }

    [Fact]
    public async Task MixedCall_StillNeedsTheBinding()
    {
        using var client = Unbound();
        var result = await PlcReadTools.PlcRead(client, new[]
        {
            new PlcOperationRequest { OperationId = "d", Operation = PlcOperationHelpMethods.DescribeOperations },
            new PlcOperationRequest { OperationId = "t", Operation = "list_tag_tables" },
        });
        Assert.True(result.IsError);
    }
}
