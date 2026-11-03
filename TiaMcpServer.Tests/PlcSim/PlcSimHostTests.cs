using System.Text.Json;
using Microsoft.Win32;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.PlcSim;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Diagnostics.Checks;
using TiaMcpServer.Json;
using TiaMcpServer.PlcSim;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.Diagnostics;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.PlcSim;

public sealed class PlcSimHostTests
{
    private static JsonElement Json(string text) => PlcSimValueCodec.Parse(text);

    private static PlcSimOperationRequest Op(string id, string operation, string? instance = "Line1", List<PlcSimTagRequest>? tags = null,
        string? nameFilter = null, int? max = null, double? value = null)
        => new() { OperationId = id, Operation = operation, Instance = instance, Tags = tags, NameFilter = nameFilter, Max = max, Value = value };

    private static JsonElement Document(CallToolResult result)
    {
        var violations = StructuredContractInspector.FindViolations(result);
        Assert.True(violations.Count == 0, string.Join("; ", violations));
        return result.StructuredContent!.Value;
    }

    private static string[] AuditLines(string directory) => Directory.Exists(directory)
        ? Directory.GetFiles(directory, "writes-*.jsonl").SelectMany(File.ReadLines).ToArray()
        : Array.Empty<string>();

    [Fact]
    public void Catalog_SeparatesReadAndWriteOperationsStrictly()
    {
        Assert.Equal(new[] { "get_instance", "list_instances", "list_tags", "probe_api", "read_tags" }, PlcSimCatalog.ReadOperationNames.Order());
        Assert.Equal(new[] { "memory_reset", "power_off", "power_on", "run", "set_scale_factor", "stop", "update_tag_list", "write_tags" },
            PlcSimCatalog.WriteOperationNames.Order());
        Assert.All(PlcSimCatalog.ReadOperationNames.Concat(PlcSimCatalog.WriteOperationNames),
            operation => Assert.True(PlcSimMethods.IsPlcSimMethod(PlcSimCatalog.WorkerMethod(operation)), operation));

        var invalid = PlcSimCatalog.Validate(new[]
        {
            Op("a", "power_on"),
            Op("b", "probe_api", instance: "Line1"),
            Op("c", "list_tags", max: 5001, nameFilter: " "),
            Op("d", "read_tags", tags: new() { new PlcSimTagRequest { Name = "X", DataType = "word" } }),
            Op("e", "test"),
            Op("e", "get_instance"),
        }, write: false);
        Assert.False(invalid.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, invalid.Category);
        Assert.Contains("a: 'power_on' belongs to plcsim_write", invalid.Error);
        Assert.Contains("b: 'instance' is not valid for probe_api.", invalid.Error);
        Assert.Contains("c: nameFilter must be nonblank", invalid.Error);
        Assert.Contains("c: max must be 1 to 5000.", invalid.Error);
        Assert.Contains("d: tags[0].dataType must be one of", invalid.Error);
        Assert.Contains("e: scenarios run through plcsim_test.", invalid.Error);
        Assert.Contains("Duplicate operationId 'e'.", invalid.Error);

        var writes = PlcSimCatalog.Validate(new[]
        {
            Op("s", "set_scale_factor", value: -1),
            Op("w", "write_tags", tags: new() { new PlcSimTagRequest { Name = "X", DataType = "string", Value = Json("\"a\"") } }),
            Op("x", "run", instance: null),
        }, write: true);
        Assert.Contains("s: set_scale_factor requires value", writes.Error);
        Assert.Contains("WriteString is not a verified", writes.Error);
        Assert.Contains("x: instance is required", writes.Error);
        Assert.Contains("at least one operation", PlcSimCatalog.Validate(Array.Empty<PlcSimOperationRequest>(), write: true).Error);
        Assert.Contains("maximum of 50", PlcSimCatalog.Validate(Enumerable.Range(0, 51).Select(i => Op("i" + i, "run")).ToArray(), write: true).Error);

        Assert.Single(PlcSimCatalog.ValidateAccessMode(new[] { Op("a", "run") }, McpAccessMode.ReadOnly));
        Assert.Empty(PlcSimCatalog.ValidateAccessMode(new[] { Op("a", "run") }, McpAccessMode.ReadWrite));
        Assert.Empty(PlcSimCatalog.ValidateAccessMode(new[] { Op("a", "probe_api", instance: null) }, McpAccessMode.ReadOnly));
    }

    [Fact]
    public void PayloadContract_RejectsMalformedPayloadsWithoutEchoingThem()
    {
        var diagnostics = new List<string>();
        var item = Op("g", "get_instance");
        var missingMember = PlcSimPayloadContract.Project(item,
            WorkerCallResult.Ok("{\"requestedName\":\"Line1\",\"name\":\"Line1\",\"id\":1,\"operatingState\":\"Run\",\"operatingMode\":null,\"scaleFactor\":1,\"communicationInterface\":null}"),
            diagnostics.Add);
        Assert.Equal(WorkerFailureCategories.ProtocolError, missingMember.Failure!.Category);
        Assert.DoesNotContain("Line1", missingMember.Failure.Message);
        Assert.Single(diagnostics);

        var wrongInstance = PlcSimPayloadContract.Project(item, WorkerCallResult.Ok(JsonSerializer.Serialize(new PlcSimInstanceInfo { RequestedName = "Other" },
            TiaMcpServer.Contracts.Json.WorkerJson.ExplicitNullPayload)), diagnostics.Add);
        Assert.Equal(WorkerFailureCategories.ProtocolError, wrongInstance.Failure!.Category);

        var worker = PlcSimPayloadContract.Project(item, WorkerCallResult.Fail(WorkerFailureCategories.PlcSimApiUnavailable, "not installed"), diagnostics.Add);
        Assert.Equal("failed", worker.Status);
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, worker.Failure!.Category);
    }

    [Fact]
    public void PayloadContract_TrimsListTagsToTheValueBudget()
    {
        var tags = Enumerable.Range(0, 400).Select(i => Json($"{{\"Name\":\"Tag_{i:D4}_{new string('x', 200)}\"}}")).ToList();
        var payload = TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(new PlcSimTagListInfo
        {
            Instance = "Line1", Max = 500, TotalCount = 400, MatchedCount = 400, ReturnedCount = 400, Tags = tags
        });
        var item = PlcSimPayloadContract.Project(Op("t", "list_tags"), WorkerCallResult.Ok(payload));
        Assert.Equal("succeeded", item.Status);
        var result = item.Result!.Value;
        Assert.True(result.GetProperty("budgetTrimmed").GetBoolean());
        Assert.True(result.GetProperty("truncated").GetBoolean());
        var returned = result.GetProperty("returnedCount").GetInt32();
        Assert.InRange(returned, 1, 399);
        Assert.Equal(returned, result.GetProperty("tags").GetArrayLength());
        Assert.True(CanonicalJson.Serialize(result).Length <= TiaMcpServer.OperationBatches.StructuredOperationBatchPayloadBudget.MaxItemChars);
        Assert.Contains(item.Warnings, warning => warning.Contains("--max-item-chars", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Client_AuthorizesBeforeStartingAWorkerAndSendsOnlyPlcSimMethods()
    {
        var created = 0;
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadOnly),
            channelFactory: () => { created++; throw new InvalidOperationException("must not start"); });
        var denied = await client.InvokeAsync(PlcSimMethods.PowerOn, new PlcSimRequestInfo { Instance = "Line1" });
        Assert.Equal(WorkerFailureCategories.AccessDenied, denied.FailureCategory);
        var foreign = await client.InvokeAsync("get_project_status", null);
        Assert.Equal(WorkerFailureCategories.ValidationError, foreign.FailureCategory);
        Assert.Equal(0, created);
        Assert.False(client.HasStartedWorker);
    }

    [Theory]
    [InlineData(PlcSimMethods.ReadTags, true)]
    [InlineData(PlcSimMethods.WriteTags, false)]
    public async Task Client_MapsTransportFailuresWithReadOrWriteGuidance(string method, bool safe)
    {
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full), TimeSpan.FromMinutes(5),
            channelFactory: () => new ThrowingChannel(new TimeoutException()));
        var timeout = await client.InvokeAsync(method, new PlcSimRequestInfo { Instance = "Line1" });
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, timeout.FailureCategory);
        Assert.Equal(safe, timeout.Error!.Contains("retrying the read is safe", StringComparison.Ordinal));
        Assert.Equal(!safe, timeout.Error.Contains("may have changed", StringComparison.Ordinal));

        var crash = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full),
            channelFactory: () => new ThrowingChannel(new IOException("pipe")));
        Assert.Equal(WorkerFailureCategories.WorkerCrashed, (await crash.InvokeAsync(method, null)).FailureCategory);

        var missing = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full), workerExecutablePath: null,
            channelFactory: () => throw new FileNotFoundException("The PLCSIM worker executable was not found."));
        var notFound = await missing.InvokeAsync(method, null);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, notFound.FailureCategory);
        Assert.False(missing.HasStartedWorker);
    }

    [Fact]
    public async Task Client_MapsLaunchProtocolAndShutdownFailures()
    {
        async Task<WorkerCallResult> With(Exception exception)
            => await new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full), channelFactory: () => new ThrowingChannel(exception))
                .InvokeAsync(PlcSimMethods.ProbeApi, null);

        var launch = await With(new System.ComponentModel.Win32Exception(2, "missing"));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, launch.FailureCategory);
        Assert.Contains("plcsim-worker", launch.Error);
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            (await With(new PersistentWorkerTransport.WorkerProtocolMismatchException("mismatch"))).FailureCategory);
        Assert.Contains("shutting down", (await With(new PersistentWorkerTransport.WorkerTransportDisposedException())).Error);

        var unknownCategory = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full),
            channelFactory: () => new FixedChannel(new WorkerResponse { Success = false, FailureCategory = "made_up", Error = "x", Warnings = new List<string> { " a ", "" } }));
        var mapped = await unknownCategory.InvokeAsync(PlcSimMethods.ProbeApi, null);
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, mapped.FailureCategory);
        Assert.Equal(new[] { "a" }, mapped.Warnings);
        Assert.True(unknownCategory.HasStartedWorker);

        unknownCategory.Dispose();
        var afterDispose = await unknownCategory.InvokeAsync(PlcSimMethods.ProbeApi, null);
        Assert.Contains("shutting down", afterDispose.Error);
    }

    [Fact]
    public void Locator_PrefersThePackagedFolderThenADevelopmentBuild()
    {
        var files = new FakeFileSystemService();
        var root = Path.Combine(Path.GetTempPath(), "plcsim-locator-" + Guid.NewGuid().ToString("N"));
        var host = Path.Combine(root, "TiaMcpServer", "bin", "Release", "net10.0");
        var dev = Path.Combine(root, "TiaMcpServer.PlcSimWorker", "bin", "Release", "net48", PlcSimWorkerLocator.WorkerExecutableName);
        var debug = Path.Combine(root, "TiaMcpServer.PlcSimWorker", "bin", "Debug", "net48", PlcSimWorkerLocator.WorkerExecutableName);
        files.AddFile(dev);
        files.AddFile(debug);
        // Without the repository marker no parent directory is probed.
        Assert.False(PlcSimWorkerLocator.Locate(host, files).Found);
        files.AddFile(Path.Combine(root, PlcSimWorkerLocator.RepositoryMarker));
        var found = PlcSimWorkerLocator.Locate(host, files);
        Assert.True(found.Found);
        Assert.Equal(dev, found.SelectedPath); // Release before Debug.

        // A worker above the nearest repository root is never used.
        var outer = Path.Combine(root, "nested");
        var nestedHost = Path.Combine(outer, "TiaMcpServer", "bin", "Release", "net10.0");
        files.AddFile(Path.Combine(outer, PlcSimWorkerLocator.RepositoryMarker));
        Assert.False(PlcSimWorkerLocator.Locate(nestedHost, files).Found);

        var packaged = Path.Combine(host, PlcSimWorkerLocator.WorkerFolderName, PlcSimWorkerLocator.WorkerExecutableName);
        files.AddFile(packaged);
        Assert.Equal(packaged, PlcSimWorkerLocator.LocateOrThrow(host, files));
        var missing = Assert.Throws<FileNotFoundException>(() => PlcSimWorkerLocator.LocateOrThrow(host, new FakeFileSystemService()));
        Assert.Contains("plcsim-worker folder", missing.Message);
    }

    [Fact]
    public async Task ReadTool_RunsIndependentItemsThroughTheWorker()
    {
        var runtime = PlcSimTestHost.Standard();
        var (client, channel) = PlcSimTestHost.Create(runtime, McpAccessMode.ReadOnly);
        var result = await PlcSimReadTools.PlcSimRead(client, new[]
        {
            Op("probe", "probe_api", instance: null),
            Op("list", "list_instances", instance: null),
            Op("inst", "get_instance"),
            Op("tags", "list_tags", nameFilter: "e"),
            Op("read", "read_tags", tags: new() { new() { Name = "Speed", DataType = "int16" }, new() { Name = "Label", DataType = "string" } }),
            Op("missing", "get_instance", instance: "Unknown"),
        });

        var document = Document(result);
        Assert.False(result.IsError);
        Assert.Equal("plcsim_read", document.GetProperty("tool").GetString());
        Assert.Equal("1.0", document.GetProperty("contractVersion").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        var operations = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "succeeded", "succeeded", "succeeded", "succeeded", "succeeded", "failed" },
            operations.EnumerateArray().Select(item => item.GetProperty("status").GetString()));
        Assert.Equal("\"Off\"", operations[2].GetProperty("result").GetProperty("operatingState").GetRawText());
        Assert.Equal("\"Ready\"", operations[4].GetProperty("result").GetProperty("tags")[1].GetProperty("value").GetRawText());
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, operations[5].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(6, channel.Requests.Count);
        Assert.Equal(500, channel.Requests[3].PlcSim!.Max);
    }

    [Fact]
    public async Task ReadTool_RejectsBeforeStartingTheWorker()
    {
        var created = false;
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadOnly), channelFactory: () => { created = true; throw new InvalidOperationException(); });
        var result = await PlcSimReadTools.PlcSimRead(client, new[] { Op("x", "run") });
        var document = Document(result);
        Assert.True(result.IsError);
        Assert.Equal(WorkerFailureCategories.ValidationError, document.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("batch").ValueKind);
        Assert.False(created);
    }

    [Fact]
    public async Task WriteTool_DryRunPlansWithoutStartingTheWorkerAndIsAudited()
    {
        using var audit = new TempAuditDirectory();
        var runtime = PlcSimTestHost.Standard();
        var created = false;
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadWrite), channelFactory: () => { created = true; throw new InvalidOperationException(); });
        var result = await PlcSimWriteTools.PlcSimWrite(client, new PlcSimAudit(new JsonlWriteAuditSink(audit.Path)), new[]
        {
            Op("on", "power_on"),
            Op("w", "write_tags", tags: new() { new() { Name = "Temperature", DataType = "float", Value = Json("0.1") } }),
            Op("tl", "update_tag_list"),
        }, dryRun: true);

        var document = Document(result);
        Assert.False(result.IsError);
        Assert.False(created);
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        Assert.True(document.GetProperty("dryRun").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("batch").ValueKind);
        var plan = document.GetProperty("plan");
        Assert.Equal("plcsim_power_on", plan[0].GetProperty("workerMethod").GetString());
        Assert.Contains("IInstance.WriteFloat", plan[1].GetProperty("apiMembers").EnumerateArray().Select(member => member.GetString()));
        Assert.Equal("0.1", plan[1].GetProperty("arguments").GetProperty("tags")[0].GetProperty("value").GetRawText());
        Assert.Equal(PlcSimWarnings.UpdateTagListColdRestart, plan[2].GetProperty("warnings")[0].GetString());

        var line = Assert.Single(AuditLines(audit.Path));
        using var record = JsonDocument.Parse(line);
        Assert.Equal("plcsim", record.RootElement.GetProperty("recordKind").GetString());
        Assert.Equal("preview", record.RootElement.GetProperty("phase").GetString());
        Assert.Equal(JsonValueKind.Null, record.RootElement.GetProperty("binding").ValueKind);
        Assert.Equal(JsonValueKind.Null, record.RootElement.GetProperty("projectPath").ValueKind);
        Assert.Equal("none", record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.StartsWith("sha256:", record.RootElement.GetProperty("responseHash").GetString());
        Assert.Equal(CanonicalJson.Serialize(document), record.RootElement.GetProperty("responseText").GetString());
    }

    [Fact]
    public async Task WriteTool_AFailedControlReturnFailsTheItemWithItsEvidenceAndStops()
    {
        using var audit = new TempAuditDirectory();
        var runtime = PlcSimTestHost.Standard();
        var instance = runtime.Instances["Line1"];
        instance.ControlReturnValue = Json("\"NotRunning\"");
        instance.ControlReturnCheck = PlcSimReturnChecks.Failed;
        instance.ControlReturnCode = "NotRunning";
        var (client, _) = PlcSimTestHost.Create(runtime, McpAccessMode.Full);

        var result = await PlcSimWriteTools.PlcSimWrite(client, new PlcSimAudit(new JsonlWriteAuditSink(audit.Path)),
            new[] { Op("on", "power_on"), Op("run", "run") });

        var document = Document(result);
        Assert.False(result.IsError);
        Assert.False(document.GetProperty("success").GetBoolean());
        var operations = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "failed", "skipped" }, operations.EnumerateArray().Select(item => item.GetProperty("status").GetString()));
        var failure = operations[0].GetProperty("failure");
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, failure.GetProperty("category").GetString());
        Assert.Contains("returned NotRunning", failure.GetProperty("message").GetString());
        var value = operations[0].GetProperty("result");
        Assert.Equal(PlcSimReturnChecks.Failed, value.GetProperty("returnCheck").GetString());
        Assert.Equal("\"Stop\"", value.GetProperty("operatingStateAfter").GetRawText());
        Assert.Equal(new[] { "PowerOn" }, instance.Calls);
    }

    [Fact]
    public void PayloadContract_RejectsInconsistentControlReturnChecks()
    {
        var item = Op("s", "stop");
        PlcSimControlResultInfo Control(string check, JsonElement? value, string? code) => new()
        {
            Instance = "Line1", Operation = "stop", InvokedSignature = "Void Stop(UInt32 in_TimeOut)", ReturnType = "ERuntimeErrorCode",
            ReturnValue = value, ReturnCheck = check, ReturnCode = code,
        };
        string Payload(PlcSimControlResultInfo control) => TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(control);

        Assert.Equal("succeeded", PlcSimPayloadContract.Project(item, WorkerCallResult.Ok(Payload(Control(PlcSimReturnChecks.Ok, Json("\"OK\""), "OK"))), _ => { }).Status);
        Assert.Equal("succeeded", PlcSimPayloadContract.Project(item, WorkerCallResult.Ok(Payload(Control(PlcSimReturnChecks.Uninterpretable, Json("\"Success\""), "Success"))), _ => { }).Status);
        Assert.Equal("succeeded", PlcSimPayloadContract.Project(item, WorkerCallResult.Ok(Payload(Control(PlcSimReturnChecks.Warning, Json("\"AlreadyRunning\""), "AlreadyRunning"))), _ => { }).Status);
        Assert.Equal("failed", PlcSimPayloadContract.Project(item, WorkerCallResult.Ok(Payload(Control(PlcSimReturnChecks.Failed, Json("\"Timeout\""), "Timeout"))), _ => { }).Status);
        foreach (var bad in new[]
        {
            Control("judged", Json("\"OK\""), "OK"),
            Control(PlcSimReturnChecks.Failed, null, "Timeout"),
            Control(PlcSimReturnChecks.NotInterpreted, null, "Timeout"),
        })
        {
            var projected = PlcSimPayloadContract.Project(item, WorkerCallResult.Ok(Payload(bad)), _ => { });
            Assert.Equal(WorkerFailureCategories.ProtocolError, projected.Failure!.Category);
        }
    }

    [Fact]
    public async Task Client_RecyclesTheWorkerOnlyWhenTheWorkerStatesARestartIsRequired()
    {
        var channels = new List<ScriptedChannel>();
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full), channelFactory: () =>
        {
            var channel = new ScriptedChannel(channels.Count == 0
                ? new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.PlcSimApiUnavailable, Error = "type initializer failed", WorkerRestartRequired = true }
                : new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.PlcSimRuntimeError, Error = "no such instance" });
            channels.Add(channel);
            return channel;
        });

        var unavailable = await client.InvokeAsync(PlcSimMethods.ListInstances, null);
        Assert.Equal(WorkerFailureCategories.PlcSimApiUnavailable, unavailable.FailureCategory);
        Assert.True(Assert.Single(channels).Disposed);
        Assert.False(client.HasStartedWorker);

        // The next call starts a fresh worker; an ordinary runtime error keeps it.
        var runtimeError = await client.InvokeAsync(PlcSimMethods.GetInstance, new PlcSimRequestInfo { Instance = "Line1" });
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, runtimeError.FailureCategory);
        Assert.Equal(2, channels.Count);
        Assert.False(channels[1].Disposed);
        await client.InvokeAsync(PlcSimMethods.GetInstance, new PlcSimRequestInfo { Instance = "Line1" });
        Assert.Equal(2, channels.Count);
        Assert.Equal(2, channels[1].Requests);
        client.Dispose();
        Assert.True(channels[1].Disposed);
    }

    [Fact]
    public async Task Client_KeepsTheWorkerForAMissingApi_AndRecyclesAfterAnItemLevelRestartFlag()
    {
        var created = 0;
        var missing = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadOnly), channelFactory: () =>
        {
            created++;
            return new ScriptedChannel(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.PlcSimApiUnavailable, Error = "DLL not found" });
        });
        await missing.InvokeAsync(PlcSimMethods.ListInstances, null);
        await missing.InvokeAsync(PlcSimMethods.ListInstances, null);
        Assert.Equal(1, created);
        Assert.True(missing.HasStartedWorker);

        // A successful read whose tag-level failure hit the cached initializer still recycles.
        var poisoned = new ScriptedChannel(new WorkerResponse { Success = true, Payload = "{}", WorkerRestartRequired = true });
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadOnly), channelFactory: () => poisoned);
        Assert.True((await client.InvokeAsync(PlcSimMethods.ReadTags, new PlcSimRequestInfo { Instance = "Line1" })).Success);
        Assert.True(poisoned.Disposed);
        Assert.False(client.HasStartedWorker);
    }

    [Fact]
    public async Task WriteTool_AWarningReturnCodeDoesNotStopLaterItems()
    {
        using var audit = new TempAuditDirectory();
        var runtime = PlcSimTestHost.Standard();
        var instance = runtime.Instances["Line1"];
        instance.ControlReturnValue = Json("\"AlreadyRunning\"");
        instance.ControlReturnCheck = PlcSimReturnChecks.Warning;
        instance.ControlReturnCode = "AlreadyRunning";
        var (client, _) = PlcSimTestHost.Create(runtime, McpAccessMode.Full);

        var result = await PlcSimWriteTools.PlcSimWrite(client, new PlcSimAudit(new JsonlWriteAuditSink(audit.Path)),
            new[] { Op("on", "power_on"), Op("run", "run") });

        var document = Document(result);
        Assert.True(document.GetProperty("success").GetBoolean());
        var operations = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "succeeded", "succeeded" }, operations.EnumerateArray().Select(item => item.GetProperty("status").GetString()));
        Assert.Equal(PlcSimReturnChecks.Warning, operations[0].GetProperty("result").GetProperty("returnCheck").GetString());
        Assert.Contains(operations[0].GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("warning code AlreadyRunning", StringComparison.Ordinal));
        Assert.Equal(new[] { "PowerOn", "Run" }, instance.Calls);
    }

    [Theory]
    [InlineData("{\"apiInitializationFailed\":true}", true)]
    [InlineData("{\"apiInitializationFailed\":false}", false)]
    [InlineData("not json", false)]
    public async Task Client_RecyclesTheWorkerAfterAProbeReportingAFailedInitialization(string payload, bool recycled)
    {
        var channels = new List<ScriptedChannel>();
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadOnly), channelFactory: () =>
        {
            var channel = new ScriptedChannel(new WorkerResponse { Success = true, Payload = payload });
            channels.Add(channel);
            return channel;
        });

        Assert.True((await client.InvokeAsync(PlcSimMethods.ProbeApi, null)).Success);
        Assert.Equal(recycled, channels[0].Disposed);
        Assert.Equal(!recycled, client.HasStartedWorker);
        // Only the probe's typed flag counts: the same payload from another read does not recycle.
        Assert.False(PlcSimWorkerClient.RequiresRestart(PlcSimMethods.ListInstances, new WorkerResponse { Success = true, Payload = payload }));
    }

    [Fact]
    public async Task Client_RecycleWaitsForAConcurrentRequestOnTheSameWorker()
    {
        var release = new TaskCompletionSource<WorkerResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channels = new List<ScriptedChannel>();
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.Full), channelFactory: () =>
        {
            var channel = new ScriptedChannel(new WorkerResponse { Success = false, FailureCategory = WorkerFailureCategories.PlcSimApiUnavailable, Error = "x", WorkerRestartRequired = true })
            {
                Pending = channels.Count == 0 ? release.Task : null,
            };
            channels.Add(channel);
            return channel;
        });

        var slow = client.InvokeAsync(PlcSimMethods.ReadTags, new PlcSimRequestInfo { Instance = "Line1" });
        channels[0].Pending = null;
        await client.InvokeAsync(PlcSimMethods.ListInstances, null);
        Assert.False(channels[0].Disposed); // The first request still uses this worker.
        Assert.False(client.HasStartedWorker);

        release.SetResult(new WorkerResponse { Success = true, Payload = "{}" });
        Assert.True((await slow).Success);
        Assert.True(channels[0].Disposed);
        Assert.Single(channels);
    }

    [Fact]
    public async Task WriteTool_StopsAtTheFirstFailedItemAndAuditsActualStatuses()
    {
        using var audit = new TempAuditDirectory();
        var runtime = PlcSimTestHost.Standard();
        var (client, _) = PlcSimTestHost.Create(runtime, McpAccessMode.Full);
        var result = await PlcSimWriteTools.PlcSimWrite(client, new PlcSimAudit(new JsonlWriteAuditSink(audit.Path)), new[]
        {
            Op("on", "power_on"),
            Op("w", "write_tags", tags: new()
            {
                new() { Name = "Speed", DataType = "int16", Value = Json("7") },
                new() { Name = "Missing", DataType = "bool", Value = Json("true") },
            }),
            Op("run", "run"),
        });

        var document = Document(result);
        Assert.False(result.IsError);
        Assert.Equal("applied", document.GetProperty("phase").GetString());
        Assert.False(document.GetProperty("success").GetBoolean());
        var operations = document.GetProperty("batch").GetProperty("operations");
        Assert.Equal(new[] { "succeeded", "failed", "skipped" }, operations.EnumerateArray().Select(item => item.GetProperty("status").GetString()));
        Assert.Equal("\"Stop\"", operations[0].GetProperty("result").GetProperty("operatingStateAfter").GetRawText());
        var failed = operations[1];
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, failed.GetProperty("failure").GetProperty("category").GetString());
        Assert.Contains("wrote 1 of 2 tags before 'Missing' failed", failed.GetProperty("failure").GetProperty("message").GetString());
        Assert.Equal(1, failed.GetProperty("result").GetProperty("writtenCount").GetInt32());
        Assert.Equal("earlierOperationFailed", operations[2].GetProperty("skipReason").GetString());
        Assert.Equal("Stop", runtime.Instances["Line1"].OperatingState);

        using var record = JsonDocument.Parse(Assert.Single(AuditLines(audit.Path)));
        Assert.Equal("policy", record.RootElement.GetProperty("confirmation").GetProperty("by").GetString());
        Assert.Equal(new[] { "succeeded", "failed", "skipped" },
            record.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()));
        Assert.Equal("Line1", record.RootElement.GetProperty("items")[0].GetProperty("target").GetString());
    }

    [Fact]
    public async Task WriteTool_ValidationAndAccessDenialsAreAuditedRejections()
    {
        using var audit = new TempAuditDirectory();
        var (client, channel) = PlcSimTestHost.Create(PlcSimTestHost.Standard(), McpAccessMode.ReadOnly);
        var sink = new PlcSimAudit(new JsonlWriteAuditSink(audit.Path));
        var invalid = await PlcSimWriteTools.PlcSimWrite(client, sink, new[] { Op("p", "probe_api", instance: null) });
        Assert.True(invalid.IsError);
        Assert.Equal(WorkerFailureCategories.ValidationError, Document(invalid).GetProperty("error").GetProperty("category").GetString());
        var denied = await PlcSimWriteTools.PlcSimWrite(client, sink, new[] { Op("r", "run") });
        Assert.Equal(WorkerFailureCategories.AccessDenied, Document(denied).GetProperty("error").GetProperty("category").GetString());
        Assert.Equal("error", Document(denied).GetProperty("phase").GetString());
        Assert.Empty(channel.Requests);
        Assert.Equal(2, AuditLines(audit.Path).Length);
    }

    private static PlcSimTestStep Step(string kind, string? tag = null, string? dataType = null, string? value = null, string? comparison = null,
        int? ms = null, int? timeoutMs = null, int? pollMs = null, string? name = null)
        => new()
        {
            Kind = kind, Tag = tag, DataType = dataType, Value = value is null ? null : Json(value), Comparison = comparison,
            Ms = ms, TimeoutMs = timeoutMs, PollMs = pollMs, Name = name
        };

    [Fact]
    public async Task TestTool_RunsTheScenarioInsideTheWorker()
    {
        using var audit = new TempAuditDirectory();
        var runtime = PlcSimTestHost.Standard();
        runtime.Instances["Line1"].React("Start", 250, "Running", true);
        var (client, channel) = PlcSimTestHost.Create(runtime);
        var result = await PlcSimTestTools.PlcSimTest(client, new PlcSimAudit(new JsonlWriteAuditSink(audit.Path)), "Line1", new[]
        {
            Step("write", "Start", "bool", "true", name: "start"),
            Step("expect", "Running", "bool", "true", "equals", timeoutMs: 1000),
            Step("wait", ms: 100),
        });

        var document = Document(result);
        Assert.False(result.IsError);
        Assert.True(document.GetProperty("success").GetBoolean());
        Assert.Equal("executed", document.GetProperty("phase").GetString());
        var scenario = document.GetProperty("result");
        Assert.True(scenario.GetProperty("passed").GetBoolean());
        Assert.Equal("start", scenario.GetProperty("steps")[0].GetProperty("name").GetString());
        Assert.Equal(6, scenario.GetProperty("steps")[1].GetProperty("attempts").GetInt32());
        Assert.Equal(PlcSimMethods.RunTest, Assert.Single(channel.Requests).Method);
        Assert.Equal(1000, channel.Requests[0].PlcSim!.Test!.Steps![1].TimeoutMs);
        Assert.Equal(50, channel.Requests[0].PlcSim!.Test!.Steps![1].PollMs);

        using var record = JsonDocument.Parse(Assert.Single(AuditLines(audit.Path)));
        Assert.Equal("plcsim_test", record.RootElement.GetProperty("tool").GetString());
        Assert.Equal("succeeded", record.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task TestTool_ClampsMaxDurationBelowTheWorkerTimeoutAndDryRunsWithoutTheWorker()
    {
        using var audit = new TempAuditDirectory();
        var created = false;
        var client = new PlcSimWorkerClient(new OperationAccessPolicy(McpAccessMode.ReadWrite), TimeSpan.FromMinutes(5),
            channelFactory: () => { created = true; throw new InvalidOperationException(); });
        var result = await PlcSimTestTools.PlcSimTest(client, new PlcSimAudit(new JsonlWriteAuditSink(audit.Path)), "Line1",
            new[] { Step("expect", "Speed", "int16", "1", "greaterOrEqual") }, maxDurationMs: 600_000, dryRun: true);

        var document = Document(result);
        Assert.False(created);
        Assert.Equal("preview", document.GetProperty("phase").GetString());
        var plan = document.GetProperty("plan");
        Assert.Equal(270_000, plan.GetProperty("maxDurationMs").GetInt32());
        Assert.Equal(300_000, plan.GetProperty("workerRequestTimeoutMs").GetInt32());
        Assert.Equal(2000, plan.GetProperty("steps")[0].GetProperty("timeoutMs").GetInt32());
        Assert.Contains("IInstance.ReadInt16", plan.GetProperty("apiMembers").EnumerateArray().Select(member => member.GetString()));
        Assert.Contains(document.GetProperty("warnings").EnumerateArray(), warning => warning.GetString()!.Contains("clamped to 270000", StringComparison.Ordinal));
        Assert.Single(AuditLines(audit.Path));
    }

    [Fact]
    public async Task TestTool_ReportsFailuresAsResultsAndWorkerFailuresAsErrors()
    {
        using var audit = new TempAuditDirectory();
        var runtime = PlcSimTestHost.Standard();
        var (client, _) = PlcSimTestHost.Create(runtime);
        var sink = new PlcSimAudit(new JsonlWriteAuditSink(audit.Path));

        var failing = await PlcSimTestTools.PlcSimTest(client, sink, "Line1", new[] { Step("expect", "Speed", "int16", "5", "equals", timeoutMs: 100) });
        Assert.False(failing.IsError);
        var document = Document(failing);
        Assert.False(document.GetProperty("success").GetBoolean());
        Assert.False(document.GetProperty("result").GetProperty("passed").GetBoolean());
        Assert.Equal("failed", document.GetProperty("result").GetProperty("steps")[0].GetProperty("status").GetString());

        var unknown = await PlcSimTestTools.PlcSimTest(client, sink, "Nope", new[] { Step("wait", ms: 1) });
        Assert.True(unknown.IsError);
        Assert.Equal(WorkerFailureCategories.PlcSimRuntimeError, Document(unknown).GetProperty("error").GetProperty("category").GetString());

        var invalid = await PlcSimTestTools.PlcSimTest(client, sink, "Line1", new[] { Step("expect", "Label", "string", "\"x\"", "lessThan") });
        Assert.True(invalid.IsError);
        Assert.Contains("not supported for string", Document(invalid).GetProperty("error").GetProperty("message").GetString());

        var readOnly = PlcSimTestHost.Create(runtime, McpAccessMode.ReadOnly).Client;
        var denied = await PlcSimTestTools.PlcSimTest(readOnly, sink, "Line1", new[] { Step("wait", ms: 1) });
        Assert.Equal(WorkerFailureCategories.AccessDenied, Document(denied).GetProperty("error").GetProperty("category").GetString());

        var lines = AuditLines(audit.Path);
        Assert.Equal(4, lines.Length);
        using var failedRecord = JsonDocument.Parse(lines[0]);
        var item = failedRecord.RootElement.GetProperty("items")[0];
        Assert.Equal("failed", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("failureCategory").ValueKind);
    }

    [Fact]
    public void TestBudget_DropsDetailInOrderAndFallsBackToAnError()
    {
        PlcSimTestResultInfo Result() => new()
        {
            Instance = "Line1",
            Passed = true,
            Summary = new PlcSimTestSummaryInfo { Total = 50, Passed = 50 },
            Steps = Enumerable.Range(0, 50).Select(index => new PlcSimTestStepResultInfo
            {
                Index = index, Kind = "expect", Status = "passed", Tag = "T", Message = new string('m', 300), LastObserved = Json("1")
            }).ToList()
        };

        PlcSimTestResponse Response() => new("plcsim_test", "1.0", true, PlcSimPhases.Executed, false, null, Array.Empty<string>(), null, Result());

        var fits = PlcSimTestTools.Fit(Response(), maxDocumentChars: 1_000_000);
        Assert.All(fits.Result!.Steps, step => Assert.NotNull(step.Message));

        var trimmed = PlcSimTestTools.Fit(Response(), maxDocumentChars: 12_000);
        Assert.NotNull(trimmed.Result);
        Assert.All(trimmed.Result!.Steps, step => Assert.Null(step.Message));
        Assert.Contains(trimmed.Warnings, warning => warning.StartsWith("50 step details", StringComparison.Ordinal));
        Assert.True(CanonicalJson.Serialize(trimmed).Length <= 12_000);

        var tooSmall = PlcSimTestTools.Fit(Response(), maxDocumentChars: 2_000);
        Assert.Null(tooSmall.Result);
        Assert.Equal(WorkerFailureCategories.ResultMetadataTooLarge, tooSmall.Error!.Category);
        Assert.Equal(PlcSimPhases.Error, tooSmall.Phase);
    }

    [Fact]
    public void DoctorCheck_WithoutRegistry_ReportsAbsenceOverrideAndWorkerOnEveryPlatform()
    {
        var app = new FakeApplicationInfoService { BaseDirectory = Path.Combine("host") };
        var files = new FakeFileSystemService();
        files.AddFile(Path.Combine("host", PlcSimWorkerLocator.WorkerFolderName, PlcSimWorkerLocator.WorkerExecutableName));
        var env = new FakeEnvironmentVariableService();
        var registry = new EmptyRegistry();

        var absent = new PlcSimAdvancedCheck(app, env, registry, files, _ => Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Passed, absent.Status);
        Assert.Equal("none", absent.Evidence!["apiSource"]);
        Assert.Equal("true", absent.Evidence["workerFound"]);

        env.Set(PlcSimApiLocator.EnvironmentVariable, Path.Combine("custom"));
        var overridden = new PlcSimAdvancedCheck(app, env, registry, files, _ => Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Warning, overridden.Status);
        Assert.Equal("environment", overridden.Evidence!["apiSource"]);

        var noWorker = new PlcSimAdvancedCheck(new FakeApplicationInfoService { BaseDirectory = Path.Combine("elsewhere") }, env, registry, files, _ => Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Warning, noWorker.Status);
        Assert.Contains("worker executable was not found", noWorker.Message);
    }

    /// <summary>A registry with no values, usable on every platform (no Windows-only fake).</summary>
    private sealed class EmptyRegistry : TiaMcpServer.Diagnostics.IRegistryService
    {
        public string? GetStringValue(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string subKey, string valueName) => null;

        public int? GetIntValue(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string subKey, string valueName) => null;

        public bool KeyExists(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string subKey) => false;
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void DoctorCheck_ReportsTheResolutionWithoutLoadingAnything()
    {
        var app = new FakeApplicationInfoService { BaseDirectory = Path.Combine("host") };
        var files = new FakeFileSystemService();
        files.AddFile(Path.Combine("host", PlcSimWorkerLocator.WorkerFolderName, PlcSimWorkerLocator.WorkerExecutableName));
        var env = new FakeEnvironmentVariableService();
        var registry = new FakeRegistryService();

        var absent = new PlcSimAdvancedCheck(app, env, registry, files, _ => Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Passed, absent.Status);
        Assert.Contains("optional", absent.Message);
        Assert.Equal("none", absent.Evidence!["apiSource"]);
        Assert.Equal("true", absent.Evidence["workerFound"]);

        var root = Path.GetFullPath(Path.Combine("pf", "PLCSIMADV"));
        var api = Path.Combine(root, "API");
        registry.SetStringValue(RegistryHive.LocalMachine, RegistryView.Registry64, PlcSimApiLocator.RegistryKeyPath, PlcSimApiLocator.RegistryValueName, root);
        files.AddDirectory(api);
        var broken = new PlcSimAdvancedCheck(app, env, registry, files, path => path == api ? new[] { "5.0", "6.0" } : Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Warning, broken.Status);
        Assert.Equal("registry", broken.Evidence!["apiSource"]);
        Assert.Equal("6.0", broken.Evidence["selectedVersion"]);
        Assert.Equal("false", broken.Evidence["dllExists"]);

        files.AddFile(Path.Combine(api, "6.0", PlcSimApiLocator.ApiDllName));
        var found = new PlcSimAdvancedCheck(app, env, registry, files, path => path == api ? new[] { "5.0", "6.0" } : Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Passed, found.Status);
        Assert.Contains("probe_api", found.Message);

        var dir = Path.Combine("custom");
        env.Set(PlcSimApiLocator.EnvironmentVariable, dir);
        var overridden = new PlcSimAdvancedCheck(app, env, registry, files, _ => Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Warning, overridden.Status);
        Assert.Equal("environment", overridden.Evidence!["apiSource"]);

        var noWorker = new PlcSimAdvancedCheck(new FakeApplicationInfoService { BaseDirectory = Path.Combine("elsewhere") }, env, registry, files, _ => Array.Empty<string>()).Run();
        Assert.Equal(DiagnosticStatus.Warning, noWorker.Status);
        Assert.Contains("worker executable was not found", noWorker.Message);
    }

    private sealed class FixedChannel : IPlcSimWorkerChannel
    {
        private readonly WorkerResponse _response;

        public FixedChannel(WorkerResponse response) => _response = response;

        public Task<WorkerResponse> SendAsync(WorkerRequest request) => Task.FromResult(_response);

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedChannel : IPlcSimWorkerChannel
    {
        private readonly WorkerResponse _response;

        public ScriptedChannel(WorkerResponse response) => _response = response;

        /// <summary>When set, the next request waits for this task instead of answering immediately.</summary>
        public Task<WorkerResponse>? Pending { get; set; }

        public int Requests { get; private set; }

        public bool Disposed { get; private set; }

        public Task<WorkerResponse> SendAsync(WorkerRequest request)
        {
            Requests++;
            return Pending ?? Task.FromResult(_response);
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingChannel : IPlcSimWorkerChannel
    {
        private readonly Exception _exception;

        public ThrowingChannel(Exception exception) => _exception = exception;

        public Task<WorkerResponse> SendAsync(WorkerRequest request) => Task.FromException<WorkerResponse>(_exception);

        public void Dispose()
        {
        }
    }
}
