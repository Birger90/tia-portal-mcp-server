using Siemens.Engineering;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc.Loadable;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc.Loadable;

/// <summary>
/// plc_write generate_loadable (issue #26, not live-tested). The V21 reference (Siemens.Engineering.Step7.xml) documents
/// <c>LoadableProvider.GenerateLoadable(FileInfo, IEnumerable&lt;PlcBlock&gt;, TargetOption)</c> and the <c>IEnumerable&lt;PlcUnit&gt;</c>
/// overload but not which object offers the provider: it is taken from the PlcSoftware's <c>GetServiceInfos()</c> by exact full
/// name, then on the CPU device item, its parents and the device (<see cref="ServiceOwnerChain"/>; none returns it: unsupported_capability). The blocks or units are resolved first (target_not_found), then the
/// overload and the <c>TargetOption</c> member are bound on the live provider by the Siemens-free <see cref="PlcLoadableRun"/>
/// (api_member_missing), all before the call; the file follows the alarm exports' caller-file rules and is re-hashed at its place.
/// No stub member was added.
/// </summary>
internal static class PlcLoadableService
{
    public static PlcLoadableResultInfo Generate(ProjectBase project, PlcLoadableRequestInfo request)
    {
        var selector = request.PlcName;
        if (selector is null && request.BlockPaths is { Count: > 0 } paths) selector = Parse(paths[0]).PlcName;
        var plc = PlcSoftwareLocator.FindUnique(project, selector);
        var software = plc.Software;
        var result = new PlcLoadableResultInfo { PlcName = software.Name, Target = request.Target };
        var objects = new List<object>();
        string elementType;
        if (request.BlockPaths is { } blockPaths)
        {
            (result.Overload, elementType) = (PlcLoadableNames.OverloadBlocks, PlcLoadableNames.BlockType);
            foreach (var path in blockPaths)
            {
                var address = Parse(path);
                if (address.PlcName is { } named && !string.Equals(named, software.Name, StringComparison.OrdinalIgnoreCase))
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                        $"Block '{path}' names PLC '{named}', not '{software.Name}'; one loadable file holds blocks of one PLC. Nothing was written.");
                var block = BlockTargetResolver.ResolveForExport(software, address).Block
                    ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{path}' was not found. Nothing was written.");
                objects.Add(block);
                result.Objects.Add(block.Name);
            }
        }
        else if (request.UnitNames is { } unitNames)
        {
            (result.Overload, elementType) = (PlcLoadableNames.OverloadUnits, PlcLoadableNames.UnitType);
            foreach (var name in unitNames)
            {
                // The documented overload takes PlcUnit; a safety unit (audit T4: now found by name too) is not one.
                var unit = PlcTypeTargetResolver.FindSoftwareUnit(software, name) as Siemens.Engineering.SW.Units.PlcUnit
                    ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                        $"'{name}' is a safety unit; the documented loadable overload takes software units (PlcUnit) only. Nothing was generated.");
                objects.Add(unit);
                result.Objects.Add(unit.Name);
            }
        }
        else
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "generate_loadable needs blockPaths or unitNames.");
        }

        return Translate(() =>
        {
            // The PLC software first (the former behaviour), then the CPU device item, its parents and the device (idea from
            // asckye/TIA_Portal_Openness_MCP, MIT): V21 does not document which object hosts LoadableProvider.
            var found = ServiceOwnerChain.Find(Owners(software, plc.HostDeviceItem), owner =>
            {
                if (owner is not IEngineeringServiceProvider) return (null, null);
                var offered = TechnologyObjectRuntime.ServiceTypes(owner);
                return RuntimeServiceValues.Find(offered, type => TechnologyObjectRuntime.TryService(owner, offered, type.FullName!, out _), PlcLoadableNames.Provider);
            });
            if (found.Service is not { } provider)
                throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                    $"PLC '{software.Name}': {found.Reason(PlcLoadableNames.Provider)} Nothing was written.");
            if (found.Searched.Count > 1) result.Messages.Add($"LoadableProvider was found on the {found.FoundOn}, not on the PLC software.");
            var bound = PlcLoadableRun.Bind(provider, elementType, request.Target);
            return PlcLoadableRun.Generate(provider, bound, objects, request.FilePath, request.Overwrite, DateTime.UtcNow, result);
        });
    }

    /// <summary>The PLC software, its CPU device item, each parent device item and the owning device, in this order.</summary>
    private static IEnumerable<ServiceOwnerChain.Owner> Owners(Siemens.Engineering.SW.PlcSoftware software, Siemens.Engineering.HW.DeviceItem? host)
    {
        yield return new ServiceOwnerChain.Owner($"PLC software '{software.Name}'", software);
        object? current = host;
        for (var depth = 0; current is not null && depth < ServiceOwnerChain.MaxObjects - 1; depth++)
        {
            switch (current)
            {
                case Siemens.Engineering.HW.Device device:
                    yield return new ServiceOwnerChain.Owner($"device '{device.Name}'", device);
                    yield break;
                case Siemens.Engineering.HW.DeviceItem item:
                    yield return new ServiceOwnerChain.Owner(depth == 0 ? $"CPU device item '{item.Name}'" : $"parent device item '{item.Name}'", item);
                    break;
                default:
                    yield break;
            }

            current = (current as IEngineeringInstance)?.Parent;
        }
    }

    private static BlockAddress Parse(string path)
    {
        try
        {
            return BlockAddress.Parse(path);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, exception.Message);
        }
    }

    private static T Translate<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }
}
