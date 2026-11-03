using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker;

/// <summary>Dispatch of the <c>library_*</c> methods; authorization already ran in HandleLine.</summary>
internal static partial class Program
{
    private static WorkerResponse Library(WorkerRequest request)
    {
        var info = request.Library ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError, $"{request.Method} requires the library request.");
        // Global libraries live on the TiaPortal, the project library on the bound project: both handles come
        // from the verified active context.
        if (!LibraryMethods.IsWriteMethod(request.Method))
        {
            return WithActiveContext(request, session =>
                request.Method == LibraryMethods.Plan
                    ? Success(LibraryWriteService.Plan(session.TiaPortal!, session.EngineeringRoot!, info))
                    : Success(LibraryReadService.Read(session.TiaPortal!, session.EngineeringRoot!, request.Method, info)));
        }

        // Like plc_download: until the item's evidence is collected, every TIA Portal confirmation (for example one
        // raised by Close(), OpenWithUpgrade or Release) is answered by the item's portalConfirmations policy instead
        // of the session-wide Yes and recorded; the service closes the scope before it copies the evidence (it then
        // lingers with the same answer until the next request starts, which reports late dialogs as warnings), this
        // using is the safety net for exits without evidence.
        var (confirmations, scope) = LibraryConfirmationScope.Open(_sharedSession, request.Method, info, PlcDownloadService.ReadConfirmationText);
        using (scope)
        {
            return WithActiveContext(request, session =>
                Success(LibraryWriteService.Write(session.TiaPortal!, session.EngineeringRoot!, request.Method, info, confirmations)));
        }
    }
}
