using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts.PlcSim;

namespace TiaMcpServer.PlcSimWorker.Runtime;

/// <summary>
/// Projects a value returned by the PLCSIM Advanced API without assuming its member names:
/// primitives, strings and enums (by name) become JSON scalars; any other struct or class becomes an
/// object of all its public instance fields and readable properties, in ordinal name order, nested
/// up to <see cref="DefaultMaxDepth"/> object levels. A one-dimensional array of such objects becomes a JSON array
/// of them (<see cref="IsDataObjectArray"/>). Other arrays, collections and deeper objects become
/// marker strings (<c>&lt;unsupported:Type&gt;</c>, <c>&lt;depthLimit:Type&gt;</c>), and a getter that throws
/// becomes <c>&lt;unreadable:ExceptionType&gt;</c>. The projection never invokes methods.
/// </summary>
internal static class PlcSimObjectProjector
{
    public const int DefaultMaxDepth = 2;
    public const int MaxStringChars = 4000;

    /// <summary>Elements written of one data-object array (a PLCSIM array tag has at most six dimensions).</summary>
    public const int MaxArrayElements = 64;

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<MemberInfo>> MembersByType = new();

    /// <param name="observeReadFailure">Sees every exception a getter threw (before it becomes an <c>&lt;unreadable&gt;</c> marker).</param>
    public static JsonElement Project(object? value, int maxDepth = DefaultMaxDepth, Action<Exception>? observeReadFailure = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value, 1, maxDepth, observeReadFailure);
        }

        return PlcSimValueCodec.Parse(Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>Public fields and readable, non-indexed properties of <paramref name="type"/>, in projection order (cached per type).</summary>
    public static IReadOnlyList<MemberInfo> DataMembers(Type type) => MembersByType.GetOrAdd(type, FindDataMembers);

    private static IReadOnlyList<MemberInfo> FindDataMembers(Type type)
    {
        var members = new List<MemberInfo>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (names.Add(field.Name))
                members.Add(field);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0 && property.GetGetMethod() is not null
                && names.Add(property.Name))
                members.Add(property);
        }

        return members.OrderBy(member => member.Name, StringComparer.Ordinal).ToList();
    }

    public static Type MemberType(MemberInfo member)
        => member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;

    public static string TypeName(Type type)
    {
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";
        if (type.IsByRef)
            return "ref " + TypeName(type.GetElementType()!);
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return TypeName(underlying) + "?";
        if (type.IsGenericType)
        {
            var name = type.Name;
            var tick = name.IndexOf('`');
            return (tick < 0 ? name : name.Substring(0, tick))
                + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
        }

        return type.Name;
    }

    private static void Write(Utf8JsonWriter writer, object? value, int depth, int maxDepth, Action<Exception>? observeReadFailure)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                return;
            case string text:
                writer.WriteStringValue(text.Length <= MaxStringChars ? text : text.Substring(0, MaxStringChars) + "<truncated>");
                return;
            case char character:
                writer.WriteStringValue(character.ToString());
                return;
            case Enum enumeration:
                writer.WriteStringValue(enumeration.ToString());
                return;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                writer.WriteRawValue(PlcSimValueCodec.ToJson(value).GetRawText());
                return;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeSpan timeSpan:
                writer.WriteStringValue(timeSpan.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Guid guid:
                writer.WriteStringValue(guid.ToString("D"));
                return;
        }

        var type = value.GetType();
        if (IsDataObjectArray(type))
        {
            WriteDataObjectArray(writer, (Array)value, depth, maxDepth, observeReadFailure);
            return;
        }

        if (type.IsPrimitive || type.IsArray || value is IEnumerable || value is Delegate || value is MemberInfo || value is Type)
        {
            writer.WriteStringValue($"<unsupported:{TypeName(type)}>");
            return;
        }

        if (depth > maxDepth)
        {
            writer.WriteStringValue($"<depthLimit:{TypeName(type)}>");
            return;
        }

        writer.WriteStartObject();
        foreach (var member in DataMembers(type))
        {
            writer.WritePropertyName(member.Name);
            object? memberValue;
            try
            {
                memberValue = member is FieldInfo field ? field.GetValue(value) : ((PropertyInfo)member).GetValue(value, null);
            }
            catch (Exception exception)
            {
                observeReadFailure?.Invoke(exception);
                writer.WriteStringValue($"<unreadable:{Unwrap(exception).GetType().Name}>");
                continue;
            }

            Write(writer, memberValue, depth + 1, maxDepth, observeReadFailure);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// A one-dimensional array of data objects (a struct or class that is projected as an object, e.g. the
    /// <c>SDimension[]</c> of <c>STagInfo.Dimension</c>, live 2026-11-02 reported as <c>&lt;unsupported:SDimension[]&gt;</c>).
    /// Arrays of scalars, strings, enums and nested arrays stay <c>&lt;unsupported:...&gt;</c>.
    /// </summary>
    internal static bool IsDataObjectArray(Type type)
    {
        if (!type.IsArray || type.GetArrayRank() != 1) return false;
        var element = type.GetElementType()!;
        return !(element.IsPrimitive || element.IsEnum || element.IsArray || element.IsPointer || element == typeof(string) || element == typeof(object)
            || element == typeof(decimal) || element == typeof(DateTime) || element == typeof(DateTimeOffset) || element == typeof(TimeSpan) || element == typeof(Guid)
            || Nullable.GetUnderlyingType(element) is not null || typeof(IEnumerable).IsAssignableFrom(element) || typeof(Delegate).IsAssignableFrom(element)
            || typeof(MemberInfo).IsAssignableFrom(element) || typeof(Type).IsAssignableFrom(element));
    }

    /// <summary>
    /// Writes a data-object array as a JSON array: each element projected like an object at the array's own depth (an
    /// array adds no level), at most <see cref="MaxArrayElements"/> elements, then one <c>&lt;moreElements:N&gt;</c> marker.
    /// An empty array is <c>[]</c>; a null array stays null (written by the caller).
    /// </summary>
    private static void WriteDataObjectArray(Utf8JsonWriter writer, Array array, int depth, int maxDepth, Action<Exception>? observeReadFailure)
    {
        writer.WriteStartArray();
        var count = array.Length;
        for (var index = 0; index < Math.Min(count, MaxArrayElements); index++)
            Write(writer, array.GetValue(index), depth, maxDepth, observeReadFailure);
        if (count > MaxArrayElements)
            writer.WriteStringValue($"<moreElements:{(count - MaxArrayElements).ToString(CultureInfo.InvariantCulture)}>");
        writer.WriteEndArray();
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException or TypeInitializationException && exception.InnerException is not null)
            exception = exception.InnerException;
        return exception;
    }
}
