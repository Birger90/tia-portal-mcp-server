using System.Security;

// Executable boundary doubles only: type shapes for the exact canonical-type walk and the
// reflection-based answers of plc_download callback configurations. Type and enum member names
// follow the V21-XML-checked download reference; they claim nothing about how V21 implements them.
namespace Siemens.Engineering.Download.Configurations
{
    public abstract class DownloadConfiguration
    {
    }

    public enum StopModulesSelections { StopAll, NoAction }

    public class StopModules : DownloadConfiguration
    {
        public StopModulesSelections CurrentSelection { get; set; } = StopModulesSelections.NoAction;
    }

    public enum StartModulesSelections { StartModule, NoAction }

    public class StartModules : DownloadConfiguration
    {
        public StartModulesSelections CurrentSelection { get; set; } = StartModulesSelections.NoAction;
    }

    public enum TargetForSoftwareSelections { CPU, PlcSimulationAdvanced }

    public class TargetForSoftware : DownloadConfiguration
    {
        public TargetForSoftwareSelections CurrentSelection { get; set; }
    }

    public enum DataBlockReinitializationSelections { StopPlcAndReinitialize, NoAction }

    public class DataBlockReinitialization : DownloadConfiguration
    {
        public DataBlockReinitializationSelections CurrentSelection { get; set; } = DataBlockReinitializationSelections.StopPlcAndReinitialize;
    }

    public enum ResetModuleSelections { DeleteAll, NoAction }

    public class ResetModule : DownloadConfiguration
    {
        public ResetModuleSelections CurrentSelection { get; set; } = ResetModuleSelections.NoAction;
    }

    /// <summary>A public subclass of a defaulted type in the configurations namespace: its own type, never StopModules.</summary>
    public class StopModulesVariant : StopModules
    {
    }

    public abstract class DownloadCheckConfiguration : DownloadConfiguration
    {
        public bool Checked { get; set; }
    }

    public class UpgradeTargetDevice : DownloadCheckConfiguration
    {
    }

    public abstract class DownloadPasswordConfiguration : DownloadConfiguration
    {
        /// <summary>What the double received, so tests can prove the value arrived without the evidence holding it.</summary>
        public string? ReceivedForTest { get; private set; }

        /// <summary>Audit T1: documented IsSecureCommunication; secure unless a test says otherwise.</summary>
        public bool IsSecureCommunication { get; set; } = true;

        public void SetPassword(SecureString password)
        {
            ArgumentNullException.ThrowIfNull(password);
            var pointer = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(password);
            try
            {
                ReceivedForTest = System.Runtime.InteropServices.Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(pointer);
            }
        }
    }

    public class ModuleWriteAccessPassword : DownloadPasswordConfiguration
    {
    }

    /// <summary>A type in the namespace whose live object offers nothing the reflection access can answer.</summary>
    public class DeleteWebApplication : DownloadConfiguration
    {
    }
}

namespace TiaMcpServer.Tests.PlcDownload.ConfigurationProxies
{
    /// <summary>A non-public runtime type outside the namespace, standing for an implementation subclass.</summary>
    internal sealed class InternalStopModules : Siemens.Engineering.Download.Configurations.StopModules
    {
    }

    /// <summary>A public runtime type outside the namespace: the walk skips it and reaches the public base.</summary>
    public sealed class ProxyStartModules : Siemens.Engineering.Download.Configurations.StartModules
    {
    }

    /// <summary>A public subclass of the variant outside the namespace: the first namespace type is the variant.</summary>
    public sealed class ProxyStopModulesVariant : Siemens.Engineering.Download.Configurations.StopModulesVariant
    {
    }
}
