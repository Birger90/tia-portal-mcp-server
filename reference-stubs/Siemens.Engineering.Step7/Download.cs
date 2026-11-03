// Compile-only V21 declarations. No Siemens implementation code.
#nullable disable // V21 net48 signatures have no nullable annotations.
namespace Siemens.Engineering.Download.Configurations
{
    public abstract class ConsistentBlocksDownload : global::Siemens.Engineering.Download.Configurations.DownloadConfiguration
    {
        public global::Siemens.Engineering.Download.Configurations.ConsistentBlocksDownloadSelections CurrentSelection { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }
    }
    public enum ConsistentBlocksDownloadSelections : int
    {
    }
    public abstract class TargetForSoftware : global::Siemens.Engineering.Download.Configurations.DownloadConfiguration
    {
        public global::Siemens.Engineering.Download.Configurations.TargetForSoftwareSelections CurrentSelection { get => throw new global::System.NotSupportedException(); set => throw new global::System.NotSupportedException(); }
    }
    public enum TargetForSoftwareSelections : int
    {
    }
}
