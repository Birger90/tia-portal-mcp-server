using TiaMcpServer.Contracts.Plc.ChangeDetection;

namespace TiaMcpServer.Plc;

/// <summary>plc_read get_plc_checksums and get_fingerprints (issue #6): their field sets and shape rule.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithChangeDetectionSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[PlcChangeDetectionMethods.GetPlcChecksums] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[PlcChangeDetectionMethods.GetFingerprints] = Read(Array.Empty<string>(), new[] { "blockPath", "typePath" });
        return specs;
    }

    private static void ValidateChangeDetectionItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.Operation != PlcChangeDetectionMethods.GetFingerprints) return;
        if ((op.BlockPath is null) == (op.TypePath is null))
            errors.Add($"{prefix}: get_fingerprints needs exactly one of blockPath (a block) or typePath (a PLC data type).");
        foreach (var (name, value) in new[] { ("blockPath", op.BlockPath), ("typePath", op.TypePath) })
        {
            if (value is not null && string.IsNullOrWhiteSpace(value)) errors.Add($"{prefix}: '{name}' must be nonblank when supplied.");
        }
    }
}
