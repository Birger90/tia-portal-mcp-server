using System.Text.RegularExpressions;

namespace TiaMcpServer.Contracts.Plc.Safety;

/// <summary>
/// The decision of whether an F-program change may run now. The rule follows TIA Portal itself (owner decision
/// 2026-11-01, "nicht strenger als TIA Portal"): a login is required only when a safety offline program password is
/// set; without one TIA Portal allows F-program changes, and so does this server. Everything this server cannot judge
/// (no SafetyAdministration, unreadable state) is left to TIA Portal, whose own rejection is reported.
/// </summary>
public enum PlcSafetyEditPermission
{
    /// <summary>A safety offline program password is set and the session is logged on to it.</summary>
    Permitted,

    /// <summary>Not logged on now, but an earlier safety_login item of the same call logs on to this PLC; the worker re-checks before the mutation.</summary>
    PendingEarlierLogin,

    /// <summary>A password is set and the session is not logged on: the only refusal.</summary>
    LoginRequired,

    /// <summary>No safety offline program password is set: TIA Portal allows the change without login, so it runs.</summary>
    NoPasswordSet,

    /// <summary>The PLC offers no SafetyAdministration (not an F-CPU, or the Safety option is not installed): TIA Portal decides.</summary>
    NotAnFCpu,

    /// <summary>The access state could not be read (a member is missing or threw): TIA Portal decides.</summary>
    Unverifiable,
}

/// <summary>
/// The Siemens-free rules of the fail-safe operations, shared by host, worker and FakeWorker so the wording and the
/// decisions cannot drift.
/// </summary>
public static class PlcSafetyPolicy
{
    /// <summary>The prefix every fail-safe ProgrammingLanguage carries (F_LAD, F_FBD, F_DB, ...; V21 import/export reference §6).</summary>
    public const string FailSafeLanguagePrefix = "F_";

    /// <summary>
    /// Carried by every fail-safe write (guard message, result notice, docs): the MCP server is a programming aid and is not
    /// part of the safety lifecycle.
    /// </summary>
    public const string AcceptanceNotice =
        "A safety program (F-program) and its access protection must be validated and accepted by a qualified person "
        + "(safety acceptance as described in the SIMATIC Safety manual, with the safety printout and the collective F-signature). "
        + "This MCP server never replaces the safety acceptance; nothing it reports (compile results, signatures, read-backs) is acceptance evidence.";

    public const int DefaultBlockLimit = 200;
    public const int MaxBlockLimit = 2000;
    public const int MaxRuntimeGroupNameLength = 128;

    private static readonly Regex FailSafeLanguageElement = new(@"<ProgrammingLanguage>\s*F_[A-Za-z0-9_]*\s*</ProgrammingLanguage>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsFailSafeLanguage(string? programmingLanguage)
        => programmingLanguage is not null && programmingLanguage.StartsWith(FailSafeLanguagePrefix, StringComparison.Ordinal);

    /// <summary>True when a SimaticML document declares a fail-safe ProgrammingLanguage anywhere (a block bundle may carry several documents).</summary>
    public static bool DeclaresFailSafeLanguage(string? simaticMl)
        => !string.IsNullOrEmpty(simaticMl) && FailSafeLanguageElement.IsMatch(simaticMl);

    /// <summary>
    /// True when a SIMATIC SD .s7dcl document (or the .s7dcl of a bundle) declares <c>S7_Safety := "TRUE"</c> in its header
    /// pragma, as the V21 export of a fail-safe FB does (HonzaT/BetterTIA sample FB_Safety).
    /// </summary>
    public static bool DeclaresSimaticSdSafety(string? s7dclOrBundle) => SimaticSd.SimaticSdText.DeclaresSafety(s7dclOrBundle);

    /// <summary>A document that makes or edits an F-block: a fail-safe SimaticML language or a SIMATIC SD S7_Safety header.</summary>
    public static bool DeclaresFailSafe(string? document) => DeclaresFailSafeLanguage(document) || DeclaresSimaticSdSafety(document);

    /// <summary>The decision for an F-program change on one PLC.</summary>
    public static PlcSafetyEditPermission Decide(bool available, bool? passwordSet, bool? loggedOn, bool earlierLoginInCall)
    {
        if (!available) return PlcSafetyEditPermission.NotAnFCpu;
        if (passwordSet == true && loggedOn == true) return PlcSafetyEditPermission.Permitted;
        if (passwordSet == true && loggedOn == false) return earlierLoginInCall ? PlcSafetyEditPermission.PendingEarlierLogin : PlcSafetyEditPermission.LoginRequired;
        if (passwordSet == false) return PlcSafetyEditPermission.NoPasswordSet;
        return PlcSafetyEditPermission.Unverifiable;
    }

    /// <summary>
    /// Whether a change may run (the worker re-checks right before it). Only <see cref="PlcSafetyEditPermission.LoginRequired"/>
    /// refuses; TIA Portal keeps its own checks for everything else, and its rejection is the item's failure.
    /// </summary>
    public static bool MayPlan(PlcSafetyEditPermission permission)
        => permission != PlcSafetyEditPermission.LoginRequired;

    /// <summary>The refusal text for a decision that does not permit the change; null when it may run.</summary>
    public static string? Refusal(PlcSafetyEditPermission permission, string plcName, string what)
        => permission == PlcSafetyEditPermission.LoginRequired
            ? $"{what} changes the safety program of PLC '{plcName}', which is protected by a safety offline program password, and this session is not logged on to it. "
                + "Run plc_write safety_login with passwordVariable (where the password is: an environment variable NAME or a TiaMcp/ credential) first, in this call or an earlier one."
            : null;

    /// <summary>
    /// The note for a change this server lets TIA Portal decide (no SafetyAdministration, unreadable state); null when the
    /// state is known. The change runs; TIA Portal's own rejection, if any, is the item's failure.
    /// </summary>
    public static string? Undetermined(PlcSafetyEditPermission permission, string plcName, string what)
        => permission switch
        {
            PlcSafetyEditPermission.NotAnFCpu =>
                $"{what} touches a fail-safe object, but PLC '{plcName}' offers no SafetyAdministration (not an F-CPU, or STEP 7 Safety is not installed); "
                + "the change is passed to TIA Portal, which decides whether it is possible.",
            PlcSafetyEditPermission.Unverifiable =>
                $"{what} changes the safety program of PLC '{plcName}', but its safety access state (password set, logged on) could not be read; "
                + "the change is passed to TIA Portal, which rejects it if a login is needed (then run safety_login; inspect plc_read get_safety_administration).",
            _ => null,
        };

    /// <summary>The documented RuntimeGroupComposition.Create overload chosen by the given main safety block and instance DB.</summary>
    public static string RuntimeGroupOverload(bool hasMainBlock, bool hasInstanceDb)
        => !hasMainBlock ? "Create(String)" : hasInstanceDb ? "Create(String, PlcBlock, PlcBlock)" : "Create(String, PlcBlock)";

    /// <summary>The integral text and its hexadecimal form of a signature value; (text, null) for anything else.</summary>
    public static (string? Text, string? Hex) SignatureText(object? value)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        ulong? bits = value switch
        {
            byte v => v,
            ushort v => v,
            uint v => v,
            ulong v => v,
            sbyte v => unchecked((byte)v),
            short v => unchecked((ushort)v),
            int v => unchecked((uint)v),
            long v => unchecked((ulong)v),
            _ => null,
        };
        return value is null ? (null, null)
            : (Convert.ToString(value, invariant), bits is { } b ? "0x" + b.ToString("X", invariant) : null);
    }

    /// <summary>A value as invariant text: booleans lower-case, enums by member name, others by their invariant ToString.</summary>
    public static string? Text(object? value)
        => value switch
        {
            null => null,
            bool flag => flag ? "true" : "false",
            Enum member => member.ToString(),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
}
