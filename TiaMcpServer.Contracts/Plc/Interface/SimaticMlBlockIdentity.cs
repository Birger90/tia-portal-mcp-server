using System.Globalization;
using System.Xml;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>The identity elements of a SimaticML block document as written (inner text, null when the element is absent).</summary>
public sealed record SimaticMlBlockIdentityValues(string BlockElement, string? Name, string? Number, string? AutoNumber);

/// <summary>
/// Reads and sets the identity of a SimaticML block document: the <c>Name</c>, <c>Number</c> and <c>AutoNumber</c> children
/// of the block element's own <c>AttributeList</c> (<c>Document/SW.Blocks.*/AttributeList</c>; never the nested
/// AttributeLists of interface members). Used by the main safety block route (live workaround, TIA Portal V21, 2026-11-02,
/// L4d): the SimaticML of a temporary F-block is imported onto the main safety block after its name, number and AutoNumber
/// were set to the main block's. Only those three elements change; every other character of the document (whitespace, line
/// endings, element order, namespaces) is kept, because the edit replaces the elements in the text instead of re-serializing
/// the document. A missing <c>AutoNumber</c> is inserted as the first child of the AttributeList (where TIA Portal's own
/// export writes it), indented like the child it precedes. Siemens-free.
/// </summary>
public static class SimaticMlBlockIdentity
{
    private const string BlockElementPrefix = "SW.Blocks.";
    private const string AttributeListElement = "AttributeList";
    public const string NameElement = "Name";
    public const string NumberElement = "Number";
    public const string AutoNumberElement = "AutoNumber";

    private sealed class Span
    {
        public int Start { get; set; }

        public int End { get; set; }

        public string? Text { get; set; }
    }

    private sealed class Located
    {
        public string BlockElement { get; set; } = string.Empty;

        public int AttributeListStartTagEnd { get; set; } = -1;

        public int FirstChildStart { get; set; } = -1;

        public Dictionary<string, Span> Elements { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>The identity elements of <paramref name="xml"/>.</summary>
    /// <exception cref="FormatException">The text is no SimaticML block document (no single Document/SW.Blocks.*/AttributeList).</exception>
    public static SimaticMlBlockIdentityValues Read(string xml)
    {
        var located = Locate(xml);
        string? Text(string name) => located.Elements.TryGetValue(name, out var span) ? span.Text ?? string.Empty : null;
        return new SimaticMlBlockIdentityValues(located.BlockElement, Text(NameElement), Text(NumberElement), Text(AutoNumberElement));
    }

    /// <summary>
    /// <paramref name="xml"/> with the block's <c>Name</c> set to <paramref name="name"/>, <c>Number</c> to
    /// <paramref name="number"/> and, when <paramref name="autoNumber"/> has a value, <c>AutoNumber</c> to it (inserted as the
    /// first AttributeList child when absent). Nothing else changes.
    /// </summary>
    /// <exception cref="FormatException">No SimaticML block document, or it has no Name or Number element to set.</exception>
    public static string Apply(string xml, string name, int number, bool? autoNumber)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        var located = Locate(xml);
        if (!located.Elements.ContainsKey(NameElement))
            throw new FormatException($"The {located.BlockElement} AttributeList has no {NameElement} element to set.");
        if (!located.Elements.ContainsKey(NumberElement))
            throw new FormatException($"The {located.BlockElement} AttributeList has no {NumberElement} element to set.");

        var edits = new List<(int Start, int End, string Text)>
        {
            Replace(located.Elements[NameElement], NameElement, name),
            Replace(located.Elements[NumberElement], NumberElement, number.ToString(CultureInfo.InvariantCulture)),
        };
        if (autoNumber is { } flag)
        {
            var value = flag ? "true" : "false";
            if (located.Elements.TryGetValue(AutoNumberElement, out var existing))
            {
                edits.Add(Replace(existing, AutoNumberElement, value));
            }
            else
            {
                if (located.FirstChildStart < 0)
                    throw new FormatException($"The {located.BlockElement} AttributeList has no child element before which {AutoNumberElement} could be inserted.");
                var gap = xml.Substring(located.AttributeListStartTagEnd, located.FirstChildStart - located.AttributeListStartTagEnd);
                var separator = gap.Trim().Length == 0 ? gap : string.Empty;
                edits.Add((located.FirstChildStart, located.FirstChildStart, $"<{AutoNumberElement}>{value}</{AutoNumberElement}>{separator}"));
            }
        }

        var result = xml;
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            result = result.Substring(0, edit.Start) + edit.Text + result.Substring(edit.End);
        return result;
    }

    private static (int Start, int End, string Text) Replace(Span span, string element, string value)
        => (span.Start, span.End, $"<{element}>{Escape(value)}</{element}>");

    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static Located Locate(string xml)
    {
        if (xml is null) throw new ArgumentNullException(nameof(xml));
        // A leading BOM is not part of the XML text the reader positions count.
        var offset = xml.Length > 0 && xml[0] == '﻿' ? 1 : 0;
        var text = offset == 0 ? xml : xml.Substring(offset);
        var lineStarts = LineStarts(text);
        var located = new Located();
        var blockDepth = -1;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = false, IgnoreWhitespace = false };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            var info = (IXmlLineInfo)reader;
            int Position() => lineStarts[info.LineNumber - 1] + info.LinePosition - 1;
            var inAttributeList = false;
            var attributeListSeen = false;
            var blocks = 0;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1 && reader.LocalName.StartsWith(BlockElementPrefix, StringComparison.Ordinal))
                {
                    blocks++;
                    if (blocks > 1) throw new FormatException("The document holds more than one SW.Blocks element.");
                    located.BlockElement = reader.LocalName;
                    blockDepth = reader.Depth;
                    continue;
                }

                if (blockDepth < 0) continue;
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == blockDepth + 1 && !attributeListSeen)
                {
                    attributeListSeen = true;
                    if (reader.LocalName != AttributeListElement)
                        throw new FormatException($"The first child of {located.BlockElement} is {reader.LocalName}, not {AttributeListElement}.");
                    if (reader.IsEmptyElement) throw new FormatException($"The {located.BlockElement} AttributeList is empty.");
                    located.AttributeListStartTagEnd = StartTagEnd(text, ElementStart(text, Position(), reader.Name)) + 1;
                    inAttributeList = true;
                    continue;
                }

                if (inAttributeList && reader.NodeType == XmlNodeType.EndElement && reader.Depth == blockDepth + 1)
                {
                    inAttributeList = false;
                    continue;
                }

                if (!inAttributeList || reader.NodeType != XmlNodeType.Element || reader.Depth != blockDepth + 2) continue;
                var start = ElementStart(text, Position(), reader.Name);
                if (located.FirstChildStart < 0) located.FirstChildStart = start;
                var localName = reader.LocalName;
                if (localName is not (NameElement or NumberElement or AutoNumberElement)) continue;
                if (located.Elements.ContainsKey(localName))
                    throw new FormatException($"The {located.BlockElement} AttributeList has more than one {localName} element.");
                var span = new Span { Start = start };
                if (reader.IsEmptyElement)
                {
                    span.End = StartTagEnd(text, start) + 1;
                    span.Text = string.Empty;
                }
                else
                {
                    var depth = reader.Depth;
                    var value = new System.Text.StringBuilder();
                    while (reader.Read() && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
                    {
                        if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                            value.Append(reader.Value);
                        else if (reader.NodeType == XmlNodeType.Element)
                            throw new FormatException($"The {localName} element of {located.BlockElement} has child elements.");
                    }

                    var endName = Position();
                    if (endName < 2 || text[endName - 2] != '<' || text[endName - 1] != '/')
                        throw new FormatException($"The end tag of {localName} could not be located.");
                    span.End = text.IndexOf('>', endName) + 1;
                    if (span.End <= 0) throw new FormatException($"The end tag of {localName} is not closed.");
                    span.Text = value.ToString();
                }

                located.Elements[localName] = span;
            }
        }
        catch (XmlException exception)
        {
            throw new FormatException($"The SimaticML document is not well-formed XML: {exception.Message}", exception);
        }

        if (blockDepth < 0) throw new FormatException("The document has no Document/SW.Blocks.* block element.");
        if (located.AttributeListStartTagEnd < 0) throw new FormatException($"The {located.BlockElement} element has no {AttributeListElement}.");
        if (offset != 0)
        {
            foreach (var span in located.Elements.Values)
            {
                span.Start += offset;
                span.End += offset;
            }

            located.AttributeListStartTagEnd += offset;
            if (located.FirstChildStart >= 0) located.FirstChildStart += offset;
        }

        return located;
    }

    /// <summary>The offset of the '&lt;' of an element whose name the reader positions at <paramref name="namePosition"/>.</summary>
    private static int ElementStart(string text, int namePosition, string qualifiedName)
    {
        if (namePosition < 1 || text[namePosition - 1] != '<'
            || string.CompareOrdinal(text, namePosition, qualifiedName, 0, qualifiedName.Length) != 0)
            throw new FormatException($"The start tag of {qualifiedName} could not be located.");
        return namePosition - 1;
    }

    /// <summary>The offset of the '&gt;' closing the start tag that begins at <paramref name="start"/> (quoted attribute values skipped).</summary>
    private static int StartTagEnd(string text, int start)
    {
        char? quote = null;
        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                if (c == quote) quote = null;
                continue;
            }

            if (c is '"' or '\'') quote = c;
            else if (c == '>') return i;
        }

        throw new FormatException("A start tag is not closed.");
    }

    /// <summary>The offsets at which lines start, splitting at CRLF, CR and LF as XML line counting does.</summary>
    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
            else if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }
}
