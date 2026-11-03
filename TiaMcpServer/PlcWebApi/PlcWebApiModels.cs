using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.PlcWebApi;

public static class PlcWebApiContractVersion
{
    public const string Current = "1.0";
}

/// <summary>One plc_online_read item. Members the operation does not use must be omitted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcOnlineReadRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    [Description("Name of a PLC Web API target from the server's --plc-webapi-config file (exact, case-sensitive). Required by every operation.")]
    public string? Target { get; set; }

    [Description("browse_tags only: symbolic name to browse, for example \"DB_Line\" or \"DB_Line\".Motor; omit for the program root (browseMode children).")]
    public string? Var { get; set; }

    [Description("browse_tags: children (default) or var. browse_modules: children or node; omitted sends no mode.")]
    public string? BrowseMode { get; set; }

    [Description("browse_tags only: code_blocks lists the code blocks (PlcProgram.Browse type filter); requires browseMode children and no var.")]
    public string? Type { get; set; }

    [Description("read_tags only: 1 to 100 symbolic names, each as the Web API expects it, for example \"DB_Line\".Speed or \"Motor_On\".")]
    public List<string>? Names { get; set; }

    [Description("read_tags and read_watch_table_live: simple (default) or raw, the PlcProgram.Read value representation.")]
    public string? Representation { get; set; }

    [Description("read_diagnostic_buffer, browse_alarms, browse_syslog: number of entries, 1 to 1000 (default 50; browse_alarms without count lets the PLC decide). read_watch_table_live: table entries read and mapped, 1 to 1000 (default 100); the rest are counted as not read.")]
    public int? Count { get; set; }

    [Description("browse_syslog only: index of the first entry to return (0 or more).")]
    public int? First { get; set; }

    [Description("browse_alarms only: one alarm id from an earlier browse_alarms result; not combinable with count.")]
    public string? AlarmId { get; set; }

    [Description("read_diagnostic_buffer, browse_alarms, read_module_status: text language as a culture name, default en-US (read_module_status sends none when omitted).")]
    public string? Language { get; set; }

    [Description("read_module_status, read_module_leds (required) and browse_modules (optional): hardware identifier from browse_modules.")]
    public long? Hwid { get; set; }

    [Description("read_watch_table_live only: the PLC software (or device) of the bound project whose table is read offline; optional when the project has one PLC. The server does not check that target runs this PLC's program.")]
    public string? PlcName { get; set; }

    [Description("read_watch_table_live: name of a watch table of the bound project (as plc_read list_watch_and_force_tables lists it); exactly one of table and tagTable.")]
    public string? Table { get; set; }

    [Description("read_watch_table_live: name of a PLC tag table of the bound project (as plc_read list_tag_tables lists it); its tags are read; exactly one of table and tagTable.")]
    public string? TagTable { get; set; }

    [Description("read_watch_table_live only: the watch-table group path (\"/\" default) or the tag-table folder (\"/\" or /Line/Cell) of the table.")]
    public string? FolderPath { get; set; }
}

/// <summary>One plc_online_write item. Members the operation does not use must be omitted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcOnlineWriteRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    [Description("Name of a PLC Web API target from the server's --plc-webapi-config file (exact, case-sensitive). Required by every operation.")]
    public string? Target { get; set; }

    [Description("change_operating_mode only: run or stop (Plc.RequestChangeOperatingMode accepts no other mode).")]
    public string? OperatingMode { get; set; }

    [Description("write_tags only: 1 to 100 tags in order, each { name, value }; writing stops at the first failed tag.")]
    public List<PlcOnlineTagWrite>? Tags { get; set; }

    [Description("write_tags only: simple (default) or raw, the PlcProgram.Write value representation.")]
    public string? Representation { get; set; }

    [Description("acknowledge_alarm only: the alarm id from plc_online_read browse_alarms.")]
    public string? AlarmId { get; set; }

    [Description("flash_leds only: hardware identifier of the module from plc_online_read browse_modules.")]
    public long? Hwid { get; set; }

    [Description("set_system_time only: the new PLC system time (UTC), RFC 3339 with Z or an offset, for example 2026-10-31T12:00:00Z.")]
    public string? Timestamp { get; set; }

    [Description("create_backup: absolute local file path to write (its folder must exist). restore_backup: absolute path of an existing backup file.")]
    public string? Path { get; set; }

    [Description("create_backup only: replace an existing file at path (default false: an existing file fails validation).")]
    public bool? Overwrite { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcOnlineTagWrite
{
    [Description("Symbolic name as the Web API expects it, for example \"DB_Line\".Speed.")]
    public string? Name { get; set; }

    [Description("The value as JSON, sent unchanged: true/false, an integral number for integer types (2, not 2.0), a number, a string, or an array for raw.")]
    public JsonElement? Value { get; set; }
}

/// <summary>A planned write as the confirmation and the preview show it.</summary>
public sealed record PlcWebApiPlannedTag(string Name, JsonElement Value);

/// <summary>A local file a backup item writes or reads; size and hash pin the restore input.</summary>
public sealed record PlcWebApiPlannedFile(string Path, bool Exists, bool Overwrite, long? Bytes, string? Sha256);

/// <summary>
/// What one plc_online_write item will do on which controller. Members an operation does not use
/// are null. The confirmation covers this whole record: a change while the prompt is open refuses the call.
/// </summary>
public sealed record PlcWebApiWriteEffect(
    string Operation,
    string Target,
    string Address,
    string CertificateValidation,
    string Authentication,
    string ApiMethod,
    string Description,
    string? OperatingMode,
    IReadOnlyList<PlcWebApiPlannedTag>? Tags,
    string? Representation,
    string? AlarmId,
    long? Hwid,
    string? Timestamp,
    PlcWebApiPlannedFile? File);

/// <summary>plc_online_write document: the shared guarded-write envelope with typed effects and the per-item batch.</summary>
public sealed record PlcOnlineWriteResponse(
    string Tool,
    string ContractVersion,
    string Phase,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<WriteEffect<PlcWebApiWriteEffect>> Effects,
    StructuredOperationBatch? Batch);

/// <summary>The three plc_online_write guards; all acknowledge, legal because the domain confirms every call.</summary>
public static class PlcWebApiGuardDefinitions
{
    public const string OnlineChangesRealController = "online_changes_real_controller";

    public const string OperatingModeStop = "operating_mode_stop";

    public const string RestoreBackupReplacesProgram = "restore_backup_replaces_program";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new WriteGuardDefinition(OnlineChangesRealController, WriteGuardSeverities.Acknowledge,
            "The call changes one or more real controllers online through their Web API; the change acts on the running machine immediately and nothing is rolled back."),
        new WriteGuardDefinition(OperatingModeStop, WriteGuardSeverities.Acknowledge,
            "The item requests STOP: the user program stops executing and the outputs go to their configured STOP behaviour."),
        new WriteGuardDefinition(RestoreBackupReplacesProgram, WriteGuardSeverities.Acknowledge,
            "The item restores a backup: the PLC is stopped and restarted and its program, configuration and data are replaced by the backup's content."),
    });

    public static WriteGuardCatalog Catalog { get; } = new(Definitions);
}
