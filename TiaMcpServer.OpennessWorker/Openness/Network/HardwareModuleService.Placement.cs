using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// Where plug, move and copy placed an item, and the refusal text of a false <c>CanPlugNew</c> (K05, K29).
/// </summary>
internal static partial class HardwareModuleService
{
    /// <summary>
    /// The refusal of a false <c>CanPlugNew</c>. On a device target it names the rack item to plug into, because modules
    /// usually plug into the rack item, not the device (live 2026-11-02, ET 200SP; K29). Nothing else is refused.
    /// </summary>
    private static string PlugRefusal(ResolvedHardwareObject container, string typeIdentifier, string name, int position)
    {
        var text = $"plug_device_item: CanPlugNew('{typeIdentifier}', '{name}', {position}) returned false for {Describe(container.Identity)}. Nothing was changed. ";
        if (container.Item is not null)
            return text + "List free positions with network_read list_plug_locations and check the type identifier.";
        var racks = new List<(DeviceItem Item, int Index)>();
        var index = 0;
        var names = new List<string>();
        foreach (var item in container.Object.DeviceItems)
        {
            try
            {
                if (item.TypeIdentifier is { } type && type.StartsWith(RackTypePrefix, StringComparison.Ordinal))
                {
                    racks.Add((item, index));
                    names.Add($"'{item.Name}'");
                }
            }
            catch (EngineeringException) { }

            index++;
        }

        if (racks.Count == 0)
            return text + "Modules usually plug into a rack or rail item, not the device itself: read the device with network_read read_hardware_config "
                + "and use its rack or rail item as a 'deviceItem' target.";
        if (racks.Count > 1)
            return text + $"Modules usually plug into a rack item, not the device itself; device '{container.Identity.DeviceName}' has {racks.Count} rack items "
                + $"({string.Join(", ", names)}): use the intended one as a 'deviceItem' target.";
        var rack = racks[0];
        return text + $"Modules usually plug into the rack item, not the device itself: retry with target {{ kind: 'deviceItem', deviceName: "
            + $"'{container.Identity.DeviceName}', itemPath: [{{ index: {rack.Index}, name: '{rack.Item.Name}', positionNumber: {rack.Item.PositionNumber}, "
            + $"typeIdentifier: '{rack.Item.TypeIdentifier}' }}] }} and list its free positions with network_read list_plug_locations.";
    }

    private const string RackTypePrefix = "System:Rack";

    /// <summary>The owning device of a top-level container item (a rack or rail); null for a device or a deeper item.</summary>
    private static Device? RackOwner(ResolvedHardwareObject container)
        => container.Item is not null && container.Identity.ItemPath.Count == 1 ? ((IEngineeringObject)container.Item).Parent as Device : null;

    private sealed record PlacementOutcome(HardwareObjectIdentityInfo? PlacedUnder, bool AtDeviceLevel, List<string> Mismatches);

    /// <summary>
    /// Where <paramref name="placed"/> landed: directly in <paramref name="container"/> at <paramref name="position"/> (name
    /// checked when given), or, for a top-level container (<paramref name="rackDevice"/> set), directly under the container's
    /// device with the same position, name and type identifier. An unreadable identity or position never passes.
    /// </summary>
    private static PlacementOutcome Placement(HardwareObjectIdentityInfo? placed, HardwareObjectIdentityInfo container, int position,
        HardwareObjectIdentityInfo? rackDevice, string? typeIdentifier, string? name)
    {
        var mismatches = PlacementMismatches(placed, container, position);
        if (placed is not null && !string.IsNullOrEmpty(name) && !string.Equals(placed.Name, name, StringComparison.Ordinal))
            mismatches.Add($"it is named '{placed.Name}', not '{name}'");
        if (mismatches.Count == 0) return new PlacementOutcome(container, false, mismatches);
        if (rackDevice is not null && placed is { ItemPath.Count: 1 } && string.Equals(placed.DeviceName, container.DeviceName, StringComparison.Ordinal)
            && placed.PositionNumber == position
            && (string.IsNullOrEmpty(name) || string.Equals(placed.Name, name, StringComparison.Ordinal))
            && (typeIdentifier is null || string.Equals(placed.TypeIdentifier, typeIdentifier, StringComparison.OrdinalIgnoreCase)))
            return new PlacementOutcome(rackDevice, true, mismatches);
        if (rackDevice is not null && placed is { ItemPath.Count: 1 } && typeIdentifier is not null
            && !string.Equals(placed.TypeIdentifier, typeIdentifier, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"it reports type identifier '{placed.TypeIdentifier}', not '{typeIdentifier}'");
        return new PlacementOutcome(null, false, mismatches);
    }

    /// <summary>
    /// When PlugNew's return value could not be read: exactly one item that did not exist before, under the container or (for
    /// a rack-level container) under its device, with the requested type identifier, name (when given) and position.
    /// </summary>
    private static PlacementOutcome FindNewItem(ResolvedHardwareObject container, Device? rackDevice, HardwareObjectIdentityInfo? deviceIdentity,
        List<DeviceItem> containerBefore,
        List<DeviceItem>? deviceBefore, string typeIdentifier, string name, int position, HardwareDeviceItemMutationResultInfo result,
        PlacementOutcome previous)
    {
        var candidates = new List<(DeviceItem Item, bool AtDeviceLevel)>();
        void Collect(IEnumerable<DeviceItem> items, List<DeviceItem> before, bool deviceLevel)
        {
            foreach (var item in items)
            {
                if (before.Any(seen => Equals(seen, item))) continue;
                try
                {
                    if (item.PositionNumber == position && string.Equals(item.TypeIdentifier, typeIdentifier, StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrEmpty(name) || string.Equals(item.Name, name, StringComparison.Ordinal)))
                        candidates.Add((item, deviceLevel));
                }
                catch (EngineeringException) { }
            }
        }

        Collect(container.Object.DeviceItems, containerBefore, false);
        if (rackDevice is not null) Collect(rackDevice.DeviceItems, deviceBefore!, true);
        if (candidates.Count != 1)
        {
            previous.Mismatches.Add($"{candidates.Count} new items with type identifier '{typeIdentifier}' at position {position} were found under "
                + $"{Describe(container.Identity)}{(rackDevice is null ? string.Empty : $" or device '{container.Identity.DeviceName}'")}");
            return previous;
        }

        var identity = IdentityOf(candidates[0].Item, result.Messages);
        if (identity is not { ItemPath.Count: > 0 }) return previous;
        result.Result = identity;
        return new PlacementOutcome(candidates[0].AtDeviceLevel ? deviceIdentity : container.Identity,
            candidates[0].AtDeviceLevel, previous.Mismatches);
    }

    /// <summary>The identity of a container's owning device, shaped like <see cref="Resolve"/>'s device identity.</summary>
    private static HardwareObjectIdentityInfo DeviceIdentity(string deviceName, Device device)
    {
        string? type = null;
        try { type = device.TypeIdentifier; } catch (EngineeringException) { }
        return new HardwareObjectIdentityInfo { DeviceName = deviceName, Name = deviceName, TypeIdentifier = type };
    }

    /// <summary>
    /// What prevents confirming that <paramref name="placed"/> is a direct child of <paramref name="container"/>
    /// at <paramref name="position"/>; an unreadable identity or position is a mismatch, never a pass.
    /// </summary>
    private static List<string> PlacementMismatches(HardwareObjectIdentityInfo? placed, HardwareObjectIdentityInfo container, int position)
    {
        var mismatches = new List<string>();
        if (placed is null)
        {
            mismatches.Add("TIA Portal returned no device item, or its identity could not be read");
            return mismatches;
        }

        if (placed.PositionNumber is not { } actual) mismatches.Add("the resulting item's position number is unreadable");
        else if (actual != position) mismatches.Add($"the item is at position {actual}, not {position}");
        var path = placed.ItemPath;
        var parentPath = container.ItemPath;
        var isDirectChild = !string.IsNullOrEmpty(placed.DeviceName)
            && string.Equals(placed.DeviceName, container.DeviceName, StringComparison.Ordinal)
            && path.Count == parentPath.Count + 1
            && parentPath.Select((segment, index) => string.Equals(segment.Name, path[index].Name, StringComparison.Ordinal)
                && segment.PositionNumber == path[index].PositionNumber).All(same => same);
        if (!isDirectChild)
            mismatches.Add(path.Count == 0
                ? "the resulting item's parent chain could not be read"
                : $"the item is at '{placed.DeviceName}/{string.Join("/", path.Select(segment => segment.Name))}', not directly in {Describe(container)}");
        return mismatches;
    }
}
