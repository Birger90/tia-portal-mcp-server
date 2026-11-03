using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Plc;

/// <summary>The project language and project text operations (issue #8): field sets and shape rules; nothing reads the project.</summary>
public static partial class PlcOperationCatalog
{
    private static IReadOnlyDictionary<string, Spec> WithProjectLanguageSpecs(IReadOnlyDictionary<string, Spec> existing)
    {
        var specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        foreach (var entry in existing) specs[entry.Key] = entry.Value;
        specs[ProjectLanguageMethods.GetLanguages] = Read(Array.Empty<string>(), Array.Empty<string>());
        specs[ProjectLanguageMethods.SetLanguages] = Write(Array.Empty<string>(), new[] { "addLanguages", "removeLanguages", "editingLanguage", "referenceLanguage" });
        specs[ProjectLanguageMethods.ExportTexts] = Write(new[] { "filePath", "sourceLanguage" }, new[] { "targetLanguages", "textCategories", "deviceNames", "overwrite" });
        specs[ProjectLanguageMethods.ImportTexts] = Write(new[] { "filePath" }, new[] { "updateSourceLanguage" });
        return specs;
    }

    private static IEnumerable<(string Name, Func<PlcOperationRequest, string?> Get)> ProjectLanguageStringFields() => new (string, Func<PlcOperationRequest, string?>)[]
    {
        ("editingLanguage", op => op.EditingLanguage),
        ("referenceLanguage", op => op.ReferenceLanguage),
        ("sourceLanguage", op => op.SourceLanguage),
    };

    private static IEnumerable<(string Name, Func<PlcOperationRequest, bool> IsSet)> ProjectLanguageOtherFields() => new (string, Func<PlcOperationRequest, bool>)[]
    {
        ("addLanguages", op => op.AddLanguages is not null),
        ("removeLanguages", op => op.RemoveLanguages is not null),
        ("targetLanguages", op => op.TargetLanguages is not null),
        ("textCategories", op => op.TextCategories is not null),
        ("deviceNames", op => op.DeviceNames is not null),
        ("updateSourceLanguage", op => op.UpdateSourceLanguage is not null),
    };

    private static void ValidateProjectLanguageItem(PlcOperationRequest op, string prefix, List<string> errors)
    {
        if (!ProjectLanguageMethods.IsWrite(op.Operation)) return;
        void Error(string message) => errors.Add($"{prefix}: {message}");
        void Cultures(string field, IReadOnlyList<string>? names, bool required)
        {
            if (names is null)
            {
                if (required) Error($"'{field}' is required: 1 to {ProjectLanguageNames.MaxLanguages} culture names such as en-US.");
                return;
            }

            if (names.Count is < 1 or > ProjectLanguageNames.MaxLanguages) Error($"'{field}' must hold 1 to {ProjectLanguageNames.MaxLanguages} culture names.");
            foreach (var name in names.Where(n => !ProjectLanguageNames.IsCultureName(n)))
                Error($"'{field}' entry '{name}' is not a culture name such as de-DE or en-US.");
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) Error($"'{field}' must not repeat a culture.");
        }

        void Culture(string field, string? name)
        {
            if (name is not null && !ProjectLanguageNames.IsCultureName(name)) Error($"'{field}' '{name}' is not a culture name such as de-DE or en-US.");
        }

        switch (op.Operation)
        {
            case ProjectLanguageMethods.SetLanguages:
                Cultures("addLanguages", op.AddLanguages, required: false);
                Cultures("removeLanguages", op.RemoveLanguages, required: false);
                Culture("editingLanguage", op.EditingLanguage);
                Culture("referenceLanguage", op.ReferenceLanguage);
                if (op.AddLanguages is null && op.RemoveLanguages is null && op.EditingLanguage is null && op.ReferenceLanguage is null)
                    Error("set_project_languages needs addLanguages, removeLanguages, editingLanguage or referenceLanguage.");
                if (op.AddLanguages?.Intersect(op.RemoveLanguages ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase).Any() == true)
                    Error("A culture must not be in both addLanguages and removeLanguages.");
                break;
            case ProjectLanguageMethods.ExportTexts:
                Culture("sourceLanguage", op.SourceLanguage);
                Cultures("targetLanguages", op.TargetLanguages, required: true);
                if (op.TextCategories is { } categories && (categories.Count is < 1 or > ProjectLanguageNames.MaxNames || categories.Any(string.IsNullOrWhiteSpace)))
                    Error($"'textCategories' must hold 1 to {ProjectLanguageNames.MaxNames} nonblank identifiers.");
                if (op.DeviceNames is { } devices && (devices.Count is < 1 or > ProjectLanguageNames.MaxNames || devices.Any(string.IsNullOrWhiteSpace)))
                    Error($"'deviceNames' must hold 1 to {ProjectLanguageNames.MaxNames} nonblank names.");
                if (PlcAlarmPolicy.CheckFilePath(op.FilePath, ProjectLanguageNames.Extension) is { } exportPath) Error(exportPath);
                break;
            case ProjectLanguageMethods.ImportTexts:
                if (PlcAlarmPolicy.CheckFilePath(op.FilePath, ProjectLanguageNames.Extension) is { } importPath) Error(importPath);
                break;
        }
    }
}
