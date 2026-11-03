// Stand-ins for the safety printout (issue #21) with the member names of Siemens.Engineering.Safety.xml (V21). Print writes a small
// placeholder file; layout "fake:false" makes it return false, "fake:unknown" throws like TIA Portal's refusal of an unknown
// document information name (K79, live 2026-11-03). Member shapes only; no evidence of Siemens behavior.
namespace Siemens.Engineering.Safety
{
    public enum SafetyPrintoutFilePrinter { MicrosoftPrintToPdf, MicrosoftXpsDocumentWriter }

    public enum SafetyPrintoutOption { All, Compact }

    public sealed class SafetyPrintout
    {
        public bool Print(SafetyPrintoutFilePrinter filePrinter, FileInfo fullOutputPath, string documentLayout, SafetyPrintoutOption documentationOption)
        {
            if (documentLayout == "fake:false") return false;
            if (documentLayout == "fake:unknown")
                throw new InvalidOperationException("No Document description object \"?\" found in System Library or Project");
            File.WriteAllText(fullOutputPath.FullName, $"%PDF-1.4 fake safety printout {filePrinter} {documentLayout} {documentationOption}");
            return true;
        }
    }
}
