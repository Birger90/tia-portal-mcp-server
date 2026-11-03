using TiaMcpServer.Contracts.Plc.Loadable;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.FakeWorker.Plc;

/// <summary>
/// plc_write generate_loadable against a stand-in LoadableProvider that the production <see cref="PlcLoadableRun"/> binds and
/// calls exactly like the worker. PLC_1 lists the provider and has the software unit <c>Unit_A</c>; PLC_2 lists none
/// (unsupported_capability).
/// </summary>
sealed partial class PlcWriteRoundtripScenario
{
    private readonly Siemens.Engineering.SW.Loader.LoadableProvider _loadable = new();

    private string Loadable(WorkerRequest request)
    {
        var info = request.PlcLoadable ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "missing request");
        var selector = info.PlcName ?? (info.BlockPaths is { Count: > 0 } paths ? paths[0].Split('/')[0] : null);
        var matches = _plcs.Where(p => selector is null || Matches(p, selector)).ToList();
        if (matches.Count != 1)
            throw new WorkerOperationException(matches.Count == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                $"PLC '{selector}' matches {matches.Count} PLCs.");
        var plc = matches[0];
        var result = new PlcLoadableResultInfo { PlcName = plc.Software, Target = info.Target };
        var objects = new List<object>();
        string element;
        if (info.BlockPaths is { } blockPaths)
        {
            (result.Overload, element) = (PlcLoadableNames.OverloadBlocks, PlcLoadableNames.BlockType);
            foreach (var path in blockPaths)
            {
                var name = path.Split('/').Last();
                if (!AllBlocks(plc.Root).Any(block => Names.Equals(block.Name, name)))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Block '{path}' was not found. Nothing was written.");
                objects.Add(new Siemens.Engineering.SW.Blocks.PlcBlock(name));
                result.Objects.Add(name);
            }
        }
        else
        {
            (result.Overload, element) = (PlcLoadableNames.OverloadUnits, PlcLoadableNames.UnitType);
            foreach (var name in info.UnitNames ?? new List<string>())
            {
                if (!Names.Equals(name, "Unit_A"))
                    throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Software Unit '{name}' not found in PLC software '{plc.Software}'.");
                objects.Add(new Siemens.Engineering.SW.Units.PlcUnit("Unit_A"));
                result.Objects.Add("Unit_A");
            }
        }

        var offered = plc.Software == "PLC_1" ? new List<Type> { typeof(Siemens.Engineering.SW.Loader.LoadableProvider) } : new List<Type>();
        try
        {
            var (provider, _) = RuntimeServiceValues.Find(offered, _ => _loadable, PlcLoadableNames.Provider);
            if (provider is null)
                throw new RuntimeBindingException(WorkerFailureCategories.UnsupportedCapability,
                    RuntimeServiceValues.NotListed($"The PLC software '{plc.Software}'", PlcLoadableNames.Provider) + " Nothing was written.");
            var bound = PlcLoadableRun.Bind(provider, element, info.Target);
            return Ok(PlcLoadableRun.Generate(provider, bound, objects, info.FilePath, info.Overwrite, DateTime.UtcNow, result));
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }
}
