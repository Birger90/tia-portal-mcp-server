namespace TiaMcpServer.Contracts.Plc.Settings;

/// <summary>
/// Issue #21, the safety printout. Evidence: Siemens.Engineering.Safety.xml (V21):
/// <c>Safety.SafetyPrintout.Print(SafetyPrintoutFilePrinter, FileInfo, String, SafetyPrintoutOption)</c> ("Creates a Safety Printout for
/// the current device item and prints it to a file. If the file already exists, it is overridden. - Returns true on success.";
/// <c>documentLayout</c> e.g. "DocuInfo_ISO_A4_Portrait" or "DocuInfo_Simple_A4_Portrait_Small_Footer"; the service "for Plus PLCs"),
/// <c>SafetyPrintoutFilePrinter</c> (<c>MicrosoftPrintToPdf</c>, <c>MicrosoftXpsDocumentWriter</c>), <c>SafetyPrintoutOption</c> (<c>All</c>,
/// <c>Compact</c>). The printout is a document for the safety acceptance by a qualified person; generating it accepts nothing.
/// </summary>
public static class PlcSafetyPrintoutMethods
{
    public const string GenerateSafetyPrintout = "generate_safety_printout";
}

public static class PlcSafetyPrintoutNames
{
    public const string Printout = "Siemens.Engineering.Safety.SafetyPrintout";

    public static readonly string[] Printers = { "MicrosoftPrintToPdf", "MicrosoftXpsDocumentWriter" };
    public static readonly string[] PrintOptions = { "All", "Compact" };
    public const string DefaultPrinter = "MicrosoftPrintToPdf";
    public const string DefaultPrintOption = "All";

    /// <summary>
    /// K79 (live 2026-11-03: "No Document description object \"?\" found in System Library or Project"): V21 documents no member
    /// that lists the documentation layouts (searched: DocumentInfo*, DocuInfo, Printout, Printing, document descriptions,
    /// master copy folders), so a failed printout says where the names come from. Appended to every failed generate_safety_printout.
    /// </summary>
    public const string LayoutHint = "documentLayout is the name of a TIA Portal document information object: in TIA Portal, "
        + "Documentation settings > Document information (\"Dokumentationseinstellungen > Dokumentinformationen\"; cover pages and frames, "
        + "\"Deckblätter\"/\"Rahmen\", are beside it) of the project or of a global library such as the system library. The names in the "
        + "Siemens reference (DocuInfo_ISO_A4_Portrait, DocuInfo_Simple_A4_Portrait_Small_Footer) are examples and exist only where "
        + "such a library provides them. Openness V21 offers no member that lists them; take the exact name from TIA Portal.";
}

public static partial class PlcSettingsCatalog
{
    private static void AddSafetyPrintout()
    {
        Add(new(PlcSafetyPrintoutMethods.GenerateSafetyPrintout, true, 21, false, PlcSafetyPrintoutNames.Printout));
    }
}
