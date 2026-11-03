namespace TiaMcpServer.Contracts.Plc.Security;

/// <summary>
/// Worker methods of the CPU protection family (access level and confidential PLC configuration data protection).
/// Each name is also the public <c>plc_read</c> / <c>plc_write</c> operation name. The lists feed
/// <c>OperationPolicyCatalog</c>, <c>ProjectCapabilityCatalog.OperationFor</c> and the worker dispatch from this one
/// place. Parameters travel in <c>WorkerRequest.PlcSecurity</c>. Passwords travel only as password-source tokens
/// (environment variable names or <c>credential:</c> targets); the worker resolves them into <c>SecureString</c>s and no
/// method generates one.
/// </summary>
public static class PlcSecurityMethods
{
    /// <summary>plc_read, Observe: access level, access-control configuration and confidential-data protection per CPU.</summary>
    public const string GetSecurity = "get_plc_security";

    /// <summary>
    /// plc_write, ProjectMutation: <c>PlcAccessLevelProvider.SetPassword(level, SecureString)</c> for the given level passwords,
    /// then <c>PlcProtectionAccessLevel</c>, then <c>ResetPassword(level)</c> for the levels to reset.
    /// </summary>
    public const string SetAccessLevel = "set_plc_access_level";

    /// <summary>plc_write, ProjectMutation: <c>PlcMasterSecretConfigurator.Unprotect()</c>, <c>Unprotect(SecureString)</c> or <c>UnprotectAllPlcConfiguration()</c>.</summary>
    public const string DisableConfidentialDataProtection = "disable_plc_confidential_data_protection";

    /// <summary>plc_write, ProjectMutation: <c>PlcMasterSecretConfigurator.Protect(SecureString)</c>, <c>ProtectAllPlcConfigurationWithPassword(SecureString)</c> or <c>ProtectAllPlcConfiguration()</c>.</summary>
    public const string ProtectConfidentialData = "protect_plc_confidential_data";

    /// <summary>plc_write, ProjectMutation: <c>PlcMasterSecretConfigurator.ChangePassword(SecureString, SecureString)</c>.</summary>
    public const string ChangeConfidentialDataPassword = "change_plc_confidential_data_password";

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[] { GetSecurity });

    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[]
    {
        SetAccessLevel, DisableConfidentialDataProtection, ProtectConfidentialData, ChangeConfidentialDataPassword,
    });

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Read.Concat(Write).ToArray());

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method) || IsWrite(method);
}
