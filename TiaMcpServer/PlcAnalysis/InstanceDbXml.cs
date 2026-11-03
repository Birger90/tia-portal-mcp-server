using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TiaMcpServer.PlcAnalysis;

/// <summary>
/// Reads the <c>InstanceOfName</c> of an instance DB from its <c>get_block_content</c> SimaticML export
/// (a single document, or the <c>.xml</c> document of a "--- FILE: name ---" bundle). Never guesses: an
/// export without the element is reported, not inferred.
/// </summary>
internal static partial class InstanceDbXml
{
    public const string NotAnInstanceDbDocument = "notAnInstanceDbDocument";
    public const string NoInstanceOfName = "noInstanceOfName";

    public static IReadOnlyList<string> Evidence { get; } = new[]
    {
        "instanceOfName is the text of AttributeList/InstanceOfName under the SW.Blocks.InstanceDB element of the DB's SimaticML export (the plc_read get_block_content xml path, unchanged). InstanceOfName is the documented V21 InstanceDB attribute 'the block name of the father instance (FB/SFB/UDT/SDT)', and SimaticML attribute lists carry Openness attributes as elements of the same name (the repository's Main.xml export shows Name, Number, ProgrammingLanguage); that every instance DB export carries InstanceOfName is not verified by a fixture or live run, and a DB whose export lacks it is reported notChecked (noInstanceOfName).",
        "An instanceOfName that is neither a block nor a PLC data type of this PLC is reported as instanceOfNotInInventory, not as missing: system FBs/SFBs and system data types such as IEC timer types are not part of the inventory.",
    };

    public static (string? InstanceOfName, string? Category, string? Problem) Read(string? content)
    {
        var xml = XmlDocumentOf(content ?? string.Empty);
        if (xml is null) return (null, NotAnInstanceDbDocument, "The export contains no SimaticML document: " + TiaMcpServer.Contracts.Plc.Interface.SimaticMlExportNotes.Cause + ".");
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return (null, NotAnInstanceDbDocument, "The export is not well-formed XML.");
        }

        var block = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "SW.Blocks.InstanceDB");
        if (block is null) return (null, NotAnInstanceDbDocument, "The export's block element is not SW.Blocks.InstanceDB.");
        var name = Child(Child(block, "AttributeList"), "InstanceOfName")?.Value.Trim();
        return string.IsNullOrEmpty(name)
            ? (null, NoInstanceOfName, "The export's AttributeList has no InstanceOfName element.")
            : (name, null, null);
    }

    private static XElement? Child(XElement? parent, string localName)
        => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName);

    /// <summary>The whole content, or the first .xml document of a bundle; null when a bundle has none.</summary>
    private static string? XmlDocumentOf(string content)
    {
        var markers = BundleMarker().Matches(content);
        if (markers.Count == 0) return content;
        for (var index = 0; index < markers.Count; index++)
        {
            if (!markers[index].Groups["name"].Value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            var start = markers[index].Index + markers[index].Length;
            var end = index + 1 < markers.Count ? markers[index + 1].Index : content.Length;
            return content[start..end];
        }

        return null;
    }

    [GeneratedRegex(@"^--- FILE: (?<name>.+) ---(?:\r?\n|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BundleMarker();
}
