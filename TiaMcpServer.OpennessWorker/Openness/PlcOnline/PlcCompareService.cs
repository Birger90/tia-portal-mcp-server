using System.Diagnostics;
using Siemens.Engineering;
using Siemens.Engineering.Compare;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// <c>compare_to_online</c> (<c>PlcSoftware.CompareToOnline()</c>) and <c>compare_software</c>
/// (<c>PlcSoftware.CompareTo(ISoftwareCompareTarget)</c> against another PLC, the project library or a
/// root-folder master copy), projected through the Siemens-free <see cref="PlcOnlinePolicy.ProjectTree{T}"/>.
/// Neither changes the project. The online compare applies no connection path: it uses whatever TIA
/// Portal does for CompareToOnline; when the PLC was not online before and is online (or connecting)
/// afterwards, on success or failure, the connection the compare opened is closed again with GoOffline.
/// </summary>
public static class PlcCompareService
{
    /// <summary>
    /// <c>compare_to_online</c> is allowed in read-only, so it must never leave TIA Portal online or answer a
    /// legitimation prompt on its own: <see cref="PlcCompareOnlineRun"/> restores <c>Offline</c> on the success and
    /// the failure path, and a fail-closed <c>OnlineLegitimation</c> handler (a recorder without decisions or
    /// passwords) aborts every authentication or password prompt raised through the PLC's connection configuration
    /// while the compare runs; a TLS verification is answered <c>Trusted</c> only when every presented certificate's
    /// SHA-256 fingerprint is in the caller's <c>trustedCertificates</c> (whether TIA Portal keeps that trust after the call is not
    /// documented; not live-tested).
    /// Any refused prompt discards the compare (the result may not reflect the device); its evidence rides on the
    /// failure (<c>PlcDownloadOutcome</c>: a <see cref="PlcCompareResultInfo"/> without a tree).
    /// </summary>
    public static PlcCompareResultInfo CompareToOnline(ProjectBase project, PlcOnlineRequestInfo request, Action<int> sleep)
        => RunOnline(project, Validate(request, online: true), PlcOnlineMethods.CompareToOnline, sleep, null, (plc, _) => plc.Software.CompareToOnline());

    /// <summary>
    /// The shared online compare run of compare_to_online and compare_hardware_to_online: the TLS list check, the PLC and its
    /// <c>OnlineProvider</c>, the optional <paramref name="precheck"/> (before anything reaches the device), the fail-closed
    /// legitimation handler, the Offline restore, and the projection of <paramref name="compare"/>'s result.
    /// </summary>
    internal static PlcCompareResultInfo RunOnline(ProjectBase project, PlcOnlineRequestInfo checkedRequest, string operation, Action<int> sleep,
        Action<PlcSoftwareLocator.DiscoveredPlcSoftware, OnlineProvider?>? precheck,
        Func<PlcSoftwareLocator.DiscoveredPlcSoftware, OnlineProvider?, CompareResult> compare)
    {
        if (PlcTlsTrustPolicy.ValidateNormalized(checkedRequest.TrustedCertificates) is { } trustError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, trustError);
        var plc = PlcSoftwareLocator.FindUnique(project, checkedRequest.PlcName);
        var provider = plc.HostDeviceItem?.GetService<OnlineProvider>();
        precheck?.Invoke(plc, provider);
        var result = NewResult(operation, plc, "online", null, checkedRequest);
        var clock = Stopwatch.StartNew();
        var run = new PlcCompareOnlineRun(provider is null ? null : () => provider.State.ToString(), provider is null ? null : provider.GoOffline,
            sleep, () => clock.ElapsedMilliseconds, PlcOnlineService.IsFatal);
        // Online passwords (e.g. OnlineReadAccessPassword of a protected CPU) are answered from the caller's password sources
        // and the server password map; nothing else is answered, and a TLS check only from trustedCertificates.
        var refusing = new PlcCallbackRecorder(null, checkedRequest.Passwords,
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null, Environment.GetEnvironmentVariable), PlcOnlineService.IsFatal,
            checkedRequest.TrustedCertificates);
        var configuration = provider?.Configuration;
        OnlineConfigurationDelegate refuse = presented => refusing.Handle(presented);
        if (configuration is not null) configuration.OnlineLegitimation += refuse;
        CompareResult compared;
        try
        {
            compared = run.Run(() => compare(plc, provider));
        }
        catch (Exception ex) when (ex is not WorkerOperationException && !PlcOnlineService.IsFatal(ex))
        {
            var refused = refusing.Abort is { } abort ? " " + PlcCallbackRecorder.AbortMessage(abort) : string.Empty;
            var restore = run.RestoreSummary() is { } summary ? " " + summary : string.Empty;
            var evidence = LegitimationEvidence(result, run, refusing);
            if (ex is EngineeringException siemens)
            {
                var category = WorkerExecution.Classify(siemens, mutates: false);
                throw new WorkerOperationException(category, WorkerExecution.FailureMessage(siemens, category) + refused + restore,
                    engineeringError: WorkerExecution.Describe(siemens), plcDownloadOutcome: evidence);
            }

            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"{ex.GetType().Name}: {ex.Message}{refused}{restore}",
                plcDownloadOutcome: evidence);
        }
        finally
        {
            if (configuration is not null) configuration.OnlineLegitimation -= refuse;
        }

        if (refusing.Abort is { } refusal)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"{operation} refused an online legitimation prompt (it answers password prompts only from passwords or the server password map, "
                + "and trusts a TLS certificate only from trustedCertificates), so the compare result was discarded. " + PlcCallbackRecorder.AbortMessage(refusal) + " "
                + "Retry with passwords (a password source for the configuration type named above) or trustedCertificates the user checked."
                + (run.RestoreSummary() is { } restore ? " " + restore : string.Empty),
                plcDownloadOutcome: LegitimationEvidence(result, run, refusing));
        run.ApplyTo(result);
        result.ConfigurationsSeen = refusing.Seen;
        result.Abort = null;
        if (run.RestoreAttempted && !run.Disconnected) Console.Error.WriteLine($"{operation}: " + run.RestoreSummary());
        return Project(compared, result, checkedRequest);
    }

    /// <summary>
    /// The failure evidence of a compare that met an online legitimation: the online states and the presented
    /// configurations with the refusal, without a compare tree. Null when nothing was presented (no evidence).
    /// </summary>
    private static string? LegitimationEvidence(PlcCompareResultInfo result, PlcCompareOnlineRun run, PlcCallbackRecorder recorder)
    {
        var seen = recorder.Seen;
        if (seen.Count == 0) return null;
        run.ApplyTo(result);
        result.ConfigurationsSeen = seen;
        result.Abort = recorder.Abort;
        return TiaMcpServer.Contracts.Json.WorkerJson.SerializePayload(result);
    }

    public static PlcCompareResultInfo CompareSoftware(ProjectBase project, PlcOnlineRequestInfo request)
    {
        var checkedRequest = Validate(request, online: false);
        var plc = PlcSoftwareLocator.FindUnique(project, checkedRequest.PlcName);
        ISoftwareCompareTarget target;
        string? targetName = null;
        switch (checkedRequest.CompareTarget)
        {
            case PlcOnlinePolicy.CompareTargets.Plc:
                var other = PlcSoftwareLocator.FindUnique(project, checkedRequest.TargetName);
                if (other.Software.Equals(plc.Software))
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                        $"targetName '{checkedRequest.TargetName}' resolves to the compared PLC '{plc.Software.Name}' itself; name another PLC.");
                target = other.Software;
                targetName = other.Software.Name;
                break;
            case PlcOnlinePolicy.CompareTargets.ProjectLibrary:
                target = Library(project);
                break;
            case PlcOnlinePolicy.CompareTargets.SecondaryPlc:
            {
                // Issue #20: a PLC of an open secondary (read-only reference) project of the same TIA Portal.
                var portal = ((IEngineeringInstance)project).Parent as TiaPortal ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                    "The bound project's TIA Portal could not be read, so no secondary project can be found.");
                var secondary = Libraries.SecondaryProjects.Find(portal, checkedRequest.TargetProject, "targetProject");
                var reference = PlcSoftwareLocator.FindUnique(secondary, checkedRequest.TargetName);
                target = reference.Software;
                targetName = reference.Software.Name;
                break;
            }
            case PlcOnlinePolicy.CompareTargets.MasterCopy:
                target = MasterCopy(Library(project), checkedRequest.TargetName!);
                targetName = checkedRequest.TargetName;
                break;
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unknown compareTarget '{checkedRequest.CompareTarget}'.");
        }

        var result = NewResult(PlcOnlineMethods.CompareSoftware, plc, checkedRequest.CompareTarget!, targetName, checkedRequest);
        return Project(plc.Software.CompareTo(target), result, checkedRequest);
    }

    internal static PlcOnlineRequestInfo Validate(PlcOnlineRequestInfo? request, bool online)
    {
        if (request is null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "The compare request is missing.");
        if (PlcOnlinePolicy.ValidateCompare(request.CompareTarget, request.TargetName, request.MaxDepth, request.MaxElements, online) is { } error)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        return request;
    }

    internal static ProjectLibrary Library(ProjectBase project)
        => (project as Siemens.Engineering.Project)?.ProjectLibrary ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            "The bound project offers no project library.");

    /// <summary>
    /// The documented lookup of the compare reference (<c>MasterCopyFolder.MasterCopies.Find(name)</c>) in the
    /// project library's root master copy folder. The master copy's own name is not read back: no V21 member
    /// for it is used, so the result reports the requested name.
    /// </summary>
    private static MasterCopy MasterCopy(ProjectLibrary library, string name)
        => library.MasterCopyFolder.MasterCopies.Find(name) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
            $"No master copy named '{name}' is in the root master copy folder of the project library.");

    internal static PlcCompareResultInfo NewResult(string operation, PlcSoftwareLocator.DiscoveredPlcSoftware plc, string target, string? targetName, PlcOnlineRequestInfo request) => new()
    {
        Operation = operation,
        PlcName = plc.Software.Name ?? string.Empty,
        DeviceName = plc.DeviceName ?? string.Empty,
        CompareTarget = target,
        TargetName = targetName,
        MaxDepth = request.MaxDepth,
        MaxElements = request.MaxElements,
        DifferencesOnly = request.DifferencesOnly,
    };

    internal static PlcCompareResultInfo Project(CompareResult compared, PlcCompareResultInfo result, PlcOnlineRequestInfo request)
        => PlcOnlinePolicy.ProjectTree<CompareResultElement>(compared?.RootElement,
            element => element.LeftName, element => element.RightName, element => element.ComparisonResult.ToString(),
            element => element.DetailedInformation, element => element.Elements,
            request.MaxDepth, request.MaxElements, request.DifferencesOnly).ApplyTo(result);
}
