using System.Globalization;
using System.Reflection;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>
/// Siemens-free reading of what <c>PlcSoftware.UpdateProgram()</c> returned. The V21 reference documents
/// the method but not its return type, so the returned object (if any) is read schema-free: its public
/// readable scalar properties are recorded, and a state, error-count or success member that indicates
/// failure fails the call (fail closed). Without such a member the outcome is <c>uninterpreted</c>,
/// never plain success.
/// </summary>
public static class PlcUpdateProgramOutcome
{
    public const string Succeeded = "succeeded";
    public const string Uninterpreted = "uninterpreted";
    public const string Failed = "failed";

    public const int MaxMembers = 20;

    private static readonly string[] StateNames = { "State", "Status", "Result", "ResultState" };
    private static readonly string[] ErrorCountNames = { "ErrorCount", "Errors", "ErrorsCount" };
    private static readonly string[] SuccessNames = { "Success", "Succeeded", "IsSuccessful", "Successful" };

    public sealed class Interpretation
    {
        public Interpretation(string outcome, List<PlcEngineeringAttributeInfo> members, string reason)
        {
            Outcome = outcome;
            Members = members;
            Reason = reason;
        }

        /// <summary><see cref="Succeeded"/>, <see cref="Uninterpreted"/> or <see cref="Failed"/>.</summary>
        public string Outcome { get; }

        /// <summary>The public readable scalar members of the returned object (name and invariant text), at most <see cref="MaxMembers"/>.</summary>
        public List<PlcEngineeringAttributeInfo> Members { get; }

        /// <summary>Why the outcome was chosen, for a message or warning.</summary>
        public string Reason { get; }
    }

    public static Interpretation Interpret(object? returned, Func<Exception, bool>? isFatal = null)
    {
        var fatal = isFatal ?? (_ => false);
        if (returned is null)
            return new Interpretation(Uninterpreted, new List<PlcEngineeringAttributeInfo>(),
                "UpdateProgram() returned no result object, so its outcome cannot be read.");
        var members = new List<PlcEngineeringAttributeInfo>();
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in returned.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            object? value;
            try
            {
                value = property.GetValue(returned);
            }
            catch (Exception ex) when (!fatal(ex is TargetInvocationException { InnerException: { } inner } ? inner : ex))
            {
                continue;
            }

            values[property.Name] = value;
            if (members.Count < MaxMembers)
                members.Add(new PlcEngineeringAttributeInfo { Name = property.Name, Value = PlcOnlinePolicy.BoundText(Text(value)) });
        }

        var type = returned.GetType().FullName ?? returned.GetType().Name;
        foreach (var name in StateNames)
        {
            if (values.TryGetValue(name, out var state) && Text(state) is { } text
                && (Contains(text, "Error") && !Contains(text, "NoError") || Contains(text, "Fail") || Contains(text, "Abort") || Contains(text, "Cancel")))
                return new Interpretation(Failed, members, $"{type}.{name} is '{text}'.");
        }

        foreach (var name in ErrorCountNames)
        {
            if (values.TryGetValue(name, out var count) && AsLong(count) is > 0 and var errors)
                return new Interpretation(Failed, members, $"{type}.{name} is {errors}.");
        }

        foreach (var name in SuccessNames)
        {
            if (values.TryGetValue(name, out var success) && success is false)
                return new Interpretation(Failed, members, $"{type}.{name} is false.");
        }

        foreach (var name in StateNames)
        {
            if (values.TryGetValue(name, out var state) && Text(state) is { } text
                && (Contains(text, "Success") || Contains(text, "Succeeded") || string.Equals(text, "Ok", StringComparison.OrdinalIgnoreCase)
                    || Contains(text, "NoError") || Contains(text, "Warning")))
                return new Interpretation(Succeeded, members, $"{type}.{name} is '{text}'.");
        }

        foreach (var name in SuccessNames)
        {
            if (values.TryGetValue(name, out var success) && success is true)
                return new Interpretation(Succeeded, members, $"{type}.{name} is true.");
        }

        foreach (var name in ErrorCountNames)
        {
            if (values.TryGetValue(name, out var count) && AsLong(count) == 0)
                return new Interpretation(Succeeded, members, $"{type}.{name} is 0.");
        }

        return new Interpretation(Uninterpreted, members,
            $"UpdateProgram() returned {type}, which has no state, error-count or success member this tool can interpret.");
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(decimal)
            || underlying == typeof(DateTime) || underlying == typeof(TimeSpan) || underlying == typeof(Guid);
    }

    private static string? Text(object? value) => value switch
    {
        null => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static long? AsLong(object? value)
    {
        if (value is null || value is bool || value is Enum) return null;
        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static bool Contains(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
}
