using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

public static partial class BlockExporter
{
    /// <summary>
    /// The block's SimaticML document alone (no .s7dcl/.s7res companions), through the same
    /// <c>PlcBlock.Export(FileInfo, ExportOptions.None)</c> call and DocumentInfo removal as the bundle.
    /// </summary>
    internal static string ExportSimaticMl(PlcBlock block, string documentName)
        => ExportToTemporaryFile(documentName, path => block.Export(new FileInfo(path), ExportOptions.None));

    /// <summary>The located block's SimaticML document; the export is counted in <see cref="BlockExportLedger"/>.</summary>
    internal static string ExportSimaticMl(LocatedBlock located)
    {
        var name = located.Block.Name;
        BlockExportLedger.Shared.RecordExport(located.Owner.PlcName, name);
        // K20: an inconsistent block's failed export names compile_check (the exception itself propagates unchanged).
        return ExportConsistencyEvidence.Observe(located.Block, () => PlcExportConsistency.BlockHint($"{located.Owner.PlcName}/{name}"),
            () => ExportSimaticMl(located.Block, name));
    }

    /// <summary>The PLC data type's SimaticML document (<c>PlcType.Export(FileInfo, ExportOptions.None)</c>).</summary>
    internal static string ExportSimaticMl(PlcType type, string documentName)
        => ExportToTemporaryFile(documentName, path => type.Export(new FileInfo(path), ExportOptions.None));

    private static string ExportToTemporaryFile(string documentName, Action<string> export)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-simaticml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "document.xml");
            export(path);
            return BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(path));
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(directory);
        }
    }
}
