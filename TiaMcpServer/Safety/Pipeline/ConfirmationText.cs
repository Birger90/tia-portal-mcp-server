using System.Globalization;
using System.Text;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>
/// Makes text safe to show in a human confirmation prompt. Every Unicode control (Cc: CR, LF, TAB, ...),
/// format (Cf: bidirectional embeddings, overrides and isolates, zero-width characters, ...), line
/// separator (Zl) and paragraph separator (Zp) character, and every unpaired surrogate, is written as a
/// JSON-style escape (<c>\n</c>, <c>\r</c>, <c>\t</c>, <c>\uXXXX</c>). A caller-supplied value can then
/// neither start a new prompt line, push the real effect out of view, nor reorder or hide the text around it.
/// </summary>
public static class ConfirmationText
{
    /// <summary>
    /// The value as one JSON string literal (double quotes; <c>"</c> and <c>\</c> escaped as well), so where
    /// a caller-supplied value starts and ends is unambiguous. Null is written as <c>null</c>.
    /// </summary>
    public static string Quote(string? value)
        => value is null ? "null" : "\"" + Escape(value, literal: true, keepLineFeeds: false) + "\"";

    /// <summary>One prompt line: escapes every unsafe character, line feeds included, without adding quotes.</summary>
    public static string Line(string? text) => text is null ? string.Empty : Escape(text, literal: false, keepLineFeeds: false);

    /// <summary>
    /// A domain's own multi-line prompt text: escapes every unsafe character but keeps line feeds, which
    /// the domain uses as its line separator. Caller values inside it must already be <see cref="Quote"/>d.
    /// </summary>
    public static string Block(string? text) => text is null ? string.Empty : Escape(text, literal: false, keepLineFeeds: true);

    /// <summary>True when the text contains a character that <see cref="Line"/> would escape.</summary>
    public static bool ContainsUnsafeCharacters(string? text)
    {
        if (text is null) return false;
        for (var index = 0; index < text.Length; index++)
        {
            if (UnsafeAt(text, index, out var width)) return true;
            index += width - 1;
        }

        return false;
    }

    private static string Escape(string text, bool literal, bool keepLineFeeds)
    {
        StringBuilder? builder = null;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var unsafeCharacter = UnsafeAt(text, index, out var width) && !(keepLineFeeds && character == '\n');
            var quoted = literal && (character is '"' or '\\');
            if (!unsafeCharacter && !quoted)
            {
                builder?.Append(text, index, width);
                index += width - 1;
                continue;
            }

            builder ??= new StringBuilder(text.Length + 16).Append(text, 0, index);
            for (var unit = 0; unit < width; unit++)
                AppendEscape(builder, text[index + unit]);
            index += width - 1;
        }

        return builder?.ToString() ?? text;
    }

    private static void AppendEscape(StringBuilder builder, char character)
    {
        switch (character)
        {
            case '\n': builder.Append("\\n"); break;
            case '\r': builder.Append("\\r"); break;
            case '\t': builder.Append("\\t"); break;
            case '"': builder.Append("\\\""); break;
            case '\\': builder.Append("\\\\"); break;
            default: builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture)); break;
        }
    }

    /// <summary>Whether the code point at <paramref name="index"/> is unsafe; <paramref name="width"/> is its UTF-16 length.</summary>
    private static bool UnsafeAt(string text, int index, out int width)
    {
        var character = text[index];
        if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            width = 2;
            return IsUnsafe(CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(character, text[index + 1])));
        }

        width = 1;
        return char.IsSurrogate(character) || IsUnsafe(CharUnicodeInfo.GetUnicodeCategory(character));
    }

    private static bool IsUnsafe(UnicodeCategory category) => category is UnicodeCategory.Control or UnicodeCategory.Format
        or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
