using System.Globalization;

namespace TiaMcpServer.Contracts.Plc.TechnologyObjects;

/// <summary>The value kinds a technology object parameter or connection property is reported as.</summary>
public static class TechnologyObjectValueKinds
{
    public const string Null = "null";
    public const string Boolean = "boolean";
    public const string Integer = "integer";
    public const string Real = "real";
    public const string String = "string";
    public const string Enum = "enum";
    public const string PlcTag = "plcTag";
    public const string DeviceItem = "deviceItem";
    public const string Channel = "channel";

    /// <summary>A value of another runtime type: reported by type name only and never written from text.</summary>
    public const string Object = "object";

    /// <summary>Reading the value threw a recoverable Openness error.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>Kinds <see cref="TechnologyObjectValues.TryConvert"/> can produce from text.</summary>
    public static IReadOnlyList<string> TextWritable { get; } = new[] { Boolean, Integer, Real, String, Enum };
}

/// <summary>
/// Siemens-free typing of <c>TechnologicalParameter.Value</c>. The V21 reference documents <c>Value</c> without a
/// type, and the Siemens skill says the accepted type depends on the parameter definition and that assigning an
/// incompatible type throws. The only declared type evidence the API offers is the runtime type of the current
/// value, so a write converts the caller's text to exactly that type (invariant culture, range checked) and refuses
/// every other type; a <c>PlcTag</c> value is assigned only from a resolved tag. Reals go through the
/// runtime-independent <see cref="TechnologyObjectReals"/>, so the worker (.NET Framework) and the host (.NET 10)
/// agree on every value and its text.
/// </summary>
public static class TechnologyObjectValues
{
    /// <summary>The kind and invariant text of a non-Siemens value (a PlcTag, device item or channel is the caller's to classify).</summary>
    public static (string Kind, string? Text) Describe(object? value)
        => value switch
        {
            null => (TechnologyObjectValueKinds.Null, null),
            bool b => (TechnologyObjectValueKinds.Boolean, b ? "true" : "false"),
            sbyte v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            byte v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            short v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            ushort v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            int v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            uint v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            long v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            ulong v => (TechnologyObjectValueKinds.Integer, v.ToString(CultureInfo.InvariantCulture)),
            float f => (TechnologyObjectValueKinds.Real, TechnologyObjectReals.Shortest(f)),
            double d => (TechnologyObjectValueKinds.Real, TechnologyObjectReals.Shortest(d)),
            decimal m => (TechnologyObjectValueKinds.Real, TechnologyObjectReals.Render(m)),
            string s => (TechnologyObjectValueKinds.String, TechnologyObjectPolicy.Bound(s)),
            // A combined [Flags] value's text ("A, B, ...") is cut at the text bound with the marker like every other text.
            Enum e => (TechnologyObjectValueKinds.Enum, TechnologyObjectPolicy.Bound(EnumText(e))),
            _ => (TechnologyObjectValueKinds.Object, null),
        };

    /// <summary>
    /// The one wording of a refused parameter value, used by the host planner, the worker and the FakeWorker alike:
    /// <c>Parameter '&lt;parameter&gt;' of '&lt;object&gt;': &lt;reason&gt;. Nothing was changed.</c>
    /// </summary>
    public static string ParameterRefusal(string? parameterName, string? objectName, string reason)
        => $"Parameter '{parameterName}' of '{objectName}': {reason}. Nothing was changed.";

    /// <summary>The member names of an enum type: the first <see cref="TechnologyObjectPolicy.MaxEnumMembers"/>.</summary>
    public static List<string> EnumMembers(Type? type)
        => type is { IsEnum: true } ? System.Enum.GetNames(type).Take(TechnologyObjectPolicy.MaxEnumMembers).ToList() : new List<string>();

    /// <summary>True when the enum has more members than <see cref="EnumMembers"/> lists.</summary>
    public static bool EnumMembersTruncated(Type? type) => type is { IsEnum: true } && System.Enum.GetNames(type).Length > TechnologyObjectPolicy.MaxEnumMembers;

    /// <summary>True for an enum declared <c>[Flags]</c>.</summary>
    public static bool IsFlags(Type? type) => type is { IsEnum: true } && type.IsDefined(typeof(FlagsAttribute), inherit: false);

    /// <summary>
    /// The listed-member check of an enum write without the live type (host planning): null when acceptable or when only
    /// the worker can tell (the list is truncated; the zero member inside a combination), otherwise the refusal. A
    /// <c>[Flags]</c> enum accepts distinct exact member names separated by commas, like <see cref="TryConvert"/>.
    /// </summary>
    public static string? EnumRefusal(string? text, IReadOnlyList<string> members, bool flags, bool truncated, string? typeName)
    {
        var names = EnumNames(text, flags);
        if (names is null) return MalformedEnumText(text, flags, typeName, members, truncated);
        var unknown = names.Where(name => !members.Contains(name, StringComparer.Ordinal)).ToList();
        if (unknown.Count == 0 || truncated) return null;
        return UnknownEnumMembers(unknown, typeName, members, truncated);
    }

    // The enum refusals of the host (EnumRefusal) and the worker (ConvertEnum) share these texts. Every caller text, name
    // list and member list in them is cut at the text bound with the marker, so a long [Flags] text never makes them unbounded.
    private static string MalformedEnumText(string? text, bool flags, string? typeName, IReadOnlyList<string> members, bool truncated)
        => $"'{TechnologyObjectPolicy.Bound(text)}' is not {(flags ? "a comma-separated list of distinct exact member names" : "an exact member name")} of {typeName} "
            + $"(members: {ListedMembers(members, truncated)})";

    private static string UnknownEnumMembers(IReadOnlyList<string> unknown, string? typeName, IReadOnlyList<string> members, bool truncated)
        => $"{TechnologyObjectPolicy.Bound(string.Join(", ", unknown.Select(name => "'" + name + "'")))} {(unknown.Count == 1 ? "is not a member" : "are not members")} "
            + $"of {typeName} (members: {ListedMembers(members, truncated)})";

    /// <summary>The first <see cref="TechnologyObjectPolicy.MaxEnumMembers"/> member names, ", ..." when there are more, cut at the text bound.</summary>
    private static string? ListedMembers(IReadOnlyList<string> members, bool truncated)
        => TechnologyObjectPolicy.Bound(string.Join(", ", members.Take(TechnologyObjectPolicy.MaxEnumMembers))
            + (truncated || members.Count > TechnologyObjectPolicy.MaxEnumMembers ? ", ..." : string.Empty));

    /// <summary>
    /// The syntax check of a real write without the live value (host planning): null for an invariant finite number text,
    /// otherwise the refusal. Range and exactness are decided by the worker against the live type (one authority).
    /// </summary>
    public static string? RealSyntaxRefusal(string? text, string? typeName)
        => TechnologyObjectReals.TryParseText(text, out _, out _, out _) ? null
            : $"'{TechnologyObjectPolicy.Bound(text)}' is not a finite invariant number (the parameter holds a {typeName})";

    /// <summary>
    /// Converts <paramref name="text"/> to exactly <paramref name="target"/>: <c>true</c>/<c>false</c> (case-insensitive)
    /// for bool; an invariant integer within the type's range for the integer types; for float/double an invariant number
    /// that is correctly rounded (to nearest, ties to even) to a finite value, accepted only when the text is that value's
    /// shortest round-trip text or its exact decimal expansion (as numbers: <c>0.1</c> and
    /// <c>0.100000001490116119384765625</c> for the float 0.1, not <c>0.10000000149</c>), otherwise refused naming the
    /// shortest text; the sign of zero is kept. For decimal an invariant number the type holds exactly (otherwise refused
    /// naming the nearest representable value; <c>-0</c> is refused as ambiguous). The text itself for string; an exact
    /// (ordinal) member name for an enum, or for a <c>[Flags]</c> enum distinct exact member names separated by commas
    /// (combined bitwise; the zero member only alone). Any other target type is refused.
    /// </summary>
    public static bool TryConvert(string? text, Type target, out object? value, out string error)
    {
        (value, error) = ConvertText(text, target);
        return error.Length == 0;
    }

    private static (object? Value, string Error) ConvertText(string? text, Type target)
    {
        if (text is null) return (null, "a value is required");
        // The same refusal (and text) as the host catalog's, before any conversion.
        if (TechnologyObjectPolicy.ValueLengthRefusal(text) is { } tooLong) return (null, tooLong);
        var invariant = CultureInfo.InvariantCulture;
        const NumberStyles IntegerStyle = NumberStyles.AllowLeadingSign;
        var shown = TechnologyObjectPolicy.Bound(text);
        var range = $"'{shown}' is not an invariant integer within the range of {target.FullName}";

        if (target == typeof(string)) return (text, string.Empty);
        if (target == typeof(bool))
        {
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return (true, string.Empty);
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return (false, string.Empty);
            return (null, $"'{shown}' is not true or false (the parameter holds a System.Boolean)");
        }

        if (target.IsEnum) return ConvertEnum(text, target);

        if (target == typeof(sbyte)) return sbyte.TryParse(text, IntegerStyle, invariant, out var v1) ? (v1, string.Empty) : (null, range);
        if (target == typeof(byte)) return byte.TryParse(text, IntegerStyle, invariant, out var v2) ? (v2, string.Empty) : (null, range);
        if (target == typeof(short)) return short.TryParse(text, IntegerStyle, invariant, out var v3) ? (v3, string.Empty) : (null, range);
        if (target == typeof(ushort)) return ushort.TryParse(text, IntegerStyle, invariant, out var v4) ? (v4, string.Empty) : (null, range);
        if (target == typeof(int)) return int.TryParse(text, IntegerStyle, invariant, out var v5) ? (v5, string.Empty) : (null, range);
        if (target == typeof(uint)) return uint.TryParse(text, IntegerStyle, invariant, out var v6) ? (v6, string.Empty) : (null, range);
        if (target == typeof(long)) return long.TryParse(text, IntegerStyle, invariant, out var v7) ? (v7, string.Empty) : (null, range);
        if (target == typeof(ulong)) return ulong.TryParse(text, IntegerStyle, invariant, out var v8) ? (v8, string.Empty) : (null, range);

        if (target == typeof(double) || target == typeof(float) || target == typeof(decimal))
        {
            if (!TechnologyObjectReals.TryParseText(text, out var negative, out var digits, out var exponent))
                return (null, $"'{shown}' is not a finite invariant number (the parameter holds a {target.FullName})");
            if (target == typeof(double))
            {
                if (!TechnologyObjectReals.TryToDouble(negative, digits, exponent, out var number))
                    return (null, $"'{shown}' is outside the range of System.Double (largest magnitude {TechnologyObjectReals.Shortest(double.MaxValue)})");
                var shortest = TechnologyObjectReals.Shortest(number);
                return TechnologyObjectReals.SameNumber(negative, digits, exponent, shortest)
                    || TechnologyObjectReals.SameNumber(negative, digits, exponent, TechnologyObjectReals.Exact(number))
                    ? (number, string.Empty) : (null, Inexact(shown, target, shortest));
            }

            if (target == typeof(float))
            {
                if (!TechnologyObjectReals.TryToSingle(negative, digits, exponent, out var single))
                    return (null, $"'{shown}' is outside the range of System.Single (largest magnitude {TechnologyObjectReals.Shortest(float.MaxValue)})");
                var shortest = TechnologyObjectReals.Shortest(single);
                return TechnologyObjectReals.SameNumber(negative, digits, exponent, shortest)
                    || TechnologyObjectReals.SameNumber(negative, digits, exponent, TechnologyObjectReals.Exact(single))
                    ? (single, string.Empty) : (null, Inexact(shown, target, shortest));
            }

            if (negative && digits.IsZero)
                return (null, $"'{shown}' is ambiguous for System.Decimal: the sign of a decimal zero is not kept by its comparison; write 0");
            if (TechnologyObjectReals.TryToDecimalExactly(negative, digits, exponent, out var amount, out var nearest)) return (amount, string.Empty);
            return nearest is { } held
                ? (null, NotHeld(shown, "cannot be held exactly by System.Decimal (a 96-bit significand with at most 28 decimal places)", TechnologyObjectReals.Render(held)))
                : (null, $"'{shown}' is outside the range of System.Decimal (largest magnitude {TechnologyObjectReals.Render(decimal.MaxValue)})");
        }

        return (null, $"the parameter holds a {target.FullName}, which cannot be written from text (writable kinds: {string.Join(", ", TechnologyObjectValueKinds.TextWritable)})");
    }

    /// <summary>A float or double text that is neither form accepted for the nearest value: the refusal names that value's shortest text.</summary>
    private static string Inexact(string? shown, Type target, string shortest)
        => NotHeld(shown, $"is neither the shortest round-trip text nor the exact decimal value of the nearest {target.FullName}", shortest + " (its shortest round-trip text)");

    /// <summary>The one wording of every real refusal that names the nearest representable value (float, double and decimal).</summary>
    private static string NotHeld(string? shown, string reason, string nearest)
        => $"'{shown}' {reason}; the nearest representable value is {nearest}. Write that text instead";

    private static (object? Value, string Error) ConvertEnum(string text, Type target)
    {
        var flags = IsFlags(target);
        var names = EnumNames(text, flags);
        var members = System.Enum.GetNames(target);
        if (names is null) return (null, MalformedEnumText(text, flags, target.FullName, members, truncated: false));
        var unknown = names.Where(name => !members.Contains(name, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0) return (null, UnknownEnumMembers(unknown, target.FullName, members, truncated: false));
        if (names.Count == 1) return (System.Enum.Parse(target, names[0]), string.Empty);
        var values = names.Select(name => (Name: name, Bits: Bits(System.Enum.Parse(target, name)))).ToList();
        if (values.FirstOrDefault(v => v.Bits == 0) is { Name: { } zero })
            return (null, $"'{TechnologyObjectPolicy.Bound(text)}' combines the zero member '{TechnologyObjectPolicy.Bound(zero)}' of {target.FullName} with others, which is ambiguous; write it alone");
        var combined = values.Aggregate(0UL, (all, v) => all | v.Bits);
        return (System.Enum.ToObject(target, combined), string.Empty);
    }

    /// <summary>
    /// An enum value's text without the runtime's enum formatting (whose integer fallback follows the current culture):
    /// the member name; for a <c>[Flags]</c> combination the names of the members that make it up (from the largest value
    /// down, each only while all its bits are unclaimed, zero members never), listed in ascending value order and
    /// separated by ", " as <c>Enum.ToString</c> lays them out; otherwise the invariant integer of the underlying value.
    /// </summary>
    private static string EnumText(Enum value)
    {
        var type = value.GetType();
        if (System.Enum.GetName(type, value) is { } name) return name;
        // Integer underlying types only (C# declares no other); anything else keeps the runtime's own text.
        if (!IsIntegerType(System.Enum.GetUnderlyingType(type))) return System.Enum.Format(type, value, "G");
        var bits = Bits(value);
        if (IsFlags(type) && bits != 0)
        {
            var names = System.Enum.GetNames(type);
            var values = System.Enum.GetValues(type);
            var members = names.Select((member, index) => (Name: member, Bits: Bits(values.GetValue(index)!)))
                .Where(member => member.Bits != 0).OrderByDescending(member => member.Bits).ToList();
            var remaining = bits;
            var found = new List<(string Name, ulong Bits)>();
            foreach (var member in members)
            {
                if ((member.Bits & remaining) != member.Bits) continue;
                found.Add(member);
                remaining &= ~member.Bits;
                if (remaining == 0) return string.Join(", ", found.OrderBy(m => m.Bits).Select(m => m.Name));
            }
        }

        var invariant = CultureInfo.InvariantCulture;
        var underlying = System.Enum.GetUnderlyingType(type);
        if (underlying == typeof(sbyte)) return unchecked((sbyte)(byte)bits).ToString(invariant);
        if (underlying == typeof(short)) return unchecked((short)(ushort)bits).ToString(invariant);
        if (underlying == typeof(int)) return unchecked((int)(uint)bits).ToString(invariant);
        if (underlying == typeof(long)) return unchecked((long)bits).ToString(invariant);
        return bits.ToString(invariant);
    }

    private static bool IsIntegerType(Type type)
        => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

    /// <summary>The raw bits of a boxed enum value in its underlying type (unboxed to that type; never sign-extended beyond it).</summary>
    private static ulong Bits(object value)
    {
        var underlying = System.Enum.GetUnderlyingType(value.GetType());
        if (underlying == typeof(sbyte)) return unchecked((byte)(sbyte)value);
        if (underlying == typeof(byte)) return (byte)value;
        if (underlying == typeof(short)) return unchecked((ushort)(short)value);
        if (underlying == typeof(ushort)) return (ushort)value;
        if (underlying == typeof(int)) return unchecked((uint)(int)value);
        if (underlying == typeof(uint)) return (uint)value;
        if (underlying == typeof(long)) return unchecked((ulong)(long)value);
        if (underlying == typeof(ulong)) return (ulong)value;
        throw new InvalidOperationException($"Unexpected enum underlying type {underlying.FullName}.");
    }

    /// <summary>
    /// The member names of an enum text: one exact name, or for flags distinct names separated by commas (blanks around
    /// them allowed); null when malformed or a name repeats.
    /// </summary>
    private static List<string>? EnumNames(string? text, bool flags)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var parts = flags ? text!.Split(',').Select(part => part.Trim(' ')).ToList() : new List<string> { text! };
        if (parts.Any(part => part.Length == 0 || part.Contains(' '))) return null;
        return parts.Distinct(StringComparer.Ordinal).Count() == parts.Count ? parts : null;
    }

    /// <summary>
    /// True when the read-back value equals the written one exactly: same runtime type and <see cref="object.Equals(object)"/>,
    /// for float and double the same IEEE 754 bits (so <c>-0</c> is not <c>0</c>).
    /// </summary>
    public static bool Equivalent(object? written, object? readBack)
        => written switch
        {
            null => readBack is null,
            double d => readBack is double r && TechnologyObjectReals.SameBits(d, r),
            float f => readBack is float r && TechnologyObjectReals.SameBits(f, r),
            _ => readBack is not null && written.GetType() == readBack.GetType() && written.Equals(readBack),
        };
}
