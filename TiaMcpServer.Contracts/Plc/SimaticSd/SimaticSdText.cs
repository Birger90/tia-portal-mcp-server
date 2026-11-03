using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.SimaticSd;

/// <summary>
/// Caller code for one network, normalized as §6 of the Siemens V21 skill <c>simatic-sd</c> prescribes: only the outer
/// wrappers (a pragma before NETWORK, the NETWORK and END_NETWORK lines) and the S7_Language pragma are stripped, inner
/// pragmas such as <c>{ S7_Templates := "..." }</c> stay, blank lines are dropped (an extra blank line inside a RUNG is
/// reported to make the import fail with "File Content has some discrepancies"). The relative indentation is kept.
/// </summary>
public sealed record SimaticSdNetworkCode(
    IReadOnlyList<string> BodyLines,
    string? DeclaredLanguage,
    IReadOnlyList<SimaticSdPragmaEntry> ExtraPragmaEntries)
{
    /// <summary>The body as the verification compares it (<see cref="SimaticSdText.NormalizeForComparison"/>).</summary>
    public string Normalized => SimaticSdText.NormalizeForComparison(BodyLines);
}

/// <summary>Siemens-free text helpers of the SIMATIC SD documents.</summary>
public static class SimaticSdText
{
    public const string BundleExtension = ".s7dcl";
    public const string ResourceExtension = ".s7res";

    /// <summary>The pragma keys a network edit never writes: titles and comments are .s7res ids (set them with set_network_text).</summary>
    public static readonly IReadOnlyList<string> TextKeys = new[] { "S7_NetworkTitle", "S7_NetworkComment" };

    private static readonly Regex PragmaLanguage = new(@"S7_Language\s*:=\s*""(\w+)""", RegexOptions.CultureInvariant);
    private static readonly Regex RungLad = new(@"^\s*RUNG\s+wire#", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex RungFbd = new(@"^\s*RUNG\s+(\d+|"")", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex AnyRung = new(@"^\s*RUNG\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex LanguageName = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// §5 of the Siemens skill, exactly: an explicit <c>S7_Language</c> pragma wins; <c>RUNG wire#</c> lines only → LAD;
    /// <c>RUNG &lt;number&gt;</c> or <c>RUNG "..."</c> lines only → FBD; no RUNG at all but content → SCL; otherwise null
    /// (ambiguous: the caller must give the language).
    /// </summary>
    public static string? DetectLanguage(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var match = PragmaLanguage.Match(line);
            if (match.Success) return match.Groups[1].Value;
        }

        var hasLad = lines.Any(l => RungLad.IsMatch(l));
        var hasFbd = lines.Any(l => RungFbd.IsMatch(l));
        if (hasLad && !hasFbd) return "LAD";
        if (hasFbd && !hasLad) return "FBD";
        var hasRung = lines.Any(l => AnyRung.IsMatch(l));
        if (!hasRung && lines.Any(l => !string.IsNullOrWhiteSpace(l))) return "SCL";
        return null;
    }

    /// <summary>A pragma-safe language name: letters, digits and underscore, not starting with a digit.</summary>
    public static bool IsLanguageName(string? value) => value is not null && LanguageName.IsMatch(value);

    /// <summary>The comparison form of a network body: every line trimmed, blank lines dropped, joined by \n.</summary>
    public static string NormalizeForComparison(IEnumerable<string> lines)
        => string.Join("\n", lines.Select(l => l.Trim()).Where(l => l.Length > 0));

    public static string NormalizeForComparison(string text)
        => NormalizeForComparison(text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));

    /// <summary>The lines without their common leading whitespace (blank lines do not count).</summary>
    public static IReadOnlyList<string> Dedent(IReadOnlyList<string> lines)
    {
        var indents = lines.Where(l => l.Trim().Length > 0).Select(l => SimaticSdPragma.LeadingWhitespace(l).Length).ToList();
        var common = indents.Count == 0 ? 0 : indents.Min();
        return lines.Select(l => l.Trim().Length == 0 ? string.Empty : l.Substring(Math.Min(common, SimaticSdPragma.LeadingWhitespace(l).Length))).ToList();
    }

    /// <summary>
    /// Normalizes caller code for insert_network / replace_network (§6 of the Siemens skill). Accepted forms: a RUNG
    /// body, a full <c>NETWORK ... END_NETWORK</c> with or without a preceding pragma, or SCL statements.
    /// </summary>
    /// <exception cref="SimaticSdException">
    /// validation_error: more than one network, unbalanced NETWORK/END_NETWORK or RUNG/END_RUNG, code outside the
    /// network, an S7_Language pragma inside the body, a title or comment key, or no code at all.
    /// </exception>
    public static SimaticSdNetworkCode NormalizeNetworkCode(string code)
    {
        if (code is null) throw new ArgumentNullException(nameof(code));
        var text = code.Length > 0 && code[0] == '﻿' ? code.Substring(1) : code;
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        var tokens = Keywords(text);
        int Count(string keyword) => tokens.Count(t => string.Equals(t.Word, keyword, StringComparison.OrdinalIgnoreCase));
        var networks = Count("NETWORK");
        var ends = Count("END_NETWORK");
        if (networks > 1 || ends > 1)
            throw Invalid($"code holds {Math.Max(networks, ends)} NETWORK blocks; exactly one network per code is allowed (the Siemens skill rejects several). Insert each network with its own item.");
        if (networks != ends)
            throw Invalid(networks == 1 ? "code has NETWORK without END_NETWORK." : "code has END_NETWORK without NETWORK.");
        RequireBalancedRungs(tokens);

        List<string> body;
        SimaticSdPragma? outer = null;
        if (networks == 1)
        {
            var start = lines.FindIndex(l => Regex.IsMatch(l, @"^\s*NETWORK\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
            var end = lines.FindIndex(l => Regex.IsMatch(l, @"^\s*END_NETWORK\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase));
            if (start < 0 || end < 0)
                throw Invalid("NETWORK and END_NETWORK must each stand on a line of their own.");
            if (end < start) throw Invalid("END_NETWORK comes before NETWORK.");
            if (lines.Skip(end + 1).Any(l => l.Trim().Length > 0)) throw Invalid("code continues after END_NETWORK; give exactly one network.");
            var before = lines.Take(start).Where(l => l.Trim().Length > 0).ToList();
            if (before.Count > 0)
            {
                if (!SimaticSdPragma.TryParse(before, out var pragma))
                    throw Invalid("only a pragma { key := \"value\"; ... } may stand before NETWORK.");
                outer = pragma;
            }

            body = lines.Skip(start + 1).Take(end - start - 1).ToList();
        }
        else
        {
            body = lines;
            // A leading pragma that carries S7_Language (single- or multi-line) is the network's outer pragma.
            var first = body.FindIndex(l => l.Trim().Length > 0);
            if (first >= 0 && body[first].TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                var close = body.FindIndex(first, l => l.TrimEnd().EndsWith("}", StringComparison.Ordinal));
                if (close >= 0 && SimaticSdPragma.TryParse(body, first, close, out var pragma) && pragma.Has("S7_Language"))
                {
                    outer = pragma;
                    body = body.Skip(close + 1).ToList();
                }
            }
        }

        var kept = new List<string>();
        foreach (var line in body)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (PragmaLanguage.IsMatch(trimmed))
            {
                // §6: an S7_Language pragma line is stripped; anything else carrying S7_Language would be corrupted by that.
                if (SimaticSdPragma.TryParse(new[] { trimmed }, out var single) && single.Entries.Count == 1) continue;
                throw Invalid("S7_Language belongs to the network's own pragma before NETWORK, not inside the network body.");
            }

            kept.Add(line);
        }

        if (kept.Count == 0) throw Invalid("code has no network content.");
        var extra = outer?.Entries.Where(e => !string.Equals(e.Key, "S7_Language", StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<SimaticSdPragmaEntry>();
        if (extra.FirstOrDefault(e => TextKeys.Contains(e.Key, StringComparer.OrdinalIgnoreCase)) is { } textKey)
            throw Invalid($"the pragma sets {textKey.Key}; network titles and comments are .s7res text ids, which a network edit never writes (inline literal titles are reported to fail on import). Set them afterwards with set_network_text.");
        return new SimaticSdNetworkCode(Dedent(kept), outer?.Get("S7_Language"), extra);
    }

    /// <summary>True when the header pragma of an .s7dcl text (or of the .s7dcl document of a bundle) declares S7_Safety := "TRUE".</summary>
    public static bool DeclaresSafety(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var s7dcl = Interface.PlcBlockBundleText.TryGetDocument(text!, BundleExtension, out _, out var document) ? document : text!;
        try
        {
            return SimaticSdDocument.Parse(s7dcl).IsFailSafe;
        }
        catch (SimaticSdException)
        {
            // A malformed body does not hide the header: read it on its own.
            var head = s7dcl.TrimStart('﻿');
            var close = head.IndexOf('}');
            return head.TrimStart().StartsWith("{", StringComparison.Ordinal) && close > 0
                && SimaticSdPragma.TryParse(head.Substring(0, close + 1).Split('\n').Select(l => l.TrimEnd('\r')).ToList(), out var pragma)
                && string.Equals(pragma.Get("S7_Safety"), "TRUE", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void RequireBalancedRungs(IReadOnlyList<(string Word, int Line)> tokens)
    {
        var open = false;
        foreach (var (word, line) in tokens)
        {
            if (string.Equals(word, "RUNG", StringComparison.OrdinalIgnoreCase))
            {
                if (open) throw Invalid($"RUNG on line {line} opens before the previous RUNG has its END_RUNG.");
                open = true;
            }
            else if (string.Equals(word, "END_RUNG", StringComparison.OrdinalIgnoreCase))
            {
                if (!open) throw Invalid($"END_RUNG on line {line} has no RUNG before it.");
                open = false;
            }
        }

        if (open) throw Invalid("a RUNG has no END_RUNG.");
    }

    /// <summary>
    /// The structure keywords (NETWORK, END_NETWORK, RUNG, END_RUNG) of the text with their 1-based lines, outside
    /// strings ('...'), quoted names ("..."), comments (// and (* *)) and not part of an operand (#RUNG, a.RUNG).
    /// </summary>
    internal static IReadOnlyList<(string Word, int Line)> Keywords(string text)
    {
        var result = new List<(string, int)>();
        var line = 1;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '\n') { line++; i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '(' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == ')'))
                {
                    if (text[i] == '\n') line++;
                    i++;
                }

                i += 2;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                i++;
                while (i < text.Length && text[i] != c && text[i] != '\n') i++;
                i++;
                continue;
            }

            if (IsWordChar(c))
            {
                var start = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                var word = text.Substring(start, i - start);
                var previous = start > 0 ? text[start - 1] : ' ';
                if (previous != '#' && previous != '.' && word.ToUpperInvariant() is "NETWORK" or "END_NETWORK" or "RUNG" or "END_RUNG")
                    result.Add((word, line));
                continue;
            }

            i++;
        }

        return result;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static SimaticSdException Invalid(string message) => new("validation_error", message);

    /// <summary>netstandard2.0 has no FirstOrDefault with a default value.</summary>
    internal static int FirstIndex(int count, Func<int, bool> predicate, bool reverse = false)
    {
        for (var k = 0; k < count; k++)
        {
            var i = reverse ? count - 1 - k : k;
            if (predicate(i)) return i;
        }

        return -1;
    }
}

/// <summary>
/// Reads and writes SD documents as bytes: UTF-8, a leading BOM kept as U+FEFF in the text so a round trip keeps it.
/// Written files always start with exactly one BOM: both files UTF-8 with BOM is reported (1748477582/tia-portal-openness-mcp,
/// not verified) to be required by ImportFromDocuments.
/// </summary>
public static class SimaticSdFileText
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    public static string Decode(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3 && bytes[0] == Bom[0] && bytes[1] == Bom[1] && bytes[2] == Bom[2];
        var text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        return hasBom ? "﻿" + text : text;
    }

    public static byte[] Encode(string text)
    {
        var body = text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
        return Bom.Concat(new UTF8Encoding(false).GetBytes(body)).ToArray();
    }

    public static bool HasBom(string text) => text.Length > 0 && text[0] == '﻿';
}
