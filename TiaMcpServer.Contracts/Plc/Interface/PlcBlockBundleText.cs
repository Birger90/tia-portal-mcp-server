using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>
/// Locates and replaces the SimaticML (.xml) document inside a get_block_content xml bundle
/// (<c>--- FILE: name ---</c> delimited). The delimiter pattern and the "first document named *.xml"
/// rule are the worker's <c>BlockBundleFormat.DocumentDelimiter</c> and
/// <c>BlockImportRouting.SelectAuthoritativeDocument</c>; tests pin this reader against both so the
/// two sides cannot drift. Every other document and delimiter stays byte-identical.
/// </summary>
public static class PlcBlockBundleText
{
    private static readonly Regex Delimiter = new(
        @"^--- FILE: (?<name>.+) ---(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>Finds the SimaticML document; false when the bundle has no delimited .xml document.</summary>
    public static bool TryGetSimaticMl(string bundle, out string name, out string content)
        => TryGetDocument(bundle, ".xml", out name, out content);

    /// <summary>
    /// Finds the first delimited document whose name ends with <paramref name="extension"/> (case-insensitive), for
    /// example <c>.s7dcl</c>; false when the bundle has no such delimited document.
    /// </summary>
    public static bool TryGetDocument(string bundle, string extension, out string name, out string content)
    {
        if (TryLocate(bundle, extension, out name, out var start, out var length))
        {
            content = bundle.Substring(start, length);
            return true;
        }

        content = string.Empty;
        return false;
    }

    /// <summary>Replaces the SimaticML document's content and leaves everything else unchanged.</summary>
    /// <exception cref="ArgumentException">The bundle has no SimaticML document.</exception>
    public static string ReplaceSimaticMl(string bundle, string content)
        => ReplaceDocument(bundle, ".xml", content, "The block bundle contains no SimaticML (.xml) document.");

    /// <summary>
    /// Replaces the content of the first document whose name ends with <paramref name="extension"/> (for example the
    /// .s7dcl of an sd bundle) and leaves every other document and delimiter unchanged.
    /// </summary>
    /// <exception cref="ArgumentException">The bundle has no such document.</exception>
    public static string ReplaceDocument(string bundle, string extension, string content, string? missingMessage = null)
    {
        if (!TryLocate(bundle, extension, out _, out var start, out var length))
            throw new ArgumentException(missingMessage ?? $"The block bundle contains no {extension} document.");
        var original = bundle.Substring(start, length);
        var end = start + length;
        // The next delimiter must keep starting a line.
        if (end < bundle.Length && original.EndsWith("\n", StringComparison.Ordinal) && !content.EndsWith("\n", StringComparison.Ordinal))
            content += original.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return bundle.Substring(0, start) + content + bundle.Substring(end);
    }

    /// <summary>Every delimited document of the bundle in order, as (name, content); empty when the text has no delimiter.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Documents(string bundle)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(bundle)) return result;
        var matches = Delimiter.Matches(bundle);
        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index + matches[index].Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : bundle.Length;
            result.Add(new KeyValuePair<string, string>(matches[index].Groups["name"].Value, bundle.Substring(start, end - start)));
        }

        return result;
    }

    /// <summary>
    /// A bundle of <paramref name="documents"/> in the worker's <c>BlockBundleFormat.Compose</c> layout: <c>--- FILE: name ---\n</c>
    /// before each, a newline appended to every document but the last that does not end in one.
    /// </summary>
    public static string Compose(IReadOnlyList<KeyValuePair<string, string>> documents)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < documents.Count; index++)
        {
            builder.Append("--- FILE: ").Append(documents[index].Key).Append(" ---\n").Append(documents[index].Value);
            if (index < documents.Count - 1 && !documents[index].Value.EndsWith("\n", StringComparison.Ordinal)) builder.Append('\n');
        }

        return builder.ToString();
    }

    private static bool TryLocate(string bundle, string extension, out string name, out int start, out int length)
    {
        name = string.Empty;
        start = length = 0;
        if (string.IsNullOrEmpty(bundle)) return false;
        var matches = Delimiter.Matches(bundle);
        for (var index = 0; index < matches.Count; index++)
        {
            var match = matches[index];
            var documentName = match.Groups["name"].Value;
            if (!documentName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
            name = documentName;
            start = match.Index + match.Length;
            length = (index + 1 < matches.Count ? matches[index + 1].Index : bundle.Length) - start;
            return true;
        }

        return false;
    }
}
