using System.Security;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Plc.Security;
using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Security;

/// <summary>
/// The Siemens-free runtime binding of the CPU protection members against stand-in types (they model only the member
/// shapes the Siemens security skill names; they are no evidence of Siemens behavior).
/// </summary>
public sealed class PlcSecurityReflectionTests
{
    private static bool Recoverable(Exception exception) => exception is RecoverableStandIn;

    private static readonly IReadOnlyList<Type> Offered = new[]
    {
        typeof(PlcAccessLevelProvider), typeof(PlcAccessControlConfigurationProvider), typeof(PlcMasterSecretConfigurator), typeof(Other.Vendor.PlcMasterSecretConfigurator),
    };

    [Fact]
    public void ServicesAreFoundByTheDocumentedFullName_OrByShortNameInASiemensNamespaceOnly()
    {
        Assert.Equal(typeof(PlcAccessLevelProvider), PlcSecurityReflection.FindByFullName(Offered, PlcSecurityPolicy.AccessLevelProviderType));
        Assert.Equal(typeof(PlcAccessControlConfigurationProvider), PlcSecurityReflection.FindByFullName(Offered, PlcSecurityPolicy.AccessControlProviderType));
        Assert.Null(PlcSecurityReflection.FindByFullName(new[] { typeof(PlcMasterSecretConfigurator) }, PlcSecurityPolicy.AccessLevelProviderType));
        // Other.Vendor.PlcMasterSecretConfigurator is not in a Siemens.Engineering namespace and is never chosen.
        Assert.Equal(typeof(PlcMasterSecretConfigurator), PlcSecurityReflection.FindByShortName(Offered, PlcSecurityPolicy.MasterSecretConfiguratorName));
        Assert.Null(PlcSecurityReflection.FindByShortName(new[] { typeof(Other.Vendor.PlcMasterSecretConfigurator) }, PlcSecurityPolicy.MasterSecretConfiguratorName));
    }

    [Fact]
    public void TwoSiemensServicesWithTheSameShortName_AreRefusedWithoutAChoice()
    {
        var error = Assert.Throws<PlcSecurityBindingException>(() => PlcSecurityReflection.FindByShortName(
            new[] { typeof(PlcMasterSecretConfigurator), typeof(Siemens.Engineering.Umac.PlcMasterSecretConfigurator) }, PlcSecurityPolicy.MasterSecretConfiguratorName));
        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, error.Category);
        Assert.Contains("none is chosen", error.Message);
    }

    /// <summary>K48 (live V21 2026-11-02): SetPassword before the level, at FullAccessIncludingFailsafe, was refused
    /// "Cannot set password for the access level"; the level is set first, then the passwords.</summary>
    [Fact]
    public void AccessLevel_IsSetBeforeThePasswordsOfTheMoreOpenLevels()
    {
        var provider = new PlcAccessLevelProvider { PasswordConfigured = true };
        provider.PlcProtectionAccessLevel = PlcProtectionAccessLevel.FullAccessIncludingFailsafe;
        provider.Calls.Clear();
        var cpu = new PlcSecurityCpu("PLC_F1", "PLC_F1", Offered, type => type == typeof(PlcAccessLevelProvider) ? provider : null);
        var info = new PlcSecurityRequestInfo
        {
            AccessLevel = "ReadAccess",
            AccessLevelPasswords = { new() { Level = "FullAccess", Source = "A" }, new() { Level = "FullAccessIncludingFailsafe", Source = "B" } },
        };

        var (result, _) = PlcSecurityRun.Write(PlcSecurityMethods.SetAccessLevel, cpu, info, Recoverable, _ => Secret());

        Assert.Equal(new[] { "level ReadAccess", "password FullAccessIncludingFailsafe", "password FullAccess" }, provider.Calls);
        Assert.Equal(("FullAccessIncludingFailsafe", "ReadAccess"), (result.Before, result.After));
    }

    /// <summary>K48 (live V21 2026-11-02): ResetPassword(FullAccessIncludingFailsafe) after setting that level was refused;
    /// the reset runs first, while the more protective level still holds.</summary>
    [Fact]
    public void Opening_TheCpu_ResetsThePasswordBeforeTheLevel()
    {
        var provider = new PlcAccessLevelProvider { PasswordConfigured = true };
        provider.PlcProtectionAccessLevel = PlcProtectionAccessLevel.FullAccess;
        provider.Calls.Clear();
        var cpu = new PlcSecurityCpu("PLC_F1", "PLC_F1", Offered, type => type == typeof(PlcAccessLevelProvider) ? provider : null);
        var info = new PlcSecurityRequestInfo { AccessLevel = "FullAccessIncludingFailsafe", ResetAccessLevelPasswords = { "FullAccessIncludingFailsafe" } };

        PlcSecurityRun.Write(PlcSecurityMethods.SetAccessLevel, cpu, info, Recoverable, _ => Secret());

        Assert.Equal(new[] { "reset FullAccessIncludingFailsafe", "level FullAccessIncludingFailsafe" }, provider.Calls);
    }

    [Fact]
    public void AccessLevel_APasswordRefusedAfterTheLevelChanged_RestoresTheLevelReadBefore()
    {
        var provider = new PlcAccessLevelProvider { PasswordConfigured = true };
        provider.PlcProtectionAccessLevel = PlcProtectionAccessLevel.FullAccessIncludingFailsafe;
        provider.Calls.Clear();
        var cpu = new PlcSecurityCpu("PLC_F1", "PLC_F1", Offered, type => type == typeof(PlcAccessLevelProvider) ? provider : null);
        // A password for a level not more open than FullAccess is refused by the double (the K48 observation).
        var info = new PlcSecurityRequestInfo { AccessLevel = "FullAccess", AccessLevelPasswords = { new() { Level = "ReadAccess", Source = "A" } } };

        var error = Assert.Throws<InvalidOperationException>(() =>
            PlcSecurityRun.Write(PlcSecurityMethods.SetAccessLevel, cpu, info, e => e is InvalidOperationException, _ => Secret()));

        Assert.Contains("Cannot set password", error.Message);
        Assert.Equal(new[] { "level FullAccess", "level FullAccessIncludingFailsafe" }, provider.Calls);
        Assert.Equal(PlcProtectionAccessLevel.FullAccessIncludingFailsafe, provider.PlcProtectionAccessLevel);
    }

    private static SecureString Secret()
    {
        var secret = new SecureString();
        foreach (var c in "x1") secret.AppendChar(c);
        secret.MakeReadOnly();
        return secret;
    }

    /// <summary>D14 (live, V21, 2026-11-02): GetServiceInfos() listed PlcAccessControlConfigurationProvider, GetService returned null.</summary>
    [Fact]
    public void ListedServiceThatGetServiceDoesNotReturn_IsUnavailableInTheRead_AndRefusedByAWrite()
    {
        var level = new PlcAccessLevelProvider { PasswordConfigured = true };
        var cpu = new PlcSecurityCpu("PLC_1", "Station_1", Offered, type => type == typeof(PlcAccessLevelProvider) ? level : null);

        var read = PlcSecurityRun.Read(cpu, Recoverable);
        Assert.True(read.AccessControl.ServiceOffered);
        Assert.False(read.AccessControl.Readable);
        Assert.Contains("GetService returned null", read.AccessControl.Note);
        Assert.True(read.AccessLevel.Readable);
        Assert.True(read.ConfidentialDataProtection.ServiceOffered);
        Assert.Empty(read.ConfidentialDataProtection.ObservedMembers);
        Assert.Contains("GetService returned null", read.ConfidentialDataProtection.Note);

        var error = Assert.Throws<PlcSecurityBindingException>(() => PlcSecurityRun.Write(PlcSecurityMethods.DisableConfidentialDataProtection, cpu,
            new PlcSecurityRequestInfo(), Recoverable, _ => throw new InvalidOperationException("no password is resolved")));
        Assert.Equal(WorkerFailureCategories.UnsupportedCapability, error.Category);
        Assert.Contains("Nothing was changed.", error.Message);
    }

    [Fact]
    public void AccessLevelRead_WithoutPassword_IsUnreadableWithTheDocumentedNote()
    {
        var provider = new PlcAccessLevelProvider { PasswordConfigured = false };
        var setting = PlcSecurityReflection.ReadSetting(PlcSecurityPolicy.AccessLevelProviderType, PlcSecurityPolicy.AccessLevelProperty,
            PlcSecurityPolicy.AccessLevelEnumName, typeof(PlcAccessLevelProvider), provider, Recoverable, PlcSecurityPolicy.NoPasswordReadNote);

        Assert.True(setting.ServiceOffered);
        Assert.False(setting.Readable);
        Assert.Null(setting.Value);
        Assert.True(setting.Writable);
        Assert.Equal(PlcSecurityPolicy.NoPasswordReadNote, setting.Note);
        Assert.Equal(PlcSecurityPolicy.DocumentedAccessLevels, setting.AvailableValues);
    }

    [Fact]
    public void AccessLevelRead_WithPassword_ReportsTheMemberName()
    {
        var provider = new PlcAccessLevelProvider { PasswordConfigured = true };
        var setting = PlcSecurityReflection.ReadSetting(PlcSecurityPolicy.AccessLevelProviderType, PlcSecurityPolicy.AccessLevelProperty,
            PlcSecurityPolicy.AccessLevelEnumName, typeof(PlcAccessLevelProvider), provider, Recoverable, PlcSecurityPolicy.NoPasswordReadNote);

        Assert.True(setting.Readable);
        Assert.Equal("NoAccess", setting.Value);
        Assert.Null(setting.Note);
    }

    [Fact]
    public void SettingRead_WithoutService_SaysSoAndReadsNothing()
    {
        var setting = PlcSecurityReflection.ReadSetting(PlcSecurityPolicy.AccessControlProviderType, PlcSecurityPolicy.AccessControlProperty,
            PlcSecurityPolicy.AccessControlEnumName, null, null, Recoverable, null);

        Assert.False(setting.ServiceOffered);
        Assert.Null(setting.ServiceType);
        Assert.False(setting.Readable);
        Assert.False(setting.Writable);
        Assert.Contains("does not list", setting.Note);
    }

    [Fact]
    public void SettingRead_MissingPropertyOrForeignType_IsReportedNotGuessed()
    {
        var missing = PlcSecurityReflection.ReadSetting("S", "Absent", PlcSecurityPolicy.AccessLevelEnumName,
            typeof(PlcAccessLevelProvider), new PlcAccessLevelProvider(), Recoverable, null);
        Assert.False(missing.Readable);
        Assert.Contains("api_member_missing", missing.Note);

        var integer = PlcSecurityReflection.ReadSetting("S", PlcSecurityPolicy.AccessLevelProperty, PlcSecurityPolicy.AccessLevelEnumName,
            typeof(IntegerLevelProvider), new IntegerLevelProvider(), Recoverable, null);
        Assert.True(integer.Readable);
        Assert.Empty(integer.AvailableValues);
        Assert.Contains("not the documented enum", integer.Note);
    }

    [Fact]
    public void GetterFailures_RecoverableIsUnreadable_OtherwiseThePortalErrorPropagatesUnwrapped()
    {
        var recoverable = PlcSecurityReflection.ReadValue(typeof(ThrowingProvider).GetProperty(nameof(ThrowingProvider.PlcProtectionAccessLevel))!,
            new ThrowingProvider(new RecoverableStandIn("busy")), Recoverable, null);
        Assert.False(recoverable.Readable);
        Assert.Contains("RecoverableStandIn: busy", recoverable.Note);

        Assert.Throws<InvalidOperationException>(() => PlcSecurityReflection.ReadValue(
            typeof(ThrowingProvider).GetProperty(nameof(ThrowingProvider.PlcProtectionAccessLevel))!,
            new ThrowingProvider(new InvalidOperationException("lost")), Recoverable, null));
    }

    [Fact]
    public void WritableEnum_RequiresTheDocumentedEnumAndAPublicSetter()
    {
        Assert.NotNull(PlcSecurityReflection.RequireWritableEnum(typeof(PlcAccessLevelProvider), PlcSecurityPolicy.AccessLevelProperty, PlcSecurityPolicy.AccessLevelEnumName));
        foreach (var type in new[] { typeof(ReadOnlyLevelProvider), typeof(IntegerLevelProvider), typeof(PlcMasterSecretConfigurator) })
        {
            var error = Assert.Throws<PlcSecurityBindingException>(() =>
                PlcSecurityReflection.RequireWritableEnum(type, PlcSecurityPolicy.AccessLevelProperty, PlcSecurityPolicy.AccessLevelEnumName));
            Assert.Equal(WorkerFailureCategories.ApiMemberMissing, error.Category);
            Assert.EndsWith("Nothing was changed.", error.Message);
        }
    }

    [Fact]
    public void EnumMembersParseExactlyAndCaseSensitively()
    {
        Assert.Equal(PlcProtectionAccessLevel.FullAccess, PlcSecurityReflection.ParseExact(typeof(PlcProtectionAccessLevel), "FullAccess"));
        Assert.Equal(PlcProtectionAccessLevel.FullAccessIncludingFailsafe, PlcSecurityReflection.ParseExact(typeof(PlcProtectionAccessLevel), "FullAccessIncludingFailsafe"));
        Assert.Throws<PlcSecurityBindingException>(() => PlcSecurityReflection.ParseExact(typeof(PlcProtectionAccessLevel), "fullaccess"));
        Assert.Throws<PlcSecurityBindingException>(() => PlcSecurityReflection.ParseExact(typeof(ShortLevels), "FullAccessIncludingFailsafe"));
    }

    [Fact]
    public void SettingTheLevel_WritesThroughTheBoundSetter()
    {
        var provider = new PlcAccessLevelProvider { PasswordConfigured = true };
        var property = PlcSecurityReflection.RequireWritableEnum(typeof(PlcAccessLevelProvider), PlcSecurityPolicy.AccessLevelProperty, PlcSecurityPolicy.AccessLevelEnumName);
        PlcSecurityReflection.SetValue(property, provider, PlcSecurityReflection.ParseExact(property.PropertyType, "FullAccess"));
        Assert.Equal("FullAccess", PlcSecurityReflection.ReadValue(property, provider, Recoverable, null).Value);

        var rejecting = new RejectingLevelProvider();
        var rejectingProperty = typeof(RejectingLevelProvider).GetProperty(nameof(RejectingLevelProvider.PlcProtectionAccessLevel))!;
        Assert.Throws<InvalidOperationException>(() => PlcSecurityReflection.SetValue(rejectingProperty, rejecting, PlcProtectionAccessLevel.FullAccess));
    }

    [Fact]
    public void Unprotect_IsBoundOnlyAsExactlyVoidUnprotectWithoutParameters()
    {
        Assert.NotNull(PlcSecurityReflection.FindUnprotect(typeof(PlcMasterSecretConfigurator), out var exact));
        Assert.Equal(new[] { "void Unprotect()" }, exact);

        Assert.Null(PlcSecurityReflection.FindUnprotect(typeof(ConfiguratorWithoutUnprotect), out var none));
        Assert.Empty(none);

        // A bool Unprotect() and an Unprotect(String) are other signatures: listed for diagnosis, never called.
        Assert.Null(PlcSecurityReflection.FindUnprotect(typeof(ConfiguratorWithOtherUnprotects), out var others));
        Assert.Equal(new[] { "Boolean Unprotect()", "void Unprotect(String)" }, others);
    }

    [Fact]
    public void Protect_IsOnlyReportedAsBindable()
    {
        Assert.True(PlcSecurityReflection.HasProtect(typeof(PlcMasterSecretConfigurator)));
        Assert.False(PlcSecurityReflection.HasProtect(typeof(ConfiguratorWithOtherUnprotects)));
    }

    [Fact]
    public void Unprotect_RunsOnce_AndItsPortalExceptionPropagatesUnwrapped()
    {
        var configurator = new PlcMasterSecretConfigurator();
        PlcSecurityReflection.InvokeParameterless(PlcSecurityReflection.FindUnprotect(typeof(PlcMasterSecretConfigurator), out _)!, configurator);
        Assert.Equal(1, configurator.UnprotectCalls);
        Assert.False(configurator.Protected);

        var failing = new FailingConfigurator();
        Assert.Throws<InvalidOperationException>(() => PlcSecurityReflection.InvokeParameterless(
            PlcSecurityReflection.FindUnprotect(typeof(FailingConfigurator), out _)!, failing));
    }

    [Fact]
    public void Observe_ListsReadableScalarPropertiesOnly_Sorted()
    {
        var members = PlcSecurityReflection.Observe(typeof(PlcMasterSecretConfigurator), new PlcMasterSecretConfigurator(), Recoverable);
        Assert.Equal(new[] { "Mode", "Protected", "UnprotectCalls" }, members.Select(m => m.Name));
        Assert.Equal("True", members.Single(m => m.Name == "Protected").Value);
        Assert.Equal("System.Boolean", members.Single(m => m.Name == "Protected").Type);
        Assert.All(members, m => Assert.True(m.Readable));
    }

    [Fact]
    public void Signatures_UseShortTypeNames()
        => Assert.Equal("void Unprotect()", PlcSecurityReflection.Signature(typeof(PlcMasterSecretConfigurator).GetMethod("Unprotect")!));

    private sealed class RecoverableStandIn(string message) : Exception(message);

    public sealed class IntegerLevelProvider
    {
        public int PlcProtectionAccessLevel { get; set; }
    }

    public sealed class ReadOnlyLevelProvider
    {
        public PlcProtectionAccessLevel PlcProtectionAccessLevel => PlcProtectionAccessLevel.FullAccess;
    }

    public sealed class ThrowingProvider(Exception failure)
    {
        public PlcProtectionAccessLevel PlcProtectionAccessLevel => throw failure;
    }

    public sealed class RejectingLevelProvider
    {
        public PlcProtectionAccessLevel PlcProtectionAccessLevel
        {
            get => PlcProtectionAccessLevel.NoAccess;
            set => throw new InvalidOperationException("rejected");
        }
    }

    public enum ShortLevels { FullAccess }

    public sealed class ConfiguratorWithoutUnprotect
    {
        public void Protect(SecureString password) { }
    }

    public sealed class ConfiguratorWithOtherUnprotects
    {
        public bool Unprotect() => true;

        public void Unprotect(string reason) { }
    }

    public sealed class FailingConfigurator
    {
        public void Unprotect() => throw new InvalidOperationException("rejected");
    }
}
