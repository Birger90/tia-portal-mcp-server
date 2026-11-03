using Siemens.Engineering;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// The SIMATIC SD import of update_block_logic: format sd (an sd bundle), and the shared ImportFromDocuments call that
/// the older implicit route (format xml with a bundle without .xml) uses too. Not live-tested.
/// </summary>
public static partial class BlockImporter
{
    /// <summary>
    /// <c>PlcBlockComposition.ImportFromDocuments(directory, name, ImportDocumentOptions.Override)</c> (Siemens V21 skill
    /// simatic-sd §3) on the target's group. A non-success state fails the import; TIA Portal's result messages are read
    /// (<see cref="SimaticSdImportMessages"/>) and reported as controlled warnings.
    /// </summary>
    private static void ImportFromDocuments(
        ResolvedBlockTarget target,
        string plcName,
        DirectoryInfo directory,
        string baseName,
        BlockImportInvocationBoundary boundary)
    {
        boundary.BeforeSiemensCall();
        var result = target.Group.Blocks.ImportFromDocuments(
            directory,
            baseName,
            ImportDocumentOptions.Override);
        boundary.AfterSiemensCallReturned();
        if (result.State == DocumentResultState.Success)
            BlockExportLedger.Shared.RecordWrite(plcName, target.DocumentName);
        boundary.RecordReturnedResult(result.State == DocumentResultState.Success
            ? BlockImportReturnedState.Success
            : BlockImportReturnedState.NonSuccess);

        if (result.State != DocumentResultState.Success)
            throw new WorkerOperationException(
                WorkerFailureCategories.WorkerOperationFailed,
                BlockImportDiagnosticSanitizer.Failure(BlockImportDiagnosticContext.Import),
                SimaticSdImportMessages.AsWarnings(SimaticSdImportMessages.Read(result), result.State.ToString()));
    }

    /// <summary>
    /// update_block_logic format sd: the bundle holds exactly one .s7dcl (and optionally its .s7res), declares the target
    /// block's name (update only, never a create or rename), passes the safety login rule (a header S7_Safety := "TRUE"
    /// marks an F-block) and the sd content hash, and goes to ImportFromDocuments with Override.
    /// </summary>
    private static BlockImportResult ImportSd(
        ProjectBase project,
        string blockPath,
        string content,
        string? expectedContentHash)
    {
        var preflight = BlockImportCoordinator.ExecuteWithPreTargetOutcome(
            () =>
            {
                var fallbackDocumentName = Path.GetFileName(blockPath) + ".s7dcl";
                var prepared = BlockWritePreflight.PrepareUpdate(blockPath, fallbackDocumentName, content);
                var s7dcl = BlockImportRouting.SelectSimaticSdDocument(prepared.Bundle);
                var existing = BlockTargetResolver.ResolveForImport(project, prepared.Address);
                if (existing.Block is null)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.TargetNotFound,
                        $"No block exists at '{prepared.Address.ToDisplayPath()}'. update_block_logic only updates a "
                        + "block that is already in the project; it never creates one (create_block_from_document does).");
                }

                BlockImportRouting.RequireDeclaredName(s7dcl, existing.Block.Name, prepared.Address.ToDisplayPath());
                Safety.PlcSafetyGate.RequireImportPermitted(project, prepared.Address.PlcName, existing.Block, content, sourceFormat: false);
                PlcWritePreconditions.RequireContentHash(
                    expectedContentHash,
                    SourceFormatNames.Sd,
                    BlockExporter.Export(project, blockPath, SourceFormatNames.Sd));
                return new XmlImportPreflight(fallbackDocumentName, prepared);
            },
            sourceApplicable: false);

        return BlockImportCoordinator.Execute(
            preflight.FallbackDocumentName,
            content,
            (directory, bundle, boundary) =>
            {
                var target = BlockTargetResolver.ResolveForImport(project, preflight.Prepared.Address);
                var plcName = PlcSoftwareLocator.FindUnique(project, preflight.Prepared.Address.PlcName).Software.Name;
                var s7dcl = BlockImportRouting.SelectSimaticSdDocument(bundle);
                ImportFromDocuments(target, plcName, directory, Path.GetFileNameWithoutExtension(s7dcl.SafeFileName), boundary);
            },
            compileAllowed => ObservePostconditions(
                project,
                preflight.Prepared.Address,
                blockPath,
                SourceFormatNames.Sd,
                primaryDocumentName: null,
                compileAllowed,
                warnings: null));
    }
}
