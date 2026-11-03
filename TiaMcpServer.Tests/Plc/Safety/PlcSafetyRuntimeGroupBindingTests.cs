using TiaMcpServer.Contracts.Plc.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Plc.Safety;

/// <summary>
/// The Siemens-free binding of set_safety_runtime_group and the re-link after a block import (E7, live 2026-11-02) against
/// plain stand-ins. The stand-ins model member shapes only; they are no evidence of how the live RuntimeGroup exposes them.
/// </summary>
public sealed class PlcSafetyRuntimeGroupBindingTests
{
    private class BlockStandIn
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class FbStandIn : BlockStandIn;

    private sealed class WritableGroup
    {
        public BlockStandIn? MainSafetyBlock { get; set; }

        public BlockStandIn? MainSafetyBlockIDB { get; set; }
    }

    private sealed class ReadOnlyGroup
    {
        public BlockStandIn? MainSafetyBlock => null;
    }

    private sealed class WrongTypeGroup
    {
        public string? MainSafetyBlock { get; set; }
    }

    [Fact]
    public void AWritablePropertyAssignableFromTheBlockType_IsTheFirstRoute()
    {
        var group = new WritableGroup();
        var assignment = PlcSafetyRuntimeGroupBinding.Bind(group, PlcSafetyRuntimeGroupBinding.MainSafetyBlock, typeof(FbStandIn), null);

        Assert.Equal(PlcSafetyRuntimeGroupBinding.Routes.Property, assignment.Route);
        Assert.Equal("BlockStandIn MainSafetyBlock { set; }", assignment.Signature);
        Assert.Null(group.MainSafetyBlock);
        var block = new FbStandIn { Name = "Main_Safety_RTG1" };
        assignment.Apply(block);
        Assert.Same(block, group.MainSafetyBlock);
    }

    [Fact]
    public void WithoutAUsableProperty_AWritableListedAttributeIsSetWithSetAttribute()
    {
        var calls = new List<(string, object)>();
        var surface = new PlcSafetyRuntimeGroupBinding.AttributeSurface(
            () => new[] { ("Name", false), (PlcSafetyRuntimeGroupBinding.MainSafetyBlock, true) }, (name, value) => calls.Add((name, value)));

        foreach (var group in new object[] { new ReadOnlyGroup(), new WrongTypeGroup(), new object() })
        {
            var assignment = PlcSafetyRuntimeGroupBinding.Bind(group, PlcSafetyRuntimeGroupBinding.MainSafetyBlock, typeof(FbStandIn), surface);
            Assert.Equal(PlcSafetyRuntimeGroupBinding.Routes.SetAttribute, assignment.Route);
            assignment.Apply(new FbStandIn());
        }

        Assert.Equal(3, calls.Count);
        Assert.All(calls, c => Assert.Equal(PlcSafetyRuntimeGroupBinding.MainSafetyBlock, c.Item1));
    }

    [Fact]
    public void NeitherRoute_IsApiMemberMissing_AndNothingIsCalled()
    {
        var calls = 0;
        var readOnlyAttribute = new PlcSafetyRuntimeGroupBinding.AttributeSurface(
            () => new[] { (PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb, false) }, (_, _) => calls++);

        var missing = Assert.Throws<PlcSafetyApiMissingException>(() =>
            PlcSafetyRuntimeGroupBinding.Bind(new ReadOnlyGroup(), PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb, typeof(FbStandIn), readOnlyAttribute));
        Assert.Contains("not writable", missing.Message);
        Assert.Contains("api_member_missing", missing.Message);
        Assert.Throws<PlcSafetyApiMissingException>(() =>
            PlcSafetyRuntimeGroupBinding.Bind(new WrongTypeGroup(), PlcSafetyRuntimeGroupBinding.MainSafetyBlock, typeof(FbStandIn), null));
        Assert.Equal(0, calls);
        Assert.Equal(PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdbName, PlcSafetyRuntimeGroupBinding.ReadBackAttribute(PlcSafetyRuntimeGroupBinding.MainSafetyBlockIdb));
        Assert.Equal(PlcSafetyRuntimeGroupBinding.MainSafetyBlockName, PlcSafetyRuntimeGroupBinding.ReadBackAttribute(PlcSafetyRuntimeGroupBinding.MainSafetyBlock));
    }

    /// <summary>Route (iii), tried only when GetAttributeInfos lists the name attribute as writable (not live-tested).</summary>
    [Theory]
    [InlineData("ReadWrite")]
    [InlineData("Write")]
    public void AWritableNameAttribute_IsSetWithTheBlockName_WhenNeitherBlockRouteExists(string accessMode)
    {
        var calls = new List<(string, object)>();
        var surface = new PlcSafetyRuntimeGroupBinding.AttributeSurface(
            () => new[] { new PlcSafetyRuntimeGroupBinding.AttributeAccess("Name", "Read"), new PlcSafetyRuntimeGroupBinding.AttributeAccess(PlcSafetyRuntimeGroupBinding.MainSafetyBlockName, accessMode) },
            (name, value) => calls.Add((name, value)));

        var assignment = PlcSafetyRuntimeGroupBinding.Bind(new ReadOnlyGroup(), PlcSafetyRuntimeGroupBinding.MainSafetyBlock, typeof(FbStandIn), surface);

        Assert.Equal(PlcSafetyRuntimeGroupBinding.Routes.SetAttributeName, assignment.Route);
        Assert.Equal("IEngineeringObject.SetAttribute(\"MainSafetyBlockName\", String)", assignment.Signature);
        assignment.Apply(new FbStandIn { Name = "Main_Safety_RTG1" }, "Main_Safety_RTG1");
        assignment.Apply(new FbStandIn { Name = "From_Property" });
        Assert.Equal(new[] { ("MainSafetyBlockName", (object)"Main_Safety_RTG1"), ("MainSafetyBlockName", (object)"From_Property") }, calls);
    }

    /// <summary>L2 (live, V21, 2026-11-02): neither route existed; the failure lists every attribute the group reported with its access mode.</summary>
    [Fact]
    public void WithoutAnyWritableRoute_TheFailureListsTheReportedAttributesWithTheirAccessModes()
    {
        var calls = 0;
        var surface = new PlcSafetyRuntimeGroupBinding.AttributeSurface(
            () => new[]
            {
                new PlcSafetyRuntimeGroupBinding.AttributeAccess("Name", "Read"),
                new PlcSafetyRuntimeGroupBinding.AttributeAccess(PlcSafetyRuntimeGroupBinding.MainSafetyBlockName, "Read"),
                new PlcSafetyRuntimeGroupBinding.AttributeAccess("MaximumCycleTime", "ReadWrite"),
            },
            (_, _) => calls++);

        var missing = Assert.Throws<PlcSafetyApiMissingException>(() =>
            PlcSafetyRuntimeGroupBinding.Bind(new object(), PlcSafetyRuntimeGroupBinding.MainSafetyBlock, typeof(FbStandIn), surface));

        Assert.Equal(0, calls);
        Assert.Contains("GetAttributeInfos() lists no attribute 'MainSafetyBlock' and 'MainSafetyBlockName' as not writable (Read)", missing.Message);
        Assert.Contains("the attributes GetAttributeInfos() reported: Name (Read), MainSafetyBlockName (Read), MaximumCycleTime (ReadWrite)", missing.Message);
        Assert.Contains("live on TIA Portal V21, 2026-11-02", missing.Message);
    }

    [Fact]
    public void Affected_FindsTheGroupsThatNameTheBlock_AsTiaPortalComparesNames()
    {
        var groups = new[]
        {
            new PlcMainSafetyLink("F-Ablaufgruppe 1", "Main_Safety_RTG1", "Main_Safety_RTG1_DB"),
            new PlcMainSafetyLink("F-Ablaufgruppe 2", "Main_Safety_RTG2", "Main_Safety_RTG2_DB"),
            new PlcMainSafetyLink("Unreadable", null, null),
        };

        Assert.Equal("F-Ablaufgruppe 1", Assert.Single(PlcMainSafetyLinks.Affected(groups, "main_safety_rtg1")).RuntimeGroup);
        Assert.Equal("F-Ablaufgruppe 2", Assert.Single(PlcMainSafetyLinks.Affected(groups, "Main_Safety_RTG2_DB")).RuntimeGroup);
        Assert.Empty(PlcMainSafetyLinks.Affected(groups, "FB_Other"));
    }

    /// <summary>E7: the SD import left MainSafetyBlockName null and MainSafetyBlockIDbName unchanged; only the lost reference is restored.</summary>
    [Fact]
    public void Reconcile_RestoresOnlyTheLostReference_AndWordsTheWarning()
    {
        var before = new[] { new PlcMainSafetyLink("F-Ablaufgruppe 1", "Main_Safety_RTG1", "Main_Safety_RTG1_DB") };
        var current = new PlcMainSafetyLink("F-Ablaufgruppe 1", null, "Main_Safety_RTG1_DB");
        var restored = new List<(string, string, string)>();

        var outcomes = PlcMainSafetyLinks.Reconcile(before, "Main_Safety_RTG1", _ => current,
            (group, member, expected) =>
            {
                restored.Add((group, member, expected));
                current = current with { MainSafetyBlockName = expected };
                return ("IEngineeringObject.SetAttribute(\"MainSafetyBlock\", PlcBlock)", current.MainSafetyBlockName);
            },
            _ => false);

        var outcome = Assert.Single(outcomes);
        Assert.True(outcome.Lost);
        Assert.True(outcome.Restored);
        Assert.Equal(("F-Ablaufgruppe 1", PlcSafetyRuntimeGroupBinding.MainSafetyBlock, "Main_Safety_RTG1"), Assert.Single(restored));
        var warning = PlcMainSafetyLinks.Warning(outcome, "PLC_1", "PLC_1/Main_Safety_RTG1");
        Assert.Contains("was restored", warning);
        Assert.Contains("'null' after it", warning);
    }

    [Fact]
    public void Reconcile_ThatCannotRestore_NamesTheRepair_AndAStillValidReferenceIsNotLost()
    {
        var before = new[]
        {
            new PlcMainSafetyLink("RTG1", "Main_Safety_RTG1", "Main_Safety_RTG1_DB"),
            new PlcMainSafetyLink("RTG2", "Main_Safety_RTG1", null),
        };
        var after = new Dictionary<string, PlcMainSafetyLink>
        {
            ["RTG1"] = new("RTG1", null, "Main_Safety_RTG1_DB"),
            ["RTG2"] = new("RTG2", "Main_Safety_RTG1", null),
        };

        var outcomes = PlcMainSafetyLinks.Reconcile(before, "Main_Safety_RTG1", group => after[group],
            (_, member, _) => throw new PlcSafetyApiMissingException("Siemens.Engineering.Safety.RuntimeGroup." + member, "no route"),
            _ => false);

        Assert.Equal(2, outcomes.Count);
        var lost = Assert.Single(outcomes, o => o.Lost);
        Assert.False(lost.Restored);
        Assert.Contains("no route", lost.Problem);
        var warning = PlcMainSafetyLinks.Warning(lost, "PLC_1", "PLC_1/Main_Safety_RTG1");
        Assert.Contains("could NOT be restored", warning);
        Assert.Contains("set_safety_runtime_group", warning);
        Assert.Contains("Safety Administration", warning);
        Assert.False(outcomes.Single(o => o.RuntimeGroup == "RTG2").Lost);
    }

    [Fact]
    public void Reconcile_LetsAFatalExceptionEscape()
    {
        var before = new[] { new PlcMainSafetyLink("RTG1", "B", null) };
        Assert.Throws<OutOfMemoryException>(() => PlcMainSafetyLinks.Reconcile(before, "B",
            _ => throw new OutOfMemoryException(), (_, _, _) => ("x", "B"), exception => exception is OutOfMemoryException));
    }

    [Fact]
    public void PlanningNote_NamesTheSimaticSdEvidenceOnlyForThatRoute()
    {
        var links = new[] { new PlcMainSafetyLink("F-Ablaufgruppe 1", "Main_Safety_RTG1", "Main_Safety_RTG1_DB") };
        Assert.Contains("SIMATIC SD import", PlcMainSafetyLinks.PlanningNote("op", "PLC_1/Main_Safety_RTG1", links, simaticSdRoute: true));
        Assert.Contains("SimaticML route (set_network_text) kept the reference", PlcMainSafetyLinks.PlanningNote("op", "PLC_1/Main_Safety_RTG1", links, simaticSdRoute: true));
        Assert.DoesNotContain("SIMATIC SD import", PlcMainSafetyLinks.PlanningNote("op", "PLC_1/Main_Safety_RTG1", links, simaticSdRoute: false));
    }
}
