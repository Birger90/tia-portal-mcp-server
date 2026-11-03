using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.ChangeDetection;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.ChangeDetection;

/// <summary>
/// plc_read get_plc_checksums and get_fingerprints (issue #6, not live-tested). The V21 reference (Siemens.Engineering.Step7.xml)
/// documents <c>PlcChecksumProvider.Software</c>/<c>.TextLists</c> and <c>FingerprintProvider.GetFingerprints()</c> but not which
/// object offers them, so the provider is taken from the object's <c>GetServiceInfos()</c> by exact full name and retrieved with
/// <c>IServiceProvider.GetService(Type)</c> (<see cref="TechnologyObjectRuntime"/>), the PlcSoftware for the checksums, the
/// resolved block or PLC data type for the fingerprints. Every member is bound at runtime by the Siemens-free
/// <see cref="PlcChangeDetectionRun"/>; no stub member was added.
/// </summary>
internal static class PlcChangeDetectionService
{
    private static bool Recoverable(Exception exception)
        => exception is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering)
            && engineering is not EngineeringObjectDisposedException;

    public static PlcChecksumReadInfo ReadChecksums(ProjectBase project, PlcChangeDetectionRequestInfo info)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, info.PlcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " Nothing was read.");
        if (plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No PLC software{(info.PlcName is null ? string.Empty : $" named '{info.PlcName}'")} was found in the project.");
        var result = new PlcChecksumReadInfo();
        foreach (var plc in plcs)
        {
            var software = plc.Software;
            var offered = TechnologyObjectRuntime.ServiceTypes(software);
            result.Plcs.Add(Bind(() => PlcChangeDetectionRun.ReadChecksums(software.Name, plc.DeviceName, offered,
                type => TechnologyObjectRuntime.TryService(software, offered, type.FullName!, out _), Recoverable)));
        }

        return result;
    }

    public static PlcFingerprintReadInfo ReadFingerprints(ProjectBase project, PlcChangeDetectionRequestInfo info)
    {
        object target;
        string plcName, kind, path;
        if (info.BlockPath is { } blockPath)
        {
            var address = Parse(() => BlockAddress.Parse(blockPath));
            var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
            target = BlockTargetResolver.ResolveForExport(plc, address).Block
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{blockPath}' was not found.");
            (plcName, kind, path) = (plc.Name, PlcChangeDetectionNames.ObjectKindBlock, blockPath);
        }
        else if (info.TypePath is { } typePath)
        {
            var address = Parse(() => PlcTypeAddress.Parse(typePath));
            target = PlcTypeTargetResolver.ResolveForExport(project, address).Type
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"PLC data type '{typePath}' was not found.");
            (plcName, kind, path) = (PlcSoftwareLocator.FindUnique(project, address.PlcName).Software.Name, PlcChangeDetectionNames.ObjectKindType, typePath);
        }
        else
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "get_fingerprints needs blockPath or typePath.");
        }

        var offered = TechnologyObjectRuntime.ServiceTypes(target);
        return Bind(() => PlcChangeDetectionRun.ReadFingerprints(kind, path, plcName, offered,
            type => TechnologyObjectRuntime.TryService(target, offered, type.FullName!, out _), Recoverable));
    }

    private static T Parse<T>(Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }
    }

    /// <summary>Translates a Siemens-free refusal into the worker's categorized failure; Siemens exceptions pass unchanged.</summary>
    private static T Bind<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }
}
