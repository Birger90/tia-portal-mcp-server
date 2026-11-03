using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
namespace TiaMcpServer.OpennessWorker.Openness.Hmi;
public static class HmiWriteDispatch
{
    /// <summary>The single-object writes, which need no Portal handle (the batch renames do; see the overload).</summary>
    public static HmiWriteOutcomeInfo Write(ProjectBase project, string method, HmiWriteRequestInfo write) => Write(null, project, method, write);

    public static HmiWriteOutcomeInfo Write(TiaPortal? portal, ProjectBase project, string method, HmiWriteRequestInfo write)
    {
        // Classic WinCC (issue #28) resolves its own target; the Unified locator refuses Classic HMIs.
        if (HmiClassicMethods.IsClassic(method)) return HmiClassicObjects.Write(project, method, write);
        var software = HmiSoftwareLocator.FindUnique(project, write.HmiName).Software;
        try
        {
            return method switch
            {
                "hmi_create_tag_table" => HmiTagTableWriter.Create(software, write),
                "hmi_update_tag_table" => HmiTagTableWriter.Update(software, write),
                "hmi_delete_tag_table" => HmiTagTableWriter.Delete(software, write),
                "hmi_create_tag" => HmiTagWriter.Create(software, write),
                "hmi_update_tag" => HmiTagWriter.Update(software, write),
                "hmi_delete_tag" => HmiTagWriter.Delete(software, write),
                "hmi_create_alarm" => HmiAlarmWriter.Create(software, write),
                "hmi_update_alarm" => HmiAlarmWriter.Update(software, write),
                "hmi_delete_alarm" => HmiAlarmWriter.Delete(software, write),
                HmiScreenMethods.CreateScreen => HmiScreenWriter.CreateScreen(software, write),
                HmiScreenMethods.DeleteScreen => HmiScreenWriter.DeleteScreen(software, write),
                HmiScreenMethods.CreateScreenItem => HmiScreenWriter.CreateScreenItem(software, write),
                HmiScreenMethods.DeleteScreenItem => HmiScreenWriter.DeleteScreenItem(software, write),
                HmiScreenMethods.SetScreenItemProperty => HmiScreenWriter.SetScreenItemProperty(software, write),
                HmiScreenMethods.SetDynamizationTag => HmiScreenWriter.SetDynamizationTag(software, write),
                HmiScreenMethods.ReplaceInScripts => HmiScreenWriter.ReplaceInScripts(software, write),
                HmiTextListMethods.ExportTextLists => HmiTextListFiles.ExportLists(software, write),
                HmiTextListMethods.ImportTextLists => HmiTextListFiles.ImportLists(software, write),
                HmiScreenEventMethods.SetEventScript => HmiScreenEvents.SetEventScript(software, write),
                HmiScreenEventMethods.DeleteEventScript => HmiScreenEvents.DeleteEventScript(software, write),
                HmiScreenEventMethods.SetDynamizationScript => HmiScreenEvents.SetDynamizationScript(software, write),
                HmiScreenEventMethods.SetDynamizationFlashing => HmiScreenEvents.SetDynamizationFlashing(software, write),
                HmiScreenEventMethods.DeleteDynamization => HmiScreenEvents.DeleteDynamization(software, write),
                var batch when HmiScreenMethods.IsBatch(batch) => HmiScreenWriter.RenameBatch(
                    portal ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"{batch} needs the TIA Portal handle for its transaction."),
                    project, software, batch, write),
                _ => throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unsupported HMI worker method '{method}'."),
            };
        }
        finally { HmiSnapshotStore.Shared.DropFor(project, software); }
    }
}
