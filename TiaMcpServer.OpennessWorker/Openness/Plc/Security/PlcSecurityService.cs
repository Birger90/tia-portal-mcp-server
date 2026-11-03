using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.Security;

/// <summary>
/// CPU protection settings of the CPU device item that hosts a PLC software (the same item <c>plc_download</c> uses).
/// Nothing here is compiled against a Siemens protection type: the services are found among the types the documented
/// <c>IEngineeringServiceProvider.GetServiceInfos()</c> lists (<see cref="TechnologyObjectRuntime.ServiceTypes"/>), retrieved
/// with <c>IServiceProvider.GetService(Type)</c> (<see cref="TechnologyObjectRuntime.Service"/>), and every member is bound
/// by exact name and signature in the Siemens-free <see cref="PlcSecurityRun"/>. Passwords come only from password sources and are never generated, returned or logged.
/// </summary>
internal static class PlcSecurityService
{
    /// <summary>A getter failure that only makes a value unreadable; session loss and disposal still propagate.</summary>
    private static bool Recoverable(Exception exception)
        => exception is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering)
            && engineering is not EngineeringObjectDisposedException;

    public static PlcSecurityReadInfo Read(ProjectBase project, PlcSecurityRequestInfo info)
    {
        var failures = new List<string>();
        var plcs = PlcSoftwareLocator.FindEveryPlc(project, info.PlcName, failures).ToList();
        if (failures.Count > 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, failures[0] + " Nothing was read.");
        if (plcs.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"No PLC software{(info.PlcName is null ? string.Empty : $" named '{info.PlcName}'")} was found in the project.");
        var result = new PlcSecurityReadInfo();
        foreach (var plc in plcs) result.Plcs.Add(Bind(() => PlcSecurityRun.Read(Cpu(plc), Recoverable)));
        return result;
    }

    public static (PlcSecurityWriteResultInfo Result, List<string> Warnings) Write(ProjectBase project, string method, PlcSecurityRequestInfo info)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, info.PlcName);
        // Password sources resolve in the worker only (environment of the worker process, or Windows generic credentials).
        return Bind(() => PlcSecurityRun.Write(method, Cpu(plc), info, Recoverable,
            PlcSecurityRun.Resolver(Environment.GetEnvironmentVariable, TiaMcpServer.Contracts.Shared.WindowsCredentialReader.Instance)));
    }

    private static PlcSecurityCpu Cpu(PlcSoftwareLocator.DiscoveredPlcSoftware plc)
    {
        DeviceItem cpu = plc.HostDeviceItem ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            $"The CPU device item of PLC '{plc.Software.Name}' is unknown. Nothing was changed.");
        var offered = TechnologyObjectRuntime.ServiceTypes(cpu);
        return new PlcSecurityCpu(plc.Software.Name, plc.DeviceName, offered,
            type => TechnologyObjectRuntime.TryService(cpu, offered, type.FullName!, out _));
    }

    /// <summary>Translates a Siemens-free refusal into the worker's categorized failure; Siemens exceptions pass unchanged.</summary>
    private static T Bind<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (PlcSecurityBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }
}
