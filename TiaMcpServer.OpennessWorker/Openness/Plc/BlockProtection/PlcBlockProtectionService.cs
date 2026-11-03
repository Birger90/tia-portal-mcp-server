using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.BlockProtection;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.BlockProtection;

/// <summary>
/// Block know-how and write protection (issue #5, not live-tested). The block is resolved like every block edit
/// (<see cref="BlockLocator"/>); both providers are found among the block's <c>GetServiceInfos()</c> by exact full name
/// (<see cref="TechnologyObjectRuntime.ServiceTypes"/>, retrieved with <c>IServiceProvider.GetService(Type)</c>), and every member
/// is bound at runtime by the Siemens-free <see cref="PlcBlockProtectionRun"/>; no protection type is compiled against and no stub
/// was added. A fail-safe block follows TIA Portal's safety login rule (<see cref="Safety.PlcSafetyGate"/>) before any change.
/// Passwords resolve in the worker only (environment of the worker process, or Windows generic credentials).
/// </summary>
internal static class PlcBlockProtectionService
{
    private static bool Recoverable(Exception exception)
        => exception is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering)
            && engineering is not EngineeringObjectDisposedException;

    public static PlcBlockProtectionInfo Read(ProjectBase project, PlcBlockProtectionRequestInfo request)
        => Bind(() => PlcBlockProtectionRun.Read(Target(project, request.BlockPath).Target, Recoverable));

    public static PlcBlockProtectionWriteResultInfo Write(ProjectBase project, string method, PlcBlockProtectionRequestInfo request)
    {
        var (target, plcName, located) = Target(project, request.BlockPath);
        Safety.PlcSafetyGate.RequireChangePermitted(project, plcName, located.Block, method);
        return Bind(() => PlcBlockProtectionRun.Write(method, target, request, Recoverable,
            PlcBlockProtectionRun.Resolver(Environment.GetEnvironmentVariable, WindowsCredentialReader.Instance)));
    }

    private static (PlcBlockProtectionTarget Target, string PlcName, LocatedBlock Located) Target(ProjectBase project, string blockPath)
    {
        BlockAddress address;
        try
        {
            address = BlockAddress.Parse(blockPath);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }

        var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var located = BlockLocator.Locate(plc, address);
        var block = located.Block;
        var offered = TechnologyObjectRuntime.ServiceTypes(block);
        string? language = BlockLocator.Read(() => block.ProgrammingLanguage.ToString());
        var target = new PlcBlockProtectionTarget(plc.Name, located.CanonicalPath, block.Name, language, block, offered,
            type => TechnologyObjectRuntime.TryService(block, offered, type.FullName!, out _));
        return (target, plc.Name, located);
    }

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
