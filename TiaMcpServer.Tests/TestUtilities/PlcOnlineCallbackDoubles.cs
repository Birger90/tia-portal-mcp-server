using System.Security;

// Executable boundary doubles only: type shapes for the canonical-type walk and the reflection-based
// answers of station-upload and online-legitimation callback configurations. They claim nothing about
// the V21 members of these types beyond the documented CurrentSelection and SetPassword(SecureString).
namespace Siemens.Engineering.Upload.Configurations
{
    public enum UploadMissingProductsSelections
    {
        NoAction,
        TryUpload,
    }

    public abstract class UploadConfiguration
    {
    }

    public class UploadMissingProducts : UploadConfiguration
    {
        public UploadMissingProductsSelections CurrentSelection { get; set; }
    }

    public class ModuleReadAccessPassword : UploadConfiguration
    {
        public int PasswordLength { get; private set; }

        /// <summary>Audit T1: documented IsSecureCommunication; secure unless a test says otherwise.</summary>
        public bool IsSecureCommunication { get; set; } = true;

        public void SetPassword(SecureString password) => PasswordLength = password.Length;
    }

    /// <summary>A configuration that is neither a selection nor a password: it cannot be answered.</summary>
    public class UploadNotice : UploadConfiguration
    {
    }

    /// <summary>A selection whose setter rejects every value.</summary>
    public class RejectingSelection : UploadConfiguration
    {
        public UploadMissingProductsSelections CurrentSelection
        {
            get => UploadMissingProductsSelections.NoAction;
            set => throw new InvalidOperationException("TIA Portal rejected the selection.");
        }
    }
}

namespace TiaMcpServer.Tests.PlcOnline.CallbackProxies
{
    /// <summary>A non-public runtime type outside the namespace, standing for an implementation subclass.</summary>
    internal sealed class InternalUploadMissingProducts : Siemens.Engineering.Upload.Configurations.UploadMissingProducts
    {
    }

    /// <summary>A public type outside every configuration namespace: it has no decision key.</summary>
    public sealed class ForeignConfiguration
    {
        public Siemens.Engineering.Upload.Configurations.UploadMissingProductsSelections CurrentSelection { get; set; }
    }
}
