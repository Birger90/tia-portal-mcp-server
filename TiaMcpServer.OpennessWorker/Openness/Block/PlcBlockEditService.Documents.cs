using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// create_block_from_document: a new block from a SIMATIC SD bundle (.s7dcl, optional .s7res) through
/// <c>PlcBlockComposition.ImportFromDocuments(DirectoryInfo, string, ImportDocumentOptions.Override)</c> (Siemens V21 skill
/// simatic-sd §3; Override is the only documented option). A block of the declared name anywhere in the PLC refuses the
/// item unless overwrite is true. Not live-tested.
/// </summary>
internal static partial class PlcBlockEditService
{
    public static PlcBlockEditResultInfo CreateBlockFromDocument(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var groupPath = Require(request.TargetGroupPath, "targetGroupPath");
        var content = Require(request.Content, "content");
        var document = SimaticSdBlockDocument.TryCheck(content, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"create_block_from_document: {error}");
        BlockAddress address;
        try
        {
            address = BlockAddress.Parse(groupPath.TrimEnd('/') + "/_");
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"targetGroupPath '{groupPath}': {exception.Message}");
        }

        var plc = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var target = BlockLocator.LocateGroup(project, plc, groupPath);
        var existing = FindAnywhere(plc, document.BlockName);
        if (existing is not null && request.Overwrite != true)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"A block named '{document.BlockName}' already exists at '{existing.CanonicalPath}'. Set overwrite:true to import with "
                + "ImportDocumentOptions.Override (TIA Portal decides what that replaces), or rename the block in the .s7dcl document. Nothing was imported.");
        // A fail-safe document (header S7_Safety := "TRUE") or an existing F-block: TIA Portal's login rule, nothing stricter.
        Safety.PlcSafetyGate.RequireImportPermitted(project, plc.Name, existing?.Block, content, sourceFormat: false);

        // overwrite:true replaces an existing block: its F-runtime group references are checked and restored after the import (E7).
        var links = existing is null ? null : Safety.PlcMainSafetyBlockLinkGuard.Before(project, plc.Name, existing.Block, existing.CanonicalPath,
            () => FindAnywhere(plc, document.BlockName)?.Block);
        var messages = new List<string>();
        if (document.CultureNote is { } cultures) messages.Add(cultures);
        if (document.WithoutBom.Count > 0)
            messages.Add($"{string.Join(" and ", document.WithoutBom)} had no UTF-8 BOM and {(document.WithoutBom.Count == 1 ? "was" : "were")} written with one: "
                + "both files UTF-8 with BOM is reported (1748477582/tia-portal-openness-mcp, not verified) to be required by ImportFromDocuments.");

        // The main safety block of an F-runtime group is never replaced by a SIMATIC SD import (live 2026-11-02, L1-L4): the
        // document goes through a temporary F-block and is imported onto the block as SimaticML.
        if (existing is not null && Safety.PlcMainSafetyBlockSdRouteWorker.Find(project, address.PlcName, plc, existing) is { } route)
            return CreateOntoMainSafetyBlock(project, plc, target, existing, document, route, links, messages, request.DocumentImportOption);

        // Audit N7: Override, or Override combined with the caller's culture member; refused before anything is written.
        var documentOptions = BlockImporter.DocumentImportOptions(request.DocumentImportOption);
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-sd-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, document.BaseName + SimaticSdText.BundleExtension), SimaticSdFileText.Encode(document.S7dcl));
            // The .s7res is optional: TIA Portal's own export of a block without texts has none (live 2026-11-02, E1/E5).
            if (document.S7res is not null)
                File.WriteAllBytes(Path.Combine(directory, document.BaseName + SimaticSdText.ResourceExtension), SimaticSdFileText.Encode(document.S7res));
            DocumentImportResultForBlocks result = target.Group.Blocks.ImportFromDocuments(new DirectoryInfo(directory), document.BaseName, documentOptions);
            var importMessages = SimaticSdImportMessages.Read(result);
            if (result.State != DocumentResultState.Success)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal returned SIMATIC SD import state '{result.State}' for '{document.BaseName}' into '{target.Path}'"
                    + (importMessages.Count == 0 ? "." : ": " + string.Join(" | ", importMessages)));
            BlockExportLedger.Shared.RecordWrite(plc.Name, document.BlockName);
            messages.AddRange(importMessages.Select(m => "TIA Portal: " + m));
        }
        catch (Exception exception) when (links is not null && exception is not (NonRecoverableException or EngineeringObjectDisposedException))
        {
            links.After();
            throw;
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }

        links?.After();

        var created = target.Group.Blocks.Find(document.BlockName)
            ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"ImportFromDocuments returned success, but '{target.Path}' holds no block named '{document.BlockName}' afterwards"
                + (FindAnywhere(plc, document.BlockName) is { } elsewhere ? $" (a block of that name is at '{elsewhere.CanonicalPath}')." : "."));
        if (existing is not null)
            messages.Add($"The import replaced or updated '{existing.CanonicalPath}' (overwrite:true, ImportDocumentOptions.Override).");
        return new PlcBlockEditResultInfo
        {
            Operation = SimaticSdMethods.CreateBlockFromDocument,
            SourceBlockPath = null,
            Block = BlockLocator.Identity(plc.Name, target.Path + "/" + created.Name, created),
            Messages = messages,
        };
    }

    /// <summary>
    /// create_block_from_document with overwrite onto the main safety block of an F-runtime group: the route of
    /// <see cref="Safety.PlcMainSafetyBlockSdRouteWorker"/>; the main block keeps its place (a different targetGroupPath is
    /// reported, never applied: move it with move_block). A failure carries the route evidence as the item's result.
    /// </summary>
    private static PlcBlockEditResultInfo CreateOntoMainSafetyBlock(ProjectBase project, PlcSoftware plc, LocatedBlockGroup target, LocatedBlock existing,
        SimaticSdBlockDocument document, Safety.PlcMainSafetyBlockSdRouteWorker.Target route, Safety.PlcMainSafetyBlockLinkGuard? links, List<string> messages,
        string? documentImportOption)
    {
        try
        {
            var (result, info, warnings) = Safety.PlcMainSafetyBlockSdRouteWorker.Run(project, route, SimaticSdMethods.CreateBlockFromDocument, document.S7dcl, document.S7res, documentImportOption);
            links?.After();
            foreach (var warning in result.Warnings.Concat(warnings)) Console.Error.WriteLine(warning);
            messages.Add($"'{existing.CanonicalPath}' is the main safety block of F-runtime group(s) {string.Join(", ", info.RuntimeGroups.Select(g => $"'{g}'"))}: the document was imported "
                + $"as the temporary F-block '{info.TemporaryBlockName}', compiled, exported as SimaticML and imported onto the block as SimaticML (route {info.Route}); "
                + $"the temporary block is {info.TemporaryBlockState}.");
            if (!Equals(existing.Group, target.Group))
                messages.Add($"targetGroupPath '{target.Path}' differs from the main safety block's group; the block stays at '{existing.CanonicalPath}' (move it with plc_write move_block).");
            var block = FindAnywhere(plc, document.BlockName)
                ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"The SimaticML import onto '{existing.CanonicalPath}' returned, but no block named '{document.BlockName}' was found afterwards.");
            return new PlcBlockEditResultInfo
            {
                Operation = SimaticSdMethods.CreateBlockFromDocument,
                SourceBlockPath = null,
                Block = BlockLocator.Identity(plc.Name, block.CanonicalPath, block.Block),
                Messages = messages,
                MainSafetyBlockRoute = info,
            };
        }
        catch (PlcMainSafetyBlockRouteException exception)
        {
            links?.After();
            throw new WorkerOperationException(WorkerFailureCategories.IsKnown(exception.Category) ? exception.Category : WorkerFailureCategories.WorkerOperationFailed,
                exception.Message,
                engineeringError: (exception.InnerException as WorkerOperationException)?.EngineeringError,
                plcDownloadOutcome: Contracts.Json.WorkerJson.SerializePayload(exception.Info));
        }
    }

    /// <summary>The block of that name anywhere in the PLC and its software units; null when there is none.</summary>
    private static LocatedBlock? FindAnywhere(PlcSoftware plc, string name)
    {
        try
        {
            return BlockLocator.Locate(plc, BlockAddress.Parse(plc.Name + "/" + name));
        }
        catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.TargetNotFound)
        {
            return null;
        }
        catch (WorkerOperationException exception) when (exception.FailureCategory == WorkerFailureCategories.TargetAmbiguous)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"Several blocks named '{name}' exist in PLC '{plc.Name}'; create_block_from_document does not choose one. Nothing was imported.");
        }
    }
}
