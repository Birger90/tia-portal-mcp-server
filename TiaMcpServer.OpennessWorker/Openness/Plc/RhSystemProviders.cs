using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Audit T3/N1 (not live-tested): where an R/H system's providers live. V21 documents <c>RHDownloadProvider</c> and
/// <c>RHOnlineProvider</c> as services but not their hosting object; a third-party note says the R/H system exposes them
/// on the system <c>Device</c>, not on a member CPU. The lookup therefore searches the CPU's device item first (the former
/// behaviour), then every parent up to and including the owning <c>Device</c> (<c>IEngineeringInstance.Parent</c>), each by
/// exact full name among its <c>GetServiceInfos()</c>, bound at runtime. The first object that lists the service decides.
/// </summary>
internal static class RhSystemProviders
{
    public const string DownloadProvider = "Siemens.Engineering.Download.RHDownloadProvider";

    public const string OnlineProvider = "Siemens.Engineering.Online.RHOnlineProvider";

    /// <summary>At most this many objects are searched (device item, its parents, the device).</summary>
    private const int MaxDepth = 8;

    /// <summary>
    /// The provider of exactly <paramref name="fullName"/>, or null with <paramref name="reason"/> naming every object that was
    /// searched (and, for one that lists it without returning it, the unavailable reason). <paramref name="foundOn"/> is
    /// <c>device item</c>, <c>parent device item</c> or <c>device</c>.
    /// </summary>
    public static object? Find(DeviceItem host, string fullName, string plcName, out string? foundOn, out string reason)
    {
        foundOn = null;
        var searched = new List<string>();
        var unavailable = new List<string>();
        object? current = host;
        for (var depth = 0; current is not null && depth < MaxDepth; depth++)
        {
            var kind = current is Device ? "device" : depth == 0 ? "device item" : "parent device item";
            searched.Add($"{kind} '{NameOf(current)}'");
            if (current is IEngineeringServiceProvider)
            {
                var offered = TechnologyObjectRuntime.ServiceTypes(current);
                var target = current;
                var (service, missing) = RuntimeServiceValues.Find(offered, type => TechnologyObjectRuntime.TryService(target, offered, type.FullName!, out _), fullName);
                if (service is not null)
                {
                    foundOn = kind;
                    reason = string.Empty;
                    return service;
                }

                if (missing is not null) unavailable.Add($"{kind} '{NameOf(current)}': {missing}");
            }

            if (current is Device) break;
            current = (current as IEngineeringInstance)?.Parent;
            if (current is not (DeviceItem or Device)) break;
        }

        reason = $"PLC '{plcName}': neither its CPU device item nor a parent up to its device offers '{fullName}' (searched: {string.Join(", ", searched)})."
            + (unavailable.Count > 0 ? " " + string.Join(" ", unavailable) : string.Empty)
            + " It is not an R/H system on this installation, or TIA Portal exposes the provider elsewhere (not live-tested).";
        return null;
    }

    private static string NameOf(object item) => item switch
    {
        DeviceItem deviceItem => deviceItem.Name ?? string.Empty,
        Device device => device.Name ?? string.Empty,
        _ => item.GetType().Name,
    };
}
