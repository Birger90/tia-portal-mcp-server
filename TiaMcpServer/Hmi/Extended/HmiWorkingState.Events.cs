using TiaMcpServer.Safety.Pipeline;
using static TiaMcpServer.Hmi.HmiGuardDefinitions;

namespace TiaMcpServer.Hmi;

/// <summary>
/// The WinCC Unified event and dynamization writes (issue #29) in the HMI working state: the worker resolves the targets
/// (no planning read covers events), so the effect lists the request; an item depends on the latest earlier item on the
/// same screen or screen item.
/// </summary>
internal sealed partial class HmiWorkingState
{
    private HmiWriteEffect ResolveEvents(HmiWriteOperationRequest r, List<FiredGuard> guards)
    {
        var changes = new List<HmiFieldChange>();
        void Change(string field, string? current, string? requested) => changes.Add(new(field, current, requested, false));
        var target = Identity("ScreenItem", $"{r.ScreenName}/{r.ItemName ?? "(screen)"}", group: r.GroupPath);
        var eventName = r.PropertyName is null ? $"event {r.EventType}" : $"property event {r.PropertyName} {r.EventType}";
        switch (r.Operation)
        {
            case "set_event_script":
                Change($"{eventName} script", null, r.ScriptCode);
                if (r.GlobalDefinitionAreaScriptCode is not null) Change($"{eventName} global definitions", null, r.GlobalDefinitionAreaScriptCode);
                if (r.Async is { } async) Change($"{eventName} async", null, async ? "true" : "false");
                break;
            case "delete_event_script":
                Change(eventName, "(handler)", null);
                guards.Add(Fire(DeletesScripting, r, $"Deleting the {eventName} handler of '{r.ScreenName}{(r.ItemName is null ? string.Empty : "/" + r.ItemName)}' removes its script."));
                break;
            case "set_dynamization_script":
                Change($"dynamization {r.PropertyName} script", null, r.ScriptCode);
                if (r.TriggerType is not null) Change($"dynamization {r.PropertyName} Trigger.Type", null, r.TriggerType);
                if (r.TriggerTags is not null) Change($"dynamization {r.PropertyName} Trigger.Tags", null, string.Join(", ", r.TriggerTags));
                if (r.CustomDuration is not null) Change($"dynamization {r.PropertyName} Trigger.CustomDuration", null, r.CustomDuration);
                break;
            case "set_dynamization_flashing":
                Change($"dynamization {r.PropertyName}", null, "Flashing");
                foreach (var (field, value) in new[] { ("Color", r.Color), ("AlternateColor", r.AlternateColor), ("FlashingCondition", r.FlashingCondition), ("FlashingRate", r.FlashingRate) })
                    if (value is not null) Change($"dynamization {r.PropertyName} {field}", null, value);
                break;
            default:
                Change($"dynamization {r.PropertyName}", "(dynamization)", null);
                guards.Add(Fire(DeletesScripting, r, $"Deleting the dynamization of '{r.PropertyName}' leaves the property at its static value."));
                break;
        }

        return new(r.Operation, target, changes, null, Dependency(new[] { Identity("Screen", r.ScreenName!), target }));
    }

    private void ApplyEvents(HmiWriteOperationRequest r, HmiWriteEffect effect)
    {
        Touch(effect.Target, r.OperationId);
        Touch(Identity("Screen", r.ScreenName!), r.OperationId);
    }
}
