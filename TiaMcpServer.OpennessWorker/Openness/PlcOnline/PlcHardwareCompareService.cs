using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.Compare;
using Siemens.Engineering.HW;
using Siemens.Engineering.Online;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.PlcOnline;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcOnline;

/// <summary>
/// plc_read compare_hardware and compare_hardware_to_online (GitHub issue #7, not live-tested). Evidence: Siemens.Engineering.Base.xml
/// (V21) documents <c>HardwareObject.CompareTo(Siemens.Engineering.HW.IHardwareCompareTarget)</c> returning
/// <c>Siemens.Engineering.Compare.CompareResult</c> and the interface itself ("Access to the device/device item in a HW compare
/// scenario"), but not which types implement it (the XML lists no inheritance). The member is not in the compile stubs, so it is
/// bound on the live object by name and an argument assignable from the target's runtime type; a target that does not implement
/// <see cref="PlcOnlinePolicy.HardwareCompareTargetType"/> is refused as unsupported_capability before anything is called. The
/// online compare uses the CPU's <c>OnlineProvider</c> as the target only when it implements the interface at runtime, and then
/// runs under exactly the rules of compare_to_online (<see cref="PlcCompareService.RunOnline"/>): fail-closed legitimation handler,
/// TLS trust only from trustedCertificates, no PLCSIM exception, Offline restored afterwards.
/// </summary>
public static class PlcHardwareCompareService
{
    public static PlcCompareResultInfo CompareHardware(ProjectBase project, PlcOnlineRequestInfo request)
    {
        var checkedRequest = Validate(request, online: false);
        var plc = PlcSoftwareLocator.FindUnique(project, checkedRequest.PlcName);
        var left = Subject(plc, checkedRequest.HardwareScope);
        object target;
        string? targetName;
        switch (checkedRequest.CompareTarget)
        {
            case PlcOnlinePolicy.CompareTargets.Plc:
                var other = PlcSoftwareLocator.FindUnique(project, checkedRequest.TargetName);
                if (other.Software.Equals(plc.Software))
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                        $"targetName '{checkedRequest.TargetName}' resolves to the compared PLC '{plc.Software.Name}' itself; name another PLC.");
                target = Subject(other, checkedRequest.HardwareScope);
                targetName = other.Software.Name;
                break;
            case PlcOnlinePolicy.CompareTargets.MasterCopy:
                target = PlcCompareService.Library(project).MasterCopyFolder.MasterCopies.Find(checkedRequest.TargetName!)
                    ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                        $"No master copy named '{checkedRequest.TargetName}' is in the root master copy folder of the project library.");
                targetName = checkedRequest.TargetName;
                break;
            default:
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Unknown compareTarget '{checkedRequest.CompareTarget}'.");
        }

        var compareTo = Bind(left, target, checkedRequest.CompareTarget!);
        var result = PlcCompareService.NewResult(PlcOnlineMethods.CompareHardware, plc, checkedRequest.CompareTarget!, targetName, checkedRequest);
        return PlcCompareService.Project(Call(compareTo, left, target), result, checkedRequest);
    }

    public static PlcCompareResultInfo CompareHardwareToOnline(ProjectBase project, PlcOnlineRequestInfo request, Action<int> sleep)
    {
        var checkedRequest = Validate(request, online: true);
        MethodInfo? compareTo = null;
        HardwareObject? left = null;
        return PlcCompareService.RunOnline(project, checkedRequest, PlcOnlineMethods.CompareHardwareToOnline, sleep,
            (plc, provider) =>
            {
                if (provider is null)
                    throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                        $"PLC '{plc.Software.Name}' offers no OnlineProvider on its CPU device item, so there is no online hardware to compare. Nothing was contacted.");
                left = Subject(plc, checkedRequest.HardwareScope);
                // Bound before the legitimation handler and the compare: a provider that is no IHardwareCompareTarget never reaches the device.
                compareTo = Bind(left, provider, "online");
            },
            (_, provider) => Call(compareTo!, left!, provider!));
    }

    private static PlcOnlineRequestInfo Validate(PlcOnlineRequestInfo? request, bool online)
    {
        if (request is null) throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "The compare request is missing.");
        if (PlcOnlinePolicy.ValidateHardwareCompare(request.CompareTarget, request.TargetName, request.HardwareScope, request.MaxDepth, request.MaxElements, online) is { } error)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, error);
        return request;
    }

    /// <summary>The PLC's station (<c>Device</c>, default) or its CPU device item.</summary>
    private static HardwareObject Subject(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? scope)
    {
        var cpu = plc.HostDeviceItem ?? throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            $"PLC '{plc.Software.Name}' has no readable CPU device item, so its hardware cannot be compared.");
        if (scope == PlcOnlinePolicy.HardwareScopes.Cpu) return cpu;
        IEngineeringObject? current = cpu;
        for (var depth = 0; depth < 32 && current is not null; depth++)
        {
            if (current is Device device) return device;
            current = current.Parent;
        }

        throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
            $"The station of PLC '{plc.Software.Name}' could not be found above its CPU device item; use hardwareScope 'cpu'.");
    }

    /// <summary>
    /// <c>CompareTo</c> on the live hardware object accepting <paramref name="target"/>. When none accepts it but a <c>CompareTo</c>
    /// taking the documented interface exists, the target simply does not implement it: unsupported_capability with its runtime type.
    /// </summary>
    private static MethodInfo Bind(HardwareObject left, object target, string what)
    {
        var method = LibraryRuntimeBinding.Find(left.GetType(), "CompareTo", new[] { target.GetType() }, out var error);
        if (method is not null && method.GetParameters()[0].ParameterType.FullName == PlcOnlinePolicy.HardwareCompareTargetType) return method;
        var documented = left.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Any(candidate => candidate.Name == "CompareTo" && candidate.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType.FullName == PlcOnlinePolicy.HardwareCompareTargetType);
        if (documented)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"The {what} compare target is a '{target.GetType().FullName}', which does not implement {PlcOnlinePolicy.HardwareCompareTargetType} on this installation; "
                + "TIA Portal Openness offers no hardware compare against it. Nothing was compared.");
        throw new WorkerOperationException(WorkerFailureCategories.ApiMemberMissing,
            $"This TIA Portal installation exposes no HardwareObject.CompareTo({PlcOnlinePolicy.HardwareCompareTargetType}) on '{left.GetType().FullName}' ({error}). Nothing was compared.");
    }

    private static CompareResult Call(MethodInfo compareTo, HardwareObject left, object target)
    {
        object? returned;
        try
        {
            returned = compareTo.Invoke(left, new[] { target });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        return returned as CompareResult ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
            $"HardwareObject.CompareTo returned {(returned is null ? "null" : $"a '{returned.GetType().FullName}'")}, not the documented CompareResult.");
    }
}
