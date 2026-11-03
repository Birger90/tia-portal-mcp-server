using System.Text.Json;

namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// Parameters of one PLCSIM worker request (<c>WorkerRequest.PlcSim</c>). Only the members a method
/// reads are set; the host validates and normalizes them with <see cref="PlcSimRequestRules"/> and the
/// worker validates them again before any API call.
/// </summary>
public sealed class PlcSimRequestInfo
{
    /// <summary>Exact (ordinal) instance name, as shown in the PLCSIM Advanced Control Panel. Every method except probe_api and list_instances.</summary>
    public string? Instance { get; set; }

    /// <summary>list_tags: optional case-insensitive substring filter on the tag name.</summary>
    public string? NameFilter { get; set; }

    /// <summary>list_tags: maximum tags returned, already defaulted by the host.</summary>
    public int? Max { get; set; }

    /// <summary>read_tags and write_tags: the tags, in order.</summary>
    public List<PlcSimTagRequestInfo>? Tags { get; set; }

    /// <summary>set_scale_factor: the new scale factor, finite and greater than zero.</summary>
    public double? ScaleFactor { get; set; }

    /// <summary>run_test: the normalized scenario.</summary>
    public PlcSimTestRequestInfo? Test { get; set; }
}

public sealed class PlcSimTagRequestInfo
{
    public string? Name { get; set; }

    public string? DataType { get; set; }

    /// <summary>write_tags only: the JSON value to write.</summary>
    public JsonElement? Value { get; set; }
}

public sealed class PlcSimTestRequestInfo
{
    public bool ContinueOnFailure { get; set; }

    public int? MaxDurationMs { get; set; }

    public List<PlcSimTestStepInfo>? Steps { get; set; }
}

/// <summary>One scenario step. Members not applicable to <see cref="Kind"/> must be null.</summary>
public sealed class PlcSimTestStepInfo
{
    public string? Kind { get; set; }

    public string? Name { get; set; }

    public string? Tag { get; set; }

    public string? DataType { get; set; }

    public JsonElement? Value { get; set; }

    public string? Comparison { get; set; }

    public JsonElement? Min { get; set; }

    public JsonElement? Max { get; set; }

    public int? Ms { get; set; }

    public int? TimeoutMs { get; set; }

    public int? PollMs { get; set; }
}
