// Origin: BetterTIA/writer, src/TiaMcpServer/Siemens/SimaticMl.cs (region "member rename": the Symbol/Instance component
// walk of RenameMembers, the GRAPH action operand patterns Segment/Operand/PlainName), commit
// fc8179c9b59d364961f2255d07d05af893c32837, MIT License, Copyright (c) 2025 J.Heilingbrunner; see THIRD_PARTY_NOTICES.md.
// Adapted: limited to the renamed object's own document (what the TIA Portal editor updates), resolved against that
// document's interface only, swaps through temporary names on the existing interface edit model.
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>One member rename of rename_members.</summary>
public class PlcMemberRenameInfo
{
    /// <summary>The member's dotted path inside its section, e.g. Count or Settings.Limits.Max.</summary>
    public string MemberPath { get; set; } = string.Empty;

    /// <summary>The new name of the last path segment.</summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>Optional section (Input, Output, InOut, Static, Temp, Constant); omitted, the one section holding the path is used.</summary>
    public string? Section { get; set; }
}

/// <summary>
/// rename_members on one block or PLC data type, Siemens-free: every rename is applied to the interface with the existing
/// edit model (<see cref="SimaticMlInterfaceDocument.Apply"/>), through temporary names so swaps and rotations work, and
/// the object's own accesses are updated the way the TIA Portal editor updates them when a member is renamed: every
/// <c>Access Scope="LocalVariable"</c> symbol and every <c>Instance Scope="LocalVariable"</c> whose leading components
/// name a renamed member (case-insensitive), in LAD, FBD, STL, SCL and GRAPH networks alike, and the local operands
/// (<c>#name</c>) that GRAPH actions keep as text. Accesses in other blocks are never touched.
/// </summary>
public static class SimaticMlMemberRenames
{
    public const int MaxRenames = 100;

    public sealed class Outcome
    {
        public List<PlcInterfaceFieldChange> Changes { get; } = new();

        public List<PlcInterfaceExpectation> Expectations { get; } = new();

        public int UpdatedAccesses { get; set; }

        public List<string> Warnings { get; } = new();
    }

    private sealed class Planned
    {
        public Planned(string section, PlcMemberPath path, string newName)
        {
            Section = section;
            Path = path;
            NewName = newName;
        }

        public string Section { get; }

        public PlcMemberPath Path { get; }

        public string NewName { get; }

        public string Temporary { get; set; } = string.Empty;
    }

    // A local operand as GRAPH writes it into an action: plain text, not an <Access> element (writer's patterns).
    private static readonly Regex Segment = new(@"(""[^""]+""|[A-Za-z_]\w*)(\[[^\]]*\])?", RegexOptions.CultureInvariant);
    private static readonly Regex Operand = new(@"(?<![\w""#])#" + Segment + @"(\." + Segment + @")*", RegexOptions.CultureInvariant);
    private static readonly Regex PlainName = new(@"^[A-Za-z_]\w*$", RegexOptions.CultureInvariant);

    /// <exception cref="PlcInterfaceEditException">A member is missing, ambiguous, a name is taken or the request is inconsistent.</exception>
    public static Outcome Apply(SimaticMlInterfaceDocument document, IReadOnlyList<PlcMemberRenameInfo> renames, bool isType)
    {
        if (renames.Count is 0 or > MaxRenames)
            throw PlcInterfaceEditException.Invalid($"renames must hold 1 to {MaxRenames} {{ memberPath, newName }} entries.");
        var sections = document.ReadSections();
        var planned = new List<Planned>();
        foreach (var rename in renames)
        {
            PlcMemberPath path;
            try
            {
                path = PlcMemberPath.Parse(rename.MemberPath, allowIndices: false);
            }
            catch (ArgumentException exception)
            {
                throw PlcInterfaceEditException.Invalid(exception.Message);
            }

            var section = isType ? "None" : rename.Section ?? ResolveSection(sections, path);
            planned.Add(new Planned(section, path, rename.NewName));
        }

        for (var i = 0; i < planned.Count; i++)
        {
            for (var j = 0; j < planned.Count; j++)
            {
                if (i == j) continue;
                if (planned[i].Section.Equals(planned[j].Section, StringComparison.OrdinalIgnoreCase) && Same(planned[i].Path, planned[j].Path))
                    throw PlcInterfaceEditException.Invalid($"'{planned[i].Path}' is renamed twice.");
                if (IsPrefix(planned[i].Path, planned[j].Path))
                    throw PlcInterfaceEditException.Invalid($"'{planned[i].Path}' and its member '{planned[j].Path}' are renamed in one item; rename them in two items.");
            }
        }

        // Member names are unique across the sections of a block: a top-level new name held in another section by a
        // member that this item does not rename away is refused (the edit model checks the same parent only).
        var leaving = new HashSet<string>(planned.Where(p => p.Path.Segments.Count == 1).Select(p => p.Path.Last.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var rename in planned.Where(p => p.Path.Segments.Count == 1))
        {
            var holder = sections.FirstOrDefault(s => !s.Name.Equals(rename.Section, StringComparison.OrdinalIgnoreCase)
                && s.Members.Any(m => m.Name.Equals(rename.NewName, StringComparison.OrdinalIgnoreCase) && !leaving.Contains(m.Name)));
            if (holder is not null)
                throw PlcInterfaceEditException.Invalid($"'{rename.NewName}' already exists in section {holder.Name}.");
        }

        var outcome = new Outcome();
        var moving = planned.Where(p => !string.Equals(p.Path.Last.Name, p.NewName, StringComparison.Ordinal)).ToList();
        var stamp = Guid.NewGuid().ToString("N").Substring(0, 8);
        // Phase 1 parks every renamed member under a temporary name, phase 2 gives it its final name: a swap, a rotation or
        // a chain never collides with itself, and a name held by a member outside the item is refused by the model.
        for (var i = 0; i < moving.Count; i++)
        {
            moving[i].Temporary = $"zzTmp_{stamp}_{i}";
            document.Apply(new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Update, Section = moving[i].Section, Path = moving[i].Path, NewName = moving[i].Temporary });
        }

        foreach (var rename in moving)
        {
            var parked = rename.Path.WithLastName(rename.Temporary);
            document.Apply(new PlcInterfaceEditRequest { Kind = PlcInterfaceEditKind.Update, Section = rename.Section, Path = parked, NewName = rename.NewName });
            outcome.Changes.Add(new PlcInterfaceFieldChange($"name {rename.Section}.{rename.Path}", rename.Path.Last.Name, rename.NewName));
            var renamed = rename.Path.WithLastName(rename.NewName);
            outcome.Expectations.Add(new PlcInterfaceExpectation
            {
                Section = rename.Section,
                Path = renamed.ToString(),
                Exists = true,
                NewName = rename.NewName,
                // The old path must be gone, unless the rename is case-only or another rename of the item takes it over (a swap).
                FormerPath = string.Equals(rename.Path.Last.Name, rename.NewName, StringComparison.OrdinalIgnoreCase)
                    || moving.Any(other => other != rename && other.Section.Equals(rename.Section, StringComparison.OrdinalIgnoreCase)
                        && Same(other.Path.WithLastName(other.NewName), rename.Path))
                    ? null : rename.Path.ToString(),
            });
        }

        if (!isType) outcome.UpdatedAccesses = RenameAccesses(document.Document, moving, outcome.Warnings);
        if (outcome.UpdatedAccesses > 0)
            outcome.Changes.Add(new PlcInterfaceFieldChange("localAccesses", null, $"{outcome.UpdatedAccesses} access(es) in the block's own code"));
        return outcome;
    }

    private static string ResolveSection(IReadOnlyList<PlcInterfaceSectionInfo> sections, PlcMemberPath path)
    {
        var holders = sections.Where(s => PlcInterfaceExpectation.Find(s.Members, path.ToString()) is not null).ToList();
        return holders.Count switch
        {
            1 => holders[0].Name,
            0 => throw PlcInterfaceEditException.NotFound($"No section holds the member '{path}'."),
            _ => throw PlcInterfaceEditException.Invalid($"The member '{path}' exists in {holders.Count} sections ({string.Join(", ", holders.Select(h => h.Name))}); give section."),
        };
    }

    private static int RenameAccesses(XDocument document, IReadOnlyList<Planned> renames, List<string> warnings)
    {
        if (renames.Count == 0) return 0;
        var count = 0;
        foreach (var symbol in document.Descendants().Where(e => e.Name.LocalName is "Symbol" or "Instance").ToList())
        {
            var scope = (string?)(symbol.Name.LocalName == "Symbol" ? symbol.Parent : symbol)?.Attribute("Scope");
            if (scope != "LocalVariable") continue;
            var components = symbol.Elements().Where(e => e.Name.LocalName == "Component").ToList();
            // Matched against the names before this access is touched, so a swap does not rename twice.
            var original = components.Select(c => (string?)c.Attribute("Name")).ToList();
            var changed = false;
            foreach (var rename in renames)
            {
                var segments = rename.Path.Segments;
                if (components.Count < segments.Count) continue;
                var matches = true;
                for (var k = 0; k < segments.Count && matches; k++)
                    matches = string.Equals(original[k], segments[k].Name, StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
                components[segments.Count - 1].SetAttributeValue("Name", rename.NewName);
                changed = true;
            }

            if (changed) count++;
        }

        foreach (var token in document.Descendants().Where(e => e.Name.LocalName == "Token" && e.Parent?.Name.LocalName == "Action").ToList())
        {
            var text = (string?)token.Attribute("Text") ?? string.Empty;
            var replaced = Operand.Replace(text, match =>
            {
                var parts = Segment.Matches(match.Value.Substring(1)).Cast<Match>().ToList();
                var names = parts.Select(p => p.Groups[1].Value.Trim('"')).ToList();
                var rebuilt = names.ToList();
                foreach (var rename in renames)
                {
                    var segments = rename.Path.Segments;
                    if (names.Count < segments.Count) continue;
                    if (Enumerable.Range(0, segments.Count).All(k => string.Equals(names[k], segments[k].Name, StringComparison.OrdinalIgnoreCase)))
                        rebuilt[segments.Count - 1] = rename.NewName;
                }

                if (rebuilt.SequenceEqual(names, StringComparer.Ordinal)) return match.Value;
                count++;
                return "#" + string.Join(".", rebuilt.Select((name, i) =>
                {
                    var quoted = parts[i].Groups[1].Value.StartsWith("\"", StringComparison.Ordinal) || !PlainName.IsMatch(name);
                    return (quoted ? $"\"{name}\"" : name) + parts[i].Groups[2].Value;
                }));
            });
            if (replaced != text) token.SetAttributeValue("Text", replaced);
        }

        // Accesses written as plain text elsewhere (comments) are not code and stay; a mention there is only reported.
        var mentioned = renames.Where(r => document.Descendants().Any(e => e.Name.LocalName == "Text" && e.Value.IndexOf("#" + r.Path.Segments[0].Name, StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(r => r.Path.ToString()).ToList();
        if (mentioned.Count > 0)
            warnings.Add($"Comments or titles of the block mention #{string.Join(", #", mentioned)}; texts are not code and were left unchanged.");
        return count;
    }

    private static bool Same(PlcMemberPath a, PlcMemberPath b)
        => a.Segments.Count == b.Segments.Count && a.Segments.Select(s => s.Name).SequenceEqual(b.Segments.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);

    private static bool IsPrefix(PlcMemberPath parent, PlcMemberPath child)
        => parent.Segments.Count < child.Segments.Count
            && parent.Segments.Select(s => s.Name).SequenceEqual(child.Segments.Take(parent.Segments.Count).Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
}
