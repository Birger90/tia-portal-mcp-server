// Derived from rickgaiser/TiaMcp src/TiaMcp.Formats/S7dclSource.cs at 8e9de1c (AGPL-3.0-only). See THIRD_PARTY_NOTICES.md.
// Adopted from that file: the system type list, the declaration, network and language patterns, the nested-index
// scan and the SimaticML AT-overlay query (its FindWriteProblems, NestedIndexLine, AtOverlays and the line pattern
// of WithAtMarkers) and the content of its finding texts, reworded as reported behaviour. This fork's own: the typed
// findings, the check that an overlay member is still declared, the textual-interface check and the comparison with a
// re-export.
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>The constructs TIA Portal V21's SIMATIC SD (<c>.s7dcl</c>) import is reported to alter without an error.</summary>
public static class S7dclImportHazardKinds
{
    /// <summary>An array access inside an index in a LAD or FBD network (<c>"DB_A".arr["DB_B".structArr[1].idx]</c>).</summary>
    public const string NestedIndex = "nested_index_in_lad_fbd";

    /// <summary>A member typed with a project FB or UDT named like a system type (<c>_.TONR</c>, <c>"TON"</c>).</summary>
    public const string SystemTypeName = "project_type_named_like_system_type";

    /// <summary>A member that overlays another one (<c>AT</c>), which <c>.s7dcl</c> has no notation for.</summary>
    public const string AtOverlay = "at_overlay";
}

/// <summary>
/// One construct of a submitted <c>.s7dcl</c> the import is reported to alter. <see cref="Subject"/> identifies it
/// (the member name, or <c>network N</c>), <see cref="Evidence"/> is the matched text (the declared type, the
/// overlaid member, or the trimmed line with the nested index) and <see cref="Language"/> the network language.
/// </summary>
public sealed record S7dclImportHazard(string Kind, string Subject, string Evidence, string? Language, string Detail);

/// <summary>One SimaticML AT overlay: <see cref="Member"/> overlays <see cref="Target"/>.</summary>
public sealed record S7dclAtOverlay(string Member, string Target);

/// <summary>What a re-export shows of one hazard: <c>preserved</c>, <c>altered</c> or <c>unverifiable</c>.</summary>
public sealed record S7dclConstructCheck(S7dclImportHazard Hazard, string State, string? Actual);

/// <summary>
/// Siemens-free checks on SIMATIC SD documents for the V21 import pitfalls reported by rickgaiser/TiaMcp (not verified
/// on V21 here). Nothing here claims the import does alter a construct; it finds the constructs the report names.
/// </summary>
public static class S7dclImportHazards
{
    public const string Preserved = "preserved";
    public const string Altered = "altered";
    public const string Unverifiable = "unverifiable";

    /// <summary>The system FB types a same-named project FB or UDT is reported to be replaced by.</summary>
    public static IReadOnlyList<string> SystemTypeNames { get; } = Array.AsReadOnly(new[]
    {
        "TON", "TOF", "TP", "TONR", "CTU", "CTD", "CTUD", "R_TRIG", "F_TRIG",
    });

    private static readonly Regex ProjectTypeNamedLikeSystemType = new(
        @"^\s*(?<member>""[^""]*""|[A-Za-z_]\w*)\s*:\s*(?:Array\s*\[[^\]]*\]\s*of\s+)?(?<type>_\.""?(?<name>" + string.Join("|", SystemTypeNames)
        + @")""?|""(?<name>" + string.Join("|", SystemTypeNames) + @")"")(?=\s*[;:{])",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A network: its attribute list (with S7_Language) and its body.
    private static readonly Regex Network = new(
        @"\{(?<attributes>[^{}]*)\}\s*NETWORK\b(?<body>.*?)\bEND_NETWORK\b",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex Language = new(@"S7_Language\s*:=\s*""(?<language>\w+)""", RegexOptions.CultureInvariant);

    // The declaration part ends at the first network or BEGIN.
    private static readonly Regex CodeStart = new(
        @"^\s*(\{[^{}]*\}\s*)?(NETWORK|BEGIN)\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex TextualInterface = new(
        @"S7_EditorMode\s*:=\s*""[^""]*TextualInterface[^""]*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SimpleIdentifier = new(@"^[A-Za-z_]\w*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The constructs of <paramref name="s7dcl"/> the import is reported to alter. <paramref name="overlays"/> are the
    /// AT overlays of the block as it is now (from its SimaticML): <c>.s7dcl</c> cannot show them, so an overlay is a
    /// hazard while its member is still declared in the submitted document.
    /// </summary>
    public static IReadOnlyList<S7dclImportHazard> Find(string s7dcl, IReadOnlyCollection<S7dclAtOverlay>? overlays)
    {
        var hazards = new List<S7dclImportHazard>();
        if (string.IsNullOrEmpty(s7dcl)) return hazards;

        foreach (Match match in ProjectTypeNamedLikeSystemType.Matches(s7dcl))
        {
            var member = match.Groups["member"].Value;
            var type = match.Groups["type"].Value;
            hazards.Add(new S7dclImportHazard(S7dclImportHazardKinds.SystemTypeName, member, type, null,
                $"'{member}' is declared with the project type {type}; the .s7dcl import is reported to take the system type of that "
                + "name instead (TONR becomes TONR_TIME, CTU becomes CTU_INT), and the block still compiles."));
        }

        foreach (var overlay in overlays ?? Array.Empty<S7dclAtOverlay>())
        {
            if (!IsDeclared(s7dcl, overlay.Member)) continue;
            hazards.Add(new S7dclImportHazard(S7dclImportHazardKinds.AtOverlay, overlay.Member, overlay.Target, null,
                $"'{overlay.Member}' overlays '{overlay.Target}' (AT) in the block now; .s7dcl has no AT notation, and the import is "
                + "reported to make it an independent variable, while the block still compiles."));
        }

        foreach (var (number, language, body) in Networks(s7dcl))
        {
            if (language is not ("LAD" or "FBD")) continue;
            var line = NestedIndexLine(body);
            if (line is null) continue;
            hazards.Add(new S7dclImportHazard(S7dclImportHazardKinds.NestedIndex, $"network {number}", line, language,
                $"network {number} ({language}) has an array access inside an index ('{line}'); the .s7dcl import is reported to turn "
                + "the inner access into one quoted name (\"DB_B\".\"arr[1]\".x), so the block no longer compiles."));
        }

        return hazards;
    }

    /// <summary>
    /// The AT overlays of a SimaticML document (<c>&lt;Member Name="m"&gt;&lt;AttributeList&gt;&lt;StringAttribute Name="At"&gt;target</c>),
    /// or null when the document cannot be parsed.
    /// </summary>
    public static IReadOnlyList<S7dclAtOverlay>? AtOverlays(string simaticMl)
    {
        if (string.IsNullOrWhiteSpace(simaticMl)) return null;
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(simaticMl),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        return document.Descendants().Where(e => e.Name.LocalName == "Member")
            .Select(m => (Member: (string?)m.Attribute("Name"), Target: m.Elements().Where(e => e.Name.LocalName == "AttributeList").Elements()
                .FirstOrDefault(a => a.Name.LocalName == "StringAttribute" && (string?)a.Attribute("Name") == "At")?.Value))
            .Where(o => o.Member is not null && o.Target is not null)
            .Select(o => new S7dclAtOverlay(o.Member!, o.Target!))
            .ToList();
    }

    /// <summary>True when the document declares a textual interface (<c>S7_EditorMode := "[SCL,TextualInterface]"</c>).</summary>
    public static bool HasTextualInterface(string s7dcl) => !string.IsNullOrEmpty(s7dcl) && TextualInterface.IsMatch(s7dcl);

    /// <summary>
    /// Checks each requested hazard against what TIA Portal exported after the import: a project type must still be
    /// declared under the same spelling, an overlay must still carry its AT attribute in the SimaticML, and a network
    /// with a nested index must still have one. A missing re-export document makes the check <c>unverifiable</c>.
    /// </summary>
    public static IReadOnlyList<S7dclConstructCheck> Compare(
        IReadOnlyList<S7dclImportHazard> requested, string? reExportedS7dcl, string? reExportedSimaticMl)
    {
        var checks = new List<S7dclConstructCheck>();
        var reTypes = reExportedS7dcl is null ? null : Find(reExportedS7dcl, null)
            .Where(h => h.Kind == S7dclImportHazardKinds.SystemTypeName).ToList();
        var reOverlays = reExportedSimaticMl is null ? null : AtOverlays(reExportedSimaticMl);
        var reNetworks = reExportedS7dcl is null ? null : Networks(reExportedS7dcl).ToList();
        foreach (var hazard in requested)
        {
            switch (hazard.Kind)
            {
                case S7dclImportHazardKinds.SystemTypeName:
                    if (reTypes is null)
                    {
                        checks.Add(new(hazard, Unverifiable, null));
                        break;
                    }

                    var same = reTypes.Any(t => SameName(t.Subject, hazard.Subject) && SameType(t.Evidence, hazard.Evidence));
                    checks.Add(new(hazard, same ? Preserved : Altered, same ? hazard.Evidence : DeclaredType(reExportedS7dcl!, hazard.Subject)));
                    break;
                case S7dclImportHazardKinds.AtOverlay:
                    if (reOverlays is null)
                    {
                        checks.Add(new(hazard, Unverifiable, null));
                        break;
                    }

                    var kept = reOverlays.FirstOrDefault(o => SameName(o.Member, hazard.Subject));
                    checks.Add(kept is not null && string.Equals(kept.Target, hazard.Evidence, StringComparison.OrdinalIgnoreCase)
                        ? new(hazard, Preserved, $"AT {kept.Target}")
                        : new(hazard, Altered, kept is null ? "no AT overlay" : $"AT {kept.Target}"));
                    break;
                case S7dclImportHazardKinds.NestedIndex:
                    if (reNetworks is null)
                    {
                        checks.Add(new(hazard, Unverifiable, null));
                        break;
                    }

                    var network = reNetworks.FirstOrDefault(n => $"network {n.Number}" == hazard.Subject);
                    var nested = network.Body is null ? null : NestedIndexLine(network.Body);
                    checks.Add(nested is not null && string.Equals(network.Language, hazard.Language, StringComparison.Ordinal)
                        ? new(hazard, Preserved, nested)
                        : new(hazard, Altered, network.Body is null ? "network missing" : nested ?? "no nested index"));
                    break;
                default:
                    checks.Add(new(hazard, Unverifiable, null));
                    break;
            }
        }

        return checks;
    }

    private static IEnumerable<(int Number, string Language, string Body)> Networks(string s7dcl)
    {
        var number = 0;
        foreach (Match match in Network.Matches(s7dcl))
        {
            number++;
            yield return (number, Language.Match(match.Groups["attributes"].Value).Groups["language"].Value, match.Groups["body"].Value);
        }
    }

    // The member is declared in the declaration part, before the first network or BEGIN.
    private static bool IsDeclared(string s7dcl, string member)
    {
        var codeStart = CodeStart.Match(s7dcl);
        var end = codeStart.Success ? codeStart.Index : s7dcl.Length;
        var name = SimpleIdentifier.IsMatch(member)
            ? $"(?:{Regex.Escape(member)}|\"{Regex.Escape(member)}\")"
            : $"\"{Regex.Escape(member)}\"";
        return new Regex(@"^[ \t]*" + name + @"[ \t]*:", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Match(s7dcl, 0, end).Success;
    }

    private static string? DeclaredType(string s7dcl, string member)
    {
        var name = Regex.Escape(Unquote(member));
        var match = new Regex(@"^\s*(?:" + name + @"|""" + name + @""")\s*:\s*(?<type>[^;{\r\n]+)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Match(s7dcl);
        return match.Success ? match.Groups["type"].Value.Trim() : "not declared";
    }

    private static bool SameName(string left, string right)
        => string.Equals(Unquote(left), Unquote(right), StringComparison.OrdinalIgnoreCase);

    private static bool SameType(string left, string right)
        => string.Equals(left.Replace(" ", string.Empty), right.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);

    private static string Unquote(string name) => name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"' ? name.Substring(1, name.Length - 2) : name;

    // The trimmed line of the first '[' inside another '[' (quoted names and strings skipped), or null.
    private static string? NestedIndexLine(string body)
    {
        var depth = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c is '"' or '\'')
            {
                var close = body.IndexOf(c, i + 1);
                if (close < 0) break;
                i = close;
            }
            else if (c == '[')
            {
                if (++depth == 2)
                {
                    var start = body.LastIndexOf('\n', i) + 1;
                    var end = body.IndexOf('\n', i);
                    return body.Substring(start, (end < 0 ? body.Length : end) - start).Trim();
                }
            }
            else if (c == ']' && depth > 0)
            {
                depth--;
            }
        }

        return null;
    }
}
