using System;
using System.Collections.Generic;
using Siemens.Engineering.Connection;
using Siemens.Engineering.Download;
using Siemens.Engineering.Download.Configurations;
using Siemens.Engineering.HW;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks for every Siemens member plc_download and list_download_targets use.
    // Building this probe against the installed V21 PublicAPI is the mechanical check of the stubs.
    // Enum members are not referenced: the worker resolves them by name at runtime (Enum.Parse/ToString).
    internal static class DownloadReferenceSurface
    {
        private static void Provider(DeviceItem cpu)
        {
            DownloadProvider provider = cpu.GetService<DownloadProvider>();
            ConnectionConfiguration configuration = provider.Configuration;
            Func<IConfiguration, DownloadConfigurationDelegate, DownloadConfigurationDelegate, DownloadOptions, DownloadResult> download = provider.Download;
        }

        private static void Path(ConnectionConfiguration configuration, ConfigurationMode mode,
            ConfigurationPcInterface pcInterface, ConfigurationTargetInterface targetInterface)
        {
            IEnumerable<ConfigurationMode> modes = configuration.Modes;
            string modeName = mode.Name;
            IEnumerable<ConfigurationPcInterface> pcInterfaces = mode.PcInterfaces;
            string pcInterfaceName = pcInterface.Name;
            int pcInterfaceNumber = pcInterface.Number;
            IEnumerable<ConfigurationTargetInterface> targetInterfaces = pcInterface.TargetInterfaces;
            string targetName = targetInterface.Name;
            IConfiguration target = targetInterface;
        }

        private static void Result(DownloadResult result, DownloadResultMessage message)
        {
            DownloadResultState state = result.State;
            int errors = result.ErrorCount;
            int warnings = result.WarningCount;
            IEnumerable<DownloadResultMessage> messages = result.Messages;
            string text = message.Message;
            DownloadResultState messageState = message.State;
            IEnumerable<DownloadResultMessage> children = message.Messages;
        }

        private static void Configurations(DownloadConfiguration configuration, StopModules stop,
            ConsistentBlocksDownload blocks, AlarmTextLibrariesDownload alarms, OverwriteSystemData systemData,
            TargetForSoftware target, StartModules start)
        {
            DownloadConfigurationDelegate callback = (DownloadConfiguration presented) => { };
            string message = configuration.Message;
            DownloadConfiguration[] all = { stop, blocks, alarms, systemData, target, start };
            StopModulesSelections stopSelection = stop.CurrentSelection;
            ConsistentBlocksDownloadSelections blocksSelection = blocks.CurrentSelection;
            AlarmTextLibrariesDownloadSelections alarmsSelection = alarms.CurrentSelection;
            OverwriteSystemDataSelections systemDataSelection = systemData.CurrentSelection;
            TargetForSoftwareSelections targetSelection = target.CurrentSelection;
            StartModulesSelections startSelection = start.CurrentSelection;
            stop.CurrentSelection = stopSelection;
            blocks.CurrentSelection = blocksSelection;
            alarms.CurrentSelection = alarmsSelection;
            systemData.CurrentSelection = systemDataSelection;
            target.CurrentSelection = targetSelection;
            start.CurrentSelection = startSelection;
        }
    }
}
