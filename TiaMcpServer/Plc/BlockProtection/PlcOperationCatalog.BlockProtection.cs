using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.BlockProtection;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Plc;

/// <summary>The block know-how and write protection operations (issue #5): field sets and shape rules. Passwords only as sources.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithBlockProtectionSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[PlcBlockProtectionMethods.GetBlockProtection] = Read(new[] { "blockPath" }, Array.Empty<string>());
        foreach (var method in PlcBlockProtectionMethods.Write)
            specs[method] = method == PlcBlockProtectionMethods.ChangeWriteProtectionPassword
                ? Write(new[] { "blockPath", "passwordVariable", "newPasswordVariable" }, Array.Empty<string>())
                : Write(new[] { "blockPath", "passwordVariable" }, Array.Empty<string>());
        return specs;
    }

    /// <summary>Messages never repeat a password source value.</summary>
    private static void ValidateBlockProtectionItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (!PlcBlockProtectionMethods.IsWorkerMethod(op.Operation)) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        if (!string.IsNullOrWhiteSpace(op.BlockPath))
        {
            try
            {
                _ = BlockAddress.Parse(op.BlockPath);
            }
            catch (ArgumentException exception)
            {
                Error($"blockPath: {exception.Message}");
            }
        }

        if (!PlcBlockProtectionMethods.IsWrite(op.Operation)) return;
        foreach (var (field, value) in new[] { ("passwordVariable", op.PasswordVariable), ("newPasswordVariable", op.NewPasswordVariable) })
        {
            if (value is PlcDownloadPasswordMap.RedactedVariable)
                Error($"'{field}' must name where the password is (an environment variable NAME or a credential target), never the password itself; the value was redacted.");
            else if (value is PlcDownloadPasswordMap.UnsetVariable)
                Error($"'{field}' names an environment variable that is not set (or empty) for this server; set it in the MCP client configuration (env) and restart the server. The name was redacted.");
        }
    }
}
