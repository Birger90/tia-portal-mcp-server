namespace Siemens.Engineering.Connection
{
    /// <summary>
    /// Stand-in with the documented V21 type name and member names of a scanned device (types are not documented;
    /// the projection must accept text and refuse anything else). Not Siemens code and no evidence of its behavior.
    /// </summary>
    public class ConfigurationAccessibleDevice(string? name, string? deviceSeries, string? address, string? macAddress)
    {
        public object? AddressOverride { get; init; }

        public string? Name { get; } = name;

        public string? DeviceSeries { get; } = deviceSeries;

        public object? Address => AddressOverride ?? address;

        public string? MACAddress { get; } = macAddress;
    }

    /// <summary>A PC interface stand-in exposing exactly one public parameterless GetAccessibleDevices().</summary>
    public sealed class ConfigurationPcInterfaceStandIn(IEnumerable<object> devices)
    {
        public IEnumerable<object> GetAccessibleDevices() => devices;
    }
}
