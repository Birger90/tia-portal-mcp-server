using TiaMcpServer.Contracts.PlcOnline;

namespace TiaMcpServer.Plc;

/// <summary>Audit N3: get_station_fingerprint_data's fields and shape rules.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithOnlineFingerprintSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(existing, StringComparer.Ordinal);
        // pcInterface is required too (ValidateOnlineFingerprintItem): the required-field check covers string fields only.
        specs[PlcOnlineMethods.GetStationFingerprintData] = Read(new[] { "mode", "targetAddress" },
            new[] { "pcInterface", "maxElements", "trustedCertificates", "passwords", "onlineUser" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> OnlineFingerprintStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("targetAddress", op => op.TargetAddress),
    };

    private static void ValidateOnlineFingerprintItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.Operation != PlcOnlineMethods.GetStationFingerprintData) return;
        if (op.TargetAddress is not null && PlcOnlinePolicy.ValidateAddress(op.TargetAddress) is { } address) errors.Add($"{prefix}: targetAddress: {address}");
        if (op.MaxElements is < 1 or > PlcFingerprintData.MaxItems) errors.Add($"{prefix}: maxElements must be between 1 and {PlcFingerprintData.MaxItems}.");
        if (PlcTlsTrustPolicy.NormalizeList(op.TrustedCertificates, out _) is { } trusted) errors.Add($"{prefix}: {trusted}");
        if (op.PcInterface is not { } pc) errors.Add($"{prefix}: pcInterface {{ name, number }} is required (there is no default interface).");
        else if (string.IsNullOrWhiteSpace(pc.Name) || pc.Number < 0) errors.Add($"{prefix}: pcInterface needs a nonblank name and a non-negative number.");
    }
}
