using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.Screens;

/// <summary>The worker's screen writes against the offline Unified doubles (runtime-bound members modelled there).</summary>
[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiScreenWriterTests
{
    private static HmiWriteRequestInfo Request(Action<HmiWriteRequestInfo> configure)
    {
        var request = new HmiWriteRequestInfo { HmiName = "Runtime" };
        configure(request);
        return request;
    }

    [Fact]
    public void CreateScreenAndItem_BindCreateAtRuntimeAndSetGeometryByCurrentType()
    {
        var software = new HmiSoftware();

        var screen = HmiScreenWriter.CreateScreen(software, Request(r => { r.ScreenName = "Detail"; r.Width = 800; }));
        var item = HmiScreenWriter.CreateScreenItem(software, Request(r => { r.ScreenName = "Detail"; r.ItemName = "Start"; r.ItemType = "HmiButton"; r.Left = 5; r.Width = 90; }));

        Assert.Equal(new[] { "Create", "Width" }, screen.AppliedProperties);
        Assert.Equal(800u, software.Screens.Find("Detail")!.Width);
        Assert.Equal(new[] { "Create", "Left", "Width" }, item.AppliedProperties);
        var button = Assert.IsType<HmiButton>(Assert.Single(software.Screens.Find("Detail")!.ScreenItems));
        Assert.Equal((5, 90u), (button.Left, button.Width));
    }

    [Fact]
    public void CreateScreenItem_UnknownTypeIsApiMemberMissingAndNothingIsCreated()
    {
        var software = new HmiSoftware();
        software.Screens.Create("Detail");

        var error = Assert.Throws<WorkerOperationException>(() => HmiScreenWriter.CreateScreenItem(software, Request(r => { r.ScreenName = "Detail"; r.ItemName = "G"; r.ItemType = "HmiGauge"; })));

        Assert.Equal(WorkerFailureCategories.ApiMemberMissing, error.FailureCategory);
        Assert.Empty(software.Screens.Find("Detail")!.ScreenItems);
    }

    [Fact]
    public void SetProperty_ConvertsEnumsAndRefusesUnknownMembersAndReadOnlyAttributes()
    {
        var software = new HmiSoftware();
        software.Screens.Create("Main").ScreenItems.Create<HmiButton>("B");

        HmiScreenWriter.SetScreenItemProperty(software, Request(r => { r.ScreenName = "Main"; r.ItemName = "B"; r.PropertyName = "Align"; r.Value = "Right"; }));
        var bad = Assert.Throws<WorkerOperationException>(() => HmiScreenWriter.SetScreenItemProperty(software, Request(r => { r.ScreenName = "Main"; r.ItemName = "B"; r.PropertyName = "Align"; r.Value = "Middle"; })));
        var missing = Assert.Throws<WorkerOperationException>(() => HmiScreenWriter.SetScreenItemProperty(software, Request(r => { r.ScreenName = "Main"; r.ItemName = "B"; r.PropertyName = "Nope"; r.Value = "1"; })));

        Assert.Equal(HmiTextAlign.Right, ((HmiButton)software.Screens.Find("Main")!.ScreenItems.Single()).Align);
        Assert.Equal(WorkerFailureCategories.ValidationError, bad.FailureCategory);
        Assert.Contains("Left, Center, Right", bad.Message);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.FailureCategory);
    }

    [Fact]
    public void SetDynamizationTag_CreatesATagDynamizationOrUpdatesTheExistingOne()
    {
        var software = new HmiSoftware();
        var button = software.Screens.Create("Main").ScreenItems.Create<HmiButton>("B");

        HmiScreenWriter.SetDynamizationTag(software, Request(r => { r.ScreenName = "Main"; r.ItemName = "B"; r.PropertyName = "Visible"; r.TagName = "T1"; }));
        var outcome = HmiScreenWriter.SetDynamizationTag(software, Request(r => { r.ScreenName = "Main"; r.ItemName = "B"; r.PropertyName = "visible"; r.TagName = "T2"; }));

        var dynamization = Assert.IsType<TagDynamization>(Assert.Single(button.Dynamizations));
        Assert.Equal(("Visible", "T2"), (dynamization.PropertyName, dynamization.Tag));
        Assert.Equal(new[] { "Tag" }, outcome.AppliedProperties);
    }

    [Fact]
    public void ReplaceInScripts_ChangesEventAndDynamizationScriptsInOnePass()
    {
        var software = new HmiSoftware();
        var button = software.Screens.Create("Main").ScreenItems.Create<HmiButton>("B");
        var handler = new Siemens.Engineering.HmiUnified.UI.Events.EventHandler { EventType = "Tapped" };
        handler.Script.ScriptCode = "Tags('A').Write(Tags('B').Read());";
        button.EventHandlers.Items.Add(handler);
        button.Dynamizations.Items.Add(new ScriptDynamization { PropertyName = "Visible", ScriptCode = "return Tags('A').Read();" });

        var outcome = HmiScreenWriter.ReplaceInScripts(software, Request(r => r.Replacements = new()
        {
            new() { Find = "'A'", Replace = "'B'" }, new() { Find = "'B'", Replace = "'A'" },
        }));

        Assert.Equal("Tags('B').Write(Tags('A').Read());", handler.Script.ScriptCode);
        Assert.Equal("return Tags('B').Read();", ((ScriptDynamization)button.Dynamizations.Single()).ScriptCode);
        Assert.Equal(2, outcome.AppliedProperties.Count);
    }

    [Fact]
    public void DeleteScreenItemAndScreen_UseTheRuntimeDelete()
    {
        var software = new HmiSoftware();
        software.Screens.Create("Main").ScreenItems.Create<HmiButton>("B");

        HmiScreenWriter.DeleteScreenItem(software, Request(r => { r.ScreenName = "Main"; r.ItemName = "B"; }));
        HmiScreenWriter.DeleteScreen(software, Request(r => r.ScreenName = "Main"));

        Assert.Empty(software.Screens);
        Assert.Equal(WorkerFailureCategories.TargetNotFound,
            Assert.Throws<WorkerOperationException>(() => HmiScreenWriter.DeleteScreen(software, Request(r => r.ScreenName = "Main"))).FailureCategory);
    }
}
