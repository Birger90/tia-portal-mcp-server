using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Network;

/// <summary>
/// One segment of a path through a device's item hierarchy.
/// Used in <see cref="NetworkObjectSelectorInfo.ItemPath"/> when targeting a device item.
/// Index, name and position number are required evidence; the type identifier is optional (K95):
/// TIA reports none for built-in items such as a CPU's interfaces, ports, card reader or display, and
/// the item is still unambiguous by its sibling index. When a type identifier is carried it is an
/// extra consistency constraint compared against the live item.
/// </summary>
public sealed class DeviceItemPathSegmentInfo
{
    /// <summary>Zero-based sibling index within the parent device item composition.</summary>
    public int Index { get; set; }

    /// <summary>Name of the module at this level of the device hierarchy.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Position number of the module at this level of the device hierarchy.</summary>
    public int PositionNumber { get; set; }

    /// <summary>
    /// Type identifier of the item at this level, or null when TIA reports none (built-in interfaces,
    /// ports and similar sub-items). Written as an explicit null.
    /// </summary>
    public string? TypeIdentifier { get; set; }
}

/// <summary>
/// Identifies exactly one existing network object. The set of non-null fields determines the kind
/// and must satisfy the per-kind shape enforced by the host catalog.
///
/// <para>
/// This type is the output/contract representation. It is serialization-compatible with the host's
/// strict <c>NetworkObjectTarget</c> request type so selectors embedded in read results can be
/// forwarded directly into an inspect request without transformation.
/// </para>
/// </summary>
public sealed class NetworkObjectSelectorInfo
{
    /// <summary>Preferred node owner path, present when exact name/position evidence is available.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NetworkInterfacePathSegmentInfo>? InterfacePath { get; set; }

    /// <summary>One of the values declared in <see cref="NetworkObjectKinds"/>.</summary>
    public string? Kind { get; set; }

    /// <summary>Exact device name. Required for deviceItem, networkInterface, node, and communicationConnection kinds.</summary>
    public string? DeviceName { get; set; }

    /// <summary>Path through the device item hierarchy. Required for deviceItem, networkInterface, and communicationConnection kinds.</summary>
    public List<DeviceItemPathSegmentInfo>? ItemPath { get; set; }

    /// <summary>Optional captured interface-name evidence for networkInterface kind.</summary>
    public string? InterfaceName { get; set; }

    /// <summary>Interface type (e.g., PROFINET, PROFIBUS). Optional for networkInterface kind.</summary>
    public string? InterfaceType { get; set; }

    /// <summary>Interface operating mode. Optional for networkInterface kind.</summary>
    public string? InterfaceOperatingMode { get; set; }

    /// <summary>Exact node identifier reported by read_hardware_config. Required for node kind.</summary>
    public string? NodeId { get; set; }

    /// <summary>Optional zero-based sibling index within the selected network interface's node collection.</summary>
    public int? NodeIndex { get; set; }

    /// <summary>Exact subnet identifier reported by read_hardware_config. Required for subnet and ioSystem kinds.</summary>
    public string? SubnetId { get; set; }

    /// <summary>IO system number within its subnet. Required for ioSystem kind.</summary>
    public int? Number { get; set; }

    /// <summary>Optional zero-based sibling index within the subnet's IO-system collection.</summary>
    public int? IoSystemIndex { get; set; }

    /// <summary>Optional captured IO system name evidence used to disambiguate duplicate numbers.</summary>
    public string? IoSystemName { get; set; }

    /// <summary>Connection index within the device. Required for communicationConnection kind.</summary>
    public int? ConnectionIndex { get; set; }

    /// <summary>Connection type. Required for communicationConnection kind.</summary>
    public string? ConnectionType { get; set; }

    /// <summary>Local connection name. Required for communicationConnection kind.</summary>
    public string? LocalConnectionName { get; set; }

    /// <summary>Local connection identifier. Optional for communicationConnection kind.</summary>
    public string? LocalConnectionId { get; set; }
}

/// <summary>
/// Shared rule for the optional type identifier of a <see cref="DeviceItemPathSegmentInfo"/> (K95):
/// null and "" both mean "TIA reports none"; a nonblank value is an extra constraint that must equal
/// the live item's type identifier. Index, name and position number stay the required evidence.
/// </summary>
public static class DeviceItemPathTypeIdentifier
{
    /// <summary>Maps null, "" and whitespace-only text to null; keeps any other value unchanged.</summary>
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>True when the value was supplied as text that is not empty but only whitespace.</summary>
    public static bool IsInvalid(string? value) => !string.IsNullOrEmpty(value) && string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// True when the requested segment value is satisfied by the live item's type identifier: no
    /// requested value constrains nothing; a requested value must equal the live value ordinally.
    /// </summary>
    public static bool Matches(string? requested, string? live)
        => Normalize(requested) is not { } wanted || string.Equals(wanted, live, StringComparison.Ordinal);
}
