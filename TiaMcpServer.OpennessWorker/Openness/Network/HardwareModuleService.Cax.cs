using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// network_write export_cax and import_cax (GitHub issue #9, not live-tested). Evidence: Siemens.Engineering.Step7.xml (V21)
/// documents <c>CaxProvider.Export</c>/<c>Import</c> with their overloads, <c>CaxImportOptions</c>, <c>TransferResult</c> and
/// <c>TransferResultMessage</c>, but not which object offers the provider; it is taken from the project's <c>GetServiceInfos()</c>
/// by exact full name (not listed or not returned: unsupported_capability). None of it is in the compile stubs: the overload is
/// bound by exact name and the documented parameter types (<c>Device</c> or <c>ProjectBase</c>, <c>FileInfo</c>, optional log
/// <c>FileInfo</c>, the runtime <c>CaxImportOptions</c> enum), the result is projected by the Siemens-free <see cref="CaxRun"/>.
/// A <c>TransferResult</c> with state <c>Error</c>, or <c>false</c> from an overload with a log file, is TIA Portal's rejection
/// (engineering_rejected). An export is verified by re-reading and hashing the file; an import by comparing the project's device
/// names before and after (the imported hardware itself is not read back).
/// </summary>
internal static partial class HardwareModuleService
{
    public static HardwareCaxExportResultInfo ExportCax(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var path = RequireText(request.FilePath, "FilePath");
        var overwrite = request.Overwrite == true;
        var log = request.LogFilePath;
        // Security review F4: the file and the log are refused when they exist (unless overwrite:true), the directories are
        // inspected for links and network locations, and the export is staged next to the target and moved into place.
        if (PlcFileTargets.WriteTargetError(path, overwrite) is { } fileError) throw Validation($"export_cax: {fileError}");
        CheckLog(log, overwrite, "export_cax");
        Inspect(path);
        var provider = CaxProviderOf(project);
        var result = new HardwareCaxExportResultInfo
        {
            Scope = request.DeviceName is null ? CaxNames.Scopes.Project : CaxNames.Scopes.Device,
            DeviceName = request.DeviceName,
        };
        object subject;
        Type subjectType;
        if (request.DeviceName is { } deviceName)
        {
            var device = Resolve(project, new HardwareObjectSelectorInfo { DeviceName = deviceName }, "device");
            subject = device.Object;
            subjectType = typeof(Device);
            result.DeviceName = device.Identity.DeviceName;
        }
        else
        {
            subject = project;
            subjectType = typeof(ProjectBase);
        }

        var subjectName = subjectType == typeof(Device) ? "Device" : "ProjectBase";
        result.Overload = log is null ? $"CaxProvider.Export({subjectName}, FileInfo)" : $"CaxProvider.Export({subjectName}, FileInfo, FileInfo)";
        var staging = Path.Combine(Path.GetDirectoryName(path)!, ".tiamcp-cax-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var keepStaging = false;
        try
        {
            var staged = Path.Combine(staging, Path.GetFileName(path));
            var returned = Translate(() => log is null
                ? RuntimeServiceValues.Invoke(provider, "Export", new[] { subjectType, typeof(FileInfo) }, new object?[] { subject, new FileInfo(staged) }, result.Overload)
                : RuntimeServiceValues.Invoke(provider, "Export", new[] { subjectType, typeof(FileInfo), typeof(FileInfo) }, new object?[] { subject, new FileInfo(staged), new FileInfo(log) }, result.Overload));
            Outcome(returned, result.Overload, transfer => result.Transfer = transfer, succeeded => result.Succeeded = succeeded);
            if (log is not null) result.Log = CaxRun.File(log);
            RejectFailure("export_cax", result.Transfer, result.Succeeded, result.Log);
            if (!File.Exists(staged))
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"export_cax: {result.Overload} reported success, but it wrote no file. '{path}' was not changed.");
            var companions = Directory.EnumerateFileSystemEntries(staging).Where(entry => !string.Equals(entry, staged, StringComparison.OrdinalIgnoreCase)).ToList();
            if (companions.Count > 0)
            {
                // TIA Portal wrote more than the one file: they are kept (and named) instead of being deleted with the staging folder.
                keepStaging = true;
                Console.Error.WriteLine($"export_cax: TIA Portal wrote {companions.Count} further file(s) next to the export; they are kept in '{staging}'.");
            }

            var bytes = File.ReadAllBytes(staged);
            PlcCommit commit;
            try
            {
                commit = PlcFileTargets.Commit(staged, path, overwrite);
            }
            catch (PlcFileCommitException exception)
            {
                keepStaging = true;
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"export_cax: the export could not replace '{path}': {exception.Message}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                var exists = !overwrite && File.Exists(path);
                throw new WorkerOperationException(exists ? WorkerFailureCategories.ValidationError : WorkerFailureCategories.WorkerOperationFailed,
                    $"export_cax: the export could not be moved to '{path}': {exception.Message}" + (exists ? " The file exists now; pass overwrite:true to replace it." : string.Empty));
            }

            try
            {
                PlcFileTargets.VerifyCommitted(path, PlcFileTargets.Sha256Hex(bytes), commit);
            }
            catch (PlcFileNotVerifiedException exception)
            {
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"export_cax: {exception.Message}");
            }

            result.File = CaxRun.File(path);
            if (!result.File.Exists)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"export_cax: {result.Overload} reported success, but '{path}' does not exist afterwards.");
            return result;
        }
        finally
        {
            if (!keepStaging)
            {
                try
                {
                    Directory.Delete(staging, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"export_cax: the staging folder '{staging}' could not be deleted: {exception.Message}");
                }
            }
        }
    }

    /// <summary>An existing log file is replaced only with overwrite:true; its directory must exist (security review F4).</summary>
    private static void CheckLog(string? log, bool overwrite, string operation)
    {
        if (log is null) return;
        if (PlcFileTargets.WriteTargetError(log, overwrite) is { } logError) throw Validation($"{operation}: logFilePath {logError}");
        Inspect(log);
    }

    /// <summary>The link, junction and network-location check of the other file exports: refused or reported on stderr.</summary>
    private static void Inspect(string path)
    {
        var location = PlcFileTargets.InspectDirectory(Path.GetDirectoryName(path)!);
        if (location.Refusal is { } refusal) throw Validation(refusal);
        if (location.Warning is { } warning) Console.Error.WriteLine(warning);
    }

    public static HardwareCaxImportResultInfo ImportCax(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var path = RequireText(request.FilePath, "FilePath");
        if (!File.Exists(path)) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"import_cax: '{path}' does not exist. Nothing was changed.");
        var optionName = RequireText(request.CaxImportOption, "ImportOption");
        var provider = CaxProviderOf(project);
        var optionType = TechnologyObjectRuntime.EnumType(provider, CaxNames.ImportOptionsType);
        var option = LibraryRuntimeBinding.EnumValue(optionType, optionName)
            ?? throw Validation($"importOption '{optionName}' is not a member of {CaxNames.ImportOptionsType} on this installation ({LibraryRuntimeBinding.MemberList(optionType)}).");
        var log = request.LogFilePath;
        CheckLog(log, request.Overwrite == true, "import_cax");
        var result = new HardwareCaxImportResultInfo
        {
            FilePath = path, ImportOption = optionName,
            Overload = log is null ? "CaxProvider.Import(FileInfo, CaxImportOptions)" : "CaxProvider.Import(FileInfo, FileInfo, CaxImportOptions)",
            DevicesBefore = DeviceNames(project),
        };
        var returned = Translate(() => log is null
            ? RuntimeServiceValues.Invoke(provider, "Import", new[] { typeof(FileInfo), optionType }, new object?[] { new FileInfo(path), option }, result.Overload)
            : RuntimeServiceValues.Invoke(provider, "Import", new[] { typeof(FileInfo), typeof(FileInfo), optionType }, new object?[] { new FileInfo(path), new FileInfo(log), option }, result.Overload));
        Outcome(returned, result.Overload, transfer => result.Transfer = transfer, succeeded => result.Succeeded = succeeded);
        if (log is not null) result.Log = CaxRun.File(log);
        result.DevicesAfter = DeviceNames(project);
        result.AddedDevices = CaxRun.Added(result.DevicesBefore, result.DevicesAfter);
        result.RemovedDevices = CaxRun.Added(result.DevicesAfter, result.DevicesBefore);
        RejectFailure("import_cax", result.Transfer, result.Succeeded, result.Log,
            $" The project may already have changed (devices added: {List(result.AddedDevices)}; removed: {List(result.RemovedDevices)}).");
        return result;
    }

    private static object CaxProviderOf(ProjectBase project)
    {
        var offered = TechnologyObjectRuntime.ServiceTypes(project);
        var (provider, unavailable) = Translate(() => RuntimeServiceValues.Find(offered,
            type => TechnologyObjectRuntime.TryService(project, offered, type.FullName!, out _), CaxNames.Provider));
        return provider ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            (unavailable ?? RuntimeServiceValues.NotListed("The project", CaxNames.Provider)) + " Nothing was changed.");
    }

    /// <summary>A <c>Boolean</c> (overloads with a log file) or a <c>TransferResult</c> (without).</summary>
    private static void Outcome(object? returned, string overload, Action<CaxTransferResultInfo> transfer, Action<bool> succeeded)
    {
        switch (returned)
        {
            case bool flag:
                succeeded(flag);
                break;
            case null:
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{overload} returned null; the outcome is unknown.");
            default:
                transfer(CaxRun.Read(returned, Recoverable));
                break;
        }
    }

    private static void RejectFailure(string operation, CaxTransferResultInfo? transfer, bool? succeeded, CaxFileInfo? log, string suffix = "")
    {
        var messages = transfer is null ? new List<string>()
            : CaxRun.Flatten(transfer.Messages).Select(message => $"{message.State.Value ?? "?"}: {message.Message.Value}").Take(50).ToList();
        if (transfer is not null && CaxRun.Failed(transfer))
            throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected,
                $"TIA Portal rejected the operation: {operation}: {CaxRun.Summary(transfer)}{suffix}", messages);
        if (succeeded == false)
            throw new WorkerOperationException(WorkerFailureCategories.EngineeringRejected,
                $"TIA Portal rejected the operation: {operation}: the overload with a log file returned false; see the log '{log?.Path}'"
                + (log is { Exists: true } ? $" ({log.Length} bytes)." : " (it was not written).") + suffix, messages);
    }

    private static List<string> DeviceNames(ProjectBase project)
        => ProjectDeviceEnumerator.Enumerate(project).Select(device => device.Name ?? string.Empty).ToList();

    private static string List(List<string> names) => names.Count == 0 ? "none" : string.Join(", ", names.Select(name => $"'{name}'"));
}
