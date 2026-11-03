using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>Resolves the document format of a PLC content operation.</summary>
public static class PlcFormatNames
{
    /// <summary>
    /// Returns the normalized format, applying the per-operation default (types default to source,
    /// blocks to xml). Throws <see cref="ArgumentException"/> for an unknown format.
    /// </summary>
    public static string Normalize(string operation, string? format)
        => TryNormalize(operation, format, out var normalized, out var error)
            ? normalized
            : throw new ArgumentException(error);

    /// <summary>Non-throwing form; <paramref name="error"/> is the plain validation text.</summary>
    public static bool TryNormalize(string operation, string? format, out string normalized, out string? error)
    {
        // The SIMATIC SD operations work on the sd export only (they take no format field).
        if (operation is Contracts.Plc.SimaticSd.SimaticSdMethods.ListNetworks or Contracts.Plc.SimaticSd.SimaticSdMethods.CreateBlockFromDocument
            || Contracts.Plc.SimaticSd.SimaticSdMethods.NetworkEdits.Contains(operation))
            return SourceFormatNames.TryNormalizeBlock(format, SourceFormatNames.Sd, out normalized, out error);
        var fallback = operation is "get_type_content" or "update_type_content"
            ? SourceFormatNames.Source
            : SourceFormatNames.Xml;
        // SIMATIC SD (sd) is a block document format: get_block_content and update_block_logic only.
        return operation is "get_block_content" or "update_block_logic"
            ? SourceFormatNames.TryNormalizeBlock(format, fallback, out normalized, out error)
            : SourceFormatNames.TryNormalize(format, fallback, out normalized, out error);
    }
}
