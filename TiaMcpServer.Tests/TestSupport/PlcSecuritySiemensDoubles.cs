// Stand-ins for the CPU protection services the Siemens openness_development security skill names
// (PlcAccessLevelProvider.PlcProtectionAccessLevel, PlcAccessControlConfigurationProvider.Configuration,
// PlcMasterSecretConfigurator.Protect(SecureString)). Unprotect() is the member only a secondary source claims. They
// model member shapes for the reflection tests only and are no evidence of Siemens behavior.
#nullable enable
namespace Siemens.Engineering.HW.Features
{
    public enum PlcProtectionAccessLevel { None, FullAccess, ReadAccess, HMIAccess, NoAccess, FullAccessIncludingFailsafe }

    public enum PlcAccessControlConfiguration { Disabled, EnabledWithAccessControl }

    public sealed class PlcAccessLevelProvider
    {
        private PlcProtectionAccessLevel level = PlcProtectionAccessLevel.NoAccess;

        /// <summary>False models the documented NullReferenceException of the getter without a password.</summary>
        public bool PasswordConfigured { get; set; }

        public PlcProtectionAccessLevel PlcProtectionAccessLevel
        {
            get => PasswordConfigured ? level : throw new NullReferenceException();
            set { level = value; Calls.Add($"level {value}"); }
        }

        public List<string> Calls { get; } = new();

        /// <summary>Models K48 (live V21 2026-11-02): a password for a level that is not more open than the current
        /// level is refused ("Cannot set password for the access level"). One observation, not a Siemens rule.</summary>
        public void SetPassword(PlcProtectionAccessLevel passwordLevel, System.Security.SecureString password)
        {
            if (Openness(passwordLevel) <= Openness(level)) throw new InvalidOperationException("Cannot set password for the access level");
            PasswordConfigured = true;
            Calls.Add($"password {passwordLevel}");
        }

        public void ResetPassword(PlcProtectionAccessLevel passwordLevel)
        {
            if (Openness(passwordLevel) <= Openness(level)) throw new InvalidOperationException("Cannot set password for the access level");
            Calls.Add($"reset {passwordLevel}");
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
        public bool Protected { get; private set; } = true;

        public int UnprotectCalls { get; private set; }

        public PlcAccessControlConfiguration Mode => PlcAccessControlConfiguration.Disabled;

        /// <summary>Object-valued: never observed.</summary>
        public object Owner { get; } = new();

        public void Protect(System.Security.SecureString password) => Protected = true;

        public void Unprotect()
        {
            UnprotectCalls++;
            Protected = false;
        }
    }
}

namespace Siemens.Engineering.Umac
{
    /// <summary>A second Siemens-namespace type with the same short name (ambiguity test only).</summary>
    public sealed class PlcMasterSecretConfigurator
    {
    }
}

namespace Other.Vendor
{
    /// <summary>A non-Siemens type with the documented short name; it must never be chosen.</summary>
    public sealed class PlcMasterSecretConfigurator
    {
        public void Unprotect()
        {
        }
    }
}
