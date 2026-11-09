using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security;

namespace TiaMcpServer.Contracts.PlcOnline;

/// <summary>Audit N2: the caller's <c>onlineUser</c>, the user a user-based PLC login (UMAC) authenticates as. Never a password.</summary>
public sealed class PlcOnlineUserInfo
{
    /// <summary>The user name set on <c>OnlineCredentials.Name</c>.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Optional exact member name of the live <c>UserType</c> enum set on <c>OnlineCredentials.Type</c>; null leaves TIA Portal's value.</summary>
    public string? UserType { get; set; }
}

/// <summary>
/// Audit N2: answering TIA Portal's user-based online login, Siemens-free and shared by worker and FakeWorker. Evidence: V21
/// <c>Siemens.Engineering.Base.xml</c>: <c>Siemens.Engineering.Online.Configurations.OnlineAuthenticationConfiguration</c> ("Object that
/// is used to authenticate user.") with <c>OnlineCredentials</c> ("Credentials for online authentication."), <c>IsSecureCommunication</c>
/// and <c>GetSupportedAuthenticationTypes()</c>; <c>OnlineCredentials</c> with <c>Name</c> ("The user name to legitimate."), <c>Type</c>
/// ("The user type to legitimate", enum <c>UserType</c>: <c>None</c>, <c>AnonymousUser</c>, <c>GlobalUser</c>, <c>PasswordOnly</c>,
/// <c>ProjectUser</c>, <c>SingleSignOnUser</c>) and <c>SetPassword(SecureString)</c>. None of these members is in the compile stubs;
/// all are bound on the live objects by exact name and type. Such a configuration is a password configuration for the recorders:
/// its password comes from <c>passwords</c> under the key <see cref="ConfigurationShortName"/> (or the password map), it is sent only
/// under the <see cref="PlcPasswordChannel"/> rule (<c>IsSecureCommunication</c>, opt-in <c>allowInsecurePasswordTransfer</c>), and the
/// user name comes from the call's <c>onlineUser</c>. Not live-tested.
/// </summary>
public static class PlcOnlineAuthentication
{
    public const string ConfigurationShortName = "OnlineAuthenticationConfiguration";
    public const string CredentialsPropertyName = "OnlineCredentials";
    public const string NamePropertyName = "Name";
    public const string TypePropertyName = "Type";
    public const string ArgumentName = "onlineUser";
    public const int MaxUserNameLength = 256;

    /// <summary>Null when valid: a non-blank user name without control characters, at most 256 characters, and an identifier user type.</summary>
    public static string? Validate(PlcOnlineUserInfo? user)
    {
        if (user is null) return null;
        if (string.IsNullOrWhiteSpace(user.UserName) || user.UserName.Length > MaxUserNameLength || user.UserName.Any(char.IsControl))
            return $"onlineUser.userName must be a non-blank user name of at most {MaxUserNameLength} characters without control characters.";
        if (user.UserType is not null && !PlcCallbackPolicy.IsIdentifier(user.UserType))
            return "onlineUser.userType must be a UserType member name (an identifier, e.g. ProjectUser or GlobalUser); omit it to keep TIA Portal's value.";
        return null;
    }

    /// <summary>True when the runtime type has the documented <c>OnlineCredentials</c> property with an answerable credentials type.</summary>
    public static bool IsAuthenticationType(Type type) => CredentialsProperty(type) is not null;

    /// <summary>The prepared answer, or the reason it cannot be given (never a secret).</summary>
    public sealed class Binding
    {
        private readonly Action<SecureString>? _apply;

        internal Binding(Action<SecureString> apply) => _apply = apply;

        internal Binding(bool selectionUnavailable, string error)
        {
            SelectionUnavailable = selectionUnavailable;
            Error = error;
        }

        /// <summary>Null when the answer can be given.</summary>
        public string? Error { get; }

        /// <summary>True when <see cref="Error"/> is an unknown <c>userType</c> (a selection that is not a member of the live enum).</summary>
        public bool SelectionUnavailable { get; }

        /// <summary>Sets <c>Name</c>, <c>Type</c> (when given) and calls <c>SetPassword</c>; the members' own exceptions propagate unwrapped.</summary>
        public void Apply(SecureString password) => (_apply ?? throw new InvalidOperationException(Error))(password);
    }

    /// <summary>
    /// Reads <c>OnlineCredentials</c> of the live configuration and binds <c>Name</c>, <c>Type</c> and <c>SetPassword(SecureString)</c>;
    /// nothing is set yet. A missing <paramref name="user"/>, member or user type is returned as the binding's error.
    /// </summary>
    public static Binding Bind(object configuration, PlcOnlineUserInfo? user)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));
        if (user is null || Validate(user) is not null)
            return new Binding(false, $"TIA Portal asks for a user-based online login ({ConfigurationShortName}); the call must name the user with "
                + $"{ArgumentName} {{ \"userName\": \"...\", \"userType\": \"ProjectUser\" (optional) }} next to the password source");
        var property = CredentialsProperty(configuration.GetType())
            ?? throw new InvalidOperationException("The configuration has no OnlineCredentials property.");
        var credentials = Invoke(() => property.GetValue(configuration, null));
        if (credentials is null) return new Binding(false, $"{CredentialsPropertyName} is null, so no user can be set");
        var type = credentials.GetType();
        var name = WritableProperty(type, NamePropertyName, typeof(string));
        var setter = PasswordSetter(type);
        if (name is null || setter is null)
            return new Binding(false, $"{CredentialsPropertyName} has no writable string Name and SetPassword(SecureString), so the login cannot be answered");
        object? userType = null;
        PropertyInfo? typeProperty = null;
        if (user.UserType is { } requested)
        {
            typeProperty = WritableProperty(type, TypePropertyName, null);
            if (typeProperty is null || !typeProperty.PropertyType.IsEnum)
                return new Binding(false, $"{CredentialsPropertyName} has no writable enum Type, so userType cannot be applied");
            if (!Enum.GetNames(typeProperty.PropertyType).Contains(requested, StringComparer.Ordinal))
                return new Binding(true, $"'{requested}' is not a member of {typeProperty.PropertyType.Name} ({string.Join(", ", Enum.GetNames(typeProperty.PropertyType))})");
            userType = Enum.Parse(typeProperty.PropertyType, requested, ignoreCase: false);
        }

        return new Binding(password =>
        {
            Invoke(() => name.SetValue(credentials, user.UserName, null));
            if (typeProperty is not null) Invoke(() => typeProperty.SetValue(credentials, userType, null));
            Invoke(() => setter.Invoke(credentials, new object[] { password }));
        });
    }

    private static PropertyInfo? CredentialsProperty(Type type)
    {
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name == CredentialsPropertyName && p.GetIndexParameters().Length == 0 && p.GetGetMethod(nonPublic: false) is not null)
            .Take(2).ToArray();
        return matches.Length == 1 && PasswordSetter(matches[0].PropertyType) is not null ? matches[0] : null;
    }

    private static PropertyInfo? WritableProperty(Type type, string name, Type? propertyType)
    {
        var matches = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name == name && p.GetIndexParameters().Length == 0 && p.GetSetMethod(nonPublic: false) is not null
                && (propertyType is null || p.PropertyType == propertyType))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static MethodInfo? PasswordSetter(Type type)
    {
        var matches = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "SetPassword" && !m.IsGenericMethodDefinition && m.GetParameters() is { Length: 1 } p && p[0].ParameterType == typeof(SecureString))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static object? Invoke(Func<object?> call)
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

    private static void Invoke(Action call) => Invoke(() =>
    {
        call();
        return null;
    });
}
