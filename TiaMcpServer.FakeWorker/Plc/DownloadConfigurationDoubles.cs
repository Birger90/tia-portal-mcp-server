using System.Security;

// Executable FakeWorker stand-ins only: live-object shapes for the reflection-based download
// configuration access (PlcDownloadReflectionAccess). Type and enum member names follow the
// V21-XML-checked download reference used for the provenance table; the members are those the
// reflection access reads (CurrentSelection, Checked, SetPassword). They claim nothing about how
// TIA Portal implements these types.
namespace Siemens.Engineering.Download.Configurations
{
    public abstract class DownloadConfiguration
    {
        protected DownloadConfiguration(string message) => Message = message;

        public string Message { get; }
    }

    public enum StopModulesSelections { StopAll, NoAction }

    public sealed class StopModules(string message) : DownloadConfiguration(message)
    {
        public StopModulesSelections CurrentSelection { get; set; } = StopModulesSelections.NoAction;
    }

    public enum ConsistentBlocksDownloadSelections { ConsistentDownload }

    public sealed class ConsistentBlocksDownload(string message) : DownloadConfiguration(message)
    {
        public ConsistentBlocksDownloadSelections CurrentSelection { get; set; }
    }

    public enum TargetForSoftwareSelections { CPU, PlcSimulationAdvanced }

    public sealed class TargetForSoftware(string message) : DownloadConfiguration(message)
    {
        public TargetForSoftwareSelections CurrentSelection { get; set; }
    }

    public enum StartModulesSelections { StartModule, NoAction }

    public sealed class StartModules(string message) : DownloadConfiguration(message)
    {
        public StartModulesSelections CurrentSelection { get; set; } = StartModulesSelections.NoAction;
    }

    public enum DataBlockReinitializationSelections { StopPlcAndReinitialize, NoAction }

    public sealed class DataBlockReinitialization(string message) : DownloadConfiguration(message)
    {
        public DataBlockReinitializationSelections CurrentSelection { get; set; } = DataBlockReinitializationSelections.StopPlcAndReinitialize;
    }

    public abstract class DownloadCheckConfiguration(string message) : DownloadConfiguration(message)
    {
        public bool Checked { get; set; }
    }

    public sealed class UpgradeTargetDevice(string message) : DownloadCheckConfiguration(message);

    public abstract class DownloadPasswordConfiguration(string message) : DownloadConfiguration(message)
    {
        /// <summary>Whether a password was supplied; the stand-in never keeps or reveals it.</summary>
        public bool PasswordSupplied { get; private set; }

        public void SetPassword(SecureString password)
        {
            ArgumentNullException.ThrowIfNull(password);
            PasswordSupplied = password.Length > 0;
        }
    }

    public sealed class ModuleWriteAccessPassword(string message) : DownloadPasswordConfiguration(message);
}
