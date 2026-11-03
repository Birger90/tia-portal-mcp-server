using TiaMcpServer.Contracts.Plc.OpcUa;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc.OpcUa;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of plc_read get_opcua_configuration and the plc_write OPC UA operations (issue #24); authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse PlcOpcUa(WorkerRequest request)
    {
        var info = request.PlcOpcUa ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the OPC UA request.");
        info.Operation = request.Method;
        return WithEngineeringRoot(request, project => PlcOpcUaMethods.IsRead(request.Method)
            ? Success(PlcOpcUaService.Read(project, info))
            : Success(PlcOpcUaService.Write(project, info)));
    }
}
