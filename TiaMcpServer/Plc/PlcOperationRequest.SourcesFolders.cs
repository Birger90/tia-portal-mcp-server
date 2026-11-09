using System.ComponentModel;
using TiaMcpServer.PlcOnline;

namespace TiaMcpServer.Plc;

/// <summary>
/// Request fields of the external-source, project-tree folder, file-export and accessible-device operations
/// (<see cref="TiaMcpServer.Contracts.Plc.PlcSourceFolderMethods"/>). The catalog restricts them per operation.
/// </summary>
public sealed partial class PlcOperationRequest
{
    // FilePath, DirectoryPath and Overwrite are shared with the alarm and technology object file operations and declared
    // once, with every description, in PlcOperationRequest.cs.

    [Description("External source name as list_external_sources lists it (case-sensitive match by PlcExternalSourceComposition.Find). Required by generate_blocks_from_source and delete_external_source; optional for import_external_source (default: the file name with its extension).")]
    public string? SourceName { get; set; }

    [Description("Optional software unit name: the external sources of that unit instead of the PLC's own (import_external_source, generate_blocks_from_source, delete_external_source), or the unit's tag table folders (rename_tag_table_group, create_tag_table_group, delete_tag_table_group). Omitted means the PLC itself. create_software_unit, delete_software_unit and rename_software_unit: the software unit to create, delete or rename (required).")]
    public string? UnitName { get; set; }

    [Description("generate_blocks_from_source: the GenerateBlockOption, exactly None (default; on an error TIA Portal throws and deletes what it generated) or KeepOnError (no exception; generated objects are kept even when they contain errors).")]
    public string? GenerateOption { get; set; }

    [Description("export_source_file: blocks to generate the source from, as blockPath values (PLC_1/Main or PLC_1/Blocks/Folder/Block). At least one of blockPaths and typePaths is required; all objects must be in one PLC and one software scope. generate_loadable: the blocks of the non-unit program to generate the loadable file for (1-200, one PLC; or unitNames).")]
    public IReadOnlyList<string>? BlockPaths { get; set; }

    [Description("export_source_file: PLC data types to generate the source from, as typePath values.")]
    public IReadOnlyList<string>? TypePaths { get; set; }

    [Description("CSV exports: field separator ',' (default), ';' or a tab character.")]
    public string? CsvDelimiter { get; set; }

    [Description("CSV exports: true writes cell values exactly as read. Omitted or false prefixes a single quote to a value starting with =, +, -, @, a tab or a carriage return so spreadsheet programs do not evaluate it as a formula.")]
    public bool? RawCells { get; set; }

    [Description("find_objects: name pattern, required; '*' matches any run of characters and '?' one character; matched case-insensitively against the whole object name.")]
    public string? Pattern { get; set; }

    [Description("find_objects: optional object kinds to search, a non-empty duplicate-free subset of software_unit, block, block_group, type, type_group, tag_table, tag_table_group, tag, user_constant (the PLC's own tag tables), watch_table, force_table, watch_table_group, external_source; omitted searches all of them.")]
    public IReadOnlyList<string>? Kinds { get; set; }

    [Description("find_objects: number of matches to skip (paging), 0 or more; default 0. get_technology_object_parameters: zero-based index of the first matching parameter returned (default 0); continue with the result's nextOffset. list_safety_blocks: number of F-blocks to skip per PLC (default 0; continue with nextOffset).")]
    public int? Offset { get; set; }

    [Description("find_objects: page size, 1 to 1000; default 100. The result's nextOffset continues the listing. get_technology_object_parameters: parameters per page, 1 to 2000, default 200. list_safety_blocks: F-blocks per PLC, 1 to 2000, default 200.")]
    public int? Limit { get; set; }

    [Description("scan_accessible_devices and get_station_fingerprint_data: exact connection mode name as plc_read get_online_state lists it (for example PN/IE). Required; there is no default.")]
    public string? Mode { get; set; }

    [Description("scan_accessible_devices and get_station_fingerprint_data: the PC interface { name, number } to scan or connect through, exactly as get_online_state lists it. Required; there is no default interface.")]
    public PlcOnlinePcInterfaceArgument? PcInterface { get; set; }
}
