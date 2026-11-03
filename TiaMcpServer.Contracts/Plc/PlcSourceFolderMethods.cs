namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Operation and worker-method names of the external-source, project-tree folder, file-export and
/// accessible-device operations of <c>plc_read</c>/<c>plc_write</c> (adoption roadmap rows 7-10). Each
/// public operation uses its name as its worker method; the host computations (<see cref="HostReads"/>,
/// <see cref="HostWrites"/>) have no worker method and appear only in <c>OperationPolicyCatalog</c>.
/// These lists feed <c>OperationPolicyCatalog</c>, <c>ProjectCapabilityCatalog</c> and the worker dispatch.
/// </summary>
public static class PlcSourceFolderMethods
{
    // plc_read, worker, Observe: reads PlcExternalSourceComposition of the PLC and its software units.
    public const string ListExternalSources = "list_external_sources";

    // plc_read, worker, Observe: the multilingual Comment of every tag and user constant of the PLC's tag tables.
    public const string ReadTagComments = "read_tag_comments";

    // plc_read, worker, OnlineObserve: ConfigurationPcInterface.GetAccessibleDevices() performs live network
    // discovery on one explicitly named PC interface; it changes neither a device nor the project.
    public const string ScanAccessibleDevices = "scan_accessible_devices";

    // plc_read, host computation over existing reads (project tree, watch tables, external sources), Observe.
    public const string FindObjects = "find_objects";

    // plc_write, worker, ProjectMutation.
    public const string ImportExternalSource = "import_external_source";
    public const string GenerateBlocksFromSource = "generate_blocks_from_source";
    public const string DeleteExternalSource = "delete_external_source";
    public const string ExportSourceFile = "export_source_file";
    public const string RenameBlockGroup = "rename_block_group";
    public const string RenameTypeGroup = "rename_type_group";
    public const string RenameTagTableGroup = "rename_tag_table_group";
    public const string RenameWatchTableGroup = "rename_watch_table_group";
    public const string CreateTypeGroup = "create_type_group";
    public const string DeleteTypeGroup = "delete_type_group";
    public const string CreateTagTableGroup = "create_tag_table_group";
    public const string DeleteTagTableGroup = "delete_tag_table_group";
    public const string CreateSoftwareUnit = "create_software_unit";
    public const string DeleteSoftwareUnit = "delete_software_unit";
    public const string RenameSoftwareUnit = "rename_software_unit";

    // plc_write, host computation over existing reads that writes files outside TIA Portal (ProjectMutation).
    public const string ExportTagTablesCsv = "export_tag_tables_csv";
    public const string ExportWatchTablesCsv = "export_watch_tables_csv";
    public const string ExportForceTablesCsv = "export_force_tables_csv";
    public const string ExportBlocks = "export_blocks";

    /// <summary>The documented <c>GenerateBlockOption</c> member names generate_blocks_from_source accepts.</summary>
    public static IReadOnlyList<string> GenerateBlockOptions { get; } = Array.AsReadOnly(new[] { "None", "KeepOnError" });

    public static IReadOnlyList<string> ObserveWorkerReads { get; } = Array.AsReadOnly(new[] { ListExternalSources, ReadTagComments });

    public static IReadOnlyList<string> OnlineObserveWorkerReads { get; } = Array.AsReadOnly(new[] { ScanAccessibleDevices });

    public static IReadOnlyList<string> HostReads { get; } = Array.AsReadOnly(new[] { FindObjects });

    public static IReadOnlyList<string> WorkerWrites { get; } = Array.AsReadOnly(new[]
    {
        ImportExternalSource, GenerateBlocksFromSource, DeleteExternalSource, ExportSourceFile,
        RenameBlockGroup, RenameTypeGroup, RenameTagTableGroup, RenameWatchTableGroup,
        CreateTypeGroup, DeleteTypeGroup, CreateTagTableGroup, DeleteTagTableGroup,
        CreateSoftwareUnit, DeleteSoftwareUnit, RenameSoftwareUnit,
    });

    public static IReadOnlyList<string> HostWrites { get; } = Array.AsReadOnly(new[]
    {
        ExportTagTablesCsv, ExportWatchTablesCsv, ExportForceTablesCsv, ExportBlocks,
    });

    /// <summary>The group renames (generic <c>SetAttribute("Name")</c> after a <c>GetAttributeInfos</c> writability check).</summary>
    public static IReadOnlyList<string> GroupRenames { get; } = Array.AsReadOnly(new[]
    {
        RenameBlockGroup, RenameTypeGroup, RenameTagTableGroup, RenameWatchTableGroup,
    });

    /// <summary>Create and delete of PLC data type groups and tag table groups (tags-types.md §2 and its V21 API list).</summary>
    public static IReadOnlyList<string> GroupCreatesDeletes { get; } = Array.AsReadOnly(new[]
    {
        CreateTypeGroup, DeleteTypeGroup, CreateTagTableGroup, DeleteTagTableGroup,
    });

    /// <summary>Software unit create, delete and rename (software-units.md §2).</summary>
    public static IReadOnlyList<string> SoftwareUnitOperations { get; } = Array.AsReadOnly(new[]
    {
        CreateSoftwareUnit, DeleteSoftwareUnit, RenameSoftwareUnit,
    });

    /// <summary>The external-source operations that address a source by name.</summary>
    public static IReadOnlyList<string> SourceItemOperations { get; } = Array.AsReadOnly(new[]
    {
        ImportExternalSource, GenerateBlocksFromSource, DeleteExternalSource,
    });

    /// <summary>The operations that write a file (or files) at a caller path.</summary>
    public static IReadOnlyList<string> FileWrites { get; } = Array.AsReadOnly(new[]
    {
        ExportSourceFile, ExportTagTablesCsv, ExportWatchTablesCsv, ExportForceTablesCsv, ExportBlocks,
    });

    /// <summary>Online-path operations: standalone projects only, like the other online operations.</summary>
    public static IReadOnlyList<string> StandaloneOnlyOperations { get; } = Array.AsReadOnly(new[] { ScanAccessibleDevices });

    public static IReadOnlyList<string> AllReads { get; } =
        Array.AsReadOnly(ObserveWorkerReads.Concat(OnlineObserveWorkerReads).Concat(HostReads).ToArray());

    public static IReadOnlyList<string> AllWrites { get; } = Array.AsReadOnly(WorkerWrites.Concat(HostWrites).ToArray());

    public static bool IsWorkerMethod(string? method)
        => method is not null && (ObserveWorkerReads.Contains(method, StringComparer.Ordinal)
            || OnlineObserveWorkerReads.Contains(method, StringComparer.Ordinal)
            || WorkerWrites.Contains(method, StringComparer.Ordinal));

    public static bool IsRead(string? method) => method is not null && AllReads.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && AllWrites.Contains(method, StringComparer.Ordinal);
}

/// <summary>The plc_write effect target kinds of these operations (next to <see cref="PlcNameRules"/>' kinds).</summary>
public static class PlcSourceFolderKinds
{
    public const string ExternalSource = "ExternalSource";
    public const string TypeGroup = "TypeGroup";
    public const string TagTableGroup = "TagTableGroup";

    /// <summary>A software unit (<c>PlcUnit</c>) of a PLC.</summary>
    public const string SoftwareUnit = "SoftwareUnit";

    /// <summary>The same spelling the watch-table operations use for a watch-and-force table group.</summary>
    public const string WatchTableGroup = "WatchTableGroup";

    /// <summary>A file a CSV export or export_source_file writes.</summary>
    public const string File = "File";

    /// <summary>The directory export_blocks writes into.</summary>
    public const string Directory = "Directory";
}
