using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>The guards of the operations adopted from BetterTIA: informative only, nothing asks the user.</summary>
public static partial class PlcGuardDefinitions
{
    public const string StoresDbSnapshot = "plc_stores_db_snapshot";
    public const string ReimportsBlockForText = "plc_text_edit_reimports_block";
    public const string BatchRenameRuns = "plc_batch_rename";
    public const string ImportReplacesTagTable = "plc_import_replaces_tag_table";

    private static IEnumerable<WriteGuardDefinition> EditingDefinitions() => new[]
    {
        new WriteGuardDefinition(StoresDbSnapshot, WriteGuardSeverities.Info,
            "create_db_snapshot reads the actual values of the DB from the online PLC (ValueService.CreateSnapshot) and TIA Portal stores them as the DB's snapshot in the offline project, replacing an earlier snapshot; the project then shows as changed. Nothing is written to the PLC and no start value changes."),
        new WriteGuardDefinition(ReimportsBlockForText, WriteGuardSeverities.Info,
            "set_network_text and replace_network_text change texts that Openness offers no setter for, so the whole block is exported, edited and imported again (ImportOptions.Override through update_block_logic); insert_network, replace_network and delete_network do the same with the SIMATIC SD export (ImportFromDocuments with ImportDocumentOptions.Override through update_block_logic format sd). BetterTIA reports that TIA Portal can then mark the block's instance DB and its callers inconsistent even when only a comment changed (not verified); compile afterwards with compile_check."),
        new WriteGuardDefinition(BatchRenameRuns, WriteGuardSeverities.Info,
            "rename_tags / rename_objects rename several objects by name in one ExclusiveAccess transaction (a failed step rolls every rename back); users in blocks re-bind by name at the next compile, while HMI tags, watch tables and technology object parameters that hold a renamed name are not changed. Fail-safe tags and F-blocks follow TIA Portal's safety login rule."),
        new WriteGuardDefinition(ImportReplacesTagTable, WriteGuardSeverities.Info,
            "import_tag_table with overwrite:true imports with ImportOptions.Override: a tag table of the same name in the folder is replaced by the file's content, so tags and user constants that the file does not hold are gone. TIA Portal decides what the import creates or replaces."),
    };
}
