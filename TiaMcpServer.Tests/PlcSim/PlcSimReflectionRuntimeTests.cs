using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcSimWorker.Runtime;
using Full = TiaMcpServer.Tests.PlcSim.FakeApiFull;
using Xunit;

namespace TiaMcpServer.Tests.PlcSim;

/// <summary>
/// The reflection binding, exercised against stand-in types reached through the same full-name
/// lookup the worker uses on the real assembly (namespace mapped to a test namespace).
/// </summary>
public sealed class PlcSimReflectionRuntimeTests
{
    internal static Func<string, Type?> Lookup(string testNamespace)
        => name => name.StartsWith(PlcSimApiCatalog.Namespace + ".", StringComparison.Ordinal)
            ? typeof(PlcSimReflectionRuntimeTests).Assembly.GetType(testNamespace + "." + name.Substring(PlcSimApiCatalog.Namespace.Length + 1))
            : null;

    private const string FullNamespace = "TiaMcpServer.Tests.PlcSim.FakeApiFull";
    private const string SparseNamespace = "TiaMcpServer.Tests.PlcSim.FakeApiSparse";
    private const string NoOkNamespace = "TiaMcpServer.Tests.PlcSim.FakeApiNoOk";
    private const string UnsignedNamespace = "TiaMcpServer.Tests.PlcSim.FakeApiUnsigned";
    private const string OkNonZeroNamespace = "TiaMcpServer.Tests.PlcSim.FakeApiOkNonZero";

    private static JsonElement Json(string text) => PlcSimValueCodec.Parse(text);

    private static ReflectionPlcSimRuntime Runtime(string ns, Action<PlcSimLoadedApi>? observe = null)
        => new(() => new PlcSimApiLocationInfo { Source = "environment", DllExists = true, DllPath = "x.dll", ApiDirectory = "." },
            location =>
            {
                var api = new PlcSimLoadedApi { Location = location, Lookup = Lookup(ns), AssemblyName = "Fake", FileVersion = "6.0.1.2" };
                observe?.Invoke(api);
                return api;
            });

    private static Full.FakeInstance Arrange()
    {
        var state = new Full.FakeApiState();
        Full.FakeApiState.Current = state;
        var instance = new Full.FakeInstance { Name = "Line1" };
        instance.Values["Run"] = true;
        instance.Values["Speed"] = (short)12;
        instance.Values["Temp"] = 21.5f;
        instance.Values["Label"] = "Ready";
        instance.TagInfos = new[]
        {
            new Full.STagInfo { Name = "Run", Area = Full.EArea.Marker, Offset = 1, Bit = 2, Detail = new Full.Nested { Level = 1, Deeper = new Full.Deeper { Value = 2 } } },
            new Full.STagInfo { Name = "Speed", Area = Full.EArea.DataBlock },
        };
        state.Instances["Line1"] = instance;
        return instance;
    }

    [Fact]
    public void Surface_ResolvesDeclaredCandidatesInOrder()
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(FullNamespace));
        Assert.All(PlcSimApiCatalog.Members, spec => Assert.True(surface[spec].Resolved, spec.Id + ": " + surface[spec].Detail));

        var powerOn = surface[PlcSimApiCatalog.PowerOn];
        Assert.Equal(typeof(uint), Assert.Single(powerOn.Method!.GetParameters()).ParameterType);
        Assert.Equal("ERuntimeErrorCode PowerOn(UInt32 in_TimeOut)", powerOn.Signature);

        // Run, Stop, PowerOff and MemoryReset declare the (UInt32) timeout overload first, like PowerOn.
        var run = surface[PlcSimApiCatalog.Run];
        Assert.False(run.Candidate!.CallEquivalent);
        Assert.Equal(typeof(uint), Assert.Single(run.Method!.GetParameters()).ParameterType);
        Assert.True(surface[PlcSimApiCatalog.Stop].Candidate!.CallEquivalent, "Stop() has no UInt32 overload; the C# call shape is the fallback");
        Assert.Equal("ERuntimeErrorCode MemoryReset(UInt32 in_TimeOut)", surface[PlcSimApiCatalog.MemoryReset].Signature);
        foreach (var timed in new[] { PlcSimApiCatalog.PowerOn, PlcSimApiCatalog.PowerOff, PlcSimApiCatalog.Run, PlcSimApiCatalog.Stop, PlcSimApiCatalog.MemoryReset })
        {
            Assert.Equal(2, timed.Candidates.Count);
            Assert.Equal(typeof(uint), Assert.Single(timed.Candidates[0].Parameters).ClrType);
            Assert.Contains("developer reference API 8.0", timed.Candidates[0].Source);
            Assert.Equal(PlcSimProvenance.ReferenceSignature, timed.Provenance);
            Assert.True(timed.Candidates[1].CallEquivalent);
        }

        Assert.Single(PlcSimApiCatalog.UpdateTagList.Candidates);
        Assert.Equal(true, surface.HasOkMember);
        Assert.Equal("ref ETagListDetails", PlcSimObjectProjector.TypeName(surface[PlcSimApiCatalog.GetTagListStatus].Method!.GetParameters()[0].ParameterType));
        Assert.Equal("Void Dispose()", surface[PlcSimApiCatalog.Dispose].Signature);
        Assert.Equal(typeof(Full.STagInfo), surface.TagInfoType);
        Assert.Equal(typeof(Full.SInstanceInfo), surface.InstanceInfoType);
        Assert.Equal("RuntimeErrorCode", surface.ErrorCodeMember(typeof(Full.SimulationRuntimeException)).Member!.Name);
    }

    [Fact]
    public void Surface_NeverGuessesBeyondTheCandidates()
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(SparseNamespace));

        var powerOn = surface[PlcSimApiCatalog.PowerOn];
        Assert.True(powerOn.Resolved);
        Assert.True(powerOn.Candidate!.CallEquivalent, "PowerOn(Int32) is not the declared PowerOn(UInt32); only the all-optional C# call shape matches");

        var stop = surface[PlcSimApiCatalog.Stop];
        Assert.False(stop.Resolved);
        Assert.Contains("ambiguous", stop.Detail);
        Assert.Equal(true, surface.HasOkMember);

        var readInt16 = surface[PlcSimApiCatalog.Reads["int16"]];
        Assert.False(readInt16.Resolved);
        Assert.Contains("Int32 ReadInt16(String in_Tag)", readInt16.Detail);

        Assert.False(surface[PlcSimApiCatalog.MemoryReset].Resolved);
        Assert.Contains("no public instance method named MemoryReset", surface[PlcSimApiCatalog.MemoryReset].Detail);
        Assert.False(surface[PlcSimApiCatalog.Dispose].Resolved);
        Assert.False(surface[PlcSimApiCatalog.IsInitialized].Resolved);
        Assert.Contains("has type Int32", surface[PlcSimApiCatalog.TagInfoName].Detail);
        Assert.Null(surface.ErrorCodeType is null ? null : surface.ErrorCodeMember(typeof(FakeApiSparse.SimulationRuntimeException)).Member);

        var missing = Assert.Throws<PlcSimOperationException>(() => surface.Require(PlcSimApiCatalog.MemoryReset));
        Assert.Equal(WorkerFailureCategories.PlcSimApiMemberMissing, missing.Category);
        Assert.Equal("IInstance.MemoryReset", missing.Detail.Member);
        Assert.Contains("probe_api", missing.Message);

        var none = PlcSimApiSurface.Resolve(_ => null);
        Assert.All(PlcSimApiCatalog.Members, spec => Assert.False(none[spec].Resolved));
        Assert.Null(none.HasOkMember);
        Assert.Equal((PlcSimReturnChecks.NotInterpreted, (string?)null, (long?)null), none.InterpretReturn(typeof(int), 3));
        Assert.Contains("was not found", none[PlcSimApiCatalog.PowerOn].Detail);
    }

    [Fact]
    public void Probe_ReportsLocationVersionsMembersTypesAndAvailability()
    {
        Arrange();
        var probe = Runtime(FullNamespace).Probe();

        Assert.True(probe.Loaded);
        Assert.Equal("6.0.1.2", probe.FileVersion);
        Assert.True(probe.IsInitialized);
        Assert.True(probe.IsRuntimeManagerAvailable);
        Assert.Null(probe.ManagerStateError);
        Assert.Equal(PlcSimApiCatalog.Members.Count, probe.Members.Count);
        Assert.All(probe.Members, member => Assert.True(member.Resolved, member.Id));
        var powerOn = probe.Members.Single(member => member.Id == "IInstance.PowerOn");
        Assert.Equal(PlcSimProvenance.ReferenceSignature, powerOn.Provenance);
        Assert.Equal(PlcSimProvenance.ManualName, probe.Members.Single(member => member.Id == "SimulationRuntimeManager.IsInitialized").Provenance);
        Assert.Equal(2, powerOn.Candidates.Count);
        Assert.StartsWith("PowerOn(UInt32)", powerOn.Candidates[0]);
        Assert.Contains("power_on", powerOn.UsedBy);
        Assert.Equal(PlcSimProvenance.ManualSignature, probe.Members.Single(member => member.Id == "SimulationRuntimeManager.CreateInterface").Provenance);
        Assert.Equal(PlcSimProvenance.SecondaryHint, probe.Members.Single(member => member.Id == "STagInfo.Name").Provenance);

        var tagInfo = probe.Types.Single(type => type.Id == "STagInfo");
        Assert.Equal("struct", tagInfo.Kind);
        Assert.Contains(tagInfo.Members, member => member.Name == "Name" && member.Type == "String");
        var state = probe.Types.Single(type => type.Id == "type of IInstance.OperatingState");
        Assert.Equal(new[] { "Off", "Booting", "Stop", "Run" }, state.EnumNames);
        Assert.Equal("RuntimeErrorCode", probe.RuntimeErrorCode!.MemberName);
        Assert.True(probe.RuntimeErrorCode.OkMemberFound);
        Assert.False(probe.ApiInitializationFailed);
        Assert.All(probe.Operations, operation => Assert.True(operation.Available, operation.Operation));
    }

    [Fact]
    public void Probe_AndCalls_ReportAFailedTypeInitializerAsApiUnavailable()
    {
        Arrange();
        Full.FakeApiState.Current.ManagerTypeInitializationFails = true;
        var runtime = Runtime(FullNamespace);

        var probe = runtime.Probe();
        Assert.True(probe.Loaded);
        Assert.True(probe.ApiInitializationFailed);
        Assert.Null(probe.IsInitialized);
        Assert.Contains(WorkerFailureCategories.PlcSimApiUnavailable, probe.ManagerStateError);

        var listed = Assert.Throws<PlcSimOperationException>(() => runtime.ListInstances());
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, listed.Category);
        Assert.Equal("SimulationInitializationException", listed.Detail.ExceptionType);
        Assert.Contains("restarted", listed.Message);
        Assert.Contains("Runtime Manager", listed.Message);
        Assert.True(runtime.RestartRequired);
    }

    [Fact]
    public void AMissingApi_DoesNotRequireARestart()
    {
        var runtime = new ReflectionPlcSimRuntime(() => new PlcSimApiLocationInfo { Problem = PlcSimApiLocator.NotInstalledProblem },
            location => new PlcSimLoadedApi { Location = location, LoadError = location.Problem });
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, Assert.Throws<PlcSimOperationException>(() => runtime.ListInstances()).Category);
        Assert.False(runtime.RestartRequired);
        Assert.False(runtime.Probe().ApiInitializationFailed);
    }

    [Fact]
    public void Probe_ReportsAnErrorCodeEnumWithoutOk()
    {
        var probe = Runtime(NoOkNamespace).Probe();
        Assert.False(probe.RuntimeErrorCode!.OkMemberFound);
        Assert.True(Runtime(SparseNamespace).Probe().RuntimeErrorCode!.OkMemberFound);
        Assert.False(probe.ApiInitializationFailed);
    }

    [Fact]
    public void Probe_WithoutTheApiReportsTheLocationAndNoGuesses()
    {
        var runtime = new ReflectionPlcSimRuntime(
            () => new PlcSimApiLocationInfo { Problem = PlcSimApiLocator.NotInstalledProblem },
            location => new PlcSimLoadedApi { Location = location, LoadError = location.Problem });
        var probe = runtime.Probe();
        Assert.False(probe.Loaded);
        Assert.Equal(PlcSimApiLocator.NotInstalledProblem, probe.LoadError);
        Assert.All(probe.Members, member => Assert.False(member.Resolved));
        Assert.Null(probe.RuntimeErrorCode);
        Assert.True(probe.Operations.Single(operation => operation.Operation == "probe_api").Available);
        Assert.All(probe.Operations.Where(operation => operation.Operation != "probe_api"), operation => Assert.False(operation.Available));

        var unavailable = Assert.Throws<PlcSimOperationException>(() => runtime.ListInstances());
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, unavailable.Category);
        Assert.Contains(PlcSimApiLocator.EnvironmentVariable, unavailable.Message);
    }

    [Fact]
    public void Probe_CapturesAManagerInitializationFailure()
    {
        Arrange();
        Full.FakeApiState.Current.ManagerThrows = true;
        var probe = Runtime(FullNamespace).Probe();
        Assert.Null(probe.IsInitialized);
        Assert.Contains("SimulationInitializationException", probe.ManagerStateError);
        Assert.Contains("plcsim_runtime_error", probe.ManagerStateError);
    }

    [Fact]
    public void LoadIsRetriedUntilItSucceedsAndThenKept()
    {
        Arrange();
        var attempts = 0;
        var runtime = new ReflectionPlcSimRuntime(() => new PlcSimApiLocationInfo(), location =>
        {
            attempts++;
            return attempts == 1
                ? new PlcSimLoadedApi { Location = location, LoadError = "BadImageFormatException: x86" }
                : new PlcSimLoadedApi { Location = location, Lookup = Lookup(FullNamespace) };
        });
        Assert.False(runtime.Probe().Loaded);
        Assert.True(runtime.Probe().Loaded);
        runtime.ListInstances();
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ListInstances_ProjectsSInstanceInfoReflectively()
    {
        Arrange();
        var list = Runtime(FullNamespace).ListInstances();
        Assert.Equal(1, list.Count);
        Assert.Equal("{\"ID\":7,\"Name\":\"Line1\"}", list.Instances[0].GetRawText());
        Assert.True(list.IsInitialized);
    }

    [Fact]
    public void Open_RequiresAnExactOrdinalNameAndReleasesTheInterface()
    {
        var instance = Arrange();
        var runtime = Runtime(FullNamespace);
        var warnings = new List<string>();

        using (var opened = runtime.Open("Line1", warnings))
            Assert.Equal("Line1", opened.Name);
        Assert.Equal(1, instance.Disposals);

        // The stand-in resolves names case-insensitively; the worker still refuses the mismatch.
        var mismatch = Assert.Throws<PlcSimOperationException>(() => runtime.Open("line1", warnings));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, mismatch.Category);
        Assert.Contains("'Line1'", mismatch.Message);
        Assert.Equal(2, instance.Disposals);

        var unknown = Assert.Throws<PlcSimOperationException>(() => runtime.Open("Nope", warnings));
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, unknown.Category);
        Assert.Equal("DoesNotExist", unknown.Detail.RuntimeErrorCode);
        Assert.Equal("RuntimeErrorCode", unknown.Detail.RuntimeErrorCodeMember);
        Assert.Equal("SimulationRuntimeException", unknown.Detail.ExceptionType);
        Assert.Contains("RuntimeErrorCode = DoesNotExist (-5)", unknown.Message);
        Assert.Contains("SimulationRuntimeManager.CreateInterface", unknown.Message);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Instance_ReadsPropertiesControlsAndTags()
    {
        var fake = Arrange();
        using var instance = Runtime(FullNamespace).Open("Line1", new List<string>());

        Assert.Equal("\"Off\"", instance.ReadProperty(PlcSimInstanceProperty.OperatingState).Value!.Value.GetRawText());
        Assert.Equal("7", instance.ReadProperty(PlcSimInstanceProperty.Id).Value!.Value.GetRawText());
        Assert.Equal("\"Softbus\"", instance.ReadProperty(PlcSimInstanceProperty.CommunicationInterface).Value!.Value.GetRawText());

        var powerOn = instance.Control(PlcSimControlAction.PowerOn);
        Assert.Equal(PlcSimApiCatalog.PowerOnTimeoutMs, powerOn.TimeoutArgumentMs);
        Assert.Equal("ERuntimeErrorCode", powerOn.ReturnType);
        Assert.Equal("\"OK\"", powerOn.ReturnValue!.Value.GetRawText());
        Assert.Equal((PlcSimReturnChecks.Ok, "OK"), (powerOn.Check, powerOn.ReturnCode));
        Assert.False(powerOn.Failed);
        Assert.Contains("PowerOn(60000)", fake.Calls);

        var run = instance.Control(PlcSimControlAction.Run);
        Assert.Equal(PlcSimApiCatalog.ControlTimeoutMs, run.TimeoutArgumentMs);
        Assert.Null(run.ReturnValue);
        Assert.Equal(PlcSimReturnChecks.NotInterpreted, run.Check);
        Assert.Contains("Run(60000)", fake.Calls);

        Full.FakeApiState.Current.MemoryResetResult = Full.ERuntimeErrorCode.AlreadyRunning;
        var warned = instance.Control(PlcSimControlAction.MemoryReset);
        Assert.Equal((PlcSimReturnChecks.Warning, "AlreadyRunning"), (warned.Check, warned.ReturnCode));
        Assert.False(warned.Failed);
        Full.FakeApiState.Current.MemoryResetResult = Full.ERuntimeErrorCode.Timeout;
        var reset = instance.Control(PlcSimControlAction.MemoryReset);
        Assert.Equal(PlcSimApiCatalog.ControlTimeoutMs, reset.TimeoutArgumentMs);
        Assert.True(reset.Failed);
        Assert.Equal("Timeout", reset.ReturnCode);
        Assert.Contains("MemoryReset(60000)", fake.Calls);
        var resetFailure = reset.Failure("memory_reset");
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, resetFailure.Category);
        Assert.Equal("Timeout", resetFailure.Detail.RuntimeErrorCode);
        Assert.Contains("returned Timeout (-2)", resetFailure.Message);

        instance.Control(PlcSimControlAction.UpdateTagList);
        Assert.Contains("UpdateTagList(IOMCTDB, True)", fake.Calls);

        var stop = Assert.Throws<PlcSimOperationException>(() => instance.Control(PlcSimControlAction.Stop));
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, stop.Category);
        Assert.Equal("NotRunning", stop.Detail.RuntimeErrorCode);

        Assert.Equal(true, instance.Read("Run", "bool"));
        Assert.Equal((short)12, instance.Read("Speed", "int16"));
        Assert.Equal("Ready", instance.Read("Label", "string"));
        instance.Write("Speed", "int16", (short)99);
        Assert.Equal((short)99, fake.Values["Speed"]);
        fake.Values["Count"] = 0L;
        fake.Values["Ratio"] = 0.0;
        Assert.Equal(PlcSimReturnChecks.Ok, instance.Write("Count", "int64", 5L).Check);
        Full.FakeApiState.Current.WriteInt64Result = Full.ERuntimeErrorCode.NotRunning;
        var refused = instance.Write("Count", "int64", 6L);
        Assert.Equal((PlcSimReturnChecks.Failed, "NotRunning"), (refused.Check, refused.ReturnCode));
        Assert.Equal(PlcSimReturnChecks.Ok, instance.Write("Ratio", "double", 0.5).Check);
        Full.FakeApiState.Current.WriteDoubleResult = false;
        var rejected = instance.Write("Ratio", "double", 0.7);
        Assert.Equal((PlcSimReturnChecks.Failed, "False"), (rejected.Check, rejected.ReturnCode));
        var missingTag = Assert.Throws<PlcSimOperationException>(() => instance.Read("Missing", "bool"));
        Assert.Equal("DoesNotExist", missingTag.Detail.RuntimeErrorCode);

        instance.SetScaleFactor(2.5);
        Assert.Equal(2.5, fake.ScaleFactor);
    }

    [Fact]
    public void Instance_ProjectsTagInfosAndTagListStatusGenerically()
    {
        Arrange();
        using var instance = Runtime(FullNamespace).Open("Line1", new List<string>());

        var tags = instance.ReadTagInfos(null, 500);
        Assert.Null(tags.NameUnavailableReason);
        Assert.Equal((2, 2, 2), (tags.TotalCount, tags.MatchedCount, tags.Tags.Count));
        Assert.Equal(new[] { "Run", "Speed" }, tags.Tags.Select(tag => tag.GetProperty("Name").GetString()));
        var filtered = instance.ReadTagInfos("SPE", 500);
        Assert.Equal((2, 1), (filtered.TotalCount, filtered.MatchedCount));
        Assert.Equal("Speed", Assert.Single(filtered.Tags).GetProperty("Name").GetString());
        var limited = instance.ReadTagInfos(null, 1);
        Assert.Equal((2, 2, 1), (limited.TotalCount, limited.MatchedCount, limited.Tags.Count));
        var first = tags.Tags[0];
        Assert.Equal("Marker", first.GetProperty("Area").GetString());
        Assert.Equal(2, first.GetProperty("Bit").GetInt32());
        Assert.Equal(1, first.GetProperty("Detail").GetProperty("Level").GetInt32());
        // Depth 2: the second object level expands, the third becomes a marker.
        Assert.Equal("<depthLimit:Deeper>", first.GetProperty("Detail").GetProperty("Deeper").GetString());

        var status = instance.ReadTagListStatus();
        Assert.True(status.Available);
        Assert.Equal("{\"out_TagListDetails\":\"IO\",\"out_IsHMIVisibleOnly\":true}", status.Value!.Value.GetRawText());
    }

    [Fact]
    public void Instance_OnTheSparseShapeFailsByMemberAndDegradesOptionalReads()
    {
        var warnings = new List<string>();
        var runtime = Runtime(SparseNamespace);

        var mismatch = Assert.Throws<PlcSimOperationException>(() => runtime.Open("line", warnings));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, mismatch.Category);
        Assert.Contains(warnings, warning => warning.Contains("IInstance.Dispose is not available", StringComparison.Ordinal));

        var failure = Assert.Throws<PlcSimOperationException>(() => runtime.Open("boom", warnings));
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, failure.Category);
        Assert.Null(failure.Detail.RuntimeErrorCode);

        using var instance = runtime.Open("LINE", warnings);
        Assert.False(instance.ReadProperty(PlcSimInstanceProperty.Id).Available);
        Assert.Equal(WorkerFailureCategories.PlcSimApiMemberMissing,
            Assert.Throws<PlcSimOperationException>(() => instance.Read("x", "int16")).Category);
        Assert.Equal(WorkerFailureCategories.PlcSimApiMemberMissing,
            Assert.Throws<PlcSimOperationException>(() => instance.Control(PlcSimControlAction.Stop)).Category);
        var powerOn = instance.Control(PlcSimControlAction.PowerOn);
        Assert.Null(powerOn.TimeoutArgumentMs);
        Assert.Contains("Int32 in_TimeOut = 1000", powerOn.Signature);
        var tags = instance.ReadTagInfos(null, 500);
        Assert.Contains("has type Int32", tags.NameUnavailableReason);
        Assert.Single(tags.Tags);
        var unfilterable = instance.ReadTagInfos("x", 500);
        Assert.Contains("has type Int32", unfilterable.NameUnavailableReason);
        Assert.Empty(unfilterable.Tags);
        Assert.False(instance.ReadTagListStatus().Available);
        instance.SetScaleFactor(0.5);
    }

    [Fact]
    public void Map_UnwrapsReflectionWrappersAndKeepsUnknownExceptionsUncategorized()
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(FullNamespace));
        // A failed type initializer is cached for the process: plcsim_api_unavailable, and the surface flags a
        // required restart (the restart flag, not the category, makes the host recycle the worker).
        var wrapped = new System.Reflection.TargetInvocationException(
            new TypeInitializationException("X", new Full.SimulationInitializationException("not running")));
        var initialization = ReflectionPlcSimRuntime.Map(surface, wrapped, "SimulationRuntimeManager.IsInitialized");
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, initialization.Category);
        Assert.True(surface.InitializationFailed);
        Assert.Equal("SimulationInitializationException", initialization.Detail.ExceptionType);
        var bareInitializer = ReflectionPlcSimRuntime.Map(surface, new TypeInitializationException("X", new DllNotFoundException("native")), "SimulationRuntimeManager.IsInitialized");
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, bareInitializer.Category);
        Assert.Equal("DllNotFoundException", bareInitializer.Detail.ExceptionType);

        // Without a type initializer in the chain, the API's exceptions stay plcsim_runtime_error.
        var runtimeError = ReflectionPlcSimRuntime.Map(surface, new System.Reflection.TargetInvocationException(
            new Full.SimulationRuntimeException(Full.ERuntimeErrorCode.NotRunning, "not running")), "IInstance.Run");
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, runtimeError.Category);
        Assert.Equal("NotRunning", runtimeError.Detail.RuntimeErrorCode);
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError,
            ReflectionPlcSimRuntime.Map(surface, new Full.SimulationInitializationException("not running"), "SimulationRuntimeManager.IsInitialized").Category);

        var other = ReflectionPlcSimRuntime.Map(surface, new System.Reflection.TargetInvocationException(new InvalidCastException("cast")), "IInstance.ReadBool");
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, other.Category);
        Assert.Contains("InvalidCastException", other.Message);
    }

    [Theory]
    [InlineData(FakeApiNoOk.ERuntimeErrorCode.Success, PlcSimReturnChecks.Ok, 0)]
    [InlineData(FakeApiNoOk.ERuntimeErrorCode.Refused, PlcSimReturnChecks.Failed, -1)]
    [InlineData(FakeApiNoOk.ERuntimeErrorCode.Notice, PlcSimReturnChecks.Warning, 1)]
    public void ErrorCodeEnumWithoutOk_IsJudgedByZeroAndSign(FakeApiNoOk.ERuntimeErrorCode returned, string check, long value)
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(NoOkNamespace));
        Assert.Equal(false, surface.HasOkMember);
        FakeApiNoOk.SimulationRuntimeManager.NextPowerOn = returned;
        try
        {
            using var instance = Runtime(NoOkNamespace).Open("Line9", new List<string>());
            var powerOn = instance.Control(PlcSimControlAction.PowerOn);
            Assert.Equal((check, returned.ToString(), (long?)value), (powerOn.Check, powerOn.ReturnCode, powerOn.ReturnCodeValue));
            Assert.Equal(check == PlcSimReturnChecks.Failed, powerOn.Failed);
            // Without an OK member, zero is only assumed to be success, and the result says so.
            Assert.Equal(value == 0, powerOn.ZeroFallback);
            if (value == 0)
                Assert.Contains("zero is assumed to mean success", powerOn.ReturnWarning("power_on"));
        }
        finally
        {
            FakeApiNoOk.SimulationRuntimeManager.NextPowerOn = FakeApiNoOk.ERuntimeErrorCode.Success;
        }
    }

    [Theory]
    [InlineData(FakeApiOkNonZero.ERuntimeErrorCode.OK, PlcSimReturnChecks.Ok)]
    [InlineData(FakeApiOkNonZero.ERuntimeErrorCode.Unknown, PlcSimReturnChecks.Uninterpretable)]
    [InlineData(FakeApiOkNonZero.ERuntimeErrorCode.Bad, PlcSimReturnChecks.Failed)]
    [InlineData(FakeApiOkNonZero.ERuntimeErrorCode.Hint, PlcSimReturnChecks.Warning)]
    public void AnOkMember_IsPreferredOverZero(FakeApiOkNonZero.ERuntimeErrorCode returned, string check)
    {
        FakeApiOkNonZero.SimulationRuntimeManager.NextPowerOn = returned;
        try
        {
            using var instance = Runtime(OkNonZeroNamespace).Open("Line9", new List<string>());
            var powerOn = instance.Control(PlcSimControlAction.PowerOn);
            Assert.Equal(check, powerOn.Check);
            Assert.False(powerOn.ZeroFallback);
        }
        finally
        {
            FakeApiOkNonZero.SimulationRuntimeManager.NextPowerOn = FakeApiOkNonZero.ERuntimeErrorCode.OK;
        }
    }

    [Fact]
    public void Probe_ReportsEnumMemberNamesWithNumericValues()
    {
        Arrange();
        var probe = Runtime(FullNamespace).Probe();
        var codes = probe.Types.Single(type => type.Id == "ERuntimeErrorCode");
        Assert.Contains(codes.EnumMembers, member => member.Name == "Timeout" && member.Value == "-2");
        Assert.Contains(codes.EnumMembers, member => member.Name == "AlreadyRunning" && member.Value == "2");
        Assert.Equal(codes.EnumNames, codes.EnumMembers.Select(member => member.Name));
        var state = probe.Types.Single(type => type.Id == "type of IInstance.OperatingState");
        Assert.Contains(state.EnumMembers, member => member.Name == "Run" && member.Value == "3");
        Assert.Empty(probe.Types.Single(type => type.Id == "STagInfo").EnumMembers);
        var unsigned = Runtime(UnsignedNamespace).Probe().Types.Single(type => type.Id == "ERuntimeErrorCode");
        Assert.Contains(unsigned.EnumMembers, member => member.Name == "Busy" && member.Value == "5");
    }

    [Fact]
    public void SetScaleFactor_OnASingleProperty_RefusesOutOfRangeValuesAsValidationErrors()
    {
        using var instance = Runtime(SparseNamespace).Open("LINE", new List<string>());
        foreach (var value in new[] { 1e39, 1e-50 })
        {
            var refused = Assert.Throws<PlcSimOperationException>(() => instance.SetScaleFactor(value));
            Assert.Equal(WorkerFailureCategories.ValidationError, refused.Category);
            Assert.Contains("is Single", refused.Message);
        }

        instance.SetScaleFactor(float.MaxValue);
    }

    [Fact]
    public void Probe_ReportsAnUnbindableApiAsALoadError_AndFlagsOnlyATypeInitializer()
    {
        ReflectionPlcSimRuntime With(Func<string, Type?> lookup) => new(() => new PlcSimApiLocationInfo { Source = "environment", DllExists = true },
            location => new PlcSimLoadedApi { Location = location, Lookup = lookup, AssemblyName = "Partial" });

        var partial = With(_ => throw new TypeLoadException("dependency missing"));
        var probe = partial.Probe();
        Assert.False(probe.Loaded);
        Assert.Contains("could not be bound (TypeLoadException: dependency missing)", probe.LoadError);
        Assert.Equal("Partial", probe.AssemblyName);
        Assert.False(probe.ApiInitializationFailed);
        Assert.False(partial.RestartRequired);
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, Assert.Throws<PlcSimOperationException>(() => partial.ListInstances()).Category);

        var initializer = With(_ => throw new TypeInitializationException("Native", new DllNotFoundException("native")));
        Assert.True(initializer.Probe().ApiInitializationFailed);
        Assert.True(initializer.RestartRequired);
    }

    [Fact]
    public void Projection_AGetterFailingWithATypeInitializer_FlagsARestart()
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(FullNamespace));
        var projected = ReflectionPlcSimRuntime.Project(surface, new InitializerGetter());
        Assert.Equal("<unreadable:DllNotFoundException>", projected.GetProperty("Broken").GetString());
        Assert.Equal(1, projected.GetProperty("Fine").GetInt32());
        Assert.True(surface.InitializationFailed);

        var other = PlcSimApiSurface.Resolve(Lookup(FullNamespace));
        ReflectionPlcSimRuntime.Project(other, new Throwing());
        Assert.False(other.InitializationFailed);
        Assert.Same(PlcSimObjectProjector.DataMembers(typeof(Throwing)), PlcSimObjectProjector.DataMembers(typeof(Throwing))); // cached per type
    }

    private sealed class InitializerGetter
    {
        public int Fine => 1;
        public int Broken => throw new TypeInitializationException("Native", new DllNotFoundException("native"));
    }

    [Fact]
    public void Resolver_AnswersOnlyMatchingDependenciesFromTheApiFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "plcsim-api");
        var path = Path.Combine(directory, "Siemens.Native.Wrapper.dll");
        var actual = new System.Reflection.AssemblyName("Siemens.Native.Wrapper, Version=8.0.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef");
        bool Exists(string candidate) => candidate == path;
        System.Reflection.AssemblyName Read(string _) => actual;

        Assert.Equal(path, PlcSimApiLoader.SelectDependency(new System.Reflection.AssemblyName("Siemens.Native.Wrapper"), directory, Exists, Read));
        Assert.Equal(path, PlcSimApiLoader.SelectDependency(new System.Reflection.AssemblyName(actual.FullName), directory, Exists, Read));
        Assert.Null(PlcSimApiLoader.SelectDependency(new System.Reflection.AssemblyName("Siemens.Native.Wrapper, Version=7.0.0.0"), directory, Exists, Read));
        Assert.Null(PlcSimApiLoader.SelectDependency(
            new System.Reflection.AssemblyName("Siemens.Native.Wrapper, Version=8.0.0.0, Culture=neutral, PublicKeyToken=fedcba9876543210"), directory, Exists, Read));
        Assert.Null(PlcSimApiLoader.SelectDependency(new System.Reflection.AssemblyName("Other"), directory, Exists, Read));
        Assert.Null(PlcSimApiLoader.SelectDependency(new System.Reflection.AssemblyName("Siemens.Native.Wrapper"), directory, Exists,
            _ => throw new BadImageFormatException("not managed")));
        Assert.True(PlcSimApiLoader.Matches(new System.Reflection.AssemblyName("siemens.native.wrapper"), actual));
    }

    [Fact]
    public void Resolver_AnswersOnlyBindsRequestedFromTheApiFolder()
    {
        var requester = typeof(PlcSimReflectionRuntimeTests).Assembly;
        var folder = Path.GetDirectoryName(requester.Location)!;
        var none = new Dictionary<System.Reflection.Assembly, string>();

        // An unknown requester (null), one outside every API folder, a framework assembly and a dynamic one are not answered.
        Assert.Null(PlcSimApiLoader.RequestingApiDirectory(null, none, new[] { folder }));
        Assert.Null(PlcSimApiLoader.RequestingApiDirectory(requester, none, Array.Empty<string>()));
        Assert.Null(PlcSimApiLoader.RequestingApiDirectory(requester, none, new[] { Path.Combine(folder, "API") }));
        Assert.Null(PlcSimApiLoader.RequestingApiDirectory(typeof(object).Assembly, none, new[] { folder }));
        var dynamic = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new System.Reflection.AssemblyName("PlcSimDynamicRequester"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        Assert.Null(PlcSimApiLoader.RequestingApiDirectory(dynamic, none, new[] { folder }));

        // An assembly whose file lies in an API folder belongs to it (trailing separators do not matter) ...
        var withSeparator = folder + Path.DirectorySeparatorChar;
        Assert.Equal(withSeparator, PlcSimApiLoader.RequestingApiDirectory(requester, none, new[] { Path.Combine(folder, "API"), withSeparator }));
        // ... and an assembly this loader loaded belongs to the folder it was loaded from.
        var loaded = new Dictionary<System.Reflection.Assembly, string> { [requester] = Path.Combine(folder, "API") };
        Assert.Equal(Path.Combine(folder, "API"), PlcSimApiLoader.RequestingApiDirectory(requester, loaded, Array.Empty<string>()));
    }

    [Fact]
    public void UnsignedErrorCodeEnum_IsUninterpretableUnlessOk()
    {
        using var instance = Runtime(UnsignedNamespace).Open("Line9", new List<string>());
        var powerOn = instance.Control(PlcSimControlAction.PowerOn);
        Assert.Equal((PlcSimReturnChecks.Uninterpretable, "Busy"), (powerOn.Check, powerOn.ReturnCode));
        Assert.False(powerOn.Failed);
        Assert.Contains("unsigned", powerOn.ReturnWarning("power_on"));
        var surface = PlcSimApiSurface.Resolve(Lookup(UnsignedNamespace));
        Assert.Equal(PlcSimReturnChecks.Ok, surface.InterpretReturn(typeof(FakeApiUnsigned.ERuntimeErrorCode), FakeApiUnsigned.ERuntimeErrorCode.OK).Check);
    }

    [Fact]
    public void PositiveCodes_AreWarnings_NegativeCodesErrors()
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(FullNamespace));
        var codeType = typeof(Full.ERuntimeErrorCode);
        Assert.Equal((PlcSimReturnChecks.Ok, "OK", (long?)0), surface.InterpretReturn(codeType, Full.ERuntimeErrorCode.OK));
        Assert.Equal((PlcSimReturnChecks.Warning, "AlreadyRunning", (long?)2), surface.InterpretReturn(codeType, Full.ERuntimeErrorCode.AlreadyRunning));
        Assert.Equal((PlcSimReturnChecks.Failed, "Timeout", (long?)-2), surface.InterpretReturn(codeType, Full.ERuntimeErrorCode.Timeout));
        Assert.Equal((PlcSimReturnChecks.Failed, "-77", (long?)-77), surface.InterpretReturn(codeType, (Full.ERuntimeErrorCode)(-77)));
        Assert.Equal((PlcSimReturnChecks.Warning, "40", (long?)40), surface.InterpretReturn(codeType, (Full.ERuntimeErrorCode)40));
        Assert.Equal(PlcSimReturnChecks.NotInterpreted, surface.InterpretReturn(typeof(int), -1).Check);

        var warning = new PlcSimInvocation("ERuntimeErrorCode PowerOn(UInt32 in_TimeOut)", 60_000, "ERuntimeErrorCode", Json("\"AlreadyRunning\""),
            PlcSimReturnChecks.Warning, "AlreadyRunning", 2);
        Assert.False(warning.Failed);
        Assert.Contains("warning code AlreadyRunning (2)", warning.ReturnWarning("power_on"));
    }

    [Fact]
    public void Map_FindsATypeInitializerAnywhereInTheBoundedChain()
    {
        var surface = PlcSimApiSurface.Resolve(Lookup(FullNamespace));
        var nested = new InvalidOperationException("outer", new AggregateException(new TypeInitializationException("X", new DllNotFoundException("native"))));
        var mapped = ReflectionPlcSimRuntime.Map(surface, nested, "SimulationRuntimeManager.RegisteredInstanceInfo");
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, mapped.Category);
        Assert.Equal("InvalidOperationException", mapped.Detail.ExceptionType);
        Assert.True(surface.InitializationFailed);

        Exception deep = new TypeInitializationException("X", null);
        for (var depth = 0; depth < ReflectionPlcSimRuntime.MaxInnerExceptionDepth; depth++)
            deep = new InvalidOperationException("wrap", deep);
        Assert.False(ReflectionPlcSimRuntime.HasTypeInitializationFailure(deep));
        Assert.False(ReflectionPlcSimRuntime.HasTypeInitializationFailure(new InvalidOperationException("plain")));
    }

    [Fact]
    public void ATagLevelTypeInitializerFailure_RequiresARestartOnASuccessfulResponse()
    {
        var fake = Arrange();
        var runtime = Runtime(FullNamespace);
        var dispatcher = new TiaMcpServer.PlcSimWorker.Operations.PlcSimWorkerDispatcher(TiaMcpServer.Contracts.Safety.McpAccessMode.Full,
            new TiaMcpServer.PlcSimWorker.Operations.PlcSimOperationHandler(runtime, new FakePlcSimClock()));
        Assert.False(runtime.RestartRequired);

        var response = dispatcher.Handle(new TiaMcpServer.Contracts.Worker.WorkerRequest
        {
            Method = PlcSimMethods.ReadTags,
            ProtocolVersion = PlcSimWorkerProtocol.Version,
            PlcSim = new PlcSimRequestInfo { Instance = "Line1", Tags = new List<PlcSimTagRequestInfo> { new() { Name = "Run", DataType = "bool" }, new() { Name = "TypeInitPoisoned", DataType = "bool" } } },
        });

        Assert.True(response.Success, response.Error);
        Assert.True(response.WorkerRestartRequired);
        Assert.True(runtime.RestartRequired);
        Assert.Contains("\"plcsim_api_unavailable\"", response.Payload);
        Assert.Equal(1, fake.Disposals);
    }

    [Fact]
    public void Loader_LoadsTheResolvedFileAndReportsLoadFailuresWithoutThrowing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "plcsim-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var missing = PlcSimApiLoader.Load(new PlcSimApiLocationInfo { Problem = "absent" });
            Assert.False(missing.Loaded);
            Assert.Equal("absent", missing.LoadError);

            var dll = Path.Combine(directory, PlcSimApiLocator.ApiDllName);
            File.WriteAllText(dll, "not an assembly");
            var location = new PlcSimApiLocationInfo { ApiDirectory = directory, DllPath = dll, DllExists = true };
            var corrupt = PlcSimApiLoader.Load(location);
            Assert.False(corrupt.Loaded);
            Assert.Contains("BadImageFormatException", corrupt.LoadError);

            // Any managed assembly stands in for the API file: the loader neither knows nor checks
            // its identity, and the lookup simply finds no Siemens types in it.
            var other = Path.Combine(directory, "other");
            Directory.CreateDirectory(other);
            var stand = Path.Combine(other, PlcSimApiLocator.ApiDllName);
            File.Copy(typeof(PlcSimApiLocator).Assembly.Location, stand);
            var loaded = PlcSimApiLoader.Load(new PlcSimApiLocationInfo { ApiDirectory = other, DllPath = stand, DllExists = true });
            Assert.True(loaded.Loaded, loaded.LoadError);
            Assert.StartsWith("TiaMcpServer.Contracts", loaded.AssemblyName);
            Assert.Null(loaded.Lookup!(PlcSimApiCatalog.Namespace + "." + PlcSimApiCatalog.ManagerTypeName));
            var probe = new ReflectionPlcSimRuntime(() => loaded.Location, _ => loaded).Probe();
            Assert.True(probe.Loaded);
            Assert.All(probe.Members, member => Assert.False(member.Resolved));
            Assert.False(probe.RuntimeErrorCode!.ExceptionTypeFound);
            Assert.Null(probe.RuntimeErrorCode.OkMemberFound);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Projector_HandlesScalarsEnumsMarkersAndUnreadableMembers()
    {
        Assert.Equal("null", PlcSimObjectProjector.Project(null).GetRawText());
        Assert.Equal("\"Run\"", PlcSimObjectProjector.Project(Full.EOperatingState.Run).GetRawText());
        Assert.Equal("<unsupported:Int32[]>", PlcSimObjectProjector.Project(new[] { 1 }).GetString());
        Assert.Equal("\"NaN\"", PlcSimObjectProjector.Project(double.NaN).GetRawText());
        var throwing = PlcSimObjectProjector.Project(new Throwing());
        Assert.Equal(new[] { "Fine", "Throws" }, throwing.EnumerateObject().Select(member => member.Name));
        Assert.Equal(1, throwing.GetProperty("Fine").GetInt32());
        Assert.Equal("<unreadable:InvalidOperationException>", throwing.GetProperty("Throws").GetString());
    }

    [Fact]
    public void Projector_ProjectsArraysOfDataObjectsByTheirRuntimeMembers()
    {
        // K17 (live 2026-11-02): list_tags reported "Dimension":"<unsupported:SDimension[]>" for every tag. The element
        // members are read from the runtime type, so these stand-in names are not assumed anywhere in the projector.
        var array = PlcSimObjectProjector.Project(new FakeTagInfo
        {
            Name = "Werte",
            Dimension = new[] { new FakeDimension { StartIndex = 0, Count = 10 }, new FakeDimension { StartIndex = -2, Count = 5 } },
        });
        var dimensions = array.GetProperty("Dimension");
        Assert.Equal(JsonValueKind.Array, dimensions.ValueKind);
        Assert.Equal(new[] { (0, 10), (-2, 5) }, dimensions.EnumerateArray().Select(d => (d.GetProperty("StartIndex").GetInt32(), d.GetProperty("Count").GetInt32())));
        Assert.Equal(new[] { "Count", "StartIndex" }, dimensions[0].EnumerateObject().Select(m => m.Name));

        var scalar = PlcSimObjectProjector.Project(new FakeTagInfo { Name = "Motor", Dimension = Array.Empty<FakeDimension>() });
        Assert.Equal("[]", scalar.GetProperty("Dimension").GetRawText());
        Assert.Equal(JsonValueKind.Null, PlcSimObjectProjector.Project(new FakeTagInfo { Name = "x" }).GetProperty("Dimension").ValueKind);

        var many = PlcSimObjectProjector.Project(new FakeTagInfo { Dimension = Enumerable.Range(0, 70).Select(i => new FakeDimension { Count = i }).ToArray() })
            .GetProperty("Dimension").EnumerateArray().ToList();
        Assert.Equal(PlcSimObjectProjector.MaxArrayElements + 1, many.Count);
        Assert.Equal("<moreElements:6>", many[^1].GetString());

        // Arrays of scalars, strings and nested arrays keep their marker; an array adds no object level.
        Assert.Equal("<unsupported:String[]>", PlcSimObjectProjector.Project(new[] { "a" }).GetString());
        Assert.Equal("<unsupported:FakeDimension[][]>", PlcSimObjectProjector.Project(new[] { new[] { new FakeDimension() } }).GetString());
        var nested = PlcSimObjectProjector.Project(new[] { new FakeNested() });
        Assert.Equal("<depthLimit:FakeNested>", nested[0].GetProperty("Inner").GetProperty("Inner").GetString());
    }

    private sealed class FakeTagInfo
    {
        public string? Name;
        public FakeDimension[]? Dimension;
    }

    private struct FakeDimension
    {
        public int StartIndex;
        public int Count { get; set; }
    }

    private sealed class FakeNested
    {
        public FakeNested? Inner => new();
    }

    private sealed class Throwing
    {
        public int Fine => 1;
        public int Throws => throw new InvalidOperationException();
    }
}
