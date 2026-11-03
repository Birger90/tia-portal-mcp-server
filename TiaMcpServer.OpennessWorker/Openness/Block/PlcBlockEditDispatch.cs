using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// The single worker entry point for <see cref="PlcBlockEditMethods"/>; one typed result per method.
/// The Portal handle serves the ExclusiveAccess transactions of copy_block, move_block and move_tag_table.
/// </summary>
internal static class PlcBlockEditDispatch
{
    public static object Run(TiaPortal portal, ProjectBase project, string method, PlcBlockEditRequestInfo request) => method switch
    {
        PlcBlockEditMethods.GetBlockInterface => PlcBlockEditService.GetBlockInterface(project, request),
        PlcBlockEditMethods.GetTypeInterface => PlcBlockEditService.GetTypeInterface(project, request),
        PlcBlockEditMethods.GetDbValues => PlcBlockEditService.GetDbValues(project, request),
        PlcBlockEditMethods.UpdateDbMember => PlcBlockEditService.UpdateDbMember(project, request),
        PlcBlockEditMethods.CreateInstanceDb => PlcBlockEditService.CreateInstanceDb(project, request),
        PlcBlockEditMethods.RenameBlock => PlcBlockEditService.RenameBlock(project, request),
        PlcBlockEditMethods.RenumberBlock => PlcBlockEditService.RenumberBlock(project, request),
        PlcBlockEditMethods.CopyBlock => PlcBlockEditService.CopyBlock(portal, project, request),
        PlcBlockEditMethods.MoveBlock => PlcBlockEditService.MoveBlock(portal, project, request),
        PlcBlockEditMethods.MoveTagTable => TagTableMoveService.Move(portal, project, request),
        PlcBlockEditMethods.GetDbSnapshot => PlcBlockEditService.GetDbSnapshot(project, request),
        PlcBlockEditMethods.CreateDbSnapshot => PlcBlockEditService.CreateDbSnapshot(project, request),
        PlcBlockEditMethods.RenameTags => PlcBatchEditService.RenameTags(portal, project, request),
        PlcBlockEditMethods.RenameObjects => PlcBatchEditService.RenameObjects(portal, project, request),
        PlcBlockEditMethods.ReplaceTagComments => PlcBatchEditService.ReplaceTagComments(portal, project, request),
        PlcBlockEditMethods.ExportTagTable => TagTableFileService.Export(project, request),
        PlcBlockEditMethods.ImportTagTable => TagTableFileService.Import(portal, project, request),
        PlcBlockEditMethods.ExportOpcUaXml => OpcUaExportService.Export(project, request),
        Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument => PlcBlockEditService.CreateBlockFromDocument(project, request),
        _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported PLC block edit method '{method}'."),
    };
}
