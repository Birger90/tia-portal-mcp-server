using TiaMcpServer.Contracts.Plc.DeviceData;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// plc_write download_to_folder and the online master secret (issue #16) against an in-memory model. Directory <c>C:\Download</c>
/// exists; a folder download "writes" one file there (PLC_2 has no RHDownloadProvider, so rhSystem fails there). PLC_1 is online;
/// PLC_2 is offline, so its master secret operations fail state_changed. <c>C:\DownloadFull</c> exists and is not empty (refused
/// without overwrite:true); <c>C:\DownloadDialog</c> exists, is empty and raises one TIA Portal confirmation, answered by the item's
/// portalConfirmations policy like the worker's scope (decline fails the item). This models object access only; it is no evidence of Siemens behavior.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private const string DownloadDirectory = @"C:\Download";
    private const string FullDownloadDirectory = @"C:\DownloadFull";
    private const string DialogDownloadDirectory = @"C:\DownloadDialog";

    private string DeviceData(WorkerRequest request)
    {
        var info = request.PlcDeviceData ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "missing request");
        info.Operation = request.Method;
        var target = _plcs.Where(p => info.PlcName is null || Matches(p, info.PlcName)).ToList();
        if (target.Count != 1)
            throw new WorkerOperationException(target.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous, $"PLC '{info.PlcName}' matches {target.Count} PLCs.");
        var plc = target[0];
        var result = new PlcDeviceDataResultInfo { Operation = info.Operation, PlcName = plc.Software, DeviceName = plc.Device };
        if (info.Operation == PlcDeviceDataMethods.DownloadToFolder)
        {
            if (!new[] { DownloadDirectory, FullDownloadDirectory, DialogDownloadDirectory }.Contains(info.DirectoryPath, StringComparer.OrdinalIgnoreCase))
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"directoryPath '{info.DirectoryPath}' does not exist.");
            if (string.Equals(info.DirectoryPath, FullDownloadDirectory, StringComparison.OrdinalIgnoreCase) && !info.Overwrite)
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, PlcDeviceDataPolicy.NotEmpty(info.DirectoryPath!));
            result.Overwrite = info.Overwrite;
            if (string.Equals(info.DirectoryPath, DialogDownloadDirectory, StringComparison.OrdinalIgnoreCase))
            {
                var accept = info.PortalConfirmations == Contracts.Plc.PlcDownloadPolicy.PortalConfirmationPolicies.Accept;
                result.PortalConfirmations.Add(new Contracts.Plc.PlcDownloadPortalConfirmationInfo
                {
                    Type = "Siemens.Engineering.ConfirmationEventArgs", Text = "Fake: compile before download?",
                    Answer = accept ? Contracts.Plc.PlcDownloadPolicy.PortalConfirmationAnswers.Yes : Contracts.Plc.PlcDownloadPolicy.PortalConfirmationAnswers.No,
                });
                result.PortalConfirmationsObserved = 1;
                if (PlcDeviceDataPolicy.Declined(info.Operation, result.PortalConfirmations, 1) is { } declined)
                    throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, declined);
                Console.Error.WriteLine(PlcDeviceDataPolicy.Accepted(info.Operation, result.PortalConfirmations));
            }
            if (info.RhSystem && plc.Software != "PLC_1")
                throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, $"The device item does not list '{PlcDeviceDataNames.RhDownloadProvider}'. Nothing was written.");
            result.Call = info.RhSystem ? "RHDownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)" : "DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)";
            result.DirectoryPath = info.DirectoryPath;
            result.ConfigurationsSeen.Add("OverwriteSystemData: selection");
            result.Files.Add(new PlcDeviceDataFileInfo { Path = info.DirectoryPath + @"\" + plc.Software + ".dat", Bytes = 3, Sha256 = new string('a', 64) });
            result.Evidence = "verified";
            return Ok(result);
        }

        result.OnlineState = plc.Software == "PLC_1" ? "Online" : "Offline";
        if (result.OnlineState != "Online")
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged, $"PLC '{plc.Software}' is Offline, not Online: connect first with plc_tia_online go_online. Nothing was changed.");
        result.Call = info.Operation == PlcDeviceDataMethods.SetMasterSecretOnline ? "OnlineProvider.SetPlcMasterSecret(SecureString)" : "OnlineProvider.ResetPlcMasterSecret()";
        result.PasswordSource = info.Operation == PlcDeviceDataMethods.SetMasterSecretOnline ? info.PasswordVariable : null;
        result.Evidence = "notReadBack";
        result.Note = "No documented member reads the PLC's master secret back.";
        return Ok(result);
    }
}
