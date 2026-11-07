using System.Text;

namespace TiaMcpServer.Tools;

/// <summary>Shortened schema descriptions (K91): the tool listing must stay small enough for Claude Desktop.</summary>
internal static class SchemaDescriptions
{
    /// <summary>
    /// The schema form of a field description (K91): its first sentence when that is at most <paramref name="limit"/>
    /// characters, otherwise none. The full text stays the <see cref="System.ComponentModel.DescriptionAttribute"/>.
    /// </summary>
    internal static string? FirstSentence(string description, int limit)
    {
        var builder = new StringBuilder();
        var depth = 0;
        var quoted = false;
        foreach (var (c, i) in description.Select((c, i) => (c, i)))
        {
            builder.Append(c);
            if (c == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && c == '.' && (i + 1 == description.Length || description[i + 1] == ' ') && !IsAbbreviation(description, i))
                break;
            if (builder.Length > limit) return null;
        }

        var sentence = builder.ToString().Trim();
        return sentence.Length > 0 && sentence.Length <= limit ? sentence : null;
    }

    private static bool IsAbbreviation(string text, int dot)
        => dot >= 3 && (string.CompareOrdinal(text, dot - 3, "e.g", 0, 3) == 0 || string.CompareOrdinal(text, dot - 3, "i.e", 0, 3) == 0);
}
