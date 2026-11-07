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
/// name (not listed or not returned: unsupported_capability). The blocks or units are resolved first (target_not_found), then the
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

        var offered = TechnologyObjectRuntime.ServiceTypes(software);
        return Translate(() =>
        {
            var (provider, unavailable) = RuntimeServiceValues.Find(offered,
                type => TechnologyObjectRuntime.TryService(software, offered, type.FullName!, out _), PlcLoadableNames.Provider);
            if (provider is null)
                throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                    (unavailable ?? RuntimeServiceValues.NotListed($"The PLC software '{software.Name}'", PlcLoadableNames.Provider)) + " Nothing was written.");
            var bound = PlcLoadableRun.Bind(provider, elementType, request.Target);
            return PlcLoadableRun.Generate(provider, bound, objects, request.FilePath, request.Overwrite, DateTime.UtcNow, result);
        });
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
