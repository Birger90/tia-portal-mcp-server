using System.Diagnostics;
using Siemens.Engineering;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Online;
using Siemens.Engineering.Upload;
using Siemens.Engineering.Upload.Configurations;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// <c>get_online_state</c>, <c>plc_go_online</c>, <c>plc_go_offline</c>,
/// <c>list_station_upload_interfaces</c> and <c>plc_station_upload</c>. Listings read only a provider's
/// offline <c>Configuration</c> (never <c>GetAccessibleDevices()</c>). Going online resolves the exact
/// path on the same <c>OnlineProvider</c> instance that applies it and goes online, then polls
/// <c>State</c> (GoOnline returns before the connection is established). Station upload resolves the
/// exact mode and PC interface on the provider that uploads. Callback configurations are answered only by
/// the caller's entries through the Siemens-free <see cref="PlcCallbackRecorder"/>; the TLS verification of
/// an online legitimation only by the caller's trusted SHA-256 fingerprints (<see cref="PlcTlsTrustPolicy"/>).
/// Going online and station upload register the legitimation handler on the same <c>ConnectionConfiguration</c>
/// instance they use and remove it in <c>finally</c>. Enum values are handled by name, never compiled in.
/// Nothing runs inside a transaction.
/// </summary>
public static class PlcOnlineService
{
    public static PlcOnlineStateInventoryInfo ListOnlineState(ProjectBase project, string? plcName)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, plcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " No online state was listed.");
        if (plcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");

        var inventory = new PlcOnlineStateInventoryInfo();
        foreach (var plc in plcs)
        {
            var info = new PlcOnlineStatePlcInfo { PlcName = plc.Software.Name ?? string.Empty, DeviceName = plc.DeviceName ?? string.Empty };
            var provider = plc.HostDeviceItem?.GetService<OnlineProvider>();
            if (provider is not null)
            {
                info.OnlineProviderAvailable = true;
                info.State = provider.State.ToString();
                info.IsConfigured = provider.Configuration.IsConfigured;
                info.Modes = Modes(provider.Configuration, withTargets: true);
            }

            inventory.Plcs.Add(info);
        }

        return inventory;
    }

    public static PlcStationUploadInterfacesInfo ListStationUploadInterfaces(ProjectBase project)
    {
        var provider = project.GetService<StationUploadProvider>();
        return provider is null
            ? new PlcStationUploadInterfacesInfo { Available = false }
            : new PlcStationUploadInterfacesInfo { Available = true, Modes = Modes(provider.Configuration, withTargets: false) };
    }

    public static PlcOnlineActionResultInfo GoOnline(ProjectBase project, PlcOnlineRequestInfo request, Action<int> sleep,
        PlcDownloadPortalConfirmationLog? confirmations = null, DeferredUntilNextRequest? deferred = null)
    {
        var checkedRequest = Require(request);
        if (ValidatePortalConfirmations(checkedRequest) is { } policyError) throw Invalid(policyError);
        if (PlcOnlinePolicy.ValidatePath(checkedRequest.Mode, checkedRequest.PcInterfaceName, checkedRequest.PcInterfaceNumber,
                checkedRequest.TargetInterface, requireTarget: true) is { } pathError)
            throw Invalid(pathError);
        if (PlcOnlinePolicy.ValidateTimeout(checkedRequest.TimeoutSeconds) is { } timeoutError) throw Invalid(timeoutError);
        if (checkedRequest.Decisions.Count > 0) throw Invalid("plc_tia_online answers only password configurations; decisions are not accepted.");
        if (PlcCallbackPolicy.ValidateEntries(null, checkedRequest.Passwords) is { } entryError) throw Invalid(entryError);
        if (PlcTlsTrustPolicy.ValidateNormalized(checkedRequest.TrustedCertificates) is { } trustError) throw Invalid(trustError);

        var (plc, provider) = Resolve(project, checkedRequest);
        var result = NewResult("go_online", plc, checkedRequest);
        result.StateBefore = provider.State.ToString();
        if (result.StateBefore == PlcOnlinePolicy.States.Online)
        {
            // Nothing is applied, so no path is reported: the existing connection is not verified against the request.
            result.Outcome = PlcOnlinePolicy.ActionOutcomes.AlreadyOnline;
            result.PathVerified = false;
            result.StateAfter = result.StateBefore;
            result.GoalReached = true;
            result.StatesObserved.Add(result.StateBefore);
            result.Note = "The PLC was already online. ApplyConfiguration (which throws while a connection is active) and GoOnline were not called, "
                + "so the existing connection's path was neither changed nor verified against the requested one (path is null, pathVerified false).";
            return Closed(result, confirmations);
        }

        result.Path = new PlcConnectionPathInfo
        {
            Mode = checkedRequest.Mode!,
            PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = checkedRequest.PcInterfaceName!, Number = checkedRequest.PcInterfaceNumber },
            TargetInterface = checkedRequest.TargetInterface,
        };

        if (result.StateBefore is PlcOnlinePolicy.States.Connecting or PlcOnlinePolicy.States.Disconnecting)
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged,
                $"PLC '{result.PlcName}' is in the transitional online state '{result.StateBefore}'. Nothing was applied; read get_online_state and retry.");

        // One provider instance for path resolution, ApplyConfiguration and GoOnline: target interfaces are
        // bound to the instance they were enumerated from (Siemens online-and-download guidance).
        var configuration = provider.Configuration;
        var target = ResolveTarget(configuration, checkedRequest);
        var recorder = new PlcCallbackRecorder(null, checkedRequest.Passwords,
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null, Environment.GetEnvironmentVariable),
            IsFatal, checkedRequest.TrustedCertificates, plcSimInterface: PlcTlsTrustPolicy.IsPlcSimInterface(checkedRequest.PcInterfaceName));
        OnlineConfigurationDelegate legitimation = presented => recorder.Handle(presented);
        configuration.OnlineLegitimation += legitimation;
        try
        {
            result.ConfigurationApplied = configuration.ApplyConfiguration(target);
            if (result.ConfigurationApplied != true)
            {
                result.Outcome = PlcOnlinePolicy.ActionOutcomes.ConfigurationNotApplied;
                result.StateAfter = provider.State.ToString();
                result.Note = "ApplyConfiguration returned false, so GoOnline was not called.";
                return Closed(result, confirmations);
            }

            result.GoOnlineReturned = provider.GoOnline().ToString();
            var clock = Stopwatch.StartNew();
            var wait = PlcOnlinePolicy.Wait(() => provider.State.ToString(), PlcOnlinePolicy.States.Online,
                checkedRequest.TimeoutSeconds * 1000, () => clock.ElapsedMilliseconds, sleep);
            Apply(wait, result);
            result.Outcome = wait.GoalReached ? PlcOnlinePolicy.ActionOutcomes.Connected : PlcOnlinePolicy.ActionOutcomes.NotReached;
            result.PathVerified = wait.GoalReached;
        }
        catch (Exception ex) when (ex is not WorkerOperationException)
        {
            result.ConfigurationsSeen = recorder.Seen;
            result.Abort = recorder.Abort;
            throw Failure(ex, recorder.Abort, recorder.AnswerFailure, Closed(result, confirmations),
                "The online connection may be partly established; read get_online_state and go offline with plc_tia_online if needed.");
        }
        finally
        {
            configuration.OnlineLegitimation -= legitimation;
            LingerRefusingLegitimation(configuration, deferred);
        }

        result.ConfigurationsSeen = recorder.Seen;
        result.Abort = recorder.Abort;
        // A refused TLS question returns normally from the callback (K27: NonTrusted, no throw); the recorded abort decides.
        if (result.Abort is { } refused)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcCallbackRecorder.AbortMessage(refused)
                + " Read get_online_state; the connection was refused.",
                engineeringError: recorder.AnswerFailure is EngineeringException rejected ? WorkerExecution.Describe(rejected) : null,
                plcDownloadOutcome: WorkerJson.SerializePayload(Closed(result, confirmations)));
        return Closed(result, confirmations);
    }

    public static PlcOnlineActionResultInfo GoOffline(ProjectBase project, PlcOnlineRequestInfo request, Action<int> sleep,
        PlcDownloadPortalConfirmationLog? confirmations = null)
    {
        var checkedRequest = Require(request);
        if (ValidatePortalConfirmations(checkedRequest) is { } policyError) throw Invalid(policyError);
        if (PlcOnlinePolicy.ValidateTimeout(checkedRequest.TimeoutSeconds) is { } timeoutError) throw Invalid(timeoutError);
        var (plc, provider) = Resolve(project, checkedRequest);
        var result = NewResult("go_offline", plc, checkedRequest);
        result.StateBefore = provider.State.ToString();
        if (result.StateBefore == PlcOnlinePolicy.States.Offline)
        {
            result.StateAfter = result.StateBefore;
            result.GoalReached = true;
            result.StatesObserved.Add(result.StateBefore);
            result.Outcome = PlcOnlinePolicy.ActionOutcomes.AlreadyOffline;
            result.Note = "The PLC was already offline; GoOffline was not called.";
            return Closed(result, confirmations);
        }

        try
        {
            provider.GoOffline();
            var clock = Stopwatch.StartNew();
            var wait = PlcOnlinePolicy.Wait(() => provider.State.ToString(), PlcOnlinePolicy.States.Offline,
                checkedRequest.TimeoutSeconds * 1000, () => clock.ElapsedMilliseconds, sleep);
            Apply(wait, result);
            result.Outcome = wait.GoalReached ? PlcOnlinePolicy.ActionOutcomes.Disconnected : PlcOnlinePolicy.ActionOutcomes.NotReached;
        }
        catch (Exception ex) when (ex is not WorkerOperationException)
        {
            throw Failure(ex, null, null, Closed(result, confirmations), "Read get_online_state before retrying.");
        }

        return Closed(result, confirmations);
    }

    public static PlcStationUploadResultInfo StationUpload(ProjectBase project, PlcOnlineRequestInfo request,
        PlcDownloadPortalConfirmationLog? confirmations = null)
    {
        var checkedRequest = Require(request);
        if (ValidatePortalConfirmations(checkedRequest) is { } policyError) throw Invalid(policyError);
        if (PlcOnlinePolicy.ValidatePath(checkedRequest.Mode, checkedRequest.PcInterfaceName, checkedRequest.PcInterfaceNumber,
                null, requireTarget: false) is { } pathError)
            throw Invalid(pathError);
        if (PlcOnlinePolicy.ValidateAddress(checkedRequest.Address) is { } addressError) throw Invalid(addressError);
        if (PlcCallbackPolicy.ValidateEntries(checkedRequest.Decisions, checkedRequest.Passwords) is { } entryError) throw Invalid(entryError);
        if (PlcTlsTrustPolicy.ValidateNormalized(checkedRequest.TrustedCertificates) is { } trustError) throw Invalid(trustError);

        var provider = project.GetService<StationUploadProvider>() ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetKindUnsupported, "The bound project offers no StationUploadProvider; station upload is unavailable.");
        // The same provider instance resolves the path, creates the address and uploads; the same
        // ConnectionConfiguration instance carries the legitimation handler.
        var connection = provider.Configuration;
        var pcInterface = ResolvePcInterface(connection, checkedRequest);
        var result = new PlcStationUploadResultInfo
        {
            Address = checkedRequest.Address!,
            PortalConfirmationPolicy = checkedRequest.PortalConfirmations,
            Path = new PlcConnectionPathInfo
            {
                Mode = checkedRequest.Mode!,
                PcInterface = new PlcConnectionPcInterfaceRefInfo { Name = checkedRequest.PcInterfaceName!, Number = checkedRequest.PcInterfaceNumber },
                TargetInterface = null,
            },
        };
        var address = pcInterface.Addresses.Create(checkedRequest.Address) ?? throw new WorkerOperationException(
            WorkerFailureCategories.WorkerOperationFailed, $"TIA Portal created no address for '{checkedRequest.Address}'. Nothing was uploaded.");
        // One recorder for the upload callbacks and the connection's online legitimation, so both share one
        // configurationsSeen list and one abort (the canonical type tells their namespaces apart).
        var recorder = new PlcCallbackRecorder(checkedRequest.Decisions, checkedRequest.Passwords,
            new PlcCallbackReflectionAccess(new[] { PlcCallbackPolicy.UploadConfigurationsNamespace, PlcCallbackPolicy.OnlineConfigurationsNamespace },
                configuration => configuration is UploadConfiguration upload ? upload.Message : null, Environment.GetEnvironmentVariable),
            IsFatal, checkedRequest.TrustedCertificates, plcSimInterface: PlcTlsTrustPolicy.IsPlcSimInterface(checkedRequest.PcInterfaceName));
        OnlineConfigurationDelegate legitimation = presented => recorder.Handle(presented);
        connection.OnlineLegitimation += legitimation;
        UploadResult uploaded;
        try
        {
            uploaded = provider.StationUpload(address, configuration => recorder.Handle(configuration));
        }
        catch (Exception ex) when (ex is not WorkerOperationException)
        {
            Evidence(recorder, result);
            throw Failure(ex, recorder.Abort, recorder.AnswerFailure, Closed(result, confirmations),
                "A partly uploaded station may have been added to the project; inspect the project tree before retrying.");
        }
        finally
        {
            connection.OnlineLegitimation -= legitimation;
        }

        Evidence(recorder, result);
        // A refused TLS question returns normally from the callback (K27: NonTrusted, no throw); the recorded abort decides.
        if (result.Abort is { } refused)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcCallbackRecorder.AbortMessage(refused)
                + " A partly uploaded station may have been added to the project; inspect the project tree before retrying.",
                engineeringError: recorder.AnswerFailure is EngineeringException rejected ? WorkerExecution.Describe(rejected) : null,
                plcDownloadOutcome: WorkerJson.SerializePayload(Closed(result, confirmations)));
        try
        {
            result.State = uploaded?.State.ToString();
            result.ErrorCount = uploaded?.ErrorCount;
            result.UploadedStationName = uploaded?.UploadedStation?.Name;
            var (messages, complete, omission) = PlcEngineeringMessageProjector.Project(uploaded?.Messages?.Cast<object>(),
                message => message is IEngineeringObject engineering ? engineering.GetAttributeInfos().Select(info => info.Name) : Enumerable.Empty<string>(),
                ReadAttribute, item => item is IEngineeringObject, IsFatal);
            result.Messages = messages;
            result.MessagesComplete = complete;
            result.MessageOmission = omission;
        }
        catch (EngineeringException ex) when (!IsFatal(ex))
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "The station upload returned, but its result could not be read: " + ex.Message
                + " A station may have been added to the project; inspect the project tree before retrying.",
                engineeringError: WorkerExecution.Describe(ex), plcDownloadOutcome: WorkerJson.SerializePayload(Closed(result, confirmations)));
        }

        return Closed(result, confirmations);
    }

    /// <summary>
    /// TIA Portal may still be connecting after go_online returned (timeout) and raise a legitimation prompt
    /// then. Until the next request starts, a fail-closed handler (a recorder without entries and without trusted
    /// fingerprints: no password, TLS or authentication answer, so nothing is trusted outside the call's evidence)
    /// refuses every such prompt and logs it; the next response lists them.
    /// </summary>
    private static void LingerRefusingLegitimation(ConnectionConfiguration configuration, DeferredUntilNextRequest? deferred)
    {
        if (deferred is null) return;
        var late = new PlcCallbackRecorder(null, null,
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null, _ => null), IsFatal);
        OnlineConfigurationDelegate refuse = presented =>
        {
            Console.Error.WriteLine($"Late online legitimation after plc_go_online returned: {presented?.GetType().FullName} refused (fail closed).");
            late.Handle(presented!);
        };
        configuration.OnlineLegitimation += refuse;
        deferred.Defer(() =>
        {
            configuration.OnlineLegitimation -= refuse;
            var seen = late.Seen;
            return seen.Count == 0
                ? Array.Empty<string>()
                : new[]
                {
                    $"{seen.Count} online legitimation prompt(s) raised after plc_go_online returned were refused "
                    + $"({string.Join(", ", seen.Take(5).Select(entry => entry.Type))}); TIA Portal may not have completed the connection: "
                    + "read get_online_state, then go online again with passwords or with trustedCertificates the user checked.",
                };
        });
    }

    private static string? ValidatePortalConfirmations(PlcOnlineRequestInfo request)
        => PlcDownloadPolicy.PortalConfirmationPolicies.IsKnown(request.PortalConfirmations)
            ? null
            : $"portalConfirmations must be one of: {string.Join(", ", PlcDownloadPolicy.PortalConfirmationPolicies.All)}.";

    /// <summary>
    /// Ends the action's confirmation scope (when the dispatch opened one) and copies the confirmations
    /// TIA Portal raised meanwhile into the evidence, before the evidence is returned or serialized.
    /// </summary>
    private static T Closed<T>(T result, PlcDownloadPortalConfirmationLog? confirmations) where T : class
    {
        (confirmations ?? new PlcDownloadPortalConfirmationLog()).CloseInto((entries, observed, complete) =>
        {
            switch (result)
            {
                case PlcOnlineActionResultInfo action:
                    action.PortalConfirmations = entries;
                    action.PortalConfirmationsObserved = observed;
                    action.PortalConfirmationsComplete = complete;
                    break;
                case PlcStationUploadResultInfo upload:
                    upload.PortalConfirmations = entries;
                    upload.PortalConfirmationsObserved = observed;
                    upload.PortalConfirmationsComplete = complete;
                    break;
                default:
                    throw new InvalidOperationException($"No portal-confirmation evidence for {typeof(T).Name}.");
            }
        });
        return result;
    }

    private static PlcOnlineRequestInfo Require(PlcOnlineRequestInfo? request)
        => request ?? throw Invalid("The online request is missing.");

    private static (PlcSoftwareLocator.DiscoveredPlcSoftware Plc, OnlineProvider Provider) Resolve(ProjectBase project, PlcOnlineRequestInfo request)
    {
        if (string.IsNullOrWhiteSpace(request.PlcName) || string.IsNullOrWhiteSpace(request.DeviceName))
            throw Invalid("The resolved plcName and deviceName are required.");
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        if (!string.Equals(plc.Software.Name, request.PlcName, StringComparison.Ordinal)
            || !string.Equals(plc.DeviceName, request.DeviceName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged,
                $"PLC '{request.PlcName}' on device '{request.DeviceName}' no longer resolves exactly. Nothing was changed.");
        var provider = plc.HostDeviceItem?.GetService<OnlineProvider>() ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetKindUnsupported, $"PLC '{request.PlcName}' offers no OnlineProvider.");
        return (plc, provider);
    }

    private static PlcOnlineActionResultInfo NewResult(string action, PlcSoftwareLocator.DiscoveredPlcSoftware plc, PlcOnlineRequestInfo request) => new()
    {
        Action = action,
        PlcName = plc.Software.Name ?? string.Empty,
        DeviceName = plc.DeviceName ?? string.Empty,
        TimeoutSeconds = request.TimeoutSeconds,
        PortalConfirmationPolicy = request.PortalConfirmations,
    };

    private static void Apply(PlcOnlineWait wait, PlcOnlineActionResultInfo result)
    {
        result.StateAfter = wait.FinalState;
        result.GoalReached = wait.GoalReached;
        result.TimedOut = wait.TimedOut;
        result.ElapsedMilliseconds = wait.ElapsedMilliseconds;
        result.StatesObserved = wait.Observed.ToList();
    }

    private static void Evidence(PlcCallbackRecorder recorder, PlcStationUploadResultInfo result)
    {
        result.ConfigurationsSeen = recorder.Seen;
        result.Abort = recorder.Abort;
        result.UnusedDecisions = recorder.UnusedKeys().ToList();
    }

    private static List<PlcConnectionModeInfo> Modes(ConnectionConfiguration configuration, bool withTargets)
    {
        var modes = new List<PlcConnectionModeInfo>();
        foreach (ConfigurationMode mode in configuration.Modes)
        {
            var modeInfo = new PlcConnectionModeInfo { Name = mode.Name ?? string.Empty };
            foreach (ConfigurationPcInterface pcInterface in mode.PcInterfaces)
            {
                var pcInfo = new PlcConnectionPcInterfaceInfo { Name = pcInterface.Name ?? string.Empty, Number = pcInterface.Number };
                if (withTargets)
                    foreach (ConfigurationTargetInterface target in pcInterface.TargetInterfaces)
                        pcInfo.TargetInterfaces.Add(new PlcConnectionTargetInterfaceInfo { Name = target.Name ?? string.Empty });
                modeInfo.PcInterfaces.Add(pcInfo);
            }

            modes.Add(modeInfo);
        }

        return modes;
    }

    /// <summary>Exactly one ordinal match per level, enumerated on <paramref name="configuration"/>'s own provider; never a fallback.</summary>
    private static ConfigurationTargetInterface ResolveTarget(ConnectionConfiguration configuration, PlcOnlineRequestInfo request)
        => Single(ResolvePcInterface(configuration, request).TargetInterfaces.Cast<ConfigurationTargetInterface>()
            .Where(t => string.Equals(t.Name, request.TargetInterface, StringComparison.Ordinal)), $"target interface '{request.TargetInterface}'");

    private static ConfigurationPcInterface ResolvePcInterface(ConnectionConfiguration configuration, PlcOnlineRequestInfo request)
    {
        var mode = Single(configuration.Modes.Cast<ConfigurationMode>().Where(m => string.Equals(m.Name, request.Mode, StringComparison.Ordinal)),
            $"connection mode '{request.Mode}'");
        return Single(mode.PcInterfaces.Cast<ConfigurationPcInterface>().Where(pc =>
                string.Equals(pc.Name, request.PcInterfaceName, StringComparison.Ordinal) && pc.Number == request.PcInterfaceNumber),
            $"PC interface '{request.PcInterfaceName}' #{request.PcInterfaceNumber}");
    }

    private static T Single<T>(IEnumerable<T> matches, string what)
    {
        var found = matches.Take(2).ToList();
        if (found.Count == 1) return found[0];
        throw new WorkerOperationException(found.Count == 0 ? WorkerFailureCategories.StateChanged : WorkerFailureCategories.TargetAmbiguous,
            $"The {what} {(found.Count == 0 ? "no longer resolves exactly" : "resolves more than once")}. Nothing was changed; list the paths again.");
    }

    private static (bool Ok, object? Value) ReadAttribute(object item, string name)
    {
        try
        {
            return item is IEngineeringObject engineering ? (true, engineering.GetAttribute(name)) : (false, null);
        }
        catch (EngineeringException ex) when (!IsFatal(ex))
        {
            return (false, null);
        }
    }

    internal static bool IsFatal(Exception exception) => exception is NonRecoverableException or EngineeringObjectDisposedException;

    /// <summary>
    /// The failure of an attempted online action with its evidence: our own abort is
    /// <c>worker_operation_failed</c>; Siemens failures follow <see cref="WorkerExecution"/> with the
    /// mutation flag set (go online/offline is OnlineControl, station upload ProjectMutation);
    /// <see cref="NonRecoverableException"/> is <c>portal_lost</c>.
    /// </summary>
    private static WorkerOperationException Failure(Exception exception, PlcCallbackAbortInfo? abort, Exception? answerFailure, object evidence, string advice)
    {
        var outcome = WorkerJson.SerializePayload(evidence);
        if (exception is NonRecoverableException)
            return new WorkerOperationException(WorkerFailureCategories.PortalLost,
                $"The TIA Portal connection was lost: {exception.Message}. Restart TIA Portal and explicitly bind_project again. {advice}",
                plcDownloadOutcome: outcome);
        if (exception is EngineeringObjectDisposedException disposed)
        {
            var category = WorkerExecution.Classify(disposed, mutates: true);
            return new WorkerOperationException(category, WorkerExecution.FailureMessage(disposed, category) + " " + advice,
                engineeringError: WorkerExecution.Describe(disposed), plcDownloadOutcome: outcome);
        }

        if (abort is not null)
        {
            var engineering = answerFailure as EngineeringException ?? exception as EngineeringException;
            return new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcCallbackRecorder.AbortMessage(abort) + " " + advice,
                engineeringError: engineering is null ? null : WorkerExecution.Describe(engineering), plcDownloadOutcome: outcome);
        }

        if (exception is EngineeringException siemens)
        {
            var category = WorkerExecution.Classify(siemens, mutates: true);
            return new WorkerOperationException(category, WorkerExecution.FailureMessage(siemens, category) + " " + advice,
                engineeringError: WorkerExecution.Describe(siemens), plcDownloadOutcome: outcome);
        }

        return new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            $"{exception.GetType().Name}: {exception.Message} {advice}", plcDownloadOutcome: outcome);
    }

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}
