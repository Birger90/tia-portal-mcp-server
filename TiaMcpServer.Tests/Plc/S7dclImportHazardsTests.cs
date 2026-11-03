// Derived from rickgaiser/TiaMcp tests/TiaMcp.Tests/S7dclSourceTests.cs at 8e9de1c (AGPL-3.0-only). See THIRD_PARTY_NOTICES.md.
// Adopted: the sample block, the declaration and parameter cases and the AT overlay sample. The assertions on typed
// findings, the declared-overlay rule, the textual-interface check and the re-export comparison are this fork's.
using TiaMcpServer.Contracts.Plc.Interface;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public sealed class S7dclImportHazardsTests
{
    private const string Fb = """
        {
            S7_Optimized := "TRUE";
            S7_PreferredLanguage := "LAD"
        }
        FUNCTION_BLOCK "Caller"
            VAR
                inst : _.Callee;
                {S7_TIMER}
            END_VAR

            { S7_Language := "LAD" }
            NETWORK
                RUNG wire#powerrail
                    #inst(
                        io := "Data_A".arr["Data_B".plain],
                        io2 := "Data_B".structArr[1].idx
                    )
                END_RUNG
            END_NETWORK
            { S7_Language := "SCL" }
            NETWORK
                #inst(io := "Data_A".arr["Data_B".structArr[1].idx], io2 := "Data_B".plain);
            END_NETWORK
        END_FUNCTION_BLOCK
        """;

    private static string With(string timer) => Fb.Replace("{S7_TIMER}", timer);

    [Fact]
    public void PlainBlock_HasNoHazards()
        => Assert.Empty(S7dclImportHazards.Find(With("tmr : TON_TIME;"), null));

    [Theory]
    [InlineData("tmr : _.TONR;", "tmr", "_.TONR")]
    [InlineData("tmr : _.TON;", "tmr", "_.TON")]
    [InlineData("edge : _.R_TRIG;", "edge", "_.R_TRIG")]
    [InlineData("tmr : \"TOF\";", "tmr", "\"TOF\"")]
    [InlineData("timers : Array[1..4] of _.TP;", "timers", "_.TP")]
    [InlineData("\"tmr 1\" : _.CTU;", "\"tmr 1\"", "_.CTU")]
    public void ProjectTypeNamedLikeSystemType_IsAHazard(string declaration, string member, string type)
    {
        var hazard = Assert.Single(S7dclImportHazards.Find(With(declaration), null));

        Assert.Equal(S7dclImportHazardKinds.SystemTypeName, hazard.Kind);
        Assert.Equal((member, type), (hazard.Subject, hazard.Evidence));
        Assert.Contains("system type", hazard.Detail);
    }

    [Theory]
    [InlineData("tmr : _.TONR_Own;")]
    [InlineData("tmr : _.MyTON;")]
    [InlineData("tmr : TONR_TIME;")]
    public void OtherTypes_AreNoHazard(string declaration)
        => Assert.Empty(S7dclImportHazards.Find(With(declaration), null));

    [Theory]
    [InlineData("LAD", "io := \"Data_A\".arr[\"Data_B\".structArr[1].idx],")]
    [InlineData("FBD", "io := #locArr[#locS[1].idx],")]
    [InlineData("LAD", "io := \"Data_A\".arr[#locS[1].idx],")]
    public void NestedIndexInLadOrFbd_IsAHazard(string language, string parameter)
    {
        var dcl = With(string.Empty).Replace("{ S7_Language := \"LAD\" }", $"{{ S7_Language := \"{language}\" }}")
            .Replace("io := \"Data_A\".arr[\"Data_B\".plain],", parameter);

        var hazard = Assert.Single(S7dclImportHazards.Find(dcl, null));

        Assert.Equal(S7dclImportHazardKinds.NestedIndex, hazard.Kind);
        Assert.Equal(("network 1", language, parameter), (hazard.Subject, hazard.Language, hazard.Evidence));
    }

    [Fact]
    public void BracketsInAQuotedName_AreNoNesting()
    {
        var dcl = With(string.Empty).Replace("io := \"Data_A\".arr[\"Data_B\".plain],", "io := \"Data_A\".arr[#\"locS[1]\".idx],");
        Assert.Empty(S7dclImportHazards.Find(dcl, null));
    }

    private const string OverlayXml = """
        <Document><SW.Blocks.FB ID="0"><AttributeList><Interface><Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
          <Section Name="Static">
            <Member Name="flags" Datatype="Array[1..16] of Bool" />
            <Member Name="flagsAt" Datatype="Word"><AttributeList><StringAttribute Name="At" SystemDefined="true">flags</StringAttribute></AttributeList></Member>
          </Section>
        </Sections></Interface></AttributeList></SW.Blocks.FB></Document>
        """;

    private const string OverlayFb = """
        {
            S7_Optimized := "TRUE"
        }
        FUNCTION_BLOCK "Overlay"
            VAR
                flags : Array[1..16] of Bool;
                flagsAt : Word;
            END_VAR

            { S7_Language := "SCL" }
            NETWORK
                #flagsAt := 16#0001;
            END_NETWORK
        END_FUNCTION_BLOCK
        """;

    [Fact]
    public void AtOverlays_ReadsTheAtAttribute()
        => Assert.Equal(new[] { new S7dclAtOverlay("flagsAt", "flags") }, S7dclImportHazards.AtOverlays(OverlayXml));

    [Fact]
    public void AtOverlays_OfAnUnreadableDocument_AreUnknown()
    {
        Assert.Null(S7dclImportHazards.AtOverlays("<Document><unclosed>"));
        Assert.Null(S7dclImportHazards.AtOverlays(string.Empty));
    }

    [Fact]
    public void DeclaredOverlayMember_IsAHazard_OnlyWhileStillDeclared()
    {
        var overlays = S7dclImportHazards.AtOverlays(OverlayXml)!;

        var hazard = Assert.Single(S7dclImportHazards.Find(OverlayFb, overlays));
        Assert.Equal((S7dclImportHazardKinds.AtOverlay, "flagsAt", "flags"), (hazard.Kind, hazard.Subject, hazard.Evidence));
        // The member is only used in code, not declared: the overlay is not imported as a variable.
        Assert.Empty(S7dclImportHazards.Find(OverlayFb.ReplaceLineEndings("\n").Replace("        flagsAt : Word;\n", string.Empty), overlays));
        Assert.Empty(S7dclImportHazards.Find(OverlayFb, null));
    }

    [Theory]
    [InlineData("S7_EditorMode := \"[SCL,TextualInterface]\"", true)]
    [InlineData("S7_EditorMode:=\"[LAD,textualinterface]\"", true)]
    [InlineData("S7_EditorMode := \"[SCL]\"", false)]
    [InlineData("// TextualInterface in a comment", false)]
    public void TextualInterface_IsTheEditorModeAttribute(string header, bool expected)
        => Assert.Equal(expected, S7dclImportHazards.HasTextualInterface($"{{\n    {header}\n}}\nFUNCTION_BLOCK \"X\"\nEND_FUNCTION_BLOCK\n"));

    [Fact]
    public void Compare_PreservedConstructs()
    {
        var requested = S7dclImportHazards.Find(With("tmr : _.TONR;"), null);

        var check = Assert.Single(S7dclImportHazards.Compare(requested, With("tmr  :  _.TONR ;"), null));

        Assert.Equal(S7dclImportHazards.Preserved, check.State);
    }

    [Fact]
    public void Compare_ReportsTheSystemTypeTheImportTook()
    {
        var requested = S7dclImportHazards.Find(With("tmr : _.TONR;"), null);

        var check = Assert.Single(S7dclImportHazards.Compare(requested, With("tmr : TONR_TIME;"), null));

        Assert.Equal((S7dclImportHazards.Altered, "TONR_TIME"), (check.State, check.Actual));
    }

    [Fact]
    public void Compare_NestedIndexTurnedIntoOneQuotedName_IsAltered()
    {
        var nested = With(string.Empty).Replace("io := \"Data_A\".arr[\"Data_B\".plain],", "io := \"Data_A\".arr[\"Data_B\".structArr[1].idx],");
        var mangled = With(string.Empty).Replace("io := \"Data_A\".arr[\"Data_B\".plain],", "io := \"Data_A\".arr[\"Data_B\".\"structArr[1]\".idx],");
        var requested = S7dclImportHazards.Find(nested, null);

        Assert.Equal(S7dclImportHazards.Preserved, Assert.Single(S7dclImportHazards.Compare(requested, nested, null)).State);
        Assert.Equal(S7dclImportHazards.Altered, Assert.Single(S7dclImportHazards.Compare(requested, mangled, null)).State);
    }

    [Fact]
    public void Compare_DroppedOverlay_IsAltered_AndAMissingDocumentUnverifiable()
    {
        var requested = S7dclImportHazards.Find(OverlayFb, S7dclImportHazards.AtOverlays(OverlayXml));
        var withoutAt = OverlayXml.Replace("<AttributeList><StringAttribute Name=\"At\" SystemDefined=\"true\">flags</StringAttribute></AttributeList>", string.Empty);

        Assert.Equal(S7dclImportHazards.Preserved, Assert.Single(S7dclImportHazards.Compare(requested, OverlayFb, OverlayXml)).State);
        var dropped = Assert.Single(S7dclImportHazards.Compare(requested, OverlayFb, withoutAt));
        Assert.Equal((S7dclImportHazards.Altered, "no AT overlay"), (dropped.State, dropped.Actual));
        Assert.Equal(S7dclImportHazards.Unverifiable, Assert.Single(S7dclImportHazards.Compare(requested, OverlayFb, null)).State);
    }
}
