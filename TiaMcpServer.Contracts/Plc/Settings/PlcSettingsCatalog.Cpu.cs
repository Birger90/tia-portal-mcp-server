namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #15, further CPU settings. Evidence: the V21 IntelliSense files (Siemens.Engineering.Base.xml, .Step7.xml) and the V21
/// manual "TIA Portal Openness: API for automation of engineering workflows", sections "Setting a display password", "Setting
/// password policy for PLC", "Managing runtime account locking configuration", "Managing Syslog configuration", "Managing system
/// web page", "Managing Web Application Configuration", "Assigning watch &amp; force tables for web server and PLC display",
/// "Assigning project language to PLC", "Supporting IP Accessibility". Every member is bound at runtime by exact name.
/// </summary>
public static class PlcCpuSettingsMethods
{
    public const string GetCpuSettings = "get_cpu_settings";
    public const string SetAccountLocking = "set_account_locking";
    public const string SetPlcPasswordPolicy = "set_plc_password_policy";
    public const string SetLegacyPlcPasswordPolicy = "set_legacy_plc_password_policy";
    public const string SetSyslog = "set_syslog";
    public const string AddSyslogServer = "add_syslog_server";
    public const string RemoveSyslogServer = "remove_syslog_server";
    public const string CreateProjectSyslogServer = "create_project_syslog_server";
    public const string UpdateProjectSyslogServer = "update_project_syslog_server";
    public const string DeleteProjectSyslogServer = "delete_project_syslog_server";
    public const string SetSystemWebPages = "set_system_web_pages";
    public const string SetDefaultWebApplication = "set_default_web_application";
    public const string SetWatchTableAccess = "set_watch_table_access";
    public const string RemoveWatchTableAccess = "remove_watch_table_access";
    public const string SetForceTableAccess = "set_force_table_access";
    public const string RemoveForceTableAccess = "remove_force_table_access";
    public const string SetDisplayPassword = "set_display_password";
    public const string SetFrontPanelDisplay = "set_front_panel_display";
    public const string SetPlcProjectLanguage = "set_plc_project_language";
    public const string SetIpAccessibility = "set_ip_accessibility";
    public const string GenerateWebBlocks = "generate_web_blocks";
}

/// <summary>The documented type and member names of issue #15.</summary>
public static class PlcCpuSettingsNames
{
    public const string AccountLocking = "Siemens.Engineering.HW.Features.PlcAccountLockingAtRuntimeFeature";
    public const string PasswordPolicy = "Siemens.Engineering.Security.PlcPasswordPolicyService";
    public const string LegacyPasswordPolicy = "Siemens.Engineering.Security.LegacyPlcPasswordPolicyService";
    public const string SyslogManager = "Siemens.Engineering.HW.Features.SysLogConfigurationManager";
    public const string ProjectSyslog = "Siemens.Engineering.Security.SyslogServerProvider";
    public const string SystemWebPages = "Siemens.Engineering.HW.Features.SystemWebPagesFeature";
    public const string DefaultWebPages = "Siemens.Engineering.HW.Features.DefaultWebPagesFeature";
    public const string WatchAndForceTables = "Siemens.Engineering.HW.Features.WatchAndForceTableAccessManager";
    public const string DisplayProtection = "Siemens.Engineering.HW.Features.DisplayProtection";
    public const string FrontPanelDisplay = "Siemens.Engineering.HW.Features.FrontPanelDisplay";
    public const string UserDefinedPages = "Siemens.Engineering.HW.Features.WebserverUserDefinedPages";

    public static readonly string[] AccountLockingProperties =
        { "EnableLockUserAccountAtRunTime", "NumberOfFailedLoginAttempts", "LoginRetryInterval", "LockAccountForDuration" };

    public static readonly string[] LegacyPolicyProperties =
    {
        "PasswordPolicyEnabled", "MinimumLength", "MinimumNumericCharacterLength", "MinimumSpecialCharacterLength",
        "IncludesLowerCaseAndUpperCaseCharacters",
    };

    public static readonly string[] SyslogProperties = { "EnableSystemLogging", "TransportProtocol" };

    /// <summary>The CPU device item attributes the manual sets with <c>SetAttribute</c> for syslog.</summary>
    public static readonly string[] SyslogAttributes = { "SysLogAutoAcceptClient", "SysLogClientCertificateId", "SysLogTrustedCertificateIds" };

    public static readonly string[] ProjectSyslogProperties = { "Address", "Port", "Tls", "Comment" };

    public static readonly string[] FrontPanelProperties = { "UserDefinedLogoActivated", "AdaptLogoActivated" };

    public const string MultilingualSupport = "MultilingualSupport";
    public const string ProjectLanguage = "ProjectLanguage";
    public const string DisplayLanguage = "DisplayLanguage";
    public const string IpAccessibility = "PlcAccessCommunicationModule";

    /// <summary>The <c>WatchAndForceTableAccess</c> members a rule may get (<c>None</c> is documented as refused).</summary>
    public static readonly string[] Accesses = { "Read", "Write" };
}

public static partial class PlcSettingsCatalog
{
    private static void AddCpuSettings()
    {
        const int Issue = 15;
        Add(new(PlcCpuSettingsMethods.GetCpuSettings, false, Issue, false, null));
        Add(new(PlcCpuSettingsMethods.SetAccountLocking, true, Issue, false, PlcCpuSettingsNames.AccountLocking, PlcCpuSettingsNames.AccountLockingProperties,
            new[] { ("EnableLockUserAccountAtRunTime", "false") }, requiresSettings: true));
        Add(new(PlcCpuSettingsMethods.SetPlcPasswordPolicy, true, Issue, true, PlcCpuSettingsNames.PasswordPolicy, new[] { "PasswordPolicyEnabled" },
            new[] { ("PasswordPolicyEnabled", "false") }, requiresSettings: true));
        Add(new(PlcCpuSettingsMethods.SetLegacyPlcPasswordPolicy, true, Issue, true, PlcCpuSettingsNames.LegacyPasswordPolicy, PlcCpuSettingsNames.LegacyPolicyProperties,
            new[] { ("PasswordPolicyEnabled", "false"), ("IncludesLowerCaseAndUpperCaseCharacters", "false") }, requiresSettings: true));
        Add(new(PlcCpuSettingsMethods.SetSyslog, true, Issue, false, PlcCpuSettingsNames.SyslogManager,
            PlcCpuSettingsNames.SyslogProperties.Concat(PlcCpuSettingsNames.SyslogAttributes).ToArray(),
            // TransportProtocol (anything but mutual TLS lowers) and SysLogAutoAcceptClient (acknowledge) are judged by the host (security review F8).
            new[] { ("EnableSystemLogging", "false") }, requiresSettings: true));
        Add(new(PlcCpuSettingsMethods.AddSyslogServer, true, Issue, false, PlcCpuSettingsNames.SyslogManager));
        Add(new(PlcCpuSettingsMethods.RemoveSyslogServer, true, Issue, false, PlcCpuSettingsNames.SyslogManager));
        Add(new(PlcCpuSettingsMethods.CreateProjectSyslogServer, true, Issue, true, PlcCpuSettingsNames.ProjectSyslog, PlcCpuSettingsNames.ProjectSyslogProperties,
            new[] { ("Tls", "false") }));
        Add(new(PlcCpuSettingsMethods.UpdateProjectSyslogServer, true, Issue, true, PlcCpuSettingsNames.ProjectSyslog, PlcCpuSettingsNames.ProjectSyslogProperties,
            new[] { ("Tls", "false") }, requiresSettings: true));
        Add(new(PlcCpuSettingsMethods.DeleteProjectSyslogServer, true, Issue, true, PlcCpuSettingsNames.ProjectSyslog));
        Add(new(PlcCpuSettingsMethods.SetSystemWebPages, true, Issue, false, PlcCpuSettingsNames.SystemWebPages, new[] { "EnableSystemWebPages" },
            new[] { ("EnableSystemWebPages", "true") }, requiresSettings: true));
        Add(new(PlcCpuSettingsMethods.SetDefaultWebApplication, true, Issue, false, PlcCpuSettingsNames.DefaultWebPages));
        Add(new(PlcCpuSettingsMethods.SetWatchTableAccess, true, Issue, false, PlcCpuSettingsNames.WatchAndForceTables));
        Add(new(PlcCpuSettingsMethods.RemoveWatchTableAccess, true, Issue, false, PlcCpuSettingsNames.WatchAndForceTables));
        Add(new(PlcCpuSettingsMethods.SetForceTableAccess, true, Issue, false, PlcCpuSettingsNames.WatchAndForceTables));
        Add(new(PlcCpuSettingsMethods.RemoveForceTableAccess, true, Issue, false, PlcCpuSettingsNames.WatchAndForceTables));
        Add(new(PlcCpuSettingsMethods.SetDisplayPassword, true, Issue, false, PlcCpuSettingsNames.DisplayProtection, raises: true));
        Add(new(PlcCpuSettingsMethods.SetFrontPanelDisplay, true, Issue, false, PlcCpuSettingsNames.FrontPanelDisplay, PlcCpuSettingsNames.FrontPanelProperties));
        Add(new(PlcCpuSettingsMethods.SetPlcProjectLanguage, true, Issue, false, null));
        Add(new(PlcCpuSettingsMethods.SetIpAccessibility, true, Issue, false, null));
        Add(new(PlcCpuSettingsMethods.GenerateWebBlocks, true, Issue, false, PlcCpuSettingsNames.UserDefinedPages));
    }
}
