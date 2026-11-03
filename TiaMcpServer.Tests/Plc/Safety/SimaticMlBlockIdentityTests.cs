using TiaMcpServer.Contracts.Plc.Interface;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Safety;

/// <summary>
/// The SimaticML identity edit of the main safety block route (live workaround L4d, TIA Portal V21, 2026-11-02): only the block
/// AttributeList's Name, Number and AutoNumber change, every other character stays. Every case runs with LF and CRLF line endings.
/// </summary>
public sealed class SimaticMlBlockIdentityTests
{
    /// <summary>The temporary F-FB's export: no AutoNumber (as live), an interface member with its own nested AttributeList and Name-like content.</summary>
    private const string TemporaryExport =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<Document>\n"
        + "  <Engineering version=\"V21\" />\n"
        + "  <SW.Blocks.FB ID=\"0\">\n"
        + "    <AttributeList>\n"
        + "      <Interface><Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\">\n"
        + "  <Section Name=\"Input\">\n"
        + "    <Member Name=\"Start\" Datatype=\"Bool\">\n"
        + "      <AttributeList>\n"
        + "        <BooleanAttribute Name=\"ExternalAccessible\" SystemDefined=\"true\">true</BooleanAttribute>\n"
        + "      </AttributeList>\n"
        + "    </Member>\n"
        + "  </Section>\n"
        + "</Sections></Interface>\n"
        + "      <MemoryLayout>Optimized</MemoryLayout>\n"
        + "      <Name>zzMcpTmp_Main_Safety_RTG1_0a1b2c3d</Name>\n"
        + "      <Namespace />\n"
        + "      <Number>2</Number>\n"
        + "      <ProgrammingLanguage>F_FBD</ProgrammingLanguage>\n"
        + "    </AttributeList>\n"
        + "    <ObjectList>\n"
        + "      <MultilingualText ID=\"1\" CompositionName=\"Comment\"><ObjectList><MultilingualTextItem ID=\"2\" CompositionName=\"Items\">\n"
        + "        <AttributeList><Culture>de-DE</Culture><Text>&lt;Name&gt; 2</Text></AttributeList>\n"
        + "      </MultilingualTextItem></ObjectList></MultilingualText>\n"
        + "    </ObjectList>\n"
        + "  </SW.Blocks.FB>\n"
        + "</Document>\n";

    public static TheoryData<string> LineEndings => new() { "\n", "\r\n" };

    private static string With(string newline, string text) => text.Replace("\n", newline);

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void Read_FindsTheBlockAttributeListIdentity_NotNestedOnes(string newline)
    {
        var values = SimaticMlBlockIdentity.Read(With(newline, TemporaryExport));

        Assert.Equal("SW.Blocks.FB", values.BlockElement);
        Assert.Equal("zzMcpTmp_Main_Safety_RTG1_0a1b2c3d", values.Name);
        Assert.Equal("2", values.Number);
        Assert.Null(values.AutoNumber);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void Apply_SetsNameAndNumber_InsertsAutoNumberFirst_AndKeepsEveryOtherCharacter(string newline)
    {
        var source = With(newline, TemporaryExport);

        var result = SimaticMlBlockIdentity.Apply(source, "Main_Safety_RTG1", 1, false);

        var expected = source
            .Replace("<Name>zzMcpTmp_Main_Safety_RTG1_0a1b2c3d</Name>", "<Name>Main_Safety_RTG1</Name>")
            .Replace("<Number>2</Number>", "<Number>1</Number>")
            .Replace($"<AttributeList>{newline}      <Interface>", $"<AttributeList>{newline}      <AutoNumber>false</AutoNumber>{newline}      <Interface>");
        Assert.Equal(expected, result);
        var values = SimaticMlBlockIdentity.Read(result);
        Assert.Equal(("Main_Safety_RTG1", "1", "false"), (values.Name, values.Number, values.AutoNumber));
        Assert.Contains("&lt;Name&gt; 2", result);
    }

    [Theory]
    [MemberData(nameof(LineEndings))]
    public void Apply_ReplacesAnExistingAutoNumberInPlace_AndLeavesItAloneWhenUnknown(string newline)
    {
        var source = With(newline, TemporaryExport).Replace($"<AttributeList>{newline}      <Interface>", $"<AttributeList>{newline}      <AutoNumber>true</AutoNumber>{newline}      <Interface>");

        var replaced = SimaticMlBlockIdentity.Apply(source, "Main_Safety_RTG1", 1, false);
        Assert.Equal(source.Replace("<AutoNumber>true</AutoNumber>", "<AutoNumber>false</AutoNumber>")
            .Replace("<Name>zzMcpTmp_Main_Safety_RTG1_0a1b2c3d</Name>", "<Name>Main_Safety_RTG1</Name>").Replace("<Number>2</Number>", "<Number>1</Number>"), replaced);

        var unknown = SimaticMlBlockIdentity.Apply(source, "Main_Safety_RTG1", 1, null);
        Assert.Contains("<AutoNumber>true</AutoNumber>", unknown);
    }

    [Fact]
    public void Apply_EscapesTheName_AndKeepsABom()
    {
        var source = "﻿" + TemporaryExport;

        var result = SimaticMlBlockIdentity.Apply(source, "A&B<C>", 7, true);

        Assert.StartsWith("﻿<?xml", result);
        Assert.Contains("<Name>A&amp;B&lt;C&gt;</Name>", result);
        Assert.Equal("A&B<C>", SimaticMlBlockIdentity.Read(result).Name);
        Assert.Equal("7", SimaticMlBlockIdentity.Read(result).Number);
    }

    [Theory]
    [InlineData("<Document><Engineering version=\"V21\" /></Document>", "no Document/SW.Blocks")]
    [InlineData("<Document><SW.Blocks.FB ID=\"0\"><ObjectList /></SW.Blocks.FB></Document>", "not AttributeList")]
    [InlineData("<Document><SW.Blocks.FB ID=\"0\"><AttributeList><Number>2</Number></AttributeList></SW.Blocks.FB></Document>", "no Name element")]
    [InlineData("<Document><SW.Blocks.FB ID=\"0\"><AttributeList><Name>x</Name><Number>2</Number></AttributeList></SW.Blocks.FB><SW.Blocks.FC ID=\"1\" /></Document>", "more than one")]
    [InlineData("<Document><SW.Blocks.FB>", "not well-formed")]
    public void Apply_RefusesAnythingButOneBlockDocument(string xml, string reason)
        => Assert.Contains(reason, Assert.Throws<FormatException>(() => SimaticMlBlockIdentity.Apply(xml, "Main", 1, false)).Message);

    [Fact]
    public void Apply_HandlesAnEmptyNameElementAndCompactDocuments()
    {
        var result = SimaticMlBlockIdentity.Apply("<Document><SW.Blocks.FC ID=\"0\"><AttributeList><Name/><Number>3</Number></AttributeList></SW.Blocks.FC></Document>", "FC_Main", 4, false);

        Assert.Equal("<Document><SW.Blocks.FC ID=\"0\"><AttributeList><AutoNumber>false</AutoNumber><Name>FC_Main</Name><Number>4</Number></AttributeList></SW.Blocks.FC></Document>", result);
    }
}
