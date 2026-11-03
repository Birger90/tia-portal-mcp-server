using System.Security.Cryptography;
using Siemens.Engineering;
using Siemens.Engineering.Download;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.DeviceData;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.DeviceData;

/// <summary>
/// plc_write download_to_folder and the online master secret (issue #16, not live-tested). The folder download calls
/// <c>DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)</c> (or, with rhSystem, the R/H system's
/// <c>RHDownloadProvider</c>, found among the CPU device item's <c>GetServiceInfos()</c> by exact full name) and answers the
/// download configurations with plc_download's recorder and built-in answers (no caller decisions; an unanswerable one aborts).
/// The master secret calls <c>OnlineProvider.SetPlcMasterSecret(SecureString)</c> / <c>ResetPlcMasterSecret()</c> only while
/// <c>OnlineProvider.State</c> reads <c>Online</c> (connect first with plc_tia_online); the secret comes only from a password source.
/// Members not on the stubs are bound at runtime (api_member_missing before any call).
/// </summary>
internal static class PlcDeviceDataService
{
    public static PlcDeviceDataResultInfo Write(ProjectBase project, PlcDeviceDataRequestInfo request, PlcDownloadPortalConfirmationLog? confirmations = null)
    {
        try
        {
            var result = WriteCore(project, request);
            Close(result, confirmations, request.Operation);
            return result;
        }
        catch (WorkerOperationException) when (confirmations is not null && Declined(confirmations, request.Operation) is { } declined)
        {
            // A dialog answered No explains the failure better than the failure itself.
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, declined);
        }
    }

    /// <summary>
    /// Ends the item's confirmation scope and copies what it answered into the result (security review F6): a declined dialog
    /// fails the item, an accepted one is a warning (stderr becomes the response's warnings).
    /// </summary>
    private static void Close(PlcDeviceDataResultInfo result, PlcDownloadPortalConfirmationLog? confirmations, string operation)
    {
        (confirmations ?? new PlcDownloadPortalConfirmationLog()).CloseInto((entries, observed, _) =>
        {
            result.PortalConfirmations = entries;
            result.PortalConfirmationsObserved = observed;
        });
        if (PlcDeviceDataPolicy.Declined(operation, result.PortalConfirmations, result.PortalConfirmationsObserved) is { } declined)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, declined);
        if (PlcDeviceDataPolicy.Accepted(operation, result.PortalConfirmations) is { } accepted) Console.Error.WriteLine(accepted);
    }

    private static string? Declined(PlcDownloadPortalConfirmationLog confirmations, string operation)
    {
        string? message = null;
        confirmations.CloseInto((entries, observed, _) => message = PlcDeviceDataPolicy.Declined(operation, entries, observed));
        return message;
    }

    private static PlcDeviceDataResultInfo WriteCore(ProjectBase project, PlcDeviceDataRequestInfo request)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var host = plc.HostDeviceItem ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            $"The device item of PLC '{plc.Software.Name}' could not be resolved.");
        var result = new PlcDeviceDataResultInfo { Operation = request.Operation, PlcName = plc.Software.Name, DeviceName = plc.DeviceName };
        switch (request.Operation)
        {
            case PlcDeviceDataMethods.DownloadToFolder:
                return ToFolder(host, request, result);
            case PlcDeviceDataMethods.SetMasterSecretOnline:
            case PlcDeviceDataMethods.ResetMasterSecretOnline:
            {
                var online = host.GetService<OnlineProvider>() ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                    $"PLC '{plc.Software.Name}' offers no OnlineProvider.");
                result.OnlineState = online.State.ToString();
                if (result.OnlineState != "Online")
                    throw new WorkerOperationException(WorkerFailureCategories.StateChanged,
                        $"PLC '{plc.Software.Name}' is {result.OnlineState}, not Online: connect first with plc_tia_online go_online. Nothing was changed.");
                if (request.Operation == PlcDeviceDataMethods.SetMasterSecretOnline)
                {
                    using var secret = Password(request.PasswordVariable);
                    result.PasswordSource = request.PasswordVariable;
                    result.Call = "OnlineProvider.SetPlcMasterSecret(SecureString)";
                    RuntimeServiceValues.Invoke(online, "SetPlcMasterSecret", new[] { typeof(System.Security.SecureString) }, new object[] { secret }, result.Call);
                }
                else
                {
                    result.Call = "OnlineProvider.ResetPlcMasterSecret()";
                    RuntimeServiceValues.Invoke(online, "ResetPlcMasterSecret", Type.EmptyTypes, Array.Empty<object>(), result.Call);
                }

                result.Evidence = "notReadBack";
                result.Note = "No documented member reads the PLC's master secret back; inspect the PLC (plc_read get_plc_security shows the offline configuration only).";
                return result;
            }
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported operation '{request.Operation}'.");
        }
    }

    private static PlcDeviceDataResultInfo ToFolder(Siemens.Engineering.HW.DeviceItem host, PlcDeviceDataRequestInfo request, PlcDeviceDataResultInfo result)
    {
        var directory = request.DirectoryPath ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "directoryPath is required.");
        if (!Directory.Exists(directory)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"directoryPath '{directory}' does not exist.");
        // Security review F3: a non-empty directory only with overwrite:true; the link and junction check of the other directory exports.
        var (refusal, warning) = PlcDeviceDataPolicy.TargetError(directory, request.Overwrite);
        if (refusal is not null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, refusal);
        if (warning is not null) Console.Error.WriteLine(warning);
        result.Overwrite = request.Overwrite;
        object provider;
        if (request.RhSystem)
        {
            var offered = TechnologyObjectRuntime.ServiceTypes(host);
            var (service, unavailable) = RuntimeServiceValues.Find(offered, type => TechnologyObjectRuntime.TryService(host, offered, type.FullName!, out _), PlcDeviceDataNames.RhDownloadProvider);
            provider = service ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                (unavailable ?? RuntimeServiceValues.NotListed($"The device item of PLC '{result.PlcName}'", PlcDeviceDataNames.RhDownloadProvider)) + " Nothing was written.");
            result.Call = "RHDownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)";
        }
        else
        {
            provider = host.GetService<DownloadProvider>() ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"PLC '{result.PlcName}' offers no DownloadProvider; it is not downloadable.");
            result.Call = "DownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate)";
        }

        var evidence = new PlcDownloadResultInfo { PlcName = result.PlcName, DeviceName = result.DeviceName ?? string.Empty };
        var recorder = new PlcDownloadCallbackRecorder(PlcDownloadDecisionSet.For(new PlcDownloadRequestInfo { PcInterfaceName = "folder", StartAfterDownload = false }), evidence,
            new PlcDownloadReflectionAccess(_ => null, Environment.GetEnvironmentVariable),
            isFatal: exception => exception is NonRecoverableException or EngineeringObjectDisposedException);
        DownloadConfigurationDelegate answer = configuration => recorder.PreDownload(configuration);
        var started = DateTime.UtcNow.AddSeconds(-2);
        try
        {
            RuntimeServiceValues.Invoke(provider, "Download", new[] { typeof(DirectoryInfo), typeof(DownloadConfigurationDelegate) },
                new object[] { new DirectoryInfo(directory), answer }, result.Call);
        }
        catch (Exception) when (evidence.Abort is not null)
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcDownloadPolicy.AbortMessage(evidence.Abort)
                + " download_to_folder answers only the built-in download configurations; download in TIA Portal for this one.");
        }
        finally
        {
            result.ConfigurationsSeen = evidence.ConfigurationsSeen.Select(c => $"{c.DecisionKey ?? c.Type}: {c.Kind}").ToList();
        }

        if (evidence.Abort is not null)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcDownloadPolicy.AbortMessage(evidence.Abort));
        result.DirectoryPath = directory;
        result.Files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(file => File.GetLastWriteTimeUtc(file) >= started)
            .Take(PlcDeviceDataNames.MaxFiles).Select(Hash).ToList();
        if (result.Files.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{result.Call} returned, but no file written by this call was found in '{directory}'.");
        result.Evidence = "verified";
        return result;
    }

    private static System.Security.SecureString Password(string? source)
    {
        if (!PasswordSources.IsSource(source))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "passwordVariable must name an environment variable or a credential target. Nothing was changed.");
        return PasswordSources.Resolve(source, Environment.GetEnvironmentVariable, null, out var reason)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{reason} Nothing was changed.");
    }

    private static PlcDeviceDataFileInfo Hash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return new PlcDeviceDataFileInfo { Path = path, Bytes = stream.Length, Sha256 = string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2"))) };
    }
}
