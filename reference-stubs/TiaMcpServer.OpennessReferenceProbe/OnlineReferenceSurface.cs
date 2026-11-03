using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.Compare;
using Siemens.Engineering.Connection;
using Siemens.Engineering.HW;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.MasterCopies;
using Siemens.Engineering.Online;
using Siemens.Engineering.Online.Configurations;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.WatchAndForceTables;
using Siemens.Engineering.Upload;
using Siemens.Engineering.Upload.Configurations;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks for every Siemens member the online/offline compare, online state,
    // station upload and watch/force table operations compile against. Building this probe against the
    // installed V21 PublicAPI is the mechanical check of the stubs. Members the worker reaches only at
    // runtime (PlcSoftware.UpdateProgram, PlcWatchTable/PlcForceTable.Entries, entry attributes, callback
    // CurrentSelection/SetPassword) are deliberately absent: their signatures are not compiled in.
    internal static class OnlineReferenceSurface
    {
        private static void Compare(PlcSoftware software, PlcSoftware other, Project project, CompareResult result, CompareResultElement element)
        {
            Func<ISoftwareCompareTarget, CompareResult> compareTo = software.CompareTo;
            Func<CompareResult> compareToOnline = software.CompareToOnline;
            ISoftwareCompareTarget plcTarget = other;
            ProjectLibrary library = project.ProjectLibrary;
            ISoftwareCompareTarget libraryTarget = library;
            MasterCopySystemFolder folder = library.MasterCopyFolder;
            MasterCopyComposition masterCopies = folder.MasterCopies;
            Func<string, MasterCopy> find = masterCopies.Find;
            MasterCopy masterCopy = find("probe");
            ISoftwareCompareTarget masterCopyTarget = masterCopy;
            CompareResultElement root = result.RootElement;
            string left = element.LeftName;
            string right = element.RightName;
            CompareResultState state = element.ComparisonResult;
            string detail = element.DetailedInformation;
            IEnumerable<CompareResultElement> children = element.Elements;
        }

        private static void Online(DeviceItem cpu, OnlineProvider provider, ConnectionConfiguration configuration,
            ConfigurationTargetInterface target, ConfigurationPcInterface pcInterface)
        {
            OnlineProvider service = cpu.GetService<OnlineProvider>();
            ConnectionConfiguration connection = provider.Configuration;
            OnlineState state = provider.State;
            Func<OnlineState> goOnline = provider.GoOnline;
            Action goOffline = provider.GoOffline;
            bool configured = configuration.IsConfigured;
            Func<ConfigurationTargetInterface, bool> apply = configuration.ApplyConfiguration;
            OnlineConfigurationDelegate legitimation = (OnlineConfiguration presented) => { };
            configuration.OnlineLegitimation += legitimation;
            configuration.OnlineLegitimation -= legitimation;
            ConfigurationAddressComposition addresses = pcInterface.Addresses;
            IEnumerable<ConfigurationAddress> enumerated = addresses;
            Func<string, ConfigurationAddress> create = addresses.Create;
        }

        private static void Upload(ProjectBase project, StationUploadProvider provider, UploadResult result, UploadResultMessage resultMessage,
            UploadConfiguration configuration)
        {
            StationUploadProvider service = project.GetService<StationUploadProvider>();
            ConnectionConfiguration connection = provider.Configuration;
            UploadConfigurationDelegate callback = (UploadConfiguration presented) => { };
            Func<ConfigurationAddress, UploadConfigurationDelegate, UploadResult> upload = provider.StationUpload;
            UploadResultState state = result.State;
            int errors = result.ErrorCount;
            IEnumerable<UploadResultMessage> messages = result.Messages;
            IEngineeringObject message = resultMessage;
            Device uploaded = result.UploadedStation;
            string text = configuration.Message;
        }

        private static void WatchAndForceTables(PlcSoftware software, PlcWatchAndForceTableUserGroup userGroup,
            PlcWatchTable watchTable, PlcForceTable forceTable)
        {
            PlcWatchAndForceTableSystemGroup root = software.WatchAndForceTableGroup;
            PlcWatchAndForceTableGroup group = root;
            PlcWatchAndForceTableGroup user = userGroup;
            string groupName = group.Name;
            PlcWatchAndForceTableUserGroupComposition groups = group.Groups;
            IEnumerable<PlcWatchAndForceTableUserGroup> userGroups = groups;
            Func<string, PlcWatchAndForceTableUserGroup> createGroup = groups.Create;
            Action deleteGroup = userGroup.Delete;
            PlcWatchTableComposition watchTables = group.WatchTables;
            IEnumerable<PlcWatchTable> watchTableItems = watchTables;
            Func<string, PlcWatchTable> createWatchTable = watchTables.Create;
            Func<FileInfo, ImportOptions, IList<PlcWatchTable>> importWatchTable = watchTables.Import;
            string watchName = watchTable.Name;
            bool watchConsistent = watchTable.IsConsistent;
            Action<FileInfo, ExportOptions> exportWatchTable = watchTable.Export;
            Action deleteWatchTable = watchTable.Delete;
            IEngineeringObject watchObject = watchTable;
            PlcForceTableComposition forceTables = group.ForceTables;
            IEnumerable<PlcForceTable> forceTableItems = forceTables;
            Func<FileInfo, ImportOptions, IList<PlcForceTable>> importForceTable = forceTables.Import;
            string forceName = forceTable.Name;
            bool forceConsistent = forceTable.IsConsistent;
            Action<FileInfo, ExportOptions> exportForceTable = forceTable.Export;
        }
    }
}
