using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Loadable;

namespace TiaMcpServer.Plc;

/// <summary>plc_write generate_loadable (issue #26): its field set and shape rules.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithLoadableSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[PlcLoadableMethods.GenerateLoadable] = Write(new[] { "filePath", "target" }, new[] { "plcName", "blockPaths", "unitNames", "overwrite" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> LoadableStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("target", op => op.Target),
    };

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> LoadableOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("unitNames", op => op.UnitNames is not null),
    };

    private static void ValidateLoadableItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.Operation != PlcLoadableMethods.GenerateLoadable) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        if (op.Target is not null && !PlcLoadableNames.Targets.Contains(op.Target, StringComparer.Ordinal))
            Error($"target must be one of {string.Join(", ", PlcLoadableNames.Targets)} (TargetOption.None is documented to throw).");
        if ((op.BlockPaths is null) == (op.UnitNames is null))
        {
            Error("generate_loadable needs exactly one of blockPaths (blocks of the non-unit program) or unitNames (software units).");
            return;
        }

        var (field, names) = op.BlockPaths is not null ? ("blockPaths", op.BlockPaths) : ("unitNames", op.UnitNames!);
        if (names.Count is < 1 or > PlcLoadableNames.MaxObjects) Error($"{field} must hold 1 to {PlcLoadableNames.MaxObjects} entries.");
        if (names.Any(string.IsNullOrWhiteSpace)) Error($"{field} entries must be nonblank.");
        if (names.Where(name => name is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count(name => name is not null))
            Error($"{field} must not repeat an entry.");
        if (op.BlockPaths is null) return;
        var plcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in op.BlockPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                if (BlockAddress.Parse(path).PlcName is { } name) plcs.Add(name);
            }
            catch (ArgumentException exception)
            {
                Error($"blockPaths: {exception.Message}");
            }
        }

        if (plcs.Count > 1) Error("blockPaths must all name blocks of one PLC.");
    }
}
