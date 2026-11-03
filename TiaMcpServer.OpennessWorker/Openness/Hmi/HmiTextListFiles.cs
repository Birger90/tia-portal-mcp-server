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
/// </summary>
public static class HmiTextListFiles
{
    public const string ExportMember = "Export(FileInfo, ExportOptions) on HmiTextList";
    public const string ImportMember = "Import(FileInfo, ImportOptions) on HmiTextListComposition";

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
