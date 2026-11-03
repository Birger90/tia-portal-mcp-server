namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// Worker method names of the S7-PLCSIM Advanced worker. <c>OperationPolicyCatalog</c> is one
/// flat namespace shared with the Openness worker, so every PLCSIM method carries the
/// <c>plcsim_</c> prefix (as HMI methods carry <c>hmi_</c>). Reads are <c>Observe</c>; every
/// method that changes a simulated CPU, its tags or its timing is <c>SimulationControl</c>.
/// </summary>
public static class PlcSimMethods
{
    public const string Prefix = "plcsim_";

    public const string ProbeApi = "plcsim_probe_api";
    public const string ListInstances = "plcsim_list_instances";
    public const string GetInstance = "plcsim_get_instance";
    public const string ListTags = "plcsim_list_tags";
    public const string ReadTags = "plcsim_read_tags";

    public const string PowerOn = "plcsim_power_on";
    public const string PowerOff = "plcsim_power_off";
    public const string Run = "plcsim_run";
    public const string Stop = "plcsim_stop";
    public const string MemoryReset = "plcsim_memory_reset";
    public const string UpdateTagList = "plcsim_update_tag_list";
    public const string WriteTags = "plcsim_write_tags";
    public const string SetScaleFactor = "plcsim_set_scale_factor";
    public const string RunTest = "plcsim_run_test";

    /// <summary>Read methods, classified <c>Observe</c>.</summary>
    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[]
    {
        ProbeApi, ListInstances, GetInstance, ListTags, ReadTags
    });

    /// <summary>Methods that change a simulated CPU, classified <c>SimulationControl</c>.</summary>
    public static IReadOnlyList<string> Control { get; } = Array.AsReadOnly(new[]
    {
        PowerOn, PowerOff, Run, Stop, MemoryReset, UpdateTagList, WriteTags, SetScaleFactor, RunTest
    });

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Read.Concat(Control).ToArray());

    /// <summary>Maps a public plcsim_read/plcsim_write operation name to its worker method.</summary>
    public static string ForOperation(string operation) => Prefix + operation;

    public static bool IsPlcSimMethod(string? method)
        => method is not null && All.Contains(method, StringComparer.Ordinal);
}
