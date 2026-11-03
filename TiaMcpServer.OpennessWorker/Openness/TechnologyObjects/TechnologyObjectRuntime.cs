using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Libraries;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;

/// <summary>
/// Runtime access to the technology object members. The V21 reference names every member and its parameter
/// types but no return or property type, and the Siemens skill uses pre-V21 type names
/// (<c>TechnologicalObject</c>, <c>TechnologicalObjectComposition</c>) that the V21 baseline does not contain,
/// so every TO-specific member is bound on the live object by its documented name and argument types
/// (<see cref="LibraryRuntimeBinding"/>: exactly one applicable member, otherwise <c>unsupported_capability</c>
/// before anything is called). Siemens exceptions thrown by a member are rethrown unwrapped so the shared
/// classification (<c>engineering_rejected</c>, <c>portal_lost</c>) applies.
/// </summary>
internal static class TechnologyObjectRuntime
{
    /// <summary>Every element of a collection; a throwing enumeration propagates (never a silently short list).</summary>
    public static List<object> Items(object? collection, string documented)
    {
        if (collection is null or string || collection is not IEnumerable enumerable)
            throw Unsupported($"{documented} is not an enumerable collection on this TIA Portal installation ({collection?.GetType().FullName ?? "null"}).");
        var items = new List<object>();
        foreach (var item in enumerable)
            if (item is not null) items.Add(item);
        return items;
    }

    /// <summary><c>TechnologicalInstanceDB.Parameters</c> (reference: "Get all technological parameters").</summary>
    public static object Parameters(object technologyObject)
        => LibraryReflection.Required(technologyObject, "Parameters", "TechnologicalInstanceDB.Parameters")
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "TechnologicalInstanceDB.Parameters returned null.");

    /// <summary><c>TechnologicalParameterComposition.Find(String)</c>; null when the composition has no such parameter.</summary>
    public static object? FindParameter(object parameters, string name)
        => LibraryReflection.Invoke(parameters, "Find", new[] { typeof(string) }, new object?[] { name }, "TechnologicalParameterComposition.Find(String)");

    /// <summary><c>TechnologicalParameter.Name</c> as text.</summary>
    public static string ParameterName(object parameter)
        => LibraryReflection.Required(parameter, "Name", "TechnologicalParameter.Name")?.ToString() ?? string.Empty;

    /// <summary><c>TechnologicalParameter.Value</c>; (false, null) when a recoverable Openness error makes it unreadable.</summary>
    public static (bool Readable, object? Value) ParameterValue(object parameter)
    {
        try
        {
            return (true, LibraryReflection.Required(parameter, "Value", "TechnologicalParameter.Value"));
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception) && exception is not EngineeringObjectDisposedException)
        {
            return (false, null);
        }
    }

    /// <summary>Assigns <c>TechnologicalParameter.Value</c> through its public setter (the Siemens skill's documented write).</summary>
    public static void SetParameterValue(object parameter, object? value)
    {
        var property = LibraryRuntimeBinding.Property(parameter.GetType(), "Value", out var error) ?? throw Unsupported(error);
        if (!property.CanWrite || property.GetSetMethod() is null)
            throw Unsupported($"TechnologicalParameter.Value has no public setter on '{parameter.GetType().FullName}'.");
        try
        {
            property.SetValue(parameter, value);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>The documented element type of <c>GetServiceInfos()</c> (Siemens engineering-objects skill; listed in the V21 Base baseline).</summary>
    private const string EngineeringServiceInfoName = "Siemens.Engineering.EngineeringServiceInfo";

    /// <summary>
    /// The service types <c>IEngineeringServiceProvider.GetServiceInfos()</c> lists for <paramref name="target"/>. The
    /// Siemens engineering-objects skill documents the member as an explicit interface implementation returning
    /// <c>IList&lt;EngineeringServiceInfo&gt;</c> whose <c>Type</c> is the service's CLR type, to be called before
    /// <c>GetService</c>, and warns that reflection on the proxy type returns null. It is not declared in the compile stubs
    /// (the sources do not say whether <c>EngineeringServiceInfo</c> is a class or a struct, which a compiled call would
    /// fix), so it is a live check: the method is taken from the interface type itself (where an explicit implementation
    /// is reachable), and the declaring type, the parameterless signature, the <c>IList&lt;EngineeringServiceInfo&gt;</c>
    /// return type, a <c>System.Type</c>-typed <c>Type</c> property, a non-null list and every element's type must all be
    /// as documented; anything else is <c>unsupported_capability</c>, never an empty list.
    /// </summary>
    public static IReadOnlyList<Type> ServiceTypes(object target)
    {
        if (target is not IEngineeringServiceProvider)
            throw Unsupported($"'{target.GetType().FullName}' is not an IEngineeringServiceProvider, so its services cannot be listed.");
        var method = typeof(IEngineeringServiceProvider).GetMethod("GetServiceInfos", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        var returnType = method?.ReturnType;
        var element = returnType is { IsGenericType: true } && returnType.GetGenericTypeDefinition() == typeof(IList<>) ? returnType.GetGenericArguments()[0] : null;
        if (method is null || method.DeclaringType != typeof(IEngineeringServiceProvider) || element is null
            || !string.Equals(element.FullName, EngineeringServiceInfoName, StringComparison.Ordinal))
            throw Unsupported("This TIA Portal installation does not expose the documented IList<" + EngineeringServiceInfoName
                + "> IEngineeringServiceProvider.GetServiceInfos() (found: " + (method is null ? "no such method" : $"{method.ReturnType.FullName} declared by {method.DeclaringType?.FullName}") + ").");
        var typeProperty = LibraryRuntimeBinding.Property(element, "Type", out var error) ?? throw Unsupported(error);
        if (typeProperty.PropertyType != typeof(Type))
            throw Unsupported($"{EngineeringServiceInfoName}.Type is a {typeProperty.PropertyType.FullName}, not the documented System.Type.");
        object? returned;
        try
        {
            returned = Call(method, target, Array.Empty<object>());
        }
        catch (MissingMethodException exception)
        {
            throw Unsupported($"IEngineeringServiceProvider.GetServiceInfos() could not be called on '{target.GetType().FullName}': {exception.Message}");
        }

        if (returned is null)
            throw Unsupported($"IEngineeringServiceProvider.GetServiceInfos() returned null on '{target.GetType().FullName}', so its services cannot be listed.");
        var types = new List<Type>();
        foreach (var info in Items(returned, "IEngineeringServiceProvider.GetServiceInfos()"))
        {
            if (!element.IsInstanceOfType(info))
                throw Unsupported($"IEngineeringServiceProvider.GetServiceInfos() listed a '{info.GetType().FullName}', not an {EngineeringServiceInfoName}.");
            if (Get(typeProperty, info) is not Type type)
                throw Unsupported($"{EngineeringServiceInfoName}.Type of a listed service is null.");
            types.Add(type);
        }

        return types;
    }

    /// <summary>
    /// The service of exactly the named type when <paramref name="offered"/> lists it, through
    /// <c>((System.IServiceProvider)target).GetService(type)</c> as the Siemens skill retrieves a listed service, then the documented
    /// generic <c>GetService&lt;T&gt;()</c> closed over the listed type (K76, <see cref="TiaMcpServer.Contracts.Shared.ListedServices.Retrieve"/>);
    /// null when not offered.
    /// A listed service that <c>GetService</c> does not return (seen live on V21, 2026-11-02: <c>SafetySignatureProvider</c> of a
    /// block, <c>PlcAccessControlConfigurationProvider</c> of an S7-1500 CPU) is <c>unsupported_capability</c> here; a read for
    /// which the service is optional uses <see cref="TryService"/> and reports the part as unavailable instead.
    /// </summary>
    public static object? Service(object target, IReadOnlyList<Type> offered, string fullName)
    {
        var service = TryService(target, offered, fullName, out var unavailable);
        // Unsupported appends the one "Nothing was changed." (K18: the text appeared twice).
        if (unavailable is not null) throw Unsupported(unavailable);
        return service;
    }

    /// <summary>
    /// <see cref="Service"/> for reads: null with <paramref name="unavailable"/> null when the service is not listed, and null with
    /// the reason in <paramref name="unavailable"/> when it is listed but <c>GetService</c> returns null (D13/D14). An ambiguous
    /// listing or a target without <c>System.IServiceProvider</c> still throws: the listing itself is not as documented.
    /// </summary>
    public static object? TryService(object target, IReadOnlyList<Type> offered, string fullName, out string? unavailable)
    {
        unavailable = null;
        var matches = offered.Where(type => string.Equals(type.FullName, fullName, StringComparison.Ordinal)).Distinct().ToList();
        if (matches.Count == 0) return null;
        if (matches.Count > 1) throw Unsupported($"GetServiceInfos() lists {matches.Count} distinct types named '{fullName}'; none is chosen.");
        if (target is not IServiceProvider && target is not IEngineeringServiceProvider)
            throw Unsupported($"'{target.GetType().FullName}' implements neither System.IServiceProvider nor IEngineeringServiceProvider, through which a listed service is retrieved.");
        // K76: the non-generic GetService(Type) returned null for nine listed CPU services live; the documented generic
        // GetService<T>() closed over the listed type is tried next (ListedServices.Retrieve).
        var service = TiaMcpServer.Contracts.Shared.ListedServices.Retrieve(target, matches[0]);
        if (service is null) unavailable = UnavailableReason(fullName);
        return service;
    }

    /// <summary>The reason text of a listed service that GetService does not return (shared with the FakeWorker wording).</summary>
    public static string UnavailableReason(string fullName)
        => TiaMcpServer.Contracts.Shared.ListedServices.UnavailableReason(fullName);

    /// <summary>The live enum type named exactly <paramref name="fullName"/> in the assembly that defines <paramref name="anchor"/>'s type.</summary>
    public static Type EnumType(object anchor, string fullName)
    {
        var type = anchor.GetType().Assembly.GetType(fullName, throwOnError: false)
            ?? AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(fullName, throwOnError: false)).FirstOrDefault(found => found is not null);
        return type is { IsEnum: true } ? type
            : throw Unsupported($"This TIA Portal installation exposes no enum '{fullName}'.");
    }

    /// <summary>A documented property of a connection interface: (found, value); a missing member or a recoverable error is (false, null).</summary>
    public static (bool Found, object? Value) Read(object target, string name)
    {
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out _);
        if (property is null) return (false, null);
        try
        {
            return (true, property.GetValue(target));
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            if (exception.InnerException is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering)
                && engineering is not EngineeringObjectDisposedException)
                return (false, null);
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>A parameter value or connection property, typed by its runtime type.</summary>
    public static (string Kind, string? Text, List<string>? ModulePath) Describe(object? value)
    {
        switch (value)
        {
            case PlcTag tag:
                return (TechnologyObjectValueKinds.PlcTag, TechnologyObjectPolicy.Bound(SafeText(() => tag.Name)), null);
            case DeviceItem item:
                return (TechnologyObjectValueKinds.DeviceItem, TechnologyObjectPolicy.Bound(SafeText(() => item.Name)), ModulePath(item));
            case Channel channel:
            {
                var text = $"{SafeText(() => channel.Type.ToString())} {SafeText(() => channel.IoType.ToString())} {SafeText(() => channel.Number.ToString(System.Globalization.CultureInfo.InvariantCulture))}";
                var owner = SafeParent(channel) as DeviceItem;
                return (TechnologyObjectValueKinds.Channel, text, owner is null ? null : ModulePath(owner));
            }
            default:
                var (kind, described) = TechnologyObjectValues.Describe(value);
                return (kind, described, null);
        }
    }

    /// <summary>The names from the device down to <paramref name="item"/> through <c>IEngineeringInstance.Parent</c>; null when the chain does not end at a device.</summary>
    public static List<string>? ModulePath(DeviceItem item)
    {
        var names = new List<string>();
        object? current = item;
        for (var depth = 0; depth < 32 && current is not null; depth++)
        {
            switch (current)
            {
                case DeviceItem deviceItem:
                    names.Add(SafeText(() => deviceItem.Name) ?? string.Empty);
                    current = SafeParent(deviceItem);
                    break;
                case Device device:
                    names.Add(SafeText(() => device.Name) ?? string.Empty);
                    names.Reverse();
                    return names;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>Invokes a parameterless self-action named in the reference (<c>Delete</c>, <c>Disconnect</c>).</summary>
    public static void InvokeParameterless(object target, string name, string documented)
        => LibraryReflection.Invoke(target, name, Type.EmptyTypes, Array.Empty<object?>(), documented);

    public static WorkerOperationException Unsupported(string? message)
        => new(WorkerFailureCategories.UnsupportedCapability, (message ?? "This TIA Portal installation does not expose the documented member.") + " Nothing was changed.");

    private static object? SafeParent(IEngineeringInstance instance)
    {
        try
        {
            return instance.Parent;
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    private static string? SafeText(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    private static object? Get(PropertyInfo property, object target)
    {
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
}
