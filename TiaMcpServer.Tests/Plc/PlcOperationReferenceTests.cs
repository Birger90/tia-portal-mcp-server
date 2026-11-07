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
    public void WriteToolRules_DoNotContradictTheAcknowledgeGuardPrompt()
    {
        // K93: "No confirmation prompt is sent." came from the first plc_write (before acknowledge guards asked the human).
        var rules = PlcOperationReference.Describe(null).WriteToolRules;
        Assert.DoesNotContain("No confirmation prompt is sent.", rules);
        var prompt = Assert.Single(rules, rule => rule.StartsWith("No confirmation prompt", StringComparison.Ordinal));
        Assert.Contains("unless an acknowledge guard fires", prompt);
        Assert.Contains("read-write asks the human once", prompt);
        Assert.Contains("full satisfies it by policy", prompt);
    }

    [Theory]
    [InlineData("get_markings")]
    [InlineData("get_mark_state")]
    [InlineData("get_object_identifier")]
    [InlineData("find_object_by_identifier")]
    [InlineData("get_creation_infos")]
    [InlineData("get_linked_tags")]
    [InlineData("get_umc_server_user")]
    [InlineData("get_umac")]
    public void SectionNotes_DoNotCarryTheCertificateOrValidationNotesOfOtherOperations(string operation)
    {
        // K93: these notes belong to get_cpu_certificates and check_activation_test, listed after them in the same paragraph.
        var notes = PlcOperationReference.DescribeOne(operation).SectionNotes;
        Assert.DoesNotContain(notes, note => note.Contains("TLS") || note.Contains("trustedCertificates") || note.Contains("trusts a certificate")
            || note.Contains("CheckValidity") || note.Contains("consistency check"));
    }

    [Fact]
    public void SectionNotes_StayWithTheirOwnOperations()
    {
        var certificates = PlcOperationReference.DescribeOne("get_cpu_certificates").SectionNotes;
        Assert.Contains(certificates, note => note.Contains("trustedCertificates"));
        Assert.Contains(PlcOperationReference.DescribeOne("check_activation_test").SectionNotes, note => note.Contains("consistency check"));
        // A clause after "; " continues its own operation: set_network_text's verification is not create_db_snapshot's.
        Assert.Contains(PlcOperationReference.DescribeOne("set_network_text").SectionNotes, note => note.Contains("check networkText"));
        Assert.DoesNotContain(PlcOperationReference.DescribeOne("update_db_member").SectionNotes, note => note.Contains("check networkText"));
        Assert.DoesNotContain(PlcOperationReference.DescribeOne("rename_members").SectionNotes, note => note.Contains("check tagComments"));
        // The SD network notes are not rename_block's, and program analysis is not get_block_content's.
        Assert.DoesNotContain(PlcOperationReference.DescribeOne("rename_block").SectionNotes, note => note.Contains(".s7res"));
        Assert.Contains(PlcOperationReference.DescribeOne("insert_network").SectionNotes, note => note.Contains(".s7res"));
        Assert.DoesNotContain(PlcOperationReference.DescribeOne("get_block_content").SectionNotes, note => note.Contains("A relationship is used only"));
        Assert.Contains(PlcOperationReference.DescribeOne("get_call_tree").SectionNotes, note => note.Contains("A relationship is used only"));
        // A sentence of a section is every named operation's rule (the safety rules of the fail-safe paragraph).
        Assert.Contains(PlcOperationReference.DescribeOne("revoke_safety_password").SectionNotes, note => note.Contains("never replaces the safety acceptance"));
    }

    [Fact]
    public void FieldDescriptions_KeepOnlyWhatConcernsTheOperation()
    {
        var snapshot = PlcOperationReference.DescribeOne("load_snapshot_as_actual_values").Fields.Single(field => field.Name == "blockPath");
        Assert.True(snapshot.Trimmed);
        Assert.StartsWith("PLC block path, e.g. PLC_1/Main", snapshot.Description);
        Assert.DoesNotContain("rename_block_group", snapshot.Description);
        Assert.DoesNotContain("get_prodiag_fb", snapshot.Description);
        Assert.DoesNotContain("interface_member", snapshot.Description);
        var member = PlcOperationReference.DescribeOne("add_interface_member").Fields.Single(field => field.Name == "blockPath");
        Assert.Contains("add/update/delete_interface_member take blockPath or typePath", member.Description);
        var technologyObject = PlcOperationReference.DescribeOne("create_technology_object").Fields.Single(field => field.Name == "name");
        Assert.Contains("for every technology object operation except", technologyObject.Description);
        var creation = PlcOperationReference.DescribeOne("get_creation_infos").Fields.Single(field => field.Name == "name");
        Assert.Contains("one composition's name", creation.Description);
        Assert.DoesNotContain("analyze_change_impact", creation.Description);
        Assert.Equal(new[] { "get_prodiag_assignment", "set_prodiag_assignment" }, PlcOperationReference.ExpandShorthand("get/set_prodiag_assignment"));
        // Nothing that names the operation is ever left out.
        foreach (var name in AllOperations)
            foreach (var field in PlcOperationReference.DescribeOne(name).Fields)
            {
                var full = PlcOperationReference.FieldDescription(field.Name)!;
                Assert.All(PlcOperationReference.Segments(full).Where(segment => PlcOperationReference.NamesOperation(segment, name)),
                    segment => Assert.Contains(segment.TrimEnd(';'), field.Description));
                Assert.Equal(field.Description != full, field.Trimmed);
            }
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
