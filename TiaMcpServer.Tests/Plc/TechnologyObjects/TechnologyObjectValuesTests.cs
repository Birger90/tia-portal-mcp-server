using System.Text.RegularExpressions;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using Xunit;

namespace TiaMcpServer.Tests.Plc.TechnologyObjects;

public sealed class TechnologyObjectValuesTests
{
    private enum Mode { Off, Jog, Auto }

    [Flags]
    private enum Sides : byte { None = 0, Left = 1, Right = 2, Top = 128 }

    public static TheoryData<object?, string, string?> Described => new()
    {
        { null, "null", null },
        { true, "boolean", "true" },
        { (byte)7, "integer", "7" },
        { -42L, "integer", "-42" },
        { ulong.MaxValue, "integer", "18446744073709551615" },
        { 0.1, "real", "0.1" },
        { 1.5f, "real", "1.5" },
        { 0.1f, "real", "0.1" },
        { 0.1 + 0.2, "real", "0.30000000000000004" },
        { 16777216f, "real", "16777216" },
        { double.Epsilon, "real", "5E-324" },
        { 3.25m, "real", "3.25" },
        { "mm/s", "string", "mm/s" },
        { Mode.Jog, "enum", "Jog" },
        { new object(), "object", null },
    };

    [Theory]
    [MemberData(nameof(Described))]
    public void DescribeTypesAValueByItsRuntimeType(object? value, string kind, string? text)
    {
        var (actualKind, actualText) = TechnologyObjectValues.Describe(value);
        Assert.Equal(kind, actualKind);
        Assert.Equal(text, actualText);
    }

    [Theory]
    [InlineData(typeof(bool), "TRUE", true)]
    [InlineData(typeof(bool), "false", false)]
    [InlineData(typeof(int), "-12", -12)]
    [InlineData(typeof(ushort), "65535", (ushort)65535)]
    [InlineData(typeof(double), "1e3", 1000.0)]
    [InlineData(typeof(double), "-0.125", -0.125)]
    [InlineData(typeof(float), "2.5", 2.5f)]
    [InlineData(typeof(string), "", "")]
    [InlineData(typeof(string), "  keep blanks ", "  keep blanks ")]
    public void TryConvertProducesExactlyTheTargetType(Type target, string text, object expected)
    {
        Assert.True(TechnologyObjectValues.TryConvert(text, target, out var value, out var error), error);
        Assert.Equal(expected, value);
        Assert.Equal(target, value!.GetType());
    }

    [Fact]
    public void EnumsAndDecimalsConvertByExactNameAndInvariantText()
    {
        Assert.True(TechnologyObjectValues.TryConvert("Auto", typeof(Mode), out var mode, out _));
        Assert.Equal(Mode.Auto, mode);
        Assert.False(TechnologyObjectValues.TryConvert("auto", typeof(Mode), out _, out var error));
        Assert.Contains("Off, Jog, Auto", error);
        Assert.False(TechnologyObjectValues.TryConvert("1", typeof(Mode), out _, out _));
        Assert.True(TechnologyObjectValues.TryConvert("12.75", typeof(decimal), out var amount, out _));
        Assert.Equal(12.75m, amount);
    }

    [Theory]
    [InlineData(typeof(bool), "1")]
    [InlineData(typeof(bool), "yes")]
    [InlineData(typeof(int), "1.5")]
    [InlineData(typeof(int), "2147483648")]
    [InlineData(typeof(byte), "-1")]
    [InlineData(typeof(ushort), "70000")]
    [InlineData(typeof(int), " 1")]
    [InlineData(typeof(int), "1,000")]
    [InlineData(typeof(double), "1,5")]
    [InlineData(typeof(double), "NaN")]
    [InlineData(typeof(double), "Infinity")]
    [InlineData(typeof(double), "1e400")]
    [InlineData(typeof(float), "1e39")]
    [InlineData(typeof(DateTime), "2026-01-01")]
    [InlineData(typeof(object), "x")]
    public void TryConvertRefusesTextThatDoesNotFitTheTypeExactly(Type target, string text)
    {
        Assert.False(TechnologyObjectValues.TryConvert(text, target, out var value, out var error));
        Assert.Null(value);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void NullTextIsRefusedAndEquivalenceIsTypeExact()
    {
        Assert.False(TechnologyObjectValues.TryConvert(null, typeof(string), out _, out _));
        Assert.True(TechnologyObjectValues.Equivalent(1.5, 1.5));
        Assert.False(TechnologyObjectValues.Equivalent(1.5, 1.5f));
        Assert.False(TechnologyObjectValues.Equivalent(1, 1L));
        Assert.True(TechnologyObjectValues.Equivalent(null, null));
        Assert.False(TechnologyObjectValues.Equivalent(true, null));
        Assert.Equal(new[] { "Off", "Jog", "Auto" }, TechnologyObjectValues.EnumMembers(typeof(Mode)));
        Assert.Empty(TechnologyObjectValues.EnumMembers(typeof(int)));
    }

    [Theory]
    [InlineData(typeof(float), "16777217", "16777216")]
    [InlineData(typeof(float), "1e-50", "0")]
    [InlineData(typeof(float), "1.5e-45", "1E-45")]
    [InlineData(typeof(float), "0.10000000149011612", "0.1")]
    [InlineData(typeof(double), "1e-400", "0")]
    [InlineData(typeof(double), "0.3000000000000000444", "0.30000000000000004")]
    [InlineData(typeof(double), "9007199254740993", "9007199254740992")]
    [InlineData(typeof(decimal), "1e-40", "0")]
    [InlineData(typeof(decimal), "0.12345678901234567890123456789", "0.1234567890123456789012345679")]
    [InlineData(typeof(float), "3.40282356e38", "3.4028235E+38")]
    [InlineData(typeof(double), "8.5639197455136085", "8.563919745513608")]
    public void PrecisionLossIsRefusedNamingTheRepresentableValue(Type target, string text, string representable)
    {
        Assert.False(TechnologyObjectValues.TryConvert(text, target, out var value, out var error));
        Assert.Null(value);
        Assert.Contains($"the nearest representable value is {representable}", error);
    }

    [Theory]
    [InlineData(typeof(float), "0.1")]
    [InlineData(typeof(float), "16777216")]
    [InlineData(typeof(float), "-0")]
    [InlineData(typeof(float), "1E-45")]
    [InlineData(typeof(double), "0.1")]
    [InlineData(typeof(double), "0.30000000000000004")]
    [InlineData(typeof(double), "5e-324")]
    [InlineData(typeof(double), "1.0")]
    [InlineData(typeof(double), "+250.50")]
    [InlineData(typeof(decimal), "1.50")]
    [InlineData(typeof(decimal), "-0.000001")]
    [InlineData(typeof(decimal), "79228162514264337593543950335")]
    [InlineData(typeof(float), "3.4028235E+38")]
    [InlineData(typeof(float), "-3.4028235e38")]
    [InlineData(typeof(float), "340282346638528859811704183484516925440")]
    [InlineData(typeof(float), "1.2379401E+27")]
    [InlineData(typeof(float), "0.100000001490116119384765625")]
    [InlineData(typeof(double), "1.7976931348623157E+308")]
    [InlineData(typeof(double), "-5E-324")]
    [InlineData(typeof(double), "8.563919745513608")]
    [InlineData(typeof(double), "2.2250738585072014E-308")]
    public void NumbersTheTypeHoldsExactlyAreAccepted(Type target, string text)
        => Assert.True(TechnologyObjectValues.TryConvert(text, target, out _, out var error), error);

    [Fact]
    public void FlagsEnumsAcceptCommaSeparatedExactMemberNames()
    {
        Assert.True(TechnologyObjectValues.IsFlags(typeof(Sides)));
        Assert.False(TechnologyObjectValues.IsFlags(typeof(Mode)));
        Assert.True(TechnologyObjectValues.TryConvert("Left, Right", typeof(Sides), out var both, out var error), error);
        Assert.Equal(Sides.Left | Sides.Right, both);
        Assert.True(TechnologyObjectValues.TryConvert("Right,Top", typeof(Sides), out var high, out _));
        Assert.Equal(Sides.Right | Sides.Top, high);
        Assert.True(TechnologyObjectValues.TryConvert("None", typeof(Sides), out var none, out _));
        Assert.Equal(Sides.None, none);
        Assert.False(TechnologyObjectValues.TryConvert("Left, left", typeof(Sides), out _, out _));
        Assert.False(TechnologyObjectValues.TryConvert("Left, Left", typeof(Sides), out _, out var duplicate));
        Assert.Contains("distinct", duplicate);
        Assert.False(TechnologyObjectValues.TryConvert("None, Left", typeof(Sides), out _, out var zero));
        Assert.Contains("zero member 'None'", zero);
        Assert.False(TechnologyObjectValues.TryConvert("Left,,Right", typeof(Sides), out _, out _));
        Assert.False(TechnologyObjectValues.TryConvert("3", typeof(Sides), out _, out _));
        Assert.False(TechnologyObjectValues.TryConvert("Off, Jog", typeof(Mode), out _, out var single));
        Assert.Contains("an exact member name", single);
        // The read-back of a combined value equals the written one exactly.
        Assert.True(TechnologyObjectValues.Equivalent(both, Sides.Left | Sides.Right));
        Assert.Equal("Left, Right", TechnologyObjectValues.Describe(both).Text);
    }

    [Fact]
    public void EnumRefusalDefersToTheWorkerWhenTheMemberListIsTruncated()
    {
        var listed = new[] { "A", "B" };
        Assert.Null(TechnologyObjectValues.EnumRefusal("B", listed, flags: false, truncated: false, "T"));
        Assert.Contains("is not a member", TechnologyObjectValues.EnumRefusal("C", listed, flags: false, truncated: false, "T"));
        Assert.Null(TechnologyObjectValues.EnumRefusal("C", listed, flags: false, truncated: true, "T"));
        Assert.Null(TechnologyObjectValues.EnumRefusal("A, B", listed, flags: true, truncated: false, "T"));
        Assert.Contains("'C'", TechnologyObjectValues.EnumRefusal("A, C", listed, flags: true, truncated: false, "T"));
        Assert.Contains("an exact member name", TechnologyObjectValues.EnumRefusal("A, B", listed, flags: false, truncated: true, "T"));

        var builder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("ManyEnumTest"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
            .DefineDynamicModule("ManyEnumTest").DefineEnum("ManyEnumTest.Many", System.Reflection.TypeAttributes.Public, typeof(int));
        for (var i = 0; i <= TechnologyObjectPolicy.MaxEnumMembers; i++) builder.DefineLiteral("M" + i, i);
        var many = builder.CreateType();
        Assert.Equal(TechnologyObjectPolicy.MaxEnumMembers, TechnologyObjectValues.EnumMembers(many).Count);
        Assert.True(TechnologyObjectValues.EnumMembersTruncated(many));
        Assert.False(TechnologyObjectValues.EnumMembersTruncated(typeof(Mode)));
        // The worker converts an unlisted member against the live type.
        Assert.True(TechnologyObjectValues.TryConvert("M" + TechnologyObjectPolicy.MaxEnumMembers, many, out _, out var error), error);
    }

    [Theory]
    [InlineData(typeof(float), "16777217", "'16777217' is neither the shortest round-trip text nor the exact decimal value of the nearest System.Single; "
        + "the nearest representable value is 16777216 (its shortest round-trip text). Write that text instead")]
    [InlineData(typeof(double), "0.3000000000000000444", "'0.3000000000000000444' is neither the shortest round-trip text nor the exact decimal value of the nearest System.Double; "
        + "the nearest representable value is 0.30000000000000004 (its shortest round-trip text). Write that text instead")]
    [InlineData(typeof(decimal), "1e-40", "'1e-40' cannot be held exactly by System.Decimal (a 96-bit significand with at most 28 decimal places); "
        + "the nearest representable value is 0. Write that text instead")]
    [InlineData(typeof(decimal), "1e29", "'1e29' is outside the range of System.Decimal (largest magnitude 79228162514264337593543950335)")]
    public void RealRefusalsNameTheShortestTextOfTheNearestValueInOneWording(Type target, string text, string message)
    {
        Assert.False(TechnologyObjectValues.TryConvert(text, target, out _, out var error));
        Assert.Equal(message, error);
        Assert.Equal($"Parameter 'P' of 'Axis_1': {message}. Nothing was changed.", TechnologyObjectValues.ParameterRefusal("P", "Axis_1", error));
    }

    [Flags]
    private enum Signed : short { None = 0, A = 1, B = 2, Low = 3, High = -32768 }

    /// <summary>Enum text never comes from the runtime's enum formatting, whose integer fallback follows the current culture.</summary>
    [Fact]
    public void EnumTextIsTheNameTheFlagsCombinationOrTheInvariantInteger()
    {
        Assert.Equal("Jog", TechnologyObjectValues.Describe(Mode.Jog).Text);
        Assert.Equal("7", TechnologyObjectValues.Describe((Mode)7).Text);
        Assert.Equal("Left, Right, Top", TechnologyObjectValues.Describe(Sides.Left | Sides.Right | Sides.Top).Text);
        // Bit 4 is no member: the combination cannot be named, so the integer is shown (like Enum.ToString).
        Assert.Equal("7", TechnologyObjectValues.Describe((Sides)7).Text);
        Assert.Equal("None", TechnologyObjectValues.Describe(Sides.None).Text);
        // A composite member is taken whole (from the largest value down), then the rest.
        Assert.Equal("Low", TechnologyObjectValues.Describe(Signed.A | Signed.B).Text);
        Assert.Equal("Low, High", TechnologyObjectValues.Describe(Signed.Low | Signed.High).Text);
        Assert.Equal((Signed.Low | Signed.High).ToString(), TechnologyObjectValues.Describe(Signed.Low | Signed.High).Text);
        Assert.Equal((Sides.Left | Sides.Top).ToString(), TechnologyObjectValues.Describe(Sides.Left | Sides.Top).Text);

        var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "~";
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            Assert.Equal("~5", ((Mode)(-5)).ToString());
            Assert.Equal("-5", TechnologyObjectValues.Describe((Mode)(-5)).Text);
            Assert.Equal("-32763", TechnologyObjectValues.Describe((Signed)(-32763)).Text);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>A long [Flags] combination is cut at the text bound with the marker, in the value and in every refusal.</summary>
    [Fact]
    public void LongFlagsTextIsCutWithTheMarkerEverywhere()
    {
        var builder = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("LongFlagsTest"), System.Reflection.Emit.AssemblyBuilderAccess.Run)
            .DefineDynamicModule("LongFlagsTest").DefineEnum("LongFlagsTest.Wide", System.Reflection.TypeAttributes.Public, typeof(long));
        builder.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(typeof(FlagsAttribute).GetConstructor(Type.EmptyTypes)!, Array.Empty<object>()));
        var names = Enumerable.Range(0, 64).Select(i => "Member" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + new string('x', 90)).ToList();
        for (var i = 0; i < names.Count; i++) builder.DefineLiteral(names[i], i == 63 ? long.MinValue : 1L << i);
        var wide = builder.CreateType();
        var all = Enum.ToObject(wide, -1L);
        var bound = TechnologyObjectPolicy.MaxTextChars + TechnologyObjectPolicy.TruncationMarker.Length;

        var text = TechnologyObjectValues.Describe(all).Text!;
        Assert.Equal(bound, text.Length);
        Assert.EndsWith(TechnologyObjectPolicy.TruncationMarker, text);
        Assert.StartsWith(names[0] + ", " + names[1] + ", ", text);

        // Refusals: a text of many unknown names, the full member list, the zero member and a malformed list.
        var unknown = string.Join(",", Enumerable.Range(0, 800).Select(i => "u" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.True(unknown.Length <= TechnologyObjectPolicy.MaxTextChars);
        Assert.False(TechnologyObjectValues.TryConvert(unknown, wide, out _, out var worker));
        var host = TechnologyObjectValues.EnumRefusal(unknown, names, flags: true, truncated: false, wide.FullName)!;
        Assert.Equal(worker, host);
        foreach (var message in new[] { worker, host })
        {
            Assert.True(message.Length < 2 * bound + 200, $"{message.Length} characters");
            Assert.Equal(2, Regex.Matches(message, Regex.Escape(TechnologyObjectPolicy.TruncationMarker)).Count);
        }

        var malformed = string.Join(", ", names.Take(30)) + ",";
        Assert.False(TechnologyObjectValues.TryConvert(malformed, wide, out _, out var shape));
        Assert.True(shape.Length < 2 * bound + 200, $"{shape.Length} characters");
        Assert.Contains(TechnologyObjectPolicy.TruncationMarker, shape);
    }

    [Fact]
    public void TheSignOfZeroIsKeptOrRefusedButNeverDropped()
    {
        Assert.True(TechnologyObjectValues.TryConvert("-0", typeof(double), out var negative, out _));
        Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits((double)negative!));
        Assert.True(TechnologyObjectValues.TryConvert("-0.0", typeof(float), out var single, out _));
        Assert.Equal(BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits((float)single!));
        Assert.Equal("-0", TechnologyObjectValues.Describe(-0.0).Text);
        // A read-back of +0 after writing -0 is not the written value.
        Assert.False(TechnologyObjectValues.Equivalent(-0.0, 0.0));
        Assert.False(TechnologyObjectValues.TryConvert("-0", typeof(decimal), out _, out var ambiguous));
        Assert.Contains("ambiguous", ambiguous);
    }

    [Theory]
    [InlineData(typeof(float), "3.4028236e38")]
    [InlineData(typeof(double), "1.7976931348623159e308")]
    [InlineData(typeof(decimal), "79228162514264337593543950336")]
    public void ValuesBeyondTheLargestFiniteValueAreOutOfRange(Type target, string text)
    {
        Assert.False(TechnologyObjectValues.TryConvert(text, target, out _, out var error));
        Assert.Contains("outside the range", error);
    }

    [Fact]
    public void ThePowerOfTwoBoundaryUsesTheShortestTextFromAbove()
    {
        // 2^90 as a float: the shortest round-trip text lies above the value (the gap below a power of two is half the gap above).
        Assert.True(TechnologyObjectValues.TryConvert("1.2379401E+27", typeof(float), out var value, out var error), error);
        Assert.Equal(BitConverter.SingleToInt32Bits((float)Math.Pow(2, 90)), BitConverter.SingleToInt32Bits((float)value!));
        Assert.Equal("1.2379401E+27", TechnologyObjectValues.Describe(value).Text);
    }
}
