// The Openness calls of this file follow BetterTIA/watch, src/BtiaWatch/OpennessPortal.cs (ExportDbSnapshot),
// commit 1978f24b330f265d8ed2a67e9b1d673d1b4122d8, MIT License, Copyright (c) 2026 HonzaT; see THIRD_PARTY_NOTICES.md.
// Re-written for this worker: every member is bound at runtime by exact name and argument types.
using System.IO;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.Online;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// get_db_snapshot and create_db_snapshot. Evidence (BetterTIA/watch, live-checked there against V21 with a real PLC):
/// <c>ValueService</c> is a service of the DB's <c>Interface</c> and <c>CreateSnapshot()</c> takes the actual values of the
/// online PLC (offline it returns without error and stores nothing useful, so create refuses an offline PLC);
/// <c>InterfaceSnapshot</c> is a service of the block itself (null on the interface) and <c>Export(FileInfo,
/// ExportOptions.WithReadOnly)</c> writes the read-only attribute <c>Snapshot</c> (ExportOptions.None leaves it out).
/// Neither service type nor <c>WithReadOnly</c> is in the compile stubs or the Siemens skills: the services are found
/// among <c>GetServiceInfos()</c> by exact short name in a Siemens.Engineering namespace, the members bound by exact name
/// and argument types (<see cref="LibraryRuntimeBinding"/>), the enum member parsed by name; anything missing is
/// <c>api_member_missing</c> with nothing changed. The value-loading members of ValueService (<c>LoadSnapshotAsActualValues</c>,
/// <c>LoadStartValuesAsActualValues</c>) are bound only by plc_write's online operations of the same names (package G,
/// <see cref="ResolveValueService"/>, OnlineControl behind an acknowledge guard).
/// </summary>
internal static partial class PlcBlockEditService
{
    internal const string ValueServiceName = "ValueService";
    internal const string InterfaceSnapshotName = "InterfaceSnapshot";

    public static PlcDbSnapshotInfo GetDbSnapshot(ProjectBase project, PlcBlockEditRequestInfo request)
        => Snapshot(project, request, create: false);

    public static PlcDbSnapshotInfo CreateDbSnapshot(ProjectBase project, PlcBlockEditRequestInfo request)
        => Snapshot(project, request, create: true);

    private static PlcDbSnapshotInfo Snapshot(ProjectBase project, PlcBlockEditRequestInfo request, bool create)
    {
        var blockPath = Require(request.BlockPath, "blockPath");
        var address = ParseBlock(blockPath);
        var discovered = PlcSoftwareLocator.FindUnique(project, address.PlcName);
        var located = BlockLocator.Locate(discovered.Software, address);
        var db = RequireDataBlock(located);
        var maxValues = Math.Min(Math.Max(request.MaxElements ?? PlcDbSnapshotDocument.DefaultMaxValues, 1), PlcDbSnapshotDocument.MaxValues);
        var info = new PlcDbSnapshotInfo
        {
            Operation = create ? PlcBlockEditMethods.CreateDbSnapshot : PlcBlockEditMethods.GetDbSnapshot,
            Block = BlockLocator.Identity(discovered.Software.Name, located.CanonicalPath, located.Block),
            MemberPath = string.IsNullOrWhiteSpace(request.MemberPath) ? null : request.MemberPath!.Trim(),
        };

        // Both services are bound before anything is called, so a missing member changes nothing.
        var snapshot = RequireService(db, InterfaceSnapshotName, located.CanonicalPath, "the block");
        var export = Bind(snapshot, "Export", new[] { typeof(FileInfo), typeof(ExportOptions) });
        var withReadOnly = LibraryRuntimeBinding.EnumValue(typeof(ExportOptions), "WithReadOnly")
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"This TIA Portal installation has no ExportOptions.WithReadOnly (members: {LibraryRuntimeBinding.MemberList(typeof(ExportOptions))}); the snapshot values cannot be exported. Nothing was changed.");
        if (create)
        {
            var values = RequireService(db.Interface, ValueServiceName, located.CanonicalPath, "the block interface");
            var createSnapshot = Bind(values, "CreateSnapshot", Type.EmptyTypes);
            var provider = discovered.HostDeviceItem?.GetService<OnlineProvider>()
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                    $"The CPU of PLC '{discovered.Software.Name}' offers no OnlineProvider, so its online state is unknown; no snapshot was taken.");
            info.OnlineState = provider.State.ToString();
            if (info.OnlineState != "Online")
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"PLC '{discovered.Software.Name}' is '{info.OnlineState}', not online: an offline snapshot holds no actual values (BetterTIA/watch observed V21 returning without error and an empty snapshot). Go online with plc_tia_online go_online first. Nothing was changed.");
            Invoke(createSnapshot, values, Array.Empty<object>());
        }

        var directory = Path.Combine(Path.GetTempPath(), "tia-mcp-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string xml;
        try
        {
            var file = new FileInfo(Path.Combine(directory, "snapshot.xml"));
            Invoke(export, snapshot, new object[] { file, withReadOnly });
            if (!File.Exists(file.FullName))
                throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                    $"InterfaceSnapshot.Export returned without writing a file for '{located.CanonicalPath}'.");
            xml = File.ReadAllText(file.FullName);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        try
        {
            var reading = PlcDbSnapshotDocument.Read(xml, info.MemberPath, maxValues);
            info.HasSnapshot = reading.HasSnapshot;
            info.SnapshotDate = reading.SnapshotDate;
            info.TotalValues = reading.TotalValues;
            info.MatchedValues = reading.MatchedValues;
            info.Truncated = reading.Truncated;
            info.Values = reading.Values;
            info.TopLevelMembers = reading.TopLevelMembers;
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"'{located.CanonicalPath}': {exception.Message}");
        }

        if (!info.HasSnapshot)
            info.Messages.Add(create
                ? "The export after CreateSnapshot carries no Snapshot attribute; the layout may differ from the one BetterTIA/watch reported for V21."
                : "No snapshot is stored for this DB (or the export layout is unknown). Take one with plc_write create_db_snapshot while the PLC is online.");
        if (create)
            info.Messages.Add("TIA Portal stores the snapshot in the offline project, which now shows as changed; save_project keeps it.");
        return info;
    }

    /// <summary>The ValueService of a DB's interface, the DB's canonical path and the CPU device item (for the online check).</summary>
    internal static (object Values, string CanonicalPath, Siemens.Engineering.HW.DeviceItem? Host, string PlcName) ResolveValueService(ProjectBase project, string? blockPath)
    {
        var address = ParseBlock(Require(blockPath, "blockPath"));
        var discovered = PlcSoftwareLocator.FindUnique(project, address.PlcName);
        var located = BlockLocator.Locate(discovered.Software, address);
        var db = RequireDataBlock(located);
        return (RequireService(db.Interface, ValueServiceName, located.CanonicalPath, "the block interface"), located.CanonicalPath, discovered.HostDeviceItem, discovered.Software.Name);
    }

    private static object RequireService(object target, string shortName, string blockPath, string what)
    {
        var offered = TechnologyObjectRuntime.ServiceTypes(target);
        var matches = offered.Where(type => type.Name == shortName && type.Namespace?.StartsWith("Siemens.Engineering", StringComparison.Ordinal) == true)
            .Distinct().ToList();
        if (matches.Count != 1)
            throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing, matches.Count == 0
                ? $"{what} of '{blockPath}' lists no service named {shortName} (offered: {string.Join(", ", offered.Select(t => t.Name).Distinct())}). Nothing was changed."
                : $"{what} of '{blockPath}' lists {matches.Count} services named {shortName}; none is chosen. Nothing was changed.");
        return TechnologyObjectRuntime.Service(target, offered, matches[0].FullName!)!;
    }

    private static MethodInfo Bind(object target, string name, Type[] argumentTypes)
        => LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"{target.GetType().Name}.{name}({string.Join(", ", argumentTypes.Select(t => t.Name))}) is not available: {error} Nothing was changed.");

    private static void Invoke(MethodInfo method, object target, object[] arguments)
    {
        try
        {
            method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }
}
