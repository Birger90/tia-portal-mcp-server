using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the hardware-module methods and <c>move_tag</c>; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse HardwareModule(WorkerRequest request)
    {
        var info = request.HardwareModule ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the hardware-module request.");
        return WithEngineeringRoot(request, project => request.Method switch
        {
            var feature when HardwareFeatureMethods.IsFeature(feature) => Success(HardwareModuleService.Feature(project, info, feature)),
            HardwareModuleMethods.ListPlugLocations => Success(HardwareModuleService.ListPlugLocations(project, info)),
            HardwareModuleMethods.FindModuleTypes => Success(HardwareModuleService.FindModuleTypes(project, info)),
            HardwareModuleMethods.FindContainerTypes => Success(HardwareModuleService.FindContainerTypes(project, info)),
            HardwareModuleMethods.ListIoAddresses => Success(HardwareModuleService.ListIoAddresses(project, info)),
            HardwareModuleMethods.ProbePlug => Success(HardwareModuleService.ProbePlug(project, info)),
            HardwareModuleMethods.ReadAttributes => Success(HardwareModuleService.ReadAttributes(project, info)),
            HardwareModuleMethods.PlugDeviceItem => Success(HardwareModuleService.Plug(project, info)),
            HardwareModuleMethods.MoveDeviceItem => Success(HardwareModuleService.Move(project, info)),
            HardwareModuleMethods.CopyDeviceItem => Success(HardwareModuleService.Copy(project, info)),
            HardwareModuleMethods.DeleteDeviceItem => Success(HardwareModuleService.Delete(project, info)),
            HardwareModuleMethods.ChangeDeviceItemType => Success(HardwareModuleService.ChangeType(project, info)),
            HardwareModuleMethods.SetHardwareAttributes => Success(HardwareModuleService.SetAttributes(project, info)),
            HardwareModuleMethods.SetIoAddress => Success(HardwareModuleService.SetIoAddress(project, info)),
            HardwareModuleMethods.GetCustomIdentity => Success(HardwareModuleService.GetCustomIdentity(project, info)),
            HardwareModuleMethods.SetCustomIdentity => Success(HardwareModuleService.SetCustomIdentity(project, info)),
            HardwareModuleMethods.RemoveCustomIdentity => Success(HardwareModuleService.RemoveCustomIdentity(project, info)),
            HardwareModuleMethods.ListPortConnections => Success(HardwareModuleService.ListPortConnections(project, info)),
            HardwareModuleMethods.ConnectPorts => Success(HardwareModuleService.ConnectPorts(project, info)),
            HardwareModuleMethods.DisconnectPorts => Success(HardwareModuleService.DisconnectPorts(project, info)),
            HardwareModuleMethods.ListTransferAreas => Success(HardwareModuleService.ListTransferAreas(project, info)),
            HardwareModuleMethods.CreateTransferArea => Success(HardwareModuleService.CreateTransferArea(project, info)),
            HardwareModuleMethods.UpdateTransferArea => Success(HardwareModuleService.UpdateTransferArea(project, info)),
            HardwareModuleMethods.DeleteTransferArea => Success(HardwareModuleService.DeleteTransferArea(project, info)),
            HardwareModuleMethods.DeleteIoSystem => Success(HardwareModuleService.DeleteIoSystem(project, info)),
            HardwareModuleMethods.DisconnectFromIoSystem => Success(HardwareModuleService.DisconnectFromIoSystem(project, info)),
            HardwareModuleMethods.ExportCax => Success(HardwareModuleService.ExportCax(project, info)),
            HardwareModuleMethods.ImportCax => Success(HardwareModuleService.ImportCax(project, info)),
            _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"Unsupported hardware-module method '{request.Method}'."),
        });
    }

    private static WorkerResponse MoveTag(WorkerRequest request)
    {
        var move = request.TagMove ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, "move_tag requires the tag-move request.");
        return WithActiveContext(request, session => Success(PlcRoundTrip.MoveTag(session.TiaPortal!, session.EngineeringRoot!, move,
            () => TagMutationService.MoveTag(session.EngineeringRoot!, move, inTransaction: true))));
    }
}
