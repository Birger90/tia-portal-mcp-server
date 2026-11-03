using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.PlcWebApi;

[McpServerToolType]
public class PlcWebApiReadTools
{
    internal const string ToolName = "plc_online_read";

    [McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(OperationBatchReadResponse))]
    [Description("Observe real S7-1500 CPUs (and PLCSIM Advanced instances with the web server enabled) online through the SIMATIC S7-1500 Web API, with up to 50 independent operations (a failed item does not stop the others). "
        + "Every operation takes target, the exact name of a PLC configured by the user in the server's --plc-webapi-config file; credentials and addresses are never tool arguments. "
        + "Operations: ping, version, permissions (Api.GetPermissions of the configured user), read_operating_mode, read_mode_selector, read_cpu_type, read_runtime_information (cycle times and load), read_system_time, read_project_information, "
        + "browse_tags (PlcProgram.Browse: var, browseMode children|var, or type code_blocks), read_tags (PlcProgram.Read: names, representation simple|raw; values are the PLC's JSON, unconverted), "
        + "read_diagnostic_buffer (count 1-1000 default 50, language), browse_alarms (count or alarmId, language), browse_modules (hwid, browseMode children|node), read_module_status (hwid, language), read_module_leds (hwid), browse_syslog (count, first), "
        + "read_watch_table_live (table, a watch table, or tagTable, a PLC tag table, of the bound project; optional plcName, folderPath, count 1-1000 default 100, representation: reads the entries offline through Openness, then each mappable symbol through PlcProgram.Read; entries are mapped to Web API names only by documented rules, others are listed with unmappedReason and not read; needs a bound project, binding_conflict otherwise; target and plcName are paired by the caller, not verified; not live-tested). "
        + "Failures: target_not_found, webapi_unreachable, webapi_authentication_failed, webapi_permission_denied, webapi_rejected (with the Web API error code and message in result.webApiError). "
        + "Needs the CPU's web server and Web API enabled in TIA Portal and a user with the matching rights; available in every access mode. A target configured without certificate validation carries a warning on every item.")]
    public static async Task<CallToolResult> PlcOnlineRead(
        PlcWebApiService webApi,
        [Description("Independent Web API read operations, each with a unique operationId, an operation and a target.")] PlcOnlineReadRequest[] operations,
        CancellationToken cancellationToken = default,
        OpennessWorkerClient? workerClient = null)
    {
        var validation = PlcWebApiCatalog.ValidateRead(operations);
        if (!validation.IsValid) return Error(validation.Category, validation.Error);
        var denied = PlcWebApiCatalog.ValidateAccessMode(operations, webApi.AccessMode);
        if (denied.Count != 0) return Error(WorkerFailureCategories.AccessDenied, string.Join("\n", denied));

        var batch = await StructuredOperationBatchExecutionEngine.ExecuteReadsAsync<PlcOnlineReadRequest>(operations,
            item => item.Operation == PlcWebApiTableReader.Operation
                ? PlcWebApiTableReader.ExecuteAsync(webApi, workerClient, item, cancellationToken)
                : PlcWebApiReader.ExecuteAsync(webApi, item, cancellationToken), cancellationToken).ConfigureAwait(false);
        var bounded = BatchReadTool.ApplyBudget(batch, ToolName, PlcWebApiContractVersion.Current, RetryGuidance);
        return StructuredToolResult.Create(new OperationBatchReadResponse(ToolName, PlcWebApiContractVersion.Current,
            bounded.IsFullySuccessful, null, PlcWebApiWarnings.ForTargets(webApi.Configuration, operations.Select(item => item.Target)), bounded), isError: false);
    }

    internal static string RetryGuidance(StructuredOperationItem item)
        => item.Operation switch
        {
            "read_diagnostic_buffer" or "browse_alarms" or "browse_syslog" => "Lower count, or re-run this operationId in its own plc_online_read call.",
            "read_tags" => "Read fewer names per item, or re-run this operationId in its own plc_online_read call.",
            "read_watch_table_live" => "Lower count, or re-run this operationId in its own plc_online_read call.",
            "browse_tags" or "browse_modules" => "Browse a narrower var or hwid, or re-run this operationId in its own plc_online_read call.",
            _ => "Re-run this operationId in its own plc_online_read call; for one value above the limit, raise --max-item-chars and --max-document-chars.",
        };

    internal static CallToolResult Error(string category, string message)
        => BatchReadTool.Error(ToolName, PlcWebApiContractVersion.Current, category, message);
}

/// <summary>Warnings every result for a target without certificate validation carries.</summary>
internal static class PlcWebApiWarnings
{
    public static string NoCertificateValidation(PlcWebApiTarget target)
        => $"PLC Web API target '{target.Name}' ({target.Address}) is configured with certificate validation 'none': the PLC's TLS certificate is not checked, "
           + "so a device on the network path could impersonate the PLC and read the credentials. Pin the certificate (validation 'thumbprint') or trust it in the system store.";

    public static IReadOnlyList<string> ForTarget(PlcWebApiTarget? target)
        => target?.CertificateValidation == PlcWebApiCertificateValidation.None ? new[] { NoCertificateValidation(target) } : Array.Empty<string>();

    public static IReadOnlyList<string> ForTargets(PlcWebApiConfiguration configuration, IEnumerable<string?> names)
        => names.Distinct(StringComparer.Ordinal).Select(configuration.Find).SelectMany(ForTarget).ToArray();

    public static string NotConfigured(PlcWebApiConfiguration configuration, string? name)
        => configuration.SourcePath is null
            ? $"PLC Web API target '{name}' is not configured: no target file was given. Start the server with --plc-webapi-config <file> (or {PlcWebApiConfigOption.EnvVar}); see the PLC online reference."
            : configuration.Targets.Count == 0
                ? $"PLC Web API target '{name}' is not configured: {configuration.SourcePath} lists no targets."
                : $"PLC Web API target '{name}' is not configured. Configured targets: {string.Join(", ", configuration.Targets.Select(target => target.Name))}.";
}

public sealed record PlcWebApiPingResult(string Id);

public sealed record PlcWebApiVersionResult(double ApiVersion);

public sealed record PlcWebApiPermissionsResult(IReadOnlyList<string> Permissions);

public sealed record PlcWebApiOperatingModeResult(string OperatingMode);

public sealed record PlcWebApiModeSelectorResult(string ModeSelector);

public sealed record PlcWebApiSystemTimeResult(string Timestamp);

public sealed record PlcWebApiBrowseResult(int Count, IReadOnlyList<PlcWebApiVariableInfo> Variables);

public sealed record PlcWebApiCodeBlocksResult(int Count, IReadOnlyList<PlcWebApiCodeBlockInfo> Blocks);

public sealed record PlcWebApiTagValue(
    string Name,
    bool Success,
    JsonElement? Value,
    string? FailureCategory,
    string? FailureMessage,
    PlcWebApiErrorInfo? WebApiError);

public sealed record PlcWebApiReadTagsResult(int Requested, int Read, IReadOnlyList<PlcWebApiTagValue> Tags);

public sealed record PlcWebApiModulesResult(IReadOnlyList<PlcWebApiModuleInfo> Nodes);

public sealed record PlcWebApiLedsResult(long Hwid, IReadOnlyList<PlcWebApiLedInfo> Leds);

/// <summary>The evidence a failed item carries in its result: the PLC's error, when it sent one.</summary>
public sealed record PlcWebApiFailureResult(PlcWebApiErrorInfo WebApiError);

/// <summary>Runs one plc_online_read item; never throws for Web API failures (only for caller cancellation).</summary>
internal static class PlcWebApiReader
{
    public static async Task<StructuredOperationItem> ExecuteAsync(PlcWebApiService service, PlcOnlineReadRequest item, CancellationToken cancellationToken)
    {
        var target = service.Configuration.Find(item.Target);
        if (target is null)
            return Failed(item, WorkerFailureCategories.TargetNotFound, PlcWebApiWarnings.NotConfigured(service.Configuration, item.Target), null,
                Array.Empty<string>());
        var warnings = PlcWebApiWarnings.ForTarget(target);
        try
        {
            if (item.Operation == "read_tags") return await ReadTagsAsync(service, target, item, warnings, cancellationToken).ConfigureAwait(false);
            var value = await ReadAsync(service, target, item, cancellationToken).ConfigureAwait(false);
            return new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, value, null, null, null, warnings);
        }
        catch (PlcWebApiException exception)
        {
            return Failed(item, exception.Category, exception.Message, exception.Error, warnings);
        }
    }

    private static async Task<JsonElement> ReadAsync(PlcWebApiService service, PlcWebApiTarget target, PlcOnlineReadRequest item, CancellationToken ct)
    {
        Task<T> Call<T>(Func<IPlcWebApiClient, CancellationToken, Task<T>> call) => service.CallAsync(target, call, ct);
        var language = item.Language ?? PlcWebApiCatalog.DefaultLanguage;
        return item.Operation switch
        {
            "ping" => CanonicalJson.ToElement(new PlcWebApiPingResult(await Call((c, t) => c.PingAsync(t)).ConfigureAwait(false))),
            "version" => CanonicalJson.ToElement(new PlcWebApiVersionResult(await Call((c, t) => c.VersionAsync(t)).ConfigureAwait(false))),
            "permissions" => CanonicalJson.ToElement(new PlcWebApiPermissionsResult(await Call((c, t) => c.GetPermissionsAsync(t)).ConfigureAwait(false))),
            "read_operating_mode" => CanonicalJson.ToElement(new PlcWebApiOperatingModeResult(await Call((c, t) => c.ReadOperatingModeAsync(t)).ConfigureAwait(false))),
            "read_mode_selector" => CanonicalJson.ToElement(new PlcWebApiModeSelectorResult(await Call((c, t) => c.ReadModeSelectorStateAsync(t)).ConfigureAwait(false))),
            "read_cpu_type" => CanonicalJson.ToElement(await Call((c, t) => c.ReadCpuTypeAsync(t)).ConfigureAwait(false)),
            "read_runtime_information" => CanonicalJson.ToElement(await Call((c, t) => c.ReadRuntimeInformationAsync(t)).ConfigureAwait(false)),
            "read_system_time" => CanonicalJson.ToElement(new PlcWebApiSystemTimeResult(
                SiemensTimestamp(await Call((c, t) => c.ReadSystemTimeAsync(t)).ConfigureAwait(false)))),
            "read_project_information" => CanonicalJson.ToElement(await Call((c, t) => c.ReadProjectInformationAsync(t)).ConfigureAwait(false)),
            "browse_tags" when item.Type == "code_blocks" => CodeBlocks(await Call((c, t) => c.BrowseCodeBlocksAsync(t)).ConfigureAwait(false)),
            "browse_tags" => Variables(await Call((c, t) => c.BrowseVariablesAsync(item.BrowseMode ?? "children", item.Var, t)).ConfigureAwait(false)),
            "read_diagnostic_buffer" => CanonicalJson.ToElement(await Call((c, t) =>
                c.BrowseDiagnosticBufferAsync(language, item.Count ?? PlcWebApiCatalog.DefaultEntries, t)).ConfigureAwait(false)),
            "browse_alarms" => CanonicalJson.ToElement(await Call((c, t) => c.BrowseAlarmsAsync(language, item.Count, item.AlarmId, t)).ConfigureAwait(false)),
            "browse_modules" => CanonicalJson.ToElement(new PlcWebApiModulesResult(await Call((c, t) =>
                c.BrowseModulesAsync(item.Hwid is { } hwid ? (uint)hwid : null, item.BrowseMode, t)).ConfigureAwait(false))),
            "read_module_status" => CanonicalJson.ToElement(await Call((c, t) => c.ReadModuleStatusAsync((uint)item.Hwid!.Value, item.Language, t)).ConfigureAwait(false)),
            "read_module_leds" => CanonicalJson.ToElement(new PlcWebApiLedsResult(item.Hwid!.Value,
                await Call((c, t) => c.ReadModuleLedsAsync((uint)item.Hwid!.Value, t)).ConfigureAwait(false))),
            "browse_syslog" => CanonicalJson.ToElement(await Call((c, t) =>
                c.BrowseSyslogAsync(item.Count ?? PlcWebApiCatalog.DefaultEntries, item.First, t)).ConfigureAwait(false)),
            _ => throw new InvalidOperationException($"plc_online_read operation '{item.Operation}' passed validation without an executor."),
        };
    }

    /// <summary>
    /// Reads names in order. A refusal of one name (for example an unknown address) is recorded and the
    /// next name is read; losing the connection or the session stops the item. The item succeeds only
    /// when every name was read.
    /// </summary>
    private static async Task<StructuredOperationItem> ReadTagsAsync(PlcWebApiService service, PlcWebApiTarget target, PlcOnlineReadRequest item,
        IReadOnlyList<string> warnings, CancellationToken ct)
    {
        var names = item.Names!;
        var (results, first) = await ReadNamesAsync(service, target, names, item.Representation, ct).ConfigureAwait(false);
        var read = results.Count(result => result.Success);
        var element = CanonicalJson.ToElement(new PlcWebApiReadTagsResult(names.Count, read, results));
        return first is null
            ? new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Succeeded, element, null, null, null, warnings)
            : new StructuredOperationItem(item.OperationId, item.Operation, OperationBatchStatus.Failed, element,
                new StructuredOperationFailure(first.Category, $"{names.Count - read} of {names.Count} names could not be read"
                    + (results.Count < names.Count ? $" (stopped after {results.Count} because of {first.Category})" : string.Empty)
                    + $"; first failure: {first.Message}"), null, null, warnings);
    }

    /// <summary>
    /// The PlcProgram.Read loop of read_tags, shared with read_watch_table_live: one request per name in
    /// order; a refused name (webapi_rejected) is recorded and the next is read, any other failure stops.
    /// </summary>
    internal static async Task<(List<PlcWebApiTagValue> Results, PlcWebApiException? First)> ReadNamesAsync(PlcWebApiService service,
        PlcWebApiTarget target, IReadOnlyList<string> names, string? representation, CancellationToken ct)
    {
        var results = new List<PlcWebApiTagValue>(names.Count);
        PlcWebApiException? first = null;
        foreach (var name in names)
        {
            try
            {
                var value = await service.CallAsync(target, (c, t) => c.ReadVariableAsync(name, representation, t), ct).ConfigureAwait(false);
                results.Add(new PlcWebApiTagValue(name, true, value, null, null, null));
            }
            catch (PlcWebApiException exception)
            {
                first ??= exception;
                results.Add(new PlcWebApiTagValue(name, false, null, exception.Category, exception.Message, exception.Error));
                if (exception.Category != WorkerFailureCategories.WebApiRejected) break;
            }
        }

        return (results, first);
    }

    internal static StructuredOperationItem Failed(IOperationBatchItem item, string category, string message, PlcWebApiErrorInfo? error, IReadOnlyList<string> warnings)
        => new(item.OperationId, item.Operation, OperationBatchStatus.Failed,
            error is null ? null : CanonicalJson.ToElement(new PlcWebApiFailureResult(error)),
            new StructuredOperationFailure(category, message), null, null, warnings);

    internal static string SiemensTimestamp(DateTime value) => PlcWebApiFormat.Timestamp(value)!;

    private static JsonElement Variables(IReadOnlyList<PlcWebApiVariableInfo> variables)
        => CanonicalJson.ToElement(new PlcWebApiBrowseResult(variables.Count, variables));

    private static JsonElement CodeBlocks(IReadOnlyList<PlcWebApiCodeBlockInfo> blocks)
        => CanonicalJson.ToElement(new PlcWebApiCodeBlocksResult(blocks.Count, blocks));
}
