namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Result of export_tag_table and import_tag_table: the tag table(s) and the SimaticML file. For an export the file is
/// the caller's target, re-read after it was placed; for an import it is the private copy that was imported (the
/// caller's file is never handed to TIA Portal, so a change after the hash cannot reach the project).
/// </summary>
public class PlcTagTableFileResultInfo
{
    public string Operation { get; set; } = PlcBlockEditMethods.ExportTagTable;

    public string PlcName { get; set; } = string.Empty;

    /// <summary>The tag table folder ("/" for the PLC's root folder).</summary>
    public string FolderPath { get; set; } = "/";

    /// <summary>The exported table, or the imported tables as the import's return value or the folder's new names show them.</summary>
    public List<string> Tables { get; set; } = new List<string>();

    /// <summary>The caller's path as the worker resolved it.</summary>
    public string FilePath { get; set; } = string.Empty;

    public long FileBytes { get; set; }

    /// <summary>Lowercase hex SHA-256 of the written file (export) or of the imported copy (import).</summary>
    public string FileSha256 { get; set; } = string.Empty;

    /// <summary>export: an existing file was replaced; import: <c>ImportOptions.Override</c> was used.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Tags and user constants of <see cref="Tables"/> after the export or import.</summary>
    public int TagCount { get; set; }

    public int UserConstantCount { get; set; }

    public List<string> Messages { get; set; } = new List<string>();
}

/// <summary>
/// The Siemens-free decision which tables an import_tag_table created or replaced, shared by the worker and the FakeWorker.
/// The reference states no return type for <c>PlcTagTableComposition.Import</c>, so the names come from the return value
/// when it is a table or tables, otherwise from the folder's names before and after; a caller <c>tableName</c> must be
/// in the folder afterwards.
/// </summary>
public static class PlcTagTableImportCheck
{
    public static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    /// <summary>The imported names, or a postcondition failure message (the import ran, the project may have changed).</summary>
    public static (List<string> Tables, string? Failure, string? Warning) Imported(
        IReadOnlyList<string>? returned, IReadOnlyCollection<string> before, IReadOnlyCollection<string> after, string? expected, bool overwrite, string folder)
    {
        if (expected is not null && !after.Contains(expected, Names))
            return (new List<string>(), $"The import reported no error but tag table '{expected}' is not in '{folder}'. The project may have changed; inspect it with plc_read list_tag_tables.", null);
        if (returned is { Count: > 0 })
        {
            var missing = returned.Where(name => !after.Contains(name, Names)).ToList();
            if (missing.Count > 0)
                return (new List<string>(), $"The import returned [{string.Join(", ", returned.Select(n => $"'{n}'"))}], but {string.Join(", ", missing.Select(n => $"'{n}'"))} is not in '{folder}'. The project may have changed; inspect it with plc_read list_tag_tables.", null);
            if (expected is not null && !returned.Contains(expected, Names))
                return (new List<string>(), $"The import returned [{string.Join(", ", returned.Select(n => $"'{n}'"))}], not tag table '{expected}'. The project may have changed; inspect it with plc_read list_tag_tables.", null);
            return (returned.ToList(), null, null);
        }

        var created = after.Where(name => !before.Contains(name, Names)).ToList();
        if (expected is not null)
        {
            if (!overwrite && !created.Contains(expected, Names))
                return (new List<string>(), $"Tag table '{expected}' was already in '{folder}' before the import without overwrite. The project may have changed; inspect it with plc_read list_tag_tables.", null);
            return (new List<string> { after.First(name => Names.Equals(name, expected)) }, null, null);
        }

        if (created.Count > 0) return (created, null, null);
        return overwrite
            ? (new List<string>(), null, $"The import returned no table and created no new name in '{folder}': with overwrite it replaced an existing table that is not known; pass tableName to have it checked.")
            : (new List<string>(), $"The import reported no error but no new tag table appeared in '{folder}'. Inspect it with plc_read list_tag_tables.", null);
    }
}
