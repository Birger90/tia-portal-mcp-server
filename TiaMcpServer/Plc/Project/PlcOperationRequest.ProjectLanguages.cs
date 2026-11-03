using System.ComponentModel;

namespace TiaMcpServer.Plc;

/// <summary>The fields of the project language operations (issue #8); filePath and overwrite are shared.</summary>
public sealed partial class PlcOperationRequest
{
    [Description("set_project_languages: culture names (e.g. fr-FR) to activate as project languages (LanguageSettings.ActiveLanguages.Add(Language), the Language found with LanguageSettings.Languages.Find(CultureInfo)); a culture TIA Portal does not support fails target_not_found before anything changes.")]
    public IReadOnlyList<string>? AddLanguages { get; set; }

    [Description("set_project_languages: culture names to deactivate (LanguageSettings.ActiveLanguages.Remove(Language)); removed after the additions and the editing/reference language. TIA Portal decides what happens to the texts in that language.")]
    public IReadOnlyList<string>? RemoveLanguages { get; set; }

    [Description("set_project_languages: the culture name of the new editing language (LanguageSettings.EditingLanguage, set at runtime; the V21 reference documents no setter).")]
    public string? EditingLanguage { get; set; }

    [Description("set_project_languages: the culture name of the new reference language (LanguageSettings.ReferenceLanguage, set at runtime; the V21 reference documents no setter).")]
    public string? ReferenceLanguage { get; set; }

    [Description("export_project_texts (required): the culture name of the source language of the exported project texts.")]
    public string? SourceLanguage { get; set; }

    [Description("export_project_texts (required): the culture names of the target languages, 1-64 distinct.")]
    public IReadOnlyList<string>? TargetLanguages { get; set; }

    [Description("export_project_texts: text category identifiers (TextCategory.Identifier, listed by get_project_languages) to export; omitted, every category when the filtered overload is used.")]
    public IReadOnlyList<string>? TextCategories { get; set; }

    [Description("export_project_texts: names of the devices whose texts are exported (ExportProjectTexts objectsToExport); omitted, every device of the project when the filtered overload is used.")]
    public IReadOnlyList<string>? DeviceNames { get; set; }

    [Description("import_project_texts: true also updates the source-language texts from the file (ImportProjectTexts updateSourceLanguage). Default false.")]
    public bool? UpdateSourceLanguage { get; set; }
}
