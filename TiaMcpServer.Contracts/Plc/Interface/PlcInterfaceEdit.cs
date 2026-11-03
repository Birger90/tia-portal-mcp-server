using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Interface;

public enum PlcInterfaceEditKind
{
    Add,
    Update,
    Delete,
}

/// <summary>One requested interface member change, validated against the document it is applied to.</summary>
public sealed class PlcInterfaceEditRequest
{
    public PlcInterfaceEditKind Kind { get; set; }

    /// <summary>Section name; PLC data types use "None".</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>The member to change, or for Add the full path of the new member.</summary>
    public PlcMemberPath Path { get; set; } = PlcMemberPath.FromNames(new[] { "_" });

    public string? NewName { get; set; }

    public string? DataType { get; set; }

    public string? StartValue { get; set; }

    public bool ClearStartValue { get; set; }

    /// <summary>Comment text for <see cref="CommentCulture"/>; an empty text removes that culture's entry.</summary>
    public string? Comment { get; set; }

    public string? CommentCulture { get; set; }

    /// <summary>Add: the sibling the new member is inserted before; null appends after the last sibling.</summary>
    public string? InsertBefore { get; set; }
}

/// <summary>One field of a planned interface change: current and requested text (null = absent).</summary>
public sealed class PlcInterfaceFieldChange
{
    public PlcInterfaceFieldChange(string field, string? current, string? requested)
    {
        Field = field;
        Current = current;
        Requested = requested;
    }

    public string Field { get; }

    public string? Current { get; }

    public string? Requested { get; }
}

/// <summary>What one applied edit changed, and what a re-read must show afterwards.</summary>
public sealed class PlcInterfaceEditOutcome
{
    public PlcInterfaceEditOutcome(IReadOnlyList<PlcInterfaceFieldChange> changes, PlcInterfaceExpectation expectation)
    {
        Changes = changes;
        Expectation = expectation;
    }

    public IReadOnlyList<PlcInterfaceFieldChange> Changes { get; }

    public PlcInterfaceExpectation Expectation { get; }
}

/// <summary>
/// A refused edit: <see cref="Category"/> is <c>target_not_found</c> (section or member missing),
/// <c>target_kind_unsupported</c> (the document or member is outside the editable model) or
/// <c>validation_error</c>.
/// </summary>
public sealed class PlcInterfaceEditException : Exception
{
    public PlcInterfaceEditException(string category, string message) : base(message) => Category = category;

    public string Category { get; }

    internal static PlcInterfaceEditException NotFound(string message) => new(WorkerFailureCategories.TargetNotFound, message);

    internal static PlcInterfaceEditException Unsupported(string message) => new(WorkerFailureCategories.TargetKindUnsupported, message);

    internal static PlcInterfaceEditException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}

/// <summary>The member state a re-read must show after an edit; compared against a fresh export.</summary>
public sealed class PlcInterfaceExpectation
{
    public string Section { get; set; } = string.Empty;

    /// <summary>The member's path after the edit (the new name for a rename).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>For a rename, the old path that must be gone.</summary>
    public string? FormerPath { get; set; }

    /// <summary>For a rename, the new name the member must carry exactly (ordinal; a case-only rename included).</summary>
    public string? NewName { get; set; }

    public bool Exists { get; set; }

    public string? DataType { get; set; }

    public bool CheckStartValue { get; set; }

    public string? StartValue { get; set; }

    public string? CommentCulture { get; set; }

    /// <summary>The expected comment text; null means that culture has no entry.</summary>
    public string? Comment { get; set; }

    public PlcInterfaceCheck Check(IReadOnlyList<PlcInterfaceSectionInfo> sections)
    {
        var expected = Describe();
        var section = sections.FirstOrDefault(s => string.Equals(s.Name, Section, StringComparison.OrdinalIgnoreCase));
        var member = section is null ? null : Find(section.Members, Path);
        if (FormerPath is not null && section is not null && Find(section.Members, FormerPath) is not null)
            return new(false, expected, $"{Section}.{FormerPath} still exists");
        if (!Exists)
            return member is null ? new(true, expected, $"{Section}.{Path} absent") : new(false, expected, $"{Section}.{Path} still exists");
        if (member is null) return new(false, expected, $"{Section}.{Path} absent");

        var actual = Describe(member);
        var ok = (NewName is null || string.Equals(member.Name, NewName, StringComparison.Ordinal))
            && (DataType is null || Same(DataType, member.DataType))
            && (!CheckStartValue || Same(StartValue, member.StartValue))
            && (CommentCulture is null || Same(Comment, member.Comments.FirstOrDefault(c => string.Equals(c.Culture, CommentCulture, StringComparison.OrdinalIgnoreCase))?.Text));
        return new(ok, expected, actual);
    }

    private string Describe()
    {
        if (!Exists) return $"{Section}.{Path} absent";
        var parts = new List<string> { $"{Section}.{Path}" };
        if (NewName is not null) parts.Add($"name={NewName}");
        if (DataType is not null) parts.Add($"dataType={DataType}");
        if (CheckStartValue) parts.Add($"startValue={StartValue ?? "(none)"}");
        if (CommentCulture is not null) parts.Add($"comment[{CommentCulture}]={Comment ?? "(none)"}");
        return string.Join(" ", parts);
    }

    private string Describe(PlcInterfaceMemberInfo member)
    {
        var parts = new List<string> { $"{Section}.{member.Path}", $"dataType={member.DataType ?? "(none)"}" };
        if (NewName is not null) parts.Add($"name={member.Name}");
        if (CheckStartValue) parts.Add($"startValue={member.StartValue ?? "(none)"}");
        if (CommentCulture is not null)
            parts.Add($"comment[{CommentCulture}]={member.Comments.FirstOrDefault(c => string.Equals(c.Culture, CommentCulture, StringComparison.OrdinalIgnoreCase))?.Text ?? "(none)"}");
        return string.Join(" ", parts);
    }

    /// <summary>Literal text compared without case and surrounding blanks: Portal may normalize the case of a literal.</summary>
    private static bool Same(string? expected, string? actual)
        => string.Equals(expected?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

    public static PlcInterfaceMemberInfo? Find(IEnumerable<PlcInterfaceMemberInfo> members, string path)
    {
        PlcMemberPath parsed;
        try
        {
            parsed = PlcMemberPath.Parse(path, allowIndices: false);
        }
        catch (ArgumentException)
        {
            return null;
        }

        PlcInterfaceMemberInfo? current = null;
        var level = members;
        foreach (var segment in parsed.Segments)
        {
            current = level.FirstOrDefault(m => string.Equals(m.Name, segment.Name, StringComparison.Ordinal))
                ?? level.FirstOrDefault(m => string.Equals(m.Name, segment.Name, StringComparison.OrdinalIgnoreCase));
            if (current is null) return null;
            level = current.Members;
        }

        return current;
    }
}

public sealed class PlcInterfaceCheck
{
    public PlcInterfaceCheck(bool success, string expected, string actual)
    {
        Success = success;
        Expected = expected;
        Actual = actual;
    }

    public bool Success { get; }

    public string Expected { get; }

    public string Actual { get; }
}
