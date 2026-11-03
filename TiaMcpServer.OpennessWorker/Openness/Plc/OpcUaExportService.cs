// Origin: feature idea from heilingbrunner/tiaportal-mcp (MIT, Copyright (c) 2025 J.Heilingbrunner), commit f4eb4d7.
// Reimplemented from the Siemens V21 reference (OpcUaExportProvider.Export(DeviceItem, FileInfo), found through
// Project.HwUtilities.Find("OPCUAExportProvider")); no code copied. See THIRD_PARTY_NOTICES.md.
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.TechnologyObjects;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// export_opcua_xml: the OPC UA data of one PLC's CPU as an XML file at a caller path. <c>Project.HwUtilities</c> is read
/// by name (documented without a declaring type), the provider is <c>HwUtilities.Find("OPCUAExportProvider")</c> when a
/// public <c>Find(String)</c> exists, otherwise the one element of the composition whose type is exactly
/// <see cref="PlcOpcUaExport.ProviderTypeName"/>; <c>Export(DeviceItem, FileInfo)</c> is bound by exact name and parameter
/// types (<see cref="PlcOpcUaExport.BindExport"/>). Anything missing is <c>api_member_missing</c> and nothing is written.
/// The export goes to a private temporary file and is placed like the tag table export (staged next to the target,
/// moved into place, re-hashed). TIA Portal needs the PLC offline for this export (reported, not verified here).
/// </summary>
internal static class OpcUaExportService
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    public static PlcOpcUaExportResultInfo Export(ProjectBase project, PlcBlockEditRequestInfo request)
    {
        var discovered = PlcSoftwareLocator.FindUnique(project, request.PlcName);
        var cpu = discovered.HostDeviceItem
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                $"PLC '{discovered.Software.Name}' has no CPU device item hosting its software. Nothing was written.");
        var target = TechnologyObjectService.LocalFile(request.FilePath);
        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"The directory of '{target}' does not exist. Nothing was written.");
        if (Directory.Exists(target)) throw TechnologyObjectService.Invalid($"'{target}' is a directory, not a file. Nothing was written.");
        var overwrite = request.Overwrite == true;
        var existed = File.Exists(target);
        if (existed && !overwrite) throw TechnologyObjectService.Invalid($"'{target}' already exists; set overwrite:true to replace it. Nothing was written.");

        // Bound before anything is written.
        var provider = Provider(project);
        var (export, missing) = PlcOpcUaExport.BindExport(provider.GetType());
        if (export is null) throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, missing + " Nothing was written.");

        var messages = TechnologyObjectService.FileWarnings(target);
        var temporary = Path.Combine(Path.GetTempPath(), "tia-mcp-opcua-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        byte[] bytes;
        try
        {
            var exported = Path.Combine(temporary, "opcua.xml");
            Invoke(export, provider, cpu, new FileInfo(exported));
            if (!File.Exists(exported))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no OPC UA export file for PLC '{discovered.Software.Name}'. Nothing was written.");
            bytes = File.ReadAllBytes(exported);
        }
        finally
        {
            TemporaryDirectoryCleanup.TryDeleteTempDirectory(temporary);
        }

        if (TechnologyObjectFiles.Place(bytes, target, overwrite, $"The OPC UA export of PLC '{discovered.Software.Name}'", messages) is { } failure)
            throw new WorkerOperationException(failure.Category, failure.Message);
        return new PlcOpcUaExportResultInfo
        {
            PlcName = discovered.Software.Name, DeviceName = discovered.DeviceName, DeviceItemName = cpu.Name, FilePath = target,
            FileBytes = bytes.LongLength, FileSha256 = TechnologyObjectFiles.Sha256(bytes), Overwrite = existed, Messages = messages,
        };
    }

    private static object Provider(ProjectBase project)
    {
        var property = project.GetType().GetProperty("HwUtilities", PublicInstance)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                "The open project exposes no HwUtilities property, so OpcUaExportProvider is unavailable. Nothing was written.");
        var utilities = Unwrap(() => property.GetValue(project))
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, "Project.HwUtilities is null. Nothing was written.");

        var find = utilities.GetType().GetMethod("Find", PublicInstance, null, new[] { typeof(string) }, null);
        if (find is not null && Unwrap(() => find.Invoke(utilities, new object[] { PlcOpcUaExport.ProviderId })) is { } found)
            return PlcOpcUaExport.IsProvider(found.GetType())
                ? found
                : throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                    $"HwUtilities.Find(\"{PlcOpcUaExport.ProviderId}\") returned {found.GetType().FullName}, not {PlcOpcUaExport.ProviderTypeName}. Nothing was written.");

        var providers = OpennessReflection.Enumerate(utilities, "project hardware utilities").Where(item => PlcOpcUaExport.IsProvider(item.GetType())).ToArray();
        return providers.Length == 1
            ? providers[0]
            : throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"Expected exactly one {PlcOpcUaExport.ProviderTypeName} in Project.HwUtilities, found {providers.Length}. Nothing was written.");
    }

    private static void Invoke(MethodInfo export, object provider, object deviceItem, FileInfo file)
        => Unwrap(() => export.Invoke(provider, new[] { deviceItem, file }));

    private static object? Unwrap(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
