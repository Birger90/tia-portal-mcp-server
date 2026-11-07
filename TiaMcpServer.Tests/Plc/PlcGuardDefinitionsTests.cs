using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

public class PlcGuardDefinitionsTests
{
    [Fact]
    public void OnlyTheFailSafeAcceptanceTheOnlineMasterSecretAndSyslogAutoAcceptGuardsAskTheUser()
    {
        var byId = PlcGuardDefinitions.Definitions.ToDictionary(guard => guard.Id, guard => guard.Severity);

        Assert.Equal(new Dictionary<string, string>
        {
            [PlcGuardDefinitions.StateUnverifiable] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ObSingletonExists] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.DeletesBlock] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesGroupContents] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesTableContents] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.AddressOverlap] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ChangesInterface] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.BlockRoundTrip] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.MovesTagTable] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.MoveTagRecreates] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesWatchTable] = WriteGuardSeverities.Info,
            [PlcSourceFolderGuards.GeneratesFromSource] = WriteGuardSeverities.Info,
            [PlcSourceFolderGuards.DeletesExternalSource] = WriteGuardSeverities.Info,
            [PlcSourceFolderGuards.RenamesGroup] = WriteGuardSeverities.Info,
            [PlcSourceFolderGuards.ExportOverwritesFiles] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesTextList] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.WorkbookRoundTrip] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ImportsAlarmData] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ExportOverwritesFile] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.FileOnNetworkOrLink] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesTechnologyObject] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DeletesTechnologyObjectGroupContents] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.TechnologyObjectImportReplaces] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.OverwritesFile] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.TechnologyParameterNotHardwareConnection] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.LowersCpuProtection] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.RaisesCpuProtection] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.StoresDbSnapshot] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ReimportsBlockForText] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.BatchRenameRuns] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ImportReplacesTagTable] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ReplacesHardwareConnection] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.S7dclImportMayAlter] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.TextualInterfaceOverwrite] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.SaveBeforeWrite] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.SdReimportsExportForm] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.SdDocumentReplacesBlock] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.SetsBlockProtection] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.LowersBlockProtection] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ChangesProjectLanguages] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ImportsProjectTexts] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.GeneratesWebBlocks] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ChangesPasswordPolicy] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ChangesProjectAccess] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ImportsCertificate] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.SafetyPrintoutIsNoAcceptance] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.SafetyAcceptance] = WriteGuardSeverities.Acknowledge,
            [PlcGuardDefinitions.SafetyLoginRequired] = WriteGuardSeverities.Block,
            [PlcGuardDefinitions.SafetyTiaDecides] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.ImportMayUnlinkMainSafetyBlock] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.OpcUaChangesServer] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.OpcUaLowersAccess] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.DownloadToFolderWritesFiles] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.MasterSecretOnline] = WriteGuardSeverities.Acknowledge,
            // Package G.
            [PlcGuardDefinitions.LoadsActualValuesOnline] = WriteGuardSeverities.Acknowledge,
            [PlcGuardDefinitions.ProtectsProject] = WriteGuardSeverities.Acknowledge,
            [PlcGuardDefinitions.ChangesMarkings] = WriteGuardSeverities.Info,
            [PlcGuardDefinitions.LoadsCamData] = WriteGuardSeverities.Info,
            // Security review F8: automatic acceptance of syslog client certificates needs the human.
            [PlcGuardDefinitions.SyslogAutoAcceptsCertificates] = WriteGuardSeverities.Acknowledge,
        }, byId);
        Assert.Contains(TiaMcpServer.Contracts.Plc.Safety.PlcSafetyPolicy.AcceptanceNotice,
            PlcGuardDefinitions.Definitions.Single(guard => guard.Id == PlcGuardDefinitions.SafetyAcceptance).Description);
        Assert.Equal("plc_deletes_watch_table", PlcGuardDefinitions.DeletesWatchTable);
        Assert.Equal("plc_state_unverifiable", PlcGuardDefinitions.StateUnverifiable);
        Assert.Equal("plc_address_overlap", PlcGuardDefinitions.AddressOverlap);
        Assert.Equal("plc_ob_singleton_exists", PlcGuardDefinitions.ObSingletonExists);
        _ = new WriteGuardCatalog(PlcGuardDefinitions.Definitions);
    }
}
