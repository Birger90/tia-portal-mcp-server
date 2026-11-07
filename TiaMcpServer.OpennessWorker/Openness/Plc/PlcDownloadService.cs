using Siemens.Engineering;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Download;
using Siemens.Engineering.Download.Configurations;
using Siemens.Engineering.HW;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// <c>list_download_targets</c> and <c>plc_download</c>. The listing reads only the offline
/// <c>DownloadProvider.Configuration</c> (modes, PC interfaces, target interfaces) and never calls
/// <c>GetAccessibleDevices()</c> or goes online. The download resolves its exact path on the same
/// provider instance that performs <c>Download</c> (any PC interface: the access mode controls
/// hardware downloads), answers callback configurations through the shared Siemens-free
/// <see cref="PlcDownloadCallbackRecorder"/> (exact canonical type, caller decisions over built-in
/// defaults, <see cref="PlcDownloadDecisionSet"/>) and the reflection-based
/// <see cref="PlcDownloadReflectionAccess"/>, never inside a transaction. While <c>Download</c> runs, an
/// <c>OnlineLegitimation</c> handler on the same provider's <c>Configuration</c> feeds the same recorder
/// (phase <c>online_legitimation</c>): the TLS verification is answered <c>Trusted</c> only from the caller's
/// fingerprints (<see cref="Contracts.PlcOnline.PlcTlsTrustPolicy"/>), everything else fails closed with
/// evidence. Passwords come only from environment variables named by the caller and are never recorded.
/// Enum members are resolved by name at runtime, so no stub enum value is compiled in.
/// </summary>
public static class PlcDownloadService
{
    public static PlcDownloadTargetInventoryInfo ListTargets(ProjectBase project, string? plcName)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, plcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                failures[0] + " No download targets were listed.");
        if (plcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No PLC software named '{plcName}' was found in the project.");

        var inventory = new PlcDownloadTargetInventoryInfo();
        foreach (var plc in plcs)
        {
            var info = new PlcDownloadTargetPlcInfo { PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            var provider = plc.HostDeviceItem?.GetService<DownloadProvider>();
            if (provider is not null)
            {
                info.Downloadable = true;
                foreach (ConfigurationMode mode in provider.Configuration.Modes)
                {
                    var modeInfo = new PlcDownloadModeInfo { Name = mode.Name ?? string.Empty };
                    foreach (ConfigurationPcInterface pcInterface in mode.PcInterfaces)
                    {
                        var pcInfo = new PlcDownloadPcInterfaceInfo { Name = pcInterface.Name ?? string.Empty, Number = pcInterface.Number };
                        foreach (ConfigurationTargetInterface target in pcInterface.TargetInterfaces)
                            pcInfo.TargetInterfaces.Add(new PlcDownloadTargetInterfaceInfo { Name = target.Name ?? string.Empty });
                        modeInfo.PcInterfaces.Add(pcInfo);
                    }

                    info.Modes.Add(modeInfo);
                }
            }

            inventory.Plcs.Add(info);
        }

        return inventory;
    }

    /// <param name="confirmations">
    /// The TIA Portal confirmations the session declined while this download ran. The caller opens
    /// the declining scope and attaches it; every exit with evidence closes the scope first and then
    /// copies the complete record into the evidence (<see cref="PlcDownloadPortalConfirmationLog.CloseInto"/>).
    /// </param>
    public static PlcDownloadResultInfo Download(ProjectBase project, PlcDownloadRequestInfo request, PlcDownloadPortalConfirmationLog? confirmations = null)
    {
        var options = ValidateRequest(request);
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        if (!string.Equals(plc.Software.Name, request.PlcName, StringComparison.Ordinal)
            || !string.Equals(plc.DeviceName, request.DeviceName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged,
                $"PLC '{request.PlcName}' on device '{request.DeviceName}' no longer resolves exactly. Nothing was downloaded.");

        var host = plc.HostDeviceItem ?? throw new WorkerOperationException(
            WorkerFailureCategories.WorkerOperationFailed, $"The device item of PLC '{request.PlcName}' could not be resolved.");
        // One provider instance for path resolution and Download: target interfaces are bound to the
        // instance they were enumerated from (Siemens online-and-download guidance).
        // Issue #16: an R/H system downloads to its primary or backup CPU through RHDownloadProvider (V21: DownloadToPrimary /
        // DownloadToBackup(IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)), found
        // by exact full name among the GetServiceInfos() of the CPU device item, then of its parents up to the Device (audit T3:
        // the system Device may host it; not live-tested), and bound at runtime; its own Configuration resolves the path.
        object? rhProvider = null;
        DownloadProvider? provider = null;
        ConnectionConfiguration connection;
        if (request.RhTarget is { } rhTarget)
        {
            if (rhTarget is not ("primary" or "backup"))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "rhTarget must be primary or backup.");
            rhProvider = RhSystemProviders.Find(host, RhSystemProviders.DownloadProvider, request.PlcName, out _, out var notFound)
                ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, notFound + " Nothing was downloaded.");
            connection = rhProvider.GetType().GetProperty("Configuration")?.GetValue(rhProvider) as ConnectionConfiguration
                ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, "RHDownloadProvider.Configuration could not be read. Nothing was downloaded.");
        }
        else
        {
            provider = host.GetService<DownloadProvider>() ?? throw new WorkerOperationException(
                WorkerFailureCategories.TargetKindUnsupported, $"PLC '{request.PlcName}' offers no DownloadProvider; it is not downloadable that way. For an R/H system CPU retry with rhTarget primary or backup (RHDownloadProvider).");
            // The same ConnectionConfiguration instance resolves the path and carries the legitimation handler.
            connection = provider.Configuration;
        }
        var target = ResolveExact(connection, request);
        var downloadOptions = (DownloadOptions)Enum.Parse(typeof(DownloadOptions), options, ignoreCase: false);

        var result = new PlcDownloadResultInfo
        {
            PlcName = request.PlcName,
            DeviceName = request.DeviceName,
            Path = new PlcDownloadPathInfo
            {
                Mode = request.Mode,
                PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = request.PcInterfaceName, Number = request.PcInterfaceNumber },
                TargetInterface = request.TargetInterface,
            },
            Scope = request.Scope,
            DownloadOptions = options,
            StartAfterDownload = request.StartAfterDownload,
            PortalConfirmationPolicy = request.PortalConfirmations,
            CompletionVerified = false,
            CompletionNote = PlcDownloadPolicy.CompletionNote,
        };
        var recorder = new PlcDownloadCallbackRecorder(PlcDownloadDecisionSet.For(request), result,
            new PlcDownloadReflectionAccess(configuration => configuration is DownloadConfiguration download ? ReadMessage(download) : null,
                Environment.GetEnvironmentVariable),
            isFatal: exception => exception is NonRecoverableException or EngineeringObjectDisposedException);

        // Registered before Download and removed in finally (online-status.md): TIA Portal delivers the TLS
        // verification and online passwords of the download's connection here; unanswered, it fails the
        // connection without evidence.
        OnlineConfigurationDelegate legitimation = presented => recorder.OnlineLegitimation(presented);
        connection.OnlineLegitimation += legitimation;
        DownloadResult downloaded;
        try
        {
            DownloadConfigurationDelegate pre = configuration => recorder.PreDownload(configuration);
            DownloadConfigurationDelegate post = configuration => recorder.PostDownload(configuration);
            downloaded = rhProvider is null
                ? provider!.Download(target, pre, post, downloadOptions)
                : Contracts.Shared.RuntimeServiceValues.Invoke(rhProvider, request.RhTarget == "primary" ? "DownloadToPrimary" : "DownloadToBackup",
                        new[] { typeof(Siemens.Engineering.Connection.IConfiguration), typeof(DownloadConfigurationDelegate), typeof(DownloadConfigurationDelegate), typeof(DownloadOptions) },
                        new object[] { target, pre, post, downloadOptions },
                        $"RHDownloadProvider.{(request.RhTarget == "primary" ? "DownloadToPrimary" : "DownloadToBackup")}(IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions)") as DownloadResult
                    ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, "The R/H download returned no DownloadResult; inspect the R/H system.");
        }
        catch (NonRecoverableException ex)
        {
            throw Failure(WorkerFailureCategories.PortalLost,
                $"The TIA Portal connection was lost during the download: {ex.Message}. Restart TIA Portal and explicitly bind_project again. "
                + "The PLC or simulator may already have been changed or stopped; inspect it before retrying.", null, result, confirmations);
        }
        catch (EngineeringObjectDisposedException ex)
        {
            // Before the abort catch: a disposed object (rethrown unchanged by the recorder after a
            // rejected selection, or raised by Download) keeps the shared classification, which turns
            // an observed Portal disposal into portal_lost. The recorded evidence is kept, and after
            // our own abort the message still explains the abort.
            var category = WorkerExecution.Classify(ex, mutates: true);
            var message = result.Abort is { } abort
                ? PlcDownloadPolicy.AbortMessage(abort) + " " + WorkerExecution.FailureMessage(ex, category)
                : WorkerExecution.FailureMessage(ex, category);
            throw Failure(category, message, WorkerExecution.Describe(ex), result, confirmations);
        }
        catch (Exception ex) when (result.Abort is not null)
        {
            // Our own fail-closed abort, surfaced by Download wrapped or unwrapped. A rejected
            // answer reports the setter's engineering error when it was one.
            var engineering = recorder.DecisionFailure as EngineeringException ?? ex as EngineeringException;
            throw Failure(WorkerFailureCategories.WorkerOperationFailed, PlcDownloadPolicy.AbortMessage(result.Abort),
                engineering is null ? null : WorkerExecution.Describe(engineering), result, confirmations);
        }
        catch (EngineeringException ex)
        {
            var category = WorkerExecution.Classify(ex, mutates: true);
            throw Failure(category, WorkerExecution.FailureMessage(ex, category), WorkerExecution.Describe(ex), result, confirmations);
        }
        finally
        {
            connection.OnlineLegitimation -= legitimation;
        }

        // A refused TLS question returns normally from the callback (K27: NonTrusted, no throw), so Download may return
        // although the connection was refused: the recorded abort decides, whatever Download returned.
        if (result.Abort is { } refused)
            throw Failure(WorkerFailureCategories.WorkerOperationFailed, PlcDownloadPolicy.AbortMessage(refused),
                recorder.DecisionFailure is EngineeringException rejected ? WorkerExecution.Describe(rejected) : null, result, confirmations);

        try
        {
            Project(downloaded, result);
        }
        catch (EngineeringException ex) when (ex is not EngineeringObjectDisposedException)
        {
            throw Failure(WorkerFailureCategories.WorkerOperationFailed,
                "The download returned, but its result could not be read: " + ex.Message
                + " The PLC or simulator may have been changed; inspect it before retrying.", WorkerExecution.Describe(ex), result, confirmations);
        }

        (confirmations ?? new PlcDownloadPortalConfirmationLog()).CloseInto(result);
        return result;
    }

    /// <summary>Defense in depth behind host validation: the shared Siemens-free <see cref="PlcDownloadPolicy.ValidateWorkerRequest"/>.</summary>
    private static string ValidateRequest(PlcDownloadRequestInfo? request)
    {
        var validation = PlcDownloadPolicy.ValidateWorkerRequest(request);
        if (!validation.IsValid)
            throw new WorkerOperationException(validation.FailureCategory!, validation.Message!);
        return validation.DownloadOptions!;
    }

    /// <summary>Exactly one ordinal match per level, enumerated on <paramref name="configuration"/>'s own provider.</summary>
    private static ConfigurationTargetInterface ResolveExact(ConnectionConfiguration configuration, PlcDownloadRequestInfo request)
    {
        var mode = Single(configuration.Modes.Cast<ConfigurationMode>().Where(m => string.Equals(m.Name, request.Mode, StringComparison.Ordinal)),
            $"connection mode '{request.Mode}'");
        var pcInterfaces = mode.PcInterfaces.Cast<ConfigurationPcInterface>().ToList();
        if (PlcDownloadPolicy.IsImplicitInterfaceNumberAmbiguous(pcInterfaces.Select(pc => pc.Name), request.PcInterfaceName, request.PcInterfaceNumberExplicit))
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"Connection mode '{request.Mode}' offers several PC interfaces named '{request.PcInterfaceName}' "
                + $"({string.Join(", ", pcInterfaces.Where(pc => string.Equals(pc.Name, request.PcInterfaceName, StringComparison.Ordinal)).Select(pc => "#" + pc.Number))}) "
                + "and no pcInterface number was supplied. Nothing was downloaded; specify pcInterface { name, number }.");
        var pcInterface = Single(pcInterfaces.Where(pc =>
                string.Equals(pc.Name, request.PcInterfaceName, StringComparison.Ordinal) && pc.Number == request.PcInterfaceNumber),
            $"PC interface '{request.PcInterfaceName}' #{request.PcInterfaceNumber}");
        return Single(pcInterface.TargetInterfaces.Cast<ConfigurationTargetInterface>().Where(t =>
                string.Equals(t.Name, request.TargetInterface, StringComparison.Ordinal)),
            $"target interface '{request.TargetInterface}'");
    }

    private static T Single<T>(IEnumerable<T> matches, string what)
    {
        var found = matches.Take(2).ToList();
        if (found.Count == 1) return found[0];
        throw new WorkerOperationException(found.Count == 0 ? WorkerFailureCategories.StateChanged : WorkerFailureCategories.TargetAmbiguous,
            $"The download {what} {(found.Count == 0 ? "no longer resolves exactly" : "resolves more than once")}. Nothing was downloaded; list_download_targets again.");
    }

    private static void Project(DownloadResult downloaded, PlcDownloadResultInfo result)
    {
        result.State = downloaded.State.ToString();
        result.ErrorCount = downloaded.ErrorCount;
        result.WarningCount = downloaded.WarningCount;
        var budget = new MessageBudget();
        try
        {
            ProjectMessages(downloaded.Messages, result.Messages, depth: 1, budget);
        }
        catch (EngineeringException ex) when (ex is not EngineeringObjectDisposedException)
        {
            budget.Note("Some download messages could not be read: " + Bound(ex.Message));
        }

        result.MessagesComplete = budget.Omission is null;
        result.MessageOmission = budget.Omission;
    }

    private static void ProjectMessages(IEnumerable<DownloadResultMessage> messages, List<PlcDownloadMessageInfo> into, int depth, MessageBudget budget)
    {
        foreach (DownloadResultMessage message in messages)
        {
            if (budget.Count >= PlcDownloadPolicy.MaxMessages)
            {
                budget.Note($"Download messages beyond {PlcDownloadPolicy.MaxMessages} were omitted.");
                return;
            }

            budget.Count++;
            var info = new PlcDownloadMessageInfo { Message = Bound(message.Message), State = message.State.ToString() };
            into.Add(info);
            if (depth >= PlcDownloadPolicy.MaxMessageDepth)
            {
                if (message.Messages.Cast<DownloadResultMessage>().Any())
                    budget.Note($"Download messages nested deeper than {PlcDownloadPolicy.MaxMessageDepth} levels were omitted.");
                continue;
            }

            ProjectMessages(message.Messages, info.Messages, depth + 1, budget);
        }
    }

    private static WorkerOperationException Failure(string category, string message, EngineeringErrorInfo? engineeringError,
        PlcDownloadResultInfo evidence, PlcDownloadPortalConfirmationLog? confirmations)
    {
        (confirmations ?? new PlcDownloadPortalConfirmationLog()).CloseInto(evidence);
        return new(category, message, engineeringError: engineeringError, plcDownloadOutcome: WorkerJson.SerializePayload(evidence));
    }

    internal static string? Bound(string? text) => PlcDownloadPolicy.BoundText(text);

    private sealed class MessageBudget
    {
        public int Count { get; set; }

        public string? Omission { get; private set; }

        public void Note(string omission) => Omission ??= omission;
    }

    private static string? ReadMessage(DownloadConfiguration configuration)
    {
        try
        {
            return configuration.Message;
        }
        catch (EngineeringException ex) when (ex is not EngineeringObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>The dialog text of a TIA Portal confirmation (see <c>TiaPortalSession.ReadConfirmationText</c>).</summary>
    internal static string? ReadConfirmationText(object eventArgs)
        => global::TiaMcpServer.OpennessWorker.Openness.Project.TiaPortalSession.ReadConfirmationText(eventArgs);
}
