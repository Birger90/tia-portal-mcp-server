using Siemens.Engineering;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// Audit N3 (not live-tested): plc_read get_station_fingerprint_data. The <c>FingerprintDataProvider</c> is found by exact full name
/// among the TIA Portal's <c>GetServiceInfos()</c>, then the bound project's (the XML names no owner; nothing else is tried); its
/// <c>Configuration</c> resolves mode and PC interface exactly (one match per level, like scan_accessible_devices), the caller's node
/// address is created on that PC interface (<see cref="PlcTargetAddresses.OnPcInterface"/>), and
/// <c>GetFingerprintData(ConfigurationAddress, OnlineConfigurationDelegate)</c> is bound and called. Online legitimation is answered by
/// the shared <see cref="PlcCallbackRecorder"/> under the compare_to_online rules (passwords and the password map, onlineUser,
/// trustedCertificates; anything else aborts). It runs in the fixed-No confirmation scope of the online compares: a declined dialog
/// fails the item. The read reaches the device and changes neither device nor project.
/// </summary>
internal static class PlcFingerprintDataService
{
    private const string Nothing = "Nothing was read.";

    public static PlcStationFingerprintDataInfo Read(ProjectBase project, TiaPortal? portal, PlcOnlineRequestInfo request, PlcDownloadPortalConfirmationLog? confirmations)
    {
        if (string.IsNullOrEmpty(request.Mode) || string.IsNullOrEmpty(request.PcInterfaceName) || request.PcInterfaceNumber < 0)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"get_station_fingerprint_data needs mode and pcInterface {{ name, number }}; there is no default interface. {Nothing}");
        if (PlcOnlinePolicy.ValidateAddress(request.TargetAddress) is { } address)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"targetAddress: {address} {Nothing}");
        if (request.MaxElements is < 1 or > PlcFingerprintData.MaxItems)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"maxElements must be between 1 and {PlcFingerprintData.MaxItems}. {Nothing}");
        if (PlcTlsTrustPolicy.ValidateNormalized(request.TrustedCertificates) is { } trustError)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, trustError);

        var (provider, owner) = FindProvider(portal, project);
        var info = new PlcStationFingerprintDataInfo
        {
            ProviderOwner = owner,
            Mode = request.Mode!,
            PcInterfaceName = request.PcInterfaceName!,
            PcInterfaceNumber = request.PcInterfaceNumber,
            TargetAddress = request.TargetAddress!,
            MaxItems = request.MaxElements,
        };
        var configuration = Bound(() => PlcFingerprintData.Configuration(provider)) as ConnectionConfiguration
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"FingerprintDataProvider.Configuration is not the documented Siemens.Engineering.Connection.ConnectionConfiguration. {Nothing}");
        var mode = Single(configuration.Modes.Cast<ConfigurationMode>().Where(m => string.Equals(m.Name, request.Mode, StringComparison.Ordinal)),
            $"connection mode '{request.Mode}'");
        var pcInterface = Single(mode.PcInterfaces.Cast<ConfigurationPcInterface>().Where(pc =>
                string.Equals(pc.Name, request.PcInterfaceName, StringComparison.Ordinal) && pc.Number == request.PcInterfaceNumber),
            $"PC interface '{request.PcInterfaceName}' #{request.PcInterfaceNumber} of mode '{request.Mode}'");
        var method = Bound(() => PlcFingerprintData.Bind(provider, typeof(ConfigurationAddress), typeof(OnlineConfigurationDelegate)));
        var configurationAddress = PlcTargetAddresses.OnPcInterface(pcInterface, request.TargetAddress!, Nothing);

        var recorder = new PlcCallbackRecorder(null, request.Passwords,
            new PlcCallbackReflectionAccess(PlcCallbackPolicy.OnlineConfigurationsNamespace, _ => null, Environment.GetEnvironmentVariable,
                onlineUser: request.OnlineUser), PlcOnlineService.IsFatal, request.TrustedCertificates);
        OnlineConfigurationDelegate legitimation = presented => recorder.Handle(presented);
        object? returned;
        try
        {
            returned = RuntimeServiceValues.Call(method, provider, new object[] { configurationAddress, legitimation });
        }
        catch (Exception exception) when (recorder.Abort is { } abort && !PlcOnlineService.IsFatal(exception))
        {
            throw Refused(abort, recorder, confirmations);
        }

        // A refused TLS question returns normally (K27: NonTrusted, no throw); the recorded abort decides.
        if (recorder.Abort is { } refused) throw Refused(refused, recorder, confirmations);
        Bound(() =>
        {
            PlcFingerprintData.Project(returned, request.MaxElements, info, OpennessExceptions.IsRecoverable);
            return info;
        });
        info.ConfigurationsSeen = recorder.Seen;
        if (Declined(confirmations) is { } declined)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, declined);
        return info;
    }

    private static (object Provider, string Owner) FindProvider(TiaPortal? portal, ProjectBase project)
    {
        foreach (var (target, owner) in new (object? Target, string Owner)[] { (portal, "portal"), (project, "project") })
        {
            if (target is null) continue;
            var provider = TechnologyObjectRuntime.TryService(target, TechnologyObjectRuntime.ServiceTypes(target), PlcFingerprintData.ProviderTypeName, out var unavailable);
            if (unavailable is not null)
                throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, $"{unavailable} {Nothing}");
            if (provider is not null) return (provider, owner);
        }

        throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            $"Neither TIA Portal nor the bound project lists {PlcFingerprintData.ProviderTypeName} among its GetServiceInfos() (V21 does not document "
            + $"which object offers it). {Nothing}");
    }

    private static WorkerOperationException Refused(PlcCallbackAbortInfo abort, PlcCallbackRecorder recorder, PlcDownloadPortalConfirmationLog? confirmations)
    {
        var declined = Declined(confirmations);
        return new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            PlcCallbackRecorder.AbortMessage(abort) + " No fingerprint data was read." + (declined is null ? string.Empty : " " + declined),
            engineeringError: recorder.AnswerFailure is EngineeringException engineering ? WorkerExecution.Describe(engineering) : null);
    }

    /// <summary>Closes the fixed-No scope; the text of the declined dialogs, or null when none was raised.</summary>
    private static string? Declined(PlcDownloadPortalConfirmationLog? confirmations)
    {
        if (confirmations is null) return null;
        var observed = 0;
        List<PlcDownloadPortalConfirmationInfo> entries = new();
        confirmations.CloseInto((recorded, count, _) =>
        {
            entries = recorded;
            observed = count;
        });
        return observed == 0
            ? null
            : $"TIA Portal raised {observed} confirmation dialog(s) during get_station_fingerprint_data and each was answered No ("
                + string.Join("; ", entries.Take(3).Select(entry => entry.Text is null ? entry.Type : $"{entry.Type}: {entry.Text}"))
                + (observed > 3 ? "; ..." : string.Empty) + "), so the result was discarded.";
    }

    private static T Bound<T>(Func<T> bind)
    {
        try
        {
            return bind();
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }

    private static T Single<T>(IEnumerable<T> matches, string what)
    {
        var found = matches.Take(2).ToList();
        if (found.Count == 1) return found[0];
        throw new WorkerOperationException(found.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
            $"The {what} {(found.Count == 0 ? "is not offered by the FingerprintDataProvider's configuration" : "is offered more than once")}. {Nothing}");
    }
}
