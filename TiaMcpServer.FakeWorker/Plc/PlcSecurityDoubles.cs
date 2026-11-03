// Stand-ins for the CPU protection services with the members the Siemens openness_development security skill and the
// "TIA Portal Openness V21 - Functions on Devices" member list name (PlcAccessLevelProvider: PlcProtectionAccessLevel,
// SetPassword, ResetPassword; PlcMasterSecretConfigurator: MasterSecretConfiguration, Protect, ProtectAllPlcConfiguration,
// ProtectAllPlcConfigurationWithPassword, Unprotect, Unprotect(SecureString), UnprotectAllPlcConfiguration, ChangePassword).
// The access level getter throws NullReferenceException while no level password is configured (documented gotcha).
// Member shapes and a simple state model only; no evidence of Siemens behavior.
namespace Siemens.Engineering.HW.Features
{
    public enum PlcProtectionAccessLevel { None, FullAccess, ReadAccess, HMIAccess, NoAccess, FullAccessIncludingFailsafe }

    public enum PlcAccessControlConfiguration { Disabled, EnabledWithAccessControl }

    public enum PlcMasterSecretConfigurationKind { NotProtected, Protected, ProtectedAllConfiguration }

    public sealed class PlcAccessLevelProvider
    {
        private PlcProtectionAccessLevel _level = PlcProtectionAccessLevel.NoAccess;
        private readonly HashSet<PlcProtectionAccessLevel> _passwords = new();

        public PlcProtectionAccessLevel PlcProtectionAccessLevel
        {
            get => _passwords.Count > 0 ? _level : throw new NullReferenceException();
            set => _level = value;
        }

        public void SetPassword(PlcProtectionAccessLevel level, System.Security.SecureString password)
        {
            // Models TIA Portal's password rules only as far as an empty password goes, plus K48 (live V21
            // 2026-11-02, one observation): a password for a level not more open than the current one is refused.
            if (password.Length == 0) throw new ArgumentException("The password is empty.");
            if (Openness(level) <= Openness(_level)) throw new InvalidOperationException("Cannot set password for the access level");
            _passwords.Add(level);
        }

        public void ResetPassword(PlcProtectionAccessLevel level)
        {
            // K48 (live V21 2026-11-02): ResetPassword of a level not more open than the current one is refused too.
            if (Openness(level) <= Openness(_level)) throw new InvalidOperationException("Cannot set password for the access level");
            _passwords.Remove(level);
        }

        private static int Openness(PlcProtectionAccessLevel value) => value switch
        {
            PlcProtectionAccessLevel.FullAccessIncludingFailsafe => 5,
            PlcProtectionAccessLevel.FullAccess => 4,
            PlcProtectionAccessLevel.ReadAccess => 3,
            PlcProtectionAccessLevel.HMIAccess => 2,
            _ => 1,
        };
    }

    public sealed class PlcAccessControlConfigurationProvider
    {
        public PlcAccessControlConfiguration Configuration { get; set; } = PlcAccessControlConfiguration.Disabled;
    }

    public sealed class PlcMasterSecretConfigurator
    {
        private string? _secret = "initial";

        public PlcMasterSecretConfigurationKind MasterSecretConfiguration { get; private set; } = PlcMasterSecretConfigurationKind.Protected;

        public void Protect(System.Security.SecureString password)
        {
            _secret = Text(password);
            MasterSecretConfiguration = PlcMasterSecretConfigurationKind.Protected;
        }

        public void ProtectAllPlcConfiguration() => MasterSecretConfiguration = PlcMasterSecretConfigurationKind.ProtectedAllConfiguration;

        public void ProtectAllPlcConfigurationWithPassword(System.Security.SecureString password)
        {
            _secret = Text(password);
            MasterSecretConfiguration = PlcMasterSecretConfigurationKind.ProtectedAllConfiguration;
        }

        public void Unprotect()
        {
            _secret = null;
            MasterSecretConfiguration = PlcMasterSecretConfigurationKind.NotProtected;
        }

        public void Unprotect(System.Security.SecureString password)
        {
            if (_secret is not null && _secret != "initial" && Text(password) != _secret) throw new InvalidOperationException("Wrong password.");
            Unprotect();
        }

        public void UnprotectAllPlcConfiguration() => MasterSecretConfiguration = PlcMasterSecretConfigurationKind.Protected;

        public void ChangePassword(System.Security.SecureString current, System.Security.SecureString changed)
        {
            if (_secret is not null && _secret != "initial" && Text(current) != _secret) throw new InvalidOperationException("Wrong password.");
            _secret = Text(changed);
        }

        private static string Text(System.Security.SecureString secure)
        {
            var pointer = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(secure);
            try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer)!; }
            finally { System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
        }
    }
}

namespace Siemens.Engineering.Umac
{
    /// <summary>A configurator without Unprotect (scenario plc-security-no-unprotect).</summary>
    public sealed class PlcMasterSecretConfigurator
    {
        public bool Protected => true;

        public void Protect(System.Security.SecureString password)
        {
        }
    }
}
