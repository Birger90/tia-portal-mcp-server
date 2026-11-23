using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Dynamization;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Flashing;
using Siemens.Engineering.HmiUnified.UI.Dynamization.Tag;
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
    public void ValueConverter_ReplacesRangeEntriesOfATagDynamizationAndIsListed()
    {
        var (software, button) = Fixture();
        HmiScreenWriter.SetDynamizationTag(software, Request(r => { r.PropertyName = "BackColorName"; r.TagName = "Level"; }));
        var table = button.Dynamizations.OfType<TagDynamization>().Single().ValueConverter.MappingTable;
        table.Entries.Create<MappingTableEntrySimple>().Condition = "old";

        var outcome = HmiScreenEvents.SetDynamizationValueConverter(software, Request(r =>
        {
            r.PropertyName = "BackColorName"; r.ConditionType = "Range";
            r.MappingEntries = new()
            {
                new() { Kind = "range", From = "0", To = "10", Value = "Green" },
                new() { Kind = "range", From = "11", To = "100", RangeType = "FromTo", Value = "Red", Flashing = true, FlashingRate = "Fast" },
            };
        }));
        var listed = HmiScreenEvents.ListDynamizations(software, new HmiQueryInfo { ScreenName = "Main", ItemName = "Start" });

        Assert.Equal(ConditionType.Range, table.ConditionType);
        var entries = table.Entries.Cast<MappingTableEntryRange>().ToList();
        Assert.Equal(2, entries.Count);
        // From/To keep the runtime type of their current value (Int32 here); Value takes the item property's type (String).
        Assert.Equal((0, 10, "Green"), ((int)entries[0].From, (int)entries[0].To, (string)entries[0].Value!));
        Assert.Equal((11, true, FlashingRate.Fast), ((int)entries[1].From, entries[1].Flashing, entries[1].FlashingRate));
        Assert.Contains("MappingTable.Entries.Delete", outcome.AppliedProperties);
        Assert.Equal("readBack 2 entries", outcome.AppliedProperties[^1]);
        var row = listed.Dynamizations.Single(d => d.PropertyName == "BackColorName");
        Assert.Contains(row.Values, v => v.Name == "MappingTable.ConditionType" && v.Value == "Range");
        Assert.Contains(row.Values, v => v.Name == "MappingTable.Entries[1].Kind" && v.Value == "MappingTableEntryRange");
        Assert.Contains(row.Values, v => v.Name == "MappingTable.Entries[1].Value" && v.Value == "Red");
    }

    [Fact]
    public void ValueConverter_SingleBitCreatesThePairOnceAndSetsBothStates()
    {
        foreach (var generated in new[] { false, true })
        {
            var (software, button) = Fixture();
            HmiScreenWriter.SetDynamizationTag(software, Request(r => { r.PropertyName = "BackColorName"; r.TagName = "Run"; }));
            var table = button.Dynamizations.OfType<TagDynamization>().Single().ValueConverter.MappingTable;
            table.GeneratesSingleBitPair = generated;

            HmiScreenEvents.SetDynamizationValueConverter(software, Request(r =>
            {
                r.PropertyName = "BackColorName"; r.ConditionType = "Singlebit";
                r.MappingEntries = new()
                {
                    new() { Kind = "bitmask", BitDynamizationType = "SingleBit", Value = "Gray" },
                    new() { Kind = "bitmask", BitDynamizationType = "SingleBit", Value = "Green" },
                };
            }));

            var entries = table.Entries.Cast<MappingTableEntryBitmask>().ToList();
            Assert.Equal(new object?[] { "Gray", "Green" }, entries.Select(e => e.Value));
            Assert.Equal(new ulong[] { 0, 1 }, entries.Select(e => e.Condition));
        }
    }

    [Fact]
    public void ValueConverter_FormulaCreatesAnExpressionDynamizationAndOtherKindsAreRefused()
    {
        var (software, button) = Fixture();
        HmiScreenEvents.SetDynamizationScript(software, Request(r => { r.PropertyName = "Visible"; r.ScriptCode = "return true;"; }));

        var created = HmiScreenEvents.SetDynamizationValueConverter(software, Request(r => { r.PropertyName = "BackColorName"; r.Formula = "'Tag_1' + 1"; }));
        var refused = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.SetDynamizationValueConverter(software, Request(r => { r.PropertyName = "Visible"; r.Formula = "1"; })));
        var missing = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.SetDynamizationValueConverter(software, Request(r => { r.PropertyName = "Text"; r.ConditionType = "Range"; })));
        var badEnum = Assert.Throws<WorkerOperationException>(() => HmiScreenEvents.SetDynamizationValueConverter(software, Request(r => { r.PropertyName = "BackColorName"; r.ConditionType = "Sometimes"; })));

        var expression = button.Dynamizations.OfType<ExpressionDynamization>().Single();
        Assert.Equal(("'Tag_1' + 1", true), (expression.ValueConverter.Formula, expression.ValueConverter.IsFormulaSelected));
        Assert.Equal(new[] { "Create", "ValueConverter.IsFormulaSelected", "ValueConverter.Formula" }, created.AppliedProperties);
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, refused.FailureCategory);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, missing.FailureCategory);
        Assert.Contains("set_dynamization_tag", missing.Message);
        Assert.Equal(WorkerFailureCategories.ValidationError, badEnum.FailureCategory);
        Assert.Contains("Range, Simple, Bitmask, Singlebit", badEnum.Message);
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
