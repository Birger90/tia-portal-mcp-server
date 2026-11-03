// Origin: BetterTIA/writer, src/TiaMcpServer/Siemens/SimaticMl.cs (region "network titles and comments":
// Networks, Texts, SetText, SetNetworkText, ReplaceAll, ReplaceNetworkText), commit
// fc8179c9b59d364961f2255d07d05af893c32837, MIT License, Copyright (c) 2025 J.Heilingbrunner; see THIRD_PARTY_NOTICES.md.
// Adapted: typed change records with a re-locatable key, creation of a missing language item, exceptions of this fork.
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>One find/replace pair (literal, case-sensitive).</summary>
public class PlcTextReplacementInfo
{
    public string Find { get; set; } = string.Empty;

    public string Replace { get; set; } = string.Empty;
}

/// <summary>
/// One changed text: where it is, its language and the old and new text. <see cref="Where"/> names the place for a
/// person ("network 2 Title", "block Comment", "network 1 step 3 Title"); <see cref="Key"/> re-locates it in a fresh
/// export of the same object.
/// </summary>
public class PlcTextChangeInfo
{
    public string Object { get; set; } = string.Empty;

    public string Where { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string Culture { get; set; } = string.Empty;

    public string Old { get; set; } = string.Empty;

    public string New { get; set; } = string.Empty;
}

/// <summary>
/// Network titles and comments of a block's SimaticML document, Siemens-free. Openness has no setter for them, so they
/// change only through export, edit and import (as BetterTIA/writer does). A network is a
/// <c>SW.Blocks.CompileUnit</c> in the block's <c>ObjectList</c>, numbered from 1 in document order as TIA Portal numbers
/// the networks; its title and comment are <c>MultilingualText</c> objects (<c>CompositionName</c> <c>Title</c> /
/// <c>Comment</c>) with one <c>MultilingualTextItem</c> (<c>Culture</c>, <c>Text</c>) per language. GRAPH texts are the
/// <c>MultiLanguageText</c> elements (attribute <c>Lang</c>) inside a <c>Graph</c>.
/// </summary>
public static class SimaticMlNetworkTexts
{
    public const string ScopeNetworks = "networks";
    public const string ScopeAll = "all";

    public static readonly IReadOnlyList<string> Scopes = Array.AsReadOnly(new[] { ScopeNetworks, ScopeAll });

    private static readonly string[] Compositions = { "Title", "Comment" };

    /// <exception cref="PlcInterfaceEditException">The document is not a block document with an ObjectList.</exception>
    public static XElement Body(XDocument document)
    {
        var body = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName.StartsWith("SW.Blocks.", StringComparison.Ordinal)
            && e.Name.LocalName != "SW.Blocks.CompileUnit");
        return body ?? throw new PlcInterfaceEditException(WorkerFailureCategories.TargetKindUnsupported,
            "The document is not a SimaticML block document (no SW.Blocks.* element below the root).");
    }

    public static string ObjectName(XDocument document)
        => Kid(Kid(Body(document), "AttributeList"), "Name")?.Value ?? string.Empty;

    public static IReadOnlyList<XElement> Networks(XDocument document)
        => Kids(Kid(Body(document), "ObjectList"), "SW.Blocks.CompileUnit").ToList();

    /// <summary>
    /// Sets the title and/or comment of one network (1-based). A null text keeps the current one. Without
    /// <paramref name="culture"/> the text must exist in exactly one language; with it, a missing language item is
    /// created, as TIA Portal lets a user type a text in any project language.
    /// </summary>
    /// <exception cref="PlcInterfaceEditException">The network does not exist, or the language is ambiguous.</exception>
    public static List<PlcTextChangeInfo> SetNetworkText(XDocument document, int network, string? title, string? comment, string? culture)
    {
        var networks = Networks(document);
        if (network < 1 || network > networks.Count)
            throw new PlcInterfaceEditException(WorkerFailureCategories.TargetNotFound,
                $"Network {network} does not exist; '{ObjectName(document)}' has {networks.Count} network(s).");
        var changes = new List<PlcTextChangeInfo>();
        var values = new[] { title, comment };
        for (var i = 0; i < Compositions.Length; i++)
        {
            if (values[i] is null) continue;
            var key = NetworkKey(network, Compositions[i]);
            var all = Texts(networks[network - 1], Compositions[i]).ToList();
            var chosen = all.Where(t => string.IsNullOrEmpty(culture) || Same(t.Culture, culture)).ToList();
            if (chosen.Count == 0 && !string.IsNullOrEmpty(culture))
            {
                var created = CreateItem(document, networks[network - 1], Compositions[i], culture!);
                chosen.Add((culture!, created));
            }

            if (chosen.Count != 1)
                throw new PlcInterfaceEditException(WorkerFailureCategories.ValidationError,
                    $"The {Compositions[i]} of network {network} exists in {all.Count} language(s) ({string.Join(", ", all.Select(t => t.Culture))}); pass one of them as culture.");
            SetText(changes, document, $"network {network} {Compositions[i]}", key, chosen[0].Culture, chosen[0].Text, values[i]!);
        }

        return changes;
    }

    /// <summary>
    /// Replaces every <c>find</c> by its <c>replace</c> in one pass (literal, case-sensitive, longest first), so a swap
    /// of two texts works, in the titles and comments of every network and, with scope <see cref="ScopeAll"/>, also in the
    /// block's own title and comment and the GRAPH texts (sequence, step and transition texts). Step and transition
    /// names are not texts (a step name is an interface member) and stay. An empty <paramref name="culture"/> covers
    /// every language.
    /// </summary>
    public static List<PlcTextChangeInfo> ReplaceTexts(XDocument document, IReadOnlyList<PlcTextReplacementInfo> replacements, string? culture, string scope)
    {
        var changes = new List<PlcTextChangeInfo>();
        foreach (var located in Locate(document, scope))
        {
            if (!string.IsNullOrEmpty(culture) && !Same(located.Culture, culture)) continue;
            SetText(changes, document, located.Where, located.Key, located.Culture, located.Text, ReplaceAll(located.Text.Value, replacements));
        }

        return changes;
    }

    /// <summary>The current text of every changed place of <paramref name="changes"/> in <paramref name="document"/> (null when gone).</summary>
    public static string? Current(XDocument document, PlcTextChangeInfo change)
        => Locate(document, ScopeAll).FirstOrDefault(l => l.Key == change.Key && Same(l.Culture, change.Culture)).Text?.Value;

    /// <summary>
    /// Replaces every key of <paramref name="replacements"/> by its value in one pass (literal, case-sensitive), so
    /// "1" to "2" together with "2" to "1" swaps instead of collapsing both into one; longest key first.
    /// </summary>
    /// <exception cref="ArgumentException">No pair, an empty find, or the same find twice.</exception>
    public static string ReplaceAll(string text, IReadOnlyList<PlcTextReplacementInfo> replacements)
    {
        var map = Map(replacements);
        var pattern = string.Join("|", map.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        return Regex.Replace(text, pattern, match => map[match.Value]);
    }

    /// <exception cref="ArgumentException">No pair, an empty find, or the same find twice.</exception>
    public static Dictionary<string, string> Map(IReadOnlyList<PlcTextReplacementInfo> replacements)
    {
        if (replacements.Count == 0) throw new ArgumentException("Give at least one { find, replace } pair.");
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in replacements)
        {
            if (string.IsNullOrEmpty(pair.Find)) throw new ArgumentException("A replacement needs a non-empty find.");
            if (map.ContainsKey(pair.Find)) throw new ArgumentException($"The find '{pair.Find}' appears twice.");
            map[pair.Find] = pair.Replace ?? string.Empty;
        }

        return map;
    }

    private readonly struct Located
    {
        public Located(string where, string key, string culture, XElement text)
        {
            Where = where;
            Key = key;
            Culture = culture;
            Text = text;
        }

        public string Where { get; }

        public string Key { get; }

        public string Culture { get; }

        public XElement Text { get; }
    }

    private static IEnumerable<Located> Locate(XDocument document, string scope)
    {
        var all = string.Equals(scope, ScopeAll, StringComparison.Ordinal);
        if (all)
        {
            foreach (var composition in Compositions)
                foreach (var (culture, text) in Texts(Body(document), composition))
                    yield return new Located($"block {composition}", $"block/{composition}", culture, text);
        }

        var networks = Networks(document);
        for (var n = 0; n < networks.Count; n++)
        {
            foreach (var composition in Compositions)
                foreach (var (culture, text) in Texts(networks[n], composition))
                    yield return new Located($"network {n + 1} {composition}", NetworkKey(n + 1, composition), culture, text);
            if (!all) continue;
            var index = 0;
            foreach (var text in networks[n].Descendants().Where(e => e.Name.LocalName == "MultiLanguageText" && e.Ancestors().Any(a => a.Name.LocalName == "Graph")))
            {
                var node = text.Ancestors().FirstOrDefault(a => a.Name.LocalName is "Step" or "Transition");
                var owner = node is null ? "sequence" : $"{node.Name.LocalName.ToLowerInvariant()} {(string?)node.Attribute("Number")}";
                yield return new Located($"network {n + 1} {owner} {text.Parent?.Name.LocalName}", $"network/{n + 1}/graph/{index++}",
                    (string?)text.Attribute("Lang") ?? string.Empty, text);
            }
        }
    }

    private static string NetworkKey(int network, string composition) => $"network/{network}/{composition}";

    private static IEnumerable<(string Culture, XElement Text)> Texts(XElement owner, string composition)
    {
        foreach (var text in Kids(Kid(owner, "ObjectList"), "MultilingualText").Where(t => (string?)t.Attribute("CompositionName") == composition))
        {
            foreach (var item in Kids(Kid(text, "ObjectList"), "MultilingualTextItem"))
            {
                var attributes = Kid(item, "AttributeList");
                var value = Kid(attributes, "Text");
                if (value is not null) yield return (Kid(attributes, "Culture")?.Value ?? string.Empty, value);
            }
        }
    }

    /// <summary>Adds a MultilingualTextItem (and the MultilingualText when missing) with a fresh hexadecimal ID.</summary>
    private static XElement CreateItem(XDocument document, XElement network, string composition, string culture)
    {
        var ns = network.Name.Namespace;
        var objects = Kid(network, "ObjectList") ?? Add(network, new XElement(ns + "ObjectList"));
        var text = Kids(objects, "MultilingualText").FirstOrDefault(t => (string?)t.Attribute("CompositionName") == composition)
            ?? Add(objects, new XElement(ns + "MultilingualText", new XAttribute("ID", NextId(document)), new XAttribute("CompositionName", composition)));
        var items = Kid(text, "ObjectList") ?? Add(text, new XElement(ns + "ObjectList"));
        var value = new XElement(ns + "Text", string.Empty);
        items.Add(new XElement(ns + "MultilingualTextItem", new XAttribute("ID", NextId(document)), new XAttribute("CompositionName", "Items"),
            new XElement(ns + "AttributeList", new XElement(ns + "Culture", culture), value)));
        return value;
    }

    private static XElement Add(XElement parent, XElement child)
    {
        parent.Add(child);
        return child;
    }

    /// <summary>One more than the largest hexadecimal ID of the document (SimaticML IDs are hexadecimal, unique per document).</summary>
    private static string NextId(XDocument document)
    {
        var max = 0L;
        foreach (var id in document.Descendants().Select(e => (string?)e.Attribute("ID")).Where(v => v is not null))
            if (long.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) && value > max) max = value;
        return (max + 1).ToString("X", CultureInfo.InvariantCulture);
    }

    private static void SetText(List<PlcTextChangeInfo> changes, XDocument document, string where, string key, string culture, XElement text, string value)
    {
        if (text.Value == value) return;
        changes.Add(new PlcTextChangeInfo { Object = ObjectName(document), Where = where, Key = key, Culture = culture, Old = text.Value, New = value });
        text.Value = value;
    }

    private static IEnumerable<XElement> Kids(XElement? parent, string name)
        => parent?.Elements().Where(e => e.Name.LocalName == name) ?? Enumerable.Empty<XElement>();

    private static XElement? Kid(XElement? parent, string name) => Kids(parent, name).FirstOrDefault();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
