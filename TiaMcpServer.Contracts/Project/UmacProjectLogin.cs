// Origin: the open path of a UMAC-protected project (answer TIA Portal's login callback once, never resend a refused
// password, report how often TIA Portal asked) follows Sparkz400/TIA_MCP commit 315493b (2026-11-20),
// src/TiaMcp.Openness.V21/OpennessSession.Projects.cs, Apache License 2.0, Copyright 2026 Nico Schürmann (NOTICE: "TIA MCP").
// Reimplemented here without their credential envelope: the password comes from the fork's password sources. The
// Siemens members are bound by reflection; see docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md.
using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Project;

/// <summary>
/// Names and shape rules of the UMAC login of <c>open_project</c>. Evidence: Siemens "TIA Portal Openness V21 - Functions for
/// Projects and Project Data" (03/2026) as quoted by Czarnak/totally-integrated-claude
/// <c>skills/tia-project-general/references/umac-and-auth.md</c> (MIT): <c>tiaPortal.Projects.Open(new FileInfo(path), umacDelegate)</c>
/// with a delegate that sets <c>UmacCredentials.Type</c> (<c>UmacUserType.Project</c> or <c>.Global</c>), <c>.Name</c> and calls
/// <c>.SetPassword(SecureString)</c>; Sparkz400/TIA_MCP compiles the same call against the V21 assemblies. Not live-tested here.
/// </summary>
public static class UmacProjectLoginNames
{
    public const string DelegateTypeName = "Siemens.Engineering.UmacDelegate";
    public const string CredentialsTypeName = "Siemens.Engineering.UmacCredentials";
    public const string UserTypeEnumName = "Siemens.Engineering.UmacUserType";

    /// <summary>A project user (UMAC managed in the project).</summary>
    public const string ProjectUser = "project";

    /// <summary>A global user (UMAC managed by a UMC server).</summary>
    public const string GlobalUser = "global";

    /// <summary>The password map purpose of the UMAC project login (project = the project file name).</summary>
    public const string PasswordPurpose = "UmacProjectLogin";

    public const int MaxUserNameLength = 256;

    /// <summary>The documented <c>UmacUserType</c> member of a public user type, or null.</summary>
    public static string? EnumMember(string? userType) => userType switch
    {
        ProjectUser => "Project",
        GlobalUser => "Global",
        _ => null,
    };

    /// <summary>Why <paramref name="userName"/> is no UMAC user name; null when it is. The message never repeats the name.</summary>
    public static string? UserNameError(string? userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return "umacUserName must name the UMAC user.";
        if (userName!.Length > MaxUserNameLength) return $"umacUserName has at most {MaxUserNameLength} characters.";
        foreach (var character in userName)
            if (char.IsControl(character)) return "umacUserName must not contain control characters.";
        return null;
    }

    /// <summary>Why <paramref name="userType"/> is no UMAC user type; null when it is.</summary>
    public static string? UserTypeError(string? userType)
        => EnumMember(userType) is null ? "umacUserType must be \"project\" (a project user) or \"global\" (a UMC user)." : null;
}

/// <summary>
/// The Siemens members of a UMAC open, bound on the live types before anything is opened or closed: the two-argument
/// <c>ProjectComposition.Open(FileInfo, UmacDelegate)</c>, the delegate's single <c>UmacCredentials</c> parameter, the writable
/// <c>Name</c> (string) and <c>Type</c> (<c>UmacUserType</c>) properties, <c>SetPassword(SecureString)</c> and the enum member.
/// Every miss is a <see cref="RuntimeBindingException"/> with <c>api_member_missing</c>.
/// </summary>
public sealed class UmacProjectLoginBinding
{
    private UmacProjectLoginBinding(MethodInfo open, Type delegateType, ParameterExpression parameter, PropertyInfo name, PropertyInfo type,
        MethodInfo setPassword, object userTypeValue)
    {
        Open = open;
        DelegateType = delegateType;
        _parameter = parameter;
        _name = name;
        _type = type;
        _setPassword = setPassword;
        _userTypeValue = userTypeValue;
    }

    private readonly ParameterExpression _parameter;
    private readonly PropertyInfo _name;
    private readonly PropertyInfo _type;
    private readonly MethodInfo _setPassword;
    private readonly object _userTypeValue;

    /// <summary><c>ProjectComposition.Open(FileInfo, UmacDelegate)</c> on the live composition type.</summary>
    public MethodInfo Open { get; }

    public Type DelegateType { get; }

    public static UmacProjectLoginBinding Bind(Type compositionType, string userType)
    {
        static RuntimeBindingException Missing(string what)
            => new(WorkerFailureCategories.ApiMemberMissing, what + " Nothing was opened or closed.");

        var member = UmacProjectLoginNames.EnumMember(userType)
            ?? throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, UmacProjectLoginNames.UserTypeError(userType)!);
        var candidates = compositionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == "Open")
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(FileInfo)
                    && parameters[1].ParameterType.FullName == UmacProjectLoginNames.DelegateTypeName;
            })
            .ToList();
        if (candidates.Count != 1)
            throw Missing($"This TIA Portal installation exposes {(candidates.Count == 0 ? "no" : "more than one")} ProjectComposition.Open(FileInfo, {UmacProjectLoginNames.DelegateTypeName}).");
        var open = candidates[0];
        var delegateType = open.GetParameters()[1].ParameterType;
        if (!typeof(Delegate).IsAssignableFrom(delegateType) || delegateType.GetMethod("Invoke") is not { } invoke
            || invoke.ReturnType != typeof(void) || invoke.GetParameters() is not { Length: 1 } invokeParameters
            || invokeParameters[0].ParameterType.FullName != UmacProjectLoginNames.CredentialsTypeName)
            throw Missing($"{UmacProjectLoginNames.DelegateTypeName} is not the documented void delegate taking one {UmacProjectLoginNames.CredentialsTypeName}.");
        var credentialsType = invokeParameters[0].ParameterType;

        var name = credentialsType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        if (name is null || name.PropertyType != typeof(string) || name.GetSetMethod() is null)
            throw Missing($"{UmacProjectLoginNames.CredentialsTypeName} has no writable string property Name.");
        var type = credentialsType.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance);
        if (type is null || !type.PropertyType.IsEnum || type.PropertyType.FullName != UmacProjectLoginNames.UserTypeEnumName || type.GetSetMethod() is null)
            throw Missing($"{UmacProjectLoginNames.CredentialsTypeName} has no writable property Type of {UmacProjectLoginNames.UserTypeEnumName}.");
        var setPassword = credentialsType.GetMethod("SetPassword", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(SecureString) }, null);
        if (setPassword is null)
            throw Missing($"{UmacProjectLoginNames.CredentialsTypeName} has no SetPassword(SecureString).");
        var value = LibraryRuntimeBindingEnum(type.PropertyType, member)
            ?? throw Missing($"{UmacProjectLoginNames.UserTypeEnumName} has no member {member} (live members: {string.Join(", ", Enum.GetNames(type.PropertyType))}).");

        return new UmacProjectLoginBinding(open, delegateType, Expression.Parameter(credentialsType, "credentials"), name, type, setPassword, value);
    }

    private static object? LibraryRuntimeBindingEnum(Type enumType, string member)
        => Enum.GetNames(enumType).Contains(member, StringComparer.Ordinal) ? Enum.Parse(enumType, member) : null;

    /// <summary>A delegate of the live <c>UmacDelegate</c> type that passes its <c>UmacCredentials</c> to <paramref name="onCall"/>.</summary>
    public Delegate CreateDelegate(Action<object> onCall)
    {
        var body = Expression.Invoke(Expression.Constant(onCall), Expression.Convert(_parameter, typeof(object)));
        return Expression.Lambda(DelegateType, body, _parameter).Compile();
    }

    /// <summary>Sets Type, Name and the password on one live <c>UmacCredentials</c> (in the documented order).</summary>
    public void Answer(object credentials, string userName, SecureString password)
    {
        RuntimeServiceValues.Call(_type.GetSetMethod()!, credentials, new[] { _userTypeValue });
        RuntimeServiceValues.Call(_name.GetSetMethod()!, credentials, new object?[] { userName });
        RuntimeServiceValues.Call(_setPassword, credentials, new object?[] { password });
    }
}

/// <summary>
/// One UMAC open: answers the first login callback and refuses every later one, so a refused password is never resent (a
/// wrong password cannot loop or lock the account; Sparkz400 checked this natively once, not verified here).
/// </summary>
public sealed class UmacProjectLoginRun
{
    private readonly UmacProjectLoginBinding _binding;
    private readonly string _userName;
    private readonly SecureString _password;

    public UmacProjectLoginRun(UmacProjectLoginBinding binding, string userName, SecureString password)
    {
        _binding = binding;
        _userName = userName;
        _password = password;
        Handler = binding.CreateDelegate(OnLogin);
    }

    /// <summary>The live <c>UmacDelegate</c> passed to <c>Open(FileInfo, UmacDelegate)</c>.</summary>
    public Delegate Handler { get; }

    /// <summary>How often TIA Portal asked for the login (0: the project is not protected).</summary>
    public int Requests { get; private set; }

    private void OnLogin(object credentials)
    {
        Requests++;
        if (Requests > 1)
            throw new RuntimeBindingException(WorkerFailureCategories.AccessDenied,
                "TIA Portal asked for the UMAC login again, so the password was not accepted. It was not sent a second time.");
        _binding.Answer(credentials, _userName, _password);
    }

    /// <summary>The warning that reports the login, without the password or its source value.</summary>
    public string Describe(string userType, string sourceKind)
        => Requests == 0
            ? $"UMAC login for user '{_userName}' ({userType}) was prepared, but TIA Portal did not ask for it (the project is not UMAC-protected)."
            : $"UMAC login: TIA Portal asked {Requests} time(s); user '{_userName}' ({userType}) was answered once with the password from a {sourceKind} source.";
}
