using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Json;
using TiaMcpServer.Plc;
using TiaMcpServer.Tests.Network;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Editing;

/// <summary>rename_members (adopted from BetterTIA/writer RenameMembers): the SimaticML model and the guarded round trip.</summary>
[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcMemberRenameTests
{
    private const string Fb = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document>
          <SW.Blocks.FB ID="0">
            <AttributeList>
              <Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
                <Section Name="Input"><Member Name="Start" Datatype="Bool" /><Member Name="Stop" Datatype="Bool" /></Section>
                <Section Name="Static"><Member Name="Data" Datatype="Struct"><Member Name="Speed" Datatype="Int" /></Member><Member Name="Timer" Datatype="TON_TIME" /></Section>
              </Sections></Interface>
              <Name>FB_Pump</Name><ProgrammingLanguage>LAD</ProgrammingLanguage>
            </AttributeList>
            <ObjectList>
              <SW.Blocks.CompileUnit ID="3" CompositionName="CompileUnits"><AttributeList><NetworkSource>
                <FlgNet xmlns="http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5"><Parts>
                  <Access Scope="LocalVariable" UId="21"><Symbol><Component Name="start" /></Symbol></Access>
                  <Access Scope="LocalVariable" UId="22"><Symbol><Component Name="Data" /><Component Name="Speed" /></Symbol></Access>
                  <Access Scope="GlobalVariable" UId="23"><Symbol><Component Name="Start" /></Symbol></Access>
                  <Call UId="24"><CallInfo Name="TON" BlockType="FB"><Instance Scope="LocalVariable" UId="25"><Component Name="Timer" /></Instance></CallInfo></Call>
                </Parts></FlgNet>
              </NetworkSource><ProgrammingLanguage>LAD</ProgrammingLanguage></AttributeList>
              <ObjectList><MultilingualText ID="4" CompositionName="Comment"><ObjectList><MultilingualTextItem ID="5" CompositionName="Items"><AttributeList><Culture>en-US</Culture><Text>Uses #Start</Text></AttributeList></MultilingualTextItem></ObjectList></MultilingualText></ObjectList>
              </SW.Blocks.CompileUnit>
              <SW.Blocks.CompileUnit ID="6" CompositionName="CompileUnits"><AttributeList><NetworkSource>
                <Graph><Steps><Step Number="1"><Actions><Action><Token Text="N #Data.Speed := #Stop" /></Action></Actions></Step></Steps></Graph>
              </NetworkSource></AttributeList></SW.Blocks.CompileUnit>
            </ObjectList>
          </SW.Blocks.FB>
        </Document>
        """;

    [Fact]
    public void Apply_SwapsNamesAndUpdatesTheBlocksOwnAccessesOnly()
    {
        var document = SimaticMlInterfaceDocument.Parse(Fb);

        var outcome = SimaticMlMemberRenames.Apply(document, new List<PlcMemberRenameInfo>
        {
            new() { MemberPath = "Start", NewName = "Stop" },
            new() { MemberPath = "Stop", NewName = "Start" },
            new() { MemberPath = "Data.Speed", NewName = "Velocity" },
            new() { MemberPath = "Timer", NewName = "Delay", Section = "Static" },
        }, isType: false);

        var xml = document.Serialize();
        Assert.Equal(new[] { "Stop", "Start" }, document.ReadSections()[0].Members.Select(m => m.Name));
        Assert.Contains("<Component Name=\"Stop\" />", xml);
        Assert.Contains("<Component Name=\"Velocity\" />", xml);
        Assert.Contains("<Component Name=\"Delay\" />", xml);
        Assert.Contains("Scope=\"GlobalVariable\" UId=\"23\"><Symbol><Component Name=\"Start\" />", xml);
        Assert.Contains("Text=\"N #Data.Velocity := #Start\"", xml);
        Assert.Contains("Uses #Start", xml);
        Assert.DoesNotContain("zzTmp_", xml);
        Assert.Equal(5, outcome.UpdatedAccesses);
        Assert.Single(outcome.Warnings);
        Assert.All(outcome.Expectations, e => Assert.True(e.Check(document.ReadSections()).Success, e.Check(document.ReadSections()).Actual));
    }

    [Fact]
    public void Apply_RefusesTakenNamesParentAndChildAndAmbiguousSections()
    {
        Assert.Contains("already exists", Assert.Throws<PlcInterfaceEditException>(() => SimaticMlMemberRenames.Apply(SimaticMlInterfaceDocument.Parse(Fb),
            new List<PlcMemberRenameInfo> { new() { MemberPath = "Start", NewName = "Timer" } }, false)).Message);
        Assert.Contains("rename them in two items", Assert.Throws<PlcInterfaceEditException>(() => SimaticMlMemberRenames.Apply(SimaticMlInterfaceDocument.Parse(Fb),
            new List<PlcMemberRenameInfo> { new() { MemberPath = "Data", NewName = "D" }, new() { MemberPath = "Data.Speed", NewName = "S" } }, false)).Message);
        Assert.Equal("target_not_found", Assert.Throws<PlcInterfaceEditException>(() => SimaticMlMemberRenames.Apply(SimaticMlInterfaceDocument.Parse(Fb),
            new List<PlcMemberRenameInfo> { new() { MemberPath = "Missing", NewName = "X" } }, false)).Category);
    }

    [Fact]
    public void Catalog_RequiresExactlyOneTargetAndValidRenames()
    {
        static string? Error(PlcOperationRequest item) => PlcOperationCatalog.ValidateWrite(new[] { item }).Error;
        Assert.Contains("exactly one of blockPath", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameMembers, MemberRenames = new() { new() { MemberPath = "A", NewName = "B" } } }));
        Assert.Contains("memberRenames is required", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameMembers, BlockPath = "PLC_1/FB" }));
        Assert.Contains("must be nonblank", Error(new PlcOperationRequest { OperationId = "r", Operation = PlcBlockEditMethods.RenameMembers, BlockPath = "PLC_1/FB", MemberRenames = new() { new() { MemberPath = "A", NewName = "a.b" } } }));
    }

    [Fact]
    public async Task RenameMembers_ImportsThroughUpdateBlockLogicAndVerifiesTheInterface()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "rename", Operation = PlcBlockEditMethods.RenameMembers, BlockPath = "Station_1/FB_Motor",
            MemberRenames = new() { new() { MemberPath = "Count", NewName = "Starts" }, new() { MemberPath = "Enable", NewName = "Run" } },
        });

        Assert.True(response.Success, response.Error?.Message ?? response.Batch?.Operations[0].Failure?.Message);
        Assert.Contains(response.Guards, g => g.Id == PlcGuardDefinitions.ChangesInterface);
        Assert.Contains(response.Effects[0].Effect!.Changes, c => c.Field == "localAccesses" && c.Requested!.StartsWith("3 access"));
        var check = Assert.Single(response.Verification!.Operations);
        Assert.Equal((PlcVerificationPolicy.RenamedMembersCheck, true), (check.Check, check.Success));
        var content = (await fixture.Client.GetBlockContentAsync("Station_1/FB_Motor", format: "xml")).Payload;
        Assert.Contains("Name=\"Starts\"", content);
        Assert.DoesNotContain("<Component Name=\"Count\"", content);
    }

    [Fact]
    public async Task RenameMembers_OnAPlcDataTypeUsesUpdateTypeContent()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-roundtrip");

        var response = await fixture.RunAsync(false, new PlcOperationRequest
        {
            OperationId = "udt", Operation = PlcBlockEditMethods.RenameMembers, TypePath = "Station_1/UDT_Settings",
            MemberRenames = new() { new() { MemberPath = "Scaling.valueMin", NewName = "minimum" } },
        });

        Assert.True(response.Success, response.Error?.Message ?? response.Batch?.Operations[0].Failure?.Message);
        Assert.Equal("Type", response.Effects[0].Effect!.Target.Kind);
        Assert.True(response.Verification!.Operations.Single().Success);
    }
}
