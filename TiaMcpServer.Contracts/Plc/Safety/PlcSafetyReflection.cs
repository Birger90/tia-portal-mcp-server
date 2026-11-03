using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using TiaMcpServer.Contracts.Library;

namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>A Siemens member the operation needs is absent from the installed API (exact name and kind).</summary>
public sealed class PlcSafetyApiMissingException(string member, string detail)
    : Exception($"api_member_missing: {member}: {detail}")
{
    public string Member { get; } = member;
}

/// <summary>
/// Siemens-free runtime access to the fail-safe objects, shared by the worker and the tests (which use plain .NET
/// stand-ins). Properties are found by exact name on the runtime type; methods by exact name and argument types through
/// <see cref="LibraryRuntimeBinding"/>. A missing member is <see cref="PlcSafetyValueStates.ApiMemberMissing"/>; a member
/// that throws is <see cref="PlcSafetyValueStates.Unreadable"/>, unless <c>isFatal</c> says the exception must escape
/// (the worker passes its Portal-loss rule, so a lost TIA Portal is never reported as an unreadable value).
/// </summary>
public static class PlcSafetyReflection
{
    /// <summary>Reads one property; falls back to <paramref name="attribute"/> (IEngineeringObject.GetAttribute) when no property of that name exists.</summary>
    public static PlcSafetyValueInfo Read(object target, string name, Func<Exception, bool> isFatal, Func<string, object?>? attribute = null)
    {
        var info = new PlcSafetyValueInfo { Name = name };
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out _);
        try
        {
            object? value;
            if (property is not null) value = Unwrap(() => property.GetValue(target));
            else if (attribute is not null) value = attribute(name);
            else
            {
                info.State = PlcSafetyValueStates.ApiMemberMissing;
                info.Error = $"api_member_missing: {target.GetType().FullName} has no readable property {name}.";
                return info;
            }

            Describe(info, value);
        }
        catch (Exception exception) when (!isFatal(exception))
        {
            info.State = PlcSafetyValueStates.Unreadable;
            info.Error = Message(exception);
        }

        return info;
    }

    /// <summary>Calls one parameterless getter method (GlobalSettings exposes its values as methods).</summary>
    public static PlcSafetyValueInfo CallGetter(object target, string name, Func<Exception, bool> isFatal)
    {
        var info = new PlcSafetyValueInfo { Name = name };
        var method = LibraryRuntimeBinding.Find(target.GetType(), name, Type.EmptyTypes, out var error);
        if (method is null || method.ReturnType == typeof(void))
        {
            info.State = PlcSafetyValueStates.ApiMemberMissing;
            info.Error = "api_member_missing: " + (error ?? $"{target.GetType().FullName}.{name}() returns nothing.");
            return info;
        }

        try
        {
            Describe(info, Unwrap(() => method.Invoke(target, Array.Empty<object>())));
        }
        catch (Exception exception) when (!isFatal(exception))
        {
            info.State = PlcSafetyValueStates.Unreadable;
            info.Error = Message(exception);
        }

        return info;
    }

    /// <summary>A property value, or <see cref="PlcSafetyApiMissingException"/> when no such property exists. Exceptions of the getter escape.</summary>
    public static object? Get(object target, string name)
    {
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out var error)
            ?? throw new PlcSafetyApiMissingException($"{target.GetType().FullName}.{name}", error ?? "no readable property");
        return Unwrap(() => property.GetValue(target));
    }

    /// <summary>A boolean property, or <see cref="PlcSafetyApiMissingException"/> when it is absent or not a bool.</summary>
    public static bool GetBool(object target, string name)
        => Get(target, name) is bool value ? value
            : throw new PlcSafetyApiMissingException($"{target.GetType().FullName}.{name}", "the property did not return a System.Boolean");

    /// <summary>
    /// Invokes the single public method named <paramref name="name"/> that accepts <paramref name="argumentTypes"/>;
    /// <see cref="PlcSafetyApiMissingException"/> when there is none. The method's own exception escapes unwrapped.
    /// </summary>
    public static object? Invoke(object target, string name, Type[] argumentTypes, object?[] arguments)
    {
        var method = LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new PlcSafetyApiMissingException($"{target.GetType().FullName}.{name}({string.Join(", ", argumentTypes.Select(t => t.Name))})", error ?? "no such method");
        return Unwrap(() => method.Invoke(target, arguments));
    }

    /// <summary>The elements of an enumerable Siemens composition; <see cref="PlcSafetyApiMissingException"/> when it is not enumerable.</summary>
    public static IEnumerable<object> Items(object? collection, string member)
    {
        if (collection is null) return Array.Empty<object>();
        if (collection is not IEnumerable enumerable) throw new PlcSafetyApiMissingException(member, $"'{collection.GetType().FullName}' is not enumerable");
        var items = new List<object>();
        foreach (var item in enumerable) if (item is not null) items.Add(item);
        return items;
    }

    /// <summary>The <c>Signatures</c> of a <c>SafetySignatureProvider</c>: each <c>Type</c> and <c>Value</c>.</summary>
    public static List<PlcSafetySignatureInfo> Signatures(object provider)
    {
        var rows = new List<PlcSafetySignatureInfo>();
        foreach (var signature in Items(Get(provider, "Signatures"), $"{provider.GetType().FullName}.Signatures"))
        {
            var (text, hex) = PlcSafetyPolicy.SignatureText(Get(signature, "Value"));
            rows.Add(new PlcSafetySignatureInfo { Type = PlcSafetyPolicy.Text(Get(signature, "Type")), Value = text, Hex = hex });
        }

        return rows;
    }

    /// <summary>The text of a SafetySystemVersion: its ToString() (the Siemens skill matches it against "V2.4"), else its Value.</summary>
    public static string? VersionText(object? version)
    {
        if (version is null) return null;
        var text = version.ToString();
        if (!string.IsNullOrEmpty(text) && !string.Equals(text, version.GetType().FullName, StringComparison.Ordinal)) return text;
        var property = LibraryRuntimeBinding.Property(version.GetType(), "Value", out _);
        return property is null ? text : PlcSafetyPolicy.Text(Unwrap(() => property.GetValue(version)));
    }

    private static void Describe(PlcSafetyValueInfo info, object? value)
    {
        info.State = PlcSafetyValueStates.Read;
        info.ValueType = value?.GetType().FullName;
        info.Value = value is null || value is bool or string or Enum || value.GetType().IsPrimitive || value is decimal or TimeSpan
            ? PlcSafetyPolicy.Text(value)
            : VersionText(value);
    }

    private static object? Unwrap(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static string Message(Exception exception)
    {
        var message = exception.Message;
        return message.Length > 1000 ? message.Substring(0, 1000) : message;
    }
}
