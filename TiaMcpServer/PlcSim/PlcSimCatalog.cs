using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.PlcSim;

/// <summary>
/// Public operation catalog of plcsim_read and plcsim_write: names, applicable members, strict
/// validation before any worker start, access checks, and the mapping to worker requests.
/// </summary>
public static class PlcSimCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> ReadFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["probe_api"] = Array.Empty<string>(),
        ["list_instances"] = Array.Empty<string>(),
        ["get_instance"] = new[] { "instance" },
        ["list_tags"] = new[] { "instance", "nameFilter", "max" },
        ["read_tags"] = new[] { "instance", "tags" },
    };

    private static readonly IReadOnlyDictionary<string, string[]> WriteFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["power_on"] = new[] { "instance" },
        ["power_off"] = new[] { "instance" },
        ["run"] = new[] { "instance" },
        ["stop"] = new[] { "instance" },
        ["memory_reset"] = new[] { "instance" },
        ["update_tag_list"] = new[] { "instance" },
        ["write_tags"] = new[] { "instance", "tags" },
        ["set_scale_factor"] = new[] { "instance", "value" },
    };

    public static IReadOnlyCollection<string> ReadOperationNames => ReadFields.Keys.ToArray();

    public static IReadOnlyCollection<string> WriteOperationNames => WriteFields.Keys.ToArray();

    public static string WorkerMethod(string operation) => PlcSimMethods.ForOperation(operation);

    public static OperationValidation Validate(IReadOnlyList<PlcSimOperationRequest>? operations, bool write)
    {
        if (OperationBatchRules.CheckCount(operations?.Count ?? 0) is { } countError)
            return OperationValidation.Invalid(WorkerFailureCategories.ValidationError, countError);

        var fields = write ? WriteFields : ReadFields;
        var other = write ? ReadFields : WriteFields;
        var tool = write ? "plcsim_write" : "plcsim_read";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var item in operations!)
        {
            if (OperationBatchRules.CheckItem(item, ids) is { } itemError)
            {
                errors.Add(itemError);
                continue;
            }

            var prefix = $"{item.OperationId}: ";
            if (string.IsNullOrWhiteSpace(item.Operation))
            {
                errors.Add(prefix + "operation is required.");
                continue;
            }

            if (!fields.TryGetValue(item.Operation, out var allowed))
            {
                errors.Add(prefix + (other.ContainsKey(item.Operation)
                    ? $"'{item.Operation}' belongs to {(write ? "plcsim_read" : "plcsim_write")}, not {tool}."
                    : item.Operation == "test"
                        ? "scenarios run through plcsim_test."
                        : $"unknown {tool} operation '{item.Operation}'. Valid operations: {string.Join(", ", fields.Keys)}."));
                continue;
            }

            foreach (var (name, present) in new (string, bool)[]
            {
                ("instance", item.Instance is not null), ("nameFilter", item.NameFilter is not null), ("max", item.Max is not null),
                ("tags", item.Tags is not null), ("value", item.Value is not null),
            })
            {
                if (present && !allowed.Contains(name, StringComparer.Ordinal))
                    errors.Add(prefix + $"'{name}' is not valid for {item.Operation}.");
            }

            if (allowed.Contains("instance", StringComparer.Ordinal))
                PlcSimRequestRules.ValidateInstance(item.Instance, errors, prefix);

            switch (item.Operation)
            {
                case "list_tags":
                    if (item.NameFilter is not null && (string.IsNullOrWhiteSpace(item.NameFilter) || item.NameFilter.Length > PlcSimRequestRules.MaxNameFilterChars))
                        errors.Add(prefix + $"nameFilter must be nonblank and at most {PlcSimRequestRules.MaxNameFilterChars} characters.");
                    if (item.Max is { } max && (max < 1 || max > PlcSimRequestRules.MaxListTagsMax))
                        errors.Add(prefix + $"max must be 1 to {PlcSimRequestRules.MaxListTagsMax}.");
                    break;
                case "read_tags":
                    PlcSimRequestRules.ValidateTags(ToTags(item.Tags), write: false, errors, prefix);
                    break;
                case "write_tags":
                    PlcSimRequestRules.ValidateTags(ToTags(item.Tags), write: true, errors, prefix);
                    break;
                case "set_scale_factor":
                    if (item.Value is not { } factor || double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
                        errors.Add(prefix + "set_scale_factor requires value, a finite number greater than zero.");
                    break;
            }
        }

        return errors.Count == 0
            ? OperationValidation.Valid
            : OperationValidation.Invalid(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    public static IReadOnlyList<string> ValidateAccessMode(IReadOnlyList<PlcSimOperationRequest> operations, McpAccessMode mode)
        => operations
            .Where(item => !OperationPolicyCatalog.IsAllowed(mode, WorkerMethod(item.Operation)))
            .Select(item => $"Operation '{item.Operation}' is not permitted in {McpAccessModeNames.ToName(mode)} mode.")
            .ToArray();

    public static PlcSimRequestInfo ToRequest(PlcSimOperationRequest item) => new()
    {
        Instance = item.Instance,
        NameFilter = item.NameFilter,
        Max = item.Operation == "list_tags" ? item.Max ?? PlcSimRequestRules.DefaultListTagsMax : null,
        Tags = item.Tags is null ? null : ToTags(item.Tags)!.Select(tag => tag!).ToList(),
        ScaleFactor = item.Value,
    };

    /// <summary>The API members an operation calls, for dry-run plans (resolution happens in the worker).</summary>
    public static IReadOnlyList<string> ApiMembers(PlcSimOperationRequest item)
    {
        var members = new List<string> { "SimulationRuntimeManager.CreateInterface(String)", "IInstance.Name" };
        switch (item.Operation)
        {
            case "power_on": members.Add("IInstance.PowerOn"); break;
            case "power_off": members.Add("IInstance.PowerOff"); break;
            case "run": members.Add("IInstance.Run"); break;
            case "stop": members.Add("IInstance.Stop"); break;
            case "memory_reset": members.Add("IInstance.MemoryReset"); break;
            case "update_tag_list": members.Add("IInstance.UpdateTagList"); break;
            case "set_scale_factor": members.Add("IInstance.ScaleFactor { set; }"); break;
            case "write_tags":
                members.AddRange((item.Tags ?? new List<PlcSimTagRequest>())
                    .Select(tag => "IInstance." + PlcSimDataTypes.WriteMethod(tag.DataType!))
                    .Distinct(StringComparer.Ordinal));
                break;
        }

        if (item.Operation is "power_on" or "power_off" or "run" or "stop" or "memory_reset" or "update_tag_list")
            members.Add("IInstance.OperatingState (before/after, optional)");
        return members;
    }

    private static List<PlcSimTagRequestInfo?>? ToTags(IReadOnlyList<PlcSimTagRequest?>? tags)
        => tags?.Select(tag => tag is null ? null : new PlcSimTagRequestInfo { Name = tag.Name, DataType = tag.DataType, Value = tag.Value }).ToList();
}
