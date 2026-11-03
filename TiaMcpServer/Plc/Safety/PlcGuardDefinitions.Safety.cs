using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Plc;

/// <summary>
/// The fail-safe guards. <see cref="SafetyAcceptance"/> is the only acknowledge guard of plc_write: every item that
/// changes the safety offline program's access or the safety program fires it, so read-write asks the human once per
/// such call (form elicitation) and full satisfies it by policy, and the audit records which. The block guard stops an
/// F-program change only when a safety offline program password is set and the session is not logged on (TIA Portal's
/// own rule; the worker checks it again right before the change). The info guard names a change this server cannot
/// judge and leaves to TIA Portal (no SafetyAdministration, unreadable access state).
/// </summary>
public static partial class PlcGuardDefinitions
{
    public const string SafetyAcceptance = "plc_safety_acceptance_required";
    public const string SafetyLoginRequired = "plc_safety_login_required";
    public const string SafetyTiaDecides = "plc_safety_tia_decides";
    public const string ImportMayUnlinkMainSafetyBlock = "plc_import_may_unlink_main_safety_block";

    private static IEnumerable<WriteGuardDefinition> SafetyDefinitions() => new[]
    {
        new WriteGuardDefinition(SafetyAcceptance, WriteGuardSeverities.Acknowledge,
            "The item changes the safety (F-) program of an F-CPU or the access protection of its safety offline program. " + PlcSafetyPolicy.AcceptanceNotice),
        new WriteGuardDefinition(SafetyLoginRequired, WriteGuardSeverities.Block,
            "The item changes the safety program of a PLC whose safety offline program has a password, the session is not logged on to it, and no earlier safety_login of the call logs on to it. Without a password TIA Portal (and this server) needs no login."),
        new WriteGuardDefinition(SafetyTiaDecides, WriteGuardSeverities.Info,
            "The item touches a fail-safe object whose safety access state this server cannot judge (no SafetyAdministration, or the state is unreadable); it runs and TIA Portal decides, its rejection being the item's failure."),
        new WriteGuardDefinition(ImportMayUnlinkMainSafetyBlock, WriteGuardSeverities.Info,
            "An SD-document write (update_block_logic format sd or a bundle without .xml, insert_network, replace_network, delete_network, "
            + "create_block_from_document with overwrite) targets the main safety block of an F-runtime group. A direct SIMATIC SD import "
            + "(ImportFromDocuments Override) of a main safety block makes TIA Portal drop the runtime group's MainSafetyBlockName (live, V21, "
            + "2026-11-02, reproduced twice; the PLC compile then fails \"does not have a valid main safety block\" and Openness offers no member to "
            + "re-assign it), so the document is routed through a temporary F-block and imported as SimaticML, which keeps the reference: "
            + "(1) the document is imported as a temporary F-block (zzMcpTmp_<block>_<8 hex>) into the main block's group, (2) that block is compiled "
            + "(TIA Portal exports no SimaticML of an uncompiled block), (3) its SimaticML is exported, (4) Name, Number and AutoNumber are set to the "
            + "main block's, (5) the SimaticML is imported onto the main safety block with update_block_logic format xml and its current hash, (6) the "
            + "temporary block is deleted (a block that cannot be deleted is reported by name). A compile error of the temporary block stops before the "
            + "main block is touched. The item result names the route (sdViaXmlForMainSafetyBlock) and the temporary block; the verification re-reads "
            + "the F-runtime groups afterwards (check safetyRuntimeGroup). For the instance DB of a main safety block the document is imported directly, "
            + "and the worker re-reads the group and tries to re-assign a lost reference."),
    };
}
