using System.Collections;
using System.Reflection;

namespace TiaMcpServer.Contracts.Library;

/// <summary>
/// Siemens-free runtime binding for library members whose return or declaring type is undocumented. A member
/// is found by its documented name and the documented argument types on the live object (and its interfaces);
/// exactly one applicable public instance method must exist, otherwise binding fails closed. Nothing is chosen
/// by a looser criterion than parameter assignability from the documented argument types.
/// </summary>
public static class LibraryRuntimeBinding
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The single applicable non-generic method; null with <paramref name="error"/> otherwise.</summary>
    public static MethodInfo? Find(Type runtimeType, string name, Type[] argumentTypes, out string? error)
    {
        var candidates = Candidates(runtimeType, name)
            .Where(method => !method.IsGenericMethodDefinition && Accepts(method, argumentTypes))
            .ToArray();
        return Single(candidates, runtimeType, name, argumentTypes, out error);
    }

    /// <summary>
    /// The single applicable generic method definition with <paramref name="genericArity"/> type parameters,
    /// closed over <paramref name="typeArguments"/>; null with <paramref name="error"/> otherwise (also when the
    /// type arguments violate its constraints).
    /// </summary>
    public static MethodInfo? FindGeneric(Type runtimeType, string name, Type[] typeArguments, Type[] argumentTypes, out string? error)
    {
        var closed = new List<MethodInfo>();
        foreach (var method in Candidates(runtimeType, name).Where(m => m.IsGenericMethodDefinition
                     && m.GetGenericArguments().Length == typeArguments.Length))
        {
            MethodInfo constructed;
            try
            {
                constructed = method.MakeGenericMethod(typeArguments);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (Accepts(constructed, argumentTypes)) closed.Add(constructed);
        }

        return Single(closed.ToArray(), runtimeType, name + "<" + string.Join(", ", typeArguments.Select(t => t.Name)) + ">", argumentTypes, out error);
    }

    /// <summary>
    /// The single method named <paramref name="name"/> whose parameters can each be given a collection of the
    /// matching <paramref name="elementTypes"/> entry (a <c>List&lt;T&gt;</c> or a <c>T[]</c>), with the built
    /// arguments; null with <paramref name="error"/> otherwise. Used where the source shows the call with
    /// collections but not the collection parameter types.
    /// </summary>
    public static (MethodInfo Method, object[] Arguments)? FindForCollections(Type runtimeType, string name,
        Type[] elementTypes, IReadOnlyList<IReadOnlyList<object>> items, out string? error)
    {
        var applicable = new List<(MethodInfo, object[])>();
        foreach (var method in Candidates(runtimeType, name).Where(m => !m.IsGenericMethodDefinition))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != elementTypes.Length) continue;
            var arguments = new object[parameters.Length];
            var ok = true;
            for (var index = 0; index < parameters.Length && ok; index++)
            {
                var built = BuildCollection(parameters[index].ParameterType, elementTypes[index], items[index]);
                if (built is null) ok = false;
                else arguments[index] = built;
            }

            if (ok) applicable.Add((method, arguments));
        }

        var distinct = applicable.GroupBy(pair => pair.Item1.ToString()).Select(group => group.First()).ToArray();
        if (distinct.Length == 1)
        {
            error = null;
            return distinct[0];
        }

        error = distinct.Length == 0
            ? $"This TIA Portal installation exposes no {name} on '{runtimeType.FullName}' taking collections of {Describe(elementTypes)}."
            : $"'{runtimeType.FullName}' exposes {distinct.Length} {name} overloads taking collections of {Describe(elementTypes)}; none is chosen.";
        return null;
    }

    /// <summary>A <c>List&lt;T&gt;</c> or <c>T[]</c> of <paramref name="items"/> assignable to <paramref name="parameterType"/>, or null.</summary>
    public static object? BuildCollection(Type parameterType, Type elementType, IReadOnlyList<object> items)
    {
        if (items.Any(item => item is null || !elementType.IsInstanceOfType(item))) return null;
        var listType = typeof(List<>).MakeGenericType(elementType);
        if (parameterType.IsAssignableFrom(listType))
        {
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var item in items) list.Add(item);
            return list;
        }

        if (parameterType.IsArray && parameterType.GetElementType() == elementType)
        {
            var array = Array.CreateInstance(elementType, items.Count);
            for (var index = 0; index < items.Count; index++) array.SetValue(items[index], index);
            return array;
        }

        return null;
    }

    /// <summary>The value of the enum member named exactly <paramref name="name"/> (ordinal), or null.</summary>
    public static object? EnumValue(Type enumType, string? name)
        => enumType.IsEnum && name is not null && Enum.GetNames(enumType).Contains(name, StringComparer.Ordinal)
            ? Enum.Parse(enumType, name) : null;

    /// <summary>The member names of <paramref name="enumType"/>, for a fail-closed message.</summary>
    public static string MemberList(Type enumType) => enumType.IsEnum ? string.Join(", ", Enum.GetNames(enumType)) : "(not an enum)";

    /// <summary>A readable public instance property declared on <paramref name="declaringType"/> or inherited by it (never the derived shadow).</summary>
    public static PropertyInfo? Property(Type declaringType, string name, out string? error)
    {
        try
        {
            var property = declaringType.GetProperty(name, PublicInstance);
            error = property is { CanRead: true } && property.GetIndexParameters().Length == 0 ? null
                : $"This TIA Portal installation exposes no readable {name} on '{declaringType.FullName}'.";
            return error is null ? property : null;
        }
        catch (AmbiguousMatchException)
        {
            error = $"'{declaringType.FullName}' exposes more than one {name} property; none is chosen.";
            return null;
        }
    }

    private static IEnumerable<MethodInfo> Candidates(Type runtimeType, string name)
        => runtimeType.GetMethods(PublicInstance).Where(m => m.Name == name)
            .Concat(runtimeType.GetInterfaces().SelectMany(i => i.GetMethods()).Where(m => m.Name == name));

    private static bool Accepts(MethodInfo method, Type[] argumentTypes)
    {
        var parameters = method.GetParameters();
        return parameters.Length == argumentTypes.Length
            && parameters.Select((p, i) => p.ParameterType.IsAssignableFrom(argumentTypes[i])).All(ok => ok);
    }

    private static MethodInfo? Single(MethodInfo[] candidates, Type runtimeType, string name, Type[] argumentTypes, out string? error)
    {
        // An interface method and its class implementation are one member: prefer the class declaration.
        var distinct = candidates.GroupBy(m => string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + "->" + m.ReturnType.FullName)
            .Select(group => group.FirstOrDefault(m => m.DeclaringType is { IsInterface: false }) ?? group.First())
            .ToArray();
        if (distinct.Length > 1)
        {
            // Several overloads accept the arguments: only an exact parameter-type match decides.
            var exact = distinct.Where(m => m.GetParameters().Select(p => p.ParameterType).SequenceEqual(argumentTypes)).ToArray();
            distinct = exact.Length == 1 ? exact : distinct;
        }

        if (distinct.Length == 1)
        {
            error = null;
            return distinct[0];
        }

        error = distinct.Length == 0
            ? $"This TIA Portal installation exposes no {name}({Describe(argumentTypes)}) on '{runtimeType.FullName}'."
            : $"'{runtimeType.FullName}' exposes {distinct.Length} {name} overloads accepting ({Describe(argumentTypes)}); none is chosen.";
        return null;
    }

    private static string Describe(IEnumerable<Type> types) => string.Join(", ", types.Select(t => t.Name));
}
