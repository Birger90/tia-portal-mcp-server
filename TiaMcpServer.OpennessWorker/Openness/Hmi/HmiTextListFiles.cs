// The YAML route (ExportYaml, ImportYaml) is derived from rickgaiser/TiaMcp src/TiaMcp.Openness/TiaConnection.HmiTextLists.cs
// at 5bcb2ab (AGPL-3.0-only). See THIRD_PARTY_NOTICES.md.
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// export_text_lists / import_text_lists of WinCC Unified user text lists (owner request 2026-11-01). Neither the
/// reference stubs nor the Siemens Openness skills document a file export or import for Unified text lists: the members
/// used are a hypothesis following the SimaticML pattern of the documented compositions (<c>Export(FileInfo,
/// ExportOptions)</c> on an object, <c>Import(FileInfo, ImportOptions)</c> on its composition). Both are bound at runtime
/// by exact name and argument types on the live type, before anything is written or changed; an installation without
/// them fails <c>api_member_missing</c>. Only <c>HmiSoftware.HmiTextLists</c> is touched: system text lists and graphic
/// lists never (V21 pitfall 3: graphic list exports are reported to crash TIA Portal). An export goes to a private
/// temporary file and is placed with <see cref="TechnologyObjectFiles.Place(byte[], string, bool, string, List{string})"/>;
/// an import reads a private copy of the caller's file.
/// <para>
/// YAML route (origin: rickgaiser/TiaMcp at 5bcb2ab, <c>src/TiaMcp.Openness/TiaConnection.HmiTextLists.cs</c>,
/// AGPL-3.0-only, Copyright (c) Rick Gaiser; see THIRD_PARTY_NOTICES.md): rickgaiser's code, compiled against the V21
/// assemblies, calls <c>the composition's Export(DirectoryInfo, String)</c> (returns the written files: TIA Portal
/// adds <c>.hmi.yml</c> and <c>.TextLibrary.hmi.yml</c> to the name) and <c>Import(DirectoryInfo, String)</c> (returns
/// a Boolean) for all lists of the HMI at once. When the SimaticML members above are missing, export uses that route,
/// and an import of a <c>&lt;name&gt;.hmi.yml</c> file (with its <c>&lt;name&gt;.TextLibrary.hmi.yml</c> beside it)
/// always does. Both are bound at runtime too; their behaviour on V21 is reported by rickgaiser, not verified here.
/// </para>
/// </summary>
public static class HmiTextListFiles
{
    public const string ExportMember = "Export(FileInfo, ExportOptions) on HmiTextList";
    public const string ImportMember = "Import(FileInfo, ImportOptions) on HmiTextListComposition";
    public const string YamlExportMember = "Export(DirectoryInfo, String) on HmiTextListComposition";
    public const string YamlImportMember = "Import(DirectoryInfo, String) on HmiTextListComposition";

    /// <summary>The name passed to the YAML Export/Import; TIA Portal adds the suffixes (rickgaiser).</summary>
    internal const string YamlBaseName = "textlists";
    internal const string YamlListsSuffix = ".hmi.yml";
    internal const string YamlLibrarySuffix = ".TextLibrary.hmi.yml";

    public static HmiWriteOutcomeInfo ExportLists(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var directory = LocalPath(write.DirectoryPath, "directoryPath");
        if (!Directory.Exists(directory))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory '{directory}' does not exist. Nothing was written.");
        var lists = software.HmiTextLists.Cast<HmiTextList>().ToList();
        var selected = write.TextListNames is { Count: > 0 } names
            ? names.Select(name => lists.FirstOrDefault(list => string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"HMI '{software.Name}' has no user text list '{name}'. Nothing was written.")).ToList()
            : lists;
        if (selected.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"HMI '{software.Name}' has no user text list. Nothing was written.");
        var perList = selected.Select(list => LibraryRuntimeBinding.Find(list.GetType(), "Export", new[] { typeof(FileInfo), typeof(ExportOptions) }, out _)).ToList();
        if (perList.Any(method => method is null)
            && LibraryRuntimeBinding.Find(software.HmiTextLists.GetType(), "Export", new[] { typeof(DirectoryInfo), typeof(string) }, out _) is { } yamlExport)
            return ExportYaml(software, write, directory, yamlExport);
        var targets = selected.Select(list => (List: list, Path: Target(directory, list.Name))).ToList();
        foreach (var (_, path) in targets)
        {
            if (Directory.Exists(path)) throw Invalid($"'{path}' is a directory, not a file. Nothing was written.");
            if (File.Exists(path) && write.Overwrite != true) throw Invalid($"'{path}' already exists; set overwrite:true to replace it. Nothing was written.");
        }

        // Bound on every list before the first export, so a missing member writes nothing.
        var bound = targets.Select(t => (t.List, t.Path, Method: Bind(t.List, "Export", new[] { typeof(FileInfo), typeof(ExportOptions) }, ExportMember))).ToList();
        var outcome = new HmiWriteOutcomeInfo { ObjectName = software.Name };
        var warnings = new List<string>();
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-hmi-text-lists-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var (list, path, method) in bound)
            {
                var exported = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".xml");
                Invoke(method, list, new FileInfo(exported), ExportOptions.None);
                if (!File.Exists(exported))
                    throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                        $"TIA Portal reported no error but produced no export file for text list '{list.Name}'. Files already written: {Written(outcome)}.");
                var bytes = File.ReadAllBytes(exported);
                if (TechnologyObjectFiles.Place(bytes, path, write.Overwrite == true, $"The export of text list '{list.Name}'", warnings) is { } failure)
                    throw new WorkerOperationException(failure.Category, $"{failure.Message} Files already written: {Written(outcome)}.");
                outcome.AppliedProperties.Add($"textList {list.Name} -> {path} bytes={bytes.LongLength} sha256={TechnologyObjectFiles.Sha256(bytes)}");
            }
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        outcome.AppliedProperties.AddRange(warnings.Select(w => "warning " + w));
        return outcome;
    }

    public static HmiWriteOutcomeInfo ImportLists(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var source = LocalPath(write.FilePath, "filePath");
        if (!File.Exists(source)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{source}' does not exist. Nothing was imported.");
        if (source.EndsWith(YamlListsSuffix, StringComparison.OrdinalIgnoreCase)) return ImportYaml(software, write, source);
        var composition = software.HmiTextLists;
        var method = Bind(composition, "Import", new[] { typeof(FileInfo), typeof(ImportOptions) }, ImportMember);
        var before = Names(software);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = software.Name };
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-hmi-text-list-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var copy = Path.Combine(temporary, "textlists.xml");
            File.Copy(source, copy);
            var bytes = File.ReadAllBytes(copy);
            outcome.AppliedProperties.Add($"import {source} bytes={bytes.LongLength} sha256={TechnologyObjectFiles.Sha256(bytes)} options={(write.Overwrite == true ? "Override" : "None")}");
            Invoke(method, composition, new FileInfo(copy), write.Overwrite == true ? ImportOptions.Override : ImportOptions.None);
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        var after = Names(software);
        var created = after.Where(name => !before.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (created.Count == 0 && write.Overwrite != true)
            throw HmiWriteRun.Failure(new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"The import reported no error but no new text list appeared in HMI '{software.Name}'. Inspect it with hmi_read list_text_and_graphic_lists."), outcome);
        outcome.AppliedProperties.AddRange(created.Select(name => "textList " + name));
        if (created.Count == 0) outcome.AppliedProperties.Add("(no new text list: with overwrite an existing list was replaced; which one is not reported by TIA Portal)");
        return outcome;
    }

    /// <summary>
    /// All user text lists through <c>the composition's Export(DirectoryInfo, String)</c> into a private directory,
    /// then every file it reports placed into <paramref name="directory"/> under its own name (overwrite rules as above)
    /// and hashed. The route exports every list: <c>textListNames</c> cannot select and is refused.
    /// </summary>
    private static HmiWriteOutcomeInfo ExportYaml(HmiSoftware software, HmiWriteRequestInfo write, string directory, MethodInfo export)
    {
        if (write.TextListNames is { Count: > 0 })
            throw Invalid($"This TIA Portal installation exports text lists only all at once ({YamlExportMember}); omit textListNames. Nothing was written.");
        var outcome = new HmiWriteOutcomeInfo { ObjectName = software.Name };
        outcome.AppliedProperties.Add("route yaml " + YamlExportMember);
        var warnings = new List<string>();
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-hmi-text-lists-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var files = Call(export, software.HmiTextLists, new DirectoryInfo(temporary), YamlBaseName) is System.Collections.IEnumerable reported
                ? reported.OfType<FileInfo>().Select(f => f.FullName).ToList()
                : Directory.GetFiles(temporary).ToList();
            files = files.Where(File.Exists).ToList();
            if (!files.Any(f => f.EndsWith(YamlLibrarySuffix, StringComparison.OrdinalIgnoreCase)) || !files.Any(f => !f.EndsWith(YamlLibrarySuffix, StringComparison.OrdinalIgnoreCase)))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal's text list export gave unexpected files ({(files.Count == 0 ? "none" : string.Join(", ", files.Select(Path.GetFileName)))}); expected {YamlBaseName}{YamlListsSuffix} and {YamlBaseName}{YamlLibrarySuffix}. Nothing was written.");
            foreach (var file in files)
            {
                var path = Path.Combine(directory, Path.GetFileName(file));
                if (Directory.Exists(path)) throw Invalid($"'{path}' is a directory, not a file. Files already written: {Written(outcome)}.");
                if (File.Exists(path) && write.Overwrite != true) throw Invalid($"'{path}' already exists; set overwrite:true to replace it. Files already written: {Written(outcome)}.");
            }

            foreach (var file in files)
            {
                var path = Path.Combine(directory, Path.GetFileName(file));
                var bytes = File.ReadAllBytes(file);
                if (TechnologyObjectFiles.Place(bytes, path, write.Overwrite == true, $"The text list export file '{Path.GetFileName(file)}'", warnings) is { } failure)
                    throw new WorkerOperationException(failure.Category, $"{failure.Message} Files already written: {Written(outcome)}.");
                outcome.AppliedProperties.Add($"file {path} bytes={bytes.LongLength} sha256={TechnologyObjectFiles.Sha256(bytes)}");
            }
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        outcome.AppliedProperties.AddRange(Names(software).Select(name => "textList " + name));
        outcome.AppliedProperties.AddRange(warnings.Select(w => "warning " + w));
        return outcome;
    }

    /// <summary>
    /// A <c>&lt;name&gt;.hmi.yml</c> file and its <c>&lt;name&gt;.TextLibrary.hmi.yml</c> sibling, copied privately under the
    /// base name <c>textlists</c>, imported with <c>HmiTextListComposition.Import(DirectoryInfo, String)</c>. A false
    /// return fails the item. Whether TIA Portal merges or replaces lists and entries is undocumented (rickgaiser), so the
    /// result lists the user text lists before and after; inspect entries with hmi_read.
    /// </summary>
    private static HmiWriteOutcomeInfo ImportYaml(HmiSoftware software, HmiWriteRequestInfo write, string source)
    {
        if (source.EndsWith(YamlLibrarySuffix, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Give the lists file <name>{YamlListsSuffix}, not the text library <name>{YamlLibrarySuffix}. Nothing was imported.");
        var baseName = Path.GetFileName(source).Substring(0, Path.GetFileName(source).Length - YamlListsSuffix.Length);
        var library = Path.Combine(Path.GetDirectoryName(source)!, baseName + YamlLibrarySuffix);
        if (!File.Exists(library))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The text library '{library}' that belongs to '{source}' does not exist. Nothing was imported.");
        var composition = software.HmiTextLists;
        var method = Bind(composition, "Import", new[] { typeof(DirectoryInfo), typeof(string) }, YamlImportMember);
        var before = Names(software);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = software.Name };
        outcome.AppliedProperties.Add("route yaml " + YamlImportMember);
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-hmi-text-list-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var (from, suffix) in new[] { (source, YamlListsSuffix), (library, YamlLibrarySuffix) })
            {
                var copy = Path.Combine(temporary, YamlBaseName + suffix);
                File.Copy(from, copy);
                var bytes = File.ReadAllBytes(copy);
                outcome.AppliedProperties.Add($"import {from} bytes={bytes.LongLength} sha256={TechnologyObjectFiles.Sha256(bytes)}");
            }

            if (Call(method, composition, new DirectoryInfo(temporary), YamlBaseName) is false)
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, "TIA Portal's text list import returned false, without a reason.");
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            throw HmiWriteRun.Failure(ex, outcome);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        var after = Names(software);
        outcome.AppliedProperties.AddRange(after.Where(name => !before.Contains(name, StringComparer.OrdinalIgnoreCase)).Select(name => "textList " + name + " (new)"));
        outcome.AppliedProperties.Add("textLists after import: " + (after.Count == 0 ? "none" : string.Join(", ", after)));
        return outcome;
    }

    private static object? Call(MethodInfo method, object target, params object[] arguments)
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

    private static List<string> Names(HmiSoftware software) => software.HmiTextLists.Cast<HmiTextList>().Select(list => list.Name).ToList();

    /// <summary>The file of one list: its name as the file name, refused when it is not a valid file name segment.</summary>
    private static string Target(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0
            || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
            throw Invalid($"Text list '{name}' has a name that is not a valid file name; export the other lists by textListNames. Nothing was written.");
        return Path.Combine(directory, name + ".xml");
    }

    private static string LocalPath(string? path, string field)
    {
        var (full, refusal) = TechnologyObjectPolicy.ResolveLocalFile(path, Path.GetFullPath);
        return full ?? throw Invalid(refusal!.Replace("filePath", field));
    }

    private static string Written(HmiWriteOutcomeInfo outcome) => outcome.AppliedProperties.Count == 0 ? "none" : string.Join("; ", outcome.AppliedProperties);

    private static MethodInfo Bind(object target, string name, Type[] argumentTypes, string member)
        => LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"{member} is not available on {target.GetType().FullName} ({error}); no Siemens document describes it for WinCC Unified text lists. Nothing was changed.");

    private static void Invoke(MethodInfo method, object target, params object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static WorkerOperationException Invalid(string message) => new(WorkerFailureCategories.ValidationError, message);
}
