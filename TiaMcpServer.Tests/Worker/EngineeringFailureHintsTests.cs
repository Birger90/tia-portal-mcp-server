using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>Audit T6/T9/T11 (K89): exception types are matched by exact full name along the type chain; the texts name the cause.</summary>
public sealed class EngineeringFailureHintsTests
{
    [Fact]
    public void Types_AreMatchedByFullNameAlongTheTypeChain()
    {
        Assert.True(EngineeringFailureHints.Is(new Siemens.Engineering.MissingProductsException("x"), EngineeringFailureHints.MissingProductsException));
        Assert.True(EngineeringFailureHints.Is(new DerivedMissing(), EngineeringFailureHints.MissingProductsException));
        Assert.False(EngineeringFailureHints.Is(new InvalidOperationException("x"), EngineeringFailureHints.MissingProductsException));
        Assert.False(EngineeringFailureHints.Is(null, EngineeringFailureHints.MissingProductsException));
    }

    [Fact]
    public void Texts_NameTheCause_AndNeverPromiseASafeRetry()
    {
        var missing = EngineeringFailureHints.MissingProducts(@"C:\p\P.ap21", "Product X is missing");
        Assert.Contains("MissingProductsException: Product X is missing", missing);
        Assert.Contains("Nothing was opened", missing);
        Assert.Contains("UMAC", EngineeringFailureHints.Security("denied"));
        Assert.Contains("Do not repeat the same call unattended", EngineeringFailureHints.NonRecoverableRetry);
        Assert.DoesNotContain("safe", EngineeringFailureHints.NonRecoverableRetry);
    }

    private sealed class DerivedMissing : Siemens.Engineering.MissingProductsException
    {
        public DerivedMissing() : base("derived")
        {
        }
    }
}
