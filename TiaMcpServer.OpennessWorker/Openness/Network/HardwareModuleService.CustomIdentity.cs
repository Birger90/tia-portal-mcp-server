using Siemens.Engineering;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.TechnologyObjects;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// network_read get_custom_identity and network_write set_custom_identity / remove_custom_identity (GitHub issue #18, not
/// live-tested). The V21 reference (Siemens.Engineering.Base.xml) documents <c>CustomIdentityProvider.Get/Set/Remove</c> but not
/// which hardware objects offer the provider, so it is taken from the device's or device item's <c>GetServiceInfos()</c> by exact
/// full name and retrieved with <c>IServiceProvider.GetService(Type)</c>; not listed or not returned is unsupported_capability.
/// The calls are the Siemens-free <see cref="CustomIdentityRun"/>; no stub member was added.
/// </summary>
internal static partial class HardwareModuleService
{
    public static HardwareCustomIdentityReadInfo GetCustomIdentity(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var (target, provider, key) = CustomIdentityProvider(project, request);
        return new HardwareCustomIdentityReadInfo
        {
            Target = target.Identity,
            Key = key,
            Observed = Translate(() => CustomIdentityRun.Get(provider, key)),
            ProviderType = provider.GetType().FullName ?? provider.GetType().Name,
        };
    }

    public static HardwareCustomIdentityWriteResultInfo SetCustomIdentity(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var (target, provider, key) = CustomIdentityProvider(project, request);
        var value = request.CustomIdentityValue ?? throw Validation("set_custom_identity requires a value.");
        var (before, after) = Translate(() => CustomIdentityRun.Set(provider, key, value, Describe(target)));
        return new HardwareCustomIdentityWriteResultInfo
        {
            Operation = HardwareModuleMethods.SetCustomIdentity, Target = target.Identity, Key = key, RequestedValue = value,
            Before = before, After = after, ProviderType = provider.GetType().FullName ?? provider.GetType().Name,
        };
    }

    public static HardwareCustomIdentityWriteResultInfo RemoveCustomIdentity(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var (target, provider, key) = CustomIdentityProvider(project, request);
        var (before, after) = Translate(() => CustomIdentityRun.Remove(provider, key, Describe(target)));
        return new HardwareCustomIdentityWriteResultInfo
        {
            Operation = HardwareModuleMethods.RemoveCustomIdentity, Target = target.Identity, Key = key, RequestedValue = null,
            Before = before, After = after, ProviderType = provider.GetType().FullName ?? provider.GetType().Name,
        };
    }

    private static (ResolvedHardwareObject Target, object Provider, string Key) CustomIdentityProvider(ProjectBase project, HardwareModuleRequestInfo request)
    {
        var key = request.CustomIdentityKey;
        if (string.IsNullOrWhiteSpace(key)) throw Validation("A custom identity key is required.");
        var target = Resolve(project, request.Target, "target");
        var offered = TechnologyObjectRuntime.ServiceTypes(target.Object);
        var (provider, unavailable) = Translate(() => RuntimeServiceValues.Find(offered,
            type => TechnologyObjectRuntime.TryService(target.Object, offered, type.FullName!, out _), CustomIdentityNames.Provider));
        if (provider is null)
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                (unavailable ?? RuntimeServiceValues.NotListed(Describe(target), CustomIdentityNames.Provider)) + " Nothing was changed.");
        return (target, provider, key!);
    }

    private static string Describe(ResolvedHardwareObject target)
        => target.Item is null
            ? $"Device '{target.Identity.DeviceName}'"
            : $"Device item '{target.Identity.Name}' of '{target.Identity.DeviceName}'";

    private static T Translate<T>(Func<T> run)
    {
        try
        {
            return run();
        }
        catch (RuntimeBindingException exception)
        {
            throw new WorkerOperationException(exception.Category, exception.Message);
        }
    }
}
