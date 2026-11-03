namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// Worker methods of the granular PLC block editing family (plc_read/plc_write). The names are
/// also the public plc_read/plc_write operation names. They feed <c>OperationPolicyCatalog</c>,
/// <c>ProjectCapabilityCatalog</c> and the worker dispatch from this one list.
/// </summary>
public static class PlcBlockEditMethods
{
    public const string GetBlockInterface = "get_block_interface";
    public const string GetTypeInterface = "get_type_interface";
    public const string GetDbValues = "get_db_values";

    public const string UpdateDbMember = "update_db_member";
    public const string CreateInstanceDb = "create_instance_db";
    public const string RenameBlock = "rename_block";
    public const string RenumberBlock = "renumber_block";
    public const string CopyBlock = "copy_block";
    public const string MoveBlock = "move_block";
    public const string MoveTagTable = "move_tag_table";

    /// <summary>The stored snapshot of a DB's actual values (<c>InterfaceSnapshot.Export</c>; adopted from BetterTIA/watch watch_db).</summary>
    public const string GetDbSnapshot = "get_db_snapshot";

    /// <summary>Takes a snapshot of a DB's actual values from the online PLC (<c>ValueService.CreateSnapshot</c>) and returns it.</summary>
    public const string CreateDbSnapshot = "create_db_snapshot";

    /// <summary>Batch renames by name with swaps in one transaction (adopted from BetterTIA/writer RenameTags / RenameObjects).</summary>
    public const string RenameTags = "rename_tags";
    public const string RenameObjects = "rename_objects";

    /// <summary>find/replace pairs over the multilingual comments of PLC tags (adopted from BetterTIA/writer ReplaceTagComments).</summary>
    public const string ReplaceTagComments = "replace_tag_comments";

    /// <summary>Network title/comment edit and find/replace, planned host-side on the exported SimaticML (adopted from BetterTIA/writer).</summary>
    public const string SetNetworkText = "set_network_text";
    public const string ReplaceNetworkText = "replace_network_text";

    /// <summary>Renames several interface members of one block or PLC data type with the block's own accesses (adopted from BetterTIA/writer RenameMembers).</summary>
    public const string RenameMembers = "rename_members";

    /// <summary>
    /// A tag table as a SimaticML file at a caller path (<c>PlcTagTable.Export(FileInfo, ExportOptions)</c>) and back
    /// (<c>PlcTagTableComposition.Import(FileInfo, ImportOptions)</c>); both documented by the Siemens tags-and-tagtables skill.
    /// </summary>
    public const string ExportTagTable = "export_tag_table";
    public const string ImportTagTable = "import_tag_table";

    /// <summary>
    /// The OPC UA data of one PLC as an XML file at a caller path (<c>OpcUaExportProvider.Export(DeviceItem, FileInfo)</c>,
    /// found through <c>Project.HwUtilities</c>, bound at runtime; see <see cref="PlcOpcUaExport"/>).
    /// </summary>
    public const string ExportOpcUaXml = "export_opcua_xml";

    public const string AddInterfaceMember = "add_interface_member";
    public const string UpdateInterfaceMember = "update_interface_member";
    public const string DeleteInterfaceMember = "delete_interface_member";

    /// <summary>Worker reads; they export temporary SimaticML documents and never mutate.</summary>
    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[]
    {
        GetBlockInterface, GetTypeInterface, GetDbValues, GetDbSnapshot,
    });

    /// <summary>Worker mutations, each one worker method.</summary>
    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[]
    {
        UpdateDbMember, CreateInstanceDb, RenameBlock, RenumberBlock, CopyBlock, MoveBlock, MoveTagTable,
        CreateDbSnapshot, RenameTags, RenameObjects, ReplaceTagComments, ExportTagTable, ImportTagTable,
        SimaticSd.SimaticSdMethods.CreateBlockFromDocument, ExportOpcUaXml,
    });

    /// <summary>The worker writes that change several objects by name in one ExclusiveAccess transaction.</summary>
    public static IReadOnlyList<string> BatchWrites { get; } = Array.AsReadOnly(new[]
    {
        RenameTags, RenameObjects, ReplaceTagComments,
    });

    /// <summary>The worker writes that move one tag table between the project and a SimaticML file at a caller path.</summary>
    public static IReadOnlyList<string> TagTableFiles { get; } = Array.AsReadOnly(new[]
    {
        ExportTagTable, ImportTagTable,
    });

    /// <summary>The worker writes that take a caller file path (<c>filePath</c>, <c>overwrite</c>): the tag table files and the OPC UA export.</summary>
    public static IReadOnlyList<string> CallerFileWrites { get; } = Array.AsReadOnly(new[]
    {
        ExportTagTable, ImportTagTable, ExportOpcUaXml,
    });

    /// <summary>
    /// Public plc_write operations the host composes from a planned SimaticML edit; they reach the
    /// worker as <c>update_block_logic</c> / <c>update_type_content</c> and have no worker method.
    /// </summary>
    public static IReadOnlyList<string> InterfaceEdits { get; } = Array.AsReadOnly(new[]
    {
        AddInterfaceMember, UpdateInterfaceMember, DeleteInterfaceMember,
    });

    /// <summary>
    /// Public plc_write operations the host composes from a planned SimaticML edit outside a single interface member
    /// (network texts, several member renames with the block's own accesses); like <see cref="InterfaceEdits"/> they reach the worker as <c>update_block_logic</c> /
    /// <c>update_type_content</c> and have no worker method.
    /// </summary>
    public static IReadOnlyList<string> DocumentEdits { get; } = Array.AsReadOnly(new[]
    {
        SetNetworkText, ReplaceNetworkText, RenameMembers,
    });

    /// <summary>
    /// The SIMATIC SD network edits (insert_network, replace_network, delete_network): planned host-side on the sd export and
    /// imported through update_block_logic format sd; no worker method.
    /// </summary>
    public static IReadOnlyList<string> NetworkEdits => SimaticSd.SimaticSdMethods.NetworkEdits;

    /// <summary>Every host-planned document edit: <see cref="InterfaceEdits"/>, <see cref="DocumentEdits"/> and <see cref="NetworkEdits"/>.</summary>
    public static IReadOnlyList<string> PlannedEdits { get; } = Array.AsReadOnly(InterfaceEdits.Concat(DocumentEdits).Concat(SimaticSd.SimaticSdMethods.NetworkEdits).ToArray());

    public static bool IsWorkerMethod(string method)
        => Read.Contains(method, StringComparer.Ordinal) || Write.Contains(method, StringComparer.Ordinal);
}
