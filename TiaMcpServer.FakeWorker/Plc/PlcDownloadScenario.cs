using System.Text.Json;
using Siemens.Engineering.Download.Configurations;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Scripted <c>list_download_targets</c> / <c>plc_download</c> fixture. PLC_1 (device Station_1) offers
/// PN/IE with PLCSIM #1 (one target, "1 X1") and a hardware NIC (two targets); PLC_2 (device
/// Station_2) offers PLCSIM #1 with two targets; PLC_3 is not downloadable. Like the real worker it
/// validates the request with the shared <see cref="PlcDownloadPolicy.ValidateWorkerRequest"/> (any
/// PC interface, hardware included), refuses a path that no longer resolves exactly, and answers
/// stand-in configuration objects (<c>DownloadConfigurationDoubles.cs</c>) through the same
/// <see cref="PlcDownloadCallbackRecorder"/> and reflection-based
/// <see cref="PlcDownloadReflectionAccess"/>, reading passwords from this process's environment.
/// <see cref="Outcome.Succeeded"/> presents StopModules, ConsistentBlocksDownload, TargetForSoftware
/// and StartModules; <see cref="Outcome.Aborted"/> additionally presents DataBlockReinitialization (a
/// selection without a default), UpgradeTargetDevice (a check) and ModuleWriteAccessPassword (a
/// password), so it aborts unless the caller decides all three; <see cref="Outcome.ErrorState"/>
/// returns a DownloadResult in state Error; <see cref="Outcome.PortalConfirmation"/> succeeds after
/// TIA Portal raised one confirmation dialog, answered by the request's policy; <see cref="Outcome.Tls"/>
/// first presents the connection's TLS verification (<c>plc-download-tls</c>: a leaf and a CA certificate,
/// <see cref="FakeTlsCertificates"/>) through the recorder's online legitimation phase, answered only from the
/// request's trusted fingerprints like the real worker's <c>OnlineLegitimation</c> handler.
/// </summary>
sealed class PlcDownloadScenario(PlcDownloadScenario.Outcome outcome)
{
    public enum Outcome { Succeeded, Aborted, ErrorState, PortalConfirmation, Tls }

    public const string HardwareInterface = "Intel(R) Ethernet Connection";

    private static readonly PlcDownloadTargetInventoryInfo Inventory = new()
    {
        Plcs =
        {
            Plc("PLC_1", "Station_1", true, Pc(PlcDownloadPolicy.SimulationPcInterfaceName, 1, "1 X1"), Pc(HardwareInterface, 1, "1 X1", "1 X2")),
            Plc("PLC_2", "Station_2", true, Pc(PlcDownloadPolicy.SimulationPcInterfaceName, 1, "1 X1", "1 X2")),
            Plc("PLC_3", "Station_3", false),
        },
    };

    public string Handle(string line)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(line, WorkerJson.Envelope)!;
        try
        {
            return request.Method switch
            {
                // Lets the write fixtures bind to the scenario like any other project.
                "read_hardware_config" => Ok(WorkerJson.SerializePayload(new HardwareConfigInfo())),
                PlcDownloadMethods.ListTargets => Ok(WorkerJson.SerializePayload(List(request.PlcName))),
                PlcDownloadMethods.Download => Download(request),
                _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"plc-download scenario does not script '{request.Method}'."),
            };
        }
        catch (WorkerOperationException ex)
        {
            return JsonSerializer.Serialize(new WorkerResponse
            {
                Success = false, FailureCategory = ex.FailureCategory, Error = ex.Message,
                EngineeringError = ex.EngineeringError, PlcDownloadOutcome = ex.PlcDownloadOutcome,
            }, WorkerJson.Envelope);
        }
    }

    private static PlcDownloadTargetInventoryInfo List(string? plcName)
    {
        var plcs = Inventory.Plcs.Where(plc => plcName is null
            || string.Equals(plc.PlcName, plcName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(plc.DeviceName, plcName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (plcName is not null && plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No PLC software named '{plcName}' was found in the project.");
        return new PlcDownloadTargetInventoryInfo { Plcs = plcs };
    }

    private string Download(WorkerRequest request)
    {
        var validation = PlcDownloadPolicy.ValidateWorkerRequest(request.PlcDownload);
        if (!validation.IsValid)
            throw new WorkerOperationException(validation.FailureCategory!, validation.Message!);
        var download = request.PlcDownload!;
        var mode = Inventory.Plcs.Where(plc => plc.PlcName == download.PlcName && plc.DeviceName == download.DeviceName)
            .SelectMany(plc => plc.Modes).Where(candidate => candidate.Name == download.Mode).ToList();
        if (mode.Count == 1 && PlcDownloadPolicy.IsImplicitInterfaceNumberAmbiguous(mode[0].PcInterfaces.Select(pc => pc.Name),
                download.PcInterfaceName, download.PcInterfaceNumberExplicit))
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"Several PC interfaces named '{download.PcInterfaceName}' are offered and no number was supplied. Nothing was downloaded.");
        var resolves = mode.Count == 1 && mode[0].PcInterfaces.Count(pc => pc.Name == download.PcInterfaceName && pc.Number == download.PcInterfaceNumber
            && pc.TargetInterfaces.Count(t => t.Name == download.TargetInterface) == 1) == 1;
        if (!resolves)
            throw new WorkerOperationException(WorkerFailureCategories.StateChanged, "The download path no longer resolves exactly. Nothing was downloaded.");

        var result = new PlcDownloadResultInfo
        {
            PlcName = download.PlcName,
            DeviceName = download.DeviceName,
            Path = new PlcDownloadPathInfo
            {
                Mode = download.Mode,
                PcInterface = new PlcDownloadPcInterfaceRefInfo { Name = download.PcInterfaceName, Number = download.PcInterfaceNumber },
                TargetInterface = download.TargetInterface,
            },
            Scope = download.Scope,
            DownloadOptions = validation.DownloadOptions!,
            StartAfterDownload = download.StartAfterDownload,
            PortalConfirmationPolicy = download.PortalConfirmations,
            CompletionVerified = false,
            CompletionNote = PlcDownloadPolicy.CompletionNote,
        };
        var recorder = new PlcDownloadCallbackRecorder(PlcDownloadDecisionSet.For(download), result,
            new PlcDownloadReflectionAccess(configuration => (configuration as DownloadConfiguration)?.Message, Environment.GetEnvironmentVariable));
        try
        {
            if (outcome == Outcome.Tls) recorder.OnlineLegitimation(FakeTlsCertificates.Configuration(download.PlcName));
            // K27: a refused TLS question returns normally (NonTrusted, no throw); TIA Portal then fails the download's
            // connection and the recorded abort decides, like the worker.
            if (result.Abort is not null) throw new PlcDownloadAbortedException(PlcDownloadPolicy.Phases.OnlineLegitimation, "connection refused");
            recorder.PreDownload(new StopModules("The modules will be stopped."));
            if (outcome == Outcome.Aborted)
            {
                recorder.PreDownload(new DataBlockReinitialization("Data blocks will be reinitialized."));
                recorder.PreDownload(new UpgradeTargetDevice("The target device firmware will be upgraded."));
                recorder.PreDownload(new ModuleWriteAccessPassword("The module is write-protected."));
            }

            recorder.PreDownload(new ConsistentBlocksDownload("Consistent download of blocks."));
            recorder.PreDownload(new TargetForSoftware("Select the target for the software."));
            recorder.PostDownload(new StartModules("Start the modules?"));
        }
        catch (PlcDownloadAbortedException)
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcDownloadPolicy.AbortMessage(result.Abort!),
                plcDownloadOutcome: WorkerJson.SerializePayload(result));
        }

        // Audit T2: an unanswered selection answered NoAction returns normally; the recorded abort decides, like the worker.
        if (result.Abort is not null)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, PlcDownloadPolicy.AbortMessage(result.Abort),
                plcDownloadOutcome: WorkerJson.SerializePayload(result));

        var confirmations = new PlcDownloadPortalConfirmationLog();
        if (outcome == Outcome.PortalConfirmation)
            confirmations.Record("Siemens.Engineering.ConfirmationEventArgs", "Do you want to continue?",
                PlcDownloadPolicy.PortalConfirmationPolicies.AnswerFor(download.PortalConfirmations));
        confirmations.CloseInto(result);
        var error = outcome == Outcome.ErrorState;
        result.State = error ? "Error" : "Success";
        result.ErrorCount = error ? 1 : 0;
        result.WarningCount = 0;
        result.Messages.Add(new PlcDownloadMessageInfo
        {
            Message = error ? "Download failed." : "Download completed.",
            State = error ? "Error" : "Success",
            Messages = { new PlcDownloadMessageInfo { Message = "PLC_1: software loaded.", State = error ? "Error" : "Success" } },
        });
        return Ok(WorkerJson.SerializePayload(result));
    }

    private static PlcDownloadTargetPlcInfo Plc(string software, string device, bool downloadable, params PlcDownloadPcInterfaceInfo[] interfaces)
        => new()
        {
            PlcName = software, DeviceName = device, Downloadable = downloadable,
            Modes = downloadable ? new() { new PlcDownloadModeInfo { Name = PlcDownloadPolicy.DefaultMode, PcInterfaces = interfaces.ToList() } } : new(),
        };

    private static PlcDownloadPcInterfaceInfo Pc(string name, int number, params string[] targets)
        => new() { Name = name, Number = number, TargetInterfaces = targets.Select(t => new PlcDownloadTargetInterfaceInfo { Name = t }).ToList() };

    private static string Ok(string payload) => JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = payload }, WorkerJson.Envelope);
}
