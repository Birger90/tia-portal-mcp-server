using System.Text;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>One segment of a member path: a member name and, for DB value paths only, an array index.</summary>
public sealed class PlcMemberPathSegment
{
    public PlcMemberPathSegment(string name, string? index)
    {
        Name = name;
        Index = index;
    }

    public string Name { get; }

    /// <summary>The index text between the brackets without spaces, e.g. "3" or "1,2"; null when absent.</summary>
    public string? Index { get; }
}

/// <summary>
/// A dotted member path such as <c>Motor.Speed</c> or <c>Axis."a.b".Value</c>; DB value paths may
/// also carry array indices (<c>Arr[3]</c>, <c>Matrix[1,2]</c>). A segment in double quotes may contain
/// dots; member names never contain a double quote.
/// </summary>
public sealed class PlcMemberPath
{
    private PlcMemberPath(IReadOnlyList<PlcMemberPathSegment> segments) => Segments = segments;

    public IReadOnlyList<PlcMemberPathSegment> Segments { get; }

    public PlcMemberPathSegment Last => Segments[Segments.Count - 1];

    /// <summary>The path without its last segment, or null for a top-level member.</summary>
    public PlcMemberPath? Parent => Segments.Count == 1 ? null : new PlcMemberPath(Segments.Take(Segments.Count - 1).ToArray());

    public bool HasIndices => Segments.Any(segment => segment.Index is not null);

    /// <exception cref="ArgumentException">The text is not a valid member path.</exception>
    public static PlcMemberPath Parse(string text, bool allowIndices)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("A member path is required.");
        var segments = new List<PlcMemberPathSegment>();
        var position = 0;
        while (true)
        {
            string name;
            if (position < text.Length && text[position] == '"')
            {
                var close = text.IndexOf('"', position + 1);
                if (close < 0) throw new ArgumentException($"Member path '{text}' has an unterminated quoted segment.");
                name = text.Substring(position + 1, close - position - 1);
                position = close + 1;
            }
            else
            {
                var start = position;
                while (position < text.Length && text[position] is not ('.' or '[' or ']' or '"')) position++;
                name = text.Substring(start, position - start).Trim();
            }

            if (name.Length == 0) throw new ArgumentException($"Member path '{text}' has an empty segment.");
            if (name.Any(char.IsControl)) throw new ArgumentException($"Member path '{text}' contains a control character.");

            string? index = null;
            if (position < text.Length && text[position] == '[')
            {
                var close = text.IndexOf(']', position + 1);
                if (close < 0) throw new ArgumentException($"Member path '{text}' has an unterminated index.");
                index = NormalizeIndex(text.Substring(position + 1, close - position - 1), text);
                if (!allowIndices)
                    throw new ArgumentException($"Member path '{text}' addresses an array element; interface edits address whole members, so omit the index.");
                position = close + 1;
            }

            segments.Add(new PlcMemberPathSegment(name, index));
            if (position == text.Length) break;
            if (text[position] != '.') throw new ArgumentException($"Member path '{text}' is malformed at position {position + 1}.");
            position++;
            if (position == text.Length) throw new ArgumentException($"Member path '{text}' ends with '.'.");
        }

        return new PlcMemberPath(segments);
    }

    public static PlcMemberPath FromNames(IEnumerable<string> names)
        => new(names.Select(name => new PlcMemberPathSegment(name, null)).ToArray());

    public PlcMemberPath Append(string name) => new(Segments.Concat(new[] { new PlcMemberPathSegment(name, null) }).ToArray());

    public PlcMemberPath WithLastName(string name)
        => new(Segments.Take(Segments.Count - 1).Concat(new[] { new PlcMemberPathSegment(name, Last.Index) }).ToArray());

    /// <summary>
    /// The path as <c>Interface.Members.Find</c> takes it in the V21 reference examples
    /// (<c>Motor.InitialSpeed[0]</c>): names joined with '.', indices in brackets.
    /// </summary>
    /// <exception cref="ArgumentException">A name contains '.', which such a path cannot express.</exception>
    public string ToFindPath()
    {
        if (Segments.Any(segment => segment.Name.IndexOf('.') >= 0))
            throw new ArgumentException($"Member path '{this}' has a name containing '.', which an Interface.Members.Find path cannot express.");
        return string.Join(".", Segments.Select(s => s.Index is null ? s.Name : s.Name + "[" + s.Index + "]"));
    }

    /// <summary>Canonical text: a name containing '.', '[', ']' or surrounding blanks is quoted.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        foreach (var segment in Segments)
        {
            if (builder.Length > 0) builder.Append('.');
            builder.Append(NeedsQuotes(segment.Name) ? "\"" + segment.Name + "\"" : segment.Name);
            if (segment.Index is not null) builder.Append('[').Append(segment.Index).Append(']');
        }

        return builder.ToString();
    }

    public static bool NeedsQuotes(string name)
        => name.IndexOfAny(new[] { '.', '[', ']' }) >= 0 || name.Trim().Length != name.Length;

    private static string NormalizeIndex(string raw, string text)
    {
        var parts = raw.Split(',').Select(part => part.Trim()).ToArray();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out _))
                throw new ArgumentException($"Member path '{text}' has a non-integer array index '{raw}'.");
        }

        return string.Join(",", parts);
    }
}
