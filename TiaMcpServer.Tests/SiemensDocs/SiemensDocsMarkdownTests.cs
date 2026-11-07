using TiaMcpServer.SiemensDocs;
using Xunit;
using static TiaMcpServer.Tests.SiemensDocs.SiemensDocsFixtures;

namespace TiaMcpServer.Tests.SiemensDocs;

public sealed class SiemensDocsMarkdownTests
{
    private static string Convert(string html, string title = "T", string host = Tia) => SiemensDocsMarkdown.Convert(html, title, host);

    [Fact]
    public void TiaTopic_KeepsCodeLineBreaks_NotesAndHeadings_WithoutTheTitleBanner()
    {
        var markdown = Convert(Text("content-tia-connecting.html"), "Connecting to the TIA Portal");
        Assert.StartsWith("# Connecting to the TIA Portal\n\n## Introduction\n", markdown, StringComparison.Ordinal);
        // The banner table repeating the title is gone (the reference rendered "| Connecting to the TIA Portal |").
        Assert.DoesNotContain("| Connecting to the TIA Portal |", markdown);
        Assert.Contains("> **Note**\n>\n> Since you always use TIA Portal Openness", markdown);
        Assert.Contains("## Using the app.config file", markdown);
        // A <br> inside p_table_l_code is a code line break (the reference joined the listing into one line).
        Assert.Contains("```\n<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<configuration>\n  <startup>\n", markdown);
        Assert.Contains("internal static class Program\n{\n    static Program()\n", markdown);
        Assert.DoesNotContain("Copies the following program code to clipboard", markdown);
        Assert.Contains("- get running TIA Portal processes and attach to TIA Portal instances\n- start new TIA Portal instances", markdown);
        // Internal links keep the host, lose the reader's empty ?section=.
        Assert.Contains("[TIA Portal Openness firewall](https://docs.tia.siemens.cloud/r/gpR5ZkKnLSuzVoGX1ovoKg/IU4JgoDdho07e_RMo4DfIg)", markdown);
        Assert.DoesNotContain("?section=", markdown);
        Assert.DoesNotContain("<meta", markdown);
    }

    [Fact]
    public void TiaObjectList_KeepsTheTableCaption_AndRendersAGfmTable()
    {
        var markdown = Convert(Text("content-tia-object-list.html"), "Object list");
        Assert.Contains("**Screens**\n\n| Object | Basic Panels | Comfort Panels | Mobile Panels | RT Advanced |\n| --- | --- | --- | --- | --- |\n| Screen | Yes | Yes | Yes | Yes |",
            markdown);
    }

    [Fact]
    public void IoxTopic_RendersAdmonitionsAndInlineCode()
    {
        var markdown = Convert(Text("content-iox-hsc-overview.html"), "High-Speed Counter overview", Iox);
        Assert.Contains("> **WARNING:** While this package remains fully functional", markdown);
        Assert.Contains("`@ax-as/s7-highspeedcounter-lib`", markdown);
        Assert.Contains("> **Note:** This library requires the PLC", markdown);
        Assert.Contains("[2610 breaking changes documentation](https://docs.industrial-operations-x.siemens.cloud/access?", markdown);
    }

    [Fact]
    public void Empty_SaysSo()
        => Assert.Equal("# T\n\n*No content available.*\n", Convert("  "));

    [Fact]
    public void Parser_ToleratesUnclosedParagraphsAndListItems_AndDecodesEntities()
    {
        var markdown = Convert("<p>one &amp; two<p>three<ul><li>a<li>b &lt;c&gt;</ul><p>after");
        Assert.Equal("# T\n\none & two\n\nthree\n\n- a\n- b <c>\n\nafter\n", markdown);
    }

    [Fact]
    public void ScriptsStylesChromeAndButtonsAreDropped()
    {
        var markdown = Convert("<nav>menu</nav><div class=\"breadcrumb\">a > b</div><script>alert('<p>x</p>')</script><style>p{}</style>"
                               + "<p>kept<button>Copy</button></p><ft-reader-action>act</ft-reader-action><div role=\"navigation\">n</div>");
        Assert.Equal("# T\n\nkept\n", markdown);
    }

    [Fact]
    public void Images_DataUrisBecomeAltText_HttpsImagesStay_OtherSchemesAreDropped()
    {
        var markdown = Convert("<p><img src=\"data:image/png;base64,AAAA\" alt=\"Diagram\"> <img src=\"https://docs.tia.siemens.cloud/i.png\" alt=\"Pic\">"
                               + " <a href=\"javascript:alert(1)\">bad</a> <a href=\"/r/x/y\">rel</a></p>");
        Assert.Contains("Diagram ![Pic](https://docs.tia.siemens.cloud/i.png) bad [rel](https://docs.tia.siemens.cloud/r/x/y)", markdown);
    }

    [Fact]
    public void Tables_EscapePipes_PadRows_AndIgnoreNestedRows()
    {
        var markdown = Convert("<table><tr><th>A</th><th>B</th></tr><tr><td>x|y</td></tr><tr><td><p>p1</p><p>p2</p></td><td><table><tr><td>inner</td></tr></table></td></tr></table>");
        Assert.Contains("| A | B |\n| --- | --- |\n| x\\|y |  |\n| p1<br>p2 | inner |", markdown);
    }

    [Fact]
    public void Code_ContainingBackticks_GetsALongerFence()
    {
        var markdown = Convert("<pre>a ``` b</pre>");
        Assert.Contains("````\na ``` b\n````", markdown);
    }

    [Fact]
    public void NestedListsAndOrderedLists()
    {
        var markdown = Convert("<ol><li>first<ul><li>inner</li></ul></li><li><p>second</p></li></ol>");
        Assert.Contains("1. first\n  - inner\n2. second", markdown);
    }

    [Fact]
    public void Emphasis_KeepsSurroundingSpaces()
        => Assert.Contains("a **b** c *d*", Convert("<p>a<b> b </b>c <em>d</em></p>"));

    [Fact]
    public void DeeplyNestedMarkup_DoesNotOverflow()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 5000)) + "deep" + string.Concat(Enumerable.Repeat("</div>", 5000));
        Assert.Contains("deep", Convert(html));
    }

    [Fact]
    public void PlainText_OfASearchExcerpt()
        => Assert.Equal("...control. You can use the ExportAsDocuments() method",
            SiemensDocsMarkdown.PlainText("<span class=\"kwictruncate\">...</span><span class=\"kwicstring\">control. You can use the </span><span class=\"kwicmatch\">ExportAsDocuments</span><span class=\"kwicstring\">() method</span>"));
}
