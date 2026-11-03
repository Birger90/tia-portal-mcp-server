// Stand-ins with the members Siemens.Engineering.Base.xml (V21) documents for project languages and project texts:
// LanguageSettings.Languages/ActiveLanguages/EditingLanguage/ReferenceLanguage, LanguageAssociation.Add/Remove/Find,
// LanguageComposition.Find, Language.Culture, TextCategory.Identifier/Name, TextCategoryComposition.Find(String),
// ProjectBase.ExportProjectTexts (both overloads), ImportProjectTexts(FileInfo, Boolean) returning ProjectTextResult (Path, State).
// The setters of EditingLanguage/ReferenceLanguage, the export's return value and the workbook layout are invented test
// behavior; member shapes only, no evidence of Siemens behavior.
namespace Siemens.Engineering
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    public interface IEngineeringObject
    {
    }

    public sealed class Language(string culture)
    {
        public CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(culture);
    }

    public sealed class LanguageComposition(List<Language> items) : IEnumerable<Language>
    {
        public Language? Find(CultureInfo culture) => items.FirstOrDefault(l => l.Culture.Name == culture.Name);

        public IEnumerator<Language> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class LanguageAssociation : IEnumerable<Language>
    {
        private readonly List<Language> _items = new();

        public void Add(Language language)
        {
            if (!_items.Contains(language)) _items.Add(language);
        }

        public bool Remove(Language language) => _items.Remove(language);

        public Language? Find(CultureInfo culture) => _items.FirstOrDefault(l => l.Culture.Name == culture.Name);

        public IEnumerator<Language> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class LanguageSettings
    {
        public LanguageComposition Languages { get; init; } = null!;

        public LanguageAssociation ActiveLanguages { get; } = new();

        public Language EditingLanguage { get; set; } = null!;

        public Language ReferenceLanguage { get; set; } = null!;
    }

    public sealed class TextCategory(string identifier, string name)
    {
        public string Identifier { get; } = identifier;

        public string Name { get; } = name;
    }

    public sealed class TextCategoryComposition(List<TextCategory> items) : IEnumerable<TextCategory>
    {
        public TextCategory? Find(string identifier) => items.FirstOrDefault(c => c.Identifier == identifier);

        public IEnumerator<TextCategory> GetEnumerator() => items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public enum ProjectTextResultState { Error, Info, Warning }

    public sealed class ProjectTextResult(string path, ProjectTextResultState state)
    {
        public string Path { get; } = path;

        public ProjectTextResultState State { get; } = state;
    }

    public sealed class FakeDevice(string name) : IEngineeringObject
    {
        public string Name { get; } = name;
    }
}
