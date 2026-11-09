using System.ComponentModel;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Plc;

/// <summary>Strict request shape for one dedicated PLC operation; the catalog restricts fields per operation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed partial class PlcOperationRequest : IOperationBatchItem
{
    [Description("Client-supplied unique identifier for this PLC operation; returned results are keyed by it.")]
    public string OperationId { get; set; } = string.Empty;

    [Description("PLC operation to run. Reads (plc_read): get_block_content, get_type_content, list_tag_tables, list_download_targets, read_address_occupancy, find_free_address, compare_to_online, compare_software, compare_hardware, compare_hardware_to_online, get_station_fingerprint_data, get_online_state, list_station_upload_interfaces, list_watch_and_force_tables, read_watch_table, read_force_table, get_watch_table_content, get_force_table_content, get_block_interface, get_type_interface, get_db_values, get_db_snapshot, get_call_tree, find_call_paths, find_unused_blocks, find_orphaned_instance_dbs, analyze_change_impact, list_external_sources, read_tag_comments, scan_accessible_devices, find_objects, list_text_lists, read_text_list_workbook, read_alarm_text_workbook, read_supervision_workbook, get_prodiag_fb, get_prodiag_assignment, list_technology_objects, get_technology_object_parameters, get_technology_object_hardware_connection, get_technology_object_content, get_plc_security, get_safety_administration, list_safety_blocks, list_networks, get_plc_checksums, get_fingerprints, get_block_protection, get_project_languages, get_cpu_settings, get_umac, get_umc_server_user, check_umc_consistency, get_markings, get_mark_state, get_object_identifier, find_object_by_identifier, get_creation_infos, get_linked_tags, get_cpu_certificates, get_safety_validation, check_activation_test, get_opcua_configuration, describe_operations. Writes (plc_write): update_block_logic, update_type_content, create_tag_table, delete_tag_table, create_tag, update_tag, delete_tag, move_tag, create_user_constant, update_user_constant, delete_user_constant, create_block, delete_block, create_block_group, delete_block_group, create_watch_table, delete_watch_table, create_watch_table_group, delete_watch_table_group, update_watch_table_content, update_force_table_content, update_program, add_interface_member, update_interface_member, delete_interface_member, update_db_member, create_db_snapshot, set_network_text, replace_network_text, rename_members, rename_tags, rename_objects, replace_tag_comments, export_tag_table, import_tag_table, export_opcua_xml, create_instance_db, rename_block, renumber_block, copy_block, move_block, move_tag_table, import_external_source, generate_blocks_from_source, delete_external_source, export_source_file, rename_block_group, rename_type_group, rename_tag_table_group, rename_watch_table_group, create_type_group, delete_type_group, create_tag_table_group, delete_tag_table_group, create_software_unit, delete_software_unit, rename_software_unit, export_tag_tables_csv, export_watch_tables_csv, export_force_tables_csv, export_blocks, update_text_list, delete_text_list, update_text_list_cells, update_alarm_text_cells, update_supervision_cells, export_text_lists, import_text_lists, export_alarm_texts, import_alarm_texts, export_alarm_classes, import_alarm_classes, export_system_diagnostics_settings, import_system_diagnostics_settings, set_project_compilation_settings, generate_loadable, set_account_locking, set_plc_password_policy, set_legacy_plc_password_policy, set_syslog, add_syslog_server, remove_syslog_server, create_project_syslog_server, update_project_syslog_server, delete_project_syslog_server, set_system_web_pages, set_default_web_application, set_watch_table_access, remove_watch_table_access, set_force_table_access, remove_force_table_access, set_display_password, set_front_panel_display, set_plc_project_language, set_ip_accessibility, generate_web_blocks, create_webserver_user, update_webserver_user, delete_webserver_user, update_simple_webserver_user, create_opcua_user, set_opcua_user_password, delete_opcua_user, create_custom_role, update_custom_role, delete_custom_role, assign_engineering_right, unassign_engineering_right, assign_device_function_right, unassign_device_function_right, create_project_user, update_project_user, delete_project_user, assign_user_role, unassign_user_role, set_anonymous_user, create_custom_device_function_right, update_custom_device_function_right, delete_custom_device_function_right, update_umc_user, delete_umc_user, set_umac_password_policy, protect_project, create_offline_umc_user, create_offline_umc_user_group, import_umc_user, mark_objects, unmark_objects, save_cam_data, load_cam_data, set_certificate_manager, create_cpu_certificate, import_cpu_certificate, export_cpu_certificate, delete_cpu_certificate, assign_cpu_certificate, unassign_cpu_certificate, set_certificate_management, add_certificate_supported_service, update_certificate_supported_service, remove_certificate_supported_service, generate_safety_printout, create_activation_test, delete_activation_test, change_activation_test_evaluation_device, create_activation_test_group, delete_activation_test_group, create_safety_function, update_safety_function, delete_safety_function, reset_safety_function_test_result, create_safety_function_condition, update_safety_function_condition, delete_safety_function_condition, export_activation_test, generate_activation_test_report, create_opcua_interface, delete_opcua_interface, export_opcua_interface, import_opcua_interface, set_opcua_interface, set_opcua_namespace_restriction, create_opcua_role, add_opcua_standard_role, delete_opcua_role, set_opcua_role, set_opcua_namespace_permission, download_to_folder, set_plc_master_secret_online, reset_plc_master_secret_online, load_snapshot_as_actual_values, load_start_values_as_actual_values, export_supervisions, import_supervisions, import_supervision_settings, set_prodiag_assignment, export_prodiag_info, create_technology_object, delete_technology_object, rename_technology_object, create_technology_object_group, delete_technology_object_group, rename_technology_object_group, set_technology_object_parameter, connect_technology_object_hardware, disconnect_technology_object_hardware, export_technology_object, import_technology_object, update_technology_object_content, set_plc_access_level, protect_plc_confidential_data, change_plc_confidential_data_password, disable_plc_confidential_data_protection, safety_login, safety_logoff, set_safety_password, revoke_safety_password, create_safety_runtime_group, set_safety_runtime_group, insert_network, replace_network, delete_network, create_block_from_document, set_block_know_how_protection, remove_block_know_how_protection, define_block_write_protection, set_block_write_protection, remove_block_write_protection, change_block_write_protection_password, set_project_languages, export_project_texts, import_project_texts.")]
    public string Operation { get; set; } = string.Empty;

    [Description("PLC block path, e.g. PLC_1/Main or PLC_1/Blocks/Folder/Block. Required by get_block_content, get_block_protection and the six block protection writes, list_networks, insert_network, replace_network, delete_network, get_block_interface, get_db_values, get_db_snapshot, create_db_snapshot, set_network_text, replace_network_text, rename_members (or typePath), update_block_logic, create_block, delete_block, create_block_group, delete_block_group, update_db_member, create_instance_db (the new DB), rename_block, renumber_block, copy_block and move_block (the source), get_call_tree and find_call_paths, and the ProDiag FB of read_supervision_workbook, update_supervision_cells, export_supervisions, import_supervisions and import_supervision_settings; one of the analyze_change_impact targets; add/update/delete_interface_member take blockPath or typePath. create_block, create_instance_db, create_block_group and delete_block_group also accept PLC/Name (the PLC root group). rename_block_group: the block user group to rename, e.g. PLC_1/Blocks/Line or PLC_1/Units/U1/Blocks/Line (required). export_blocks: optional block group to export, e.g. PLC_1/Blocks or PLC_1/Blocks/Line (its blocks and subgroups). The ProDiag operations: the FB of get_prodiag_fb and export_prodiag_info, the instance DB or global DB of get/set_prodiag_assignment. get_fingerprints: the block whose fingerprints are read (or typePath).")]
    public string? BlockPath { get; set; }

    [Description("PLC data type path, e.g. PLC_1/Types/AnalogInputSettings. Required by get_type_content, get_type_interface and update_type_content; one of the analyze_change_impact targets; add/update/delete_interface_member and rename_members take typePath or blockPath. rename_type_group, create_type_group and delete_type_group: the PLC data type user group to rename, create (its parent must exist) or delete, e.g. PLC_1/Types/Motion or PLC_1/Units/U1/Types/Motion (required). export_blocks: optional PLC data type group to export, e.g. PLC_1/Types. get_fingerprints: the PLC data type whose fingerprints are read (or blockPath).")]
    public string? TypePath { get; set; }

    [Description("Document format. Valid values: source, xml, and for get_block_content and update_block_logic also sd (SIMATIC SD: only the .s7dcl and .s7res documents of PlcBlock.ExportAsDocuments, delimited like the xml bundle; written with PlcBlockComposition.ImportFromDocuments and ImportDocumentOptions.Override; the .s7dcl must declare the target block; not live-tested). update_block_logic and update_type_content take the write format and expectedContentHash must carry the same format. get_type_content and update_type_content default to source; get_block_content defaults to xml and honors source for global data blocks and SCL-language FB/FC/OB. export_blocks: xml (default; the get_block_content/get_type_content xml documents) or source (global DBs, SCL FB/FC/OB and PLC data types; other blocks are listed as skipped).")]
    public string? Format { get; set; }

    [Description("Include the object's dependency closure in the exported source. Optional for get_block_content and get_type_content; only meaningful when format is source. The result is context only and carries no contentHash. export_source_file: true passes GenerateOptions.WithDependencies (the objects' dependencies are generated too), omitted or false GenerateOptions.None.")]
    public bool? WithDependencies { get; set; }

    [Description("Optional PLC software name. rename_tags and rename_objects: the PLC whose objects are renamed (optional when the project has one PLC). For list_tag_tables, list_download_targets, get_online_state, list_watch_and_force_tables and list_text_lists, when omitted every PLC is read. For the text list, alarm text and their workbook operations, the compare, watch/force table, update_program, find_unused_blocks, find_orphaned_instance_dbs, the analyze_change_impact tag target and the plc_write tag, tag-table and user-constant operations it scopes the target; an omitted name that matches several PLCs fails with target_ambiguous. list_external_sources, read_tag_comments and find_objects read every PLC when it is omitted; the external-source operations, the tag-table group operations, the software unit operations, rename_watch_table_group, the CSV exports, export_blocks and scan_accessible_devices scope their target with it. Technology object operations: list_technology_objects reads every PLC when omitted (unless folderPath narrows one PLC); every other technology object operation addresses the only PLC when omitted. get_plc_security reads every PLC when omitted; set_plc_access_level and disable_plc_confidential_data_protection address the only PLC when omitted. get_safety_administration and list_safety_blocks read every PLC when omitted; the fail-safe writes address the only PLC when omitted. get_plc_checksums reads every PLC when omitted. generate_loadable addresses the PLC its blockPaths name, else the only PLC, when omitted.")]
    public string? PlcName { get; set; }

    [Description("Tag table name (case-insensitive). export_tag_table: required, the table to export (with folderPath). import_tag_table: optional, the name of the table the file holds; then it must not exist yet without overwrite and must be in the folder after the import. replace_tag_comments: optional, only this table (with folderPath); omitted covers every tag table of the PLC. Optional for list_tag_tables (returns only that table; no match fails the item with target_not_found); required by the tag, tag-table and user-constant write operations; with name, the tag target of analyze_change_impact. Watch or force table name (case-insensitive) for read_watch_table, read_force_table, get_watch_table_content, get_force_table_content and the watch/force table writes except the group ones. export_tag_tables_csv, export_watch_tables_csv, export_force_tables_csv and read_tag_comments: optional, only that table (omitted: every table of the PLC, or of folderPath). With tagName, the tag table (case-insensitive, found anywhere in the PLC's tag table tree) of a PlcTag value for set_technology_object_parameter or of connect_technology_object_hardware connectionKind tag. get/set_prodiag_assignment: with name, the tag whose AssignedProDiagFB is read or set. set_watch_table_access, remove_watch_table_access (watch table), set_force_table_access, remove_force_table_access (force table): the table whose web server or display access rule is set or removed (with folderPath for a group).")]
    public string? TableName { get; set; }

    [Description("Optional tag table folder for list_tag_tables and the plc_write tag, tag-table and user-constant operations, as the inventory emits it in folderPath (\"/\" for the root, e.g. /Line/Cell; case-insensitive). Returns only tables directly in that folder; no match fails the item with target_not_found. For watch/force table operations: the watch-and-force table group path as list_watch_and_force_tables emits it (\"/\" or omitted for the system group); required by create_watch_table_group and delete_watch_table_group, where it names the group itself. Technology object operations: the technology object group path (\"/\" or omitted for PlcSoftware.TechnologicalObjectGroup, e.g. /Axes/Line1; case-insensitive): optional for list_technology_objects (lists that group's subtree of one PLC), create_technology_object and import_technology_object (the target group); required by create/delete/rename_technology_object_group, where it names the group itself. rename_tag_table_group, create_tag_table_group and delete_tag_table_group (a tag table folder; with unitName, a folder of that software unit) and rename_watch_table_group (a watch-and-force table group): the user group to rename, create (its parent must exist) or delete, e.g. /Line/Cell (required). CSV exports and read_tag_comments: optional folder or group narrowing the tables (tables directly in it). The watch/force table access operations of the settings family: the watch-and-force table group path of tableName (omitted: the root group).")]
    public string? FolderPath { get; set; }

    [Description("Full replacement document for update_block_logic (Simatic ML XML, external source, or with format sd the bundle of get_block_content sd, edited), update_type_content (external source or Simatic ML XML), or update_watch_table_content/update_force_table_content (the SimaticML from get_watch_table_content/get_force_table_content, edited), in the write format. Required by all four, and by update_technology_object_content (the SimaticML from get_technology_object_content, edited). create_block_from_document (required): an sd bundle '--- FILE: Name.s7dcl ---' with the .s7dcl text and optionally '--- FILE: Name.s7res ---' with the .s7res text (same name; the .s7dcl declares the new block; no text ids are generated; every .s7res culture must be a project language).")]
    public string? Content { get; set; }

    [Description("Format-tagged contentHash from a plc_read of the same object in the same format (xml:sha256:<hex>, source:sha256:<hex> or sd:sha256:<hex>). Optional sd hash (from get_block_content sd or list_networks) for insert_network, replace_network and delete_network. Optional for set_network_text, replace_network_text and rename_members (xml). Required by update_block_logic, update_type_content, update_watch_table_content, update_force_table_content and update_technology_object_content (xml); optional xml hash for add/update/delete_interface_member (from get_block_interface, get_type_interface, get_block_content or get_type_content in xml). A changed object fails with state_changed.")]
    public string? ExpectedContentHash { get; set; }

    [Description("Tag or user-constant name. Required by create/update/delete tag and user-constant operations; with tableName, the tag target of analyze_change_impact. Technology object name (case-insensitive, unique in the PLC's technology object tree) for every technology object operation except list_technology_objects, the three group operations and import_technology_object. The settings family: create_project_syslog_server/update_project_syslog_server/delete_project_syslog_server (the project syslog server), set_default_web_application (the web application), generate_web_blocks (the application name, with directoryPath). UMAC: create_custom_role, update_custom_role, delete_custom_role (the custom role), create_custom_device_function_right, update_custom_device_function_right, delete_custom_device_function_right (the custom device function right); get_umc_server_user and import_umc_user (the UMC server user, or with userKind umcUserGroup the group), create_offline_umc_user_group (optional, the new group's name). Package G: get_creation_infos (optional, one composition's name; omitted lists every composition), find_object_by_identifier (required, an identifier from get_object_identifier). get/set_prodiag_assignment: with tableName, the tag whose AssignedProDiagFB is read or set. add_certificate_supported_service: optional service group name. Safety Validation Assistant: create_activation_test, delete_activation_test and change_activation_test_evaluation_device (the activation test).")]
    public string? Name { get; set; }

    [Description("New name: update_text_list renames the user text list, update_tag renames the tag, update_interface_member renames the member, rename_block renames the block (required) and copy_block names the copy (required); the group renames (rename_block_group, rename_type_group, rename_tag_table_group, rename_watch_table_group) take the new group name and rename_software_unit the new unit name (required); rename_technology_object and rename_technology_object_group (required) rename through the Name attribute when GetAttributeInfos lists it writable. Renaming user constants is not supported. UMAC: update_custom_role, update_custom_device_function_right, update_project_user, update_umc_user rename through SetName.")]
    public string? NewName { get; set; }

    [Description("Data type, e.g. Bool, Int, \"MyUdt\" or Array[0..9] of Real. Required by create_tag, create_user_constant, add_interface_member and find_free_address (a type with a documented 1/8/16/32-bit length: Bool, Byte, Char, SInt, USInt, Word, WChar, Int, UInt, DWord, DInt, UDInt, Real, TOD/Time_Of_Day); optional for update_tag, update_user_constant and update_interface_member.")]
    public string? DataType { get; set; }

    [Description("Optional logical address, e.g. %I0.0.")]
    public string? LogicalAddress { get; set; }

    [Description("Constant value. Required by create_user_constant; optional for update_user_constant. set_technology_object_parameter: the new value as text of at most 4000 characters, converted to exactly the runtime type of the current value (true/false, an invariant integer within range, a finite invariant number that is the shortest or the exact text of the nearest real, otherwise refused naming the shortest, any text for a string, an exact enum member name or for a [Flags] enum distinct member names separated by commas); exclusive with tableName/tagName.")]
    public string? Value { get; set; }

    [Description("Optional tag attribute (update_tag) or DB member attribute (update_db_member): external accessibility.")]
    public bool? ExternalAccessible { get; set; }

    [Description("Optional tag attribute (update_tag) or DB member attribute (update_db_member): external visibility.")]
    public bool? ExternalVisible { get; set; }

    [Description("Optional tag attribute (update_tag) or DB member attribute (update_db_member): external writability.")]
    public bool? ExternalWritable { get; set; }

    [Description("Block type for create_block. Valid values: FB, FC, OB, GlobalDB.")]
    public string? BlockType { get; set; }

    [Description("Programming language for create_block (FB/FC/OB only). Valid values: LAD, FBD, STL, SCL, GRAPH. Defaults to LAD. Omit for blockType=GlobalDB, which always uses DB. insert_network: the network language written as given into the { S7_Language := \"...\" } pragma (LAD, FBD, SCL, STL, ...; TIA Portal decides); omitted, it is the code's own S7_Language pragma or detected as the Siemens skill does (RUNG wire# is LAD, RUNG \"...\" or RUNG <number> is FBD, no RUNG is SCL). replace_network: omitted keeps the network's language; given, only the S7_Language value of its pragma changes.")]
    public string? Language { get; set; }

    [Description("OB event class for create_block when blockType=OB; defaults to ProgramCycle. The server assigns the OB number.")]
    public string? ObEventClass { get; set; }

    [Description("Interface section for add/update/delete_interface_member on a block: Input, Output, InOut, Static, Temp, Constant or Return, as the block's SimaticML export lists them. Omit for a PLC data type (section None).")]
    public string? Section { get; set; }

    [Description("Dotted member path. get_db_snapshot and create_db_snapshot (optional): return only the members at or below this path as the snapshot export writes it, e.g. Config or Buffer[2] (case-insensitive). add/update/delete_interface_member: the member within its section, e.g. Settings.Limits.Max (a name containing '.' goes in double quotes); for add it is the new member's full path. get_db_values (optional narrowing) and update_db_member (required): a DB value path as Interface.Members.Find takes it, with array indices allowed, e.g. Motor.Speed or Axis[2].Position. get/set_prodiag_assignment: with blockPath, the UDT-instance member of a global DB, array indices allowed, e.g. UdtArray[0].Valve.")]
    public string? MemberPath { get; set; }

    [Description("Start value literal in TIA syntax, e.g. 10, TRUE, T#1s or 'text'. Optional for add_interface_member and update_interface_member (elementary members only) and update_db_member.")]
    public string? StartValue { get; set; }

    [Description("update_interface_member: true removes the member's start value element.")]
    public bool? ClearStartValue { get; set; }

    [Description("Member comment text for commentCulture (add/update_interface_member); set_network_text: the new network comment in culture (an empty text clears it); an empty text removes that culture's comment. update_text_list: the text list comment for commentCulture (an empty text clears it). create_tag/update_tag: the tag comment text for commentCulture (an empty text clears it), set through the MultilingualTextItem of that culture and read back.")]
    public string? Comment { get; set; }

    [Description("Culture of comment, e.g. en-US or de-DE; required with comment.")]
    public string? CommentCulture { get; set; }

    [Description("add_interface_member: insert the new member before this sibling member name; omitted appends after the last sibling.")]
    public string? InsertBefore { get; set; }

    [Description("Target block group for copy_block (optional, default: the source group) and move_block (required), as a deterministic group path, e.g. PLC_1/Blocks or PLC_1/Blocks/Line/Cell. Copy and move stay within the source's PLC or software unit. generate_blocks_from_source: optional block group (PLC_1/Blocks/...) or PLC data type group (PLC_1/Types/...) of the source's own scope to generate into; omitted or the root generates into the scope's root group. create_block_from_document (required): the deterministic block group the new block is imported into, e.g. PLC_1/Blocks or PLC_1/Blocks/Line.")]
    public string? TargetGroupPath { get; set; }

    [Description("Target tag-table folder, written like folderPath (\"/\" for the root, e.g. /Line/Cell): optional for move_tag, required by move_tag_table.")]
    public string? TargetFolderPath { get; set; }

    [Description("Block number: renumber_block (or autoNumber:true), copy_block and create_instance_db (optional; omitted means automatic numbering).")]
    public int? Number { get; set; }

    [Description("renumber_block: true switches the block to automatic numbering instead of a fixed number.")]
    public bool? AutoNumber { get; set; }

    [Description("create_instance_db: name of the FB the instance DB belongs to.")]
    public string? InstanceOfName { get; set; }

    [Description("update_db_member: the member's Retain attribute.")]
    public bool? Retain { get; set; }

    [Description("update_db_member: the member's SetPoint attribute.")]
    public bool? SetPoint { get; set; }

    [Description("get_db_values: also read ExternalAccessible, ExternalVisible, ExternalWritable, Retain and SetPoint of every value.")]
    public bool? WithAttributes { get; set; }

    [Description("Target tag table name for move_tag (case-insensitive, same PLC). Required by move_tag.")]
    public string? TargetTableName { get; set; }

    [Description("Address area for find_free_address: I (input), Q (output) or M (memory). Required by find_free_address.")]
    public string? Area { get; set; }

    [Description("Optional areas for read_address_occupancy: a non-empty, duplicate-free subset of I, Q, M; omitted reads all three.")]
    public IReadOnlyList<string>? Areas { get; set; }

    [Description("Optional read_address_occupancy flag: include the PLC's hardware I/O addresses (from read_hardware_config ioDetails). Defaults to true.")]
    public bool? IncludeHardware { get; set; }

    [Description("Optional first byte find_free_address considers, 0 to 65535 (default 0).")]
    public int? StartByte { get; set; }

    [Description("Optional last byte find_free_address considers, 0 to 65535 (default 65535); must not be below startByte.")]
    public int? EndByte { get; set; }

    [Description("Optional find_free_address flag: the address must lie inside one hardware I/O address range of the PLC (I and Q only). Defaults to false.")]
    public bool? RequireHardware { get; set; }

    [Description("Optional find_free_address byte alignment of the start byte for byte, word and double-word types: 1 (default), 2, 4 or 8.")]
    public int? AlignBytes { get; set; }
    [Description("compare_software target kind, required: plc (another PLC of the project, named by targetName), project_library (the project library) or master_copy (a master copy of the project library's root master copy folder, named by targetName) or secondary_plc (a PLC of an open secondary project: targetName, with targetProject; issue #20). compare_hardware takes plc or master_copy (required, with targetName).")]
    public string? CompareTarget { get; set; }

    [Description("compare_software and compare_hardware: the other PLC's software or device name (compareTarget plc, or secondary_plc: a PLC of the secondary project targetProject) or the master copy name (compareTarget master_copy).")]
    public string? TargetName { get; set; }

    [Description("compare_hardware and compare_hardware_to_online: which hardware object of the PLC is compared, device (default: the station, HardwareObject Device) or cpu (the CPU device item). For compareTarget plc the other PLC's object of the same scope is the target.")]
    public string? HardwareScope { get; set; }

    [Description("compare_to_online/compare_software/compare_hardware/compare_hardware_to_online: depth cap of the projected compare tree, 1 to 64 (root is depth 1); default 16. get_call_tree (default 10), find_call_paths (default 20) and analyze_change_impact (default 10): depth cap in relationship hops, 1 to 50.")]
    public int? MaxDepth { get; set; }

    [Description("compare_to_online/compare_software/compare_hardware/compare_hardware_to_online: element cap of the projected compare tree, 1 to 5000, default 300. get_station_fingerprint_data: fingerprint data items returned, 1 to 2000, default 200. get_db_snapshot/create_db_snapshot: value cap, 1 to 20000, default 2000. read_watch_table/read_force_table: entry cap, 1 to 5000, default 1000. read_text_list_workbook, read_alarm_text_workbook, read_supervision_workbook: rows returned per worksheet, 1 to 5000, default 50; the read also stops at the value budget, which several workbook reads of one call share (a sheet then reports rowsTruncated and nextStartRow, a cut row cellsTruncated and nextStartColumn), and workbookHash always covers every cell. get_call_tree: node cap, 1 to 2000, default 100. find_call_paths: path cap, 1 to 1000, default 50. find_unused_blocks: listed-block cap, 1 to 5000, default 500. find_orphaned_instance_dbs: instance DB exports read, 1 to 1000, default 200. analyze_change_impact: user cap, 1 to 5000, default 500. export_blocks: object cap, 1 to 2000, default 500 (a scope with more objects is refused before anything is written). scan_accessible_devices: device cap, 1 to 1000, default 256.")]
    public int? MaxElements { get; set; }

    [Description("compare_to_online/compare_software/compare_hardware/compare_hardware_to_online: skip elements whose state is ObjectsIdentical or FolderContentsIdentical (counted in identicalElementsSkipped); default true. The root is always listed.")]
    public bool? DifferencesOnly { get; set; }

    [Description("compare_to_online, compare_hardware_to_online and get_station_fingerprint_data only: SHA-256 fingerprints of the device's TLS certificates that the USER checked and approved (64 hexadecimal characters each, case-insensitive, colons and spaces ignored; at most 16). A TLS certificate check during the compare (TlsVerificationConfiguration) is answered Trusted only when the device presents at least one certificate and EVERY presented certificate's fingerprint is listed (whether TIA Portal keeps that trust after the call is not documented and not live-tested); otherwise NonTrusted, the compare is discarded and the failed item's result.abort.tls lists every certificate's sha256, subject, issuer and validity. Take the values only from that evidence after the user compared them with the device's certificate; omitted trusts nothing.")]
    public IReadOnlyList<string>? TrustedCertificates { get; set; }

    [Description("update_text_list / delete_text_list: the user text list name (case-insensitive) as list_text_lists returns it. System text lists are refused.")]
    public string? TextListName { get; set; }

    [Description("Optional for read_text_list_workbook and export_text_lists, required for update_text_list_cells: export only these text lists (the filtered ExportToXlsx overload, names as TIA Portal spells them are used); omitted, a read or export covers every text list, system lists included. update_text_list_cells takes user text lists only, so it never imports system list rows again; read the workbook with the same textListNames for its expectedWorkbookHash.")]
    public IReadOnlyList<string>? TextListNames { get; set; }

    [Description("Optional cultures (TIA Portal languages, e.g. en-US, de-DE, found in LanguageSettings.Languages like the documented filtered export; list_text_lists returns the active projectLanguages, and a culture that is not active passes with a warning) of the text list and alarm text workbook operations; omitted means every active project language. For the text list workbook operations cultures needs textListNames (the filtered export takes both).")]
    public IReadOnlyList<string>? Cultures { get; set; }

    [Description("update_text_list: the new ListRange enum member name as list_text_lists reports it on this TIA Portal installation.")]
    public string? ListRange { get; set; }

    [Description("Required by read_alarm_text_workbook, update_alarm_text_cells and export_alarm_texts: the PlcAlarmTextXlsxExportOption member name of the installed TIA Portal (an unknown name fails and lists the members). Optional for import_alarm_texts: the option its verification re-export uses (omitted: the import is not read back; importCheck.outcome notReadBack, verification trusted with a warning).")]
    public string? ExportOption { get; set; }

    [Description("Optional ImportOptions member for import_text_lists, update_text_list_cells, import_supervisions, update_supervision_cells and import_supervision_settings: None or Override (default Override).")]
    public string? ImportOption { get; set; }

    [Description("Absolute file path on a drive or a UNC share (\\\\server\\share\\...); \\\\?\\ and \\\\.\\ device paths are refused. export_project_texts / import_project_texts: the project text workbook (.xlsx, documented). import_external_source: the source file to import (.scl, .awl, .db or .udt; a software unit takes .scl, .db or .udt), required. export_source_file: the source file to write (same extensions), required. export_tag_tables_csv, export_watch_tables_csv, export_force_tables_csv: the .csv file to write, required. The directory must exist; an existing file is refused unless overwrite is true. Required by the export_* and import_* alarm operations: an absolute file path on a drive or a UNC share (X:\\...\\name.xlsx or \\\\server\\share\\name.xlsx; the alarm classes a .DAT file: another extension made TIA Portal V21 show a modal error and the call wait on it, live; the system diagnostics settings any extension, none being documented). generate_loadable: required, the loadable file written (any extension; none is documented), with the same rules as the alarm exports. Device (\\\\?\\, \\\\.\\, \\??\\) and alternate-data-stream paths, '..' segments, reserved device names and segments ending in a space or a dot are refused (lexically); a mapped network drive or a link on the way is allowed and reported. An existing export target is replaced only with overwrite:true, never through a link. export_tag_table (the SimaticML file written) and import_tag_table (the file read; a private copy is imported): required. export_opcua_xml: required, the OPC UA XML file written. export_technology_object (the file written) and import_technology_object (the file read): an absolute local file path on a drive, e.g. C:\\Exports\\Axis_1.xml. UNC (\\\\server\\share) and device (\\\\?\\, \\\\.\\) paths are refused. set_front_panel_display: the logo file (FrontPanelDisplay.SetUserDefinedLogo). generate_web_blocks: the default HTML page (with directoryPath).")]
    public string? FilePath { get; set; }

    [Description("export_source_file, export_tag_tables_csv, export_watch_tables_csv, export_force_tables_csv, export_blocks, generate_loadable and export_project_texts: true replaces existing files; omitted or false refuses to overwrite any file. Optional for the export_* alarm operations: true replaces an existing file; omitted or false refuses to overwrite. export_technology_object: true replaces an existing file (default false: an existing file fails the item before anything is written). import_technology_object: true imports with ImportOptions.Override, replacing objects of the same name (default ImportOptions.None). export_tag_table and export_opcua_xml: true replaces an existing file. import_tag_table: true imports with ImportOptions.Override, replacing a tag table of the same name (default ImportOptions.None). create_block_from_document: true imports although a block of the declared name exists anywhere in the PLC (ImportDocumentOptions.Override, TIA Portal decides what it replaces); omitted or false refuses that. generate_web_blocks: true passes WebDBGenerateOptions.Override (existing web DBs are overridden), omitted or false None. download_to_folder: true downloads into a non-empty directory (TIA Portal may replace files there); omitted or false refuses a directory that holds anything.")]
    public bool? Overwrite { get; set; }

    [Description("Required by update_text_list_cells, update_alarm_text_cells and update_supervision_cells: 1 to 500 cell edits applied to the exported workbook, each { sheet, cell, expected, value, kind? }.")]
    public IReadOnlyList<PlcAlarms.PlcAlarmCellEditRequest>? CellEdits { get; set; }

    [Description("Required by the update_*_cells operations: the workbookHash (cells:sha256:<hex>) of the read_*_workbook read the edits were based on, with the same plcName, blockPath, textListNames, cultures and exportOption. A changed workbook fails with state_changed.")]
    public string? ExpectedWorkbookHash { get; set; }

    [Description("Optional for read_text_list_workbook, read_alarm_text_workbook and read_supervision_workbook: return rows of this worksheet only (exact name, else the one case-insensitive match); the other sheets report their counts and nextStartRow.")]
    public string? Sheet { get; set; }

    [Description("Optional with sheet: the first row number returned (default 1); pass a sheet's nextStartRow to read on.")]
    public int? StartRow { get; set; }

    [Description("Optional with sheet: the first column returned (upper-case letters A to XFD, default A). A row wider than the value budget is returned in parts: it reports cellsTruncated and nextStartColumn; read on with startRow = that row and startColumn = nextStartColumn.")]
    public string? StartColumn { get; set; }

    [Description("Optional for every alarm, text list, supervision and ProDiag plc_write operation: how TIA Portal confirmation dialogs raised while it runs are answered; also download_to_folder, set_plc_master_secret_online and reset_plc_master_secret_online. decline (default): each is answered No, recorded in result.portalConfirmations and fails the item; accept: each is answered Yes (never in read-only), recorded and warned about.")]
    public string? PortalConfirmations { get; set; }

    [Description("set_prodiag_assignment: the name of the ProDiag FB (an FB of the same PLC) to assign to the instance DB (blockPath), the UDT-instance member of a global DB (blockPath, memberPath) or the tag (tableName, name, optional folderPath).")]
    public string? ProDiagFb { get; set; }

    [Description("export_blocks: absolute local directory the exports are written into (it must exist; subfolders mirroring the project tree are created inside it). Required. Required by export_prodiag_info: an absolute local directory that does not exist yet; ExportProDIAGInfo writes the FB's ProDiag alarm information (CSV) into it. An export that writes no file fails postcondition_failed and creates no directory. generate_web_blocks: the HTML directory of the user-defined web pages (GenerateBlocks(DirectoryInfo, FileInfo, String, WebDBGenerateOptions)); omitted, GenerateBlocks(WebDBGenerateOptions) regenerates from the CPU's configured pages. Required by download_to_folder (issue #16): an existing absolute local directory the PLC's download data is written into.")]
    public string? DirectoryPath { get; set; }

    [Description("Program-analysis operations: time cap in seconds, 1 to 600, default 60, for the traversal and (find_orphaned_instance_dbs) the start of further exports; the evidence reads themselves (tree, one cross-reference sweep per block folder) always complete, each bounded by the worker request timeout. A hit cap is named in the result's truncation.")]
    public int? TimeoutSeconds { get; set; }
}
