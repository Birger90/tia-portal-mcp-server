using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.PlcSim;

public static class PlcSimContractVersion
{
    public const string Current = "1.0";
}

/// <summary>One plcsim_read or plcsim_write item. Members not used by the operation must be omitted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcSimOperationRequest : IOperationBatchItem
{
    public string OperationId { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    [Description("Exact, case-sensitive PLCSIM instance name as created in the S7-PLCSIM Advanced Control Panel. Required by every operation except probe_api and list_instances.")]
    public string? Instance { get; set; }

    [Description("list_tags only: case-insensitive substring of the tag name (needs the STagInfo.Name member; see probe_api).")]
    public string? NameFilter { get; set; }

    [Description("list_tags only: 1 to 5000 tags, default 500.")]
    public int? Max { get; set; }

    [Description("read_tags and write_tags: 1 to 100 tags in order. Each is {name, dataType} plus value for write_tags.")]
    public List<PlcSimTagRequest>? Tags { get; set; }

    [Description("set_scale_factor only: the new scale factor, a finite number greater than zero.")]
    public double? Value { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcSimTagRequest
{
    [Description("PLC tag name as PLCSIM Advanced addresses it (the form list_tags returns, without quotes), for example Motor_On, DB_Line.Speed or DB_Line.Values[2]. TIA Portal notation with quoted segments (\"DB_Line\".Speed) is accepted too: the outer quotes of each segment are removed before the API call.")]
    public string? Name { get; set; }

    [Description("One of bool, int8, int16, int32, int64, uint8, uint16, uint32, uint64, float, double, string; selects the typed Read*/Write* API method (a Byte/USInt tag needs uint8: ReadInt8 on a Byte failed TypeMismatch live). string and the uint types are read-only (no WriteString/WriteUInt* member verified).")]
    public string? DataType { get; set; }

    [Description("write_tags only: JSON true/false for bool, an integral number for intN, a finite number for float/double.")]
    public JsonElement? Value { get; set; }
}

/// <summary>One plcsim_test scenario step. Members not used by the step kind must be omitted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PlcSimTestStep
{
    [Description("write, wait or expect.")]
    public string? Kind { get; set; }

    [Description("Optional label echoed in the step result.")]
    public string? Name { get; set; }

    [Description("write and expect: tag name, as in read_tags (DB_Line.Speed; quoted segments such as \"DB_Line\".Speed are accepted).")]
    public string? Tag { get; set; }

    [Description("write and expect: bool, int8, int16, int32, int64, float, double; uint8, uint16, uint32, uint64 and string for expect only.")]
    public string? DataType { get; set; }

    [Description("write: value to write. expect: expected value for every comparison except between.")]
    public JsonElement? Value { get; set; }

    [Description("expect: equals, notEquals, greaterThan, greaterOrEqual, lessThan, lessOrEqual or between. bool and string allow only equals/notEquals.")]
    public string? Comparison { get; set; }

    [Description("expect with between: inclusive lower bound.")]
    public JsonElement? Min { get; set; }

    [Description("expect with between: inclusive upper bound.")]
    public JsonElement? Max { get; set; }

    [Description("wait: 1 to 60000 ms.")]
    public int? Ms { get; set; }

    [Description("expect: how long to poll until the comparison holds, 0 to 60000 ms, default 2000.")]
    public int? TimeoutMs { get; set; }

    [Description("expect: poll interval, 10 to 60000 ms, default 50.")]
    public int? PollMs { get; set; }
}
