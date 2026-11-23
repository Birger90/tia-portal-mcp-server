using System.Collections;
using System.Globalization;
using System.Reflection;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Screens;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiWriteRun;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// WinCC Unified events and dynamizations of screens and screen items (issue #29). Every member is documented in the V21
/// <c>Siemens.Engineering.WinCCUnified.xml</c> and bound at runtime on the live object (the compile stubs do not carry
/// them): the <c>EventHandlers</c> composition of a screen or item (<c>Create</c>/<c>Find</c> taking that type's event
/// enum, e.g. <c>HmiButtonEventHandlerComposition.Create(HmiButtonEventType)</c>), <c>PropertyEventHandlers</c>
/// (<c>Create</c>/<c>Find(String, PropertyEventType)</c>), the handler's <c>Script</c> (<c>IHmiScript.ScriptCode</c>,
/// <c>GlobalDefinitionAreaScriptCode</c>, <c>Async</c>) and <c>Delete()</c>; <c>Dynamizations</c>
/// (<c>DynamizationBaseComposition.Create&lt;T&gt;(String)</c>, <c>Find(String)</c>) with <c>ScriptDynamization</c>
/// (<c>Trigger</c>: <c>Type</c>, <c>Tags</c>, <c>CustomDuration</c>) and <c>FlashingDynamization</c> (<c>Color</c>,
/// <c>AlternateColor</c>, <c>FlashingCondition</c>, <c>FlashingRate</c>), <c>DynamizationBase.Delete()</c>. Reads name only
/// documented properties (no generic attribute enumeration, V21 pitfall 3). Every write is read back.
/// </summary>
internal static class HmiScreenEvents
{
    private const string EventHandlersProperty = "EventHandlers";
    private const string PropertyEventHandlersProperty = "PropertyEventHandlers";
    private const string DynamizationsProperty = "Dynamizations";

    /// <summary>The documented dynamization properties a read reports when the dynamization has them.</summary>
    internal static readonly string[] DynamizationValueNames =
    {
        "Tag", "PlcTag", "Address", "DataType", "ReadOnly", "UseIndirectAddressing", "ScriptCode", "GlobalDefinitionAreaScriptCode", "Async",
        "Color", "AlternateColor", "FlashingCondition", "FlashingRate", "ResourceList", "DynamicTagName",
    };

    private static readonly string[] TriggerValueNames = { "Type", "Tags", "CustomDuration" };

    /// <summary>The documented members of a mapping table entry (<c>MappingTableEntryBase</c> and its three entry types) a read reports.</summary>
    internal static readonly string[] MappingEntryValueNames =
    {
        "From", "To", "RangeType", "Condition", "BitDynamizationType", "Relevant", "Value", "AlternateValue", "Flashing", "FlashingRate",
    };

    /// <summary>The documented entry types of <c>set_dynamization_value_converter</c> by request kind.</summary>
    internal static readonly IReadOnlyDictionary<string, string> MappingEntryTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["range"] = "MappingTableEntryRange",
        ["simple"] = "MappingTableEntrySimple",
        ["bitmask"] = "MappingTableEntryBitmask",
    };

    // ---- reads ---------------------------------------------------------------------------------------

    public static HmiScreenItemEventsInfo ListEvents(HmiSoftware software, HmiQueryInfo query)
    {
        var (screen, target) = Target(software, query.ScreenName, null, query.ItemName);
        var info = new HmiScreenItemEventsInfo { ScreenName = screen.Name, ItemName = query.ItemName is null ? null : NameOf(target), TypeName = target.GetType().Name };
        foreach (var handler in Handlers(target, EventHandlersProperty, info.Messages))
            info.Events.Add(Describe(handler, propertyEvent: false, info.Messages));
        foreach (var handler in Handlers(target, PropertyEventHandlersProperty, info.Messages))
            info.PropertyEvents.Add(Describe(handler, propertyEvent: true, info.Messages));
        info.IsComplete = info.Messages.Count == 0;
        return info;
    }

    public static HmiScreenItemDynamizationsInfo ListDynamizations(HmiSoftware software, HmiQueryInfo query)
    {
        var (screen, target) = Target(software, query.ScreenName, null, query.ItemName);
        var info = new HmiScreenItemDynamizationsInfo { ScreenName = screen.Name, ItemName = query.ItemName is null ? null : NameOf(target), TypeName = target.GetType().Name };
        foreach (var dynamization in Handlers(target, DynamizationsProperty, info.Messages))
        {
            var row = new HmiDynamizationInfo
            {
                PropertyName = ReadText(dynamization, "PropertyName", info.Messages) ?? string.Empty,
                TypeName = dynamization.GetType().Name,
                DynamizationType = ReadText(dynamization, "DynamizationType", info.Messages),
            };
            foreach (var name in DynamizationValueNames)
                if (LibraryRuntimeBinding.Property(dynamization.GetType(), name, out _) is not null)
                    row.Values.Add(new HmiNamedValueInfo { Name = name, Value = ReadText(dynamization, name, info.Messages) });
            if (LibraryRuntimeBinding.Property(dynamization.GetType(), "Trigger", out _) is { } triggerProperty && SafeGet(triggerProperty, dynamization, info.Messages) is { } trigger)
                foreach (var name in TriggerValueNames)
                    if (LibraryRuntimeBinding.Property(trigger.GetType(), name, out _) is not null)
                        row.Values.Add(new HmiNamedValueInfo { Name = "Trigger." + name, Value = ReadText(trigger, name, info.Messages) });
            if (LibraryRuntimeBinding.Property(dynamization.GetType(), "ValueConverter", out _) is { } converterProperty && SafeGet(converterProperty, dynamization, info.Messages) is { } converter)
                AddValueConverter(row, converter, info.Messages);
            info.Dynamizations.Add(row);
        }

        info.Dynamizations = info.Dynamizations.OrderBy(d => d.PropertyName, StringComparer.OrdinalIgnoreCase).ToList();
        info.IsComplete = info.Messages.Count == 0;
        return info;
    }

    // ---- event writes --------------------------------------------------------------------------------

    public static HmiWriteOutcomeInfo SetEventScript(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, target) = Target(software, write.ScreenName, write.GroupPath, write.ItemName);
        var code = HmiScreenWriter.Required(write.ScriptCode, "scriptCode");
        var (composition, find, create, arguments) = EventMembers(target, write);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = Describe(write) };
        try
        {
            var handler = HmiScreenWriter.Invoke(find, composition, arguments);
            if (handler is null) Set(outcome, "Create", () => handler = HmiScreenWriter.Invoke(create, composition, arguments));
            if (handler is null) throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{create.Name} returned no event handler for {Describe(write)}.");
            var script = Value(handler, "Script")
                ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The event handler of {Describe(write)} has no Script.");
            WriteScript(script, code, write.GlobalDefinitionAreaScriptCode, write.Async, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo DeleteEventScript(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, target) = Target(software, write.ScreenName, write.GroupPath, write.ItemName);
        var (composition, find, _, arguments) = EventMembers(target, write);
        var handler = HmiScreenWriter.Invoke(find, composition, arguments)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"{Describe(write)} has no event handler to delete.");
        var delete = HmiScreenWriter.Bind(handler, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = Describe(write) };
        try
        {
            Set(outcome, "Delete", () => HmiScreenWriter.Invoke(delete, handler));
            if (HmiScreenWriter.Invoke(find, composition, arguments) is not null)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The event handler of {Describe(write)} is still found after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    /// <summary>
    /// The event composition of the target with its Find/Create pair and arguments: <c>EventHandlers</c> takes the
    /// type's own event enum (the single Find/Create with one enum parameter), <c>PropertyEventHandlers</c> a property
    /// name and <c>PropertyEventType</c>. An event name that is not a member of the enum is a validation_error naming the members.
    /// </summary>
    private static (object Composition, MethodInfo Find, MethodInfo Create, object[] Arguments) EventMembers(IEngineeringObject target, HmiWriteRequestInfo write)
    {
        var propertyEvent = write.PropertyName is not null;
        var compositionName = propertyEvent ? PropertyEventHandlersProperty : EventHandlersProperty;
        var composition = HmiScreenWriter.Composition(target, compositionName, required: true)!;
        var arity = propertyEvent ? 2 : 1;
        var create = EnumMethod(composition, "Create", arity);
        var find = EnumMethod(composition, "Find", arity);
        var enumType = create.GetParameters()[arity - 1].ParameterType;
        var eventType = LibraryRuntimeBinding.EnumValue(enumType, HmiScreenWriter.Required(write.EventType, "eventType"))
            ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"'{write.EventType}' is not a member of {enumType.Name} ({LibraryRuntimeBinding.MemberList(enumType)}); nothing was changed.");
        if (find.GetParameters()[arity - 1].ParameterType != enumType)
            throw HmiScreenWriter.Missing($"{composition.GetType().Name}.Find and Create take different event types.");
        var arguments = propertyEvent ? new[] { (object)write.PropertyName!, eventType } : new[] { eventType };
        return (composition, find, create, arguments);
    }

    /// <summary>The single public method <paramref name="name"/> whose last of <paramref name="arity"/> parameters is an enum (and whose first is a String for arity 2).</summary>
    private static MethodInfo EnumMethod(object composition, string name, int arity)
    {
        var matches = composition.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == name && !m.IsGenericMethodDefinition && m.GetParameters().Length == arity
                && m.GetParameters()[arity - 1].ParameterType.IsEnum && (arity == 1 || m.GetParameters()[0].ParameterType == typeof(string)))
            .ToList();
        return matches.Count == 1 ? matches[0]
            : throw HmiScreenWriter.Missing($"{composition.GetType().Name}.{name}({(arity == 2 ? "String, " : string.Empty)}<event type>): {matches.Count} candidates on this installation.");
    }

    // ---- dynamization writes -------------------------------------------------------------------------

    public static HmiWriteOutcomeInfo SetDynamizationScript(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, target) = Target(software, write.ScreenName, write.GroupPath, write.ItemName);
        var code = HmiScreenWriter.Required(write.ScriptCode, "scriptCode");
        var (dynamizations, existing, create) = DynamizationMembers(target, write, "ScriptDynamization");
        var outcome = new HmiWriteOutcomeInfo { ObjectName = Describe(write) };
        try
        {
            var dynamization = existing ?? Created(dynamizations, create!, write, outcome);
            WriteScript(dynamization, code, write.GlobalDefinitionAreaScriptCode, write.Async, outcome);
            if (write.TriggerType is not null || write.TriggerTags is not null || write.CustomDuration is not null)
            {
                var trigger = Value(dynamization, "Trigger")
                    ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The script dynamization of {Describe(write)} has no Trigger.");
                if (write.TriggerType is { } type) Set(outcome, "Trigger.Type", () => SetEnumOrText(trigger, "Type", type));
                if (write.TriggerTags is { } tags) Set(outcome, "Trigger.Tags", () => SetTags(trigger, tags));
                if (write.CustomDuration is { } duration) Set(outcome, "Trigger.CustomDuration", () => SetEnumOrText(trigger, "CustomDuration", duration));
            }

            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo SetDynamizationFlashing(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, target) = Target(software, write.ScreenName, write.GroupPath, write.ItemName);
        var (dynamizations, existing, create) = DynamizationMembers(target, write, "FlashingDynamization");
        var outcome = new HmiWriteOutcomeInfo { ObjectName = Describe(write) };
        try
        {
            var dynamization = existing ?? Created(dynamizations, create!, write, outcome);
            foreach (var (name, value) in new[] { ("Color", write.Color), ("AlternateColor", write.AlternateColor), ("FlashingCondition", write.FlashingCondition), ("FlashingRate", write.FlashingRate) })
                if (value is not null) Set(outcome, name, () => HmiScreenWriter.SetValue(dynamization, name, value, null));
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo DeleteDynamization(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, target) = Target(software, write.ScreenName, write.GroupPath, write.ItemName);
        var property = HmiScreenWriter.Required(write.PropertyName, "propertyName");
        var dynamizations = HmiScreenWriter.Composition(target, DynamizationsProperty, required: true)!;
        var existing = Dynamization(dynamizations, property)
            ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{property}' of {Describe(write)} has no dynamization to delete.");
        var delete = HmiScreenWriter.Bind(existing, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = Describe(write) };
        try
        {
            Set(outcome, "Delete", () => HmiScreenWriter.Invoke(delete, existing));
            if (Dynamization(dynamizations, property) is not null)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"'{property}' of {Describe(write)} is still dynamized after Delete().");
            outcome.AppliedProperties.Add("readBack gone");
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    /// <summary>
    /// The value converter of the property's tag or expression dynamization (origin: idea and call order from
    /// jessenic/TIA-MCP-Server at e817033, MIT, Copyright (c) 2026 TIA Openness MCP Contributors; see THIRD_PARTY_NOTICES.md;
    /// reimplemented on the documented V21 members, bound at runtime): <c>ValueConverter.IsFormulaSelected</c> and
    /// <c>Formula</c>, then the <c>MappingTable</c>. Given entries replace every existing entry (each <c>Delete()</c>d first),
    /// then <c>ConditionType</c> is set, then the entries are created: range and simple entries with
    /// <c>Entries.Create&lt;T&gt;()</c>, bitmask entries with the one <c>Entries.Create(BitDynamizationType)</c>, which creates
    /// the whole set (jessenic reports that TIA Portal may already create the Singlebit pair when the condition type is
    /// assigned; then no Create is called; not verified). Without a dynamization on the property a formula creates an
    /// <c>ExpressionDynamization</c>; anything else needs <c>set_dynamization_tag</c> first. Every value is read back.
    /// </summary>
    public static HmiWriteOutcomeInfo SetDynamizationValueConverter(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var (_, target) = Target(software, write.ScreenName, write.GroupPath, write.ItemName);
        var property = HmiScreenWriter.Required(write.PropertyName, "propertyName");
        if (write.Formula is null && write.FormulaSelected is null && write.ConditionType is null && write.MappingEntries is null)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Give formula, formulaSelected, conditionType or mappingEntries; nothing was changed.");
        var dynamizations = HmiScreenWriter.Composition(target, DynamizationsProperty, required: true)!;
        var existing = Dynamization(dynamizations, property);
        MethodInfo? create = null;
        if (existing is null)
        {
            if (write.Formula is null)
                throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound,
                    $"'{property}' of {Describe(write)} has no tag or expression dynamization; create one with set_dynamization_tag, or give a formula to create an expression dynamization. Nothing was changed.");
            var type = HmiScreenWriter.TypeNamed(typeof(HmiSoftware).Assembly, "ExpressionDynamization", typeof(object));
            create = LibraryRuntimeBinding.FindGeneric(dynamizations.GetType(), "Create", new[] { type }, new[] { typeof(string) }, out var error)
                ?? throw HmiScreenWriter.Missing($"Dynamizations.Create<ExpressionDynamization>(String): {error}");
        }
        else if (!IsOfType(existing.GetType(), "TagDynamization") && !IsOfType(existing.GetType(), "ExpressionDynamization"))
        {
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"'{property}' of {Describe(write)} is dynamized by a {existing.GetType().Name}, which has no value converter; delete it with delete_dynamization first. Nothing was changed.");
        }

        var itemValue = ItemValue(target, property);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = Describe(write) };
        try
        {
            var dynamization = existing ?? Created(dynamizations, create!, write, outcome);
            var converter = Value(dynamization, "ValueConverter")
                ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The dynamization of {Describe(write)} has no ValueConverter.");
            var selected = write.FormulaSelected ?? (write.Formula is null ? null : true);
            if (write.Formula is { } formula)
            {
                // As jessenic does: select the formula before writing it.
                Set(outcome, "ValueConverter.IsFormulaSelected", () => SetProperty(converter, "IsFormulaSelected", true));
                Set(outcome, "ValueConverter.Formula", () => SetProperty(converter, "Formula", formula));
            }

            if (selected is { } flag && (write.Formula is null || !flag))
                Set(outcome, "ValueConverter.IsFormulaSelected=" + (flag ? "true" : "false"), () => SetProperty(converter, "IsFormulaSelected", flag));
            if (write.ConditionType is not null || write.MappingEntries is not null)
                WriteMappingTable(converter, write, itemValue, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    private static void WriteMappingTable(object converter, HmiWriteRequestInfo write, object? itemValue, HmiWriteOutcomeInfo outcome)
    {
        var table = Value(converter, "MappingTable")
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The value converter of {Describe(write)} has no MappingTable.");
        var entries = Value(table, "Entries")
            ?? throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, $"The mapping table of {Describe(write)} has no Entries.");
        var requested = write.MappingEntries;

        // Bind every Create before the first change.
        var bitmask = requested is { Count: > 0 } && requested.All(e => e.Kind == "bitmask");
        if (requested is not null && requested.Any(e => e.Kind == "bitmask") && !bitmask)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Bitmask entries cannot be mixed with range or simple entries; nothing was changed.");
        MethodInfo? createBits = null;
        object? bitType = null;
        var creates = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        if (bitmask)
        {
            var kinds = requested!.Select(e => e.BitDynamizationType).Distinct(StringComparer.Ordinal).ToList();
            if (kinds.Count != 1 || kinds[0] is null)
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "Every bitmask entry needs the same bitDynamizationType (e.g. SingleBit); nothing was changed.");
            createBits = EnumMethod(entries, "Create", 1);
            var enumType = createBits.GetParameters()[0].ParameterType;
            bitType = LibraryRuntimeBinding.EnumValue(enumType, kinds[0])
                ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"'{kinds[0]}' is not a member of {enumType.Name} ({LibraryRuntimeBinding.MemberList(enumType)}); nothing was changed.");
        }
        else if (requested is not null)
        {
            foreach (var kind in requested.Select(e => e.Kind).Distinct(StringComparer.Ordinal))
            {
                if (!MappingEntryTypes.TryGetValue(kind, out var typeName))
                    throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"Mapping entry kind '{kind}' is not range, simple or bitmask; nothing was changed.");
                var type = HmiScreenWriter.TypeNamed(typeof(HmiSoftware).Assembly, typeName, typeof(object));
                creates[kind] = LibraryRuntimeBinding.FindGeneric(entries.GetType(), "Create", new[] { type }, Type.EmptyTypes, out var error)
                    ?? throw HmiScreenWriter.Missing($"MappingTable.Entries.Create<{typeName}>(): {error}");
            }
        }

        if (requested is not null)
        {
            Set(outcome, "MappingTable.Entries.Delete", () =>
            {
                foreach (var entry in Items(entries).ToList())
                    HmiScreenWriter.Invoke(HmiScreenWriter.Bind(entry, "Delete", Type.EmptyTypes), entry);
                if (Items(entries).Any())
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The mapping table of {Describe(write)} still holds entries after Delete().");
            });
        }

        if (write.ConditionType is { } condition)
            Set(outcome, "MappingTable.ConditionType", () => SetEnumOrText(table, "ConditionType", condition));
        if (requested is null) return;

        if (bitmask)
        {
            Set(outcome, "MappingTable.Entries.Create(" + write.MappingEntries![0].BitDynamizationType + ")", () =>
            {
                if (!Items(entries).Any()) HmiScreenWriter.Invoke(createBits!, entries, bitType!);
            });
            var created = Items(entries).ToList();
            if (created.Count != requested.Count)
                throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                    $"TIA Portal created {created.Count} {write.MappingEntries![0].BitDynamizationType} entries, the request has {requested.Count}; the mapping table keeps them.");
            for (var i = 0; i < created.Count; i++)
                WriteEntry(created[i], requested[i], i, itemValue, outcome);
        }
        else
        {
            for (var i = 0; i < requested.Count; i++)
            {
                object? entry = null;
                var index = i;
                Set(outcome, $"MappingTable.Entries[{index}].Create", () => entry = HmiScreenWriter.Invoke(creates[requested[index].Kind], entries));
                if (entry is null)
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Entries.Create returned no {requested[i].Kind} entry.");
                WriteEntry(entry, requested[i], i, itemValue, outcome);
            }
        }

        var count = Items(entries).Count();
        if (count != requested.Count)
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The mapping table of {Describe(write)} reads back {count} entries, not {requested.Count}.");
        outcome.AppliedProperties.Add($"readBack {count} entries");
    }

    private static void WriteEntry(object entry, HmiMappingEntryInfo requested, int index, object? itemValue, HmiWriteOutcomeInfo outcome)
    {
        var prefix = $"MappingTable.Entries[{index}].";
        foreach (var (name, value, fallback) in new (string Name, string? Value, object? Fallback)[]
        {
            ("From", requested.From, null), ("To", requested.To, null), ("RangeType", requested.RangeType, null),
            ("Condition", requested.Condition, null), ("Value", requested.Value, itemValue), ("AlternateValue", requested.AlternateValue, itemValue),
            ("FlashingRate", requested.FlashingRate, null),
        })
        {
            if (value is null) continue;
            Set(outcome, prefix + name, () => SetConverted(entry, name, value, fallback));
        }

        if (requested.Flashing) Set(outcome, prefix + "Flashing", () => SetProperty(entry, "Flashing", true));
    }

    /// <summary>
    /// A member set from text: an enum by member name; otherwise converted to the runtime type of its current value, of
    /// <paramref name="fallback"/> (the item's own property value for Value/AlternateValue), or of the declared type
    /// (string, number); a member declared <c>Object</c> with neither takes an integer, else a number, else the text.
    /// </summary>
    private static void SetConverted(object owner, string name, string text, object? fallback)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out var error)
            ?? throw HmiScreenWriter.Missing($"{owner.GetType().Name}.{name}: {error}");
        var type = property.PropertyType;
        object value;
        if (type.IsEnum)
        {
            value = LibraryRuntimeBinding.EnumValue(type, text)
                ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{text}' is not a member of {type.Name} ({LibraryRuntimeBinding.MemberList(type)}).");
        }
        else
        {
            var current = property.GetValue(owner) ?? fallback;
            if (current is not null && (type == typeof(object) || type.IsInstanceOfType(current))) value = HmiScreenWriter.Convert(current, text, name);
            else if (type == typeof(string)) value = text;
            else if (type.IsPrimitive || type == typeof(decimal)) value = HmiScreenWriter.Convert(System.Activator.CreateInstance(type), text, name);
            else if (type == typeof(object)) value = LooseNumber(text);
            else throw HmiScreenWriter.Missing($"{owner.GetType().Name}.{name} holds a {type.FullName}, which is not set from text here.");
        }

        SetProperty(owner, name, value);
    }

    private static object LooseNumber(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer
            : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number
            : text;

    /// <summary>The current value of the dynamized property on the screen or item (its type converts mapping values); null when unreadable.</summary>
    private static object? ItemValue(IEngineeringObject target, string property)
    {
        if (string.Equals(property, HmiScreenWriter.ReportedCrashingProperty, StringComparison.Ordinal)) return null;
        try
        {
            return target.GetAttribute(property);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            return null;
        }
    }

    private static IEnumerable<object> Items(object composition) => composition is IEnumerable items ? items.Cast<object>() : Enumerable.Empty<object>();

    private static void AddValueConverter(HmiDynamizationInfo row, object converter, List<string> messages)
    {
        foreach (var name in new[] { "IsFormulaSelected", "Formula" })
            if (LibraryRuntimeBinding.Property(converter.GetType(), name, out _) is not null)
                row.Values.Add(new HmiNamedValueInfo { Name = "ValueConverter." + name, Value = ReadText(converter, name, messages) });
        if (LibraryRuntimeBinding.Property(converter.GetType(), "MappingTable", out _) is not { } tableProperty || SafeGet(tableProperty, converter, messages) is not { } table) return;
        if (LibraryRuntimeBinding.Property(table.GetType(), "ConditionType", out _) is not null)
            row.Values.Add(new HmiNamedValueInfo { Name = "MappingTable.ConditionType", Value = ReadText(table, "ConditionType", messages) });
        if (LibraryRuntimeBinding.Property(table.GetType(), "Entries", out _) is not { } entriesProperty || SafeGet(entriesProperty, table, messages) is not { } entries) return;
        var index = 0;
        foreach (var entry in Items(entries))
        {
            var prefix = $"MappingTable.Entries[{index++}].";
            row.Values.Add(new HmiNamedValueInfo { Name = prefix + "Kind", Value = entry.GetType().Name });
            foreach (var name in MappingEntryValueNames)
                if (LibraryRuntimeBinding.Property(entry.GetType(), name, out _) is not null)
                    row.Values.Add(new HmiNamedValueInfo { Name = prefix + name, Value = ReadText(entry, name, messages) });
        }
    }

    /// <summary>
    /// The Dynamizations composition, the property's existing dynamization when it is of <paramref name="typeName"/>, and
    /// <c>Create&lt;T&gt;(String)</c> when there is none. Another kind of dynamization on the property is refused (delete it
    /// first with delete_dynamization), as set_dynamization_tag refuses a script dynamization.
    /// </summary>
    private static (object Composition, IEngineeringObject? Existing, MethodInfo? Create) DynamizationMembers(IEngineeringObject target, HmiWriteRequestInfo write, string typeName)
    {
        var property = HmiScreenWriter.Required(write.PropertyName, "propertyName");
        var dynamizations = HmiScreenWriter.Composition(target, DynamizationsProperty, required: true)!;
        var existing = Dynamization(dynamizations, property);
        if (existing is not null)
        {
            if (!IsOfType(existing.GetType(), typeName))
                throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                    $"'{property}' of {Describe(write)} is dynamized by a {existing.GetType().Name}, not a {typeName}; delete it with delete_dynamization first. Nothing was changed.");
            return (dynamizations, existing, null);
        }

        var type = HmiScreenWriter.TypeNamed(typeof(HmiSoftware).Assembly, typeName, typeof(object));
        var create = LibraryRuntimeBinding.FindGeneric(dynamizations.GetType(), "Create", new[] { type }, new[] { typeof(string) }, out var error)
            ?? throw HmiScreenWriter.Missing($"Dynamizations.Create<{typeName}>(String): {error}");
        return (dynamizations, null, create);
    }

    private static IEngineeringObject Created(object dynamizations, MethodInfo create, HmiWriteRequestInfo write, HmiWriteOutcomeInfo outcome)
    {
        IEngineeringObject? created = null;
        Set(outcome, "Create", () => created = (IEngineeringObject?)HmiScreenWriter.Invoke(create, dynamizations, write.PropertyName!));
        return created ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Create returned no dynamization for '{write.PropertyName}'.");
    }

    private static IEngineeringObject? Dynamization(object dynamizations, string property)
        => HmiScreenWriter.Children(dynamizations).FirstOrDefault(d => string.Equals(HmiScreenWriter.Text(d, "PropertyName"), property, StringComparison.OrdinalIgnoreCase));

    private static bool IsOfType(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.Name == name) return true;
        return type.GetInterfaces().Any(i => i.Name == name);
    }

    // ---- scripts and values ----------------------------------------------------------------------------

    /// <summary>ScriptCode, GlobalDefinitionAreaScriptCode and Async through the documented IHmiScript properties, each read back.</summary>
    private static void WriteScript(object script, string code, string? globalDefinitions, bool? async, HmiWriteOutcomeInfo outcome)
    {
        Set(outcome, "ScriptCode", () => SetProperty(script, "ScriptCode", code));
        if (globalDefinitions is not null) Set(outcome, "GlobalDefinitionAreaScriptCode", () => SetProperty(script, "GlobalDefinitionAreaScriptCode", globalDefinitions));
        if (async is { } flag) Set(outcome, "Async", () => SetProperty(script, "Async", flag));
    }

    private static void SetProperty(object owner, string name, object value)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out var error);
        if (property is null || !property.CanWrite || !property.PropertyType.IsInstanceOfType(value))
            throw HmiScreenWriter.Missing($"{owner.GetType().Name}.{name} is not a writable {value.GetType().Name} property on this installation{(error is null ? string.Empty : ": " + error)}.");
        try
        {
            property.SetValue(owner, value);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }

        var observed = property.GetValue(owner);
        if (!Equals(observed, value))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{name} reads back as '{HmiScreenWriter.Format(observed)}', not '{HmiScreenWriter.Format(value)}'.");
    }

    /// <summary>A Trigger member converted to exactly the runtime type of its current value (an enum member name, a number, a string), read back.</summary>
    private static void SetEnumOrText(object owner, string name, string text)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out var error)
            ?? throw HmiScreenWriter.Missing($"{owner.GetType().Name}.{name}: {error}");
        var current = property.GetValue(owner);
        var value = property.PropertyType.IsEnum
            ? LibraryRuntimeBinding.EnumValue(property.PropertyType, text)
              ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{text}' is not a member of {property.PropertyType.Name} ({LibraryRuntimeBinding.MemberList(property.PropertyType)}).")
            : HmiScreenWriter.Convert(current, text, name);
        SetProperty(owner, name, value);
    }

    /// <summary>
    /// Trigger.Tags: a writable list of strings is replaced (Clear, Add), a writable String[] or IList&lt;String&gt; property
    /// is assigned; read back as the same names in order. Any other shape is api_member_missing.
    /// </summary>
    private static void SetTags(object trigger, IReadOnlyList<string> tags)
    {
        var property = LibraryRuntimeBinding.Property(trigger.GetType(), "Tags", out var error)
            ?? throw HmiScreenWriter.Missing($"Trigger.Tags: {error}");
        var current = property.GetValue(trigger);
        if (current is IList<string> list && !list.IsReadOnly)
        {
            list.Clear();
            foreach (var tag in tags) list.Add(tag);
        }
        else if (property.CanWrite && property.PropertyType.IsAssignableFrom(typeof(string[])))
            property.SetValue(trigger, tags.ToArray());
        else if (property.CanWrite && property.PropertyType.IsAssignableFrom(typeof(List<string>)))
            property.SetValue(trigger, tags.ToList());
        else
            throw HmiScreenWriter.Missing($"Trigger.Tags holds a {property.PropertyType.FullName}, which is not set from a list of tag names here.");

        var observed = (property.GetValue(trigger) as IEnumerable)?.Cast<object>().Select(t => t?.ToString()).ToList() ?? new List<string?>();
        if (!observed.SequenceEqual(tags))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"Trigger.Tags reads back as [{string.Join(", ", observed)}].");
    }

    // ---- lookup and reads ----------------------------------------------------------------------------

    private static (HmiScreen Screen, IEngineeringObject Target) Target(HmiSoftware software, string? screenName, string? groupPath, string? itemName)
    {
        var screen = HmiScreenWriter.Screen(software, screenName, groupPath);
        return (screen, itemName is null ? screen : HmiScreenWriter.Item(screen, itemName, optional: false)!);
    }

    private static string NameOf(IEngineeringObject target) => target is HmiScreenItemBase item ? item.Name : HmiScreenWriter.Text(target, "Name");

    private static string Describe(HmiWriteRequestInfo write)
        => $"'{write.ScreenName}{(write.ItemName is null ? string.Empty : "/" + write.ItemName)}'"
           + (write.EventType is null ? string.Empty : $" event {(write.PropertyName is null ? string.Empty : write.PropertyName + " ")}{write.EventType}")
           + (write.EventType is null && write.PropertyName is not null ? $" property {write.PropertyName}" : string.Empty);

    private static IEnumerable<IEngineeringObject> Handlers(IEngineeringObject target, string composition, List<string> messages)
    {
        object? value;
        try
        {
            value = HmiScreenWriter.Composition(target, composition, required: false);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            messages.Add($"{composition} of {target.GetType().Name} could not be read: {exception.Message}");
            return Enumerable.Empty<IEngineeringObject>();
        }

        if (value is null) return Enumerable.Empty<IEngineeringObject>();
        try
        {
            return HmiScreenWriter.Children(value).ToList();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            messages.Add($"{composition} of {target.GetType().Name} could not be enumerated: {exception.Message}");
            return Enumerable.Empty<IEngineeringObject>();
        }
    }

    private static HmiEventScriptInfo Describe(IEngineeringObject handler, bool propertyEvent, List<string> messages)
    {
        var info = new HmiEventScriptInfo
        {
            EventType = ReadText(handler, "EventType", messages) ?? string.Empty,
            PropertyName = propertyEvent ? ReadText(handler, "PropertyName", messages) : null,
        };
        if (LibraryRuntimeBinding.Property(handler.GetType(), "Script", out _) is { } property && SafeGet(property, handler, messages) is { } script)
        {
            info.ScriptCode = ReadText(script, "ScriptCode", messages);
            info.GlobalDefinitionAreaScriptCode = ReadText(script, "GlobalDefinitionAreaScriptCode", messages);
            info.Async = LibraryRuntimeBinding.Property(script.GetType(), "Async", out _) is { } asyncProperty && SafeGet(asyncProperty, script, messages) is bool flag ? flag : null;
        }

        return info;
    }

    private static object? Value(object owner, string name)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out var error)
            ?? throw HmiScreenWriter.Missing($"{owner.GetType().Name}.{name}: {error}");
        try
        {
            return property.GetValue(owner);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object? SafeGet(PropertyInfo property, object owner, List<string> messages)
    {
        try
        {
            return property.GetValue(owner);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is EngineeringException engineering && OpennessExceptions.IsRecoverable(engineering))
        {
            messages.Add($"{owner.GetType().Name}.{property.Name} could not be read: {engineering.Message}");
            return null;
        }
    }

    /// <summary>A documented property as text: enums and numbers invariant, colours #AARRGGBB, named objects by name, lists joined by ", ".</summary>
    private static string? ReadText(object owner, string name, List<string> messages)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out _);
        if (property is null) return null;
        var value = SafeGet(property, owner, messages);
        return Text(value);
    }

    internal static string? Text(object? value)
    {
        switch (value)
        {
            case null: return null;
            case string text: return text;
            case bool flag: return flag ? "true" : "false";
            case IFormattable formattable when !value.GetType().IsEnum: return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        var type = value.GetType();
        if (type.IsEnum) return value.ToString();
        if (type.FullName == "System.Drawing.Color" && type.GetMethod("ToArgb", Type.EmptyTypes) is { } toArgb)
            return "#" + ((int)toArgb.Invoke(value, null)!).ToString("X8", CultureInfo.InvariantCulture);
        if (LibraryRuntimeBinding.Property(type, "Name", out _) is { PropertyType: var nameType } nameProperty && nameType == typeof(string))
            return nameProperty.GetValue(value) as string;
        if (value is IEnumerable items) return string.Join(", ", items.Cast<object?>().Select(Text));
        return value.ToString();
    }
}
