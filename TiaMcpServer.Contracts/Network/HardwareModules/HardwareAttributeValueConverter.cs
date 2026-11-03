using System.Globalization;

namespace TiaMcpServer.Contracts.Network;

/// <summary>Outcome of converting one requested attribute value to a CLR value the attribute supports.</summary>
public sealed class HardwareAttributeConversion
{
    private HardwareAttributeConversion(bool success, object? value, Type? targetType, string? error)
    {
        Success = success;
        Value = value;
        TargetType = targetType;
        Error = error;
    }

    public bool Success { get; }

    public object? Value { get; }

    public Type? TargetType { get; }

    public string? Error { get; }

    public static HardwareAttributeConversion Ok(object value, Type targetType) => new(true, value, targetType, null);

    public static HardwareAttributeConversion Fail(string error) => new(false, null, null, error);
}

/// <summary>
/// Pure, Siemens-free conversion of a typed request value to one of the CLR types an attribute's
/// <c>EngineeringAttributeInfo.SupportedTypes</c> declares. Strict: a value is converted only to a
/// declared type; nothing is coerced through text.
/// <list type="bullet">
/// <item><c>boolean</c> needs <see cref="bool"/>; <c>string</c> needs <see cref="string"/>.</item>
/// <item><c>integer</c> goes to the first declared integral type (metadata order) whose range holds the
/// value; without any integral type, to the first declared floating type (<c>double</c>,
/// <c>float</c>, <c>decimal</c>).</item>
/// <item><c>number</c> goes only to a declared floating type; an integral-only attribute needs <c>integer</c>.</item>
/// <item><c>enum</c> needs exactly one declared enum type defining the ordinal member name.</item>
/// </list>
/// </summary>
public static class HardwareAttributeValueConverter
{
    private static readonly Type[] IntegralTypes =
    {
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong),
    };

    private static readonly Type[] FloatingTypes = { typeof(double), typeof(float), typeof(decimal) };

    private const int MaxListedSymbols = 40;

    public static HardwareAttributeConversion Convert(HardwareAttributeValueInfo value, IReadOnlyList<Type> supportedTypes)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (supportedTypes is null) throw new ArgumentNullException(nameof(supportedTypes));
        var declared = supportedTypes.Where(type => type is not null).Select(Unwrap).ToArray();
        var prefix = $"Attribute '{value.Name}'";
        if (declared.Length == 0)
            return HardwareAttributeConversion.Fail($"{prefix} declares no supported CLR type, so no value can be converted for it.");

        switch (value.Kind)
        {
            case HardwareModuleMethods.AttributeValueKinds.Boolean:
                return value.BooleanValue is { } flag && declared.Contains(typeof(bool))
                    ? HardwareAttributeConversion.Ok(flag, typeof(bool))
                    : Mismatch(prefix, value.Kind, declared);
            case HardwareModuleMethods.AttributeValueKinds.String:
                return value.StringValue is { } text && declared.Contains(typeof(string))
                    ? HardwareAttributeConversion.Ok(text, typeof(string))
                    : Mismatch(prefix, value.Kind, declared);
            case HardwareModuleMethods.AttributeValueKinds.Integer:
                return value.IntegerValue is { } integer ? ConvertInteger(prefix, integer, declared) : Mismatch(prefix, value.Kind, declared);
            case HardwareModuleMethods.AttributeValueKinds.Number:
                return value.NumberValue is { } number ? ConvertNumber(prefix, number, declared) : Mismatch(prefix, value.Kind, declared);
            case HardwareModuleMethods.AttributeValueKinds.Enum:
                return value.EnumSymbol is { } symbol ? ConvertEnum(prefix, symbol, declared) : Mismatch(prefix, value.Kind, declared);
            default:
                return HardwareAttributeConversion.Fail(
                    $"{prefix}: value kind '{value.Kind}' is not supported. Valid kinds: {string.Join(", ", HardwareModuleMethods.AttributeValueKinds.All)}.");
        }
    }

    /// <summary>Equality used to verify a read-back: same CLR type family and equal value; enums by type and numeric value.</summary>
    public static bool ReadBackEquals(object? expected, object? actual)
    {
        if (expected is null || actual is null) return expected is null && actual is null;
        if (expected is Enum && actual is Enum)
            return expected.GetType() == actual.GetType() && expected.Equals(actual);
        if (expected.GetType() == actual.GetType()) return expected.Equals(actual);
        if (IsNumeric(expected) && IsNumeric(actual))
        {
            try
            {
                return System.Convert.ToDecimal(expected, CultureInfo.InvariantCulture) == System.Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    private static HardwareAttributeConversion ConvertInteger(string prefix, long integer, Type[] declared)
    {
        var integral = declared.Where(IntegralTypes.Contains).ToArray();
        foreach (var type in integral)
        {
            if (TryConvertIntegral(integer, type, out var converted)) return HardwareAttributeConversion.Ok(converted!, type);
        }

        if (integral.Length > 0)
            return HardwareAttributeConversion.Fail(
                $"{prefix}: integer {integer.ToString(CultureInfo.InvariantCulture)} is outside the range of every declared integral type ({Names(integral)}).");

        var floating = declared.FirstOrDefault(FloatingTypes.Contains);
        if (floating is not null) return ToFloating(prefix, integer, floating);
        return Mismatch(prefix, HardwareModuleMethods.AttributeValueKinds.Integer, declared);
    }

    private static HardwareAttributeConversion ConvertNumber(string prefix, double number, Type[] declared)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            return HardwareAttributeConversion.Fail($"{prefix}: number must be finite.");
        var floating = declared.FirstOrDefault(FloatingTypes.Contains);
        if (floating is not null) return ToFloating(prefix, number, floating);
        if (declared.Any(IntegralTypes.Contains))
            return HardwareAttributeConversion.Fail(
                $"{prefix} declares only integral types ({Names(declared.Where(IntegralTypes.Contains))}); supply kind 'integer'.");
        return Mismatch(prefix, HardwareModuleMethods.AttributeValueKinds.Number, declared);
    }

    private static HardwareAttributeConversion ToFloating(string prefix, double number, Type type)
    {
        if (type == typeof(double)) return HardwareAttributeConversion.Ok(number, type);
        if (type == typeof(float))
        {
            var single = (float)number;
            return float.IsInfinity(single)
                ? HardwareAttributeConversion.Fail($"{prefix}: {number.ToString("R", CultureInfo.InvariantCulture)} is outside the range of System.Single.")
                : HardwareAttributeConversion.Ok(single, type);
        }

        try
        {
            return HardwareAttributeConversion.Ok(System.Convert.ToDecimal(number, CultureInfo.InvariantCulture), type);
        }
        catch (OverflowException)
        {
            return HardwareAttributeConversion.Fail($"{prefix}: {number.ToString("R", CultureInfo.InvariantCulture)} is outside the range of System.Decimal.");
        }
    }

    private static HardwareAttributeConversion ConvertEnum(string prefix, string symbol, Type[] declared)
    {
        var enums = declared.Where(type => type.IsEnum).ToArray();
        if (enums.Length == 0) return Mismatch(prefix, HardwareModuleMethods.AttributeValueKinds.Enum, declared);
        var defining = enums.Where(type => Enum.GetNames(type).Contains(symbol, StringComparer.Ordinal)).ToArray();
        if (defining.Length == 1)
            return HardwareAttributeConversion.Ok(Enum.Parse(defining[0], symbol, ignoreCase: false), defining[0]);
        if (defining.Length > 1)
            return HardwareAttributeConversion.Fail($"{prefix}: enum member '{symbol}' is defined by several declared enum types ({Names(defining)}).");
        var valid = enums.SelectMany(Enum.GetNames).Distinct(StringComparer.Ordinal).ToArray();
        var listed = string.Join(", ", valid.Take(MaxListedSymbols)) + (valid.Length > MaxListedSymbols ? $", ... ({valid.Length} in total)" : string.Empty);
        return HardwareAttributeConversion.Fail($"{prefix}: '{symbol}' is not a member of {Names(enums)}. Valid members (case-sensitive): {listed}.");
    }

    private static bool TryConvertIntegral(long value, Type type, out object? converted)
    {
        converted = null;
        try
        {
            converted = type == typeof(ulong)
                ? value < 0 ? throw new OverflowException() : (object)(ulong)value
                : System.Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static HardwareAttributeConversion Mismatch(string prefix, string kind, IEnumerable<Type> declared)
        => HardwareAttributeConversion.Fail(
            $"{prefix}: a value of kind '{kind}' cannot be converted to any declared supported type ({Names(declared)}).");

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool IsNumeric(object value) => value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static string Names(IEnumerable<Type> types) => string.Join(", ", types.Select(type => type.FullName ?? type.Name));
}
