using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.SimaticSd;

/// <summary>A structural problem of an .s7dcl document or of caller code; <see cref="Category"/> is a worker failure category.</summary>
public sealed class SimaticSdException(string category, string message) : Exception(message)
{
    public string Category { get; } = category;
}

/// <summary>
/// One network of an .s7dcl document. <see cref="Pragma"/> is the pragma right before its <c>NETWORK</c> line (blank
/// lines between them allowed), null when the network has none. Line numbers index <see cref="SimaticSdDocument.Lines"/>.
/// </summary>
public sealed record SimaticSdNetwork(
    int Number,
    SimaticSdPragma? Pragma,
    int NetworkLine,
    int EndLine,
    string? Language,
    string? LanguageSource,
    IReadOnlyList<string> BodyLines)
{
    /// <summary>The first line of the network: its pragma's opening line, else the NETWORK line.</summary>
    public int StartLine => Pragma?.StartLine ?? NetworkLine;

    /// <summary>The body without its common indentation, lines joined by \n.</summary>
    public string Code => string.Join("\n", SimaticSdText.Dedent(BodyLines));
}

/// <summary>
/// A SIMATIC SD .s7dcl document split into lines, with its header pragma, block declaration and networks, and the
/// network edits of the Siemens V21 skill <c>simatic-sd</c> (§4 parsing, §5 language detection, §6 normalization,
/// §7 insertion): a network's pragma stays immediately before its NETWORK; a new network gets the single-line
/// <c>{ S7_Language := "..." }</c> pragma; the .s7res is never touched. A leading UTF-8 BOM and the line endings are kept.
/// Siemens-free; the document is never interpreted beyond its structure, TIA Portal's import decides the rest.
/// </summary>
public sealed class SimaticSdDocument
{
    private static readonly Regex NetworkStart = new(@"^\s*NETWORK\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex NetworkEnd = new(@"^\s*END_NETWORK\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Declaration = new(
        @"^\s*(?<kind>FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK)\s+(?<name>""(?:[^""]|"""")+""|[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant);
    private static readonly Regex BlockEnd = new(@"^\s*END_(?:FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK)\s*$", RegexOptions.CultureInvariant);

    private SimaticSdDocument(bool bom, string newline, bool trailingNewline, List<string> lines)
    {
        HasBom = bom;
        Newline = newline;
        TrailingNewline = trailingNewline;
        Lines = lines;
    }

    public bool HasBom { get; }

    /// <summary>"\r\n" when the document uses CRLF line endings, else "\n".</summary>
    public string Newline { get; }

    public bool TrailingNewline { get; }

    public IReadOnlyList<string> Lines { get; }

    public SimaticSdPragma? HeaderPragma { get; private set; }

    /// <summary>FUNCTION_BLOCK, FUNCTION, ORGANIZATION_BLOCK or DATA_BLOCK; null when no declaration was found.</summary>
    public string? BlockKind { get; private set; }

    /// <summary>The declared block name without quotes; null when no declaration was found.</summary>
    public string? BlockName { get; private set; }

    public int DeclarationLine { get; private set; } = -1;

    public IReadOnlyList<SimaticSdNetwork> Networks { get; private set; } = Array.Empty<SimaticSdNetwork>();

    /// <summary>The header's S7_PreferredLanguage, or null.</summary>
    public string? PreferredLanguage => HeaderPragma?.Get("S7_PreferredLanguage");

    /// <summary>True when the header pragma declares <c>S7_Safety := "TRUE"</c> (a fail-safe block).</summary>
    public bool IsFailSafe => string.Equals(HeaderPragma?.Get("S7_Safety"), "TRUE", StringComparison.OrdinalIgnoreCase);

    /// <exception cref="SimaticSdException">The document's NETWORK/END_NETWORK lines do not pair (validation_error).</exception>
    public static SimaticSdDocument Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        var bom = text.Length > 0 && text[0] == '﻿';
        var body = bom ? text.Substring(1) : text;
        var newline = body.Contains("\r\n") ? "\r\n" : "\n";
        var trailing = body.EndsWith("\n", StringComparison.Ordinal);
        var raw = body.Split('\n').Select(l => l.EndsWith("\r", StringComparison.Ordinal) ? l.Substring(0, l.Length - 1) : l).ToList();
        if (trailing) raw.RemoveAt(raw.Count - 1);
        var document = new SimaticSdDocument(bom, newline, trailing, raw);
        document.Analyze();
        return document;
    }

    private void Analyze()
    {
        var lines = (List<string>)Lines;
        for (var i = 0; i < lines.Count; i++)
        {
            var match = Declaration.Match(lines[i]);
            if (!match.Success) continue;
            DeclarationLine = i;
            BlockKind = match.Groups["kind"].Value;
            BlockName = SimaticSdPragma.Unquote(match.Groups["name"].Value);
            break;
        }

        if (DeclarationLine > 0)
        {
            var first = SimaticSdText.FirstIndex(DeclarationLine, i => lines[i].Trim().Length > 0 && !lines[i].Trim().StartsWith("//", StringComparison.Ordinal));
            var last = SimaticSdText.FirstIndex(DeclarationLine, i => lines[i].Trim().Length > 0, reverse: true);
            if (first >= 0 && last >= first && lines[first].TrimStart().StartsWith("{", StringComparison.Ordinal)
                && SimaticSdPragma.TryParse(lines, first, last, out var header))
                HeaderPragma = header;
        }

        var networks = new List<SimaticSdNetwork>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (NetworkEnd.IsMatch(lines[i]))
                throw new SimaticSdException("validation_error", $"Line {i + 1} has END_NETWORK without a NETWORK before it.");
            if (!NetworkStart.IsMatch(lines[i])) continue;
            var end = -1;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (NetworkStart.IsMatch(lines[j]))
                    throw new SimaticSdException("validation_error", $"The NETWORK on line {i + 1} has no END_NETWORK before the next NETWORK (line {j + 1}).");
                if (NetworkEnd.IsMatch(lines[j]))
                {
                    end = j;
                    break;
                }
            }

            if (end < 0) throw new SimaticSdException("validation_error", $"The NETWORK on line {i + 1} has no END_NETWORK.");
            var pragma = PragmaBefore(lines, i);
            var bodyLines = lines.Skip(i + 1).Take(end - i - 1).ToList();
            var language = pragma?.Get("S7_Language");
            var source = language is null ? null : "pragma";
            if (language is null && SimaticSdText.DetectLanguage(string.Join("\n", bodyLines)) is { } detected)
                (language, source) = (detected, "detected");
            networks.Add(new SimaticSdNetwork(networks.Count + 1, pragma, i, end, language, source, bodyLines));
            i = end;
        }

        Networks = networks;
    }

    /// <summary>
    /// The pragma right before a NETWORK line: the nearest non-blank line before it is a whole single-line pragma, or
    /// the closing brace of a multi-line one (searched back to its opening brace, never across another statement).
    /// </summary>
    private static SimaticSdPragma? PragmaBefore(IReadOnlyList<string> lines, int networkLine)
    {
        var last = networkLine - 1;
        while (last >= 0 && lines[last].Trim().Length == 0) last--;
        if (last < 0 || !lines[last].TrimEnd().EndsWith("}", StringComparison.Ordinal)) return null;
        for (var first = last; first >= 0 && networkLine - first <= 200; first--)
        {
            var trimmed = lines[first].Trim();
            if (first < last && (NetworkStart.IsMatch(lines[first]) || NetworkEnd.IsMatch(lines[first]) || trimmed.EndsWith("}", StringComparison.Ordinal))) return null;
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) continue;
            return SimaticSdPragma.TryParse(lines, first, last, out var pragma) ? pragma : null;
        }

        return null;
    }

    /// <summary>The document text with <see cref="HasBom"/>, the line endings and the trailing newline as parsed.</summary>
    public string Render(IReadOnlyList<string>? lines = null)
    {
        var text = string.Join(Newline, lines ?? Lines);
        if (TrailingNewline) text += Newline;
        return HasBom ? "﻿" + text : text;
    }

    /// <summary>The network with <paramref name="number"/> (1-based), or a target_not_found error.</summary>
    public SimaticSdNetwork Network(int number)
        => number >= 1 && number <= Networks.Count ? Networks[number - 1]
            : throw new SimaticSdException("target_not_found",
                $"network {number} does not exist: the block has {Networks.Count} network(s){(Networks.Count == 0 ? string.Empty : $", numbered 1 to {Networks.Count}")}.");

    /// <summary>
    /// Inserts <paramref name="code"/> as a new network before network <paramref name="position"/> (1-based; null or
    /// one past the last network appends) with the pragma <c>{ S7_Language := "language" }</c> (plus the caller's other
    /// pragma keys), indented like the document's networks. §7 of the Siemens skill.
    /// </summary>
    public string InsertNetwork(SimaticSdNetworkCode code, string language, int? position)
    {
        var count = Networks.Count;
        if (position is { } p && (p < 1 || p > count + 1))
            throw new SimaticSdException("target_not_found", $"position {p} is outside 1 to {count + 1} (the block has {count} network(s); {count + 1} or omitted appends).");
        int index;
        if (position is { } before && before <= count) index = Networks[before - 1].StartLine;
        else if (count > 0) index = Networks[count - 1].EndLine + 1;
        else
        {
            index = SimaticSdText.FirstIndex(Lines.Count, i => BlockEnd.IsMatch(Lines[i]), reverse: true);
            if (index < 0) throw new SimaticSdException("validation_error", "the document has no network and no END_FUNCTION_BLOCK, END_FUNCTION, END_ORGANIZATION_BLOCK or END_DATA_BLOCK line to insert before.");
        }

        var (networkIndent, bodyIndent) = Indentation(null);
        var entries = new[] { new SimaticSdPragmaEntry("S7_Language", SimaticSdPragma.Quote(language)) }.Concat(code.ExtraPragmaEntries).ToList();
        var lines = Lines.ToList();
        lines.InsertRange(index, SimaticSdPragma.Render(networkIndent, entries, multiLine: false)
            .Append(networkIndent + "NETWORK")
            .Concat(code.BodyLines.Select(l => bodyIndent + l))
            .Append(networkIndent + "END_NETWORK"));
        return Render(lines);
    }

    /// <summary>
    /// Replaces the body of network <paramref name="number"/> with <paramref name="code"/>. Its pragma stays, every
    /// key kept; only S7_Language changes when <paramref name="language"/> differs, and the caller's other pragma keys
    /// are set. A network without a pragma gets one only when a language or keys are given.
    /// </summary>
    public string ReplaceNetwork(int number, SimaticSdNetworkCode code, string? language)
    {
        var network = Network(number);
        var updates = new List<SimaticSdPragmaEntry>();
        if (language is not null && !string.Equals(language, network.Pragma?.Get("S7_Language"), StringComparison.Ordinal))
            updates.Add(new("S7_Language", SimaticSdPragma.Quote(language)));
        updates.AddRange(code.ExtraPragmaEntries);
        var (networkIndent, bodyIndent) = Indentation(network);
        IReadOnlyList<string> pragmaLines = network.Pragma is { } pragma
            ? pragma.Rewrite(updates)
            : updates.Count == 0 ? Array.Empty<string>() : SimaticSdPragma.Render(networkIndent, updates, multiLine: false);
        var lines = Lines.ToList();
        var replacement = pragmaLines
            .Concat(network.Pragma is null ? Array.Empty<string>() : Lines.Skip(network.Pragma.EndLine + 1).Take(network.NetworkLine - network.Pragma.EndLine - 1))
            .Append(Lines[network.NetworkLine])
            .Concat(code.BodyLines.Select(l => bodyIndent + l))
            .Append(Lines[network.EndLine]).ToList();
        lines.RemoveRange(network.StartLine, network.EndLine - network.StartLine + 1);
        lines.InsertRange(network.StartLine, replacement);
        return Render(lines);
    }

    /// <summary>Removes network <paramref name="number"/> with its pragma (comment lines before the pragma stay).</summary>
    public string DeleteNetwork(int number)
    {
        var network = Network(number);
        var lines = Lines.ToList();
        lines.RemoveRange(network.StartLine, network.EndLine - network.StartLine + 1);
        // Two blank lines meeting where the network was collapse to one.
        var at = network.StartLine;
        if (at > 0 && at < lines.Count && lines[at - 1].Trim().Length == 0 && lines[at].Trim().Length == 0) lines.RemoveAt(at);
        return Render(lines);
    }

    /// <summary>The indentation of NETWORK lines and of network bodies, from <paramref name="network"/> or the document (default 4 and 8 spaces).</summary>
    private (string Network, string Body) Indentation(SimaticSdNetwork? network)
    {
        var sample = network ?? Networks.FirstOrDefault();
        if (sample is null) return ("    ", "        ");
        var networkIndent = SimaticSdPragma.LeadingWhitespace(Lines[sample.NetworkLine]);
        var body = sample.BodyLines.FirstOrDefault(l => l.Trim().Length > 0);
        var bodyIndent = body is null ? networkIndent + "    " : SimaticSdPragma.LeadingWhitespace(body);
        return (networkIndent, bodyIndent.Length > networkIndent.Length ? bodyIndent : networkIndent + "    ");
    }
}
