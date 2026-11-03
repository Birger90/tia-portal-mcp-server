using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Siemens.Engineering;
using Siemens.Engineering.SW.Alarm.TextLists;
using TiaMcpServer.Contracts.PlcAlarms;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.PlcAlarms;

/// <summary>
/// Text lists under <c>PlcSoftware.PlcAlarmTextlistGroup</c> (official skill: <c>plcSoftware.PlcAlarmTextlistGroup.PlcAlarmUserTextlists</c>).
/// User lists are matched by exact enumeration (case-insensitive, unique), never by a lookup member the sources do not
/// show. The documented <c>PlcAlarmTextlist</c> members <c>ID</c>, <c>ListRange</c> and <c>Comment</c> (and the system
/// lists' <c>Name</c>) have no documented type, so they are read as exact public properties of the runtime object,
/// falling back to <c>IEngineeringObject.GetAttribute</c> of the same name; an unreadable member is reported, never guessed.
/// </summary>
internal static class PlcTextListResolver
{
    public const int MaxTextLists = 5000;

    public static PlcAlarmTextlistGroup Group(PlcSoftwareLocator.DiscoveredPlcSoftware plc)
        => plc.Software.PlcAlarmTextlistGroup ?? throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
            $"PLC '{plc.Software.Name}' offers no PlcAlarmTextlistGroup (no text lists on this PLC).");

    /// <summary>The user text list named <paramref name="name"/>; a system list of that name is refused.</summary>
    public static PlcAlarmUserTextlist User(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string? name)
    {
        if (PlcAlarmPolicy.CheckName(name, "textListName") is { } invalid)
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, invalid);
        var group = Group(plc);
        var matches = group.PlcAlarmUserTextlists.Where(list => string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (matches.Count == 1) return matches[0];
        if (matches.Count > 1)
            throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"User text list '{name}' is ambiguous in PLC '{plc.Software.Name}'.");
        if (group.PlcAlarmSystemTextlists.Any(list => string.Equals(SystemName(list), name, StringComparison.OrdinalIgnoreCase)))
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                $"'{name}' is a system text list of PLC '{plc.Software.Name}'; system text lists are not changed through this server. Nothing was changed.");
        throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"User text list '{name}' was not found in PLC '{plc.Software.Name}'.");
    }

    /// <summary>
    /// The text lists <paramref name="names"/> name, as TIA Portal spells them (matched case-insensitively, each exactly once
    /// among the user and system lists). A missing name is target_not_found; with <paramref name="refuseSystem"/> (cell
    /// updates, which import the workbook again) a system list is refused with validation_error. Nothing is exported before.
    /// </summary>
    public static List<string> ResolveNames(PlcSoftwareLocator.DiscoveredPlcSoftware plc, IReadOnlyList<string> names, bool refuseSystem)
    {
        var group = Group(plc);
        var users = group.PlcAlarmUserTextlists.Select(list => list.Name).ToList();
        var systems = group.PlcAlarmSystemTextlists.Select(SystemName).Where(name => name is not null).Select(name => name!).ToList();
        var resolved = new List<string>();
        foreach (var name in names)
        {
            var user = users.Where(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            var system = systems.Where(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (user.Count + system.Count > 1)
                throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Text list '{name}' is ambiguous in PLC '{plc.Software.Name}'. Nothing was exported.");
            if (system.Count == 1 && refuseSystem)
                throw new WorkerOperationException(WorkerFailureCategories.ValidationError,
                    $"'{system[0]}' is a system text list of PLC '{plc.Software.Name}'; a cell update imports the workbook again, and system text lists are not changed "
                    + "through this server. Read it with read_text_list_workbook; leave it out of textListNames to update user lists. Nothing was exported.");
            resolved.Add(user.Count == 1 ? user[0] : system.Count == 1 ? system[0]
                : throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Text list '{name}' was not found in PLC '{plc.Software.Name}'. Nothing was exported."));
        }

        return resolved;
    }

    /// <summary>The user or system list other than <paramref name="except"/> already named <paramref name="name"/> (case-insensitive, as TIA Portal resolves names); null when the name is free.</summary>
    public static string? Taken(PlcSoftwareLocator.DiscoveredPlcSoftware plc, string name, PlcAlarmUserTextlist except)
    {
        var group = Group(plc);
        var user = group.PlcAlarmUserTextlists.FirstOrDefault(list => !Equals(list, except) && string.Equals(list.Name, name, StringComparison.OrdinalIgnoreCase));
        if (user is not null) return $"user text list '{user.Name}'";
        var system = group.PlcAlarmSystemTextlists.Select(SystemName).FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        return system is null ? null : $"system text list '{system}'";
    }

    public static string? SystemName(PlcAlarmSystemTextlist list) => Member(list, "Name", null)?.ToString();

    public static PlcTextListInfo Describe(object list, bool isSystem, string? name)
    {
        var info = new PlcTextListInfo { Name = name ?? string.Empty, IsSystem = isSystem };
        if (name is null) info.Unreadable.Add("Name");
        info.Id = Text(Member(list, "ID", info.Unreadable));
        info.ListRange = Text(Member(list, "ListRange", info.Unreadable));
        var comment = Member(list, "Comment", info.Unreadable);
        if (comment is MultilingualText text) info.Comment = Items(text, info.Unreadable);
        else if (comment is not null) info.Unreadable.Add($"Comment (a {comment.GetType().FullName}, not a MultilingualText)");
        return info;
    }

    public static List<PlcAlarmTextItemInfo>? Items(MultilingualText text, List<string> unreadable)
    {
        try
        {
            var items = new List<PlcAlarmTextItemInfo>();
            foreach (MultilingualTextItem item in text.Items)
                items.Add(new PlcAlarmTextItemInfo { Culture = item.Language.Culture.Name, Text = item.Text ?? string.Empty });
            return items.OrderBy(item => item.Culture, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
            unreadable.Add("Comment items");
            return null;
        }
    }

    /// <summary>
    /// The member <paramref name="name"/>: the most derived readable public instance property of that exact name
    /// (a derived list may redeclare a base member), else the Openness attribute of that name. A recoverable Siemens
    /// failure or an absent member adds the name to <paramref name="unreadable"/> and returns null.
    /// </summary>
    public static object? Member(object target, string name, List<string>? unreadable)
    {
        PropertyInfo? property = null;
        for (var type = target.GetType(); type is not null && property is null; type = type.BaseType)
            property = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.Name == name && candidate.CanRead && candidate.GetIndexParameters().Length == 0);
        try
        {
            if (property is not null) return Unwrap(() => property.GetValue(target));
            if (target is IEngineeringObject engineering) return engineering.GetAttribute(name);
        }
        catch (EngineeringException exception) when (OpennessExceptions.IsRecoverable(exception))
        {
        }

        unreadable?.Add(name);
        return null;
    }

    /// <summary>Fails closed (unsupported_capability, nothing changed) unless GetAttributeInfos lists <paramref name="name"/> as writable.</summary>
    public static void RequireWritable(IEngineeringObject target, string name, string description)
    {
        var info = target.GetAttributeInfos().FirstOrDefault(candidate => candidate.Name == name);
        if (info is null || info.AccessMode is not (EngineeringAttributeAccessMode.Write or EngineeringAttributeAccessMode.ReadWrite))
            throw new WorkerOperationException(WorkerFailureCategories.UnsupportedCapability,
                $"{description} lists no writable {name} attribute (GetAttributeInfos); it cannot be changed through Openness. Nothing was changed.");
    }

    public static string? Text(object? value) => value switch
    {
        null => null,
        Enum member => member.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static object? Unwrap(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
