using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

/// <summary>
/// Info guards of a planned <c>create_io_system</c> item, read from the call's one planning snapshot. None of them blocks:
/// whether the node can own an IO system is TIA Portal's decision (never stricter than TIA Portal).
/// </summary>
public static class NetworkIoSystemGuards
{
    public static IEnumerable<FiredGuard> Evaluate(string operationId, NetworkWriteEffect effect)
    {
        yield return new FiredGuard(NetworkGuardDefinitions.IoSystemCreateUnverified, operationId,
            "create_io_system was live-tested once (TIA Portal V21, 2026-11-02: S7-1200 G2 CPU, IO system created, an ET 200SP attached with configure_network_device): IoController.CreateIoSystem(System.String) is bound at runtime (api_member_missing when absent) "
            + "and the new IO system is read back from its subnet's IoSystems. Re-read with network_read read_hardware_config afterwards.");
        var current = effect.CurrentSettings;
        var complete = effect.ConnectionsComplete == true;
        if (!complete)
            yield return new FiredGuard(NetworkGuardDefinitions.Unverifiable, operationId,
                "The node's subnet and IO-system relationships could not be read completely; the item runs and TIA Portal decides.");
        else if (current.GetValueOrDefault(NetworkSettingKeys.Subnet) is null)
            yield return new FiredGuard(NetworkGuardDefinitions.IoSystemNodeUnconnected, operationId,
                $"Node '{effect.Target.NodeId}' of device '{effect.Target.DeviceName}' is not connected to a subnet; TIA Portal decides whether "
                + "CreateIoSystem succeeds. Connect it first with configure_network_device changes.subnet.");
        if (complete && current.GetValueOrDefault(NetworkSettingKeys.IoSystemNumber) is { } number)
            yield return new FiredGuard(NetworkGuardDefinitions.IoSystemAlreadyAssigned, operationId,
                $"The node's interface already belongs to IO system {number} on subnet '{current.GetValueOrDefault(NetworkSettingKeys.IoSystemSubnet)}'; "
                + "TIA Portal decides whether another IO system can be created.");
    }
}
