using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// <c>get_block_export_state</c>: resolves the block like an export (without exporting it) and reads its
/// <see cref="BlockExportLedger"/> entry. Planning-only for plc_write; authorization already ran in HandleLine.
/// </summary>
internal static partial class Program
{
    private static WorkerResponse GetBlockExportState(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");

        return WithEngineeringRoot(request, project =>
        {
            BlockAddress address;
            try
            {
                address = BlockAddress.Parse(request.BlockPath!);
            }
            catch (ArgumentException exception)
            {
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Block path is invalid: " + exception.Message);
            }

            var software = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
            var target = BlockTargetResolver.ResolveForExport(software, address);
            return Success(BlockExportLedger.Shared.Read(software.Name, target.DocumentName));
        });
    }
}
