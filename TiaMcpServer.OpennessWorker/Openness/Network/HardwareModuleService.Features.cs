using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// Package G (not live-tested): the hardware features of <see cref="HardwareFeatureMethods"/>. This partial only supplies the live
/// target and partner, the documented <c>GetServiceInfos()</c> lookups and descriptions to the Siemens-free <see cref="HardwareFeatureRun"/>.
/// </summary>
internal static partial class HardwareModuleService
{
    public static HardwareFeatureResultInfo Feature(ProjectBase project, HardwareModuleRequestInfo request, string method)
    {
        var target = Resolve(project, request.Target, "target");
        var partner = request.Partner is null ? null : Resolve(project, request.Partner, "partner");
        var context = new HardwareFeatureContext
        {
            Target = target.Object,
            TargetIdentity = target.Identity,
            Partner = partner?.Item ?? (object?)partner?.Object,
            ServiceTypes = owner => owner is IEngineeringServiceProvider ? TechnologyObjectRuntime.ServiceTypes(owner) : Array.Empty<Type>(),
            Service = (owner, type) => TechnologyObjectRuntime.TryService(owner, TechnologyObjectRuntime.ServiceTypes(owner), type.FullName!, out _),
            Describe = DescribeLive,
            Recoverable = Recoverable,
        };
        return Translate(() => HardwareFeatureRun.Run(method, context, request));
    }

    private static string DescribeLive(object? value)
    {
        try
        {
            return value switch
            {
                null => "(none)",
                DeviceItem item => $"device item '{item.Name}'" + (DeviceOfItem(item) is { } device ? $" of '{device}'" : string.Empty),
                Device device => $"device '{device.Name}'",
                _ => $"{value.GetType().Name} '{value.GetType().GetProperty("Name")?.GetValue(value, null) ?? "(unnamed)"}'",
            };
        }
        catch (EngineeringException)
        {
            return value?.GetType().Name ?? "(none)";
        }
    }

    private static string? DeviceOfItem(DeviceItem item)
    {
        IEngineeringObject? current = item;
        for (var depth = 0; depth < 32 && current is not null; depth++)
        {
            if (current is Device device) return device.Name;
            current = (current as IEngineeringInstance)?.Parent;
        }

        return null;
    }
}
