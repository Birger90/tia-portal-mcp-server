using Siemens.Engineering;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// Project languages and project texts (issue #8) against a stand-in project the production <see cref="ProjectLanguagesRun"/>
/// binds and calls exactly like the worker. Supported: de-DE, en-US, fr-FR, it-IT; active de-DE and en-US, editing and reference
/// de-DE; text categories <c>Comment</c> and <c>AlarmText</c>; devices Station_1 and PLC_1. Removing the editing language is
/// rejected like TIA Portal would (invented). The text workbook is an invented layout: sheet <c>Texts</c>, header
/// <c>Category</c>, <c>Key</c> and one column per language; an import keeps its rows, so the verification export shows them.
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    public sealed class FakeTextProject
    {
        private readonly Dictionary<(string Category, string Key), Dictionary<string, string>> _texts = new();

        public FakeTextProject()
        {
            var supported = new[] { "de-DE", "en-US", "fr-FR", "it-IT" }.Select(c => new Language(c)).ToList();
            LanguageSettings = new LanguageSettings { Languages = new LanguageComposition(supported) };
            LanguageSettings.ActiveLanguages.Add(supported[0]);
            LanguageSettings.ActiveLanguages.Add(supported[1]);
            LanguageSettings.EditingLanguage = supported[0];
            LanguageSettings.ReferenceLanguage = supported[0];
            TextCategories = new TextCategoryComposition(new List<TextCategory> { new("Comment", "Kommentar"), new("AlarmText", "Meldetext") });
            _texts[("Comment", "PLC_1/Main")] = new() { ["de-DE"] = "Hauptprogramm", ["en-US"] = "Main program" };
        }

        public LanguageSettings LanguageSettings { get; }

        public TextCategoryComposition TextCategories { get; }

        public void ExportProjectTexts(FileInfo path, System.Globalization.CultureInfo sourceLanguage, System.Globalization.CultureInfo targetLanguage)
            => Write(path, sourceLanguage.Name, new[] { targetLanguage.Name });

        public ProjectTextResult ExportProjectTexts(FileInfo path, Language sourceLanguage, IEnumerable<Language> targetLanguages,
            IEnumerable<TextCategory> categories, IEnumerable<IEngineeringObject> objectsToExport)
        {
            _ = categories.ToList();
            _ = objectsToExport.ToList();
            Write(path, sourceLanguage.Culture.Name, targetLanguages.Select(l => l.Culture.Name).ToList());
            return new ProjectTextResult(path.FullName + ".log", ProjectTextResultState.Info);
        }

        public ProjectTextResult ImportProjectTexts(FileInfo path, bool updateSourceLanguage)
        {
            var grid = TiaMcpServer.Contracts.PlcAlarms.XlsxCellWorkbook.Read(File.ReadAllBytes(path.FullName));
            var sheet = grid.Sheets.Single(s => s.Name == "Texts");
            var header = sheet.Cells.Where(c => c.Row == 1).ToDictionary(c => c.Column, c => c.Value);
            foreach (var row in sheet.Cells.Where(c => c.Row > 1).GroupBy(c => c.Row))
            {
                var values = row.ToDictionary(c => header[c.Column], c => c.Value);
                var key = (values["Category"], values["Key"]);
                if (!_texts.TryGetValue(key, out var texts)) _texts[key] = texts = new();
                foreach (var (column, value) in values.Where(v => v.Key is not ("Category" or "Key")))
                    if (updateSourceLanguage || column != LanguageSettings.EditingLanguage.Culture.Name) texts[column] = value;
            }

            return new ProjectTextResult(path.FullName + ".log", ProjectTextResultState.Info);
        }

        private void Write(FileInfo path, string source, IReadOnlyList<string> targets)
        {
            var languages = new[] { source }.Concat(targets).Distinct().ToList();
            var rows = new List<IReadOnlyList<object?>> { new object?[] { "Category", "Key" }.Concat(languages).ToList() };
            foreach (var ((category, key), texts) in _texts.OrderBy(e => e.Key))
                rows.Add(new object?[] { category, key }.Concat(languages.Select(l => (object?)texts.GetValueOrDefault(l))).ToList());
            File.WriteAllBytes(path.FullName, XlsxFixture.Build(new[] { new XlsxFixture.Sheet("Texts", rows) }, "Project texts"));
        }
    }

    private readonly FakeTextProject _textProject = new();

    private string ProjectLanguages(WorkerRequest request)
    {
        var info = request.ProjectLanguages ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "missing request");
        var devices = new List<(string Name, object Device)> { ("Station_1", new FakeDevice("Station_1")), ("PLC_1", new FakeDevice("PLC_1")) };
        var target = new ProjectLanguageTarget(_textProject, names => names is null ? devices
            : names.Select(n => devices.FirstOrDefault(d => Names.Equals(d.Name, n)) is { Device: not null } found ? found
                : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The project has no device named '{n}'. Nothing was written.")).ToList());
        try
        {
            switch (request.Method)
            {
                case ProjectLanguageMethods.GetLanguages:
                    return Ok(ProjectLanguagesRun.Read(target, _ => false));
                case ProjectLanguageMethods.SetLanguages:
                    if (info.RemoveLanguages.Any(c => Names.Equals(c, _textProject.LanguageSettings.EditingLanguage.Culture.Name))
                        && info.EditingLanguage is null)
                        throw PortalRejection("The editing language cannot be deactivated.");
                    return Ok(ProjectLanguagesRun.Set(target, info, _ => false));
                case ProjectLanguageMethods.ExportTexts:
                    return Ok(ProjectLanguagesRun.Export(target, info, DateTime.UtcNow, _ => false));
                default:
                    return Ok(ProjectLanguagesRun.Import(target, info, _ => false));
            }
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }
}
