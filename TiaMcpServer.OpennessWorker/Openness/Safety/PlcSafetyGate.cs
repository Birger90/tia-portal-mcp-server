using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Plc.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.Contracts.Plc.Safety.PlcSafetyApiCatalog;

namespace TiaMcpServer.OpennessWorker.Openness.Safety;

/// <summary>
/// The worker's own check before a fail-safe block changes, independent of the host's planning. The rule is TIA
/// Portal's, not a stricter one (owner decision 2026-11-01): an F-block (its ProgrammingLanguage starts with F_, or the
/// imported SimaticML declares one) changes through every operation TIA Portal offers for it (update_block_logic in xml
/// or source, the interface edits, update_db_member, delete, rename, renumber, copy, move); only when the PLC's safety
/// offline program has a password and the session is not logged on is the change refused, before any Siemens write.
/// A PLC without SafetyAdministration or an unreadable access state is left to TIA Portal, whose rejection (an
/// EngineeringTargetInvocationException in a mutation method) is reported as engineering_rejected.
/// </summary>
internal static partial class PlcSafetyGate
{
    /// <summary>
    /// The SafetyAdministration of a CPU device item. Implemented next to <see cref="PlcSafetyService"/>; the unit tests
    /// link this file without it, so there every PLC reads as having none (TIA Portal decides there).
    /// </summary>
    static partial void FindAdministration(Siemens.Engineering.HW.DeviceItem? host, ref object? administration);

    /// <summary>update_block_logic (xml or source; the interface edits import through it).</summary>
    public static void RequireImportPermitted(ProjectBase project, string? plcName, PlcBlock? block, string? content, bool sourceFormat)
        => RequireChangePermitted(project, plcName, block, content, $"update_block_logic{(sourceFormat ? " (source)" : string.Empty)} of '{block?.Name}'");

    /// <summary>
    /// Every other operation that changes an F-block (delete_block, rename_block, renumber_block, update_db_member,
    /// copy_block, move_block): the same login rule as an update.
    /// </summary>
    public static void RequireChangePermitted(ProjectBase project, string? plcName, PlcBlock? block, string operation)
        => RequireChangePermitted(project, plcName, block, null, $"{operation} of '{block?.Name}'");

    /// <summary>
    /// move_tag / move_tag_table of tags that report <c>IsSafety=true</c> (F-I/O tags): the same login rule as an F-block
    /// change, no refusal otherwise (owner decision: TIA Portal moves such tags, so the server does too).
    /// </summary>
    public static void RequireSafetyTagChangePermitted(ProjectBase project, string? plcName, string described)
        => RequirePermitted(project, plcName, described, "reports IsSafety=true (a fail-safe tag)");

    private static void RequireChangePermitted(ProjectBase project, string? plcName, PlcBlock? block, string? content, string described)
    {
        var language = Language(block);
        if (!PlcSafetyPolicy.IsFailSafeLanguage(language) && !PlcSafetyPolicy.DeclaresFailSafe(content)) return;
        RequirePermitted(project, plcName, described, $"'{block?.Name}' is a fail-safe block");
    }

    private static void RequirePermitted(ProjectBase project, string? plcName, string described, string subject)
    {
        var plc = PlcSoftwareLocator.FindUnique(project, plcName);
        object? administration = null;
        FindAdministration(plc.HostDeviceItem, ref administration);
        bool? passwordSet = null, loggedOn = null;
        if (administration is not null)
        {
            passwordSet = TryBool(administration, IsPasswordSet);
            loggedOn = TryBool(administration, IsLoggedOn);
        }

        var decision = PlcSafetyPolicy.Decide(administration is not null, passwordSet, loggedOn, earlierLoginInCall: false);
        if (PlcSafetyPolicy.Refusal(decision, plc.Software.Name, described) is { } refusal)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"{subject}. {refusal} Nothing was changed.");
    }

    private static string? Language(PlcBlock? block)
    {
        if (block is null) return null;
        try
        {
            return block.ProgrammingLanguage.ToString();
        }
        catch (EngineeringException)
        {
            // Unknown, not standard: the import check still reads the document's own ProgrammingLanguage, and TIA
            // Portal keeps its own protection of the safety program.
            return null;
        }
    }

    private static bool? TryBool(object administration, string name)
    {
        try
        {
            return PlcSafetyReflection.GetBool(administration, name);
        }
        catch (Exception exception) when (exception is EngineeringException or PlcSafetyApiMissingException)
        {
            // Unreadable access state is Unverifiable: TIA Portal decides.
            return null;
        }
    }
}
