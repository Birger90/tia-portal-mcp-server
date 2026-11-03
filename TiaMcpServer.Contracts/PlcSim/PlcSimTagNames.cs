using System.Text;

namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// K56 (live 2026-11-02, PLCSIM Advanced): a tag name in TIA Portal notation with quoted segments (<c>"DB_Line".Speed</c>)
/// failed with DoesNotExist; the PLCSIM Advanced API takes the name without quotes (<c>DB_Line.Speed</c>, the form
/// list_tags returns). Both forms are accepted: the outer double quotes of each dot-separated segment are removed before
/// the API call; array indices (<c>[1]</c>, <c>[1,2]</c>) stay as they are. A segment whose quoted text itself contains a
/// dot, a double quote or a bracket would be ambiguous without its quotes, so such a name, and any name whose quotes are
/// not balanced per segment, is passed on unchanged. Siemens-free; used by the PLCSIM worker.
/// </summary>
public static class PlcSimTagNames
{
    /// <summary>The name as the PLCSIM Advanced API addresses it.</summary>
    public static string ToApiName(string name)
    {
        if (name is null || name.IndexOf('"') < 0) return name!;
        var result = new StringBuilder(name.Length);
        var index = 0;
        var segmentStart = true;
        while (index < name.Length)
        {
            var character = name[index];
            if (segmentStart && character == '"')
            {
                var close = name.IndexOf('"', index + 1);
                if (close < 0) return name;
                var inner = name.Substring(index + 1, close - index - 1);
                if (inner.Length == 0 || inner.IndexOfAny(new[] { '.', '[', ']' }) >= 0) return name;
                var next = close + 1 < name.Length ? name[close + 1] : '\0';
                if (next != '\0' && next != '.' && next != '[') return name;
                result.Append(inner);
                index = close + 1;
                segmentStart = false;
                continue;
            }

            if (character == '"') return name;
            result.Append(character);
            segmentStart = character == '.';
            index++;
        }

        return result.ToString();
    }
}
