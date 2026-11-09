using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>Audit N7/N8: presence and shape rules of swImportOptions and documentImportOption.</summary>
public static partial class PlcOperationCatalog
{
    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> ImportOptionOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("swImportOptions", op => op.SwImportOptions is not null),
        ("documentImportOption", op => op.DocumentImportOption is not null),
    };

    private static void ValidateImportOptionsItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (PlcImportOptionNames.ValidateSwImportOptions(op.SwImportOptions) is { } sw) errors.Add($"{prefix}: {sw}");
        if (PlcImportOptionNames.ValidateDocumentCultureOption(op.DocumentImportOption) is { } document) errors.Add($"{prefix}: {document}");
        if (op.Operation is not ("update_block_logic" or "update_type_content")) return;
        // The write format: update_block_logic defaults to xml, update_type_content to source (PlcFormatNames).
        if (!PlcFormatNames.TryNormalize(op.Operation, op.Format, out var format, out _)) return;
        if (op.SwImportOptions is not null && format != SourceFormatNames.Xml)
            errors.Add($"{prefix}: swImportOptions apply to format xml (SimaticML) only, not to format {format}.");
        if (op.DocumentImportOption is not null && format is not (SourceFormatNames.Sd or SourceFormatNames.Xml))
            errors.Add($"{prefix}: documentImportOption applies to SIMATIC SD imports (format sd) only, not to format {format}.");
        if (op.SwImportOptions is not null && op.DocumentImportOption is not null)
            errors.Add($"{prefix}: swImportOptions (SimaticML) and documentImportOption (SIMATIC SD) exclude each other.");
    }
}
