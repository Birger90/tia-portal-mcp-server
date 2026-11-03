using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of <c>create_io_system</c>; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse CreateIoSystem(WorkerRequest request)
    {
        var target = NetworkConfigurationTargetBinding.Resolve(request);
        if (request.NewIoSystemName is not null && string.IsNullOrWhiteSpace(request.NewIoSystemName))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "NewIoSystemName must not be blank when supplied.");
        return WithEngineeringRoot(request, project => Success(IoSystemCreationService.Create(project, target, request.NewIoSystemName)));
    }
}
