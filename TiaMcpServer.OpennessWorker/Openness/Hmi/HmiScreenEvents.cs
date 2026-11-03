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
