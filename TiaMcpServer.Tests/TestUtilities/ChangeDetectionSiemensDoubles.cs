// Stand-ins of the documented change-detection providers (Siemens.Engineering.Step7.xml, V21), named exactly as the run
// matches them. Value types are undocumented; these use what the tests need. Member shapes only.
namespace Siemens.Engineering.SW
{
    public sealed class PlcChecksumProvider
    {
        public object? Software { get; set; }

        public object? TextLists { get; set; }
    }

    public sealed class FingerprintProvider(System.Collections.Generic.IList<object>? fingerprints)
    {
        public System.Collections.Generic.IList<object>? GetFingerprints() => fingerprints;
    }
}
