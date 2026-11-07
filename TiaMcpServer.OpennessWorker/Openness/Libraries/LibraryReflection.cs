using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Libraries;

/// <summary>
/// Runtime access to library members whose return, declaring or collection parameter types the sources do not
/// document. Every member is reached by its documented name and argument types on the live object
/// (<see cref="LibraryRuntimeBinding"/>); a missing or ambiguous member fails closed with
/// <c>unsupported_capability</c> before anything is called. A Siemens exception thrown by the member is
/// rethrown unwrapped, so the shared classification (<c>engineering_rejected</c>/<c>portal_lost</c>) applies.
/// </summary>
internal static class LibraryReflection
{
    /// <summary>Invokes the single applicable method <paramref name="name"/>(<paramref name="argumentTypes"/>).</summary>
    public static object? Invoke(object target, string name, Type[] argumentTypes, object?[] arguments, string documented)
    {
        var method = LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw Missing(error, documented);
        return Call(method, target, arguments);
    }

    /// <summary>
    /// <see cref="Invoke(object, string, Type[], object?[], string)"/> that also reports the bound member's return type, so a
    /// caller can tell a member without a return value (<c>void</c>) from one that returned null.
    /// </summary>
    public static object? Invoke(object target, string name, Type[] argumentTypes, object?[] arguments, string documented, out Type returnType)
    {
        var method = LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw Missing(error, documented);
        returnType = method.ReturnType;
        return Call(method, target, arguments);
    }

    /// <summary>Invokes the single applicable generic method <paramref name="name"/>&lt;<paramref name="typeArguments"/>&gt;.</summary>
    public static object? InvokeGeneric(object target, string name, Type[] typeArguments, Type[] argumentTypes, object?[] arguments, string documented)
    {
        var method = LibraryRuntimeBinding.FindGeneric(target.GetType(), name, typeArguments, argumentTypes, out var error)
            ?? throw Missing(error, documented);
        return Call(method, target, arguments);
    }

    /// <summary>
    /// Invokes the single method <paramref name="name"/> whose parameters match the documented signature
    /// (<see cref="LibrarySignatureBinding"/>: enums and collection element types by their documented full names); reports the
    /// bound member's return type. Fails closed with <c>unsupported_capability</c> when none or several match.
    /// </summary>
    public static object? InvokeSignature(object target, string name, IReadOnlyList<SignatureArgument> arguments, string documented, out Type returnType)
    {
        var bound = LibrarySignatureBinding.Bind(target.GetType(), name, arguments, out var error) ?? throw Missing(error, documented);
        returnType = bound.Method.ReturnType;
        return Call(bound.Method, target, bound.Arguments);
    }

    public static object? InvokeSignature(object target, string name, IReadOnlyList<SignatureArgument> arguments, string documented)
        => InvokeSignature(target, name, arguments, documented, out _);

    /// <summary>
    /// Sets a documented writable property (<paramref name="name"/>) on the live object by reflection and returns the value read back
    /// through the same property; a missing or read-only property fails closed (<c>unsupported_capability</c>, nothing changed).
    /// </summary>
    public static object? SetProperty(object target, string name, object? value, string documented)
    {
        System.Reflection.PropertyInfo? property;
        try
        {
            property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        }
        catch (AmbiguousMatchException)
        {
            property = null;
        }

        if (property is null || !property.CanWrite || !property.CanRead || property.GetIndexParameters().Length != 0
            || (value is not null && !property.PropertyType.IsInstanceOfType(value)))
            throw Missing($"This TIA Portal installation exposes no writable {name} of the value's type on '{target.GetType().FullName}'.", documented);
        try
        {
            property.SetValue(target, value);
            return property.GetValue(target);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Invokes the single overload of <paramref name="name"/> that takes one collection per element type.</summary>
    public static object? InvokeWithCollections(object target, string name, Type[] elementTypes, IReadOnlyList<IReadOnlyList<object>> items, string documented)
    {
        var bound = LibraryRuntimeBinding.FindForCollections(target.GetType(), name, elementTypes, items, out var error)
            ?? throw Missing(error, documented);
        return Call(bound.Method, target, bound.Arguments);
    }

    /// <summary>A required property, read on the documented declaring type (never a derived shadow).</summary>
    public static object? Required(object target, Type declaringType, string name, string documented)
    {
        // K82: a null owner made PropertyInfo.GetValue throw an unhandled TargetException ("non-static method requires a target").
        if (target is null)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                $"{documented} cannot be read: TIA Portal returned no owning object (null). Nothing was changed.");
        var property = LibraryRuntimeBinding.Property(declaringType, name, out var error) ?? throw Missing(error, documented);
        try
        {
            return property.GetValue(target);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>A required property on the runtime object.</summary>
    public static object? Required(object target, string name, string documented) => Required(target, target.GetType(), name, documented);

    /// <summary>An informative property: null (and an entry in <paramref name="unreadable"/>) when absent or throwing.</summary>
    public static object? Optional(object? target, string name, List<string> unreadable, string description)
    {
        if (target is null) return null;
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out _);
        if (property is null)
        {
            unreadable.Add(description);
            return null;
        }

        try
        {
            return property.GetValue(target);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // A recoverable Siemens failure makes the member unreadable; anything else (Portal loss included)
            // is rethrown unwrapped so the shared classification applies.
            if (exception.InnerException is EngineeringException && OpennessExceptions.IsRecoverable(exception.InnerException))
            {
                unreadable.Add(description);
                return null;
            }

            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>An informative text: the CLR property, then the Openness attribute of the same name.</summary>
    public static string? Text(object? target, string name, List<string> unreadable, string description)
    {
        if (target is null) return null;
        var value = OpennessReflection.ReadPropertyOrAttribute(target, name);
        if (value is null) unreadable.Add(description);
        return LibraryPolicy.Bound(value);
    }

    /// <summary>The value as text: a file-system path by full name, an enum by name, anything else by ToString.</summary>
    public static string? Display(object? value)
        => value switch
        {
            null => null,
            FileSystemInfo info => info.FullName,
            _ => LibraryPolicy.Bound(value.ToString()),
        };

    public static IEnumerable<object> Enumerate(object? collection, string description)
        => OpennessReflection.Enumerate(collection, description);

    /// <summary>The live enum value of the exact member name, or a fail-closed refusal listing the members.</summary>
    public static object EnumValue(Type enumType, string name, string documented)
        => LibraryRuntimeBinding.EnumValue(enumType, name)
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"{documented} has no member '{name}' on this TIA Portal installation (members: {LibraryRuntimeBinding.MemberList(enumType)}). Nothing was changed.");

    /// <summary>Readable scalar attributes through the documented generic attribute API, at most <see cref="LibraryPolicy.MaxAttributes"/>.</summary>
    public static List<LibraryAttributeInfo> Attributes(object? target)
    {
        var result = new List<LibraryAttributeInfo>();
        if (target is not IEngineeringObject engineeringObject) return result;
        IList<EngineeringAttributeInfo> infos;
        try
        {
            infos = engineeringObject.GetAttributeInfos();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return result;
        }

        foreach (var info in infos)
        {
            if (result.Count >= LibraryPolicy.MaxAttributes) break;
            string? name;
            try
            {
                name = info.Name;
                if (name is null || info.AccessMode is not (EngineeringAttributeAccessMode.Read or EngineeringAttributeAccessMode.ReadWrite)) continue;
            }
            catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
            {
                continue;
            }

            try
            {
                var value = engineeringObject.GetAttribute(name);
                if (value is null || value is string || value is bool || value.GetType().IsPrimitive || value is decimal || value is Enum || value is Guid
                    || value is Version || value is DateTime)
                    result.Add(new LibraryAttributeInfo { Name = name, Value = Display(value) });
            }
            catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
            {
                // Unreadable on this object; schema-free projection skips it.
            }
        }

        return result;
    }

    /// <summary>
    /// Renames through the documented generic attribute API: <c>Name</c> must be listed writable by
    /// <c>GetAttributeInfos()</c>, then <c>SetAttribute("Name", value)</c> and a <c>GetAttribute("Name")</c> read-back.
    /// A silently ignored rename fails <c>postcondition_failed</c>.
    /// </summary>
    public static string Rename(object target, string newName, string description)
    {
        if (target is not IEngineeringObject engineeringObject)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"{description} is not an engineering object; it cannot be renamed.");
        var info = engineeringObject.GetAttributeInfos().FirstOrDefault(candidate => candidate.Name == "Name");
        if (info is null || info.AccessMode is not (EngineeringAttributeAccessMode.Write or EngineeringAttributeAccessMode.ReadWrite))
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"{description} lists no writable Name attribute (GetAttributeInfos); it cannot be renamed through Openness. Nothing was changed.");
        engineeringObject.SetAttribute("Name", newName);
        var after = engineeringObject.GetAttribute("Name")?.ToString();
        if (!string.Equals(after, newName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{description} reads back the name '{after}' after SetAttribute(\"Name\", \"{newName}\"); TIA Portal did not apply the rename.");
        return after!;
    }

    public static string? RuntimeType(object? value) => value?.GetType().FullName;

    private static object? Call(MethodInfo method, object target, object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static WorkerOperationException Missing(string? error, string documented)
        => new(WorkerFailureCategories.UnsupportedCapability,
            (error ?? $"This TIA Portal installation does not expose {documented}.") + $" ({documented}) Nothing was changed.");
}
