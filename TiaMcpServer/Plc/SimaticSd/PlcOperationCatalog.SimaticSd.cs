using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.SimaticSd;

namespace TiaMcpServer.Plc;

/// <summary>
/// The SIMATIC SD operations: list_networks (plc_read), insert_network, replace_network, delete_network and
/// create_block_from_document (plc_write). Their field sets and shape rules; nothing here reads the project.
/// Structural refusals only: the code or document would corrupt the edited .s7dcl; LAD/FBD/SCL semantics are TIA Portal's.
/// </summary>
public static partial class PlcOperationCatalog
{
    /// <summary>A network number or insert position larger than this is refused before anything is read.</summary>
    public const int MaxNetworkNumber = 9999;

    // networkNumber and position are listed as optional because the shared required check reads string fields only;
    // ValidateSimaticSdItem requires networkNumber for replace_network and delete_network.
    private static IReadOnlyDictionary<string, Spec> WithSimaticSdSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(existing, StringComparer.Ordinal);
        specs[SimaticSdMethods.ListNetworks] = Read(new[] { "blockPath" }, Array.Empty<string>());
        specs[SimaticSdMethods.InsertNetwork] = Write(new[] { "blockPath", "code" }, new[] { "language", "position", "expectedContentHash" });
        specs[SimaticSdMethods.ReplaceNetwork] = Write(new[] { "blockPath", "code" }, new[] { "networkNumber", "language", "expectedContentHash" });
        specs[SimaticSdMethods.DeleteNetwork] = Write(new[] { "blockPath" }, new[] { "networkNumber", "expectedContentHash" });
        specs[SimaticSdMethods.CreateBlockFromDocument] = Write(new[] { "targetGroupPath", "content" }, new[] { "overwrite", "documentImportOption" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> SimaticSdStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("code", op => op.Code),
    };

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> SimaticSdOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("position", op => op.Position is not null),
    };

    private static void ValidateSimaticSdItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        void Error(string message) => errors.Add($"{prefix}: {message}");
        switch (op.Operation)
        {
            case SimaticSdMethods.InsertNetwork:
            case SimaticSdMethods.ReplaceNetwork:
            case SimaticSdMethods.DeleteNetwork:
                if (op.Operation != SimaticSdMethods.InsertNetwork)
                {
                    if (op.NetworkNumber is null) Error("networkNumber is required (1-based, in document order as list_networks numbers them).");
                    else if (op.NetworkNumber is < 1 or > MaxNetworkNumber) Error($"networkNumber must be between 1 and {MaxNetworkNumber}.");
                }

                if (op.Position is < 1 or > MaxNetworkNumber) Error($"position must be between 1 and {MaxNetworkNumber} (omitted appends).");
                if (op.Language is not null && !SimaticSdText.IsLanguageName(op.Language))
                    Error("language must be a language name such as LAD, FBD, SCL or STL (letters, digits, underscore); it is written as given into the S7_Language pragma and TIA Portal decides.");
                if (op.Code is not null)
                {
                    try
                    {
                        var code = SimaticSdText.NormalizeNetworkCode(op.Code);
                        if (op.Language is not null && code.DeclaredLanguage is not null && !string.Equals(op.Language, code.DeclaredLanguage, StringComparison.OrdinalIgnoreCase))
                            Error($"language '{op.Language}' contradicts the code's own pragma S7_Language \"{code.DeclaredLanguage}\"; give one of them.");
                        if (op.Operation == SimaticSdMethods.InsertNetwork && op.Language is null && code.DeclaredLanguage is null
                            && SimaticSdText.DetectLanguage(string.Join("\n", code.BodyLines)) is null)
                            Error("the network language cannot be detected from the code (RUNG wire# is LAD, RUNG \"...\" or RUNG <number> is FBD, no RUNG is SCL); give language.");
                    }
                    catch (SimaticSdException exception)
                    {
                        Error(exception.Message);
                    }
                }

                break;
            case SimaticSdMethods.CreateBlockFromDocument:
                if (op.TargetGroupPath is not null && !PlcGroupPathLooksDeterministic(op.TargetGroupPath))
                    Error("targetGroupPath must be a deterministic block group path, e.g. PLC_1/Blocks or PLC_1/Blocks/Line (as copy_block takes it).");
                if (op.Content is not null && DocumentError(op.Content) is { } documentError) Error(documentError);
                break;
        }
    }

    /// <summary>The structural check of a create_block_from_document bundle (<see cref="SimaticSdBlockDocument"/>); null when it is usable.</summary>
    internal static string? DocumentError(string content)
    {
        _ = SimaticSdBlockDocument.TryCheck(content, out var error);
        return error;
    }

    private static bool PlcGroupPathLooksDeterministic(string groupPath)
    {
        try
        {
            var address = BlockAddress.Parse(groupPath.TrimEnd('/') + "/_");
            return address.PlcName is not null && address.IsDeterministic;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
