using System.Net;
using System.Text;

namespace TiaMcpServer.SiemensDocs;

/// <summary>An element or text node of <see cref="SiemensDocsHtml"/>; <see cref="Name"/> is null for text.</summary>
public sealed class HtmlNode
{
    private HtmlNode(string? name, string? text, Dictionary<string, string>? attributes)
    {
        Name = name;
        Text = text;
        Attributes = attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public string? Name { get; }

    /// <summary>Decoded text of a text node.</summary>
    public string? Text { get; }

    public Dictionary<string, string> Attributes { get; }

    public List<HtmlNode> Children { get; } = new();

    public HtmlNode? Parent { get; private set; }

    public bool IsText => Name is null;

    public static HtmlNode Element(string name, Dictionary<string, string>? attributes = null) => new(name, null, attributes);

    public static HtmlNode TextNode(string text) => new(null, text, null);

    public void Add(HtmlNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

    public IReadOnlyList<string> Classes
        => Attribute("class") is { } value ? value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();

    public bool HasClass(string name) => Classes.Contains(name, StringComparer.Ordinal);

    /// <summary>All text below this node, in document order, without markup.</summary>
    public string InnerText()
    {
        if (IsText) return Text!;
        var builder = new StringBuilder();
        foreach (var node in Descendants())
            if (node.IsText) builder.Append(node.Text);
            else if (node.Name == "br") builder.Append('\n');
        return builder.ToString();
    }

    public IEnumerable<HtmlNode> Descendants()
    {
        var stack = new Stack<HtmlNode>();
        for (var i = Children.Count - 1; i >= 0; i--) stack.Push(Children[i]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }

    public void Remove() => Parent?.Children.Remove(this);
}

/// <summary>
/// A small, tolerant HTML parser for documentation topic fragments (no scripts are run, nothing is
/// fetched). It handles comments, doctype, quoted/unquoted attributes, void elements, raw text in
/// script/style, entity decoding and the usual implied end tags (p, li, dt/dd, tr, td/th, option);
/// a stray end tag is ignored. It is not a full HTML5 tree builder: it only needs to recover the
/// structure of generated documentation HTML well enough to convert it to Markdown.
/// </summary>
public static class SiemensDocsHtml
{
    public const int MaxDepth = 256;

    private static readonly HashSet<string> Void = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> RawText = new(StringComparer.Ordinal) { "script", "style", "textarea", "title" };

    /// <summary>Block starts that close an open p, as browsers do.</summary>
    private static readonly HashSet<string> ClosesParagraph = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "details", "div", "dl", "fieldset", "figcaption", "figure", "footer", "form",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "main", "nav", "ol", "p", "pre", "section", "table", "ul",
    };

    public static HtmlNode Parse(string html)
    {
        var root = HtmlNode.Element("#root");
        var stack = new List<HtmlNode> { root };
        var position = 0;
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length == 0) return;
            stack[^1].Add(HtmlNode.TextNode(WebUtility.HtmlDecode(text.ToString())));
            text.Clear();
        }

        while (position < html.Length)
        {
            var lt = html.IndexOf('<', position);
            if (lt < 0)
            {
                text.Append(html, position, html.Length - position);
                break;
            }

            text.Append(html, position, lt - position);
            position = lt;
            if (StartsWith(html, position, "<!--"))
            {
                FlushText();
                var end = html.IndexOf("-->", position + 4, StringComparison.Ordinal);
                position = end < 0 ? html.Length : end + 3;
                continue;
            }

            if (StartsWith(html, position, "<!") || StartsWith(html, position, "<?"))
            {
                FlushText();
                var end = html.IndexOf('>', position + 2);
                position = end < 0 ? html.Length : end + 1;
                continue;
            }

            var closing = position + 1 < html.Length && html[position + 1] == '/';
            var nameStart = position + (closing ? 2 : 1);
            if (nameStart >= html.Length || !char.IsAsciiLetter(html[nameStart]))
            {
                text.Append('<');
                position++;
                continue;
            }

            FlushText();
            var (name, attributes, selfClosing, next) = ReadTag(html, nameStart);
            position = next;
            if (closing)
            {
                Close(stack, name);
                continue;
            }

            ImpliedEnds(stack, name);
            var element = HtmlNode.Element(name, attributes);
            stack[^1].Add(element);
            if (Void.Contains(name) || selfClosing) continue;
            if (RawText.Contains(name))
            {
                var endTag = html.IndexOf("</" + name, position, StringComparison.OrdinalIgnoreCase);
                var content = endTag < 0 ? html[position..] : html[position..endTag];
                if (name is "title" or "textarea") element.Add(HtmlNode.TextNode(WebUtility.HtmlDecode(content)));
                position = endTag < 0 ? html.Length : Math.Max(endTag, html.IndexOf('>', endTag) + 1);
                continue;
            }

            if (stack.Count < MaxDepth) stack.Add(element);
        }

        FlushText();
        return root;
    }

    private static void ImpliedEnds(List<HtmlNode> stack, string name)
    {
        if (ClosesParagraph.Contains(name)) CloseIfOpen(stack, "p", "div", "td", "th", "li", "dd", "dt", "blockquote", "table");
        switch (name)
        {
            case "li":
                CloseIfOpen(stack, "li", "ul", "ol");
                break;
            case "dt":
            case "dd":
                CloseIfOpen(stack, "dt", "dl");
                CloseIfOpen(stack, "dd", "dl");
                break;
            case "tr":
                CloseIfOpen(stack, "td", "tr", "table");
                CloseIfOpen(stack, "th", "tr", "table");
                CloseIfOpen(stack, "tr", "table");
                break;
            case "td":
            case "th":
                CloseIfOpen(stack, "td", "tr", "table");
                CloseIfOpen(stack, "th", "tr", "table");
                break;
            case "thead":
            case "tbody":
            case "tfoot":
                CloseIfOpen(stack, "td", "table");
                CloseIfOpen(stack, "th", "table");
                CloseIfOpen(stack, "tr", "table");
                foreach (var section in new[] { "thead", "tbody", "tfoot" }) CloseIfOpen(stack, section, "table");
                break;
            case "option":
                CloseIfOpen(stack, "option", "select");
                break;
        }
    }

    /// <summary>Closes the innermost open <paramref name="name"/> when no <paramref name="boundaries"/> element is open inside it.</summary>
    private static void CloseIfOpen(List<HtmlNode> stack, string name, params string[] boundaries)
    {
        for (var i = stack.Count - 1; i > 0; i--)
        {
            if (stack[i].Name == name)
            {
                stack.RemoveRange(i, stack.Count - i);
                return;
            }

            if (boundaries.Contains(stack[i].Name, StringComparer.Ordinal)) return;
        }
    }

    private static void Close(List<HtmlNode> stack, string name)
    {
        for (var i = stack.Count - 1; i > 0; i--)
        {
            if (stack[i].Name != name) continue;
            stack.RemoveRange(i, stack.Count - i);
            return;
        }
    }

    private static (string Name, Dictionary<string, string> Attributes, bool SelfClosing, int Next) ReadTag(string html, int start)
    {
        var i = start;
        while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '/') i++;
        var name = html[start..i].ToLowerInvariant();
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var selfClosing = false;
        while (i < html.Length)
        {
            while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
            if (i >= html.Length) break;
            if (html[i] == '>') return (name, attributes, selfClosing, i + 1);
            if (html[i] == '/')
            {
                selfClosing = true;
                i++;
                continue;
            }

            var attributeStart = i;
            while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '>' && html[i] != '/') i++;
            var attributeName = html[attributeStart..i];
            if (attributeName.Length == 0)
            {
                i++;
                continue;
            }

            selfClosing = false;
            while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
            var value = string.Empty;
            if (i < html.Length && html[i] == '=')
            {
                i++;
                while (i < html.Length && char.IsWhiteSpace(html[i])) i++;
                if (i < html.Length && html[i] is '"' or '\'')
                {
                    var quote = html[i];
                    var end = html.IndexOf(quote, i + 1);
                    if (end < 0) end = html.Length;
                    value = html[(i + 1)..end];
                    i = Math.Min(end + 1, html.Length);
                }
                else
                {
                    var valueStart = i;
                    while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>') i++;
                    value = html[valueStart..i];
                }
            }

            attributes.TryAdd(attributeName, WebUtility.HtmlDecode(value));
        }

        return (name, attributes, selfClosing, html.Length);
    }

    private static bool StartsWith(string html, int position, string value)
        => string.CompareOrdinal(html, position, value, 0, value.Length) == 0;
}
