// Test stand-in of the documented Siemens.Engineering.TextCategory (Siemens.Engineering.Base.xml, V21), named exactly as
// ProjectLanguagesRun binds the filtered ExportProjectTexts overload. Shapes only.
namespace Siemens.Engineering
{
    /// <summary>Test stand-in of the documented TextCategory (Identifier, Name), named as ProjectLanguagesRun binds it.</summary>
    public sealed class TextCategory(string identifier, string name)
    {
        public string Identifier { get; } = identifier;

        public string Name { get; } = name;
    }
}
