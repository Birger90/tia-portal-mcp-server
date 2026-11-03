// The WinCC Unified screen calls follow BetterTIA/writer, src/TiaMcpServer/Siemens/Portal.AnetFork.HmiScreens.cs and
// Portal.AnetFork.Hmi.cs (CreateHmiScreen, DeleteHmiScreen, CreateHmiScreenItems, DeleteHmiScreenItems,
// SetHmiItemAttribute, SetHmiDynamizationTag, ReplaceInHmiScripts, RenameHmiTags, RenameHmiScreens; the screen walk
// Children/Spots), commit fc8179c9b59d364961f2255d07d05af893c32837, MIT License, Copyright (c) 2025 J.Heilingbrunner;
// see THIRD_PARTY_NOTICES.md. Re-written for this worker: every member outside the compile stubs is bound at runtime by
// exact name and argument types and fails api_member_missing before anything changes.
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Plc.Interface;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiWriteRun;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// The WinCC Unified screen writes of hmi_write. Compiled members (stubs): <c>HmiSoftware.Screens</c>/<c>ScreenGroups</c>,
/// <c>HmiScreenComposition.Find</c>, <c>HmiScreenGroup.Screens</c>/<c>Groups</c>, <c>HmiScreen.ScreenItems</c> (enumerated),
/// <c>HmiSoftware.Tags.Find</c>, the generic attribute API. Bound at runtime (evidence: BetterTIA/writer, checked there
/// by reflection over the V21 assemblies, not in the Siemens skills): <c>HmiScreenComposition.Create(string)</c>,
/// <c>HmiScreenItemBaseComposition.Create&lt;T&gt;(string)</c> with <c>T</c> the screen item type named exactly by the
/// caller, <c>Delete()</c> of screens and items, the <c>Dynamizations</c> composition with
/// <c>Create&lt;TagDynamization&gt;(string)</c> and the <c>PropertyName</c>/<c>Tag</c> attributes, the <c>EventHandlers</c>
/// and <c>PropertyEventHandlers</c> compositions, the handler's <c>Script</c> and the script's <c>ScriptCode</c> /
/// <c>GlobalDefinitionAreaScriptCode</c>.
/// </summary>
internal static class HmiScreenWriter
{
    public static HmiWriteOutcomeInfo CreateScreen(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var name = Required(write.ScreenName, "screenName");
        var composition = write.GroupPath is null ? software.Screens : Group(software, write.GroupPath).Screens;
        var create = Bind(composition, "Create", new[] { typeof(string) });
        return HmiWriteRun.Create(name, "screen",
            () => (HmiScreen)Invoke(create, composition, name)!,
            () => composition.Find(name),
            screen => Invoke(Bind(screen, "Delete", Type.EmptyTypes), screen),
            (screen, outcome) =>
            {
                if (write.Width is { } width) Set(outcome, "Width", () => SetValue(screen, "Width", width.ToString(CultureInfo.InvariantCulture), null));
                if (write.Height is { } height) Set(outcome, "Height", () => SetValue(screen, "Height", height.ToString(CultureInfo.InvariantCulture), null));
                outcome.FailedProperty = null;
            });
    }

    public static HmiWriteOutcomeInfo DeleteScreen(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var screen = Screen(software, write.ScreenName, write.GroupPath);
        var delete = Bind(screen, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = screen.Name };
        try { Set(outcome, "Delete", () => Invoke(delete, screen)); return outcome; }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo CreateScreenItem(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var screen = Screen(software, write.ScreenName, write.GroupPath);
        var name = Required(write.ItemName, "itemName");
        var itemType = ScreenItemType(Required(write.ItemType, "itemType"));
        var composition = screen.ScreenItems;
        var create = LibraryRuntimeBinding.FindGeneric(composition.GetType(), "Create", new[] { itemType }, new[] { typeof(string) }, out var error)
            ?? throw Missing($"HmiScreenItemBaseComposition.Create<{itemType.Name}>(String): {error}");
        return HmiWriteRun.Create(name, "screen item",
            () => (HmiScreenItemBase)Invoke(create, composition, name)!,
            () => Item(screen, name, optional: true),
            item => Invoke(Bind(item, "Delete", Type.EmptyTypes), item),
            (item, outcome) =>
            {
                foreach (var (property, value) in new[] { ("Left", write.Left), ("Top", write.Top), ("Width", write.Width), ("Height", write.Height) })
                    if (value is { } number) Set(outcome, property, () => SetValue(item, property, number.ToString(CultureInfo.InvariantCulture), null));
                outcome.FailedProperty = null;
            });
    }

    public static HmiWriteOutcomeInfo DeleteScreenItem(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var screen = Screen(software, write.ScreenName, write.GroupPath);
        var item = Item(screen, Required(write.ItemName, "itemName"), optional: false)!;
        var delete = Bind(item, "Delete", Type.EmptyTypes);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = item.Name };
        try { Set(outcome, "Delete", () => Invoke(delete, item)); return outcome; }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo SetScreenItemProperty(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var screen = Screen(software, write.ScreenName, write.GroupPath);
        IEngineeringObject target = write.ItemName is null ? screen : Item(screen, write.ItemName, optional: false)!;
        var property = Required(write.PropertyName, "propertyName");
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.ItemName ?? screen.Name };
        try { Set(outcome, property, () => SetValue(target, property, write.Value ?? string.Empty, write.Culture)); return outcome; }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    public static HmiWriteOutcomeInfo SetDynamizationTag(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var screen = Screen(software, write.ScreenName, write.GroupPath);
        IEngineeringObject target = write.ItemName is null ? screen : Item(screen, write.ItemName, optional: false)!;
        var property = Required(write.PropertyName, "propertyName");
        var tag = Required(write.TagName, "tagName");
        var dynamizations = Composition(target, "Dynamizations", required: true)!;
        var existing = Children(dynamizations).Where(d => string.Equals(Text(d, "PropertyName"), property, StringComparison.OrdinalIgnoreCase)).ToList();
        if (existing.Count > 0 && existing[0].GetType().Name != "TagDynamization" && !existing[0].GetType().GetInterfaces().Concat(Ancestors(existing[0].GetType())).Any(t => t.Name == "TagDynamization"))
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                $"'{property}' of '{write.ItemName ?? screen.Name}' is dynamized by a {existing[0].GetType().Name}, not a tag dynamization; nothing was changed.");
        MethodInfo? create = null;
        if (existing.Count == 0)
        {
            var dynamizationType = TypeNamed(typeof(HmiSoftware).Assembly, "TagDynamization", typeof(object));
            create = LibraryRuntimeBinding.FindGeneric(dynamizations.GetType(), "Create", new[] { dynamizationType }, new[] { typeof(string) }, out var error)
                ?? throw Missing($"Dynamizations.Create<TagDynamization>(String): {error}");
        }

        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.ItemName ?? screen.Name };
        try
        {
            var dynamization = existing.Count > 0 ? existing[0] : null;
            if (dynamization is null) Set(outcome, "Create", () => dynamization = (IEngineeringObject)Invoke(create!, dynamizations, property)!);
            Set(outcome, "Tag", () =>
            {
                dynamization!.SetAttribute("Tag", tag);
                var observed = Text(dynamization, "Tag");
                if (!string.Equals(observed, tag, StringComparison.Ordinal))
                    throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"The tag dynamization of '{property}' reads back the tag '{observed}', not '{tag}'.");
            });
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    /// <summary>One-pass find/replace in every script of the screens (event handlers, property events, script dynamizations, global definition areas).</summary>
    public static HmiWriteOutcomeInfo ReplaceInScripts(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var replacements = write.Replacements ?? new List<PlcTextReplacementInfo>();
        try
        {
            _ = SimaticMlNetworkTexts.Map(replacements);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"replace_in_scripts: {exception.Message}");
        }

        var screens = write.ScreenName is null ? AllScreens(software).ToList() : new List<HmiScreen> { Screen(software, write.ScreenName, write.GroupPath) };
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.ScreenName ?? software.Name };
        var pending = new List<(object Script, PropertyInfo Property, string New, string Where)>();
        foreach (var screen in screens)
        {
            foreach (var (where, script) in Scripts(screen.Name, screen, 0))
            {
                foreach (var name in new[] { "ScriptCode", "GlobalDefinitionAreaScriptCode" })
                {
                    var property = LibraryRuntimeBinding.Property(script.GetType(), name, out _);
                    if (property is null || property.PropertyType != typeof(string) || !property.CanWrite) continue;
                    var code = property.GetValue(script) as string ?? string.Empty;
                    var replaced = SimaticMlNetworkTexts.ReplaceAll(code, replacements);
                    if (replaced != code) pending.Add((script, property, replaced, $"{where} {name}"));
                }
            }
        }

        try
        {
            foreach (var (script, property, replaced, where) in pending)
                Set(outcome, where, () =>
                {
                    property.SetValue(script, replaced);
                    if (!string.Equals(property.GetValue(script) as string, replaced, StringComparison.Ordinal))
                        throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"{where} does not read back as written.");
                });
            if (pending.Count == 0) outcome.AppliedProperties.Add("(no script contained a find)");
            return outcome;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is EngineeringException engineering) { throw Failure(engineering, outcome); }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }

    /// <summary>rename_tags / rename_screens: by name, swaps through temporary names, one ExclusiveAccess transaction.</summary>
    public static HmiWriteOutcomeInfo RenameBatch(TiaPortal portal, ProjectBase project, HmiSoftware software, string method, HmiWriteRequestInfo write)
    {
        var renames = write.Renames ?? new List<PlcNameRenameInfo>();
        if (renames.Count is 0 or > PlcBatchRenamePlan.MaxRenames)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"renames must hold 1 to {PlcBatchRenamePlan.MaxRenames} {{ name, newName }} entries.");
        var screens = method == HmiScreenMethods.RenameScreens ? AllScreens(software).ToList() : null;
        var targets = renames.Select(rename =>
        {
            IEngineeringObject? found = screens is null
                ? HmiTagLookup.FindUnique(software, rename.Name, describe: true)
                : Single(screens.Where(s => string.Equals(s.Name, rename.Name, StringComparison.OrdinalIgnoreCase)).ToList(), $"screen '{rename.Name}'");
            return (Rename: rename, Target: found!, Old: Text(found!, "Name"));
        }).ToList();
        List<(int Index, string Name)> steps;
        try
        {
            steps = PlcBatchRenamePlan.Plan(targets.Select(t => t.Old).ToList(), targets.Select(t => t.Rename.NewName).ToList(),
                string.IsNullOrWhiteSpace(write.TemporaryStamp) ? Guid.NewGuid().ToString("N").Substring(0, 8) : write.TemporaryStamp!);
        }
        catch (ArgumentException exception)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"{method}: {exception.Message} Nothing was changed.");
        }

        var outcome = new HmiWriteOutcomeInfo { ObjectName = string.Join(", ", targets.Select(t => t.Old)) };
        var run = PlcRoundTrip.Run(portal, project, $"{method} ({targets.Count} objects)", () =>
        {
            foreach (var (index, name) in steps)
            {
                var target = targets[index];
                Rename(target.Target, name, $"'{target.Old}'");
                outcome.AppliedProperties.Add($"Name '{target.Old}' -> '{name}'");
            }

            return outcome;
        });
        if (run.State == PlcRoundTrip.TransactionState.Committed) return outcome;
        outcome.AppliedProperties.Clear();
        throw PlcRoundTrip.RolledBackFailure(run, () => string.Join(", ", targets.Select(t => $"'{t.Old}' is named '{Text(t.Target, "Name")}'")) + ".");
    }

    /// <summary>
    /// SetAttribute("Name") with read-back. Unlike the PLC group renames there is no writability check from the attribute list:
    /// generic attribute enumeration is kept out of the HMI worker (V21 pitfall 3), so TIA Portal decides.
    /// </summary>
    private static void Rename(IEngineeringObject target, string newName, string description)
    {
        target.SetAttribute("Name", newName);
        var after = target.GetAttribute("Name")?.ToString();
        if (!string.Equals(after, newName, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
                $"{description} reads back the name '{after}' after SetAttribute(\"Name\", \"{newName}\"); TIA Portal did not apply the rename.");
    }

    // ---- lookup ----------------------------------------------------------------------------------

    private static HmiScreenGroup Group(HmiSoftware software, string groupPath)
    {
        HmiScreenGroup? group = null;
        IEnumerable<HmiScreenGroup> level = software.ScreenGroups;
        foreach (var segment in groupPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            group = level.FirstOrDefault(g => string.Equals(g.Name, segment, StringComparison.OrdinalIgnoreCase))
                ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Screen group '{groupPath}' was not found.");
            level = group.Groups;
        }

        return group ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "groupPath names no screen group.");
    }

    internal static HmiScreen Screen(HmiSoftware software, string? name, string? groupPath)
    {
        var screenName = Required(name, "screenName");
        var candidates = groupPath is null ? AllScreens(software) : Group(software, groupPath).Screens;
        return Single(candidates.Where(s => string.Equals(s.Name, screenName, StringComparison.OrdinalIgnoreCase)).ToList(), $"screen '{screenName}'");
    }

    private static IEnumerable<HmiScreen> AllScreens(HmiSoftware software)
    {
        foreach (HmiScreen screen in software.Screens) yield return screen;
        foreach (var screen in software.ScreenGroups.SelectMany(GroupScreens)) yield return screen;
    }

    private static IEnumerable<HmiScreen> GroupScreens(HmiScreenGroup group)
        => group.Screens.Concat(group.Groups.SelectMany(GroupScreens));

    internal static HmiScreenItemBase? Item(HmiScreen screen, string name, bool optional)
    {
        var matches = screen.ScreenItems.Where(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (optional && matches.Count == 0) return null;
        return Single(matches, $"screen item '{name}' on screen '{screen.Name}'");
    }

    private static T Single<T>(List<T> matches, string what)
        => matches.Count switch
        {
            1 => matches[0],
            0 => throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"No {what} was found."),
            _ => throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"{matches.Count} objects match {what}."),
        };

    /// <summary>The non-abstract screen item type whose short name is exactly <paramref name="name"/> (e.g. HmiButton), in the Unified assembly.</summary>
    private static Type ScreenItemType(string name)
        => TypeNamed(typeof(HmiScreenItemBase).Assembly, name, typeof(HmiScreenItemBase));

    internal static Type TypeNamed(Assembly assembly, string name, Type baseType)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.Where(t => t is not null).ToArray()!;
        }

        var matches = types.Where(t => t.IsPublic && !t.IsAbstract && t.Name == name && baseType.IsAssignableFrom(t)).ToList();
        return matches.Count == 1 ? matches[0]
            : throw Missing(matches.Count == 0
                ? $"This TIA Portal installation has no public {baseType.Name} type named exactly '{name}' in {assembly.GetName().Name}."
                : $"{matches.Count} types are named '{name}'; none is chosen.");
    }

    // ---- runtime members ---------------------------------------------------------------------------

    internal static object? Composition(object owner, string name, bool required)
    {
        var property = LibraryRuntimeBinding.Property(owner.GetType(), name, out _);
        if (property is null)
            return required ? throw Missing($"{owner.GetType().Name} has no {name} composition.") : null;
        return property.GetValue(owner);
    }

    internal static IEnumerable<IEngineeringObject> Children(object? composition)
        => composition is IEnumerable items ? items.OfType<IEngineeringObject>() : Enumerable.Empty<IEngineeringObject>();

    /// <summary>The scripts of a screen or item and of its items: script dynamizations, event and property-event handlers.</summary>
    private static IEnumerable<(string Where, object Script)> Scripts(string where, object owner, int depth)
    {
        foreach (var dynamization in Children(Composition(owner, "Dynamizations", required: false)))
            if (LibraryRuntimeBinding.Property(dynamization.GetType(), "ScriptCode", out _) is not null)
                yield return ($"{where} dynamization {Text(dynamization, "PropertyName")}", dynamization);
        foreach (var handlers in new[] { "EventHandlers", "PropertyEventHandlers" })
        {
            foreach (var handler in Children(Composition(owner, handlers, required: false)))
            {
                var scriptProperty = LibraryRuntimeBinding.Property(handler.GetType(), "Script", out _);
                if (scriptProperty?.GetValue(handler) is { } script)
                    yield return ($"{where} {(handlers == "EventHandlers" ? "event" : "property event " + Text(handler, "PropertyName"))} {Text(handler, "EventType")}", script);
            }
        }

        if (depth < 4)
            foreach (var child in Children(Composition(owner, "ScreenItems", required: false)))
                foreach (var nested in Scripts($"{where}/{Text(child, "Name")}", child, depth + 1))
                    yield return nested;
    }

    /// <summary>The property V21 pitfall 3 reports to crash TIA Portal when read; never touched.</summary>
    internal const string ReportedCrashingProperty = "ScriptDiagnosis" + "OverviewText";

    /// <summary>
    /// Sets an attribute converted to exactly the runtime type of its current value (a MultilingualText takes the text of
    /// <paramref name="culture"/>) and reads it back. The attribute is read by name only (no generic enumeration, V21
    /// pitfall 3); an attribute the object does not have fails with TIA Portal's own message (target_not_found here),
    /// a read-only one with TIA Portal's rejection.
    /// </summary>
    internal static void SetValue(IEngineeringObject target, string name, string text, string? culture)
    {
        if (string.Equals(name, ReportedCrashingProperty, StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"skipped: reported to crash TIA Portal V21 ({name}); nothing was changed.");
        object? current;
        try
        {
            current = target.GetAttribute(name);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"'{name}' is not an attribute of {target.GetType().Name}: {exception.Message}");
        }

        if (current is MultilingualText multilingual)
        {
            if (string.IsNullOrWhiteSpace(culture))
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{name}' is a multilingual text; give culture.");
            HmiTextMapper.Write(multilingual, new[] { new HmiText(culture!, text) });
            return;
        }

        var value = Convert(current, text, name);
        target.SetAttribute(name, value);
        var observed = target.GetAttribute(name);
        if (!Equals(observed, value) && !string.Equals(Format(observed), Format(value), StringComparison.Ordinal))
            throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed, $"'{name}' reads back as '{Format(observed)}', not '{Format(value)}'.");
    }

    internal static object Convert(object? current, string text, string name)
    {
        if (current is null)
            throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"The current value of '{name}' is null, so its type is unknown; nothing was changed.");
        var type = current.GetType();
        try
        {
            if (type == typeof(string)) return text;
            if (type == typeof(bool)) return bool.Parse(text);
            if (type.IsEnum)
                return LibraryRuntimeBinding.EnumValue(type, text)
                    ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{text}' is not a member of {type.Name} ({LibraryRuntimeBinding.MemberList(type)}).");
            if (type.FullName == "System.Drawing.Color") return ParseColor(type, text);
            if (type.IsPrimitive || type == typeof(decimal)) return System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{text}' is not a valid {type.Name} for '{name}': {exception.Message}");
        }

        throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported, $"'{name}' holds a {type.FullName}, which is not set from text here; nothing was changed.");
    }

    /// <summary>#RRGGBB or #AARRGGBB, through Color.FromArgb(Int32) bound on the runtime type.</summary>
    private static object ParseColor(Type colorType, string text)
    {
        var hex = text.StartsWith("#", StringComparison.Ordinal) ? text.Substring(1) : text;
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            throw new FormatException("a colour is #RRGGBB or #AARRGGBB");
        if (hex.Length == 6) argb |= 0xFF000000;
        var fromArgb = colorType.GetMethod("FromArgb", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null)
            ?? throw Missing("Color.FromArgb(Int32) is not available.");
        return fromArgb.Invoke(null, new object[] { unchecked((int)argb) })!;
    }

    internal static string? Format(object? value) => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString();

    internal static string Text(IEngineeringObject target, string attribute)
    {
        try
        {
            return target.GetAttribute(attribute)?.ToString() ?? string.Empty;
        }
        catch (EngineeringException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<Type> Ancestors(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType) yield return current;
    }

    internal static MethodInfo Bind(object target, string name, Type[] argumentTypes)
        => LibraryRuntimeBinding.Find(target.GetType(), name, argumentTypes, out var error)
            ?? throw Missing($"{target.GetType().Name}.{name}({string.Join(", ", argumentTypes.Select(t => t.Name))}): {error}");

    internal static object? Invoke(MethodInfo method, object target, params object[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static string Required(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw new WorkerOperationException(WorkerFailureCategories.ValidationError, $"'{field}' is required.") : value!;

    internal static WorkerOperationException Missing(string message)
        => new(WorkerFailureCategories.ApiMemberMissing, message + " Nothing was changed.");
}
