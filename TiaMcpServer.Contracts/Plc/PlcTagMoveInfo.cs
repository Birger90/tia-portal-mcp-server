namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// The tag move worker method. Openness V21 documents no member that moves a <c>PlcTag</c> between
/// tag tables, so <c>move_tag</c> is a guarded re-create: create a temporary tag in the target table,
/// copy the documented attributes, delete the source, rename the temporary tag and verify.
/// </summary>
public static class PlcTagMoveMethods
{
    /// <summary>Also the public <c>plc_write</c> operation name (<c>ProjectMutation</c>).</summary>
    public const string MoveTag = "move_tag";

    /// <summary>Public <c>plc_read</c> operations computed by the host from existing reads (<c>Observe</c>); no worker method of their own.</summary>
    public const string ReadAddressOccupancy = "read_address_occupancy";

    public const string FindFreeAddress = "find_free_address";

    /// <summary>Prefix of the temporary tag name; a random suffix keeps it unique.</summary>
    public const string TemporaryNamePrefix = "__mcp_move_";
}

/// <summary>Worker request of <see cref="PlcTagMoveMethods.MoveTag"/>.</summary>
public sealed class PlcTagMoveRequestInfo
{
    public string? PlcName { get; set; }

    public string TableName { get; set; } = string.Empty;

    public string? FolderPath { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TargetTableName { get; set; } = string.Empty;

    public string? TargetFolderPath { get; set; }

    /// <summary>The temporary name, chosen by the host so a failure message can name it.</summary>
    public string TemporaryName { get; set; } = string.Empty;
}

/// <summary>Verified outcome of a completed tag move.</summary>
public sealed class PlcTagMoveResultInfo
{
    public string PlcName { get; set; } = string.Empty;

    public string TagName { get; set; } = string.Empty;

    public string SourceTableName { get; set; } = string.Empty;

    public string SourceFolderPath { get; set; } = "/";

    public string TargetTableName { get; set; } = string.Empty;

    public string TargetFolderPath { get; set; } = "/";

    public string DataType { get; set; } = string.Empty;

    public string LogicalAddress { get; set; } = string.Empty;

    public bool? ExternalAccessible { get; set; }

    public bool? ExternalVisible { get; set; }

    public bool? ExternalWritable { get; set; }

    /// <summary>The temporary name used between create and rename.</summary>
    public string TemporaryName { get; set; } = string.Empty;

    /// <summary>Each step that ran, in order.</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>What the re-create could not carry over (the documented attribute set is copied; nothing else).</summary>
    public List<string> NotCopied { get; set; } = new();
}
