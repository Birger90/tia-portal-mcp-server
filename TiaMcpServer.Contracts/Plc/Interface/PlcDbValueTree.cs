using System.Globalization;
using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>One value path of a DB as <c>Interface.Members.Find</c> addresses it.</summary>
public sealed class PlcDbValueEntry
{
    public PlcDbValueEntry(string path, string? dataType, string kind, string? message)
    {
        Path = path;
        DataType = dataType;
        Kind = kind;
        Message = message;
    }

    public string Path { get; }

    public string? DataType { get; }

    /// <summary><see cref="PlcDbValueKinds.Value"/> or <see cref="PlcDbValueKinds.Unexpanded"/>.</summary>
    public string Kind { get; }

    public string? Message { get; }
}

public sealed class PlcDbValueTreeResult
{
    public List<PlcDbValueEntry> Entries { get; } = new List<PlcDbValueEntry>();

    public List<string> Messages { get; } = new List<string>();

    public bool IsComplete { get; internal set; } = true;

    /// <summary>False when a requested narrowing path matched no member of the expanded tree.</summary>
    public bool SubtreeFound { get; internal set; }
}

/// <summary>
/// Enumerates the value paths of a global or instance DB from the interface trees of its SimaticML
/// documents: Struct members recurse into their nested members, arrays with literal integer bounds
/// expand into <c>name[i]</c> / <c>name[i,j]</c> elements, and a quoted data type (PLC data type or
/// multi-instance FB) is expanded through <c>resolveType</c>. Everything else is an elementary value.
/// Siemens-free: the worker supplies the documents and reads the values natively.
/// </summary>
public static class PlcDbValueTree
{
    public const int MaxEntries = 400;
    public const int MaxArrayElements = 256;
    public const int MaxDepth = 24;

    private static readonly Regex ArrayPattern = new(@"^\s*Array\s*\[(?<dims>[^\]]*)\]\s+of\s+(?<element>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BoundPattern = new(@"^\s*(?<low>-?\d+)\s*\.\.\s*(?<high>-?\d+)\s*$", RegexOptions.CultureInvariant);

    /// <param name="resolveType">The members of a quoted type name (without quotes), or null when it cannot be resolved.</param>
    /// <param name="subtree">Only paths at or below this path; null for every path.</param>
    public static PlcDbValueTreeResult Build(
        IReadOnlyList<PlcInterfaceMemberInfo> roots,
        Func<string, IReadOnlyList<PlcInterfaceMemberInfo>?> resolveType,
        string? subtree,
        int maxEntries = MaxEntries)
    {
        var result = new PlcDbValueTreeResult { SubtreeFound = subtree is null };
        var walker = new Walker(result, resolveType, subtree, maxEntries);
        walker.Members(roots, null, 0, new List<string>());
        return result;
    }

    private sealed class Walker(PlcDbValueTreeResult result, Func<string, IReadOnlyList<PlcInterfaceMemberInfo>?> resolveType, string? subtree, int maxEntries)
    {
        private bool _stopped;

        public void Members(IEnumerable<PlcInterfaceMemberInfo> members, string? parent, int depth, List<string> typeStack)
        {
            foreach (var member in members)
            {
                if (_stopped) return;
                if (member.Name.IndexOf('.') >= 0)
                {
                    var odd = Join(parent, member.Name);
                    if (Relevant(odd)) Add(new(odd, member.DataType, PlcDbValueKinds.Unexpanded, "The member name contains '.', which an Interface.Members.Find path cannot express."));
                    continue;
                }

                Value(Join(parent, member.Name), member.DataType, member.Members, depth, typeStack);
            }
        }

        private void Value(string path, string? dataType, IReadOnlyList<PlcInterfaceMemberInfo> children, int depth, List<string> typeStack)
        {
            if (_stopped || !Relevant(path)) return;
            if (subtree is not null && string.Equals(path, subtree, StringComparison.OrdinalIgnoreCase)) result.SubtreeFound = true;
            if (depth > MaxDepth)
            {
                Add(new(path, dataType, PlcDbValueKinds.Unexpanded, $"Nesting deeper than {MaxDepth} levels is not expanded."));
                return;
            }

            var array = dataType is null ? null : ArrayPattern.Match(dataType);
            if (array is { Success: true })
            {
                ArrayValue(path, dataType!, array, children, depth, typeStack);
                return;
            }

            if (SimaticMlInterfaceDocument.IsStruct(dataType) || dataType is null && children.Count > 0)
            {
                Members(children, path, depth + 1, typeStack);
                return;
            }

            if (SimaticMlInterfaceDocument.IsQuoted(dataType))
            {
                var typeName = dataType!.Trim().Substring(1, dataType.Trim().Length - 2);
                if (typeStack.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                {
                    Add(new(path, dataType, PlcDbValueKinds.Unexpanded, $"Type '{typeName}' contains itself; it is not expanded again."));
                    return;
                }

                var members = resolveType(typeName);
                if (members is null)
                {
                    Add(new(path, dataType, PlcDbValueKinds.Unexpanded, $"Type '{typeName}' could not be exported as a PLC data type or FB, so its members are not listed. Address a known member path with memberPath."));
                    return;
                }

                Members(members, path, depth + 1, typeStack.Concat(new[] { typeName }).ToList());
                return;
            }

            if (subtree is null || IsPrefix(subtree, path)) Add(new(path, dataType, PlcDbValueKinds.Value, null));
        }

        private void ArrayValue(string path, string dataType, Match array, IReadOnlyList<PlcInterfaceMemberInfo> children, int depth, List<string> typeStack)
        {
            var bounds = new List<(int Low, int High)>();
            foreach (var dimension in array.Groups["dims"].Value.Split(','))
            {
                var bound = BoundPattern.Match(dimension);
                if (!bound.Success)
                {
                    Add(new(path, dataType, PlcDbValueKinds.Unexpanded, "The array bounds are not integer literals; address an element with memberPath, e.g. name[0]."));
                    return;
                }

                var low = int.Parse(bound.Groups["low"].Value, CultureInfo.InvariantCulture);
                var high = int.Parse(bound.Groups["high"].Value, CultureInfo.InvariantCulture);
                if (high < low)
                {
                    Add(new(path, dataType, PlcDbValueKinds.Unexpanded, "The array has an empty range."));
                    return;
                }

                bounds.Add((low, high));
            }

            long count = 1;
            foreach (var (low, high) in bounds) count = Math.Min(count * ((long)high - low + 1), int.MaxValue);
            var narrowedInside = subtree is not null && subtree.Length > path.Length && IsPrefix(path, subtree);
            if (count > MaxArrayElements && !narrowedInside)
            {
                Add(new(path, dataType, PlcDbValueKinds.Unexpanded, $"The array has {count} elements (more than {MaxArrayElements}); address an element or a smaller range of its members with memberPath, e.g. {path}[{bounds[0].Low}]."));
                return;
            }

            var element = array.Groups["element"].Value;
            if (narrowedInside)
            {
                // memberPath points into this array: only the requested element is relevant, however large the array is.
                if (RequestedIndex(subtree!, path, bounds) is { } requested)
                    Value($"{path}[{requested}]", element, children, depth + 1, typeStack);
                return;
            }

            foreach (var index in Indices(bounds))
            {
                if (_stopped) return;
                Value($"{path}[{index}]", element, children, depth + 1, typeStack);
            }
        }

        /// <summary>
        /// The element index <paramref name="subtree"/> names right after <paramref name="path"/>, in
        /// the canonical form this walker produces; null when it names none, is not an integer list of
        /// the array's rank, or lies outside the bounds (the worker then asks Openness directly).
        /// </summary>
        private static string? RequestedIndex(string subtree, string path, IReadOnlyList<(int Low, int High)> bounds)
        {
            if (subtree.Length <= path.Length + 1 || subtree[path.Length] != '[') return null;
            var close = subtree.IndexOf(']', path.Length + 1);
            if (close < 0) return null;
            var parts = subtree.Substring(path.Length + 1, close - path.Length - 1).Split(',');
            if (parts.Length != bounds.Count) return null;
            var indices = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index)
                    || index < bounds[i].Low || index > bounds[i].High) return null;
                indices[i] = index;
            }

            var canonical = string.Join(",", indices.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            // A subtree spelled differently from the canonical index (e.g. "+2") is still matched by Relevant only in canonical form.
            return string.Equals(canonical, subtree.Substring(path.Length + 1, close - path.Length - 1), StringComparison.Ordinal) ? canonical : null;
        }

        private static IEnumerable<string> Indices(IReadOnlyList<(int Low, int High)> bounds)
        {
            var current = bounds.Select(b => b.Low).ToArray();
            while (true)
            {
                yield return string.Join(",", current.Select(i => i.ToString(CultureInfo.InvariantCulture)));
                var dimension = current.Length - 1;
                while (dimension >= 0 && current[dimension] == bounds[dimension].High)
                {
                    current[dimension] = bounds[dimension].Low;
                    dimension--;
                }

                if (dimension < 0) yield break;
                current[dimension]++;
            }
        }

        private bool Relevant(string path) => subtree is null || IsPrefix(path, subtree) || IsPrefix(subtree, path);

        private void Add(PlcDbValueEntry entry)
        {
            if (!Relevant(entry.Path)) return;
            if (subtree is not null && IsPrefix(subtree, entry.Path)) result.SubtreeFound = true;
            if (result.Entries.Count >= maxEntries)
            {
                result.IsComplete = false;
                result.Messages.Add($"Stopped after {maxEntries} values; narrow the read with memberPath.");
                _stopped = true;
                return;
            }

            if (entry.Kind == PlcDbValueKinds.Unexpanded) result.IsComplete = false;
            result.Entries.Add(entry);
        }

        private static string Join(string? parent, string name) => parent is null ? name : parent + "." + name;
    }

    /// <summary>True when <paramref name="path"/> equals <paramref name="prefix"/> or lies below it.</summary>
    public static bool IsPrefix(string prefix, string path)
        => string.Equals(prefix, path, StringComparison.OrdinalIgnoreCase)
            || path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path[prefix.Length] is '.' or '[';
}
