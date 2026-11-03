using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Plc;

/// <summary>The fail-safe (F-) PLC operations of plc_read/plc_write: field sets merged into the catalog's specs, and their shape validation.</summary>
public static partial class PlcOperationCatalog
{
    /// <summary>Appends the fail-safe specs after every other operation, so the listed operation order stays stable.</summary>
    private static IReadOnlyDictionary<string, Spec> WithSafetySpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        var plc = new[] { "plcName" };
        specs[PlcSafetyMethods.GetAdministration] = Read(Array.Empty<string>(), plc);
        specs[PlcSafetyMethods.ListBlocks] = Read(Array.Empty<string>(), new[] { "plcName", "offset", "limit" });
        specs[PlcSafetyMethods.Login] = Write(new[] { "passwordVariable" }, plc);
        specs[PlcSafetyMethods.Logoff] = Write(Array.Empty<string>(), plc);
        specs[PlcSafetyMethods.SetPassword] = Write(new[] { "passwordVariable" }, plc);
        specs[PlcSafetyMethods.RevokePassword] = Write(new[] { "passwordVariable" }, plc);
        specs[PlcSafetyMethods.CreateRuntimeGroup] = Write(new[] { "runtimeGroupName" }, new[] { "plcName", "mainSafetyBlockPath", "mainSafetyInstanceDbPath" });
        specs[PlcSafetyMethods.SetRuntimeGroup] = Write(new[] { "runtimeGroupName", "mainSafetyBlockPath" }, new[] { "plcName", "mainSafetyInstanceDbPath" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> SafetyStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("passwordVariable", op => op.PasswordVariable),
        ("runtimeGroupName", op => op.RuntimeGroupName),
        ("mainSafetyBlockPath", op => op.MainSafetyBlockPath),
        ("mainSafetyInstanceDbPath", op => op.MainSafetyInstanceDbPath),
    };

    /// <summary>The shape rules of one fail-safe item; nothing here reads the project. Messages never repeat a password variable value.</summary>
    private static void ValidateSafetyItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (!PlcSafetyMethods.IsWorkerMethod(op.Operation)) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        if (op.PasswordVariable is PlcDownloadPasswordMap.RedactedVariable)
            Error("'passwordVariable' must be the NAME of an environment variable (letters, digits, underscore; not starting with a digit), never the password itself; the value was redacted.");
        else if (op.PasswordVariable is PlcDownloadPasswordMap.UnsetVariable)
            Error("'passwordVariable' names an environment variable that is not set (or empty) for this server; set it in the MCP client configuration (env) and restart the server. The name was redacted.");
        if (op.RuntimeGroupName is { } group)
        {
            if (string.IsNullOrWhiteSpace(group)) Error("'runtimeGroupName' must be nonblank.");
            else if (group.Length > PlcSafetyPolicy.MaxRuntimeGroupNameLength) Error($"'runtimeGroupName' must be at most {PlcSafetyPolicy.MaxRuntimeGroupNameLength} characters.");
            else if (group.Any(char.IsControl) || group.Contains('/')) Error("'runtimeGroupName' must not contain control characters or '/'.");
        }

        if (op.MainSafetyInstanceDbPath is not null && op.MainSafetyBlockPath is null)
            Error("'mainSafetyInstanceDbPath' needs mainSafetyBlockPath (Create(name, mainSafetyFB, instanceDB); set_safety_runtime_group assigns both).");
        foreach (var (field, path) in new[] { ("mainSafetyBlockPath", op.MainSafetyBlockPath), ("mainSafetyInstanceDbPath", op.MainSafetyInstanceDbPath) })
        {
            if (path is null) continue;
            try
            {
                if (BlockAddress.Parse(path).PlcName is null) Error($"'{field}' must start with the PLC name, e.g. PLC_1/{path}.");
            }
            catch (ArgumentException exception)
            {
                Error($"'{field}': {exception.Message}");
            }
        }

        if (op.Operation == PlcSafetyMethods.ListBlocks)
        {
            if (op.Offset is < 0) Error("'offset' must be 0 or more.");
            if (op.Limit is < 1 or > PlcSafetyPolicy.MaxBlockLimit) Error($"'limit' must be between 1 and {PlcSafetyPolicy.MaxBlockLimit}.");
        }
    }
}
