using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// Guards for TIA Portal V21 pitfalls reported by rickgaiser/TiaMcp and not verified on V21 here (roadmap row 22).
/// All three are informative: the reports are unverified, the access mode is the human's control, and a block guard
/// would make the SIMATIC SD route and every overwrite of a textual-interface block unwritable for an unverified reason
/// (blocks stay reserved for invalid, ambiguous or unverifiable input). Their texts say "is reported to" until a live
/// reproduction on V21 is recorded in <c>docs/SupportedOperations/V21_PITFALLS.md</c>.
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string S7dclImportMayAlter = "plc_s7dcl_import_may_alter";
    public const string TextualInterfaceOverwrite = "plc_textual_interface_overwrite_may_terminate";
    public const string SaveBeforeWrite = "plc_save_before_write_recommended";

    private static IEnumerable<WriteGuardDefinition> V21PitfallDefinitions() => new[]
    {
        new WriteGuardDefinition(S7dclImportMayAlter, WriteGuardSeverities.Info,
            "update_block_logic imports a bundle without a SimaticML (.xml) document through SIMATIC SD (.s7dcl). The V21 .s7dcl import is reported "
            + "(rickgaiser/TiaMcp, not verified on V21) to alter without an error an array access inside an index in a LAD/FBD network, a member typed "
            + "with a project FB or UDT named like a system type (TON, TOF, TP, TONR, CTU, CTD, CTUD, R_TRIG, F_TRIG) and an AT overlay. Write the block "
            + "through its SimaticML document instead; the verification compares these constructs with the re-export."),
        new WriteGuardDefinition(TextualInterfaceOverwrite, WriteGuardSeverities.Info,
            "The block has a textual interface (S7_EditorMode TextualInterface). TIA Portal V21 is reported (rickgaiser/TiaMcp, not verified on V21) to "
            + "terminate when it overwrites such a block after it was exported more than once since the project was opened or the block was last "
            + "written. This write path exports the block more than once before its import (planning read, worker hash check, unchanged-companion check)."),
        new WriteGuardDefinition(SaveBeforeWrite, WriteGuardSeverities.Info,
            "TIA Portal is reported to terminate during this write; a terminated TIA Portal loses every unsaved change. Save the project "
            + "(save_project) before the call."),
    };
}
