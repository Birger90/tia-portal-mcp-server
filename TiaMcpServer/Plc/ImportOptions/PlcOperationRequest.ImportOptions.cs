using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>Audit N7/N8: caller-selected import options of the SimaticML and SIMATIC SD block/type imports (not live-tested).</summary>
public sealed partial class PlcOperationRequest
{
    [Description("update_block_logic and update_type_content with format xml only (optional): SWImportOptions members IgnoreStructuralChanges, IgnoreMissingReferencedObjects and/or IgnoreUnitAttributes; the SimaticML document is then imported with Import(FileInfo, ImportOptions.Override, SWImportOptions) so TIA Portal does not abort on a structural change of a referenced object (instance data can be lost), a missing referenced object or unit attributes. Several members are combined only when the installed enum is a flags enum (otherwise unsupported_capability, nothing imported). Omitted: SWImportOptions is not passed (unchanged behaviour). Not live-tested.")]
    public IReadOnlyList<string>? SwImportOptions { get; set; }

    [Description("update_block_logic with format sd (or an SD bundle without .xml) and create_block_from_document (optional): SkipInactiveCultures or ActivateInactiveCultures, combined with ImportDocumentOptions.Override for PlcBlockComposition.ImportFromDocuments: skip the texts of cultures that are not active in the project, or activate valid supported cultures and import their texts. Combined only when the installed enum is a flags enum (otherwise unsupported_capability, nothing imported). Omitted: Override alone. Not live-tested.")]
    public string? DocumentImportOption { get; set; }
}
