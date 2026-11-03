using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace TiaMcpServer.Contracts.PlcAlarms;

/// <summary>
/// Reads the result objects of the alarm exports and imports (<c>TextListXlsxResult</c>, <c>PlcAlarmTextXlsxResult</c>,
/// <c>SupervisionXlsxResult</c>, <c>AlarmClassExportImportResult</c>) by the documented member names <c>State</c>,
/// <c>LogFilePath</c>, <c>ErrorCount</c>, <c>WarningCount</c> and <c>Messages</c> (each message: <c>Message</c>, <c>State</c>).
/// Their types are not documented, so the members are bound by exact name on the declared return type of the call
/// (<see cref="Bind"/>) before the call runs: a result whose <c>State</c> does not bind fails closed before anything is
/// exported or imported. A name declared on several levels of the type hierarchy (a derived type hiding a base member)
/// binds the most derived declaration, never <see cref="AmbiguousMatchException"/>. A Siemens exception thrown by a
/// getter is rethrown unwrapped.
/// </summary>
public static class PlcAlarmResultReader
{
    public const int MaxMessages = 50;

    public sealed class Outcome
    {
        public string? RuntimeType { get; set; }

        public string? State { get; set; }

        public string? LogFilePath { get; set; }

        public int? ErrorCount { get; set; }

        public int? WarningCount { get; set; }

        public List<string>? Messages { get; set; }

        public List<string> Unreadable { get; } = new List<string>();
    }

    /// <summary>The members of one result type, bound before the call.</summary>
    public sealed class Binding
    {
        internal Binding(Type type, bool withCounts)
        {
            Type = type;
            WithCounts = withCounts;
        }

        public Type Type { get; }

        public bool WithCounts { get; }

        internal PropertyInfo? State { get; set; }

        internal PropertyInfo? LogFilePath { get; set; }

        internal PropertyInfo? ErrorCount { get; set; }

        internal PropertyInfo? WarningCount { get; set; }

        internal PropertyInfo? Messages { get; set; }

        internal PropertyInfo? MessageText { get; set; }

        internal PropertyInfo? MessageState { get; set; }

        /// <summary>Optional members that did not bind (read as null and named in <see cref="Outcome.Unreadable"/>).</summary>
        public List<string> Missing { get; } = new List<string>();

        /// <summary>True when the required <c>State</c> bound.</summary>
        public bool IsUsable => State is not null;

        /// <summary>The bound members for a planning record, e.g. "TextListXlsxResult.State".</summary>
        public string Describe()
            => $"{Type.Name}.{string.Join("/", new[] { State, LogFilePath, ErrorCount, WarningCount, Messages }.Where(p => p is not null).Select(p => p!.Name))}";
    }

    /// <summary>
    /// Binds the documented result members on <paramref name="resultType"/> (the call's declared return type, or its
    /// <see cref="MethodInfo.ReturnType"/> for a runtime-bound call). Check <see cref="Binding.IsUsable"/> before calling.
    /// </summary>
    public static Binding Bind(Type resultType, bool withCounts)
    {
        if (resultType is null) throw new ArgumentNullException(nameof(resultType));
        var binding = new Binding(resultType, withCounts) { State = Property(resultType, "State") };
        if (withCounts)
        {
            binding.ErrorCount = Optional(resultType, "ErrorCount", binding);
            binding.WarningCount = Optional(resultType, "WarningCount", binding);
            binding.Messages = Optional(resultType, "Messages", binding);
            if (binding.Messages is not null && ElementType(binding.Messages.PropertyType) is { } element)
            {
                binding.MessageText = Property(element, "Message");
                binding.MessageState = Property(element, "State");
            }
        }
        else
        {
            binding.LogFilePath = Optional(resultType, "LogFilePath", binding);
        }

        return binding;
    }

    /// <summary>
    /// Binds only <c>State</c>: for a result type whose reference documents no other member
    /// (<c>SystemdiagnosticsSettingsExportImportResult</c>, Siemens.Engineering.Base.xml V21: State only).
    /// </summary>
    public static Binding BindStateOnly(Type resultType)
    {
        if (resultType is null) throw new ArgumentNullException(nameof(resultType));
        return new Binding(resultType, withCounts: false) { State = Property(resultType, "State") };
    }

    /// <summary>A refusal for a result type whose State does not bind; null when it binds.</summary>
    public static string? Refusal(Binding binding, string documented)
        => binding.IsUsable ? null
            : $"{binding.Type.FullName}, the result of {documented}, has no readable public State property on this TIA Portal installation, so the outcome could not be judged; nothing was called.";

    /// <summary>Reads <paramref name="result"/> through <paramref name="binding"/> (bound before the call).</summary>
    public static Outcome Read(object? result, Binding binding)
    {
        if (binding is null) throw new ArgumentNullException(nameof(binding));
        var outcome = new Outcome { RuntimeType = result?.GetType().FullName };
        if (result is null)
        {
            outcome.Unreadable.Add("the call returned no result object");
            return outcome;
        }

        outcome.Unreadable.AddRange(binding.Missing.Select(name => $"{binding.Type.Name}.{name}"));
        if (!binding.Type.IsInstanceOfType(result))
        {
            outcome.Unreadable.Add($"the result is a {result.GetType().FullName}, not the declared {binding.Type.FullName}");
            return outcome;
        }

        if (binding.State is null) outcome.Unreadable.Add($"{binding.Type.Name}.State");
        outcome.State = Text(Get(binding.State, result));
        if (!binding.WithCounts)
        {
            outcome.LogFilePath = PathText(Get(binding.LogFilePath, result));
            return outcome;
        }

        outcome.ErrorCount = Count(Get(binding.ErrorCount, result));
        outcome.WarningCount = Count(Get(binding.WarningCount, result));
        if (Get(binding.Messages, result) is IEnumerable messages and not string)
        {
            outcome.Messages = new List<string>();
            foreach (var message in messages)
            {
                if (outcome.Messages.Count >= MaxMessages)
                {
                    outcome.Messages.Add($"(more than {MaxMessages} messages; the rest is omitted)");
                    break;
                }

                if (message is null) continue;
                // A message type the collection does not declare is read by name on the runtime type (informative only).
                var text = Text(Get(binding.MessageText ?? Property(message.GetType(), "Message"), message)) ?? message.ToString();
                var state = Text(Get(binding.MessageState ?? Property(message.GetType(), "State"), message));
                outcome.Messages.Add(PlcAlarmPolicy.Bound(state is null ? text : $"{state}: {text}", 1000) ?? string.Empty);
            }
        }

        return outcome;
    }

    /// <summary>Binds on the runtime type of <paramref name="result"/> and reads it (tests and results of undeclared type).</summary>
    public static Outcome Read(object? result, bool withCounts)
        => result is null ? Read(null, Bind(typeof(object), withCounts)) : Read(result, Bind(result.GetType(), withCounts));

    /// <summary>
    /// The readable public instance property <paramref name="name"/> without index parameters, declared on the most derived
    /// type that declares one (so a hiding redeclaration never raises <see cref="AmbiguousMatchException"/>).
    /// </summary>
    public static PropertyInfo? Property(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var declared = current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(candidate => candidate.Name == name && candidate.CanRead && candidate.GetIndexParameters().Length == 0).ToList();
            if (declared.Count == 1) return declared[0];
            if (declared.Count > 1) return null;
        }

        // Interfaces (a result type declared as an interface) are searched last, exactly once each.
        var fromInterfaces = type.GetInterfaces().SelectMany(i => i.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(candidate => candidate.Name == name && candidate.CanRead && candidate.GetIndexParameters().Length == 0).ToList();
        return fromInterfaces.Count == 1 ? fromInterfaces[0] : null;
    }

    private static PropertyInfo? Optional(Type type, string name, Binding binding)
    {
        var property = Property(type, name);
        if (property is null) binding.Missing.Add(name);
        return property;
    }

    private static Type? ElementType(Type collection)
        => collection.IsGenericType && collection.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? collection.GetGenericArguments()[0]
            : collection.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0]).Where(t => t != typeof(object)).Distinct().SingleOrDefaultSafe();

    private static Type? SingleOrDefaultSafe(this IEnumerable<Type> types)
    {
        var list = types.Take(2).ToList();
        return list.Count == 1 ? list[0] : null;
    }

    private static object? Get(PropertyInfo? property, object target)
    {
        if (property is null) return null;
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

    private static string? Text(object? value) => value switch
    {
        null => null,
        Enum member => member.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string? PathText(object? value) => value switch
    {
        null => null,
        FileSystemInfo info => info.FullName,
        _ => value.ToString(),
    };

    private static int? Count(object? value) => value switch
    {
        int number => number,
        long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
        short number => number,
        uint number when number <= int.MaxValue => (int)number,
        _ => null,
    };
}
