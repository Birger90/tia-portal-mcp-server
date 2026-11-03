using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcSimWorker.Operations;
using TiaMcpServer.PlcSimWorker.Runtime;
using Xunit;

namespace TiaMcpServer.Tests.PlcSim;

public sealed class PlcSimWorkerTests
{
    private static JsonElement Json(string text) => PlcSimValueCodec.Parse(text);

    private static PlcSimTestStepInfo Write(string tag, string dataType, string value) => new() { Kind = "write", Tag = tag, DataType = dataType, Value = Json(value) };

    private static PlcSimTestStepInfo Wait(int ms) => new() { Kind = "wait", Ms = ms };

    private static PlcSimTestStepInfo Expect(string tag, string dataType, string comparison, string value, int? timeoutMs = null, int? pollMs = null)
        => new() { Kind = "expect", Tag = tag, DataType = dataType, Comparison = comparison, Value = Json(value), TimeoutMs = timeoutMs, PollMs = pollMs };

    private static (PlcSimTestResultInfo Result, List<string> Warnings) RunScenario(FakePlcSimRuntime runtime, PlcSimTestRequestInfo test)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        PlcSimRequestRules.ValidateTest(test, errors);
        Assert.Empty(errors);
        using var instance = runtime.Open("Line1", warnings);
        return (PlcSimTestRunner.Run(instance, PlcSimRequestRules.NormalizeTest(test), runtime.Clock, warnings), warnings);
    }

    [Fact]
    public void Runner_PollsUntilAReactionArrivesWithinTheTimeout()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].React("Start", 300, "Running", true);
        var (result, warnings) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo>
            {
                Write("Start", "bool", "true"),
                Expect("Running", "bool", "equals", "true", timeoutMs: 1000, pollMs: 100),
            }
        });

        Assert.True(result.Passed);
        Assert.Empty(warnings);
        Assert.Equal("passed", result.Steps[0].Status);
        var expect = result.Steps[1];
        Assert.Equal("passed", expect.Status);
        Assert.Equal(4, expect.Attempts);
        Assert.Equal(300, expect.ElapsedMs);
        Assert.Equal("true", expect.LastObserved!.Value.GetRawText());
        Assert.Equal(2, result.Summary.Passed);
        Assert.Equal(1, result.Summary.Expects);
        Assert.Equal(1, result.Summary.ExpectsPassed);
        Assert.Equal("wallClock", result.TimingModel);
    }

    [Fact]
    public void Runner_FailsAfterTheTimeoutAndSkipsTheRestByDefault()
    {
        var runtime = PlcSimTestHost.Standard();
        var (result, _) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo>
            {
                Expect("Speed", "int16", "greaterThan", "10", timeoutMs: 120, pollMs: 50),
                Wait(10),
            }
        });

        Assert.False(result.Passed);
        var expect = result.Steps[0];
        Assert.Equal("failed", expect.Status);
        Assert.Equal(4, expect.Attempts);
        Assert.Equal(120, expect.ElapsedMs);
        Assert.Contains("greaterThan 10", expect.Message);
        Assert.Contains("last observed 0", expect.Message);
        Assert.Equal("skipped", result.Steps[1].Status);
        Assert.Contains("continueOnFailure is false", result.Steps[1].Message);
        Assert.Equal(1, result.Summary.Failed);
        Assert.Equal(1, result.Summary.Skipped);
        Assert.Equal(new[] { 50, 50, 20 }, runtime.Clock.Sleeps);
    }

    [Fact]
    public void Runner_ContinueOnFailureRunsEveryStepAndReportsErrors()
    {
        var runtime = PlcSimTestHost.Standard();
        var (result, _) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            ContinueOnFailure = true,
            Steps = new List<PlcSimTestStepInfo>
            {
                Write("Missing", "bool", "true"),
                Expect("Temperature", "float", "equals", "21.5", timeoutMs: 0),
                new() { Kind = "expect", Tag = "Temperature", DataType = "float", Comparison = "between", Min = Json("20"), Max = Json("21") },
                Expect("Label", "string", "equals", "\"Ready\""),
            }
        });

        Assert.False(result.Passed);
        Assert.Equal(new[] { "error", "passed", "failed", "passed" }, result.Steps.Select(step => step.Status));
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, result.Steps[0].Error!.Category);
        Assert.Equal("DoesNotExist", result.Steps[0].Error!.RuntimeErrorCode);
        Assert.Equal(1, result.Steps[1].Attempts);
        Assert.Equal("21.5", result.Steps[2].LastObserved!.Value.GetRawText());
        Assert.Contains("between 20 and 21", result.Steps[2].Message);
        Assert.Equal(1, result.Summary.Errors);
        Assert.Equal(3, result.Summary.Expects);
        Assert.Equal(2, result.Summary.ExpectsPassed);
    }

    [Fact]
    public void Runner_AbortsWhenMaxDurationElapses()
    {
        var runtime = PlcSimTestHost.Standard();
        var (result, _) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            ContinueOnFailure = true,
            MaxDurationMs = 500,
            Steps = new List<PlcSimTestStepInfo>
            {
                Wait(300),
                Expect("Running", "bool", "equals", "true", timeoutMs: 5000, pollMs: 100),
                Wait(10),
            }
        });

        Assert.False(result.Passed);
        Assert.True(result.Aborted);
        Assert.Contains("maxDurationMs (500)", result.AbortReason);
        Assert.Equal(new[] { "passed", "error", "skipped" }, result.Steps.Select(step => step.Status));
        Assert.Equal(500, result.DurationMs);
        Assert.Equal(2, result.Steps[1].Attempts);
    }

    [Fact]
    public void Runner_WaitLongerThanTheRemainingBudgetAborts()
    {
        var runtime = PlcSimTestHost.Standard();
        var (result, warnings) = RunScenario(runtime, new PlcSimTestRequestInfo { MaxDurationMs = 100, Steps = new List<PlcSimTestStepInfo> { Wait(1000) } });
        Assert.True(result.Aborted);
        Assert.Equal("error", result.Steps[0].Status);
        Assert.Equal(100, runtime.Clock.Now);
        Assert.Contains(warnings, warning => warning.Contains("no expect step", StringComparison.Ordinal));
    }

    private static object Execute(FakePlcSimRuntime runtime, string method, PlcSimRequestInfo request, List<string> warnings)
        => new PlcSimOperationHandler(runtime, runtime.Clock).Execute(method, request, warnings);

    [Fact]
    public void Handler_GetInstanceReportsMissingMembersAsNullWithWarnings()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].MissingMembers.Add("IInstance.OperatingMode");
        var warnings = new List<string>();
        var info = (PlcSimInstanceInfo)Execute(runtime, PlcSimMethods.GetInstance, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Equal("Line1", info.RequestedName);
        Assert.Null(info.OperatingMode);
        Assert.Equal("\"Off\"", info.OperatingState!.Value.GetRawText());
        Assert.Contains(warnings, warning => warning.Contains("IInstance.OperatingMode", StringComparison.Ordinal) && warning.EndsWith("reported as null.", StringComparison.Ordinal));
        Assert.Equal(1, runtime.Disposed);
    }

    [Fact]
    public void Handler_ListTagsFiltersCaseInsensitivelyAndTruncatesByMax()
    {
        var runtime = PlcSimTestHost.Standard();
        var warnings = new List<string>();
        var list = (PlcSimTagListInfo)Execute(runtime, PlcSimMethods.ListTags, new PlcSimRequestInfo { Instance = "Line1", NameFilter = "RUN", Max = 500 }, warnings);
        Assert.Equal(5, list.TotalCount);
        Assert.Equal(1, list.MatchedCount);
        Assert.Equal("Running", list.Tags.Single().GetProperty("Name").GetString());
        Assert.NotNull(list.TagListStatus);

        runtime.Instances["Line1"].Projected = 0;
        var truncated = (PlcSimTagListInfo)Execute(runtime, PlcSimMethods.ListTags, new PlcSimRequestInfo { Instance = "Line1", Max = 2 }, warnings);
        Assert.Equal(2, truncated.ReturnedCount);
        Assert.True(truncated.Truncated);
        Assert.Equal((5, 5), (truncated.TotalCount, truncated.MatchedCount));
        Assert.Equal(2, runtime.Instances["Line1"].Projected); // Only the returned entries are projected.

        runtime.Instances["Line1"].TagInfoNameMissing = true;
        var missing = Assert.Throws<PlcSimOperationException>(() =>
            Execute(runtime, PlcSimMethods.ListTags, new PlcSimRequestInfo { Instance = "Line1", NameFilter = "x" }, warnings));
        Assert.Equal(WorkerFailureCategories.PlcSimApiMemberMissing, missing.Category);
        Assert.Equal("STagInfo.Name", missing.Detail.Member);
        var unfiltered = (PlcSimTagListInfo)Execute(runtime, PlcSimMethods.ListTags, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Equal(PlcSimRequestRules.DefaultListTagsMax, unfiltered.Max);
        Assert.Equal(5, unfiltered.ReturnedCount);
    }

    [Fact]
    public void Handler_ReadTagsReportsPerTagFailures()
    {
        var runtime = PlcSimTestHost.Standard();
        var warnings = new List<string>();
        var reads = (PlcSimTagReadListInfo)Execute(runtime, PlcSimMethods.ReadTags, new PlcSimRequestInfo
        {
            Instance = "Line1",
            Tags = new List<PlcSimTagRequestInfo>
            {
                new() { Name = "Speed", DataType = "int16" },
                new() { Name = "Nope", DataType = "bool" },
                new() { Name = "Temperature", DataType = "float" },
            }
        }, warnings);
        Assert.Equal(new[] { "read", "failed", "read" }, reads.Tags.Select(tag => tag.Status));
        Assert.Equal("0", reads.Tags[0].Value!.Value.GetRawText());
        Assert.Equal("21.5", reads.Tags[2].Value!.Value.GetRawText());
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, reads.Tags[1].Error!.Category);
        Assert.Equal(2, reads.ReadCount);
        Assert.Equal(1, reads.FailedCount);
        Assert.Contains(warnings, warning => warning.StartsWith("1 of 3 tag reads failed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("DB_Line.Speed", "DB_Line.Speed")]
    [InlineData("\"DB_Line\".Speed", "DB_Line.Speed")]
    [InlineData("\"DB_Line\".\"Speed\"", "DB_Line.Speed")]
    [InlineData("\"Motor_On\"", "Motor_On")]
    [InlineData("\"DB_Line\".Values[2]", "DB_Line.Values[2]")]
    [InlineData("\"DB_Line\".\"Axes\"[1,2].Pos", "DB_Line.Axes[1,2].Pos")]
    [InlineData("\"My.DB\".Speed", "\"My.DB\".Speed")]
    [InlineData("\"DB_Line.Speed", "\"DB_Line.Speed")]
    [InlineData("\"DB\"x.Speed", "\"DB\"x.Speed")]
    public void TagNames_StripTheOuterQuotesOfEachSegmentOnly(string name, string api)
        => Assert.Equal(api, PlcSimTagNames.ToApiName(name));

    /// <summary>K56 (live 2026-11-02): "DB_Line".Speed failed DoesNotExist; PLCSIM Advanced takes DB_Line.Speed.</summary>
    [Fact]
    public void Handler_ReadWriteAndTest_AcceptTiaQuotedNames()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].Tag("DB_Line.Speed", PlcSimDataTypes.Int16, (short)5);
        var warnings = new List<string>();
        var reads = (PlcSimTagReadListInfo)Execute(runtime, PlcSimMethods.ReadTags, new PlcSimRequestInfo
        {
            Instance = "Line1", Tags = new List<PlcSimTagRequestInfo> { new() { Name = "\"DB_Line\".Speed", DataType = "int16" } },
        }, warnings);
        Assert.Equal(("read", "\"DB_Line\".Speed", "5"), (reads.Tags[0].Status, reads.Tags[0].Name, reads.Tags[0].Value!.Value.GetRawText()));
        var writes = (PlcSimTagWriteListInfo)Execute(runtime, PlcSimMethods.WriteTags, new PlcSimRequestInfo
        {
            Instance = "Line1", Tags = new List<PlcSimTagRequestInfo> { new() { Name = "\"DB_Line\".Speed", DataType = "int16", Value = Json("7") } },
        }, warnings);
        Assert.Equal(1, writes.WrittenCount);
        Assert.Equal((short)7, runtime.Instances["Line1"].Tags["DB_Line.Speed"].Value);
        var (result, _) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo> { Write("\"DB_Line\".Speed", "int16", "9"), Expect("\"DB_Line\".\"Speed\"", "int16", "equals", "9") },
        });
        Assert.True(result.Passed);
    }

    [Fact]
    public void Handler_WriteTagsStopsAtTheFirstFailedTag()
    {
        var runtime = PlcSimTestHost.Standard();
        var writes = (PlcSimTagWriteListInfo)Execute(runtime, PlcSimMethods.WriteTags, new PlcSimRequestInfo
        {
            Instance = "Line1",
            Tags = new List<PlcSimTagRequestInfo>
            {
                new() { Name = "Speed", DataType = "int16", Value = Json("42") },
                new() { Name = "Nope", DataType = "bool", Value = Json("true") },
                new() { Name = "Start", DataType = "bool", Value = Json("true") },
            }
        }, new List<string>());
        Assert.Equal(new[] { "written", "failed", "skipped" }, writes.Tags.Select(tag => tag.Status));
        Assert.Equal((short)42, runtime.Instances["Line1"].Tags["Speed"].Value);
        Assert.Equal(false, runtime.Instances["Line1"].Tags["Start"].Value);
        Assert.Equal("42", writes.Tags[0].Value.GetRawText());
        Assert.Equal(1, writes.WrittenCount);
        Assert.Equal(1, writes.SkippedCount);
    }

    [Fact]
    public void Handler_ControlReportsStatesReturnValuesAndTheColdRestartWarning()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].ControlReturnValue = Json("\"OK\"");
        var warnings = new List<string>();
        var result = (PlcSimControlResultInfo)Execute(runtime, PlcSimMethods.PowerOn, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Equal("power_on", result.Operation);
        Assert.Equal("\"Off\"", result.OperatingStateBefore!.Value.GetRawText());
        Assert.Equal("\"Stop\"", result.OperatingStateAfter!.Value.GetRawText());
        Assert.Equal(PlcSimApiCatalog.PowerOnTimeoutMs, result.TimeoutArgumentMs);
        Assert.Contains(warnings, warning => warning.Contains("not interpreted", StringComparison.Ordinal));

        warnings.Clear();
        Execute(runtime, PlcSimMethods.UpdateTagList, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Contains(PlcSimWarnings.UpdateTagListColdRestart, warnings);

        runtime.Instances["Line1"].MissingMembers.Add("IInstance.MemoryReset");
        var missing = Assert.Throws<PlcSimOperationException>(() =>
            Execute(runtime, PlcSimMethods.MemoryReset, new PlcSimRequestInfo { Instance = "Line1" }, new List<string>()));
        Assert.Equal(WorkerFailureCategories.PlcSimApiMemberMissing, missing.Category);
    }

    [Fact]
    public void Handler_ControlWithAFailedReturnCode_RecordsItWithTheStateEvidence()
    {
        var runtime = PlcSimTestHost.Standard();
        var instance = runtime.Instances["Line1"];
        instance.ControlReturnValue = Json("\"Timeout\"");
        instance.ControlReturnCheck = PlcSimReturnChecks.Failed;
        instance.ControlReturnCode = "Timeout";
        var warnings = new List<string>();

        var result = (PlcSimControlResultInfo)Execute(runtime, PlcSimMethods.Stop, new PlcSimRequestInfo { Instance = "Line1" }, warnings);

        Assert.Equal(PlcSimReturnChecks.Failed, result.ReturnCheck);
        Assert.Equal("Timeout", result.ReturnCode);
        Assert.Equal(PlcSimApiCatalog.ControlTimeoutMs, result.TimeoutArgumentMs);
        Assert.NotNull(result.OperatingStateAfter);
        Assert.DoesNotContain(warnings, warning => warning.Contains("not interpreted", StringComparison.Ordinal));
        Assert.Equal("Stop", Assert.Single(instance.Calls));
    }

    [Fact]
    public void Handler_ControlWarnsForAnUninterpretableCodeAndAParameterlessFallback()
    {
        var runtime = PlcSimTestHost.Standard();
        var instance = runtime.Instances["Line1"];
        instance.ControlReturnValue = Json("\"Success\"");
        instance.ControlReturnCheck = PlcSimReturnChecks.Uninterpretable;
        instance.ControlReturnCode = "Success";
        instance.ControlWithoutTimeout = true;
        var warnings = new List<string>();

        var result = (PlcSimControlResultInfo)Execute(runtime, PlcSimMethods.PowerOff, new PlcSimRequestInfo { Instance = "Line1" }, warnings);

        Assert.Equal(PlcSimReturnChecks.Uninterpretable, result.ReturnCheck);
        Assert.Null(result.TimeoutArgumentMs);
        Assert.Contains(warnings, warning => warning.Contains("could not be interpreted", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("operatingStateAfter may lag", StringComparison.Ordinal));

        warnings.Clear();
        instance.ControlWithoutTimeout = false;
        instance.ControlReturnValue = Json("\"OK\"");
        instance.ControlReturnCheck = PlcSimReturnChecks.Ok;
        instance.ControlReturnCode = "OK";
        Assert.Equal(PlcSimReturnChecks.Ok, ((PlcSimControlResultInfo)Execute(runtime, PlcSimMethods.MemoryReset,
            new PlcSimRequestInfo { Instance = "Line1" }, warnings)).ReturnCheck);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(PlcSimMethods.PowerOn, true)]
    [InlineData(PlcSimMethods.Run, true)]
    [InlineData(PlcSimMethods.Stop, false)]
    [InlineData(PlcSimMethods.PowerOff, false)]
    public void Handler_StartingAnInstanceOffTheSoftbusWarnsWithTheRawValue(string method, bool warns)
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].CommunicationInterface = "TCPIP";
        var warnings = new List<string>();
        Execute(runtime, method, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Equal(warns, warnings.Any(warning => warning.Contains("CommunicationInterface \"TCPIP\"", StringComparison.Ordinal)
            && warning.Contains("virtual Ethernet adapter", StringComparison.Ordinal)));

        warnings.Clear();
        runtime.Instances["Line1"].CommunicationInterface = "Softbus";
        Execute(runtime, method, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.DoesNotContain(warnings, warning => warning.Contains("CommunicationInterface", StringComparison.Ordinal));

        runtime.Instances["Line1"].CommunicationInterface = "softbus";
        Execute(runtime, method, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Equal(warns, warnings.Any(warning => warning.Contains("CommunicationInterface \"softbus\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Handler_PowerOnWarnsWhenTheCommunicationInterfaceIsUnreadable_AndPlcSimTestWarnsToo()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].CommunicationInterface = null;
        var warnings = new List<string>();
        Execute(runtime, PlcSimMethods.PowerOn, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Contains(warnings, warning => warning.Contains("CommunicationInterface unreadable", StringComparison.Ordinal));

        warnings.Clear();
        runtime.Instances["Line1"].CommunicationInterface = "TCPIP";
        var test = (PlcSimTestResultInfo)Execute(runtime, PlcSimMethods.RunTest, new PlcSimRequestInfo
        {
            Instance = "Line1",
            Test = new PlcSimTestRequestInfo { Steps = new List<PlcSimTestStepInfo> { Write("Start", "bool", "true") } },
        }, warnings);
        Assert.True(test.Passed);
        Assert.Contains(warnings, warning => warning.StartsWith("plcsim_test:", StringComparison.Ordinal) && warning.Contains("\"TCPIP\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Handler_WriteTagsFailsATagWhoseWriteReturnsAFailureCode()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].WriteReturns["Start"] = (PlcSimReturnChecks.Failed, "NotRunning", Json("\"NotRunning\""));
        var writes = (PlcSimTagWriteListInfo)Execute(runtime, PlcSimMethods.WriteTags, new PlcSimRequestInfo
        {
            Instance = "Line1",
            Tags = new List<PlcSimTagRequestInfo>
            {
                new() { Name = "Speed", DataType = "int16", Value = Json("42") },
                new() { Name = "Start", DataType = "bool", Value = Json("true") },
                new() { Name = "Speed", DataType = "int16", Value = Json("43") },
            }
        }, new List<string>());

        Assert.Equal(new[] { "written", "failed", "skipped" }, writes.Tags.Select(tag => tag.Status));
        var failed = writes.Tags[1];
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, failed.Error!.Category);
        Assert.Equal("NotRunning", failed.Error.RuntimeErrorCode);
        Assert.Contains("returned NotRunning", failed.Error.Message);
        Assert.Equal("\"NotRunning\"", failed.ReturnValue!.Value.GetRawText());
        Assert.NotNull(failed.InvokedSignature);
        Assert.Equal((1, 1, 1), (writes.WrittenCount, writes.FailedCount, writes.SkippedCount));
    }

    [Fact]
    public void Runner_WriteStepWithAFailureReturn_IsAnError()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].WriteReturns["Start"] = (PlcSimReturnChecks.Failed, "False", Json("false"));
        var (result, _) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo> { Write("Start", "bool", "true"), Expect("Running", "bool", "equals", "false") }
        });

        Assert.False(result.Passed);
        Assert.Equal(new[] { "error", "skipped" }, result.Steps.Select(step => step.Status));
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, result.Steps[0].Error!.Category);
        Assert.Equal("False", result.Steps[0].Error!.RuntimeErrorCode);
        Assert.Contains("step 0 write 'Start'", result.Steps[0].Message);

        runtime.Instances["Line1"].WriteReturns["Start"] = (PlcSimReturnChecks.Uninterpretable, "Success", Json("\"Success\""));
        var (uninterpretable, warnings) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo> { Write("Start", "bool", "true"), Expect("Start", "bool", "equals", "true") }
        });
        Assert.True(uninterpretable.Passed);
        Assert.Contains(warnings, warning => warning.Contains("could not be interpreted", StringComparison.Ordinal));

        runtime.Instances["Line1"].WriteReturns["Start"] = (PlcSimReturnChecks.Warning, "AlreadyRunning", Json("\"AlreadyRunning\""));
        var (warned, warnedWarnings) = RunScenario(runtime, new PlcSimTestRequestInfo
        {
            Steps = new List<PlcSimTestStepInfo> { Write("Start", "bool", "true"), Expect("Start", "bool", "equals", "true") }
        });
        Assert.True(warned.Passed);
        Assert.Contains(warnedWarnings, warning => warning.Contains("warning code AlreadyRunning", StringComparison.Ordinal));
    }

    [Fact]
    public void Handler_AWarningCodeSucceedsWithAWarning()
    {
        var runtime = PlcSimTestHost.Standard();
        var instance = runtime.Instances["Line1"];
        instance.ControlReturnValue = Json("\"AlreadyRunning\"");
        instance.ControlReturnCheck = PlcSimReturnChecks.Warning;
        instance.ControlReturnCode = "AlreadyRunning";
        var warnings = new List<string>();
        var result = (PlcSimControlResultInfo)Execute(runtime, PlcSimMethods.PowerOn, new PlcSimRequestInfo { Instance = "Line1" }, warnings);
        Assert.Equal((PlcSimReturnChecks.Warning, "AlreadyRunning"), (result.ReturnCheck, result.ReturnCode));
        Assert.Contains(warnings, warning => warning.Contains("warning code AlreadyRunning", StringComparison.Ordinal)
            && warning.Contains("treated as successful", StringComparison.Ordinal));

        runtime.Instances["Line1"].WriteReturns["Speed"] = (PlcSimReturnChecks.Warning, "AlreadyRunning", Json("\"AlreadyRunning\""));
        warnings.Clear();
        var writes = (PlcSimTagWriteListInfo)Execute(runtime, PlcSimMethods.WriteTags, new PlcSimRequestInfo
        {
            Instance = "Line1",
            Tags = new List<PlcSimTagRequestInfo> { new() { Name = "Speed", DataType = "int16", Value = Json("5") }, new() { Name = "Start", DataType = "bool", Value = Json("true") } }
        }, warnings);
        Assert.Equal(new[] { "written", "written" }, writes.Tags.Select(tag => tag.Status));
        Assert.Single(warnings, warning => warning.Contains("warning code AlreadyRunning", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispatcher_StatesARequiredRestartOnEveryResponseOnlyWhenTheRuntimeSaysSo()
    {
        var runtime = PlcSimTestHost.Standard();
        var request = new WorkerRequest { Method = PlcSimMethods.GetInstance, ProtocolVersion = PlcSimWorkerProtocol.Version, PlcSim = new PlcSimRequestInfo { Instance = "Line1" } };
        Assert.Null(Dispatch(McpAccessMode.ReadOnly, request, runtime).WorkerRestartRequired);

        runtime.UnavailableReason = "not installed";
        var unavailable = Dispatch(McpAccessMode.ReadOnly, request, runtime);
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, unavailable.FailureCategory);
        Assert.Null(unavailable.WorkerRestartRequired);

        runtime.RestartRequired = true;
        Assert.True(Dispatch(McpAccessMode.ReadOnly, request, runtime).WorkerRestartRequired);
        runtime.UnavailableReason = null;
        var success = Dispatch(McpAccessMode.ReadOnly, request, runtime);
        Assert.True(success.Success);
        Assert.True(success.WorkerRestartRequired);
        Assert.Contains("\"workerRestartRequired\":true", System.Text.Json.JsonSerializer.Serialize(success, WorkerJson.Envelope));
    }

    [Fact]
    public void Handler_SetScaleFactorReadsBeforeAndAfter()
    {
        var runtime = PlcSimTestHost.Standard();
        var result = (PlcSimScaleFactorResultInfo)Execute(runtime, PlcSimMethods.SetScaleFactor, new PlcSimRequestInfo { Instance = "Line1", ScaleFactor = 0.5 }, new List<string>());
        Assert.Equal("1", result.Previous!.Value.GetRawText());
        Assert.Equal("0.5", result.Current!.Value.GetRawText());
    }

    [Theory]
    [InlineData(PlcSimMethods.GetInstance, "{\"instance\":\"Line1\",\"max\":5}", "max is not valid")]
    [InlineData(PlcSimMethods.ProbeApi, "{\"instance\":\"Line1\"}", "instance is not valid")]
    [InlineData(PlcSimMethods.SetScaleFactor, "{\"instance\":\"Line1\",\"scaleFactor\":0}", "greater than zero")]
    [InlineData(PlcSimMethods.ReadTags, "{\"instance\":\"Line1\"}", "at least one tag")]
    [InlineData(PlcSimMethods.ListTags, "{\"instance\":\" \"}", "instance is required")]
    [InlineData(PlcSimMethods.RunTest, "{\"instance\":\"Line1\",\"test\":{\"steps\":[]}}", "at least one step")]
    public void Handler_RevalidatesEveryRequest(string method, string requestJson, string expected)
    {
        var request = JsonSerializer.Deserialize<PlcSimRequestInfo>(requestJson, WorkerJson.Envelope)!;
        var failure = Assert.Throws<PlcSimOperationException>(() => Execute(PlcSimTestHost.Standard(), method, request, new List<string>()));
        Assert.Equal(WorkerFailureCategories.ValidationError, failure.Category);
        Assert.Contains(expected, failure.Message);
    }

    [Fact]
    public void Handler_UnknownInstanceIsARuntimeErrorAndCaseMismatchIsNotFound()
    {
        var runtime = PlcSimTestHost.Standard();
        Assert.Equal(WorkerFailureCategories.TargetNotFound, Assert.Throws<PlcSimOperationException>(() =>
            Execute(runtime, PlcSimMethods.GetInstance, new PlcSimRequestInfo { Instance = "line1" }, new List<string>())).Category);
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, Assert.Throws<PlcSimOperationException>(() =>
            Execute(runtime, PlcSimMethods.GetInstance, new PlcSimRequestInfo { Instance = "Other" }, new List<string>())).Category);
    }

    private static WorkerResponse Dispatch(McpAccessMode mode, WorkerRequest request, FakePlcSimRuntime? runtime = null)
    {
        runtime ??= PlcSimTestHost.Standard();
        return new PlcSimWorkerDispatcher(mode, new PlcSimOperationHandler(runtime, runtime.Clock)).Handle(request);
    }

    [Fact]
    public void Dispatcher_AnswersHelloWithThePlcSimProtocolOnly()
    {
        var hello = Dispatch(McpAccessMode.ReadOnly, new WorkerRequest { Method = "hello", ProtocolVersion = PlcSimWorkerProtocol.Version });
        Assert.True(hello.Success);
        Assert.Equal(PlcSimWorkerProtocol.Version, hello.ProtocolVersion);
        Assert.Equal(PlcSimWorkerProtocol.RequiredCapabilities, hello.Capabilities);
        Assert.Contains("plcsim-restart-flag-v1", hello.Capabilities!);
        Assert.DoesNotContain(WorkerProtocol.RequiredCapabilities[0], hello.Capabilities!);

        var openness = Dispatch(McpAccessMode.ReadOnly, new WorkerRequest { Method = "hello", ProtocolVersion = WorkerProtocol.Version });
        Assert.False(openness.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, openness.FailureCategory);

        var stale = Dispatch(McpAccessMode.ReadOnly, new WorkerRequest { Method = PlcSimMethods.ProbeApi, ProtocolVersion = "old" });
        Assert.Contains("No PLCSIM API call was made", stale.Error);
    }

    [Fact]
    public void Dispatcher_AuthorizesBeforeAnyApiCall()
    {
        var runtime = PlcSimTestHost.Standard();
        var denied = Dispatch(McpAccessMode.ReadOnly, new WorkerRequest
        {
            Method = PlcSimMethods.PowerOn, ProtocolVersion = PlcSimWorkerProtocol.Version, PlcSim = new PlcSimRequestInfo { Instance = "Line1" }
        }, runtime);
        Assert.False(denied.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, denied.FailureCategory);
        Assert.Equal(0, runtime.Opened);

        var openness = Dispatch(McpAccessMode.Full, new WorkerRequest { Method = "get_project_status", ProtocolVersion = PlcSimWorkerProtocol.Version }, runtime);
        Assert.Equal(WorkerFailureCategories.ValidationError, openness.FailureCategory);
        Assert.Contains("Unsupported PLCSIM worker method", openness.Error);

        var unknown = Dispatch(McpAccessMode.Full, new WorkerRequest { Method = "plcsim_create_instance", ProtocolVersion = PlcSimWorkerProtocol.Version }, runtime);
        Assert.Equal(WorkerFailureCategories.AccessDenied, unknown.FailureCategory);
    }

    [Fact]
    public void Dispatcher_ReturnsTypedPayloadsWithExplicitNullsAndWarnings()
    {
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].MissingMembers.Add("IInstance.CPUType");
        var response = Dispatch(McpAccessMode.ReadOnly, new WorkerRequest
        {
            Method = PlcSimMethods.GetInstance, ProtocolVersion = PlcSimWorkerProtocol.Version, PlcSim = new PlcSimRequestInfo { Instance = "Line1" }
        }, runtime);
        Assert.True(response.Success, response.Error);
        Assert.Contains("\"cpuType\":null", response.Payload);
        Assert.Single(response.Warnings!);

        var failed = new PlcSimWorkerDispatcher(McpAccessMode.ReadOnly, new PlcSimOperationHandler(runtime, runtime.Clock)).HandleLine("{not json");
        Assert.Equal(WorkerFailureCategories.ValidationError, failed.FailureCategory);
    }
}
