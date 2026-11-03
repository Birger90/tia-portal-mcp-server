using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi.UnifiedExtensions;

/// <summary>The worker's WinCC Unified event and dynamization writes and reads (issue #29) against the offline doubles.</summary>
[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiScreenEventsTests
{
    private static (HmiSoftware Software, HmiButton Button) Fixture()
    {
        var software = new HmiSoftware();
        var button = software.Screens.Create("Main").ScreenItems.Create<HmiButton>("Start");
        return (software, button);
    }

    private static HmiWriteRequestInfo Request(Action<HmiWriteRequestInfo> configure)
    {
        var request = new HmiWriteRequestInfo { HmiName = "Runtime", ScreenName = "Main", ItemName = "Start" };
        configure(request);
        return request;
    }

    [Fact]
    public void SetEventScript_CreatesThenUpdatesTheHandlerOfTheTypedEnum()
    {
        var (software, button) = Fixture();

        var created = HmiScreenEvents.SetEventScript(software, Request(r => { r.EventType = "Tapped"; r.ScriptCode = "a();"; r.Async = true; }));
        var updated = HmiScreenEvents.SetEventScript(software, Request(r => { r.EventType = "Tapped"; r.ScriptCode = "b();"; r.GlobalDefinitionAreaScriptCode = "let x;"; }));
        var bad = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.SetEventScript(software, Request(r => { r.EventType = "Clicked"; r.ScriptCode = "c();"; })));

        Assert.Equal(new[] { "Create", "ScriptCode", "Async" }, created.AppliedProperties);
        Assert.Equal(new[] { "ScriptCode", "GlobalDefinitionAreaScriptCode" }, updated.AppliedProperties);
        var handler = Assert.Single(button.EventHandlers);
        Assert.Equal(("Tapped", "b();", "let x;", true), (handler.EventType, handler.Script.ScriptCode, handler.Script.GlobalDefinitionAreaScriptCode, handler.Script.Async));
        Assert.Equal(WorkerFailureCategories.ValidationError, bad.FailureCategory);
        Assert.Contains("None, Tapped, Up, Down", bad.Message);
    }

    [Fact]
    public void PropertyEvents_AreCreatedReadAndDeleted()
    {
        var (software, button) = Fixture();

        HmiScreenEvents.SetEventScript(software, Request(r => { r.PropertyName = "Visible"; r.EventType = "Change"; r.ScriptCode = "v();"; }));
        var read = HmiScreenEvents.ListEvents(software, new HmiQueryInfo { ScreenName = "Main", ItemName = "Start" });
        var deleted = HmiScreenEvents.DeleteEventScript(software, Request(r => { r.PropertyName = "Visible"; r.EventType = "Change"; }));
        var again = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.DeleteEventScript(software, Request(r => { r.PropertyName = "Visible"; r.EventType = "Change"; })));

        var row = Assert.Single(read.PropertyEvents);
        Assert.Equal(("Visible", "Change", "v();"), (row.PropertyName, row.EventType, row.ScriptCode));
        Assert.Equal("HmiButton", read.TypeName);
        Assert.Equal(new[] { "Delete", "readBack gone" }, deleted.AppliedProperties);
        Assert.Empty(button.PropertyEventHandlers);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, again.FailureCategory);
    }

    [Fact]
    public void ScriptDynamization_SetsTriggerAndIsListed()
    {
        var (software, button) = Fixture();

        var outcome = HmiScreenEvents.SetDynamizationScript(software, Request(r =>
        {
            r.PropertyName = "BackColor"; r.ScriptCode = "return 1;"; r.TriggerType = "Tags"; r.TriggerTags = new() { "A", "B" };
        }));
        var listed = HmiScreenEvents.ListDynamizations(software, new HmiQueryInfo { ScreenName = "Main", ItemName = "Start" });

        Assert.Equal(new[] { "Create", "ScriptCode", "Trigger.Type", "Trigger.Tags" }, outcome.AppliedProperties);
        var dynamization = Assert.IsType<ScriptDynamization>(Assert.Single(button.Dynamizations));
        Assert.Equal(new[] { "A", "B" }, dynamization.Trigger.Tags);
        var row = Assert.Single(listed.Dynamizations);
        Assert.Equal(("BackColor", "ScriptDynamization"), (row.PropertyName, row.TypeName));
        Assert.Contains(row.Values, v => v.Name == "Trigger.Tags" && v.Value == "A, B");
        Assert.Contains(row.Values, v => v.Name == "Trigger.Type" && v.Value == "Tags");
    }

    [Fact]
    public void FlashingDynamization_ConvertsColoursAndEnumsAndAnotherKindIsRefused()
    {
        var (software, button) = Fixture();
        HmiScreenWriter.SetDynamizationTag(software, Request(r => { r.PropertyName = "Text"; r.TagName = "T"; }));

        var flashing = HmiScreenEvents.SetDynamizationFlashing(software, Request(r => { r.PropertyName = "BackColor"; r.Color = "#FF0000"; r.FlashingRate = "Fast"; }));
        var refused = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.SetDynamizationFlashing(software, Request(r => { r.PropertyName = "Text"; })));
        var listed = HmiScreenEvents.ListDynamizations(software, new HmiQueryInfo { ScreenName = "Main", ItemName = "Start" });

        Assert.Equal(new[] { "Create", "Color", "FlashingRate" }, flashing.AppliedProperties);
        var dynamization = button.Dynamizations.OfType<FlashingDynamization>().Single();
        Assert.Equal((unchecked((int)0xFFFF0000), FlashingRate.Fast), (dynamization.Color.ToArgb(), dynamization.FlashingRate));
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, refused.FailureCategory);
        Assert.Contains("delete_dynamization", refused.Message);
        Assert.Contains(listed.Dynamizations.Single(d => d.PropertyName == "BackColor").Values, v => v.Name == "Color" && v.Value == "#FFFF0000");
        Assert.Contains(listed.Dynamizations.Single(d => d.PropertyName == "Text").Values, v => v.Name == "Tag" && v.Value == "T");
    }

    [Fact]
    public void DeleteDynamization_IsReadBack()
    {
        var (software, button) = Fixture();
        HmiScreenEvents.SetDynamizationScript(software, Request(r => { r.PropertyName = "Visible"; r.ScriptCode = "return true;"; }));

        var deleted = HmiScreenEvents.DeleteDynamization(software, Request(r => r.PropertyName = "Visible"));
        var missing = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.DeleteDynamization(software, Request(r => r.PropertyName = "Visible")));

        Assert.Equal(new[] { "Delete", "readBack gone" }, deleted.AppliedProperties);
        Assert.Empty(button.Dynamizations);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.FailureCategory);
    }

    [Fact]
    public void ReadNamesOnlyDocumentedProperties()
    {
        // The reads name documented properties only (no generic attribute enumeration, V21 pitfall 3).
        Assert.DoesNotContain(HmiScreenEvents.DynamizationValueNames, n => n.Contains("Diagnosis", StringComparison.Ordinal));
        Assert.Equal("#FF000000", HmiScreenEvents.Text(System.Drawing.Color.Black));
        Assert.Equal("Fast", HmiScreenEvents.Text(FlashingRate.Fast));
        Assert.Equal("a, b", HmiScreenEvents.Text(new List<string> { "a", "b" }));
    }
}
