using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The plc_write guards (spec §4.2). Block and info guards ask nothing; the acknowledge guards are the fail-safe
/// <see cref="SafetyAcceptance"/> (PlcGuardDefinitions.Safety.cs), which only fail-safe items fire, and
/// <see cref="MasterSecretOnline"/> (issue #16), which only the online master secret items fire.
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string StateUnverifiable = "plc_state_unverifiable";
    public const string DeletesBlock = "plc_deletes_block";
    public const string DeletesGroupContents = "plc_deletes_group_contents";
    public const string DeletesTableContents = "plc_deletes_table_contents";
    public const string AddressOverlap = "plc_address_overlap";
    public const string ObSingletonExists = "plc_ob_singleton_exists";
    public const string ChangesInterface = "plc_changes_interface";
    public const string BlockRoundTrip = "plc_block_round_trip";
    public const string MovesTagTable = "plc_moves_tag_table";
    public const string MoveTagRecreates = "plc_move_tag_recreates";
    public const string DeletesWatchTable = "plc_deletes_watch_table";
    public const string DeletesTextList = "plc_deletes_text_list";
    public const string WorkbookRoundTrip = "plc_workbook_round_trip";
    public const string ImportsAlarmData = "plc_imports_alarm_data";
    public const string ExportOverwritesFile = "plc_export_overwrites_file";
    public const string FileOnNetworkOrLink = "plc_file_on_network_or_link";

    public static IReadOnlyList<WriteGuardDefinition> Definitions { get; } = WithSafety(WriteGuardCatalog.NonInteractive(new[]
    {
        new WriteGuardDefinition(StateUnverifiable, WriteGuardSeverities.Info, "The PLC inventory is incomplete, the update or delete target or what a delete removes is unreadable, the user-constant value an update replaces is unreadable, or the content cannot be exported; unreadable evidence is never treated as empty, but the item runs and TIA Portal decides (owner decision: never stricter than TIA Portal)."),
        new WriteGuardDefinition(ObSingletonExists, WriteGuardSeverities.Block, "create_block targets a singleton OB event class whose number an existing or in-call OB already holds."),
        new WriteGuardDefinition(DeletesBlock, WriteGuardSeverities.Info, "Deleting the block removes it and its content."),
        new WriteGuardDefinition(DeletesGroupContents, WriteGuardSeverities.Info, "Deleting the block group, PLC data type group, tag table group or software unit removes every object and group inside it."),
        new WriteGuardDefinition(DeletesTableContents, WriteGuardSeverities.Info, "Deleting the tag table removes its tags and user constants."),
        new WriteGuardDefinition(AddressOverlap, WriteGuardSeverities.Info, "The requested logical address is already used by another tag; TIA Portal allows the overlap."),
        new WriteGuardDefinition(ChangesInterface, WriteGuardSeverities.Info, "An interface member edit re-imports the block or PLC data type; instance DBs, multi-instances and callers stay inconsistent until compiled, and TIA Portal may refuse a structural change of an FB that has instances. A rename or delete is not a refactoring: references to the member in the block's code and in other blocks are not updated; only rename_members updates the object's own accesses (as the TIA Portal editor does), never those in other blocks."),
        new WriteGuardDefinition(BlockRoundTrip, WriteGuardSeverities.Info, "copy_block and move_block import the block's SimaticML export inside one ExclusiveAccess transaction that any failure rolls back: the result has a new object identity and no library-type link. move_block deletes the source before the import; should the rollback not restore it, it is re-imported, and its export is kept in a recovery folder if that fails too."),
        new WriteGuardDefinition(MovesTagTable, WriteGuardSeverities.Info, "move_tag_table exports the table, then deletes it and imports the export into the target folder inside one ExclusiveAccess transaction that any failure rolls back; should the rollback not restore it, it is re-imported into the source folder, and its export is kept in a recovery folder if that fails too."),
        new WriteGuardDefinition(MoveTagRecreates, WriteGuardSeverities.Info, "move_tag re-creates the tag in the target table (no native move) inside one ExclusiveAccess transaction that any failure rolls back: the data type, address, external-access flags and multilingual comment are copied, other attributes are not, and the tag briefly carries a temporary name."),
        new WriteGuardDefinition(DeletesWatchTable, WriteGuardSeverities.Info, "Deleting the watch table removes its entries; deleting a watch-and-force table group removes every watch table, force table and group inside it."),
        new WriteGuardDefinition(DeletesTextList, WriteGuardSeverities.Info, "Deleting the user text list removes it and its entries."),
        new WriteGuardDefinition(WorkbookRoundTrip, WriteGuardSeverities.Info, "A cell update exports the workbook, patches the listed cells, imports the whole workbook again with TIA Portal's XLSX import and re-exports it, all under ExclusiveAccess; it fails when the re-export misses an edit or changed any other cell. The import's semantics are TIA Portal's."),
        new WriteGuardDefinition(ImportsAlarmData, WriteGuardSeverities.Info, "The import reads a caller file into the project; which existing text lists, alarm texts, alarm classes, system diagnostics settings or supervisions it replaces, adds or keeps is decided by TIA Portal. A re-export of the same kind afterwards (where one is documented) shows whether every imported row (alarm classes and system diagnostics settings: the whole file) came back; otherwise the import is reported not verified."),
        new WriteGuardDefinition(ExportOverwritesFile, WriteGuardSeverities.Info, "The export replaces an existing file because overwrite:true was requested."),
        new WriteGuardDefinition(FileOnNetworkOrLink, WriteGuardSeverities.Info, "The caller file or directory lies on a mapped network drive or below a link (reparse point); it is read or written there, as the worker's file system resolves it (the host's path check is lexical)."),
    }.Concat(PlcSourceFolderGuards.Definitions).Concat(TechnologyObjectDefinitions()).Concat(SecurityDefinitions()).Concat(EditingDefinitions()).Concat(V21PitfallDefinitions()).Concat(SimaticSdDefinitions()).Concat(BlockProtectionDefinitions()).Concat(ProjectLanguageDefinitions()).Concat(SettingsDefinitions()).Concat(OpcUaDefinitions()).Concat(DeviceDataDefinitions()), "PLC"));

    /// <summary>The non-interactive set plus the fail-safe guards, validated as one catalog.</summary>
    private static IReadOnlyList<WriteGuardDefinition> WithSafety(IReadOnlyList<WriteGuardDefinition> nonInteractive)
    {
        var all = nonInteractive.Concat(SafetyDefinitions()).Concat(DeviceDataAcknowledgeDefinitions()).Concat(SettingsAcknowledgeDefinitions()).ToArray();
        _ = new WriteGuardCatalog(all);
        return Array.AsReadOnly(all);
    }
}
