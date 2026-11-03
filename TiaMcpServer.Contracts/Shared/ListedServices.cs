using System.Reflection;
using System.Runtime.ExceptionServices;

namespace TiaMcpServer.Contracts.Shared;

/// <summary>
/// Services an Openness object lists in <c>IEngineeringServiceProvider.GetServiceInfos()</c> but does not return from
/// <c>GetService</c>. Seen live on TIA Portal V21 on 2026-11-02: <c>Siemens.Engineering.Safety.SafetySignatureProvider</c>
/// of a block (plc_read list_safety_blocks, defect D13) and
/// <c>Siemens.Engineering.HW.Features.PlcAccessControlConfigurationProvider</c> of an S7-1500 CPU (plc_read
/// get_plc_security, defect D14); on 2026-11-03 nine CPU services of get_cpu_settings/get_cpu_certificates (K76). A read for
/// which the service is optional reports that part as unavailable with this reason and returns everything else; a write that
/// needs the service fails <c>unsupported_capability</c> before any change. Shared by the worker and the FakeWorker so both
/// word it alike. Siemens-free.
/// </summary>
public static class ListedServices
{
    /// <summary>The non-generic retrieval path (the Siemens skill's <c>((IServiceProvider)target).GetService(type)</c>).</summary>
    public const string NonGenericPath = "System.IServiceProvider.GetService(Type)";

    /// <summary>
    /// The documented generic retrieval path (V21: <c>M:Siemens.Engineering.HW.HardwareObject.GetService``1</c>,
    /// <c>M:Siemens.Engineering.ProjectBase.GetService``1</c>, ... and <c>IEngineeringServiceProvider.GetService&lt;T&gt;()</c>),
    /// closed over the listed type with <c>MakeGenericMethod</c>.
    /// </summary>
    public const string GenericPath = "IEngineeringServiceProvider.GetService<T>()";

    private const string EngineeringServiceProvider = "Siemens.Engineering.IEngineeringServiceProvider";

    /// <summary>The reason a listed service is unavailable, naming the retrieval paths tried.</summary>
    public static string UnavailableReason(string fullName)
        => $"GetServiceInfos() lists '{fullName}', but GetService returned null for it (tried {NonGenericPath} and {GenericPath}); "
            + "this part is unavailable through Openness here.";

    /// <summary>
    /// K76: retrieves a listed service by <see cref="NonGenericPath"/> and, when that is absent or returns null, by
    /// <see cref="GenericPath"/> (the generic <c>GetService</c> method of the object's type or of its
    /// <c>Siemens.Engineering.IEngineeringServiceProvider</c> interface, closed over <paramref name="type"/>; a type that violates
    /// the method's constraint counts as not returned). Null when neither returns it. An exception thrown by Openness inside
    /// either call propagates unwrapped, so callers classify it as before.
    /// </summary>
    public static object? Retrieve(object owner, Type type)
    {
        if (owner is null) throw new ArgumentNullException(nameof(owner));
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (owner is IServiceProvider provider && provider.GetService(type) is { } service) return service;
        var generic = GenericGetService(owner.GetType());
        if (generic is null) return null;
        MethodInfo closed;
        try
        {
            closed = generic.MakeGenericMethod(type);
        }
        catch (ArgumentException)
        {
            return null;
        }

        try
        {
            return closed.Invoke(owner, Array.Empty<object>());
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo? GenericGetService(Type ownerType)
    {
        static bool IsGetService(MethodInfo method)
            => method.Name == "GetService" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1
                && method.GetParameters().Length == 0;

        var own = ownerType.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(IsGetService).ToList();
        if (own.Count == 1) return own[0];
        var viaInterface = ownerType.GetInterfaces()
            .Where(candidate => string.Equals(candidate.FullName, EngineeringServiceProvider, StringComparison.Ordinal))
            .SelectMany(candidate => candidate.GetMethods())
            .Where(IsGetService).ToList();
        return viaInterface.Count == 1 ? viaInterface[0] : null;
    }
}
