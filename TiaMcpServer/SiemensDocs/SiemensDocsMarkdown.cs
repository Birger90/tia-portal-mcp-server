// Ported from Czarnak/siemens-docs-mcp (MIT, Copyright (c) 2026 Łukasz Czarnacki), siemens_docs_mcp/converter.py and
// content.py; reviewed and rewritten in C#. Differences found against the live HTML of 2026-11-03 are noted inline.
// See THIRD_PARTY_NOTICES.md.
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.SiemensDocs;

/// <summary>
/// Converts one documentation topic (the HTML fragment of <c>/api/khub/maps/{id}/topics/{cid}/content</c>) to
/// Markdown. It understands the Siemens TIA help classes (BlocktitleFirst/Blocktitle headings, table_sourcecode and
/// p_table_l_code code, safety note tables, p_table_title captions), MkDocs admonitions of the Industrial Operations X
/// pages, regular tables (GFM), nested lists and fenced code. Text only: embedded data-URI images become their alt
/// text; links and https images are kept. Reader chrome (navigation, breadcrumbs, feedback, buttons, the reader's
/// action elements) and the TIA title banner (repeated by the H1) are removed.
/// </summary>
public static partial class SiemensDocsMarkdown
{
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "script", "style", "head", "nav", "button", "form", "meta", "link", "noscript", "template", "svg", "iframe", "object", "input", "select",
    };

    private static readonly HashSet<string> Inline = new(StringComparer.Ordinal)
    {
        "a", "abbr", "b", "strong", "i", "em", "code", "span", "img", "sub", "sup", "u", "br", "small", "big", "kbd", "samp", "var", "mark", "q",
        "cite", "dfn", "s", "strike", "del", "ins", "font", "tt", "label", "time", "wbr",
    };

    private static readonly string[] ChromeClassParts = { "breadcrumb", "feedback", "rating", "prev-next", "pagination" };

    /// <summary>Converts a topic to Markdown that starts with <c># title</c>; <paramref name="host"/> makes root-relative links absolute.</summary>
    public static string Convert(string html, string title, string host)
    {
        var heading = "# " + Clean(title);
        if (string.IsNullOrWhiteSpace(html)) return heading + "\n\n*No content available.*\n";
        var root = SiemensDocsHtml.Parse(html);
        RemoveChrome(root);
        var body = PostProcess(new Renderer(host).Container(root));
        return body.Length == 0 ? heading + "\n\n*No content available.*\n" : heading + "\n\n" + body + "\n";
    }

    /// <summary>Plain text of an HTML snippet (search excerpts), whitespace collapsed.</summary>
    public static string PlainText(string? html)
        => string.IsNullOrEmpty(html) ? string.Empty : Clean(SiemensDocsHtml.Parse(html).InnerText());

    private static void RemoveChrome(HtmlNode root)
    {
        foreach (var node in root.Descendants().Where(IsChrome).ToArray()) node.Remove();
    }

    private static bool IsChrome(HtmlNode node)
    {
        if (node.IsText) return false;
        if (node.Name!.StartsWith("ft-reader-", StringComparison.Ordinal) || node.Name == "ft-slotted-floating-menu") return true;
        if (node.Attribute("role") == "navigation") return true;
        // Live 2026-11-03: every TIA topic starts with <div id="nsbanner"> holding the title in a table; the reference
        // implementation rendered it as a one-cell table under the H1.
        if (node.Attribute("id") == "nsbanner") return true;
        var classes = node.Attribute("class");
        return classes is not null && ChromeClassParts.Any(part => classes.Contains(part, StringComparison.Ordinal));
    }

    internal static string Clean(string text)
        => Whitespace().Replace(text.Replace(' ', ' '), " ").Trim();

    /// <summary>Code text: newlines kept, non-breaking spaces made spaces, outer blank lines and trailing blanks removed.</summary>
    private static string NormalizeCode(string code)
    {
        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ').Split('\n').Select(line => line.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static string Fence(string code)
    {
        if (code.Length == 0) return string.Empty;
        var longest = Backticks().Matches(code).Select(match => match.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}\n{code}\n{fence}";
    }

    private static string PostProcess(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd());
        var builder = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (++blank > 1) continue;
            }
            else
            {
                blank = 0;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex("`+")]
    private static partial Regex Backticks();

    private sealed class Renderer(string host)
    {
        /// <summary>Renders the children of a block container: inline runs become paragraphs, block children their own blocks.</summary>
        public string Container(HtmlNode node)
        {
            var blocks = new List<string>();
            var run = new StringBuilder();

            void Flush()
            {
                var paragraph = Paragraph(run.ToString());
                if (paragraph.Length > 0) blocks.Add(paragraph);
                run.Clear();
            }

            foreach (var child in node.Children)
            {
                if (child.IsText || Inline.Contains(child.Name!))
                {
                    run.Append(InlineText(child));
                    continue;
                }

                Flush();
                var rendered = Block(child);
                // Wrapper divs sometimes repeat a block; keep adjacent duplicates once (as the reference does).
                if (rendered.Length > 0 && (blocks.Count == 0 || blocks[^1] != rendered)) blocks.Add(rendered);
            }

            Flush();
            return string.Join("\n\n", blocks);
        }

        private string Block(HtmlNode node)
        {
            var name = node.Name!;
            if (Skipped.Contains(name)) return string.Empty;
            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    var heading = Clean(Inlines(node));
                    return heading.Length == 0 ? string.Empty : new string('#', name[1] - '0') + " " + heading;
                case "p":
                    return ParagraphElement(node);
                case "ul" or "ol":
                    return List(node, 0);
                case "table":
                    if (node.HasClass("table_sourcecode")) return SourceCodeTable(node);
                    if (node.HasClass("safety")) return SafetyTable(node);
                    return Table(node);
                case "div" when node.HasClass("admonition"):
                    return Admonition(node);
                case "pre":
                    return Fence(NormalizeCode(node.InnerText()));
                case "img":
                    return Image(node);
                case "blockquote":
                    return Quote(Container(node));
                case "dl":
                    return DefinitionList(node);
                case "hr":
                    return string.Empty;
                default:
                    return Container(node);
            }
        }

        private string ParagraphElement(HtmlNode node)
        {
            if (node.HasClass("p_table_l_code") || node.HasClass("table_sourcecode"))
                return Fence(NormalizeCode(CodeText(node)));
            if (node.HasClass("BlocktitleFirst") || node.HasClass("Blocktitle"))
            {
                var title = Clean(Inlines(node));
                return title.Length == 0 ? string.Empty : "## " + title;
            }

            if (node.HasClass("p_table_title"))
            {
                // The reference implementation drops table captions; live TIA pages put the only name of a table here
                // (for example "Screens" in the Openness object list), so it is kept as a bold caption.
                var caption = Clean(Inlines(node));
                return caption.Length == 0 ? string.Empty : "**" + caption + "**";
            }

            return node.Children.Any(child => !child.IsText && !Inline.Contains(child.Name!)) ? Container(node) : Paragraph(Inlines(node));
        }

        /// <summary>
        /// Live 2026-11-03: a TIA code line break is a &lt;br&gt; inside one p_table_l_code paragraph. The reference
        /// implementation used get_text() there, which drops &lt;br&gt; and joined whole listings into one line.
        /// </summary>
        private static string CodeText(HtmlNode node) => node.InnerText();

        private string SourceCodeTable(HtmlNode table)
        {
            var lines = table.Descendants().Where(node => node.Name == "p" && node.HasClass("p_table_l_code")).Select(CodeText).ToArray();
            var code = lines.Length > 0
                ? string.Join("\n", lines)
                : string.Join("\n", table.Descendants().Where(node => node.Name is "td").Select(CodeText));
            return Fence(NormalizeCode(code));
        }

        private static string SafetyTable(HtmlNode table)
        {
            var lines = new List<string>();
            foreach (var block in table.Descendants().Where(node => node.Name is "p" or "li"))
            {
                if (block.Descendants().Any(inner => inner.Name is "p" or "li")) continue;
                var text = Clean(block.InnerText());
                if (text.Length > 0) lines.Add(block.Name == "li" ? "- " + text : text);
            }

            if (lines.Count == 0)
                lines.AddRange(table.InnerText().Split('\n').Select(Clean).Where(line => line.Length > 0));
            if (lines.Count == 0) return string.Empty;
            var label = lines[0];
            if (label.ToLowerInvariant() is "note" or "notice" or "warning" or "caution" or "danger" or "hinweis" or "warnung" or "vorsicht" or "gefahr" or "achtung")
            {
                var heading = char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
                return lines.Count == 1 ? $"> **{heading}**" : $"> **{heading}**\n>\n> " + string.Join("\n> ", lines.Skip(1));
            }

            return "> " + string.Join("\n> ", lines);
        }

        private string Table(HtmlNode table)
        {
            var rows = new List<List<string>>();
            foreach (var row in Rows(table))
            {
                var cells = row.Children.Where(cell => cell.Name is "td" or "th")
                    .Select(cell => Clean(CellText(cell)).Replace("|", "\\|", StringComparison.Ordinal)).ToList();
                if (cells.Count > 0) rows.Add(cells);
            }

            if (rows.Count == 0) return string.Empty;
            var columns = rows.Max(row => row.Count);
            foreach (var row in rows) while (row.Count < columns) row.Add(string.Empty);
            if (rows.Count == 1) return "| " + string.Join(" | ", rows[0]) + " |";
            var lines = new List<string>
            {
                "| " + string.Join(" | ", rows[0]) + " |",
                "| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |",
            };
            lines.AddRange(rows.Skip(1).Select(row => "| " + string.Join(" | ", row) + " |"));
            return string.Join("\n", lines);
        }

        /// <summary>The rows of this table (directly or in thead/tbody/tfoot), not those of nested tables.</summary>
        private static IEnumerable<HtmlNode> Rows(HtmlNode table)
        {
            foreach (var child in table.Children)
            {
                if (child.Name == "tr") yield return child;
                else if (child.Name is "thead" or "tbody" or "tfoot")
                    foreach (var row in child.Children.Where(row => row.Name == "tr")) yield return row;
            }
        }

        /// <summary>Cell content on one line: paragraphs and list items are joined with "; " style separators.</summary>
        private string CellText(HtmlNode cell)
        {
            var parts = new List<string>();
            var run = new StringBuilder();
            void Flush()
            {
                var text = Clean(run.ToString());
                if (text.Length > 0) parts.Add(text);
                run.Clear();
            }

            foreach (var child in cell.Children)
            {
                if (child.IsText || Inline.Contains(child.Name!))
                {
                    run.Append(InlineText(child).Replace('\n', ' '));
                    continue;
                }

                Flush();
                if (child.Name is "ul" or "ol")
                    parts.AddRange(child.Children.Where(item => item.Name == "li").Select(item => "• " + Clean(CellText(item))).Where(item => item.Length > 2));
                else if (child.Name is "table")
                    parts.Add(Clean(child.InnerText()));
                else if (!Skipped.Contains(child.Name!))
                {
                    var text = Clean(CellText(child));
                    if (text.Length > 0) parts.Add(text);
                }
            }

            Flush();
            return string.Join("<br>", parts);
        }

        private string Admonition(HtmlNode div)
        {
            var titleNode = div.Children.FirstOrDefault(child => child.HasClass("admonition-title"));
            var label = titleNode is not null
                ? Clean(titleNode.InnerText())
                : div.Classes.FirstOrDefault(name => name != "admonition") is { } kind ? char.ToUpperInvariant(kind[0]) + kind[1..] : "Note";
            if (titleNode is not null) titleNode.Remove();
            var body = Container(div);
            if (body.Length == 0) return $"> **{label}:**";
            var lines = body.Split('\n');
            return string.Join("\n", new[] { $"> **{label}:** {lines[0]}" }.Concat(lines.Skip(1).Select(line => ("> " + line).TrimEnd())));
        }

        private string DefinitionList(HtmlNode list)
        {
            var lines = new List<string>();
            foreach (var child in list.Children.Where(child => child.Name is "dt" or "dd"))
            {
                var text = Clean(Inlines(child));
                if (text.Length == 0) continue;
                lines.Add(child.Name == "dt" ? "**" + text + "**" : ": " + text);
            }

            return string.Join("\n", lines);
        }

        private string List(HtmlNode list, int level)
        {
            var ordered = list.Name == "ol";
            var lines = new List<string>();
            var index = 1;
            foreach (var item in list.Children.Where(child => child.Name == "li"))
            {
                var bullet = ordered ? $"{index++}." : "-";
                var indent = new string(' ', level * 2);
                var continuation = new string(' ', (level + 1) * 2);
                var texts = new List<string>();
                var nested = new List<string>();
                var run = new StringBuilder();
                void Flush()
                {
                    var text = Paragraph(run.ToString());
                    if (text.Length > 0) texts.Add(text);
                    run.Clear();
                }

                foreach (var child in item.Children)
                {
                    if (child.IsText || Inline.Contains(child.Name!))
                    {
                        run.Append(InlineText(child));
                        continue;
                    }

                    Flush();
                    if (child.Name is "ul" or "ol") nested.Add(List(child, level + 1));
                    else if (child.Name == "table" || child.Name == "pre" || child.HasClass("p_table_l_code") || child.HasClass("admonition"))
                    {
                        var block = Block(child);
                        if (block.Length > 0) nested.Add(string.Join("\n", block.Split('\n').Select(line => continuation + line)));
                    }
                    else
                    {
                        var block = Block(child);
                        if (block.Length > 0) texts.Add(block);
                    }
                }

                Flush();
                var itemText = string.Join("\n\n", texts).Trim();
                var itemLines = itemText.Length == 0 ? new[] { string.Empty } : itemText.Split('\n');
                lines.Add((indent + bullet + " " + itemLines[0]).TrimEnd());
                lines.AddRange(itemLines.Skip(1).Select(line => line.Length == 0 ? string.Empty : continuation + line));
                lines.AddRange(nested.Where(block => block.Length > 0));
            }

            return string.Join("\n", lines).TrimEnd();
        }

        private static string Quote(string body)
            => body.Length == 0 ? string.Empty : string.Join("\n", body.Split('\n').Select(line => ("> " + line).TrimEnd()));

        private string Inlines(HtmlNode node) => string.Concat(node.Children.Select(InlineText));

        /// <summary>A paragraph from inline Markdown: whitespace collapsed per line, &lt;br&gt; line breaks kept.</summary>
        private static string Paragraph(string inline)
            => string.Join("\n", inline.Replace(' ', ' ').Split('\n').Select(line => HorizontalWhitespace().Replace(line.Replace('\t', ' '), " ").Trim())
                .Where(line => line.Length > 0));

        private string InlineText(HtmlNode node)
        {
            if (node.IsText) return Whitespace().Replace(node.Text!, " ");
            var name = node.Name!;
            if (Skipped.Contains(name)) return string.Empty;
            switch (name)
            {
                case "br":
                    return "\n";
                case "a":
                {
                    var label = Clean(Inlines(node));
                    var href = Link(node.Attribute("href"));
                    if (href is null) return label;
                    return label.Length == 0 ? string.Empty : $"[{label}]({href})";
                }
                case "b" or "strong":
                    return Wrap("**", Clean(Inlines(node)), Inlines(node));
                case "i" or "em":
                    return Wrap("*", Clean(Inlines(node)), Inlines(node));
                case "code" or "kbd" or "samp" or "tt":
                {
                    var code = Clean(Inlines(node));
                    if (code.Length == 0) return string.Empty;
                    var longest = Backticks().Matches(code).Select(match => match.Length).DefaultIfEmpty(0).Max();
                    var ticks = new string('`', longest + 1);
                    return longest > 0 ? $"{ticks} {code} {ticks}" : $"`{code}`";
                }
                case "img":
                    return Image(node);
                default:
                    return Inlines(node);
            }
        }

        /// <summary>Emphasis around trimmed text, keeping the outer spaces the markup had ("a <b> b </b> c" → "a **b** c").</summary>
        private static string Wrap(string marker, string clean, string raw)
        {
            if (clean.Length == 0) return raw.Length > 0 && char.IsWhiteSpace(raw[0]) ? " " : string.Empty;
            var before = raw.Length > 0 && char.IsWhiteSpace(raw[0]) ? " " : string.Empty;
            var after = raw.Length > 0 && char.IsWhiteSpace(raw[^1]) ? " " : string.Empty;
            return before + marker + clean + marker + after;
        }

        private string Image(HtmlNode node)
        {
            var alt = Clean(node.Attribute("alt") ?? string.Empty);
            var src = node.Attribute("src") ?? string.Empty;
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return alt;
            var link = Link(src);
            return link is null ? alt : $"![{alt}]({link})";
        }

        /// <summary>Keeps http(s) links and makes root-relative ones absolute on the page's host; other schemes are dropped.</summary>
        private string? Link(string? href)
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            href = href.Trim();
            if (href.StartsWith('#')) return null;
            if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
            else if (href.StartsWith('/')) href = $"https://{host}{href}";
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
            var text = uri.AbsoluteUri;
            // The TIA reader appends an empty "?section=" to internal links.
            if (text.EndsWith("?section=", StringComparison.Ordinal)) text = text[..^"?section=".Length];
            return text.Replace(" ", "%20", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal);
        }
    }
}
