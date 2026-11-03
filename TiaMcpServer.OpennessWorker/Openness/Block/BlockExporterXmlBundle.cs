using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

public static partial class BlockExporter
{
    // XML export orchestration. Callbacks keep the one Siemens Export path and make the
    // failure policy executable in offline tests without loading Siemens assemblies. A failed
    // SimaticML export with exported companions returns the companions and writes the D15 warning
    // (SimaticMlExportNotes.NotExported) to stderr, which the worker forwards as a response warning.
    internal static string ExportXmlBundle(
        string displayPath,
        string documentName,
        Func<string> exportXml,
        Action<ICollection<BlockImportDocument>> exportCompanions)
    {
        var documents = new List<BlockImportDocument>();
        var xmlExported = false;
        Exception? xmlFailure = null;
        try
        {
            var content = exportXml();
            var xmlName = documentName + ".xml";
            documents.Add(new BlockImportDocument(xmlName, xmlName, content));
            xmlExported = true;
        }
        catch (Exception exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            // The companion-only fallback stays, but is no longer silent (D15): TIA Portal does not export an inconsistent
            // block as SimaticML (BetterTIA pitfall B4, confirmed live on V21 2026-11-02). The warning reaches the response of
            // every request that exports the block (get_block_content and the worker's own reads); the Siemens message is not
            // forwarded because it can carry the temporary export path.
            xmlFailure = exception;
        }

        if (xmlFailure is not null)
            Console.Error.WriteLine(SimaticMlExportNotes.NotExported(displayPath, null));

        try
        {
            // Companion exports are one package. Do not expose an incomplete package when
            // ExportAsDocuments (or a later file read) fails after producing earlier files.
            var companionDocuments = new List<BlockImportDocument>();
            exportCompanions(companionDocuments);
            documents.AddRange(companionDocuments);
        }
        catch (Exception exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            var outcome = BlockDocumentPackagePolicy.Decide(xmlExported, displayPath, exception.Message);
            if (outcome.IsFatal)
            {
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, outcome.Message);
            }

            Console.Error.WriteLine(outcome.Message);
        }

        return BlockBundleFormat.Compose(documents);
    }
}
