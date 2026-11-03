using System.Collections;
using System.Globalization;
using System.Reflection;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Project;

/// <summary>
/// Project languages and project texts (GitHub issue #8). Evidence: the V21 IntelliSense file Siemens.Engineering.Base.xml:
/// <c>P:Siemens.Engineering.ProjectBase.LanguageSettings</c>, <c>LanguageSettings.Languages</c> ("all supported languages"),
/// <c>.ActiveLanguages</c>, <c>.EditingLanguage</c>, <c>.ReferenceLanguage</c>; <c>LanguageAssociation.Add(Language)</c>,
/// <c>.Remove(Language)</c>, <c>.Find(CultureInfo)</c>; <c>LanguageComposition.Find(CultureInfo)</c>; <c>Language.Culture</c>;
/// <c>P:Siemens.Engineering.ProjectBase.TextCategories</c>, <c>TextCategory.Identifier</c>/<c>.Name</c>,
/// <c>TextCategoryComposition.Find(String)</c>; <c>ProjectBase.ExportProjectTexts(FileInfo, CultureInfo, CultureInfo)</c> and
/// <c>ExportProjectTexts(FileInfo, Language, IEnumerable&lt;Language&gt;, IEnumerable&lt;TextCategory&gt;, IEnumerable&lt;IEngineeringObject&gt;)</c>
/// ("Export project text to an xlsx file"); <c>ProjectBase.ImportProjectTexts(FileInfo, Boolean)</c> returning
/// <c>ProjectTextResult</c> (<c>Path</c>, <c>State</c> of <c>ProjectTextResultState</c> Error/Info/Warning). The XML documents no
/// setter for the editing and reference language and no return type for the export; they are bound at runtime.
/// Each method name is also the public plc_read / plc_write operation name; parameters travel in <c>WorkerRequest.ProjectLanguages</c>.
/// </summary>
public static class ProjectLanguageMethods
{
    /// <summary>plc_read, Observe: the active, editing, reference and supported languages and the text categories.</summary>
    public const string GetLanguages = "get_project_languages";

    /// <summary>plc_write, ProjectMutation: activates and deactivates project languages, sets the editing and reference language.</summary>
    public const string SetLanguages = "set_project_languages";

    /// <summary>plc_write, ProjectMutation (writes a caller file): <c>ProjectBase.ExportProjectTexts</c>.</summary>
    public const string ExportTexts = "export_project_texts";

    /// <summary>plc_write, ProjectMutation: <c>ProjectBase.ImportProjectTexts(FileInfo, Boolean)</c>.</summary>
    public const string ImportTexts = "import_project_texts";

    public static IReadOnlyList<string> Read { get; } = Array.AsReadOnly(new[] { GetLanguages });

    public static IReadOnlyList<string> Write { get; } = Array.AsReadOnly(new[] { SetLanguages, ExportTexts, ImportTexts });

    public static bool IsRead(string? method) => method is not null && Read.Contains(method, StringComparer.Ordinal);

    public static bool IsWrite(string? method) => method is not null && Write.Contains(method, StringComparer.Ordinal);

    public static bool IsWorkerMethod(string? method) => IsRead(method) || IsWrite(method);
}

/// <summary>The documented names and limits of the project language operations.</summary>
public static class ProjectLanguageNames
{
    public const string LanguageType = "Siemens.Engineering.Language";
    public const string TextCategoryType = "Siemens.Engineering.TextCategory";
    public const string EngineeringObjectType = "Siemens.Engineering.IEngineeringObject";
    public const string Export = "ExportProjectTexts";
    public const string Import = "ImportProjectTexts";

    /// <summary>The documented file format: "Export project text to an xlsx file".</summary>
    public const string Extension = ".xlsx";

    public const string OverloadCultures = "cultures";
    public const string OverloadFiltered = "filtered";

    public const int MaxLanguages = 64;
    public const int MaxNames = 500;

    /// <summary>The <c>importCheck.outcome</c> values.</summary>
    public const string Verified = "verified";
    public const string Inconclusive = "inconclusive";
    public const string NotReadBack = "notReadBack";

    /// <summary>A culture name as TIA Portal's languages use (<c>de-DE</c>, <c>en-US</c>, <c>zh-CN</c>); the worker resolves it.</summary>
    public static bool IsCultureName(string? name)
        => !string.IsNullOrEmpty(name) && name!.Length <= 32
            && System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

/// <summary>The worker parameters of the project language operations; only the method's fields are read.</summary>
public sealed class ProjectLanguagesRequestInfo
{
    public List<string> AddLanguages { get; set; } = new();

    public List<string> RemoveLanguages { get; set; } = new();

    public string? EditingLanguage { get; set; }

    public string? ReferenceLanguage { get; set; }

    public string? FilePath { get; set; }

    public bool Overwrite { get; set; }

    public string? SourceLanguage { get; set; }

    public List<string> TargetLanguages { get; set; } = new();

    /// <summary>Text category identifiers (<c>TextCategory.Identifier</c>); null means every category.</summary>
    public List<string>? TextCategories { get; set; }

    /// <summary>Device names whose texts are exported; null means every device of the project.</summary>
    public List<string>? DeviceNames { get; set; }

    public bool UpdateSourceLanguage { get; set; }
}

public sealed class ProjectTextCategoryInfo
{
    public string Identifier { get; set; } = string.Empty;

    public string? Name { get; set; }
}

/// <summary>The language state of a project; culture names, null where unreadable.</summary>
public sealed class ProjectLanguageStateInfo
{
    public List<string> Active { get; set; } = new();

    public string? Editing { get; set; }

    public string? Reference { get; set; }
}

/// <summary>get_project_languages.</summary>
public sealed class ProjectLanguagesInfo
{
    public ProjectLanguageStateInfo State { get; set; } = new();

    /// <summary>Every language TIA Portal supports in this project (<c>LanguageSettings.Languages</c>).</summary>
    public List<string> Supported { get; set; } = new();

    public List<ProjectTextCategoryInfo> TextCategories { get; set; } = new();

    public List<string> Notes { get; set; } = new();
}

/// <summary>set_project_languages: the state before and after (read back right after the calls) and the calls made.</summary>
public sealed class ProjectLanguagesWriteResultInfo
{
    public string Operation { get; set; } = ProjectLanguageMethods.SetLanguages;

    public ProjectLanguageStateInfo Before { get; set; } = new();

    public ProjectLanguageStateInfo After { get; set; } = new();

    public List<string> Added { get; set; } = new();

    public List<string> Removed { get; set; } = new();

    public List<string> Calls { get; set; } = new();

    /// <summary>K75: the TIA Portal notifications (information messages) handled while the call ran; empty when none.</summary>
    public List<PortalNotificationInfo> PortalNotifications { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>A ProjectTextResult (<c>State</c> member name, <c>Path</c> as text); null members when not returned or unreadable.</summary>
public sealed class ProjectTextOutcomeInfo
{
    public string? State { get; set; }

    public string? Path { get; set; }

    public string? ReturnType { get; set; }
}

/// <summary>export_project_texts.</summary>
public sealed class ProjectTextsExportResultInfo
{
    public string Operation { get; set; } = ProjectLanguageMethods.ExportTexts;

    /// <summary><see cref="ProjectLanguageNames.OverloadCultures"/> or <see cref="ProjectLanguageNames.OverloadFiltered"/>.</summary>
    public string Overload { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public string SourceLanguage { get; set; } = string.Empty;

    public List<string> TargetLanguages { get; set; } = new();

    /// <summary>The categories passed (identifiers); empty for the culture overload, which takes none.</summary>
    public List<string> TextCategories { get; set; } = new();

    /// <summary>The devices passed (names); empty for the culture overload, which takes none.</summary>
    public List<string> Devices { get; set; } = new();

    public ProjectTextOutcomeInfo Outcome { get; set; } = new();

    public string FilePath { get; set; } = string.Empty;

    public bool Overwritten { get; set; }

    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    /// <summary>K75: the TIA Portal notifications (information messages) handled while the call ran; empty when none.</summary>
    public List<PortalNotificationInfo> PortalNotifications { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>The re-export after an import: whether every imported row shows (<see cref="PlcAlarms.XlsxCellWorkbook.CompareRows"/>).</summary>
public sealed class ProjectTextsImportCheckInfo
{
    public string Outcome { get; set; } = ProjectLanguageNames.NotReadBack;

    public int ImportedRows { get; set; }

    public int RowsNotShown { get; set; }

    public List<string> NotShown { get; set; } = new();

    public string? Reason { get; set; }
}

/// <summary>import_project_texts.</summary>
public sealed class ProjectTextsImportResultInfo
{
    public string Operation { get; set; } = ProjectLanguageMethods.ImportTexts;

    public string Signature { get; set; } = string.Empty;

    public string FilePath { get; set; } = string.Empty;

    /// <summary>Size and SHA-256 of the private copy that was imported.</summary>
    public long Bytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public bool UpdateSourceLanguage { get; set; }

    public ProjectTextOutcomeInfo Outcome { get; set; } = new();

    public ProjectTextsImportCheckInfo ImportCheck { get; set; } = new();

    /// <summary>K75: the TIA Portal notifications (information messages) handled while the call ran; empty when none.</summary>
    public List<PortalNotificationInfo> PortalNotifications { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

/// <summary>
/// The project as the language operations see it: the live ProjectBase (bound by reflection, so the FakeWorker can stand in) and
/// a resolver of devices by name (null: every device), which the worker implements over <c>Devices</c> and <c>DeviceGroups</c>.
/// </summary>
public sealed class ProjectLanguageTarget(object project, Func<IReadOnlyList<string>?, IReadOnlyList<(string Name, object Device)>> devices)
{
    public object Project { get; } = project;

    public Func<IReadOnlyList<string>?, IReadOnlyList<(string Name, object Device)>> Devices { get; } = devices;
}

/// <summary>
/// The Siemens-free part of the project language operations, shared by the worker and the FakeWorker. Every member is bound at
/// runtime by exact name and parameter types (<see cref="LibraryRuntimeBinding"/>); a missing one fails <c>api_member_missing</c>
/// before anything changes. A culture TIA Portal does not support (<c>Languages.Find</c> returns null) is <c>target_not_found</c>.
/// Failures are <see cref="RuntimeBindingException"/> with their category.
/// </summary>
public static class ProjectLanguagesRun
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    public static ProjectLanguagesInfo Read(ProjectLanguageTarget target, Func<Exception, bool> recoverable)
    {
        var info = new ProjectLanguagesInfo();
        var settings = Settings(target);
        info.State = State(settings, recoverable, info.Notes);
        try
        {
            info.Supported = Elements(Get(settings, "Languages"), "LanguageSettings.Languages").Select(Culture).OfType<string>()
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception exception) when (recoverable(exception))
        {
            info.Notes.Add($"LanguageSettings.Languages is not readable: {exception.GetType().Name}: {exception.Message}");
        }

        try
        {
            foreach (var category in Elements(Get(target.Project, "TextCategories"), "ProjectBase.TextCategories"))
                info.TextCategories.Add(new ProjectTextCategoryInfo
                {
                    Identifier = Text(Get(category!, "Identifier")) ?? string.Empty,
                    Name = Text(Get(category!, "Name")),
                });
        }
        catch (Exception exception) when (recoverable(exception) || exception is RuntimeBindingException)
        {
            info.Notes.Add($"ProjectBase.TextCategories is not readable: {exception.Message}");
        }

        return info;
    }

    /// <summary>Adds the languages, sets the reference and editing language, removes the languages, then reads everything back.</summary>
    public static ProjectLanguagesWriteResultInfo Set(ProjectLanguageTarget target, ProjectLanguagesRequestInfo request, Func<Exception, bool> recoverable)
    {
        var settings = Settings(target);
        var result = new ProjectLanguagesWriteResultInfo();
        var supported = Get(settings, "Languages") ?? throw Missing("LanguageSettings.Languages returned null.");
        var active = Get(settings, "ActiveLanguages") ?? throw Missing("LanguageSettings.ActiveLanguages returned null.");

        // Everything is resolved and bound before the first change.
        object Resolve(string culture) => Find(supported, culture, "LanguageSettings.Languages")
            ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound,
                $"TIA Portal supports no language '{culture}' in this project (LanguageSettings.Languages.Find returned null). Nothing was changed.");
        var adds = request.AddLanguages.Select(c => (Culture: c, Language: Resolve(c))).ToList();
        var removes = request.RemoveLanguages.Select(c => (Culture: c, Language: Resolve(c))).ToList();
        var editing = request.EditingLanguage is { } e ? Resolve(e) : null;
        var reference = request.ReferenceLanguage is { } r ? Resolve(r) : null;
        MethodInfo? add = null, remove = null;
        if (adds.Count > 0) add = Bind(active, "Add", adds[0].Language.GetType(), "LanguageAssociation.Add(Language)");
        if (removes.Count > 0) remove = Bind(active, "Remove", removes[0].Language.GetType(), "LanguageAssociation.Remove(Language)");
        var editingProperty = editing is null ? null : Setter(settings, "EditingLanguage", editing.GetType());
        var referenceProperty = reference is null ? null : Setter(settings, "ReferenceLanguage", reference.GetType());

        result.Before = State(settings, recoverable, result.Messages);
        foreach (var (culture, language) in adds)
        {
            if (result.Before.Active.Contains(culture, StringComparer.OrdinalIgnoreCase))
                result.Messages.Add($"'{culture}' was already active; LanguageAssociation.Add was still called (TIA Portal decides).");
            RuntimeServiceValues.Call(add!, active, new[] { language });
            result.Calls.Add($"ActiveLanguages.Add({culture})");
            result.Added.Add(culture);
        }

        if (referenceProperty is not null)
        {
            Assign(referenceProperty, settings, reference!);
            result.Calls.Add($"LanguageSettings.ReferenceLanguage = {request.ReferenceLanguage}");
        }

        if (editingProperty is not null)
        {
            Assign(editingProperty, settings, editing!);
            result.Calls.Add($"LanguageSettings.EditingLanguage = {request.EditingLanguage}");
        }

        foreach (var (culture, language) in removes)
        {
            var returned = RuntimeServiceValues.Call(remove!, active, new[] { language });
            result.Calls.Add($"ActiveLanguages.Remove({culture}){(returned is bool removed ? $" returned {(removed ? "true" : "false")}" : string.Empty)}");
            result.Removed.Add(culture);
        }

        result.After = State(settings, recoverable, result.Messages);
        var problems = new List<string>();
        bool Has(string culture) => result.After.Active.Contains(culture, StringComparer.OrdinalIgnoreCase);
        problems.AddRange(request.AddLanguages.Where(c => !Has(c)).Select(c => $"'{c}' is not active"));
        problems.AddRange(request.RemoveLanguages.Where(Has).Select(c => $"'{c}' is still active"));
        if (request.EditingLanguage is { } wantedEditing && !Same(result.After.Editing, wantedEditing))
            problems.Add($"the editing language reads '{result.After.Editing ?? "(unreadable)"}'");
        if (request.ReferenceLanguage is { } wantedReference && !Same(result.After.Reference, wantedReference))
            problems.Add($"the reference language reads '{result.After.Reference ?? "(unreadable)"}'");
        if (problems.Count > 0)
            throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                $"The language calls returned without an error ({string.Join("; ", result.Calls)}), but afterwards {string.Join(", ", problems)}. "
                + "The calls before the mismatch may have changed the project; read get_project_languages.");
        return result;
    }

    /// <summary>
    /// The export: the overload with cultures when exactly one target language and no categories or devices are named, otherwise the
    /// filtered overload with the named (or every) category and device. The file is written next to the target, moved into place
    /// without overwriting unless <c>overwrite</c>, and re-hashed (the alarm exports' caller-file rules).
    /// </summary>
    public static ProjectTextsExportResultInfo Export(ProjectLanguageTarget target, ProjectLanguagesRequestInfo request, DateTime utcNow,
        Func<Exception, bool> recoverable)
    {
        var path = request.FilePath ?? string.Empty;
        Files(() => PlcAlarmFiles.PrecheckExport(path, request.Overwrite, ProjectLanguageNames.Extension));
        var result = new ProjectTextsExportResultInfo { SourceLanguage = request.SourceLanguage ?? string.Empty, TargetLanguages = request.TargetLanguages.ToList() };
        var call = PrepareExport(target, request.SourceLanguage!, request.TargetLanguages, request.TextCategories, request.DeviceNames, result);
        result.Messages.AddRange(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path)));
        var swept = PlcAlarmFiles.SweepStaleTemporaries(path, utcNow);
        if (swept > 0) result.Messages.Add($"Removed {swept} temporary file(s) next to '{path}' that an interrupted earlier call left behind.");
        var temporary = PlcAlarmFiles.TemporaryPath(path);
        try
        {
            result.Outcome = Outcome(call(temporary), recoverable);
            if (string.Equals(result.Outcome.State, "Error", StringComparison.Ordinal))
                throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed,
                    $"ExportProjectTexts reported state Error{(result.Outcome.Path is { } log ? $" (result path '{log}')" : string.Empty)}. Nothing was placed at '{path}'.");
            if (!File.Exists(temporary))
                throw new RuntimeBindingException(WorkerFailureCategories.PostconditionFailed,
                    $"ExportProjectTexts returned, but no file was written at the path it was given ('{temporary}'). Nothing was placed at the target.");
            var (overwritten, bytes, sha256) = Files(() => PlcAlarmFiles.Commit(temporary, path, request.Overwrite));
            (result.FilePath, result.Overwritten, result.Bytes, result.Sha256) = (path, overwritten, bytes, sha256);
            if (string.Equals(result.Outcome.State, "Warning", StringComparison.Ordinal))
                result.Messages.Add("ExportProjectTexts reported state Warning" + (result.Outcome.Path is { } warningPath ? $"; see '{warningPath}'." : "."));
            return result;
        }
        finally
        {
            PlcAlarmFiles.Discard(temporary);
        }
    }

    /// <summary>
    /// The import of a private copy of the caller's file, then a verification re-export (the filtered overload: the editing
    /// language as source, every other active language as target, every category and device) whose rows are compared with the
    /// imported ones by header (<see cref="XlsxCellWorkbook.CompareRows"/>). The re-export's scope is not documented to equal the
    /// imported file's, so anything but every row shown is <c>inconclusive</c>, never a failure of the import.
    /// </summary>
    public static ProjectTextsImportResultInfo Import(ProjectLanguageTarget target, ProjectLanguagesRequestInfo request, Func<Exception, bool> recoverable,
        string? temporaryRoot = null)
    {
        var path = request.FilePath ?? string.Empty;
        Files(() => PlcAlarmFiles.PrecheckImport(path, ProjectLanguageNames.Extension));
        var result = new ProjectTextsImportResultInfo { FilePath = path, UpdateSourceLanguage = request.UpdateSourceLanguage };
        result.Messages.AddRange(PlcAlarmFiles.Notes(PlcAlarmFiles.Inspect(path)));
        var method = LibraryRuntimeBinding.Find(target.Project.GetType(), ProjectLanguageNames.Import, new[] { typeof(FileInfo), typeof(bool) }, out var error)
            ?? throw Missing($"{error ?? "No ImportProjectTexts."} (documented: ProjectBase.ImportProjectTexts(FileInfo, Boolean)) Nothing was imported.");
        result.Signature = Signature(method);
        var directory = Path.Combine(temporaryRoot ?? Path.GetTempPath(), "tia-mcp-projecttexts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var copy = Path.Combine(directory, Path.GetFileName(path));
            File.Copy(path, copy);
            (result.Bytes, result.Sha256) = PlcAlarmFiles.Hash(copy);
            result.Outcome = Outcome(RuntimeServiceValues.Call(method, target.Project, new object?[] { new FileInfo(copy), request.UpdateSourceLanguage }), recoverable);
            if (result.Outcome.State is null or "Error")
                throw new RuntimeBindingException(WorkerFailureCategories.WorkerOperationFailed,
                    $"ImportProjectTexts reported {(result.Outcome.State is null ? "no readable state" : "state Error")}"
                    + (result.Outcome.Path is { } log ? $" (result path '{log}')" : string.Empty) + "; the import may already have changed texts of the project.");
            if (result.Outcome.State == "Warning")
                result.Messages.Add("ImportProjectTexts reported state Warning" + (result.Outcome.Path is { } warningPath ? $"; see '{warningPath}'." : "."));
            result.ImportCheck = Verify(target, copy, Path.Combine(directory, "reexport" + ProjectLanguageNames.Extension), recoverable);
            if (result.ImportCheck.Outcome != ProjectLanguageNames.Verified)
                result.Messages.Add($"The import reported state {result.Outcome.State} and changed the project; it is {result.ImportCheck.Outcome}: {result.ImportCheck.Reason}.");
            return result;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result.Messages.Add($"The private copy directory '{directory}' could not be removed ({exception.GetType().Name}).");
            }
        }
    }

    private static ProjectTextsImportCheckInfo Verify(ProjectLanguageTarget target, string imported, string again, Func<Exception, bool> recoverable)
    {
        var check = new ProjectTextsImportCheckInfo();
        try
        {
            var state = State(Settings(target), recoverable, new List<string>());
            if (state.Editing is null)
            {
                check.Reason = "the editing language is not readable, so no verification export was made";
                return check;
            }

            var targets = state.Active.Where(c => !Same(c, state.Editing)).ToList();
            if (targets.Count == 0)
            {
                check.Reason = "the project has no active language besides the editing language, so no verification export was made";
                return check;
            }

            var scratch = new ProjectTextsExportResultInfo();
            var call = PrepareExport(target, state.Editing, targets, null, null, scratch);
            var outcome = Outcome(call(again), recoverable);
            if (outcome.State == "Error" || !File.Exists(again))
            {
                (check.Outcome, check.Reason) = (ProjectLanguageNames.Inconclusive,
                    $"the verification export ({scratch.Signature}) reported {outcome.State ?? "no state"}{(File.Exists(again) ? string.Empty : " and wrote no file")}");
                return check;
            }

            var comparison = XlsxCellWorkbook.CompareRows(XlsxCellWorkbook.Read(File.ReadAllBytes(imported)), XlsxCellWorkbook.Read(File.ReadAllBytes(again)));
            (check.ImportedRows, check.RowsNotShown, check.NotShown) = (comparison.ImportedRows, comparison.RowsNotShown, comparison.NotShown.ToList());
            if (comparison.RowsNotShown == 0 && comparison.NotCompared.Count == 0)
            {
                check.Outcome = ProjectLanguageNames.Verified;
                return check;
            }

            check.Outcome = ProjectLanguageNames.Inconclusive;
            check.Reason = (comparison.RowsNotShown > 0
                    ? $"the verification export (source {state.Editing}, targets {string.Join(", ", targets)}, every category and device) does not show {comparison.RowsNotShown} of {comparison.ImportedRows} imported rows ({string.Join(", ", comparison.NotShown.Take(5))})"
                    : "every compared row shows, but the layouts differ")
                + (comparison.NotCompared.Count > 0 ? $"; not compared: {string.Join("; ", comparison.NotCompared.Take(5))}" : string.Empty)
                + "; the export's scope is not documented to equal the imported file's (with updateSourceLanguage false the source-language column is not imported)";
            return check;
        }
        catch (Exception exception) when (recoverable(exception) || exception is RuntimeBindingException or PlcAlarmException or IOException or UnauthorizedAccessException)
        {
            (check.Outcome, check.Reason) = (ProjectLanguageNames.Inconclusive, $"the verification export could not be compared ({exception.GetType().Name}: {exception.Message})");
            return check;
        }
    }

    /// <summary>Resolves languages, categories and devices and binds the overload; returns the call. Nothing is called here.</summary>
    private static Func<string, object?> PrepareExport(ProjectLanguageTarget target, string source, IReadOnlyList<string> targets, IReadOnlyList<string>? categories,
        IReadOnlyList<string>? devices, ProjectTextsExportResultInfo result)
    {
        var projectType = target.Project.GetType();
        if (targets.Count == 1 && categories is null && devices is null)
        {
            var simple = LibraryRuntimeBinding.Find(projectType, ProjectLanguageNames.Export, new[] { typeof(FileInfo), typeof(CultureInfo), typeof(CultureInfo) }, out var simpleError)
                ?? throw Missing($"{simpleError ?? "No ExportProjectTexts."} (documented: ProjectBase.ExportProjectTexts(FileInfo, CultureInfo, CultureInfo)) Nothing was written.");
            var sourceCulture = CultureOf(source);
            var targetCulture = CultureOf(targets[0]);
            (result.Overload, result.Signature) = (ProjectLanguageNames.OverloadCultures, Signature(simple));
            return file => RuntimeServiceValues.Call(simple, target.Project, new object?[] { new FileInfo(file), sourceCulture, targetCulture });
        }

        var method = projectType.GetMethods(PublicInstance).Where(m => m.Name == ProjectLanguageNames.Export && !m.IsGenericMethodDefinition)
            .Select(m => (Method: m, Parameters: m.GetParameters()))
            .Where(e => e.Parameters.Length == 5 && e.Parameters[0].ParameterType == typeof(FileInfo) && e.Parameters[1].ParameterType.FullName == ProjectLanguageNames.LanguageType
                && Element(e.Parameters[2].ParameterType)?.FullName == ProjectLanguageNames.LanguageType
                && Element(e.Parameters[3].ParameterType)?.FullName == ProjectLanguageNames.TextCategoryType
                && Element(e.Parameters[4].ParameterType)?.FullName == ProjectLanguageNames.EngineeringObjectType)
            .Select(e => e.Method).Distinct().ToList();
        if (method.Count != 1)
            throw Missing($"'{projectType.FullName}' exposes {method.Count} ExportProjectTexts(FileInfo, Language, IEnumerable<Language>, IEnumerable<TextCategory>, IEnumerable<IEngineeringObject>); none is chosen. Nothing was written.");
        var bound = method[0];
        var parameters = bound.GetParameters();
        var supported = Get(Settings(target), "Languages") ?? throw Missing("LanguageSettings.Languages returned null.");
        object Language(string culture) => Find(supported, culture, "LanguageSettings.Languages")
            ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound, $"TIA Portal supports no language '{culture}' in this project. Nothing was written.");
        var sourceLanguage = Language(source);
        var targetLanguages = targets.Select(Language).ToList();

        var composition = Get(target.Project, "TextCategories") ?? throw Missing("ProjectBase.TextCategories returned null.");
        List<object> chosenCategories;
        if (categories is null)
        {
            chosenCategories = Elements(composition, "ProjectBase.TextCategories").OfType<object>().ToList();
        }
        else
        {
            var find = LibraryRuntimeBinding.Find(composition.GetType(), "Find", new[] { typeof(string) }, out var findError)
                ?? throw Missing($"{findError ?? "No Find."} (documented: TextCategoryComposition.Find(String)) Nothing was written.");
            chosenCategories = categories.Select(id => RuntimeServiceValues.Call(find, composition, new object?[] { id })
                ?? throw new RuntimeBindingException(WorkerFailureCategories.TargetNotFound,
                    $"The project has no text category '{id}' (TextCategoryComposition.Find returned null; read get_project_languages for the identifiers). Nothing was written.")).ToList();
        }

        var chosenDevices = target.Devices(devices);
        result.TextCategories = chosenCategories.Select(c => Text(Get(c, "Identifier")) ?? "(unreadable)").ToList();
        result.Devices = chosenDevices.Select(d => d.Name).ToList();
        if (devices is null) result.Messages.Add($"No deviceNames were given: the texts of every device of the project ({chosenDevices.Count}) were requested.");
        if (categories is null) result.Messages.Add($"No textCategories were given: every text category of the project ({chosenCategories.Count}) was requested.");

        object Collection(int index, IReadOnlyList<object> items)
        {
            var element = Element(parameters[index].ParameterType)!;
            return LibraryRuntimeBinding.BuildCollection(parameters[index].ParameterType, element, items)
                ?? throw Missing($"A collection of {element.FullName} for ExportProjectTexts could not be built. Nothing was written.");
        }

        var languages = Collection(2, targetLanguages);
        var textCategories = Collection(3, chosenCategories);
        var objects = Collection(4, chosenDevices.Select(d => d.Device).ToList());
        (result.Overload, result.Signature) = (ProjectLanguageNames.OverloadFiltered, Signature(bound));
        return file => RuntimeServiceValues.Call(bound, target.Project, new object?[] { new FileInfo(file), sourceLanguage, languages, textCategories, objects });
    }

    private static ProjectTextOutcomeInfo Outcome(object? returned, Func<Exception, bool> recoverable)
    {
        var outcome = new ProjectTextOutcomeInfo { ReturnType = returned?.GetType().FullName };
        if (returned is null) return outcome;
        var state = RuntimeServiceValues.ReadProperty(returned, "State", recoverable);
        outcome.State = state.State == RuntimeValueStates.Value ? state.Value : null;
        var path = RuntimeServiceValues.ReadProperty(returned, "Path", recoverable);
        outcome.Path = path.State == RuntimeValueStates.Value ? path.Value : null;
        return outcome;
    }

    private static object Settings(ProjectLanguageTarget target)
        => Get(target.Project, "LanguageSettings") ?? throw Missing("ProjectBase.LanguageSettings returned null.");

    private static ProjectLanguageStateInfo State(object settings, Func<Exception, bool> recoverable, List<string> notes)
    {
        var state = new ProjectLanguageStateInfo();
        try
        {
            state.Active = Elements(Get(settings, "ActiveLanguages"), "LanguageSettings.ActiveLanguages").Select(Culture).OfType<string>().ToList();
        }
        catch (Exception exception) when (recoverable(exception))
        {
            notes.Add($"LanguageSettings.ActiveLanguages is not readable: {exception.GetType().Name}: {exception.Message}");
        }

        state.Editing = ReadLanguage(settings, "EditingLanguage", recoverable, notes);
        state.Reference = ReadLanguage(settings, "ReferenceLanguage", recoverable, notes);
        return state;
    }

    private static string? ReadLanguage(object settings, string property, Func<Exception, bool> recoverable, List<string> notes)
    {
        try
        {
            return Culture(Get(settings, property));
        }
        catch (Exception exception) when (recoverable(exception))
        {
            notes.Add($"LanguageSettings.{property} is not readable: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static string? Culture(object? language)
        => language is null ? null : Get(language, "Culture") is CultureInfo culture ? culture.Name : null;

    private static CultureInfo CultureOf(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            throw new RuntimeBindingException(WorkerFailureCategories.ValidationError, $"'{name}' is not a culture this system knows. Nothing was written.");
        }
    }

    private static object? Find(object composition, string culture, string documented)
    {
        var find = LibraryRuntimeBinding.Find(composition.GetType(), "Find", new[] { typeof(CultureInfo) }, out var error)
            ?? throw Missing($"{error ?? "No Find."} (documented: {documented}.Find(CultureInfo)) Nothing was changed.");
        return RuntimeServiceValues.Call(find, composition, new object?[] { CultureOf(culture) });
    }

    private static MethodInfo Bind(object target, string name, Type argument, string documented)
        => LibraryRuntimeBinding.Find(target.GetType(), name, new[] { argument }, out var error)
            ?? throw Missing($"{error ?? $"No {name}."} (documented: {documented}) Nothing was changed.");

    /// <summary>A public writable property of exactly that name accepting the language; the XML documents no setter, so none is api_member_missing.</summary>
    private static PropertyInfo Setter(object settings, string name, Type value)
    {
        var property = settings.GetType().GetProperty(name, PublicInstance);
        if (property is { CanWrite: true } && property.GetSetMethod() is not null && property.PropertyType.IsAssignableFrom(value)) return property;
        throw Missing($"LanguageSettings.{name} is not writable on this TIA Portal installation (the V21 reference documents the property, not a setter). Nothing was changed.");
    }

    private static void Assign(PropertyInfo property, object target, object value)
    {
        try
        {
            property.SetValue(target, value, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object? Get(object target, string name)
    {
        var property = LibraryRuntimeBinding.Property(target.GetType(), name, out var error)
            ?? throw Missing($"{error} Nothing was changed.");
        try
        {
            return property.GetValue(target, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static List<object?> Elements(object? collection, string documented)
        => collection is IEnumerable enumerable and not string ? enumerable.Cast<object?>().ToList()
            : throw Missing($"{documented} is not a collection.");

    private static string? Text(object? value) => value?.ToString();

    private static Type? Element(Type parameter)
        => parameter.IsGenericType && parameter.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? parameter.GetGenericArguments()[0] : null;

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Signature(MethodInfo method)
        => $"{(method.ReturnType == typeof(void) ? "void" : method.ReturnType.Name)} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    private static RuntimeBindingException Missing(string message) => new(WorkerFailureCategories.ApiMemberMissing, message);

    private static T Files<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (PlcAlarmException exception)
        {
            throw new RuntimeBindingException(exception.Category, exception.Message);
        }
    }

    private static void Files(Action run) => Files<object?>(() => { run(); return null; });
}
