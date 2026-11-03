namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// Worker methods of the technology object (TO) family. Each name is also the public <c>plc_read</c> /
/// <c>plc_write</c> operation name (like <c>list_tag_tables</c>). The lists feed <c>OperationPolicyCatalog</c>,
/// <c>ProjectCapabilityCatalog.OperationFor</c> and the worker dispatch from this one place, so they cannot drift.
/// Parameters travel in <c>WorkerRequest.TechnologyObject</c>.
/// </summary>
public static class TechnologyObjectMethods
{
    // plc_read, Observe: walks PlcSoftware.TechnologicalObjectGroup, reads parameters and hardware connections.
    public const string List = "list_technology_objects";
    public const string GetParameters = "get_technology_object_parameters";
    public const string GetHardwareConnection = "get_technology_object_hardware_connection";

    // plc_read, TemporaryExport: SimaticML through a temporary file, returned inline with a content hash.
    public const string GetContent = "get_technology_object_content";

    // plc_write, ProjectMutation: project changes, and the export that writes a file to a caller path.
    public const string Create = "create_technology_object";
    public const string Delete = "delete_technology_object";
    public const string Rename = "rename_technology_object";
    public const string CreateGroup = "create_technology_object_group";
    public const string DeleteGroup = "delete_technology_object_group";
    public const string RenameGroup = "rename_technology_object_group";
    public const string SetParameter = "set_technology_object_parameter";
    public const string ConnectHardware = "connect_technology_object_hardware";
    public const string DisconnectHardware = "disconnect_technology_object_hardware";
    public const string Export = "export_technology_object";
    public const string Import = "import_technology_object";
    public const string UpdateContent = "update_technology_object_content";

    /// <summary>Reads that never write a file (<c>Observe</c>).</summary>
    public static IReadOnlyList<string> Observe { get; } = Array.AsReadOnly(new[] { List, GetParameters, GetHardwareConnection });

    /// <summary>Reads that export through a temporary file (<c>TemporaryExport</c>).</summary>
    public static IReadOnlyList<string> TemporaryExport { get; } = Array.AsReadOnly(new[] { GetContent });

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(Observe.Concat(TemporaryExport).ToArray());

    /// <summary>Mutations (<c>ProjectMutation</c>); export writes outside TIA Portal, so it is writable-mode only like <c>export_io_map</c>.</summary>
    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[]
    {
        Create, Delete, Rename, CreateGroup, DeleteGroup, RenameGroup, SetParameter,
        ConnectHardware, DisconnectHardware, Export, Import, UpdateContent,
    });

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Read.Concat(Write).ToArray());

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method) || IsWrite(method);
}
