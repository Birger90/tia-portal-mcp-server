using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// network_read list_transfer_areas and network_write create/update/delete_transfer_area, delete_io_system and
/// disconnect_from_io_system (GitHub issue #11, not live-tested). Evidence: Siemens.Engineering.Base.xml (V21). The transfer area
/// compositions, their <c>Create</c> overloads, the areas' properties and <c>Delete()</c>, <c>IoSystem.Delete()</c> and
/// <c>IoConnector.DisconnectFromIoSystem()</c> are not in the compile stubs; they are bound at runtime by exact name and argument
/// types (the Siemens-free <see cref="TransferAreaRun"/> for transfer areas). The network interface is the device item's
/// <c>GetService&lt;NetworkInterface&gt;()</c> (stubbed). Creating a DP master system has no V21 member besides the generic
/// <c>IoController.CreateIoSystem(System.String)</c> of create_io_system; deleting one is <c>IoSystem.Delete()</c> like any IO system.
/// </summary>
internal static partial class HardwareModuleService
{
    public static HardwareTransferAreaListInfo ListTransferAreas(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var networkInterface = InterfaceOf(target, HardwareModuleMethods.ListTransferAreas);
        var result = new HardwareTransferAreaListInfo { Target = target.Identity };
        result.TransferAreas = ReadAreas(networkInterface, NetworkSystemNames.TransferAreaCollections.TransferAreas, result.Messages);
        result.MulticastableTransferAreas = ReadAreas(networkInterface, NetworkSystemNames.TransferAreaCollections.Multicastable, result.Messages);
        return result;
    }

    public static HardwareTransferAreaWriteResultInfo CreateTransferArea(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var area = request.TransferArea ?? throw Validation("create_transfer_area requires transferArea.");
        var target = Resolve(project, request.Target, "target");
        var networkInterface = InterfaceOf(target, HardwareModuleMethods.CreateTransferArea);
        var composition = RequireComposition(networkInterface, area.Collection);
        var typeName = RequireText(area.Type, "transferArea.type");
        var enumType = TechnologyObjectRuntime.EnumType(networkInterface, NetworkSystemNames.TransferAreaType);
        var type = LibraryRuntimeBinding.EnumValue(enumType, typeName)
            ?? throw Validation($"transferArea.type '{typeName}' is not a member of {NetworkSystemNames.TransferAreaType} on this installation ({LibraryRuntimeBinding.MemberList(enumType)}).");
        var result = new HardwareTransferAreaWriteResultInfo { Operation = HardwareModuleMethods.CreateTransferArea, Target = target.Identity, Collection = area.Collection };
        object? partner = null;
        if (area.Collection == NetworkSystemNames.TransferAreaCollections.Multicastable)
        {
            var partnerTarget = Resolve(project, request.Partner, "partner");
            result.Partner = partnerTarget.Identity;
            var partnerInterface = InterfaceOf(partnerTarget, HardwareModuleMethods.CreateTransferArea);
            partner = partnerInterface;
            if (area.TemplateIndex is { } templateIndex)
            {
                var templates = TransferAreaRun.Elements(RequireComposition(partnerInterface, area.Collection), area.Collection);
                if (templateIndex < 0 || templateIndex >= templates.Count)
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                        $"The partner interface has no multicastable transfer area at index {templateIndex} ({templates.Count} listed). Nothing was changed.");
                partner = templates[templateIndex];
            }
        }

        var (created, index, before, after) = Translate(() => TransferAreaRun.Create(composition, area, type, partner));
        result.CountBefore = before;
        result.CountAfter = after;
        result.After = TransferAreaRun.Describe(created, area.Collection, index, Recoverable, AttributeInfosOf);
        if (after != before + 1) result.Messages.Add($"The composition lists {after} areas after Create (before: {before}).");
        if (area.Name is { } requested && !string.Equals(result.After.Name, requested, StringComparison.Ordinal))
            result.Messages.Add($"TIA Portal named the area '{result.After.Name ?? "(unreadable)"}', not '{requested}'.");
        return result;
    }

    public static HardwareTransferAreaWriteResultInfo UpdateTransferArea(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var (target, composition, area, selected) = SelectArea(project, request, HardwareModuleMethods.UpdateTransferArea);
        var requested = request.Attributes is { Count: > 0 } list ? list : throw Validation("update_transfer_area requires attributes.");
        if (area is not IEngineeringObject engineeringObject)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"The transfer area is a '{area.GetType().FullName}', not an IEngineeringObject, so its attributes cannot be set. Nothing was changed.");
        var infos = engineeringObject.GetAttributeInfos().ToDictionary(info => info.Name, StringComparer.Ordinal);
        var planned = new List<(HardwareAttributeValueInfo Request, object Value, Type Type, object? Before)>();
        foreach (var attribute in requested)
        {
            if (!infos.TryGetValue(attribute.Name, out var info))
                throw Validation($"update_transfer_area: the area has no attribute '{attribute.Name}' (GetAttributeInfos, case-sensitive). Nothing was changed.");
            if (info.AccessMode is not (EngineeringAttributeAccessMode.Write or EngineeringAttributeAccessMode.ReadWrite))
                throw Validation($"update_transfer_area: attribute '{attribute.Name}' reports access mode {info.AccessMode}; it is not writable. Nothing was changed.");
            var conversion = HardwareAttributeValueConverter.Convert(attribute, info.SupportedTypes.ToArray());
            if (!conversion.Success) throw Validation($"update_transfer_area: {conversion.Error} Nothing was changed.");
            planned.Add((attribute, conversion.Value!, conversion.TargetType!, TryRead(engineeringObject, attribute.Name)));
        }

        var result = new HardwareTransferAreaWriteResultInfo
        {
            Operation = HardwareModuleMethods.UpdateTransferArea, Target = target.Identity, Collection = request.TransferArea!.Collection,
            Before = selected, CountBefore = TransferAreaRun.Elements(composition, request.TransferArea.Collection).Count,
        };
        var rejected = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var entry in planned) engineeringObject.SetAttribute(entry.Request.Name, entry.Value);
        }
        catch (EngineeringException exception)
        {
            var readBack = Outcomes(engineeringObject, planned, rejected, HardwareModuleMethods.AttributeErrorPolicies.Abort, result.Messages);
            throw new WorkerOperationException(WorkerExecution.Classify(exception, mutates: true),
                $"update_transfer_area: {exception.Message} Earlier attributes may already be applied; the warnings list the read-back of every requested attribute.",
                readBack.Select(Describe).Concat(result.Messages).ToList(), engineeringError: WorkerExecution.Describe(exception));
        }

        result.Attributes = Outcomes(engineeringObject, planned, rejected, HardwareModuleMethods.AttributeErrorPolicies.Abort, result.Messages);
        var after = TransferAreaRun.Elements(composition, result.Collection);
        result.CountAfter = after.Count;
        var index = after.FindIndex(element => Equals(element, area));
        result.After = TransferAreaRun.Describe(area, result.Collection, index < 0 ? selected.Index : index, Recoverable, AttributeInfosOf);
        if (index < 0) result.Messages.Add("The re-read composition does not list the updated area by identity; its properties were read from the original object.");
        var notApplied = result.Attributes.Where(outcome => outcome.Status != "applied").ToArray();
        if (notApplied.Length > 0)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"update_transfer_area: {notApplied.Length} attribute(s) did not read back the written value: {string.Join("; ", notApplied.Select(Describe))}.",
                result.Attributes.Select(Describe).ToList());
        return result;
    }

    public static HardwareTransferAreaWriteResultInfo DeleteTransferArea(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var (target, composition, area, selected) = SelectArea(project, request, HardwareModuleMethods.DeleteTransferArea);
        var collection = request.TransferArea!.Collection;
        var (before, after) = Translate(() => TransferAreaRun.Delete(composition, collection, area));
        return new HardwareTransferAreaWriteResultInfo
        {
            Operation = HardwareModuleMethods.DeleteTransferArea, Target = target.Identity, Collection = collection, Before = selected,
            CountBefore = before, CountAfter = after,
        };
    }

    public static HardwareIoSystemDeleteResultInfo DeleteIoSystem(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var selector = request.IoSystem ?? throw Validation("delete_io_system requires the IO system selector.");
        var selection = NetworkObjectSelectorResolver.Resolve(project, selector);
        if (!selection.Success) throw new WorkerOperationException(selection.FailureCategory ?? WorkerFailureCategories.TargetNotFound, selection.Error ?? "The IO system was not found.");
        if (selection.Resolved!.Value is not IoSystem ioSystem)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, "The selector does not name an IO system.");
        var result = new HardwareIoSystemDeleteResultInfo { SubnetId = selection.Resolved.Target.SubnetId ?? selector.SubnetId ?? string.Empty, Number = selector.Number ?? -1 };
        try
        {
            result.Number = ioSystem.Number;
            result.Name = ioSystem.Name;
            result.ConnectedIoDeviceCount = ioSystem.ConnectedIoDevices.Cast<IoConnector>().Count();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception) && exception is not EngineeringObjectDisposedException)
        {
            result.Messages.Add($"The IO system's name, number or connected IO devices could not be read before the delete ({exception.Message}).");
        }

        Translate(() => RuntimeServiceValues.Invoke(ioSystem, "Delete", Type.EmptyTypes, Array.Empty<object?>(), "IoSystem.Delete()"));
        // Re-resolve by subnet, number and name: the deleted reference must not be reused.
        var again = NetworkObjectSelectorResolver.Resolve(project, new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.IoSystem, SubnetId = result.SubnetId, Number = result.Number, IoSystemName = result.Name,
        });
        if (again.Success)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"delete_io_system: IoSystem.Delete() returned, but subnet '{result.SubnetId}' still lists an IO system with number {result.Number}"
                + (result.Name is null ? "." : $" named '{result.Name}'.") + " Inspect it with network_read read_hardware_config before retrying.");
        return result;
    }

    public static HardwareIoConnectorDisconnectResultInfo DisconnectFromIoSystem(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var target = Resolve(project, request.Target, "target");
        var networkInterface = InterfaceOf(target, HardwareModuleMethods.DisconnectFromIoSystem);
        var connectors = networkInterface.IoConnectors.Cast<IoConnector>().ToList();
        if (connectors.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"The interface of {Describe(target.Identity)} has no IO connector (IoConnectors is empty). Nothing was changed.");
        var connected = connectors.Select((connector, index) => (Connector: connector, Index: index, IoSystem: connector.ConnectedToIoSystem))
            .Where(entry => entry.IoSystem is not null).ToList();
        if (connected.Count > 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"The interface of {Describe(target.Identity)} has {connected.Count} IO connectors connected to an IO system; none is chosen. Nothing was changed.");
        if (connected.Count == 0 && connectors.Count > 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous,
                $"The interface of {Describe(target.Identity)} has {connectors.Count} IO connectors and none is connected to an IO system; none is chosen. Nothing was changed.");
        (IoConnector Connector, int Index, IoSystem? IoSystem) chosen = connected.Count == 1 ? connected[0] : (connectors[0], 0, null);
        var result = new HardwareIoConnectorDisconnectResultInfo { Target = target.Identity, ConnectorIndex = chosen.Index };
        if (chosen.IoSystem is { } before)
        {
            result.IoSystemName = before.Name;
            result.IoSystemNumber = before.Number;
        }
        else result.Messages.Add("The IO connector is not connected to an IO system; DisconnectFromIoSystem was called anyway and TIA Portal decided.");

        Translate(() => RuntimeServiceValues.Invoke(chosen.Connector, "DisconnectFromIoSystem", Type.EmptyTypes, Array.Empty<object?>(), "IoConnector.DisconnectFromIoSystem()"));
        result.DisconnectedAfter = chosen.Connector.ConnectedToIoSystem is null;
        if (!result.DisconnectedAfter)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                "disconnect_from_io_system: DisconnectFromIoSystem returned, but IoConnector.ConnectedToIoSystem still reads an IO system. "
                + "Inspect it with network_read read_hardware_config before retrying.");
        return result;
    }

    private static NetworkInterface InterfaceOf(ResolvedHardwareObject target, string operation)
    {
        var item = RequireItem(target, operation);
        return item.GetService<NetworkInterface>()
            ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"{Describe(target.Identity)} offers no NetworkInterface service; select the interface device item (read_hardware_config). Nothing was changed.");
    }

    private static List<HardwareTransferAreaInfo>? ReadAreas(NetworkInterface networkInterface, string collection, List<string> messages)
    {
        var (composition, error) = TransferAreaRun.Composition(networkInterface, collection);
        if (composition is null)
        {
            messages.Add($"{collection}: {error}");
            return null;
        }

        return Translate(() => TransferAreaRun.Elements(composition, collection))
            .Select((area, index) => TransferAreaRun.Describe(area, collection, index, Recoverable, AttributeInfosOf)).ToList();
    }

    private static object RequireComposition(NetworkInterface networkInterface, string collection)
    {
        if (!NetworkSystemNames.TransferAreaCollections.All.Contains(collection, StringComparer.Ordinal))
            throw Validation($"transferArea.collection must be one of: {string.Join(", ", NetworkSystemNames.TransferAreaCollections.All)}.");
        var (composition, error) = TransferAreaRun.Composition(networkInterface, collection);
        return composition ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability, $"{error} Nothing was changed.");
    }

    private static (ResolvedHardwareObject Target, object Composition, object Area, HardwareTransferAreaInfo Selected) SelectArea(ProjectBase project,
        HardwareModuleRequestInfo request, string operation)
    {
        var selector = request.TransferArea ?? throw Validation($"{operation} requires transferArea.");
        var target = Resolve(project, request.Target, "target");
        var composition = RequireComposition(InterfaceOf(target, operation), selector.Collection);
        var index = selector.Index ?? throw Validation("transferArea.index is required.");
        var name = selector.Name ?? throw Validation("transferArea.name is required as evidence.");
        var elements = Translate(() => TransferAreaRun.Elements(composition, selector.Collection));
        var area = Translate(() => TransferAreaRun.Select(elements, selector.Collection, index, name,
            candidate => RuntimeServiceValues.ReadProperty(candidate, "Name", Recoverable).Value));
        return (target, composition, area, TransferAreaRun.Describe(area, selector.Collection, index, Recoverable, AttributeInfosOf));
    }

    private static List<TransferAreaAttributeInfo> AttributeInfosOf(object area)
    {
        if (area is not IEngineeringObject engineeringObject) return new List<TransferAreaAttributeInfo>();
        try
        {
            return engineeringObject.GetAttributeInfos()
                .Select(info => new TransferAreaAttributeInfo { Name = info.Name, AccessMode = info.AccessMode.ToString() }).ToList();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception) && exception is not EngineeringObjectDisposedException)
        {
            return new List<TransferAreaAttributeInfo>();
        }
    }
}
