using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The SIMATIC SD guards: informative only (owner decision: never stricter than TIA Portal; TIA Portal's import decides).
/// The network edits also fire <see cref="ReimportsBlockForText"/> (BetterTIA B2: an import can leave the instance DB and
/// callers inconsistent) and the V21 pitfall guard <see cref="S7dclImportMayAlter"/>; F-blocks fire the safety guards.
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string SdReimportsExportForm = "plc_sd_reimports_export_form";
    public const string SdDocumentReplacesBlock = "plc_sd_document_replaces_block";

    private static IEnumerable<WriteGuardDefinition> SimaticSdDefinitions() => new[]
    {
        new WriteGuardDefinition(SdReimportsExportForm, WriteGuardSeverities.Info,
            "A SIMATIC SD import re-imports networks in the form TIA Portal exported them. Some V21 export forms (Gt{ ... }(, Lt{, Eq{, Ne{, Ge{, Le{, "
            + "PBox(, NBox(, Move{ ... }() are reported (1748477582/tia-portal-openness-mcp) to fail ImportFromDocuments with a generic "
            + "\"Failed importing\"; reported, not reproduced live on V21 2026-11-02 for GT/EQ_Contact, Move, P_Trig/N_Trig, TON/TOF, Add (their re-export "
            + "equalled the import form apart from whitespace); other instructions were not tested. The reported import forms are GT_Contact( in1 := ..., in2 := ... ) with { S7_Templates := \"SrcType := Int\" }, "
            + "P_Trig( #mem ) and Move( in := ..., out1 => ... ). Nothing is rewritten. If TIA Portal refuses the import, rewrite those networks in the "
            + "import form (replace_network) or change texts through the SimaticML route (set_network_text)."),
        new WriteGuardDefinition(SdDocumentReplacesBlock, WriteGuardSeverities.Info,
            "create_block_from_document with overwrite:true imports with ImportDocumentOptions.Override although a block of the declared name exists; "
            + "TIA Portal decides what it replaces (and what it does when that block is in another group)."),
    };
}
