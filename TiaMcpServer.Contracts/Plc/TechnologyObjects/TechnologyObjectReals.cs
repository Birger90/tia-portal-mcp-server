using System.Globalization;
using System.Numerics;

namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>
/// Runtime-independent conversion between invariant number text and <see cref="float"/>, <see cref="double"/> and
/// <see cref="decimal"/>. .NET Framework (the worker) and .NET 10 (the host) parse and format reals differently (the
/// Framework parser is not correctly rounded, parses <c>float</c> through <c>double</c> and loses the sign of <c>-0</c>;
/// its G16/G8 and R formatting differ from .NET Core's shortest round-trip), so this class never calls the runtime's
/// real parsers or formatters: every step is exact integer arithmetic on <see cref="BigInteger"/> plus the IEEE 754 bit
/// layout (<see cref="BitConverter"/>). Text is parsed exactly and rounded to nearest, ties to even (IEEE 754
/// round-to-nearest; overflow only at or beyond the largest finite value plus half an ulp; subnormals and the sign of
/// zero kept). <see cref="Shortest(double)"/> is the shortest digit string that parses back to the same bits (among
/// equally short ones the closest to the exact value), laid out like .NET 10's R (round-trip) format; it is computed by
/// an exact search, never by a runtime formatter, and equals .NET 10's R text except where that text does not round-trip
/// (the doubles 2^-25 and 2^-958), where this one still does. The range of an exponent is decided before any power of ten
/// is formed (comparisons that cannot wrap around, whatever the <see cref="long"/> exponent), so every power of ten is at
/// most 10^(digit count + a format constant) and no input overflows, loops or throws.
/// </summary>
public static class TechnologyObjectReals
{
    private sealed class Format
    {
        public Format(int mantissaBits, int minExponent, int maxExponent, int precision, int decimalLimit)
        {
            MantissaBits = mantissaBits;
            MinExponent = minExponent;
            MaxExponent = maxExponent;
            Precision = precision;
            DecimalLimit = decimalLimit;
        }

        /// <summary>Significand bits including the implicit one (53 or 24).</summary>
        public int MantissaBits { get; }

        /// <summary>Smallest unbiased exponent of a normal number (-1022 or -126).</summary>
        public int MinExponent { get; }

        /// <summary>Largest unbiased exponent (1023 or 127).</summary>
        public int MaxExponent { get; }

        /// <summary>The digit count below which .NET 10's shortest text never switches to scientific layout (17 or 9, the round-trip digits).</summary>
        public int Precision { get; }

        /// <summary>A decimal exponent bound beyond which a value certainly overflows or rounds to zero.</summary>
        public int DecimalLimit { get; }

        public int Bias => MaxExponent;

        public int MinBinaryExponent => MinExponent - (MantissaBits - 1);
    }

    private static readonly Format DoubleFormat = new(53, -1022, 1023, 17, 400);
    private static readonly Format SingleFormat = new(24, -126, 127, 9, 60);

    /// <summary>
    /// An invariant number text: optional sign, digits with an optional decimal point, optional exponent; no blanks,
    /// group separators, NaN or infinity. On success the value is <c>(negative ? -1 : 1) × digits × 10^exponent</c> with
    /// no trailing zeros in <paramref name="digits"/> (zero is digits 0, exponent 0, the sign kept).
    /// </summary>
    public static bool TryParseText(string? text, out bool negative, out BigInteger digits, out long exponent)
    {
        negative = false;
        digits = BigInteger.Zero;
        exponent = 0;
        if (string.IsNullOrEmpty(text)) return false;
        var s = text!;
        var i = 0;
        if (s[0] is '+' or '-')
        {
            negative = s[0] == '-';
            i = 1;
        }

        var integerDigits = 0;
        var fractionDigits = 0;
        // The significand's digits in a char buffer (no StringBuilder: its ToString is a virtual Object.ToString call,
        // which the IL scan refuses in this class so that no boxed real can ever be formatted here).
        var significand = new char[s.Length];
        var count = 0;
        while (i < s.Length && s[i] is >= '0' and <= '9')
        {
            significand[count++] = s[i++];
            integerDigits++;
        }

        if (i < s.Length && s[i] == '.')
        {
            i++;
            while (i < s.Length && s[i] is >= '0' and <= '9')
            {
                significand[count++] = s[i++];
                fractionDigits++;
            }
        }

        if (integerDigits + fractionDigits == 0) return false;
        long exponentValue = 0;
        if (i < s.Length && s[i] is 'e' or 'E')
        {
            i++;
            var exponentNegative = false;
            if (i < s.Length && s[i] is '+' or '-')
            {
                exponentNegative = s[i] == '-';
                i++;
            }

            var exponentDigits = 0;
            while (i < s.Length && s[i] is >= '0' and <= '9')
            {
                // Saturates far beyond any representable magnitude; the value then overflows or rounds to zero.
                if (exponentValue < 1_000_000_000_000L) exponentValue = exponentValue * 10 + (s[i] - '0');
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0) return false;
            if (exponentNegative) exponentValue = -exponentValue;
        }

        if (i != s.Length) return false;
        var all = new string(significand, 0, count).TrimStart('0');
        if (all.Length == 0) return true;
        var trimmed = all.TrimEnd('0');
        digits = BigInteger.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
        exponent = exponentValue - fractionDigits + (all.Length - trimmed.Length);
        return true;
    }

    /// <summary>The correctly rounded <see cref="double"/> of a parsed text; false on overflow.</summary>
    public static bool TryToDouble(bool negative, BigInteger digits, long exponent, out double value)
    {
        value = 0;
        if (!TryRound(digits, exponent, DoubleFormat, out var mantissa, out var binaryExponent)) return false;
        value = BitConverter.Int64BitsToDouble(Bits(negative, mantissa, binaryExponent, DoubleFormat));
        return true;
    }

    /// <summary>The correctly rounded <see cref="float"/> of a parsed text (rounded once, never through <c>double</c>); false on overflow.</summary>
    public static bool TryToSingle(bool negative, BigInteger digits, long exponent, out float value)
    {
        value = 0;
        if (!TryRound(digits, exponent, SingleFormat, out var mantissa, out var binaryExponent)) return false;
        value = BitConverter.ToSingle(BitConverter.GetBytes(unchecked((int)Bits(negative, mantissa, binaryExponent, SingleFormat))), 0);
        return true;
    }

    /// <summary>The shortest text that parses back to exactly <paramref name="value"/>, in .NET Core's R layout.</summary>
    public static string Shortest(double value) => Shortest(BitConverter.DoubleToInt64Bits(value), DoubleFormat);

    /// <inheritdoc cref="Shortest(double)"/>
    public static string Shortest(float value) => Shortest(SingleBits(value), SingleFormat);

    /// <summary>The exact decimal value of <paramref name="value"/> (every binary fraction has a finite decimal expansion).</summary>
    public static string Exact(double value) => ExactText(BitConverter.DoubleToInt64Bits(value), DoubleFormat);

    /// <inheritdoc cref="Exact(double)"/>
    public static string Exact(float value) => ExactText(SingleBits(value), SingleFormat);

    /// <summary>True when the two values have the same IEEE 754 bits (so <c>-0</c> differs from <c>0</c>).</summary>
    public static bool SameBits(double left, double right) => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);

    /// <inheritdoc cref="SameBits(double, double)"/>
    public static bool SameBits(float left, float right) => SingleBits(left) == SingleBits(right);

    /// <summary>True when the parsed text and the number text <paramref name="other"/> denote the same number (a zero's sign ignored).</summary>
    public static bool SameNumber(bool negative, BigInteger digits, long exponent, string other)
        => TryParseText(other, out var otherNegative, out var otherDigits, out var otherExponent)
            && digits == otherDigits && (digits.IsZero || (negative == otherNegative && exponent == otherExponent));

    /// <summary>
    /// The exact <see cref="decimal"/> of a parsed text, or false with <paramref name="nearest"/> set to the nearest
    /// representable decimal (over every scale 0..28 the grid points just below and above the value whose significand
    /// fits 96 bits; the closest wins, a tie at one scale goes to the even significand), or null when the magnitude is at
    /// least 2^96 (beyond <see cref="decimal.MaxValue"/> plus one). A value below half of 10^-28 has the nearest 0. Never
    /// throws and never overflows, whatever <paramref name="exponent"/> is: the range is decided before any power of ten
    /// is computed, and every power of ten is then at most 10^(digit count + 57).
    /// </summary>
    public static bool TryToDecimalExactly(bool negative, BigInteger digits, long exponent, out decimal value, out decimal? nearest)
    {
        value = 0m;
        nearest = null;
        if (digits.IsZero) return true;
        var limit = BigInteger.One << 96;
        var length = DigitCount(digits);
        // Compared without adding to the exponent, so long.MinValue/MaxValue cannot wrap around.
        // At least 10^29 > decimal.MaxValue: out of range. At most 10^-29 < half the resolution 10^-28: the nearest is 0.
        if (exponent > 29L - length) return false;
        if (exponent <= -29L - length)
        {
            nearest = 0m;
            return false;
        }

        // From here -29 - length < exponent <= 29 - length, so the exponent fits an int (length is an int digit count).
        var power = checked((int)exponent);
        if (power >= 0)
        {
            var whole = digits * BigInteger.Pow(10, power);
            if (whole < limit)
            {
                value = Decimal(negative, whole, 0);
                return true;
            }
        }
        else if (-power <= 28 && digits < limit)
        {
            value = Decimal(negative, digits, -power);
            return true;
        }

        var common = Math.Max(-power, 28);
        var scaled = digits * BigInteger.Pow(10, power + common);
        BigInteger? best = null;
        var bestScale = 0;
        var bestDistance = BigInteger.Zero;
        for (var scale = 28; scale >= 0; scale--)
        {
            var unit = BigInteger.Pow(10, common - scale);
            var floor = BigInteger.DivRem(scaled, unit, out var remainder);
            foreach (var candidate in remainder.IsZero ? new[] { floor } : new[] { floor, floor + 1 })
            {
                if (candidate >= limit) continue;
                var distance = BigInteger.Abs(scaled - candidate * unit);
                var closer = best is null || distance < bestDistance
                    || (distance == bestDistance && scale == bestScale && candidate.IsEven && !best.Value.IsEven);
                if (!closer) continue;
                (best, bestScale, bestDistance) = (candidate, scale, distance);
            }
        }

        if (best is not { } significand) return false;
        if (bestDistance.IsZero)
        {
            // Only reachable from a caller whose digits carry trailing zeros (TryParseText strips them): the value is exact.
            value = Decimal(negative, significand, bestScale);
            return true;
        }

        var minimal = bestScale;
        while (minimal > 0 && !significand.IsZero && (significand % 10).IsZero)
        {
            significand /= 10;
            minimal--;
        }

        nearest = Decimal(negative && !significand.IsZero, significand, significand.IsZero ? 0 : minimal);
        return false;
    }

    /// <summary>A decimal's significand and scale as invariant text (trailing zeros of the scale kept, a zero unsigned).</summary>
    public static string Render(decimal value)
    {
        var bits = decimal.GetBits(value);
        var magnitude = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | new BigInteger((uint)bits[0]);
        var scale = (bits[3] >> 16) & 0xFF;
        var negative = (bits[3] & int.MinValue) != 0 && !magnitude.IsZero;
        var text = magnitude.ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        if (scale > 0) text = text.Substring(0, text.Length - scale) + "." + text.Substring(text.Length - scale);
        return (negative ? "-" : string.Empty) + text;
    }

    private static decimal Decimal(bool negative, BigInteger magnitude, int scale)
    {
        var mask = new BigInteger(uint.MaxValue);
        return new decimal(unchecked((int)(uint)(magnitude & mask)), unchecked((int)(uint)((magnitude >> 32) & mask)),
            unchecked((int)(uint)((magnitude >> 64) & mask)), negative, (byte)scale);
    }

    private static long SingleBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0) & 0xFFFFFFFFL;

    /// <summary>Rounds digits × 10^exponent to significand × 2^binaryExponent, to nearest, ties to even; false on overflow.</summary>
    private static bool TryRound(BigInteger digits, long exponent, Format format, out BigInteger mantissa, out int binaryExponent)
    {
        mantissa = BigInteger.Zero;
        binaryExponent = format.MinBinaryExponent;
        if (digits.IsZero) return true;
        var length = DigitCount(digits);
        // Compared without adding to the exponent, so long.MinValue/MaxValue cannot wrap around.
        if (exponent > (long)format.DecimalLimit - (length - 1)) return false;
        if (exponent < -(long)format.DecimalLimit - length) return true;
        BigInteger numerator, denominator;
        // Bounded: -(length + DecimalLimit) <= exponent <= DecimalLimit + 1 - length, so the casts cannot overflow and
        // every power of ten is at most 10^(length + DecimalLimit).
        if (exponent >= 0)
        {
            numerator = digits * BigInteger.Pow(10, checked((int)exponent));
            denominator = BigInteger.One;
        }
        else
        {
            numerator = digits;
            denominator = BigInteger.Pow(10, checked((int)-exponent));
        }

        // floor(log2(numerator / denominator)) is the bit-length difference or one less.
        var log2 = BitLength(numerator) - BitLength(denominator);
        if (!AtLeastPowerOfTwo(numerator, denominator, log2)) log2--;
        var shift = Math.Max(log2 - (format.MantissaBits - 1), format.MinBinaryExponent);
        var scaledNumerator = shift < 0 ? numerator << -shift : numerator;
        var scaledDenominator = shift > 0 ? denominator << shift : denominator;
        mantissa = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
        var twice = remainder << 1;
        var comparison = twice.CompareTo(scaledDenominator);
        if (comparison > 0 || (comparison == 0 && !mantissa.IsEven)) mantissa += 1;
        if (mantissa == BigInteger.One << format.MantissaBits)
        {
            mantissa >>= 1;
            shift++;
        }

        binaryExponent = shift;
        return mantissa.IsZero || BitLength(mantissa) - 1 + shift <= format.MaxExponent;
    }

    private static long Bits(bool negative, BigInteger mantissa, int binaryExponent, Format format)
    {
        var sign = negative ? 1L << (format.MantissaBits - 1 + (format.MantissaBits == 53 ? 11 : 8)) : 0L;
        var fractionBits = format.MantissaBits - 1;
        if (mantissa < BigInteger.One << fractionBits) return sign | (long)mantissa;
        var field = (long)(binaryExponent + fractionBits + format.Bias);
        return sign | (field << fractionBits) | (long)(mantissa - (BigInteger.One << fractionBits));
    }

    /// <summary>(negative, significand, binary exponent) of finite bits; false for infinity and NaN.</summary>
    private static bool Decompose(long bits, Format format, out bool negative, out BigInteger mantissa, out int binaryExponent)
    {
        var fractionBits = format.MantissaBits - 1;
        var exponentBits = format.MantissaBits == 53 ? 11 : 8;
        negative = ((bits >> (fractionBits + exponentBits)) & 1) == 1;
        var field = (int)((bits >> fractionBits) & ((1L << exponentBits) - 1));
        var fraction = bits & ((1L << fractionBits) - 1);
        mantissa = BigInteger.Zero;
        binaryExponent = format.MinBinaryExponent;
        if (field == (1 << exponentBits) - 1) return false;
        if (field == 0)
        {
            mantissa = fraction;
            return true;
        }

        mantissa = fraction | (1L << fractionBits);
        binaryExponent = field - format.Bias - fractionBits;
        return true;
    }

    /// <summary>The exact decimal digits and exponent of significand × 2^binaryExponent, without trailing zeros.</summary>
    private static (BigInteger Digits, long Exponent) ExactDigits(BigInteger mantissa, int binaryExponent)
    {
        if (mantissa.IsZero) return (BigInteger.Zero, 0);
        var (digits, exponent) = binaryExponent >= 0
            ? (mantissa << binaryExponent, 0L)
            : (mantissa * BigInteger.Pow(5, -binaryExponent), (long)binaryExponent);
        while (!digits.IsZero && (digits % 10).IsZero)
        {
            digits /= 10;
            exponent++;
        }

        return (digits, exponent);
    }

    private static string ExactText(long bits, Format format)
    {
        if (!Decompose(bits, format, out var negative, out var mantissa, out var binaryExponent)) return NonFinite(bits, format);
        var (digits, exponent) = ExactDigits(mantissa, binaryExponent);
        return Layout(negative, digits, exponent, format.Precision);
    }

    private static string Shortest(long bits, Format format)
    {
        if (!Decompose(bits, format, out var negative, out var mantissa, out var binaryExponent)) return NonFinite(bits, format);
        if (mantissa.IsZero) return negative ? "-0" : "0";
        var (exact, exactExponent) = ExactDigits(mantissa, binaryExponent);
        var length = DigitCount(exact);

        bool RoundTrips(BigInteger candidate, long candidateExponent)
            => TryRound(candidate, candidateExponent, format, out var m, out var e) && m == mantissa && (m.IsZero || e == binaryExponent);

        (BigInteger Digits, long Exponent)? Candidate(int count)
        {
            if (count >= length) return (exact, exactExponent);
            var divisor = BigInteger.Pow(10, length - count);
            var floor = BigInteger.DivRem(exact, divisor, out var remainder);
            var exponent = exactExponent + (length - count);
            var floorFits = RoundTrips(floor, exponent);
            var ceilingFits = RoundTrips(floor + 1, exponent);
            if (floorFits && ceilingFits)
            {
                var comparison = (remainder << 1).CompareTo(divisor);
                return comparison < 0 || (comparison == 0 && floor.IsEven) ? (floor, exponent) : (floor + 1, exponent);
            }

            return floorFits ? (floor, exponent) : ceilingFits ? (floor + 1, exponent) : null;
        }

        // Fitting is monotone in the digit count (a fitting string padded with a zero still fits), so search the smallest.
        var low = 1;
        var high = Math.Min(length, format.MantissaBits == 53 ? 17 : 9);
        var best = Candidate(high) ?? (exact, exactExponent);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Candidate(middle) is { } fits)
            {
                best = fits;
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        var (digits, digitsExponent) = Normalize(best.Digits, best.Exponent);
        return Layout(negative, digits, digitsExponent, format.Precision);
    }

    private static (BigInteger Digits, long Exponent) Normalize(BigInteger digits, long exponent)
    {
        while (!digits.IsZero && (digits % 10).IsZero)
        {
            digits /= 10;
            exponent++;
        }

        return (digits, exponent);
    }

    /// <summary>
    /// .NET Core's R layout (Number.Formatting FormatGeneral with at least <paramref name="precision"/> digits): plain
    /// decimals unless the leading digit's power of ten is below -4 or at least max(digit count, precision); then
    /// <c>d.dddE+XX</c> with a signed exponent of at least two digits.
    /// </summary>
    private static string Layout(bool negative, BigInteger digits, long exponent, int precision)
    {
        if (digits.IsZero) return negative ? "-0" : "0";
        var text = digits.ToString(CultureInfo.InvariantCulture);
        var leading = text.Length - 1 + exponent;
        string body;
        if (leading < -4 || leading >= Math.Max(text.Length, precision))
            body = text.Substring(0, 1) + (text.Length > 1 ? "." + text.Substring(1) : string.Empty)
                + "E" + (leading < 0 ? "-" : "+") + Math.Abs(leading).ToString("00", CultureInfo.InvariantCulture);
        else if (exponent >= 0)
            body = text + new string('0', checked((int)exponent));
        else
        {
            var point = checked(text.Length + (int)exponent);
            body = point > 0 ? text.Substring(0, point) + "." + text.Substring(point) : "0." + new string('0', -point) + text;
        }

        return (negative ? "-" : string.Empty) + body;
    }

    private static string NonFinite(long bits, Format format)
    {
        var fractionBits = format.MantissaBits - 1;
        if ((bits & ((1L << fractionBits) - 1)) != 0) return "NaN";
        var exponentBits = format.MantissaBits == 53 ? 11 : 8;
        return ((bits >> (fractionBits + exponentBits)) & 1) == 1 ? "-Infinity" : "Infinity";
    }

    private static bool AtLeastPowerOfTwo(BigInteger numerator, BigInteger denominator, int power)
        => power >= 0 ? numerator >= denominator << power : numerator << -power >= denominator;

    /// <summary>
    /// The decimal digit count: an estimate from the bit length in integer arithmetic only (30103/100000 approximates
    /// log10(2)), corrected in either direction by power-of-ten comparisons (one or two for any realistic size); no text
    /// conversion of the number.
    /// </summary>
    private static int DigitCount(BigInteger value)
    {
        if (value.IsZero) return 1;
        var magnitude = BigInteger.Abs(value);
        var estimate = (int)((BitLength(magnitude) - 1L) * 30103L / 100000L) + 1;
        while (magnitude >= BigInteger.Pow(10, estimate)) estimate++;
        while (estimate > 1 && magnitude < BigInteger.Pow(10, estimate - 1)) estimate--;
        return estimate;
    }

    private static int BitLength(BigInteger value)
    {
        var bytes = BigInteger.Abs(value).ToByteArray();
        var top = bytes.Length - 1;
        while (top > 0 && bytes[top] == 0) top--;
        var bits = top * 8;
        for (var b = bytes[top]; b != 0; b >>= 1) bits++;
        return bits;
    }
}
