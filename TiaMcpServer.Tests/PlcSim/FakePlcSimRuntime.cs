using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcSim;
using TiaMcpServer.PlcSimWorker.Operations;
using TiaMcpServer.PlcSimWorker.Runtime;
using TiaMcpServer.Safety;

namespace TiaMcpServer.Tests.PlcSim;

/// <summary>Virtual time: <see cref="Sleep"/> advances the clock instantly.</summary>
internal sealed class FakePlcSimClock : IPlcSimClock
{
    public long Now { get; set; }

    public long ElapsedMilliseconds => Now;

    public List<int> Sleeps { get; } = new();

    public void Sleep(int milliseconds)
    {
        Sleeps.Add(milliseconds);
        Now += Math.Max(0, milliseconds);
    }
}

/// <summary>In-memory stand-in for S7-PLCSIM Advanced, used to exercise the worker logic on any OS.</summary>
internal sealed class FakePlcSimRuntime : IPlcSimRuntime
{
    public FakePlcSimRuntime(FakePlcSimClock? clock = null) => Clock = clock ?? new FakePlcSimClock();

    public FakePlcSimClock Clock { get; }

    public Dictionary<string, FakePlcSimInstance> Instances { get; } = new(StringComparer.Ordinal);

    public string? UnavailableReason { get; set; }

    /// <summary>Simulates a cached type-initializer failure observed in this process.</summary>
    public bool RestartRequired { get; set; }

    public int Opened { get; private set; }

    public int Disposed { get; internal set; }

    public FakePlcSimInstance Add(string name)
    {
        var instance = new FakePlcSimInstance(this, name);
        Instances[name] = instance;
        return instance;
    }

    public PlcSimProbeResultInfo Probe() => new()
    {
        Location = new PlcSimApiLocationInfo
        {
            Source = PlcSimApiLocator.SourceEnvironment,
            EnvironmentValue = "fake",
            ApiDirectory = "fake",
            DllPath = "fake/" + PlcSimApiLocator.ApiDllName,
            DllExists = UnavailableReason is null,
            Problem = UnavailableReason
        },
        Loaded = UnavailableReason is null,
        LoadError = UnavailableReason,
        Operations = new List<PlcSimOperationAvailabilityInfo> { new() { Operation = "probe_api", Available = true } }
    };

    public PlcSimInstanceListInfo ListInstances()
    {
        ThrowIfUnavailable();
        var instances = Instances.Values
            .Select(instance => PlcSimValueCodec.Parse(JsonSerializer.Serialize(new { instance.Name, ID = instance.Id })))
            .ToList();
        return new PlcSimInstanceListInfo { IsInitialized = true, IsRuntimeManagerAvailable = true, Count = instances.Count, Instances = instances };
    }

    public IPlcSimInstance Open(string exactName, ICollection<string> warnings)
    {
        ThrowIfUnavailable();
        if (Instances.TryGetValue(exactName, out var instance))
        {
            Opened++;
            instance.Warnings = warnings;
            return instance.Interface();
        }

        if (Instances.Keys.Any(name => string.Equals(name, exactName, StringComparison.OrdinalIgnoreCase)))
            throw new PlcSimOperationException(WorkerFailureCategories.TargetNotFound, $"No PLCSIM instance is named exactly '{exactName}'.");
        throw new PlcSimOperationException(WorkerFailureCategories.PlcSimRuntimeError,
            $"S7-PLCSIM Advanced raised SimulationRuntimeException, RuntimeErrorCode = DoesNotExist (-5) during SimulationRuntimeManager.CreateInterface: '{exactName}'.",
            new PlcSimErrorInfo { ExceptionType = "SimulationRuntimeException", RuntimeErrorCode = "DoesNotExist", RuntimeErrorCodeMember = "RuntimeErrorCode" });
    }

    private void ThrowIfUnavailable()
    {
        if (UnavailableReason is not null)
            throw new PlcSimOperationException(WorkerFailureCategories.PlcSimApiUnavailable, "The S7-PLCSIM Advanced API is unavailable: " + UnavailableReason);
    }
}

internal sealed class FakePlcSimInstance
{
    private readonly FakePlcSimRuntime _runtime;
    private readonly List<(long Due, string Tag, object Value)> _pending = new();

    public FakePlcSimInstance(FakePlcSimRuntime runtime, string name)
    {
        _runtime = runtime;
        Name = name;
    }

    public string Name { get; }

    public int Id { get; set; } = 1;

    public string OperatingState { get; set; } = "Off";

    public double ScaleFactor { get; set; } = 1.0;

    public Dictionary<string, (string DataType, object Value)> Tags { get; } = new(StringComparer.Ordinal);

    /// <summary>Members reported as unavailable (for example <c>IInstance.PowerOn</c>).</summary>
    public HashSet<string> MissingMembers { get; } = new(StringComparer.Ordinal);

    /// <summary>Tags whose access throws a runtime error.</summary>
    public HashSet<string> FailingTags { get; } = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = new();

    public JsonElement? ControlReturnValue { get; set; }

    /// <summary>How the fake judges <see cref="ControlReturnValue"/> (one of <see cref="PlcSimReturnChecks"/>).</summary>
    public string ControlReturnCheck { get; set; } = PlcSimReturnChecks.NotInterpreted;

    public string? ControlReturnCode { get; set; }

    /// <summary>When true, timed control calls resolve to the parameterless fallback (no timeout argument).</summary>
    public bool ControlWithoutTimeout { get; set; }

    /// <summary>Tags whose write returns this (check, code, return value) instead of void.</summary>
    public Dictionary<string, (string Check, string? Code, JsonElement Value)> WriteReturns { get; } = new(StringComparer.Ordinal);

    /// <summary>The projected IInstance.CommunicationInterface; null makes the member unavailable.</summary>
    public string? CommunicationInterface { get; set; } = "Softbus";

    public bool TagInfoNameMissing { get; set; }

    /// <summary>How many TagInfos entries were projected (list_tags projects only up to max).</summary>
    public int Projected { get; set; }

    public ICollection<string>? Warnings { get; set; }

    public FakePlcSimInstance Tag(string name, string dataType, object value)
    {
        Tags[name] = (dataType, value);
        return this;
    }

    /// <summary>After <paramref name="writeTag"/> is written, <paramref name="tag"/> becomes <paramref name="value"/> <paramref name="delayMs"/> later.</summary>
    public Dictionary<string, List<(int DelayMs, string Tag, object Value)>> Reactions { get; } = new(StringComparer.Ordinal);

    public FakePlcSimInstance React(string writeTag, int delayMs, string tag, object value)
    {
        if (!Reactions.TryGetValue(writeTag, out var list))
            Reactions[writeTag] = list = new();
        list.Add((delayMs, tag, value));
        return this;
    }

    public IPlcSimInstance Interface() => new Handle(this);

    private void Apply()
    {
        foreach (var change in _pending.Where(change => change.Due <= _runtime.Clock.Now).ToList())
        {
            Tags[change.Tag] = (Tags[change.Tag].DataType, change.Value);
            _pending.Remove(change);
        }
    }

    private void Require(string member)
    {
        if (MissingMembers.Contains(member))
            throw PlcSimOperationException.MemberMissing(member, $"The loaded S7-PLCSIM Advanced API does not expose {member} with a declared signature.");
    }

    private sealed class Handle : IPlcSimInstance
    {
        private readonly FakePlcSimInstance _state;
        private bool _disposed;

        public Handle(FakePlcSimInstance state) => _state = state;

        public string Name => _state.Name;

        public PlcSimPropertyRead ReadProperty(PlcSimInstanceProperty property)
        {
            var spec = PlcSimApiCatalog.For(property);
            if (_state.MissingMembers.Contains(spec.Id)
                || property == PlcSimInstanceProperty.CommunicationInterface && _state.CommunicationInterface is null)
                return PlcSimPropertyRead.Missing(spec.Id, $"{spec.Id} is not available.");
            object value = property switch
            {
                PlcSimInstanceProperty.Name => _state.Name,
                PlcSimInstanceProperty.Id => _state.Id,
                PlcSimInstanceProperty.OperatingState => _state.OperatingState,
                PlcSimInstanceProperty.OperatingMode => "Default",
                PlcSimInstanceProperty.ScaleFactor => _state.ScaleFactor,
                PlcSimInstanceProperty.CommunicationInterface => _state.CommunicationInterface!,
                _ => "CPU1500_Unspecified"
            };
            return PlcSimPropertyRead.Of(spec.Id, PlcSimValueCodec.ToJson(value));
        }

        public PlcSimInvocation Control(PlcSimControlAction action)
        {
            var spec = PlcSimApiCatalog.For(action);
            _state.Require(spec.Id);
            _state.Calls.Add(spec.Name);
            _state.OperatingState = action switch
            {
                PlcSimControlAction.PowerOn => "Stop",
                PlcSimControlAction.PowerOff => "Off",
                PlcSimControlAction.Run => "Run",
                PlcSimControlAction.Stop => "Stop",
                _ => _state.OperatingState
            };
            var timed = action != PlcSimControlAction.UpdateTagList && !_state.ControlWithoutTimeout;
            return new PlcSimInvocation(spec.Name + (timed ? "(UInt32 in_TimeOut)" : "()"), timed ? PlcSimApiCatalog.ControlTimeoutMs : null,
                _state.ControlReturnValue is null ? "Void" : "ERuntimeErrorCode", _state.ControlReturnValue,
                _state.ControlReturnCheck, _state.ControlReturnCode);
        }

        public PlcSimTagInfoSnapshot ReadTagInfos(string? nameFilter, int max)
        {
            _state.Require(PlcSimApiCatalog.TagInfos.Id);
            const string nameMissing = "STagInfo has no public field or property named Name";
            if (nameFilter is not null && _state.TagInfoNameMissing)
                return new PlcSimTagInfoSnapshot(Array.Empty<JsonElement>(), 0, 0, nameMissing);
            var names = _state.Tags.Keys.OrderBy(name => name, StringComparer.Ordinal).ToList();
            var matched = names.Where(name => nameFilter is null || name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            _state.Projected += Math.Min(max, matched.Count);
            var tags = matched.Take(max)
                .Select(name => PlcSimValueCodec.Parse(JsonSerializer.Serialize(new { Name = name, DataType = _state.Tags[name].DataType })))
                .ToList();
            return new PlcSimTagInfoSnapshot(tags, names.Count, matched.Count, _state.TagInfoNameMissing ? nameMissing : null);
        }

        public PlcSimPropertyRead ReadTagListStatus()
            => _state.MissingMembers.Contains(PlcSimApiCatalog.GetTagListStatus.Id)
                ? PlcSimPropertyRead.Missing(PlcSimApiCatalog.GetTagListStatus.Id, "IInstance.GetTagListStatus is not available.")
                : PlcSimPropertyRead.Of(PlcSimApiCatalog.GetTagListStatus.Id, PlcSimValueCodec.Parse("{\"details\":\"IOMCTDB\",\"isHMIVisibleOnly\":true}"));

        public object Read(string tag, string dataType)
        {
            _state.Require("IInstance." + PlcSimDataTypes.ReadMethod(dataType));
            _state.Apply();
            _state.Calls.Add($"Read {tag}");
            if (_state.FailingTags.Contains(tag) || !_state.Tags.TryGetValue(tag, out var entry))
                throw new PlcSimOperationException(WorkerFailureCategories.PlcSimRuntimeError, $"S7-PLCSIM Advanced raised SimulationRuntimeException during IInstance.{PlcSimDataTypes.ReadMethod(dataType)}: '{tag}'.",
                    new PlcSimErrorInfo { ExceptionType = "SimulationRuntimeException", RuntimeErrorCode = "DoesNotExist", RuntimeErrorCodeMember = "RuntimeErrorCode" });
            if (!PlcSimValueCodec.TryFromApiValue(dataType, entry.Value, out var typed, out var error))
                throw new PlcSimOperationException(WorkerFailureCategories.WorkerOperationFailed, error!);
            return typed!;
        }

        public PlcSimInvocation Write(string tag, string dataType, object value)
        {
            _state.Require("IInstance." + PlcSimDataTypes.WriteMethod(dataType));
            _state.Calls.Add($"Write {tag}");
            if (_state.FailingTags.Contains(tag) || !_state.Tags.ContainsKey(tag))
                throw new PlcSimOperationException(WorkerFailureCategories.PlcSimRuntimeError, $"S7-PLCSIM Advanced raised SimulationRuntimeException during IInstance.{PlcSimDataTypes.WriteMethod(dataType)}: '{tag}'.",
                    new PlcSimErrorInfo { ExceptionType = "SimulationRuntimeException", RuntimeErrorCode = "DoesNotExist", RuntimeErrorCodeMember = "RuntimeErrorCode" });
            _state.Tags[tag] = (_state.Tags[tag].DataType, value);
            if (_state.Reactions.TryGetValue(tag, out var reactions))
                foreach (var (delay, target, result) in reactions)
                    _state._pending.Add((_state._runtime.Clock.Now + delay, target, result));
            if (_state.WriteReturns.TryGetValue(tag, out var returned))
                return new PlcSimInvocation($"ERuntimeErrorCode {PlcSimDataTypes.WriteMethod(dataType)}(String in_Tag, {value.GetType().Name} in_Value)", null,
                    "ERuntimeErrorCode", returned.Value, returned.Check, returned.Code);
            return new PlcSimInvocation($"Void {PlcSimDataTypes.WriteMethod(dataType)}(String in_Tag, {value.GetType().Name} in_Value)", null, "Void", null);
        }

        public void SetScaleFactor(double value)
        {
            _state.Require(PlcSimApiCatalog.ScaleFactor.Id);
            _state.ScaleFactor = value;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _state._runtime.Disposed++;
        }
    }
}

/// <summary>Routes host requests through the real worker dispatcher in-process, as the worker process would.</summary>
internal sealed class InProcessPlcSimChannel : IPlcSimWorkerChannel
{
    private readonly PlcSimWorkerDispatcher _dispatcher;

    public InProcessPlcSimChannel(McpAccessMode mode, IPlcSimRuntime runtime, IPlcSimClock clock)
        => _dispatcher = new PlcSimWorkerDispatcher(mode, new PlcSimOperationHandler(runtime, clock));

    public List<WorkerRequest> Requests { get; } = new();

    public bool Disposed { get; private set; }

    public Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        // Same wire format as the real worker: the request is serialized and parsed again.
        request.ProtocolVersion = PlcSimWorkerProtocol.Version;
        Requests.Add(request);
        var line = JsonSerializer.Serialize(request, TiaMcpServer.Contracts.Json.WorkerJson.Request);
        var response = _dispatcher.HandleLine(line);
        var wire = JsonSerializer.Serialize(response, TiaMcpServer.Contracts.Json.WorkerJson.Envelope);
        return Task.FromResult(JsonSerializer.Deserialize<WorkerResponse>(wire, TiaMcpServer.Contracts.Json.WorkerJson.Envelope)!);
    }

    public void Dispose() => Disposed = true;
}

internal static class PlcSimTestHost
{
    public static (PlcSimWorkerClient Client, InProcessPlcSimChannel Channel) Create(FakePlcSimRuntime runtime, McpAccessMode mode = McpAccessMode.ReadWrite,
        TimeSpan? requestTimeout = null)
    {
        var channel = new InProcessPlcSimChannel(mode, runtime, runtime.Clock);
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(mode), requestTimeout, channelFactory: () => channel);
        return (client, channel);
    }

    public static FakePlcSimRuntime Standard()
    {
        var runtime = new FakePlcSimRuntime();
        runtime.Add("Line1")
            .Tag("Start", PlcSimDataTypes.Bool, false)
            .Tag("Running", PlcSimDataTypes.Bool, false)
            .Tag("Speed", PlcSimDataTypes.Int16, (short)0)
            .Tag("Temperature", PlcSimDataTypes.Float, 21.5f)
            .Tag("Label", PlcSimDataTypes.String, "Ready");
        return runtime;
    }
}
