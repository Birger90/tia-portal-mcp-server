using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.SW.Alarm;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Supervision;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcAlarms;

/// <summary>
/// The export and import of one workbook kind, resolved and bound before anything is called: the PLC or ProDiag FB, the
/// provider service, the languages, the enum members, the documented provider member (compile-time stubs, or exact
/// runtime lookup by name and documented argument types where the return type is undocumented) and the result members
/// (<see cref="PlcAlarmResultReader.Bind"/> on the declared return type). A member or result State that does not bind
/// fails closed with unsupported_capability before the first export or import. <c>plc_alarm_plan</c> runs the same
/// resolution without calling anything, so a dryRun refuses what the mutation would.
/// </summary>
internal sealed class PlcAlarmTransfers
{
    private const string AlarmTextExport = "PlcAlarmTextProvider.ExportInstanceTextsToXlsx(FileInfo, IEnumerable<Language>, PlcAlarmTextXlsxExportOption)";
    private const string AlarmTextImport = "PlcAlarmTextProvider.ImportInstanceTextsFromXlsx(FileInfo, IEnumerable<Language>)";

    public string Kind { get; private set; } = string.Empty;

    public PlcSoftwareLocator.DiscoveredPlcSoftware? Plc { get; private set; }

    public string? BlockPath { get; private set; }

    public string? ProgrammingLanguage { get; private set; }

    public int? SupervisionCount { get; private set; }

    /// <summary>The cultures passed to the export or import; empty for the unfiltered text list export.</summary>
    public List<string> Cultures { get; } = new();

    /// <summary>Text lists the filtered export names, as TIA Portal spells them; null for every list.</summary>
    public List<string>? TextListNames { get; private set; }

    public string? ExportOption { get; private set; }

    public string? ImportOption { get; private set; }

    /// <summary>Exports the kind's workbook (null when the request gives nothing to export with, e.g. import_alarm_texts without exportOption).</summary>
    public PlcAlarmWorkbookRun.Transfer? Export { get; private set; }

    public PlcAlarmWorkbookRun.Transfer? Import { get; private set; }

    /// <summary>The bound members, "Provider.Member(arguments) -> Result.State/...", for the plan record.</summary>
    public List<string> Bound { get; } = new();

    /// <summary>Warnings found while resolving (a culture that is not active in the project).</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Resolves the workbook kind of <paramref name="method"/> (a read, cell update, export or import).</summary>
    public static PlcAlarmTransfers For(ProjectBase project, string method, PlcAlarmRequestInfo request)
    {
        var kind = PlcAlarmMethods.WorkbookKind(method) ?? throw Invalid($"'{method}' exports or imports no workbook.");
        var transfers = new PlcAlarmTransfers { Kind = kind };
        var exports = PlcAlarmMethods.TemporaryExportMethods.Contains(method) || PlcAlarmMethods.CellUpdates.Contains(method) || PlcAlarmMethods.FileExports.Contains(method);
        var imports = PlcAlarmMethods.CellUpdates.Contains(method) || PlcAlarmMethods.FileImports.Contains(method);
        switch (kind)
        {
            case PlcAlarmPolicy.WorkbookKinds.TextLists:
                transfers.TextLists(project, method, request, exports, imports);
                break;
            case PlcAlarmPolicy.WorkbookKinds.AlarmTexts:
                transfers.AlarmTexts(project, method, request, exports, imports);
                break;
            case PlcAlarmPolicy.WorkbookKinds.Supervisions:
            case PlcAlarmPolicy.WorkbookKinds.SupervisionSettings:
                transfers.Supervisions(project, method, request, exports, imports);
                break;
            case PlcAlarmPolicy.WorkbookKinds.AlarmClasses:
                transfers.AlarmClasses(project, method == PlcAlarmMethods.ExportAlarmClasses, method == PlcAlarmMethods.ImportAlarmClasses);
                break;
            case PlcAlarmPolicy.WorkbookKinds.SystemDiagnosticsSettings:
                transfers.SystemDiagnosticsSettings(project, method == PlcAlarmMethods.ImportSystemDiagnosticsSettings);
                break;
            default:
                throw Invalid($"'{method}' exports or imports no workbook.");
        }

        return transfers;
    }

    private void TextLists(ProjectBase project, string method, PlcAlarmRequestInfo request, bool exports, bool imports)
    {
        var plc = Plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var provider = plc.Software.GetService<PlcAlarmTextListProvider>() ?? throw Unsupported($"PLC '{plc.Software.Name}' offers no PlcAlarmTextListProvider.");
        var binding = Bind(typeof(TextListXlsxResult), withCounts: false, "PlcAlarmTextListProvider export/import");
        if (exports || method == PlcAlarmMethods.ImportTextLists)
        {
            // The verification export after import_text_lists is the unfiltered one: the imported file may hold any list.
            // A cell update always uses the filtered export with user list names, so it never imports a system list's rows again.
            var cellUpdate = method == PlcAlarmMethods.UpdateTextListCells;
            if (cellUpdate && request.TextListNames is null) throw Invalid(PlcAlarmPolicy.CellUpdatesNeedTextListNames);
            var filtered = cellUpdate || method != PlcAlarmMethods.ImportTextLists && (request.TextListNames is not null || request.Cultures is not null);
            if (!filtered)
            {
                Export = path => PlcAlarmResultReader.Read(provider.ExportToXlsx(new FileInfo(path)), binding);
                Bound.Add($"PlcAlarmTextListProvider.ExportToXlsx(FileInfo) -> {binding.Describe()}");
            }
            else
            {
                if (request.TextListNames is null) throw Invalid(PlcAlarmPolicy.CulturesNeedTextListNames);
                // F1: a cell update imports the workbook again, so it never names a system list; reads and exports may.
                var names = TextListNames = PlcTextListResolver.ResolveNames(plc, request.TextListNames, refuseSystem: cellUpdate);
                var languages = Languages(project, request.Cultures);
                Export = path => PlcAlarmResultReader.Read(provider.ExportToXlsx(new FileInfo(path), names, languages), binding);
                Bound.Add($"PlcAlarmTextListProvider.ExportToXlsx(FileInfo, IEnumerable<string>, IEnumerable<Language>) -> {binding.Describe()}");
            }
        }

        if (imports)
        {
            var options = ImportOptionValue(request.ImportOption);
            ImportOption = options.ToString();
            Import = path => PlcAlarmResultReader.Read(provider.ImportFromXlsx(new FileInfo(path), options), binding);
            Bound.Add($"PlcAlarmTextListProvider.ImportFromXlsx(FileInfo, ImportOptions.{ImportOption}) -> {binding.Describe()}");
        }
    }

    private void AlarmTexts(ProjectBase project, string method, PlcAlarmRequestInfo request, bool exports, bool imports)
    {
        var plc = Plc = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var provider = plc.Software.GetService<PlcAlarmTextProvider>() ?? throw Unsupported($"PLC '{plc.Software.Name}' offers no PlcAlarmTextProvider.");
        var languages = Languages(project, request.Cultures);
        // import_alarm_texts takes an optional exportOption only for its verification export.
        if (exports || method == PlcAlarmMethods.ImportAlarmTexts && request.ExportOption is not null)
        {
            var option = ExportOptionValue(request.ExportOption);
            ExportOption = option.ToString();
            var export = Method(provider, "ExportInstanceTextsToXlsx", new[] { typeof(FileInfo), typeof(IEnumerable<Language>), typeof(PlcAlarmTextXlsxExportOption) }, AlarmTextExport);
            var binding = Bind(export.ReturnType, withCounts: false, AlarmTextExport);
            Export = path => PlcAlarmResultReader.Read(Call(export, provider, new object?[] { new FileInfo(path), languages, option }), binding);
            Bound.Add($"{AlarmTextExport} -> {binding.Describe()}");
        }

        if (imports)
        {
            var import = Method(provider, "ImportInstanceTextsFromXlsx", new[] { typeof(FileInfo), typeof(IEnumerable<Language>) }, AlarmTextImport);
            var binding = Bind(import.ReturnType, withCounts: false, AlarmTextImport);
            Import = path => PlcAlarmResultReader.Read(Call(import, provider, new object?[] { new FileInfo(path), languages }), binding);
            Bound.Add($"{AlarmTextImport} -> {binding.Describe()}");
        }
    }

    private void Supervisions(ProjectBase project, string method, PlcAlarmRequestInfo request, bool exports, bool imports)
    {
        var (provider, blockPath, fb) = ResolveSupervisions(project, request.BlockPath);
        BlockPath = blockPath;
        (ProgrammingLanguage, SupervisionCount) = PlcProDiagService.Evidence(fb);
        var binding = Bind(typeof(SupervisionXlsxResult), withCounts: false, "SupervisionProvider export/import");
        // The verification export after import_supervisions; supervision settings have no documented export.
        if (exports || method == PlcAlarmMethods.ImportSupervisions)
        {
            Export = path => PlcAlarmResultReader.Read(provider.ExportSupervisionsToXlsx(new FileInfo(path)), binding);
            Bound.Add($"SupervisionProvider.ExportSupervisionsToXlsx(FileInfo) -> {binding.Describe()}");
        }

        if (imports)
        {
            var options = ImportOptionValue(request.ImportOption);
            ImportOption = options.ToString();
            var settings = method == PlcAlarmMethods.ImportSupervisionSettings;
            Import = path => PlcAlarmResultReader.Read(settings
                ? provider.ImportSupervisionSettingsFromXlsx(new FileInfo(path), options)
                : provider.ImportSupervisionsFromXlsx(new FileInfo(path), options), binding);
            Bound.Add($"SupervisionProvider.{(settings ? "ImportSupervisionSettingsFromXlsx" : "ImportSupervisionsFromXlsx")}(FileInfo, ImportOptions.{ImportOption}) -> {binding.Describe()}");
        }
    }

    private void AlarmClasses(ProjectBase project, bool exports, bool imports)
    {
        var provider = AlarmClassProvider(project);
        var binding = Bind(typeof(AlarmClassExportImportResult), withCounts: true, "AlarmClassDataProvider.Export/Import");
        // An import is verified by exporting the classes again and comparing the files.
        Export = path => PlcAlarmResultReader.Read(provider.Export(new FileInfo(path)), binding);
        Bound.Add($"AlarmClassDataProvider.Export(FileInfo) -> {binding.Describe()}");
        if (imports)
        {
            Import = path => PlcAlarmResultReader.Read(provider.Import(new FileInfo(path)), binding);
            Bound.Add($"AlarmClassDataProvider.Import(FileInfo) -> {binding.Describe()}");
        }
    }

    /// <summary>
    /// The project's system diagnostics settings (issue #19, not live-tested): <c>SystemdiagnosticsSettingsDataProvider.Export(FileInfo)</c>
    /// and <c>.Import(FileInfo)</c>, both returning <c>SystemdiagnosticsSettingsExportImportResult</c> (Siemens.Engineering.Base.xml, V21).
    /// The XML does not say which object offers the provider; it is taken from the Project's <c>GetServiceInfos()</c> by exact full
    /// name (not listed or not returned: unsupported_capability). Neither the provider nor the result type is in the compile stubs,
    /// so both methods are bound by exact name and the <c>FileInfo</c> parameter on the live provider, and <c>State</c> on the bound
    /// method's return type (<see cref="PlcAlarmResultReader.BindStateOnly"/>), all before anything is called. An import is verified
    /// by exporting again and comparing the files byte for byte, like the alarm classes.
    /// </summary>
    private void SystemDiagnosticsSettings(ProjectBase project, bool imports)
    {
        const string ProviderName = "Siemens.Engineering.HW.Systemdiagnostics.Settings.SystemdiagnosticsSettingsDataProvider";
        if (project is not global::Siemens.Engineering.Project)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "The system diagnostics settings are reached through the Project's services; the bound container is not a standalone project.");
        var offered = TechnologyObjects.TechnologyObjectRuntime.ServiceTypes(project);
        var (provider, unavailable) = Translate(() => TiaMcpServer.Contracts.Shared.RuntimeServiceValues.Find(offered,
            type => TechnologyObjects.TechnologyObjectRuntime.TryService(project, offered, type.FullName!, out _), ProviderName));
        if (provider is null)
            throw Unsupported(unavailable ?? TiaMcpServer.Contracts.Shared.RuntimeServiceValues.NotListed("The project", ProviderName));
        const string ExportDoc = "SystemdiagnosticsSettingsDataProvider.Export(FileInfo)";
        const string ImportDoc = "SystemdiagnosticsSettingsDataProvider.Import(FileInfo)";
        var export = Method(provider, "Export", new[] { typeof(FileInfo) }, ExportDoc);
        var exportBinding = BindState(export.ReturnType, ExportDoc);
        Export = path => PlcAlarmResultReader.Read(Call(export, provider, new object?[] { new FileInfo(path) }), exportBinding);
        Bound.Add($"{ExportDoc} -> {exportBinding.Describe()}");
        if (imports)
        {
            var import = Method(provider, "Import", new[] { typeof(FileInfo) }, ImportDoc);
            var importBinding = BindState(import.ReturnType, ImportDoc);
            Import = path => PlcAlarmResultReader.Read(Call(import, provider, new object?[] { new FileInfo(path) }), importBinding);
            Bound.Add($"{ImportDoc} -> {importBinding.Describe()}");
        }
    }

    private static PlcAlarmResultReader.Binding BindState(Type resultType, string documented)
    {
        var binding = PlcAlarmResultReader.BindStateOnly(resultType);
        return PlcAlarmResultReader.Refusal(binding, documented) is { } refusal ? throw Unsupported(refusal) : binding;
    }

    private static T Translate<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (TiaMcpServer.Contracts.Shared.RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }

    /// <summary>Source: <c>FB.GetService&lt;SupervisionProvider&gt;()</c> on a ProDiag FB.</summary>
    public static (SupervisionProvider Provider, string BlockPath, FB Fb) ResolveSupervisions(ProjectBase project, string? blockPath)
    {
        var fb = PlcProDiagService.ResolveFb(project, blockPath, "supervisions belong to ProDiag FBs");
        var provider = ((IEngineeringServiceProvider)fb).GetService<SupervisionProvider>();
        if (provider is null)
        {
            var (language, count) = PlcProDiagService.Evidence(fb);
            throw Unsupported($"FB '{blockPath}' offers no SupervisionProvider (it is not a ProDiag FB: ProgrammingLanguage {language ?? "(unreadable)"}, "
                + $"FB.Supervisions.Count {(count is { } n ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(unreadable)")}).");
        }

        return (provider, blockPath!, fb);
    }

    /// <summary>Source: <c>Project.GetService&lt;AlarmClassDataProvider&gt;()</c>.</summary>
    public static AlarmClassDataProvider AlarmClassProvider(ProjectBase project)
    {
        if (project is not global::Siemens.Engineering.Project)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                "The common alarm classes are reached through Project.GetService<AlarmClassDataProvider>(); the bound container is not a standalone project.");
        return project.GetService<AlarmClassDataProvider>() ?? throw Unsupported("The project offers no AlarmClassDataProvider.");
    }

    /// <summary>
    /// The Language of each culture, found in <c>LanguageSettings.Languages</c> (the catalogue, as the documented filtered
    /// <c>ExportToXlsx</c> sample finds them); every active project language when <paramref name="cultures"/> is null. A culture
    /// that is not active is passed on with a warning: what the export or import does with it is TIA Portal's decision.
    /// </summary>
    private List<Language> Languages(ProjectBase project, IReadOnlyList<string>? cultures)
    {
        var active = project.LanguageSettings.ActiveLanguages.ToList();
        if (cultures is null)
        {
            Cultures.AddRange(active.Select(language => language.Culture.Name));
            return active;
        }

        var catalogue = project.LanguageSettings.Languages.ToList();
        var resolved = new List<Language>();
        foreach (var culture in cultures)
        {
            var language = catalogue.Where(candidate => string.Equals(candidate.Culture.Name, culture, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (language.Count != 1)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"'{culture}' is not a TIA Portal language (LanguageSettings.Languages); the active project languages are {string.Join(", ", active.Select(l => l.Culture.Name))}. Nothing was exported or imported.");
            if (!active.Any(candidate => Equals(candidate, language[0])))
                Warnings.Add($"'{language[0].Culture.Name}' is not an active project language; TIA Portal decides what the export or import does with it.");
            resolved.Add(language[0]);
            Cultures.Add(language[0].Culture.Name);
        }

        return resolved;
    }

    private static PlcAlarmTextXlsxExportOption ExportOptionValue(string? name)
        => LibraryRuntimeBinding.EnumValue(typeof(PlcAlarmTextXlsxExportOption), name) is PlcAlarmTextXlsxExportOption option ? option
            : throw Invalid($"exportOption '{name}' is not a PlcAlarmTextXlsxExportOption member on this TIA Portal installation (members: "
                + $"{LibraryRuntimeBinding.MemberList(typeof(PlcAlarmTextXlsxExportOption))}). Nothing was exported.");

    private static ImportOptions ImportOptionValue(string? name)
    {
        var requested = name ?? PlcAlarmPolicy.DefaultImportOption;
        if (!PlcAlarmPolicy.ImportOptionNames.Contains(requested, StringComparer.Ordinal))
            throw Invalid($"importOption must be one of {string.Join(", ", PlcAlarmPolicy.ImportOptionNames)}.");
        return LibraryRuntimeBinding.EnumValue(typeof(ImportOptions), requested) is ImportOptions options ? options
            : throw Unsupported($"ImportOptions has no member '{requested}' on this TIA Portal installation (members: {LibraryRuntimeBinding.MemberList(typeof(ImportOptions))}).");
    }

    private static PlcAlarmResultReader.Binding Bind(Type resultType, bool withCounts, string documented)
    {
        var binding = PlcAlarmResultReader.Bind(resultType, withCounts);
        return PlcAlarmResultReader.Refusal(binding, documented) is { } refusal ? throw Unsupported(refusal) : binding;
    }

    /// <summary>The single applicable method by documented name and argument types, without calling it.</summary>
    public static MethodInfo Method(object target, string name, Type[] argumentTypes, string documented)
        => LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                (error ?? $"This TIA Portal installation does not expose {documented}.") + $" ({documented}) Nothing was changed.");

    /// <summary>Calls a bound method; a Siemens exception it throws is rethrown unwrapped.</summary>
    public static object? Call(MethodInfo method, object target, object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);

    private static WorkerOperationException Unsupported(string message) => new(WorkerFailureCategories.UnsupportedCapability, message + " Nothing was changed.");
}
