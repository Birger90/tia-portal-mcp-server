namespace TiaMcpServer.Contracts.Block;

/// <summary>
/// Document format selector shared by the type and block read/write operations.
///
/// <para>
/// Deliberately object-kind-agnostic: <see cref="Source"/> means "Siemens' external-source text
/// for whatever this object is" — .udt for a PlcType, .db for a GlobalDB, .scl for an SCL block —
/// and the extension is always derived from the resolved object, never from the caller.
/// </para>
/// <para>
/// This class exposes no default. The default is per-operation and passed in by the caller:
/// the type operations default to <see cref="Source"/> because they are net-new surface, and the
/// block operations default to <see cref="Xml"/> because they have callers whose payloads must
/// not change. Flipping block defaults belongs to roadmap Phase 5, not here.
/// </para>
/// </summary>
public static class SourceFormatNames
{
    public const string Source = "source";
    public const string Xml = "xml";

    /// <summary>
    /// SIMATIC SD: only the documents of <c>PlcBlock.ExportAsDocuments</c> (.s7dcl and, when exported, .s7res), delimited
    /// like the xml bundle and imported with <c>PlcBlockComposition.ImportFromDocuments(..., ImportDocumentOptions.Override)</c>.
    /// A block format only (get_block_content, update_block_logic); type operations keep <see cref="Allowed"/>.
    /// </summary>
    public const string Sd = "sd";

    /// <summary>The formats of the type operations (and every operation that is not a block content operation).</summary>
    public static readonly IReadOnlyList<string> Allowed = new[] { Source, Xml };

    /// <summary>The formats of get_block_content and update_block_logic.</summary>
    public static readonly IReadOnlyList<string> BlockAllowed = new[] { Source, Xml, Sd };

    /// <summary>Every format a content hash can carry (<c>&lt;format&gt;:sha256:&lt;hex&gt;</c>).</summary>
    public static readonly IReadOnlyList<string> HashFormats = new[] { Source, Xml, Sd };

    public static bool TryNormalize(string? value, string fallback, out string normalized, out string? error)
        => TryNormalize(value, fallback, Allowed, out normalized, out error);

    /// <summary>The block content operations' variant of <see cref="TryNormalize(string?, string, out string, out string?)"/>: also accepts <see cref="Sd"/>.</summary>
    public static bool TryNormalizeBlock(string? value, string fallback, out string normalized, out string? error)
        => TryNormalize(value, fallback, BlockAllowed, out normalized, out error);

    private static bool TryNormalize(string? value, string fallback, IReadOnlyList<string> allowedValues, out string normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = fallback;
            error = null;
            return true;
        }

        foreach (var allowed in allowedValues)
        {
            if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = allowed;
                error = null;
                return true;
            }
        }

        normalized = string.Empty;
        error = $"Invalid format '{value}'. Allowed values: {string.Join(", ", allowedValues)}.";
        return false;
    }
}
