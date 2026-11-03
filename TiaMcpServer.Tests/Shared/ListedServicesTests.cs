using TiaMcpServer.Contracts.Shared;
using Xunit;

namespace TiaMcpServer.Tests.Shared;

/// <summary>
/// K76 (live 2026-11-03): nine CPU services were listed by GetServiceInfos() but GetService(Type) returned null. The shared
/// retrieval tries the non-generic path first, then the documented generic GetService&lt;T&gt;() closed over the listed type.
/// </summary>
public sealed class ListedServicesTests
{
    public interface IFakeService { }

    public sealed class SysLogConfigurationManager : IFakeService { }

    public sealed class NotAService { }

    /// <summary>Like the live CPU item: the non-generic path returns null, the generic one returns the service.</summary>
    private sealed class GenericOnly : IServiceProvider
    {
        public List<Type> Generic { get; } = new();

        public object? GetService(Type serviceType) => null;

        public T GetService<T>() where T : class, IFakeService
        {
            Generic.Add(typeof(T));
            return (T)(object)new SysLogConfigurationManager();
        }
    }

    private sealed class NonGeneric : IServiceProvider
    {
        public bool GenericCalled { get; private set; }

        public object? GetService(Type serviceType) => new SysLogConfigurationManager();

        public T? GetService<T>() where T : class, IFakeService
        {
            GenericCalled = true;
            return null;
        }
    }

    private sealed class Neither : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;

        public T? GetService<T>() where T : class, IFakeService => null;
    }

    private sealed class Throwing : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;

        public T GetService<T>() where T : class, IFakeService => throw new InvalidOperationException("engineering refusal");
    }

    [Fact]
    public void Retrieve_FallsBackToTheGenericGetService_WhenGetServiceTypeReturnsNull()
    {
        var owner = new GenericOnly();
        Assert.IsType<SysLogConfigurationManager>(ListedServices.Retrieve(owner, typeof(SysLogConfigurationManager)));
        Assert.Equal(new[] { typeof(SysLogConfigurationManager) }, owner.Generic);
    }

    [Fact]
    public void Retrieve_UsesTheNonGenericPathFirst()
    {
        var owner = new NonGeneric();
        Assert.NotNull(ListedServices.Retrieve(owner, typeof(SysLogConfigurationManager)));
        Assert.False(owner.GenericCalled);
    }

    [Fact]
    public void Retrieve_IsNull_WhenNeitherPathReturnsIt_OrTheTypeViolatesTheConstraint()
    {
        Assert.Null(ListedServices.Retrieve(new Neither(), typeof(SysLogConfigurationManager)));
        Assert.Null(ListedServices.Retrieve(new GenericOnly(), typeof(NotAService)));
        Assert.Null(ListedServices.Retrieve(new object(), typeof(SysLogConfigurationManager)));
    }

    [Fact]
    public void Retrieve_PropagatesTheEngineeringExceptionUnwrapped()
        => Assert.Equal("engineering refusal",
            Assert.Throws<InvalidOperationException>(() => ListedServices.Retrieve(new Throwing(), typeof(SysLogConfigurationManager))).Message);

    [Fact]
    public void UnavailableReason_NamesBothRetrievalPaths()
    {
        var reason = ListedServices.UnavailableReason("Siemens.Engineering.HW.Features.SystemWebPagesFeature");
        Assert.Contains("System.IServiceProvider.GetService(Type)", reason);
        Assert.Contains("IEngineeringServiceProvider.GetService<T>()", reason);
    }
}

/// <summary>K76: the settings family keeps searching the CPU's owners when one lists a service it does not return.</summary>
public sealed class PlcSettingsFindServiceTests
{
    private sealed class Owner(string name, object? service) { public string Name { get; } = name; public object? Service { get; } = service; }

    private static (object? Service, object? Owner, string? Unavailable) Find(TiaMcpServer.Contracts.Plc.Settings.PlcSettingsContext context,
        IEnumerable<object> owners, string fullName)
    {
        var method = typeof(TiaMcpServer.Contracts.Plc.Settings.PlcSettingsRun).GetMethod("FindService",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return ((object?, object?, string?))method.Invoke(null, new object[] { context, owners, fullName })!;
    }

    [Fact]
    public void FindService_ALaterOwnerThatReturnsTheService_Wins_OverAnEarlierListedButUnavailableOne()
    {
        var service = new ListedServicesTests.SysLogConfigurationManager();
        var cpu = new Owner("cpu", null);
        var item = new Owner("item", service);
        var type = typeof(ListedServicesTests.SysLogConfigurationManager);
        var context = new TiaMcpServer.Contracts.Plc.Settings.PlcSettingsContext
        {
            ServiceTypes = _ => new[] { type },
            Service = (owner, _) => ((Owner)owner).Service,
        };

        var found = Find(context, new object[] { cpu, item }, type.FullName!);
        Assert.Same(service, found.Service);
        Assert.Same(item, found.Owner);
        Assert.Null(found.Unavailable);

        var none = Find(context, new object[] { cpu, new Owner("other", null) }, type.FullName!);
        Assert.Null(none.Service);
        Assert.Same(cpu, none.Owner);
        Assert.Equal(ListedServices.UnavailableReason(type.FullName!), none.Unavailable);
    }
}
