using System.Reflection;
using System.Runtime.ExceptionServices;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Plc.Security;

/// <summary>
/// Siemens-free runtime binding of the CPU protection members, shared by the worker (live Openness services) and the
/// tests (stand-in types). Nothing is compiled against a Siemens protection type: the services are found among the
/// types <c>GetServiceInfos()</c> lists, and every member is bound on the listed service type by its exact name, its
/// exact parameter list and, for properties, an enum type of the documented short name. Anything else fails closed
/// with <see cref="WorkerFailureCategories.ApiMemberMissing"/> before a member is called.
/// </summary>
public static class PlcSecurityReflection
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    /// <summary>The listed service type with exactly <paramref name="fullName"/>; null when not listed.</summary>
    /// <exception cref="PlcSecurityBindingException">Several distinct listed types carry the name.</exception>
    public static Type? FindByFullName(IReadOnlyList<Type> offered, string fullName)
        => Single(offered.Where(type => string.Equals(type.FullName, fullName, StringComparison.Ordinal)), fullName);

    /// <summary>
    /// The listed service type whose short name is exactly <paramref name="shortName"/> in a <c>Siemens.Engineering</c>
    /// namespace (for a service whose namespace is not documented); null when not listed.
    /// </summary>
    /// <exception cref="PlcSecurityBindingException">Several distinct listed types match.</exception>
    public static Type? FindByShortName(IReadOnlyList<Type> offered, string shortName)
        => Single(offered.Where(type => string.Equals(type.Name, shortName, StringComparison.Ordinal)
            && (type.Namespace ?? string.Empty).StartsWith("Siemens.Engineering", StringComparison.Ordinal)), shortName);

    private static Type? Single(IEnumerable<Type> matches, string name)
    {
        var distinct = matches.Distinct().ToList();
        if (distinct.Count > 1)
            throw new PlcSecurityBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"GetServiceInfos() lists {distinct.Count} distinct service types named '{name}' ({string.Join(", ", distinct.Select(t => t.FullName))}); none is chosen. Nothing was changed.");
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>The one public, non-indexed instance property <paramref name="name"/> of the type or (for an interface) its base interfaces.</summary>
    public static PropertyInfo? Property(Type type, string name)
    {
        var found = new[] { type }.Concat(type.IsInterface ? type.GetInterfaces() : Type.EmptyTypes)
            .SelectMany(candidate => candidate.GetProperties(PublicInstance))
            .Where(property => string.Equals(property.Name, name, StringComparison.Ordinal) && property.GetIndexParameters().Length == 0)
            .Distinct()
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Reads one documented enum setting of a service; never throws for a missing service or member.</summary>
    /// <param name="recoverable">True for an exception of the getter that only makes the value unreadable (a recoverable Openness error).</param>
    /// <param name="nullReferenceNote">The note for a getter NullReferenceException (documented for the access level without a password).</param>
    public static PlcSecuritySettingInfo ReadSetting(string service, string propertyName, string enumName, Type? serviceType,
        object? serviceObject, Func<Exception, bool> recoverable, string? nullReferenceNote)
    {
        var info = new PlcSecuritySettingInfo { Service = service, Property = propertyName, ServiceOffered = serviceType is not null, ServiceType = serviceType?.FullName };
        if (serviceType is null)
        {
            info.Note = $"The CPU's GetServiceInfos() does not list {service}; this CPU does not offer the setting through Openness.";
            return info;
        }

        if (serviceObject is null)
        {
            // Listed but not returned by GetService (D14): this setting is unavailable, the rest of the read goes on.
            info.Note = Shared.ListedServices.UnavailableReason(serviceType.FullName ?? service);
            return info;
        }

        var property = Property(serviceType, propertyName);
        if (property is null || !property.CanRead || property.GetGetMethod() is null)
        {
            info.Note = $"{serviceType.FullName} has no public readable property '{propertyName}' on this TIA Portal installation (api_member_missing).";
            return info;
        }

        info.Writable = property.CanWrite && property.GetSetMethod() is not null;
        if (property.PropertyType.IsEnum) info.AvailableValues = Enum.GetNames(property.PropertyType).ToList();
        if (!property.PropertyType.IsEnum || !string.Equals(property.PropertyType.Name, enumName, StringComparison.Ordinal))
            info.Note = $"{serviceType.FullName}.{propertyName} is a {property.PropertyType.FullName}, not the documented enum {enumName}.";
        var (readable, value, note) = ReadValue(property, serviceObject, recoverable, nullReferenceNote);
        info.Readable = readable;
        info.Value = value;
        info.Note = note ?? info.Note;
        return info;
    }

    /// <summary>The property's value as text; (false, null, note) for a NullReferenceException or a recoverable error.</summary>
    public static (bool Readable, string? Value, string? Note) ReadValue(PropertyInfo property, object target,
        Func<Exception, bool> recoverable, string? nullReferenceNote)
    {
        try
        {
            return (true, PlcSecurityPolicy.Bound(property.GetValue(target, null)?.ToString()), null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is NullReferenceException)
        {
            return (false, null, nullReferenceNote ?? $"Reading {property.Name} threw NullReferenceException.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } inner && recoverable(inner))
        {
            return (false, null, PlcSecurityPolicy.Bound($"Reading {property.Name} failed: {inner.GetType().Name}: {inner.Message}"));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>The documented enum property with a public setter, or <see cref="WorkerFailureCategories.ApiMemberMissing"/>.</summary>
    public static PropertyInfo RequireWritableEnum(Type serviceType, string propertyName, string enumName)
    {
        var property = Property(serviceType, propertyName);
        if (property is null)
            throw Missing($"{serviceType.FullName} has no public property '{propertyName}'.");
        if (!property.PropertyType.IsEnum || !string.Equals(property.PropertyType.Name, enumName, StringComparison.Ordinal))
            throw Missing($"{serviceType.FullName}.{propertyName} is a {property.PropertyType.FullName}, not the documented enum {enumName}.");
        if (!property.CanWrite || property.GetSetMethod() is null)
            throw Missing($"{serviceType.FullName}.{propertyName} has no public setter on this TIA Portal installation.");
        return property;
    }

    /// <summary>The enum member named exactly <paramref name="name"/> (case-sensitive); <see cref="WorkerFailureCategories.ApiMemberMissing"/> when the live enum lacks it.</summary>
    public static object ParseExact(Type enumType, string name)
    {
        if (!enumType.IsEnum || !Enum.GetNames(enumType).Contains(name, StringComparer.Ordinal))
            throw Missing($"The live enum {enumType.FullName} has no member '{name}' (members: {(enumType.IsEnum ? string.Join(", ", Enum.GetNames(enumType)) : "none")}).");
        return Enum.Parse(enumType, name, ignoreCase: false);
    }

    /// <summary>
    /// The claimed <c>void Unprotect()</c> on the type or its interfaces: exactly one public instance method with that name,
    /// no parameters and no generic arguments returning <c>void</c>; null otherwise. <paramref name="candidates"/> lists the
    /// signature of every public instance method named <c>Unprotect</c>.
    /// </summary>
    public static MethodInfo? FindUnprotect(Type type, out List<string> candidates)
    {
        var named = Methods(type, PlcSecurityPolicy.UnprotectMethod);
        candidates = named.Select(Signature).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var exact = named.Where(method => method.GetParameters().Length == 0 && !method.IsGenericMethodDefinition && method.ReturnType == typeof(void)).ToList();
        return exact.Count == 1 ? exact[0] : null;
    }

    /// <summary>True when exactly one documented <c>Protect(System.Security.SecureString)</c> exists (reported only, never called).</summary>
    public static bool HasProtect(Type type)
        => Methods(type, PlcSecurityPolicy.ProtectMethod).Count(method => !method.IsGenericMethodDefinition
            && method.GetParameters() is { Length: 1 } parameters && parameters[0].ParameterType.FullName == "System.Security.SecureString") == 1;

    /// <summary>Parameter shapes of the documented members: a <c>SecureString</c>, or the <c>PlcProtectionAccessLevel</c> enum.</summary>
    public static bool IsSecureString(Type type) => type.FullName == "System.Security.SecureString";

    public static bool IsAccessLevelEnum(Type type) => type.IsEnum && string.Equals(type.Name, PlcSecurityPolicy.AccessLevelEnumName, StringComparison.Ordinal);

    /// <summary>
    /// The one public instance method <paramref name="name"/> returning <c>void</c> whose parameters match
    /// <paramref name="parameters"/> one to one; <see cref="WorkerFailureCategories.ApiMemberMissing"/> otherwise (the
    /// message lists every signature of that name found). Bound before anything is called.
    /// </summary>
    public static MethodInfo RequireMethod(Type type, string name, string documented, params Func<Type, bool>[] parameters)
    {
        var named = Methods(type, name);
        var exact = named.Where(method => !method.IsGenericMethodDefinition && method.ReturnType == typeof(void)
            && method.GetParameters() is var actual && actual.Length == parameters.Length
            && actual.Select((parameter, index) => parameters[index](parameter.ParameterType)).All(match => match)).ToList();
        if (exact.Count == 1) return exact[0];
        var found = named.Select(Signature).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        throw Missing($"{type.FullName} has no single public {documented} on this TIA Portal installation (found: {(found.Count == 0 ? $"no method named {name}" : string.Join("; ", found))}).");
    }

    /// <summary>Invokes a bound method; a Siemens exception is rethrown unwrapped for the shared classification.</summary>
    public static void Invoke(MethodInfo method, object target, params object?[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static List<MethodInfo> Methods(Type type, string name)
        => new[] { type }.Concat(type.IsInterface ? type.GetInterfaces() : Type.EmptyTypes)
            .SelectMany(candidate => candidate.GetMethods(PublicInstance))
            .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
            .Distinct()
            .ToList();

    /// <summary>Public readable scalar (primitive, enum, string) properties of the service type and their values, sorted by name and capped.</summary>
    public static List<PlcSecurityMemberInfo> Observe(Type serviceType, object target, Func<Exception, bool> recoverable)
    {
        var members = new List<PlcSecurityMemberInfo>();
        var properties = new[] { serviceType }.Concat(serviceType.IsInterface ? serviceType.GetInterfaces() : Type.EmptyTypes)
            .SelectMany(candidate => candidate.GetProperties(PublicInstance))
            .Where(property => property.CanRead && property.GetGetMethod() is not null && property.GetIndexParameters().Length == 0 && IsScalar(property.PropertyType))
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Take(PlcSecurityPolicy.MaxMembers);
        foreach (var property in properties)
        {
            var (readable, value, _) = ReadValue(property, target, recoverable, null);
            members.Add(new PlcSecurityMemberInfo { Name = property.Name, Type = property.PropertyType.FullName ?? property.PropertyType.Name, Readable = readable, Value = value });
        }

        return members;
    }

    private static bool IsScalar(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string);

    /// <summary>Sets the property; a Siemens exception of the setter is rethrown unwrapped for the shared classification.</summary>
    public static void SetValue(PropertyInfo property, object target, object value)
    {
        try
        {
            property.SetValue(target, value, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Invokes a parameterless method; a Siemens exception is rethrown unwrapped for the shared classification.</summary>
    public static void InvokeParameterless(MethodInfo method, object target)
    {
        try
        {
            method.Invoke(target, Array.Empty<object>());
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary><c>ReturnType Name(ParameterTypes)</c> with short type names.</summary>
    public static string Signature(MethodInfo method)
        => $"{(method.ReturnType == typeof(void) ? "void" : method.ReturnType.Name)} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    public static PlcSecurityBindingException Missing(string message)
        => new(WorkerFailureCategories.ApiMemberMissing, message + " Nothing was changed.");
}

/// <summary>A CPU protection member could not be bound as documented (or claimed); carries the failure category.</summary>
public sealed class PlcSecurityBindingException : Exception
{
    public PlcSecurityBindingException(string category, string message) : base(message) => Category = category;

    public string Category { get; }
}
