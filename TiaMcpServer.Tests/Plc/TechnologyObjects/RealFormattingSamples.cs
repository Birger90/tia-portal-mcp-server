using System.Globalization;
using System.Text;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

/// <summary>
/// Sample methods for the IL scanner's self-tests (never called): one forbidden shape per method, by the rule set that
/// reports it, and clean methods that neither rule set may report. The source file name is what the document-based
/// selection test selects.
/// </summary>
internal static class RealFormattingSamples
{
    /// <summary>Reported by both rule sets: the runtime parses, formats or converts a real.</summary>
    internal static class Reals
    {
        public static double ParseDouble(string text) => double.Parse(text, CultureInfo.InvariantCulture);

        public static bool TryParseSingle(string text) => float.TryParse(text, out _);

        public static decimal ParseDecimal(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

        public static string FormatR(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        public static string FormatE9(float value) => value.ToString("E9", CultureInfo.InvariantCulture);

        public static string FormatDecimal(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        public static bool TryFormat(double value, Span<char> buffer) => value.TryFormat(buffer, out _, "R", CultureInfo.InvariantCulture);

        public static decimal DecimalFromDouble(double value) => new(value);

        public static double ExplicitConversion(decimal value) => (double)value;

        public static double ConvertToDouble(string text) => Convert.ToDouble(text, CultureInfo.InvariantCulture);

        public static string ConvertToString(double value) => Convert.ToString(value, CultureInfo.InvariantCulture);

        public static object ChangeType(object value) => Convert.ChangeType(value, typeof(double), CultureInfo.InvariantCulture);

        public static string AppendDouble(double value) => new StringBuilder().Append(value).ToString();

        public static string InterpolateDouble(double value) => $"{value}";

        public static string NullableToString(double? value) => value.ToString() ?? string.Empty;

        public static string XmlConvertDouble(double value) => System.Xml.XmlConvert.ToString(value);

        public static string JoinDoubles(double[] values) => string.Join(",", values);

        public static void WriteDouble(TextWriter writer, double value) => writer.Write(value);

        public static Func<string> DelegateToString(double value) => value.ToString;

        public static Func<double, string> InLambda() => value => value.ToString(CultureInfo.InvariantCulture);

        public static string InLocalFunction(double value)
        {
            return Format(value);

            static string Format(double inner) => inner.ToString(CultureInfo.InvariantCulture);
        }

        public static async Task<double> InAsync(string text)
        {
            await Task.Yield();
            return double.Parse(text, CultureInfo.InvariantCulture);
        }

        public static IEnumerable<string> InIterator(float value)
        {
            yield return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Reported by the strict rule set only: these format whatever object they get, a boxed real included.</summary>
    internal static class Objects
    {
        public static string FormatObject(object value) => string.Format(CultureInfo.InvariantCulture, "{0}", value);

        public static string ConcatObject(object value) => string.Concat("x", value);

        public static string JoinObjects(object[] values) => string.Join(",", values);

        public static string AppendObject(object value) => new StringBuilder().Append(value).ToString();

        public static string ObjectToString(object value) => value.ToString() ?? string.Empty;

        public static string ConstrainedToString<T>(T value) => value?.ToString() ?? string.Empty;

        public static string Formattable(IFormattable value) => value.ToString("R", CultureInfo.InvariantCulture);

        public static int ConvertInteger(object value) => Convert.ToInt32(value, CultureInfo.InvariantCulture);

        public static string InterpolateObject(object value) => $"{value}";
    }

    /// <summary>Reported by neither: bit access, comparison and integer or string work.</summary>
    internal static class Clean
    {
        public static double Bits(double value) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) ^ 1);

        public static float SingleBits(float value) => BitConverter.ToSingle(BitConverter.GetBytes(value), 0);

        public static int[] DecimalBits(decimal value) => decimal.GetBits(value).Concat(decimal.GetBits(new decimal(1, 2, 3, false, 4))).ToArray();

        public static bool Compare(double left, double right) => double.IsFinite(left) && left.Equals(right) && left.CompareTo(right) == 0;

        public static decimal Held(decimal? value) => value.HasValue ? value.GetValueOrDefault() : 0m;

        public static string Integers(long value) => value.ToString(CultureInfo.InvariantCulture) + long.Parse("42", CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

        public static string Strings(string left, string right) => string.Concat(left, right) + string.Join(",", new[] { left, right });
    }
}
