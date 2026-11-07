using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Plc;

/// <summary>
/// One field of a described operation: its wire name and its documentation for that operation. <paramref name="Trimmed"/> is
/// true when parts of the field's shared description that name only other operations were left out (K93).
/// </summary>
public sealed record PlcOperationFieldDescription(string Name, bool Required, string? Description, bool Trimmed = false);

/// <summary>One operation as describe_operations returns it.</summary>
public sealed record PlcOperationDescription(
    string Operation,
    string Tool,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields,
    IReadOnlyList<PlcOperationFieldDescription> Fields,
    IReadOnlyList<string> Reference,
    IReadOnlyList<string> SectionNotes);

/// <summary>
/// One entry of the operation index (describe_operations without operationNames): the fields as one compact text, the
/// required ones first, then "optional: " and the optional ones ("none" for an operation without fields).
/// </summary>
public sealed record PlcOperationIndexEntry(string Operation, string Tool, string Fields);

/// <summary>The describe_operations result: the described operations or the index, plus the tool-wide rules.</summary>
public sealed record PlcOperationDescribeResult(
    IReadOnlyList<PlcOperationDescription>? Operations,
    IReadOnlyList<PlcOperationIndexEntry>? Index,
    IReadOnlyList<string> ReadToolRules,
    IReadOnlyList<string> WriteToolRules,
    string ReferenceDocument);

/// <summary>
/// describe_operations (K91): the per-operation reference that the plc_read and plc_write tool descriptions carried before
/// they were shortened. Computed from <see cref="PlcOperationCatalog"/> (fields), the <see cref="DescriptionAttribute"/> of
/// every <see cref="PlcOperationRequest"/> field (field rules) and the reference texts <see cref="ReadTool"/> and
/// <see cref="WriteTool"/> (operation rules). The texts are split into segments at top-level "; " and ". " boundaries and
/// grouped into sections (a "Header (...): " segment or one of <see cref="SectionStarts"/> opens one); an operation gets every
/// segment that names it and, as section notes, the segments naming no operation that belong to it (<see cref="NoteOwners"/>:
/// a clause continuing one of its segments, or a sentence of a section that names it); a section that names no operation is a
/// tool-wide rule. Pure, Siemens-free; reads nothing from TIA Portal.
/// </summary>
internal static partial class PlcOperationReference
{
    internal const string ReferenceDocument =
        "https://github.com/Birger90/tia-portal-mcp-server/blob/main/docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md";

    /// <summary>Segments that open a section although they do not look like a header.</summary>
    private static readonly string[] SectionStarts =
    {
        "Reads run independently",
        "get_cpu_settings (optional plcName; issue #15",
        "expectedContentHash is the contentHash from a plc_read",
        "A same-table update_tag rename collision",
        "An export or read that TIA Portal refuses for a block",
        // K93: paragraphs added one after another without a capitalised header; each is its own section, so its notes
        // (e.g. the TLS fingerprint advice of get_cpu_certificates) are not attached to the operations before it.
        "Program analysis, computed from the block inventory",
        "create_instance_db (blockPath, instanceOfName",
        "get_umc_server_user (name, optional userKind",
        "get_umac (optional plcName; issue #12",
        "get_cpu_certificates (optional plcName; issue #14",
        "get_safety_validation (issue #25",
    };

    /// <summary>Segments that look like a header but continue the section before them.</summary>
    private static readonly string[] NotSectionStarts =
    {
        "A relationship is used only",
        "There is no dedicated F-compile",
        "OnlineControl (read-write and full)",
    };

    private static readonly Regex Header = new(@"^[A-Z](?:[^:;()]|\((?:[^()]|\([^()]*\))*\))*?: ", RegexOptions.CultureInvariant);

    private static readonly Regex Parentheses = new(@"\((?:[^()]|\([^()]*\))*\)", RegexOptions.CultureInvariant);

    private sealed record Section(IReadOnlyList<string> Segments, IReadOnlyList<IReadOnlyList<string>> Names);

    private static readonly Lazy<IReadOnlyList<string>> AllOperations = new(() =>
        PlcOperationCatalog.ReadOperationNames.Concat(PlcOperationCatalog.WriteOperationNames).ToArray());

    private static readonly Lazy<IReadOnlyList<Section>> ReadSections = new(() => Sections(ReadTool));

    private static readonly Lazy<IReadOnlyList<Section>> WriteSections = new(() => Sections(WriteTool));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> FieldDocs = new(() =>
        typeof(PlcOperationRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Name: property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                Description: property.GetCustomAttribute<DescriptionAttribute>()?.Description))
            .Where(field => field.Description is not null)
            .ToDictionary(field => field.Name, field => field.Description!, StringComparer.Ordinal));

    /// <summary>True for the plc_read operation this class answers.</summary>
    internal static bool Handles(string? operation) => operation == PlcOperationHelpMethods.DescribeOperations;

    /// <summary>The full documentation of one wire field, or null when it has none.</summary>
    internal static string? FieldDescription(string field) => FieldDocs.Value.GetValueOrDefault(field);

    internal static StructuredOperationItem Execute(PlcOperationRequest operation)
        => new(operation.OperationId, operation.Operation, OperationBatchStatus.Succeeded,
            CanonicalJson.ToElement(Describe(operation.OperationNames)), Failure: null, Omission: null, SkipReason: null,
            Array.Empty<string>());

    /// <summary>The described operations (validated names) or, for null, the index of every operation.</summary>
    internal static PlcOperationDescribeResult Describe(IReadOnlyList<string>? operationNames)
    {
        var readRules = ToolRules(ReadSections.Value);
        var writeRules = ToolRules(WriteSections.Value);
        if (operationNames is null)
        {
            var index = AllOperations.Value.Select(name =>
            {
                PlcOperationCatalog.TryGetFields(name, out var required, out var optional, out var isWrite);
                return new PlcOperationIndexEntry(name, isWrite ? "plc_write" : "plc_read", IndexFields(required, optional));
            }).ToArray();
            return new PlcOperationDescribeResult(null, index, readRules, writeRules, ReferenceDocument);
        }

        return new PlcOperationDescribeResult(operationNames.Select(DescribeOne).ToArray(), null, readRules, writeRules, ReferenceDocument);
    }

    internal static string IndexFields(IReadOnlyList<string> required, IReadOnlyList<string> optional)
        => (required.Count, optional.Count) switch
        {
            (0, 0) => "none",
            (_, 0) => string.Join(", ", required),
            (0, _) => "optional: " + string.Join(", ", optional),
            _ => string.Join(", ", required) + "; optional: " + string.Join(", ", optional),
        };

    /// <summary>One operation in full; the name must be a catalog operation.</summary>
    internal static PlcOperationDescription DescribeOne(string name)
    {
        if (!PlcOperationCatalog.TryGetFields(name, out var required, out var optional, out var isWrite))
            throw new ArgumentException($"'{name}' is no PLC operation.", nameof(name));
        var fields = required.Select(field => DescribeField(field, true, name))
            .Concat(optional.Select(field => DescribeField(field, false, name)))
            .ToArray();
        var reference = new List<string>();
        var notes = new List<string>();
        // A read is documented in the plc_read text, a write in the plc_write text; either text may name the other tool's
        // operations (a read named as a write's evidence source), which is context, not the operation's own rules.
        foreach (var section in isWrite ? WriteSections.Value : ReadSections.Value)
        {
            for (var i = 0; i < section.Segments.Count; i++)
                if (section.Names[i].Contains(name, StringComparer.Ordinal)) reference.Add(section.Segments[i]);
                else if (section.Names[i].Count == 0 && NoteOwners(section, i).Contains(name, StringComparer.Ordinal)) notes.Add(section.Segments[i]);
        }

        return new PlcOperationDescription(name, isWrite ? "plc_write" : "plc_read", required, optional, fields, reference, notes);
    }

    /// <summary>
    /// K93: the operations a segment that names none belongs to. A clause after "; " continues the clause before it, so it
    /// belongs to the operations that clause (or the first naming clause of the chain) names, e.g. "verified by re-export ..."
    /// after set_network_text. A new sentence (after ". ") states a rule of its whole section, e.g. "Every fail-safe item fires
    /// the acknowledge guard ...", and belongs to every operation the section names.
    /// </summary>
    private static IReadOnlyList<string> NoteOwners(Section section, int index)
    {
        for (var i = index - 1; i >= 0 && section.Segments[i].EndsWith(';'); i--)
            if (section.Names[i].Count > 0) return section.Names[i];
        return section.Names.SelectMany(names => names).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static PlcOperationFieldDescription DescribeField(string field, bool required, string operation)
    {
        var full = FieldDescription(field);
        if (full is null) return new(field, required, null);
        var trimmed = FieldDescriptionFor(full, operation);
        return new(field, required, trimmed, !string.Equals(trimmed, full, StringComparison.Ordinal));
    }

    /// <summary>Shorthands like add/update/delete_interface_member or get/set_prodiag_assignment, which name operations only in part.</summary>
    private static readonly Regex OperationShorthand = new(@"\b[a-z]+(?:/[a-z]+)*/[a-z]+_[a-z_]+", RegexOptions.CultureInvariant);

    /// <summary>Words by which a segment speaks of operations it does not name (every technology object operation except ...).</summary>
    private static readonly Regex GroupWords = new(@"\b(?:every|all|any|each|other|except|reads|writes)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// K93: a field description shared by many operations, reduced to what concerns <paramref name="operation"/>: its segments
    /// (split like the reference texts) that name the operation or name no operation. A segment that names only other
    /// operations is left out, unless a shorthand in it (a/b/c_name) expands to the operation or it speaks of a group of
    /// operations (every, all, except, ...), where leaving it out could drop a rule that applies.
    /// </summary>
    internal static string FieldDescriptionFor(string description, string operation)
    {
        var kept = Segments(description).Where(segment =>
        {
            var named = NamedOperations(segment);
            return named.Count == 0 || named.Contains(operation, StringComparer.Ordinal) || GroupWords.IsMatch(segment)
                || OperationShorthand.Matches(segment).Any(match => ExpandShorthand(match.Value).Contains(operation, StringComparer.Ordinal));
        }).ToArray();
        if (kept.Length == 0) return description;
        var text = string.Join(" ", kept);
        // A kept clause that ended a list with ";" is now the end of the text.
        return text.EndsWith(';') ? text[..^1] + "." : text;
    }

    /// <summary>add/update/delete_interface_member: add_interface_member, update_interface_member, delete_interface_member.</summary>
    internal static IReadOnlyList<string> ExpandShorthand(string shorthand)
    {
        var parts = shorthand.Split('/');
        var last = parts[^1];
        var separator = last.IndexOf('_');
        var rest = last[(separator + 1)..];
        return parts[..^1].Select(prefix => prefix + "_" + rest).Append(last).ToArray();
    }

    /// <summary>The segments of the sections that name no operation.</summary>
    private static IReadOnlyList<string> ToolRules(IReadOnlyList<Section> sections)
        => sections.Where(section => section.Names.All(names => names.Count == 0)).SelectMany(section => section.Segments).ToArray();

    private static IReadOnlyList<Section> Sections(string text)
    {
        var sections = new List<(List<string> Segments, List<IReadOnlyList<string>> Names)>();
        foreach (var segment in Segments(text))
        {
            if (sections.Count == 0 || OpensSection(segment)) sections.Add((new List<string>(), new List<IReadOnlyList<string>>()));
            sections[^1].Segments.Add(segment);
            sections[^1].Names.Add(NamedOperations(segment));
        }

        return sections.Select(section => new Section(section.Segments, section.Names)).ToArray();
    }

    private static bool OpensSection(string segment)
    {
        if (SectionStarts.Any(start => segment.StartsWith(start, StringComparison.Ordinal))) return true;
        if (NotSectionStarts.Any(start => segment.StartsWith(start, StringComparison.Ordinal))) return false;
        var header = Header.Match(segment);
        return header.Success && Parentheses.Replace(header.Value, "").Length <= 70;
    }

    private static IReadOnlyList<string> NamedOperations(string segment)
        => AllOperations.Value.Where(name => NamesOperation(segment, name)).ToArray();

    /// <summary>True when <paramref name="text"/> names <paramref name="operation"/> as a whole word (letters, digits and _).</summary>
    internal static bool NamesOperation(string text, string operation)
    {
        for (var at = text.IndexOf(operation, StringComparison.Ordinal); at >= 0; at = text.IndexOf(operation, at + 1, StringComparison.Ordinal))
        {
            var end = at + operation.Length;
            if ((at == 0 || !IsWordChar(text[at - 1])) && (end == text.Length || !IsWordChar(text[end]))) return true;
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    /// <summary>
    /// The text split at "; " and ". " outside parentheses, brackets, braces and double quotes ("e.g. " and "i.e. " do
    /// not split); each segment keeps its terminator.
    /// </summary>
    internal static IReadOnlyList<string> Segments(string text)
    {
        var segments = new List<string>();
        var depth = 0;
        var quoted = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') quoted = !quoted;
            else if (quoted) continue;
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && c is ';' or '.' && i + 1 < text.Length && text[i + 1] == ' '
                     && !(c == '.' && IsAbbreviation(text, i)))
            {
                Add(text[start..(i + 1)]);
                start = i + 2;
            }
        }

        Add(text[start..]);
        return segments;

        void Add(string segment)
        {
            var trimmed = segment.Trim();
            if (trimmed.Length > 0) segments.Add(trimmed);
        }
    }

    private static bool IsAbbreviation(string text, int dot)
        => dot >= 3 && (string.CompareOrdinal(text, dot - 3, "e.g", 0, 3) == 0 || string.CompareOrdinal(text, dot - 3, "i.e", 0, 3) == 0);

    /// <summary>The schema form of a field description (K91); see <see cref="Tools.SchemaDescriptions.FirstSentence"/>.</summary>
    internal static string? ShortDescription(string description, int limit) => Tools.SchemaDescriptions.FirstSentence(description, limit);
}
