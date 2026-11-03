using System.Collections;
using System.Reflection;

namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// One argument of a member bound by its documented signature (<see cref="LibrarySignatureBinding"/>): a value whose
/// parameter must accept it, an enum member named exactly (the parameter's enum type must have the documented full
/// name), or a collection whose parameter is a generic collection of the documented element type.
/// </summary>
public sealed class SignatureArgument
{
    private SignatureArgument(string kind, object? value, Type? valueType, string? typeFullName, string? memberName, IReadOnlyList<object>? items)
    {
        Kind = kind;
        Value = value;
        ValueType = valueType;
        TypeFullName = typeFullName;
        MemberName = memberName;
        Items = items;
    }

    public string Kind { get; }

    public object? Value { get; }

    public Type? ValueType { get; }

    /// <summary>The documented full name of the enum type, or of the collection's element type.</summary>
    public string? TypeFullName { get; }

    /// <summary>The enum member name(s); several names are combined only on a <c>[Flags]</c> enum.</summary>
    public string? MemberName { get; }

    public IReadOnlyList<object>? Items { get; }

    /// <summary>A value: the parameter must be assignable from <paramref name="type"/> (a null value too).</summary>
    public static SignatureArgument Of(object? value, Type type) => new("value", value, type, null, null, null);

    public static SignatureArgument Of<T>(T value) => Of(value, typeof(T));

    /// <summary>
    /// An enum value: the parameter's type must be an enum named exactly <paramref name="enumFullName"/>; the value is its
    /// member <paramref name="member"/> (ordinal). A comma-separated list of members binds only on a <c>[Flags]</c> enum.
    /// </summary>
    public static SignatureArgument Enum(string enumFullName, string member) => new("enum", null, null, enumFullName, member, null);

    /// <summary>A null reference for a parameter whose type has exactly the documented full name (e.g. an optional delegate).</summary>
    public static SignatureArgument Null(string typeFullName) => new("null", null, null, typeFullName, null, null);

    /// <summary>A collection: the parameter must take a <c>List&lt;T&gt;</c> or <c>T[]</c> of the element type named <paramref name="elementFullName"/>.</summary>
    public static SignatureArgument Collection(string elementFullName, IReadOnlyList<object> items) => new("collection", null, null, elementFullName, null, items);

    public string Describe() => Kind switch
    {
        "enum" => TypeFullName!,
        "collection" => $"IEnumerable<{TypeFullName}>",
        "null" => TypeFullName + " (null)",
        _ => ValueType!.Name,
    };
}

/// <summary>
/// Siemens-free binding of a public instance method by its documented name and parameter list where some parameter types
/// are not declared on the compile-time stubs (enums, collections of an interface type): enum parameters are matched by the
/// documented full type name, collections by the documented element type name, values by assignability. Exactly one
/// applicable method must exist; otherwise binding fails closed with a message naming what is missing. Nothing is chosen by
/// a looser criterion.
/// </summary>
public static class LibrarySignatureBinding
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The single applicable method and its built arguments; null with <paramref name="error"/> otherwise.</summary>
    public static (MethodInfo Method, object?[] Arguments)? Bind(Type runtimeType, string name, IReadOnlyList<SignatureArgument> arguments, out string? error)
    {
        var applicable = new List<(MethodInfo, object?[])>();
        var candidates = runtimeType.GetMethods(PublicInstance).Where(m => m.Name == name)
            .Concat(runtimeType.GetInterfaces().SelectMany(i => i.GetMethods()).Where(m => m.Name == name))
            .Where(m => !m.IsGenericMethodDefinition && m.GetParameters().Length == arguments.Count);
        var enumProblems = new List<string>();
        foreach (var method in candidates)
        {
            var parameters = method.GetParameters();
            var built = new object?[parameters.Length];
            var ok = true;
            for (var index = 0; index < parameters.Length && ok; index++)
            {
                var parameter = parameters[index].ParameterType;
                var argument = arguments[index];
                switch (argument.Kind)
                {
                    case "enum":
                        if (!parameter.IsEnum || parameter.FullName != argument.TypeFullName) { ok = false; break; }
                        var value = EnumValue(parameter, argument.MemberName!, out var enumError);
                        if (value is null) { enumProblems.Add(enumError!); ok = false; break; }
                        built[index] = value;
                        break;
                    case "null":
                        if (parameter.IsValueType || parameter.FullName != argument.TypeFullName) { ok = false; break; }
                        built[index] = null;
                        break;
                    case "collection":
                        var element = ElementType(parameter);
                        if (element is null || element.FullName != argument.TypeFullName) { ok = false; break; }
                        var collection = LibraryRuntimeBinding.BuildCollection(parameter, element, argument.Items!);
                        if (collection is null) { ok = false; break; }
                        built[index] = collection;
                        break;
                    default:
                        if (!parameter.IsAssignableFrom(argument.ValueType!)) { ok = false; break; }
                        built[index] = argument.Value;
                        break;
                }
            }

            if (ok) applicable.Add((method, built));
        }

        // An interface method and its class implementation are one member: prefer the class declaration.
        var distinct = applicable
            .GroupBy(pair => string.Join(",", pair.Item1.GetParameters().Select(p => p.ParameterType.FullName)) + "->" + pair.Item1.ReturnType.FullName)
            .Select(group => group.FirstOrDefault(pair => pair.Item1.DeclaringType is { IsInterface: false }) is { Item1: not null } cls ? cls : group.First())
            .ToArray();
        if (distinct.Length == 1)
        {
            error = null;
            return distinct[0];
        }

        var signature = $"{name}({string.Join(", ", arguments.Select(a => a.Describe()))})";
        error = distinct.Length == 0
            ? enumProblems.Count > 0
                ? $"'{runtimeType.FullName}' exposes {signature}, but {string.Join(" ", enumProblems.Distinct(StringComparer.Ordinal))}"
                : $"This TIA Portal installation exposes no {signature} on '{runtimeType.FullName}'."
            : $"'{runtimeType.FullName}' exposes {distinct.Length} {name} overloads accepting ({string.Join(", ", arguments.Select(a => a.Describe()))}); none is chosen.";
        return null;
    }

    /// <summary>The element type of a parameter that takes <c>IEnumerable&lt;T&gt;</c> (or a <c>T[]</c>), else null.</summary>
    public static Type? ElementType(Type parameter)
    {
        if (parameter.IsArray) return parameter.GetElementType();
        if (parameter.IsGenericType && parameter.GetGenericArguments().Length == 1 && typeof(IEnumerable).IsAssignableFrom(parameter))
            return parameter.GetGenericArguments()[0];
        return null;
    }

    /// <summary>
    /// The value of <paramref name="members"/> on <paramref name="enumType"/>: one exact member name, or a comma-separated
    /// list combined bitwise only on a <c>[Flags]</c> enum. Null with <paramref name="error"/> otherwise.
    /// </summary>
    public static object? EnumValue(Type enumType, string members, out string? error)
    {
        var names = members.Split(',').Select(name => name.Trim()).ToArray();
        var known = System.Enum.GetNames(enumType);
        var missing = names.Where(name => !known.Contains(name, StringComparer.Ordinal)).ToArray();
        if (missing.Length > 0)
        {
            error = $"{enumType.FullName} has no member {string.Join(", ", missing.Select(m => "'" + m + "'"))} on this installation (members: {string.Join(", ", known)}).";
            return null;
        }

        if (names.Length > 1 && !enumType.IsDefined(typeof(FlagsAttribute), false))
        {
            error = $"{enumType.FullName} is not a [Flags] enum on this installation, so the members {string.Join(", ", names)} cannot be combined; name one.";
            return null;
        }

        error = null;
        ulong combined = 0;
        foreach (var name in names) combined |= Convert.ToUInt64(System.Enum.Parse(enumType, name), System.Globalization.CultureInfo.InvariantCulture);
        return System.Enum.ToObject(enumType, combined);
    }
}
