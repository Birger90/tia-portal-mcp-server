using System.Diagnostics;
using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.PlcSimWorker.Runtime;

/// <summary>Instance properties <c>get_instance</c> reports; each maps to one catalog member.</summary>
internal enum PlcSimInstanceProperty
{
    Name,
    Id,
    OperatingState,
    OperatingMode,
    ScaleFactor,
    CommunicationInterface,
    CpuType
}

/// <summary>Parameterless instance control calls of <c>plcsim_write</c>.</summary>
internal enum PlcSimControlAction
{
    PowerOn,
    PowerOff,
    Run,
    Stop,
    MemoryReset,
    UpdateTagList
}

/// <summary>
/// The Siemens-free view of S7-PLCSIM Advanced the operation logic is written against. The
/// production implementation binds the real API by reflection (<see cref="ReflectionPlcSimRuntime"/>);
/// tests use an in-memory fake. Implementations report failures as <see cref="PlcSimOperationException"/>.
/// </summary>
internal interface IPlcSimRuntime
{
    /// <summary>Locates and (when possible) loads the API and reports every catalog member. Never throws for a missing API.</summary>
    PlcSimProbeResultInfo Probe();

    PlcSimInstanceListInfo ListInstances();

    /// <summary>
    /// Opens the registered instance whose name equals <paramref name="exactName"/> ordinally. A
    /// name the API resolves to a differently spelled instance is <c>target_not_found</c>.
    /// </summary>
    IPlcSimInstance Open(string exactName, ICollection<string> warnings);

    /// <summary>
    /// True once this process observed a failure only a new process can retry (a failed, cached
    /// type initializer of the API); the worker then tells the host to recycle it.
    /// </summary>
    bool RestartRequired { get; }
}

/// <summary>One opened instance interface; disposing it releases the interface, never the instance.</summary>
internal interface IPlcSimInstance : IDisposable
{
    string Name { get; }

    PlcSimPropertyRead ReadProperty(PlcSimInstanceProperty property);

    PlcSimInvocation Control(PlcSimControlAction action);

    /// <summary>
    /// <c>TagInfos</c> counted in full, filtered by case-insensitive name substring when
    /// <paramref name="nameFilter"/> is set (needs the name member), projected up to <paramref name="max"/> matches.
    /// </summary>
    PlcSimTagInfoSnapshot ReadTagInfos(string? nameFilter, int max);

    PlcSimPropertyRead ReadTagListStatus();

    /// <summary>Reads one tag with the typed method of <paramref name="dataType"/>; returns the CLR type of the data type.</summary>
    object Read(string tag, string dataType);

    PlcSimInvocation Write(string tag, string dataType, object value);

    void SetScaleFactor(double value);
}

/// <summary>The value of one reflective read, or why it is absent.</summary>
internal sealed record PlcSimPropertyRead(bool Available, JsonElement? Value, string Member, string? Problem)
{
    public static PlcSimPropertyRead Of(string member, JsonElement? value) => new(true, value, member, null);

    public static PlcSimPropertyRead Missing(string member, string problem) => new(false, null, member, problem);
}

/// <summary>
/// What one API invocation did: the overload used, any timeout argument, the projected return value
/// and how that value was judged (<see cref="PlcSimReturnChecks"/>): a negative
/// <c>ERuntimeErrorCode</c> of the loaded API, or a <c>Boolean</c> <c>false</c>, is a failure; a
/// positive code is a warning. <see cref="ReturnCodeValue"/> is the code's numeric value.
/// </summary>
internal sealed record PlcSimInvocation(string Signature, int? TimeoutArgumentMs, string ReturnType, JsonElement? ReturnValue,
    string Check = PlcSimReturnChecks.NotInterpreted, string? ReturnCode = null, long? ReturnCodeValue = null, bool ZeroFallback = false)
{
    public bool Failed => Check == PlcSimReturnChecks.Failed;

    private string CodeText => (ReturnCode ?? ReturnValue?.GetRawText() ?? "null")
        + (ReturnCodeValue is { } value && !string.Equals(ReturnCode, value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            ? $" ({value})" : string.Empty);

    /// <summary>The <c>plcsim_runtime_error</c> describing a failed return, naming the code.</summary>
    public PlcSimOperationException Failure(string action)
        => new(WorkerFailureCategories.PlcSimRuntimeError,
            $"{Signature} returned {CodeText} ({ReturnType}) during {action}: the S7-PLCSIM Advanced API reported an error.",
            new PlcSimErrorInfo { RuntimeErrorCode = ReturnCode });

    /// <summary>The warning for a warning code, or a return value that is reported but not (or not reliably) judged; null when none applies.</summary>
    public string? ReturnWarning(string action) => Check switch
    {
        PlcSimReturnChecks.Ok when ZeroFallback =>
            $"{Signature} returned {CodeText} ({ReturnType}) during {action}. The loaded {ReturnType} enum has no member named OK, so zero is "
            + "assumed to mean success; confirm the success code with probe_api (runtimeErrorCode.okMemberFound and the enum values).",
        PlcSimReturnChecks.Warning =>
            $"{Signature} returned the warning code {CodeText} ({ReturnType}) during {action}. S7-PLCSIM Advanced reports warnings as positive codes; "
            + "the call is treated as successful.",
        PlcSimReturnChecks.Uninterpretable =>
            $"{Signature} returned {CodeText} ({ReturnType}) during {action}, which is neither the OK value nor signed (an unsigned enum, or zero "
            + "while OK has another value), so success could not be interpreted; check the instance state.",
        PlcSimReturnChecks.NotInterpreted when ReturnValue is not null =>
            $"{Signature} returned {ReturnValue.Value.GetRawText()} ({ReturnType}) during {action}; the return value is reported, not interpreted.",
        _ => null,
    };
}

/// <summary>
/// The first matching <c>TagInfos</c> entries, projected; every entry counted in <see cref="TotalCount"/>,
/// every match in <see cref="MatchedCount"/>; why the name member is unavailable, if it is.
/// </summary>
internal sealed record PlcSimTagInfoSnapshot(IReadOnlyList<JsonElement> Tags, int TotalCount, int MatchedCount, string? NameUnavailableReason);

/// <summary>A categorized PLCSIM failure; <see cref="Detail"/> is the structured form carried in results.</summary>
internal sealed class PlcSimOperationException : Exception
{
    public PlcSimOperationException(string category, string message, PlcSimErrorInfo? detail = null)
        : base(message)
    {
        if (!WorkerFailureCategories.IsKnown(category))
            throw new ArgumentException($"'{category}' is not an approved failure category.", nameof(category));
        Category = category;
        Detail = detail ?? new PlcSimErrorInfo();
        Detail.Category = category;
        Detail.Message = message;
    }

    public string Category { get; }

    public PlcSimErrorInfo Detail { get; }

    public static PlcSimOperationException MemberMissing(string member, string message)
        => new(WorkerFailureCategories.PlcSimApiMemberMissing, message, new PlcSimErrorInfo { Member = member });
}

/// <summary>Monotonic time for the scenario runner, so tests can run on virtual time.</summary>
internal interface IPlcSimClock
{
    long ElapsedMilliseconds { get; }

    void Sleep(int milliseconds);
}

internal sealed class SystemPlcSimClock : IPlcSimClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    public void Sleep(int milliseconds)
    {
        if (milliseconds > 0)
            Thread.Sleep(milliseconds);
    }
}
