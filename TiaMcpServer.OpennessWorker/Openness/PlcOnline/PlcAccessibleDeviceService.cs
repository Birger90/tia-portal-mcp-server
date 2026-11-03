using Siemens.Engineering;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// scan_accessible_devices: <c>ConfigurationPcInterface.GetAccessibleDevices()</c> on exactly one PC interface
/// the caller names (mode, PC interface name and number; never a default), found on the PLC's own
/// <c>OnlineProvider.Configuration</c> by ordinal comparison, one match per level. The call performs live
/// network discovery through that interface and changes neither a device nor the project; nothing is applied to
/// the connection configuration (the server calls no ApplyConfiguration or GoOnline; whether TIA Portal opens a
/// connection internally for the discovery is not documented). Like compare_to_online it runs inside a fixed-No
/// TIA Portal confirmation scope: every dialog raised during the call is answered No and recorded, and a declined
/// dialog fails the item (the result is projected while the scope is open, then the scope is closed and checked; a dialog
/// declined while the call failed is reported with that failure). The member and the returned device type's
/// members are bound at runtime (<see cref="PlcAccessibleDeviceProjection"/>), because the V21 reference does
/// not document their types; anything unexpected fails closed with <c>unsupported_capability</c>.
/// </summary>
internal static class PlcAccessibleDeviceService
{
    public static PlcAccessibleDeviceScanInfo Scan(ProjectBase project, PlcSourceFolderRequestInfo request, PlcDownloadPortalConfirmationLog? confirmations)
    {
        if (string.IsNullOrEmpty(request.Mode) || string.IsNullOrEmpty(request.PcInterfaceName) || request.PcInterfaceNumber is null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                "scan_accessible_devices needs mode and pcInterface { name, number }; there is no default interface. Nothing was scanned.");
        var max = request.MaxDevices ?? PlcAccessibleDeviceProjection.DefaultMaxDevices;
        if (max < 1 || max > PlcAccessibleDeviceProjection.MaxDevices)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"maxElements must be between 1 and {PlcAccessibleDeviceProjection.MaxDevices}.");

        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var provider = plc.HostDeviceItem?.GetService<OnlineProvider>() ?? throw new WorkerOperationException(
            WorkerFailureCategories.TargetKindUnsupported, $"PLC '{plc.Software.Name}' offers no OnlineProvider, so it has no PC interfaces to scan through.");
        var configuration = provider.Configuration;
        var mode = Single(configuration.Modes.Cast<ConfigurationMode>().Where(m => string.Equals(m.Name, request.Mode, StringComparison.Ordinal)),
            $"connection mode '{request.Mode}'");
        var pcInterface = Single(mode.PcInterfaces.Cast<ConfigurationPcInterface>().Where(pc =>
                string.Equals(pc.Name, request.PcInterfaceName, StringComparison.Ordinal) && pc.Number == request.PcInterfaceNumber),
            $"PC interface '{request.PcInterfaceName}' #{request.PcInterfaceNumber} of mode '{request.Mode}'");

        object? returned;
        try
        {
            returned = PlcAccessibleDeviceProjection.Invoke(pcInterface);
        }
        catch (InvalidOperationException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, exception.Message + " Nothing was scanned."
                + (PlcAccessibleDeviceProjection.DeclinedConfirmations(confirmations) is { } declined ? " " + declined : string.Empty));
        }
        catch (Exception exception)
        {
            // A dialog declined while GetAccessibleDevices() failed is part of the failure (a lost Portal keeps its own category).
            if (PlcOnlineConfirmationScope.ScanFailure(exception, confirmations, "GetAccessibleDevices()") is { } failure) throw failure;
            throw;
        }

        // Project while the scope is still open (reading the device members may raise dialogs too), then close it.
        PlcAccessibleDeviceProjection.Outcome outcome;
        try
        {
            outcome = PlcAccessibleDeviceProjection.Project(returned, max);
        }
        catch (Exception exception)
        {
            if (PlcOnlineConfirmationScope.ScanFailure(exception, confirmations, "Reading the scanned devices") is { } failure) throw failure;
            throw;
        }

        if (PlcAccessibleDeviceProjection.DeclinedConfirmations(confirmations) is { } declinedDialogs)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, declinedDialogs);
        if (outcome.Error is not null)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                outcome.Error + " The scan ran, but its result cannot be reported without guessing; no device is listed.");
        var result = new PlcAccessibleDeviceScanInfo
        {
            PlcName = plc.Software.Name ?? string.Empty,
            DeviceName = plc.DeviceName ?? string.Empty,
            Mode = mode.Name ?? string.Empty,
            PcInterfaceName = pcInterface.Name ?? string.Empty,
            PcInterfaceNumber = pcInterface.Number,
            DeviceCount = outcome.Count,
            Truncated = outcome.Truncated,
            MaxDevices = max,
        };
        result.Devices.AddRange(outcome.Devices);
        return result;
    }

    private static T Single<T>(IEnumerable<T> matches, string what)
    {
        var found = matches.Take(2).ToList();
        return found.Count switch
        {
            1 => found[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"The {what} was not found on the PLC's online configuration. Nothing was scanned; list the paths with plc_read get_online_state."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"The {what} resolves more than once. Nothing was scanned."),
        };
    }
}
