using TiaMcpServer.Contracts.Plc;

namespace TiaMcpServer.Plc;

/// <summary>
/// The operation reference (describe_operations, K91): field set and shape rules. It reads nothing from TIA Portal; the
/// reference itself is <see cref="PlcOperationReference"/>.
/// </summary>
public static partial class PlcOperationCatalog
{
    /// <summary>The most operation names one describe_operations item takes (keeps one item within the value budget).</summary>
    public const int MaxDescribedOperations = 3;

    private static Dictionary<string, Spec> WithHelpSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[PlcOperationHelpMethods.DescribeOperations] = Read(Array.Empty<string>(), new[] { "operationNames" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> HelpOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("operationNames", op => op.OperationNames is not null),
    };

    /// <summary>The declared required and optional wire fields of any PLC read or write operation, and whether it is a write.</summary>
    public static bool TryGetFields(string operation, out IReadOnlyList<string> required, out IReadOnlyList<string> optional, out bool isWrite)
    {
        if (Specs.TryGetValue(operation, out var spec))
        {
            required = spec.Required;
            optional = spec.Optional;
            isWrite = spec.IsWrite;
            return true;
        }

        required = optional = None;
        isWrite = false;
        return false;
    }

    private static void ValidateHelpItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (op.Operation != PlcOperationHelpMethods.DescribeOperations || op.OperationNames is not { } names) return;
        if (names.Count is < 1 or > MaxDescribedOperations)
        {
            errors.Add($"{prefix}: operationNames must list 1 to {MaxDescribedOperations} operation names; omit it for the index of every operation.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add($"{prefix}: operationNames entries must be nonblank.");
                continue;
            }

            if (!seen.Add(name))
                errors.Add($"{prefix}: operationNames lists '{Bounded(name)}' more than once.");
            else if (!Specs.ContainsKey(name))
                errors.Add($"{prefix}: '{Bounded(name)}' in operationNames is no PLC operation. Valid read operations: "
                    + $"{string.Join(", ", ReadOperationNames)}. Valid write operations: {string.Join(", ", WriteOperationNames)}.");
        }
    }

    private static string Bounded(string name) => name.Length <= 80 ? name : name[..80] + "...";
}
