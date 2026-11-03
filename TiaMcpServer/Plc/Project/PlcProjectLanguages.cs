using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Plc;

/// <summary>The project language guards (issue #8): informative only.</summary>
public static partial class PlcGuardDefinitions
{
    public const string ChangesProjectLanguages = "plc_changes_project_languages";
    public const string ImportsProjectTexts = "plc_imports_project_texts";

    private static IEnumerable<WriteGuardDefinition> ProjectLanguageDefinitions() => new[]
    {
        new WriteGuardDefinition(ChangesProjectLanguages, WriteGuardSeverities.Info,
            "set_project_languages changes the project's languages, which every multilingual text of the project (comments, titles, alarm texts, text lists, HMI texts) is kept in: a new language starts without texts, and TIA Portal decides what happens to the texts of a language that is deactivated. The editing language is the one new texts are entered in."),
        new WriteGuardDefinition(ImportsProjectTexts, WriteGuardSeverities.Info,
            "import_project_texts writes the texts of a caller XLSX file into the project (ImportProjectTexts); which texts it changes is TIA Portal's decision. A verification export afterwards shows whether every imported row came back; otherwise the import is reported inconclusive."),
    };
}

/// <summary>
/// Planning, payload contracts and verification of the project language and project text operations (issue #8, not live-tested).
/// They are project-wide like the compilation settings (no plcName; <c>LanguageSettings</c> and the text export/import are members of
/// <c>ProjectBase</c>, so standalone projects and local sessions alike). set_project_languages makes one planning read
/// (<c>get_project_languages</c>) for the effect's current values; unreadable, it informs and the item runs. The worker reads the
/// languages back (<c>postcondition_failed</c> on a mismatch); the export is re-hashed at its place (check <c>file</c>); the import
/// is verified by a re-export whose rows must show every imported row (check <see cref="ImportedRowsCheck"/>), otherwise trusted
/// with the worker's warning.
/// </summary>
internal static class PlcProjectLanguages
{
    public const string LanguagesKind = "ProjectLanguages";
    public const string TextsKind = "ProjectTexts";
    public const string LanguagesCheck = "projectLanguages";
    public const string ImportedRowsCheck = "importedRows";

    public static ProjectLanguagesRequestInfo ToRequest(PlcOperationRequest item) => new()
    {
        AddLanguages = item.AddLanguages?.ToList() ?? new List<string>(),
        RemoveLanguages = item.RemoveLanguages?.ToList() ?? new List<string>(),
        EditingLanguage = item.EditingLanguage,
        ReferenceLanguage = item.ReferenceLanguage,
        FilePath = item.FilePath,
        Overwrite = item.Overwrite == true,
        SourceLanguage = item.SourceLanguage,
        TargetLanguages = item.TargetLanguages?.ToList() ?? new List<string>(),
        TextCategories = item.TextCategories?.ToList(),
        DeviceNames = item.DeviceNames?.ToList(),
        UpdateSourceLanguage = item.UpdateSourceLanguage == true,
    };

    public static async Task<(PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards, WriteToolError? Error)> ResolveAsync(OpennessWorkerClient client, PlcOperationRequest item)
    {
        var guards = new List<FiredGuard>();
        switch (item.Operation)
        {
            case ProjectLanguageMethods.SetLanguages:
            {
                ProjectLanguageStateInfo? current = null;
                var read = await client.ProjectLanguagesAsync(ProjectLanguageMethods.GetLanguages, new ProjectLanguagesRequestInfo()).ConfigureAwait(false);
                if (read.Success)
                {
                    try
                    {
                        current = CanonicalJson.Deserialize<ProjectLanguagesInfo>(DecodeRead(read.Payload ?? string.Empty).GetRawText()).State;
                    }
                    catch (JsonException)
                    {
                        current = null;
                    }
                }

                if (current is null)
                    guards.Add(new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                        $"Operation '{item.OperationId}': the current project languages could not be read for planning ({(read.Success ? "the result did not decode" : read.Error)}); the item runs and TIA Portal decides."));
                guards.Add(new FiredGuard(PlcGuardDefinitions.ChangesProjectLanguages, item.OperationId,
                    $"Operation '{item.OperationId}' changes the project languages ({Describe(item)})."));
                var changes = new List<PlcFieldChange>();
                string? Active(string culture) => current is null ? null : current.Active.Contains(culture, StringComparer.OrdinalIgnoreCase) ? "active" : "inactive";
                changes.AddRange((item.AddLanguages ?? Array.Empty<string>()).Select(c => new PlcFieldChange($"language:{c}", Active(c), "active")));
                changes.AddRange((item.RemoveLanguages ?? Array.Empty<string>()).Select(c => new PlcFieldChange($"language:{c}", Active(c), "inactive")));
                if (item.EditingLanguage is not null) changes.Add(new PlcFieldChange("editingLanguage", current?.Editing, item.EditingLanguage));
                if (item.ReferenceLanguage is not null) changes.Add(new PlcFieldChange("referenceLanguage", current?.Reference, item.ReferenceLanguage));
                return (new PlcWriteEffect(item.Operation, Target(LanguagesKind), changes, null, null, null, null), guards, null);
            }
            case ProjectLanguageMethods.ExportTexts:
            {
                if (item.Overwrite == true)
                    guards.Add(new FiredGuard(PlcGuardDefinitions.ExportOverwritesFile, item.OperationId, $"export_project_texts replaces '{item.FilePath}' if it exists (overwrite:true)."));
                var changes = new List<PlcFieldChange>
                {
                    new("filePath", null, item.FilePath),
                    new("sourceLanguage", null, item.SourceLanguage),
                    new("targetLanguages", null, string.Join(", ", item.TargetLanguages ?? Array.Empty<string>())),
                    new("textCategories", null, item.TextCategories is null ? "(every category)" : string.Join(", ", item.TextCategories)),
                    new("deviceNames", null, item.DeviceNames is null ? "(every device)" : string.Join(", ", item.DeviceNames)),
                };
                return (new PlcWriteEffect(item.Operation, Target(TextsKind), changes, item.FilePath, null, null, null), guards, null);
            }
            default:
                guards.Add(new FiredGuard(PlcGuardDefinitions.ImportsProjectTexts, item.OperationId,
                    $"import_project_texts imports '{item.FilePath}' into the project (updateSourceLanguage {(item.UpdateSourceLanguage == true ? "true" : "false")})."));
                return (new PlcWriteEffect(item.Operation, Target(TextsKind), new[]
                {
                    new PlcFieldChange("filePath", null, item.FilePath),
                    new PlcFieldChange("updateSourceLanguage", null, item.UpdateSourceLanguage == true ? "true" : "false"),
                }, null, null, null, null), guards, null);
        }
    }

    private static PlcTargetIdentity Target(string kind) => new(kind, string.Empty, null, null, null, null, null, null);

    private static string Describe(PlcOperationRequest item)
    {
        var parts = new List<string>();
        if (item.AddLanguages is { Count: > 0 } add) parts.Add("add " + string.Join(", ", add));
        if (item.RemoveLanguages is { Count: > 0 } remove) parts.Add("remove " + string.Join(", ", remove));
        if (item.EditingLanguage is not null) parts.Add("editing " + item.EditingLanguage);
        if (item.ReferenceLanguage is not null) parts.Add("reference " + item.ReferenceLanguage);
        return string.Join("; ", parts);
    }

    /// <exception cref="JsonException">The payload does not decode as <see cref="ProjectLanguagesInfo"/>.</exception>
    public static JsonElement DecodeRead(string payload)
        => CanonicalJson.NormalizeWorkerPayload<ProjectLanguagesInfo>(payload, info =>
        {
            if (info.State is null || info.Supported is null || info.TextCategories is null || info.Notes is null) throw new JsonException();
            CheckState(info.State);
            StandalonePayloadContract.RejectNullElements(info.Supported);
            StandalonePayloadContract.RejectNullElements(info.TextCategories);
            StandalonePayloadContract.RejectNullElements(info.Notes);
        }).Element;

    private static void CheckState(ProjectLanguageStateInfo? state)
    {
        if (state?.Active is null) throw new JsonException("The language state is missing.");
        StandalonePayloadContract.RejectNullElements(state.Active);
    }

    /// <exception cref="JsonException">The payload does not decode as the operation's result or contradicts the request.</exception>
    public static JsonElement DecodeWrite(PlcOperationRequest item, string payload) => item.Operation switch
    {
        ProjectLanguageMethods.SetLanguages => CanonicalJson.NormalizeWorkerPayload<ProjectLanguagesWriteResultInfo>(payload, result =>
        {
            if (result.Operation != item.Operation || result.Calls is null || result.Messages is null || result.Added is null || result.Removed is null)
                throw new JsonException();
            CheckState(result.Before);
            CheckState(result.After);
            if (!result.Added.SequenceEqual(item.AddLanguages ?? Array.Empty<string>(), StringComparer.Ordinal)
                || !result.Removed.SequenceEqual(item.RemoveLanguages ?? Array.Empty<string>(), StringComparer.Ordinal))
                throw new JsonException("The language result does not describe the request.");
        }).Element,
        ProjectLanguageMethods.ExportTexts => CanonicalJson.NormalizeWorkerPayload<ProjectTextsExportResultInfo>(payload, result =>
        {
            if (result.Operation != item.Operation || result.FilePath != item.FilePath || result.SourceLanguage != item.SourceLanguage
                || result.Overload is not (ProjectLanguageNames.OverloadCultures or ProjectLanguageNames.OverloadFiltered) || string.IsNullOrWhiteSpace(result.Signature)
                || result.TargetLanguages is null || !result.TargetLanguages.SequenceEqual(item.TargetLanguages ?? Array.Empty<string>(), StringComparer.Ordinal)
                || result.Sha256 is null || result.Sha256.Length != 64 || result.Bytes < 0 || result.Overwritten && item.Overwrite != true
                || result.Outcome is null || result.TextCategories is null || result.Devices is null || result.Messages is null)
                throw new JsonException("The export result does not describe the request.");
            StandalonePayloadContract.RejectNullElements(result.Messages);
        }).Element,
        _ => CanonicalJson.NormalizeWorkerPayload<ProjectTextsImportResultInfo>(payload, result =>
        {
            if (result.Operation != item.Operation || result.FilePath != item.FilePath || result.UpdateSourceLanguage != (item.UpdateSourceLanguage == true)
                || result.Outcome is null || result.ImportCheck is null || result.Messages is null || result.Sha256 is null || result.Sha256.Length != 64
                || result.ImportCheck.Outcome is not (ProjectLanguageNames.Verified or ProjectLanguageNames.Inconclusive or ProjectLanguageNames.NotReadBack)
                || result.ImportCheck.NotShown is null)
                throw new JsonException("The import result does not describe the request.");
            StandalonePayloadContract.RejectNullElements(result.Messages);
        }).Element,
    };

    /// <summary>Completed items with a read-back: the languages, the exported file, an import whose re-export showed every row.</summary>
    public static bool HasReadBack(PlcOperationRequest item, IReadOnlyDictionary<string, JsonElement> results)
    {
        if (!ProjectLanguageMethods.IsWrite(item.Operation) || !results.TryGetValue(item.OperationId, out var raw)) return false;
        return item.Operation != ProjectLanguageMethods.ImportTexts
            || CanonicalJson.Deserialize<ProjectTextsImportResultInfo>(raw.GetRawText()).ImportCheck.Outcome == ProjectLanguageNames.Verified;
    }

    public static WriteOperationVerification Verify(PlcOperationRequest item, IReadOnlyDictionary<string, JsonElement> results)
    {
        var raw = results[item.OperationId].GetRawText();
        switch (item.Operation)
        {
            case ProjectLanguageMethods.SetLanguages:
            {
                var result = CanonicalJson.Deserialize<ProjectLanguagesWriteResultInfo>(raw);
                var after = result.After;
                bool Has(string c) => after.Active.Contains(c, StringComparer.OrdinalIgnoreCase);
                var success = (item.AddLanguages ?? Array.Empty<string>()).All(Has) && !(item.RemoveLanguages ?? Array.Empty<string>()).Any(Has)
                    && (item.EditingLanguage is null || string.Equals(after.Editing, item.EditingLanguage, StringComparison.OrdinalIgnoreCase))
                    && (item.ReferenceLanguage is null || string.Equals(after.Reference, item.ReferenceLanguage, StringComparison.OrdinalIgnoreCase));
                return new(item.OperationId, success, LanguagesCheck, Describe(item),
                    $"active {string.Join(", ", after.Active)}; editing {after.Editing ?? "(unreadable)"}; reference {after.Reference ?? "(unreadable)"}",
                    success ? $"Operation '{item.OperationId}': the worker read LanguageSettings back right after the calls."
                        : $"Operation '{item.OperationId}': the languages did not read back as requested; read get_project_languages.");
            }
            case ProjectLanguageMethods.ExportTexts:
            {
                var result = CanonicalJson.Deserialize<ProjectTextsExportResultInfo>(raw);
                return new(item.OperationId, true, PlcVerificationPolicy.FileCheck, result.Sha256, result.Sha256,
                    $"Operation '{item.OperationId}': the worker re-read '{result.FilePath}' after placing it ({result.Bytes} bytes, sha256 {result.Sha256}).");
            }
            default:
            {
                var result = CanonicalJson.Deserialize<ProjectTextsImportResultInfo>(raw);
                var rows = result.ImportCheck.ImportedRows.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new(item.OperationId, true, ImportedRowsCheck, $"{rows} imported rows shown", $"{rows} imported rows shown",
                    $"Operation '{item.OperationId}': a verification export after the import shows every imported row (compared by header and value).");
            }
        }
    }

    /// <summary>The supersession keys: the languages are one project-wide object, an export is its file, imports share the project's texts.</summary>
    public static string[] ObjectKeys(PlcOperationRequest item, PlcTargetIdentity target)
        => item.Operation switch
        {
            ProjectLanguageMethods.SetLanguages => new[] { "project\nlanguages" },
            ProjectLanguageMethods.ExportTexts => new[] { PlcAlarms.PlcAlarmVerification.FileKey(item.FilePath) },
            _ => new[] { $"project\nprojecttexts\n{item.OperationId}" },
        };
}
