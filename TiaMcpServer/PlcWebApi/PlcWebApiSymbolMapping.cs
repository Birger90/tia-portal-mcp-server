using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.PlcWebApi;

/// <summary>
/// Maps offline table entries to Web API symbolic names, only by rules with recorded evidence, never by
/// guessing the Openness notation:
/// <list type="bullet">
/// <item><c>tagTable</c> and <c>plcTag</c>: a PLC tag (by its tag-table name) is <c>"Name"</c> — a root-level
/// variable in the client's <c>ApiPlcProgramData.GetNameWithQuotes</c> form and the reference's <c>"Motor_On"</c>
/// example. A watch-table name maps this way only when it equals (case-insensitive) a tag of the same PLC's tag
/// inventory, bare or in one pair of double quotes; the inventory spelling is used.</item>
/// <item><c>webApiNotation</c>: a watch-table name that already is a documented Web API name is passed unchanged:
/// double-quoted segments joined by dots, each optionally followed by an integer index list <c>[i]</c>/<c>[i,j]</c>
/// (the client's <c>GetVarNameForMethods</c> and array element naming, README <c>"DataTypes"."Bool"</c>), or
/// a quoted first segment followed by exactly one plain identifier member (the reference's <c>"DB_Line".Speed</c>;
/// deeper unquoted paths are not documented and stay unmapped).</item>
/// </list>
/// Everything else is unmapped with a reason: absolute addresses (no documented Web API notation), local or
/// mixed notations, names with an embedded quote (no documented escaping) and empty names.
/// </summary>
public static partial class PlcWebApiSymbolMapping
{
    public const string TagTableRule = "tagTable";
    public const string PlcTagRule = "plcTag";
    public const string WebApiNotationRule = "webApiNotation";

    public static IReadOnlyList<string> Rules { get; } = new[]
    {
        "tagTable/plcTag: a PLC tag is read as \"Name\" (the client's GetNameWithQuotes form for a root-level variable; the reference's \"Motor_On\" example); a watch-table entry maps this way only when its name is a tag of the same PLC, bare or quoted.",
        "webApiNotation: a watch-table name already written as double-quoted segments joined by dots (each optionally with an integer index list such as [1] or [1,2]), or as a quoted first segment followed by exactly one plain identifier (\"DB_Line\".Speed, the only documented unquoted form), is sent unchanged.",
        "Anything else is unmapped and not read: absolute addresses, names with # or an embedded quote, bare multi-segment names, more than one unquoted member (\"DB\".Struct.Member: write the members quoted), an index after an unquoted member, and entries without a name.",
    };

    public record struct Mapping(string? WebApiName, string? Rule, string? UnmappedReason);

    public static Mapping MapTag(string? tagName)
        => Quotable(tagName)
            ? new Mapping($"\"{tagName}\"", TagTableRule, null)
            : new Mapping(null, null, string.IsNullOrWhiteSpace(tagName)
                ? "The tag has no name."
                : "The tag name contains a double quote or a control character; no escaping rule is documented for Web API names.");

    /// <param name="tagNames">The PLC's tag names (case-insensitive lookup returning the inventory spelling).</param>
    public static Mapping MapWatchEntry(string? name, IReadOnlyDictionary<string, string> tagNames)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new Mapping(null, null, "The entry has no symbolic name (an address-only or comment entry).");
        var trimmed = name.Trim();
        var bare = trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' && trimmed.IndexOf('"', 1) == trimmed.Length - 1
            ? trimmed[1..^1]
            : trimmed;
        if (tagNames.TryGetValue(bare, out var tag) && Quotable(tag))
            return new Mapping($"\"{tag}\"", PlcTagRule, null);
        if (trimmed.Length <= PlcWebApiCatalog.MaxSymbolChars && WebApiNotation().IsMatch(trimmed))
            return new Mapping(trimmed, WebApiNotationRule, null);
        return new Mapping(null, null, trimmed.StartsWith('%')
            ? "An absolute address has no documented Web API notation; give the entry a symbol."
            : "The name is neither a tag of this PLC nor in a documented Web API notation.");
    }

    /// <summary>The PLC's tags for <see cref="MapWatchEntry"/>.</summary>
    public static IReadOnlyDictionary<string, string> TagNames(PlcTagInventoryPlcInfo plc)
    {
        var names = new Dictionary<string, string>(PlcNameRules.Comparer);
        foreach (var tag in plc.Tables.SelectMany(table => table.Tags))
            names.TryAdd(tag.Name, tag.Name);
        return names;
    }

    private static bool Quotable(string? name)
        => !string.IsNullOrWhiteSpace(name) && name.Length + 2 <= PlcWebApiCatalog.MaxSymbolChars && !name.Any(c => c == '"' || char.IsControl(c));

    [GeneratedRegex(@"^(?:""[^""\p{Cc}]+""(?:\[-?\d+(?:,-?\d+)*\])?(?:\.""[^""\p{Cc}]+""(?:\[-?\d+(?:,-?\d+)*\])?)*|""[^""\p{Cc}]+""\.[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex WebApiNotation();
}
