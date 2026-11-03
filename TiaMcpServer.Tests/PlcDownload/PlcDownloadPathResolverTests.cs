using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.PlcDownload;
using Xunit;
using static TiaMcpServer.Tests.PlcDownload.PlcDownloadTestData;

namespace TiaMcpServer.Tests.PlcDownload;

/// <summary>Exact, fallback-free resolution of mode → PC interface → target interface.</summary>
public sealed class PlcDownloadPathResolverTests
{
    [Fact]
    public void SoleTargetInterface_IsSelectedAutomatically()
    {
        var resolution = PlcDownloadPathResolver.Resolve(SinglePlc(), Item());

        Assert.True(resolution.Success);
        var effects = resolution.Effects!;
        Assert.Equal("PLC_1", effects.PlcName);
        Assert.Equal("Station_1", effects.DeviceName);
        Assert.Equal("PN/IE", effects.Path.Mode);
        Assert.Equal("PLCSIM", effects.Path.PcInterface.Name);
        Assert.Equal(1, effects.Path.PcInterface.Number);
        Assert.Equal("1 X1", effects.Path.TargetInterface);
        Assert.True(effects.TargetInterfaceSelectedAutomatically);
        Assert.True(effects.SimulationTarget);
        Assert.Equal("Hardware, Software", effects.DownloadOptions);
        Assert.True(effects.StartAfterDownload);
    }

    [Fact]
    public void ExplicitTargetInterface_IsNotAutomatic()
    {
        var effects = PlcDownloadPathResolver.Resolve(SinglePlc(), Item(target: "1 X1", scope: "software", start: false)).Effects!;
        Assert.False(effects.TargetInterfaceSelectedAutomatically);
        Assert.Equal("Software", effects.DownloadOptions);
        Assert.False(effects.StartAfterDownload);
    }

    [Fact]
    public void SeveralTargetInterfaces_WithoutChoice_AreAmbiguousWithCandidates()
    {
        var resolution = PlcDownloadPathResolver.Resolve(SinglePlc(), Item(pc: Hardware));

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, resolution.Error!.Category);
        Assert.Contains("'1 X1'", resolution.Error.Message);
        Assert.Contains("'1 X2'", resolution.Error.Message);
        Assert.Contains("Specify targetInterface", resolution.Error.Message);
    }

    [Fact]
    public void SeveralPlcsimInterfaces_WithAnOmittedPcInterface_AreAmbiguousBeforeAnyDefault()
    {
        var resolution = PlcDownloadPathResolver.Resolve(TwoPlcsimInterfaces(), Item());

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, resolution.Error!.Category);
        Assert.Contains("'PLCSIM' #1", resolution.Error.Message);
        Assert.Contains("'PLCSIM' #2", resolution.Error.Message);
        Assert.Contains("Specify pcInterface", resolution.Error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SeveralPlcsimInterfaces_ResolveWhenTheNumberIsExplicit(int number)
    {
        var effects = PlcDownloadPathResolver.Resolve(TwoPlcsimInterfaces(), Item(number: number, numberExplicit: true)).Effects!;
        Assert.Equal(number, effects.Path.PcInterface.Number);
        Assert.True(effects.SimulationTarget);
    }

    [Fact]
    public void HardwareInterface_ResolvesButIsMarkedNonSimulation()
    {
        var effects = PlcDownloadPathResolver.Resolve(SinglePlc(), Item(pc: Hardware, target: "1 X2")).Effects!;
        Assert.False(effects.SimulationTarget);
        Assert.Equal("1 X2", effects.Path.TargetInterface);
    }

    [Fact]
    public void SeveralEquallyNamedHardwareInterfaces_WithoutAnExplicitNumber_AreAmbiguous()
    {
        var inventory = new PlcDownloadTargetInventoryInfo
        {
            Plcs = { PlcEntry("PLC_1", "Station_1", true, Pc("PLCSIM", 1, "1 X1"), Pc(Hardware, 1, "1 X1"), Pc(Hardware, 2, "1 X1")) },
        };

        var ambiguous = PlcDownloadPathResolver.Resolve(inventory, Item(pc: Hardware, numberExplicit: false));
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, ambiguous.Error!.Category);
        Assert.Contains($"'{Hardware}' #1", ambiguous.Error.Message);
        Assert.Contains($"'{Hardware}' #2", ambiguous.Error.Message);
        Assert.DoesNotContain("'PLCSIM' #1", ambiguous.Error.Message);

        Assert.Equal(2, PlcDownloadPathResolver.Resolve(inventory, Item(pc: Hardware, number: 2, numberExplicit: true)).Effects!.Path.PcInterface.Number);
        // The default PLCSIM interface is unique here, so an omitted pcInterface still resolves.
        Assert.True(PlcDownloadPathResolver.Resolve(inventory, Item()).Effects!.SimulationTarget);
    }

    [Fact]
    public void Effects_CarryTheCallerDecisionsPasswordsAndPortalPolicy()
    {
        var item = Item(decisions: new[] { Select("ResetModule", "NoAction") }, passwords: new[] { Password("ModuleWriteAccessPassword", "PLC_PW") },
            portalConfirmations: PlcDownloadPolicy.PortalConfirmationPolicies.Accept);
        var effects = PlcDownloadPathResolver.Resolve(SinglePlc(), item).Effects!;
        Assert.Same(item.Decisions, effects.Decisions);
        Assert.Same(item.Passwords, effects.Passwords);
        Assert.Equal(PlcDownloadPolicy.PortalConfirmationPolicies.Accept, effects.PortalConfirmations);
    }

    [Fact]
    public void SeveralPlcs_AreAmbiguousWithCandidates()
    {
        var inventory = SinglePlc();
        inventory.Plcs.Add(PlcEntry("PLC_2", "Station_2", true, Pc("PLCSIM", 1, "1 X1")));

        var resolution = PlcDownloadPathResolver.Resolve(inventory, Item(plc: null));

        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, resolution.Error!.Category);
        Assert.Contains("'PLC_1' (device 'Station_1')", resolution.Error.Message);
        Assert.Contains("'PLC_2' (device 'Station_2')", resolution.Error.Message);
    }

    [Fact]
    public void NoPlc_IsNotFound()
        => Assert.Equal(WorkerFailureCategories.TargetNotFound,
            PlcDownloadPathResolver.Resolve(new PlcDownloadTargetInventoryInfo(), Item()).Error!.Category);

    [Fact]
    public void NotDownloadablePlc_IsUnsupportedKind()
    {
        var inventory = new PlcDownloadTargetInventoryInfo { Plcs = { PlcEntry("PLC_3", "Station_3", downloadable: false) } };
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, PlcDownloadPathResolver.Resolve(inventory, Item()).Error!.Category);
    }

    [Theory]
    [InlineData("PROFIBUS", "PLCSIM", 1, null, "Connection mode 'PROFIBUS'")]
    [InlineData("pn/ie", "PLCSIM", 1, null, "Connection mode 'pn/ie'")]
    [InlineData("PN/IE", "plcsim", 1, null, "PC interface 'plcsim' #1")]
    [InlineData("PN/IE", "PLCSIM", 2, null, "PC interface 'PLCSIM' #2")]
    [InlineData("PN/IE", "PLCSIM", 1, "1 x1", "Target interface '1 x1'")]
    [InlineData("PN/IE", "PLCSIM", 1, "2 X1", "Target interface '2 X1'")]
    public void EveryLevelMatchesExactly_WithoutFallback(string mode, string pc, int number, string? target, string named)
    {
        var resolution = PlcDownloadPathResolver.Resolve(SinglePlc(), Item(pc: pc, number: number, target: target, mode: mode));

        Assert.False(resolution.Success);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, resolution.Error!.Category);
        Assert.Contains(named, resolution.Error.Message);
    }

    [Fact]
    public void DuplicateTargetName_IsAmbiguous()
    {
        var inventory = new PlcDownloadTargetInventoryInfo { Plcs = { PlcEntry("PLC_1", "Station_1", true, Pc("PLCSIM", 1, "1 X1", "1 X1")) } };
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous,
            PlcDownloadPathResolver.Resolve(inventory, Item(target: "1 X1")).Error!.Category);
    }

    [Fact]
    public void PcInterfaceWithoutTargets_IsNotFound()
    {
        var inventory = new PlcDownloadTargetInventoryInfo { Plcs = { PlcEntry("PLC_1", "Station_1", true, Pc("PLCSIM", 1)) } };
        var error = PlcDownloadPathResolver.Resolve(inventory, Item()).Error!;
        Assert.Equal(WorkerFailureCategories.TargetNotFound, error.Category);
        Assert.Contains("No target interface", error.Message);
    }

    [Fact]
    public void CandidateLists_AreBounded()
    {
        var inventory = new PlcDownloadTargetInventoryInfo
        {
            Plcs = { PlcEntry("PLC_1", "Station_1", true, Pc("PLCSIM", 1, Enumerable.Range(1, 30).Select(n => $"T{n}").ToArray())) },
        };
        var error = PlcDownloadPathResolver.Resolve(inventory, Item()).Error!;
        Assert.Contains("'T20'", error.Message);
        Assert.DoesNotContain("'T21'", error.Message);
        Assert.Contains("and 10 more", error.Message);
    }
}
