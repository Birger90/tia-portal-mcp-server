namespace TiaMcpServer.Contracts.Plc.Security;

/// <summary>
/// Siemens-free rules of the CPU protection operations: the documented member names, the access levels and the value
/// vocabularies of the results. Evidence: the Siemens openness_development skill <c>security/SKILL.md</c> and the member
/// list of "TIA Portal Openness V21 - Functions on Devices (03/2026)" (PlcAccessLevelProvider.SetPassword/ResetPassword,
/// every PlcMasterSecretConfigurator member; extracted in totally-integrated-claude
/// <c>skills/tia-devices-general/references/device-attributes.md</c>). Passwords come only from password sources.
/// </summary>
public static class PlcSecurityPolicy
{
    /// <summary>Documented service on the CPU device item (Siemens security skill, namespace <c>Siemens.Engineering.HW.Features</c>).</summary>
    public const string AccessLevelProviderType = "Siemens.Engineering.HW.Features.PlcAccessLevelProvider";

    /// <summary>Documented property of <see cref="AccessLevelProviderType"/>; reading it throws NullReferenceException without a password (skill).</summary>
    public const string AccessLevelProperty = "PlcProtectionAccessLevel";

    /// <summary>Documented short name of the property's enum type.</summary>
    public const string AccessLevelEnumName = "PlcProtectionAccessLevel";

    /// <summary>Documented service on the CPU device item (Siemens security skill, namespace <c>Siemens.Engineering.HW.Features</c>).</summary>
    public const string AccessControlProviderType = "Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider";

    /// <summary>Documented property of <see cref="AccessControlProviderType"/>.</summary>
    public const string AccessControlProperty = "Configuration";

    /// <summary>Documented short name of the property's enum type.</summary>
    public const string AccessControlEnumName = "PlcAccessControlConfiguration";

    /// <summary>Documented service short name (<c>cpu.GetService&lt;PlcMasterSecretConfigurator&gt;()</c>; V21 manual: <c>Siemens.Engineering.HW.Features</c>).</summary>
    public const string MasterSecretConfiguratorName = "PlcMasterSecretConfigurator";

    /// <summary>Documented <c>Protect(SecureString)</c> (Siemens skill and V21 manual).</summary>
    public const string ProtectMethod = "Protect";

    /// <summary>Documented <c>Unprotect()</c> and <c>Unprotect(SecureString)</c> (V21 manual).</summary>
    public const string UnprotectMethod = "Unprotect";

    /// <summary>Documented <c>ProtectAllPlcConfiguration()</c> and <c>ProtectAllPlcConfigurationWithPassword(SecureString)</c> (V21 manual).</summary>
    public const string ProtectAllMethod = "ProtectAllPlcConfiguration";

    public const string ProtectAllWithPasswordMethod = "ProtectAllPlcConfigurationWithPassword";

    /// <summary>Documented <c>UnprotectAllPlcConfiguration()</c> (V21 manual).</summary>
    public const string UnprotectAllMethod = "UnprotectAllPlcConfiguration";

    /// <summary>Documented <c>ChangePassword(SecureString, SecureString)</c> (V21 manual).</summary>
    public const string ChangePasswordMethod = "ChangePassword";

    /// <summary>Documented property "Indicates the PLC protection type" (V21 manual); its type is not documented, so it is reported as text.</summary>
    public const string MasterSecretConfigurationProperty = "MasterSecretConfiguration";

    /// <summary>Documented <c>PlcAccessLevelProvider.SetPassword(PlcProtectionAccessLevel, SecureString)</c> (V21 manual).</summary>
    public const string SetPasswordMethod = "SetPassword";

    /// <summary>Documented <c>PlcAccessLevelProvider.ResetPassword(PlcProtectionAccessLevel)</c> (V21 manual).</summary>
    public const string ResetPasswordMethod = "ResetPassword";

    /// <summary>The password map purpose of the confidential-data (master secret) password; equal to the download configuration short name.</summary>
    public const string MasterSecretPurpose = "PlcMasterSecretPassword";

    /// <summary>The documented <c>PlcProtectionAccessLevel</c> members (Siemens security skill).</summary>
    public static IReadOnlyList<string> DocumentedAccessLevels { get; } = Array.AsReadOnly(new[]
    {
        "None", "FullAccess", "ReadAccess", "HMIAccess", "NoAccess", "FullAccessIncludingFailsafe",
    });

    /// <summary>
    /// The levels set_plc_access_level accepts, from the most open to the most protected: every level of the TIA Portal
    /// protection dialog. <c>None</c> is the enum's unset value, not a level of the dialog, and is never set.
    /// </summary>
    public static IReadOnlyList<string> SettableAccessLevels { get; } = Array.AsReadOnly(new[]
    {
        "FullAccessIncludingFailsafe", "FullAccess", "ReadAccess", "HMIAccess", "NoAccess",
    });

    /// <summary>The levels that take a password (<c>SetPassword(level, ...)</c>): every settable level but <c>NoAccess</c>.</summary>
    public static IReadOnlyList<string> PasswordLevels { get; } = Array.AsReadOnly(SettableAccessLevels.Take(4).ToArray());

    /// <summary>The levels that protect nothing (no password needed for the selected level itself).</summary>
    public static bool IsUnprotectedLevel(string? level) => level is "FullAccess" or "FullAccessIncludingFailsafe";

    /// <summary>
    /// The password levels more open than <paramref name="level"/>: in TIA Portal a CPU at a protected level needs a
    /// password for each of them that a user may use (FullAccess at least; FullAccessIncludingFailsafe on an F-CPU).
    /// </summary>
    public static IReadOnlyList<string> LevelsAbove(string level)
    {
        var index = SettableAccessLevels.ToList().IndexOf(level);
        return index <= 0 ? Array.Empty<string>() : PasswordLevels.Take(Math.Min(index, PasswordLevels.Count)).ToArray();
    }

    /// <summary>The <c>setting</c> values of a write result.</summary>
    public static class Settings
    {
        public const string AccessLevel = "accessLevel";
        public const string ConfidentialDataProtection = "confidentialDataProtection";

        public static string For(string? operation) => operation == PlcSecurityMethods.SetAccessLevel ? AccessLevel : ConfidentialDataProtection;
    }

    /// <summary>The <c>readBack</c> values of a write result.</summary>
    public static class ReadBacks
    {
        /// <summary>The setting was read back and equals the requested value.</summary>
        public const string Matched = "matched";

        /// <summary>The setting could not be read back (e.g. the documented NullReferenceException without a password).</summary>
        public const string Unreadable = "unreadable";

        /// <summary>No documented member reports the setting (confidential-data protection).</summary>
        public const string NotAvailable = "notAvailable";

        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Matched, Unreadable, NotAvailable });

        public static bool IsKnown(string? value) => value is not null && All.Contains(value, StringComparer.Ordinal);
    }

    public const int MaxServices = 64;
    public const int MaxMembers = 20;
    public const int MaxText = 400;

    /// <summary>
    /// The note for an access level read that failed with the NullReferenceException the Siemens security skill documents
    /// for a CPU without a configured password.
    /// </summary>
    public const string NoPasswordReadNote =
        "Reading PlcProtectionAccessLevel threw NullReferenceException; the Siemens security skill documents this for a CPU that has no password configured, so the current level is unknown here (check the CPU properties in TIA Portal or run compile_check with scope hardware).";

    /// <summary>Null when <paramref name="level"/> may be set; otherwise the refusal (exact, case-sensitive member names).</summary>
    public static string? AccessLevelError(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return $"'accessLevel' is required. Valid values: {string.Join(", ", SettableAccessLevels)}.";
        if (SettableAccessLevels.Contains(level, StringComparer.Ordinal)) return null;
        if (level == "None")
            return "'accessLevel' None is the enum's unset value, not a level of the TIA Portal protection dialog, and is never set. Valid values: " + string.Join(", ", SettableAccessLevels) + ".";
        return $"'accessLevel' value '{level}' is not supported (member names are case-sensitive). Valid values: {string.Join(", ", SettableAccessLevels)}.";
    }

    /// <summary>Null when <paramref name="level"/> takes a password (a key of accessLevelPasswords or an entry of resetAccessLevelPasswords).</summary>
    public static string? PasswordLevelError(string field, string? level)
        => level is not null && PasswordLevels.Contains(level, StringComparer.Ordinal) ? null
            : $"'{field}' names '{level}', which takes no password; valid levels: {string.Join(", ", PasswordLevels)} (case-sensitive).";

    /// <summary>Text cut to <see cref="MaxText"/> characters with a marker.</summary>
    public static string? Bound(string? text) => text is null || text.Length <= MaxText ? text : text.Substring(0, MaxText) + "...";
}
