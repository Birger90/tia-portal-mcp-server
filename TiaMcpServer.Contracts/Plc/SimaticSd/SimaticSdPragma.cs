using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.SimaticSd;

/// <summary>One <c>key := value</c> entry of a SIMATIC SD pragma; <see cref="RawValue"/> is the value as written (quotes included).</summary>
public sealed record SimaticSdPragmaEntry(string Key, string RawValue)
{
    /// <summary>The value without its surrounding double quotes (a doubled quote inside is one quote).</summary>
    public string Value => SimaticSdPragma.Unquote(RawValue);
}

/// <summary>
/// A SIMATIC SD pragma <c>{ key := "value"; ... }</c> over one or several lines of an .s7dcl document, in both forms
/// the V21 exports write: single line <c>{ S7_Language := "LAD" }</c> and the multi-line block
/// <c>{</c> / <c>S7_Language := "LAD";</c> / <c>S7_NetworkTitle := "MLC_463"</c> / <c>}</c>. Every entry is kept in its
/// order with its raw value, so keys this server does not know survive a rewrite. Siemens-free.
/// </summary>
public sealed class SimaticSdPragma
{
    private static readonly Regex KeyPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private SimaticSdPragma(int startLine, int endLine, IReadOnlyList<string> lines, IReadOnlyList<SimaticSdPragmaEntry> entries)
    {
        StartLine = startLine;
        EndLine = endLine;
        Lines = lines;
        Entries = entries;
    }

    /// <summary>Index of the line holding the opening brace (in the document the pragma was parsed from).</summary>
    public int StartLine { get; }

    /// <summary>Index of the line holding the closing brace.</summary>
    public int EndLine { get; }

    /// <summary>The pragma's lines exactly as written.</summary>
    public IReadOnlyList<string> Lines { get; }

    public IReadOnlyList<SimaticSdPragmaEntry> Entries { get; }

    public bool IsMultiLine => Lines.Count > 1;

    /// <summary>The indentation of the opening brace line.</summary>
    public string Indent => LeadingWhitespace(Lines[0]);

    /// <summary>The unquoted value of <paramref name="key"/> (case-insensitive), or null.</summary>
    public string? Get(string key) => Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    public bool Has(string key) => Entries.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Parses the pragma spanning <paramref name="startLine"/>..<paramref name="endLine"/> of <paramref name="lines"/>;
    /// false when the text is not one <c>{ ... }</c> of <c>key := value</c> entries separated by <c>;</c>.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> lines, int startLine, int endLine, out SimaticSdPragma pragma)
    {
        pragma = null!;
        if (startLine < 0 || endLine >= lines.Count || startLine > endLine) return false;
        var own = new List<string>();
        for (var i = startLine; i <= endLine; i++) own.Add(lines[i]);
        var text = string.Join("\n", own).Trim();
        if (!text.StartsWith("{", StringComparison.Ordinal) || !text.EndsWith("}", StringComparison.Ordinal)) return false;
        var inner = text.Substring(1, text.Length - 2);
        var entries = new List<SimaticSdPragmaEntry>();
        foreach (var part in SplitOutsideQuotes(inner, ';'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0) continue;
            var assign = IndexOutsideQuotes(trimmed, ":=");
            if (assign <= 0) return false;
            var key = trimmed.Substring(0, assign).Trim();
            if (!KeyPattern.IsMatch(key)) return false;
            entries.Add(new SimaticSdPragmaEntry(key, trimmed.Substring(assign + 2).Trim()));
        }

        pragma = new SimaticSdPragma(startLine, endLine, own, entries);
        return true;
    }

    /// <summary>Parses a pragma written on its own lines (the whole of <paramref name="lines"/>).</summary>
    public static bool TryParse(IReadOnlyList<string> lines, out SimaticSdPragma pragma) => TryParse(lines, 0, lines.Count - 1, out pragma);

    /// <summary>
    /// The pragma's lines with <paramref name="updates"/> applied (a key present is changed in place, keeping the
    /// layout; a new key is added) and <paramref name="removals"/> removed. Unchanged when nothing differs.
    /// </summary>
    public IReadOnlyList<string> Rewrite(IReadOnlyList<SimaticSdPragmaEntry> updates, IReadOnlyCollection<string>? removals = null)
    {
        removals ??= Array.Empty<string>();
        var remaining = Entries.Where(e => !removals.Contains(e.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        var adds = new List<SimaticSdPragmaEntry>();
        var inPlace = remaining.Count == Entries.Count;
        foreach (var update in updates)
        {
            var index = remaining.FindIndex(e => string.Equals(e.Key, update.Key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) adds.Add(update);
            else remaining[index] = remaining[index] with { RawValue = update.RawValue };
        }

        if (remaining.SequenceEqual(Entries) && adds.Count == 0) return Lines;
        if (inPlace && adds.Count == 0)
        {
            // Every key stays: replace only the changed values, so the layout (and every other byte) is kept.
            var lines = Lines.ToList();
            foreach (var (before, after) in Entries.Zip(remaining, (b, a) => (b, a)).Where(p => p.b.RawValue != p.a.RawValue))
            {
                var pattern = new Regex(@"(?<head>(?<![A-Za-z0-9_])" + Regex.Escape(before.Key) + @"\s*:=\s*)" + Regex.Escape(before.RawValue), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                var changed = false;
                for (var i = 0; i < lines.Count && !changed; i++)
                {
                    var match = pattern.Match(lines[i]);
                    if (!match.Success) continue;
                    lines[i] = lines[i].Substring(0, match.Index) + match.Groups["head"].Value + after.RawValue + lines[i].Substring(match.Index + match.Length);
                    changed = true;
                }

                if (!changed) return Render(Indent, remaining, IsMultiLine, EntryIndent());
            }

            return lines;
        }

        return Render(Indent, remaining.Concat(adds).ToList(), IsMultiLine, EntryIndent());
    }

    /// <summary>A pragma in the export style: <c>{ k := v; ... }</c> on one line, or one entry per line between braces.</summary>
    public static IReadOnlyList<string> Render(string indent, IReadOnlyList<SimaticSdPragmaEntry> entries, bool multiLine, string? entryIndent = null)
    {
        if (!multiLine || entries.Count == 0)
            return new[] { indent + "{ " + string.Join("; ", entries.Select(e => $"{e.Key} := {e.RawValue}")) + (entries.Count == 0 ? "}" : " }") };
        var inner = entryIndent ?? indent + "    ";
        var lines = new List<string> { indent + "{" };
        for (var i = 0; i < entries.Count; i++)
            lines.Add($"{inner}{entries[i].Key} := {entries[i].RawValue}{(i < entries.Count - 1 ? ";" : string.Empty)}");
        lines.Add(indent + "}");
        return lines;
    }

    /// <summary>A double-quoted pragma value for <paramref name="value"/> (a quote inside is doubled).</summary>
    public static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    internal static string Unquote(string raw)
    {
        var text = raw.Trim();
        return text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"'
            ? text.Substring(1, text.Length - 2).Replace("\"\"", "\"")
            : text;
    }

    private string? EntryIndent()
        => IsMultiLine && Lines.Skip(1).Take(Lines.Count - 2).FirstOrDefault(l => l.Trim().Length > 0) is { } line ? LeadingWhitespace(line) : null;

    internal static string LeadingWhitespace(string line)
    {
        var count = 0;
        while (count < line.Length && (line[count] == ' ' || line[count] == '\t')) count++;
        return line.Substring(0, count);
    }

    private static IEnumerable<string> SplitOutsideQuotes(string text, char separator)
    {
        var current = new StringBuilder();
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            if (c == separator && !quoted)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        yield return current.ToString();
    }

    private static int IndexOutsideQuotes(string text, string token)
    {
        var quoted = false;
        for (var i = 0; i <= text.Length - token.Length; i++)
        {
            if (text[i] == '"') quoted = !quoted;
            if (!quoted && string.CompareOrdinal(text, i, token, 0, token.Length) == 0) return i;
        }

        return -1;
    }
}
