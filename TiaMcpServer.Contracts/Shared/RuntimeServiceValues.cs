using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>A Siemens-free refusal with its worker failure category; the worker and the FakeWorker turn it into their own failure.</summary>
public sealed class RuntimeBindingException : Exception
{
    public RuntimeBindingException(string category, string message) : base(message) => Category = category;

    public string Category { get; }
}

/// <summary>
/// A value read from a Siemens object whose type the V21 reference does not document, projected as text. <c>state</c>:
/// <c>value</c> (non-empty text), <c>empty</c> (an empty string or empty byte sequence), <c>null</c> (the member returned null),
/// <c>unreadable</c> (the member threw a recoverable Siemens exception; <c>error</c> holds its type and message) or
/// <c>missing</c> (the live object has no such readable member; <c>error</c> says so). <c>runtimeType</c> is the CLR type of
/// the returned value, null when nothing was returned.
/// </summary>
public sealed class RuntimeValueInfo
{
    public string State { get; set; } = RuntimeValueStates.Null;

    public string? Value { get; set; }

    public string? RuntimeType { get; set; }

    public string? Error { get; set; }
}

public static class RuntimeValueStates
{
    public const string Value = "value";
    public const string Empty = "empty";
    public const string Null = "null";
    public const string Unreadable = "unreadable";
    public const string Missing = "missing";

    public static IReadOnlyList<string> All { get; } = new[] { Value, Empty, Null, Unreadable, Missing };
}

/// <summary>
/// Siemens-free helpers for services found at runtime among <c>IEngineeringServiceProvider.GetServiceInfos()</c> and for
/// members of undocumented type: the service type is matched by exact full name (never by a looser rule), a property or
/// method is bound by exact name and documented parameter types (<see cref="LibraryRuntimeBinding"/>), and a value is
/// projected as text without interpreting it. Shared by the worker and the FakeWorker.
/// </summary>
public static class RuntimeServiceValues
{
    public const int MaxValueChars = 4000;

    /// <summary>
    /// The listed service of exactly <paramref name="fullName"/>: (null, null) when it is not listed, (null, reason) when it is
    /// listed but <paramref name="retrieve"/> returns null (<see cref="ListedServices"/>), (service, null) otherwise. Two distinct
    /// listed types of that name are a refusal (unsupported_capability).
    /// </summary>
    public static (object? Service, string? Unavailable) Find(IReadOnlyList<Type> offered, Func<Type, object?> retrieve, string fullName)
    {
        var matches = offered.Where(type => string.Equals(type.FullName, fullName, StringComparison.Ordinal)).Distinct().ToList();
        if (matches.Count == 0) return (null, null);
        if (matches.Count > 1)
            throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                $"GetServiceInfos() lists {matches.Count} distinct types named '{fullName}'; none is chosen.");
        var service = retrieve(matches[0]);
        return service is null ? (null, ListedServices.UnavailableReason(fullName)) : (service, null);
    }

    /// <summary>The reason text for a service the object does not list.</summary>
    public static string NotListed(string objectDescription, string fullName)
        => $"{objectDescription} does not list '{fullName}' among its GetServiceInfos(); this TIA Portal installation offers no such service on it.";

    /// <summary>Reads the public readable instance property <paramref name="name"/> on the live object and projects it.</summary>
    public static RuntimeValueInfo ReadProperty(object target, string name, Func<Exception, bool> recoverable)
    {
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out var error);
        if (property is null) return new RuntimeValueInfo { State = RuntimeValueStates.Missing, Error = error };
        object? value;
        try
        {
            value = property.GetValue(target, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            if (!recoverable(exception.InnerException))
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }

            return new RuntimeValueInfo
            {
                State = RuntimeValueStates.Unreadable,
                Error = Bound($"{exception.InnerException.GetType().Name}: {exception.InnerException.Message}"),
            };
        }

        return Project(value);
    }

    /// <summary>
    /// The value as text: a string as is, a byte sequence as upper-case hexadecimal, an enum by member name, a formattable value
    /// with the invariant culture, anything else by <c>ToString()</c>; at most <see cref="MaxValueChars"/> characters.
    /// </summary>
    public static RuntimeValueInfo Project(object? value)
    {
        if (value is null) return new RuntimeValueInfo { State = RuntimeValueStates.Null };
        var text = value switch
        {
            string s => s,
            byte[] bytes => Hex(bytes),
            IEnumerable<byte> sequence => Hex(sequence.ToArray()),
            Enum member => member.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
        return new RuntimeValueInfo
        {
            State = text.Length == 0 ? RuntimeValueStates.Empty : RuntimeValueStates.Value,
            Value = text.Length == 0 ? string.Empty : Bound(text),
            RuntimeType = value.GetType().FullName,
        };
    }

    /// <summary>Invokes the single public instance method <paramref name="name"/>(<paramref name="argumentTypes"/>); a missing one is api_member_missing.</summary>
    public static object? Invoke(object target, string name, Type[] argumentTypes, object?[] arguments, string documented)
    {
        var method = LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ApiMemberMissing,
                $"{error ?? $"No {name} on '{target.GetType().FullName}'."} ({documented}) Nothing was changed.");
        return Call(method, target, arguments);
    }

    /// <summary>Calls a bound method; an exception it throws is rethrown unwrapped, so the shared classification applies.</summary>
    public static object? Call(MethodInfo method, object target, object?[] arguments)
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

    /// <summary>The elements of a returned collection; a non-collection is a refusal.</summary>
    public static List<object?> Elements(object? collection, string documented)
    {
        if (collection is not IEnumerable enumerable || collection is string)
            throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed,
                $"{documented} returned {(collection is null ? "null" : $"a {collection.GetType().FullName}")}, not a collection.");
        return enumerable.Cast<object?>().ToList();
    }

    public static string Bound(string text) => text.Length <= MaxValueChars ? text : text.Substring(0, MaxValueChars) + " [TRUNCATED]";

    private static string Hex(byte[] bytes)
    {
        var builder = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
