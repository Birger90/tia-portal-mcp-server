using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Plc.PlcGuardDefinitions;

namespace TiaMcpServer.Plc;

/// <summary>
/// The CPU protection part of the planner's working state. The target is the PLC (resolved from the tag inventory like
/// every PLC item) and one setting of its CPU; no extra planning read is made, so a preview shows the requested value
/// only (read the current one with plc_read get_plc_security). The two settings are separate objects: one item never
/// supersedes the other.
/// </summary>
internal sealed partial class PlcWorkingState
{
    public const string PlcSecurityKind = "PlcSecurity";

    private static bool IsSecurityOperation(string operation) => PlcSecurityMethods.IsWrite(operation);

    private PlcWriteEffect ResolveSecurity(PlcOperationRequest item, List<FiredGuard> guards)
    {
        var setting = PlcSecurityPolicy.Settings.For(item.Operation);
        var target = SecurityTarget(_plc, _device, setting);
        switch (item.Operation)
        {
            case PlcSecurityMethods.SetAccessLevel:
            {
                var passwords = (item.AccessLevelPasswords?.Entries ?? Array.Empty<Contracts.Plc.PlcDownloadPasswordInfo>()).Select(e => e.Type).ToList();
                var resets = item.ResetAccessLevelPasswords ?? Array.Empty<string>();
                if (item.AccessLevel == "FullAccess" && passwords.Contains("FullAccessIncludingFailsafe", StringComparer.Ordinal))
                    // K54 (live V21 2026-11-02): FullAccess with a fail-safe password protects the F-program of an F-CPU
                    // (SIMATIC Safety manual: in safety mode at least FullAccess with a password for FullAccessIncludingFailsafe).
                    guards.Add(Fire(RaisesCpuProtection, item,
                        $"set_plc_access_level sets the CPU of PLC '{_plc}' to FullAccess with a FullAccessIncludingFailsafe password in the offline project: "
                        + "the standard program stays open, the fail-safe program needs that password after a download."));
                else if (PlcSecurityPolicy.IsUnprotectedLevel(item.AccessLevel))
                    guards.Add(Fire(LowersCpuProtection, item,
                        $"set_plc_access_level sets the CPU of PLC '{_plc}' to {item.AccessLevel} (no access protection) in the offline project."));
                else
                    guards.Add(Fire(RaisesCpuProtection, item,
                        $"set_plc_access_level protects the CPU of PLC '{_plc}' with {item.AccessLevel} in the offline project; after a download every access above it needs its password"
                        + (passwords.Count == 0 ? " (no level password is set by this item; TIA Portal needs at least the FullAccess password)." : $" (passwords set from sources: {string.Join(", ", passwords)}).")));
                if (resets.Length > 0)
                    guards.Add(Fire(LowersCpuProtection, item, $"set_plc_access_level removes the password of {string.Join(", ", resets)} on the CPU of PLC '{_plc}' (ResetPassword)."));
                var changes = Change(setting, null, item.AccessLevel)
                    .Concat(passwords.Select(level => new PlcFieldChange($"password:{level}", null, "set")))
                    .Concat(resets.Select(level => new PlcFieldChange($"password:{level}", null, "reset")))
                    .ToArray();
                return Effect(item, target, changes, null);
            }
            case PlcSecurityMethods.ProtectConfidentialData:
                guards.Add(Fire(RaisesCpuProtection, item,
                    $"protect_plc_confidential_data protects the confidential PLC configuration data of the CPU of PLC '{_plc}' with a password ({(item.AllConfiguration == true ? "ProtectAllPlcConfiguration" : "Protect")}); downloads then need it (PlcMasterSecretPassword)."));
                return Effect(item, target, Change(setting, null, item.AllConfiguration == true ? "on (all configuration)" : "on"), null);
            case PlcSecurityMethods.ChangeConfidentialDataPassword:
                guards.Add(Fire(RaisesCpuProtection, item,
                    $"change_plc_confidential_data_password replaces the confidential-data password of the CPU of PLC '{_plc}' (ChangePassword); downloads then need the new one."));
                return Effect(item, target, Change(setting, null, "password changed"), null);
            default:
                guards.Add(Fire(LowersCpuProtection, item,
                    $"disable_plc_confidential_data_protection turns off \"Vertrauliche PLC-Konfigurationsdaten schützen\" of the CPU of PLC '{_plc}' ({(item.AllConfiguration == true ? "UnprotectAllPlcConfiguration(), the additional protection only" : item.PasswordVariable is null ? "Unprotect()" : "Unprotect(SecureString)")})."));
                return Effect(item, target, Change(setting, null, item.AllConfiguration == true ? "additional protection off" : "off"), null);
        }
    }

    private static PlcTargetIdentity SecurityTarget(string plcName, string? deviceName, string setting)
        => new(PlcSecurityKind, plcName, deviceName, null, null, setting, null, null);

    /// <summary>The requested identity of a CPU protection item whose PLC could not be verified.</summary>
    private static PlcTargetIdentity? UnresolvedSecurityTarget(PlcOperationRequest item, string plcName, string? deviceName)
        => IsSecurityOperation(item.Operation) ? SecurityTarget(plcName, deviceName, PlcSecurityPolicy.Settings.For(item.Operation)) : null;
}
