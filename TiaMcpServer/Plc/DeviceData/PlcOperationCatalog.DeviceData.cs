using TiaMcpServer.Contracts.Plc.DeviceData;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Plc;

/// <summary>plc_write download_to_folder and the online master secret (issue #16): field sets and shape rules.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithDeviceDataSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[PlcDeviceDataMethods.DownloadToFolder] = Write(new[] { "directoryPath" }, new[] { "plcName", "rhSystem", "portalConfirmations", "overwrite" });
        specs[PlcDeviceDataMethods.SetMasterSecretOnline] = Write(new[] { "passwordVariable" }, new[] { "plcName", "portalConfirmations" });
        specs[PlcDeviceDataMethods.ResetMasterSecretOnline] = Write(Array.Empty<string>(), new[] { "plcName", "portalConfirmations" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> DeviceDataOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("rhSystem", op => op.RhSystem is not null),
    };

    private static void ValidateDeviceDataItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (!PlcDeviceDataMethods.IsWorkerMethod(op.Operation)) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        if (op.Operation == PlcDeviceDataMethods.DownloadToFolder
            && Contracts.Plc.PlcFileTargets.PathShapeError("directoryPath", op.DirectoryPath) is { } directoryError)
            Error(directoryError);
        if (op.Operation == PlcDeviceDataMethods.SetMasterSecretOnline)
        {
            if (op.PasswordVariable is PlcDownloadPasswordMap.RedactedVariable)
                Error("'passwordVariable' must name where the master secret is (an environment variable NAME or a credential target), never the secret itself; the value was redacted.");
            else if (op.PasswordVariable is PlcDownloadPasswordMap.UnsetVariable)
                Error("'passwordVariable' names an environment variable that is not set (or empty) for this server; set it in the MCP client configuration (env) and restart the server. The name was redacted.");
        }
    }
}
