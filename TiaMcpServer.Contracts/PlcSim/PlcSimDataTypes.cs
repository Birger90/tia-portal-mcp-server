using System.Globalization;
using System.Text.Json;

namespace TiaMcpServer.Contracts.PlcSim;

/// <summary>
/// The closed set of tag data types the PLCSIM tools accept. Each maps to exactly one typed
/// <c>IInstance</c> read method and, except <c>string</c>, one typed write method. The method names
/// are verified against the S7-PLCSIM Advanced API manual; their parameter lists are not, so the
/// worker resolves them at runtime against declared candidates (see <c>probe_api</c>).
/// </summary>
public static class PlcSimDataTypes
{
    public const string Bool = "bool";
    public const string Int8 = "int8";
    public const string Int16 = "int16";
    public const string Int32 = "int32";
    public const string Int64 = "int64";
    public const string Float = "float";
    public const string Double = "double";
    public const string String = "string";
    public const string UInt8 = "uint8";
    public const string UInt16 = "uint16";
    public const string UInt32 = "uint32";
    public const string UInt64 = "uint64";

    /// <summary>
    /// Every readable data type. The unsigned types read through <c>ReadUInt8</c>,
    /// <c>ReadUInt16</c>, <c>ReadUInt32</c> and <c>ReadUInt64</c>, which the Siemens developer
    /// reference lists for the .NET <c>IInstance</c> (checked 2026-11-02). Live on V21 with
    /// PLCSIM Advanced V8.0 (2026-11-02) a Byte tag read with <c>ReadInt8</c> failed with
    /// TypeMismatch (-29), so a Byte/USInt needs <c>uint8</c>.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Bool, Int8, Int16, Int32, Int64, UInt8, UInt16, UInt32, UInt64, Float, Double, String
    });

    /// <summary>
    /// Data types with a write method. <c>string</c> is read-only: <c>WriteString</c> is not a
    /// verified member name of the PLCSIM Advanced API, so no string write is attempted. The
    /// unsigned types are read-only for the same reason: the part of the developer reference
    /// that could be read on 2026-11-02 listed no <c>WriteUInt*</c> member.
    /// </summary>
    public static IReadOnlyList<string> Writable { get; } = Array.AsReadOnly(new[]
    {
        Bool, Int8, Int16, Int32, Int64, Float, Double
    });

    public static bool IsKnown(string? dataType)
        => dataType is not null && All.Contains(dataType, StringComparer.Ordinal);

    public static bool IsWritable(string? dataType)
        => dataType is not null && Writable.Contains(dataType, StringComparer.Ordinal);

    public static bool IsInteger(string dataType)
        => dataType is Int8 or Int16 or Int32 or Int64 or UInt8 or UInt16 or UInt32 or UInt64;

    public static bool IsFloatingPoint(string dataType)
        => dataType is Float or Double;

    public static bool IsNumeric(string dataType) => IsInteger(dataType) || IsFloatingPoint(dataType);

    /// <summary>The <c>IInstance</c> read method a data type uses, for example <c>ReadInt16</c>.</summary>
    public static string ReadMethod(string dataType) => dataType switch
    {
        Bool => "ReadBool",
        Int8 => "ReadInt8",
        Int16 => "ReadInt16",
        Int32 => "ReadInt32",
        Int64 => "ReadInt64",
        UInt8 => "ReadUInt8",
        UInt16 => "ReadUInt16",
        UInt32 => "ReadUInt32",
        UInt64 => "ReadUInt64",
        Float => "ReadFloat",
        Double => "ReadDouble",
        String => "ReadString",
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown PLCSIM data type.")
    };

    /// <summary>The <c>IInstance</c> write method a data type uses, or null for the read-only types.</summary>
    public static string? WriteMethod(string dataType) => dataType switch
    {
        Bool => "WriteBool",
        Int8 => "WriteInt8",
        Int16 => "WriteInt16",
        Int32 => "WriteInt32",
        Int64 => "WriteInt64",
        Float => "WriteFloat",
        Double => "WriteDouble",
        String or UInt8 or UInt16 or UInt32 or UInt64 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown PLCSIM data type.")
    };

    /// <summary>The CLR type of a typed value of <paramref name="dataType"/>.</summary>
    public static Type ClrType(string dataType) => dataType switch
    {
        Bool => typeof(bool),
        Int8 => typeof(sbyte),
        Int16 => typeof(short),
        Int32 => typeof(int),
        Int64 => typeof(long),
        UInt8 => typeof(byte),
        UInt16 => typeof(ushort),
        UInt32 => typeof(uint),
        UInt64 => typeof(ulong),
        Float => typeof(float),
        Double => typeof(double),
        String => typeof(string),
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown PLCSIM data type.")
    };

    public static string Names(IEnumerable<string> names) => string.Join(", ", names);
}

/// <summary>
/// Strict conversion between JSON values and typed tag values. There is no lenient coercion: a
/// boolean needs <c>true</c>/<c>false</c>, an integer needs an integral JSON number in range, a
/// floating-point value needs a finite JSON number in range, and a string needs a JSON string.
/// </summary>
public static class PlcSimValueCodec
{
    /// <summary>
    /// Rounds a finite double to single precision strictly: a value that rounds to infinity is out of
    /// range and a non-zero value that rounds to zero underflows; both are refused, never coerced.
    /// Values that round to <see cref="float.MaxValue"/> (such as this codec's own output) are accepted.
    /// </summary>
    public static bool TryToSingle(double number, out object? typed, out string? error)
    {
        typed = null;
        var single = (float)number;
        if (float.IsInfinity(single))
        {
            error = "float values must be within the single-precision range";
            return false;
        }

        if (single == 0f && number != 0d)
        {
            error = "float values must not underflow to zero in single precision";
            return false;
        }

        typed = single;
        error = null;
        return true;
    }

    /// <summary>Parses <paramref name="value"/> as <paramref name="dataType"/>, returning the exact CLR type of <see cref="PlcSimDataTypes.ClrType"/>.</summary>
    public static bool TryParse(string dataType, JsonElement? value, out object? typed, out string? error)
    {
        typed = null;
        error = null;
        if (value is not { } element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            error = $"a {dataType} value is required";
            return false;
        }

        switch (dataType)
        {
            case PlcSimDataTypes.Bool:
                if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    typed = element.GetBoolean();
                    return true;
                }

                error = "bool values must be JSON true or false";
                return false;
            case PlcSimDataTypes.Int8:
            case PlcSimDataTypes.Int16:
            case PlcSimDataTypes.Int32:
            case PlcSimDataTypes.Int64:
            case PlcSimDataTypes.UInt8:
            case PlcSimDataTypes.UInt16:
            case PlcSimDataTypes.UInt32:
            case PlcSimDataTypes.UInt64:
                if (element.ValueKind != JsonValueKind.Number || !TryGetIntegral(element, out var integer))
                {
                    error = $"{dataType} values must be integral JSON numbers";
                    return false;
                }

                var (min, max) = IntegerRange(dataType);
                if (integer < min || integer > max)
                {
                    error = $"{dataType} values must be between {min} and {max}";
                    return false;
                }

                typed = ToClrInteger(dataType, integer);
                return true;
            case PlcSimDataTypes.Float:
            case PlcSimDataTypes.Double:
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var number)
                    || double.IsNaN(number) || double.IsInfinity(number))
                {
                    error = $"{dataType} values must be finite JSON numbers";
                    return false;
                }

                if (dataType == PlcSimDataTypes.Float)
                    return TryToSingle(number, out typed, out error);

                typed = number;
                return true;
            case PlcSimDataTypes.String:
                if (element.ValueKind == JsonValueKind.String)
                {
                    typed = element.GetString();
                    return true;
                }

                error = "string values must be JSON strings";
                return false;
            default:
                error = $"unknown data type '{dataType}'";
                return false;
        }
    }

    /// <summary>
    /// Converts a value an API read method returned into the exact CLR type of
    /// <paramref name="dataType"/>. Integral API values of another width are accepted only when
    /// they fit; any other CLR type is rejected rather than coerced.
    /// </summary>
    public static bool TryFromApiValue(string dataType, object? apiValue, out object? typed, out string? error)
    {
        typed = null;
        error = null;
        var expected = PlcSimDataTypes.ClrType(dataType);
        if (apiValue is null)
        {
            if (dataType == PlcSimDataTypes.String)
            {
                error = "the API returned null for a string read";
                return false;
            }

            error = $"the API returned null for a {dataType} read";
            return false;
        }

        if (apiValue.GetType() == expected)
        {
            typed = apiValue;
            return true;
        }

        if (PlcSimDataTypes.IsInteger(dataType) && TryGetIntegerExact(apiValue, out var integer))
        {
            var (min, max) = IntegerRange(dataType);
            if (integer >= min && integer <= max)
            {
                typed = ToClrInteger(dataType, integer);
                return true;
            }
        }

        error = $"the API returned {apiValue.GetType().FullName} where {expected.FullName} was expected for {dataType}";
        return false;
    }

    /// <summary>
    /// Renders a typed value as a JSON scalar. Floating-point values use the shortest invariant text
    /// that round-trips; non-finite values, which JSON cannot represent, become the strings
    /// <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c>.
    /// </summary>
    public static JsonElement ToJson(object? value)
    {
        string text = value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            sbyte or byte or short or ushort or int or uint or long or ulong
                => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            float single => FormatSingle(single),
            double real => FormatDouble(real),
            decimal money => money.ToString(CultureInfo.InvariantCulture),
            string text1 => JsonSerializer.Serialize(text1),
            char character => JsonSerializer.Serialize(character.ToString()),
            _ => throw new ArgumentException($"Unsupported scalar type {value.GetType().FullName}.", nameof(value))
        };
        return Parse(text);
    }

    /// <summary>Parses JSON text into a detached element.</summary>
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static bool TryGetInteger(object value, out long integer)
    {
        switch (value)
        {
            case sbyte v: integer = v; return true;
            case byte v: integer = v; return true;
            case short v: integer = v; return true;
            case ushort v: integer = v; return true;
            case int v: integer = v; return true;
            case uint v: integer = v; return true;
            case long v: integer = v; return true;
            case ulong v when v <= long.MaxValue: integer = (long)v; return true;
            default: integer = 0; return false;
        }
    }

    /// <summary>
    /// Any CLR integer as an exact <see cref="decimal"/>, which holds the full ranges of both
    /// <see cref="long"/> and <see cref="ulong"/> (netstandard2.0 has no Int128).
    /// </summary>
    public static bool TryGetIntegerExact(object value, out decimal integer)
    {
        switch (value)
        {
            case ulong v: integer = v; return true;
            default:
                if (TryGetInteger(value, out var signed))
                {
                    integer = signed;
                    return true;
                }

                integer = 0m;
                return false;
        }
    }

    private static bool TryGetIntegral(JsonElement element, out decimal integer)
    {
        if (element.TryGetInt64(out var signed))
        {
            integer = signed;
            return true;
        }

        if (element.TryGetUInt64(out var unsigned))
        {
            integer = unsigned;
            return true;
        }

        integer = 0m;
        return false;
    }

    private static object ToClrInteger(string dataType, decimal integer) => dataType switch
    {
        PlcSimDataTypes.Int8 => (sbyte)integer,
        PlcSimDataTypes.Int16 => (short)integer,
        PlcSimDataTypes.Int32 => (int)integer,
        PlcSimDataTypes.Int64 => (long)integer,
        PlcSimDataTypes.UInt8 => (byte)integer,
        PlcSimDataTypes.UInt16 => (ushort)integer,
        PlcSimDataTypes.UInt32 => (uint)integer,
        PlcSimDataTypes.UInt64 => (object)(ulong)integer,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Not an integer data type.")
    };

    private static (decimal Min, decimal Max) IntegerRange(string dataType) => dataType switch
    {
        PlcSimDataTypes.Int8 => (sbyte.MinValue, sbyte.MaxValue),
        PlcSimDataTypes.Int16 => (short.MinValue, short.MaxValue),
        PlcSimDataTypes.Int32 => (int.MinValue, int.MaxValue),
        PlcSimDataTypes.UInt8 => (byte.MinValue, byte.MaxValue),
        PlcSimDataTypes.UInt16 => (ushort.MinValue, ushort.MaxValue),
        PlcSimDataTypes.UInt32 => (uint.MinValue, uint.MaxValue),
        PlcSimDataTypes.UInt64 => (ulong.MinValue, ulong.MaxValue),
        _ => (long.MinValue, long.MaxValue)
    };

    private static string FormatSingle(float value)
    {
        if (float.IsNaN(value)) return "\"NaN\"";
        if (float.IsPositiveInfinity(value)) return "\"Infinity\"";
        if (float.IsNegativeInfinity(value)) return "\"-Infinity\"";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
            text = value.ToString("G9", CultureInfo.InvariantCulture);
        return text;
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value)) return "\"NaN\"";
        if (double.IsPositiveInfinity(value)) return "\"Infinity\"";
        if (double.IsNegativeInfinity(value)) return "\"-Infinity\"";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        if (double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
            text = value.ToString("G17", CultureInfo.InvariantCulture);
        return text;
    }
}
