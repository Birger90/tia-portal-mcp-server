using System.Security;

// Executable stand-ins only: the documented public V21 callback configuration types the plc-online
// FakeWorker scenario presents, so the real reflection-based callback access sees their exact type
// names, a CurrentSelection enum property or a SetPassword(SecureString) method. They claim nothing
// else about the V21 members of these types.
namespace Siemens.Engineering.Upload.Configurations
{
    public enum UploadMissingProductsSelections
    {
        NoAction,
        TryUpload,
    }

    public class UploadMissingProducts
    {
        public UploadMissingProductsSelections CurrentSelection { get; set; }
    }

    public class ModuleReadAccessPassword
    {
        /// <summary>Audit T1: documented IsSecureCommunication ("Is secure communication used."); the stand-in reports a secure channel.</summary>
        public bool IsSecureCommunication { get; set; } = true;

        public void SetPassword(SecureString password)
        {
            if (password is null || password.Length == 0) throw new ArgumentException("An empty password was supplied.");
        }
    }
}

namespace Siemens.Engineering.Online.Configurations
{
    public class OnlinePasswordConfiguration
    {
        /// <summary>Audit T1: documented IsSecureCommunication ("Is secure communication used."); the stand-in reports a secure channel.</summary>
        public bool IsSecureCommunication { get; set; } = true;

        public void SetPassword(SecureString password)
        {
            if (password is null || password.Length == 0) throw new ArgumentException("An empty password was supplied.");
        }
    }

    /// <summary>Audit N2: the documented V21 member names of a user-based online login (values are the fixture's, not Siemens').</summary>
    public enum UserType
    {
        None,
        AnonymousUser,
        GlobalUser,
        PasswordOnly,
        ProjectUser,
        SingleSignOnUser,
    }

    /// <summary>Audit N2: OnlineCredentials (Name, Type, SetPassword); the stand-in keeps only whether a password was supplied.</summary>
    public class OnlineCredentials
    {
        public string Name { get; set; } = string.Empty;

        public UserType Type { get; set; } = UserType.None;

        public bool PasswordSupplied { get; private set; }

        public void SetPassword(SecureString password)
        {
            if (password is null || password.Length == 0) throw new ArgumentException("An empty password was supplied.");
            PasswordSupplied = true;
        }
    }

    /// <summary>Audit N2: OnlineAuthenticationConfiguration (OnlineCredentials, IsSecureCommunication); secure unless the scenario says otherwise.</summary>
    public class OnlineAuthenticationConfiguration
    {
        public bool IsSecureCommunication { get; set; } = true;

        public OnlineCredentials OnlineCredentials { get; } = new();
    }
}
