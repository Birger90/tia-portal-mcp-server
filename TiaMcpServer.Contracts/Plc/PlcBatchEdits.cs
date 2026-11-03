// PlcBatchRenamePlan.Plan: origin BetterTIA/writer, src/TiaMcpServer/Siemens/BatchRename.cs (BatchRename.Plan), commit
// fc8179c9b59d364961f2255d07d05af893c32837, MIT License, Copyright (c) 2025 J.Heilingbrunner; see THIRD_PARTY_NOTICES.md.
// Adapted: no collision check against names outside the batch (TIA Portal decides, every step is read back), this fork's exceptions.
namespace TiaMcpServer.Contracts.Plc;

/// <summary>One rename by name of rename_tags / rename_objects.</summary>
public class PlcNameRenameInfo
{
    public string Name { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;
}

/// <summary>
/// Orders a batch of renames so that it can be carried out one setter call at a time, even when the new names are
/// currently held by other objects of the same batch (a swap, a rotation, a chain): an object whose new name is still
/// held by another object of the batch is parked under a temporary name first and gets its final name last. Pure; sees
/// names only. Names compare case-insensitively (TIA Portal's PLC namespaces do); a case-only rename is no conflict.
/// </summary>
public static class PlcBatchRenamePlan
{
    public const int MaxRenames = 200;
    public const string TemporaryPrefix = "zzTmp_";

    /// <summary>The setter calls to make, in order: (index into the batch, name to set).</summary>
    /// <exception cref="ArgumentException">A name appears twice, as an old name or as a new name.</exception>
    public static List<(int Index, string Name)> Plan(IReadOnlyList<string> oldNames, IReadOnlyList<string> newNames, string stamp)
    {
        if (oldNames.Count != newNames.Count) throw new ArgumentException("Every rename needs a name and a newName.");
        var comparer = StringComparer.OrdinalIgnoreCase;
        var twice = oldNames.GroupBy(n => n, comparer).FirstOrDefault(g => g.Count() > 1)?.Key
            ?? newNames.GroupBy(n => n, comparer).FirstOrDefault(g => g.Count() > 1)?.Key;
        if (twice is not null) throw new ArgumentException($"'{twice}' appears twice in the batch.");
        var moving = new HashSet<string>(oldNames, comparer);
        var steps = new List<(int Index, string Name)>();
        var parked = new List<int>();
        for (var i = 0; i < oldNames.Count; i++)
        {
            if (moving.Contains(newNames[i]) && !comparer.Equals(oldNames[i], newNames[i]))
            {
                steps.Add((i, $"{TemporaryPrefix}{stamp}_{i}"));
                parked.Add(i);
            }
        }

        steps.AddRange(Enumerable.Range(0, oldNames.Count)
            .Where(i => !parked.Contains(i) && !string.Equals(oldNames[i], newNames[i], StringComparison.Ordinal))
            .Select(i => (i, newNames[i])));
        steps.AddRange(parked.Select(i => (i, newNames[i])));
        return steps;
    }
}

/// <summary>One object of a batch rename as the worker resolved and renamed it.</summary>
public class PlcBatchRenameItemInfo
{
    /// <summary>tag, block, type or technologyObject.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Where the object is: the tag table folder and table of a tag, the block or type path, the TO group.</summary>
    public string Path { get; set; } = string.Empty;

    public string OldName { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;

    /// <summary>The name read back after the transaction (GetAttribute("Name")).</summary>
    public string? Observed { get; set; }
}

/// <summary>Result of rename_tags / rename_objects: committed in one ExclusiveAccess transaction.</summary>
public class PlcBatchRenameResultInfo
{
    public string Operation { get; set; } = string.Empty;

    public string PlcName { get; set; } = string.Empty;

    public List<PlcBatchRenameItemInfo> Items { get; set; } = new List<PlcBatchRenameItemInfo>();

    /// <summary>The setter calls made, temporary names included, in order.</summary>
    public List<string> Steps { get; set; } = new List<string>();

    public List<string> Messages { get; set; } = new List<string>();
}

/// <summary>Result of replace_tag_comments: every changed comment text, committed in one ExclusiveAccess transaction.</summary>
public class PlcTagCommentReplaceResultInfo
{
    public string Operation { get; set; } = PlcBlockEditMethods.ReplaceTagComments;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The tags in scope whose comment was read.</summary>
    public int TagsScanned { get; set; }

    /// <summary>Object is "folder/table/tag" (folder "/" or "/A/B"), Where "comment", Key the object, Culture the language.</summary>
    public List<Interface.PlcTextChangeInfo> Changes { get; set; } = new List<Interface.PlcTextChangeInfo>();

    public List<string> Messages { get; set; } = new List<string>();
}
