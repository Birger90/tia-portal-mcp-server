using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The fields of download_to_folder (issue #16); plcName, directoryPath, passwordVariable and portalConfirmations are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("download_to_folder: true downloads an R/H system through its RHDownloadProvider.Download(DirectoryInfo, DownloadConfigurationDelegate) instead of the PLC's DownloadProvider. Optional, default false.")]
    public bool? RhSystem { get; set; }
}
