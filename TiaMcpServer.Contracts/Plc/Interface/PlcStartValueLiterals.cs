using System.Globalization;

namespace TiaMcpServer.Contracts.Plc.Interface;

/// <summary>
/// When a start value TIA Portal stored counts as the literal that was requested. Equal text
/// (without case and surrounding blanks) always counts. Beyond that only numeric spellings of the
/// elementary integer and floating-point types are compared, by value with the invariant culture,
/// because those are the only types whose literal forms the sources describe as plain numbers. Time,
/// date, character, bit-string and typed literals (<c>T#…</c>, <c>16#…</c>, <c>INT#…</c>) are not
/// normalized: a different spelling of those does not count.
/// </summary>
public static class PlcStartValueLiterals
{
    private static readonly HashSet<string> IntegerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SInt", "Int", "DInt", "LInt", "USInt", "UInt", "UDInt", "ULInt",
    };

    private static readonly HashSet<string> FloatTypes = new(StringComparer.OrdinalIgnoreCase) { "Real", "LReal" };

    public static bool SameText(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <param name="dataType">The member's data type from the interface document; null when unknown (then only equal text counts).</param>
    public static bool Equivalent(string? dataType, string? requested, string? stored)
    {
        if (SameText(requested, stored)) return true;
        if (requested is null || stored is null || dataType is null) return false;
        var type = dataType.Trim();
        if (IntegerTypes.Contains(type))
            return TryInteger(requested, out var left) && TryInteger(stored, out var right) && left == right;
        if (FloatTypes.Contains(type))
            return TryFloat(requested, out var left) && TryFloat(stored, out var right) && left.Equals(right);
        return false;
    }

    /// <summary>
    /// Type-free check used where the data type is not known: true when both texts are plain
    /// invariant decimal numbers of equal value (for example 1.5 and 1.50).
    /// </summary>
    public static bool SameNumber(string? left, string? right)
        => left is not null && right is not null
            && decimal.TryParse(left.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var a)
            && decimal.TryParse(right.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var b)
            && a == b;

    private static bool TryInteger(string text, out decimal value)
        => decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static bool TryFloat(string text, out double value)
        => double.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
               CultureInfo.InvariantCulture, out value)
            && !double.IsNaN(value) && !double.IsInfinity(value);
}
