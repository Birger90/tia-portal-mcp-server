namespace TiaMcpServer.Contracts.Network;

/// <summary>Worker method names of the PROFINET IO-system writes of <c>network_write</c>.</summary>
public static class NetworkIoSystemMethods
{
    /// <summary>
    /// Creates a PROFINET IO system on a CPU's node: the node's owning <c>NetworkInterface</c>, its first
    /// <c>IoController</c> (<c>IoControllers</c>), then <c>IoController.CreateIoSystem(System.String)</c>, which is bound at
    /// runtime by exact name and parameter type (no stub member; a missing member fails <c>api_member_missing</c>).
    /// </summary>
    public const string CreateIoSystem = "create_io_system";
}

/// <summary>
/// Result of <c>create_io_system</c>, read back after the create: the IO system as its subnet's <c>IoSystems</c> lists it.
/// <see cref="SubnetId"/> and <see cref="Number"/> are the values <c>configure_network_device</c> <c>changes.ioSystem</c>
/// takes to attach an IO device.
/// </summary>
public sealed class CreateIoSystemResultInfo
{
    /// <summary>The device of the IO controller's node (the request's <c>target.deviceName</c>).</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>The node whose interface owns the IO controller (the request's <c>target.nodeId</c>).</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>The name passed to <c>CreateIoSystem</c>; null when the caller gave none (an empty string was passed).</summary>
    public string? RequestedName { get; set; }

    /// <summary>The created IO system's <c>Name</c> as read back.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The created IO system's <c>Number</c> within its subnet, as read back.</summary>
    public int Number { get; set; }

    /// <summary>The <c>subnetId</c> of the subnet whose <c>IoSystems</c> list the created IO system.</summary>
    public string SubnetId { get; set; } = string.Empty;

    public List<string> Messages { get; set; } = new();
}
