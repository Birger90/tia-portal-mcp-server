using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Settings;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Plc;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Settings;

/// <summary>
/// Security review 2026-11-03, F1 and F8: the password policies and account locking are compared with the current values read
/// in planning; the syslog transport, automatic certificate acceptance, certificate manager, supported services and simple web
/// server permissions fire their guards. Nothing is refused (owner principle: never stricter than TIA Portal).
/// </summary>
public sealed class PlcSettingsProtectionGuardTests
{
    private static readonly PlcTagInventoryPlcInfo Plc = new() { PlcName = "PLC_1", DeviceName = "Station_1" };

    private static PlcOperationRequest Item(string operation, params (string Key, string Value)[] settings) => new()
    {
        OperationId = "op", Operation = operation, PlcName = "PLC_1",
        Settings = new PlcSettingValues(settings.Select(s => new KeyValuePair<string, string>(s.Key, s.Value)).ToList()),
    };

    private static Dictionary<string, string> Current(params (string Key, string Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    [Theory]
    [InlineData("MinimumLength", "8", "6", true)]
    [InlineData("MinimumLength", "8", "10", false)]
    [InlineData("MinimumLength", "8", "8", false)]
    [InlineData("MinimumNumericCharacterLength", "1", "0", true)]
    [InlineData("MinimumSpecialCharacterLength", "2", "1", true)]
    [InlineData("MinimumUserPasswordsBlockedForReuse", "5", "3", true)]
    [InlineData("PasswordValidity", "90", "180", true)]
    [InlineData("PasswordValidity", "90", "30", false)]
    [InlineData("LockAccountForDuration", "10", "2", true)]
    [InlineData("LoginRetryInterval", "5", "1", true)]
    [InlineData("NumberOfFailedLoginAttempts", "3", "10", true)]
    [InlineData("NumberOfFailedLoginAttempts", "5", "3", false)]
    [InlineData("EnableLockUserAccountAtRunTime", "True", "false", true)]
    [InlineData("EnableLockUserAccountAtRunTime", "False", "false", false)]
    [InlineData("PasswordPolicyEnabled", "True", "true", false)]
    [InlineData("IncludesLowerCaseAndUpperCaseCharacters", "True", "false", true)]
    [InlineData("EnablePasswordAging", "False", "true", false)]
    [InlineData("PasswordValidityPrewarningTime", "7", "1", false)]
    [InlineData("LockAccountForDuration", "00:10:00", "00:05:00", true)]
    [InlineData("MinimumLength", "8", "eight", true)]
    [InlineData("PasswordPolicyEnabled", "unknown", "true", true)]
    public void IsWeaker_ComparesInTheProtectionDirection_AndFailsSafe(string setting, string current, string requested, bool weaker)
        => Assert.Equal(weaker, PlcSettings.IsWeaker(setting, current, requested));

    [Fact]
    public void IsWeaker_UnreadableCurrentValueIsWeaker()
        => Assert.True(PlcSettings.IsWeaker("MinimumLength", null, "12"));

    [Fact]
    public void Policy_WeakerValue_FiresLowers_WithBeforeAndAfter()
    {
        var item = Item(PlcUmacMethods.SetUmacPasswordPolicy, ("MinimumLength", "6"), ("EnablePasswordAging", "true"));
        var (effect, guards) = PlcSettings.ResolvePolicy(item, null, Current(("MinimumLength", "8"), ("EnablePasswordAging", "False")), null);
        var guard = Assert.Single(guards);
        Assert.Equal(PlcGuardDefinitions.LowersCpuProtection, guard.Id);
        Assert.Contains("MinimumLength 8 -> 6", guard.Message);
        Assert.Equal("8", effect.Changes.Single(c => c.Field == "MinimumLength").Current);
    }

    [Fact]
    public void Policy_StrongerOrEqualValues_FireTheInfoGuardOnly()
    {
        var item = Item(PlcCpuSettingsMethods.SetLegacyPlcPasswordPolicy, ("PasswordPolicyEnabled", "true"), ("MinimumLength", "8"));
        var (_, guards) = PlcSettings.ResolvePolicy(item, null, Current(("PasswordPolicyEnabled", "False"), ("MinimumLength", "5")), null);
        Assert.Equal(new[] { PlcGuardDefinitions.ChangesPasswordPolicy }, guards.Select(g => g.Id));
    }

    [Fact]
    public void Policy_UnreadableCurrent_FiresLowers()
    {
        var item = Item(PlcCpuSettingsMethods.SetAccountLocking, ("NumberOfFailedLoginAttempts", "3"));
        var (_, guards) = PlcSettings.ResolvePolicy(item, Plc, new Dictionary<string, string>(), "the planning read get_cpu_settings failed");
        var guard = Assert.Single(guards);
        Assert.Equal(PlcGuardDefinitions.LowersCpuProtection, guard.Id);
        Assert.Contains("could not be read", guard.Message);
    }

    [Fact]
    public void Policy_OneUnreadableValue_FiresLowers()
    {
        var item = Item(PlcCpuSettingsMethods.SetPlcPasswordPolicy, ("PasswordPolicyEnabled", "true"));
        var (_, guards) = PlcSettings.ResolvePolicy(item, null, new Dictionary<string, string>(), null);
        Assert.Equal(new[] { PlcGuardDefinitions.LowersCpuProtection }, guards.Select(g => g.Id));
    }

    [Fact]
    public void CurrentFrom_ReadsTheSection_AndReportsAMissingOrUnofferedOne()
    {
        var read = new PlcSettingsReadInfo
        {
            Operation = PlcCpuSettingsMethods.GetCpuSettings,
            Plcs = { new PlcSettingsPlcInfo { PlcName = "PLC_1", DeviceName = "Station_1", Sections =
            {
                new PlcSettingsSectionInfo { Name = "accountLocking", Offered = true, Properties =
                {
                    new PlcSettingPropertyInfo { Name = "NumberOfFailedLoginAttempts", Value = new RuntimeValueInfo { State = RuntimeValueStates.Value, Value = "5" } },
                    new PlcSettingPropertyInfo { Name = "LockAccountForDuration", Value = new RuntimeValueInfo { State = RuntimeValueStates.Unreadable } },
                } },
            } } },
            Project = { new PlcSettingsSectionInfo { Name = "plcPasswordPolicy", Offered = false, Unavailable = "not listed" } },
        };
        var (current, unreadable) = PlcSettings.CurrentFrom(read, "accountLocking", true, Plc);
        Assert.Null(unreadable);
        Assert.Equal("5", current["NumberOfFailedLoginAttempts"]);
        Assert.False(current.ContainsKey("LockAccountForDuration"));
        Assert.Contains("not offered", PlcSettings.CurrentFrom(read, "plcPasswordPolicy", false, null).Unreadable);
        Assert.Contains("no section", PlcSettings.CurrentFrom(read, "legacyPlcPasswordPolicy", false, null).Unreadable);
    }

    [Fact]
    public void PolicyOperations_AreTheFourAndAllPlannedFromAReadOfTheirFamily()
    {
        Assert.Equal(4, PlcSettings.PolicyOperations.Count);
        Assert.All(PlcSettings.PolicyOperations, operation => Assert.True(PlcSettingsCatalog.IsWrite(operation), operation));
    }

    [Theory]
    [InlineData("UDP", true)]
    [InlineData("None", true)]
    [InlineData("TLSOnlyServerAuthentication", true)]
    [InlineData("TLSServerAndClientAuthentication", false)]
    public void Syslog_TransportWeakerThanMutualTls_Lowers(string transport, bool lowers)
    {
        var (_, guards) = PlcSettings.Resolve(Item(PlcCpuSettingsMethods.SetSyslog, ("TransportProtocol", transport)), Plc);
        Assert.Equal(lowers, guards.Any(g => g.Id == PlcGuardDefinitions.LowersCpuProtection));
    }

    [Fact]
    public void Syslog_AutoAcceptClient_FiresTheAcknowledgeGuard()
    {
        var (_, guards) = PlcSettings.Resolve(Item(PlcCpuSettingsMethods.SetSyslog, ("SysLogAutoAcceptClient", "true")), Plc);
        Assert.Contains(guards, g => g.Id == PlcGuardDefinitions.SyslogAutoAcceptsCertificates);
        var definition = PlcGuardDefinitions.Definitions.Single(d => d.Id == PlcGuardDefinitions.SyslogAutoAcceptsCertificates);
        Assert.Equal(TiaMcpServer.Safety.Pipeline.WriteGuardSeverities.Acknowledge, definition.Severity);
        var (_, off) = PlcSettings.Resolve(Item(PlcCpuSettingsMethods.SetSyslog, ("SysLogAutoAcceptClient", "false")), Plc);
        Assert.DoesNotContain(off, g => g.Id == PlcGuardDefinitions.SyslogAutoAcceptsCertificates);
    }

    [Fact]
    public void Certificates_RemoveSupportedServiceAndCertificateManager_Lower()
    {
        var remove = new PlcOperationRequest { OperationId = "r", Operation = PlcCertificateMethods.RemoveCertificateSupportedService, PlcName = "PLC_1", CertificateId = "3" };
        Assert.Contains(PlcSettings.Resolve(remove, Plc).Guards, g => g.Id == PlcGuardDefinitions.LowersCpuProtection);
        var manager = Item(PlcCertificateMethods.SetCertificateManager, ("EnableGlobalCertificatesStore", "true"));
        Assert.Contains(PlcSettings.Resolve(manager, Plc).Guards, g => g.Id == PlcGuardDefinitions.LowersCpuProtection);
    }

    [Theory]
    [InlineData("None", false)]
    [InlineData("", false)]
    [InlineData("ReadOnly", true)]
    [InlineData("ReadWrite", true)]
    [InlineData("ReadOnly, ReadWrite", true)]
    [InlineData("None, ReadOnly", true)]
    public void SimpleWebserverUser_AnyPermissionButNone_Lowers(string permissions, bool lowers)
    {
        var item = Item(PlcCpuUserMethods.UpdateSimpleWebserverUser, ("Permissions", permissions));
        item.UserName = "web";
        Assert.Equal(lowers, PlcSettings.Resolve(item, Plc).Guards.Any(g => g.Id == PlcGuardDefinitions.LowersCpuProtection));
    }
}
