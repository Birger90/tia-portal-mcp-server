using System.Xml.Linq;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing;

/// <summary>set_network_text / replace_network_text (adopted from BetterTIA/writer): the SimaticML model and the guarded round trip.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcNetworkTextTests
{
    private const string Lad = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <SW.Blocks.FC ID="0">
            <AttributeList><Name>FC_Pumps</Name><ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList>
            <ObjectList>
              <MultilingualText ID="1" CompositionName="Comment"><ObjectList><MultilingualTextItem ID="2" CompositionName="Items"><AttributeList><Culture>en-US</Culture><Text>Pump 1 block</Text></AttributeList></MultilingualTextItem></ObjectList></MultilingualText>
              <SW.Blocks.CompileUnit ID="3" CompositionName="CompileUnits">
                <AttributeList><ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList>
                <ObjectList>
                  <MultilingualText ID="4" CompositionName="Comment" />
                  <MultilingualText ID="5" CompositionName="Title"><ObjectList>
                    <MultilingualTextItem ID="6" CompositionName="Items"><AttributeList><Culture>en-US</Culture><Text>Pump 1</Text></AttributeList></MultilingualTextItem>
                    <MultilingualTextItem ID="7" CompositionName="Items"><AttributeList><Culture>de-DE</Culture><Text>Pumpe 1</Text></AttributeList></MultilingualTextItem>
                  </ObjectList></MultilingualText>
                </ObjectList>
              </SW.Blocks.CompileUnit>
              <SW.Blocks.CompileUnit ID="1A" CompositionName="CompileUnits">
                <AttributeList><ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList>
                <ObjectList>
                  <MultilingualText ID="8" CompositionName="Title"><ObjectList><MultilingualTextItem ID="9" CompositionName="Items"><AttributeList><Culture>en-US</Culture><Text>Pump 10 and Pump 2</Text></AttributeList></MultilingualTextItem></ObjectList></MultilingualText>
                </ObjectList>
              </SW.Blocks.CompileUnit>
            </ObjectList>
          </SW.Blocks.FC>
        </Document>
        """;

    private static XDocument Doc() => SimaticMlInterfaceDocument.Parse(Lad).Document;

    [Fact]
    public void SetNetworkText_NeedsACultureWhenSeveralExistAndAddsAMissingOne()
    {
        var document = Doc();
        var error = Assert.Throws<PlcInterfaceEditException>(() => SimaticMlNetworkTexts.SetNetworkText(document, 1, "X", null, null));
        Assert.Contains("2 language(s) (en-US, de-DE)", error.Message);
        Assert.Equal("target_not_found", Assert.Throws<PlcInterfaceEditException>(() => SimaticMlNetworkTexts.SetNetworkText(document, 3, "X", null, null)).Category);

        var changes = SimaticMlNetworkTexts.SetNetworkText(document, 1, "Pumpe Eins", "Neu", "de-DE");

        Assert.Equal(new[] { ("network 1 Title", "de-DE", "Pumpe 1", "Pumpe Eins"), ("network 1 Comment", "de-DE", "", "Neu") },
            changes.Select(c => (c.Where, c.Culture, c.Old, c.New)));
        var created = document.Descendants().Single(e => e.Name.LocalName == "Text" && e.Value == "Neu").Parent!.Parent!;
        Assert.Equal("1B", (string?)created.Attribute("ID"));
        Assert.Equal("Neu", SimaticMlNetworkTexts.Current(document, changes[1]));
    }

    [Fact]
    public void ReplaceTexts_SwapsInOnePassLongestFirstAndHonoursTheScope()
    {
        var replacements = new List<PlcTextReplacementInfo> { new() { Find = "Pump 1", Replace = "Pump 2" }, new() { Find = "Pump 2", Replace = "Pump 1" }, new() { Find = "Pump 10", Replace = "Pump 11" } };

        var networks = SimaticMlNetworkTexts.ReplaceTexts(Doc(), replacements, "en-US", SimaticMlNetworkTexts.ScopeNetworks);
        Assert.Equal(new[] { ("network 1 Title", "Pump 2"), ("network 2 Title", "Pump 11 and Pump 1") }, networks.Select(c => (c.Where, c.New)));

        var all = SimaticMlNetworkTexts.ReplaceTexts(Doc(), replacements, null, SimaticMlNetworkTexts.ScopeAll);
        Assert.Contains(all, c => c.Where == "block Comment" && c.New == "Pump 2 block");
        Assert.Throws<ArgumentException>(() => SimaticMlNetworkTexts.ReplaceAll("x", new List<PlcTextReplacementInfo> { new() { Find = "", Replace = "y" } }));
    }

    [Fact]
    public void Catalog_RequiresTheFieldsAndRefusesBadScopes()
    {
        static string? Error(PlcOperationRequest item) => PlcOperationCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("networkNumber is required", Error(new PlcOperationRequest { OperationId = "a", Operation = PlcBlockEditMethods.SetNetworkText, BlockPath = "PLC_1/FB", Title = "x" }));
        Assert.Contains("needs title, comment or both", Error(new PlcOperationRequest { OperationId = "a", Operation = PlcBlockEditMethods.SetNetworkText, BlockPath = "PLC_1/FB", NetworkNumber = 1 }));
        Assert.Contains("replacements is required", Error(new PlcOperationRequest { OperationId = "a", Operation = PlcBlockEditMethods.ReplaceNetworkText, BlockPath = "PLC_1/FB" }));
        Assert.Contains("textScope must be one of", Error(new PlcOperationRequest
        {
            OperationId = "a", Operation = PlcBlockEditMethods.ReplaceNetworkText, BlockPath = "PLC_1/FB", TextScope = "blocks",
            Replacements = new() { new() { Find = "a", Replace = "b" } },
        }));
        Assert.Contains("appears twice", Error(new PlcOperationRequest
        {
            OperationId = "a", Operation = PlcBlockEditMethods.ReplaceNetworkText, BlockPath = "PLC_1/FB",
            Replacements = new() { new() { Find = "a", Replace = "b" }, new() { Find = "a", Replace = "c" } },
        }));
    }

    [Fact]
    public async Task NetworkTextEdits_ApplyThroughUpdateBlockLogicAndVerifyByReExport()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");
        var hash = await fixture.ReadHashAsync("Station_1/FB_Motor", "xml");

        var preview = await fixture.RunAsync(true, new PlcOperationRequest
        {
            OperationId = "title", Operation = PlcBlockEditMethods.SetNetworkText, BlockPath = "Station_1/FB_Motor", NetworkNumber = 1, Title = "Pump A", ExpectedContentHash = hash,
        });
        Assert.True(preview.Success, preview.Error?.Message);
        Assert.Equal(new[] { ("network 1 Title [en-US]", "Pump 1", "Pump A") }, preview.Effects[0].Effect!.Changes.Select(c => (c.Field, c.Current, c.Requested)));
        Assert.Contains(preview.Guards, g => g.Id == PlcGuardDefinitions.ReimportsBlockForText && g.Severity == "info");
        Assert.Equal(hash, await fixture.ReadHashAsync("Station_1/FB_Motor", "xml"));

        var response = await fixture.RunAsync(false,
            new PlcOperationRequest { OperationId = "title", Operation = PlcBlockEditMethods.SetNetworkText, BlockPath = "Station_1/FB_Motor", NetworkNumber = 2, Comment = "Spare", Culture = "en-US", ExpectedContentHash = hash },
            new PlcOperationRequest
            {
                OperationId = "swap", Operation = PlcBlockEditMethods.ReplaceNetworkText, BlockPath = "Station_1/FB_Motor",
                Replacements = new() { new() { Find = "Pump 1", Replace = "Pump 10" }, new() { Find = "Pump 10", Replace = "Pump 1" } },
            });

        Assert.True(response.Success, response.Error?.Message ?? string.Join("; ", response.Batch!.Operations.Select(o => o.Failure?.Message)));
        Assert.Equal(new[] { null, "title" }, response.Effects.Select(e => e.Effect!.DependsOn));
        Assert.Equal(new[] { "superseded", PlcVerificationPolicy.NetworkTextCheck }, response.Verification!.Operations.Select(v => v.Check));
        Assert.All(response.Verification.Operations, v => Assert.True(v.Success, v.Message));
        Assert.Equal(2, requests.Methods().Count(m => m == "update_block_logic"));
        var after = await fixture.ReadHashAsync("Station_1/FB_Motor", "xml");
        Assert.Equal(after, response.Verification.Operations[^1].ContentHash);
        var result = response.Batch!.Operations[1].Result!.Value;
        Assert.Equal(PlcBlockEditMethods.ReplaceNetworkText, result.GetProperty("operation").GetString());
    }

    [Fact]
    public async Task NetworkTextEdits_RefuseADataBlockAndAnEditThatChangesNothing()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var db = await fixture.RunAsync(true, new PlcOperationRequest { OperationId = "db", Operation = PlcBlockEditMethods.SetNetworkText, BlockPath = "Station_1/Settings", NetworkNumber = 1, Title = "x" });
        Assert.Equal("target_kind_unsupported", db.Error?.Category);

        var none = await fixture.RunAsync(true, new PlcOperationRequest
        {
            OperationId = "none", Operation = PlcBlockEditMethods.ReplaceNetworkText, BlockPath = "Station_1/FB_Motor", Replacements = new() { new() { Find = "Valve", Replace = "Gate" } },
        });
        Assert.Equal("validation_error", none.Error?.Category);
        Assert.Contains("no find matched", none.Error!.Message);
    }
}
