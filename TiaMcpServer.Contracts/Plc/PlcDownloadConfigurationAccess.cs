using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;

namespace TiaMcpServer.Contracts.Plc;

/// <summary>
/// How <see cref="PlcDownloadCallbackRecorder"/> reads and answers a live download configuration
/// without referencing Siemens types. Answer methods return an outcome for the fail-closed cases
/// (missing member, unknown selection, unset environment variable) and let the setter's own
/// exception propagate (<c>decision_rejected</c>).
/// </summary>
public interface IPlcDownloadConfigurationAccess
{
    /// <summary>The runtime type name recorded as evidence.</summary>
    string TypeName(object configuration);

    /// <summary>The exact canonical type (<see cref="PlcDownloadPolicy.CanonicalConfigurationType(Type)"/>), or null. An exception counts as null.</summary>
    string? CanonicalType(object configuration);

    /// <summary>The configuration message, or null when unreadable. Its exceptions propagate unrecorded.</summary>
    string? ReadMessage(object configuration);

    /// <summary>What the live object exposes (kind and, for a selection, the enum member names). An exception counts as <c>other</c>.</summary>
    PlcDownloadConfigurationShape Describe(object configuration);

    /// <summary>Sets <c>CurrentSelection</c> to the enum member named exactly <paramref name="selection"/>.</summary>
    PlcDownloadApplyOutcome ApplySelection(object configuration, string selection);

    /// <summary>Sets <c>Checked</c> to true.</summary>
    PlcDownloadApplyOutcome ApplyCheck(object configuration);

    /// <summary>Calls <c>SetPassword(SecureString)</c> with the value of the environment variable <paramref name="variable"/>.</summary>
    PlcDownloadApplyOutcome ApplyPassword(object configuration, string variable);
}

/// <summary>A configuration's kind and, for a selection configuration, its live enum member names.</summary>
public sealed class PlcDownloadConfigurationShape
{
    public PlcDownloadConfigurationShape(string kind, IReadOnlyList<string>? availableSelections = null)
    {
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        AvailableSelections = availableSelections ?? Array.Empty<string>();
    }

    public static PlcDownloadConfigurationShape Other { get; } = new(PlcDownloadPolicy.Kinds.Other);

    /// <summary>One of <see cref="PlcDownloadPolicy.Kinds"/>.</summary>
    public string Kind { get; }

    public IReadOnlyList<string> AvailableSelections { get; }
}

/// <summary>The fail-closed result of one answer: applied, or the abort reason with a detail that never holds a secret.</summary>
public sealed class PlcDownloadApplyOutcome
{
    private PlcDownloadApplyOutcome(string? abortReason, string? detail)
    {
        AbortReason = abortReason;
        Detail = detail;
    }

    public static PlcDownloadApplyOutcome Success { get; } = new(null, null);

    public bool Applied => AbortReason is null;

    public string? AbortReason { get; }

    public string? Detail { get; }

    public static PlcDownloadApplyOutcome NotApplicable(string detail) => new(PlcDownloadPolicy.AbortReasons.DecisionNotApplicable, detail);

    public static PlcDownloadApplyOutcome SelectionUnavailable(string detail) => new(PlcDownloadPolicy.AbortReasons.SelectionUnavailable, detail);

    public static PlcDownloadApplyOutcome PasswordUnavailable(string detail) => new(PlcDownloadPolicy.AbortReasons.PasswordUnavailable, detail);
}

/// <summary>
/// The Siemens-free production <see cref="IPlcDownloadConfigurationAccess"/>, used by the worker and
/// the FakeWorker. It answers any configuration generically by reflection on the runtime object, never
/// by a compiled cast: <c>CurrentSelection</c> (exactly one public readable and writable enum
/// property; the value is parsed by exact member name against that property's own enum type with
/// <c>Enum.IsDefined</c>), <c>Checked</c> (exactly one public readable and writable <c>bool</c>
/// property) and <c>SetPassword</c> (exactly one public instance method taking one
/// <see cref="SecureString"/>). A missing or ambiguous member fails closed
/// (<c>decision_not_applicable</c>). A password is read from the process environment, copied into a
/// read-only <see cref="SecureString"/> that is disposed after the call, and is never recorded.
/// </summary>
public sealed class PlcDownloadReflectionAccess : IPlcDownloadConfigurationAccess
{
    public const string SelectionPropertyName = "CurrentSelection";
    public const string CheckPropertyName = "Checked";
    public const string PasswordMethodName = "SetPassword";

    private readonly Func<object, string?> _readMessage;
    private readonly Func<string, string?> _readEnvironment;
    private readonly Shared.ICredentialReader? _credentials;

    /// <param name="readMessage">Reads <c>DownloadConfiguration.Message</c> (the worker uses its compiled member).</param>
    /// <param name="readEnvironment">Reads an environment variable of this process; null when unset.</param>
    /// <param name="credentials">Reads Windows generic credentials; null uses <see cref="Shared.WindowsCredentialReader"/>.</param>
    public PlcDownloadReflectionAccess(Func<object, string?> readMessage, Func<string, string?> readEnvironment, Shared.ICredentialReader? credentials = null)
    {
        _readMessage = readMessage ?? throw new ArgumentNullException(nameof(readMessage));
        _readEnvironment = readEnvironment ?? throw new ArgumentNullException(nameof(readEnvironment));
        _credentials = credentials;
    }

    public string TypeName(object configuration) => configuration.GetType().FullName ?? configuration.GetType().Name;

    public string? CanonicalType(object configuration) => PlcDownloadPolicy.CanonicalConfigurationType(configuration.GetType());

    public string? ReadMessage(object configuration) => _readMessage(configuration);

    public PlcDownloadConfigurationShape Describe(object configuration)
    {
        var type = configuration.GetType();
        if (PasswordSetter(type) is not null) return new PlcDownloadConfigurationShape(PlcDownloadPolicy.Kinds.Password);
        if (CheckProperty(type) is not null) return new PlcDownloadConfigurationShape(PlcDownloadPolicy.Kinds.Check);
        if (SelectionProperty(type) is { } selection)
            return new PlcDownloadConfigurationShape(PlcDownloadPolicy.Kinds.Selection,
                Enum.GetNames(selection.PropertyType).Take(PlcDownloadPolicy.MaxAvailableSelections).ToArray());
        return PlcDownloadConfigurationShape.Other;
    }

    public PlcDownloadApplyOutcome ApplySelection(object configuration, string selection)
    {
        var property = SelectionProperty(configuration.GetType());
        if (property is null)
            return PlcDownloadApplyOutcome.NotApplicable(
                "it has no single public readable and writable enum CurrentSelection property, so a selection name cannot be applied");
        // Exact, case-sensitive member name of the live property's own enum type; never a number or a flag combination.
        if (!PlcDownloadPolicy.IsIdentifier(selection) || !Enum.IsDefined(property.PropertyType, selection))
            return PlcDownloadApplyOutcome.SelectionUnavailable($"{property.PropertyType.Name} has no member with that exact name");
        var value = Enum.Parse(property.PropertyType, selection, ignoreCase: false);
        Invoke(() => property.SetValue(configuration, value, null));
        return PlcDownloadApplyOutcome.Success;
    }

    public PlcDownloadApplyOutcome ApplyCheck(object configuration)
    {
        var property = CheckProperty(configuration.GetType());
        if (property is null)
            return PlcDownloadApplyOutcome.NotApplicable("it has no single public readable and writable bool Checked property, so true cannot be applied");
        Invoke(() => property.SetValue(configuration, true, null));
        return PlcDownloadApplyOutcome.Success;
    }

    public PlcDownloadApplyOutcome ApplyPassword(object configuration, string variable)
    {
        var setter = PasswordSetter(configuration.GetType());
        if (setter is null)
            return PlcDownloadApplyOutcome.NotApplicable("it has no single public SetPassword(SecureString) method, so it is not a password configuration");
        if (!Shared.PasswordSources.IsSource(variable))
            return PlcDownloadApplyOutcome.PasswordUnavailable("The password entry does not name a valid environment variable or credential target.");
        using var secure = Shared.PasswordSources.Resolve(variable, _readEnvironment, _credentials, out var reason);
        if (secure is null)
            return PlcDownloadApplyOutcome.PasswordUnavailable(Shared.PasswordSources.KindOf(variable) == Shared.PasswordSourceKind.Environment
                ? "The environment variable named for this configuration is not set or is empty in the worker process."
                : reason ?? "The credential named for this configuration could not be read.");
        Invoke(() => setter.Invoke(configuration, new object[] { secure }));
        return PlcDownloadApplyOutcome.Success;
    }

    private static PropertyInfo? SelectionProperty(Type type)
        => SingleProperty(type, SelectionPropertyName, property => property.PropertyType.IsEnum);

    private static PropertyInfo? CheckProperty(Type type)
        => SingleProperty(type, CheckPropertyName, property => property.PropertyType == typeof(bool));

    private static PropertyInfo? SingleProperty(Type type, string name, Func<PropertyInfo, bool> fits)
    {
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => string.Equals(property.Name, name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1) return null;
        var found = matches[0];
        return fits(found) && found.GetIndexParameters().Length == 0
            && found.GetGetMethod(nonPublic: false) is not null && found.GetSetMethod(nonPublic: false) is not null
            ? found
            : null;
    }

    private static MethodInfo? PasswordSetter(Type type)
    {
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => string.Equals(method.Name, PasswordMethodName, StringComparison.Ordinal) && !method.IsGenericMethodDefinition)
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(SecureString);
            })
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Reflection wraps the member's own exception; rethrow it unchanged so its classification survives.</summary>
    private static void Invoke(Action action)
    {
        try
        {
            action();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
}
