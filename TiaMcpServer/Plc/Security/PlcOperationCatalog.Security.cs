using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.PlcDownload;

namespace TiaMcpServer.Plc;

/// <summary>The CPU protection operations of plc_read/plc_write: their field sets and shape validation.</summary>
public static partial class PlcOperationCatalog
{
    private static Dictionary<string, Spec> WithSecuritySpecs(Dictionary<string, Spec> specs)
    {
        specs[PlcSecurityMethods.GetSecurity] = Read(Array.Empty<string>(), new[] { "plcName" });
        specs[PlcSecurityMethods.SetAccessLevel] = Write(new[] { "accessLevel" }, new[] { "plcName", "accessLevelPasswords", "resetAccessLevelPasswords" });
        specs[PlcSecurityMethods.DisableConfidentialDataProtection] = Write(Array.Empty<string>(), new[] { "plcName", "passwordVariable", "allConfiguration" });
        specs[PlcSecurityMethods.ProtectConfidentialData] = Write(Array.Empty<string>(), new[] { "plcName", "passwordVariable", "allConfiguration" });
        specs[PlcSecurityMethods.ChangeConfidentialDataPassword] = Write(new[] { "passwordVariable", "newPasswordVariable" }, new[] { "plcName" });
        return specs;
    }

    private static (string Name, Func<PlcOperationRequest, string?> Get)[] WithSecurityStringFields(
        (string Name, Func<PlcOperationRequest, string?> Get)[] fields)
        => fields.Concat(new (string Name, Func<PlcOperationRequest, string?> Get)[]
        {
            ("accessLevel", op => op.AccessLevel),
            ("newPasswordVariable", op => op.NewPasswordVariable),
        }).ToArray();

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> SecurityOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("accessLevelPasswords", op => op.AccessLevelPasswords is not null),
        ("resetAccessLevelPasswords", op => op.ResetAccessLevelPasswords is not null),
        ("allConfiguration", op => op.AllConfiguration is not null),
        ("passwords", op => op.Passwords is not null),
    };

    /// <summary>The CPU protection shape rules; nothing here reads the project. Messages never repeat a password source value.</summary>
    private static void ValidateSecurityItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        void Error(string message) => errors.Add($"{prefix}: {message}");
        foreach (var entry in op.Passwords?.Entries ?? Array.Empty<Contracts.Plc.PlcDownloadPasswordInfo>())
        {
            if (entry.Variable is PlcDownloadPasswordMap.RedactedVariable or PlcDownloadPasswordMap.UnsetVariable)
                Error($"'passwords.{entry.Type}' must name a set environment variable or a credential target, never the password itself; the value was redacted.");
            if (entry.Type == "TlsVerificationConfiguration")
                Error("'passwords' must not answer TlsVerificationConfiguration (use trustedCertificates).");
        }

        if (!PlcSecurityMethods.IsWrite(op.Operation)) return;
        if (op.Operation == PlcSecurityMethods.SetAccessLevel && op.AccessLevel is not null
            && PlcSecurityPolicy.AccessLevelError(op.AccessLevel) is { } error)
            Error(error);
        foreach (var (field, value) in new[] { ("passwordVariable", op.PasswordVariable), ("newPasswordVariable", op.NewPasswordVariable) }
                     .Concat((op.AccessLevelPasswords?.Entries ?? Array.Empty<Contracts.Plc.PlcDownloadPasswordInfo>()).Select(e => ($"accessLevelPasswords.{e.Type}", e.Variable))))
        {
            if (value is PlcDownloadPasswordMap.RedactedVariable)
                Error($"'{field}' must name where the password is (an environment variable NAME or a credential target), never the password itself; the value was redacted.");
            else if (value is PlcDownloadPasswordMap.UnsetVariable)
                Error($"'{field}' names an environment variable that is not set (or empty) for this server; set it in the MCP client configuration (env) and restart the server. The name was redacted.");
        }

        foreach (var entry in op.AccessLevelPasswords?.Entries ?? Array.Empty<Contracts.Plc.PlcDownloadPasswordInfo>())
            if (PlcSecurityPolicy.PasswordLevelError("accessLevelPasswords", entry.Type) is { } levelError) Error(levelError);
        var resets = op.ResetAccessLevelPasswords ?? Array.Empty<string>();
        foreach (var level in resets)
            if (PlcSecurityPolicy.PasswordLevelError("resetAccessLevelPasswords", level) is { } levelError) Error(levelError);
        if (resets.Distinct(StringComparer.Ordinal).Count() != resets.Length) Error("'resetAccessLevelPasswords' must not repeat a level.");
        if (resets.Any(level => op.AccessLevelPasswords?.Entries.Any(e => e.Type == level) == true))
            Error("A level must not be in both accessLevelPasswords and resetAccessLevelPasswords.");
        if (op.Operation == PlcSecurityMethods.ProtectConfidentialData && op.AllConfiguration != true && string.IsNullOrWhiteSpace(op.PasswordVariable))
            Error("protect_plc_confidential_data needs passwordVariable (or the server password map, purpose PlcMasterSecretPassword) unless allConfiguration is true.");
        if (op.Operation == PlcSecurityMethods.DisableConfidentialDataProtection && op.AllConfiguration == true && op.PasswordVariable is not null)
            Error("disable_plc_confidential_data_protection with allConfiguration:true calls UnprotectAllPlcConfiguration(), which takes no password; omit passwordVariable.");
    }
}
