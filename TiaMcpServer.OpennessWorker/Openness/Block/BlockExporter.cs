using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Plc.SimaticSd;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

public static partial class BlockExporter
{
    internal static BlockPostconditionEvidence VerifyPrimaryDocument(
        ProjectBase project,
        string blockPath,
        string primaryDocumentName)
    {
        try
        {
            var address = BlockAddress.Parse(blockPath);
            var software = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
            var target = BlockTargetResolver.ResolveForExport(software, address);
            BlockExportLedger.Shared.RecordExport(software.Name, target.DocumentName);
            return VerifyPrimaryDocument(
                target.DocumentName,
                primaryDocumentName,
                (directory, documentName) =>
                {
                    var result = target.Block!.ExportAsDocuments(directory, documentName);
                    if (result.State != DocumentResultState.Success)
                    {
                        return false;
                    }

                    foreach (FileInfo exported in result.ExportedDocuments)
                    {
                        if (exported.Exists && exported.Length > 0)
                        {
                            return true;
                        }
                    }

                    return false;
                });
        }
        catch (Exception exception)
        {
            return new BlockPostconditionEvidence(
                compileSucceeded: true,
                reExportSucceeded: false,
                diagnosticMessage: "Re-export could not complete after block import: " + exception.Message);
        }
    }

    /// <param name="format">
    /// <see cref="SourceFormatNames.Xml"/> (the block default) produces the multi-document bundle
    /// this operation has always returned, byte for byte. <see cref="SourceFormatNames.Source"/>
    /// produces Siemens external-source text and is available for a global data block or an
    /// SCL-language FB/FC/OB. The host normalizes this to exactly one of the two before it reaches
    /// the worker.
    /// </param>
    /// <param name="withDependencies">
    /// When true, the exported source carries the block's dependency closure and therefore declares
    /// several objects. Such a document is context only — a write refuses it. Ignored for
    /// <see cref="SourceFormatNames.Xml"/>.
    /// </param>
    public static string Export(
        ProjectBase project,
        string blockPath,
        string format,
        bool withDependencies = false)
    {
        var address = BlockAddress.Parse(blockPath);
        var software = PlcSoftwareLocator.FindUnique(project, address.PlcName).Software;
        var target = BlockTargetResolver.ResolveForExport(software, address);
        // Every export counts, attempted ones included (V21 textual-interface pitfall, see BlockExportLedger).
        BlockExportLedger.Shared.RecordExport(software.Name, target.DocumentName);
        // K20: an inconsistent block's failed export names compile_check (the exception itself propagates unchanged).
        return ExportConsistencyEvidence.Observe(target.Block, () => TiaMcpServer.Contracts.Plc.PlcExportConsistency.BlockHint(address.ToDisplayPath()),
            () => ExportResolved(software, target, address, format, withDependencies));
    }

    private static string ExportResolved(PlcSoftware software, ResolvedBlockTarget target, BlockAddress address, string format, bool withDependencies)
    {
        if (string.Equals(format, SourceFormatNames.Sd, StringComparison.Ordinal))
        {
            var sd = ExportSd(target, address);
            if (PlcBlockBundleText.TryGetDocument(sd, ".s7dcl", out _, out var sdS7dcl))
                BlockExportLedger.Shared.RecordTextualInterface(software.Name, target.DocumentName, S7dclImportHazards.HasTextualInterface(sdS7dcl));
            return sd;
        }

        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
        {
            return ExportSource(target, address, withDependencies);
        }

        var bundle = ExportXml(target, address);
        if (PlcBlockBundleText.TryGetDocument(bundle, ".s7dcl", out _, out var s7dcl))
            BlockExportLedger.Shared.RecordTextualInterface(software.Name, target.DocumentName, S7dclImportHazards.HasTextualInterface(s7dcl));
        return bundle;
    }

    private static string ExportXml(ResolvedBlockTarget target, BlockAddress address)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            return ExportXmlBundle(
                address.ToDisplayPath(),
                target.DocumentName,
                () =>
                {
                    // Sole Simatic ML export path.
                    string xmlPath = Path.Combine(tempDir, target.DocumentName + ".xml");
                    target.Block!.Export(new FileInfo(xmlPath), ExportOptions.None);
                    return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(xmlPath));
                },
                documents =>
                {
                    DocumentExportResult result = target.Block!.ExportAsDocuments(
                        new DirectoryInfo(tempDir), target.DocumentName);
                    if (result.State != DocumentResultState.Success)
                    {
                        throw new InvalidOperationException(
                            $"TIA Portal returned document export state '{result.State}'.");
                    }

                    foreach (FileInfo file in result.ExportedDocuments)
                    {
                        documents.Add(new BlockImportDocument(
                            file.Name, file.Name, File.ReadAllText(file.FullName)));
                    }
                });
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// SIMATIC SD: only the documents of <c>PlcBlock.ExportAsDocuments(DirectoryInfo, string)</c> (Siemens V21 skill
    /// simatic-sd §2; .s7dcl, and .s7res when TIA Portal writes one), delimited like the xml bundle with the .s7dcl first.
    /// The files are read as UTF-8 with a leading BOM kept in the text (<see cref="SimaticSdFileText"/>), so the next import
    /// writes them back alike. A refused export fails with TIA Portal's own error or result state: nothing is stricter here.
    /// The export goes into a fresh directory, so no earlier file can make it throw (§2).
    /// </summary>
    private static string ExportSd(ResolvedBlockTarget target, BlockAddress address)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-sd-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            DocumentExportResult result = target.Block!.ExportAsDocuments(new DirectoryInfo(tempDir), target.DocumentName);
            if (result.State != DocumentResultState.Success)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal returned SIMATIC SD document export state '{result.State}' for '{address.ToDisplayPath()}'.");
            var documents = new List<BlockImportDocument>();
            foreach (FileInfo file in result.ExportedDocuments)
                documents.Add(new BlockImportDocument(file.Name, file.Name, SimaticSdFileText.Decode(File.ReadAllBytes(file.FullName))));
            if (documents.Count == 0)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported a successful SIMATIC SD export of '{address.ToDisplayPath()}' but wrote no document.");
            return BlockBundleFormat.Compose(SdDocumentOrder(documents));
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(tempDir);
        }
    }

    /// <summary>The .s7dcl first (the import's primary document), then the others by name.</summary>
    internal static IReadOnlyList<BlockImportDocument> SdDocumentOrder(IEnumerable<BlockImportDocument> documents)
        => documents.OrderBy(d => d.LogicalName.EndsWith(".s7dcl", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(d => d.LogicalName, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Exports one block as Siemens external-source text, raw and unbundled: unlike the XML route,
    /// which carries an .xml plus a companion .s7dcl/.s7res pair and therefore needs
    /// BlockBundleFormat's delimiters, this is a single document with nothing to delimit.
    ///
    /// <para>
    /// GenerateOptions is always passed explicitly. The two-argument overload's default is
    /// undocumented, and the difference matters: with dependencies the file declares several
    /// objects and a write will refuse it, so the caller has to have asked for that.
    /// </para>
    /// </summary>
    private static string ExportSource(
        ResolvedBlockTarget target,
        BlockAddress address,
        bool withDependencies)
    {
        var decision = DecideSourceFormat(target.Block, address);

        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-block-source-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, target.DocumentName + decision.Extension);

            // The resolver's own group, not one re-derived from the block: a unit-scoped block must
            // be generated by the unit's external source group.
            target.ExternalSourceGroup.GenerateSource(
                new List<IGenerateSource> { target.Block! },
                new FileInfo(path),
                withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);

            if (!File.Exists(path))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no source file for "
                    + $"'{target.DocumentName}'. Compile the block in TIA Portal and try again.");
            }

            return SourceTextEncoding.ForTransport(File.ReadAllText(path));
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(tempDir);
        }
    }

    /// <summary>
    /// Decides whether external-source text is defined for this block, throwing the refusal as a
    /// validation error if not.
    ///
    /// <para>
    /// The kind and language are reduced to plain strings here so the decision itself lives in
    /// SourceFormatEligibility, which is Siemens-free and therefore unit-tested. This method is the
    /// only part that needs a live Openness object.
    /// </para>
    /// </summary>
    internal static SourceFormatDecision DecideSourceFormat(PlcBlock? block, BlockAddress address)
    {
        if (block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block was found at '{address.ToDisplayPath()}'.");
        }

        var decision = SourceFormatEligibility.Decide(
            BlockKindName(block),
            block.ProgrammingLanguage.ToString(),
            address.ToDisplayPath());

        if (!decision.IsAllowed)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                decision.RefusalMessage!);
        }

        return decision;
    }

    private static string BlockKindName(PlcBlock block) => block switch
    {
        GlobalDB => "GlobalDB",
        InstanceDB => "InstanceDB",
        ArrayDB => "ArrayDB",
        OB => "OB",
        FB => "FB",
        FC => "FC",
        _ => block.GetType().Name
    };
}
