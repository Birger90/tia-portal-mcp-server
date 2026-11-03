using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// <c>create_io_system</c> (K28; not live-tested): resolves the CPU's node exactly as <c>configure_network_device</c> does,
/// takes the node's owning <c>NetworkInterface</c> (<c>GetService&lt;NetworkInterface&gt;</c> inside the resolver), its
/// <c>IoControllers</c> composition and the first <c>IoController</c> (both declared in the reference stubs and already read
/// by <see cref="HardwareConfigReader"/>), and calls <c>IoController.CreateIoSystem(System.String)</c>. That member is not
/// in the stubs, so it is bound at runtime by exact name and parameter type; when it is absent the call fails
/// <c>api_member_missing</c> and nothing is changed. The new IO system is read back from <c>IoController.IoSystem</c> (or the
/// returned object) and must be listed by its subnet's <c>IoSystems</c>; otherwise <c>postcondition_failed</c>.
/// </summary>
internal static class IoSystemCreationService
{
    internal const string Documented = "IoController.CreateIoSystem(System.String)";

    public static CreateIoSystemResultInfo Create(ProjectBase project, NetworkObjectSelectorInfo target, string? ioSystemName)
    {
        var selection = NetworkObjectSelectorResolver.ResolveNode(project, target);
        if (!selection.Success) throw new WorkerOperationException(selection.FailureCategory!, selection.Error!);
        var networkInterface = selection.Resolved!.OwningInterface
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"create_io_system: the node '{target.NodeId}' of device '{target.DeviceName}' has no owning network interface. Nothing was changed.");
        var controllers = networkInterface.IoControllers.Cast<IoController>().ToList();
        if (controllers.Count == 0)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"create_io_system: the interface of node '{target.NodeId}' of device '{target.DeviceName}' has no IO controller "
                + "(IoControllers is empty), so it cannot own an IO system. Select the CPU's PROFINET node (or a DP master node). Nothing was changed.");
        var result = new CreateIoSystemResultInfo { DeviceName = target.DeviceName!, NodeId = target.NodeId!, RequestedName = ioSystemName };
        if (controllers.Count > 1)
            result.Messages.Add($"The interface has {controllers.Count} IO controllers; the first one (IoControllers[0]) was used.");
        var controller = controllers[0];
        var method = LibraryRuntimeBinding.Find(controller.GetType(), "CreateIoSystem", new[] { typeof(string) }, out var bindingError)
            ?? throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
                $"create_io_system: this TIA Portal installation exposes no {Documented} on '{controller.GetType().FullName}' ({bindingError}). Nothing was changed.");
        var previous = controller.IoSystem;
        object? returned;
        try
        {
            returned = method.Invoke(controller, new object[] { ioSystemName ?? string.Empty });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        var created = returned as IoSystem ?? controller.IoSystem;
        if (created is null || previous is not null && Equals(created, previous))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                "create_io_system: CreateIoSystem returned, but no new IO system could be read from the IO controller. "
                + "Inspect the subnet with network_read read_hardware_config before retrying.");
        ReadBack(created, result);
        if (ioSystemName is not null && !string.Equals(result.Name, ioSystemName, StringComparison.Ordinal))
            result.Messages.Add($"TIA Portal named the IO system '{result.Name}', not '{ioSystemName}'.");
        if (!Equals(controller.IoSystem, created))
            result.Messages.Add("IoController.IoSystem does not read back as the created IO system.");
        return result;
    }

    /// <summary>Number, name and owning subnet of the created IO system; it must be listed by that subnet's <c>IoSystems</c>.</summary>
    private static void ReadBack(IoSystem created, CreateIoSystemResultInfo result)
    {
        Subnet? subnet;
        try
        {
            result.Number = created.Number;
            result.Name = created.Name ?? string.Empty;
            subnet = created.Subnet;
        }
        catch (EngineeringException exception)
        {
            throw Unconfirmed($"its number, name or subnet could not be read ({exception.Message})");
        }

        if (subnet is null) throw Unconfirmed("it reports no subnet");
        var subnetId = SubnetIdentity.Read(subnet, "Created IO system subnet identity");
        if (!subnetId.IsUsable) throw Unconfirmed(subnetId.Diagnostic);
        result.SubnetId = subnetId.Value;
        var listed = false;
        foreach (IoSystem candidate in subnet.IoSystems)
            if (Equals(candidate, created)) listed = true;
        if (!listed) throw Unconfirmed($"subnet '{result.SubnetId}' does not list it in IoSystems");
    }

    private static WorkerOperationException Unconfirmed(string reason)
        => new(WorkerFailureCategories.PostconditionFailed,
            $"create_io_system: CreateIoSystem ran, but the created IO system could not be confirmed: {reason}. "
            + "The IO system may exist; inspect the subnet with network_read read_hardware_config before retrying.");
}
