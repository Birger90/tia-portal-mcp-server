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
        public void SetPassword(SecureString password)
        {
            if (password is null || password.Length == 0) throw new ArgumentException("An empty password was supplied.");
        }
    }
}
