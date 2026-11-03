namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// The closed comparison vocabulary of <c>plcsim_test</c> expect steps. Names are matched exactly
/// (ordinal). <c>bool</c> and <c>string</c> support only <c>equals</c>/<c>notEquals</c>; numeric
/// types support every comparison. Integers (signed and unsigned) compare exactly as decimals; <c>float</c> and
/// <c>double</c> compare as IEEE doubles (no tolerance: use <c>between</c> for a band), so a NaN
/// observation satisfies only <c>notEquals</c>.
/// </summary>
public static class PlcSimComparisons
{
    public const string EqualsTo = "equals";
    public const string NotEquals = "notEquals";
    public const string GreaterThan = "greaterThan";
    public const string GreaterOrEqual = "greaterOrEqual";
    public const string LessThan = "lessThan";
    public const string LessOrEqual = "lessOrEqual";
    public const string Between = "between";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        EqualsTo, NotEquals, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Between
    });

    public static bool IsKnown(string? comparison)
        => comparison is not null && All.Contains(comparison, StringComparer.Ordinal);

    public static bool IsSupported(string dataType, string comparison)
        => PlcSimDataTypes.IsNumeric(dataType) || comparison is EqualsTo or NotEquals;

    /// <summary>
    /// Evaluates <paramref name="observed"/> against the typed expectation. All typed values must
    /// already be the CLR type of <paramref name="dataType"/> (see <see cref="PlcSimValueCodec"/>).
    /// </summary>
    public static bool Evaluate(string dataType, string comparison, object observed, object? expected, object? min, object? max)
    {
        if (!IsKnown(comparison) || !IsSupported(dataType, comparison))
            throw new ArgumentException($"Comparison '{comparison}' is not supported for {dataType}.", nameof(comparison));

        if (dataType == PlcSimDataTypes.Bool)
        {
            var equal = (bool)observed == (bool)expected!;
            return comparison == EqualsTo ? equal : !equal;
        }

        if (dataType == PlcSimDataTypes.String)
        {
            var equal = string.Equals((string)observed, (string)expected!, StringComparison.Ordinal);
            return comparison == EqualsTo ? equal : !equal;
        }

        if (PlcSimDataTypes.IsInteger(dataType))
        {
            var value = ToInteger(observed);
            return comparison switch
            {
                EqualsTo => value == ToInteger(expected!),
                NotEquals => value != ToInteger(expected!),
                GreaterThan => value > ToInteger(expected!),
                GreaterOrEqual => value >= ToInteger(expected!),
                LessThan => value < ToInteger(expected!),
                LessOrEqual => value <= ToInteger(expected!),
                _ => value >= ToInteger(min!) && value <= ToInteger(max!)
            };
        }

        var real = ToDouble(observed);
        return comparison switch
        {
            EqualsTo => real == ToDouble(expected!),
            NotEquals => !(real == ToDouble(expected!)),
            GreaterThan => real > ToDouble(expected!),
            GreaterOrEqual => real >= ToDouble(expected!),
            LessThan => real < ToDouble(expected!),
            LessOrEqual => real <= ToDouble(expected!),
            _ => real >= ToDouble(min!) && real <= ToDouble(max!)
        };
    }

    /// <summary>Human-readable form of an expectation, for step messages.</summary>
    public static string Describe(string comparison, string? expected, string? min, string? max)
        => comparison == Between ? $"between {min} and {max}" : $"{comparison} {expected}";

    private static decimal ToInteger(object value)
        => PlcSimValueCodec.TryGetIntegerExact(value, out var integer)
            ? integer
            : throw new ArgumentException($"Expected an integer value but got {value.GetType().FullName}.");

    private static double ToDouble(object value) => value switch
    {
        float single => single,
        double real => real,
        _ => throw new ArgumentException($"Expected a floating-point value but got {value.GetType().FullName}.")
    };
}
