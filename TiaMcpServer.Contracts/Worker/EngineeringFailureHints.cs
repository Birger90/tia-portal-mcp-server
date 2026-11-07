namespace TiaMcpServer.Contracts.Worker;

/// <summary>
/// Audit T6/T9/T11 (K89, not live-tested): clearer texts for three documented Siemens exception types (V21 members.txt:
/// <c>Siemens.Engineering.MissingProductsException</c>, "can be thrown if TiaPortal has a missing mandatory Product,
/// Optional Product or Support Package", documented on <c>ProjectComposition.Open(FileInfo)</c>;
/// <c>Siemens.Engineering.EngineeringSecurityException</c>; <c>Siemens.Engineering.NonRecoverableException</c>). Types are
/// matched by exact full name along the runtime type chain, so the Siemens-free host and tests can use the same rules.
/// The category does not change; only the message says what happened and what helps.
/// </summary>
public static class EngineeringFailureHints
{
    public const string MissingProductsException = "Siemens.Engineering.MissingProductsException";

    public const string EngineeringSecurityException = "Siemens.Engineering.EngineeringSecurityException";

    /// <summary>True when the exception's runtime type or a base type has exactly <paramref name="fullName"/>.</summary>
    public static bool Is(Exception? exception, string fullName)
    {
        for (var type = exception?.GetType(); type is not null; type = type.BaseType)
            if (string.Equals(type.FullName, fullName, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary><c>Projects.Open</c> refused because products, optional products or support packages are missing.</summary>
    public static string MissingProducts(string projectPath, string? siemensMessage)
        => $"TIA Portal cannot open '{projectPath}': the project needs products, optional packages or support packages that are not installed "
            + $"(MissingProductsException: {siemensMessage}). Nothing was opened. Install the missing products (open the project once in the TIA Portal UI "
            + "to see the list) and retry; the MCP cannot install them.";

    /// <summary>TIA Portal refused for a security reason (for example a UMAC-protected project without the needed right).</summary>
    public static string Security(string? siemensMessage)
        => $"TIA Portal refused the operation for a security reason (EngineeringSecurityException: {siemensMessage}). For a project protected by user "
            + "management (UMAC) the Windows/UMAC user needs the function right that allows changing the project through Openness "
            + "(named \"Modify project via Openness API\" in a third-party guide; not verified on V21). Ask the project administrator; the MCP does not bypass it.";

    /// <summary>
    /// Appended to every portal_lost message caused by a <c>NonRecoverableException</c> or a lost worker: never repeat the same
    /// call unattended (it may stop TIA Portal again). The diagnostics location is from a third-party guide and not verified.
    /// </summary>
    public const string NonRecoverableRetry =
        "Do not repeat the same call unattended: a call that stopped TIA Portal may stop it again. Check that TIA Portal runs and, if it wrote a crash "
        + "report (a third-party guide names %ProgramData%\\Siemens\\Automation\\Portal V21\\Diagnostics; not verified), look at it first.";
}
