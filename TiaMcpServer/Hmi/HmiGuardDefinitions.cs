using TiaMcpServer.Safety.Pipeline;
namespace TiaMcpServer.Hmi;

public static class HmiGuardDefinitions
{
    public const string StateUnverifiable = "hmi_state_unverifiable";
    public const string DeletesTableContents = "hmi_deletes_table_contents";
    public const string DeletesReferencedTag = "hmi_deletes_referenced_tag";
    public const string DeletesScreen = "hmi_deletes_screen";
    public const string RenameUpdatesNoReferences = "hmi_rename_references_unverified";
    public const string TextListFileUnverified = "hmi_text_list_file_unverified";
    public const string ClassicDeletes = "hmi_classic_deletes";
    public const string ClassicImportOverrides = "hmi_classic_import_overrides";
    public const string DeletesScripting = "hmi_deletes_event_or_dynamization";
    public const string DeletesConfiguration = "hmi_deletes_audit_class_or_plant_view";
    public const string AuditPropertyUnverified = "hmi_audit_property_unverified";
    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Info, "Delete-consequence evidence, an update/delete target, or the HMI's device or tag-table listing could not be read completely; the item runs and TIA Portal decides."),
        new WriteGuardDefinition(DeletesTableContents, WriteGuardSeverities.Info, "Deleting the tag table also deletes its tags."),
        new WriteGuardDefinition(DeletesReferencedTag, WriteGuardSeverities.Info, "Deleting a referenced tag leaves its references dangling."),
        new WriteGuardDefinition(DeletesScreen, WriteGuardSeverities.Info, "Deleting the screen removes it and its screen items; screen windows, navigation and scripts that name it are not changed."),
        new WriteGuardDefinition(RenameUpdatesNoReferences, WriteGuardSeverities.Info, "A batch rename of screens or HMI tags renames the objects only; references by name in screen windows, navigation, scripts, dynamizations or alarm triggers may keep the old name (not verified)."),
        new WriteGuardDefinition(TextListFileUnverified, WriteGuardSeverities.Info, "export_text_lists and import_text_lists use HmiTextList.Export(FileInfo, ExportOptions) and HmiTextListComposition.Import(FileInfo, ImportOptions), members no Siemens document describes for WinCC Unified text lists; they are bound at runtime and fail api_member_missing with nothing written or changed when absent. Graphic lists and system text lists are never touched."),
        new WriteGuardDefinition(ClassicDeletes, WriteGuardSeverities.Info, "Deleting a classic WinCC object or user folder removes it (a folder with its contents); screens, scripts, alarms and other objects that name it are not changed."),
        new WriteGuardDefinition(ClassicImportOverrides, WriteGuardSeverities.Info, "A classic WinCC import with overwrite uses ImportOptions.Override: objects of the same name are replaced; TIA Portal decides what the file creates or replaces."),
        new WriteGuardDefinition(DeletesScripting, WriteGuardSeverities.Info, "Deleting an event handler or a dynamization removes its script or binding; the screen item keeps its static property values."),
        new WriteGuardDefinition(DeletesConfiguration, WriteGuardSeverities.Info, "Deleting an alarm audit class, a plant view or a plant view node removes it (a view or node with its nodes); alarms or screens that name it are not changed."),
        new WriteGuardDefinition(AuditPropertyUnverified, WriteGuardSeverities.Info, "Audit and GMP properties were seen to fail on Comfort panels and one tag audit property (ConfirmationType) crashed TIA Portal when read in the hmi_read spike; the write reads the current value first to learn its type. Not verified for audit classes and GMP runtime settings; TIA Portal decides."),
    }, "HMI");
}
