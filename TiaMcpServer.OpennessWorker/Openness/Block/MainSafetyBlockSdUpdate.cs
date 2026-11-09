using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.Safety;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// update_block_logic (format sd, or format xml with a bundle without .xml, which takes the SIMATIC SD route) of the main
/// safety block of an F-runtime group: the same preflight as the direct SD import (documents, declared name, safety login
/// rule, content hash in the request's format), then <see cref="PlcMainSafetyBlockSdRouteWorker"/> instead of
/// <c>ImportFromDocuments</c> on the main block (live TIA Portal V21, 2026-11-02, L1-L4). The network edits arrive here too,
/// because they are mutated as update_block_logic format sd. The response carries the route evidence
/// (<see cref="PlcMainSafetyBlockRouteInfo"/>) in <see cref="WorkerResponse.PlcDownloadOutcome"/>, on success and on failure.
/// </summary>
internal static class MainSafetyBlockSdUpdate
{
    /// <summary>The route target when the import would use SIMATIC SD and the block is a main safety block; null otherwise.</summary>
    public static PlcMainSafetyBlockSdRouteWorker.Target? Applies(ProjectBase project, string blockPath, string content, string format)
    {
        if (format == SourceFormatNames.Xml)
        {
            try
            {
                var bundle = BlockImportBundleParser.Parse(Path.GetFileName(blockPath) + ".xml", content);
                if (BlockImportRouting.SelectRoute(bundle) != BlockImportRoute.SimaticSd) return null;
            }
            catch (WorkerOperationException)
            {
                // The import's own preflight reports the malformed bundle.
                return null;
            }
        }
        else if (format != SourceFormatNames.Sd)
        {
            return null;
        }

        return PlcMainSafetyBlockSdRouteWorker.Find(project, blockPath);
    }

    public static WorkerResponse Run(ProjectBase project, PlcMainSafetyBlockSdRouteWorker.Target target, string blockPath, string content, string format, string? expectedContentHash,
        string? documentImportOption = null)
    {
        var fallback = Path.GetFileName(blockPath) + (format == SourceFormatNames.Sd ? SimaticSdText.BundleExtension : ".xml");
        var (s7dcl, s7res) = BlockImportCoordinator.ExecuteWithPreTargetOutcome(
            () =>
            {
                var prepared = BlockWritePreflight.PrepareUpdate(blockPath, fallback, content);
                var document = BlockImportRouting.SelectSimaticSdDocument(prepared.Bundle);
                BlockImportRouting.RequireDeclaredName(document, target.Located.Block.Name, prepared.Address.ToDisplayPath());
                Safety.PlcSafetyGate.RequireImportPermitted(project, prepared.Address.PlcName, target.Located.Block, content, sourceFormat: false);
                PlcWritePreconditions.RequireContentHash(expectedContentHash, format, BlockExporter.Export(project, blockPath, format));
                var resources = prepared.Bundle.Documents.Where(d => d.LogicalName.EndsWith(SimaticSdText.ResourceExtension, StringComparison.OrdinalIgnoreCase)).ToList();
                return (document.Content, resources.Count == 1 ? resources[0].Content : null);
            },
            sourceApplicable: false);

        try
        {
            var (result, info, warnings) = PlcMainSafetyBlockSdRouteWorker.Run(project, target, "update_block_logic", s7dcl, s7res, documentImportOption);
            return new WorkerResponse
            {
                Success = true,
                Payload = result.Payload,
                Warnings = Merge(result.Warnings, warnings),
                BlockImportOutcome = result.Outcome,
                PlcDownloadOutcome = WorkerJson.SerializePayload(info),
            };
        }
        catch (PlcMainSafetyBlockRouteException exception)
        {
            var inner = exception.InnerException as WorkerOperationException;
            return new WorkerResponse
            {
                Success = false,
                FailureCategory = WorkerFailureCategories.IsKnown(exception.Category) ? exception.Category : WorkerFailureCategories.WorkerOperationFailed,
                Error = exception.Message,
                Warnings = Merge(inner is null ? Array.Empty<string>() : BlockImportDiagnosticSanitizer.SanitizeWarnings(inner.Warnings), Array.Empty<string>()),
                BlockImportOutcome = inner?.BlockImportOutcome ?? BlockUpdateOutcomeDecorator.Outcome(format, importerEntered: exception.Info.MainImport != PlcMainSafetyBlockSdRoute.MainImports.NotStarted),
                EngineeringError = inner?.EngineeringError ?? (exception.InnerException is EngineeringException engineering ? WorkerExecution.Describe(engineering) : null),
                PlcDownloadOutcome = WorkerJson.SerializePayload(exception.Info),
            };
        }
    }

    private static List<string>? Merge(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        var all = first.Concat(second).ToList();
        return all.Count == 0 ? null : all;
    }
}
