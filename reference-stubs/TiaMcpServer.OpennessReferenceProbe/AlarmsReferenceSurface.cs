using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Alarm;
using Siemens.Engineering.SW.Alarm.TextLists;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Supervision;

namespace TiaMcpServer.OpennessReferenceProbe
{
    // Compile-only signature checks for every Siemens member the plc_read/plc_write alarm, alarm class, text list and
    // ProDiag supervision operations bind at compile time. Building this probe against the installed V21 PublicAPI is
    // the mechanical check of the stubs. Members whose return or property types the sources do not document
    // (PlcAlarmTextProvider.ExportInstanceTextsToXlsx/ImportInstanceTextsFromXlsx, PlcAlarmTextlist.ID/ListRange/Comment,
    // the system text lists' Name, the result objects' State/LogFilePath/ErrorCount/WarningCount/Messages and
    // PlcAlarmUserTextlistComposition.CreateFrom, CodeBlock.ExportProDIAGInfo, PlcBlock.IsConsistent read as evidence)
    // are bound by exact runtime lookup in the worker; they are deliberately not referenced here.
    internal static class AlarmsReferenceSurface
    {
        private static void TextLists(PlcSoftware software, PlcAlarmTextlistGroup group, PlcAlarmUserTextlist user, PlcAlarmSystemTextlist system)
        {
            PlcAlarmTextlistGroup textLists = software.PlcAlarmTextlistGroup;
            PlcAlarmUserTextlistComposition users = group.PlcAlarmUserTextlists;
            PlcAlarmSystemTextlistComposition systems = group.PlcAlarmSystemTextlists;
            IEnumerable<PlcAlarmUserTextlist> userItems = users;
            IEnumerable<PlcAlarmSystemTextlist> systemItems = systems;
            string name = user.Name;
            Action delete = user.Delete;
            PlcAlarmTextlist userAsBase = user;
            PlcAlarmTextlist systemAsBase = system;
            IEngineeringObject engineering = user;
        }

        private static void Providers(PlcAlarmTextListProvider textLists, AlarmClassDataProvider alarmClasses, SupervisionProvider supervisions,
            PlcSoftware software, Project project, FB fb, PlcAlarmTextXlsxExportOption option)
        {
            Func<FileInfo, TextListXlsxResult> export = textLists.ExportToXlsx;
            Func<FileInfo, IEnumerable<string>, IEnumerable<Language>, TextListXlsxResult> exportFiltered = textLists.ExportToXlsx;
            Func<FileInfo, ImportOptions, TextListXlsxResult> import = textLists.ImportFromXlsx;
            Func<FileInfo, AlarmClassExportImportResult> exportClasses = alarmClasses.Export;
            Func<FileInfo, AlarmClassExportImportResult> importClasses = alarmClasses.Import;
            Func<FileInfo, SupervisionXlsxResult> exportSupervisions = supervisions.ExportSupervisionsToXlsx;
            Func<FileInfo, ImportOptions, SupervisionXlsxResult> importSupervisions = supervisions.ImportSupervisionsFromXlsx;
            Func<FileInfo, ImportOptions, SupervisionXlsxResult> importSettings = supervisions.ImportSupervisionSettingsFromXlsx;
            Func<PlcAlarmTextListProvider> textListService = software.GetService<PlcAlarmTextListProvider>;
            Func<PlcAlarmTextProvider> alarmTextService = software.GetService<PlcAlarmTextProvider>;
            Func<AlarmClassDataProvider> alarmClassService = project.GetService<AlarmClassDataProvider>;
            IEngineeringServiceProvider block = fb;
            Func<SupervisionProvider> supervisionService = block.GetService<SupervisionProvider>;
            IEngineeringService textListProviderAsService = textLists;
        }

        private static void ProDiag(FB fb, PlcBlock block)
        {
            SupervisionComposition supervisions = fb.Supervisions;
            int count = supervisions.Count;
            IEnumerable<Supervision> items = supervisions;
            IEngineeringInstance instance = supervisions;
            ProgrammingLanguage language = block.ProgrammingLanguage;
        }
    }
}
