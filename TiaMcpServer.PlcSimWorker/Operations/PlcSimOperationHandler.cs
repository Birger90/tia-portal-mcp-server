using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcSimWorker.Runtime;

namespace TiaMcpServer.PlcSimWorker.Operations;

/// <summary>
/// Siemens-free operation logic of the PLCSIM worker: re-validates every request with the shared
/// <see cref="PlcSimRequestRules"/>, opens the instance by exact name, performs the operation through
/// <see cref="IPlcSimRuntime"/>, and returns one typed payload per method. Failures are thrown as
/// <see cref="PlcSimOperationException"/>; non-fatal notes go to <c>warnings</c>.
/// </summary>
internal sealed class PlcSimOperationHandler
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedMembers = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [PlcSimMethods.ProbeApi] = Array.Empty<string>(),
        [PlcSimMethods.ListInstances] = Array.Empty<string>(),
        [PlcSimMethods.GetInstance] = new[] { "instance" },
        [PlcSimMethods.ListTags] = new[] { "instance", "nameFilter", "max" },
        [PlcSimMethods.ReadTags] = new[] { "instance", "tags" },
        [PlcSimMethods.PowerOn] = new[] { "instance" },
        [PlcSimMethods.PowerOff] = new[] { "instance" },
        [PlcSimMethods.Run] = new[] { "instance" },
        [PlcSimMethods.Stop] = new[] { "instance" },
        [PlcSimMethods.MemoryReset] = new[] { "instance" },
        [PlcSimMethods.UpdateTagList] = new[] { "instance" },
        [PlcSimMethods.WriteTags] = new[] { "instance", "tags" },
        [PlcSimMethods.SetScaleFactor] = new[] { "instance", "scaleFactor" },
        [PlcSimMethods.RunTest] = new[] { "instance", "test" },
    };

    private readonly IPlcSimRuntime _runtime;
    private readonly IPlcSimClock _clock;

    public PlcSimOperationHandler(IPlcSimRuntime runtime, IPlcSimClock clock)
    {
        _runtime = runtime;
        _clock = clock;
    }

    /// <summary>See <see cref="IPlcSimRuntime.RestartRequired"/>; reported on every response after it becomes true.</summary>
    public bool RestartRequired => _runtime.RestartRequired;

    public object Execute(string method, PlcSimRequestInfo? request, List<string> warnings)
    {
        request ??= new PlcSimRequestInfo();
        Validate(method, request);
        switch (method)
        {
            case PlcSimMethods.ProbeApi:
                return _runtime.Probe();
            case PlcSimMethods.ListInstances:
                var list = _runtime.ListInstances();
                if (list.Count == 0)
                    warnings.Add("No PLCSIM instance is registered. Create and start one in the S7-PLCSIM Advanced Control Panel; these tools never register instances.");
                return list;
            case PlcSimMethods.GetInstance:
                return WithInstance(request, warnings, instance => GetInstance(instance, warnings));
            case PlcSimMethods.ListTags:
                return WithInstance(request, warnings, instance => ListTags(instance, request, warnings));
            case PlcSimMethods.ReadTags:
                return WithInstance(request, warnings, instance => ReadTags(instance, request.Tags!, warnings));
            case PlcSimMethods.PowerOn:
                return WithInstance(request, warnings, instance => Control(instance, PlcSimControlAction.PowerOn, "power_on", warnings));
            case PlcSimMethods.PowerOff:
                return WithInstance(request, warnings, instance => Control(instance, PlcSimControlAction.PowerOff, "power_off", warnings));
            case PlcSimMethods.Run:
                return WithInstance(request, warnings, instance => Control(instance, PlcSimControlAction.Run, "run", warnings));
            case PlcSimMethods.Stop:
                return WithInstance(request, warnings, instance => Control(instance, PlcSimControlAction.Stop, "stop", warnings));
            case PlcSimMethods.MemoryReset:
                return WithInstance(request, warnings, instance => Control(instance, PlcSimControlAction.MemoryReset, "memory_reset", warnings));
            case PlcSimMethods.UpdateTagList:
                warnings.Add(PlcSimWarnings.UpdateTagListColdRestart);
                return WithInstance(request, warnings, instance => Control(instance, PlcSimControlAction.UpdateTagList, "update_tag_list", warnings));
            case PlcSimMethods.WriteTags:
                return WithInstance(request, warnings, instance => WriteTags(instance, request.Tags!, warnings));
            case PlcSimMethods.SetScaleFactor:
                return WithInstance(request, warnings, instance => SetScaleFactor(instance, request.ScaleFactor!.Value, warnings));
            case PlcSimMethods.RunTest:
                return WithInstance(request, warnings, instance =>
                {
                    WarnUnlessSoftbus(instance, "plcsim_test", warnings);
                    return PlcSimTestRunner.Run(instance, PlcSimRequestRules.NormalizeTest(request.Test!), _clock, warnings);
                });
            default:
                throw new PlcSimOperationException(WorkerFailureCategories.ValidationError, $"Unsupported PLCSIM worker method '{method}'.");
        }
    }

    private static void Validate(string method, PlcSimRequestInfo request)
    {
        if (!AllowedMembers.TryGetValue(method, out var allowed))
            throw new PlcSimOperationException(WorkerFailureCategories.ValidationError, $"Unsupported PLCSIM worker method '{method}'.");

        var errors = new List<string>();
        foreach (var (name, present) in new (string, bool)[]
        {
            ("instance", request.Instance is not null), ("nameFilter", request.NameFilter is not null), ("max", request.Max is not null),
            ("tags", request.Tags is not null), ("scaleFactor", request.ScaleFactor is not null), ("test", request.Test is not null),
        })
        {
            if (present && !allowed.Contains(name, StringComparer.Ordinal))
                errors.Add($"{name} is not valid for {method}.");
        }

        if (allowed.Contains("instance", StringComparer.Ordinal))
            PlcSimRequestRules.ValidateInstance(request.Instance, errors, string.Empty);
        switch (method)
        {
            case PlcSimMethods.ListTags:
                if (request.NameFilter is not null && (string.IsNullOrWhiteSpace(request.NameFilter) || request.NameFilter.Length > PlcSimRequestRules.MaxNameFilterChars))
                    errors.Add($"nameFilter must be nonblank and at most {PlcSimRequestRules.MaxNameFilterChars} characters.");
                if (request.Max is { } max && (max < 1 || max > PlcSimRequestRules.MaxListTagsMax))
                    errors.Add($"max must be 1 to {PlcSimRequestRules.MaxListTagsMax}.");
                break;
            case PlcSimMethods.ReadTags:
                PlcSimRequestRules.ValidateTags(request.Tags, write: false, errors, string.Empty);
                break;
            case PlcSimMethods.WriteTags:
                PlcSimRequestRules.ValidateTags(request.Tags, write: true, errors, string.Empty);
                break;
            case PlcSimMethods.SetScaleFactor:
                if (request.ScaleFactor is not { } factor || double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
                    errors.Add("scaleFactor must be a finite number greater than zero.");
                break;
            case PlcSimMethods.RunTest:
                PlcSimRequestRules.ValidateTest(request.Test, errors);
                break;
        }

        if (errors.Count != 0)
            throw new PlcSimOperationException(WorkerFailureCategories.ValidationError, string.Join("\n", errors));
    }

    private object WithInstance(PlcSimRequestInfo request, List<string> warnings, Func<IPlcSimInstance, object> operation)
    {
        using var instance = _runtime.Open(request.Instance!, warnings);
        return operation(instance);
    }

    private static PlcSimInstanceInfo GetInstance(IPlcSimInstance instance, List<string> warnings)
    {
        JsonElement? Read(PlcSimInstanceProperty property)
        {
            var read = instance.ReadProperty(property);
            if (!read.Available)
                warnings.Add($"{read.Problem} The value is reported as null.");
            return read.Value;
        }

        return new PlcSimInstanceInfo
        {
            RequestedName = instance.Name,
            Name = Read(PlcSimInstanceProperty.Name),
            Id = Read(PlcSimInstanceProperty.Id),
            OperatingState = Read(PlcSimInstanceProperty.OperatingState),
            OperatingMode = Read(PlcSimInstanceProperty.OperatingMode),
            ScaleFactor = Read(PlcSimInstanceProperty.ScaleFactor),
            CommunicationInterface = Read(PlcSimInstanceProperty.CommunicationInterface),
            CpuType = Read(PlcSimInstanceProperty.CpuType),
        };
    }

    private static PlcSimTagListInfo ListTags(IPlcSimInstance instance, PlcSimRequestInfo request, List<string> warnings)
    {
        var max = request.Max ?? PlcSimRequestRules.DefaultListTagsMax;
        var snapshot = instance.ReadTagInfos(request.NameFilter, max);
        if (request.NameFilter is not null && snapshot.NameUnavailableReason is not null)
            throw PlcSimOperationException.MemberMissing(PlcSimApiCatalog.TagInfoName.Id,
                $"nameFilter needs the tag name member {PlcSimApiCatalog.TagInfoName.Id}, which did not resolve: {snapshot.NameUnavailableReason}. Omit nameFilter to list unfiltered tags.");

        var status = instance.ReadTagListStatus();
        if (!status.Available)
            warnings.Add($"{status.Problem} tagListStatus is reported as null.");
        if (snapshot.TotalCount == 0)
            warnings.Add("IInstance.TagInfos is empty. The tag list may not have been read since the last download; plcsim_write update_tag_list refreshes it (see its cold-restart warning).");

        var returned = snapshot.Tags.Take(max).ToList();
        return new PlcSimTagListInfo
        {
            Instance = instance.Name,
            NameFilter = request.NameFilter,
            Max = max,
            TotalCount = snapshot.TotalCount,
            MatchedCount = snapshot.MatchedCount,
            ReturnedCount = returned.Count,
            Truncated = returned.Count < snapshot.MatchedCount,
            BudgetTrimmed = false,
            TagListStatus = status.Value,
            Tags = returned,
        };
    }

    private static PlcSimTagReadListInfo ReadTags(IPlcSimInstance instance, IReadOnlyList<PlcSimTagRequestInfo> tags, List<string> warnings)
    {
        var result = new PlcSimTagReadListInfo { Instance = instance.Name };
        foreach (var tag in tags)
        {
            var entry = new PlcSimTagValueInfo { Name = tag.Name!, DataType = tag.DataType! };
            try
            {
                entry.Value = PlcSimValueCodec.ToJson(instance.Read(PlcSimTagNames.ToApiName(tag.Name!), tag.DataType!));
                entry.Status = "read";
                result.ReadCount++;
            }
            catch (PlcSimOperationException exception)
            {
                entry.Status = "failed";
                entry.Error = exception.Detail;
                result.FailedCount++;
            }

            result.Tags.Add(entry);
        }

        if (result.FailedCount > 0)
            warnings.Add($"{result.FailedCount} of {tags.Count} tag reads failed; see each tag's error.");
        return result;
    }

    /// <summary>
    /// One control call. A failed return (<see cref="PlcSimReturnChecks.Failed"/>) is recorded in the
    /// result and fails the item host-side, so the operating state evidence is kept.
    /// </summary>
    private static PlcSimControlResultInfo Control(IPlcSimInstance instance, PlcSimControlAction action, string operation, List<string> warnings)
    {
        if (action is PlcSimControlAction.PowerOn or PlcSimControlAction.Run)
            WarnUnlessSoftbus(instance, operation, warnings);
        var before = instance.ReadProperty(PlcSimInstanceProperty.OperatingState);
        var invocation = instance.Control(action);
        var after = instance.ReadProperty(PlcSimInstanceProperty.OperatingState);
        if (!after.Available)
            warnings.Add($"{after.Problem} operatingStateBefore/After are reported as null; confirm the state another way.");
        if (invocation.TimeoutArgumentMs is null && action != PlcSimControlAction.UpdateTagList)
            warnings.Add($"{invocation.Signature} takes no timeout argument and may return before the state change completes; operatingStateAfter may lag.");
        if (invocation.ReturnWarning(operation) is { } returnWarning)
            warnings.Add(returnWarning + " Check operatingStateAfter.");
        return new PlcSimControlResultInfo
        {
            Instance = instance.Name,
            Operation = operation,
            InvokedSignature = invocation.Signature,
            TimeoutArgumentMs = invocation.TimeoutArgumentMs,
            ReturnType = invocation.ReturnType,
            ReturnValue = invocation.ReturnValue,
            ReturnCheck = invocation.Check,
            ReturnCode = invocation.ReturnCode,
            OperatingStateBefore = before.Value,
            OperatingStateAfter = after.Value,
        };
    }

    /// <summary>
    /// Warns (never blocks) when the instance's <c>CommunicationInterface</c> is not exactly
    /// <see cref="PlcSimApiCatalog.SoftbusCommunicationInterface"/>: a started instance on another
    /// interface can reach real network devices through the PLCSIM virtual Ethernet adapter.
    /// </summary>
    internal static void WarnUnlessSoftbus(IPlcSimInstance instance, string operation, ICollection<string> warnings)
    {
        var read = instance.ReadProperty(PlcSimInstanceProperty.CommunicationInterface);
        if (read.Available && read.Value is { ValueKind: JsonValueKind.String } value
            && string.Equals(value.GetString(), PlcSimApiCatalog.SoftbusCommunicationInterface, StringComparison.Ordinal))
            return;
        var raw = read.Available ? read.Value?.GetRawText() ?? "null" : $"unreadable ({read.Problem})";
        warnings.Add($"{operation}: instance '{instance.Name}' has IInstance.CommunicationInterface {raw}, not '{PlcSimApiCatalog.SoftbusCommunicationInterface}'. "
            + "Unless the instance uses the PLCSIM softbus, a running virtual CPU can communicate with real network devices through the "
            + "PLCSIM virtual Ethernet adapter; check the instance's online access in the S7-PLCSIM Advanced Control Panel.");
    }

    private static PlcSimTagWriteListInfo WriteTags(IPlcSimInstance instance, IReadOnlyList<PlcSimTagRequestInfo> tags, List<string> warnings)
    {
        var result = new PlcSimTagWriteListInfo { Instance = instance.Name };
        var stopped = false;
        foreach (var tag in tags)
        {
            PlcSimValueCodec.TryParse(tag.DataType!, tag.Value, out var typed, out _);
            var entry = new PlcSimTagWriteInfo { Name = tag.Name!, DataType = tag.DataType!, Value = PlcSimValueCodec.ToJson(typed) };
            result.Tags.Add(entry);
            if (stopped)
            {
                entry.Status = "skipped";
                result.SkippedCount++;
                continue;
            }

            try
            {
                var invocation = instance.Write(PlcSimTagNames.ToApiName(tag.Name!), tag.DataType!, typed!);
                entry.InvokedSignature = invocation.Signature;
                entry.ReturnValue = invocation.ReturnValue;
                if (invocation.Failed)
                    throw invocation.Failure($"write_tags '{tag.Name}'");
                entry.Status = "written";
                result.WrittenCount++;
                if (invocation.ReturnWarning($"write_tags '{tag.Name}'") is { } returnWarning)
                    warnings.Add(returnWarning);
            }
            catch (PlcSimOperationException exception)
            {
                entry.Status = "failed";
                entry.Error = exception.Detail;
                result.FailedCount++;
                stopped = true;
            }
        }

        return result;
    }

    private static PlcSimScaleFactorResultInfo SetScaleFactor(IPlcSimInstance instance, double value, List<string> warnings)
    {
        var previous = instance.ReadProperty(PlcSimInstanceProperty.ScaleFactor);
        instance.SetScaleFactor(value);
        var current = instance.ReadProperty(PlcSimInstanceProperty.ScaleFactor);
        if (!current.Available)
            warnings.Add($"{current.Problem} The new scale factor could not be read back.");
        return new PlcSimScaleFactorResultInfo
        {
            Instance = instance.Name,
            Requested = value,
            Previous = previous.Value,
            Current = current.Value,
        };
    }
}
