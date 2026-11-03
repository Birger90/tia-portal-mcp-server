using System.Text.Json;

namespace TiaMcpServer.Contracts.PlcSim;

// Worker payload contracts of the PLCSIM worker. Every member is written, null or not, and the
// host decodes each payload with the required-member worker-payload reader. Values read from the
// Siemens API whose CLR type is not verified (instance properties, SInstanceInfo, STagInfo, return
// values) are carried as reflective projections: JSON scalars, enum names as strings, or objects of
// all public fields/properties up to depth 2. Their member names come from the loaded assembly.

/// <summary>How the PLCSIM Advanced API directory was (or was not) resolved.</summary>
public sealed class PlcSimApiLocationInfo
{
    /// <summary><c>environment</c>, <c>registry</c> or <c>none</c>.</summary>
    public string Source { get; set; } = PlcSimApiLocator.SourceNone;

    public string EnvironmentVariable { get; set; } = PlcSimApiLocator.EnvironmentVariable;

    /// <summary>Raw environment value when set (an explicit override disables the registry lookup).</summary>
    public string? EnvironmentValue { get; set; }

    public string RegistryKey { get; set; } = PlcSimApiLocator.RegistryKeyDisplay;

    public string RegistryValueName { get; set; } = PlcSimApiLocator.RegistryValueName;

    /// <summary>The registry <c>Path</c> value, when the registry was consulted and the value exists.</summary>
    public string? RegistryPath { get; set; }

    /// <summary>Subfolder names found under <c>API</c>, in ordinal order.</summary>
    public List<string> VersionDirectories { get; set; } = new();

    /// <summary>The highest subfolder name that parses as a version, or null.</summary>
    public string? SelectedVersion { get; set; }

    public string? ApiDirectory { get; set; }

    public string? DllPath { get; set; }

    public bool DllExists { get; set; }

    /// <summary>Why no usable DLL was found; null when <see cref="DllExists"/> is true.</summary>
    public string? Problem { get; set; }
}

/// <summary>Result of <c>probe_api</c>: what was loaded and how every used member resolved.</summary>
public sealed class PlcSimProbeResultInfo
{
    public PlcSimApiLocationInfo Location { get; set; } = new();

    public bool Loaded { get; set; }

    public string? LoadError { get; set; }

    public string? AssemblyName { get; set; }

    public string? AssemblyVersion { get; set; }

    public string? FileVersion { get; set; }

    public string? ProductVersion { get; set; }

    public bool? IsInitialized { get; set; }

    public bool? IsRuntimeManagerAvailable { get; set; }

    /// <summary>Failure while reading the two manager flags, if any.</summary>
    public string? ManagerStateError { get; set; }

    /// <summary>
    /// True when a type initializer of the loaded API failed in this worker process (for example the
    /// static <c>SimulationRuntimeManager</c> initializer while the Runtime Manager was not running).
    /// .NET keeps that failure for the process lifetime, so the host restarts the PLCSIM worker
    /// after this result.
    /// </summary>
    public bool ApiInitializationFailed { get; set; }

    public List<PlcSimApiMemberInfo> Members { get; set; } = new();

    public List<PlcSimApiTypeInfo> Types { get; set; } = new();

    public PlcSimErrorCodeMemberInfo? RuntimeErrorCode { get; set; }

    public List<PlcSimOperationAvailabilityInfo> Operations { get; set; } = new();
}

public sealed class PlcSimApiMemberInfo
{
    /// <summary>For example <c>IInstance.PowerOn</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>staticProperty</c>, <c>staticMethod</c>, <c>instanceProperty</c>, <c>instanceMethod</c> or <c>dataMember</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Where the expectation comes from; see the PLCSIM operations reference.</summary>
    public string Provenance { get; set; } = string.Empty;

    /// <summary>Declared signature candidates, in resolution order.</summary>
    public List<string> Candidates { get; set; } = new();

    public bool Resolved { get; set; }

    public string? ResolvedSignature { get; set; }

    /// <summary>Why the member did not resolve, or what was observed instead.</summary>
    public string? Detail { get; set; }

    public List<string> UsedBy { get; set; } = new();
}

public sealed class PlcSimApiTypeInfo
{
    public string Id { get; set; } = string.Empty;

    /// <summary>How the type was found: by its declared full name, or as the type of another member.</summary>
    public string Source { get; set; } = string.Empty;

    public bool Found { get; set; }

    public string? FullName { get; set; }

    /// <summary><c>enum</c>, <c>struct</c>, <c>class</c> or <c>interface</c>.</summary>
    public string? Kind { get; set; }

    public List<string> EnumNames { get; set; } = new();

    /// <summary>Enum members with their numeric values (decimal text of the underlying type); empty for other kinds.</summary>
    public List<PlcSimApiEnumMemberInfo> EnumMembers { get; set; } = new();

    public List<PlcSimApiTypeMemberInfo> Members { get; set; } = new();
}

public sealed class PlcSimApiEnumMemberInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The numeric value as decimal text (it may be negative or exceed 64-bit signed range).</summary>
    public string Value { get; set; } = string.Empty;
}

public sealed class PlcSimApiTypeMemberInfo
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;
}

/// <summary>The public member of <c>SimulationRuntimeException</c> whose type is <c>ERuntimeErrorCode</c>.</summary>
public sealed class PlcSimErrorCodeMemberInfo
{
    public string ExceptionType { get; set; } = string.Empty;

    public bool ExceptionTypeFound { get; set; }

    public string? MemberName { get; set; }

    public string? MemberKind { get; set; }

    public string? Detail { get; set; }

    /// <summary>
    /// Whether the loaded <c>ERuntimeErrorCode</c> enum has a member named exactly <c>OK</c> (verified
    /// only here at runtime; the native success code is <c>SREC_OK</c>). That member's value is
    /// success; without it zero is assumed to be success; otherwise the sign decides. Null when the
    /// enum was not found.
    /// </summary>
    public bool? OkMemberFound { get; set; }
}

public sealed class PlcSimOperationAvailabilityInfo
{
    public string Operation { get; set; } = string.Empty;

    public bool Available { get; set; }

    public List<string> MissingMembers { get; set; } = new();
}

/// <summary>Structured detail of a failure inside a result (one tag or one step).</summary>
public sealed class PlcSimErrorInfo
{
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Exception type name raised by the API, when one was.</summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// The ERuntimeErrorCode value name, when the exception exposes exactly one such member, or the
    /// negative code (or <c>False</c>) a write or control call returned.
    /// </summary>
    public string? RuntimeErrorCode { get; set; }

    /// <summary>The name of the exception member the error code was read from.</summary>
    public string? RuntimeErrorCodeMember { get; set; }

    /// <summary>The catalog member that was missing, for <c>plcsim_api_member_missing</c>.</summary>
    public string? Member { get; set; }
}

public sealed class PlcSimInstanceListInfo
{
    public bool? IsInitialized { get; set; }

    public bool? IsRuntimeManagerAvailable { get; set; }

    public int Count { get; set; }

    /// <summary>Reflective projections of <c>RegisteredInstanceInfo</c> elements.</summary>
    public List<JsonElement> Instances { get; set; } = new();
}

public sealed class PlcSimInstanceInfo
{
    public string RequestedName { get; set; } = string.Empty;

    public JsonElement? Name { get; set; }

    public JsonElement? Id { get; set; }

    public JsonElement? OperatingState { get; set; }

    public JsonElement? OperatingMode { get; set; }

    public JsonElement? ScaleFactor { get; set; }

    public JsonElement? CommunicationInterface { get; set; }

    public JsonElement? CpuType { get; set; }
}

public sealed class PlcSimTagListInfo
{
    public string Instance { get; set; } = string.Empty;

    public string? NameFilter { get; set; }

    public int Max { get; set; }

    /// <summary>Entries in <c>TagInfos</c> (the tag list is not refreshed by list_tags).</summary>
    public int TotalCount { get; set; }

    public int MatchedCount { get; set; }

    public int ReturnedCount { get; set; }

    /// <summary>True when fewer tags were returned than matched (by max or by the response budget).</summary>
    public bool Truncated { get; set; }

    /// <summary>True when the host dropped trailing tags so the result fits the per-value budget.</summary>
    public bool BudgetTrimmed { get; set; }

    /// <summary>Reflective projection of the GetTagListStatus out values, or null when unavailable.</summary>
    public JsonElement? TagListStatus { get; set; }

    /// <summary>Reflective projections of <c>STagInfo</c> elements.</summary>
    public List<JsonElement> Tags { get; set; } = new();
}

public sealed class PlcSimTagReadListInfo
{
    public string Instance { get; set; } = string.Empty;

    public int ReadCount { get; set; }

    public int FailedCount { get; set; }

    public List<PlcSimTagValueInfo> Tags { get; set; } = new();
}

public sealed class PlcSimTagValueInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    /// <summary><c>read</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public JsonElement? Value { get; set; }

    public PlcSimErrorInfo? Error { get; set; }
}

public sealed class PlcSimTagWriteListInfo
{
    public string Instance { get; set; } = string.Empty;

    public int WrittenCount { get; set; }

    public int FailedCount { get; set; }

    public int SkippedCount { get; set; }

    public List<PlcSimTagWriteInfo> Tags { get; set; } = new();
}

public sealed class PlcSimTagWriteInfo
{
    public string Name { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    /// <summary>The typed value written (or that would have been written), as JSON.</summary>
    public JsonElement Value { get; set; }

    /// <summary><c>written</c>, <c>failed</c> or <c>skipped</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string? InvokedSignature { get; set; }

    /// <summary>Projection of a non-void return value; a negative <c>ERuntimeErrorCode</c> or a <c>false</c> fails the tag.</summary>
    public JsonElement? ReturnValue { get; set; }

    public PlcSimErrorInfo? Error { get; set; }
}

public sealed class PlcSimControlResultInfo
{
    public string Instance { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string InvokedSignature { get; set; } = string.Empty;

    /// <summary>The timeout passed when the resolved overload takes one, otherwise null.</summary>
    public int? TimeoutArgumentMs { get; set; }

    public string ReturnType { get; set; } = string.Empty;

    /// <summary>Projection of a non-void return value.</summary>
    public JsonElement? ReturnValue { get; set; }

    /// <summary>How the return value was judged: one of <see cref="PlcSimReturnChecks"/>. <c>failed</c> fails the item.</summary>
    public string ReturnCheck { get; set; } = PlcSimReturnChecks.NotInterpreted;

    /// <summary>The returned <c>ERuntimeErrorCode</c> name (or number when undefined), <c>False</c> for a Boolean failure; otherwise null.</summary>
    public string? ReturnCode { get; set; }

    public JsonElement? OperatingStateBefore { get; set; }

    public JsonElement? OperatingStateAfter { get; set; }
}

public sealed class PlcSimScaleFactorResultInfo
{
    public string Instance { get; set; } = string.Empty;

    public double Requested { get; set; }

    public JsonElement? Previous { get; set; }

    public JsonElement? Current { get; set; }
}

public sealed class PlcSimTestResultInfo
{
    public string Instance { get; set; } = string.Empty;

    /// <summary>All expect steps passed and no step ended in error.</summary>
    public bool Passed { get; set; }

    public bool Aborted { get; set; }

    public string? AbortReason { get; set; }

    public bool ContinueOnFailure { get; set; }

    public int MaxDurationMs { get; set; }

    public long DurationMs { get; set; }

    /// <summary>Always <c>wallClock</c>: no virtual-time synchronization with the simulated CPU.</summary>
    public string TimingModel { get; set; } = "wallClock";

    public PlcSimTestSummaryInfo Summary { get; set; } = new();

    public List<PlcSimTestStepResultInfo> Steps { get; set; } = new();
}

public sealed class PlcSimTestSummaryInfo
{
    public int Total { get; set; }

    public int Passed { get; set; }

    public int Failed { get; set; }

    public int Errors { get; set; }

    public int Skipped { get; set; }

    public int Expects { get; set; }

    public int ExpectsPassed { get; set; }
}

public sealed class PlcSimTestStepResultInfo
{
    public int Index { get; set; }

    public string? Name { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string? Tag { get; set; }

    public string? DataType { get; set; }

    /// <summary><c>passed</c>, <c>failed</c>, <c>error</c> or <c>skipped</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The last value read by an expect step, as JSON.</summary>
    public JsonElement? LastObserved { get; set; }

    public int Attempts { get; set; }

    public long StartOffsetMs { get; set; }

    public long ElapsedMs { get; set; }

    public string? Message { get; set; }

    public PlcSimErrorInfo? Error { get; set; }
}

/// <summary>
/// How a write or control call's return value was judged. Only the loaded API's
/// <c>ERuntimeErrorCode</c> (the member named exactly <c>OK</c> succeeds, or zero when there is no such
/// member; otherwise negative is an error, positive a warning) and <c>Boolean</c> (success:
/// <c>true</c>) are interpreted. Per the Siemens developer reference (API 8.0) only
/// <c>PowerOn</c> returns <c>ERuntimeErrorCode</c>; the other control and write methods return void.
/// </summary>
public static class PlcSimReturnChecks
{
    /// <summary>Void, or a return type that is not interpreted.</summary>
    public const string NotInterpreted = "notInterpreted";

    public const string Ok = "ok";

    /// <summary>A positive <c>ERuntimeErrorCode</c>: a warning; the call succeeded.</summary>
    public const string Warning = "warning";

    /// <summary>A negative <c>ERuntimeErrorCode</c>, or <c>false</c>: the call failed.</summary>
    public const string Failed = "failed";

    /// <summary>A non-<c>OK</c> code without a usable sign (unsigned enum, or zero while <c>OK</c> has another value): success could not be judged.</summary>
    public const string Uninterpretable = "uninterpretable";

    public static bool IsKnown(string? check) => check is NotInterpreted or Ok or Warning or Failed or Uninterpretable;
}
