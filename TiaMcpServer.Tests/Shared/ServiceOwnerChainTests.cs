using Xunit;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Tests.Shared;

public class ServiceOwnerChainTests
{
    private static ServiceOwnerChain.Owner Owner(string description) => new(description, new object());

    [Fact]
    public void Find_ReturnsTheFirstOwnerThatReturnsTheService_AndStopsThere()
    {
        var software = Owner("PLC software 'PLC_1'");
        var cpu = Owner("CPU device item 'PLC_1'");
        var device = Owner("device 'S7-1500'");
        var service = new object();
        var asked = new List<object>();

        var result = ServiceOwnerChain.Find(new[] { software, cpu, device }, owner =>
        {
            asked.Add(owner);
            return owner == cpu.Instance ? (service, null) : (null, null);
        });

        Assert.Same(service, result.Service);
        Assert.Equal("CPU device item 'PLC_1'", result.FoundOn);
        Assert.Equal(new[] { software.Instance, cpu.Instance }, asked);
        Assert.Equal(new[] { "PLC software 'PLC_1'", "CPU device item 'PLC_1'" }, result.Searched);
    }

    [Fact]
    public void Find_KeepsTheFormerBehaviourWhenThePlcSoftwareOffersTheService()
    {
        var service = new object();
        var result = ServiceOwnerChain.Find(new[] { Owner("PLC software 'PLC_1'"), Owner("device 'D'") }, _ => (service, null));

        Assert.Same(service, result.Service);
        Assert.Single(result.Searched);
    }

    [Fact]
    public void Find_NamesEveryObjectSearchedAndEveryUnavailableReason_WhenNoneOffersTheService()
    {
        var listedButNull = Owner("CPU device item 'PLC_1'");
        var result = ServiceOwnerChain.Find(new[] { Owner("PLC software 'PLC_1'"), listedButNull, Owner("device 'D'") },
            owner => owner == listedButNull.Instance ? (null, "GetService returned null.") : (null, null));

        Assert.Null(result.Service);
        Assert.Null(result.FoundOn);
        var reason = result.Reason("Siemens.Engineering.SW.Loader.LoadableProvider");
        Assert.Contains("'Siemens.Engineering.SW.Loader.LoadableProvider'", reason, StringComparison.Ordinal);
        Assert.Contains("PLC software 'PLC_1', CPU device item 'PLC_1', device 'D'", reason, StringComparison.Ordinal);
        Assert.Contains("CPU device item 'PLC_1': GetService returned null.", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_SearchesAtMostMaxObjects()
    {
        var owners = Enumerable.Range(0, ServiceOwnerChain.MaxObjects + 5).Select(i => Owner($"item {i}")).ToList();
        var result = ServiceOwnerChain.Find(owners, _ => (null, null));

        Assert.Equal(ServiceOwnerChain.MaxObjects, result.Searched.Count);
    }

    [Fact]
    public void Find_DoesNotSwallowALookupFailure()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ServiceOwnerChain.Find(new[] { Owner("PLC software 'PLC_1'") }, _ => throw new InvalidOperationException("portal lost")));
    }
}
